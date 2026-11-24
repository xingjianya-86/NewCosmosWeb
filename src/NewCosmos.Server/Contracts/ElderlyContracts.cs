namespace NewCosmos.Server.Contracts;

#region 高龄津贴发放（只读查询）

/// <summary>
/// 高龄模块概览。统计口径全部来自 Core；某一统计块失败时对应字段为 null，前端显示为「—」。
/// 命名避开首页 DashboardContracts 的 ElderlySummaryDto（四个统计块共用类型，字段不可空）。
/// </summary>
public sealed record ElderlyModuleSummaryDto(
    int? ActiveCount,
    int? MonthlyNew,
    int? PendingNew,
    int? PendingTransfer);

/// <summary>高龄登记列表项。身份证强制脱敏，完整证件只在详情返回。</summary>
public sealed record ElderlyApplicationListItemDto(
    long Id,
    string ApplicationNo,
    string Name,
    string IdCardMasked,
    string Gender,
    DateOnly? BirthDate,
    int? Age,
    string Category,
    string CategoryText,
    string Status,
    string StatusText,
    DateTime? ApplyDate,
    DateTime? ConfirmedAt,
    string IssueStartMonth,
    decimal IssueAmount);

public sealed record ElderlyApplicationListResponse(
    IReadOnlyList<ElderlyApplicationListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>高龄登记详情（只读）。完整身份证/银行卡仅限本接口，不写日志。</summary>
public sealed record ElderlyApplicationDetailDto(
    long Id,
    string ApplicationNo,
    string Name,
    string IdCard,
    string Gender,
    DateOnly? BirthDate,
    int? Age,
    string Phone,
    string HukouAddress,
    string HukouVillage,
    string HukouDetailAddress,
    string FamilyAddress,
    string DetailAddress,
    string Category,
    string CategoryText,
    bool IsCategoryManual,
    string IdentityFlag,
    string IdentitySource,
    decimal IssueAmount,
    string IssueStartMonth,
    string BankName,
    string BankAccount,
    string AgentName,
    string AgentRelation,
    string AgentReceiveName,
    string AgentReceiveRelation,
    string AgentReceiveBankName,
    string AgentReceiveBankAccount,
    string AgentReceiveReason,
    string PaybackStartMonth,
    string PaybackEndMonth,
    int PaybackMonths,
    decimal PaybackAmount,
    string PaybackReason,
    bool IsSpecialCase,
    string SpecialReason,
    string Status,
    string StatusText,
    DateTime? ApplyDate,
    DateTime? ConfirmedAt,
    string ConfirmedBy,
    DateTime? DueStopDate,
    DateTime? ActualStopDate,
    DateTime? StoppedAt,
    string StoppedBy,
    string StopReason,
    string StopReasonText,
    bool IsRecover,
    string RecoverStartMonth,
    string RecoverEndMonth,
    decimal RecoverAmount,
    string Remark,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

/// <summary>下月待办明细项（姓名/身份证脱敏）。</summary>
public sealed record ElderlyPendingItemDto(
    string IdCardMasked,
    string Name,
    int Age,
    string Type,
    string TypeText,
    string CurrentCategory,
    string CurrentCategoryText,
    string SuggestedAction,
    string StatusLabel,
    string MemberRole,
    string Gender,
    DateOnly? BirthDate,
    long ElderlyApplicationId,
    long ReviewId,
    string SourceApplicationNo,
    string ElderlyApplicationNo);

public sealed record ElderlyPendingResponse(
    int PendingNew,
    int PendingTransfer,
    IReadOnlyList<ElderlyPendingItemDto> Items);

/// <summary>高龄类别复核列表项。</summary>
public sealed record ElderlyReviewListItemDto(
    long Id,
    string ReviewNo,
    string Name,
    string IdCardMasked,
    string TriggerSource,
    string TriggerText,
    string TriggerReason,
    string OldCategory,
    string OldCategoryText,
    string NewCategory,
    string NewCategoryText,
    string CategoryChangeText,
    string ReviewResult,
    string ResultText,
    string Status,
    string StatusText,
    string EffectiveMonth,
    string IssueStartMonth,
    string ReviewedBy,
    DateTime? ReviewedAt,
    DateTime? CreatedAt);

public sealed record ElderlyReviewListResponse(
    IReadOnlyList<ElderlyReviewListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

#endregion
