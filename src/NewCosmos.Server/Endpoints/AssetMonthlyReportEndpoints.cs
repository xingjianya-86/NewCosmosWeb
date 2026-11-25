using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Database;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 核对月报表 / 周报导出（xlsx 文件流）。
/// <para>
/// 权限：<see cref="PermissionCodes.ASSET_REPORT"/>；数据范围在导出服务内按村社区叠加。
/// 周期优先级：allMonths（全部月份） &gt; weekStart/weekEnd（周报） &gt; year + month（月报 A 线周期）
/// &gt; year（整年 A 线周期）。
/// </para>
/// </summary>
public static class AssetMonthlyReportEndpoints
{
    public static IEndpointRouteBuilder MapAssetMonthlyReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/asset-verification/monthly/export", ExportAsync);
        return app;
    }

    private static async Task<IResult> ExportAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        CancellationToken ct,
        int? year,
        int? month,
        string? weekStart,
        string? weekEnd,
        string? status,
        bool? onlyCurrent,
        bool? allMonths,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_REPORT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        if (month is < 1 or > 12)
        {
            return InvalidRequest(context, "月份参数应为 1-12");
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (normalizedStatus is not null
            && normalizedStatus is not (AssetCheckStatusConstants.SUBMITTED
                or AssetCheckStatusConstants.VERIFIED
                or AssetCheckStatusConstants.INCLUDED
                or AssetCheckStatusConstants.REFUSED))
        {
            return InvalidRequest(context, "状态参数应为 0、1、2 或 3");
        }

        if (!TryReadDate(weekStart, out var weekStartDate)
            || !TryReadDate(weekEnd, out var weekEndDate))
        {
            return InvalidRequest(context, "周起始日 / 结束日格式应为 YYYY-MM-DD");
        }

        if ((weekStartDate is null) != (weekEndDate is null))
        {
            return InvalidRequest(context, "周起始日与结束日必须同时提供");
        }

        if (weekStartDate is { } start && weekEndDate is { } end && start > end)
        {
            return InvalidRequest(context, "周起始日不能晚于结束日");
        }

        var request = new AssetMonthlyReportExportRequest
        {
            Year = year,
            Month = month,
            WeekStart = weekStartDate,
            WeekEnd = weekEndDate,
            Status = normalizedStatus,
            OnlyCurrent = onlyCurrent == true,
            AllMonths = allMonths == true,
            Keyword = keyword,
        };

        var service = new AssetMonthlyReportExportService(database, dataScope);
        var result = await service.ExportAsync(userId, request, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.File(
            result.Value.Content,
            AssetMonthlyReportExportService.ContentType,
            result.Value.FileName);
    }

    private static bool TryReadDate(string? raw, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (DateTime.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
