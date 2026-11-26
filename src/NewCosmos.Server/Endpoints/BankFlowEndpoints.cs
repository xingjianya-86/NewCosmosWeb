using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Domain.BankFlow;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 银行流水核对查看页接口（数据中心 → 银行流水核对）。
/// <para>
/// 数据与业务口径全部来自 Core <see cref="IBankFlowQueryService"/>：批次列表、
/// 分页筛选、按当前筛选导出 xlsx。查看需 <see cref="PermissionCodes.ARCHIVE_VIEW"/>，
/// 导出需 <see cref="PermissionCodes.SYSTEM_EXPORT"/>；端点只做权限、分页边界与契约映射。
/// </para>
/// </summary>
public static class BankFlowEndpoints
{
    private const string Prefix = "/api/bank-flow";
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapBankFlowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/imports", GetImportsAsync);
        app.MapGet($"{Prefix}/records", SearchRecordsAsync);
        app.MapGet($"{Prefix}/export", ExportAsync);
        return app;
    }

    // ───────────────────────────── 批次 ─────────────────────────────

    private static async Task<IResult> GetImportsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IBankFlowQueryService bankFlow,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await bankFlow.GetImportsAsync(ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = (result.Value ?? [])
            .Select(import => new BankFlowImportDto(
                import.Id,
                import.BatchNo ?? string.Empty,
                import.SourceFileName ?? string.Empty,
                import.TotalCount,
                import.ExceedCount,
                import.InvalidIdCardCount,
                import.ExceedThreshold,
                import.CreatedAt))
            .ToList();

        return Results.Json(items);
    }

    // ───────────────────────────── 分页记录 ─────────────────────────────

    private static async Task<IResult> SearchRecordsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IBankFlowQueryService bankFlow,
        CancellationToken ct,
        string? keyword,
        long? importId,
        bool? exceedOnly,
        int? page,
        int? pageSize)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var pageIndex = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(
            pageSize is null or <= 0 ? DefaultPageSize : pageSize.Value, 1, MaxPageSize);
        var keywordValue = (keyword ?? string.Empty).Trim();
        var importFilter = importId is > 0 ? importId : null;

        var result = await bankFlow.SearchRecordsAsync(
            keywordValue, importFilter, exceedOnly == true, pageIndex, size, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var pageResult = result.Value;
        var items = (pageResult?.Items ?? [])
            .Select(ToDto)
            .ToList();

        return Results.Json(new BankFlowRecordListResponse(
            items,
            pageResult?.PageIndex ?? pageIndex,
            pageResult?.PageSize ?? size,
            pageResult?.TotalCount ?? 0,
            pageResult?.TotalPages ?? 0));
    }

    // ───────────────────────────── 导出 ─────────────────────────────

    private static async Task<IResult> ExportAsync(
        HttpContext context,
        INewPermissionService permissions,
        IBankFlowQueryService bankFlow,
        CancellationToken ct,
        string? keyword,
        long? importId,
        bool? exceedOnly)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.SYSTEM_EXPORT, ct);
        if (denied is not null)
        {
            return denied;
        }

        var keywordValue = (keyword ?? string.Empty).Trim();
        var importFilter = importId is > 0 ? importId : null;

        var result = await bankFlow.ExportAsync(keywordValue, importFilter, exceedOnly == true, ct);
        if (result.IsFailure || result.Value is null)
        {
            return ApiErrors.FromResult(context, result);
        }

        var fileName = $"银行流水核对_{DateTime.Now:yyyyMMddHHmm}.xlsx";
        return Results.File(result.Value, ExcelContentType, fileName);
    }

    // ───────────────────────────── 契约映射 ─────────────────────────────

    private static BankFlowRecordDto ToDto(BankFlowRecord record)
        => new(
            record.Id,
            record.RowNo,
            record.PersonName ?? string.Empty,
            record.IdCard ?? string.Empty,
            record.IdCardValid,
            record.Relationship ?? string.Empty,
            record.FamilyAddress ?? string.Empty,
            record.TotalBalance,
            record.MaxFlowAmount,
            record.BankSummary ?? string.Empty,
            record.IsExceed,
            record.MachineryPower,
            record.TotalBalanceDisplay,
            record.MaxFlowAmountDisplay,
            record.MachineryPowerDisplay,
            record.IdCardValidDisplay);
}
