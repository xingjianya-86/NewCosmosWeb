using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 高龄津贴发放接口：
/// 只读（概览、登记列表/详情、下月待办、类别复核）与登记办理（表单回填、判类评估、导入名册、
/// 创建/更新/确认/删除、身份证 OCR）。
/// 所有端点先校验登录会话与对应 ELDERLY_* 权限；列表/详情在查询服务内叠加数据范围。
/// </summary>
public static class ElderlyEndpoints
{
    /// <summary>OCR 上传图片大小上限（与端点约定一致）。</summary>
    private const long MaxOcrUploadBytes = 10L * 1024 * 1024;

    /// <summary>OCR 请求体上限：文件 10MB + multipart 头开销。</summary>
    private const long MaxOcrRequestBodyBytes = MaxOcrUploadBytes + 1024 * 1024;

    public static IEndpointRouteBuilder MapElderlyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/elderly/summary", GetSummaryAsync);
        app.MapGet("/api/elderly/applications", ListApplicationsAsync);
        app.MapGet("/api/elderly/applications/{id:long}", GetApplicationDetailAsync);

        // 登记表单 / 评估 / 导入名册
        app.MapGet("/api/elderly/applications/{id:long}/form", GetApplicationFormAsync);
        app.MapGet("/api/elderly/evaluate", EvaluateApplicationAsync);
        app.MapPost("/api/elderly/evaluate/special", EvaluateSpecialPaybackAsync);
        app.MapGet("/api/elderly/imported", GetImportedByCardAsync);
        app.MapGet("/api/elderly/imported/search", SearchImportedAsync);

        // 写接口
        app.MapPost("/api/elderly/applications", CreateApplicationAsync);
        app.MapPut("/api/elderly/applications/{id:long}", UpdateApplicationAsync);
        app.MapPost("/api/elderly/applications/{id:long}/confirm", ConfirmApplicationAsync);
        app.MapDelete("/api/elderly/applications/{id:long}", DeleteApplicationAsync);
        app.MapPost("/api/elderly/applications/{id:long}/stop", StopApplicationAsync);
        app.MapPost("/api/elderly/stops/from-history/{historyId:long}", StopFromHistoryAsync);
        app.MapGet("/api/elderly/stops/stats", GetStopStatsAsync);
        app.MapGet("/api/elderly/operator-address", GetOperatorAddressAsync);

        // 身份证 OCR（multipart file；请求体上限略大于 10MB，端点再按文件大小拦截）
        app.MapPost("/api/elderly/ocr/idcard", RecognizeIdCardAsync)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxOcrRequestBodyBytes));

        app.MapGet("/api/elderly/pending", GetPendingAsync);
        app.MapGet("/api/elderly/reviews", ListReviewsAsync);
        return app;
    }

    // ───────────────────────────── 只读查询 ─────────────────────────────

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await CreateQueryService(context).GetSummaryAsync(ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ListApplicationsAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? status,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).ListApplicationsAsync(
            userId,
            page ?? 1,
            pageSize ?? ElderlyQueryService.DefaultPageSize,
            status,
            keyword,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetApplicationDetailAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).GetApplicationDetailAsync(userId, id, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetPendingAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).GetPendingAsync(userId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ListReviewsAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? status,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).ListReviewsAsync(
            userId,
            page ?? 1,
            pageSize ?? ElderlyQueryService.DefaultPageSize,
            status,
            keyword,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 表单 / 评估 / 导入 ─────────────────────────────

    private static async Task<IResult> GetApplicationFormAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await CreateWriteService(context).GetFormAsync(id, ct);
        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> EvaluateApplicationAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        string? idCard,
        string? applyDate)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var apply = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(applyDate))
        {
            if (!DateTime.TryParseExact(
                    applyDate.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return InvalidRequest(context, "applyDate 格式应为 yyyy-MM-dd");
            }

            apply = parsed.Date;
        }

        var result = await CreateWriteService(context).EvaluateAsync(idCard, apply, ct);
        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> EvaluateSpecialPaybackAsync(
        ElderlySpecialEvaluationRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var result = await CreateWriteService(context).EvaluateSpecialPaybackAsync(
            request.IdCard, request.StartMonth, request.EndMonth, ct);

        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetImportedByCardAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(idCard))
        {
            return InvalidRequest(context, "请填写身份证号");
        }

        var result = await CreateWriteService(context).GetImportedByIdCardAsync(idCard, ct);
        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> SearchImportedAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await CreateWriteService(context).SearchImportedAsync(keyword, ct);
        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 写接口 ─────────────────────────────

    private static async Task<IResult> CreateApplicationAsync(
        ElderlyApplicationFormRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_CREATE, ct);
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

        var result = await CreateWriteService(context).CreateAsync(
            user.Id, OperatorName(user), request, ct);

        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UpdateApplicationAsync(
        long id,
        ElderlyApplicationFormRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        var result = await CreateWriteService(context).UpdateAsync(
            id, OperatorName(user), request, ct);

        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> ConfirmApplicationAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await CreateWriteService(context).ConfirmAsync(id, OperatorName(user), ct);
        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> DeleteApplicationAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_DELETE, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await CreateWriteService(context).DeleteAsync(id, ct);
        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.NoContent();
    }

    // ───────────────────────────── 停发办理 ─────────────────────────────

    private static async Task<IResult> GetOperatorAddressAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var empty = new ElderlyOperatorAddressDto(
            null, null, null, null, null,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            0, "当前账号未绑定区划，地址不可选择", []);

        if (user.OrganizationId is not > 0)
        {
            return Results.Json(empty);
        }

        var services = context.RequestServices;
        var organizationService = services.GetRequiredService<IOrganizationService>();
        var organizationResult = await organizationService.GetByIdAsync(user.OrganizationId.Value, ct);
        if (organizationResult.IsFailure || organizationResult.Value is null)
        {
            return Results.Json(empty);
        }

        var org = organizationResult.Value;
        var lockLevel = ComputeAddressLockLevel(org);
        var scopeDescription = lockLevel switch
        {
            >= 5 => $"户籍地址限本单位管辖范围（{org.ProvinceName}{org.CityName}{org.CountyName}{org.TownName}{org.VillageName}）",
            >= 4 => $"户籍地址限本单位管辖范围（{org.ProvinceName}{org.CityName}{org.CountyName}{org.TownName}，村/社区可选）",
            >= 3 => $"户籍地址限本单位管辖范围（{org.ProvinceName}{org.CityName}{org.CountyName}，乡镇村可选）",
            >= 2 => $"户籍地址限本单位管辖范围（{org.ProvinceName}{org.CityName}，县乡村可选）",
            >= 1 => $"户籍地址限本省范围（{org.ProvinceName}）",
            _ => "当前账号未绑定区划，地址不可选择"
        };

        var villages = new List<ElderlyOperatorVillageDto>();
        if (org.TownId is > 0)
        {
            var regionService = services.GetRequiredService<IRegionService>();
            var villageResult = await regionService.GetVillagesByTownIdAsync(org.TownId.Value, ct);
            if (villageResult.IsSuccess)
            {
                villages = (villageResult.Value ?? [])
                    .Select(item => new ElderlyOperatorVillageDto(
                        item.Id, item.VillageName ?? string.Empty, item.VillageCode ?? string.Empty))
                    .ToList();
            }
        }

        return Results.Json(new ElderlyOperatorAddressDto(
            org.ProvinceId,
            org.CityId,
            org.CountyId,
            org.TownId,
            org.VillageId,
            org.ProvinceName ?? string.Empty,
            org.CityName ?? string.Empty,
            org.CountyName ?? string.Empty,
            org.TownName ?? string.Empty,
            org.VillageName ?? string.Empty,
            lockLevel,
            scopeDescription,
            villages));
    }

    private static int ComputeAddressLockLevel(Organization org)
    {
        if (org.VillageId is > 0 && !string.IsNullOrWhiteSpace(org.VillageName)) return 5;
        if (org.TownId is > 0 && !string.IsNullOrWhiteSpace(org.TownName)) return 4;
        if (org.CountyId is > 0 && !string.IsNullOrWhiteSpace(org.CountyName)) return 3;
        if (org.CityId is > 0 && !string.IsNullOrWhiteSpace(org.CityName)) return 2;
        if (org.ProvinceId is > 0 && !string.IsNullOrWhiteSpace(org.ProvinceName)) return 1;
        return 0;
    }

    private static async Task<IResult> GetStopStatsAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var statistics = context.RequestServices.GetRequiredService<IStatisticsService>();
        var stopResult = await statistics.GetElderlyStopStatsAsync(ct);
        var activeResult = await statistics.GetElderlyStatsAsync(ct);

        return Results.Json(new
        {
            activeCount = activeResult.IsSuccess ? activeResult.Value.ActiveCount : (int?)null,
            monthlyStopped = stopResult.IsSuccess ? stopResult.Value.MonthlyStopped : (int?)null,
        });
    }

    private static async Task<IResult> StopApplicationAsync(
        long id,
        ElderlyStopRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        var service = context.RequestServices.GetRequiredService<IElderlyApplicationService>();
        var existing = await service.GetByIdAsync(id, ct);
        if (existing.IsFailure)
        {
            return MapElderlyResult(context, existing);
        }

        if (existing.Value is null)
        {
            return NotFound(context, "登记记录不存在或已删除");
        }

        if (!string.Equals(
                existing.Value.Status, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
        {
            return InvalidRequest(context, "只有已确认（在享）人员才能停发");
        }

        if (!TryBuildStopValues(request, out var values, out var validation))
        {
            return InvalidRequest(context, validation!);
        }

        var result = await service.StopAsync(
            id,
            values.Reason,
            OperatorName(user),
            values.DueStopDate,
            values.DeathDate,
            values.IsRecover,
            values.RecoverStartMonth,
            values.RecoverEndMonth,
            values.RecoverAmount,
            values.Remark,
            ct);

        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.NoContent();
    }

    /// <summary>导入名册“建档并停发”：先迁移建档，再按同一停发参数停发。</summary>
    private static async Task<IResult> StopFromHistoryAsync(
        long historyId,
        ElderlyStopRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        if (!TryBuildStopValues(request, out var values, out var validation))
        {
            return InvalidRequest(context, validation!);
        }

        var service = context.RequestServices.GetRequiredService<IElderlyApplicationService>();
        var operatorName = OperatorName(user);
        var migrateResult = await service.MigrateFromHistoryAsync(historyId, operatorName, ct);
        if (migrateResult.IsFailure)
        {
            return MapElderlyResult(context, migrateResult);
        }

        var applicationId = migrateResult.Value;
        var result = await service.StopAsync(
            applicationId,
            values.Reason,
            operatorName,
            values.DueStopDate,
            values.DeathDate,
            values.IsRecover,
            values.RecoverStartMonth,
            values.RecoverEndMonth,
            values.RecoverAmount,
            values.Remark,
            ct);

        return result.IsFailure
            ? MapElderlyResult(context, result)
            : Results.Json(new { id = applicationId });
    }

    private sealed record StopValues(
        string Reason,
        DateTime? DueStopDate,
        DateTime? DeathDate,
        bool IsRecover,
        string RecoverStartMonth,
        string RecoverEndMonth,
        decimal RecoverAmount,
        string Remark);

    private static bool TryBuildStopValues(
        ElderlyStopRequest request,
        out StopValues values,
        out string? error)
    {
        values = default!;
        error = null;

        var reasonKey = (request.StopReason ?? string.Empty).Trim();
        if (reasonKey.Length == 0)
        {
            error = "请选择停发原因";
            return false;
        }

        string reason;
        if (string.Equals(reasonKey, ElderlyBenefitConstants.StopReasonOther, StringComparison.Ordinal))
        {
            var other = (request.OtherReasonText ?? string.Empty).Trim();
            if (other.Length == 0)
            {
                error = "请填写“其他”停发原因说明";
                return false;
            }

            reason = other;
        }
        else
        {
            reason = reasonKey;
        }

        if (string.Equals(reasonKey, ElderlyBenefitConstants.StopReasonDeceased, StringComparison.Ordinal)
            && request.DeathDate is null)
        {
            error = "请选择死亡日期";
            return false;
        }

        var start = string.Empty;
        var end = string.Empty;
        var amount = 0m;
        if (request.IsRecover)
        {
            if (!TryReadMonth(request.RecoverStartMonth, out start))
            {
                error = "追缴起算月格式应为 yyyy-MM";
                return false;
            }

            if (!TryReadMonth(request.RecoverEndMonth, out end))
            {
                error = "追缴止算月格式应为 yyyy-MM";
                return false;
            }

            if (string.CompareOrdinal(start, end) > 0)
            {
                error = "追缴止算月不能早于起算月";
                return false;
            }

            amount = request.RecoverAmount ?? 0m;
            if (amount < 0)
            {
                error = "追缴金额不能为负数";
                return false;
            }
        }

        values = new StopValues(
            reason,
            (request.DueStopDate ?? DateTime.Today).Date,
            request.DeathDate?.Date,
            request.IsRecover,
            start,
            end,
            amount,
            (request.Remark ?? string.Empty).Trim());
        return true;
    }

    private static bool TryReadMonth(string? raw, out string month)
    {
        month = string.Empty;
        var value = (raw ?? string.Empty).Trim();
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        month = parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return true;
    }

    // ───────────────────────────── OCR ─────────────────────────────

    private static async Task<IResult> RecognizeIdCardAsync(
        IFormFile? file,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        // OCR 只用于表单录入辅助，查看权限即可；新增/编辑权限在保存时再校验。
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (file is null || file.Length <= 0)
        {
            return InvalidRequest(context, "请选择要识别的身份证图片");
        }

        if (file.Length > MaxOcrUploadBytes)
        {
            return InvalidRequest(context, "身份证图片不能超过 10MB");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType)
            || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidRequest(context, "仅支持图片文件");
        }

        await using var stream = file.OpenReadStream();
        var result = await CreateOcrService(context).RecognizeIdCardAsync(stream, ct);

        if (result.IsFailure)
        {
            return MapOcrResult(context, result);
        }

        return Results.Json(result.Value);
    }

    // ───────────────────────────── 服务装配 ─────────────────────────────

    /// <summary>
    /// Program.cs 未注册本模块查询服务（共享文件不在本批改动范围），
    /// 依赖均已注册，这里按请求从 RequestServices 解析后组合。
    /// </summary>
    private static ElderlyQueryService CreateQueryService(HttpContext context)
    {
        var services = context.RequestServices;
        return new ElderlyQueryService(
            services.GetRequiredService<IDatabaseService>(),
            services.GetRequiredService<DataScopeFilterBuilder>(),
            services.GetRequiredService<IElderlyApplicationService>(),
            services.GetRequiredService<IStatisticsService>(),
            services.GetRequiredService<ILoggerService>());
    }

    /// <summary>写服务同样按请求组装（无状态：每次请求新建，避免跨请求共享可变状态）。</summary>
    private static ElderlyApplicationWriteService CreateWriteService(HttpContext context)
    {
        var services = context.RequestServices;
        return new ElderlyApplicationWriteService(
            services.GetRequiredService<IElderlyApplicationService>(),
            services.GetRequiredService<IRegionService>(),
            services.GetRequiredService<ILoggerService>());
    }

    private static ElderlyOcrService CreateOcrService(HttpContext context)
        => new(context.RequestServices.GetRequiredService<ILoggerService>());

    // ───────────────────────────── 通用 ─────────────────────────────

    /// <summary>操作人一律取会话中的登录用户名，不接受前端传入。</summary>
    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.Username) ? user.FullName ?? "System" : user.Username;

    /// <summary>
    /// 高龄写接口错误出口：未找到 404、身份证重复/唯一冲突 409、校验/配置 400，其余交统一映射。
    /// </summary>
    private static IResult MapElderlyResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, result.Message);
        }

        if (result.ErrorCode is ErrorCodes.DUPLICATE_ID_CARD)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict,
                ErrorCodes.DUPLICATE_ID_CARD, result.Message);
        }

        if (result.ErrorCode is ErrorCodes.DB_UNIQUE_VIOLATION or ErrorCodes.CONCURRENCY_CONFLICT)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict,
                "CONFLICT", result.Message);
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.INVALID_ID_CARD
            or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.CONFIG_NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, result.Message);
        }

        return ApiErrors.FromResult(context, result);
    }

    /// <summary>OCR 错误映射：图片问题 400、服务未配置/不可达 503、识别/超时 502。</summary>
    private static IResult MapOcrResult(HttpContext context, Result result)
    {
        var status = result.ErrorCode switch
        {
            ErrorCodes.OCR_SERVICE_UNREACHABLE => StatusCodes.Status503ServiceUnavailable,
            ErrorCodes.OCR_IMAGE_INVALID or ErrorCodes.OCR_NO_FIELD => StatusCodes.Status400BadRequest,
            ErrorCodes.OCR_TIMEOUT or ErrorCodes.OCR_RECOGNIZE_FAILED => StatusCodes.Status502BadGateway,
            ErrorCodes.CANCELLED => StatusCodes.Status408RequestTimeout,
            _ => StatusCodes.Status502BadGateway
        };

        return ApiErrors.Write(
            context,
            status,
            result.ErrorCode ?? ApiErrorCodes.InternalError,
            result.Message);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
