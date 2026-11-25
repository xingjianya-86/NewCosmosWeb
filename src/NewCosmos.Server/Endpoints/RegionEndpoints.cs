using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.System;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 行政区划级联查询（省 → 市 → 县区 → 乡镇 → 村/社区）。
/// 数据来自 Core <see cref="IRegionService"/>（nc_regions_*），只读、按登录会话鉴权
///（SessionAuthenticationMiddleware 已拦截匿名请求，这里再做一次防御性校验）。
/// </summary>
public static class RegionEndpoints
{
    public static IEndpointRouteBuilder MapRegionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/regions");
        group.MapGet("/provinces", GetProvincesAsync);
        group.MapGet("/cities", GetCitiesAsync);
        group.MapGet("/counties", GetCountiesAsync);
        group.MapGet("/towns", GetTownsAsync);
        group.MapGet("/villages", GetVillagesAsync);
        return app;
    }

    private static async Task<IResult> GetProvincesAsync(
        HttpContext context,
        IRegionService regions,
        CancellationToken ct)
    {
        if (RequireLogin(context) is { } denied)
        {
            return denied;
        }

        var result = await regions.GetProvincesAsync(ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value
                .Select(x => new RegionOption(x.Id, x.ProvinceName))
                .ToList());
    }

    private static async Task<IResult> GetCitiesAsync(
        int? provinceId,
        HttpContext context,
        IRegionService regions,
        CancellationToken ct)
    {
        if (RequireLogin(context) is { } denied)
        {
            return denied;
        }

        if (provinceId is null or <= 0)
        {
            return Results.Json(Array.Empty<RegionOption>());
        }

        var result = await regions.GetCitiesByProvinceIdAsync(provinceId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value
                .Select(x => new RegionOption(x.Id, x.CityName))
                .ToList());
    }

    private static async Task<IResult> GetCountiesAsync(
        int? cityId,
        HttpContext context,
        IRegionService regions,
        CancellationToken ct)
    {
        if (RequireLogin(context) is { } denied)
        {
            return denied;
        }

        if (cityId is null or <= 0)
        {
            return Results.Json(Array.Empty<RegionOption>());
        }

        var result = await regions.GetCountiesByCityIdAsync(cityId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value
                .Select(x => new RegionOption(x.Id, x.CountyName))
                .ToList());
    }

    private static async Task<IResult> GetTownsAsync(
        int? countyId,
        HttpContext context,
        IRegionService regions,
        CancellationToken ct)
    {
        if (RequireLogin(context) is { } denied)
        {
            return denied;
        }

        if (countyId is null or <= 0)
        {
            return Results.Json(Array.Empty<RegionOption>());
        }

        var result = await regions.GetTownsByCountyIdAsync(countyId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value
                .Select(x => new RegionOption(x.Id, x.TownName))
                .ToList());
    }

    private static async Task<IResult> GetVillagesAsync(
        int? townId,
        HttpContext context,
        IRegionService regions,
        CancellationToken ct)
    {
        if (RequireLogin(context) is { } denied)
        {
            return denied;
        }

        if (townId is null or <= 0)
        {
            return Results.Json(Array.Empty<RegionOption>());
        }

        var result = await regions.GetVillagesByTownIdAsync(townId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value
                .Select(x => new RegionOption(x.Id, x.VillageName))
                .ToList());
    }

    private static IResult? RequireLogin(HttpContext context)
        => context.GetUserId() is null
            ? ApiErrors.Write(
                context,
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired,
                "请先登录")
            : null;

    private static IResult Failure(HttpContext context, string? code, string? message)
    {
        var mapped = ApiErrors.Map(code, message);
        return ApiErrors.Write(context, mapped.Status, mapped.Code, mapped.Message);
    }

    /// <summary>下拉项：前端 RegionOption { id, name }。</summary>
    private sealed record RegionOption(int Id, string Name);
}
