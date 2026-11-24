using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 首页概览接口：登录即可访问，不校验具体权限码；
/// 各数据块失败由服务层降级为 null，前端显示「—」而不是假 0。
/// </summary>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/summary", GetSummaryAsync);
        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        IStatisticsService statistics,
        IGracePeriodService gracePeriods,
        IElderlyApplicationService elderlyApplications,
        ICollegeStudentService collegeStudents,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (context.GetUserId() is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "请先登录");
        }

        var summary = await DashboardQueryService.GetSummaryAsync(
            statistics, gracePeriods, elderlyApplications, collegeStudents, logger, ct);
        return Results.Json(summary);
    }
}
