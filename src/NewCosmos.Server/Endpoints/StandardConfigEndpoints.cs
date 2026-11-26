using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 数据中心 → 标准配置管理接口。
/// 读取要求 STANDARD_VIEW，写入/同步/初始化要求 STANDARD_MANAGE；登录由 EndpointSecurity 统一校验。
/// 业务读写全部走 Core <see cref="IStandardConfigService"/>（参数化 SQL 在 Core），
/// 本层只做参数校验、DTO 映射与权限判定。
/// </summary>
public static class StandardConfigEndpoints
{
    private const string Prefix = "/api/system/standards";

    private const int MaxStandardTypeLength = 50;
    private const int MaxStandardNameLength = 100;
    private const int MaxHukouTypeLength = 50;
    private const int MaxSupportModeLength = 50;
    private const int MaxUnitLength = 20;
    private const int MaxDescriptionLength = 2000;

    public static IEndpointRouteBuilder MapStandardConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Prefix);

        group.MapGet("/", GetStandardsAsync);
        group.MapGet("/overview", GetOverviewAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:long}", UpdateAsync);
        group.MapDelete("/{id:long}", DeleteAsync);

        group.MapPost("/sync-schema", SyncSchemaAsync);
        group.MapPost("/seed", SeedAsync);

        return app;
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    /// <summary>GET /api/system/standards?type=MinWageLevel1 — 按标准类型筛选（不传/空串返回全部启用项）。</summary>
    private static async Task<IResult> GetStandardsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IStandardConfigService standards,
        CancellationToken ct,
        string? type)
    {
        if (await RequireViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var standardType = type?.Trim();
        if (standardType is { Length: > 0 } && standardType.Length > MaxStandardTypeLength)
        {
            return Invalid(context, $"标准类型长度不能超过 {MaxStandardTypeLength} 个字符");
        }

        var result = await standards.GetConfigStandardsAsync(
            string.IsNullOrEmpty(standardType) ? null! : standardType,
            ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = (result.Value ?? new List<ConfigStandard>())
            .Select(MapStandard)
            .ToList();

        return Results.Json(items);
    }

    /// <summary>GET /api/system/standards/overview — 标准配置总览（条数与城乡关键金额）。</summary>
    private static async Task<IResult> GetOverviewAsync(
        HttpContext context,
        INewPermissionService permissions,
        IStandardConfigService standards,
        CancellationToken ct)
    {
        if (await RequireViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await standards.GetOverviewAsync(ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var overview = result.Value;
        return Results.Json(new StandardOverviewDto(
            overview.IncomeStandardsCount,
            overview.SubsidyStandardsCount,
            overview.DestituteStandardsCount,
            overview.SystemConfigsCount,
            overview.RuralIncomeStandard,
            overview.UrbanIncomeStandard,
            overview.RuralSubsidyAmount,
            overview.UrbanSubsidyAmount,
            overview.ConfigStandardsCount));
    }

    // ───────────────────────────── 写入 ─────────────────────────────

    /// <summary>POST /api/system/standards — 新增标准配置（标准类型必填）。</summary>
    private static async Task<IResult> CreateAsync(
        StandardConfigRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IStandardConfigService standards,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var validation = NormalizeStandardRequest(request, null, out var standard);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        standard.CreatedBy = context.GetCurrentUser()?.Username ?? string.Empty;

        var result = await standards.CreateConfigStandardAsync(standard, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(
            new StandardCreatedResponse(result.Value, "标准配置已创建"),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// PUT /api/system/standards/{id} — 编辑标准配置。
    /// Version 用于乐观并发：请求未带版本时沿用当前记录版本；带版本时由 Core 校验冲突。
    /// StandardType 不参与更新（Core 更新 SQL 不修改该列）。
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        long id,
        StandardConfigRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IStandardConfigService standards,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var listResult = await standards.GetConfigStandardsAsync(null, ct);
        if (listResult.IsFailure)
        {
            return ApiErrors.FromResult(context, listResult);
        }

        var existing = (listResult.Value ?? new List<ConfigStandard>())
            .FirstOrDefault(s => s.Id == id);
        if (existing is null)
        {
            return NotFound(context, "标准配置不存在");
        }

        var validation = NormalizeStandardRequest(request, existing, out var standard);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        standard.Id = id;
        standard.StandardType = existing.StandardType;
        standard.Version = request.Version ?? existing.Version;

        var result = await standards.UpdateConfigStandardAsync(standard, ct);
        if (result.IsFailure)
        {
            // Core 用该错误码表达乐观并发冲突（0 行更新），不应按数据库故障返回 500。
            if (result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION)
            {
                return ApiErrors.Write(
                    context,
                    StatusCodes.Status409Conflict,
                    ApiErrorCodes.ValidationError,
                    string.IsNullOrWhiteSpace(result.Message)
                        ? "数据已被其他人修改，请刷新后重试"
                        : result.Message);
            }

            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new StandardActionResult("标准配置已更新"));
    }

    /// <summary>DELETE /api/system/standards/{id} — 停用（软删除）标准配置。</summary>
    private static async Task<IResult> DeleteAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IStandardConfigService standards,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var listResult = await standards.GetConfigStandardsAsync(null, ct);
        if (listResult.IsFailure)
        {
            return ApiErrors.FromResult(context, listResult);
        }

        var existing = (listResult.Value ?? new List<ConfigStandard>())
            .FirstOrDefault(s => s.Id == id);
        if (existing is null)
        {
            return NotFound(context, "标准配置不存在");
        }

        var result = await standards.DeleteConfigStandardAsync(id, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new StandardActionResult("标准配置已删除"));
    }

    // ───────────────────────────── 同步 / 初始化 ─────────────────────────────

    /// <summary>POST /api/system/standards/sync-schema — 重建 nc_config_standards 表结构。</summary>
    private static async Task<IResult> SyncSchemaAsync(
        HttpContext context,
        INewPermissionService permissions,
        IStandardConfigService standards,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await standards.SyncSchemaAsync(null, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new StandardActionResult("标准配置表结构同步完成"));
    }

    /// <summary>POST /api/system/standards/seed — 从 YAML 种子重建并导入标准配置（清空覆盖）。</summary>
    private static async Task<IResult> SeedAsync(
        HttpContext context,
        INewPermissionService permissions,
        IStandardConfigService standards,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await standards.InitializeFromSeedAsync(null, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new StandardActionResult("标准配置数据初始化完成"));
    }

    // ───────────────────────────── 校验 / 映射 ─────────────────────────────

    /// <summary>
    /// 校验并归一化标准配置写入字段；existing 非空表示编辑，未提供的字段沿用原值。
    /// EffectiveEndDate 例外：null 表示「长期有效」（编辑时显式清空结束日期）。
    /// </summary>
    private static string? NormalizeStandardRequest(
        StandardConfigRequest request,
        ConfigStandard? existing,
        out ConfigStandard standard)
    {
        standard = new ConfigStandard();

        var standardType = (request.StandardType ?? existing?.StandardType ?? string.Empty).Trim();
        if (standardType.Length == 0)
        {
            return "标准类型不能为空";
        }

        if (standardType.Length > MaxStandardTypeLength)
        {
            return $"标准类型长度不能超过 {MaxStandardTypeLength} 个字符";
        }

        var standardName = (request.StandardName ?? existing?.StandardName ?? string.Empty).Trim();
        if (standardName.Length == 0)
        {
            return "标准名称不能为空";
        }

        if (standardName.Length > MaxStandardNameLength)
        {
            return $"标准名称长度不能超过 {MaxStandardNameLength} 个字符";
        }

        var hukouType = request.HukouType is null ? existing?.HukouType : request.HukouType.Trim();
        hukouType = string.IsNullOrWhiteSpace(hukouType) ? null : hukouType;
        if (hukouType is not null && hukouType.Length > MaxHukouTypeLength)
        {
            return $"户籍类型长度不能超过 {MaxHukouTypeLength} 个字符";
        }

        var supportMode = request.SupportMode is null ? existing?.SupportMode : request.SupportMode.Trim();
        supportMode = string.IsNullOrWhiteSpace(supportMode) ? null : supportMode;
        if (supportMode is not null && supportMode.Length > MaxSupportModeLength)
        {
            return $"供养方式长度不能超过 {MaxSupportModeLength} 个字符";
        }

        var standardValue = request.StandardValue ?? existing?.StandardValue;
        if (standardValue is null)
        {
            return "标准值不能为空";
        }

        if (standardValue < 0)
        {
            return "标准值不能为负数";
        }

        var unit = request.Unit is null ? existing?.Unit ?? string.Empty : request.Unit.Trim();
        if (unit.Length > MaxUnitLength)
        {
            return $"单位长度不能超过 {MaxUnitLength} 个字符";
        }

        var startDate = request.EffectiveStartDate ?? existing?.EffectiveStartDate;
        if (startDate is null || startDate == default)
        {
            return "生效开始日期不能为空";
        }

        var endDate = request.EffectiveEndDate;
        if (endDate is { } end && end < startDate.Value)
        {
            return "生效结束日期不能早于生效开始日期";
        }

        var version = request.Version ?? existing?.Version ?? 1;
        if (version <= 0)
        {
            return "版本号必须大于 0";
        }

        var description = request.Description is null
            ? existing?.Description ?? string.Empty
            : request.Description.Trim();
        if (description.Length > MaxDescriptionLength)
        {
            return $"标准配置说明长度不能超过 {MaxDescriptionLength} 个字符";
        }

        standard.StandardType = standardType;
        standard.StandardName = standardName;
        standard.HukouType = hukouType;
        standard.SupportMode = supportMode;
        standard.StandardValue = standardValue.Value;
        standard.Unit = unit;
        standard.EffectiveStartDate = startDate.Value;
        standard.EffectiveEndDate = endDate;
        standard.Version = version;
        standard.IsActive = request.IsActive ?? existing?.IsActive ?? true;
        standard.Description = description;
        return null;
    }

    private static StandardConfigDto MapStandard(ConfigStandard standard) => new(
        standard.Id,
        standard.StandardType,
        standard.StandardName,
        standard.HukouType,
        standard.SupportMode,
        standard.StandardValue,
        standard.Unit,
        standard.EffectiveStartDate,
        standard.EffectiveEndDate,
        standard.Version,
        standard.IsActive,
        standard.Description,
        standard.UpdatedAt == default ? null : standard.UpdatedAt);

    // ───────────────────────────── 权限 / 错误 ─────────────────────────────

    private static Task<IResult?> RequireViewAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(context, permissions, PermissionCodes.STANDARD_VIEW, ct);

    private static Task<IResult?> RequireManageAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(context, permissions, PermissionCodes.STANDARD_MANAGE, ct);

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, message);
}
