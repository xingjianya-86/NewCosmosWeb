using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NewCosmos.Constants;
using NewCosmos.Models.Categories;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 文书模板管理（Server 侧）：模板 CRUD、分类树、文档类型词表、文件下载与排序。
/// <para>
/// 文件与字段映射存 <c>nc_biz_templates</c>（file_data bytea / config_json jsonb）。
/// 列表不返回 file_data；下载用 <c>encode(..., 'base64')</c> 取回，避免实体映射大字段。
/// 分类树与文档类型直接复用 Core <see cref="TemplateCategoryProvider"/> / <see cref="TemplateDocTypes"/>，
/// 禁止在 Server 另抄一份词表。
/// </para>
/// </summary>
public sealed class TemplateAdminService(IDatabaseService database, ILoggerService logger)
{
    public const int MaxListRows = 500;
    public const int MinCopies = 1;
    public const int MaxCopies = 99;

    /// <summary>自定义分类不存在（端点映射 404）。</summary>
    public const string CategoryNotFound = "TEMPLATE_CATEGORY_NOT_FOUND";

    /// <summary>自定义分类下仍有子分类（端点映射 409）。</summary>
    public const string CategoryHasChildren = "TEMPLATE_CATEGORY_HAS_CHILDREN";

    /// <summary>自定义分类被模板引用（端点映射 409）。</summary>
    public const string CategoryInUse = "TEMPLATE_CATEGORY_IN_USE";

    /// <summary>同级已存在同名分类 / path 重复（端点映射 409）。</summary>
    public const string CategoryDuplicate = "TEMPLATE_CATEGORY_DUPLICATE";

    private const int MaxCategoryLabelLength = 120;
    private const int MaxCategoryPathLength = 400;
    private const int MaxCategoryParentPathLength = 300;
    private const string LegacyCategoryRootPath = "__legacy_categories__";

    private static readonly Regex PlaceholderPattern = new(
        @"^\{[^{}]+\}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>FieldKeys public const string 白名单（反射一次并缓存）。</summary>
    private static readonly Lazy<HashSet<string>> FieldKeyWhitelist = new(() =>
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in typeof(FieldKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral
                && field.FieldType == typeof(string)
                && field.GetRawConstantValue() is string value
                && value.Length > 0)
            {
                set.Add(value);
            }
        }

        return set;
    });

    /// <summary>内置分类树的全部 path（含非叶子中间节点），用于父路径校验。</summary>
    private static readonly Lazy<HashSet<string>> BuiltInCategoryPaths = new(() =>
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in TemplateCategoryProvider.BuildCategoryTree())
        {
            foreach (var node in root.Flatten())
            {
                paths.Add(node.Path);
            }
        }

        return paths;
    });

    /// <summary>模板列表（不含 file_data）。</summary>
    public async Task<Result<IReadOnlyList<TemplateAdminListItem>>> ListAsync(
        string? keyword, CancellationToken ct = default)
    {
        var kw = keyword?.Trim() ?? string.Empty;

        const string sql = @"
SELECT id,
       name,
       COALESCE(file_type, '') AS file_type,
       COALESCE(doc_type, '') AS doc_type,
       COALESCE(array_to_json(COALESCE(categories, ARRAY[]::text[]))::text, '[]') AS categories_json,
       sort_order,
       created_at,
       COALESCE(updated_at, created_at) AS updated_at
FROM nc_biz_templates
WHERE ($1::text = '' OR name ILIKE '%' || $1::text || '%'
       OR COALESCE(doc_type, '') ILIKE '%' || $1::text || '%')
ORDER BY sort_order, id
LIMIT $2";

        var result = await database.QueryAsync<TemplateAdminRow>(sql, ct, kw, MaxListRows);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<TemplateAdminListItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "模板列表查询失败");
        }

        var items = (result.Value ?? [])
            .Select(row => new TemplateAdminListItem(
                row.Id,
                row.Name ?? string.Empty,
                row.FileType ?? string.Empty,
                row.DocType ?? string.Empty,
                ParseCategoriesJson(row.CategoriesJson),
                row.SortOrder,
                row.CreatedAt,
                row.UpdatedAt))
            .ToList();

        return Result.Success<IReadOnlyList<TemplateAdminListItem>>(items);
    }

    /// <summary>模板详情（含 configJson 与默认份数 copies）。</summary>
    public async Task<Result<TemplateAdminDetail>> GetDetailAsync(
        long id, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure<TemplateAdminDetail>(
                ErrorCodes.VALIDATION_FAILED, "模板 ID 无效");
        }

        const string sql = @"
SELECT id,
       name,
       COALESCE(file_type, '') AS file_type,
       COALESCE(doc_type, '') AS doc_type,
       COALESCE(array_to_json(COALESCE(categories, ARRAY[]::text[]))::text, '[]') AS categories_json,
       COALESCE(config_json::text, '') AS config_json,
       sort_order,
       created_at,
       COALESCE(updated_at, created_at) AS updated_at
FROM nc_biz_templates
WHERE id = $1
LIMIT 1";

        var result = await database.QuerySingleAsync<TemplateAdminDetailRow?>(sql, ct, id);
        if (result.IsFailure)
        {
            return Result.Failure<TemplateAdminDetail>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "模板详情查询失败");
        }

        var row = result.Value;
        if (row is null)
        {
            return Result.Failure<TemplateAdminDetail>(
                ErrorCodes.TEMPLATE_NOT_FOUND, "模板不存在");
        }

        var configJson = row.ConfigJson ?? string.Empty;
        return Result.Success(new TemplateAdminDetail(
            row.Id,
            row.Name ?? string.Empty,
            row.FileType ?? string.Empty,
            row.DocType ?? string.Empty,
            ParseCategoriesJson(row.CategoriesJson),
            configJson,
            ResolveCopies(configJson),
            ResolveCopiesByClassification(configJson),
            BuildConfigWarnings(configJson),
            row.SortOrder,
            row.CreatedAt,
            row.UpdatedAt));
    }

    /// <summary>下载原始模板文件。</summary>
    public async Task<Result<TemplateAdminFile>> GetFileAsync(
        long id, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure<TemplateAdminFile>(
                ErrorCodes.VALIDATION_FAILED, "模板 ID 无效");
        }

        const string sql = @"
SELECT name,
       COALESCE(file_type, '') AS file_type,
       encode(file_data, 'base64') AS file_base64
FROM nc_biz_templates
WHERE id = $1
LIMIT 1";

        var result = await database.QuerySingleAsync<TemplateAdminFileRow?>(sql, ct, id);
        if (result.IsFailure)
        {
            return Result.Failure<TemplateAdminFile>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "模板文件读取失败");
        }

        var row = result.Value;
        if (row is null)
        {
            return Result.Failure<TemplateAdminFile>(
                ErrorCodes.TEMPLATE_NOT_FOUND, "模板不存在");
        }

        if (string.IsNullOrWhiteSpace(row.FileBase64))
        {
            return Result.Failure<TemplateAdminFile>(
                ErrorCodes.FILE_NOT_FOUND, "模板文件数据为空");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(row.FileBase64);
        }
        catch (FormatException)
        {
            return Result.Failure<TemplateAdminFile>(
                ErrorCodes.FILE_NOT_FOUND, "模板文件数据损坏");
        }

        return Result.Success(new TemplateAdminFile(
            content,
            row.FileType ?? string.Empty,
            row.Name ?? string.Empty));
    }

    /// <summary>新增模板：docx/xlsx、docType 必须在词表内、name 必填。返回值附非阻断配置体检警告。</summary>
    public async Task<Result<TemplateAdminCreateResult>> CreateAsync(
        string? name,
        string? docType,
        string? categoriesRaw,
        string? configJson,
        byte[]? fileData,
        string? fileName,
        CancellationToken ct = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Result.Failure<TemplateAdminCreateResult>(
                ErrorCodes.VALIDATION_FAILED, "请输入模板名称");
        }

        if (fileData is not { Length: > 0 })
        {
            return Result.Failure<TemplateAdminCreateResult>(
                ErrorCodes.VALIDATION_FAILED, "请选择模板文件");
        }

        var fileTypeResult = ResolveFileType(fileName);
        if (fileTypeResult.IsFailure)
        {
            return Result.Failure<TemplateAdminCreateResult>(fileTypeResult.ErrorCode!, fileTypeResult.Message!);
        }

        var trimmedDocType = docType?.Trim() ?? string.Empty;
        if (!IsValidDocType(trimmedDocType))
        {
            return Result.Failure<TemplateAdminCreateResult>(
                ErrorCodes.VALIDATION_FAILED, "文档类型不在词表内");
        }

        var configError = ValidateConfigJson(configJson);
        if (configError is not null)
        {
            return Result.Failure<TemplateAdminCreateResult>(ErrorCodes.VALIDATION_FAILED, configError);
        }

        var categoriesResult = ParseCategories(categoriesRaw);
        if (categoriesResult.IsFailure)
        {
            return Result.Failure<TemplateAdminCreateResult>(categoriesResult.ErrorCode!, categoriesResult.Message!);
        }

        var fileHash = ComputeSha256(fileData);

        const string sql = @"
INSERT INTO nc_biz_templates
    (name, file_type, file_data, file_hash, categories, doc_type, config_json,
     sort_order, created_at, updated_at)
VALUES ($1, $2, $3, $4, $5::text[], $6, NULLIF($7, '')::jsonb,
        COALESCE((SELECT MAX(sort_order) + 1 FROM nc_biz_templates), 0), NOW(), NOW())
RETURNING id";

        var result = await database.QuerySingleAsync<long>(
            sql, ct,
            trimmedName,
            fileTypeResult.Value,
            fileData,
            fileHash,
            categoriesResult.Value.ToArray(),
            trimmedDocType,
            configJson ?? string.Empty);

        if (result.IsFailure)
        {
            logger.Warn($"模板新增失败: {result.ErrorCode} {result.Message}");
            return Result.Failure<TemplateAdminCreateResult>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "保存模板失败");
        }

        logger.LogBusiness("模板新增",
            ("TemplateId", result.Value),
            ("DocType", trimmedDocType),
            ("FileType", fileTypeResult.Value));

        return Result.Success(new TemplateAdminCreateResult(
            result.Value,
            BuildConfigWarnings(configJson)));
    }

    /// <summary>
    /// 更新模板：可选替换文件 / 名称 / 文档类型 / 分类 / configJson / 默认份数。
    /// copies 只改 config_json.copies，其余键原样保留。返回值附非阻断配置体检警告。
    /// </summary>
    public async Task<Result<IReadOnlyList<string>>> UpdateAsync(
        long id,
        string? name,
        string? docType,
        string? categoriesRaw,
        string? configJson,
        int? copies,
        byte[]? fileData,
        string? fileName,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure<IReadOnlyList<string>>(ErrorCodes.VALIDATION_FAILED, "模板 ID 无效");
        }

        var currentResult = await GetDetailAsync(id, ct);
        if (currentResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(currentResult.ErrorCode!, currentResult.Message!);
        }

        var current = currentResult.Value;

        string? newName = null;
        if (name is not null)
        {
            newName = name.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                return Result.Failure<IReadOnlyList<string>>(ErrorCodes.VALIDATION_FAILED, "模板名称不能为空");
            }
        }

        string? newDocType = null;
        if (docType is not null)
        {
            newDocType = docType.Trim();
            if (!IsValidDocType(newDocType))
            {
                return Result.Failure<IReadOnlyList<string>>(ErrorCodes.VALIDATION_FAILED, "文档类型不在词表内");
            }
        }

        if (configJson is not null)
        {
            var configError = ValidateConfigJson(configJson);
            if (configError is not null)
            {
                return Result.Failure<IReadOnlyList<string>>(ErrorCodes.VALIDATION_FAILED, configError);
            }
        }

        if (copies is < MinCopies or > MaxCopies)
        {
            return Result.Failure<IReadOnlyList<string>>(
                ErrorCodes.VALIDATION_FAILED,
                $"默认份数必须在 {MinCopies}-{MaxCopies} 之间");
        }

        string[]? newCategories = null;
        if (categoriesRaw is not null)
        {
            var categoriesResult = ParseCategories(categoriesRaw);
            if (categoriesResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<string>>(categoriesResult.ErrorCode!, categoriesResult.Message!);
            }

            newCategories = categoriesResult.Value.ToArray();
        }

        byte[]? newFileData = null;
        string? newFileType = null;
        string? newFileHash = null;
        if (fileData is { Length: > 0 })
        {
            var fileTypeResult = ResolveFileType(fileName);
            if (fileTypeResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<string>>(fileTypeResult.ErrorCode!, fileTypeResult.Message!);
            }

            newFileData = fileData;
            newFileType = fileTypeResult.Value;
            newFileHash = ComputeSha256(fileData);
        }

        var configProvided = configJson is not null || copies is not null;
        var configText = configJson ?? current.ConfigJson;

        if (copies is not null)
        {
            var copiesResult = ApplyCopies(configText, copies.Value);
            if (copiesResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<string>>(copiesResult.ErrorCode!, copiesResult.Message!);
            }

            configText = copiesResult.Value;
        }

        const string sql = @"
UPDATE nc_biz_templates
SET name = COALESCE($1, name),
    doc_type = COALESCE($2, doc_type),
    categories = COALESCE($3::text[], categories),
    file_data = COALESCE($4::bytea, file_data),
    file_hash = COALESCE($5, file_hash),
    file_type = COALESCE($6, file_type),
    config_json = CASE WHEN $7 THEN NULLIF($8, '')::jsonb ELSE config_json END,
    updated_at = NOW()
WHERE id = $9";

        var result = await database.ExecuteNonQueryAsync(
            sql, ct,
            newName,
            newDocType,
            newCategories,
            newFileData,
            newFileHash,
            newFileType,
            configProvided,
            configText,
            id);

        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "更新模板失败");
        }

        if (result.Value <= 0)
        {
            return Result.Failure<IReadOnlyList<string>>(ErrorCodes.TEMPLATE_NOT_FOUND, "模板不存在");
        }

        logger.LogBusiness("模板更新",
            ("TemplateId", id),
            ("ReplaceFile", newFileData is not null),
            ("Copies", copies ?? 0));

        return Result.Success<IReadOnlyList<string>>(BuildConfigWarnings(configText));
    }

    /// <summary>物理删除模板。</summary>
    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "模板 ID 无效");
        }

        var result = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_templates WHERE id = $1", ct, id);
        if (result.IsFailure)
        {
            return Result.Failure(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "删除模板失败");
        }

        if (result.Value <= 0)
        {
            return Result.Failure(ErrorCodes.TEMPLATE_NOT_FOUND, "模板不存在");
        }

        logger.LogBusiness("模板删除", ("TemplateId", id));
        return Result.Success();
    }

    /// <summary>上移 / 下移：与相邻模板交换 sort_order。</summary>
    public async Task<Result> MoveAsync(
        long id, string? direction, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "模板 ID 无效");
        }

        var normalized = direction?.Trim().ToLowerInvariant();
        if (normalized is not ("up" or "down"))
        {
            return Result.Failure(
                ErrorCodes.VALIDATION_FAILED, "direction 只能是 up 或 down");
        }

        var isUp = normalized == "up";
        var neighborSql = isUp
            ? @"SELECT id, sort_order
                FROM nc_biz_templates
                WHERE sort_order < $1 OR (sort_order = $1 AND id < $2)
                ORDER BY sort_order DESC, id DESC
                LIMIT 1"
            : @"SELECT id, sort_order
                FROM nc_biz_templates
                WHERE sort_order > $1 OR (sort_order = $1 AND id > $2)
                ORDER BY sort_order ASC, id ASC
                LIMIT 1";

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        // 历史数据 sort_order 可能全为 0 或存在并列：先按 (sort_order, id) 归一化为 0..n-1，
        // 保证上移 / 下移与相邻行严格交换，不会因并列值交换后顺序不变。
        var normalizeResult = await database.ExecuteNonQueryAsync(@"
WITH ordered AS (
    SELECT id, (ROW_NUMBER() OVER (ORDER BY sort_order, id) - 1)::int AS new_order
    FROM nc_biz_templates
)
UPDATE nc_biz_templates t
SET sort_order = ordered.new_order
FROM ordered
WHERE t.id = ordered.id", ct);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure(
                normalizeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                normalizeResult.Message ?? "模板排序归一化失败");
        }

        var currentResult = await database.QuerySingleAsync<TemplateSortRow?>(
            "SELECT id, sort_order FROM nc_biz_templates WHERE id = $1 LIMIT 1",
            ct, id);
        if (currentResult.IsFailure)
        {
            return Result.Failure(
                currentResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                currentResult.Message ?? "模板排序查询失败");
        }

        var current = currentResult.Value;
        if (current is null)
        {
            return Result.Failure(ErrorCodes.TEMPLATE_NOT_FOUND, "模板不存在");
        }

        var neighborResult = await database.QuerySingleAsync<TemplateSortRow?>(
            neighborSql, ct, current.SortOrder, current.Id);
        if (neighborResult.IsFailure)
        {
            return Result.Failure(
                neighborResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                neighborResult.Message ?? "相邻模板查询失败");
        }

        var neighbor = neighborResult.Value;
        if (neighbor is null)
        {
            return Result.Failure(
                ErrorCodes.VALIDATION_FAILED,
                isUp ? "已经是第一个模板" : "已经是最后一个模板");
        }

        var currentUpdate = await database.ExecuteNonQueryAsync(
            "UPDATE nc_biz_templates SET sort_order = $1, updated_at = NOW() WHERE id = $2",
            ct, neighbor.SortOrder, current.Id);
        if (currentUpdate.IsFailure)
        {
            return Result.Failure(
                currentUpdate.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                currentUpdate.Message ?? "模板排序更新失败");
        }

        var neighborUpdate = await database.ExecuteNonQueryAsync(
            "UPDATE nc_biz_templates SET sort_order = $1, updated_at = NOW() WHERE id = $2",
            ct, current.SortOrder, neighbor.Id);
        if (neighborUpdate.IsFailure)
        {
            return Result.Failure(
                neighborUpdate.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                neighborUpdate.Message ?? "模板排序更新失败");
        }

        await tx.CommitAsync(ct);
        logger.LogBusiness("模板排序", ("TemplateId", id), ("Direction", normalized));
        return Result.Success();
    }

    private const string CategorySelectSql = @"
SELECT id, COALESCE(parent_path, '') AS parent_path, label, path
FROM nc_biz_template_category_nodes";

    /// <summary>
    /// 合并分类树：内置树 + 自定义节点（<c>nc_biz_template_category_nodes</c>）。
    /// 自定义节点按 parent_path 挂载；父路径不存在或形成循环时挂到根并记 Warn。
    /// </summary>
    public async Task<Result<IReadOnlyList<TemplateCategoryNodeDto>>> GetCategoryTreeAsync(
        CancellationToken ct = default)
    {
        var result = await database.QueryAsync<TemplateCategoryRow>(
            CategorySelectSql + " ORDER BY parent_path, sort_order, id", ct);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<TemplateCategoryNodeDto>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "分类树查询失败");
        }

        // 历史分类兜底：模板 categories 里已有、但不在内置/自定义树里的值（如「值班管理」
        // 「公示」「追缴」「变更档案」「Ineligible」）。不补进树，编辑模板分类时会被静默丢掉。
        var usedResult = await database.QueryAsync<string>(
            @"SELECT DISTINCT unnest(categories)
              FROM nc_biz_templates
              WHERE categories IS NOT NULL AND cardinality(categories) > 0",
            ct);
        if (usedResult.IsFailure)
        {
            logger.Warn($"[TemplateAdmin] 查询历史使用分类失败（分类树将只含内置/自定义节点）: "
                        + $"{usedResult.ErrorCode} {usedResult.Message}");
        }

        return Result.Success<IReadOnlyList<TemplateCategoryNodeDto>>(
            BuildMergedCategoryTree(
                result.Value ?? new List<TemplateCategoryRow>(),
                usedResult.Value ?? new List<string>()));
    }

    /// <summary>固定分类树（<see cref="TemplateCategoryProvider.BuildCategoryTree"/> 的纯数据投影）。</summary>
    public static IReadOnlyList<TemplateCategoryNodeDto> GetCategoryTree()
        => TemplateCategoryProvider.BuildCategoryTree()
            .Select(ToCategoryDto)
            .ToList();

    /// <summary>新增自定义分类节点：父路径必须存在，label 1-120、不含 /，path 唯一。</summary>
    public async Task<Result<TemplateCategoryMutation>> CreateCategoryAsync(
        string? parentPath,
        string? label,
        CancellationToken ct = default)
    {
        var labelError = ValidateCategoryLabel(label, out var normalizedLabel);
        if (labelError is not null)
        {
            return Result.Failure<TemplateCategoryMutation>(ErrorCodes.VALIDATION_FAILED, labelError);
        }

        var parent = parentPath?.Trim() ?? string.Empty;
        if (parent.Length > 0)
        {
            if (parent.Length > MaxCategoryParentPathLength)
            {
                return Result.Failure<TemplateCategoryMutation>(
                    ErrorCodes.VALIDATION_FAILED,
                    $"父分类路径不能超过 {MaxCategoryParentPathLength} 个字符");
            }

            var parentExists = await CategoryPathExistsAsync(parent, ct);
            if (parentExists.IsFailure)
            {
                return Result.Failure<TemplateCategoryMutation>(
                    parentExists.ErrorCode!, parentExists.Message!);
            }

            if (!parentExists.Value)
            {
                return Result.Failure<TemplateCategoryMutation>(
                    ErrorCodes.VALIDATION_FAILED, "父分类不存在");
            }
        }

        var path = parent.Length == 0 ? normalizedLabel : $"{parent}/{normalizedLabel}";
        if (path.Length > MaxCategoryPathLength)
        {
            return Result.Failure<TemplateCategoryMutation>(
                ErrorCodes.VALIDATION_FAILED,
                $"分类路径不能超过 {MaxCategoryPathLength} 个字符");
        }

        if (BuiltInCategoryPaths.Value.Contains(path))
        {
            return Result.Failure<TemplateCategoryMutation>(CategoryDuplicate, "同级已存在同名分类");
        }

        var duplicateResult = await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_template_category_nodes WHERE path = $1", ct, path);
        if (duplicateResult.IsFailure)
        {
            return Result.Failure<TemplateCategoryMutation>(
                duplicateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                duplicateResult.Message ?? "分类路径查询失败");
        }

        if (duplicateResult.Value > 0)
        {
            return Result.Failure<TemplateCategoryMutation>(CategoryDuplicate, "同级已存在同名分类");
        }

        const string sql = @"
INSERT INTO nc_biz_template_category_nodes
    (parent_path, label, path, sort_order, created_at, updated_at)
VALUES ($1, $2, $3,
        COALESCE((SELECT MAX(sort_order) + 1
                  FROM nc_biz_template_category_nodes
                  WHERE parent_path = $1), 0),
        NOW(), NOW())
RETURNING id";

        var result = await database.QuerySingleAsync<long>(sql, ct, parent, normalizedLabel, path);
        if (result.IsFailure)
        {
            if (result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION)
            {
                return Result.Failure<TemplateCategoryMutation>(CategoryDuplicate, "同级已存在同名分类");
            }

            logger.Warn($"分类新增失败: {result.ErrorCode} {result.Message}");
            return Result.Failure<TemplateCategoryMutation>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "保存分类失败");
        }

        logger.LogBusiness("分类新增", ("CategoryId", result.Value), ("Path", path));
        return Result.Success(new TemplateCategoryMutation(result.Value, path));
    }

    /// <summary>
    /// 重命名自定义分类：级联更新子节点 path 前缀，并同步模板 categories 中的旧路径引用。
    /// 内置节点无自定义行，按 id 查不到返回 <see cref="CategoryNotFound"/>。
    /// </summary>
    public async Task<Result<TemplateCategoryMutation>> RenameCategoryAsync(
        long id,
        string? label,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure<TemplateCategoryMutation>(ErrorCodes.VALIDATION_FAILED, "分类 ID 无效");
        }

        var labelError = ValidateCategoryLabel(label, out var normalizedLabel);
        if (labelError is not null)
        {
            return Result.Failure<TemplateCategoryMutation>(ErrorCodes.VALIDATION_FAILED, labelError);
        }

        var currentResult = await database.QuerySingleAsync<TemplateCategoryRow?>(
            CategorySelectSql + " WHERE id = $1 LIMIT 1", ct, id);
        if (currentResult.IsFailure)
        {
            return Result.Failure<TemplateCategoryMutation>(
                currentResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                currentResult.Message ?? "分类查询失败");
        }

        var current = currentResult.Value;
        if (current is null || string.IsNullOrWhiteSpace(current.Path))
        {
            return Result.Failure<TemplateCategoryMutation>(CategoryNotFound, "分类不存在");
        }

        var oldPath = current.Path!;
        var parentPath = current.ParentPath ?? string.Empty;
        var newPath = parentPath.Length == 0 ? normalizedLabel : $"{parentPath}/{normalizedLabel}";
        if (newPath.Length > MaxCategoryPathLength)
        {
            return Result.Failure<TemplateCategoryMutation>(
                ErrorCodes.VALIDATION_FAILED,
                $"分类路径不能超过 {MaxCategoryPathLength} 个字符");
        }

        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
        {
            return Result.Success(new TemplateCategoryMutation(id, newPath));
        }

        if (BuiltInCategoryPaths.Value.Contains(newPath))
        {
            return Result.Failure<TemplateCategoryMutation>(CategoryDuplicate, "同级已存在同名分类");
        }

        var conflictResult = await database.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*) FROM nc_biz_template_category_nodes
              WHERE (path = $1 AND id <> $2) OR starts_with(path, $1 || '/')",
            ct, newPath, id);
        if (conflictResult.IsFailure)
        {
            return Result.Failure<TemplateCategoryMutation>(
                conflictResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                conflictResult.Message ?? "分类路径查询失败");
        }

        if (conflictResult.Value > 0)
        {
            return Result.Failure<TemplateCategoryMutation>(CategoryDuplicate, "同级已存在同名分类");
        }

        var delta = newPath.Length - oldPath.Length;
        if (delta > 0)
        {
            var lengthResult = await database.QuerySingleAsync<CategoryLengthRow>(
                @"SELECT COALESCE(MAX(char_length(path)), 0) AS max_path,
                         COALESCE(MAX(char_length(parent_path)), 0) AS max_parent_path
                  FROM nc_biz_template_category_nodes
                  WHERE starts_with(path, $1 || '/')",
                ct, oldPath);
            if (lengthResult.IsFailure)
            {
                return Result.Failure<TemplateCategoryMutation>(
                    lengthResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    lengthResult.Message ?? "子分类路径长度检查失败");
            }

            var lengths = lengthResult.Value;
            if (lengths is not null
                && (lengths.MaxPath + delta > MaxCategoryPathLength
                    || lengths.MaxParentPath + delta > MaxCategoryParentPathLength))
            {
                return Result.Failure<TemplateCategoryMutation>(
                    ErrorCodes.VALIDATION_FAILED,
                    "子分类路径过长，请改用更短的名称");
            }
        }

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        var selfUpdate = await database.ExecuteNonQueryAsync(
            @"UPDATE nc_biz_template_category_nodes
              SET label = $1, path = $2, updated_at = NOW()
              WHERE id = $3",
            ct, normalizedLabel, newPath, id);
        if (selfUpdate.IsFailure)
        {
            return CategoryDbFailure(selfUpdate, "分类改名失败");
        }

        // 子节点 path / parent_path 统一替换前缀（parent_path = 旧 path 时自然变成新 path）。
        const string cascadeSql = @"
UPDATE nc_biz_template_category_nodes
SET path = $2 || substring(path FROM char_length($1) + 1),
    parent_path = $2 || substring(parent_path FROM char_length($1) + 1),
    updated_at = NOW()
WHERE starts_with(path, $1 || '/')";
        var cascadeResult = await database.ExecuteNonQueryAsync(cascadeSql, ct, oldPath, newPath);
        if (cascadeResult.IsFailure)
        {
            return CategoryDbFailure(cascadeResult, "子分类路径更新失败");
        }

        // 模板 categories：先 array_replace 精确替换，再按旧前缀批量改写（全部参数化）。
        const string templateSql = @"
UPDATE nc_biz_templates
SET categories = (
        SELECT COALESCE(array_agg(
                   CASE WHEN starts_with(x, $1 || '/')
                        THEN $2 || substring(x FROM char_length($1) + 1)
                        ELSE x END
                   ORDER BY ord), ARRAY[]::text[])
        FROM unnest(array_replace(categories, $1, $2)) WITH ORDINALITY AS t(x, ord)
    ),
    updated_at = NOW()
WHERE categories IS NOT NULL
  AND (categories @> ARRAY[$1]::text[]
       OR EXISTS (
            SELECT 1 FROM unnest(categories) AS c
            WHERE starts_with(c, $1 || '/')
       ))";
        var templateResult = await database.ExecuteNonQueryAsync(templateSql, ct, oldPath, newPath);
        if (templateResult.IsFailure)
        {
            return Result.Failure<TemplateCategoryMutation>(
                templateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                templateResult.Message ?? "模板分类引用更新失败");
        }

        await tx.CommitAsync(ct);
        logger.LogBusiness("分类改名",
            ("CategoryId", id),
            ("OldPath", oldPath),
            ("NewPath", newPath));
        return Result.Success(new TemplateCategoryMutation(id, newPath));
    }

    /// <summary>
    /// 删除自定义分类：有子分类 409；被模板引用 409（force=true 时先从模板 categories 移除）。
    /// </summary>
    public async Task<Result> DeleteCategoryAsync(
        long id,
        bool force,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "分类 ID 无效");
        }

        var currentResult = await database.QuerySingleAsync<TemplateCategoryRow?>(
            CategorySelectSql + " WHERE id = $1 LIMIT 1", ct, id);
        if (currentResult.IsFailure)
        {
            return Result.Failure(
                currentResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                currentResult.Message ?? "分类查询失败");
        }

        var current = currentResult.Value;
        if (current is null || string.IsNullOrWhiteSpace(current.Path))
        {
            return Result.Failure(CategoryNotFound, "分类不存在");
        }

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        var childCount = await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_template_category_nodes WHERE parent_path = $1",
            ct, current.Path);
        if (childCount.IsFailure)
        {
            return Result.Failure(
                childCount.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                childCount.Message ?? "子分类查询失败");
        }

        if (childCount.Value > 0)
        {
            return Result.Failure(CategoryHasChildren, "该分类下存在子分类");
        }

        var usedCount = await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_templates WHERE categories @> ARRAY[$1]::text[]",
            ct, current.Path);
        if (usedCount.IsFailure)
        {
            return Result.Failure(
                usedCount.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                usedCount.Message ?? "模板引用查询失败");
        }

        if (usedCount.Value > 0)
        {
            if (!force)
            {
                return Result.Failure(CategoryInUse, $"该分类被 {usedCount.Value} 个模板引用");
            }

            var detachResult = await database.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_templates
                  SET categories = array_remove(categories, $1), updated_at = NOW()
                  WHERE categories @> ARRAY[$1]::text[]",
                ct, current.Path);
            if (detachResult.IsFailure)
            {
                return Result.Failure(
                    detachResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    detachResult.Message ?? "模板分类引用移除失败");
            }
        }

        var deleteResult = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_template_category_nodes WHERE id = $1", ct, id);
        if (deleteResult.IsFailure)
        {
            return Result.Failure(
                deleteResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                deleteResult.Message ?? "删除分类失败");
        }

        await tx.CommitAsync(ct);
        logger.LogBusiness("分类删除",
            ("CategoryId", id),
            ("Path", current.Path),
            ("Force", force),
            ("DetachedTemplates", usedCount.Value));
        return Result.Success();
    }

    /// <summary>文档类型词表（<see cref="TemplateDocTypes.All"/>）。</summary>
    public static IReadOnlyList<string> GetDocTypes() => TemplateDocTypes.All;

    private async Task<Result<bool>> CategoryPathExistsAsync(string path, CancellationToken ct)
    {
        if (BuiltInCategoryPaths.Value.Contains(path))
        {
            return Result.Success(true);
        }

        var result = await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_template_category_nodes WHERE path = $1", ct, path);
        if (result.IsFailure)
        {
            return Result.Failure<bool>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "分类路径查询失败");
        }

        return Result.Success(result.Value > 0);
    }

    private static string? ValidateCategoryLabel(string? label, out string normalized)
    {
        normalized = label?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return "请输入分类名称";
        }

        if (normalized.Length > MaxCategoryLabelLength)
        {
            return $"分类名称不能超过 {MaxCategoryLabelLength} 个字符";
        }

        if (normalized.Contains('/'))
        {
            return "分类名称不能包含 /";
        }

        return null;
    }

    private static Result<TemplateCategoryMutation> CategoryDbFailure(Result result, string fallback)
    {
        if (result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION)
        {
            return Result.Failure<TemplateCategoryMutation>(CategoryDuplicate, "同级已存在同名分类");
        }

        return Result.Failure<TemplateCategoryMutation>(
            result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
            result.Message ?? fallback);
    }

    private IReadOnlyList<TemplateCategoryNodeDto> BuildMergedCategoryTree(
        IReadOnlyList<TemplateCategoryRow> rows,
        IReadOnlyList<string> usedCategories)
    {
        var map = new Dictionary<string, MutableCategoryNode>(StringComparer.Ordinal);
        var roots = new List<MutableCategoryNode>();
        foreach (var builtInRoot in TemplateCategoryProvider.BuildCategoryTree())
        {
            roots.Add(ToMutableCategoryNode(builtInRoot, map));
        }

        var customNodes = new List<MutableCategoryNode>(rows.Count);
        foreach (var row in rows)
        {
            var path = row.Path?.Trim() ?? string.Empty;
            if (path.Length == 0)
            {
                logger.Warn($"[TemplateAdmin] 自定义分类 id={row.Id} 缺少 path，已跳过");
                continue;
            }

            if (map.ContainsKey(path))
            {
                logger.Warn($"[TemplateAdmin] 自定义分类 id={row.Id} 的 path「{path}」与内置/其他分类重复，已跳过");
                continue;
            }

            var node = new MutableCategoryNode
            {
                Id = row.Id,
                Label = row.Label ?? string.Empty,
                Path = path,
                ParentPath = row.ParentPath?.Trim() ?? string.Empty,
                IsBuiltIn = false
            };
            map[path] = node;
            customNodes.Add(node);
        }

        foreach (var node in customNodes)
        {
            if (node.ParentPath.Length == 0)
            {
                roots.Add(node);
                continue;
            }

            if (IsCategoryCycle(node, node.ParentPath, map))
            {
                logger.Warn(
                    $"[TemplateAdmin] 自定义分类「{node.Path}」的父路径「{node.ParentPath}」形成循环引用，已挂到根节点");
                roots.Add(node);
                continue;
            }

            if (map.TryGetValue(node.ParentPath, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                logger.Warn(
                    $"[TemplateAdmin] 自定义分类「{node.Path}」的父路径「{node.ParentPath}」不存在，已挂到根节点");
                roots.Add(node);
            }
        }

        // 历史分类（未归类）：模板已经在用、但树里没有的路径。
        // 只读展示，避免编辑模板分类时把「值班管理」「公示」「追缴」等静默清空。
        var legacyPaths = usedCategories
            .Select(path => path?.Trim() ?? string.Empty)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !map.ContainsKey(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (legacyPaths.Count > 0)
        {
            var legacyRoot = new MutableCategoryNode
            {
                Id = null,
                Label = "历史分类（未归类）",
                Path = LegacyCategoryRootPath,
                ParentPath = string.Empty,
                IsBuiltIn = true
            };
            foreach (var path in legacyPaths)
            {
                legacyRoot.Children.Add(new MutableCategoryNode
                {
                    Id = null,
                    Label = ClassificationConstants.ConvertFromCode(path),
                    Path = path,
                    ParentPath = LegacyCategoryRootPath,
                    IsBuiltIn = true
                });
            }

            roots.Add(legacyRoot);
        }

        return roots.Select(ToCategoryDto).ToList();
    }

    private static bool IsCategoryCycle(
        MutableCategoryNode node,
        string parentPath,
        IReadOnlyDictionary<string, MutableCategoryNode> map)
    {
        var cursor = parentPath;
        var guard = 0;
        while (cursor.Length > 0 && guard++ < 128)
        {
            if (string.Equals(cursor, node.Path, StringComparison.Ordinal))
            {
                return true;
            }

            if (!map.TryGetValue(cursor, out var ancestor) || ancestor.IsBuiltIn)
            {
                return false;
            }

            cursor = ancestor.ParentPath;
        }

        return false;
    }

    private static MutableCategoryNode ToMutableCategoryNode(
        TemplateCategoryNode node,
        Dictionary<string, MutableCategoryNode> map)
    {
        var mutable = new MutableCategoryNode
        {
            Id = null,
            Label = node.Label,
            Path = node.Path,
            ParentPath = string.Empty,
            IsBuiltIn = true
        };
        map[node.Path] = mutable;
        foreach (var child in node.Children)
        {
            mutable.Children.Add(ToMutableCategoryNode(child, map));
        }

        return mutable;
    }

    private static TemplateCategoryNodeDto ToCategoryDto(TemplateCategoryNode node)
        => new(
            null,
            node.Label,
            node.Path,
            node.Children.Count == 0,
            true,
            node.Children.Select(ToCategoryDto).ToList());

    private static TemplateCategoryNodeDto ToCategoryDto(MutableCategoryNode node)
    {
        var children = node.Children.Select(ToCategoryDto).ToList();
        return new(
            node.Id,
            node.Label,
            node.Path,
            children.Count == 0,
            node.IsBuiltIn,
            children);
    }

    private static Result<string> ResolveFileType(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".docx" => Result.Success(TemplateFileTypes.Word),
            ".xlsx" => Result.Success(TemplateFileTypes.Excel),
            _ => Result.Failure<string>(
                ErrorCodes.VALIDATION_FAILED, "模板文件仅支持 .docx 或 .xlsx")
        };
    }

    private static bool IsValidDocType(string docType)
        => !string.IsNullOrEmpty(docType)
           && Array.IndexOf(TemplateDocTypes.All, docType) >= 0;

    private static string? ValidateConfigJson(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(configJson);
            return null;
        }
        catch (JsonException)
        {
            return "configJson 不是合法 JSON";
        }
    }

    private static Result<IReadOnlyList<string>> ParseCategories(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Success<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var text = raw.Trim();
        if (text.StartsWith('['))
        {
            try
            {
                var values = JsonSerializer.Deserialize<string[]>(text);
                return Result.Success<IReadOnlyList<string>>(NormalizeCategories(values));
            }
            catch (JsonException)
            {
                return Result.Failure<IReadOnlyList<string>>(
                    ErrorCodes.VALIDATION_FAILED, "categories 不是合法的 JSON 数组");
            }
        }

        var parts = text.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Result.Success<IReadOnlyList<string>>(NormalizeCategories(parts));
    }

    private static IReadOnlyList<string> NormalizeCategories(IEnumerable<string?>? values)
        => (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private IReadOnlyList<string> ParseCategoriesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return NormalizeCategories(JsonSerializer.Deserialize<string[]>(json));
        }
        catch (JsonException)
        {
            logger.Debug("[TemplateAdmin] categories JSON 解析失败，按空数组处理");
            return Array.Empty<string>();
        }
    }

    /// <summary>默认份数：config_json.copies，缺失/非法按 1，范围 1-99。</summary>
    public static int ResolveCopies(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return 1;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return 1;
            }

            if (doc.RootElement.TryGetProperty("copies", out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var copies))
            {
                return Math.Clamp(copies, MinCopies, MaxCopies);
            }
        }
        catch (JsonException)
        {
            // 损坏配置按默认 1 份处理（与旧版 TemplateCopiesResolver 一致）
        }

        return 1;
    }

    /// <summary>读 config_json.copiesByClassification（缺失/损坏/非对象 → 空表，不抛）。</summary>
    public static IReadOnlyDictionary<string, int> ResolveCopiesByClassification(string? configJson)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && TryGetPropertyIgnoreCase(doc.RootElement, "copiesByClassification", out var element)
                && element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var copies))
                    {
                        result[property.Name] = copies;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 损坏配置只影响提示，不阻断详情
        }

        return result;
    }

    /// <summary>
    /// config_json 非阻断体检：fieldKey 白名单、placeholder 格式与同模板重复。
    /// 配置损坏返回单条「config 解析失败：...」，绝不抛出。
    /// </summary>
    public static IReadOnlyList<string> BuildConfigWarnings(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return Array.Empty<string>();
        }

        var warnings = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configJson);
        }
        catch (JsonException ex)
        {
            return new[] { $"config 解析失败：{ex.Message}" };
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("config 顶层不是 JSON 对象");
                return warnings;
            }

            // 兼容 {"elem":{"fields":[...]}} 旧格式：根上没有 fields 时看 elem。
            var fieldsContainer = root;
            if (!TryGetPropertyIgnoreCase(root, "fields", out _)
                && TryGetPropertyIgnoreCase(root, "elem", out var elem)
                && elem.ValueKind == JsonValueKind.Object)
            {
                fieldsContainer = elem;
            }

            if (!TryGetPropertyIgnoreCase(fieldsContainer, "fields", out var fields))
            {
                return warnings;
            }

            if (fields.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("fields 不是数组");
                return warnings;
            }

            var seenPlaceholders = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var field in fields.EnumerateArray())
            {
                index++;
                if (field.ValueKind != JsonValueKind.Object)
                {
                    warnings.Add($"fields 第 {index} 项不是对象");
                    continue;
                }

                var fieldKey = TryGetString(field, "fieldKey");
                var placeholder = TryGetString(field, "placeholder");

                if (string.IsNullOrWhiteSpace(fieldKey))
                {
                    warnings.Add($"fields 第 {index} 项缺少 fieldKey");
                }
                else if (!FieldKeyWhitelist.Value.Contains(fieldKey))
                {
                    warnings.Add($"字段 {fieldKey} 不在 FieldKeys 白名单");
                }

                if (string.IsNullOrWhiteSpace(placeholder))
                {
                    warnings.Add($"fields 第 {index} 项缺少 placeholder");
                    continue;
                }

                if (!PlaceholderPattern.IsMatch(placeholder))
                {
                    warnings.Add($"占位符 {placeholder} 格式不正确");
                }

                if (!seenPlaceholders.Add(placeholder))
                {
                    warnings.Add($"占位符 {placeholder} 重复");
                }
            }
        }

        return warnings;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string name)
        => TryGetPropertyIgnoreCase(element, name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    /// <summary>只写 / 改 config_json.copies，保留 fields / tables / copiesByClassification 等键。</summary>
    private static Result<string> ApplyCopies(string? configJson, int copies)
    {
        JsonNode? node;
        try
        {
            node = string.IsNullOrWhiteSpace(configJson) ? new JsonObject() : JsonNode.Parse(configJson);
        }
        catch (JsonException)
        {
            return Result.Failure<string>(
                ErrorCodes.VALIDATION_FAILED, "configJson 不是合法 JSON，无法写入份数");
        }

        if (node is not JsonObject obj)
        {
            return Result.Failure<string>(
                ErrorCodes.VALIDATION_FAILED, "configJson 不是 JSON 对象，无法写入份数");
        }

        obj["copies"] = copies;
        return Result.Success(obj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    private static string ComputeSha256(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed class TemplateAdminRow
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? FileType { get; set; }
        public string? DocType { get; set; }
        public string? CategoriesJson { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class TemplateAdminDetailRow
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? FileType { get; set; }
        public string? DocType { get; set; }
        public string? CategoriesJson { get; set; }
        public string? ConfigJson { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class TemplateAdminFileRow
    {
        public string? Name { get; set; }
        public string? FileType { get; set; }
        public string? FileBase64 { get; set; }
    }

    private sealed class TemplateSortRow
    {
        public long Id { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class TemplateCategoryRow
    {
        public long Id { get; set; }
        public string? ParentPath { get; set; }
        public string? Label { get; set; }
        public string? Path { get; set; }
    }

    private sealed class CategoryLengthRow
    {
        public int MaxPath { get; set; }
        public int MaxParentPath { get; set; }
    }

    private sealed class MutableCategoryNode
    {
        public long? Id { get; init; }
        public string Label { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string ParentPath { get; init; } = string.Empty;
        public bool IsBuiltIn { get; init; }
        public List<MutableCategoryNode> Children { get; } = new();
    }
}

/// <summary>模板列表项（不含 file_data）。</summary>
public sealed record TemplateAdminListItem(
    long Id,
    string Name,
    string FileType,
    string DocType,
    IReadOnlyList<string> Categories,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>新增模板结果（id + config 体检警告）。</summary>
public sealed record TemplateAdminCreateResult(long Id, IReadOnlyList<string> ConfigWarnings);

/// <summary>模板详情。</summary>
public sealed record TemplateAdminDetail(
    long Id,
    string Name,
    string FileType,
    string DocType,
    IReadOnlyList<string> Categories,
    string ConfigJson,
    int Copies,
    IReadOnlyDictionary<string, int> CopiesByClassification,
    IReadOnlyList<string> ConfigWarnings,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>模板原始文件。</summary>
public sealed record TemplateAdminFile(byte[] Content, string FileType, string FileName);

/// <summary>分类新增/改名的返回值。</summary>
public sealed record TemplateCategoryMutation(long Id, string Path);

/// <summary>分类树节点（纯数据，避免序列化 ObservableObject / Action 属性）。</summary>
public sealed record TemplateCategoryNodeDto(
    long? Id,
    string Label,
    string Path,
    bool IsLeaf,
    bool IsBuiltIn,
    IReadOnlyList<TemplateCategoryNodeDto> Children);
