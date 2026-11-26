using System.Collections.Concurrent;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Configuration;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Import;

namespace NewCosmos.Server.Services.DataCenter;

/// <summary>
/// 数据导入（数据中心）服务：导入类型、模板导出、预览、后台导入任务与任务日志。
/// <para>
/// 语义严格对齐 PC <c>DataImportViewModel</c>：组合类型走
/// <see cref="IImportServiceManager.PreviewCombinedAsync"/> / <see cref="IImportServiceManager.ImportCombinedAsync"/>，
/// 单文件类型走 <see cref="IImportServiceManager.ImportSingleFilesAsync"/>（内部逐文件调用 <see cref="IImportService.ImportAsync(string[], ImportScope?, bool, IProgress{string}?, CancellationToken)"/>），
/// 年份 / 区间 / 年段清空由 <see cref="ImportScope"/> 表达。
/// </para>
/// <para>
/// 文件只落在 <see cref="DataCenterOptions.ImportRoot"/> 下的临时目录（preview-* / job-* / template-*），
/// 预览或任务结束立即删除；任务结果保留 2 小时供日志下载。
/// </para>
/// </summary>
public sealed class DataImportService(
    IImportServiceManager manager,
    IEnumerable<IImportService> singleFileServices,
    DataCenterOptions options,
    DataCenterJobRegistry jobs,
    ILoggerService logger)
{
    /// <summary>预览行数（与 PC 一致，固定 10 行）。</summary>
    public const int PreviewRowCount = 10;

    private const int MaxCapturedLogLines = 2000;
    private const int MaxCapturedErrors = 500;
    private const int MaxCapturedWarnings = 500;
    private const int MaxCapturedUnlinked = 200;
    private const int MaxRetainedResults = 50;
    private static readonly TimeSpan ResultRetention = TimeSpan.FromHours(2);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ConcurrentDictionary<string, ImportJobResult> _results = new(StringComparer.Ordinal);

    // ───────────────────────── 导入类型 ─────────────────────────

    /// <summary>是否为本服务支持的导入类型（仅 Implemented）。</summary>
    public bool IsImplementedType(string? importType)
        => !string.IsNullOrWhiteSpace(importType)
           && ImportTypeCodes.Implemented.Contains(importType, StringComparer.Ordinal);

    /// <summary>导入类型的显示名（未知类型回退为代码本身）。</summary>
    public string GetTypeName(string importType)
        => ImportTypeCodes.GetDisplayName(importType);

    /// <summary>该类型是否支持导入范围（年份覆盖 / 区间校验 / 年段清空）。</summary>
    public bool SupportsScope(string importType) => manager.SupportsScope(importType);

    /// <summary>类型下拉：只返回已实现类型，字段对齐 PC 页面。</summary>
    public IReadOnlyList<ImportTypeDto> GetTypes()
    {
        var names = manager.GetImportTypes();
        var list = new List<ImportTypeDto>(ImportTypeCodes.Implemented.Length);

        foreach (var code in ImportTypeCodes.Implemented)
        {
            if (!names.TryGetValue(code, out var name) || string.IsNullOrWhiteSpace(name))
            {
                name = ImportTypeCodes.GetDisplayName(code);
            }

            var patterns = ImportTypeCodes.GetFilePatterns(code);
            list.Add(new ImportTypeDto(
                code,
                name,
                ImportTypeCodes.IsCombinedType(code),
                manager.SupportsScope(code),
                manager.HasTemplate(code),
                patterns.FamilyPattern,
                patterns.PersonPattern));
        }

        return list;
    }

    // ───────────────────────── 模板导出 ─────────────────────────

    /// <summary>
    /// 导出标准模板到 <see cref="DataCenterOptions.ImportRoot"/> 下的临时目录。
    /// 调用方下载完成后必须调用 <see cref="DeleteWorkingDirectory"/> 清理目录。
    /// </summary>
    public async Task<TemplateExportResult> ExportTemplateAsync(string importType, CancellationToken ct)
    {
        if (!IsImplementedType(importType))
        {
            return new TemplateExportResult(null, null, null, $"未知的导入类型：{importType}");
        }

        if (!manager.HasTemplate(importType))
        {
            return new TemplateExportResult(
                null, null, null, $"「{GetTypeName(importType)}」暂无可导出的模板");
        }

        var directory = CreateWorkingDirectory("template");
        try
        {
            var result = await manager.ExportTemplateAsync(importType, directory, ct);
            if (result.IsFailure || string.IsNullOrWhiteSpace(result.Value))
            {
                DeleteWorkingDirectory(directory);
                return new TemplateExportResult(null, null, null, result.Message ?? "模板导出失败");
            }

            var path = EnsureInsideImportRoot(result.Value);
            if (!File.Exists(path))
            {
                DeleteWorkingDirectory(directory);
                return new TemplateExportResult(null, null, null, "模板文件生成失败，请稍后重试");
            }

            return new TemplateExportResult(path, directory, Path.GetFileName(path), null);
        }
        catch
        {
            DeleteWorkingDirectory(directory);
            throw;
        }
    }

    // ───────────────────────── 预览 ─────────────────────────

    /// <summary>
    /// 预览导入数据。组合类型使用 family/person 文件（缺失时按文件名模式在
    /// <paramref name="workingDirectory"/> 内自动匹配）；单文件类型逐个文件预览。
    /// </summary>
    public async Task<DataImportPreviewResponse> PreviewAsync(
        string importType,
        string? familyFilePath,
        string? personFilePath,
        IReadOnlyList<string> singleFilePaths,
        string workingDirectory,
        CancellationToken ct)
    {
        if (!IsImplementedType(importType))
        {
            return PreviewFailure($"未知的导入类型：{importType}");
        }

        return ImportTypeCodes.IsCombinedType(importType)
            ? await PreviewCombinedAsync(importType, familyFilePath, personFilePath, workingDirectory, ct)
            : await PreviewSingleAsync(importType, singleFilePaths, ct);
    }

    private async Task<DataImportPreviewResponse> PreviewCombinedAsync(
        string importType,
        string? familyFilePath,
        string? personFilePath,
        string workingDirectory,
        CancellationToken ct)
    {
        var patterns = ImportTypeCodes.GetFilePatterns(importType);

        familyFilePath = UseExistingFile(familyFilePath);
        personFilePath = UseExistingFile(personFilePath);

        // 未显式指定（或指定的文件已不存在）时，按平台既有的文件名模式在临时目录内自动匹配（PC FindMatchingFiles 同源）。
        if (familyFilePath is null || personFilePath is null)
        {
            var (matchedFamily, matchedPerson) = manager.FindMatchingFiles(importType, workingDirectory);
            familyFilePath ??= UseExistingFile(matchedFamily);
            personFilePath ??= UseExistingFile(matchedPerson);
        }

        if (familyFilePath is null)
        {
            return PreviewFailure(
                $"未找到家庭文件，请确保文件名符合「{patterns.FamilyPattern}」或手动指定家庭文件。");
        }

        if (personFilePath is null)
        {
            return PreviewFailure(
                $"未找到人员文件，请确保文件名符合「{patterns.PersonPattern}」或手动指定人员文件。");
        }

        var files = new List<ImportPreviewFileDto>
        {
            BuildPreviewFile(familyFilePath, "家庭"),
            BuildPreviewFile(personFilePath, "人员")
        };

        var result = await manager.PreviewCombinedAsync(
            importType, familyFilePath, personFilePath, PreviewRowCount, ct);

        return new DataImportPreviewResponse(
            result.Success,
            string.IsNullOrWhiteSpace(result.ErrorMessage) ? null : result.ErrorMessage,
            result.FamilyTotalRows,
            result.PersonTotalRows,
            MapColumns(result.FamilyColumnMappings),
            MapColumns(result.PersonColumnMappings),
            result.FamilyPreviewRows,
            result.PersonPreviewRows,
            files,
            new ImportMatchedFilesDto(
                Path.GetFileName(familyFilePath),
                Path.GetFileName(personFilePath)));
    }

    private async Task<DataImportPreviewResponse> PreviewSingleAsync(
        string importType,
        IReadOnlyList<string> singleFilePaths,
        CancellationToken ct)
    {
        var files = singleFilePaths
            .Select(UseExistingFile)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToList();

        if (files.Count == 0)
        {
            return PreviewFailure("请至少选择一个 Excel 文件");
        }

        var service = FindSingleFileService(importType);
        if (service is null)
        {
            return PreviewFailure($"未找到「{GetTypeName(importType)}」的导入服务");
        }

        var columns = new List<ImportColumnMappingDto>();
        var rows = new List<Dictionary<string, object?>>();
        var errors = new List<string>();
        var totalRows = 0;

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            try
            {
                var preview = await service.PreviewAsync(path, PreviewRowCount, ct);
                if (!string.IsNullOrWhiteSpace(preview.ErrorMessage))
                {
                    // 单文件服务尚未实现 PreviewAsync（BaseImportService 默认返回"预览功能未实现"）时，
                    // 退回 Core 的通用表格读取器给出只读表头预览，避免预览按钮整体不可用。
                    if (preview.ErrorMessage.Contains("预览功能未实现", StringComparison.Ordinal))
                    {
                        preview = ReadRawPreview(path, PreviewRowCount);
                        if (preview is null)
                        {
                            errors.Add($"{fileName}：无法读取文件");
                            continue;
                        }
                    }
                    else
                    {
                        errors.Add($"{fileName}：{preview.ErrorMessage}");
                        continue;
                    }
                }

                totalRows += preview.TotalRows;
                if (columns.Count == 0 && preview.ColumnMappings.Count > 0)
                {
                    columns.AddRange(MapColumns(preview.ColumnMappings));
                }

                foreach (var row in preview.PreviewRows)
                {
                    if (rows.Count >= PreviewRowCount)
                    {
                        break;
                    }

                    rows.Add(row);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{fileName}：{ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            return PreviewFailure(string.Join("；", errors));
        }

        return new DataImportPreviewResponse(
            true,
            null,
            0,
            totalRows,
            [],
            columns,
            [],
            rows,
            files.Select(path => BuildPreviewFile(path, "单文件")).ToList(),
            null);
    }

    /// <summary>
    /// 通用表格只读预览（表头 → 前 N 行），用于单文件服务未实现预览时兜底。
    /// 列只用于展示，不参与导入逻辑。
    /// </summary>
    private static ImportPreviewResult? ReadRawPreview(string path, int previewRows)
    {
        try
        {
            var table = TabularFileReader.Read(path);
            if (table.Headers.Length == 0)
            {
                return null;
            }

            var headers = new List<string>(table.Headers.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < table.Headers.Length; i++)
            {
                var header = table.Headers[i];
                if (string.IsNullOrWhiteSpace(header))
                {
                    header = $"列{i + 1}";
                }

                var unique = header;
                var suffix = 2;
                while (!seen.Add(unique))
                {
                    unique = $"{header} ({suffix++})";
                }

                headers.Add(unique);
            }

            var rows = new List<Dictionary<string, object?>>();
            foreach (var cells in table.Rows.Take(previewRows))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < headers.Count; i++)
                {
                    row[headers[i]] = i < cells.Length ? cells[i] : null;
                }

                rows.Add(row);
            }

            return new ImportPreviewResult
            {
                Success = true,
                FilePath = path,
                TotalRows = table.Rows.Count,
                ColumnMappings = headers
                    .Select(header => new ColumnMappingInfo
                    {
                        SourceColumn = header,
                        // 兜底预览没有映射语义；前端仅展示 isMapped=true 的列，这里标为可展示。
                        TargetField = string.Empty,
                        IsMapped = true
                    })
                    .ToList(),
                PreviewRows = rows
            };
        }
        catch
        {
            return null;
        }
    }

    // ───────────────────────── 导入范围 ─────────────────────────

    /// <summary>
    /// 按页面输入构造 <see cref="ImportScope"/>，规则与 PC <c>DataImportViewModel.BuildImportScope</c> 一致：
    /// 单年 = 强制覆盖（2000–2100）；区间 = 仅校验（起止 ≤ 5 年）；两者互斥；参数非法即拒绝导入。
    /// 不支持范围的类型忽略这三个输入（返回 null scope）。
    /// </summary>
    public static bool TryBuildScope(
        string? yearText,
        string? rangeStartText,
        string? rangeEndText,
        bool clearBeforeImport,
        bool supportsScope,
        out ImportScope? scope,
        out string? error)
    {
        scope = null;
        error = null;

        if (!supportsScope)
        {
            return true;
        }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(yearText))
        {
            if (!int.TryParse(yearText.Trim(), out var parsedYear) || parsedYear < 2000 || parsedYear > 2100)
            {
                error = $"「导入年份」必须是 2000–2100 的四位年份，当前填写：{yearText.Trim()}";
                return false;
            }

            year = parsedYear;
        }

        int? start = null;
        int? end = null;
        var rangeMode = !string.IsNullOrWhiteSpace(rangeStartText) || !string.IsNullOrWhiteSpace(rangeEndText);
        if (rangeMode)
        {
            if (!int.TryParse((rangeStartText ?? string.Empty).Trim(), out var parsedStart) ||
                !int.TryParse((rangeEndText ?? string.Empty).Trim(), out var parsedEnd) ||
                parsedStart < 2000 || parsedEnd > 2100 || parsedStart > parsedEnd)
            {
                error = "「导入区间」起止年份必须是 2000–2100，且起始年 ≤ 结束年。";
                return false;
            }

            if (parsedEnd - parsedStart > 4)
            {
                error = $"「导入区间」跨度过大（{parsedStart}–{parsedEnd}），最多允许 5 年，请分批导入。";
                return false;
            }

            if (year.HasValue)
            {
                error = "已开启「按区间」，请清空单年输入（区间模式下年份按文件解析）。";
                return false;
            }

            start = parsedStart;
            end = parsedEnd;
        }

        scope = new ImportScope
        {
            OverrideYear = year,
            RangeStart = start,
            RangeEnd = end,
            ClearBeforeImport = clearBeforeImport
        };
        return true;
    }

    // ───────────────────────── 后台任务 ─────────────────────────

    /// <summary>占用全局数据中心任务槽（同一时刻只允许一个导入任务）。</summary>
    public bool TryStartJob(string importType, int userId, out string jobId, out string? error)
    {
        var title = $"数据导入 - {GetTypeName(importType)}";
        if (!jobs.TryStart("import", title, userId, out var job, out error))
        {
            jobId = string.Empty;
            return false;
        }

        jobId = job.JobId;
        return true;
    }

    /// <summary>在 ImportRoot 下创建 job-{jobId} 目录（已存在则复用）。</summary>
    public string CreateJobDirectory(string jobId)
    {
        var safeJobId = Path.GetFileName(jobId);
        var root = EnsureImportRootCreated();
        var directory = Path.Combine(root, $"job-{safeJobId}");
        Directory.CreateDirectory(directory);
        return EnsureInsideImportRoot(directory);
    }

    /// <summary>在 ImportRoot 下创建 preview-* / template-* 临时目录。</summary>
    public string CreateWorkingDirectory(string prefix)
    {
        var root = EnsureImportRootCreated();
        var safePrefix = Path.GetFileName(prefix);
        var directory = Path.Combine(root, $"{safePrefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return EnsureInsideImportRoot(directory);
    }

    /// <summary>删除 ImportRoot 下的临时目录；失败只记警告，不影响主流程。</summary>
    public void DeleteWorkingDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            var root = Path.GetFullPath(options.ImportRoot);
            var full = Path.GetFullPath(directory);
            if (!IsInside(root, full))
            {
                logger.Warn($"拒绝删除 ImportRoot 之外的目录：{full}");
                return;
            }

            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"删除导入临时目录失败：{Path.GetFileName(directory)} {ex.Message}");
        }
    }

    /// <summary>
    /// 执行导入任务（在 Task.Run 中调用）。文件已由端点落到 <see cref="ImportJobRequest.JobDirectory"/>；
    /// 任务结束时无论成功失败都会删除该目录，并把结果写入内存供日志接口下载。
    /// </summary>
    public async Task RunImportJobAsync(ImportJobRequest request)
    {
        var ct = jobs.GetCancellationToken(request.JobId);
        var result = new ImportJobResult(
            request.JobId,
            request.ImportType,
            GetTypeName(request.ImportType),
            request.StartedBy,
            request.OperatorName);
        var startedAt = DateTime.Now;

        // 先登记结果：任务结束与前端轮询之间可能有毫秒级竞态，提前入字典保证汇总接口总能读到。
        _results[request.JobId] = result;
        PruneResults();

        try
        {
            jobs.Start(request.JobId, $"开始导入：{result.ImportTypeName}");
            LogLine(result, request.JobId, $"导入类型：{result.ImportTypeName}（{request.ImportType}）");
            LogLine(result, request.JobId, $"导入模式：{(ImportTypeCodes.IsCombinedType(request.ImportType) ? "家庭 + 人员组合导入" : "单文件导入")}");
            LogLine(result, request.JobId, $"导入前清空：{(request.ClearBeforeImport ? "是" : "否")}");
            if (request.Scope is not null)
            {
                LogLine(result, request.JobId, $"导入范围：{request.Scope.Describe()}");
            }

            var progress = new Progress<string>(message =>
            {
                result.AddLog(message);
                // 进度回调可能晚于任务结束投递，避免覆盖 Complete/Fail 的最终消息。
                if (jobs.Get(request.JobId)?.State == DataCenterJobState.Running)
                {
                    jobs.Report(request.JobId, message);
                }
            });

            if (ImportTypeCodes.IsCombinedType(request.ImportType))
            {
                await RunCombinedAsync(request, result, progress, ct);
            }
            else
            {
                await RunSingleFilesAsync(request, result, progress, ct);
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Canceled = true;
            result.Error = "导入已取消";
            LogLine(result, request.JobId, "收到取消请求，已停止导入并清理临时文件。");
            jobs.Cancel(request.JobId, "导入已取消");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.AddLog($"导入失败：{ex.Message}");
            jobs.Fail(request.JobId, ex.Message);
        }
        finally
        {
            result.Duration = result.Duration > TimeSpan.Zero ? result.Duration : DateTime.Now - startedAt;
            result.FinishedAt = DateTime.Now;
            DeleteWorkingDirectory(request.JobDirectory);
            StoreResult(result);

            logger.LogBusiness(
                "数据中心-数据导入结束",
                ("JobId", result.JobId),
                ("Type", result.ImportType),
                ("Success", result.Success == true),
                ("Canceled", result.Canceled),
                ("Family", result.FamilyCount),
                ("Person", result.PersonCount),
                ("Error", result.ErrorCount));
        }
    }

    private async Task RunCombinedAsync(
        ImportJobRequest request,
        ImportJobResult result,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var patterns = ImportTypeCodes.GetFilePatterns(request.ImportType);
        var familyPath = UseExistingFile(request.FamilyFilePath);
        var personPath = UseExistingFile(request.PersonFilePath);

        if (familyPath is null || personPath is null)
        {
            var (matchedFamily, matchedPerson) = manager.FindMatchingFiles(
                request.ImportType, request.JobDirectory);
            familyPath ??= UseExistingFile(matchedFamily);
            personPath ??= UseExistingFile(matchedPerson);
        }

        if (familyPath is null)
        {
            throw new InvalidOperationException(
                $"未找到家庭文件，请确保文件名符合「{patterns.FamilyPattern}」或手动指定家庭文件。");
        }

        if (personPath is null)
        {
            throw new InvalidOperationException(
                $"未找到人员文件，请确保文件名符合「{patterns.PersonPattern}」或手动指定人员文件。");
        }

        LogLine(result, request.JobId, $"家庭文件：{Path.GetFileName(familyPath)}");
        LogLine(result, request.JobId, $"人员文件：{Path.GetFileName(personPath)}");

        var importResult = await manager.ImportCombinedAsync(
            request.ImportType,
            familyPath,
            personPath,
            request.ClearBeforeImport,
            progress,
            ct);

        // 组合导入内部会吞掉取消异常并返回失败结果，这里统一转成取消分支。
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        result.FamilyCount = importResult.FamilyImportedCount;
        result.PersonCount = importResult.PersonImportedCount;
        result.LinkedCount = importResult.LinkedCount;
        result.UnlinkedCount = importResult.UnlinkedCount;
        result.ErrorCount = importResult.Errors.Count;
        result.WarningCount = importResult.Warnings.Count;
        result.Duration = importResult.Duration > TimeSpan.Zero
            ? importResult.Duration
            : DateTime.Now - result.StartedAt;

        CaptureDiagnostics(result, importResult.Errors, importResult.Warnings, importResult.UnlinkedPersons);

        if (!importResult.Success)
        {
            var message = string.IsNullOrWhiteSpace(importResult.ErrorMessage)
                ? "导入失败"
                : importResult.ErrorMessage;
            result.Success = false;
            result.Error = message;
            result.AddLog($"导入失败：{message}");
            ReportDiagnostics(result, request.JobId);
            jobs.Fail(request.JobId, message);
            return;
        }

        result.Success = true;
        var summary = BuildCombinedSummary(result);
        result.AddLog(summary);
        ReportDiagnostics(result, request.JobId);
        jobs.Complete(request.JobId, summary);
    }

    private async Task RunSingleFilesAsync(
        ImportJobRequest request,
        ImportJobResult result,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var filePaths = request.FilePaths
            .Select(UseExistingFile)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToList();

        if (filePaths.Count == 0)
        {
            throw new InvalidOperationException("未找到可导入的 Excel 文件，请重新选择后重试。");
        }

        var scope = request.Scope;
        if (scope is not null)
        {
            LogLine(result, request.JobId, $"导入范围：{scope.Describe()}");
        }

        var importResult = await manager.ImportSingleFilesAsync(
            request.ImportType,
            filePaths,
            request.ClearBeforeImport,
            progress,
            ct,
            scope);

        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        result.PersonCount = importResult.ImportedCount;
        result.FamilyCount = importResult.FamilyImportedCount;
        result.ErrorCount = importResult.ErrorCount > 0
            ? importResult.ErrorCount
            : importResult.Errors.Count;
        result.WarningCount = importResult.Warnings.Count;
        result.Duration = importResult.Duration > TimeSpan.Zero
            ? importResult.Duration
            : DateTime.Now - result.StartedAt;

        CaptureDiagnostics(result, importResult.Errors, importResult.Warnings, Array.Empty<UnlinkedPersonInfo>());

        if (!importResult.Success)
        {
            var message = string.IsNullOrWhiteSpace(importResult.Message)
                ? (importResult.Errors.Count > 0 ? importResult.Errors[0] : "导入失败")
                : importResult.Message;
            result.Success = false;
            result.Error = message;
            result.AddLog($"导入失败：{message}");
            ReportDiagnostics(result, request.JobId);
            jobs.Fail(request.JobId, message);
            return;
        }

        result.Success = true;
        var summary = BuildSingleSummary(result);
        result.AddLog(summary);
        ReportDiagnostics(result, request.JobId);
        jobs.Complete(request.JobId, summary);
    }

    private static void CaptureDiagnostics(
        ImportJobResult result,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<UnlinkedPersonInfo> unlinkedPersons)
    {
        foreach (var error in errors.Take(MaxCapturedErrors))
        {
            result.AddError(error);
        }

        foreach (var warning in warnings.Take(MaxCapturedWarnings))
        {
            result.AddWarning(warning);
        }

        foreach (var person in unlinkedPersons.Take(MaxCapturedUnlinked))
        {
            result.AddUnlinkedPerson(new ImportUnlinkedPersonDto(
                person.Name, person.IdCard, person.HeadIdCard, person.RowNumber));
        }
    }

    private void LogLine(ImportJobResult result, string jobId, string message)
    {
        result.AddLog(message);
        jobs.Report(jobId, message);
    }

    private void ReportDiagnostics(ImportJobResult result, string jobId)
    {
        var shownErrors = 0;
        foreach (var error in result.Errors.Take(20))
        {
            var message = $"错误：{error}";
            result.AddLog(message);
            jobs.Report(jobId, message);
            shownErrors++;
        }

        if (result.ErrorCount > shownErrors)
        {
            var message = $"另有 {result.ErrorCount - shownErrors} 条错误，详见导入日志。";
            result.AddLog(message);
            jobs.Report(jobId, message);
        }

        var shownWarnings = 0;
        foreach (var warning in result.Warnings.Take(20))
        {
            var message = $"警告：{warning}";
            result.AddLog(message);
            jobs.Report(jobId, message);
            shownWarnings++;
        }

        if (result.WarningCount > shownWarnings)
        {
            var message = $"另有 {result.WarningCount - shownWarnings} 条警告，详见导入日志。";
            result.AddLog(message);
            jobs.Report(jobId, message);
        }

        foreach (var person in result.UnlinkedPersonItems.Take(20))
        {
            var message = $"未关联人员：{FormatUnlinkedPerson(person)}";
            result.AddLog(message);
            jobs.Report(jobId, message);
        }
    }

    private static string BuildCombinedSummary(ImportJobResult result)
        => $"导入完成：家庭 {result.FamilyCount} 户、人员 {result.PersonCount} 人、"
           + $"关联 {result.LinkedCount} 条、未关联 {result.UnlinkedCount} 条"
           + $"（错误 {result.ErrorCount} / 警告 {result.WarningCount}），耗时 {FormatDuration(result.Duration)}";

    private static string BuildSingleSummary(ImportJobResult result)
        => $"导入完成：共 {result.PersonCount} 条"
           + $"（错误 {result.ErrorCount} / 警告 {result.WarningCount}），耗时 {FormatDuration(result.Duration)}";

    // ───────────────────────── 任务日志 ─────────────────────────

    /// <summary>任务结果汇总（仅发起人可见）。任务执行期间 success 为 null。</summary>
    public ImportJobSummaryDto? GetJobSummary(string jobId, int userId)
    {
        PruneResults();

        if (!_results.TryGetValue(jobId, out var result) || result.StartedBy != userId)
        {
            return null;
        }

        return new ImportJobSummaryDto(
            result.JobId,
            result.ImportType,
            result.ImportTypeName,
            result.Success,
            result.Canceled,
            result.Error,
            result.FamilyCount,
            result.PersonCount,
            result.LinkedCount,
            result.UnlinkedCount,
            result.ErrorCount,
            result.WarningCount,
            (int)Math.Round(result.Duration.TotalSeconds),
            result.UnlinkedPersonItems);
    }

    /// <summary>
    /// 读取任务日志（仅发起人可见）。任务仍在执行时回退到任务注册表快照日志。
    /// </summary>
    public ImportJobLog? GetJobLog(string jobId, int userId)
    {
        PruneResults();

        if (_results.TryGetValue(jobId, out var result) && result.StartedBy == userId)
        {
            return new ImportJobLog(
                Utf8NoBom.GetBytes(BuildJobLogText(result)),
                $"导入日志_{SafeLogFileName(jobId)}.txt");
        }

        var snapshot = jobs.Get(jobId, userId);
        if (snapshot is null
            || snapshot.StartedBy != userId
            || !string.Equals(snapshot.Kind, "import", StringComparison.Ordinal))
        {
            return null;
        }

        return new ImportJobLog(
            Utf8NoBom.GetBytes(BuildSnapshotLogText(snapshot)),
            $"导入日志_{SafeLogFileName(jobId)}.txt");
    }

    private static string BuildJobLogText(ImportJobResult result)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "数据导入日志");
        AppendLine(sb, new string('=', 48));
        AppendLine(sb, $"任务编号：{result.JobId}");
        AppendLine(sb, $"导入类型：{result.ImportTypeName}（{result.ImportType}）");
        AppendLine(sb, $"发起人：{DescribeOperator(result.OperatorName, result.StartedBy)}");
        AppendLine(sb, $"开始时间：{result.StartedAt:yyyy-MM-dd HH:mm:ss}");
        AppendLine(sb, $"结束时间：{result.FinishedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "进行中"}");
        AppendLine(sb, $"耗时：{FormatDuration(result.Duration)}");
        AppendLine(sb, $"结果：{DescribeState(result.Success, result.Canceled, result.FinishedAt is not null)}");
        AppendLine(sb, $"家庭：{result.FamilyCount} 户");
        AppendLine(sb, $"成员：{result.PersonCount} 人");
        AppendLine(sb, $"关联：{result.LinkedCount} 条");
        AppendLine(sb, $"未关联：{result.UnlinkedCount} 条");
        AppendLine(sb, $"错误：{result.ErrorCount} 条");
        AppendLine(sb, $"警告：{result.WarningCount} 条");

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AppendLine(sb, $"失败原因：{result.Error}");
        }

        if (result.ErrorCount > 0 && result.Errors.Count > 0)
        {
            AppendLine(sb, string.Empty);
            AppendLine(sb, new string('-', 48));
            AppendLine(sb, $"错误明细（共 {result.ErrorCount} 条，最多记录 {MaxCapturedErrors} 条）");
            var index = 1;
            foreach (var error in result.Errors)
            {
                AppendLine(sb, $"{index++}. {error}");
            }
        }

        if (result.WarningCount > 0 && result.Warnings.Count > 0)
        {
            AppendLine(sb, string.Empty);
            AppendLine(sb, new string('-', 48));
            AppendLine(sb, $"警告明细（共 {result.WarningCount} 条，最多记录 {MaxCapturedWarnings} 条）");
            var index = 1;
            foreach (var warning in result.Warnings)
            {
                AppendLine(sb, $"{index++}. {warning}");
            }
        }

        if (result.UnlinkedCount > 0 && result.UnlinkedPersonItems.Count > 0)
        {
            AppendLine(sb, string.Empty);
            AppendLine(sb, new string('-', 48));
            AppendLine(sb, $"未关联人员（共 {result.UnlinkedCount} 条，最多记录 {MaxCapturedUnlinked} 条）");
            var index = 1;
            foreach (var person in result.UnlinkedPersonItems)
            {
                AppendLine(sb, $"{index++}. {FormatUnlinkedPerson(person)}");
            }
        }

        AppendLine(sb, string.Empty);
        AppendLine(sb, new string('-', 48));
        AppendLine(sb, "执行日志");
        foreach (var line in result.Logs)
        {
            AppendLine(sb, line);
        }

        return sb.ToString();
    }

    private static string BuildSnapshotLogText(DataCenterJobSnapshot snapshot)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "数据导入日志");
        AppendLine(sb, new string('=', 48));
        AppendLine(sb, $"任务编号：{snapshot.JobId}");
        AppendLine(sb, $"任务名称：{snapshot.Title}");
        AppendLine(sb, $"发起人：用户#{snapshot.StartedBy}");
        AppendLine(sb, $"开始时间：{snapshot.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        AppendLine(sb, $"结束时间：{snapshot.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "进行中"}");
        AppendLine(sb, $"状态：{snapshot.State}");
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            AppendLine(sb, $"失败原因：{snapshot.Error}");
        }

        AppendLine(sb, string.Empty);
        AppendLine(sb, new string('-', 48));
        AppendLine(sb, "执行日志");
        foreach (var line in snapshot.Logs)
        {
            AppendLine(sb, line);
        }

        return sb.ToString();
    }

    private static string DescribeOperator(string operatorName, int userId)
    {
        var name = string.IsNullOrWhiteSpace(operatorName) ? $"用户#{userId}" : operatorName.Trim();
        return $"{name}（用户#{userId}）";
    }

    private static string DescribeState(bool? success, bool canceled, bool finished)
    {
        if (!finished)
        {
            return "进行中";
        }

        if (canceled)
        {
            return "已取消";
        }

        return success == true ? "成功" : "失败";
    }

    private static string SafeLogFileName(string jobId)
    {
        var name = Path.GetFileName(jobId);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    private void StoreResult(ImportJobResult result)
    {
        _results[result.JobId] = result;
        PruneResults();
    }

    private void PruneResults()
    {
        var cutoff = DateTime.Now - ResultRetention;
        foreach (var pair in _results)
        {
            if (pair.Value.FinishedAt is { } finished && finished < cutoff)
            {
                _results.TryRemove(pair.Key, out _);
            }
        }

        var expired = _results.Values
            .Where(item => item.FinishedAt.HasValue)
            .OrderByDescending(item => item.FinishedAt)
            .Skip(MaxRetainedResults)
            .ToList();
        foreach (var item in expired)
        {
            _results.TryRemove(item.JobId, out _);
        }
    }

    // ───────────────────────── 文件系统辅助 ─────────────────────────

    private string EnsureImportRootCreated()
    {
        var root = Path.GetFullPath(options.ImportRoot);
        Directory.CreateDirectory(root);
        return root;
    }

    private string EnsureInsideImportRoot(string path)
    {
        var root = Path.GetFullPath(options.ImportRoot);
        var full = Path.GetFullPath(path);
        if (!IsInside(root, full))
        {
            throw new InvalidOperationException("导入临时文件必须位于 ImportRoot 目录内");
        }

        return full;
    }

    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate);
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.Ordinal);
    }

    private string? UseExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = EnsureInsideImportRoot(path);
        return File.Exists(full) ? full : null;
    }

    private static ImportPreviewFileDto BuildPreviewFile(string path, string role)
    {
        var length = 0L;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch
        {
            // 文件刚被清理时忽略大小，仍返回文件名供前端提示。
        }

        return new ImportPreviewFileDto(Path.GetFileName(path), role, FormatSize(length));
    }

    private IImportService? FindSingleFileService(string importType)
        => singleFileServices.FirstOrDefault(
            service => string.Equals(service.ImportTypeName, importType, StringComparison.OrdinalIgnoreCase));

    private static List<ImportColumnMappingDto> MapColumns(IEnumerable<ColumnMappingInfo> mappings)
        => mappings
            .Select(item => new ImportColumnMappingDto(
                item.SourceColumn,
                item.TargetField,
                item.IsMapped))
            .ToList();

    private static DataImportPreviewResponse PreviewFailure(string message)
        => new(
            false,
            message,
            0,
            0,
            [],
            [],
            [],
            [],
            [],
            null);

    private static void AppendLine(StringBuilder builder, string text)
        => builder.Append(text).Append("\r\n");

    private static string FormatUnlinkedPerson(ImportUnlinkedPersonDto person)
        => $"{person.Name}（{person.IdCard}），户主身份证 {person.HeadIdCard}，行号 {person.RowNumber}";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0 秒";
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分 {duration.Seconds} 秒"
            : duration.TotalMinutes >= 1
                ? $"{duration.Minutes} 分 {duration.Seconds} 秒"
                : $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))} 秒";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    // ───────────────────────── 内部类型 ─────────────────────────

    /// <summary>模板导出结果（Directory 由调用方在下载后删除）。</summary>
    public sealed record TemplateExportResult(
        string? FilePath,
        string? Directory,
        string? FileName,
        string? ErrorMessage);

    /// <summary>后台导入任务启动参数（文件已落盘到 JobDirectory）。</summary>
    public sealed record ImportJobRequest(
        string JobId,
        string ImportType,
        string JobDirectory,
        string? FamilyFilePath,
        string? PersonFilePath,
        IReadOnlyList<string> FilePaths,
        ImportScope? Scope,
        bool ClearBeforeImport,
        int StartedBy,
        string OperatorName);

    /// <summary>日志下载内容。</summary>
    public sealed record ImportJobLog(byte[] Content, string FileName);

    /// <summary>
    /// 任务结果（内存保留 2 小时）。计数在入字典前写入；日志列表在任务执行中会被并发追加，统一加锁。
    /// </summary>
    public sealed class ImportJobResult(
        string jobId,
        string importType,
        string importTypeName,
        int startedBy,
        string operatorName)
    {
        private readonly object _sync = new();
        private readonly List<string> _logs = [];
        private readonly List<string> _errors = [];
        private readonly List<string> _warnings = [];
        private readonly List<ImportUnlinkedPersonDto> _unlinkedPersons = [];

        public string JobId { get; } = jobId;
        public string ImportType { get; } = importType;
        public string ImportTypeName { get; } = importTypeName;
        public int StartedBy { get; } = startedBy;
        public string OperatorName { get; } = operatorName;
        public DateTime StartedAt { get; } = DateTime.Now;
        public DateTime? FinishedAt { get; set; }
        public bool? Success { get; set; }
        public bool Canceled { get; set; }
        public string? Error { get; set; }
        public int FamilyCount { get; set; }
        public int PersonCount { get; set; }
        public int LinkedCount { get; set; }
        public int UnlinkedCount { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public TimeSpan Duration { get; set; }

        public IReadOnlyList<string> Logs
        {
            get
            {
                lock (_sync)
                {
                    return _logs.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_sync)
                {
                    return _errors.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_sync)
                {
                    return _warnings.ToArray();
                }
            }
        }

        public IReadOnlyList<ImportUnlinkedPersonDto> UnlinkedPersonItems
        {
            get
            {
                lock (_sync)
                {
                    return _unlinkedPersons.ToArray();
                }
            }
        }

        public void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (_sync)
            {
                _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (_logs.Count > MaxCapturedLogLines)
                {
                    _logs.RemoveRange(0, _logs.Count - MaxCapturedLogLines);
                }
            }
        }

        public void AddError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (_sync)
            {
                if (_errors.Count < MaxCapturedErrors)
                {
                    _errors.Add(message);
                }
            }
        }

        public void AddWarning(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (_sync)
            {
                if (_warnings.Count < MaxCapturedWarnings)
                {
                    _warnings.Add(message);
                }
            }
        }

        public void AddUnlinkedPerson(ImportUnlinkedPersonDto person)
        {
            if (person is null || string.IsNullOrWhiteSpace(person.Name))
            {
                return;
            }

            lock (_sync)
            {
                if (_unlinkedPersons.Count < MaxCapturedUnlinked)
                {
                    _unlinkedPersons.Add(person);
                }
            }
        }
    }
}
