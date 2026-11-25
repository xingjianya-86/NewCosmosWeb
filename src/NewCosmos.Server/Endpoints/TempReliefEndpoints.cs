using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 临时救助接口（低收入人口救助帮扶 → 临时救助）。
/// 读取 TEMP_VIEW；新增 TEMP_CREATE；编辑 / 确认 / 终止 / 归档 TEMP_EDIT；删除 TEMP_DELETE。
/// 所有业务读写均经 Core <see cref="ITempReliefService"/>，本文件不直接访问数据库，
/// 也不在前端/端点重抄金额公式：小额金额只按 Core 返回的低保月标准档位确认上限。
/// </summary>
public static class TempReliefEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTempReliefEndpoints(this IEndpointRouteBuilder app)
    {
        var prefix = "/api/social-assistance/temp-relief";

        app.MapGet(prefix, ListAsync);
        app.MapGet($"{prefix}/summary", GetSummaryAsync);
        app.MapGet($"{prefix}/candidates", SearchCandidatesAsync);
        app.MapGet($"{prefix}/candidates/{{sourceTable}}/{{sourceFamilyId:long}}", GetCandidateAsync);
        app.MapGet($"{prefix}/hukou-type", ResolveHukouTypeAsync);
        app.MapGet($"{prefix}/small-amount-levels", GetSmallAmountLevelsAsync);
        app.MapGet($"{prefix}/policy-snapshot", GetPolicySnapshotAsync);
        app.MapGet($"{prefix}/family-status", GetFamilyStatusAsync);
        app.MapGet($"{prefix}/check-annual", CheckAnnualAsync);
        app.MapGet($"{prefix}/report-unit-phone", GetReportUnitPhoneAsync);
        app.MapGet($"{prefix}/{{id:long}}", GetByIdAsync);

        app.MapPost(prefix, CreateAsync);
        app.MapPut($"{prefix}/{{id:long}}", UpdateAsync);
        app.MapDelete($"{prefix}/{{id:long}}", DeleteAsync);
        app.MapPost($"{prefix}/{{id:long}}/confirm", ConfirmAsync);
        app.MapPost($"{prefix}/{{id:long}}/stop", StopAsync);
        app.MapPost($"{prefix}/{{id:long}}/complete", CompleteAsync);

        return app;
    }

    // ───────────────────────────── 列表 / 统计 ─────────────────────────────

    private static async Task<IResult> ListAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        string? status)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var normalizedStatus = (status ?? string.Empty).Trim();
        if (normalizedStatus.Length > 0
            && normalizedStatus != TempReliefConstants.StatusDraft
            && normalizedStatus != TempReliefConstants.StatusConfirmed
            && normalizedStatus != TempReliefConstants.StatusStopped)
        {
            return Invalid(context, "status 仅支持 Draft / Confirmed / Stopped 或留空");
        }

        var pageIndex = Math.Max(page ?? 1, 1);
        var effectivePageSize = Math.Clamp(
            pageSize is null or <= 0 ? DefaultPageSize : pageSize.Value, 1, MaxPageSize);

        var result = await service.GetPagedAsync(
            (keyword ?? string.Empty).Trim(),
            normalizedStatus,
            pageIndex,
            effectivePageSize,
            ct);

        if (result.IsFailure || result.Value is null)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = result.Value.Items.Select(ToListItem).ToList();
        return Results.Json(new TempReliefListResponse(
            items,
            result.Value.PageIndex,
            result.Value.PageSize,
            result.Value.TotalCount,
            result.Value.TotalPages));
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        IStatisticsService statistics,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        int? draftCount = null;
        int? confirmedCount = null;
        int? stoppedCount = null;

        var draft = await service.GetPagedAsync(string.Empty, TempReliefConstants.StatusDraft, 1, 1, ct);
        if (draft.HasValue)
        {
            draftCount = draft.Value.TotalCount;
        }

        var confirmed = await service.GetPagedAsync(string.Empty, TempReliefConstants.StatusConfirmed, 1, 1, ct);
        if (confirmed.HasValue)
        {
            confirmedCount = confirmed.Value.TotalCount;
        }

        var stopped = await service.GetPagedAsync(string.Empty, TempReliefConstants.StatusStopped, 1, 1, ct);
        if (stopped.HasValue)
        {
            stoppedCount = stopped.Value.TotalCount;
        }

        int? yearConfirmed = null;
        decimal? yearAmount = null;
        var stats = await statistics.GetTempReliefStatsAsync(ct);
        if (stats.HasValue)
        {
            yearConfirmed = stats.Value.YearConfirmed;
            yearAmount = stats.Value.YearConfirmedAmount;
        }

        return Results.Json(new TempReliefSummaryResponse(
            draftCount, confirmedCount, stoppedCount, yearConfirmed, yearAmount));
    }

    // ───────────────────────────── 详情 / 候选人 ─────────────────────────────

    private static async Task<IResult> GetByIdAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var appResult = await service.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return appResult.IsFailure
                ? ApiErrors.FromResult(context, appResult)
                : NotFound(context, "临时救助申请不存在");
        }

        var membersResult = await service.GetMembersByApplicationIdAsync(id, ct);
        if (membersResult.IsFailure)
        {
            return ApiErrors.FromResult(context, membersResult);
        }

        var diseasesResult = await service.GetDiseasesByApplicationIdAsync(id, ct);
        if (diseasesResult.IsFailure)
        {
            return ApiErrors.FromResult(context, diseasesResult);
        }

        var accidentsResult = await service.GetAccidentsByApplicationIdAsync(id, ct);
        if (accidentsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, accidentsResult);
        }

        var educationsResult = await service.GetEducationsByApplicationIdAsync(id, ct);
        if (educationsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, educationsResult);
        }

        return Results.Json(ToDetail(
            appResult.Value,
            membersResult.Value ?? new List<TempReliefMember>(),
            diseasesResult.Value ?? new List<TempReliefDisease>(),
            accidentsResult.Value ?? new List<TempReliefAccident>(),
            educationsResult.Value ?? new List<TempReliefEducation>()));
    }

    private static async Task<IResult> SearchCandidatesAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? keyword,
        string? excludeIdCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Results.Json(new TempReliefCandidateListResponse(Array.Empty<TempReliefCandidateDto>()));
        }

        var result = await service.SearchCandidatesAsync(
            keyword.Trim(),
            string.IsNullOrWhiteSpace(excludeIdCard) ? null : excludeIdCard.Trim(),
            ct);

        if (result.IsFailure || result.Value is null)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new TempReliefCandidateListResponse(
            result.Value.Select(ToCandidate).ToList()));
    }

    private static async Task<IResult> GetCandidateAsync(
        string sourceTable,
        long sourceFamilyId,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? excludeIdCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TempReliefConstants.IsAllowedSourceTable(sourceTable))
        {
            return Invalid(context, "来源表不在白名单内");
        }

        var result = await service.GetCandidateDetailAsync(
            sourceTable,
            sourceFamilyId,
            string.IsNullOrWhiteSpace(excludeIdCard) ? null : excludeIdCard.Trim(),
            ct);

        if (result.IsFailure || result.Value is null)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(ToCandidate(result.Value));
    }

    // ───────────────────────────── 表单回填辅助 ─────────────────────────────

    private static async Task<IResult> ResolveHukouTypeAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.ResolveHukouTypeAsync(idCard, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new TempReliefHukouTypeResponse(result.Value ?? string.Empty));
    }

    private static async Task<IResult> GetSmallAmountLevelsAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? hukouType)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.GetSmallAmountLevelsAsync(hukouType, ct);
        if (result.IsFailure || result.Value is null)
        {
            return ApiErrors.FromResult(context, result);
        }

        var levels = result.Value
            .Select(level => new TempReliefSmallAmountLevelDto(
                level.StandardType ?? string.Empty,
                level.StandardName ?? string.Empty,
                level.StandardValue,
                level.Unit ?? string.Empty,
                level.HukouType ?? string.Empty))
            .ToList();

        return Results.Json(new TempReliefSmallAmountLevelsResponse(levels));
    }

    private static async Task<IResult> GetPolicySnapshotAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(idCard))
        {
            return Results.Json(new TempReliefPolicySnapshotResponse(Array.Empty<string>()));
        }

        var result = await service.GetPolicySnapshotAsync(idCard.Trim(), ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new TempReliefPolicySnapshotResponse(result.Value ?? new List<string>()));
    }

    private static async Task<IResult> GetFamilyStatusAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(idCard))
        {
            return Results.Json(new TempReliefFamilyStatusResponse(string.Empty));
        }

        var result = await service.GetFamilyMemberStatusAsync(idCard.Trim(), ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new TempReliefFamilyStatusResponse(result.Value ?? string.Empty));
    }

    private static async Task<IResult> CheckAnnualAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? idCard,
        string? applyYear,
        long? excludeId)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(idCard))
        {
            return Invalid(context, "请填写身份证号");
        }

        if (string.IsNullOrWhiteSpace(applyYear) || !int.TryParse(applyYear.Trim(), out var year)
            || year < 1900 || year > 2200)
        {
            return Invalid(context, "applyYear 须为四位年份");
        }

        var result = await service.CheckAnnualExistsAsync(
            idCard.Trim(), year.ToString(), excludeId, ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new TempReliefAnnualCheckResponse(result.Value));
    }

    private static async Task<IResult> GetReportUnitPhoneAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? reportUnit)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.GetReportUnitPhoneAsync(reportUnit, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new TempReliefReportUnitPhoneResponse(result.Value ?? string.Empty));
    }

    // ───────────────────────────── 写接口 ─────────────────────────────

    private static async Task<IResult> CreateAsync(
        TempReliefSaveRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_CREATE, ct);
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

        var validation = ValidateRequest(request, create: true, existing: null);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        var sourceCheck = await VerifySourceApplicantAsync(request, service, ct);
        if (sourceCheck is not null)
        {
            return Invalid(context, sourceCheck);
        }

        var (application, members, buildError) = await BuildApplicationAsync(
            request, existing: null, service, OperatorName(user), ct);
        if (application is null)
        {
            return Invalid(context, buildError ?? "临时救助申请数据不完整");
        }

        var created = await service.CreateAsync(application, members, ct);
        return created.IsFailure
            ? MapWriteResult(context, created)
            : Results.Json(new TempReliefCreatedResponse(created.Value));
    }

    private static async Task<IResult> UpdateAsync(
        long id,
        TempReliefSaveRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_EDIT, ct);
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

        var existingResult = await service.GetByIdAsync(id, ct);
        if (existingResult.IsFailure || existingResult.Value is null)
        {
            return existingResult.IsFailure
                ? ApiErrors.FromResult(context, existingResult)
                : NotFound(context, "临时救助申请不存在");
        }

        var existing = existingResult.Value;
        if (!string.Equals(existing.Status, TempReliefConstants.StatusDraft, StringComparison.Ordinal))
        {
            return Conflict(context, ErrorCodes.INVALID_TRANSITION, "仅草稿状态可编辑");
        }

        var validation = ValidateRequest(request, create: false, existing);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        var sourceCheck = await VerifySourceApplicantAsync(request, service, ct);
        if (sourceCheck is not null)
        {
            return Invalid(context, sourceCheck);
        }

        var (application, members, buildError) = await BuildApplicationAsync(
            request, existing, service, OperatorName(user), ct);
        if (application is null)
        {
            return Invalid(context, buildError ?? "临时救助申请数据不完整");
        }

        var updated = await service.UpdateAsync(application, members, ct);
        return updated.IsFailure
            ? MapWriteResult(context, updated)
            : Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_DELETE, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.DeleteAsync(id, ct);
        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> ConfirmAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await service.ConfirmAsync(id, OperatorName(user), ct);
        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> StopAsync(
        long id,
        TempReliefStopRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await service.StopAsync(
            id, request?.Reason?.Trim() ?? string.Empty, OperatorName(user), ct);

        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> CompleteAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await service.CompleteArchiveAsync(id, ct);
        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.NoContent();
    }

    // ───────────────────────────── 保存校验 / 组装 ─────────────────────────────

    private static string? ValidateRequest(
        TempReliefSaveRequest request, bool create, TempReliefApplication? existing)
    {
        var sourceTable = request.SourceTable?.Trim() ?? string.Empty;
        var sourceFamilyId = request.SourceFamilyId > 0
            ? request.SourceFamilyId
            : existing?.SourceFamilyId ?? 0;

        if (create || sourceFamilyId > 0)
        {
            if (!TempReliefConstants.IsAllowedSourceTable(sourceTable))
            {
                return "申请人必须从检索结果中选择（来源表不在白名单内）";
            }

            if (sourceFamilyId <= 0)
            {
                return "申请人必须从检索结果中选择";
            }
        }

        if (string.IsNullOrWhiteSpace(request.ApplicantName))
        {
            return "申请人姓名缺失";
        }

        if (string.IsNullOrWhiteSpace(request.ApplicantIdCard))
        {
            return "申请人身份证号缺失";
        }

        var reliefType = string.IsNullOrWhiteSpace(request.ReliefType)
            ? TempReliefConstants.ReliefTypeLarge
            : request.ReliefType.Trim();

        if (reliefType != TempReliefConstants.ReliefTypeLarge
            && reliefType != TempReliefConstants.ReliefTypeSmall)
        {
            return "救助类型仅支持大额或小额";
        }

        var difficultyType = string.IsNullOrWhiteSpace(request.DifficultyType)
            ? TempReliefConstants.DifficultyTypeOther
            : request.DifficultyType.Trim();

        if (Array.IndexOf(TempReliefConstants.DifficultyTypeOptions, difficultyType) < 0)
        {
            return "困难类型不在允许范围内";
        }

        if (string.IsNullOrWhiteSpace(request.DifficultyReason))
        {
            return "请填写申请救助原因/困难情况";
        }

        if (difficultyType == TempReliefConstants.DifficultyTypeDisease)
        {
            if (request.Diseases is null || request.Diseases.Count == 0)
            {
                return "困难类型为疾病，请至少填写一条疾病明细";
            }

            if (request.Diseases.Any(d => string.IsNullOrWhiteSpace(d.DiseaseName)
                && string.IsNullOrWhiteSpace(d.DiseaseCode)))
            {
                return "请填写每条疾病明细的诊断结果或疾病编码";
            }

            if (request.Diseases.Any(d => d.SelfPaid.HasValue && d.SelfPaid.Value < 0)
                || request.Diseases.Any(d => (d.MedicalTotal.HasValue && d.MedicalTotal.Value < 0)
                    || (d.InsurancePaid.HasValue && d.InsurancePaid.Value < 0)))
            {
                return "疾病明细金额不能为负数";
            }
        }

        if (difficultyType == TempReliefConstants.DifficultyTypeAccident)
        {
            if (request.Accidents is null || request.Accidents.Count == 0)
            {
                return "困难类型为意外灾害，请至少填写一条意外灾害明细";
            }

            if (request.Accidents.Any(a => string.IsNullOrWhiteSpace(a.AccidentType)))
            {
                return "请填写每条意外灾害的类型";
            }
        }

        if (difficultyType == TempReliefConstants.DifficultyTypeEducation)
        {
            if (request.Educations is null || request.Educations.Count == 0)
            {
                return "困难类型为教育支出，请至少填写一条教育支出明细";
            }

            if (request.Educations.Any(e => string.IsNullOrWhiteSpace(e.StudentName)
                && string.IsNullOrWhiteSpace(e.EducationStage)))
            {
                return "请填写每条教育支出明细的学生姓名或就学阶段";
            }
        }

        if (request.PublicizeStartDate.HasValue && request.PublicizeEndDate.HasValue
            && request.PublicizeEndDate.Value.Date < request.PublicizeStartDate.Value.Date)
        {
            return "公示结束日期不能早于开始日期";
        }

        return null;
    }

    /// <summary>
    /// 服务端复核「申请人只能从候选人检索选择」：来源表 + 来源家庭 ID 必须能取到候选人，
    /// 且候选人身份证与提交的申请人身份证一致。更新历史无来源记录时放行（沿用库中原来源）。
    /// </summary>
    private static async Task<string?> VerifySourceApplicantAsync(
        TempReliefSaveRequest request,
        ITempReliefService service,
        CancellationToken ct)
    {
        var sourceTable = request.SourceTable?.Trim() ?? string.Empty;
        if (request.SourceFamilyId <= 0 || !TempReliefConstants.IsAllowedSourceTable(sourceTable))
        {
            return null;
        }

        var candidateResult = await service.GetCandidateDetailAsync(
            sourceTable, request.SourceFamilyId, null, ct);

        if (candidateResult.IsFailure || candidateResult.Value is null)
        {
            return "申请人来源记录不存在，请重新检索选择";
        }

        if (!string.Equals(
                candidateResult.Value.IdCard?.Trim(),
                request.ApplicantIdCard?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return "申请人身份证与来源记录不一致，请重新检索选择";
        }

        return null;
    }

    private static async Task<(TempReliefApplication? Application, List<TempReliefMember> Members, string? Error)>
        BuildApplicationAsync(
            TempReliefSaveRequest request,
            TempReliefApplication? existing,
            ITempReliefService service,
            string operatorName,
            CancellationToken ct)
    {
        var sourceTable = request.SourceTable?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(sourceTable))
        {
            sourceTable = existing?.SourceTable ?? string.Empty;
        }

        var sourceFamilyId = request.SourceFamilyId > 0
            ? request.SourceFamilyId
            : existing?.SourceFamilyId ?? 0;

        var reliefType = string.IsNullOrWhiteSpace(request.ReliefType)
            ? TempReliefConstants.ReliefTypeLarge
            : request.ReliefType.Trim();

        var hukouType = request.HukouType?.Trim() ?? existing?.HukouType ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hukouType) && !string.IsNullOrWhiteSpace(request.ApplicantIdCard))
        {
            var resolved = await service.ResolveHukouTypeAsync(request.ApplicantIdCard.Trim(), ct);
            if (resolved.IsSuccess)
            {
                hukouType = resolved.Value ?? string.Empty;
            }
        }

        var applyDate = (request.ApplyDate ?? existing?.ApplyDate ?? DateTime.Today).Date;
        var familyCategory = TempReliefConstants.NormalizeFamilyCategory(
            request.FamilyCategory ?? existing?.FamilyCategory);

        var memberInputs = (request.Members ?? new List<TempReliefMemberInput>())
            .Where(m => !string.IsNullOrWhiteSpace(m.MemberName))
            .Where(m => !string.Equals(
                m.IdCard?.Trim(),
                request.ApplicantIdCard?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Where(m => !(string.Equals(
                    TempReliefConstants.GetRelationshipDisplayName(m.Relation),
                    TempReliefConstants.RelationHead,
                    StringComparison.Ordinal)
                && string.Equals(
                    m.MemberName?.Trim(),
                    request.ApplicantName?.Trim(),
                    StringComparison.Ordinal)))
            .ToList();

        var effectiveMemberCount = TempReliefConstants.CountEffectiveMembers(
            memberInputs.Select(m => m.MemberName));

        var members = memberInputs
            .Select((m, index) => new TempReliefMember
            {
                Id = m.Id,
                MemberName = m.MemberName?.Trim() ?? string.Empty,
                Gender = m.Gender?.Trim() ?? string.Empty,
                Relation = m.Relation?.Trim() ?? string.Empty,
                IdCard = m.IdCard?.Trim() ?? string.Empty,
                WorkUnit = m.WorkUnit?.Trim() ?? string.Empty,
                AnnualIncome = m.AnnualIncome,
                SortOrder = m.SortOrder > 0 ? m.SortOrder : index
            })
            .ToList();

        var application = new TempReliefApplication
        {
            Id = existing?.Id ?? 0,
            ReliefType = reliefType,
            SourceType = string.Equals(sourceTable, TempReliefConstants.CurrentSourceTable, StringComparison.Ordinal)
                ? TempReliefConstants.SourceApplication
                : TempReliefConstants.SourceImportTab,
            SourceTable = sourceTable,
            SourceFamilyId = sourceFamilyId,
            HukouType = hukouType,
            ApplicantName = request.ApplicantName?.Trim() ?? string.Empty,
            ApplicantIdCard = request.ApplicantIdCard?.Trim() ?? string.Empty,
            Gender = request.Gender?.Trim() ?? existing?.Gender ?? string.Empty,
            Age = request.Age ?? existing?.Age,
            Phone = request.Phone?.Trim() ?? string.Empty,
            BeneficiaryName = request.BeneficiaryName?.Trim(),
            BeneficiaryIdCard = request.BeneficiaryIdCard?.Trim(),
            BeneficiaryGender = request.BeneficiaryGender?.Trim(),
            BeneficiaryAge = request.BeneficiaryAge,
            BeneficiaryRelation = request.BeneficiaryRelation?.Trim(),
            FamilyProvince = request.FamilyProvince?.Trim() ?? string.Empty,
            FamilyCity = request.FamilyCity?.Trim() ?? string.Empty,
            FamilyDistrict = request.FamilyDistrict?.Trim() ?? string.Empty,
            FamilyTown = request.FamilyTown?.Trim() ?? string.Empty,
            FamilyDetail = request.FamilyDetail?.Trim() ?? string.Empty,
            FamilyAddress = request.FamilyAddress?.Trim() ?? string.Empty,
            HukouProvince = request.HukouProvince?.Trim() ?? string.Empty,
            HukouCity = request.HukouCity?.Trim() ?? string.Empty,
            HukouDistrict = request.HukouDistrict?.Trim() ?? string.Empty,
            HukouTown = request.HukouTown?.Trim() ?? string.Empty,
            HukouAddress = request.HukouAddress?.Trim() ?? string.Empty,
            Town = request.Town?.Trim() ?? string.Empty,
            Village = request.Village?.Trim() ?? string.Empty,
            FamilySize = TempReliefConstants.ResolveFamilySize(
                request.FamilySize ?? existing?.FamilySize, effectiveMemberCount),
            FamilyCategory = familyCategory,
            BankName = request.BankName?.Trim() ?? string.Empty,
            BankAccount = request.BankAccount?.Trim() ?? string.Empty,
            PolicyEnjoyed = request.PolicyEnjoyed ?? string.Empty,
            FamilyMemberStatus = request.FamilyMemberStatus ?? string.Empty,
            DifficultyReason = request.DifficultyReason ?? string.Empty,
            DifficultyType = string.IsNullOrWhiteSpace(request.DifficultyType)
                ? TempReliefConstants.DifficultyTypeOther
                : request.DifficultyType.Trim(),
            ApplyDate = applyDate,
            ReportUnit = request.ReportUnit?.Trim() ?? string.Empty,
            ReportTime = request.ReportTime ?? existing?.ReportTime ?? DateTime.Now,
            VerifyResult = request.VerifyResult ?? string.Empty,
            PublicizeStartDate = request.PublicizeStartDate,
            PublicizeEndDate = request.PublicizeEndDate,
            AcceptancePerson = request.AcceptancePerson?.Trim() ?? string.Empty,
            Status = existing?.Status ?? TempReliefConstants.StatusDraft,
            CreatedBy = existing?.CreatedBy ?? operatorName,
            UpdatedBy = operatorName,
            Diseases = (request.Diseases ?? new List<TempReliefDiseaseInput>())
                .Select((d, index) => new TempReliefDisease
                {
                    Id = d.Id,
                    DiseaseName = d.DiseaseName?.Trim() ?? string.Empty,
                    DiseaseCode = d.DiseaseCode?.Trim() ?? string.Empty,
                    MemberName = d.MemberName?.Trim() ?? string.Empty,
                    Hospital = d.Hospital?.Trim() ?? string.Empty,
                    TreatStartDate = d.TreatStartDate ?? DateTime.MinValue,
                    TreatEndDate = d.TreatEndDate ?? DateTime.MinValue,
                    MedicalTotal = d.MedicalTotal,
                    InsurancePaid = d.InsurancePaid,
                    SelfPaid = d.SelfPaid,
                    SortOrder = d.SortOrder > 0 ? d.SortOrder : index
                })
                .ToList(),
            Accidents = (request.Accidents ?? new List<TempReliefAccidentInput>())
                .Select((a, index) => new TempReliefAccident
                {
                    Id = a.Id,
                    AccidentType = a.AccidentType?.Trim() ?? string.Empty,
                    MemberName = a.MemberName?.Trim() ?? string.Empty,
                    HappenDate = a.HappenDate ?? DateTime.MinValue,
                    HappenPlace = a.HappenPlace?.Trim() ?? string.Empty,
                    InjurySituation = a.InjurySituation?.Trim() ?? string.Empty,
                    PropertyLoss = a.PropertyLoss,
                    CompensationPaid = a.CompensationPaid,
                    ResponsibilityDesc = a.ResponsibilityDesc?.Trim() ?? string.Empty,
                    MaterialDesc = a.MaterialDesc?.Trim() ?? string.Empty,
                    SortOrder = a.SortOrder > 0 ? a.SortOrder : index
                })
                .ToList(),
            Educations = (request.Educations ?? new List<TempReliefEducationInput>())
                .Select((e, index) => new TempReliefEducation
                {
                    Id = e.Id,
                    StudentName = e.StudentName?.Trim() ?? string.Empty,
                    EducationStage = e.EducationStage?.Trim() ?? string.Empty,
                    SchoolName = e.SchoolName?.Trim() ?? string.Empty,
                    SchoolDuration = e.SchoolDuration,
                    TuitionFee = e.TuitionFee,
                    FeeDate = e.FeeDate ?? DateTime.Today,
                    SortOrder = e.SortOrder > 0 ? e.SortOrder : index
                })
                .ToList()
        };

        if (string.IsNullOrWhiteSpace(application.FamilyAddress))
        {
            application.FamilyAddress = AddressResolver.BuildAddressFromText(
                application.FamilyDistrict,
                application.FamilyTown,
                application.Village,
                application.FamilyDetail);
        }

        if (string.IsNullOrWhiteSpace(application.HukouAddress))
        {
            application.HukouAddress = BuildTextAddress(
                application.HukouProvince,
                application.HukouCity,
                application.HukouDistrict,
                application.HukouTown);
        }

        // 救助对象缺省为户主（旧版口径：户主恒为申请人，不因成员选择而改写申请人）。
        if (string.IsNullOrWhiteSpace(application.BeneficiaryName))
        {
            application.BeneficiaryName = application.ApplicantName;
            application.BeneficiaryIdCard = application.ApplicantIdCard;
            application.BeneficiaryGender = application.Gender;
            application.BeneficiaryAge = application.Age;
            application.BeneficiaryRelation = TempReliefConstants.RelationHead;
        }

        if (reliefType == TempReliefConstants.ReliefTypeSmall)
        {
            var levelsResult = await service.GetSmallAmountLevelsAsync(hukouType, ct);
            if (levelsResult.IsFailure || levelsResult.Value is null || levelsResult.Value.Count == 0)
            {
                return (null, members, levelsResult.Message ?? "未取到小额救助金额档位");
            }

            var level = levelsResult.Value.FirstOrDefault(l =>
                string.Equals(l.StandardName, request.SmallAmountLevel?.Trim(), StringComparison.Ordinal));

            if (level is null)
            {
                // 存量档位（旧政策 500/1000/2000 等）不在当前低保月标准列表中：
                // 保留原档位名与原金额回显/保存，只有用户改选新档位后才按新口径落库。
                if (!string.IsNullOrWhiteSpace(request.SmallAmountLevel)
                    && request.ConfirmAmount.HasValue
                    && request.ConfirmAmount.Value > 0)
                {
                    application.SmallAmountLevel = request.SmallAmountLevel.Trim();
                    application.ConfirmAmount = Math.Round(
                        request.ConfirmAmount.Value, 2, MidpointRounding.AwayFromZero);
                    return (application, members, null);
                }

                return (null, members, "请选择有效的小额救助金额档位");
            }

            var amount = request.ConfirmAmount ?? level.StandardValue;
            if (amount <= 0 || amount > level.StandardValue)
            {
                return (null, members,
                    $"确定金额须大于 0 且不能超过户籍对应低保月标准 {level.StandardValue:F2} 元");
            }

            application.SmallAmountLevel = level.StandardName;
            application.ConfirmAmount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            // 大额救助金额不填写，由上级审批核定。
            application.SmallAmountLevel = string.Empty;
            application.ConfirmAmount = null;
        }

        return (application, members, null);
    }

    private static string BuildTextAddress(params string?[] parts)
        => string.Concat(parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim()));

    // ───────────────────────────── DTO 映射 ─────────────────────────────

    private static TempReliefListItemDto ToListItem(TempReliefApplication app) => new(
        app.Id,
        app.ApplicationNo ?? string.Empty,
        app.ApplicantName ?? string.Empty,
        DataMasker.MaskIdCard(app.ApplicantIdCard ?? string.Empty),
        app.ReliefType ?? string.Empty,
        TempReliefConstants.GetReliefTypeName(app.ReliefType),
        app.FamilyCategory ?? string.Empty,
        app.Town ?? string.Empty,
        app.Village ?? string.Empty,
        app.ApplyDate,
        app.ConfirmAmount,
        app.Status ?? string.Empty,
        TempReliefConstants.GetStatusName(app.Status),
        app.StoppedAt,
        app.StopReason ?? string.Empty,
        app.ReportUnit ?? string.Empty,
        app.CreatedAt);

    private static TempReliefDetailResponse ToDetail(
        TempReliefApplication app,
        IReadOnlyList<TempReliefMember> members,
        IReadOnlyList<TempReliefDisease> diseases,
        IReadOnlyList<TempReliefAccident> accidents,
        IReadOnlyList<TempReliefEducation> educations) => new(
        app.Id,
        app.ApplicationNo ?? string.Empty,
        app.ReliefType ?? string.Empty,
        TempReliefConstants.GetReliefTypeName(app.ReliefType),
        app.ApplyYear ?? string.Empty,
        app.SourceType ?? string.Empty,
        app.SourceTable ?? string.Empty,
        app.SourceFamilyId,
        app.HukouType ?? string.Empty,
        app.ApplicantName ?? string.Empty,
        app.ApplicantIdCard ?? string.Empty,
        app.Gender ?? string.Empty,
        app.Age,
        app.Phone ?? string.Empty,
        app.BeneficiaryName,
        app.BeneficiaryIdCard,
        app.BeneficiaryGender,
        app.BeneficiaryAge,
        app.BeneficiaryRelation,
        app.FamilyProvince ?? string.Empty,
        app.FamilyCity ?? string.Empty,
        app.FamilyDistrict ?? string.Empty,
        app.FamilyTown ?? string.Empty,
        app.FamilyDetail ?? string.Empty,
        app.FamilyAddress ?? string.Empty,
        app.HukouProvince ?? string.Empty,
        app.HukouCity ?? string.Empty,
        app.HukouDistrict ?? string.Empty,
        app.HukouTown ?? string.Empty,
        app.HukouAddress ?? string.Empty,
        app.Town ?? string.Empty,
        app.Village ?? string.Empty,
        app.FamilySize,
        app.FamilyCategory ?? string.Empty,
        app.BankName ?? string.Empty,
        app.BankAccount ?? string.Empty,
        app.PolicyEnjoyed ?? string.Empty,
        app.FamilyMemberStatus ?? string.Empty,
        app.DifficultyReason ?? string.Empty,
        app.DifficultyType ?? string.Empty,
        app.ApplyDate,
        app.ReportUnit ?? string.Empty,
        app.ReportTime,
        app.SmallAmountLevel ?? string.Empty,
        app.ConfirmAmount,
        app.PublicizeStartDate,
        app.PublicizeEndDate,
        app.VerifyResult ?? string.Empty,
        app.AcceptancePerson ?? string.Empty,
        app.Status ?? string.Empty,
        TempReliefConstants.GetStatusName(app.Status),
        app.ConfirmedAt,
        app.ConfirmedBy ?? string.Empty,
        app.StoppedAt,
        app.StoppedBy ?? string.Empty,
        app.StopReason ?? string.Empty,
        app.CreatedAt,
        app.UpdatedAt,
        members.Select(m => new TempReliefMemberDto(
            m.Id,
            m.MemberName ?? string.Empty,
            m.Gender ?? string.Empty,
            m.Relation ?? string.Empty,
            m.IdCard ?? string.Empty,
            m.WorkUnit ?? string.Empty,
            m.AnnualIncome,
            m.SortOrder)).ToList(),
        diseases.Select(d => new TempReliefDiseaseDto(
            d.Id,
            d.DiseaseName ?? string.Empty,
            d.DiseaseCode ?? string.Empty,
            d.MemberName ?? string.Empty,
            d.Hospital ?? string.Empty,
            d.TreatStartDate == DateTime.MinValue ? null : d.TreatStartDate,
            d.TreatEndDate == DateTime.MinValue ? null : d.TreatEndDate,
            d.MedicalTotal,
            d.InsurancePaid,
            d.SelfPaid,
            d.SortOrder)).ToList(),
        accidents.Select(a => new TempReliefAccidentDto(
            a.Id,
            a.AccidentType ?? string.Empty,
            a.MemberName ?? string.Empty,
            a.HappenDate == DateTime.MinValue ? null : a.HappenDate,
            a.HappenPlace ?? string.Empty,
            a.InjurySituation ?? string.Empty,
            a.PropertyLoss,
            a.CompensationPaid,
            a.ResponsibilityDesc ?? string.Empty,
            a.MaterialDesc ?? string.Empty,
            a.SortOrder)).ToList(),
        educations.Select(e => new TempReliefEducationDto(
            e.Id,
            e.StudentName ?? string.Empty,
            e.EducationStage ?? string.Empty,
            e.SchoolName ?? string.Empty,
            e.SchoolDuration,
            e.TuitionFee,
            e.FeeDate == DateTime.MinValue ? null : e.FeeDate,
            e.SortOrder)).ToList());

    private static TempReliefCandidateDto ToCandidate(TempReliefCandidate candidate) => new(
        candidate.SourceFamilyId,
        candidate.SourceTable ?? string.Empty,
        candidate.SourceName ?? string.Empty,
        candidate.HukouType ?? string.Empty,
        candidate.Name ?? string.Empty,
        candidate.IdCard ?? string.Empty,
        candidate.Gender ?? string.Empty,
        candidate.Phone ?? string.Empty,
        candidate.Province ?? string.Empty,
        candidate.City ?? string.Empty,
        candidate.District ?? string.Empty,
        candidate.DetailAddress ?? string.Empty,
        candidate.FamilyAddress ?? string.Empty,
        candidate.HukouAddress ?? string.Empty,
        candidate.Town ?? string.Empty,
        candidate.Village ?? string.Empty,
        candidate.FamilySize,
        candidate.FamilyCategory ?? string.Empty,
        candidate.BankName ?? string.Empty,
        candidate.BankAccount ?? string.Empty,
        candidate.OneCardAccount ?? string.Empty,
        candidate.MatchedPersonInfo ?? string.Empty,
        candidate.MatchedMemberIdCard ?? string.Empty,
        candidate.HasAppliedThisYear,
        (candidate.Members ?? new List<TempReliefMemberSnapshot>())
            .Select(m => new TempReliefMemberSnapshotDto(
                m.MemberName ?? string.Empty,
                m.Gender ?? string.Empty,
                m.Relation ?? string.Empty,
                m.IdCard ?? string.Empty,
                m.WorkUnit ?? string.Empty,
                m.AnnualIncome))
            .ToList());

    // ───────────────────────────── 通用出口 ─────────────────────────────

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.Username) ? user.FullName ?? "System" : user.Username;

    private static IResult MapWriteResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND)
        {
            return NotFound(context, result.Message);
        }

        if (result.ErrorCode is ErrorCodes.ANNUAL_LIMIT_EXCEEDED)
        {
            return Conflict(context, ErrorCodes.ANNUAL_LIMIT_EXCEEDED,
                result.Message ?? "该申请人本年度已申请过临时救助");
        }

        if (result.ErrorCode is ErrorCodes.INVALID_TRANSITION)
        {
            return Conflict(context, ErrorCodes.INVALID_TRANSITION,
                result.Message ?? "当前状态不允许该操作");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.INVALID_ID_CARD
            or ErrorCodes.CONFIG_NOT_FOUND)
        {
            return Invalid(context, result.Message ?? "请求参数校验失败");
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    private static IResult NotFound(HttpContext context, string? message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            message ?? "请求的资源不存在");

    private static IResult Conflict(HttpContext context, string code, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict, code, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
