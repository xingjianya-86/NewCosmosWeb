using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 数据中心 → 数据字典管理接口。
/// 读取要求 DICTIONARY_VIEW，写入/同步/初始化要求 DICTIONARY_MANAGE；登录由 EndpointSecurity 统一校验。
/// 业务读写全部走 Core <see cref="IDictionaryService"/>，本层只做参数校验、DTO 映射与权限判定。
/// 字典采用「种子表 + 更新表」双表视图：新增记录需要沿用视图 Id 体系，
/// 服务端在新增时按当前最大 Id + 1 分配（与桌面端 DictionaryManagementViewModel 的做法一致，避免 Id=0 冲突）。
/// </summary>
public static class DictionaryEndpoints
{
    private const string Prefix = "/api/system/dictionary";

    private const int MaxCategoryKeyLength = 100;
    private const int MaxDisplayNameLength = 100;
    private const int MaxCategoryDescriptionLength = 2000;
    private const int MaxItemKeyLength = 100;
    private const int MaxItemValueLength = 500;
    private const int MaxItemDescriptionLength = 200;

    public static IEndpointRouteBuilder MapDictionaryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Prefix);

        group.MapGet("/categories", GetCategoriesAsync);
        group.MapPost("/categories", CreateCategoryAsync);
        group.MapPut("/categories/{id:int}", UpdateCategoryAsync);
        group.MapDelete("/categories/{id:int}", DeleteCategoryAsync);

        group.MapGet("/items", GetItemsAsync);
        group.MapPost("/items", CreateItemAsync);
        group.MapPut("/items/{id:int}", UpdateItemAsync);
        group.MapDelete("/items/{id:int}", DeleteItemAsync);

        group.MapPost("/sync-schema", SyncSchemaAsync);
        group.MapPost("/seed", SeedAsync);

        return app;
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    /// <summary>GET /api/system/dictionary/categories — 全部启用中的字典分类。</summary>
    private static async Task<IResult> GetCategoriesAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
        CancellationToken ct)
    {
        if (await RequireViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await dictionaries.GetCategoriesAsync(ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = (result.Value ?? new List<DictCategoryView>())
            .Select(MapCategory)
            .ToList();

        return Results.Json(items);
    }

    /// <summary>GET /api/system/dictionary/items?category=Gender — 指定分类下的启用字典项。</summary>
    private static async Task<IResult> GetItemsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
        CancellationToken ct,
        string? category)
    {
        if (await RequireViewAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var categoryKey = category?.Trim() ?? string.Empty;
        if (categoryKey.Length == 0)
        {
            return Invalid(context, "字典分类键不能为空");
        }

        if (categoryKey.Length > MaxCategoryKeyLength)
        {
            return Invalid(context, $"字典分类键长度不能超过 {MaxCategoryKeyLength} 个字符");
        }

        var result = await dictionaries.GetItemsByCategoryAsync(categoryKey, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = (result.Value ?? new List<DictItemView>())
            .Select(MapItem)
            .ToList();

        return Results.Json(items);
    }

    // ───────────────────────────── 分类写入 ─────────────────────────────

    /// <summary>POST /api/system/dictionary/categories — 新增字典分类，Id 由服务端分配。</summary>
    private static async Task<IResult> CreateCategoryAsync(
        DictCategoryRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
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

        var validation = NormalizeCategoryRequest(request, null, out var category);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        var listResult = await dictionaries.GetCategoriesAsync(ct);
        if (listResult.IsFailure)
        {
            return ApiErrors.FromResult(context, listResult);
        }

        var categories = listResult.Value ?? new List<DictCategoryView>();
        if (categories.Any(c => string.Equals(c.Category, category.Category, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(context, $"字典分类键「{category.Category}」已存在");
        }

        var nextId = categories.Count == 0 ? 1 : categories.Max(c => c.Id) + 1;
        category.Id = nextId;
        category.CreatedBy = context.GetUserId() ?? 0;

        var result = await dictionaries.CreateCategoryAsync(category, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(
            new DictionaryCreatedResponse(nextId, "字典分类已创建"),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>PUT /api/system/dictionary/categories/{id} — 编辑字典分类（可改名/停用）。</summary>
    private static async Task<IResult> UpdateCategoryAsync(
        int id,
        DictCategoryRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
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

        var listResult = await dictionaries.GetCategoriesAsync(ct);
        if (listResult.IsFailure)
        {
            return ApiErrors.FromResult(context, listResult);
        }

        var categories = listResult.Value ?? new List<DictCategoryView>();
        var existing = categories.FirstOrDefault(c => c.Id == id);
        if (existing is null)
        {
            return NotFound(context, "字典分类不存在");
        }

        var validation = NormalizeCategoryRequest(request, existing, out var category);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        if (categories.Any(c => c.Id != id
                && string.Equals(c.Category, category.Category, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(context, $"字典分类键「{category.Category}」已存在");
        }

        category.Id = id;
        category.CreatedBy = context.GetUserId() ?? 0;

        var result = await dictionaries.UpdateCategoryAsync(category, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new DictionaryActionResult("字典分类已更新"));
    }

    /// <summary>DELETE /api/system/dictionary/categories/{id} — 软删除分类（前端已做二次确认）。</summary>
    private static async Task<IResult> DeleteCategoryAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var listResult = await dictionaries.GetCategoriesAsync(ct);
        if (listResult.IsFailure)
        {
            return ApiErrors.FromResult(context, listResult);
        }

        var categories = listResult.Value ?? new List<DictCategoryView>();
        if (categories.All(c => c.Id != id))
        {
            return NotFound(context, "字典分类不存在");
        }

        var result = await dictionaries.DeleteCategoryAsync(id, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new DictionaryActionResult("字典分类已删除"));
    }

    // ───────────────────────────── 字典项写入 ─────────────────────────────

    /// <summary>POST /api/system/dictionary/items — 新增字典项，Id 由服务端分配。</summary>
    private static async Task<IResult> CreateItemAsync(
        DictItemRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
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

        var validation = NormalizeItemRequest(request, null, out var item);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        var categoriesResult = await dictionaries.GetCategoriesAsync(ct);
        if (categoriesResult.IsFailure)
        {
            return ApiErrors.FromResult(context, categoriesResult);
        }

        var category = (categoriesResult.Value ?? new List<DictCategoryView>())
            .FirstOrDefault(c => string.Equals(c.Category, item.Category, StringComparison.Ordinal));
        if (category is null)
        {
            return NotFound(context, $"字典分类「{item.Category}」不存在");
        }

        var allItemsResult = await dictionaries.GetAllItemsAsync(ct);
        if (allItemsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, allItemsResult);
        }

        var allItems = allItemsResult.Value ?? new List<DictItemView>();
        if (allItems.Any(i => string.Equals(i.Category, item.Category, StringComparison.Ordinal)
                && string.Equals(i.ItemKey, item.ItemKey, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(context, $"字典项键「{item.ItemKey}」已存在");
        }

        // 种子数据 Id 按「分类 Id × 1000 + 序号」编址；先取全局最大 + 1，保证新 Id 不与现有项冲突。
        var nextId = Math.Max(
            allItems.Count == 0 ? 0 : allItems.Max(i => i.Id) + 1,
            category.Id * 1000 + 1);
        item.Id = nextId;
        item.CreatedBy = context.GetUserId() ?? 0;

        var result = await dictionaries.CreateItemAsync(item, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(
            new DictionaryCreatedResponse(nextId, "字典项已创建"),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>PUT /api/system/dictionary/items/{id} — 编辑字典项（键/值/排序/描述/启停）。</summary>
    private static async Task<IResult> UpdateItemAsync(
        int id,
        DictItemRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
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

        var allItemsResult = await dictionaries.GetAllItemsAsync(ct);
        if (allItemsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, allItemsResult);
        }

        var allItems = allItemsResult.Value ?? new List<DictItemView>();
        var existing = allItems.FirstOrDefault(i => i.Id == id);
        if (existing is null)
        {
            return NotFound(context, "字典项不存在");
        }

        var validation = NormalizeItemRequest(request, existing, out var item);
        if (validation is not null)
        {
            return Invalid(context, validation);
        }

        var categoriesResult = await dictionaries.GetCategoriesAsync(ct);
        if (categoriesResult.IsFailure)
        {
            return ApiErrors.FromResult(context, categoriesResult);
        }

        var categories = categoriesResult.Value ?? new List<DictCategoryView>();
        if (categories.All(c => !string.Equals(c.Category, item.Category, StringComparison.Ordinal)))
        {
            return NotFound(context, $"字典分类「{item.Category}」不存在");
        }

        if (allItems.Any(i => i.Id != id
                && string.Equals(i.Category, item.Category, StringComparison.Ordinal)
                && string.Equals(i.ItemKey, item.ItemKey, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(context, $"字典项键「{item.ItemKey}」已存在");
        }

        item.Id = id;
        item.CreatedBy = context.GetUserId() ?? 0;

        var result = await dictionaries.UpdateItemAsync(item, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new DictionaryActionResult("字典项已更新"));
    }

    /// <summary>DELETE /api/system/dictionary/items/{id} — 软删除字典项。</summary>
    private static async Task<IResult> DeleteItemAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var allItemsResult = await dictionaries.GetAllItemsAsync(ct);
        if (allItemsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, allItemsResult);
        }

        var allItems = allItemsResult.Value ?? new List<DictItemView>();
        if (allItems.All(i => i.Id != id))
        {
            return NotFound(context, "字典项不存在");
        }

        var result = await dictionaries.DeleteItemAsync(id, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new DictionaryActionResult("字典项已删除"));
    }

    // ───────────────────────────── 同步 / 初始化 ─────────────────────────────

    /// <summary>POST /api/system/dictionary/sync-schema — 重建字典表与分类/字典项视图。</summary>
    private static async Task<IResult> SyncSchemaAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await dictionaries.SyncSchemaAsync(null, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new DictionaryActionResult("字典表结构同步完成"));
    }

    /// <summary>POST /api/system/dictionary/seed — 从 YAML 种子导入字典分类与字典项（合并写入）。</summary>
    private static async Task<IResult> SeedAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDictionaryService dictionaries,
        CancellationToken ct)
    {
        if (await RequireManageAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var result = await dictionaries.InitializeFromSeedAsync(null, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        return Results.Json(new DictionaryActionResult("字典数据初始化完成"));
    }

    // ───────────────────────────── 校验 / 映射 ─────────────────────────────

    /// <summary>校验并归一化分类写入字段；existing 非空时，未提供的字段沿用原值。</summary>
    private static string? NormalizeCategoryRequest(
        DictCategoryRequest request,
        DictCategoryView? existing,
        out DictCategoryUpdate category)
    {
        category = new DictCategoryUpdate();

        var categoryKey = (request.Category ?? existing?.Category ?? string.Empty).Trim();
        if (categoryKey.Length == 0)
        {
            return "字典分类键不能为空";
        }

        if (categoryKey.Length > MaxCategoryKeyLength)
        {
            return $"字典分类键长度不能超过 {MaxCategoryKeyLength} 个字符";
        }

        var displayName = (request.DisplayName ?? existing?.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
        {
            return "字典分类显示名称不能为空";
        }

        if (displayName.Length > MaxDisplayNameLength)
        {
            return $"字典分类显示名称长度不能超过 {MaxDisplayNameLength} 个字符";
        }

        var description = request.Description is null
            ? existing?.Description ?? string.Empty
            : request.Description.Trim();
        if (description.Length > MaxCategoryDescriptionLength)
        {
            return $"字典分类说明长度不能超过 {MaxCategoryDescriptionLength} 个字符";
        }

        var sortOrder = request.SortOrder ?? existing?.SortOrder ?? 0;
        if (sortOrder < 0)
        {
            return "排序号不能为负数";
        }

        category.Category = categoryKey;
        category.DisplayName = displayName;
        category.Description = description;
        category.SortOrder = sortOrder;
        category.IsActive = request.IsActive ?? existing?.IsActive ?? true;
        category.Source = "user";
        return null;
    }

    /// <summary>校验并归一化字典项写入字段；existing 非空时，未提供的字段沿用原值。</summary>
    private static string? NormalizeItemRequest(
        DictItemRequest request,
        DictItemView? existing,
        out DictItemUpdate item)
    {
        item = new DictItemUpdate();

        var categoryKey = (request.Category ?? existing?.Category ?? string.Empty).Trim();
        if (categoryKey.Length == 0)
        {
            return "字典分类键不能为空";
        }

        if (categoryKey.Length > MaxCategoryKeyLength)
        {
            return $"字典分类键长度不能超过 {MaxCategoryKeyLength} 个字符";
        }

        var itemKey = (request.ItemKey ?? existing?.ItemKey ?? string.Empty).Trim();
        if (itemKey.Length == 0)
        {
            return "字典项键不能为空";
        }

        if (itemKey.Length > MaxItemKeyLength)
        {
            return $"字典项键长度不能超过 {MaxItemKeyLength} 个字符";
        }

        var itemValue = request.ItemValue is null
            ? existing?.ItemValue ?? string.Empty
            : request.ItemValue.Trim();
        if (itemValue.Length > MaxItemValueLength)
        {
            return $"字典项值长度不能超过 {MaxItemValueLength} 个字符";
        }

        var description = request.Description is null
            ? existing?.Description ?? string.Empty
            : request.Description.Trim();
        if (description.Length > MaxItemDescriptionLength)
        {
            return $"字典项说明长度不能超过 {MaxItemDescriptionLength} 个字符";
        }

        var sortOrder = request.SortOrder ?? existing?.SortOrder ?? 0;
        if (sortOrder < 0)
        {
            return "排序号不能为负数";
        }

        item.Category = categoryKey;
        item.ItemKey = itemKey;
        item.ItemValue = itemValue;
        item.Description = description;
        item.SortOrder = sortOrder;
        item.IsActive = request.IsActive ?? existing?.IsActive ?? true;
        item.Source = "user";
        return null;
    }

    private static DictCategoryDto MapCategory(DictCategoryView category) => new(
        category.Id,
        category.Category,
        category.DisplayName,
        category.Description,
        category.SortOrder,
        category.IsActive,
        category.Status,
        category.Source,
        category.Action,
        category.UpdatedAt == default ? null : category.UpdatedAt,
        category.UpdatedBy > 0 ? category.UpdatedBy : null);

    private static DictItemDto MapItem(DictItemView item) => new(
        item.Id,
        item.Category,
        item.ItemKey,
        item.ItemValue,
        item.SortOrder,
        item.IsActive,
        item.Description,
        item.Status,
        item.Source,
        item.Action,
        item.UpdatedAt == default ? null : item.UpdatedAt,
        item.UpdatedBy > 0 ? item.UpdatedBy : null);

    // ───────────────────────────── 权限 / 错误 ─────────────────────────────

    private static Task<IResult?> RequireViewAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(context, permissions, PermissionCodes.DICTIONARY_VIEW, ct);

    private static Task<IResult?> RequireManageAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(context, permissions, PermissionCodes.DICTIONARY_MANAGE, ct);

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict, ApiErrorCodes.ValidationError, message);
}
