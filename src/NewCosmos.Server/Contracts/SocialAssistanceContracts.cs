namespace NewCosmos.Server.Contracts;

#region 低收入人口救助帮扶

/// <summary>
/// 低收入模块首页统计胶囊。单项失败返回 null（前端显示「—」），不伪造 0。
/// </summary>
public sealed record SocialAssistanceModuleSummaryResponse(
    int? ActiveCount,
    int? MonthlyNewAdditions,
    int? MonthlyExits,
    int? GracePeriodExpiring);

/// <summary>渐退期列表（进行中 + 已到期，只读）。</summary>
public sealed record GracePeriodListResponse(
    IReadOnlyList<GracePeriodListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>渐退期列表项：身份证统一脱敏，跳转申请详情用 applicationId。</summary>
public sealed record GracePeriodListItemDto(
    long ApplicationId,
    string ApplicationNo,
    string ApplicantName,
    string ApplicantIdCardMasked,
    string Town,
    string Village,
    DateTime? StartDate,
    DateTime? EndDate,
    int DaysLeft,
    string Status,
    string StatusText,
    int? GracePeriodMonths,
    string DurationText,
    string OriginalClassification);

/// <summary>渐退期选中户的家庭成员明细（户主+共同生活成员，已排除赡养与死亡成员）。</summary>
public sealed record GracePeriodMembersResponse(
    long ApplicationId,
    IReadOnlyList<GracePeriodMemberDto> Items);

/// <summary>家庭成员行：关系同时给出代码与字典显示文本；身份证与旧版一致展示完整值。</summary>
public sealed record GracePeriodMemberDto(
    long Id,
    string Name,
    string Relationship,
    string RelationshipText,
    int? Age,
    string IdCard,
    decimal AnnualIncome);

#endregion
