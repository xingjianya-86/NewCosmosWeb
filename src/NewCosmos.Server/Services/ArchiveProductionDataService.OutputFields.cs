using System.Globalization;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services;

/// <summary>
/// 档案输出：按模板 docType 装配专属字段与目录行（封面 / 目录）。
/// <para>
/// 逐行移植旧版 <c>ArchiveOutputViewModel.BatchAndArchive.cs</c> 的
/// <c>BuildDirectoryFieldsAsync</c> / <c>BuildTempReliefDirectoryFieldsAsync</c> /
/// <c>BuildDirectoryRows</c> / <c>BuildTempReliefDirectoryRows</c> /
/// <c>CalculateBLineVolumeDateAsync</c> / <c>NormalizeDirectoryDate</c>，
/// 以及 <c>ArchiveProductionViewModel.cs</c> 的封面字段装配口径。
/// 本文件只做字段拼装与搬运，不重算分类/保障金/时间线口径。
/// </para>
/// </summary>
public sealed partial class ArchiveProductionDataService
{
    /// <summary>目录行装配所需的模板元数据（本次输出勾选的模板）。</summary>
    public sealed record ArchiveDirectoryTemplate(
        long Id,
        string Name,
        string DocType,
        int SortOrder,
        string? DirectoryDateField,
        int Copies);

    /// <summary>
    /// 按模板文档类型装配字段与行数据。
    /// <list type="bullet">
    /// <item>封面：补 COVER_CLASSIFICATION / ARCHIVE_NUMBER / COPYRIGHT_INFO，保留 baseFields。</item>
    /// <item>目录：补目录头部字段 + DIRECTORY_* 行数据（静态材料 + 勾选模板文书）。</item>
    /// <item>其他：原样返回 baseFields，TableRows 为空（调用方保留业务主行数据）。</item>
    /// </list>
    /// </summary>
    public async Task<(Dictionary<string, string> Fields, List<Dictionary<string, string>> TableRows)> BuildForTemplateAsync(
        string businessType,
        long businessId,
        string? classification,
        string? docType,
        Dictionary<string, string> baseFields,
        CancellationToken ct,
        IReadOnlyList<ArchiveDirectoryTemplate>? directoryTemplates = null)
    {
        var normalized = NormalizeBusinessType(businessType);

        if (TemplateDocTypes.IsCover(docType))
        {
            return await BuildCoverAsync(normalized, businessId, classification, baseFields, ct);
        }

        if (string.Equals(docType?.Trim(), TemplateDocTypes.Directory, StringComparison.Ordinal))
        {
            return await BuildDirectoryAsync(
                normalized, businessId, classification, baseFields, directoryTemplates, ct);
        }

        return (new Dictionary<string, string>(baseFields, StringComparer.Ordinal), []);
    }

    // ───────────────────────────── 封面 ─────────────────────────────

    private async Task<(Dictionary<string, string> Fields, List<Dictionary<string, string>> TableRows)> BuildCoverAsync(
        string businessType,
        long businessId,
        string? classification,
        Dictionary<string, string> baseFields,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, string>(baseFields, StringComparer.Ordinal);

        // 文书分类 = 复核前旧分类（停旧建新链 / 同档复核变更记录），取不到回落传入分类。
        var documentClassification = await ResolveDocumentClassificationAsync(
            businessType, businessId, classification, ct);
        fields[FieldKeys.COVER_CLASSIFICATION] =
            ClassificationConstants.ConvertToFullName(documentClassification);

        // 档案编号 = B 线结算日次月 1 日（与目录卷号同源）。
        fields[FieldKeys.ARCHIVE_NUMBER] =
            await CalculateBLineVolumeDateAsync(businessType, businessId, ct);

        fields[FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(
            FirstNonEmpty(
                GetField(fields, FieldKeys.APPLICANT_ID_CARD),
                GetField(fields, FieldKeys.TEMP_APPLICANT_ID_CARD)));

        return (fields, []);
    }

    // ───────────────────────────── 目录 ─────────────────────────────

    private async Task<(Dictionary<string, string> Fields, List<Dictionary<string, string>> TableRows)> BuildDirectoryAsync(
        string businessType,
        long businessId,
        string? classification,
        Dictionary<string, string> baseFields,
        IReadOnlyList<ArchiveDirectoryTemplate>? directoryTemplates,
        CancellationToken ct)
    {
        var isTempRelief = string.Equals(
            businessType, PrintBusinessTypes.TempRelief, StringComparison.Ordinal);
        var fields = new Dictionary<string, string>(baseFields, StringComparer.Ordinal);

        var applicantName = isTempRelief
            ? FirstNonEmpty(
                GetField(fields, FieldKeys.TEMP_APPLICANT_NAME),
                GetField(fields, FieldKeys.APPLICANT_NAME),
                "未知")
            : FirstNonEmpty(GetField(fields, FieldKeys.APPLICANT_NAME), "未知");

        var applicationDateRaw = isTempRelief
            ? FirstNonEmpty(
                GetField(fields, FieldKeys.TEMP_APPLY_DATE),
                GetField(fields, FieldKeys.APPLICATION_DATE))
            : GetField(fields, FieldKeys.APPLICATION_DATE);
        if (string.IsNullOrWhiteSpace(applicationDateRaw))
        {
            applicationDateRaw = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // 申请年份：申请日期前 4 位，缺省当前年。
        fields[FieldKeys.APPLICATION_YEAR] = applicationDateRaw.Length >= 4
            ? applicationDateRaw[..4]
            : DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);

        // 业务终结时间：当天。
        fields[FieldKeys.BUSINESS_END_DATE] =
            DateTime.Now.ToString("yyyy年MM月dd日", CultureInfo.InvariantCulture);

        // 案卷题名：姓名 + 社会救助档案 / 临时救助档案。
        fields[FieldKeys.ARCHIVE_TITLE] = isTempRelief
            ? $"{applicantName}临时救助档案"
            : $"{applicantName}社会救助档案";

        // 保管期限：留空。
        fields[FieldKeys.RETENTION_PERIOD] = string.Empty;

        // 社会救助类型：分类全称；临时救助固定“临时救助”。
        var assistanceType = isTempRelief
            ? "临时救助"
            : FirstNonEmpty(
                ClassificationConstants.ConvertToFullName(classification ?? string.Empty),
                GetField(fields, FieldKeys.CLASSIFICATION_RESULT));
        fields[FieldKeys.SOCIAL_ASSISTANCE_TYPE] = assistanceType;
        // 档案_目录（212）config 用 CLASSIFICATION_RESULT 映射「社会救助类型」，同值双写。
        fields[FieldKeys.CLASSIFICATION_RESULT] = assistanceType;

        // 档案卷号 = B 线结算日次月 1 日（模板配置兼容键 ARCHIVE_VOLUME 同值双写）。
        var timelineResult = await ResolveBLineTimelineAsync(businessType, businessId, ct);
        var volumeDate = new DateTime(
                timelineResult.CycleEndDate.Year, timelineResult.CycleEndDate.Month, 1)
            .AddMonths(1)
            .ToString("yyyy-M-d", CultureInfo.InvariantCulture);
        fields[FieldKeys.ARCHIVE_VOLUME_NO] = volumeDate;
        fields[FieldKeys.ARCHIVE_VOLUME] = volumeDate;

        // 组卷单位：baseFields 有则保留；临时救助回退填报单位。
        if (string.IsNullOrWhiteSpace(GetField(fields, FieldKeys.OPERATOR_UNIT)))
        {
            fields[FieldKeys.OPERATOR_UNIT] = GetField(fields, FieldKeys.REPORT_UNIT);
        }

        fields[FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(
            FirstNonEmpty(
                GetField(fields, FieldKeys.APPLICANT_ID_CARD),
                GetField(fields, FieldKeys.TEMP_APPLICANT_ID_CARD)));

        var rows = isTempRelief
            ? BuildTempReliefDirectoryRows(fields)
            : BuildFamilyDirectoryRows(fields, timelineResult.MeetingDate, directoryTemplates);

        return (fields, rows);
    }

    /// <summary>FamilyApplication 目录行：静态材料 9 条 + 会议记录/资产核查报告 + 勾选模板文书。</summary>
    private static List<Dictionary<string, string>> BuildFamilyDirectoryRows(
        Dictionary<string, string> fieldData,
        DateTime meetingDate,
        IReadOnlyList<ArchiveDirectoryTemplate>? templates)
    {
        var rows = new List<Dictionary<string, string>>();
        var applicantName = FirstNonEmpty(
            GetField(fieldData, FieldKeys.APPLICANT_NAME), "未知");
        var appDateRaw = GetField(fieldData, FieldKeys.APPLICATION_DATE);
        if (string.IsNullOrWhiteSpace(appDateRaw))
        {
            appDateRaw = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var appDate = NormalizeDirectoryDate(appDateRaw);
        var currentPage = 1;

        // 静态预填条目（无模板，仅在目录中列出），责任人 = 申请人姓名。
        var staticItems = new (string Title, int Copies)[]
        {
            ("户口簿（必要）", 1),
            ("身份证（必要）", 1),
            ("赡养、抚养、扶养义务人相关材料", 1),
            ("残疾人证或其他证明残疾人的材料", 1),
            ("住院病案或相关诊断材料", 1),
            ("结婚证或离婚证、离婚协议", 1),
            ("在校就读相关佐证材料", 1),
            ("申请前12个月内家庭刚性支出材料", 1),
            ("其他相关材料", 1)
        };
        foreach (var (title, copies) in staticItems)
        {
            AddDirectoryRow(rows, applicantName, title, appDate, ref currentPage, copies);
        }

        // 特殊条目（无模板，仅在目录中列出），责任人 = 民政办，日期 = B 线会议时间。
        var meetingDateText = NormalizeDirectoryDate(
            meetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var specialItems = new (string Title, int Copies)[]
        {
            ("会议记录", 1),
            ("资产核查报告", 1)
        };
        foreach (var (title, copies) in specialItems)
        {
            AddDirectoryRow(rows, "民政办", title, meetingDateText, ref currentPage, copies);
        }

        // 勾选的非封面/非目录模板，按 SortOrder + Name 排序。
        var sortedTemplates = (templates ?? [])
            .Where(template => !TemplateDocTypes.IsCover(template.DocType)
                && !string.Equals(template.DocType, TemplateDocTypes.Directory, StringComparison.Ordinal))
            .OrderBy(template => template.SortOrder)
            .ThenBy(template => template.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var template in sortedTemplates)
        {
            // 题名过滤：去除“档案_”/“通用_”前缀，去除“封面”“目录”；为空则跳过。
            var title = template.Name;
            if (title.StartsWith("档案_", StringComparison.Ordinal))
            {
                title = title["档案_".Length..];
            }
            else if (title.StartsWith("通用_", StringComparison.Ordinal))
            {
                title = title["通用_".Length..];
            }

            title = title.Replace("封面", string.Empty, StringComparison.Ordinal)
                .Replace("目录", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            // 目录日期：模板声明了目录日期字段且业务字段有值时取该值，否则回退申请日期。
            var directoryDate = appDate;
            if (!string.IsNullOrWhiteSpace(template.DirectoryDateField)
                && fieldData.TryGetValue(template.DirectoryDateField, out var templateDate)
                && !string.IsNullOrWhiteSpace(templateDate))
            {
                directoryDate = NormalizeDirectoryDate(templateDate);
            }

            AddDirectoryRow(
                rows, "民政办", title, directoryDate, ref currentPage, template.Copies);
        }

        return rows;
    }

    /// <summary>临时救助目录行：固定 7 条佐证材料，责任人 = 申请人，日期 = 申请日期。</summary>
    private static List<Dictionary<string, string>> BuildTempReliefDirectoryRows(
        Dictionary<string, string> fieldData)
    {
        var applicantName = FirstNonEmpty(
            GetField(fieldData, FieldKeys.TEMP_APPLICANT_NAME),
            GetField(fieldData, FieldKeys.APPLICANT_NAME),
            "未知");

        var appDateRaw = FirstNonEmpty(
            GetField(fieldData, FieldKeys.TEMP_APPLY_DATE),
            GetField(fieldData, FieldKeys.APPLICATION_DATE));
        if (string.IsNullOrWhiteSpace(appDateRaw))
        {
            appDateRaw = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var appDate = NormalizeDirectoryDate(appDateRaw);
        var rows = new List<Dictionary<string, string>>();
        var currentPage = 1;

        var staticItems = new[]
        {
            "身份证（必要）",
            "户口本（必要）",
            "受残疾人证或其他证明残疾人的材料",
            "住院病历或相关诊断材料",
            "在校就读相关佐证材料",
            "因灾造成家庭损失相关佐证材料",
            "公示照片"
        };
        foreach (var title in staticItems)
        {
            AddDirectoryRow(rows, applicantName, title, appDate, ref currentPage, 1);
        }

        return rows;
    }

    private static void AddDirectoryRow(
        List<Dictionary<string, string>> rows,
        string person,
        string title,
        string date,
        ref int currentPage,
        int copies)
    {
        var pageCount = copies > 0 ? copies : 1;
        var startPage = currentPage;
        var endPage = currentPage + pageCount - 1;
        rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.DIRECTORY_PERSON] = person,
            [FieldKeys.DIRECTORY_TITLE] = title,
            [FieldKeys.DIRECTORY_DATE] = date,
            [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
        });
        currentPage = endPage + 1;
    }

    // ───────────────────────────── 时间 / 分类锚点 ─────────────────────────────

    /// <summary>B 线窗口：FamilyApplication 按业务锚点（申请日期=调查日期新口径），其余域按当天。</summary>
    private async Task<TimelineResult> ResolveBLineTimelineAsync(
        string businessType,
        long businessId,
        CancellationToken ct)
    {
        var anchorDate = await ResolveDirectoryAnchorDateAsync(businessType, businessId, ct);
        return timeline.GetTimelineForApplicationDate(anchorDate, TimelineType.BusinessProcess);
    }

    private async Task<DateTime> ResolveDirectoryAnchorDateAsync(
        string businessType,
        long businessId,
        CancellationToken ct)
    {
        if (!string.Equals(businessType, PrintBusinessTypes.FamilyApplication, StringComparison.Ordinal)
            || businessId <= 0)
        {
            return DateTime.Today;
        }

        var appResult = await applications.GetByIdAsync(businessId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            logger.Warn("[ArchiveOutput] 目录/卷号锚点读取申请失败，回退当天: "
                + $"BusinessId={businessId}, ErrorCode={appResult.ErrorCode ?? "-"}");
            return DateTime.Today;
        }

        var app = appResult.Value;
        var applicationDate = ArchiveAnchorHelper.ResolveApplicationDate(app);

        var surveyResult = await householdSurveys.GetByApplicationIdAsync(businessId, ct);
        if (surveyResult.IsFailure)
        {
            logger.Warn("[ArchiveOutput] 目录/卷号锚点读取入户调查失败，按受理锚点计算: "
                + $"ApplicationId={businessId}, ErrorCode={surveyResult.ErrorCode ?? "-"}");
            return applicationDate;
        }

        if (surveyResult.Value?.SurveyDate is { } surveyDate && surveyDate != default)
        {
            var appCycle = timeline.GetTimelineForDate(applicationDate, TimelineType.BusinessProcess);
            var surveyCycle = timeline.GetTimelineForDate(surveyDate, TimelineType.BusinessProcess);
            return ArchiveAnchorHelper.ResolveBusinessAnchorDate(
                applicationDate,
                surveyDate,
                (appCycle.Year, appCycle.Month),
                (surveyCycle.Year, surveyCycle.Month));
        }

        return applicationDate;
    }

    /// <summary>B 线结算日次月 1 日，格式 yyyy-M-d（档案编号 / 档案卷号）。</summary>
    private async Task<string> CalculateBLineVolumeDateAsync(
        string businessType,
        long businessId,
        CancellationToken ct)
    {
        var timelineResult = await ResolveBLineTimelineAsync(businessType, businessId, ct);
        return new DateTime(
                timelineResult.CycleEndDate.Year, timelineResult.CycleEndDate.Month, 1)
            .AddMonths(1)
            .ToString("yyyy-M-d", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 文书基准分类（复核前旧分类）：停旧建新链原档分类 → 同档复核变更记录 old_classification
    /// → 档案自身分类 → 传入分类。
    /// </summary>
    private async Task<string> ResolveDocumentClassificationAsync(
        string businessType,
        long businessId,
        string? fallback,
        CancellationToken ct)
    {
        var fallbackValue = fallback?.Trim() ?? string.Empty;
        if (!string.Equals(businessType, PrintBusinessTypes.FamilyApplication, StringComparison.Ordinal)
            || businessId <= 0)
        {
            return fallbackValue;
        }

        var appResult = await applications.GetByIdAsync(businessId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            logger.Warn("[ArchiveOutput] 文书分类读取申请失败，回退传入分类: "
                + $"BusinessId={businessId}, ErrorCode={appResult.ErrorCode ?? "-"}");
            return fallbackValue;
        }

        var app = appResult.Value;
        var own = app.ClassificationResult ?? string.Empty;

        // ① 停旧建新链：原档分类即复核/变更前分类。
        if (app.OriginalApplicationId > 0)
        {
            var oldResult = await applications.GetByIdAsync(app.OriginalApplicationId, ct);
            var oldClassification = oldResult.Value?.ClassificationResult;
            if (oldResult.IsSuccess && !string.IsNullOrWhiteSpace(oldClassification))
            {
                return oldClassification!;
            }

            logger.Warn("[ArchiveOutput] 文书分类-原档分类缺失，回退档案自身分类: "
                + $"ApplicationId={businessId}, OriginalApplicationId={app.OriginalApplicationId}, "
                + $"ErrorCode={oldResult.ErrorCode ?? "-"}");
        }

        // ② 同档复核：最新一条复核/变更记录的 old_classification。
        var reviewResult = await changes.GetLatestReviewChangeRecordAsync(app.Id, ct);
        if (reviewResult.IsSuccess
            && reviewResult.Value is { } review
            && !string.IsNullOrWhiteSpace(review.OldClassification)
            && !string.Equals(review.OldClassification, own, StringComparison.Ordinal))
        {
            return review.OldClassification!;
        }

        if (reviewResult.IsFailure)
        {
            logger.Warn("[ArchiveOutput] 文书分类-复核记录查询失败，回退档案自身分类: "
                + $"ApplicationId={businessId}, ErrorCode={reviewResult.ErrorCode ?? "-"}");
        }

        return string.IsNullOrWhiteSpace(own) ? fallbackValue : own;
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    /// <summary>
    /// 统一目录日期为 yyyy-MM-dd；兼容 yyyy-MM-dd / yyyy/M/d / yyyy年M月d日 / 含时间等格式，
    /// 无法解析时保留原值（与旧版 NormalizeDirectoryDate 一致）。
    /// </summary>
    private static string NormalizeDirectoryDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var formats = new[]
        {
            "yyyy-MM-dd", "yyyy/M/d", "yyyy/M/dd", "yyyy/MM/d", "yyyy/MM/dd",
            "yyyy年M月d日", "yyyy年MM月d日", "yyyy年M月dd日", "yyyy年MM月dd日",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"
        };
        if (DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value;
    }

    /// <summary>读取模板 config_json 的目录日期字段与份数（解析失败按 1 份）。</summary>
    private static (string? DirectoryDateField, int Copies) ReadDirectoryTemplateConfig(
        string? configJson,
        string? classification)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return (null, 1);
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);
            var root = document.RootElement;

            string? dateField = null;
            if (root.TryGetProperty("directoryDateField", out var dateElement)
                && dateElement.ValueKind == JsonValueKind.String)
            {
                dateField = dateElement.GetString();
            }

            var copies = 1;
            if (!string.IsNullOrWhiteSpace(classification)
                && root.TryGetProperty("copiesByClassification", out var byClassification)
                && byClassification.ValueKind == JsonValueKind.Object
                && byClassification.TryGetProperty(classification, out var exactCopies)
                && TryGetInt(exactCopies, out var exactValue)
                && exactValue > 0)
            {
                copies = exactValue;
            }
            else if (root.TryGetProperty("copies", out var copiesElement)
                && TryGetInt(copiesElement, out var configuredCopies)
                && configuredCopies > 0)
            {
                copies = configuredCopies;
            }

            return (string.IsNullOrWhiteSpace(dateField) ? null : dateField, copies);
        }
        catch (JsonException)
        {
            return (null, 1);
        }
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        return element.ValueKind == JsonValueKind.String
            && int.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
    }
}
