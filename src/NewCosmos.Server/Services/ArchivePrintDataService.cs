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
public sealed class ArchivePrintDataService(ApplicationFormService forms)
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
                 fields_json, table_rows_json, supporter_table_data_json,
                 job_kind, photo_data, photo_layout, photo_category,
                 status, requested_by, requested_by_name, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11,
                        $12::jsonb, $13::jsonb, $14::jsonb,
                        $15, $16, $17, $18,
                        $21, $19, $20, NOW())
                RETURNING id, job_no, status, created_at";

        var result = await database.QuerySingleAsync<PrintJob>(sql, ct,
            jobNo, request.BusinessType, request.BusinessId, request.Classification,
            request.TemplateId, request.TemplateName,
            request.ApplicantName, request.ApplicantIdCard, request.PrinterName,
            request.Copies, request.IsDuplex,
            fieldsJson, tableRowsJson, supporterJson,
            jobKind, request.PhotoData, request.PhotoLayout, request.PhotoCategory,
            request.RequestedBy, request.RequestedByName,
            PrintJobConstants.StatusPending);

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

        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $4, agent_machine = $1, claimed_at = NOW()
                WHERE id IN (
                    SELECT id FROM nc_biz_print_jobs
                    WHERE status = $3 AND deleted_at IS NULL
                      AND (target_device_id IS NULL OR target_device_id = $5)
                    ORDER BY created_at
                    LIMIT $2
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING id, job_no, business_type, business_id, classification, template_id, template_name,
                          applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                          job_kind, photo_data, photo_layout, photo_category,
                          fields_json::text AS FieldsJson,
                          table_rows_json::text AS TableRowsJson,
                          supporter_table_data_json::text AS SupporterTableDataJson,
                          status, error_message, requested_by, requested_by_name, agent_machine,
                          created_at, claimed_at, completed_at";

        var result = await database.QueryAsync<PrintJob>(sql, ct,
            agentMachine, max, PrintJobConstants.StatusPending, PrintJobConstants.StatusProcessing, deviceId);
        if (result.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<List<PrintJob>>(result.ErrorCode!, result.Message!);
        }

        await tx.CommitAsync(ct);
        return Result.Success(result.Value ?? []);
    }

    public async Task<Result> CompleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $2, completed_at = NOW(), error_message = NULL
                WHERE id = $1";
        var result = await database.ExecuteNonQueryAsync(sql, ct, id, PrintJobConstants.StatusCompleted);
        if (result.IsFailure)
        {
            return Result.Failure(result.ErrorCode!, result.Message!);
        }

        logger.Info($"打印任务完成: Id={id}");
        return Result.Success();
    }

    public async Task<Result> FailAsync(long id, string errorMessage, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $3, completed_at = NOW(), error_message = $2
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

    public async Task<Result> RetryAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $2, agent_machine = NULL, claimed_at = NULL, completed_at = NULL, error_message = NULL
                WHERE id = $1 AND status = $3 AND deleted_at IS NULL";
        var result = await database.ExecuteNonQueryAsync(
            sql, ct, id, PrintJobConstants.StatusPending, PrintJobConstants.StatusFailed);
        return result.IsFailure
            ? Result.Failure(result.ErrorCode!, result.Message!)
            : Result.Success();
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
                                    job_kind, photo_category,
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
        const string sql = @"SELECT id, job_no, business_type, business_id, classification, template_id, template_name,
                                    applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                                    job_kind, photo_data, photo_layout, photo_category,
                                    fields_json::text AS FieldsJson,
                                    table_rows_json::text AS TableRowsJson,
                                    supporter_table_data_json::text AS SupporterTableDataJson,
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

    /// <summary>模板文件字节（Agent payload 用；file_data 必须原样下发）。</summary>
    public async Task<Result<PrintTemplateFileRow?>> GetTemplateFileAsync(
        long templateId,
        CancellationToken ct = default)
    {
        var result = await database.QuerySingleAsync<PrintTemplateFileRow?>(
            @"SELECT id, name, doc_type, file_type, file_data, config_json
              FROM nc_biz_templates
              WHERE id = $1",
            ct, templateId);

        if (result.IsFailure)
        {
            return Result.Failure<PrintTemplateFileRow?>(result.ErrorCode!, result.Message!);
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
}

/// <summary>模板文件行（payload 下发字节与映射配置）。</summary>
public sealed class PrintTemplateFileRow
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public byte[]? FileData { get; set; }
    public string? ConfigJson { get; set; }
}
