using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 补打任务编排：单记录多模板 → 一个 TemplateBatch 任务；按月批量 → 每户一个任务、共享 batch_no。
/// <para>Server 不生成 PDF；任务由 Agent 渲染并回传留痕。</para>
/// </summary>
public sealed class ReprintJobService(
    ArchivePrintQueueService queue,
    PrinterBindingService printers,
    IDatabaseService database,
    ILoggerService logger)
{
    private static readonly string[] SingleModes = ["preview", "print", "save"];
    private static readonly string[] BatchModes = ["print", "save"];

    /// <summary>生成批次号（同一次批量内共享；单条补打也生成，便于留痕归档）。</summary>
    public static string NewBatchNo()
        => $"RP{DateTime.Now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 1000)}";

    /// <summary>
    /// 单条补打：一个记录 + 多个模板 → 一个 TemplateBatch 任务（一个 part/模板）。
    /// </summary>
    public async Task<Result<ReprintJobCreated>> CreateSingleJobAsync(
        IReprintProvider provider,
        ReprintRecordItem record,
        IReadOnlyList<long> templateIds,
        string mode,
        int copies,
        bool isDuplex,
        long? printerBindingId,
        string? printerName,
        string batchNo,
        UserDto user,
        CancellationToken ct = default)
    {
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (!SingleModes.Contains(normalizedMode, StringComparer.Ordinal))
        {
            return Result.Failure<ReprintJobCreated>(
                ErrorCodes.VALIDATION_FAILED, "mode 只能是 preview / print / save");
        }

        var ids = NormalizeTemplateIds(templateIds);
        if (ids.Count == 0)
        {
            return Result.Failure<ReprintJobCreated>(
                ErrorCodes.VALIDATION_FAILED, "至少选择一个模板");
        }

        var targetResult = await ResolveTargetAsync(user.Id, printerBindingId, printerName, ct);
        if (targetResult.IsFailure || targetResult.Value is null)
        {
            return Result.Failure<ReprintJobCreated>(
                targetResult.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                targetResult.Message ?? "解析打印机失败");
        }

        var dataResult = await provider.BuildPrintDataAsync(record.BusinessId, user.Id, ct);
        if (dataResult.IsFailure || dataResult.Value is null)
        {
            return Result.Failure<ReprintJobCreated>(
                dataResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                dataResult.Message ?? "打印数据装配失败");
        }

        var partsResult = await BuildPartsAsync(ids, dataResult.Value, null, ct);
        if (partsResult.IsFailure || partsResult.Value is null)
        {
            return Result.Failure<ReprintJobCreated>(
                partsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                partsResult.Message ?? "模板加载失败");
        }

        var enqueueResult = await EnqueueAsync(
            provider.DomainKey, record, dataResult.Value, partsResult.Value,
            normalizedMode, copies, isDuplex, targetResult.Value, batchNo, user, ct);

        if (enqueueResult.IsFailure || enqueueResult.Value is null)
        {
            return Result.Failure<ReprintJobCreated>(
                enqueueResult.ErrorCode ?? ErrorCodes.UNKNOWN_ERROR,
                enqueueResult.Message ?? "打印任务入队失败");
        }

        var job = enqueueResult.Value;
        return Result.Success(new ReprintJobCreated(
            job.Id, job.JobNo, job.Status, job.JobKind, batchNo, normalizedMode, "template"));
    }

    /// <summary>
    /// 按月批量补打：逐户装配字段并入队，一记录一任务、共享同一 batch_no。
    /// 单户失败不阻断其余户，错误在 <see cref="ReprintBatchCreated.Errors"/> 返回。
    /// </summary>
    public async Task<Result<ReprintBatchCreated>> CreateBatchAsync(
        IReprintProvider provider,
        IReadOnlyList<ReprintRecordItem> records,
        IReadOnlyList<long> templateIds,
        string mode,
        int copies,
        bool isDuplex,
        long? printerBindingId,
        string? printerName,
        UserDto user,
        CancellationToken ct = default)
    {
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (!BatchModes.Contains(normalizedMode, StringComparer.Ordinal))
        {
            return Result.Failure<ReprintBatchCreated>(
                ErrorCodes.VALIDATION_FAILED, "批量补打 mode 只能是 print / save");
        }

        var ids = NormalizeTemplateIds(templateIds);

        if (records.Count == 0)
        {
            return Result.Failure<ReprintBatchCreated>(
                ErrorCodes.VALIDATION_FAILED, "当前月份没有可补打的记录");
        }

        var targetResult = await ResolveTargetAsync(user.Id, printerBindingId, printerName, ct);
        if (targetResult.IsFailure || targetResult.Value is null)
        {
            return Result.Failure<ReprintBatchCreated>(
                targetResult.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                targetResult.Message ?? "解析打印机失败");
        }

        var batchNo = NewBatchNo();
        var jobs = new List<ReprintBatchJobItem>();
        var errors = new List<string>();
        var templateCache = new Dictionary<long, PrintTemplateMetaRow>();

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();

            var dataResult = await provider.BuildPrintDataAsync(record.BusinessId, user.Id, ct);
            if (dataResult.IsFailure || dataResult.Value is null)
            {
                errors.Add($"{record.Name}: {dataResult.Message ?? "打印数据装配失败"}");
                continue;
            }

            var recordTemplateIds = ids;
            if (recordTemplateIds.Count == 0)
            {
                // 「整份档案」批量：逐户取全部适用模板（与旧版默认模式一致）。
                var templatesResult = await provider.GetTemplatesAsync(record.BusinessId, user.Id, ct);
                if (templatesResult.IsFailure || templatesResult.Value is null)
                {
                    errors.Add($"{record.Name}: {templatesResult.Message ?? "模板加载失败"}");
                    continue;
                }

                recordTemplateIds = templatesResult.Value
                    .Where(template => template.IsApplicable && template.TemplateId > 0)
                    .Select(template => template.TemplateId)
                    .Distinct()
                    .ToList();

                if (recordTemplateIds.Count == 0)
                {
                    errors.Add($"{record.Name}: 没有适用模板");
                    continue;
                }
            }

            var partsResult = await BuildPartsAsync(recordTemplateIds, dataResult.Value, templateCache, ct);
            if (partsResult.IsFailure || partsResult.Value is null)
            {
                errors.Add($"{record.Name}: {partsResult.Message ?? "模板加载失败"}");
                continue;
            }

            var enqueueResult = await EnqueueAsync(
                provider.DomainKey, record, dataResult.Value, partsResult.Value,
                normalizedMode, copies, isDuplex, targetResult.Value, batchNo, user, ct);

            if (enqueueResult.IsFailure || enqueueResult.Value is null)
            {
                errors.Add($"{record.Name}: {enqueueResult.Message ?? "打印任务入队失败"}");
                continue;
            }

            jobs.Add(new ReprintBatchJobItem(
                enqueueResult.Value.JobNo, record.BusinessId, record.Name));
        }

        if (jobs.Count == 0)
        {
            return Result.Failure<ReprintBatchCreated>(
                ErrorCodes.DB_QUERY_ERROR,
                errors.Count > 0 ? string.Join("；", errors.Take(5)) : "批量任务入队失败");
        }

        logger.Info($"补打批量入队: BatchNo={batchNo}, Jobs={jobs.Count}, Errors={errors.Count}");

        return Result.Success(new ReprintBatchCreated(batchNo, jobs.Count, jobs, errors));
    }

    /// <summary>批次进度：按 batch_no 聚合任务状态。</summary>
    public async Task<Result<ReprintBatchProgress>> GetBatchProgressAsync(
        string batchNo,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(batchNo))
        {
            return Result.Failure<ReprintBatchProgress>(
                ErrorCodes.VALIDATION_FAILED, "batchNo 不能为空");
        }

        const string sql = @"SELECT job_no, business_id, applicant_name, status, error_message,
                                    created_at, completed_at
                             FROM nc_biz_print_jobs
                             WHERE batch_no = $1 AND deleted_at IS NULL
                             ORDER BY id ASC";

        var result = await database.QueryAsync<ReprintBatchJobRow>(sql, ct, batchNo.Trim());
        if (result.IsFailure)
        {
            return Result.Failure<ReprintBatchProgress>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "批次进度查询失败");
        }

        var rows = result.Value ?? [];
        if (rows.Count == 0)
        {
            return Result.Failure<ReprintBatchProgress>(
                ErrorCodes.NOT_FOUND, "补打批次不存在");
        }

        var items = rows.Select(row => new ReprintBatchProgressItem(
            row.JobNo ?? string.Empty,
            row.BusinessId ?? 0,
            row.ApplicantName ?? string.Empty,
            row.Status ?? string.Empty,
            string.Equals(row.Status, PrintJobConstants.StatusFailed, StringComparison.Ordinal)
                ? row.ErrorMessage
                : null,
            row.CreatedAt,
            row.CompletedAt)).ToList();

        var completed = rows.Count(row => string.Equals(
            row.Status, PrintJobConstants.StatusCompleted, StringComparison.Ordinal));
        var failed = rows.Count(row => string.Equals(
            row.Status, PrintJobConstants.StatusFailed, StringComparison.Ordinal));
        var processing = rows.Count(row => string.Equals(
            row.Status, PrintJobConstants.StatusProcessing, StringComparison.Ordinal));
        var pending = rows.Count - completed - failed - processing;

        return Result.Success(new ReprintBatchProgress(
            rows.Count,
            completed,
            failed,
            Math.Max(pending, 0),
            processing,
            completed + failed == rows.Count,
            items));
    }

    private async Task<Result<PrintJob>> EnqueueAsync(
        string domainKey,
        ReprintRecordItem record,
        ReprintPrintData data,
        List<PrintBatchPart> parts,
        string mode,
        int copies,
        bool isDuplex,
        PrintJobTarget target,
        string batchNo,
        UserDto user,
        CancellationToken ct)
    {
        var batch = new PrintBatchDefinition
        {
            Mode = mode,
            Category = data.Category,
            Period = DateTime.Now.ToString("yyyyMM"),
            Parts = parts
        };

        var request = new PrintJobRequest
        {
            BusinessType = domainKey,
            BusinessId = record.BusinessId,
            BatchNo = batchNo,
            Classification = data.Classification,
            TemplateId = null,
            TemplateName = parts.Count == 1
                ? parts[0].TemplateName
                : $"补打·{data.Category}",
            ApplicantName = data.ApplicantName,
            ApplicantIdCard = data.ApplicantIdCard,
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = Math.Clamp(copies <= 0 ? 1 : copies, 1, 99),
            IsDuplex = isDuplex,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal),
            TableRows = [],
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch
        };

        return await queue.EnqueueAsync(request, ct);
    }

    private async Task<Result<List<PrintBatchPart>>> BuildPartsAsync(
        IReadOnlyList<long> templateIds,
        ReprintPrintData data,
        Dictionary<long, PrintTemplateMetaRow>? templateCache,
        CancellationToken ct)
    {
        var parts = new List<PrintBatchPart>(templateIds.Count);

        foreach (var templateId in templateIds)
        {
            PrintTemplateMetaRow? meta;
            if (templateCache is not null && templateCache.TryGetValue(templateId, out var cached))
            {
                meta = cached;
            }
            else
            {
                var metaResult = await queue.GetTemplateMetaAsync(templateId, ct);
                if (metaResult.IsFailure)
                {
                    return Result.Failure<List<PrintBatchPart>>(
                        metaResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        metaResult.Message ?? "模板加载失败");
                }

                meta = metaResult.Value;
                if (meta is not null && templateCache is not null)
                {
                    templateCache[templateId] = meta;
                }
            }

            if (meta is null)
            {
                return Result.Failure<List<PrintBatchPart>>(
                    ErrorCodes.NOT_FOUND, $"模板不存在（ID={templateId}）");
            }

            var (_, extension) = ResolveFileType(meta.FileType);
            parts.Add(new PrintBatchPart
            {
                TemplateId = meta.Id,
                TemplateName = meta.Name,
                FileName = BuildFileName(meta.Name, extension),
                Fields = new Dictionary<string, string>(data.Fields, StringComparer.Ordinal),
                TableRows = data.TableRows
                    .Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal))
                    .ToList()
            });
        }

        return Result.Success(parts);
    }

    private async Task<Result<PrintJobTarget?>> ResolveTargetAsync(
        int userId,
        long? printerBindingId,
        string? printerName,
        CancellationToken ct)
    {
        var targetResult = await printers.ResolveTargetAsync(userId, printerBindingId, printerName, ct);
        if (targetResult.IsFailure)
        {
            return targetResult;
        }

        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0 || string.IsNullOrWhiteSpace(target.PrinterName))
        {
            return Result.Failure<PrintJobTarget?>(
                ErrorCodes.VALIDATION_FAILED,
                "当前账号未绑定打印机，请先在「我的打印机」中生成绑定码并完成绑定");
        }

        return Result.Success<PrintJobTarget?>(target);
    }

    private static List<long> NormalizeTemplateIds(IReadOnlyList<long>? templateIds)
        => (templateIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;

    private static (string FileType, string Extension) ResolveFileType(string? fileType)
    {
        try
        {
            var normalized = TemplateFileTypes.Normalize(fileType);
            return (normalized, TemplateFileTypes.ToExtension(normalized));
        }
        catch (NotSupportedException)
        {
            var fallback = string.IsNullOrWhiteSpace(fileType) ? "docx" : fileType.Trim().ToLowerInvariant();
            return (fallback, ".docx");
        }
    }

    private static string BuildFileName(string? name, string extension)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "template" : name.Trim();
        if (safe.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            || safe.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            safe = Path.GetFileNameWithoutExtension(safe);
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe + extension;
    }

    /// <summary>批次任务行（进度查询用）。</summary>
    private sealed class ReprintBatchJobRow
    {
        public string? JobNo { get; set; }
        public long? BusinessId { get; set; }
        public string? ApplicantName { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}

/// <summary>单条补打任务创建结果。</summary>
public sealed record ReprintJobCreated(
    long JobId,
    string JobNo,
    string Status,
    string JobKind,
    string BatchNo,
    string Mode,
    string Source);

/// <summary>批量补打创建结果。</summary>
public sealed record ReprintBatchCreated(
    string BatchNo,
    int Total,
    IReadOnlyList<ReprintBatchJobItem> Jobs,
    IReadOnlyList<string> Errors);

public sealed record ReprintBatchJobItem(string JobNo, long BusinessId, string Name);

/// <summary>批次进度。</summary>
public sealed record ReprintBatchProgress(
    int Total,
    int Completed,
    int Failed,
    int Pending,
    int Processing,
    bool Done,
    IReadOnlyList<ReprintBatchProgressItem> Items);

public sealed record ReprintBatchProgressItem(
    string JobNo,
    long BusinessId,
    string Name,
    string Status,
    string? Error,
    DateTime CreatedAt,
    DateTime? CompletedAt);
