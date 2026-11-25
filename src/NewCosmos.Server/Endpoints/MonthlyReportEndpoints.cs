using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 低收入人口救助帮扶 · 救助月报表。
/// <para>
/// 权限：<see cref="PermissionCodes.REPORT_VIEW"/>（查看/生成/删除/导出/会议记录同权限，按旧版）。
/// 业务口径全部复用 <see cref="IMonthlyReportService"/>，本文件只做参数校验、DTO 映射与文件下载。
/// 打印/Office 模板渲染不在 Server（无 COM/PC Agent 管线），Web 端仅提供数据预览与导出。
/// </para>
/// </summary>
public static class MonthlyReportEndpoints
{
    private const string BasePath = "/api/social-assistance/monthly-report";

    private static readonly string[] AllowedCategories =
    [
        MonthlyReportCategoryConstants.Subsistence,
        MonthlyReportCategoryConstants.LowIncomeEdge,
        MonthlyReportCategoryConstants.Destitute,
        MonthlyReportCategoryConstants.RigidExpenditure,
        MonthlyReportCategoryConstants.OtherDifficulty,
    ];

    /// <summary>rows 接口可按需裁剪数据集，避免为「全部」请求触发分类施保增发快照落库等重查询。</summary>
    private static readonly string[] AllowedDatasets =
    [
        "added",
        "stopped",
        "increase",
        "decrease",
        "shiBaoReduction",
        "classifiedAdds",
        "death",
        "exitRectification",
        "tempRelief",
    ];

    public static IEndpointRouteBuilder MapMonthlyReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{BasePath}/options", GetOptionsAsync);
        app.MapGet($"{BasePath}/summary", GetSummaryAsync);
        app.MapGet($"{BasePath}/history", GetHistoryAsync);
        app.MapGet($"{BasePath}/rows", GetRowsAsync);
        app.MapPost($"{BasePath}/generate", GenerateAsync);
        app.MapDelete($"{BasePath}/{{year:int}}/{{month:int}}", DeleteAsync);
        app.MapGet($"{BasePath}/meeting", GetMeetingAsync);
        app.MapPost($"{BasePath}/meeting", SaveMeetingAsync);
        app.MapGet($"{BasePath}/meeting-attendance", GetMeetingAttendanceAsync);
        app.MapGet($"{BasePath}/export", ExportAsync);
        return app;
    }

    // ─────────────────────────── 选项 / 汇总 / 历史 ───────────────────────────

    private static async Task<IResult> GetOptionsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var now = DateTime.Now;
        var cycleResult = await reports.GetCycleRangeAsync(now.Year, now.Month, ct);
        if (cycleResult.IsFailure)
        {
            return ApiErrors.FromResult(context, cycleResult);
        }

        var townsResult = await reports.GetTownOptionsAsync(ct);
        if (townsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, townsResult);
        }

        var years = Enumerable.Range(now.Year - 5, 7).ToArray();
        return Results.Json(new MonthlyReportOptionsResponse(
            years,
            Enumerable.Range(1, 12).ToArray(),
            townsResult.Value ?? new List<string>(),
            now.Year,
            now.Month,
            ToCycleDto(cycleResult.Value)));
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct,
        int? year,
        int? month,
        string? town)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TryResolvePeriod(year, month, out var safeYear, out var safeMonth, out var invalid))
        {
            return InvalidRequest(context, invalid!);
        }

        var townFilter = NormalizeTown(town);

        var cycleResult = await reports.GetCycleRangeAsync(safeYear, safeMonth, ct);
        if (cycleResult.IsFailure)
        {
            return ApiErrors.FromResult(context, cycleResult);
        }

        var added = await reports.GetAddedRowsAsync(safeYear, safeMonth, townFilter, null, ct);
        if (added.IsFailure) return ApiErrors.FromResult(context, added);

        var stopped = await reports.GetStoppedRowsAsync(safeYear, safeMonth, townFilter, null, ct);
        if (stopped.IsFailure) return ApiErrors.FromResult(context, stopped);

        var increase = await reports.GetIncreaseRowsAsync(safeYear, safeMonth, townFilter, null, ct);
        if (increase.IsFailure) return ApiErrors.FromResult(context, increase);

        var decrease = await reports.GetDecreaseRowsAsync(safeYear, safeMonth, townFilter, null, ct);
        if (decrease.IsFailure) return ApiErrors.FromResult(context, decrease);

        var death = await reports.GetDeathRowsAsync(safeYear, safeMonth, townFilter, null, ct);
        if (death.IsFailure) return ApiErrors.FromResult(context, death);

        // 在保/月保障金为全县口径（旧版同样调用无乡镇参数的 GetStatisticsAsync）。
        var stats = await reports.GetStatisticsAsync(safeYear, safeMonth, ct);
        if (stats.IsFailure) return ApiErrors.FromResult(context, stats);

        var generated = await reports.GetAsync(safeYear, safeMonth, ct);
        if (generated.IsFailure) return ApiErrors.FromResult(context, generated);

        return Results.Json(new MonthlyReportSummaryResponse(
            safeYear,
            safeMonth,
            string.IsNullOrEmpty(townFilter) ? "全部" : townFilter,
            ToCycleDto(cycleResult.Value),
            added.Value?.Count ?? 0,
            stopped.Value?.Count ?? 0,
            increase.Value?.Count ?? 0,
            decrease.Value?.Count ?? 0,
            death.Value?.Count ?? 0,
            stats.Value.TotalArchives,
            stats.Value.TotalAmount,
            generated.Value is null ? null : ToHistoryDto(generated.Value)));
    }

    private static async Task<IResult> GetHistoryAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct,
        int? year)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        var result = await reports.GetReportHistoryAsync(year, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json((result.Value ?? new List<MonthlyReportSummary>()).Select(ToHistoryDto).ToList());
    }

    // ─────────────────────────── 分表数据 ───────────────────────────

    private static async Task<IResult> GetRowsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct,
        int? year,
        int? month,
        string? town,
        string? category,
        string? datasets)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TryResolvePeriod(year, month, out var safeYear, out var safeMonth, out var invalid))
        {
            return InvalidRequest(context, invalid!);
        }

        var normalizedCategory = NormalizeCategory(category);
        if (normalizedCategory is null)
        {
            return InvalidRequest(context, "分类参数无效");
        }

        if (!TryParseDatasets(datasets, out var wanted))
        {
            return InvalidRequest(context, "datasets 参数含未知数据集");
        }

        bool Wants(string name) => wanted is null || wanted.Contains(name);

        var townFilter = NormalizeTown(town);
        var isAll = normalizedCategory.Length == 0;
        var isSubsistence = normalizedCategory == MonthlyReportCategoryConstants.Subsistence;
        var isOtherDifficulty = normalizedCategory == MonthlyReportCategoryConstants.OtherDifficulty;

        // 新增：四类认定分类（其他困难人员无新增表）
        IReadOnlyList<MonthlyAddedRowDto> added = [];
        if (!isOtherDifficulty && Wants("added"))
        {
            var result = await reports.GetAddedRowsAsync(safeYear, safeMonth, townFilter, normalizedCategory, ct);
            if (result.IsFailure) return ApiErrors.FromResult(context, result);
            added = (result.Value ?? new List<MonthlyAddedRow>()).Select(ToAddedDto).ToList();
        }

        // 停保：含「其他困难人员」独立停止表（服务内分流）
        IReadOnlyList<MonthlyStoppedRowDto> stopped = [];
        if (Wants("stopped"))
        {
            var result = await reports.GetStoppedRowsAsync(safeYear, safeMonth, townFilter, normalizedCategory, ct);
            if (result.IsFailure) return ApiErrors.FromResult(context, result);
            stopped = (result.Value ?? new List<MonthlyStoppedRow>()).Select(ToStoppedDto).ToList();
        }

        // 保障金增发/减发：仅最低生活保障（旧版表单口径）
        IReadOnlyList<MonthlyAmountChangeRowDto> increase = [];
        IReadOnlyList<MonthlyAmountChangeRowDto> decrease = [];
        if ((isAll || isSubsistence) && Wants("increase"))
        {
            var inc = await reports.GetIncreaseRowsAsync(safeYear, safeMonth, townFilter, normalizedCategory, ct);
            if (inc.IsFailure) return ApiErrors.FromResult(context, inc);
            increase = (inc.Value ?? new List<MonthlyAmountChangeRow>()).Select(ToAmountChangeDto).ToList();
        }

        if ((isAll || isSubsistence) && Wants("decrease"))
        {
            var dec = await reports.GetDecreaseRowsAsync(safeYear, safeMonth, townFilter, normalizedCategory, ct);
            if (dec.IsFailure) return ApiErrors.FromResult(context, dec);
            decrease = (dec.Value ?? new List<MonthlyAmountChangeRow>()).Select(ToAmountChangeDto).ToList();
        }

        // 分类施保减发/增发：仅最低生活保障口径（服务无分类/乡镇参数，避免重复请求）
        IReadOnlyList<MonthlyShiBaoRowDto> shiBao = [];
        IReadOnlyList<MonthlyClassifiedAddRowDto> classifiedAdds = [];
        if ((isAll || isSubsistence) && Wants("shiBaoReduction"))
        {
            var shiBaoResult = await reports.GetShiBaoReductionRowsAsync(safeYear, safeMonth, ct);
            if (shiBaoResult.IsFailure) return ApiErrors.FromResult(context, shiBaoResult);
            shiBao = (shiBaoResult.Value ?? new List<MonthlyShiBaoRow>()).Select(ToShiBaoDto).ToList();
        }

        if ((isAll || isSubsistence) && Wants("classifiedAdds"))
        {
            // 该调用首次会实时计算并落库快照；只有勾选分类施保增发表时才应触发。
            var addResult = await reports.GetClassifiedSubsidyAddRowsAsync(safeYear, safeMonth, ct);
            if (addResult.IsFailure) return ApiErrors.FromResult(context, addResult);
            classifiedAdds = (addResult.Value ?? new List<MonthlyClassifiedAddRow>()).Select(ToClassifiedAddDto).ToList();
        }

        // 自然减员 / 退出对象纠治：按四类拆分（其他困难人员无此两表）
        IReadOnlyList<MonthlyDeathRowDto> death = [];
        IReadOnlyList<MonthlyExitRectificationRowDto> exitRectification = [];
        if (!isOtherDifficulty && Wants("death"))
        {
            var deathResult = await reports.GetDeathRowsAsync(safeYear, safeMonth, townFilter, normalizedCategory, ct);
            if (deathResult.IsFailure) return ApiErrors.FromResult(context, deathResult);
            death = (deathResult.Value ?? new List<MonthlyDeathRow>()).Select(ToDeathDto).ToList();
        }

        if (!isOtherDifficulty && Wants("exitRectification"))
        {
            var exitResult = await reports.GetExitRectificationRowsAsync(safeYear, safeMonth, townFilter, normalizedCategory, ct);
            if (exitResult.IsFailure) return ApiErrors.FromResult(context, exitResult);
            exitRectification = (exitResult.Value ?? new List<MonthlyExitRectificationRow>()).Select(ToExitDto).ToList();
        }

        // 临时救助：独立表单，无分类拆分；仅在「全部」请求时返回，避免逐分类重复
        IReadOnlyList<MonthlyTempReliefRowDto> tempRelief = [];
        if (isAll && Wants("tempRelief"))
        {
            var tempResult = await reports.GetTempReliefSummaryRowsAsync(safeYear, safeMonth, townFilter, ct);
            if (tempResult.IsFailure) return ApiErrors.FromResult(context, tempResult);
            tempRelief = (tempResult.Value ?? new List<MonthlyTempReliefRow>()).Select(ToTempReliefDto).ToList();
        }

        return Results.Json(new MonthlyReportRowsResponse(
            safeYear,
            safeMonth,
            string.IsNullOrEmpty(townFilter) ? "全部" : townFilter,
            normalizedCategory,
            added,
            stopped,
            increase,
            decrease,
            shiBao,
            classifiedAdds,
            death,
            exitRectification,
            tempRelief));
    }

    // ─────────────────────────── 生成 / 删除 / 导出 ───────────────────────────

    private static async Task<IResult> GenerateAsync(
        MonthlyReportGenerateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        if (request.Year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        if (request.Month is < 1 or > 12)
        {
            return InvalidRequest(context, "月份参数应为 1-12");
        }

        var existing = await reports.GetAsync(request.Year, request.Month, ct);
        if (existing.IsFailure)
        {
            return ApiErrors.FromResult(context, existing);
        }

        if (existing.Value is not null && request.Overwrite != true)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.ValidationError,
                $"{request.Year}年{request.Month}月已生成月报，如需重新统计请使用「覆盖生成」");
        }

        var result = await reports.GenerateAsync(request.Year, request.Month, NormalizeTown(request.Town), ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        // GenerateAsync 返回的内存对象不含 DB 生成的 generated_at，重新读取一次保证响应字段真实。
        var stored = await reports.GetAsync(request.Year, request.Month, ct);
        var report = stored.IsSuccess && stored.Value is not null ? stored.Value : result.Value;
        return Results.Json(new MonthlyReportGenerateResponse(
            report.Id,
            report.Year,
            report.Month,
            string.IsNullOrEmpty(report.Town) ? "全部" : report.Town,
            report.Status,
            report.TotalArchives,
            report.NewArchives,
            report.StoppedArchives,
            report.TotalAmount,
            report.GeneratedAt));
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct,
        int year,
        int month)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        if (month is < 1 or > 12)
        {
            return InvalidRequest(context, "月份参数应为 1-12");
        }

        var result = await reports.DeleteAsync(year, month, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new { deleted = result.Value });
    }

    private static async Task<IResult> ExportAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct,
        int? year,
        int? month,
        string? format)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TryResolvePeriod(year, month, out var safeYear, out var safeMonth, out var invalid))
        {
            return InvalidRequest(context, invalid!);
        }

        var normalizedFormat = string.IsNullOrWhiteSpace(format) ? "Excel" : format.Trim();
        var isExcel = normalizedFormat.Equals("Excel", StringComparison.OrdinalIgnoreCase);
        var isCsv = normalizedFormat.Equals("Csv", StringComparison.OrdinalIgnoreCase)
            || normalizedFormat.Equals("CSV", StringComparison.OrdinalIgnoreCase);
        if (!isExcel && !isCsv)
        {
            return InvalidRequest(context, "format 只能是 Excel 或 Csv");
        }

        var result = await reports.ExportAsync(safeYear, safeMonth, isExcel ? "Excel" : "Csv", ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var fileName = $"救助月报表_{safeYear:D4}-{safeMonth:D2}.{(isExcel ? "xlsx" : "csv")}";
        var contentType = isExcel
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/csv; charset=utf-8";

        return Results.File(result.Value, contentType, fileName);
    }

    // ─────────────────────────── 会议记录 ───────────────────────────

    private static async Task<IResult> GetMeetingAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct,
        int? year,
        int? month,
        string? town,
        bool? specialApproval)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TryResolvePeriod(year, month, out var safeYear, out var safeMonth, out var invalid))
        {
            return InvalidRequest(context, invalid!);
        }

        // 会议记录按「全部」/乡镇唯一；服务内会把空值归一为「全部」并回填默认值。
        var result = await reports.GetMeetingAsync(
            safeYear, safeMonth, NormalizeTown(town), specialApproval == true, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(ToMeetingDto(result.Value));
    }

    private static async Task<IResult> SaveMeetingAsync(
        MonthlyMeetingSaveRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        if (request.Year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        if (request.Month is < 1 or > 12)
        {
            return InvalidRequest(context, "月份参数应为 1-12");
        }

        var user = context.GetCurrentUser();
        var createdBy = user is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName!;
        var town = NormalizeTown(request.Town);

        var meeting = new MonthlyMeeting
        {
            Year = request.Year,
            Month = request.Month,
            Town = string.IsNullOrEmpty(town) ? "全部" : town,
            MeetingTime = request.MeetingTime?.Trim() ?? string.Empty,
            Host = request.Host?.Trim() ?? string.Empty,
            Recorder = request.Recorder?.Trim() ?? string.Empty,
            Attendees = NormalizeLineBreaks(request.Attendees),
            Absentees = NormalizeLineBreaks(request.Absentees),
            ApplyCategory = request.ApplyCategory?.Trim() ?? string.Empty,
            CreatedBy = createdBy,
        };

        var saveResult = await reports.SaveMeetingAsync(meeting, ct);
        if (saveResult.IsFailure)
        {
            return ApiErrors.FromResult(context, saveResult);
        }

        var historyResult = await reports.SaveMeetingAttendanceHistoryAsync(
            request.Year, request.Month, town, meeting.Attendees, meeting.Absentees, createdBy, ct);
        if (historyResult.IsFailure)
        {
            var code = string.IsNullOrEmpty(historyResult.ErrorCode)
                ? ApiErrorCodes.InternalError
                : historyResult.ErrorCode;
            return ApiErrors.Write(
                context,
                StatusCodes.Status500InternalServerError,
                code,
                $"会议记录已保存，但出席/缺席历史保存失败：{historyResult.Message}");
        }

        var saved = await reports.GetMeetingAsync(request.Year, request.Month, town, false, ct);
        return Results.Json(ToMeetingDto(saved.IsSuccess && saved.Value is not null ? saved.Value : saveResult.Value));
    }

    private static async Task<IResult> GetMeetingAttendanceAsync(
        HttpContext context,
        INewPermissionService permissions,
        IMonthlyReportService reports,
        CancellationToken ct,
        int? year,
        int? month,
        string? town)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.REPORT_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TryResolvePeriod(year, month, out var safeYear, out var safeMonth, out var invalid))
        {
            return InvalidRequest(context, invalid!);
        }

        var result = await reports.GetLatestMeetingAttendanceAsync(
            safeYear, safeMonth, NormalizeTown(town), ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var attendance = result.Value;
        return Results.Json(new MonthlyMeetingAttendanceResponse(
            attendance.Id,
            attendance.Year == 0 ? safeYear : attendance.Year,
            attendance.Month == 0 ? safeMonth : attendance.Month,
            string.IsNullOrEmpty(attendance.Town) ? "全部" : attendance.Town,
            attendance.Attendees,
            attendance.Absentees,
            attendance.CreatedBy,
            attendance.CreatedAt));
    }

    // ─────────────────────────── 映射 / 校验 ───────────────────────────

    private static MonthlyCycleDto ToCycleDto(MonthlyCycleRange cycle)
        => new(
            cycle.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cycle.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cycle.Display);

    private static MonthlyReportHistoryItemDto ToHistoryDto(MonthlyReportSummary row)
        => new(
            row.Id,
            row.Year,
            row.Month,
            string.IsNullOrEmpty(row.Town) ? "全部" : row.Town,
            row.TotalArchives,
            row.NewArchives,
            row.StoppedArchives,
            row.TotalAmount,
            row.Status,
            row.GeneratedAt,
            row.GeneratedBy);

    private static MonthlyReportHistoryItemDto ToHistoryDto(MonthlyReport row)
        => new(
            row.Id,
            row.Year,
            row.Month,
            string.IsNullOrEmpty(row.Town) ? "全部" : row.Town,
            row.TotalArchives,
            row.NewArchives,
            row.StoppedArchives,
            row.TotalAmount,
            row.Status,
            row.GeneratedAt,
            row.GeneratedBy);

    private static MonthlyAddedRowDto ToAddedDto(MonthlyAddedRow row)
        => new(
            row.ApplicationId,
            row.AuditDate,
            row.Name,
            row.IdCard,
            row.Gender,
            row.Age,
            row.Address,
            row.Town,
            row.Community,
            row.FamilySize,
            row.Classification,
            row.CategoryCode,
            row.IsRural,
            row.ReasonDetail,
            row.MaritalStatus,
            row.HealthStatus,
            row.BankAccount,
            row.AnnualIncome,
            row.Relation);

    private static MonthlyStoppedRowDto ToStoppedDto(MonthlyStoppedRow row)
        => new(
            row.HeadName,
            row.HeadIdCard,
            row.HeadBirth,
            row.FamilySize,
            row.Address,
            row.Category,
            row.Classification,
            row.MonthAmount,
            row.StopReason,
            row.StopDate,
            row.EnjoyDate,
            row.CategoryCode,
            row.BizType,
            row.IsRural);

    private static MonthlyAmountChangeRowDto ToAmountChangeDto(MonthlyAmountChangeRow row)
        => new(
            row.HeadName,
            row.HeadBirth,
            row.FamilySize,
            row.Address,
            row.Category,
            row.EnjoyDate,
            row.OldAmount,
            row.NewAmount,
            row.Classification,
            row.ChangeAmount,
            row.Reason,
            row.DecreasePerson,
            row.ClassifiedSubsidyAmount,
            row.OldFamilySize,
            row.CategoryCode,
            row.IsRural);

    private static MonthlyShiBaoRowDto ToShiBaoDto(MonthlyShiBaoRow row)
        => new(
            row.HeadName,
            row.MemberName,
            row.Address,
            row.Category,
            row.EnjoyDate,
            row.OriginalAmount,
            row.OriginalClassified,
            row.Reason,
            row.DecreaseAmount,
            row.CurrentAmount,
            row.CurrentClassified,
            row.IsRural);

    private static MonthlyClassifiedAddRowDto ToClassifiedAddDto(MonthlyClassifiedAddRow row)
        => new(
            row.ApplicantName,
            row.ApplicantGender,
            row.ApplicantBirthDate,
            row.Nationality,
            row.HealthStatus,
            row.Classification,
            row.FamilySize,
            row.Phone,
            row.Address,
            row.ApplicantIdCard,
            row.ClassifiedType,
            row.Count,
            row.Amount,
            row.IsRural,
            (row.Members ?? new List<MonthlyClassifiedMember>()).Select(m => new MonthlyClassifiedMemberDto(
                m.Name,
                m.Gender,
                m.BirthDate,
                m.Relation,
                m.Health,
                m.MonthlyIncome,
                m.AnnualIncome)).ToList());

    private static MonthlyDeathRowDto ToDeathDto(MonthlyDeathRow row)
        => new(
            row.HeadName,
            row.HeadIdCard,
            row.Address,
            row.DeceasedName,
            row.DeceasedDate,
            row.EnjoyDate,
            row.NewHeadName,
            row.CategoryCode,
            row.IsRural);

    private static MonthlyExitRectificationRowDto ToExitDto(MonthlyExitRectificationRow row)
        => new(
            row.Name,
            row.IdCard,
            row.Category,
            row.FamilySize,
            row.StopMonth,
            row.StopReason,
            row.GracePeriod,
            row.IntoDibao,
            row.IntoTeku,
            row.IntoEdge,
            row.IntoRigid,
            row.DirectExit);

    private static MonthlyTempReliefRowDto ToTempReliefDto(MonthlyTempReliefRow row)
        => new(
            row.ReliefType,
            row.Name,
            row.IdCard,
            row.Age,
            row.Gender,
            row.FamilySize,
            row.Address,
            row.Category,
            row.Phone,
            row.Amount,
            row.AuditDate,
            row.Reason,
            row.SelfPay,
            row.BankAccount);

    private static MonthlyMeetingResponse ToMeetingDto(MonthlyMeeting meeting)
        => new(
            meeting.Id,
            meeting.Year,
            meeting.Month,
            string.IsNullOrEmpty(meeting.Town) ? "全部" : meeting.Town,
            meeting.MeetingTime,
            meeting.Host,
            meeting.Recorder,
            meeting.Attendees,
            meeting.Absentees,
            meeting.MemberInfo,
            meeting.ApplyCategory,
            meeting.MemberNames,
            meeting.CreatedBy,
            meeting.CreatedAt);

    private static string NormalizeTown(string? town)
    {
        var value = town?.Trim() ?? string.Empty;
        return value == "全部" ? string.Empty : value;
    }

    /// <summary>空 = 全部；未识别分类返回 null（参数错误）。</summary>
    private static string? NormalizeCategory(string? category)
    {
        var value = category?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        return AllowedCategories.Contains(value, StringComparer.Ordinal) ? value : null;
    }

    /// <summary>空 = 全部数据集；含未知名称返回 false（参数错误）。</summary>
    private static bool TryParseDatasets(string? raw, out HashSet<string>? wanted)
    {
        wanted = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!AllowedDatasets.Contains(part, StringComparer.Ordinal))
            {
                return false;
            }

            set.Add(part);
        }

        if (set.Count > 0)
        {
            wanted = set;
        }

        return true;
    }

    private static string NormalizeLineBreaks(string? text)
        => (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();

    private static bool TryResolvePeriod(
        int? year,
        int? month,
        out int safeYear,
        out int safeMonth,
        out string? error)
    {
        safeYear = 0;
        safeMonth = 0;
        error = null;

        if (year is null || month is null)
        {
            error = "year 与 month 为必填参数";
            return false;
        }

        if (year is < 1900 or > 9999)
        {
            error = "年份参数无效";
            return false;
        }

        if (month is < 1 or > 12)
        {
            error = "月份参数应为 1-12";
            return false;
        }

        safeYear = year.Value;
        safeMonth = month.Value;
        return true;
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);
}
