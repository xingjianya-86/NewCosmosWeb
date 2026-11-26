using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Server.Services;

/// <summary>
/// 每月公示文档输出：逐行移植旧版
/// <c>Services/Domain/ArchiveManagement/PublicityOutputService.cs</c> 的取数与装配口径。
/// <para>
/// 两种口径必须同时保留（旧版既有行为）：
/// ① 预览名单（families）：只取 5 个导入库全部在保家庭，不含当前库，不做退出排除；
/// ② 生成名单（groups）：5 个导入库为基数 + 当前库已审批在保户补充 − 退出排除，并标记近亲属备案。
/// </para>
/// <para>
/// 服务端不重算金额/分类，只搬运导入库/当前库已有值；月份只影响生成时的退出截止与文档周期，
/// 不过滤预览名单。Office 渲染/保存由 Agent 执行，本服务只装配批量任务。
/// </para>
/// </summary>
public sealed class PublicityService(
    IDatabaseService database,
    ArchivePrintQueueService queue,
    PrinterBindingService printers,
    IOrganizationService organizations,
    ILoggerService logger)
{
    /// <summary>公示单模板名称（对应 nc_biz_templates.name）。</summary>
    public const string TemplateName = "公共_每月公示名单";

    /// <summary>每页最大数据行数（模板第 6~27 行固定 22 行）。</summary>
    public const int RowsPerPage = 22;

    /// <summary>输出分类目录（OutputCategories.PublicityFile = 公示文件）。</summary>
    public const string OutputCategory = OutputCategories.PublicityFile;

    /// <summary>类别显示排序（村内顺序），与旧版一致。</summary>
    private static readonly Dictionary<string, int> CategoryOrder = new(StringComparer.Ordinal)
    {
        ["农村低保"] = 0,
        ["城市低保"] = 1,
        ["特困供养"] = 2,
        ["低保边缘"] = 3,
        ["刚性支出"] = 4,
    };

    // ───────────────────────────── 预览名单 ─────────────────────────────

    /// <summary>
    /// 旧版「加载公示名单」口径：5 个导入库全部在保家庭，按村分组。
    /// 年份/月份只回显，不参与取数过滤（旧版行为）。
    /// </summary>
    public async Task<Result<PublicityListDto>> GetPreviewAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        var importedResult = await QueryImportedFamiliesAsync(ct);
        if (importedResult.IsFailure)
        {
            return Result.Failure<PublicityListDto>(
                importedResult.ErrorCode!, importedResult.Message!);
        }

        var rows = importedResult.Value ?? [];
        logger.Info($"每月公示-预览取数完成: {year}-{month:D2}, {rows.Count} 户");

        return Result.Success(new PublicityListDto
        {
            Year = year,
            Month = month,
            TotalFamilies = rows.Count,
            Villages = GroupByVillage(rows, includeNearRelative: false),
        });
    }

    // ───────────────────────────── 生成名单 ─────────────────────────────

    /// <summary>
    /// 旧版「生成公示文档」口径：导入库基数 + 当前库已审批在保补充 − 退出排除，
    /// 按村分组并标记近亲属备案。
    /// </summary>
    public async Task<Result<PublicityListDto>> GetGroupsAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        var importedResult = await QueryImportedFamiliesAsync(ct);
        if (importedResult.IsFailure)
        {
            return Result.Failure<PublicityListDto>(
                importedResult.ErrorCode!, importedResult.Message!);
        }

        var imported = importedResult.Value ?? [];
        var monthEnd = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMonths(1)
            .AddDays(-1);

        var exitedResult = await LoadExitedIdCardSetAsync(monthEnd, ct);
        if (exitedResult.IsFailure)
        {
            return Result.Failure<PublicityListDto>(
                exitedResult.ErrorCode!, exitedResult.Message!);
        }

        var currentResult = await QueryActiveCurrentFamiliesAsync(monthEnd, ct);
        if (currentResult.IsFailure)
        {
            return Result.Failure<PublicityListDto>(
                currentResult.ErrorCode!, currentResult.Message!);
        }

        var exitedIds = exitedResult.Value ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = currentResult.Value ?? [];

        // 导入库优先；当前库只补导入库没有的户；两者均剔除退出身份证。
        var merged = new Dictionary<string, PublicityFamilyRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in imported)
        {
            if (string.IsNullOrWhiteSpace(family.ApplicantIdCard)) continue;
            if (exitedIds.Contains(family.ApplicantIdCard)) continue;
            merged.TryAdd(family.ApplicantIdCard, family);
        }

        foreach (var family in current)
        {
            if (string.IsNullOrWhiteSpace(family.ApplicantIdCard)) continue;
            if (exitedIds.Contains(family.ApplicantIdCard)) continue;
            merged.TryAdd(family.ApplicantIdCard, family);
        }

        var rows = merged.Values.ToList();
        await MarkNearRelativeAsync(rows, ct);

        logger.Info(
            $"每月公示-生成取数完成: {year}-{month:D2}, " +
            $"导入库 {imported.Count} + 当前库 {current.Count} - 退出 {exitedIds.Count} = {rows.Count} 户");

        return Result.Success(new PublicityListDto
        {
            Year = year,
            Month = month,
            TotalFamilies = rows.Count,
            Villages = GroupByVillage(rows, includeNearRelative: true),
        });
    }

    // ───────────────────────────── 生成文档任务 ─────────────────────────────

    /// <summary>
    /// 按村生成每月公示名单文档：每村每页 22 户，一个文件一个 <see cref="PrintBatchPart"/>，
    /// 全部 part 放入同一个 TemplateBatch 任务（Mode=save），由 Agent 保存到本机输出目录并打开。
    /// </summary>
    public async Task<Result<PublicityJobResultDto>> CreateJobsAsync(
        int userId,
        string operatorName,
        int? organizationId,
        int year,
        int month,
        IReadOnlyList<string>? villages,
        CancellationToken ct = default)
    {
        var groupsResult = await GetGroupsAsync(year, month, ct);
        if (groupsResult.IsFailure)
        {
            return Result.Failure<PublicityJobResultDto>(
                groupsResult.ErrorCode!, groupsResult.Message!);
        }

        var list = groupsResult.Value!;
        var groups = list.Villages;

        var selectedVillages = (villages ?? [])
            .Select(village => village?.Trim() ?? string.Empty)
            .Where(village => village.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedVillages.Count > 0)
        {
            groups = groups
                .Where(group => selectedVillages.Contains(group.Village))
                .ToList();
        }

        if (groups.Count == 0 || list.TotalFamilies == 0)
        {
            return Result.Failure<PublicityJobResultDto>(
                ErrorCodes.VALIDATION_FAILED,
                selectedVillages.Count > 0
                    ? "所选村在本月公示名单中没有数据"
                    : "公示名单无数据");
        }

        var templateResult = await LoadTemplateAsync(ct);
        if (templateResult.IsFailure)
        {
            return Result.Failure<PublicityJobResultDto>(
                templateResult.ErrorCode!, templateResult.Message!);
        }

        var template = templateResult.Value!;

        var targetResult = await printers.ResolveTargetAsync(userId, null, null, ct);
        if (targetResult.IsFailure)
        {
            return Result.Failure<PublicityJobResultDto>(
                targetResult.ErrorCode!, targetResult.Message!);
        }

        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0)
        {
            return Result.Failure<PublicityJobResultDto>(
                ErrorCodes.VALIDATION_FAILED,
                "当前账号未绑定打印机，请先在打印机绑定页生成绑定码并在本机 Agent 完成绑定");
        }

        var (county, town, townPhone) = await ResolveHeaderAsync(organizationId, ct);
        var period = $"{year:D4}-{month:D2}";
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var publicityPeriod = $"{year}年{month}月1日至{year}年{month}月{daysInMonth}日";

        var batch = new PrintBatchDefinition
        {
            Mode = "save",
            Category = OutputCategory,
            Period = period,
        };

        var summaries = new List<PublicityJobSummaryDto>();
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var safeVillage = SanitizeFileName(group.Village);
            var pageCount = (int)Math.Ceiling(group.Families.Count / (double)RowsPerPage);

            for (var page = 0; page < pageCount; page++)
            {
                var slice = group.Families
                    .Skip(page * RowsPerPage)
                    .Take(RowsPerPage)
                    .ToList();

                var baseName = pageCount == 1
                    ? $"{safeVillage}_社会救助对象公示单"
                    : $"{safeVillage}_社会救助对象公示单_第{page + 1}页";

                batch.Parts.Add(new PrintBatchPart
                {
                    TemplateId = template.Id,
                    TemplateName = template.Name,
                    FileName = $"{baseName}.xlsx",
                    Fields = BuildPublicityFields(
                        county, town, publicityPeriod, townPhone, slice, group.Village),
                });
            }

            summaries.Add(new PublicityJobSummaryDto
            {
                Village = group.Village,
                PageCount = pageCount,
            });
        }

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.Publicity,
            BusinessId = long.Parse($"{year:D4}{month:D2}", CultureInfo.InvariantCulture),
            Classification = OutputCategory,
            TemplateId = template.Id,
            TemplateName = template.Name,
            ApplicantName = $"每月公示名单 {year}年{month}月",
            ApplicantIdCard = "-",
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = 1,
            IsDuplex = false,
            RequestedBy = userId,
            RequestedByName = operatorName,
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch,
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
        {
            return Result.Failure<PublicityJobResultDto>(
                enqueueResult.ErrorCode!, enqueueResult.Message!);
        }

        var job = enqueueResult.Value!;
        foreach (var summary in summaries)
        {
            summary.Id = job.Id;
            summary.JobNo = job.JobNo;
        }

        var outputHint =
            $"已提交保存任务：Agent 将把 {batch.Parts.Count} 个文件保存到本机" +
            $"「{OutputCategory}\\{period}」目录并自动打开";

        logger.Info(
            $"每月公示-文档任务入队: JobNo={job.JobNo}, 村={summaries.Count}, 文件={batch.Parts.Count}, " +
            $"Period={period}");

        return Result.Success(new PublicityJobResultDto
        {
            Jobs = summaries,
            OutputHint = outputHint,
        });
    }

    // ───────────────────────────── 取数 ─────────────────────────────

    /// <summary>5 个导入库全量在保家庭 UNION（表名全部来自本服务白名单，禁止外部传入）。</summary>
    private async Task<Result<List<PublicityFamilyRow>>> QueryImportedFamiliesAsync(CancellationToken ct)
    {
        const string sql = @"
                SELECT '农村低保' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(NULLIF(guarantee_size, 0), family_size, 0) AS guarantee_size,
                       COALESCE(monthly_guarantee_amount, 0) AS base_amount,
                       COALESCE(family_classified_amount, 0) AS family_classified,
                       COALESCE(person_classified_amount, 0) AS person_classified,
                       0 AS care_cost,
                       COALESCE(total_amount, 0) AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_rural_subsistence_families
                UNION ALL
                SELECT '城市低保' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(NULLIF(guarantee_size, 0), family_size, 0) AS guarantee_size,
                       COALESCE(monthly_guarantee_amount, 0) AS base_amount,
                       COALESCE(family_classified_amount, 0) AS family_classified,
                       COALESCE(person_classified_amount, 0) AS person_classified,
                       0 AS care_cost,
                       COALESCE(total_amount, 0) AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_urban_subsistence_families
                UNION ALL
                SELECT '低保边缘' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(guarantee_size, 0) AS guarantee_size,
                       0 AS base_amount, 0 AS family_classified, 0 AS person_classified, 0 AS care_cost,
                       0 AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_low_income_edge_families
                UNION ALL
                SELECT '特困供养' AS category, applicant_name, applicant_id_card, phone, hukou_address AS address, community,
                       COALESCE(NULLIF(destitute_count, 0), family_size, 0) AS guarantee_size,
                       COALESCE(basic_living_cost, 0) AS base_amount,
                       COALESCE(family_classified_amount, 0) AS family_classified,
                       COALESCE(person_classified_amount, 0) AS person_classified,
                       COALESCE(care_cost, 0) AS care_cost,
                       COALESCE(total_amount, 0) AS total_amount,
                       COALESCE(support_mode, '') AS remark
                  FROM nc_biz_destitute_families
                UNION ALL
                SELECT '刚性支出' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(guarantee_size, 0) AS guarantee_size,
                       0 AS base_amount, 0 AS family_classified, 0 AS person_classified, 0 AS care_cost,
                       0 AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_rigid_expenditure_families";

        return await database.QueryAsync<PublicityFamilyRow>(sql, ct);
    }

    /// <summary>
    /// 退出排除集合：当前库已停保（Stopped 且 stop_date ≤ 月末）的户主身份证 +
    /// 死亡记录中 is_household_head=true 的身份证。
    /// 停旧建新接续、渐退接续、真·首拒不算退出（复用 <see cref="StoppedArchiveFilter"/>）。
    /// </summary>
    private async Task<Result<HashSet<string>>> LoadExitedIdCardSetAsync(
        DateTime monthEnd,
        CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var stoppedSql = $@"
                SELECT applicant_id_card FROM nc_biz_applications
                 WHERE status = $1 AND stop_date IS NOT NULL AND stop_date <= $2::date AND deleted_at IS NULL
                 {StoppedArchiveFilter.NotRebuildContinuationSql("nc_biz_applications")}
                 {StoppedArchiveFilter.NotGraceContinuationSql("nc_biz_applications")}
                 {StoppedArchiveFilter.NotFirstRefusalSql("nc_biz_applications")}";

        var stoppedResult = await database.QueryAsync<IdCardRow>(
            stoppedSql, ct, ApplicationStatusCodes.STOPPED, monthEnd);
        if (stoppedResult.IsFailure)
        {
            return Result.Failure<HashSet<string>>(
                stoppedResult.ErrorCode!, stoppedResult.Message!);
        }

        foreach (var row in stoppedResult.Value ?? [])
        {
            if (!string.IsNullOrWhiteSpace(row.IdCard)) ids.Add(row.IdCard.Trim());
        }

        var deathResult = await database.QueryAsync<IdCardRow>(
            @"SELECT member_id_card AS id_card FROM nc_biz_death_records WHERE is_household_head = true",
            ct);
        if (deathResult.IsFailure)
        {
            return Result.Failure<HashSet<string>>(
                deathResult.ErrorCode!, deathResult.Message!);
        }

        foreach (var row in deathResult.Value ?? [])
        {
            if (!string.IsNullOrWhiteSpace(row.IdCard)) ids.Add(row.IdCard.Trim());
        }

        return Result.Success(ids);
    }

    /// <summary>
    /// 当前库在保户（Approved 且未停保），作为「新增纳入」的补充来源。
    /// 类别显示名映射为与导入库一致的口径。
    /// </summary>
    private async Task<Result<List<PublicityFamilyRow>>> QueryActiveCurrentFamiliesAsync(
        DateTime monthEnd,
        CancellationToken ct)
    {
        const string sql = @"
                SELECT
                    CASE classification_result
                        WHEN 'RuralSubsistence' THEN '农村低保'
                        WHEN 'UrbanSubsistence' THEN '城市低保'
                        WHEN 'RuralLowIncome' THEN '低保边缘'
                        WHEN 'UrbanLowIncome' THEN '低保边缘'
                        WHEN 'RuralLowIncomeSingle' THEN '低保边缘'
                        WHEN 'UrbanLowIncomeSingle' THEN '低保边缘'
                        WHEN 'RuralDestituteScattered' THEN '特困供养'
                        WHEN 'RuralDestituteCentralized' THEN '特困供养'
                        WHEN 'UrbanDestituteScattered' THEN '特困供养'
                        WHEN 'UrbanDestituteCentralized' THEN '特困供养'
                        WHEN 'RuralRigidExpenditure' THEN '刚性支出'
                        WHEN 'UrbanRigidExpenditure' THEN '刚性支出'
                        ELSE COALESCE(classification_result, '')
                    END AS category,
                    applicant_name, applicant_id_card, COALESCE(applicant_phone, '') AS phone,
                    COALESCE(address, '') AS address, COALESCE(community, '') AS community,
                    COALESCE(NULLIF(confirmed_family_size, 0), family_size, 0) AS guarantee_size,
                    COALESCE(household_monthly_guarantee_amount, 0) AS base_amount,
                    COALESCE(classified_subsidy_amount, 0) AS family_classified,
                    COALESCE(person_category_protection_total_amount, 0) AS person_classified,
                    COALESCE(caregiver_subsidy_amount, 0) AS care_cost,
                    COALESCE(total_guarantee_amount, 0) AS total_amount,
                    COALESCE(application_reason, '') AS remark
                  FROM nc_biz_applications
                 WHERE status = $2
                   AND (stop_date IS NULL OR stop_date >= $1::date)
                   AND deleted_at IS NULL";

        return await database.QueryAsync<PublicityFamilyRow>(
            sql, ct, monthEnd, ApplicationStatusCodes.APPROVED);
    }

    /// <summary>批量标记近亲属备案（户主身份证命中 nc_biz_near_relative_links.id_card）。</summary>
    private async Task MarkNearRelativeAsync(List<PublicityFamilyRow> families, CancellationToken ct)
    {
        if (families.Count == 0) return;

        var cards = families
            .Select(family => family.ApplicantIdCard?.Trim())
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (cards.Length == 0) return;

        var result = await database.QueryAsync<IdCardRow>(
            @"SELECT DISTINCT id_card FROM nc_biz_near_relative_links
              WHERE id_card IS NOT NULL AND id_card <> '' AND id_card = ANY($1::text[])",
            ct, new object[] { cards });
        if (result.IsFailure)
        {
            logger.Warn(
                $"每月公示-近亲属备案标记查询失败，该列按未备案处理: {result.ErrorCode} {result.Message}");
            return;
        }

        var hit = new HashSet<string>(
            (result.Value ?? [])
                .Select(row => row.IdCard?.Trim())
                .Where(card => !string.IsNullOrWhiteSpace(card))
                .Select(card => card!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var family in families)
        {
            if (!string.IsNullOrWhiteSpace(family.ApplicantIdCard)
                && hit.Contains(family.ApplicantIdCard.Trim()))
            {
                family.IsNearRelative = true;
            }
        }
    }

    // ───────────────────────────── 分组 / 模板 ─────────────────────────────

    /// <summary>按村分组并排序（村按户数升序 → 村内按类别顺序 → 户主姓名）。</summary>
    private static List<PublicityVillageDto> GroupByVillage(
        IEnumerable<PublicityFamilyRow> families,
        bool includeNearRelative)
    {
        return families
            .GroupBy(family => PublicityVillageResolver.Resolve(family.Address, family.Community))
            .Select(group => new PublicityVillageDto
            {
                Village = group.Key,
                FamilyCount = group.Count(),
                Families = group
                    .OrderBy(family => CategoryOrder.TryGetValue(family.Category, out var order) ? order : 99)
                    .ThenBy(family => family.ApplicantName, StringComparer.Ordinal)
                    .Select(family => new PublicityFamilyDto
                    {
                        IdCard = family.ApplicantIdCard ?? string.Empty,
                        HeadName = family.ApplicantName ?? string.Empty,
                        Category = family.Category ?? string.Empty,
                        FamilySize = Math.Max(0, family.GuaranteeSize),
                        BaseAmount = family.BaseAmount,
                        ClassifiedAmount = family.ClassifiedAmount,
                        TotalAmount = family.TotalAmount,
                        Remark = family.Remark ?? string.Empty,
                        NearRelative = includeNearRelative ? family.IsNearRelative : null,
                    })
                    .ToList(),
            })
            .OrderBy(group => group.FamilyCount)
            .ToList();
    }

    /// <summary>按名称精确解析公示模板（xlsx）。</summary>
    private async Task<Result<PublicityTemplateRow>> LoadTemplateAsync(CancellationToken ct)
    {
        var result = await database.QuerySingleAsync<PublicityTemplateRow?>(
            @"SELECT id, name, file_type
              FROM nc_biz_templates
              WHERE name = $1
              ORDER BY id
              LIMIT 1",
            ct, TemplateName);

        if (result.IsFailure)
        {
            return Result.Failure<PublicityTemplateRow>(
                result.ErrorCode!, result.Message!);
        }

        if (result.Value is not { Id: > 0 } template)
        {
            return Result.Failure<PublicityTemplateRow>(
                ErrorCodes.NOT_FOUND, $"公示模板不存在：{TemplateName}");
        }

        var fileType = (template.FileType ?? string.Empty).Trim().ToLowerInvariant();
        if (fileType is not ("xlsx" or "excel"))
        {
            return Result.Failure<PublicityTemplateRow>(
                ErrorCodes.NOT_FOUND,
                $"公示模板类型不支持：{TemplateName}（{template.FileType ?? "-"}）");
        }

        return Result.Success(template);
    }

    /// <summary>表头：所在县/所在镇/所在镇电话（当前用户组织；县空默认「林口县」）。</summary>
    private async Task<(string County, string Town, string Phone)> ResolveHeaderAsync(
        int? organizationId,
        CancellationToken ct)
    {
        if (organizationId is not > 0)
        {
            return ("-", "-", "-");
        }

        var result = await organizations.GetByIdAsync(organizationId.Value, ct);
        if (result.IsFailure || result.Value is null)
        {
            logger.Warn(
                $"每月公示-用户组织查询失败，表头按默认值输出: OrgId={organizationId} " +
                $"{result.ErrorCode} {result.Message}");
            return ("-", "-", "-");
        }

        var organization = result.Value;
        return (
            string.IsNullOrWhiteSpace(organization.CountyName) ? "林口县" : organization.CountyName,
            string.IsNullOrWhiteSpace(organization.TownName) ? "-" : organization.TownName,
            string.IsNullOrWhiteSpace(organization.Phone) ? "-" : organization.Phone);
    }

    /// <summary>
    /// 组装单页公示单字段（表头 + 22 行占位符）。末页不足 22 户时，尾部行字段显式置空，
    /// 避免模板占位符残留（旧版用删除行实现，Agent 批量任务不支持删行）。
    /// </summary>
    private static Dictionary<string, string> BuildPublicityFields(
        string county,
        string town,
        string publicityPeriod,
        string townPhone,
        IReadOnlyList<PublicityFamilyDto> slice,
        string village)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.PUBLICITY_COUNTY] = county,
            [FieldKeys.PUBLICITY_TOWN] = town,
            [FieldKeys.PUBLICITY_PERIOD] = publicityPeriod,
            [FieldKeys.PUBLICITY_TOWN_PHONE] = townPhone,
        };

        for (var index = 0; index < RowsPerPage; index++)
        {
            var rowNo = index + 1;
            var hasFamily = index < slice.Count;
            var family = hasFamily ? slice[index] : null;

            fields[FieldKeys.PUBLICITY_ROW_NAME + rowNo + "}"] =
                hasFamily ? family!.HeadName : string.Empty;
            fields[FieldKeys.PUBLICITY_ROW_VILLAGE + rowNo + "}"] =
                hasFamily ? village : string.Empty;
            fields[FieldKeys.PUBLICITY_ROW_TYPE + rowNo + "}"] =
                hasFamily ? family!.Category : string.Empty;
            fields[FieldKeys.PUBLICITY_ROW_SIZE + rowNo + "}"] =
                hasFamily
                    ? Math.Max(0, family!.FamilySize).ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            fields[FieldKeys.PUBLICITY_ROW_RELATIVE + rowNo + "}"] =
                hasFamily ? (family!.NearRelative == true ? "是" : "否") : string.Empty;
        }

        return fields;
    }

    /// <summary>文件名安全化（去除 Windows 非法字符与路径分隔符）。</summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "未分组";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name
            .ToCharArray()
            .Where(character => !invalid.Contains(character) && character != '/' && character != '\\')
            .ToArray();

        var sanitized = new string(chars).Trim();
        return string.IsNullOrEmpty(sanitized) ? "未分组" : sanitized;
    }

    /// <summary>5 个导入库统一口径的公示家庭行（仅服务端内部使用）。</summary>
    private sealed class PublicityFamilyRow
    {
        public string Category { get; set; } = string.Empty;
        public string ApplicantName { get; set; } = string.Empty;
        public string ApplicantIdCard { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Community { get; set; } = string.Empty;
        public int GuaranteeSize { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal FamilyClassified { get; set; }
        public decimal PersonClassified { get; set; }
        public decimal CareCost { get; set; }
        public decimal TotalAmount { get; set; }
        public string Remark { get; set; } = string.Empty;
        public bool IsNearRelative { get; set; }

        public decimal ClassifiedAmount => FamilyClassified + PersonClassified;
    }

    private sealed class PublicityTemplateRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? FileType { get; set; }
    }

    private sealed class IdCardRow
    {
        public string? IdCard { get; set; }
    }
}

// ───────────────────────────── DTO ─────────────────────────────

/// <summary>公示名单（families 预览 / groups 生成共用响应形状）。</summary>
public sealed class PublicityListDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalFamilies { get; set; }
    public List<PublicityVillageDto> Villages { get; set; } = [];
}

/// <summary>按村分组的公示名单。</summary>
public sealed class PublicityVillageDto
{
    public string Village { get; set; } = string.Empty;
    public int FamilyCount { get; set; }
    public List<PublicityFamilyDto> Families { get; set; } = [];
}

/// <summary>公示家庭行（不重算金额，只搬运库内已有值）。</summary>
public sealed class PublicityFamilyDto
{
    public string IdCard { get; set; } = string.Empty;
    public string HeadName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int FamilySize { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal ClassifiedAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Remark { get; set; } = string.Empty;

    /// <summary>仅生成口径（groups）返回；预览口径（families）为 null 并在 JSON 中省略。</summary>
    public bool? NearRelative { get; set; }
}

/// <summary>公示文档任务响应：按村汇总（同一 TemplateBatch 任务的 id/jobNo）。</summary>
public sealed class PublicityJobResultDto
{
    public List<PublicityJobSummaryDto> Jobs { get; set; } = [];
    public string OutputHint { get; set; } = string.Empty;
}

/// <summary>单村任务摘要。</summary>
public sealed class PublicityJobSummaryDto
{
    public long Id { get; set; }
    public string JobNo { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public int PageCount { get; set; }
}
