using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.DutyManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 值班管理（Web）：月度值班表、轮转生成、成员/请假/班务调整、节假日、导入导出与打印。
/// <para>
/// 业务口径全部来自 Core <see cref="IDutyService"/> / <see cref="IHolidayManageService"/>；
/// 本端点只做权限、HTTP 契约映射与 Agent 打印任务装配（Server 不渲染 Office 文档）。
/// 读接口要求 <c>DUTY_VIEW</c>；写/生成/调整/导入/导出/打印要求 <c>DUTY_MANAGE</c>。
/// </para>
/// </summary>
public static class DutyEndpoints
{
    private const string Prefix = "/api/duty";
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxImportFileBytes = 20L * 1024 * 1024;

    private static readonly string[] ExcelExtensions = [".xlsx", ".xls"];

    public static IEndpointRouteBuilder MapDutyEndpoints(this IEndpointRouteBuilder app)
    {
        // ── 月度值班表 ──
        app.MapGet($"{Prefix}/months/{{year:int}}/{{month:int}}", GetMonthAsync);
        app.MapPost($"{Prefix}/months/{{year:int}}/{{month:int}}/generate", GenerateAsync);
        app.MapPost($"{Prefix}/months/{{year:int}}/{{month:int}}/clear", ClearAsync);
        app.MapPost($"{Prefix}/months/{{year:int}}/{{month:int}}/rebuild-downstream", RebuildDownstreamAsync);
        app.MapPost($"{Prefix}/months/{{year:int}}/{{month:int}}/fill-missing", FillMissingAsync);

        // ── 设置 ──
        app.MapGet($"{Prefix}/settings", GetSettingsAsync);
        app.MapPut($"{Prefix}/settings", SaveSettingsAsync);

        // ── 成员 ──
        app.MapGet($"{Prefix}/members", GetMembersAsync);
        app.MapPost($"{Prefix}/members", CreateMemberAsync);
        app.MapPut($"{Prefix}/members/{{id:long}}", UpdateMemberAsync);
        app.MapDelete($"{Prefix}/members/{{id:long}}", DeleteMemberAsync);
        app.MapPost($"{Prefix}/members/{{id:long}}/move", MoveMemberAsync);
        app.MapPost($"{Prefix}/members/{{id:long}}/toggle", ToggleMemberAsync);
        app.MapGet($"{Prefix}/members/import-template", DownloadImportTemplateAsync);
        app.MapPost($"{Prefix}/members/import/preview", PreviewImportAsync).DisableAntiforgery();
        app.MapPost($"{Prefix}/members/import", ImportAsync).DisableAntiforgery();
        app.MapGet($"{Prefix}/members/{{id:long}}/future-shifts", GetFutureShiftsAsync);

        // ── 请假 ──
        app.MapGet($"{Prefix}/leaves", GetLeavesAsync);
        app.MapPost($"{Prefix}/leaves", CreateLeaveAsync);
        app.MapDelete($"{Prefix}/leaves/{{id:long}}", DeleteLeaveAsync).DisableAntiforgery();
        app.MapGet($"{Prefix}/leaves/affected-months", GetLeaveAffectedMonthsAsync);

        // ── 班务调整 ──
        app.MapGet($"{Prefix}/adjustments", GetAdjustmentsAsync);
        app.MapPost($"{Prefix}/adjustments", CreateAdjustmentAsync);
        app.MapPut($"{Prefix}/adjustments/{{id:long}}", RescheduleAdjustmentAsync);
        app.MapDelete($"{Prefix}/adjustments/{{id:long}}", RevokeAdjustmentAsync);
        app.MapPost($"{Prefix}/adjustments/{{id:long}}/revoke", RevokeAdjustmentAsync);

        // ── 节假日 ──
        app.MapGet($"{Prefix}/holidays", GetHolidaysAsync);
        app.MapPost($"{Prefix}/holidays", CreateHolidayAsync);
        app.MapDelete($"{Prefix}/holidays/{{id:long}}", DeleteHolidayAsync);
        app.MapPost($"{Prefix}/holidays/download", DownloadHolidaysAsync);

        // ── 导出 / 打印（Agent TemplateBatch）──
        app.MapPost($"{Prefix}/export", ExportAsync);
        app.MapPost($"{Prefix}/print", PrintAsync);

        return app;
    }

    // ───────────────────────────── 月度值班表 ─────────────────────────────

    private static async Task<IResult> GetMonthAsync(
        int year,
        int month,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;

        var result = await duty.GetMonthScheduleAsync(year, month, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(ToMonthResponse(result.Value));
    }

    private static async Task<IResult> GenerateAsync(
        int year,
        int month,
        bool? force,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.GenerateMonthAsync(year, month, force == true, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        var summary = result.Value;
        return Results.Json(new DutyGenerationResponse(
            Generated: true,
            ShiftCount: summary.ScheduleCount,
            Warnings: summary.Warnings,
            HasDownstream: summary.DownstreamMonths.Count > 0 && summary.RotationChanged != false,
            HasPendingCrossMonth: summary.PendingChangeMonths.Count > 0));
    }

    private static async Task<IResult> ClearAsync(
        int year,
        int month,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.ClearMonthAsync(year, month, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DutyClearResponse(true));
    }

    private static async Task<IResult> RebuildDownstreamAsync(
        int year,
        int month,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.RebuildDownstreamAsync(year, month, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DutyRebuildResponse(result.Value));
    }

    private static async Task<IResult> FillMissingAsync(
        int year,
        int month,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var monthText = $"{year:D4}-{month:D2}";
        var result = await duty.GenerateMissingMonthsAsync([monthText], ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DutyFillMissingResponse(result.Value, monthText));
    }

    // ───────────────────────────── 设置 ─────────────────────────────

    private static async Task<IResult> GetSettingsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;

        var result = await duty.GetRestdayLeaderSamePairAsync(ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DutySettingsDto(result.Value));
    }

    private static async Task<IResult> SaveSettingsAsync(
        DutySettingsRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        if (request?.RestdayLeaderSamePair is null)
            return InvalidRequest(context, "restdayLeaderSamePair 不能为空");

        var result = await duty.SaveRestdayLeaderSamePairAsync(request.RestdayLeaderSamePair.Value, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DutySettingsDto(request.RestdayLeaderSamePair.Value));
    }

    // ───────────────────────────── 成员 ─────────────────────────────

    private static async Task<IResult> GetMembersAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;

        var result = await duty.GetMembersAsync(ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateMemberAsync(
        DutyMemberSave? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");

        request.Id = 0;
        var result = await duty.SaveMemberAsync(request, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true });
    }

    private static async Task<IResult> UpdateMemberAsync(
        long id,
        DutyMemberSave? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");
        if (id <= 0)
            return InvalidRequest(context, "成员 ID 无效");

        request.Id = id;
        var result = await duty.SaveMemberAsync(request, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true });
    }

    private static async Task<IResult> DeleteMemberAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.DeleteMemberAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> MoveMemberAsync(
        long id,
        string? direction,
        string? group,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var dir = direction?.Trim().ToLowerInvariant() switch
        {
            "up" or "-1" => -1,
            "down" or "1" => 1,
            _ => (int?)null
        };
        if (dir is null)
            return InvalidRequest(context, "direction 只能是 up 或 down");
        if (string.IsNullOrWhiteSpace(group) || !DutyConstants.Groups.IsValid(group.Trim()))
            return InvalidRequest(context, "group 必须为 LEADER / MIDDLE / MALE / FEMALE");

        var result = await duty.MoveMemberAsync(id, group.Trim(), dir.Value, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true });
    }

    private static async Task<IResult> ToggleMemberAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.ToggleMemberActiveAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DutyToggleResponse(result.Value));
    }

    private static async Task<IResult> DownloadImportTemplateAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.BuildMemberImportTemplateAsync(ct);
        if (result.IsFailure || result.Value is null)
            return MapResult(context, result);

        return Results.File(
            result.Value,
            ExcelContentType,
            DutyConstants.TemplateSettings.MemberImportTemplateFileName);
    }

    private static async Task<IResult> PreviewImportAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var (file, error) = await ReadFormFileAsync(context, ct);
        if (file is null)
            return error!;

        var (path, directory) = await SaveTempUploadAsync(file, ct);
        try
        {
            var result = await duty.PreviewMemberImportAsync(path, ct);
            return result.IsFailure
                ? MapResult(context, result)
                : Results.Json(result.Value);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static async Task<IResult> ImportAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var (file, error) = await ReadFormFileAsync(context, ct);
        if (file is null)
            return error!;

        var (path, directory) = await SaveTempUploadAsync(file, ct);
        try
        {
            var result = await duty.ImportMembersAsync(path, ct);
            return result.IsFailure
                ? MapResult(context, result)
                : Results.Json(result.Value);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    // ───────────────────────────── 请假 ─────────────────────────────

    private static async Task<IResult> GetLeavesAsync(
        long? memberId,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;

        var result = await duty.GetLeavesAsync(memberId, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateLeaveAsync(
        DutyLeaveSave? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");

        var result = await duty.SaveLeaveAsync(request, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true, deletedMonths = result.Value });
    }

    private static async Task<IResult> DeleteLeaveAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.DeleteLeaveAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true, deletedMonths = result.Value });
    }

    private static async Task<IResult> GetLeaveAffectedMonthsAsync(
        long? memberId,
        DateTime? start,
        DateTime? end,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;
        if (start is null)
            return InvalidRequest(context, "start 不能为空");
        if (end.HasValue && end.Value.Date < start.Value.Date)
            return InvalidRequest(context, "end 不能早于 start");

        // 删除范围口径与成员/结束日无关：请假开始月 → 最后已生成月（memberId/end 仅作前端上下文）
        var result = await duty.GetLeaveAffectedMonthsAsync(start.Value.Date, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DutyLeaveAffectedMonthsResponse(result.Value));
    }

    // ───────────────────────────── 班务调整 ─────────────────────────────

    private static async Task<IResult> GetAdjustmentsAsync(
        string? changeType,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;

        var normalized = string.IsNullOrWhiteSpace(changeType) ? null : changeType.Trim().ToUpperInvariant();
        if (normalized is not null && !DutyConstants.ChangeTypes.IsValid(normalized))
            return InvalidRequest(context, "changeType 只能是 SWAP 或 SUBSTITUTE");

        var result = await duty.GetShiftChangesAsync(normalized, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateAdjustmentAsync(
        DutyShiftChangeSave? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");

        var result = await duty.SaveShiftChangeAsync(request, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true });
    }

    private static async Task<IResult> RescheduleAdjustmentAsync(
        long id,
        DutyRescheduleRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (request?.NewDate is null)
            return InvalidRequest(context, "newDate 不能为空");

        var result = await duty.RescheduleShiftChangeAsync(id, request.NewDate.Value.Date, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true });
    }

    private static async Task<IResult> RevokeAdjustmentAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await duty.CancelShiftChangeAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { success = true });
    }

    private static async Task<IResult> GetFutureShiftsAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;

        var result = await duty.GetMemberFutureSchedulesAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 节假日 ─────────────────────────────

    private static async Task<IResult> GetHolidaysAsync(
        int? year,
        HttpContext context,
        INewPermissionService permissions,
        IHolidayManageService holidays,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_VIEW, ct) is { } denied)
            return denied;

        var result = await holidays.GetHolidaysAsync(year ?? DateTime.Today.Year, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateHolidayAsync(
        DutyHolidayRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IHolidayManageService holidays,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (request?.Date is null)
            return InvalidRequest(context, "date 不能为空");
        if (string.IsNullOrWhiteSpace(request.Name))
            return InvalidRequest(context, "节日名称不能为空");

        var dateType = string.IsNullOrWhiteSpace(request.DateType)
            ? DutyConstants.HolidayRowTypes.HOLIDAY
            : request.DateType.Trim().ToUpperInvariant();

        var result = await holidays.UpsertHolidayAsync(
            request.Date.Value.Date, dateType, request.Name.Trim(), DutyConstants.Sources.MANUAL, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        var refresh = await holidays.RefreshCacheAsync(ct);
        if (refresh.IsFailure)
            logger.Warn($"[DutyEndpoints] 节假日快照刷新失败（数据已入库）: {refresh.Message}");

        return Results.Json(new { success = true });
    }

    private static async Task<IResult> DeleteHolidayAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IHolidayManageService holidays,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await holidays.DeleteHolidayAsync(id, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        var refresh = await holidays.RefreshCacheAsync(ct);
        if (refresh.IsFailure)
            logger.Warn($"[DutyEndpoints] 节假日快照刷新失败（数据已删除）: {refresh.Message}");

        return Results.NoContent();
    }

    private static async Task<IResult> DownloadHolidaysAsync(
        int? year,
        HttpContext context,
        INewPermissionService permissions,
        IHolidayManageService holidays,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;

        var result = await holidays.DownloadYearAsync(year ?? DateTime.Today.Year, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 导出 / 打印 ─────────────────────────────

    private static async Task<IResult> ExportAsync(
        DutyExportRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        IDutyPrintFieldBuilder fieldBuilder,
        IDatabaseService database,
        ArchivePrintQueueService queue,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");

        var startYear = request.StartYear ?? 0;
        var startMonth = request.StartMonth ?? 0;
        var endYear = request.EndYear ?? 0;
        var endMonth = request.EndMonth ?? 0;

        if (startYear is < 2000 or > 2100 || endYear is < 2000 or > 2100
            || startMonth is < 1 or > 12 || endMonth is < 1 or > 12)
        {
            return InvalidRequest(context, "年月无效");
        }

        var startValue = startYear * 12 + startMonth;
        var endValue = endYear * 12 + endMonth;
        if (startValue > endValue)
            return InvalidRequest(context, "起始月份不能晚于结束月份");
        if (endValue - startValue + 1 > 36)
            return InvalidRequest(context, "连续导出最多 36 个月");

        var templateResult = await LoadTemplateAsync(database, ct);
        if (templateResult.IsFailure)
            return MapResult(context, templateResult);
        var template = templateResult.Value;

        var targetResult = await printers.ResolveTargetAsync(user.Id, null, null, ct);
        if (targetResult.IsFailure)
            return MapResult(context, targetResult);
        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0 || string.IsNullOrWhiteSpace(target.PrinterName))
            return InvalidRequest(context, "当前账号未绑定打印机，请先在「我的打印机」中生成绑定码并完成绑定");

        var batch = new PrintBatchDefinition
        {
            Mode = "save",
            Category = OutputCategories.DutySchedule,
            Period = $"{startYear:D4}{startMonth:D2}-{endYear:D4}{endMonth:D2}",
        };

        var (curYear, curMonth) = (startYear, startMonth);
        while (curYear * 12 + curMonth <= endValue)
        {
            ct.ThrowIfCancellationRequested();

            var monthResult = await duty.GetMonthScheduleAsync(curYear, curMonth, ct);
            if (monthResult.IsFailure || monthResult.Value is null)
            {
                return MapResult(context, Result.Failure(
                    monthResult.ErrorCode!,
                    $"已生成 {batch.Parts.Count} 个月，{curYear} 年 {curMonth} 月失败：{monthResult.Message}"));
            }

            batch.Parts.Add(new PrintBatchPart
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                FileName = string.Format(
                    DutyConstants.TemplateSettings.ExportFileNameFormat, curYear, curMonth),
                Fields = fieldBuilder.Build(monthResult.Value),
            });

            (curYear, curMonth) = curMonth == 12 ? (curYear + 1, 1) : (curYear, curMonth + 1);
        }

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.Duty,
            BusinessId = startYear * 100L + startMonth,
            Classification = OutputCategories.DutySchedule,
            TemplateId = template.Id,
            TemplateName = template.Name,
            ApplicantName = $"政务值班表 {startYear}年{startMonth}月-{endYear}年{endMonth}月",
            ApplicantIdCard = "-",
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = 1,
            IsDuplex = false,
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch,
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
            return MapResult(context, enqueueResult);

        var job = enqueueResult.Value;
        return Results.Json(new DutyJobResponse(
            job.Id, job.JobNo, job.Status, job.JobKind, "save",
            template.Id, template.Name, batch.Parts.Count));
    }

    private static async Task<IResult> PrintAsync(
        DutyPrintRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDutyService duty,
        IDutyPrintFieldBuilder fieldBuilder,
        IDatabaseService database,
        ArchivePrintQueueService queue,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.DUTY_MANAGE, ct) is { } denied)
            return denied;
        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");

        var year = request.Year ?? 0;
        var month = request.Month ?? 0;
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            return InvalidRequest(context, "年月无效");

        var monthResult = await duty.GetMonthScheduleAsync(year, month, ct);
        if (monthResult.IsFailure || monthResult.Value is null)
            return MapResult(context, monthResult);

        var templateResult = await LoadTemplateAsync(database, ct);
        if (templateResult.IsFailure)
            return MapResult(context, templateResult);
        var template = templateResult.Value;

        var targetResult = await printers.ResolveTargetAsync(
            user.Id, request.PrinterBindingId, request.PrinterName, ct);
        if (targetResult.IsFailure)
            return MapResult(context, targetResult);
        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0 || string.IsNullOrWhiteSpace(target.PrinterName))
            return InvalidRequest(context, "当前账号未绑定打印机，请先在「我的打印机」中生成绑定码并完成绑定");

        var batch = new PrintBatchDefinition
        {
            Mode = "print",
            Category = OutputCategories.DutySchedule,
            Period = $"{year:D4}{month:D2}",
            Parts =
            [
                new PrintBatchPart
                {
                    TemplateId = template.Id,
                    TemplateName = template.Name,
                    FileName = string.Format(DutyConstants.TemplateSettings.ExportFileNameFormat, year, month),
                    Fields = fieldBuilder.Build(monthResult.Value),
                }
            ]
        };

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.Duty,
            BusinessId = year * 100L + month,
            Classification = OutputCategories.DutySchedule,
            TemplateId = template.Id,
            TemplateName = template.Name,
            ApplicantName = $"政务值班表 {year}年{month}月",
            ApplicantIdCard = "-",
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex ?? false,
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch,
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
            return MapResult(context, enqueueResult);

        var job = enqueueResult.Value;
        return Results.Json(new DutyJobResponse(
            job.Id, job.JobNo, job.Status, job.JobKind, "print",
            template.Id, template.Name, 1));
    }

    // ───────────────────────────── 辅助 ─────────────────────────────

    private static DutyMonthResponse ToMonthResponse(DutyMonthSchedule schedule)
    {
        var days = schedule.Days
            .Select(row => new DutyDayDto(
                Date: row.DutyDate.ToString("yyyy-MM-dd"),
                Week: row.WeekLabel,
                DateType: row.DateType,
                Leader: row.LeaderCell.MemberName,
                Middle: row.MiddleCell.MemberName,
                Male: row.MaleCell.MemberName,
                Female: row.FemaleCell.MemberName,
                IsAdjusted: row.Cells.Any(cell => cell.IsAdjusted)))
            .ToList();

        return new DutyMonthResponse(
            schedule.Year,
            schedule.Month,
            schedule.IsGenerated,
            schedule.ScheduleCount,
            days,
            new DutySettingsDto(schedule.RestdayLeaderSamePair));
    }

    private static async Task<Result<DutyTemplateRow>> LoadTemplateAsync(
        IDatabaseService database, CancellationToken ct)
    {
        var result = await database.QuerySingleAsync<DutyTemplateRow?>(
            @"SELECT id, name, file_type
              FROM nc_biz_templates
              WHERE name = $1
              ORDER BY id
              LIMIT 1",
            ct, DutyConstants.TemplateSettings.GovDutyTemplateName);

        if (result.IsFailure)
            return Result.Failure<DutyTemplateRow>(result.ErrorCode!, result.Message!);

        if (result.Value is not { Id: > 0 } template)
        {
            return Result.Failure<DutyTemplateRow>(
                ErrorCodes.TEMPLATE_NOT_FOUND,
                $"模板不存在：{DutyConstants.TemplateSettings.GovDutyTemplateName}（请到「模板管理」上传并确认名称）");
        }

        try
        {
            if (!TemplateFileTypes.IsExcel(template.FileType ?? string.Empty))
            {
                return Result.Failure<DutyTemplateRow>(
                    ErrorCodes.TEMPLATE_NOT_FOUND,
                    $"模板类型不支持：{template.Name}（{template.FileType ?? "-"}，需要 xlsx）");
            }
        }
        catch (NotSupportedException)
        {
            return Result.Failure<DutyTemplateRow>(
                ErrorCodes.TEMPLATE_NOT_FOUND,
                $"模板类型不支持：{template.Name}（{template.FileType ?? "-"}，需要 xlsx）");
        }

        return Result.Success(template);
    }

    private static async Task<IResult?> DenyAsync(
        HttpContext context,
        INewPermissionService permissions,
        string permissionCode,
        CancellationToken ct)
        => await EndpointSecurity.RequirePermissionAsync(context, permissions, permissionCode, ct);

    private static async Task<(IFormFile? File, IResult? Error)> ReadFormFileAsync(
        HttpContext context, CancellationToken ct)
    {
        if (!context.Request.HasFormContentType)
            return (null, InvalidRequest(context, "请使用 multipart/form-data 上传 Excel 文件"));

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(ct);
        }
        catch (InvalidDataException)
        {
            return (null, InvalidRequest(context, "上传的表单数据无效，请重新选择文件"));
        }

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null)
            return (null, InvalidRequest(context, "请选择要上传的 Excel 文件（字段名 file）"));

        var name = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(name)
            || !ExcelExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            return (null, InvalidRequest(context, "仅支持 .xlsx / .xls 文件"));
        }
        if (file.Length <= 0)
            return (null, InvalidRequest(context, "上传文件为空，请重新选择"));
        if (file.Length > MaxImportFileBytes)
            return (null, InvalidRequest(context, "上传文件不能超过 20 MB"));

        return (file, null);
    }

    private static async Task<(string Path, string Directory)> SaveTempUploadAsync(
        IFormFile file, CancellationToken ct)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "nc-duty-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var safeName = Path.GetFileName(file.FileName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalid, '_');

        var path = Path.Combine(directory, string.IsNullOrWhiteSpace(safeName) ? "duty-import.xlsx" : safeName);
        await using (var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
        {
            await file.CopyToAsync(stream, ct);
        }
        return (path, directory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时文件清理失败不影响请求结果
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;

    private static IResult MapResult(HttpContext context, Result result)
    {
        var code = result.ErrorCode ?? string.Empty;
        var message = string.IsNullOrWhiteSpace(result.Message) ? "操作失败" : result.Message;

        if (code is ErrorCodes.NOT_FOUND or ErrorCodes.RECORD_NOT_FOUND or ErrorCodes.TEMPLATE_NOT_FOUND
            or ErrorCodes.DUTY_MEMBER_NOT_FOUND or ErrorCodes.DUTY_SCHEDULE_NOT_FOUND
            or ErrorCodes.DUTY_SHIFT_NOT_FOUND or ErrorCodes.DUTY_CHANGE_NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, message);
        }

        if (code is ErrorCodes.DUTY_SCHEDULE_EXISTS or ErrorCodes.DUTY_MEMBER_CONFLICT
            or ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED or ErrorCodes.HOLIDAY_DUPLICATE_DATE)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict, ApiErrorCodes.ValidationError, message);
        }

        if (code is ErrorCodes.HOLIDAY_API_UNREACHABLE)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.ValidationError, message);
        }

        if (code is ErrorCodes.VALIDATION_FAILED or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.FILE_NOT_FOUND or ErrorCodes.FILE_FORMAT_ERROR
            or ErrorCodes.DUTY_NO_MEMBERS or ErrorCodes.DUTY_INVALID_DATE
            or ErrorCodes.DUTY_LEAVE_INVALID_RANGE or ErrorCodes.DUTY_LEAVE_BACKFILL_REASON_REQUIRED
            or ErrorCodes.DUTY_SHIFT_INVALID_DATE or ErrorCodes.DUTY_SHIFT_MEMBER_INVALID
            or ErrorCodes.HOLIDAY_YEAR_NOT_PUBLISHED or ErrorCodes.HOLIDAY_DATA_INVALID)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, message);
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");

    private sealed class DutyTemplateRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? FileType { get; set; }
    }
}
