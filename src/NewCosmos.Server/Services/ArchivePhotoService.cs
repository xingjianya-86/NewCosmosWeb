using System.Globalization;
using System.IO.Compression;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 档案照片查询 / 补传 / 导出（Server 侧）。
/// <para>
/// 照片二进制存 <c>nc_biz_archive_photos.photo_data</c>（bytea），列表只取元数据；
/// 详情与导出用 PostgreSQL <c>encode(..., 'base64')</c> 取回，避免实体映射大字段。
/// 归属人必须存在于低收入 / 高龄 / 临时救助三类业务档案（按身份证，deleted_at IS NULL）。
/// </para>
/// <para>
/// 命名沿用旧版 <see cref="PhotoNameBuilder"/>：姓名_身份证后4位_类别_编号（该人该类别内递增）。
/// 水印（入户排查 / 公示核查）本期不做：Server 不引用 Office/图形栈，按原图入库；
/// 后续如需水印，应在采集端或独立图像服务完成后再入库。
/// </para>
/// </summary>
public sealed class ArchivePhotoService(IDatabaseService database, ILoggerService logger)
{
    /// <summary>单张照片上限 10MB（与临时救助照片上传一致）。</summary>
    public const long MaxPhotoBytes = 10L * 1024 * 1024;

    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>导出上限（keyword/category 分支；ids 分支同样按此上限截断）。</summary>
    public const int MaxExportPhotos = 500;

    private static readonly string[] AllowedFormats =
    {
        PhotoFormatConstants.Png,
        PhotoFormatConstants.Jpeg,
        PhotoFormatConstants.WebP
    };

    /// <summary>列表查询（元数据，不含 photo_data）。</summary>
    public async Task<Result<ArchivePhotoPage>> GetPagedAsync(
        string? keyword,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var kw = keyword?.Trim() ?? string.Empty;
        var cat = category?.Trim() ?? string.Empty;

        const string where = @"
 WHERE ($1::text = ''
        OR applicant_id_card ILIKE '%' || $1::text || '%'
        OR COALESCE(photo_name, '') ILIKE '%' || $1::text || '%')
   AND ($2::text = '' OR photo_category = $2::text)";

        var countResult = await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*)::bigint FROM nc_biz_archive_photos" + where,
            ct, kw, cat);
        if (countResult.IsFailure)
        {
            return Result.Failure<ArchivePhotoPage>(
                countResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                countResult.Message ?? "照片数量统计失败");
        }

        const string listSql = @"
SELECT id,
       applicant_id_card,
       photo_category,
       photo_name,
       file_size,
       image_format,
       created_at,
       (photo_data IS NOT NULL AND octet_length(photo_data) > 0) AS has_data
FROM nc_biz_archive_photos";

        var listResult = await database.QueryAsync<ArchivePhotoListRow>(
            listSql + where + @"
 ORDER BY created_at DESC, id DESC
 LIMIT $3 OFFSET $4",
            ct, kw, cat, pageSize, (page - 1) * pageSize);
        if (listResult.IsFailure)
        {
            return Result.Failure<ArchivePhotoPage>(
                listResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                listResult.Message ?? "照片列表查询失败");
        }

        var items = (listResult.Value ?? [])
            .Select(row => new ArchivePhotoListItem(
                row.Id,
                row.ApplicantIdCard ?? string.Empty,
                row.PhotoCategory ?? string.Empty,
                PhotoCategoryConstants.GetDisplayName(row.PhotoCategory),
                string.IsNullOrWhiteSpace(row.PhotoName) ? $"照片 #{row.Id}" : row.PhotoName!,
                row.FileSize,
                NormalizeFormat(row.ImageFormat),
                row.CreatedAt,
                row.HasData))
            .ToList();

        return Result.Success(new ArchivePhotoPage(
            items,
            (int)Math.Min(countResult.Value, int.MaxValue),
            page,
            pageSize));
    }

    /// <summary>单张详情（含 Base64 图片数据）。</summary>
    public async Task<Result<ArchivePhotoDetail>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure<ArchivePhotoDetail>(
                ErrorCodes.VALIDATION_FAILED, "照片 ID 无效");
        }

        const string sql = @"
SELECT id,
       applicant_id_card,
       photo_category,
       photo_name,
       file_size,
       image_format,
       created_at,
       encode(photo_data, 'base64') AS photo_base64
FROM nc_biz_archive_photos
WHERE id = $1
LIMIT 1";

        var result = await database.QuerySingleAsync<ArchivePhotoDetailRow?>(sql, ct, id);
        if (result.IsFailure)
        {
            return Result.Failure<ArchivePhotoDetail>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "照片详情查询失败");
        }

        var row = result.Value;
        if (row is null)
        {
            return Result.Failure<ArchivePhotoDetail>(
                ErrorCodes.RECORD_NOT_FOUND, "照片不存在");
        }

        return Result.Success(new ArchivePhotoDetail(
            row.Id,
            row.ApplicantIdCard ?? string.Empty,
            row.PhotoCategory ?? string.Empty,
            PhotoCategoryConstants.GetDisplayName(row.PhotoCategory),
            string.IsNullOrWhiteSpace(row.PhotoName) ? $"照片 #{row.Id}" : row.PhotoName!,
            NormalizeFormat(row.ImageFormat),
            row.FileSize,
            row.CreatedAt,
            row.PhotoBase64 ?? string.Empty));
    }

    /// <summary>照片类别（10 类，顺序同 <see cref="PhotoCategoryConstants.All"/>）。</summary>
    public static IReadOnlyList<ArchivePhotoCategoryOption> GetCategories()
        => PhotoCategoryConstants.All
            .Select(item => new ArchivePhotoCategoryOption(item.Code, item.Name))
            .ToList();

    /// <summary>
    /// 新增照片：校验身份证 / 类别 / 文件大小与格式，解析归属人后入库。
    /// </summary>
    public async Task<Result<long>> CreateAsync(
        string? idCard,
        string? category,
        byte[]? fileData,
        int? operatorId,
        string? operatorName,
        CancellationToken ct = default)
    {
        var trimmedIdCard = idCard?.Trim() ?? string.Empty;
        if (!IdCardValidator.IsValid(trimmedIdCard))
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "身份证号格式不正确（需 18 位）");
        }

        var trimmedCategory = category?.Trim() ?? string.Empty;
        if (!IsAllowedCategory(trimmedCategory))
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "照片类别不在允许范围内");
        }

        if (fileData is not { Length: > 0 })
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "照片内容为空");
        }

        if (fileData.LongLength > MaxPhotoBytes)
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "照片不能超过 10MB");
        }

        var format = ImageTranscoder.DetectFormat(fileData);
        if (!IsAllowedFormat(format))
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "照片格式仅支持 png / jpeg / webp");
        }

        var ownerResult = await ResolveOwnerAsync(trimmedIdCard, ct);
        if (ownerResult.IsFailure)
        {
            return Result.Failure<long>(ownerResult.ErrorCode!, ownerResult.Message!);
        }

        var owner = ownerResult.Value;
        var sequenceResult = await ResolveNextSequenceAsync(trimmedIdCard, trimmedCategory, ct);
        if (sequenceResult.IsFailure)
        {
            return Result.Failure<long>(sequenceResult.ErrorCode!, sequenceResult.Message!);
        }

        var photoName = PhotoNameBuilder.Build(
            owner.PersonName, trimmedIdCard, trimmedCategory, sequenceResult.Value);

        // 水印（HOUSEHOLD_INSPECTION 入户排查 / PUBLICITY_CHECK 公示核查）本期不处理：
        // Server 端不引用 Office/图形栈，按原图入库；后续如需水印，应在采集端或独立图像服务
        // 生成带水印的图片后再上传，避免在请求线程里做耗时绘制。
        const string sql = @"
INSERT INTO nc_biz_archive_photos
    (photo_data, image_format, applicant_id_card, application_id, photo_category,
     photo_name, file_size, created_by, created_by_name, created_at)
VALUES ($1, $2, $3, $4::bigint, $5, $6, $7, $8::integer, $9, NOW())
RETURNING id";

        var result = await database.QuerySingleAsync<long>(
            sql, ct,
            fileData,
            format,
            trimmedIdCard,
            owner.ApplicationId,
            trimmedCategory,
            photoName,
            fileData.LongLength,
            operatorId is > 0 ? operatorId : null,
            operatorName ?? string.Empty);

        if (result.IsFailure)
        {
            logger.Warn($"档案照片保存失败: {result.ErrorCode} {result.Message}");
            return Result.Failure<long>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "保存照片失败");
        }

        logger.LogBusiness("档案照片上传",
            ("PhotoId", result.Value),
            ("Category", trimmedCategory),
            ("Applicant", DataMasker.MaskIdCard(trimmedIdCard)),
            ("Bytes", fileData.LongLength));

        return Result.Success(result.Value);
    }

    /// <summary>物理删除一张照片。</summary>
    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "照片 ID 无效");
        }

        var result = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_archive_photos WHERE id = $1", ct, id);
        if (result.IsFailure)
        {
            return Result.Failure(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "删除照片失败");
        }

        if (result.Value <= 0)
        {
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "照片不存在");
        }

        logger.LogBusiness("档案照片删除", ("PhotoId", id));
        return Result.Success();
    }

    /// <summary>
    /// 导出 ZIP：ids 非空按 ids（最多 500 张），否则按 keyword/category（最多 500 张）。
    /// 不转码，扩展名按库内 image_format；同名文件追加 _2、_3…。
    /// </summary>
    public async Task<Result<ArchivePhotoExport>> ExportAsync(
        string? keyword,
        string? category,
        string? ids,
        CancellationToken ct = default)
    {
        var idList = ParseIds(ids);
        var kw = keyword?.Trim() ?? string.Empty;
        var cat = category?.Trim() ?? string.Empty;

        const string select = @"
SELECT id,
       photo_category,
       photo_name,
       image_format,
       created_at,
       encode(photo_data, 'base64') AS photo_base64
FROM nc_biz_archive_photos";

        Result<List<ArchivePhotoExportRow>> queryResult;
        if (idList.Count > 0)
        {
            queryResult = await database.QueryAsync<ArchivePhotoExportRow>(
                select + @"
 WHERE id = ANY($1::bigint[])
 ORDER BY created_at DESC, id DESC
 LIMIT $2",
                ct, idList.ToArray(), MaxExportPhotos);
        }
        else
        {
            queryResult = await database.QueryAsync<ArchivePhotoExportRow>(
                select + @"
 WHERE ($1::text = ''
        OR applicant_id_card ILIKE '%' || $1::text || '%'
        OR COALESCE(photo_name, '') ILIKE '%' || $1::text || '%')
   AND ($2::text = '' OR photo_category = $2::text)
 ORDER BY created_at DESC, id DESC
 LIMIT $3",
                ct, kw, cat, MaxExportPhotos);
        }

        if (queryResult.IsFailure)
        {
            return Result.Failure<ArchivePhotoExport>(
                queryResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                queryResult.Message ?? "导出照片查询失败");
        }

        var rows = (queryResult.Value ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.PhotoBase64))
            .ToList();
        if (rows.Count == 0)
        {
            return Result.Failure<ArchivePhotoExport>(
                ErrorCodes.NOT_FOUND, "没有可导出的照片");
        }

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                byte[] data;
                try
                {
                    data = Convert.FromBase64String(row.PhotoBase64!);
                }
                catch (FormatException)
                {
                    logger.Warn($"档案照片导出跳过（Base64 解码失败）: Id={row.Id}");
                    continue;
                }

                if (data.Length == 0)
                {
                    continue;
                }

                var format = ResolveExportFormat(row.ImageFormat, data);
                var baseName = PhotoNameBuilder.SanitizeFileName(row.PhotoName);
                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = $"{row.PhotoCategory}_{row.CreatedAt:yyyyMMdd_HHmmss}_{row.Id}";
                }

                var extension = PhotoFormatConstants.GetExtension(format);
                var entryName = baseName + extension;
                var suffix = 2;
                while (!usedNames.Add(entryName))
                {
                    entryName = $"{baseName}_{suffix}{extension}";
                    suffix++;
                }

                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(data, ct);
            }
        }

        var fileName = $"档案照片_{DateTime.Now:yyyyMMddHHmm}.zip";
        logger.LogBusiness("档案照片导出", ("Count", rows.Count), ("FileName", fileName));

        return Result.Success(new ArchivePhotoExport(buffer.ToArray(), fileName));
    }

    /// <summary>身份证 → 三类业务档案中的归属人（低收入优先，其次高龄、临时救助）。</summary>
    public async Task<Result<ArchivePhotoOwner>> ResolveOwnerAsync(
        string idCard, CancellationToken ct = default)
    {
        var trimmed = idCard?.Trim() ?? string.Empty;
        if (!IdCardValidator.IsValid(trimmed))
        {
            return Result.Failure<ArchivePhotoOwner>(
                ErrorCodes.VALIDATION_FAILED, "身份证号格式不正确（需 18 位）");
        }

        const string sql = @"
SELECT person_name, application_id
FROM (
    SELECT a.applicant_name AS person_name,
           a.id AS application_id,
           1 AS priority
    FROM nc_biz_applications a
    WHERE a.applicant_id_card = $1 AND a.deleted_at IS NULL

    UNION ALL

    SELECT e.name AS person_name,
           NULL::bigint AS application_id,
           2 AS priority
    FROM nc_biz_elderly_applications e
    WHERE e.id_card = $1 AND e.deleted_at IS NULL

    UNION ALL

    SELECT t.applicant_name AS person_name,
           NULL::bigint AS application_id,
           3 AS priority
    FROM nc_biz_temp_relief_applications t
    WHERE t.applicant_id_card = $1 AND t.deleted_at IS NULL
) owner
ORDER BY priority, application_id DESC NULLS LAST
LIMIT 1";

        var result = await database.QuerySingleAsync<ArchivePhotoOwner?>(sql, ct, trimmed);
        if (result.IsFailure)
        {
            return Result.Failure<ArchivePhotoOwner>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "照片归属人解析失败");
        }

        if (result.Value is null)
        {
            return Result.Failure<ArchivePhotoOwner>(
                ErrorCodes.RECORD_NOT_FOUND,
                "该身份证不在低收入/高龄/临时救助档案中");
        }

        return Result.Success(result.Value);
    }

    private async Task<Result<int>> ResolveNextSequenceAsync(
        string idCard, string category, CancellationToken ct)
    {
        const string sql = @"
SELECT photo_name
FROM nc_biz_archive_photos
WHERE applicant_id_card = $1 AND photo_category = $2
ORDER BY id DESC
LIMIT 500";

        var result = await database.QueryAsync<ArchivePhotoNameRow>(sql, ct, idCard, category);
        if (result.IsFailure)
        {
            return Result.Failure<int>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "照片编号解析失败");
        }

        var existing = (result.Value ?? [])
            .Select(row => new ArchivePhoto { PhotoName = row.PhotoName })
            .ToList();

        return Result.Success(PhotoNameBuilder.NextSequence(existing));
    }

    private static List<long> ParseIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return [];
        }

        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(MaxExportPhotos)
            .ToList();
    }

    private static bool IsAllowedCategory(string category)
        => PhotoCategoryConstants.All.Any(item => string.Equals(item.Code, category, StringComparison.Ordinal));

    private static bool IsAllowedFormat(string format)
        => Array.IndexOf(AllowedFormats, format) >= 0;

    private static string NormalizeFormat(string? format) => (format ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        PhotoFormatConstants.WebP => PhotoFormatConstants.WebP,
        PhotoFormatConstants.Png => PhotoFormatConstants.Png,
        _ => PhotoFormatConstants.Jpeg
    };

    /// <summary>导出格式：优先按魔数识别，识别失败回退库内 image_format；未知按 JPEG。</summary>
    private static string ResolveExportFormat(string? storedFormat, byte[] data)
    {
        var detected = ImageTranscoder.DetectFormat(data);
        return string.IsNullOrEmpty(detected) ? NormalizeFormat(storedFormat) : detected;
    }

    private sealed class ArchivePhotoListRow
    {
        public long Id { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? PhotoCategory { get; set; }
        public string? PhotoName { get; set; }
        public long FileSize { get; set; }
        public string? ImageFormat { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool HasData { get; set; }
    }

    private sealed class ArchivePhotoDetailRow
    {
        public long Id { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? PhotoCategory { get; set; }
        public string? PhotoName { get; set; }
        public long FileSize { get; set; }
        public string? ImageFormat { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? PhotoBase64 { get; set; }
    }

    private sealed class ArchivePhotoExportRow
    {
        public long Id { get; set; }
        public string? PhotoCategory { get; set; }
        public string? PhotoName { get; set; }
        public string? ImageFormat { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? PhotoBase64 { get; set; }
    }

    private sealed class ArchivePhotoNameRow
    {
        public string? PhotoName { get; set; }
    }
}

/// <summary>照片分页结果。</summary>
public sealed record ArchivePhotoPage(
    IReadOnlyList<ArchivePhotoListItem> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>照片列表项（不含二进制）。</summary>
public sealed record ArchivePhotoListItem(
    long Id,
    string ApplicantIdCard,
    string PhotoCategory,
    string PhotoCategoryDisplay,
    string PhotoName,
    long FileSize,
    string ImageFormat,
    DateTime CreatedAt,
    bool HasData);

/// <summary>照片详情（含 Base64）。</summary>
public sealed record ArchivePhotoDetail(
    long Id,
    string ApplicantIdCard,
    string PhotoCategory,
    string PhotoCategoryDisplay,
    string PhotoName,
    string ImageFormat,
    long FileSize,
    DateTime CreatedAt,
    string PhotoBase64);

/// <summary>照片类别选项。</summary>
public sealed record ArchivePhotoCategoryOption(string Code, string Label);

/// <summary>照片归属人。</summary>
public sealed class ArchivePhotoOwner
{
    public string? PersonName { get; set; }

    /// <summary>仅低收入申请映射 nc_biz_applications.id；高龄/临时救助为 NULL。</summary>
    public long? ApplicationId { get; set; }
}

/// <summary>照片 ZIP 导出结果。</summary>
public sealed record ArchivePhotoExport(byte[] Content, string FileName);
