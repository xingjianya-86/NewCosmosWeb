using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>新增模板：docx/xlsx、docType 必须在词表内、name 必填。</summary>
    public async Task<Result<long>> CreateAsync(
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
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "请输入模板名称");
        }

        if (fileData is not { Length: > 0 })
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "请选择模板文件");
        }

        var fileTypeResult = ResolveFileType(fileName);
        if (fileTypeResult.IsFailure)
        {
            return Result.Failure<long>(fileTypeResult.ErrorCode!, fileTypeResult.Message!);
        }

        var trimmedDocType = docType?.Trim() ?? string.Empty;
        if (!IsValidDocType(trimmedDocType))
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "文档类型不在词表内");
        }

        var configError = ValidateConfigJson(configJson);
        if (configError is not null)
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, configError);
        }

        var categoriesResult = ParseCategories(categoriesRaw);
        if (categoriesResult.IsFailure)
        {
            return Result.Failure<long>(categoriesResult.ErrorCode!, categoriesResult.Message!);
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
            return Result.Failure<long>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "保存模板失败");
        }

        logger.LogBusiness("模板新增",
            ("TemplateId", result.Value),
            ("DocType", trimmedDocType),
            ("FileType", fileTypeResult.Value));

        return Result.Success(result.Value);
    }

    /// <summary>
    /// 更新模板：可选替换文件 / 名称 / 文档类型 / 分类 / configJson / 默认份数。
    /// copies 只改 config_json.copies，其余键原样保留。
    /// </summary>
    public async Task<Result> UpdateAsync(
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
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "模板 ID 无效");
        }

        var currentResult = await GetDetailAsync(id, ct);
        if (currentResult.IsFailure)
        {
            return Result.Failure(currentResult.ErrorCode!, currentResult.Message!);
        }

        var current = currentResult.Value;

        string? newName = null;
        if (name is not null)
        {
            newName = name.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "模板名称不能为空");
            }
        }

        string? newDocType = null;
        if (docType is not null)
        {
            newDocType = docType.Trim();
            if (!IsValidDocType(newDocType))
            {
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "文档类型不在词表内");
            }
        }

        if (configJson is not null)
        {
            var configError = ValidateConfigJson(configJson);
            if (configError is not null)
            {
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, configError);
            }
        }

        if (copies is < MinCopies or > MaxCopies)
        {
            return Result.Failure(
                ErrorCodes.VALIDATION_FAILED,
                $"默认份数必须在 {MinCopies}-{MaxCopies} 之间");
        }

        string[]? newCategories = null;
        if (categoriesRaw is not null)
        {
            var categoriesResult = ParseCategories(categoriesRaw);
            if (categoriesResult.IsFailure)
            {
                return Result.Failure(categoriesResult.ErrorCode!, categoriesResult.Message!);
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
                return Result.Failure(fileTypeResult.ErrorCode!, fileTypeResult.Message!);
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
                return Result.Failure(copiesResult.ErrorCode!, copiesResult.Message!);
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
            return Result.Failure(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "更新模板失败");
        }

        if (result.Value <= 0)
        {
            return Result.Failure(ErrorCodes.TEMPLATE_NOT_FOUND, "模板不存在");
        }

        logger.LogBusiness("模板更新",
            ("TemplateId", id),
            ("ReplaceFile", newFileData is not null),
            ("Copies", copies ?? 0));

        return Result.Success();
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

    /// <summary>固定分类树（<see cref="TemplateCategoryProvider.BuildCategoryTree"/> 的纯数据投影）。</summary>
    public static IReadOnlyList<TemplateCategoryNodeDto> GetCategoryTree()
        => TemplateCategoryProvider.BuildCategoryTree()
            .Select(ToCategoryDto)
            .ToList();

    /// <summary>文档类型词表（<see cref="TemplateDocTypes.All"/>）。</summary>
    public static IReadOnlyList<string> GetDocTypes() => TemplateDocTypes.All;

    private static TemplateCategoryNodeDto ToCategoryDto(TemplateCategoryNode node)
        => new(
            node.Label,
            node.Path,
            node.IsLeaf,
            node.Children.Select(ToCategoryDto).ToList());

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

/// <summary>模板详情。</summary>
public sealed record TemplateAdminDetail(
    long Id,
    string Name,
    string FileType,
    string DocType,
    IReadOnlyList<string> Categories,
    string ConfigJson,
    int Copies,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>模板原始文件。</summary>
public sealed record TemplateAdminFile(byte[] Content, string FileType, string FileName);

/// <summary>分类树节点（纯数据，避免序列化 ObservableObject / Action 属性）。</summary>
public sealed record TemplateCategoryNodeDto(
    string Label,
    string Path,
    bool IsLeaf,
    IReadOnlyList<TemplateCategoryNodeDto> Children);
