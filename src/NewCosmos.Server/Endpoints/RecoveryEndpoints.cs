using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 后补追缴接口（Server 端完整 API）。
/// <para>
/// 读取 <c>RECOVERY_VIEW</c>；新增/修改/删除/状态推进 <c>RECOVERY_MANAGE</c>。
/// 业务口径（停止人员检索、默认月保障额、按身份证查档、金额分段计算、编号分配、状态机）全部在
/// Core <see cref="IRecoveryService"/> / <see cref="IRecoveryHistoryProvider"/> /
/// <see cref="RecoveryAmountCalculator"/>，本文件只做权限、参数校验与 Result 映射。
/// 1:1 对齐旧版全量数据，不按组织过滤。
/// </para>
/// </summary>
public static class RecoveryEndpoints
{
    /// <summary>记录状态白名单（与 RecoveryConstants 同源）。</summary>
    private static readonly string[] RecordStatuses =
    [
        RecoveryConstants.STATUS_DRAFT,
        RecoveryConstants.STATUS_CONFIRMED,
        RecoveryConstants.STATUS_PRINTED
    ];

    public static IEndpointRouteBuilder MapRecoveryEndpoints(this IEndpointRouteBuilder app)
    {
        const string prefix = "/api/recovery";

        app.MapGet($"{prefix}/stopped-persons", ListStoppedPersonsAsync);
        app.MapGet($"{prefix}/stopped-persons/stats", GetStoppedPersonStatsAsync);
        app.MapGet($"{prefix}/lookup", LookupArchiveAsync);
        app.MapGet($"{prefix}/default-monthly-amount", GetDefaultMonthlyAmountAsync);
        app.MapGet($"{prefix}/history", GetHistoryAsync);
        app.MapGet($"{prefix}/calculate", CalculateAsync);

        app.MapGet($"{prefix}/records", ListRecordsAsync);
        app.MapGet($"{prefix}/records/{{id:long}}", GetRecordAsync);
        app.MapGet($"{prefix}/records/{{id:long}}/segments", GetSegmentsAsync);
        app.MapGet($"{prefix}/records/{{id:long}}/code", GetCodeAsync);

        app.MapPost($"{prefix}/records", CreateRecordAsync);
        app.MapPost($"{prefix}/records/manual", CreateManualRecordAsync);
        app.MapPut($"{prefix}/records/{{id:long}}", UpdateRecordAsync);
        app.MapPut($"{prefix}/records/{{id:long}}/manual", UpdateManualRecordAsync);
        app.MapDelete($"{prefix}/records/{{id:long}}", DeleteRecordAsync);
        app.MapPost($"{prefix}/records/{{id:long}}/status", UpdateStatusAsync);

        return app;
    }

    // ───────────────────────────── 停止人员 / 查档 ─────────────────────────────

    /// <summary>GET /api/recovery/stopped-persons?keyword=</summary>
    private static async Task<IResult> ListStoppedPersonsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.SearchStoppedPersonsAsync(keyword?.Trim() ?? string.Empty, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    /// <summary>GET /api/recovery/stopped-persons/stats</summary>
    private static async Task<IResult> GetStoppedPersonStatsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.GetStoppedPersonStatsAsync(ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    /// <summary>GET /api/recovery/lookup?idCard= —— 未命中返回 null（允许档案外人员转手工）。</summary>
    private static async Task<IResult> LookupArchiveAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.LookupArchiveByIdCardAsync(idCard?.Trim() ?? string.Empty, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    // ───────────────────────────── 金额 / 历史 ─────────────────────────────

    /// <summary>GET /api/recovery/default-monthly-amount?sourceType=&amp;sourceId=</summary>
    private static async Task<IResult> GetDefaultMonthlyAmountAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct,
        string? sourceType,
        long? sourceId)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(sourceType) || sourceId is not > 0)
        {
            return Invalid(context, "sourceType 与 sourceId 不能为空");
        }

        var result = await service.GetDefaultMonthlyAmountAsync(sourceType.Trim(), sourceId.Value, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new RecoveryAmountResponse(result.Value));
    }

    /// <summary>GET /api/recovery/history?sourceType=&amp;sourceId=</summary>
    private static async Task<IResult> GetHistoryAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryHistoryProvider history,
        CancellationToken ct,
        string? sourceType,
        long? sourceId)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        // sourceId=0 表示无来源档案（手工录入），没有历史时间轴。
        if (sourceId is not > 0 || string.IsNullOrWhiteSpace(sourceType))
        {
            return Results.Json(new List<RecoveryMonthlyStandardPoint>());
        }

        var result = await history.GetMonthlyStandardsAsync(sourceType.Trim(), sourceId.Value, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value ?? []);
    }

    /// <summary>
    /// GET /api/recovery/calculate?sourceType=&amp;sourceId=&amp;startMonth=&amp;endMonth=&amp;monthlyAmount=
    /// 自动分段计算；sourceId=0 时时间轴为空，按 monthlyAmount 定额兜底或返回不可计算原因。
    /// </summary>
    private static async Task<IResult> CalculateAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryHistoryProvider history,
        CancellationToken ct,
        string? sourceType,
        long? sourceId,
        string? startMonth,
        string? endMonth,
        decimal? monthlyAmount)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        IReadOnlyList<RecoveryMonthlyStandardPoint> timeline = [];
        if (sourceId is > 0 && !string.IsNullOrWhiteSpace(sourceType))
        {
            var historyResult = await history.GetMonthlyStandardsAsync(sourceType.Trim(), sourceId.Value, ct);
            if (historyResult.IsFailure)
            {
                return MapResult(context, historyResult);
            }

            timeline = historyResult.Value ?? [];
        }

        var amount = RecoveryAmountCalculator.Calculate(
            startMonth, endMonth, timeline, monthlyAmount ?? 0m);
        return Results.Json(new
        {
            canCalculate = amount.CanCalculate,
            reason = amount.Reason,
            amount = amount.TotalAmount,
            totalAmount = amount.TotalAmount,
            totalMonths = amount.TotalMonths,
            amountSource = amount.AmountSource,
            segments = amount.Segments
        });
    }

    // ───────────────────────────── 追缴记录 ─────────────────────────────

    /// <summary>GET /api/recovery/records?keyword=&amp;status= —— 1:1 旧版，单页最多 100 条。</summary>
    private static async Task<IResult> ListRecordsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct,
        string? keyword,
        string? status)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        string? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryNormalizeStatus(status, out var normalized))
            {
                return Invalid(context, "status 只能是 Draft / Confirmed / Printed");
            }

            statusFilter = normalized;
        }

        var result = await service.GetRecoveryRecordsAsync(keyword?.Trim(), statusFilter, 100, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value ?? []);
    }

    /// <summary>GET /api/recovery/records/{id}</summary>
    private static async Task<IResult> GetRecordAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.GetRecoveryRecordByIdAsync(id, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        return result.Value is null ? NotFound(context, "追缴记录不存在") : Results.Json(result.Value);
    }

    /// <summary>GET /api/recovery/records/{id}/segments</summary>
    private static async Task<IResult> GetSegmentsAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.GetRecoverySegmentsAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value ?? []);
    }

    /// <summary>GET /api/recovery/records/{id}/code —— 取/补分配追缴编号（补打同号）。</summary>
    private static async Task<IResult> GetCodeAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var recordResult = await service.GetRecoveryRecordByIdAsync(id, ct);
        if (recordResult.IsFailure)
        {
            return MapResult(context, recordResult);
        }

        if (recordResult.Value is null)
        {
            return NotFound(context, "追缴记录不存在");
        }

        var result = await service.GetOrAssignRecoveryCodeAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new RecoveryCodeResponse(result.Value ?? string.Empty));
    }

    /// <summary>POST /api/recovery/records —— CreatedBy 强制取当前登录用户名。</summary>
    private static async Task<IResult> CreateRecordAsync(
        RecoveryRecordDto? request,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_MANAGE, ct);
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
            return Invalid(context, "请求体不能为空");
        }

        request.CreatedBy = user.Username;
        var result = await service.SaveRecoveryRecordAsync(request, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new RecoveryCreatedResponse(result.Value));
    }

    /// <summary>POST /api/recovery/records/manual —— CreatedBy 强制取当前登录用户名。</summary>
    private static async Task<IResult> CreateManualRecordAsync(
        ManualRecoveryRecordDto? request,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_MANAGE, ct);
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
            return Invalid(context, "请求体不能为空");
        }

        request.CreatedBy = user.Username;
        var result = await service.SaveManualRecoveryRecordAsync(request, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new RecoveryCreatedResponse(result.Value));
    }

    /// <summary>PUT /api/recovery/records/{id}</summary>
    private static async Task<IResult> UpdateRecordAsync(
        long id,
        RecoveryRecordDto? request,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var result = await service.UpdateRecoveryRecordAsync(id, request, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>PUT /api/recovery/records/{id}/manual</summary>
    private static async Task<IResult> UpdateManualRecordAsync(
        long id,
        ManualRecoveryRecordDto? request,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var result = await service.UpdateManualRecoveryRecordAsync(id, request, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>DELETE /api/recovery/records/{id} —— 仅草稿可删。</summary>
    private static async Task<IResult> DeleteRecordAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.DeleteRecoveryRecordAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>POST /api/recovery/records/{id}/status —— 只允许 Confirmed / Printed。</summary>
    private static async Task<IResult> UpdateStatusAsync(
        long id,
        RecoveryStatusRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IRecoveryService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.RECOVERY_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null || !TryNormalizeTargetStatus(request.Status, out var status))
        {
            return Invalid(context, "status 只能是 Confirmed 或 Printed");
        }

        var result = await service.UpdateRecoveryRecordStatusAsync(id, status, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static bool TryNormalizeStatus(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        foreach (var status in RecordStatuses)
        {
            if (string.Equals(status, input.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                normalized = status;
                return true;
            }
        }

        return false;
    }

    /// <summary>状态推进只接受 Confirmed / Printed（Draft 由保存时确定）。</summary>
    private static bool TryNormalizeTargetStatus(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (!TryNormalizeStatus(input, out var status))
        {
            return false;
        }

        if (status is not (RecoveryConstants.STATUS_CONFIRMED or RecoveryConstants.STATUS_PRINTED))
        {
            return false;
        }

        normalized = status;
        return true;
    }

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND)
        {
            return NotFound(context, result.Message ?? "请求的资源不存在");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED)
        {
            return Invalid(context, result.Message ?? "请求参数校验失败");
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound, message);
}

#region 请求/响应契约

/// <summary>POST /api/recovery/records/{id}/status 请求体。</summary>
public sealed record RecoveryStatusRequest(string? Status);

/// <summary>追缴记录新增响应。</summary>
public sealed record RecoveryCreatedResponse(long Id);

/// <summary>追缴编号响应。</summary>
public sealed record RecoveryCodeResponse(string Code);

/// <summary>默认月保障额响应。</summary>
public sealed record RecoveryAmountResponse(decimal Amount);

#endregion
