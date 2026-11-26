using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.UserManagement;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 临时救助接口（低收入人口救助帮扶 → 临时救助）。
/// 读取 TEMP_VIEW；新增 TEMP_CREATE；编辑 / 提交 / 退回 / 不予 / 确认 / 备案 / 终止 / 改期 / 归档 TEMP_EDIT；删除 TEMP_DELETE。
/// <para>
/// 业务口径（两大类 × 三小类、6 状态、时限、金额档位/上限、公示、年度唯一）全部在 Core
/// <see cref="ITempReliefService"/> / <c>TempReliefConstants</c> / <c>TempReliefPrintDataBuilder</c>，
/// 本文件只做权限、数据范围、请求映射与 DTO 组装，不重抄金额/状态/时限公式。
/// </para>
/// </summary>
public static class TempReliefEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    /// <summary>高级筛选（镇/年/月/逾期）为避免错误分页而全量扫描时的单页大小。</summary>
    private const int ScanPageSize = 100;

    /// <summary>高级筛选全量扫描上限；超过时显式要求缩小范围，不返回不完整结果。</summary>
    private const int MaxScanRecords = 5000;

    /// <summary>OCR 上传图片大小上限（与高龄端一致）。</summary>
    private const long MaxOcrUploadBytes = 10L * 1024 * 1024;

    /// <summary>OCR 请求体上限：文件 10MB + multipart 头开销。</summary>
    private const long MaxOcrRequestBodyBytes = MaxOcrUploadBytes + 1024 * 1024;

    private static readonly string[] PendingStatuses =
    {
        TempReliefConstants.StatusDraft,
        TempReliefConstants.StatusSubmitted,
        TempReliefConstants.StatusReturned
    };

    private static readonly string[] ArchivedStatuses =
    {
        TempReliefConstants.StatusConfirmed,
        TempReliefConstants.StatusStopped,
        TempReliefConstants.StatusRejected
    };

    public static IEndpointRouteBuilder MapTempReliefEndpoints(this IEndpointRouteBuilder app)
    {
        var prefix = "/api/social-assistance/temp-relief";

        app.MapGet(prefix, ListAsync);
        app.MapGet($"{prefix}/summary", GetSummaryAsync);
        app.MapGet($"{prefix}/archived", GetArchivedAsync);
        app.MapGet($"{prefix}/candidates", SearchCandidatesAsync);
        app.MapGet($"{prefix}/candidates/{{sourceTable}}/{{sourceFamilyId:long}}", GetCandidateAsync);
        app.MapGet($"{prefix}/insured-identity", ResolveInsuredIdentityAsync);
        app.MapGet($"{prefix}/hukou-type", ResolveHukouTypeAsync);
        app.MapGet($"{prefix}/small-amount-levels", GetSmallAmountLevelsAsync);
        app.MapGet($"{prefix}/small-tiers", GetSmallTiersAsync);
        app.MapGet($"{prefix}/policy-snapshot", GetPolicySnapshotAsync);
        app.MapGet($"{prefix}/family-status", GetFamilyStatusAsync);
        app.MapGet($"{prefix}/check-annual", CheckAnnualAsync);
        app.MapGet($"{prefix}/report-unit-phone", GetReportUnitPhoneAsync);
        app.MapGet($"{prefix}/form-defaults", GetFormDefaultsAsync);
        app.MapGet($"{prefix}/publicize-preview", GetPublicizePreviewAsync);
        app.MapGet($"{prefix}/icd10", GetIcd10Async);
        app.MapGet($"{prefix}/{{id:long}}", GetByIdAsync);
        app.MapGet($"{prefix}/{{id:long}}/print-data", GetPrintDataAsync);
        app.MapGet($"{prefix}/{{id:long}}/photos", GetPhotosAsync);

        app.MapPost(prefix, CreateAsync);
        app.MapPut($"{prefix}/{{id:long}}", UpdateAsync);
        app.MapDelete($"{prefix}/{{id:long}}", DeleteAsync);
        app.MapPost($"{prefix}/{{id:long}}/submit", SubmitAsync);
        app.MapPost($"{prefix}/{{id:long}}/return", ReturnAsync);
        app.MapPost($"{prefix}/{{id:long}}/reject", RejectAsync);
        app.MapPost($"{prefix}/{{id:long}}/confirm", ConfirmAsync);
        app.MapPost($"{prefix}/{{id:long}}/file", MarkFiledAsync);
        app.MapPost($"{prefix}/{{id:long}}/change-date", ChangeApplyDateAsync);
        app.MapPost($"{prefix}/{{id:long}}/stop", StopAsync);
        app.MapPost($"{prefix}/{{id:long}}/complete", CompleteAsync);
        app.MapPost($"{prefix}/{{id:long}}/photos", UploadPhotoAsync).DisableAntiforgery();
        app.MapDelete($"{prefix}/{{id:long}}/photos/{{photoId:long}}", DeletePhotoAsync);

        // 身份证 OCR（multipart file）：{id}/ocr 校验记录存在且类别仅 ID_CARD；/ocr/idcard 供新建表单使用。
        app.MapPost($"{prefix}/{{id:long}}/ocr", RecognizeIdCardAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxOcrRequestBodyBytes));
        app.MapPost($"{prefix}/ocr/idcard", RecognizeIdCardAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxOcrRequestBodyBytes));

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
        string? status,
        string? populationStatus,
        string? reliefType,
        string? paymentChannel,
        string? town,
        int? year,
        int? month,
        bool? overdueOnly)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var statuses = NormalizeStatusFilter(status, out var statusError);
        if (statusError is not null)
        {
            return Invalid(context, statusError);
        }

        var population = NormalizePopulationStatus(populationStatus);
        if (!string.IsNullOrWhiteSpace(populationStatus) && population is null)
        {
            return Invalid(context, "populationStatus 仅支持 Insured / Uninsured 或留空");
        }

        var type = NormalizeReliefType(reliefType);
        if (!string.IsNullOrWhiteSpace(reliefType) && type is null)
        {
            return Invalid(context, "reliefType 仅支持 Large / Small 或留空");
        }

        var channel = NormalizePaymentChannel(paymentChannel);
        if (!string.IsNullOrWhiteSpace(paymentChannel) && channel is null)
        {
            return Invalid(context, "paymentChannel 仅支持 County / Reserve 或留空");
        }

        if (year is < 1900 or > 9999)
        {
            return Invalid(context, "year 参数无效");
        }

        if (month is < 1 or > 12)
        {
            return Invalid(context, "month 参数应为 1-12");
        }

        if (month.HasValue && !year.HasValue)
        {
            return Invalid(context, "按月筛选时 year 必填");
        }

        var pageIndex = Math.Max(page ?? 1, 1);
        var effectivePageSize = Math.Clamp(
            pageSize is null or <= 0 ? DefaultPageSize : pageSize.Value, 1, MaxPageSize);

        var keywordValue = (keyword ?? string.Empty).Trim();
        var townValue = (town ?? string.Empty).Trim();
        var advanced = townValue.Length > 0 || year.HasValue || month.HasValue || overdueOnly == true;

        if (!advanced)
        {
            var pageResult = await QueryBasePageAsync(
                service, keywordValue, statuses, population, type, channel,
                pageIndex, effectivePageSize, ct);

            if (pageResult.IsFailure || pageResult.Value is null)
            {
                return ApiErrors.FromResult(context, pageResult);
            }

            return Results.Json(new TempReliefListResponse(
                pageResult.Value.Items.Select(ToListItem).ToList(),
                pageResult.Value.PageIndex,
                pageResult.Value.PageSize,
                pageResult.Value.TotalCount,
                pageResult.Value.TotalPages));
        }

        // 高级筛选：Core 分页接口不提供 town/year/month/overdue 条件，
        // 按同一业务过滤条件有界扫描后内存分页；超过上限显式报错，绝不返回错误页数据。
        var firstResult = await QueryBasePageAsync(
            service, keywordValue, statuses, population, type, channel, 1, ScanPageSize, ct);
        if (firstResult.IsFailure || firstResult.Value is null)
        {
            return ApiErrors.FromResult(context, firstResult);
        }

        var firstPage = firstResult.Value;
        if (firstPage.TotalCount > MaxScanRecords)
        {
            return Invalid(context,
                $"筛选结果超过 {MaxScanRecords} 条，请增加关键字或缩小年份范围后再查询");
        }

        var scanned = new List<TempReliefApplication>(firstPage.TotalCount);
        scanned.AddRange(firstPage.Items);

        for (var pageNo = 2; pageNo <= firstPage.TotalPages; pageNo++)
        {
            var nextResult = await QueryBasePageAsync(
                service, keywordValue, statuses, population, type, channel, pageNo, ScanPageSize, ct);
            if (nextResult.IsFailure || nextResult.Value is null)
            {
                return ApiErrors.FromResult(context, nextResult);
            }

            scanned.AddRange(nextResult.Value.Items);
        }

        var filtered = scanned
            .Where(app => MatchesAdvancedFilters(app, townValue, year, month, overdueOnly == true))
            .ToList();

        var skip = (pageIndex - 1) * effectivePageSize;
        var items = filtered.Skip(skip).Take(effectivePageSize).Select(ToListItem).ToList();
        var totalPages = (int)Math.Ceiling((double)filtered.Count / effectivePageSize);

        return Results.Json(new TempReliefListResponse(
            items, pageIndex, effectivePageSize, filtered.Count, Math.Max(totalPages, 1)));
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

        var pendingCount = await CountAsync(service, PendingStatuses, ct);
        var submittedCount = await CountAsync(
            service, new[] { TempReliefConstants.StatusSubmitted }, ct);
        var confirmedCount = await CountAsync(
            service, new[] { TempReliefConstants.StatusConfirmed }, ct);
        var stoppedCount = await CountAsync(
            service, new[] { TempReliefConstants.StatusStopped, TempReliefConstants.StatusRejected }, ct);

        int? yearConfirmed = null;
        decimal? yearAmount = null;
        var stats = await statistics.GetTempReliefStatsAsync(ct);
        if (stats.HasValue)
        {
            yearConfirmed = stats.Value.YearConfirmed;
            yearAmount = stats.Value.YearConfirmedAmount;
        }

        var overdueCount = await CountOverdueAsync(service, ct);

        return Results.Json(new TempReliefSummaryResponse(
            pendingCount,
            confirmedCount,
            stoppedCount,
            yearConfirmed,
            yearAmount,
            pendingCount,
            submittedCount,
            overdueCount));
    }

    private static async Task<IResult> GetArchivedAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        int? year,
        int? month)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (year is < 1900 or > 9999)
        {
            return Invalid(context, "year 参数无效");
        }

        if (month is < 1 or > 12)
        {
            return Invalid(context, "month 参数应为 1-12");
        }

        if (month.HasValue && !year.HasValue)
        {
            return Invalid(context, "按月补打时 year 必填");
        }

        var pageIndex = Math.Max(page ?? 1, 1);
        var effectivePageSize = Math.Clamp(
            pageSize is null or <= 0 ? DefaultPageSize : pageSize.Value, 1, MaxPageSize);
        var keywordValue = (keyword ?? string.Empty).Trim();

        // 仅按年份补打：Core 无「已完结 + 年」组合查询，按已完结集合有界扫描后按业务时间过滤。
        if (year.HasValue && !month.HasValue)
        {
            var yearOnlyFirstResult = await service.GetArchivedPagedAsync(
                keywordValue.Length == 0 ? null : keywordValue, 1, ScanPageSize, ct);
            if (yearOnlyFirstResult.IsFailure || yearOnlyFirstResult.Value is null)
            {
                return ApiErrors.FromResult(context, yearOnlyFirstResult);
            }

            var yearOnlyFirstPage = yearOnlyFirstResult.Value;
            if (yearOnlyFirstPage.TotalCount > MaxScanRecords)
            {
                return Invalid(context,
                    $"已完结记录超过 {MaxScanRecords} 条，请改用 year + month 或增加关键字后重试");
            }

            var yearOnlyScanned = new List<TempReliefApplication>(yearOnlyFirstPage.TotalCount);
            yearOnlyScanned.AddRange(yearOnlyFirstPage.Items);

            for (var yearOnlyPageNo = 2; yearOnlyPageNo <= yearOnlyFirstPage.TotalPages; yearOnlyPageNo++)
            {
                var yearOnlyNextResult = await service.GetArchivedPagedAsync(
                    keywordValue.Length == 0 ? null : keywordValue, yearOnlyPageNo, ScanPageSize, ct);
                if (yearOnlyNextResult.IsFailure || yearOnlyNextResult.Value is null)
                {
                    return ApiErrors.FromResult(context, yearOnlyNextResult);
                }

                yearOnlyScanned.AddRange(yearOnlyNextResult.Value.Items);
            }

            var yearOnlyFiltered = yearOnlyScanned
                .Where(app => (app.ApplyDate ?? app.ReportTime ?? app.CreatedAt)?.Year == year.Value)
                .ToList();

            var skipYear = (pageIndex - 1) * effectivePageSize;
            var yearItems = yearOnlyFiltered.Skip(skipYear).Take(effectivePageSize).Select(ToListItem).ToList();
            var yearTotalPages = (int)Math.Ceiling((double)yearOnlyFiltered.Count / effectivePageSize);

            return Results.Json(new TempReliefListResponse(
                yearItems, pageIndex, effectivePageSize, yearOnlyFiltered.Count, Math.Max(yearTotalPages, 1)));
        }

        if (!year.HasValue || !month.HasValue)
        {
            var result = await service.GetArchivedPagedAsync(
                keywordValue.Length == 0 ? null : keywordValue,
                pageIndex, effectivePageSize, ct);
            if (result.IsFailure || result.Value is null)
            {
                return ApiErrors.FromResult(context, result);
            }

            return Results.Json(new TempReliefListResponse(
                result.Value.Items.Select(ToListItem).ToList(),
                result.Value.PageIndex,
                result.Value.PageSize,
                result.Value.TotalCount,
                result.Value.TotalPages));
        }

        var firstResult = await service.GetByMonthPagedAsync(
            year.Value, month.Value, 1, ScanPageSize, ct);
        if (firstResult.IsFailure || firstResult.Value is null)
        {
            return ApiErrors.FromResult(context, firstResult);
        }

        var firstPage = firstResult.Value;
        if (firstPage.TotalCount > MaxScanRecords)
        {
            return Invalid(context,
                $"该月已完结记录超过 {MaxScanRecords} 条，请增加姓名/身份证关键字后重试");
        }

        var scanned = new List<TempReliefApplication>(firstPage.TotalCount);
        scanned.AddRange(firstPage.Items);

        for (var pageNo = 2; pageNo <= firstPage.TotalPages; pageNo++)
        {
            var nextResult = await service.GetByMonthPagedAsync(
                year.Value, month.Value, pageNo, ScanPageSize, ct);
            if (nextResult.IsFailure || nextResult.Value is null)
            {
                return ApiErrors.FromResult(context, nextResult);
            }

            scanned.AddRange(nextResult.Value.Items);
        }

        var filtered = keywordValue.Length == 0
            ? scanned
            : scanned.Where(app =>
                    (app.ApplicantName ?? string.Empty).Contains(keywordValue, StringComparison.OrdinalIgnoreCase)
                    || (app.ApplicantIdCard ?? string.Empty).Contains(keywordValue, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var skip = (pageIndex - 1) * effectivePageSize;
        var items = filtered.Skip(skip).Take(effectivePageSize).Select(ToListItem).ToList();
        var totalPages = (int)Math.Ceiling((double)filtered.Count / effectivePageSize);

        return Results.Json(new TempReliefListResponse(
            items, pageIndex, effectivePageSize, filtered.Count, Math.Max(totalPages, 1)));
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

    private static async Task<IResult> ResolveInsuredIdentityAsync(
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
            return Invalid(context, "请填写身份证号");
        }

        var result = await service.ResolveInsuredIdentityAsync(idCard.Trim(), ct);
        if (result.IsFailure || result.Value is null)
        {
            return ApiErrors.FromResult(context, result);
        }

        var identity = result.Value;
        return Results.Json(new TempReliefInsuredIdentityResponse(
            identity.IsInsured,
            identity.PopulationStatus,
            TempReliefConstants.GetPopulationStatusName(identity.PopulationStatus),
            identity.LedgerNames,
            identity.PrimaryLedgerName,
            identity.BasisText));
    }

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

    private static async Task<IResult> GetSmallTiersAsync(
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        int? personCount)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var count = personCount is > 0 ? personCount.Value : 1;
        var baseResult = await service.GetReliefBaseStandardAsync(ct);
        if (baseResult.IsFailure)
        {
            return ApiErrors.FromResult(context, baseResult);
        }

        var baseStandard = baseResult.Value;
        var items = TempReliefConstants.SmallTierCodes
            .Select(code => new TempReliefSmallTierDto(
                code,
                TempReliefConstants.GetTierLabel(code),
                TempReliefConstants.TierMultiplier(code),
                baseStandard,
                count,
                TempReliefConstants.GetSmallTierCeiling(baseStandard, count, code)))
            .ToList();

        return Results.Json(new TempReliefSmallTiersResponse(baseStandard, count, items));
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

    /// <summary>
    /// 新建表单默认值：户籍地址按当前用户机构带出，乡镇验收人取本机构民政助理。
    /// 与 PC TempReliefFormViewModel.ResolveHukouFromOrganizationAsync / GetCivilAssistantNameAsync 同口径。
    /// </summary>
    private static async Task<IResult> GetFormDefaultsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        IUserService users,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var province = "黑龙江省";
        var city = string.Empty;
        var county = string.Empty;
        var town = string.Empty;
        var acceptancePerson = string.Empty;

        if (user.OrganizationId is > 0)
        {
            var orgResult = await organizations.GetByIdAsync(user.OrganizationId.Value, ct);
            if (orgResult.IsSuccess && orgResult.Value is { } org)
            {
                province = string.IsNullOrWhiteSpace(org.ProvinceName) ? province : org.ProvinceName;
                city = org.CityName ?? string.Empty;
                county = org.CountyName ?? string.Empty;
                town = org.TownName ?? string.Empty;
            }

            var usersResult = await users.GetByOrganizationAsync(user.OrganizationId.Value, ct);
            if (usersResult.IsSuccess && usersResult.Value is { Count: > 0 } staff)
            {
                acceptancePerson = staff
                    .FirstOrDefault(u => string.Equals(
                        u.Position, PositionTitles.CivilAssistant, StringComparison.Ordinal))
                    ?.FullName ?? string.Empty;
            }
        }

        return Results.Json(new TempReliefFormDefaultsResponse(
            province, city, county, town, acceptancePerson));
    }

    /// <summary>
    /// 申请日期对应的批次/公示预览（PC ApplyPublicizeDefaultsAsync 同口径）。
    /// 小额快救免公示，返回空日期与空批次提示。
    /// </summary>
    private static async Task<IResult> GetPublicizePreviewAsync(
        HttpContext context,
        INewPermissionService permissions,
        IBusinessTimelineService timeline,
        CancellationToken ct,
        DateTime? applyDate,
        string? reliefType)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.Equals(reliefType, TempReliefConstants.ReliefTypeSmall, StringComparison.Ordinal))
        {
            return Results.Json(new TempReliefPublicizePreviewResponse(null, null, null, null, string.Empty));
        }

        var date = (applyDate ?? DateTime.Today).Date;
        var batch = await timeline.CalculateTempReliefForApplyDateAsync(date);
        var curve = timeline.BuildTempReliefCurve(date);
        var hint = $"本次归入{batch.Year}年{batch.Month}月批次，" +
                   $"公示 {curve.PublicityStartDate:MM-dd}~{curve.PublicityEndDate:MM-dd}，" +
                   $"审核 {curve.AuditDate:MM-dd}。";

        return Results.Json(new TempReliefPublicizePreviewResponse(
            curve.PublicityStartDate,
            curve.PublicityEndDate,
            batch.Year,
            batch.Month,
            hint));
    }

    /// <summary>ICD-10 编码 → 常见病名（PC Icd10Catalog 同源）。</summary>
    private static async Task<IResult> GetIcd10Async(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        string? code)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        await Task.CompletedTask;
        var normalized = (code ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return Invalid(context, "请填写 ICD-10 编码");
        }

        Icd10Catalog.TryGetName(normalized, out var name);
        return Results.Json(new TempReliefIcd10Response(normalized, name ?? string.Empty));
    }

    // ───────────────────────────── 打印 / OCR / 照片 ─────────────────────────────

    private static async Task<IResult> GetPrintDataAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        IBusinessTimelineService timeline,
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

        var app = appResult.Value;

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
        app.Diseases = diseasesResult.Value ?? new List<TempReliefDisease>();

        var accidentsResult = await service.GetAccidentsByApplicationIdAsync(id, ct);
        if (accidentsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, accidentsResult);
        }
        app.Accidents = accidentsResult.Value ?? new List<TempReliefAccident>();

        var educationsResult = await service.GetEducationsByApplicationIdAsync(id, ct);
        if (educationsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, educationsResult);
        }
        app.Educations = educationsResult.Value ?? new List<TempReliefEducation>();

        var members = membersResult.Value ?? new List<TempReliefMember>();
        var (acceptanceDate, investigationDate) =
            await TempReliefPrintDataBuilder.ResolveScheduleAsync(timeline, app);

        var phoneResult = await service.GetReportUnitPhoneAsync(app.ReportUnit, ct);
        var contactPhone = phoneResult.IsSuccess ? phoneResult.Value ?? string.Empty : string.Empty;

        var fields = TempReliefPrintDataBuilder.BuildSingleFields(
            app, members, contactPhone, acceptanceDate, investigationDate);

        var classification = ArchiveCategoryResolver
            .GetRecordCategories(TempReliefConstants.BusinessType, app.ReliefType)
            .FirstOrDefault() ?? app.ReliefType;

        return Results.Json(new TempReliefPrintDataResponse(
            app.Id,
            app.ApplicationNo ?? string.Empty,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            DataMasker.MaskIdCard(app.ApplicantIdCard ?? string.Empty),
            app.ReliefType ?? string.Empty,
            TempReliefConstants.GetCategoryDisplayName(app.ReliefType, app.PopulationStatus, app.PaymentChannel),
            classification,
            acceptanceDate,
            investigationDate,
            fields,
            Array.Empty<Dictionary<string, string>>(),
            members.Select(ToMemberDto).ToList()));
    }

    private static async Task<IResult> RecognizeIdCardAsync(
        long? id,
        IFormFile? file,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        CancellationToken ct,
        string? category = null)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category)
            ? PhotoCategoryConstants.ID_CARD
            : category.Trim();
        if (!string.Equals(normalizedCategory, PhotoCategoryConstants.ID_CARD, StringComparison.Ordinal))
        {
            return Invalid(context, "OCR 类别仅支持 ID_CARD（身份证）");
        }

        if (id is <= 0)
        {
            return Invalid(context, "申请 ID 无效");
        }

        if (id is > 0)
        {
            var appResult = await service.GetByIdAsync(id.Value, ct);
            if (appResult.IsFailure || appResult.Value is null)
            {
                return appResult.IsFailure
                    ? ApiErrors.FromResult(context, appResult)
                    : NotFound(context, "临时救助申请不存在");
            }
        }

        if (file is null || file.Length <= 0)
        {
            return Invalid(context, "请选择要识别的身份证图片");
        }

        if (file.Length > MaxOcrUploadBytes)
        {
            return Invalid(context, "身份证图片不能超过 10MB");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType)
            || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(context, "仅支持图片文件");
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerService>();
        var ocr = new ElderlyOcrService(logger);

        await using var stream = file.OpenReadStream();
        var result = await ocr.RecognizeIdCardAsync(stream, ct);
        if (result.IsFailure || result.Value is null)
        {
            return MapOcrResult(context, result);
        }

        var value = result.Value;
        return Results.Json(new TempReliefOcrResponse(
            normalizedCategory,
            value.Name ?? string.Empty,
            value.IdCard ?? string.Empty,
            value.Gender ?? string.Empty,
            value.BirthDate,
            value.Address ?? string.Empty,
            value.Fields ?? new Dictionary<string, string>()));
    }

    private static async Task<IResult> GetPhotosAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        string? category)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var photoService = CreatePhotoService(context);
        var result = await photoService.ListAsync(id, userId, category, ct);
        if (result.IsFailure)
        {
            return MapPhotoResult(context, result);
        }

        return Results.Json(new TempReliefPhotoListResponse(result.Value ?? []));
    }

    private static async Task<IResult> UploadPhotoAsync(
        long id,
        IFormFile? file,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        string? category,
        string? photoName)
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

        if (file is null || file.Length <= 0)
        {
            return Invalid(context, "请选择要上传的照片");
        }

        if (file.Length > TempReliefPhotoService.MaxPhotoBytes)
        {
            return Invalid(context, "照片不能超过 10MB");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType)
            || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(context, "仅支持图片文件");
        }

        using var memory = new MemoryStream();
        await using (var stream = file.OpenReadStream())
        {
            await stream.CopyToAsync(memory, ct);
        }

        var photoService = CreatePhotoService(context);
        var result = await photoService.AddAsync(
            id,
            user.Id,
            OperatorName(user),
            category,
            photoName,
            memory.ToArray(),
            file.ContentType,
            ct);

        return result.IsFailure
            ? MapPhotoResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> DeletePhotoAsync(
        long id,
        long photoId,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMP_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var photoService = CreatePhotoService(context);
        var result = await photoService.DeleteAsync(id, photoId, userId, ct);

        return result.IsFailure
            ? MapPhotoResult(context, result)
            : Results.NoContent();
    }

    // ───────────────────────────── 写接口 ─────────────────────────────

    private static async Task<IResult> CreateAsync(
        TempReliefSaveRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ITempReliefService service,
        IBusinessTimelineService timeline,
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

        var validation = ValidateRequest(request, create: true);
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
            request, existing: null, service, timeline, OperatorName(user), ct);
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
        IBusinessTimelineService timeline,
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
        if (existing.Status is not (TempReliefConstants.StatusDraft or TempReliefConstants.StatusReturned))
        {
            return Conflict(context, ErrorCodes.INVALID_TRANSITION, "仅草稿或退回补正状态可编辑");
        }

        // 更新时前端可能不重复提交来源信息：沿用库内来源并照常做来源身份证复核，
        // 防止改身份证绕过「必须从检索结果选择」的服务端闸门。
        if (string.IsNullOrWhiteSpace(request.SourceTable)
            && !string.IsNullOrWhiteSpace(existing.SourceTable))
        {
            request.SourceTable = existing.SourceTable;
            if (request.SourceFamilyId <= 0)
            {
                request.SourceFamilyId = existing.SourceFamilyId;
            }
        }

        var validation = ValidateRequest(request, create: false);
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
            request, existing, service, timeline, OperatorName(user), ct);
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

    private static async Task<IResult> SubmitAsync(
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

        var result = await service.SubmitAsync(id, OperatorName(user), ct);
        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> ReturnAsync(
        long id,
        TempReliefReasonRequest? request,
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

        var reason = request?.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Invalid(context, "请填写退回补正原因");
        }

        var result = await service.ReturnAsync(id, reason, OperatorName(user), ct);
        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> RejectAsync(
        long id,
        TempReliefRejectRequest? request,
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

        var reason = request?.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Invalid(context, "请填写不予救助理由");
        }

        var result = await service.RejectAsync(
            id,
            reason,
            OperatorName(user),
            request?.NotifiedAt,
            string.IsNullOrWhiteSpace(request?.NoticeMethod) ? "书面" : request.NoticeMethod.Trim(),
            ct);

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

    private static async Task<IResult> MarkFiledAsync(
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

        var result = await service.MarkFiledAsync(id, OperatorName(user), ct);
        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> ChangeApplyDateAsync(
        long id,
        TempReliefChangeDateRequest? request,
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

        if (request?.ApplyDate is not { } newDate)
        {
            return Invalid(context, "请提供新的申请日期");
        }

        if (!TempReliefConstants.IsApplyDateWithinRange(newDate, DateTime.Today))
        {
            var upper = TempReliefConstants.GetApplyDateUpperBound(DateTime.Today);
            return Invalid(context,
                $"申请日期最多只能提前 {TempReliefConstants.ApplyDateFutureMonths} 个月（最晚 {upper:yyyy-MM-dd}）");
        }

        var result = await service.ChangeApplyDateAsync(id, newDate, OperatorName(user), ct);
        return result.IsFailure
            ? MapWriteResult(context, result)
            : Results.Json(new TempReliefChangeDateResponse(result.Value ?? string.Empty));
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

    private static string? ValidateRequest(TempReliefSaveRequest request, bool create)
    {
        var sourceTable = request.SourceTable?.Trim() ?? string.Empty;
        if (sourceTable.Length > 0)
        {
            if (!TempReliefConstants.IsAllowedSourceTable(sourceTable))
            {
                return "申请人来源表不在白名单内";
            }

            if (request.SourceFamilyId <= 0)
            {
                return "申请人必须从检索结果中选择";
            }
        }
        else if (request.SourceFamilyId > 0)
        {
            return "手工登记不应携带来源记录 ID";
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
            return "救助类型仅支持常规救助（Large）或小额快救（Small）";
        }

        if (!string.IsNullOrWhiteSpace(request.PopulationStatus)
            && request.PopulationStatus.Trim() is not (TempReliefConstants.PopulationInsured
                or TempReliefConstants.PopulationUninsured))
        {
            return "populationStatus 仅支持 Insured / Uninsured";
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentChannel)
            && request.PaymentChannel.Trim() is not (TempReliefConstants.PaymentCounty
                or TempReliefConstants.PaymentReserve))
        {
            return "paymentChannel 仅支持 County / Reserve";
        }

        var applyDate = (request.ApplyDate ?? DateTime.Today).Date;
        if (!TempReliefConstants.IsApplyDateWithinRange(applyDate, DateTime.Today))
        {
            var upper = TempReliefConstants.GetApplyDateUpperBound(DateTime.Today);
            return $"申请日期最多只能提前 {TempReliefConstants.ApplyDateFutureMonths} 个月（最晚 {upper:yyyy-MM-dd}）";
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

            if (request.Diseases.Any(d => d.SelfPaid.HasValue && d.SelfPaid.Value < 0
                || d.MedicalTotal.HasValue && d.MedicalTotal.Value < 0
                || d.InsurancePaid.HasValue && d.InsurancePaid.Value < 0))
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

        if (reliefType == TempReliefConstants.ReliefTypeSmall)
        {
            if (ResolveTierCode(request.StandardTier, request.SmallAmountLevel) is null)
            {
                return "请选择小额快救档位（50%/75%/100%）";
            }
        }
        else
        {
            var channel = TempReliefConstants.ResolvePaymentChannel(reliefType, request.PaymentChannel);
            if (channel == TempReliefConstants.PaymentReserve
                && request.ConfirmAmount is null or <= 0)
            {
                return "备用金发放请填写救助金额";
            }
        }

        if (reliefType != TempReliefConstants.ReliefTypeSmall
            && request.PublicizeStartDate.HasValue != request.PublicizeEndDate.HasValue)
        {
            return "公示开始与结束日期需同时填写";
        }

        if (request.PublicizeStartDate.HasValue && request.PublicizeEndDate.HasValue)
        {
            if (request.PublicizeEndDate.Value.Date < request.PublicizeStartDate.Value.Date)
            {
                return "公示结束日期不能早于开始日期";
            }

            if (reliefType != TempReliefConstants.ReliefTypeSmall
                && (request.PublicizeEndDate.Value.Date - request.PublicizeStartDate.Value.Date).Days != 2)
            {
                return "公示期须为 3 天（公示止 = 公示起 + 2 天）";
            }
        }

        return null;
    }

    /// <summary>
    /// 服务端复核「申请人只能从候选人检索选择」：来源表 + 来源家庭 ID 必须能取到候选人，
    /// 且候选人身份证与提交的申请人身份证一致。非在册手工登记（无来源）不适用。
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
            IBusinessTimelineService timeline,
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

        // 非在册手工登记：无来源表/来源记录，source_type=Manual。
        if (string.IsNullOrEmpty(sourceTable) || sourceFamilyId <= 0)
        {
            sourceTable = string.Empty;
            sourceFamilyId = 0;
        }

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

        var familySize = TempReliefConstants.ResolveFamilySize(
            request.FamilySize ?? existing?.FamilySize, effectiveMemberCount);

        var sourceType = sourceTable.Length == 0
            ? TempReliefConstants.SourceManual
            : string.Equals(sourceTable, TempReliefConstants.CurrentSourceTable, StringComparison.Ordinal)
                ? TempReliefConstants.SourceApplication
                : TempReliefConstants.SourceImportTab;

        var paymentChannel = TempReliefConstants.ResolvePaymentChannel(
            reliefType, request.PaymentChannel ?? existing?.PaymentChannel);

        var populationStatus = !string.IsNullOrWhiteSpace(request.PopulationStatus)
            && request.PopulationStatus.Trim() is TempReliefConstants.PopulationInsured
                or TempReliefConstants.PopulationUninsured
            ? request.PopulationStatus.Trim()
            : !string.IsNullOrWhiteSpace(existing?.PopulationStatus)
                ? existing!.PopulationStatus
                : TempReliefConstants.ResolvePopulationStatus(sourceType, sourceTable);

        // 公示期由 Core 逐笔曲线推导（3 天）；小额免公示。请求显式提供时以请求为准并已校验 3 天。
        DateTime? publicizeStart;
        DateTime? publicizeEnd;
        if (reliefType == TempReliefConstants.ReliefTypeSmall)
        {
            publicizeStart = null;
            publicizeEnd = null;
        }
        else if (request.PublicizeStartDate.HasValue || request.PublicizeEndDate.HasValue)
        {
            publicizeStart = request.PublicizeStartDate;
            publicizeEnd = request.PublicizeEndDate;
        }
        else if (existing is not null
                 && existing.ApplyDate?.Date == applyDate
                 && existing.PublicizeStartDate.HasValue)
        {
            publicizeStart = existing.PublicizeStartDate;
            publicizeEnd = existing.PublicizeEndDate;
        }
        else
        {
            var curve = timeline.BuildTempReliefCurve(applyDate);
            publicizeStart = curve.PublicityStartDate;
            publicizeEnd = curve.PublicityEndDate;
        }

        var application = new TempReliefApplication
        {
            Id = existing?.Id ?? 0,
            ReliefType = reliefType,
            PopulationStatus = populationStatus,
            PaymentChannel = paymentChannel,
            SourceType = sourceType,
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
            FamilySize = familySize,
            ReliefPersonCount = TempReliefConstants.ResolveReliefPersonCount(
                request.ReliefPersonCount, familySize),
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
            PublicizeStartDate = publicizeStart,
            PublicizeEndDate = publicizeEnd,
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

        ApplyReliefAmount(application, request, reliefType, paymentChannel);
        return (application, members, null);
    }

    /// <summary>
    /// 金额/档位仅做请求映射：小额档位码 + 可下调金额、备用金手工金额、上级发放留空；
    /// 人群状态重判、渠道归一、档位上限与备用金上限全部由 Core <c>PrepareReliefAmountsAsync</c> 校验。
    /// </summary>
    private static void ApplyReliefAmount(
        TempReliefApplication application,
        TempReliefSaveRequest request,
        string reliefType,
        string paymentChannel)
    {
        if (reliefType == TempReliefConstants.ReliefTypeSmall)
        {
            var tier = ResolveTierCode(request.StandardTier, request.SmallAmountLevel);
            application.StandardTier = tier;
            application.StandardMultiplier =
                TempReliefConstants.TierMultiplier(tier) > 0
                    ? TempReliefConstants.TierMultiplier(tier)
                    : null;
            application.SmallAmountLevel = string.IsNullOrEmpty(tier)
                ? request.SmallAmountLevel?.Trim() ?? string.Empty
                : $"{TempReliefConstants.GetTierLabel(tier)}档";
            application.ConfirmAmount = request.ConfirmAmount;
            return;
        }

        application.StandardTier = null;
        application.StandardMultiplier = null;

        if (paymentChannel == TempReliefConstants.PaymentReserve)
        {
            application.SmallAmountLevel = string.Empty;
            application.ConfirmAmount = request.ConfirmAmount;
        }
        else
        {
            // 常规·上级发放：金额由上级核定，系统不计算、不落库。
            application.SmallAmountLevel = string.Empty;
            application.ConfirmAmount = null;
        }
    }

    private static string BuildTextAddress(params string?[] parts)
        => string.Concat(parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim()));

    // ───────────────────────────── 查询辅助 ─────────────────────────────

    private static async Task<Result<PagedResult<TempReliefApplication>>> QueryBasePageAsync(
        ITempReliefService service,
        string keyword,
        string[]? statuses,
        string? populationStatus,
        string? reliefType,
        string? paymentChannel,
        int pageIndex,
        int pageSize,
        CancellationToken ct)
    {
        if (statuses is { Length: > 0 })
        {
            return await service.GetPagedByStatusesAsync(
                keyword, statuses, populationStatus, reliefType, paymentChannel,
                pageIndex, pageSize, ct);
        }

        return await service.GetPagedAsync(
            keyword, string.Empty, populationStatus, reliefType, paymentChannel,
            pageIndex, pageSize, ct);
    }

    private static bool MatchesAdvancedFilters(
        TempReliefApplication app,
        string town,
        int? year,
        int? month,
        bool overdueOnly)
    {
        if (town.Length > 0
            && !(app.Town ?? string.Empty).Contains(town, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var businessDate = app.ApplyDate ?? app.ReportTime ?? app.CreatedAt;
        if (year.HasValue && businessDate?.Year != year.Value)
        {
            return false;
        }

        if (month.HasValue && businessDate?.Month != month.Value)
        {
            return false;
        }

        if (overdueOnly && !app.IsOverdue)
        {
            return false;
        }

        return true;
    }

    private static async Task<int?> CountAsync(
        ITempReliefService service,
        string[] statuses,
        CancellationToken ct)
    {
        var result = await service.GetPagedByStatusesAsync(
            string.Empty, statuses, 1, 1, ct);
        return result.IsSuccess ? result.Value.TotalCount : null;
    }

    /// <summary>
    /// 逾期数：Core 无逾期计数接口，按待办状态集合有界扫描后按 <c>IsOverdue</c> 统计；
    /// 扫描超限或读取失败返回 null（前端显示「—」），不伪造 0。
    /// </summary>
    private static async Task<int?> CountOverdueAsync(
        ITempReliefService service,
        CancellationToken ct)
    {
        var firstResult = await service.GetPagedByStatusesAsync(
            string.Empty, PendingStatuses, 1, ScanPageSize, ct);
        if (firstResult.IsFailure || firstResult.Value is null)
        {
            return null;
        }

        var firstPage = firstResult.Value;
        if (firstPage.TotalCount > MaxScanRecords)
        {
            return null;
        }

        var count = firstPage.Items.Count(app => app.IsOverdue);
        for (var pageNo = 2; pageNo <= firstPage.TotalPages; pageNo++)
        {
            var nextResult = await service.GetPagedByStatusesAsync(
                string.Empty, PendingStatuses, pageNo, ScanPageSize, ct);
            if (nextResult.IsFailure || nextResult.Value is null)
            {
                return null;
            }

            count += nextResult.Value.Items.Count(app => app.IsOverdue);
        }

        return count;
    }

    private static string[]? NormalizeStatusFilter(string? status, out string? error)
    {
        error = null;
        var raw = (status ?? string.Empty).Trim();
        if (raw.Length == 0 || string.Equals(raw, "All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(raw, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            return PendingStatuses;
        }

        if (string.Equals(raw, "Archived", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "Done", StringComparison.OrdinalIgnoreCase))
        {
            return ArchivedStatuses;
        }

        var allowed = new[]
        {
            TempReliefConstants.StatusDraft,
            TempReliefConstants.StatusSubmitted,
            TempReliefConstants.StatusReturned,
            TempReliefConstants.StatusConfirmed,
            TempReliefConstants.StatusRejected,
            TempReliefConstants.StatusStopped
        };

        var statuses = raw
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var invalid = statuses.FirstOrDefault(s => Array.IndexOf(allowed, s) < 0);
        if (invalid is not null)
        {
            error = "status 仅支持 Draft/Submitted/Returned/Confirmed/Rejected/Stopped"
                    + "（或 Pending / Archived 分组）";
            return null;
        }

        return statuses.Length == 0 ? null : statuses;
    }

    private static string? NormalizePopulationStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim() is TempReliefConstants.PopulationInsured or TempReliefConstants.PopulationUninsured
            ? value.Trim()
            : null;
    }

    private static string? NormalizeReliefType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim() is TempReliefConstants.ReliefTypeLarge or TempReliefConstants.ReliefTypeSmall
            ? value.Trim()
            : null;
    }

    private static string? NormalizePaymentChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim() is TempReliefConstants.PaymentCounty or TempReliefConstants.PaymentReserve
            ? value.Trim()
            : null;
    }

    /// <summary>
    /// 小额档位码解析：优先 standardTier（S50/S75/S100），兼容旧前端 smallAmountLevel 的
    /// 「50%档 / 75%档 / 100%档」展示文本；解析不到返回 null 由校验显式拒绝。
    /// </summary>
    private static string? ResolveTierCode(string? standardTier, string? smallAmountLevel)
    {
        var tier = (standardTier ?? string.Empty).Trim();
        if (Array.IndexOf(TempReliefConstants.SmallTierCodes, tier) >= 0)
        {
            return tier;
        }

        var label = (smallAmountLevel ?? string.Empty).Trim();
        if (label.Length == 0) return null;
        if (label.Contains("100", StringComparison.Ordinal)) return TempReliefConstants.TierSmall100;
        if (label.Contains("75", StringComparison.Ordinal)) return TempReliefConstants.TierSmall75;
        if (label.Contains("50", StringComparison.Ordinal)) return TempReliefConstants.TierSmall50;
        return null;
    }

    // ───────────────────────────── DTO 映射 ─────────────────────────────

    private static TempReliefListItemDto ToListItem(TempReliefApplication app) => new(
        app.Id,
        app.ApplicationNo ?? string.Empty,
        app.ApplicantName ?? string.Empty,
        app.ApplicantIdCard ?? string.Empty,
        DataMasker.MaskIdCard(app.ApplicantIdCard ?? string.Empty),
        app.ReliefType ?? string.Empty,
        TempReliefConstants.GetReliefTypeName(app.ReliefType),
        app.PopulationStatus ?? string.Empty,
        TempReliefConstants.GetPopulationStatusName(app.PopulationStatus),
        app.PaymentChannel ?? string.Empty,
        TempReliefConstants.GetPaymentChannelName(app.PaymentChannel),
        TempReliefConstants.GetCategoryDisplayName(app.ReliefType, app.PopulationStatus, app.PaymentChannel),
        app.FamilyCategory ?? string.Empty,
        app.Town ?? string.Empty,
        app.Village ?? string.Empty,
        app.ApplyDate,
        app.ConfirmAmount,
        app.Status ?? string.Empty,
        TempReliefConstants.GetStatusName(app.Status),
        app.DeadlineText,
        app.DeadlineDate,
        app.IsOverdue,
        app.StoppedAt,
        app.StopReason ?? string.Empty,
        app.ReportUnit ?? string.Empty,
        app.CreatedAt,
        app.ReliefPersonCount,
        app.FilingStatus,
        app.FilingStatus switch
        {
            TempReliefConstants.FilingPending => "待备案",
            TempReliefConstants.FilingFiled => "已备案",
            _ => string.Empty
        },
        app.CanSubmit,
        app.CanReturn,
        app.CanReject,
        app.Status is TempReliefConstants.StatusDraft or TempReliefConstants.StatusSubmitted,
        app.CanMarkFiled,
        app.CanMarkFiled);

    private static TempReliefMemberDto ToMemberDto(TempReliefMember member) => new(
        member.Id,
        member.MemberName ?? string.Empty,
        member.Gender ?? string.Empty,
        member.Relation ?? string.Empty,
        member.IdCard ?? string.Empty,
        member.WorkUnit ?? string.Empty,
        member.AnnualIncome,
        member.SortOrder);

    private static TempReliefDetailResponse ToDetail(
        TempReliefApplication app,
        IReadOnlyList<TempReliefMember> members,
        IReadOnlyList<TempReliefDisease> diseases,
        IReadOnlyList<TempReliefAccident> accidents,
        IReadOnlyList<TempReliefEducation> educations)
    {
        var isSmall = string.Equals(
            app.ReliefType, TempReliefConstants.ReliefTypeSmall, StringComparison.Ordinal);
        var isCounty = string.Equals(
            app.PaymentChannel, TempReliefConstants.PaymentCounty, StringComparison.Ordinal);
        var isUninsured = string.Equals(
            app.PopulationStatus, TempReliefConstants.PopulationUninsured, StringComparison.Ordinal);

        int? publicizeDays = app.PublicizeStartDate.HasValue && app.PublicizeEndDate.HasValue
            ? Math.Max((app.PublicizeEndDate.Value.Date - app.PublicizeStartDate.Value.Date).Days + 1, 0)
            : null;

        var canConfirm = app.Status is TempReliefConstants.StatusDraft
            or TempReliefConstants.StatusSubmitted;

        var filingStatusText = app.FilingStatus switch
        {
            TempReliefConstants.FilingFiled => "已备案",
            TempReliefConstants.FilingPending => "待备案",
            _ => isCounty && app.Status == TempReliefConstants.StatusConfirmed ? "待备案" : string.Empty
        };

        return new TempReliefDetailResponse
        {
            Id = app.Id,
            ApplicationNo = app.ApplicationNo ?? string.Empty,
            ReliefType = app.ReliefType ?? string.Empty,
            ReliefTypeText = TempReliefConstants.GetReliefTypeName(app.ReliefType),
            PopulationStatus = app.PopulationStatus ?? string.Empty,
            PopulationStatusText = TempReliefConstants.GetPopulationStatusName(app.PopulationStatus),
            PaymentChannel = app.PaymentChannel ?? string.Empty,
            PaymentChannelText = TempReliefConstants.GetPaymentChannelName(app.PaymentChannel),
            CategoryDisplayName = TempReliefConstants.GetCategoryDisplayName(
                app.ReliefType, app.PopulationStatus, app.PaymentChannel),
            ApplyYear = app.ApplyYear ?? string.Empty,
            SourceType = app.SourceType ?? string.Empty,
            SourceName = TempReliefConstants.GetSourceName(app.SourceType),
            SourceTable = app.SourceTable ?? string.Empty,
            SourceFamilyId = app.SourceFamilyId,
            IsManualEntry = string.Equals(
                app.SourceType, TempReliefConstants.SourceManual, StringComparison.Ordinal),
            HukouType = app.HukouType ?? string.Empty,
            ApplicantName = app.ApplicantName ?? string.Empty,
            ApplicantIdCard = app.ApplicantIdCard ?? string.Empty,
            Gender = app.Gender ?? string.Empty,
            Age = app.Age,
            Phone = app.Phone ?? string.Empty,
            BeneficiaryName = app.BeneficiaryName,
            BeneficiaryIdCard = app.BeneficiaryIdCard,
            BeneficiaryGender = app.BeneficiaryGender,
            BeneficiaryAge = app.BeneficiaryAge,
            BeneficiaryRelation = app.BeneficiaryRelation,
            FamilyProvince = app.FamilyProvince ?? string.Empty,
            FamilyCity = app.FamilyCity ?? string.Empty,
            FamilyDistrict = app.FamilyDistrict ?? string.Empty,
            FamilyTown = app.FamilyTown ?? string.Empty,
            FamilyDetail = app.FamilyDetail ?? string.Empty,
            FamilyAddress = app.FamilyAddress ?? string.Empty,
            HukouProvince = app.HukouProvince ?? string.Empty,
            HukouCity = app.HukouCity ?? string.Empty,
            HukouDistrict = app.HukouDistrict ?? string.Empty,
            HukouTown = app.HukouTown ?? string.Empty,
            HukouAddress = app.HukouAddress ?? string.Empty,
            Town = app.Town ?? string.Empty,
            Village = app.Village ?? string.Empty,
            FamilySize = app.FamilySize,
            ReliefPersonCount = app.ReliefPersonCount,
            FamilyCategory = app.FamilyCategory ?? string.Empty,
            BankName = app.BankName ?? string.Empty,
            BankAccount = app.BankAccount ?? string.Empty,
            PolicyEnjoyed = app.PolicyEnjoyed ?? string.Empty,
            FamilyMemberStatus = app.FamilyMemberStatus ?? string.Empty,
            DifficultyReason = app.DifficultyReason ?? string.Empty,
            DifficultyType = app.DifficultyType ?? string.Empty,
            ApplyDate = app.ApplyDate,
            ReportUnit = app.ReportUnit ?? string.Empty,
            ReportTime = app.ReportTime,
            SmallAmountLevel = app.SmallAmountLevel ?? string.Empty,
            StandardTier = app.StandardTier,
            StandardTierLabel = string.IsNullOrWhiteSpace(app.StandardTier)
                ? string.Empty
                : TempReliefConstants.GetTierLabel(app.StandardTier),
            StandardMultiplier = app.StandardMultiplier,
            ConfirmAmount = app.ConfirmAmount,
            PublicizeStartDate = app.PublicizeStartDate,
            PublicizeEndDate = app.PublicizeEndDate,
            PublicizeRangeText = app.PublicizeRangeDisplay,
            PublicizeDays = publicizeDays,
            PublicizeRequired = !isSmall,
            VerifyResult = app.VerifyResult ?? string.Empty,
            AcceptancePerson = app.AcceptancePerson ?? string.Empty,
            Status = app.Status ?? string.Empty,
            StatusText = TempReliefConstants.GetStatusName(app.Status),
            AcceptedAt = app.AcceptedAt,
            InvestigationDoneAt = app.InvestigationDoneAt,
            DeadlineDate = app.DeadlineDate,
            DeadlineText = app.DeadlineText,
            RemainingDays = app.RemainingDays,
            IsOverdue = app.IsOverdue,
            CanSubmit = app.CanSubmit,
            CanReturn = app.CanReturn,
            CanReject = app.CanReject,
            CanConfirm = canConfirm,
            CanMarkFiled = app.CanMarkFiled,
            EconomicCheckRequired = !isSmall && isUninsured,
            CountyConfirmRequired = isCounty,
            FilingRequired = isCounty,
            CountyConfirmedAt = app.CountyConfirmedAt,
            CountyConfirmedBy = app.CountyConfirmedBy ?? string.Empty,
            FilingStatus = app.FilingStatus,
            FilingStatusText = filingStatusText,
            RejectReason = app.RejectReason,
            RejectNotifiedAt = app.RejectNotifiedAt,
            RejectNoticeMethod = app.RejectNoticeMethod,
            ConfirmedAt = app.ConfirmedAt,
            ConfirmedBy = app.ConfirmedBy ?? string.Empty,
            StoppedAt = app.StoppedAt,
            StoppedBy = app.StoppedBy ?? string.Empty,
            StopReason = app.StopReason ?? string.Empty,
            CreatedAt = app.CreatedAt,
            UpdatedAt = app.UpdatedAt,
            CreatedBy = app.CreatedBy ?? string.Empty,
            UpdatedBy = app.UpdatedBy ?? string.Empty,
            Members = members.Select(ToMemberDto).ToList(),
            Diseases = diseases.Select(d => new TempReliefDiseaseDto(
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
            Accidents = accidents.Select(a => new TempReliefAccidentDto(
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
            Educations = educations.Select(e => new TempReliefEducationDto(
                e.Id,
                e.StudentName ?? string.Empty,
                e.EducationStage ?? string.Empty,
                e.SchoolName ?? string.Empty,
                e.SchoolDuration,
                e.TuitionFee,
                e.FeeDate == DateTime.MinValue ? null : e.FeeDate,
                e.SortOrder)).ToList()
        };
    }

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

    // ───────────────────────────── 服务构造 / 通用出口 ─────────────────────────────

    /// <summary>
    /// Program.cs 未注册照片服务（避免改启动装配）；按请求作用域依赖就地构造，
    /// 与高龄端 CreateQueryService 同模式。
    /// </summary>
    private static TempReliefPhotoService CreatePhotoService(HttpContext context)
    {
        var services = context.RequestServices;
        return new TempReliefPhotoService(
            services.GetRequiredService<IDatabaseService>(),
            services.GetRequiredService<DataScopeFilterBuilder>(),
            services.GetRequiredService<ILoggerService>());
    }

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

        if (result.ErrorCode is ErrorCodes.ANNUAL_LIMIT_EXCEEDED
            or ErrorCodes.INVALID_TRANSITION
            or ErrorCodes.CONCURRENCY_CONFLICT
            or ErrorCodes.DB_UNIQUE_VIOLATION)
        {
            return Conflict(context, result.ErrorCode, result.Message ?? "当前状态不允许该操作");
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

    private static IResult MapPhotoResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND or ErrorCodes.NOT_FOUND)
        {
            return NotFound(context, result.Message ?? "临时救助申请不存在或无权访问");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED or ErrorCodes.REQUIRED_FIELD_MISSING)
        {
            return Invalid(context, result.Message ?? "照片操作参数校验失败");
        }

        return ApiErrors.FromResult(context, result);
    }

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
