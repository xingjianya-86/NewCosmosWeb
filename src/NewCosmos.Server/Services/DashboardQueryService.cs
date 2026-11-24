using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Server.Services;

/// <summary>
/// 首页概览聚合：只编排 Core 已有统计服务，不重抄统计口径。
/// 每个数据块独立 try/catch + Result 判断，失败返回 null 并记 Warn，绝不整页 500。
/// 采用静态聚合器而非 DI 服务，避免改动共享的 Program.cs 注册。
/// </summary>
public static class DashboardQueryService
{
    public static async Task<DashboardSummaryResponse> GetSummaryAsync(
        IStatisticsService statistics,
        IGracePeriodService gracePeriods,
        IElderlyApplicationService elderlyApplications,
        ICollegeStudentService collegeStudents,
        ILoggerService logger,
        CancellationToken ct = default)
    {
        try
        {
            // 各块互不依赖，并发取数；每个 Load 内部已捕获自身异常。
            var dashboardTask = LoadBlockAsync(
                "首页数据概览", () => statistics.GetDashboardStatisticsAsync(ct), logger);
            var socialTask = LoadBlockAsync(
                "低收入模块统计", () => statistics.GetSocialAssistanceStatsAsync(ct), logger);
            var elderlyStatsTask = LoadBlockAsync(
                "高龄模块统计", () => statistics.GetElderlyStatsAsync(ct), logger);
            var assetTask = LoadBlockAsync(
                "经济核对模块统计", () => statistics.GetAssetVerificationStatsAsync(ct), logger);
            var graceTask = LoadCountAsync(
                "渐退期临期",
                () => gracePeriods.GetWarningCountAsync(GracePeriodConstants.EXPIRING_WARNING_DAYS, ct),
                logger);
            var pendingTask = LoadBlockAsync(
                "高龄下月待办",
                () => elderlyApplications.GetPendingElderlyCountsAsync(ct),
                logger);
            var collegeTask = LoadCountAsync(
                "大学生毕业",
                async () =>
                {
                    var result = await collegeStudents.GetGraduatingStudentsAsync(DateTime.Today.Year, ct);
                    return result.HasValue
                        ? Result<int>.Success(result.Value.Count)
                        : Result<int>.Failure(result.ErrorCode, result.Message);
                },
                logger);

            await Task.WhenAll(
                dashboardTask, socialTask, elderlyStatsTask, assetTask,
                graceTask, pendingTask, collegeTask);

            var dashboardStats = await dashboardTask;
            var socialStats = await socialTask;
            var elderlyStats = await elderlyStatsTask;
            var assetStats = await assetTask;
            var graceExpiring = await graceTask;
            var pendingCounts = await pendingTask;
            var collegeGraduating = await collegeTask;

            var dashboard = dashboardStats is null
                ? null
                : new DashboardOverviewDto(
                    dashboardStats.MonthlyNewAdditions,
                    dashboardStats.MonthlyExits,
                    dashboardStats.NewAssetChecks,
                    dashboardStats.MonthlyNewElderly);

            var socialAssistance = socialStats is null
                ? null
                : new SocialAssistanceSummaryDto(
                    socialStats.ActiveCount,
                    socialStats.MonthlyNewAdditions,
                    socialStats.MonthlyExits,
                    graceExpiring);

            var elderly = elderlyStats is null
                ? null
                : new ElderlySummaryDto(
                    elderlyStats.ActiveCount,
                    elderlyStats.MonthlyNew,
                    pendingCounts?.PendingNew,
                    pendingCounts?.PendingTransfer);

            var assetVerification = assetStats is null
                ? null
                : new AssetVerificationSummaryDto(
                    assetStats.PendingReportCount,
                    assetStats.MonthlyCompleted,
                    assetStats.MonthlyNew,
                    assetStats.YearTotal);

            ReminderSummaryDto? reminders = null;
            if (graceExpiring.HasValue || pendingCounts is not null || collegeGraduating.HasValue)
            {
                reminders = new ReminderSummaryDto(
                    graceExpiring,
                    pendingCounts?.PendingNew,
                    pendingCounts?.PendingTransfer,
                    collegeGraduating);
            }

            return new DashboardSummaryResponse(
                dashboard, socialAssistance, elderly, assetVerification, reminders);
        }
        catch (Exception ex)
        {
            // 兜底：聚合层自身异常也不让首页整页 500。
            logger.Error($"首页概览聚合异常: {ex.Message}");
            return new DashboardSummaryResponse(null, null, null, null, null);
        }
    }

    private static async Task<T?> LoadBlockAsync<T>(
        string blockName,
        Func<Task<Result<T>>> loader,
        ILoggerService logger)
        where T : class
    {
        try
        {
            var result = await loader();
            if (result.HasValue)
            {
                return result.Value;
            }

            logger.Warn($"{blockName}查询失败: {result.ErrorCode} {result.Message}");
            return null;
        }
        catch (Exception ex)
        {
            logger.Warn($"{blockName}查询异常: {ex.Message}");
            return null;
        }
    }

    private static async Task<int?> LoadCountAsync(
        string blockName,
        Func<Task<Result<int>>> loader,
        ILoggerService logger)
    {
        try
        {
            var result = await loader();
            if (result.IsSuccess)
            {
                return result.Value;
            }

            logger.Warn($"{blockName}查询失败: {result.ErrorCode} {result.Message}");
            return null;
        }
        catch (Exception ex)
        {
            logger.Warn($"{blockName}查询异常: {ex.Message}");
            return null;
        }
    }
}
