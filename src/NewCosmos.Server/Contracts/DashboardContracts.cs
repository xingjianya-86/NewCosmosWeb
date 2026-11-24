namespace NewCosmos.Server.Contracts;

#region 首页概览

/// <summary>
/// 首页概览汇总。任一数据块查询失败时返回 null，由前端显示「—」并提供重试，
/// 不用假 0 掩盖数据库异常（与旧版 MainViewModel 的失败降级口径一致，但更显式）。
/// </summary>
public sealed record DashboardSummaryResponse(
    DashboardOverviewDto? Dashboard,
    SocialAssistanceSummaryDto? SocialAssistance,
    ElderlySummaryDto? Elderly,
    AssetVerificationSummaryDto? AssetVerification,
    ReminderSummaryDto? Reminders);

/// <summary>首页四张统计卡（旧版 MainViewModel 口径）。</summary>
public sealed record DashboardOverviewDto(
    int MonthlyNewAdditions,
    int MonthlyExits,
    int NewAssetChecks,
    int MonthlyNewElderly);

/// <summary>低收入人口救助帮扶模块概览。</summary>
public sealed record SocialAssistanceSummaryDto(
    int ActiveCount,
    int MonthlyNewAdditions,
    int MonthlyExits,
    int? GracePeriodExpiring);

/// <summary>高龄津贴发放模块概览。</summary>
public sealed record ElderlySummaryDto(
    int ActiveCount,
    int MonthlyNew,
    int? PendingNew,
    int? PendingTransfer);

/// <summary>家庭经济状况核对模块概览。</summary>
public sealed record AssetVerificationSummaryDto(
    int PendingReportCount,
    int MonthlyCompleted,
    int MonthlyNew,
    int YearTotal);

/// <summary>
/// 首页提醒计数。单项查询失败时为 null（该提醒不显示），全部失败时整个 reminders 为 null。
/// 死亡申报联动域尚未迁入 Core，首页本期不接入。
/// </summary>
public sealed record ReminderSummaryDto(
    int? GracePeriodExpiring,
    int? ElderlyPendingNew,
    int? ElderlyPendingTransfer,
    int? CollegeStudentGraduating);

#endregion
