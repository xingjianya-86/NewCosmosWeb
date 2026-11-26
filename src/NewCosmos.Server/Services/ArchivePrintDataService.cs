using System.Globalization;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 档案制作页推送打印的数据装配：只读 <see cref="ApplicationFormService"/> 返回的 DTO，
/// 不重抄收入/分类/保障金公式，也不修改表单服务。
/// <para>
/// 输出键一律使用 <see cref="FieldKeys"/> 常量（PC Agent 按模板 config_json 的 fieldKey 映射到占位符）。
/// DTO 没有的字段直接跳过，不硬造、不报错。
/// </para>
/// </summary>
public sealed class ArchivePrintDataService(ApplicationFormService forms, IDatabaseService database)
{
    public async Task<Result<ArchivePrintData>> BuildAsync(
        long applicationId,
        int userId,
        CancellationToken ct = default)
    {
        var formResult = await forms.GetFormAsync(applicationId, userId, ct);
        if (formResult.IsFailure)
        {
            return Result.Failure<ArchivePrintData>(
                formResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                formResult.Message ?? "加载申请表单失败");
        }

        var form = formResult.Value;
        var basic = form.Basic;
        var economy = form.Economy;
        var classification = form.Classification;
        var data = new ArchivePrintData
        {
            ApplicantName = basic.ApplicantName ?? string.Empty,
            ApplicantIdCard = basic.ApplicantIdCard ?? string.Empty,
            Classification = FirstNonEmpty(
                classification?.ClassificationResult,
                classification?.ClassificationText)
        };

        // ── 申请人 ──
        Put(data.Fields, FieldKeys.APPLICANT_NAME, basic.ApplicantName);
        Put(data.Fields, FieldKeys.APPLICANT_ID_CARD, basic.ApplicantIdCard);
        Put(data.Fields, FieldKeys.CONTACT_PHONE, basic.Phone);
        Put(data.Fields, FieldKeys.APPLICANT_GENDER, basic.Gender);
        Put(data.Fields, FieldKeys.MARITAL_STATUS, basic.MaritalStatus);
        Put(data.Fields, FieldKeys.HEALTH_STATUS, basic.HealthStatus);
        Put(data.Fields, FieldKeys.APPLICATION_REASON, basic.ApplicationReason);
        Put(data.Fields, FieldKeys.APPLICATION_REASON_DETAIL, basic.ApplicationReasonDetail);
        Put(data.Fields, FieldKeys.STATUS, form.StatusText);

        // ── 家庭人口 ──
        // FormDto 未直接暴露 family_size；members = 户主 + 共同生活成员（不含赡养人），
        // 有成员时按行数回填，否则跳过（不臆造 1 人）。
        var memberCount = form.Members?.Count ?? 0;
        if (memberCount > 0)
        {
            var familySizeText = memberCount.ToString(CultureInfo.InvariantCulture);
            Put(data.Fields, FieldKeys.FAMILY_SIZE, familySizeText);
            Put(data.Fields, FieldKeys.HEAD_FAMILY_SIZE, familySizeText);
            Put(data.Fields, FieldKeys.APPLICANT_COUNT, familySizeText);

            var head = form.Members!.FirstOrDefault(m => m.IsHouseholdHead);
            Put(data.Fields, FieldKeys.HEAD_ID_CARD,
                FirstNonEmpty(head?.IdCard, basic.ApplicantIdCard));
        }

        // ── 地址 / 乡镇村 ──
        var hukouAddress = JoinAddress(
            basic.HukouProvince, basic.HukouCity, basic.HukouDistrict,
            basic.HukouTown, basic.HukouVillage, basic.HukouDetailAddress);
        Put(data.Fields, FieldKeys.HUKOU_ADDRESS, hukouAddress);

        var residenceAddress = JoinAddress(
            basic.Province, basic.City, basic.District,
            basic.Town, basic.Village, basic.Address);
        Put(data.Fields, FieldKeys.FAMILY_ADDRESS, residenceAddress);
        Put(data.Fields, FieldKeys.RESIDENCE_ADDRESS, residenceAddress);
        Put(data.Fields, FieldKeys.FAMILY_DETAIL_ADDRESS, basic.Address);

        Put(data.Fields, FieldKeys.TOWN, FirstNonEmpty(basic.HukouTown, basic.Town));
        Put(data.Fields, FieldKeys.COMMUNITY, FirstNonEmpty(basic.HukouVillage, basic.Village));
        Put(data.Fields, FieldKeys.FAMILY_VILLAGE, FirstNonEmpty(basic.HukouVillage, basic.Village));
        Put(data.Fields, FieldKeys.DISTRICT, FirstNonEmpty(basic.HukouDistrict, basic.District));

        // ── 申请编号 / 分类 ──
        Put(data.Fields, FieldKeys.APPLICATION_NO, form.ApplicationNo);
        // FormDto 没有独立 application_date；2026-11 口径「申请日期=调查日期」，
        // 有调查日时同值供给 APPLICATION_DATE，同时回填 SURVEY_DATE。
        Put(data.Fields, FieldKeys.SURVEY_DATE, form.Survey?.SurveyDate);
        Put(data.Fields, FieldKeys.APPLICATION_DATE, form.Survey?.SurveyDate);
        Put(data.Fields, FieldKeys.APPLY_CATEGORY, classification?.ClassificationText);
        Put(data.Fields, FieldKeys.CLASSIFICATION_RESULT,
            FirstNonEmpty(classification?.ClassificationText, classification?.ClassificationResult));

        // ── 保障金 ──
        Put(data.Fields, FieldKeys.GUARANTEE_AMOUNT,
            FormatAmount(classification?.HouseholdMonthlyGuaranteeAmount));

        // ── 收入（年口径为主，月口径顺带供给；键均来自 FieldKeys） ──
        if (economy is not null)
        {
            Put(data.Fields, FieldKeys.TOTAL_FAMILY_INCOME_ANNUAL, FormatAmount(economy.TotalAnnualIncome));
            Put(data.Fields, FieldKeys.PER_CAPITA_INCOME_ANNUAL, FormatAmount(economy.PerCapitaAnnualIncome));
            Put(data.Fields, FieldKeys.TOTAL_FAMILY_INCOME_MONTHLY, FormatAmount(economy.TotalFamilyIncome));
            Put(data.Fields, FieldKeys.PER_CAPITA_INCOME_MONTHLY, FormatAmount(economy.PerCapitaIncome));
        }

        // ── 银行 ──
        // FieldKeys 只有通用「一卡通账号」TR_BANK_ACCOUNT，没有通用开户行键；
        // 开户行名称不硬套高龄/照料人专用键，直接跳过。
        Put(data.Fields, FieldKeys.TR_BANK_ACCOUNT, basic.BankAccount);

        // ── 成员表 ──
        if (form.Members is { Count: > 0 })
        {
            foreach (var member in form.Members)
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                Put(row, FieldKeys.FAMILY_MEMBER_NAME, member.Name);
                Put(row, FieldKeys.FAMILY_MEMBER_ID_CARD, member.IdCard);
                Put(row, FieldKeys.FAMILY_MEMBER_RELATION, member.Relation);
                Put(row, FieldKeys.FAMILY_MEMBER_ADDRESS,
                    FirstNonEmpty(member.HukouAddress, member.HomeAddress));
                Put(row, FieldKeys.FAMILY_MEMBER_AGE,
                    member.Age?.ToString(CultureInfo.InvariantCulture));
                Put(row, FieldKeys.FAMILY_MEMBER_HUKOU, member.HukouType);
                Put(row, FieldKeys.FAMILY_MEMBER_EMPLOYMENT, member.EmploymentStatus);
                if (row.Count > 0)
                {
                    data.TableRows.Add(row);
                }
            }
        }

        return Result.Success(data);
    }

    /// <summary>
    /// 照片下发：只取模板 config_json 中确实声明了 <c>PHOTO_*</c> 占位符的字段，
    /// 按身份证（或申请 ID）到 nc_biz_archive_photos 取一张最合适的照片，转 Base64 供 Agent 内嵌。
    /// 无照片时不报错（返回空列表，打印件无照片）。
    /// </summary>
    public async Task<List<PrintImagePayload>> BuildPhotoImagesAsync(
        string? applicantIdCard,
        long? applicationId,
        string? templateConfigJson,
        CancellationToken ct = default)
    {
        var images = new List<PrintImagePayload>();

        var photoFields = ReadPhotoFieldMappings(templateConfigJson);
        if (photoFields.Count == 0) return images;

        var photo = await FindBestPhotoAsync(applicantIdCard, applicationId, ct);
        if (photo?.PhotoData is not { Length: > 0 }) return images;

        var base64 = Convert.ToBase64String(photo.PhotoData);
        var fileName = BuildPhotoFileName(photo);
        foreach (var (fieldKey, placeholder) in photoFields)
        {
            images.Add(new PrintImagePayload
            {
                FieldKey = fieldKey,
                Placeholder = placeholder,
                Base64 = base64,
                FileName = fileName
            });
        }

        return images;
    }

    /// <summary>
    /// 取照片优先级：本人证件照 &gt; 身份证 &gt; 户口本 &gt; 其他证件照 &gt; 其余类别，同类取最新。
    /// 身份证号为空时退化为按 application_id 查。
    /// </summary>
    private async Task<ArchivePhotoRow?> FindBestPhotoAsync(
        string? applicantIdCard,
        long? applicationId,
        CancellationToken ct)
    {
        var idCard = applicantIdCard?.Trim() ?? string.Empty;
        if (idCard.Length == 0 && applicationId is not > 0) return null;

        const string sql = @"SELECT id, photo_data, photo_category, photo_name, image_format
                             FROM nc_biz_archive_photos
                             WHERE photo_data IS NOT NULL
                               AND ((NULLIF($1::text, '') IS NOT NULL AND applicant_id_card = $1)
                                    OR ($2::bigint IS NOT NULL AND application_id = $2))
                             ORDER BY CASE photo_category
                                          WHEN $3 THEN 0
                                          WHEN $4 THEN 1
                                          WHEN $5 THEN 2
                                          WHEN $6 THEN 3
                                          ELSE 9
                                      END,
                                      created_at DESC
                             LIMIT 1";

        var result = await database.QuerySingleAsync<ArchivePhotoRow>(
            sql, ct,
            idCard,
            applicationId is > 0 ? applicationId.Value : null,
            PhotoCategoryConstants.PERSON_PHOTO,
            PhotoCategoryConstants.ID_CARD,
            PhotoCategoryConstants.HOUSEHOLD_REGISTER,
            PhotoCategoryConstants.OTHER_CERT);

        if (result.IsFailure)
        {
            // 照片查询失败不阻断打印（与 legacy PrintExecuteService 口径一致）。
            return null;
        }

        return result.Value;
    }

    private static List<(string FieldKey, string Placeholder)> ReadPhotoFieldMappings(string? configJson)
    {
        var mappings = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(configJson)) return mappings;

        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return mappings;
            if (!TryGetProperty(document.RootElement, "fields", out var fields)
                || fields.ValueKind != JsonValueKind.Array)
            {
                return mappings;
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var seenPlaceholders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object) continue;

                var fieldKey = TryGetProperty(field, "fieldKey", out var keyElement)
                               && keyElement.ValueKind == JsonValueKind.String
                    ? keyElement.GetString()
                    : null;
                var placeholder = TryGetProperty(field, "placeholder", out var placeholderElement)
                                  && placeholderElement.ValueKind == JsonValueKind.String
                    ? placeholderElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(fieldKey) || string.IsNullOrWhiteSpace(placeholder)) continue;
                if (!fieldKey.TrimStart().StartsWith("PHOTO_", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seenKeys.Add(fieldKey.Trim())) continue;
                if (!seenPlaceholders.Add(placeholder.Trim())) continue;

                mappings.Add((fieldKey.Trim(), placeholder.Trim()));
            }
        }
        catch (JsonException)
        {
            return mappings;
        }

        return mappings;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string BuildPhotoFileName(ArchivePhotoRow photo)
    {
        var safeName = string.IsNullOrWhiteSpace(photo.PhotoName)
            ? $"photo-{photo.Id}"
            : photo.PhotoName!.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalid, '_');

        var extension = (photo.ImageFormat ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "png" => ".png",
            "jpeg" or "jpg" => ".jpg",
            _ => ".webp"
        };

        return safeName + extension;
    }

    private static void Put(Dictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? JoinAddress(params string?[] parts)
    {
        var joined = string.Concat(parts.Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? FormatAmount(decimal? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>档案推送打印数据快照（字段键 = FieldKeys，表格行 = 成员表）。</summary>
public sealed class ArchivePrintData
{
    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);

    public List<Dictionary<string, string>> TableRows { get; } = new();

    public string ApplicantName { get; set; } = string.Empty;

    public string ApplicantIdCard { get; set; } = string.Empty;

    public string? Classification { get; set; }
}

/// <summary>照片表最小行（照片嵌入用；只读，不向前端暴露）。</summary>
public sealed class ArchivePhotoRow
{
    public long Id { get; set; }
    public byte[]? PhotoData { get; set; }
    public string? PhotoCategory { get; set; }
    public string? PhotoName { get; set; }
    public string? ImageFormat { get; set; }
}

/// <summary>
/// Server 侧打印队列网关。
/// <para>
/// 为什么需要它：队列契约 <c>IPrintQueueService</c> 位于 NewCosmos.Office（net10.0-windows），
/// 而 Server 是 net10.0 且不引用 Office（Linux 部署不装 COM）。因此这里按 Office
/// <c>PrintQueueService</c> 的同一张表、同一状态机、同一 JSONB 快照结构做最小镜像，
/// 端点只调用本网关，不直接拼 INSERT/UPDATE 绕过队列。
/// </para>
/// <para>
/// 若后续把 IPrintQueueService 下沉到 NewCosmos.Core，应删除本类并改注入 Core 契约。
/// </para>
/// </summary>
public sealed class ArchivePrintQueueService(IDatabaseService database, ILoggerService logger)
{
    /// <summary>最大重试次数：超时回收与 Agent 失败累计，达到后不再自动/手动重试。</summary>
    public const int MaxRetryCount = 5;

    /// <summary>Processing 超过该时长未回报视为 Agent 失联，回收为 Pending。</summary>
    public const int ProcessingTimeoutMinutes = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task<Result<PrintJob>> EnqueueAsync(PrintJobRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessType))
        {
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "业务类型不能为空");
        }

        var jobNo = $"PJ{DateTime.Now:yyyyMMddHHmmssfff}";
        var fieldsJson = JsonSerializer.Serialize(request.Fields ?? new(), JsonOptions);
        var tableRowsJson = JsonSerializer.Serialize(request.TableRows ?? new(), JsonOptions);
        var supporterJson = request.SupporterTableData == null
            ? null
            : JsonSerializer.Serialize(request.SupporterTableData, JsonOptions);
        var imagesJson = request.Images is { Count: > 0 }
            ? JsonSerializer.Serialize(request.Images, JsonOptions)
            : null;
        var batchJson = request.Batch is null
            ? null
            : JsonSerializer.Serialize(request.Batch, JsonOptions);

        var jobKind = string.IsNullOrWhiteSpace(request.JobKind)
            ? PrintJobConstants.JobKindTemplate
            : request.JobKind;
        if (jobKind == PrintJobConstants.JobKindPhoto
            && (request.PhotoData == null || request.PhotoData.Length == 0))
        {
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "照片打印任务缺少照片数据");
        }

        const string sql = @"INSERT INTO nc_biz_print_jobs
                (job_no, business_type, business_id, classification, template_id, template_name,
                 applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                 fields_json, table_rows_json, supporter_table_data_json, images_json,
                 job_kind, photo_data, photo_layout, photo_category,
                 status, requested_by, requested_by_name, created_at,
                 target_user_id, target_device_id, target_printer, batch_json, batch_no)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11,
                        $12::jsonb, $13::jsonb, $14::jsonb, $15::jsonb,
                        $16, $17, $18, $19,
                        $22, $20, $21, NOW(),
                        $23, $24, $25, $26::jsonb, $27)
                RETURNING id, job_no, status, created_at";

        var result = await database.QuerySingleAsync<PrintJob>(sql, ct,
            jobNo, request.BusinessType, request.BusinessId, request.Classification,
            request.TemplateId, request.TemplateName,
            request.ApplicantName, request.ApplicantIdCard, request.PrinterName,
            request.Copies, request.IsDuplex,
            fieldsJson, tableRowsJson, supporterJson, imagesJson,
            jobKind, request.PhotoData, request.PhotoLayout, request.PhotoCategory,
            request.RequestedBy, request.RequestedByName,
            batchJson is null ? PrintJobConstants.StatusPending : PrintJobConstants.StatusBatchPending,
            request.TargetUserId, request.TargetDeviceId, request.TargetPrinter,
            batchJson, request.BatchNo);

        if (result.IsFailure)
        {
            logger.Error($"打印任务入队失败: {result.Message}");
            return result;
        }

        var job = result.Value!;
        job.JobNo = jobNo;
        job.BusinessType = request.BusinessType;
        job.TemplateName = request.TemplateName;
        logger.Info($"打印任务入队: {jobNo} ({request.BusinessType}, Kind={jobKind})");

        var full = await GetByIdAsync(job.Id, ct);
        if (full.HasValue)
        {
            return Result.Success(full.Value);
        }

        logger.Warn($"打印任务回读失败，返回入队结果: JobNo={jobNo}, {full.ErrorCode} {full.Message}");
        return Result.Success(job);
    }

    public async Task<Result<List<PrintJob>>> ClaimPendingAsync(
        int max,
        string agentMachine,
        long deviceId,
        CancellationToken ct = default)
    {
        if (max <= 0) max = 5;

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        // 1) 超时回收：Processing 且 retry_count 已达上限 → Failed（必须先于重新排队执行，
        //    否则这些任务会被 +1 后继续排队，突破上限）。
        const string failExpiredSql = @"UPDATE nc_biz_print_jobs
                SET status = $1, agent_machine = NULL, completed_at = NOW(), error_message = $2
                WHERE status = $3 AND deleted_at IS NULL
                  AND claimed_at < NOW() - ($5 * interval '1 minute')
                  AND retry_count >= $4";

        var failExpired = await database.ExecuteNonQueryAsync(
            failExpiredSql, ct,
            PrintJobConstants.StatusFailed, "超过最大重试次数", PrintJobConstants.StatusProcessing,
            MaxRetryCount, ProcessingTimeoutMinutes);

        // 2) 超时回收：Processing 未达上限 → Pending，清 agent_machine，retry_count + 1。
        const string recycleSql = @"UPDATE nc_biz_print_jobs
                SET status = CASE WHEN batch_json IS NOT NULL THEN $6 ELSE $1 END,
                    agent_machine = NULL, claimed_at = NULL, completed_at = NULL,
                    retry_count = retry_count + 1, error_message = $2
                WHERE status = $3 AND deleted_at IS NULL
                  AND claimed_at < NOW() - ($5 * interval '1 minute')
                  AND retry_count < $4";

        var recycled = await database.ExecuteNonQueryAsync(
            recycleSql, ct,
            PrintJobConstants.StatusPending, "任务执行超时（10 分钟无回报），已自动重新排队",
            PrintJobConstants.StatusProcessing, MaxRetryCount, ProcessingTimeoutMinutes,
            PrintJobConstants.StatusBatchPending);

        if (failExpired.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<List<PrintJob>>(failExpired.ErrorCode!, failExpired.Message!);
        }

        if (recycled.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<List<PrintJob>>(recycled.ErrorCode!, recycled.Message!);
        }

        if (failExpired.Value > 0 || recycled.Value > 0)
        {
            logger.Warn($"打印任务超时回收: 置失败 {failExpired.Value} 个（重试已达 {MaxRetryCount} 次），重新排队 {recycled.Value} 个");
        }

        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $4, agent_machine = $1, claimed_at = NOW()
                WHERE id IN (
                    SELECT id FROM nc_biz_print_jobs
                    WHERE status = ANY($3::text[]) AND deleted_at IS NULL
                      AND target_device_id = $5
                    ORDER BY created_at
                    LIMIT $2
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING id, job_no, business_type, business_id, classification, template_id, template_name,
                          applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                          job_kind, photo_data, photo_layout, photo_category, retry_count,
                          fields_json::text AS FieldsJson,
                          table_rows_json::text AS TableRowsJson,
                          supporter_table_data_json::text AS SupporterTableDataJson,
                          images_json::text AS ImagesJson,
                          status, error_message, requested_by, requested_by_name, agent_machine,
                          created_at, claimed_at, completed_at";

        var result = await database.QueryAsync<PrintJob>(sql, ct,
            agentMachine, max,
            new[] { PrintJobConstants.StatusPending, PrintJobConstants.StatusBatchPending },
            PrintJobConstants.StatusProcessing, deviceId);
        if (result.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<List<PrintJob>>(result.ErrorCode!, result.Message!);
        }

        await tx.CommitAsync(ct);
        return Result.Success(result.Value ?? []);
    }

    /// <summary>
    /// 完成回写。<paramref name="message"/> 为可选警告文案（如降级为系统 PDF 打印），
    /// 写入 error_message 供 Web 任务列表展示；任务本身仍为 Completed，不因警告标失败。
    /// </summary>
    public async Task<Result> CompleteAsync(long id, string? message = null, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $2, completed_at = NOW(), error_message = $3
                WHERE id = $1";
        var trimmed = string.IsNullOrWhiteSpace(message) ? null : Truncate(message, 2000);
        var result = await database.ExecuteNonQueryAsync(sql, ct, id, PrintJobConstants.StatusCompleted, trimmed);
        if (result.IsFailure)
        {
            return Result.Failure(result.ErrorCode!, result.Message!);
        }

        logger.Info(string.IsNullOrEmpty(trimmed)
            ? $"打印任务完成: Id={id}"
            : $"打印任务完成（有警告）: Id={id} - {trimmed}");
        return Result.Success();
    }

    public async Task<Result> FailAsync(long id, string errorMessage, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $3, completed_at = NOW(), error_message = $2,
                    retry_count = retry_count + 1
                WHERE id = $1";
        var result = await database.ExecuteNonQueryAsync(
            sql, ct, id, Truncate(errorMessage, 2000), PrintJobConstants.StatusFailed);
        if (result.IsFailure)
        {
            return Result.Failure(result.ErrorCode!, result.Message!);
        }

        logger.Warn($"打印任务失败: Id={id} - {errorMessage}");
        return Result.Success();
    }

    /// <summary>失败任务重试：retry_count 已达上限（{MaxRetryCount}）时拒绝。</summary>
    public async Task<Result> RetryAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = CASE WHEN batch_json IS NOT NULL THEN $5 ELSE $2 END,
                    agent_machine = NULL, claimed_at = NULL, completed_at = NULL, error_message = NULL
                WHERE id = $1 AND status = $3 AND deleted_at IS NULL AND retry_count < $4";
        var result = await database.ExecuteNonQueryAsync(
            sql, ct, id, PrintJobConstants.StatusPending, PrintJobConstants.StatusFailed, MaxRetryCount,
            PrintJobConstants.StatusBatchPending);
        if (result.IsFailure)
        {
            return Result.Failure(result.ErrorCode!, result.Message!);
        }

        if (result.Value <= 0)
        {
            return Result.Failure(
                ErrorCodes.VALIDATION_FAILED,
                $"超过最大重试次数（{MaxRetryCount}），请人工处理");
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET deleted_at = NOW()
                WHERE id = $1 AND status <> $2 AND deleted_at IS NULL";
        var result = await database.ExecuteNonQueryAsync(
            sql, ct, id, PrintJobConstants.StatusProcessing);
        return result.IsFailure
            ? Result.Failure(result.ErrorCode!, result.Message!)
            : Result.Success();
    }

    public async Task<Result<PagedResult<PrintJob>>> GetPagedAsync(
        string? status,
        int requestedBy,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var statusFilter = string.IsNullOrWhiteSpace(status) ? string.Empty : status;
        var offset = (pageIndex - 1) * pageSize;

        const string sql = @"SELECT id, job_no, business_type, business_id, classification, template_id, template_name,
                                    applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                                    job_kind, photo_category, retry_count,
                                    status, error_message, requested_by, requested_by_name, agent_machine,
                                    created_at, claimed_at, completed_at
                             FROM nc_biz_print_jobs
                             WHERE deleted_at IS NULL
                               AND ($1 = '' OR status = $1)
                               AND requested_by = $2
                             ORDER BY created_at DESC
                             LIMIT $3 OFFSET $4";

        var result = await database.QueryAsync<PrintJob>(sql, ct, statusFilter, requestedBy, pageSize, offset);
        if (result.IsFailure)
        {
            return Result.Failure<PagedResult<PrintJob>>(result.ErrorCode!, result.Message!);
        }

        var countResult = await database.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*) FROM nc_biz_print_jobs
              WHERE deleted_at IS NULL
                AND ($1 = '' OR status = $1)
                AND requested_by = $2",
            ct, statusFilter, requestedBy);
        var total = countResult.IsSuccess ? (int)countResult.Value : 0;

        return Result.Success(PagedResult<PrintJob>.FromList(
            result.Value ?? [], pageIndex, pageSize, total));
    }

    public async Task<Result<PrintJob>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"SELECT id, job_no, batch_no, business_type, business_id, classification, template_id, template_name,
                                    applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                                    job_kind, photo_data, photo_layout, photo_category, retry_count,
                                    fields_json::text AS FieldsJson,
                                    table_rows_json::text AS TableRowsJson,
                                    supporter_table_data_json::text AS SupporterTableDataJson,
                                    images_json::text AS ImagesJson,
                                    batch_json::text AS BatchJson,
                                    status, error_message, requested_by, requested_by_name, agent_machine,
                                    created_at, claimed_at, completed_at
                             FROM nc_biz_print_jobs WHERE id = $1 AND deleted_at IS NULL";
        return await database.QuerySingleAsync<PrintJob>(sql, ct, id);
    }

    /// <summary>按设备密钥解析 Agent 设备（不存在返回 null）。</summary>
    public async Task<Result<PrintAgentDeviceRow?>> GetDeviceByKeyAsync(
        Guid deviceKey,
        CancellationToken ct = default)
    {
        var result = await database.QuerySingleAsync<PrintAgentDeviceRow?>(
            @"SELECT id, machine_name, device_key
              FROM nc_sys_print_devices
              WHERE device_key = $1",
            ct, deviceKey);

        if (result.IsFailure)
        {
            return Result.Failure<PrintAgentDeviceRow?>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value);
    }

    /// <summary>模板元数据（不读 file_data，供列表/入队校验）。</summary>
    public async Task<Result<PrintTemplateMetaRow?>> GetTemplateMetaAsync(
        long templateId,
        CancellationToken ct = default)
    {
        var result = await database.QuerySingleAsync<PrintTemplateMetaRow?>(
            @"SELECT id, name, doc_type, file_type, sort_order
              FROM nc_biz_templates
              WHERE id = $1",
            ct, templateId);

        if (result.IsFailure)
        {
            return Result.Failure<PrintTemplateMetaRow?>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value);
    }

    /// <summary>模板文件字节（Agent payload 用；file_data 必须原样下发）。file_hash 供 Agent 本地缓存键。</summary>
    public async Task<Result<PrintTemplateFileRow?>> GetTemplateFileAsync(
        long templateId,
        CancellationToken ct = default)
    {
        var result = await database.QuerySingleAsync<PrintTemplateFileRow?>(
            @"SELECT id, name, doc_type, file_type, file_data, file_hash, config_json
              FROM nc_biz_templates
              WHERE id = $1",
            ct, templateId);

        if (result.IsFailure)
        {
            return Result.Failure<PrintTemplateFileRow?>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value);
    }

    /// <summary>模板 config_json（入队时判断 PHOTO_* 字段与缺字段提示用；不读 file_data）。</summary>
    public async Task<Result<string?>> GetTemplateConfigAsync(long templateId, CancellationToken ct = default)
    {
        var result = await database.QuerySingleAsync<string?>(
            @"SELECT config_json FROM nc_biz_templates WHERE id = $1",
            ct, templateId);

        if (result.IsFailure)
        {
            return Result.Failure<string?>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value);
    }

    /// <summary>按业务域列出模板（只返回元数据，不含 file_data）。</summary>
    public async Task<Result<List<PrintTemplateMetaRow>>> ListTemplatesAsync(
        string[]? categories,
        CancellationToken ct = default)
    {
        var sql = @"SELECT id, name, doc_type, file_type, sort_order
                    FROM nc_biz_templates";
        object?[] parameters;
        if (categories is { Length: > 0 })
        {
            sql += " WHERE categories && $1::text[]";
            parameters = [categories];
        }
        else
        {
            parameters = [];
        }

        sql += " ORDER BY sort_order ASC, name ASC";

        var result = await database.QueryAsync<PrintTemplateMetaRow>(sql, ct, parameters);
        if (result.IsFailure)
        {
            return Result.Failure<List<PrintTemplateMetaRow>>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value ?? []);
    }

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);
}

/// <summary>Agent 设备最小行（device_key 已在端点校验，不向下游暴露）。</summary>
public sealed class PrintAgentDeviceRow
{
    public long Id { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public Guid DeviceKey { get; set; }
}

/// <summary>模板元数据行（不含 file_data）。</summary>
public sealed class PrintTemplateMetaRow
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>模板配置 JSON（仅按 ID 装配输出任务时读取；列表接口不查此列）。</summary>
    public string? ConfigJson { get; set; }
}

/// <summary>模板文件行（payload 下发字节与映射配置）。</summary>
public sealed class PrintTemplateFileRow
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public byte[]? FileData { get; set; }

    /// <summary>模板文件 SHA256（历史数据可能为空，payload 侧按字节现算兜底）。</summary>
    public string? FileHash { get; set; }

    public string? ConfigJson { get; set; }
}
