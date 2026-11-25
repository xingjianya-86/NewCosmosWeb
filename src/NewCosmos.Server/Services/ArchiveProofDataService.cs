using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services;

/// <summary>
/// 档案查询页「低保证明」打印字段装配。
/// <para>
/// 严格按旧版 <c>ArchiveQueryViewModel</c> 的证明字段口径：
/// 当前库取 <c>nc_biz_applications</c> + 家庭成员 + 渐退期；导入库取导入家庭详情；
/// 通用字段（操作员/单位/接收单位/日期）双写中文占位符与 <see cref="FieldKeys"/>。
/// </para>
/// <para>
/// 本服务只装配字段，不重算分类/保障金/渐退期等业务口径；取不到的字段留空，不臆造。
/// </para>
/// </summary>
public sealed class ArchiveProofDataService(
    IApplicationService applications,
    IImportedArchiveService importedArchives,
    IFamilyMemberService familyMembers,
    IGracePeriodService gracePeriods,
    IBusinessTimelineService timeline,
    IDictCacheService dictCache,
    IOrganizationService organizations,
    ILoggerService logger)
{
    /// <summary>当前库档案证明字段。</summary>
    public async Task<Result<ArchiveProofData>> BuildCurrentAsync(
        long applicationId,
        string? targetUnitName,
        UserDto user,
        CancellationToken ct = default)
    {
        var appResult = await applications.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<ArchiveProofData>(
                appResult.ErrorCode!, appResult.Message!);
        }

        var app = appResult.Value;
        if (app is null)
        {
            return Result.Failure<ArchiveProofData>(
                ErrorCodes.APPLICATION_NOT_FOUND, "当前库档案不存在");
        }

        var classification = ResolveClassification(app.ClassificationResult);
        var enjoyDate = await ResolveCurrentEnjoyDateAsync(app);
        await dictCache.EnsureFreshAsync(ct);
        var memberText = await BuildFamilyMembersTextAsync(app.Id, ct);
        var gracePeriodText = await BuildGracePeriodInfoAsync(app.Id, ct);
        var (unitName, unitPhone) = await ResolveOperatorUnitAsync(user, ct);

        var fullAddress = AddressResolver.BuildAddressFromText(
            app.District, app.Town, app.Community, app.Address);

        var data = new ArchiveProofData
        {
            ApplicantName = app.ApplicantName ?? string.Empty,
            ApplicantIdCard = app.ApplicantIdCard ?? string.Empty,
            Classification = classification
        };

        var fields = data.Fields;
        fields["{户主姓名}"] = data.ApplicantName;
        fields[FieldKeys.APPLICANT_NAME] = data.ApplicantName;
        fields["{身份证号}"] = data.ApplicantIdCard;
        fields[FieldKeys.APPLICANT_ID_CARD] = data.ApplicantIdCard;
        fields["{分类结果}"] = classification;
        fields[FieldKeys.PROOF_CLASSIFICATION_RESULT] = classification;
        fields["{保障类别}"] = classification;
        fields["{保障金额}"] = FormatAmount(app.TotalGuaranteeAmount);
        fields[FieldKeys.PROOF_GUARANTEE_AMOUNT] = FormatAmount(app.TotalGuaranteeAmount);
        fields["{起始日期}"] = FormatChineseDate(enjoyDate);
        fields[FieldKeys.PROOF_ENJOY_START_DATE] = FormatIsoDate(enjoyDate);
        fields["{入保时间}"] = FormatChineseDate(enjoyDate);
        fields["{家庭地址}"] = fullAddress;
        fields[FieldKeys.FAMILY_ADDRESS] = fullAddress;
        fields["{家庭住址}"] = fullAddress;
        fields["{家庭人数}"] = app.FamilySize.ToString(CultureInfo.InvariantCulture);
        fields[FieldKeys.FAMILY_SIZE] = app.FamilySize.ToString(CultureInfo.InvariantCulture);
        fields["{家庭成员}"] = memberText;
        fields["{渐退期信息}"] = gracePeriodText;
        var reason = ResolveApplicationReason(app.ApplicationReason);
        fields["{申请原因}"] = reason;
        fields[FieldKeys.APPLICATION_REASON] = reason;

        AppendCommonFields(data, user, unitName, unitPhone, targetUnitName);
        return Result.Success(data);
    }

    /// <summary>导入库家庭证明字段。</summary>
    public async Task<Result<ArchiveProofData>> BuildImportedAsync(
        ArchiveSearchResultItem item,
        string? targetUnitName,
        UserDto user,
        CancellationToken ct = default)
    {
        var detailResult = await importedArchives.GetImportedFamilyDetailAsync(item, ct);
        if (detailResult.IsFailure)
        {
            return Result.Failure<ArchiveProofData>(
                detailResult.ErrorCode!, detailResult.Message!);
        }

        var detail = detailResult.Value;
        if (detail is null)
        {
            return Result.Failure<ArchiveProofData>(
                ErrorCodes.RECORD_NOT_FOUND, "导入库家庭记录不存在或已被清理");
        }

        var classification = string.IsNullOrEmpty(detail.DerivedClassificationCode)
            ? detail.SourceDisplay
            : ResolveClassification(detail.DerivedClassificationCode);

        var amount = detail.MonthlyGuaranteeAmount > 0
            ? detail.MonthlyGuaranteeAmount
            : detail.TotalAmount;

        var enjoyDate = ParseFirstReceiveMonth(detail.FirstReceiveMonth);
        await dictCache.EnsureFreshAsync(ct);
        var memberText = BuildImportedMembersText(detail.Members);
        var (unitName, unitPhone) = await ResolveOperatorUnitAsync(user, ct);
        var fullAddress = AddressResolver.BuildAddressFromText(
            detail.District, detail.Town, detail.Community, detail.Address);

        var data = new ArchiveProofData
        {
            ApplicantName = detail.ApplicantName ?? string.Empty,
            ApplicantIdCard = detail.ApplicantIdCard ?? string.Empty,
            Classification = classification
        };

        var fields = data.Fields;
        fields["{户主姓名}"] = data.ApplicantName;
        fields[FieldKeys.APPLICANT_NAME] = data.ApplicantName;
        fields["{身份证号}"] = data.ApplicantIdCard;
        fields[FieldKeys.APPLICANT_ID_CARD] = data.ApplicantIdCard;
        fields["{分类结果}"] = classification;
        fields[FieldKeys.PROOF_CLASSIFICATION_RESULT] = classification;
        fields["{保障类别}"] = classification;
        fields["{保障金额}"] = FormatAmount(amount);
        fields[FieldKeys.PROOF_GUARANTEE_AMOUNT] = FormatAmount(amount);
        fields["{起始日期}"] = FormatChineseDate(enjoyDate);
        fields[FieldKeys.PROOF_ENJOY_START_DATE] = FormatIsoDate(enjoyDate);
        fields["{入保时间}"] = FormatChineseDate(enjoyDate);
        fields["{家庭地址}"] = fullAddress;
        fields[FieldKeys.FAMILY_ADDRESS] = fullAddress;
        fields["{家庭住址}"] = fullAddress;
        fields["{家庭人数}"] = detail.FamilySize.ToString(CultureInfo.InvariantCulture);
        fields[FieldKeys.FAMILY_SIZE] = detail.FamilySize.ToString(CultureInfo.InvariantCulture);
        fields["{家庭成员}"] = memberText;
        fields["{渐退期信息}"] = string.Empty;
        fields["{申请原因}"] = detail.ApplyReason ?? string.Empty;
        fields[FieldKeys.APPLICATION_REASON] = detail.ApplyReason ?? string.Empty;

        AppendCommonFields(data, user, unitName, unitPhone, targetUnitName);
        return Result.Success(data);
    }

    // ───────────────────────────── 字段口径 ─────────────────────────────

    /// <summary>
    /// 当前库正式享受时间 = data_completed_at 所在 B 线周期结算日次月 1 日；
    /// 数据缺失时回退 first_approved_at（旧版仅当前库口径，导入库无渐退期）。
    /// </summary>
    private Task<DateTime?> ResolveCurrentEnjoyDateAsync(Application app)
    {
        if (app.DataCompletedAt is { } completedAt)
        {
            try
            {
                var line = timeline.GetTimelineForApplicationDate(
                    completedAt, TimelineType.BusinessProcess);
                return Task.FromResult<DateTime?>(
                    new DateTime(line.CycleEndDate.Year, line.CycleEndDate.Month, 1).AddMonths(1));
            }
            catch (Exception ex)
            {
                logger.Warn(
                    $"证明文书：B 线周期计算失败，回退 first_approved_at。ApplicationId={app.Id}: {ex.Message}");
            }
        }

        return Task.FromResult(app.FirstApprovedAt);
    }

    /// <summary>
    /// 当前库家庭成员文本：仅户主 + 共同生活成员，排除赡养/抚养/扶养与已死亡成员；
    /// 格式「姓名（身份证：X，家庭关系：Y）」，多条以「；」连接。
    /// </summary>
    private async Task<string> BuildFamilyMembersTextAsync(long applicationId, CancellationToken ct)
    {
        var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
        if (membersResult.IsFailure)
        {
            logger.Warn(
                $"证明文书：家庭成员查询失败，成员栏留空。ApplicationId={applicationId}: " +
                $"{membersResult.ErrorCode} {membersResult.Message}");
            return string.Empty;
        }

        var members = membersResult.Value;
        if (members is not { Count: > 0 })
        {
            return string.Empty;
        }

        var deathResult = await familyMembers.GetDeadIdCardsByApplicationIdAsync(applicationId, ct);
        if (deathResult.IsFailure)
        {
            logger.Warn(
                $"证明文书：死亡成员过滤查询失败，可能混入已死亡成员。ApplicationId={applicationId}: " +
                $"{deathResult.ErrorCode} {deathResult.Message}");
        }

        var deathIdCards = deathResult.HasValue && deathResult.Value is not null
            ? deathResult.Value.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var eligible = members
            .Where(member => !string.Equals(
                member.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase))
            .Where(member => !deathIdCards.Contains(member.IdCard ?? string.Empty))
            .OrderByDescending(member => member.IsHouseholdHead)
            .ThenBy(member => member.Id)
            .ToList();

        if (eligible.Count == 0)
        {
            return string.Empty;
        }

        var parts = eligible.Select(member =>
        {
            var relation = ResolveRelationship(member.RelationshipToHead);
            var idCard = string.IsNullOrWhiteSpace(member.IdCard) ? "-" : member.IdCard;
            return $"{member.Name}（身份证：{idCard}，家庭关系：{relation}）";
        });

        return string.Join("；", parts);
    }

    /// <summary>导入库家庭成员文本（导入成员表即家庭成员，无赡养人/死亡标记）。</summary>
    private string BuildImportedMembersText(IReadOnlyList<ImportedMemberDetail>? members)
    {
        if (members is not { Count: > 0 })
        {
            return string.Empty;
        }

        var parts = members.Select(member =>
        {
            var relation = ResolveRelationship(member.Relationship);
            var idCard = string.IsNullOrWhiteSpace(member.IdCard) ? "-" : member.IdCard;
            return $"{member.Name}（身份证：{idCard}，家庭关系：{relation}）";
        });

        return string.Join("；", parts);
    }

    /// <summary>渐退期文本（仅当前库；无在效记录或日期缺失返回空串）。</summary>
    private async Task<string> BuildGracePeriodInfoAsync(long applicationId, CancellationToken ct)
    {
        var result = await gracePeriods.GetActiveAsync(applicationId, ct);
        if (result.IsFailure)
        {
            logger.Warn(
                $"证明文书：渐退期查询失败，渐退说明留空。ApplicationId={applicationId}: " +
                $"{result.ErrorCode} {result.Message}");
            return string.Empty;
        }

        var record = result.Value;
        if (record?.StartDate is null || record.EndDate is null)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder("该家庭处于渐退期（");
        builder.Append(record.StartDate.Value.ToString("yyyy年M月d日", CultureInfo.InvariantCulture));
        builder.Append('至');
        builder.Append(record.EndDate.Value.ToString("yyyy年M月d日", CultureInfo.InvariantCulture));
        builder.Append('）');

        if (!string.IsNullOrWhiteSpace(record.OriginalClassification))
        {
            builder.Append("，原保障类别：")
                .Append(ResolveClassification(record.OriginalClassification));
        }

        if (record.OriginalGuaranteeAmount is > 0)
        {
            builder.Append("，原保障金额：")
                .Append(record.OriginalGuaranteeAmount.Value.ToString("F2", CultureInfo.InvariantCulture))
                .Append("元/月");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 操作员姓名与当前用户所属单位名称/电话。
    /// 单位查不到时回退会话里的单位名，再回退用户名；不抛异常、不阻断打印。
    /// </summary>
    private async Task<(string Name, string Phone)> ResolveOperatorUnitAsync(
        UserDto user,
        CancellationToken ct)
    {
        var fallbackName = FirstNonEmpty(user.OrganizationName, user.Username) ?? string.Empty;
        if (user.OrganizationId is not > 0)
        {
            return (fallbackName, string.Empty);
        }

        var orgResult = await organizations.GetByIdAsync(user.OrganizationId.Value, ct);
        if (orgResult.IsFailure || orgResult.Value is null)
        {
            logger.Warn(
                $"证明文书：落款单位查询失败，回退会话单位名。OrganizationId={user.OrganizationId}: " +
                $"{orgResult.ErrorCode} {orgResult.Message}");
            return (fallbackName, string.Empty);
        }

        var org = orgResult.Value;
        return (FirstNonEmpty(org.Name, fallbackName) ?? string.Empty, org.Phone ?? string.Empty);
    }

    private void AppendCommonFields(
        ArchiveProofData data,
        UserDto user,
        string unitName,
        string unitPhone,
        string? targetUnitName)
    {
        var operatorName = FirstNonEmpty(user.FullName, user.Username) ?? string.Empty;
        var receiveUnit = targetUnitName?.Trim() ?? string.Empty;
        var today = DateTime.Now;

        data.Fields["{经办人}"] = operatorName;
        data.Fields[FieldKeys.OPERATOR_NAME] = operatorName;
        data.Fields["{当前用户单位名称}"] = unitName;
        data.Fields[FieldKeys.OPERATOR_UNIT] = unitName;
        data.Fields["{当前用户单位电话}"] = unitPhone;
        data.Fields[FieldKeys.OPERATOR_UNIT_PHONE] = unitPhone;
        data.Fields["{接收单位}"] = receiveUnit;
        data.Fields[FieldKeys.RECEIVE_UNIT_NAME] = receiveUnit;
        data.Fields["{日期}"] = today.ToString("yyyy年MM月dd日", CultureInfo.InvariantCulture);
        data.Fields["{开具日期}"] = today.ToString("yyyy年MM月dd日", CultureInfo.InvariantCulture);
        data.Fields[FieldKeys.PROOF_ISSUE_DATE] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static string ResolveClassification(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var fullName = ClassificationConstants.ConvertToFullName(code.Trim());
        return string.IsNullOrWhiteSpace(fullName) ? code.Trim() : fullName;
    }

    private string ResolveRelationship(string? relationship)
    {
        if (string.IsNullOrWhiteSpace(relationship))
        {
            return "-";
        }

        var display = dictCache.GetValue(
            DictionaryTypeCodes.FamilyRelationships, relationship.Trim());
        return string.IsNullOrWhiteSpace(display) ? relationship.Trim() : display;
    }

    private string ResolveApplicationReason(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return PickerConstants.ApplicationReason.OtherDisplay;
        }

        var display = dictCache.GetValue(
            DictionaryTypeCodes.ApplicationReasons, code.Trim());
        return string.IsNullOrWhiteSpace(display) ? code.Trim() : display;
    }

    /// <summary>导入库首次享受月份（"yyyy-MM"/"yyyy-M"）→ 当月 1 日；无法解析返回 null。</summary>
    private static DateTime? ParseFirstReceiveMonth(string? firstReceiveMonth)
    {
        if (string.IsNullOrWhiteSpace(firstReceiveMonth))
        {
            return null;
        }

        var value = firstReceiveMonth.Trim();
        if (DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month)
            || DateTime.TryParseExact(value, "yyyy-M", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out month))
        {
            return new DateTime(month.Year, month.Month, 1);
        }

        return null;
    }

    private static string FormatAmount(decimal amount)
        => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatChineseDate(DateTime? value)
        => value?.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatIsoDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>证明字段快照（字段键 = 中文占位符 + FieldKeys）。</summary>
public sealed class ArchiveProofData
{
    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);

    public string ApplicantName { get; set; } = string.Empty;

    public string ApplicantIdCard { get; set; } = string.Empty;

    public string Classification { get; set; } = string.Empty;
}
