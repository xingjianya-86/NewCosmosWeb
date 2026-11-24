using NewCosmos.Constants;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Server.Services;

/// <summary>
/// 低收入人口救助帮扶模块首页统计：复用 Core IStatisticsService 与 IGracePeriodService。
/// 两项独立取数、独立失败降级（失败项为 null，前端显示「—」）。
/// </summary>
public static class SocialAssistanceQueryService
{
    public static async Task<SocialAssistanceModuleSummaryResponse> GetSummaryAsync(
        IStatisticsService statistics,
        IGracePeriodService gracePeriods,
        ILoggerService logger,
        CancellationToken ct = default)
    {
        SocialAssistanceModuleStats? stats = null;
        try
        {
            var result = await statistics.GetSocialAssistanceStatsAsync(ct);
            if (result.HasValue)
            {
                stats = result.Value;
            }
            else
            {
                logger.Warn($"低收入模块统计查询失败: {result.ErrorCode} {result.Message}");
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"低收入模块统计查询异常: {ex.Message}");
        }

        int? graceExpiring = null;
        try
        {
            // 模块首页胶囊沿用旧版渐退期管理页口径：进行中 + 已到期全量（不是首页 30 天预警窗口）。
            var result = await gracePeriods.GetExpiringCountAsync(
                GracePeriodConstants.EXPIRING_WARNING_DAYS, ct);
            if (result.IsSuccess)
            {
                graceExpiring = result.Value;
            }
            else
            {
                logger.Warn($"渐退期户数查询失败: {result.ErrorCode} {result.Message}");
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"渐退期户数查询异常: {ex.Message}");
        }

        return new SocialAssistanceModuleSummaryResponse(
            stats?.ActiveCount,
            stats?.MonthlyNewAdditions,
            stats?.MonthlyExits,
            graceExpiring);
    }
}
