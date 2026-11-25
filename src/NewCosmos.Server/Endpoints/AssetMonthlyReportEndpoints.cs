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
        app.MapPost("/api/asset-verification/monthly/output", CreateOutputAsync);
        return app;
    }

    /// <summary>
    /// 预览或打印核对月报/周报（模板任务交给 PC Agent）。
    /// 目标固定为当前账号默认绑定打印机；导出源文件仍走 GET /monthly/export。
    /// </summary>
    private static async Task<IResult> CreateOutputAsync(
        MonthlyReportOutputRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ArchivePrintQueueService queue,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_ASSETVERIFICATIONMONTHLYREPORT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var mode = (request.Mode ?? "preview").Trim().ToLowerInvariant();
        if (mode is not ("preview" or "print"))
        {
            return InvalidRequest(context, "mode 只能是 preview 或 print");
        }

        if (request.Year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        if (request.Month is < 1 or > 12)
        {
            return InvalidRequest(context, "月份参数应为 1-12");
        }

        if ((request.WeekStart is null) != (request.WeekEnd is null))
        {
            return InvalidRequest(context, "周起始日与结束日必须同时提供");
        }

        if (request.WeekStart is { } weekStart
            && request.WeekEnd is { } weekEnd
            && weekStart.Date > weekEnd.Date)
        {
            return InvalidRequest(context, "周起始日不能晚于结束日");
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(request.Status)
            ? null
            : request.Status.Trim();
        if (normalizedStatus is not null
            && normalizedStatus is not (AssetCheckStatusConstants.SUBMITTED
                or AssetCheckStatusConstants.VERIFIED
                or AssetCheckStatusConstants.INCLUDED
                or AssetCheckStatusConstants.REFUSED))
        {
            return InvalidRequest(context, "状态参数应为 0、1、2 或 3");
        }

        var normalizedRequest = new MonthlyReportOutputRequest
        {
            Mode = mode,
            Year = request.Year,
            Month = request.Month,
            WeekStart = request.WeekStart?.Date,
            WeekEnd = request.WeekEnd?.Date,
            AllMonths = request.AllMonths,
            Status = normalizedStatus,
            OnlyCurrent = request.OnlyCurrent,
            Copies = request.Copies,
            IsDuplex = request.IsDuplex,
        };

        var service = new AssetMonthlyReportPrintService(
            database, dataScope, queue, printers);
        var result = await service.CreateAsync(
            user.Id,
            string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName!,
            normalizedRequest,
            ct);

        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var job = result.Value;
        return Results.Json(new PrintJobCreatedResponse(
            job.Id, job.JobNo, job.Status, job.JobKind, []));
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
            result.Value.ContentType,
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
