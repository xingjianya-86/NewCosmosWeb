// Core 已迁移同名 ArchivePhotoService；Program.cs 同时引入了 Server/Core 两个命名空间，
// 这里用全局别名固定为 Server 侧旧照片查询服务（Program.cs 不在本代理改动范围）。
global using ArchivePhotoService = NewCosmos.Server.Services.ArchivePhotoService;

using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 全局照片补全 / 照片中心。
/// <para>
/// 读操作（补全候选、查询、按人查看、取图、导出、OCR、类别、待归属）使用 <c>ARCHIVE_VIEW</c>；
/// 上传/删除使用 <c>ARCHIVE_MANAGE</c>；推送打印使用 <c>PRINT_FAMILYAPPLICATION</c>。
/// 所有查询先按当前登录用户的数据范围过滤，列表只返回元数据，图片一律走 content 端点。
/// </para>
/// </summary>
public static class PhotoEndpoints
{
    private const string Prefix = "/api/photos";

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxPageIndex = 100_000;

    /// <summary>补传候选受限范围扫描上限（每页 100，最多 2 万人；超出返回 scopeTruncated）。</summary>
    private const int MaxRestrictedScanPages = 200;

    /// <summary>单次导出/打印上限（与档案照片导出上限一致）。</summary>
    private const int MaxBatchPhotos = 500;

    /// <summary>单张照片上传上限 10MB（与旧版/临时救助一致）。</summary>
    private const long MaxPhotoBytes = 10L * 1024 * 1024;

    private const string StatusMissing = "未传";
    private const string StatusPartial = "部分";
    private const string StatusComplete = "已传";

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>业务域选项（与 Core PersonCte 的四域代码一致）。</summary>
    private static readonly (string Code, string Name)[] BusinessDomains =
    [
        ("ElderlyBenefits", "高龄津贴"),
        ("SocialAssistance", "低收入人口"),
        ("TempRelief", "临时救助"),
        ("AssetVerification", "资产核查"),
    ];

    /// <summary>
    /// 各业务域的常用证件类别（用于补全状态与 missingCategories）。
    /// 仅在此处维护；新增业务域时同步 BusinessDomains 与 RequiredCategoriesByDomain。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> RequiredCategoriesByDomain =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ElderlyBenefits"] =
            [
                PhotoCategoryConstants.ID_CARD,
                PhotoCategoryConstants.HOUSEHOLD_REGISTER,
                PhotoCategoryConstants.PERSON_PHOTO,
                PhotoCategoryConstants.BANK_CARD,
            ],
            ["SocialAssistance"] =
            [
                PhotoCategoryConstants.ID_CARD,
                PhotoCategoryConstants.HOUSEHOLD_REGISTER,
                PhotoCategoryConstants.INCOME_PROOF,
                PhotoCategoryConstants.MEDICAL_RECORD,
                PhotoCategoryConstants.BANK_CARD,
            ],
            ["TempRelief"] =
            [
                PhotoCategoryConstants.ID_CARD,
                PhotoCategoryConstants.HOUSEHOLD_REGISTER,
                PhotoCategoryConstants.MEDICAL_RECORD,
                PhotoCategoryConstants.BANK_CARD,
            ],
            ["AssetVerification"] =
            [
                PhotoCategoryConstants.ID_CARD,
                PhotoCategoryConstants.HOUSEHOLD_REGISTER,
                PhotoCategoryConstants.INCOME_PROOF,
                PhotoCategoryConstants.BANK_CARD,
            ],
        };

    public static IEndpointRouteBuilder MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/backfill", GetBackfillAsync);
        app.MapGet($"{Prefix}/query", QueryAsync);
        app.MapGet($"{Prefix}/person/{{idCard}}", GetPersonAsync);
        app.MapGet($"{Prefix}/{{id:long}}/content", GetContentAsync);
        app.MapPost($"{Prefix}/person/{{idCard}}", UploadAsync).DisableAntiforgery();
        app.MapPost($"{Prefix}/ocr", RecognizeIdCardAsync).DisableAntiforgery();
        app.MapDelete($"{Prefix}/{{id:long}}", DeleteAsync);
        app.MapPost($"{Prefix}/print", PrintAsync);
        app.MapGet($"{Prefix}/export", ExportAsync);
        app.MapGet($"{Prefix}/pending", GetPendingAsync);
        app.MapPost($"{Prefix}/pending", UploadPendingAsync).DisableAntiforgery();
        app.MapGet($"{Prefix}/pending/{{id:long}}/content", GetPendingContentAsync);
        app.MapPost($"{Prefix}/pending/{{id:long}}/assign", AssignPendingAsync).DisableAntiforgery();
        app.MapDelete($"{Prefix}/pending/{{id:long}}", DeletePendingAsync);
        app.MapGet($"{Prefix}/categories", GetCategoriesAsync);

        return app;
    }

    // ───────────────────────────── 补全候选 ─────────────────────────────

    /// <summary>
    /// GET /api/photos/backfill?domain=&amp;keyword=&amp;status=&amp;page=&amp;pageSize=
    /// domain 空=全部；status 空/未传/部分/已传（按 PhotoCount 与业务域常用类别数判定）。
    /// </summary>
    private static async Task<IResult> GetBackfillAsync(
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct,
        string? domain,
        string? keyword,
        string? status,
        int? page,
        int? pageSize)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var normalizedDomain = NormalizeDomain(domain);
        if (normalizedDomain is null)
        {
            return InvalidRequest(context, "domain 仅支持 ElderlyBenefits/SocialAssistance/TempRelief/AssetVerification");
        }

        if (!TryNormalizeStatus(status, out var normalizedStatus))
        {
            return InvalidRequest(context, "status 仅支持 未传/部分/已传");
        }

        var pageIndex = Math.Clamp(page ?? 1, 1, MaxPageIndex);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var scope = scopeResult.Value!;

        // 全量范围且不需要状态过滤：直接走 Core 分页，保持单一事实来源与最佳性能。
        if (!scope.Restricted && string.IsNullOrEmpty(normalizedStatus))
        {
            var result = await photos.GetBackfillCandidatesAsync(
                normalizedDomain, keyword, pageIndex, size, ct);
            if (result.IsFailure)
            {
                return MapPhotoResult(context, result);
            }

            var pageResult = result.Value;
            var items = pageResult.Items
                .Select(ToBackfillDto)
                .ToList();

            return Results.Json(new PhotoBackfillListResponse(
                items,
                pageResult.PageIndex,
                pageResult.PageSize,
                pageResult.TotalCount,
                pageResult.TotalPages,
                false,
                false,
                null));
        }

        if (scope.Restricted && scope.IdCards.Count == 0)
        {
            return Results.Json(new PhotoBackfillListResponse(
                [], pageIndex, size, 0, 1, true, false,
                "当前用户数据范围内没有可补全的档案人员"));
        }

        // 受限范围或带状态过滤：按页扫描 Core 候选，在服务端做数据范围/状态过滤后重新分页。
        var desiredStart = (long)(pageIndex - 1) * size;
        var collected = new List<PhotoBackfillCandidateDto>(size);
        var seenAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var scanPage = 1;
        var truncated = false;

        while (true)
        {
            var result = await photos.GetBackfillCandidatesAsync(
                normalizedDomain, keyword, scanPage, MaxPageSize, ct);
            if (result.IsFailure)
            {
                return MapPhotoResult(context, result);
            }

            var batch = result.Value.Items;
            foreach (var candidate in batch)
            {
                var candidateIdCard = candidate.IdCard?.Trim() ?? string.Empty;
                if (!scope.Allows(candidateIdCard))
                {
                    continue;
                }

                if (candidateIdCard.Length > 0)
                {
                    seenAllowed.Add(candidateIdCard);
                }

                if (!MatchesStatus(candidate, normalizedStatus))
                {
                    continue;
                }

                if (total >= desiredStart && collected.Count < size)
                {
                    collected.Add(ToBackfillDto(candidate));
                }

                total++;
            }

            if (batch.Count < MaxPageSize)
            {
                break;
            }

            if (scope.Restricted && seenAllowed.Count >= scope.IdCards.Count)
            {
                break;
            }

            scanPage++;
            if (scanPage > MaxRestrictedScanPages)
            {
                truncated = true;
                break;
            }
        }

        var note = truncated
            ? $"候选扫描达到 {MaxRestrictedScanPages * MaxPageSize} 条上限，结果可能不完整"
            : null;

        return Results.Json(new PhotoBackfillListResponse(
            collected,
            pageIndex,
            size,
            total,
            TotalPages(total, size),
            scope.Restricted,
            truncated,
            note));
    }

    // ───────────────────────────── 照片查询 ─────────────────────────────

    /// <summary>GET /api/photos/query?keyword=&amp;category=&amp;page=&amp;pageSize=</summary>
    private static async Task<IResult> QueryAsync(
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct,
        string? keyword,
        string? category,
        int? page,
        int? pageSize)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!string.IsNullOrWhiteSpace(category) && !IsKnownCategory(category))
        {
            return InvalidRequest(context, "照片类别不存在");
        }

        var pageIndex = Math.Clamp(page ?? 1, 1, MaxPageIndex);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var scope = scopeResult.Value!;
        if (!scope.Restricted)
        {
            var result = await photos.GetPhotosPagedAsync(keyword, category, pageIndex, size, ct);
            return result.IsFailure
                ? MapPhotoResult(context, result)
                : Results.Json(ToPhotoListResponse(result.Value));
        }

        // 受限范围：照片表本身没有区划列，按数据范围解析出的身份证集合过滤元数据。
        if (scope.IdCards.Count == 0)
        {
            return Results.Json(new PhotoListResponse([], pageIndex, size, 0, 1));
        }

        const string where = @"
WHERE ($1::text = '' OR applicant_id_card ILIKE '%' || $1::text || '%'
       OR COALESCE(photo_name, '') ILIKE '%' || $1::text || '%')
  AND ($2::text = '' OR photo_category = $2::text)
  AND applicant_id_card = ANY($3::text[])";

        var kw = keyword?.Trim() ?? string.Empty;
        var cat = category?.Trim() ?? string.Empty;
        var allowedCards = scope.IdCards.ToArray();

        var countResult = await database.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM nc_biz_archive_photos" + where,
            ct, kw, cat, allowedCards);
        if (countResult.IsFailure)
        {
            return ApiErrors.FromResult(context, countResult);
        }

        var rowsResult = await database.QueryAsync<PhotoImageRow>(
            "SELECT " + PhotoImageSql.SelectColumns + " FROM nc_biz_archive_photos" + where +
            " ORDER BY created_at DESC, id DESC LIMIT $4 OFFSET $5",
            ct, kw, cat, allowedCards, size, (pageIndex - 1) * size);
        if (rowsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, rowsResult);
        }

        var rows = rowsResult.Value ?? [];
        var total = countResult.Value;
        return Results.Json(new PhotoListResponse(
            rows.Select(ToPhotoListItem).ToList(),
            pageIndex,
            size,
            total,
            TotalPages(total, size)));
    }

    // ───────────────────────────── 按人查看 ─────────────────────────────

    /// <summary>GET /api/photos/person/{idCard}?category=&amp;domain=</summary>
    private static async Task<IResult> GetPersonAsync(
        string idCard,
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct,
        string? category,
        string? domain)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!string.IsNullOrWhiteSpace(category) && !IsKnownCategory(category))
        {
            return InvalidRequest(context, "照片类别不存在");
        }

        var normalizedDomain = NormalizeDomain(domain);
        if (normalizedDomain is null)
        {
            return InvalidRequest(context, "domain 仅支持 ElderlyBenefits/SocialAssistance/TempRelief/AssetVerification");
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var ownerResult = await photos.ResolveOwnerAsync(idCard, ct);
        if (ownerResult.IsFailure)
        {
            return MapPhotoResult(context, ownerResult);
        }

        var owner = ownerResult.Value!;
        if (!scopeResult.Value!.Allows(owner.IdCard))
        {
            // 越权与不存在统一 404，不泄露档案是否存在。
            return NotFound(context, "该人员不在当前用户数据范围内");
        }

        var photosResult = await photos.GetByPersonAsync(owner.IdCard, null, ct);
        if (photosResult.IsFailure)
        {
            return MapPhotoResult(context, photosResult);
        }

        var all = photosResult.Value ?? [];
        var visible = string.IsNullOrWhiteSpace(category)
            ? all
            : all.Where(item => string.Equals(
                item.PhotoCategory, category.Trim(), StringComparison.Ordinal)).ToList();

        var presentCategories = all
            .Select(item => item.PhotoCategory)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal);

        var requiredCodes = !string.IsNullOrEmpty(normalizedDomain)
            ? RequiredCategoriesByDomain[normalizedDomain].ToHashSet(StringComparer.Ordinal)
            : RequiredCategoriesForBusinessTypes(owner.BusinessTypes);

        var missing = requiredCodes
            .Where(code => !presentCategories.Contains(code))
            .Select(code => new PhotoCategoryOptionDto(code, PhotoCategoryConstants.GetDisplayName(code)))
            .ToList();

        var counts = PhotoCategoryConstants.All
            .Select(item => new PhotoCategoryCountDto(
                item.Code,
                item.Name,
                all.Count(photo => string.Equals(
                    photo.PhotoCategory, item.Code, StringComparison.Ordinal))))
            .ToList();

        return Results.Json(new PhotoPersonResponse(
            ToOwnerDto(owner),
            visible.Select(ToPhotoListItem).ToList(),
            counts,
            missing));
    }

    // ───────────────────────────── 取图 ─────────────────────────────

    /// <summary>GET /api/photos/{id}/content → 图片二进制（支持 Range，私有缓存 5 分钟）。</summary>
    private static async Task<IResult> GetContentAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var rowResult = await LoadPhotoRowAsync(database, id, ct);
        if (rowResult.IsFailure)
        {
            return ApiErrors.FromResult(context, rowResult);
        }

        var row = rowResult.Value;
        if (row is null || !scopeResult.Value!.Allows(row.ApplicantIdCard))
        {
            return NotFound(context, "照片不存在或无权访问");
        }

        var dataResult = await photos.GetPhotoDataAsync(id, ct);
        if (dataResult.IsFailure || dataResult.Value is not { Length: > 0 })
        {
            return NotFound(context, "照片不存在或内容为空");
        }

        context.Response.Headers.CacheControl = "private, max-age=300";
        return Results.File(
            dataResult.Value,
            PhotoFormatConstants.GetMimeType(row.ImageFormat),
            enableRangeProcessing: true);
    }

    // ───────────────────────────── 上传 ─────────────────────────────

    /// <summary>POST /api/photos/person/{idCard}（multipart: file/category/applicationId?/businessType?）</summary>
    private static async Task<IResult> UploadAsync(
        string idCard,
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct,
        IFormFile? file,
        [FromForm] string? category,
        [FromForm] long? applicationId,
        [FromForm] string? businessType)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
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
            return InvalidRequest(context, "请选择照片文件");
        }

        if (file.Length > MaxPhotoBytes)
        {
            return InvalidRequest(context, "照片不能超过 10MB");
        }

        if (!IsAllowedImageFile(file, out var fileError))
        {
            return InvalidRequest(context, fileError!);
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category)
            ? PhotoCategoryConstants.OTHER_CERT
            : category.Trim();
        if (!IsKnownCategory(normalizedCategory))
        {
            return InvalidRequest(context, "照片类别不存在");
        }

        var normalizedDomain = NormalizeDomain(businessType);
        if (normalizedDomain is null)
        {
            return InvalidRequest(context, "businessType 仅支持 ElderlyBenefits/SocialAssistance/TempRelief/AssetVerification");
        }

        if (applicationId is <= 0)
        {
            return InvalidRequest(context, "applicationId 无效");
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            userId: user.Id, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var ownerResult = await photos.ResolveOwnerAsync(idCard, ct);
        if (ownerResult.IsFailure)
        {
            return MapPhotoResult(context, ownerResult);
        }

        var owner = ownerResult.Value!;
        if (!scopeResult.Value!.Allows(owner.IdCard))
        {
            return NotFound(context, "该人员不在当前用户数据范围内");
        }

        if (!string.IsNullOrEmpty(normalizedDomain)
            && !owner.BusinessTypes
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(code => string.Equals(code, normalizedDomain, StringComparison.OrdinalIgnoreCase)))
        {
            return InvalidRequest(context, "businessType 与照片归属人的业务域不匹配");
        }

        long? resolvedApplicationId = owner.ApplicationId;
        if (applicationId is { } requestedApplicationId)
        {
            var belongs = await ApplicationBelongsToPersonAsync(
                database, requestedApplicationId, owner.IdCard, ct);
            if (belongs.IsFailure)
            {
                return ApiErrors.FromResult(context, belongs);
            }

            if (!belongs.Value)
            {
                return InvalidRequest(context, "applicationId 与照片归属人不匹配");
            }

            resolvedApplicationId = requestedApplicationId;
        }

        byte[] fileData;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, ct);
            fileData = buffer.ToArray();
        }

        var targetOwner = new PhotoOwner
        {
            IdCard = owner.IdCard,
            PersonName = owner.PersonName,
            BusinessTypes = owner.BusinessTypes,
            ApplicationId = resolvedApplicationId,
            PhotoCount = owner.PhotoCount,
        };

        // 自动水印：仅「公示核查 / 入户排查」类别。上下文只给时间/姓名/身份证；
        // 地址、村屯坐标与水印时间由 Core SavePhotoAsync → FillWatermarkContextAsync 按档案补齐，
        // 绘制失败由 Core 记 Warn 并按原图入库（不阻断保存），此处不重抄绘制逻辑。
        PhotoWatermarkInfo? watermark = null;
        if (PhotoCategoryConstants.RequiresWatermark(normalizedCategory))
        {
            watermark = new PhotoWatermarkInfo(
                CapturedAt: DateTime.Now,
                PersonName: owner.PersonName ?? string.Empty,
                IdCard: owner.IdCard);
        }

        var insertResult = await photos.InsertPhotosAsync(
            targetOwner, normalizedCategory, [fileData], watermark, ct);
        if (insertResult.IsFailure)
        {
            return MapPhotoResult(context, insertResult);
        }

        var inserted = insertResult.Value!;
        if (!inserted.HasSuccess || inserted.PhotoIds.Count == 0)
        {
            return InvalidRequest(context, inserted.FirstError ?? "照片保存失败");
        }

        var photoId = inserted.PhotoIds[0];
        var rowResult = await LoadPhotoRowAsync(database, photoId, ct);
        var row = rowResult.IsSuccess ? rowResult.Value : null;

        logger.LogBusiness("照片中心上传照片",
            ("PhotoId", photoId),
            ("Category", normalizedCategory),
            ("IdCard", DataMasker.MaskIdCard(owner.IdCard)),
            ("Size", row?.FileSize ?? fileData.LongLength));

        return Results.Json(new PhotoUploadResponse(
            photoId,
            row?.PhotoName ?? string.Empty,
            row?.FileSize ?? fileData.LongLength));
    }

    // ───────────────────────────── OCR ─────────────────────────────

    /// <summary>POST /api/photos/ocr（multipart: file）→ 身份证姓名/证号/性别/住址。</summary>
    private static async Task<IResult> RecognizeIdCardAsync(
        HttpContext context,
        INewPermissionService permissions,
        ILoggerService logger,
        CancellationToken ct,
        IFormFile? file)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (file is null || file.Length <= 0)
        {
            return InvalidRequest(context, "请选择要识别的身份证图片");
        }

        if (file.Length > MaxPhotoBytes)
        {
            return InvalidRequest(context, "身份证图片不能超过 10MB");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType)
            || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidRequest(context, "仅支持图片文件");
        }

        // 与 TempReliefEndpoints 同法：按请求构造（服务无状态，避免跨请求共享）。
        var ocr = new ElderlyOcrService(logger);
        await using var stream = file.OpenReadStream();
        var result = await ocr.RecognizeIdCardAsync(stream, ct);
        if (result.IsFailure || result.Value is null)
        {
            return MapOcrResult(context, result);
        }

        var value = result.Value;
        return Results.Json(new PhotoOcrResponse(
            value.Name ?? string.Empty,
            value.IdCard ?? string.Empty,
            value.Gender ?? string.Empty,
            value.Address ?? string.Empty));
    }

    // ───────────────────────────── 删除 ─────────────────────────────

    /// <summary>DELETE /api/photos/{id}</summary>
    private static async Task<IResult> DeleteAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var rowResult = await LoadPhotoRowAsync(database, id, ct);
        if (rowResult.IsFailure)
        {
            return ApiErrors.FromResult(context, rowResult);
        }

        var row = rowResult.Value;
        if (row is null || !scopeResult.Value!.Allows(row.ApplicantIdCard))
        {
            return NotFound(context, "照片不存在或无权访问");
        }

        var deleteResult = await photos.DeletePhotoAsync(id, ct);
        if (deleteResult.IsFailure)
        {
            return MapPhotoResult(context, deleteResult);
        }

        logger.LogBusiness("照片中心删除照片",
            ("PhotoId", id),
            ("IdCard", DataMasker.MaskIdCard(row.ApplicantIdCard ?? string.Empty)));
        return Results.NoContent();
    }

    // ───────────────────────────── 推送打印 ─────────────────────────────

    /// <summary>POST /api/photos/print</summary>
    private static async Task<IResult> PrintAsync(
        PhotoPrintRequest request,
        HttpContext context,
        INewPermissionService permissions,
        PhotoPrintService printService,
        PrinterBindingService printers,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_FAMILYAPPLICATION, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var photoIds = (request.PhotoIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (photoIds.Length == 0)
        {
            return InvalidRequest(context, "请选择要打印的照片");
        }

        if (photoIds.Length > MaxBatchPhotos)
        {
            return InvalidRequest(context, $"单次最多打印 {MaxBatchPhotos} 张照片");
        }

        if (!TryNormalizeLayout(request.Layout, out var layout))
        {
            return InvalidRequest(context, "打印版式不存在");
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            user.Id, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var targetResult = await printers.ResolveTargetAsync(
            user.Id, request.BindingId, request.PrinterName, ct);
        if (targetResult.IsFailure)
        {
            return ApiErrors.FromResult(context, targetResult);
        }

        var response = await printService.PushAsync(
            user.Id,
            string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
            photoIds,
            layout,
            targetResult.Value,
            scopeResult.Value!.Allows,
            ct);

        return Results.Json(response);
    }

    // ───────────────────────────── 导出 ─────────────────────────────

    /// <summary>GET /api/photos/export?ids=1,2,3 → application/zip</summary>
    private static async Task<IResult> ExportAsync(
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct,
        string? ids)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var photoIds = ParseIds(ids);
        if (photoIds.Length == 0)
        {
            return InvalidRequest(context, "请提供要导出的照片 ids（逗号分隔）");
        }

        if (photoIds.Length > MaxBatchPhotos)
        {
            return InvalidRequest(context, $"单次最多导出 {MaxBatchPhotos} 张照片");
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var rowsResult = await LoadPhotoRowsAsync(database, photoIds, ct);
        if (rowsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, rowsResult);
        }

        var rowById = (rowsResult.Value ?? [])
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var ownerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(PhotoImageRow Row, byte[] Data)>();

        foreach (var photoId in photoIds)
        {
            if (!rowById.TryGetValue(photoId, out var row))
            {
                continue;
            }

            if (!scopeResult.Value!.Allows(row.ApplicantIdCard))
            {
                continue;
            }

            var dataResult = await photos.GetPhotoDataAsync(photoId, ct);
            if (dataResult.IsFailure || dataResult.Value is not { Length: > 0 })
            {
                continue;
            }

            entries.Add((row, dataResult.Value));
        }

        if (entries.Count == 0)
        {
            return NotFound(context, "没有可导出的照片");
        }

        byte[] zipBytes;
        using (var buffer = new MemoryStream())
        {
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (row, data) in entries)
                {
                    var personName = await ResolveOwnerNameAsync(
                        photos, row.ApplicantIdCard, ownerNames, ct);
                    var fileName = BuildExportFileName(row, personName);
                    var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(data, ct);
                }
            }

            zipBytes = buffer.ToArray();
        }

        logger.LogBusiness("照片中心导出",
            ("Requested", photoIds.Length),
            ("Exported", entries.Count),
            ("OperatorId", userId));

        return Results.File(
            zipBytes,
            "application/zip",
            $"档案照片_{DateTime.Now:yyyyMMddHHmmss}.zip");
    }

    // ───────────────────────────── 待归属 / 类别 ─────────────────────────────

    /// <summary>
    /// GET /api/photos/pending?page=&amp;pageSize=&amp;keyword=&amp;category=
    /// 待归属照片池（PC/扫描入口写入 nc_biz_archive_photo_pending，本端点只读池 + 归属/删除）。
    /// 数据范围受限时：识别身份证在范围内，或未识别且由本人/本组织成员上传的记录才可见。
    /// </summary>
    private static async Task<IResult> GetPendingAsync(
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoPendingService pending,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        string? category)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!string.IsNullOrWhiteSpace(category) && !IsKnownCategory(category))
        {
            return InvalidRequest(context, "照片类别不存在");
        }

        var pageIndex = Math.Clamp(page ?? 1, 1, MaxPageIndex);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var scope = scopeResult.Value!;
        var filter = scope.Restricted
            ? new PendingPhotoScopeFilter(scope.IdCards, userId, scope.SharedOrganizationIds)
            : null;

        var result = await pending.GetPendingPagedAsync(
            keyword, category, pageIndex, size, filter, ct);
        if (result.IsFailure)
        {
            return MapPhotoResult(context, result);
        }

        var pageResult = result.Value;
        var message = scope.Restricted && scope.IdCards.Count == 0
            ? "当前用户数据范围内没有可归属人员，仅显示本人/本组织上传的未识别照片"
            : null;

        return Results.Json(new PhotoPendingResponse(
            pageResult.Items.Select(ToPendingDto).ToList(),
            pageResult.PageIndex,
            pageResult.PageSize,
            pageResult.TotalCount,
            pageResult.TotalPages,
            true,
            message));
    }

    /// <summary>POST /api/photos/pending（multipart: file/category/photoName?/recognizedName?/recognizedIdCard?）</summary>
    private static async Task<IResult> UploadPendingAsync(
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoPendingService pending,
        CancellationToken ct,
        IFormFile? file,
        [FromForm] string? category,
        [FromForm] string? photoName,
        [FromForm] string? recognizedName,
        [FromForm] string? recognizedIdCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (file is null || file.Length <= 0)
        {
            return InvalidRequest(context, "请选择照片文件");
        }

        if (file.Length > MaxPhotoBytes)
        {
            return InvalidRequest(context, "照片不能超过 10MB");
        }

        if (!IsAllowedImageFile(file, out var fileError))
        {
            return InvalidRequest(context, fileError!);
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category)
            ? PhotoCategoryConstants.OTHER_CERT
            : category.Trim();
        if (!IsKnownCategory(normalizedCategory))
        {
            return InvalidRequest(context, "照片类别不存在");
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, ct);

        var save = await pending.SavePendingAsync(
            buffer.ToArray(),
            normalizedCategory,
            string.IsNullOrWhiteSpace(photoName) ? Path.GetFileName(file.FileName) : photoName.Trim(),
            recognizedName?.Trim(),
            recognizedIdCard?.Trim(),
            "web",
            ct);

        if (save.IsFailure)
        {
            return MapPhotoResult(context, save);
        }

        return Results.Json(new { id = save.Value, message = "已加入待归属照片池" });
    }

    /// <summary>GET /api/photos/pending/{id}/content → 待归属照片二进制（私有缓存 5 分钟）。</summary>
    private static async Task<IResult> GetPendingContentAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoPendingService pending,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var metaResult = await pending.GetPendingMetaAsync(id, ct);
        if (metaResult.IsFailure)
        {
            return ApiErrors.FromResult(context, metaResult);
        }

        var meta = metaResult.Value;
        if (meta is null || !AllowsPending(scopeResult.Value!, meta, userId))
        {
            // 越权与不存在统一 404，不泄露待归属池是否存在。
            return NotFound(context, "待归属照片不存在或无权访问");
        }

        var dataResult = await pending.GetPendingDataAsync(id, ct);
        if (dataResult.IsFailure || dataResult.Value is not { Length: > 0 })
        {
            return NotFound(context, "待归属照片不存在或内容为空");
        }

        context.Response.Headers.CacheControl = "private, max-age=300";
        return Results.File(
            dataResult.Value,
            PhotoFormatConstants.GetMimeType(meta.ImageFormat),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// POST /api/photos/pending/{id}/assign
    /// body：{ idCard, name?, category?, applicationId? } → 写入 nc_biz_archive_photos 并删除待归属行。
    /// </summary>
    private static async Task<IResult> AssignPendingAsync(
        long id,
        PhotoPendingAssignRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoPendingService pending,
        IArchivePhotoService photos,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var idCard = request?.IdCard?.Trim() ?? string.Empty;
        if (idCard.Length == 0)
        {
            return InvalidRequest(context, "请提供归属人身份证号");
        }

        var categoryOverride = request?.Category?.Trim();
        if (!string.IsNullOrEmpty(categoryOverride) && !IsKnownCategory(categoryOverride))
        {
            return InvalidRequest(context, "照片类别不存在");
        }

        if (request?.ApplicationId is <= 0)
        {
            return InvalidRequest(context, "applicationId 无效");
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            user.Id, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var scope = scopeResult.Value!;

        // 待归属照片本身必须可见（先判 404，避免越权探测）。
        var metaResult = await pending.GetPendingMetaAsync(id, ct);
        if (metaResult.IsFailure)
        {
            return ApiErrors.FromResult(context, metaResult);
        }

        var meta = metaResult.Value;
        if (meta is null || !AllowsPending(scope, meta, user.Id))
        {
            return NotFound(context, "待归属照片不存在或无权访问");
        }

        var ownerResult = await photos.ResolveOwnerAsync(idCard, ct);
        if (ownerResult.IsFailure)
        {
            return MapPhotoResult(context, ownerResult);
        }

        var owner = ownerResult.Value!;
        if (!scope.Allows(owner.IdCard))
        {
            return NotFound(context, "该人员不在当前用户数据范围内");
        }

        long? applicationId = null;
        if (request?.ApplicationId is { } requestedApplicationId)
        {
            var belongs = await ApplicationBelongsToPersonAsync(
                database, requestedApplicationId, owner.IdCard, ct);
            if (belongs.IsFailure)
            {
                return ApiErrors.FromResult(context, belongs);
            }

            if (!belongs.Value)
            {
                return InvalidRequest(context, "applicationId 与照片归属人不匹配");
            }

            applicationId = requestedApplicationId;
        }

        var assignResult = await pending.AssignPendingAsync(
            [id], owner.IdCard, request?.Name, categoryOverride, applicationId, ct);
        if (assignResult.IsFailure)
        {
            return MapPhotoResult(context, assignResult);
        }

        var summary = assignResult.Value!;
        if (!summary.HasSuccess || summary.PhotoIds.Count == 0)
        {
            return InvalidRequest(context, summary.FirstError ?? "照片归属失败");
        }

        var photoId = summary.PhotoIds[0];
        var rowResult = await LoadPhotoRowAsync(database, photoId, ct);
        var row = rowResult.IsSuccess ? rowResult.Value : null;

        logger.LogBusiness("待归属照片归属",
            ("PendingId", id),
            ("PhotoId", photoId),
            ("Category", row?.PhotoCategory ?? meta.PhotoCategory),
            ("IdCard", DataMasker.MaskIdCard(owner.IdCard)),
            ("Size", row?.FileSize ?? meta.FileSize));

        return Results.Json(new PhotoUploadResponse(
            photoId,
            row?.PhotoName ?? string.Empty,
            row?.FileSize ?? meta.FileSize));
    }

    /// <summary>DELETE /api/photos/pending/{id} → 删除待归属照片（ARCHIVE_MANAGE）。</summary>
    private static async Task<IResult> DeletePendingAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IArchivePhotoPendingService pending,
        DataScopeFilterBuilder scopeBuilder,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var scopeResult = await ResolvePhotoScopeAsync(
            userId, dataScope, scopeBuilder, database, logger, ct);
        if (scopeResult.IsFailure)
        {
            return ScopeFailure(context, scopeResult.Message);
        }

        var metaResult = await pending.GetPendingMetaAsync(id, ct);
        if (metaResult.IsFailure)
        {
            return ApiErrors.FromResult(context, metaResult);
        }

        var meta = metaResult.Value;
        if (meta is null || !AllowsPending(scopeResult.Value!, meta, userId))
        {
            return NotFound(context, "待归属照片不存在或无权访问");
        }

        var deleteResult = await pending.DeletePendingAsync(id, ct);
        if (deleteResult.IsFailure)
        {
            return MapPhotoResult(context, deleteResult);
        }

        logger.LogBusiness("删除待归属照片",
            ("PendingId", id),
            ("Category", meta.PhotoCategory),
            ("IdCard", DataMasker.MaskIdCard(meta.RecognizedIdCard ?? string.Empty)));
        return Results.NoContent();
    }

    /// <summary>GET /api/photos/categories → 照片类别 + 业务域 + 打印版式。</summary>
    private static async Task<IResult> GetCategoriesAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        return Results.Json(new PhotoCategoriesResponse(
            PhotoCategoryConstants.All
                .Select(item => new PhotoCategoryOptionDto(item.Code, item.Name))
                .ToList(),
            BusinessDomains
                .Select(item => new PhotoBusinessDomainOptionDto(item.Code, item.Name))
                .ToList(),
            PhotoLayoutConstants.All
                .Select(item => new PhotoLayoutOptionDto(item.Code, item.Name))
                .ToList()));
    }

    // ───────────────────────────── 数据范围 ─────────────────────────────

    /// <summary>
    /// 照片数据范围：照片表无区划列，先按四业务域的可管理机构解析出身份证集合。
    /// ALL 范围不限制；其余范围（ORG/ORG_AND_CHILDREN/SELF）一律按集合过滤，解析失败显式拒绝。
    /// </summary>
    private static async Task<Result<PhotoScope>> ResolvePhotoScopeAsync(
        int userId,
        IDataScopeService dataScope,
        DataScopeFilterBuilder scopeBuilder,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        var scopeInfo = await dataScope.GetEffectiveScopeAsync(userId, ct);
        if (string.Equals(scopeInfo.Scope, DataScopeConstants.ALL, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(PhotoScope.All);
        }

        var targets = new (DataScopeTarget Target, string Alias, string Sql)[]
        {
            (DataScopeTarget.Applications, "a",
                "SELECT DISTINCT btrim(a.applicant_id_card) AS id_card FROM nc_biz_applications a " +
                "WHERE a.deleted_at IS NULL AND btrim(COALESCE(a.applicant_id_card, '')) <> ''"),
            (DataScopeTarget.ElderlyApplications, "e",
                "SELECT DISTINCT btrim(e.id_card) AS id_card FROM nc_biz_elderly_applications e " +
                "WHERE e.deleted_at IS NULL AND btrim(COALESCE(e.id_card, '')) <> ''"),
            (DataScopeTarget.TempRelief, "t",
                "SELECT DISTINCT btrim(t.applicant_id_card) AS id_card FROM nc_biz_temp_relief_applications t " +
                "WHERE t.deleted_at IS NULL AND btrim(COALESCE(t.applicant_id_card, '')) <> ''"),
            (DataScopeTarget.AssetChecks, "ac",
                "SELECT DISTINCT btrim(ac.applicant_id_card) AS id_card FROM nc_biz_asset_checks ac " +
                "WHERE ac.deleted_at IS NULL AND btrim(COALESCE(ac.applicant_id_card, '')) <> ''"),
        };

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, alias, sql) in targets)
        {
            var clause = await scopeBuilder.BuildAsync(userId, target, alias, 1, ct);
            if (clause.IsEmpty)
            {
                // 理论上只有 ALL 才会为空；此处按"该域不过滤"处理，避免误拒绝。
                continue;
            }

            var result = await database.QueryAsync<IdCardRow>(
                sql + " AND " + clause.Sql, ct, clause.Parameters.ToArray());
            if (result.IsFailure)
            {
                logger.Error(
                    $"照片数据范围解析失败: UserId={userId}, Target={target}, {result.ErrorCode} {result.Message}");
                return Result.Failure<PhotoScope>(
                    ErrorCodes.DB_QUERY_ERROR, "数据范围校验失败");
            }

            foreach (var row in result.Value ?? [])
            {
                var idCard = row.IdCard?.Trim();
                if (!string.IsNullOrEmpty(idCard))
                {
                    allowed.Add(idCard);
                }
            }
        }

        // 未识别待归属照片的组织共享范围：SELF 不共享，ORG/ORG_AND_CHILDREN 取可管理组织。
        var sharedOrgIds = new HashSet<int>();
        if (!string.Equals(scopeInfo.Scope, DataScopeConstants.SELF, StringComparison.OrdinalIgnoreCase))
        {
            var orgIds = await dataScope.GetManageableOrganizationIdsAsync(userId, ct);
            sharedOrgIds.UnionWith(orgIds);
        }

        return Result.Success(new PhotoScope(true, allowed, sharedOrgIds));
    }

    private sealed class IdCardRow
    {
        public string? IdCard { get; set; }
    }

    private sealed record PhotoScope(
        bool Restricted,
        HashSet<string> IdCards,
        HashSet<int> SharedOrganizationIds)
    {
        public static readonly PhotoScope All = new(
            false,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<int>());

        public bool Allows(string? idCard)
            => !Restricted
               || (!string.IsNullOrWhiteSpace(idCard) && IdCards.Contains(idCard.Trim()));
    }

    /// <summary>
    /// 待归属照片可见性：全量范围全可见；受限范围下识别身份证须在可管理集合内，
    /// 未识别照片仅上传人本人或其可管理组织（SELF 范围不共享）成员可见。
    /// 待归属池无区划列，按身份证 + 上传人组织归属。
    /// </summary>
    private static bool AllowsPending(PhotoScope scope, PendingArchivePhoto row, int userId)
    {
        if (!scope.Restricted)
        {
            return true;
        }

        var recognizedIdCard = row.RecognizedIdCard?.Trim();
        if (!string.IsNullOrEmpty(recognizedIdCard))
        {
            return scope.Allows(recognizedIdCard);
        }

        if (row.CreatedBy == userId)
        {
            return true;
        }

        return scope.SharedOrganizationIds.Count > 0
               && row.CreatedByOrganizationId is { } orgId
               && scope.SharedOrganizationIds.Contains(orgId);
    }

    // ───────────────────────────── 映射/校验 ─────────────────────────────

    private static PhotoBackfillCandidateDto ToBackfillDto(PhotoBackfillCandidate candidate)
    {
        var requiredCount = Math.Max(1, RequiredCategoriesForBusinessTypes(candidate.BusinessTypes).Count);
        var status = candidate.PhotoCount <= 0
            ? StatusMissing
            : candidate.PhotoCount < requiredCount ? StatusPartial : StatusComplete;

        return new(
            candidate.IdCard?.Trim() ?? string.Empty,
            candidate.PersonName,
            candidate.BusinessTypes,
            BusinessTypeDisplayNames(candidate.BusinessTypes),
            candidate.BusinessId,
            candidate.PhotoCount,
            candidate.PhotoCount > 0 ? $"已传 {candidate.PhotoCount} 张" : "未传",
            candidate.PhotoCount > 0,
            requiredCount,
            status);
    }

    private static PhotoListItemDto ToPhotoListItem(ArchivePhoto photo)
        => new(
            photo.Id,
            photo.ApplicantIdCard?.Trim() ?? string.Empty,
            photo.ApplicationId,
            photo.PhotoCategory,
            PhotoCategoryConstants.GetDisplayName(photo.PhotoCategory),
            photo.PhotoName,
            photo.FileSize,
            string.IsNullOrWhiteSpace(photo.ImageFormat)
                ? PhotoFormatConstants.Jpeg
                : photo.ImageFormat,
            photo.CreatedAt,
            photo.CreatedByName);

    private static PhotoListItemDto ToPhotoListItem(PhotoImageRow row)
        => new(
            row.Id,
            row.ApplicantIdCard?.Trim() ?? string.Empty,
            row.ApplicationId,
            row.PhotoCategory ?? PhotoCategoryConstants.OTHER_CERT,
            PhotoCategoryConstants.GetDisplayName(row.PhotoCategory),
            row.PhotoName,
            row.FileSize,
            string.IsNullOrWhiteSpace(row.ImageFormat)
                ? PhotoFormatConstants.Jpeg
                : row.ImageFormat!,
            row.CreatedAt,
            row.CreatedByName);

    private static PhotoPendingItemDto ToPendingDto(PendingArchivePhoto photo)
        => new(
            photo.Id,
            string.IsNullOrWhiteSpace(photo.PhotoCategory)
                ? PhotoCategoryConstants.OTHER_CERT
                : photo.PhotoCategory,
            PhotoCategoryConstants.GetDisplayName(photo.PhotoCategory),
            photo.PhotoName,
            photo.FileSize,
            string.IsNullOrWhiteSpace(photo.ImageFormat)
                ? PhotoFormatConstants.WebP
                : photo.ImageFormat,
            photo.RecognizedName,
            photo.RecognizedIdCard,
            photo.Source,
            photo.CreatedAt,
            photo.CreatedByName);

    private static PhotoListResponse ToPhotoListResponse(PagedResult<ArchivePhoto> page)
        => new(
            page.Items.Select(ToPhotoListItem).ToList(),
            page.PageIndex,
            page.PageSize,
            page.TotalCount,
            page.TotalPages);

    private static PhotoOwnerDto ToOwnerDto(PhotoOwner owner)
        => new(
            owner.IdCard?.Trim() ?? string.Empty,
            owner.PersonName,
            owner.BusinessTypes,
            BusinessTypeDisplayNames(owner.BusinessTypes),
            owner.ApplicationId,
            owner.PhotoCount,
            owner.HasPhoto);

    private static string BusinessTypeDisplayNames(string? businessTypes)
    {
        var parts = (businessTypes ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => BusinessDomains.FirstOrDefault(
                item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)).Name ?? code)
            .ToArray();

        return parts.Length == 0 ? string.Empty : string.Join(" · ", parts);
    }

    private static HashSet<string> RequiredCategoriesForBusinessTypes(string? businessTypes)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in (businessTypes ?? string.Empty)
                     .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RequiredCategoriesByDomain.TryGetValue(code, out var categories))
            {
                required.UnionWith(categories);
            }
        }

        return required;
    }

    /// <summary>
    /// 状态过滤（按 PhotoCount）：未传=0；部分=1..业务域常用类别数-1；
    /// 已传&gt;=业务域常用类别数。仅用于列表徽标筛选，不替代逐类别核对。
    /// </summary>
    private static bool MatchesStatus(PhotoBackfillCandidate candidate, string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return true;
        }

        var requiredCount = RequiredCategoriesForBusinessTypes(candidate.BusinessTypes).Count;
        if (requiredCount <= 0)
        {
            requiredCount = 1;
        }

        return status switch
        {
            StatusMissing => candidate.PhotoCount == 0,
            StatusPartial => candidate.PhotoCount > 0 && candidate.PhotoCount < requiredCount,
            StatusComplete => candidate.PhotoCount >= requiredCount,
            _ => true,
        };
    }

    private static bool TryNormalizeStatus(string? status, out string normalized)
    {
        normalized = (status ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        switch (normalized.ToLowerInvariant())
        {
            case "未传":
            case "missing":
            case "none":
            case "unuploaded":
                normalized = StatusMissing;
                return true;
            case "部分":
            case "partial":
                normalized = StatusPartial;
                return true;
            case "已传":
            case "complete":
            case "done":
            case "uploaded":
                normalized = StatusComplete;
                return true;
            default:
                return false;
        }
    }

    private static string? NormalizeDomain(string? domain)
    {
        var value = (domain ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        return BusinessDomains.Any(item => string.Equals(
            item.Code, value, StringComparison.OrdinalIgnoreCase))
            ? BusinessDomains.First(item => string.Equals(
                item.Code, value, StringComparison.OrdinalIgnoreCase)).Code
            : null;
    }

    private static bool IsKnownCategory(string? category)
        => PhotoCategoryConstants.All.Any(item => string.Equals(
            item.Code, category?.Trim(), StringComparison.Ordinal));

    private static bool TryNormalizeLayout(string? layout, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(layout))
        {
            return true;
        }

        var value = layout.Trim();
        if (!PhotoLayoutConstants.All.Any(item => string.Equals(
                item.Code, value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        normalized = PhotoLayoutConstants.All.First(item => string.Equals(
            item.Code, value, StringComparison.OrdinalIgnoreCase)).Code;
        return true;
    }

    private static bool IsAllowedImageFile(IFormFile file, out string? error)
    {
        error = null;

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            error = "仅支持 JPG/JPEG/PNG/WebP 图片";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType)
            && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            error = "仅支持图片文件";
            return false;
        }

        return true;
    }

    private static long[] ParseIds(string? ids)
        => (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, out var id) && id > 0 ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

    private static int TotalPages(long totalCount, int pageSize)
        => pageSize <= 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling((double)Math.Max(0, totalCount) / pageSize));

    private static async Task<Result<PhotoImageRow?>> LoadPhotoRowAsync(
        IDatabaseService database,
        long id,
        CancellationToken ct)
        => await database.QuerySingleAsync<PhotoImageRow?>(PhotoImageSql.SelectById, ct, id);

    private static async Task<Result<List<PhotoImageRow>>> LoadPhotoRowsAsync(
        IDatabaseService database,
        IReadOnlyCollection<long> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Result.Success(new List<PhotoImageRow>());
        }

        return await database.QueryAsync<PhotoImageRow>(
            PhotoImageSql.SelectByIds, ct, (object)ids.ToArray());
    }

    /// <summary>校验 applicationId 是否属于该身份证（四域任一主表存在即可）。</summary>
    private static async Task<Result<bool>> ApplicationBelongsToPersonAsync(
        IDatabaseService database,
        long applicationId,
        string idCard,
        CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(*)
FROM (
    SELECT 1 FROM nc_biz_applications
    WHERE id = $1 AND btrim(COALESCE(applicant_id_card, '')) = $2 AND deleted_at IS NULL
    UNION ALL
    SELECT 1 FROM nc_biz_elderly_applications
    WHERE id = $1 AND btrim(COALESCE(id_card, '')) = $2 AND deleted_at IS NULL
    UNION ALL
    SELECT 1 FROM nc_biz_temp_relief_applications
    WHERE id = $1 AND btrim(COALESCE(applicant_id_card, '')) = $2 AND deleted_at IS NULL
    UNION ALL
    SELECT 1 FROM nc_biz_asset_checks
    WHERE id = $1 AND btrim(COALESCE(applicant_id_card, '')) = $2 AND deleted_at IS NULL
) matched";

        var result = await database.ExecuteScalarAsync(sql, ct, applicationId, idCard.Trim());
        if (result.IsFailure)
        {
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value > 0);
    }

    private static async Task<string> ResolveOwnerNameAsync(
        IArchivePhotoService photos,
        string? idCard,
        Dictionary<string, string> cache,
        CancellationToken ct)
    {
        var trimmed = idCard?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "未知";
        }

        if (cache.TryGetValue(trimmed, out var cached))
        {
            return string.IsNullOrWhiteSpace(cached) ? "未知" : cached;
        }

        var owner = await photos.ResolveOwnerAsync(trimmed, ct);
        var name = owner.IsSuccess ? owner.Value?.PersonName?.Trim() ?? string.Empty : string.Empty;
        cache[trimmed] = name;
        return string.IsNullOrWhiteSpace(name) ? "未知" : name;
    }

    private static string BuildExportFileName(PhotoImageRow row, string personName)
    {
        var categoryName = PhotoCategoryConstants.GetDisplayName(row.PhotoCategory);
        var extension = PhotoFormatConstants.GetExtension(row.ImageFormat);
        return $"{SanitizeFileName(personName)}_{SanitizeFileName(categoryName)}_{row.Id}{extension}";
    }

    private static string SanitizeFileName(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "未知";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        if (result.Length > 60)
        {
            result = result[..60];
        }

        return result.Length == 0 ? "未知" : result;
    }

    // ───────────────────────────── 错误出口 ─────────────────────────────

    private static IResult MapPhotoResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.ARCHIVE_NOT_FOUND)
        {
            return NotFound(context, result.Message ?? "请求的资源不存在");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED or ErrorCodes.INVALID_ID_CARD)
        {
            return InvalidRequest(context, result.Message ?? "请求参数校验失败");
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
            _ => StatusCodes.Status502BadGateway,
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

    private static IResult ScopeFailure(HttpContext context, string? message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden,
            message ?? "数据范围校验失败，已拒绝访问");
}
