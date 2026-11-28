using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 行政区划级联查询（省 → 市 → 县区 → 乡镇 → 村/社区）与维护。
/// 数据来自 Core <see cref="IRegionService"/>（nc_regions_*）。
/// <para>
/// 只读接口沿用原有口径：登录即可（供业务表单级联），不额外要求权限。
/// 写接口（增删改 / 同步表结构 / 种子初始化 / 清空）统一要求 <see cref="PermissionCodes.REGION_MANAGE"/>，
/// 业务校验与关联检查全部由 Core 返回，端点只做权限、路由校验与错误映射。
/// </para>
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
        group.MapGet("/villages/by-code", GetVillageByCodeAsync);

        group.MapPost("/cities", CreateCityAsync);
        group.MapPut("/cities/{id:int}", UpdateCityAsync);
        group.MapDelete("/cities/{id:int}", DeleteCityAsync);

        group.MapPost("/counties", CreateCountyAsync);
        group.MapPut("/counties/{id:int}", UpdateCountyAsync);
        group.MapDelete("/counties/{id:int}", DeleteCountyAsync);

        group.MapPost("/towns", CreateTownAsync);
        group.MapPut("/towns/{id:int}", UpdateTownAsync);
        group.MapDelete("/towns/{id:int}", DeleteTownAsync);

        group.MapPost("/villages", CreateVillageAsync);
        group.MapPut("/villages/{id:int}", UpdateVillageAsync);
        group.MapDelete("/villages/{id:int}", DeleteVillageAsync);

        group.MapPost("/sync-schema", SyncSchemaAsync);
        group.MapPost("/seed", SeedAsync);
        group.MapPost("/clear", ClearAsync);

        // 管理页读取完整对象（含编码/排序/启用状态），与下拉接口分离。
        group.MapGet("/manage/provinces", ManageProvincesAsync);
        group.MapGet("/manage/cities", ManageCitiesAsync);
        group.MapGet("/manage/counties", ManageCountiesAsync);
        group.MapGet("/manage/towns", ManageTownsAsync);
        group.MapGet("/manage/villages", ManageVillagesAsync);

        return app;
    }

    // ───────────────────────────── 管理页读取 ─────────────────────────────

    private static async Task<IResult> ManageProvincesAsync(
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await regions.GetProvincesAsync(ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ManageCitiesAsync(
        int? provinceId,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (provinceId is null or <= 0)
        {
            return Results.Json(Array.Empty<object>());
        }

        var result = await regions.GetCitiesByProvinceIdAsync(provinceId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ManageCountiesAsync(
        int? cityId,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (cityId is null or <= 0)
        {
            return Results.Json(Array.Empty<object>());
        }

        var result = await regions.GetCountiesByCityIdAsync(cityId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ManageTownsAsync(
        int? countyId,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (countyId is null or <= 0)
        {
            return Results.Json(Array.Empty<object>());
        }

        var result = await regions.GetTownsByCountyIdAsync(countyId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ManageVillagesAsync(
        int? townId,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (townId is null or <= 0)
        {
            return Results.Json(Array.Empty<object>());
        }

        var result = await regions.GetVillagesByTownIdAsync(townId.Value, ct);
        return result.IsFailure
            ? Failure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 只读级联（登录即可） ─────────────────────────────

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

    /// <summary>
    /// 按村编码反查村/社区（含所属乡镇名）：资产核对等表的 community 列存的是**村编码**，
    /// 而表单 AddressCascader 按名称级联，需要镇/村名称才能选中。
    /// 只读口径与本文件其它级联一致（登录即可）；查无此村返回 404，由前端按“解析失败”降级。
    /// </summary>
    private static async Task<IResult> GetVillageByCodeAsync(
        string? code,
        HttpContext context,
        IRegionService regions,
        CancellationToken ct)
    {
        if (RequireLogin(context) is { } denied)
        {
            return denied;
        }

        var normalized = code?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Invalid(context, "村/社区编码不能为空");
        }

        var villageResult = await regions.GetVillageByCodeAsync(normalized, ct);
        if (villageResult.IsFailure)
        {
            return Failure(context, villageResult.ErrorCode, villageResult.Message);
        }

        var village = villageResult.Value;
        if (village is null)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "未找到对应的村/社区");
        }

        // 镇名查不到不阻断：村名仍可用于回填，前端按空镇名处理。
        var townName = string.Empty;
        if (village.TownId > 0)
        {
            var townResult = await regions.GetTownByIdAsync(village.TownId, ct);
            if (townResult.IsSuccess && townResult.Value is not null)
            {
                townName = townResult.Value.TownName ?? string.Empty;
            }
        }

        return Results.Json(new
        {
            id = village.Id,
            townId = village.TownId,
            villageName = village.VillageName,
            villageCode = village.VillageCode,
            townName,
        });
    }

    // ───────────────────────────── 地级市维护 ─────────────────────────────

    private static async Task<IResult> CreateCityAsync(
        RegionCitySaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var result = await regions.CreateCityAsync(request, ct);
        return await CityWriteResultAsync(context, regions, result, ct);
    }

    private static async Task<IResult> UpdateCityAsync(
        int id,
        RegionCitySaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        if (id <= 0)
        {
            return Invalid(context, "地级市 ID 无效");
        }

        request.Id = id;
        var result = await regions.UpdateCityAsync(request, ct);
        return await CityWriteResultAsync(context, regions, result, ct);
    }

    private static async Task<IResult> DeleteCityAsync(
        int id,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (id <= 0)
        {
            return Invalid(context, "地级市 ID 无效");
        }

        var result = await regions.DeleteCityAsync(id, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(new { message = "删除成功" });
    }

    /// <summary>
    /// 地级市创建/更新后返回完整对象。Core 的地级市 INSERT/UPDATE 仅 RETURNING id，
    /// 返回的实体只有主键；这里按 id 回读一次，避免前端拿到空名称/空编码。
    /// </summary>
    private static async Task<IResult> CityWriteResultAsync(
        HttpContext context,
        IRegionService regions,
        Result<RegionCity> result,
        CancellationToken ct)
    {
        if (result.IsFailure)
        {
            return WriteFailure(context, result.ErrorCode, result.Message);
        }

        var fresh = await regions.GetCityByIdAsync(result.Value.Id, ct);
        return Results.Json(fresh.HasValue ? fresh.Value : result.Value);
    }

    // ───────────────────────────── 县区维护 ─────────────────────────────

    private static async Task<IResult> CreateCountyAsync(
        RegionCountySaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var result = await regions.CreateCountyAsync(request, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UpdateCountyAsync(
        int id,
        RegionCountySaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        if (id <= 0)
        {
            return Invalid(context, "县区 ID 无效");
        }

        request.Id = id;
        var result = await regions.UpdateCountyAsync(request, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> DeleteCountyAsync(
        int id,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (id <= 0)
        {
            return Invalid(context, "县区 ID 无效");
        }

        var result = await regions.DeleteCountyAsync(id, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(new { message = "删除成功" });
    }

    // ───────────────────────────── 乡镇维护 ─────────────────────────────

    private static async Task<IResult> CreateTownAsync(
        RegionTownSaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var result = await regions.CreateTownAsync(request, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UpdateTownAsync(
        int id,
        RegionTownSaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        if (id <= 0)
        {
            return Invalid(context, "乡镇 ID 无效");
        }

        request.Id = id;
        var result = await regions.UpdateTownAsync(request, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> DeleteTownAsync(
        int id,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (id <= 0)
        {
            return Invalid(context, "乡镇 ID 无效");
        }

        var result = await regions.DeleteTownAsync(id, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(new { message = "删除成功" });
    }

    // ───────────────────────────── 村/社区维护 ─────────────────────────────

    private static async Task<IResult> CreateVillageAsync(
        RegionVillageSaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var result = await regions.CreateVillageAsync(request, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UpdateVillageAsync(
        int id,
        RegionVillageSaveRequest? request,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        if (id <= 0)
        {
            return Invalid(context, "村/社区 ID 无效");
        }

        request.Id = id;
        var result = await regions.UpdateVillageAsync(request, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> DeleteVillageAsync(
        int id,
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (id <= 0)
        {
            return Invalid(context, "村/社区 ID 无效");
        }

        var result = await regions.DeleteVillageAsync(id, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(new { message = "删除成功" });
    }

    // ───────────────────────────── 表结构 / 种子数据 ─────────────────────────────

    private static async Task<IResult> SyncSchemaAsync(
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await regions.SyncSchemaAsync(null, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(new { message = "行政区划表结构同步完成" });
    }

    private static async Task<IResult> SeedAsync(
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await regions.InitializeFromSeedAsync(null, ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(new { message = "行政区划数据初始化完成" });
    }

    private static async Task<IResult> ClearAsync(
        HttpContext context,
        IRegionService regions,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await regions.ClearTablesAsync(ct);
        return result.IsFailure
            ? WriteFailure(context, result.ErrorCode, result.Message)
            : Results.Json(new { message = "行政区划数据已清空" });
    }

    // ───────────────────────────── 通用 ─────────────────────────────

    private static IResult? RequireLogin(HttpContext context)
        => context.GetUserId() is null
            ? ApiErrors.Write(
                context,
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired,
                "请先登录")
            : null;

    /// <summary>管理页读取要求 REGION_VIEW；返回 null 表示放行。</summary>
    private static Task<IResult?> DenyViewAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(context, permissions, PermissionCodes.REGION_VIEW, ct);

    /// <summary>写接口统一要求 REGION_MANAGE；返回 null 表示放行。</summary>
    private static Task<IResult?> DenyManageAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(context, permissions, PermissionCodes.REGION_MANAGE, ct);

    private static IResult Failure(HttpContext context, string? code, string? message)
    {
        var mapped = ApiErrors.Map(code, message);
        return ApiErrors.Write(context, mapped.Status, mapped.Code, mapped.Message);
    }

    /// <summary>
    /// 写接口失败出口：地区业务错误保留 Core 的中文消息并给出合适状态码。
    /// 删除时 Core 会先做关联检查（该县区下存在乡镇 / 该乡镇下存在村社区），
    /// 有下级直接 409 返回，不落到通用 500。
    /// </summary>
    private static IResult WriteFailure(HttpContext context, string? code, string? message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? "操作失败" : message.Trim();

        return code switch
        {
            ErrorCodes.VALIDATION_FAILED or ErrorCodes.REQUIRED_FIELD_MISSING
                => ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                    ApiErrorCodes.ValidationError, text),

            ErrorCodes.REGION_CITY_NOT_FOUND or ErrorCodes.REGION_COUNTY_NOT_FOUND
                or ErrorCodes.REGION_TOWN_NOT_FOUND or ErrorCodes.REGION_VILLAGE_NOT_FOUND
                or ErrorCodes.NOT_FOUND
                => ApiErrors.Write(context, StatusCodes.Status404NotFound,
                    ApiErrorCodes.NotFound, text),

            ErrorCodes.REGION_HAS_CHILDREN
                => ApiErrors.Write(context, StatusCodes.Status409Conflict,
                    "CONFLICT", text),

            ErrorCodes.REGION_NAME_DUPLICATE
                => ApiErrors.Write(context, StatusCodes.Status409Conflict,
                    "CONFLICT", text),

            ErrorCodes.DB_UNIQUE_VIOLATION
                => ApiErrors.Write(context, StatusCodes.Status409Conflict,
                    "CONFLICT", "地区名称或编码已存在"),

            ErrorCodes.DB_FOREIGN_KEY_VIOLATION
                => ApiErrors.Write(context, StatusCodes.Status409Conflict,
                    "CONFLICT", "该地区存在下级数据或已被引用，无法删除"),

            _ => Failure(context, code, message),
        };
    }

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    /// <summary>下拉项：前端 RegionOption { id, name }。</summary>
    private sealed record RegionOption(int Id, string Name);
}
