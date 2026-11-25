using System.Security.Cryptography;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 月度审核页附件能力：核查照片、PDF 报告版本、报告原文。
/// 所有 SQL 使用明确列与 $1..$n 参数；报告上传在环境事务内完成
/// “旧版本转历史 + 新版本落库”，并发上传按 check 行锁串行化。
/// 数据范围暂只做权限校验（不做机构/村社区过滤，后续补）。
/// </summary>
public sealed class AssetMonthlyAuditService(IDatabaseService database)
{
    /// <summary>单个上传文件上限：20MB。</summary>
    public const long MaxUploadBytes = 20L * 1024 * 1024;

    private const string LoadCheckSql =
        @"SELECT id, batch_id, applicant_name, applicant_id_card, head_id_card
          FROM nc_biz_asset_checks
          WHERE id = $1 AND deleted_at IS NULL
          LIMIT 1";

    private const string LoadCheckForUpdateSql =
        @"SELECT id, batch_id, applicant_name, applicant_id_card, head_id_card
          FROM nc_biz_asset_checks
          WHERE id = $1 AND deleted_at IS NULL
          LIMIT 1
          FOR UPDATE";

    /// <summary>check 是否存在（未删除）。供驳回等只写操作先做 404 判定。</summary>
    public async Task<Result<bool>> CheckExistsAsync(long checkId, CancellationToken ct = default)
    {
        var result = await database.ExecuteScalarAsync<long?>(
            "SELECT id FROM nc_biz_asset_checks WHERE id = $1 AND deleted_at IS NULL LIMIT 1",
            ct,
            checkId);

        return result.IsFailure
            ? Result.Failure<bool>(result.ErrorCode!, result.Message!)
            : Result.Success(result.Value is not null);
    }

    /// <summary>按 check 的申请人身份证号返回全部归档照片（原图 Base64）。</summary>
    public async Task<Result<AssetCheckPhotoListResponse>> ListPhotosAsync(
        long checkId,
        CancellationToken ct = default)
    {
        var checkResult = await LoadCheckAsync(checkId, forUpdate: false, ct);
        if (checkResult.IsFailure)
        {
            return Result.Failure<AssetCheckPhotoListResponse>(
                checkResult.ErrorCode!, checkResult.Message!);
        }

        var idCard = (checkResult.Value.ApplicantIdCard ?? string.Empty).Trim();
        if (idCard.Length == 0)
        {
            return Result.Success(new AssetCheckPhotoListResponse([]));
        }

        const string sql = @"
            SELECT id, photo_category, photo_name, image_format, file_size, photo_data
            FROM nc_biz_archive_photos
            WHERE applicant_id_card = $1 AND photo_data IS NOT NULL
            ORDER BY created_at DESC, id DESC
            LIMIT 500";

        var rowsResult = await database.QueryAsync<ArchivePhotoRow>(sql, ct, idCard);
        if (rowsResult.IsFailure)
        {
            return Result.Failure<AssetCheckPhotoListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var photos = new List<AssetCheckPhotoItem>();
        foreach (var row in rowsResult.Value ?? [])
        {
            var data = row.PhotoData ?? [];
            photos.Add(new AssetCheckPhotoItem(
                row.Id,
                row.PhotoCategory ?? string.Empty,
                string.IsNullOrWhiteSpace(row.PhotoName) ? $"照片 #{row.Id}" : row.PhotoName!,
                row.ImageFormat ?? string.Empty,
                row.FileSize > 0 ? row.FileSize : data.LongLength,
                data.Length > 0 ? Convert.ToBase64String(data) : string.Empty));
        }

        return Result.Success(new AssetCheckPhotoListResponse(photos));
    }

    /// <summary>上传一张归档照片到 nc_biz_archive_photos（按申请人身份证归档）。</summary>
    public async Task<Result<long>> UploadPhotoAsync(
        long checkId,
        string fileName,
        string? category,
        byte[] data,
        int userId,
        string userName,
        CancellationToken ct = default)
    {
        var checkResult = await LoadCheckAsync(checkId, forUpdate: false, ct);
        if (checkResult.IsFailure)
        {
            return Result.Failure<long>(checkResult.ErrorCode!, checkResult.Message!);
        }

        var check = checkResult.Value;
        var idCard = (check.ApplicantIdCard ?? string.Empty).Trim();
        if (idCard.Length == 0)
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, "核查记录缺少申请人身份证号，无法归档照片");
        }

        var effectiveCategory = string.IsNullOrWhiteSpace(category)
            ? PhotoCategoryConstants.PERSON_PHOTO
            : category.Trim();
        var format = ResolvePhotoFormat(fileName);

        const string sql = @"
            INSERT INTO nc_biz_archive_photos
                (photo_data, applicant_id_card, application_id, photo_category, photo_name,
                 file_size, created_by, created_by_name, created_at, image_format)
            VALUES ($1, $2, NULL, $3, $4, $5, $6, $7, NOW(), $8)
            RETURNING id";

        var insertResult = await database.ExecuteScalarAsync<long?>(
            sql,
            ct,
            data,
            idCard,
            effectiveCategory,
            fileName,
            (long)data.Length,
            userId,
            userName,
            format);

        return insertResult.IsFailure
            ? Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!)
            : insertResult.Value is { } id
                ? Result.Success(id)
                : Result.Failure<long>(ErrorCodes.UNKNOWN_ERROR, "照片写入失败：未返回记录 ID");
    }

    /// <summary>按 check 返回报告版本列表（版本号倒序）。</summary>
    public async Task<Result<AssetCheckReportListResponse>> ListReportsAsync(
        long checkId,
        CancellationToken ct = default)
    {
        var checkResult = await LoadCheckAsync(checkId, forUpdate: false, ct);
        if (checkResult.IsFailure)
        {
            return Result.Failure<AssetCheckReportListResponse>(
                checkResult.ErrorCode!, checkResult.Message!);
        }

        const string sql = @"
            SELECT id, report_file_name, report_date, version, is_current,
                   COALESCE(report_size, octet_length(report_data)) AS size
            FROM nc_biz_asset_check_reports
            WHERE check_id = $1
            ORDER BY version DESC, id DESC
            LIMIT 200";

        var rowsResult = await database.QueryAsync<CheckReportRow>(sql, ct, checkId);
        if (rowsResult.IsFailure)
        {
            return Result.Failure<AssetCheckReportListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var reports = (rowsResult.Value ?? [])
            .Select(row => new AssetCheckReportItem(
                row.Id,
                row.ReportFileName ?? string.Empty,
                row.ReportDate,
                row.Version,
                row.IsCurrent,
                row.Size ?? 0))
            .ToList();

        return Result.Success(new AssetCheckReportListResponse(reports));
    }

    /// <summary>
    /// 上传新的报告版本：同 check 最大 version + 1；旧当前版本转历史后写入新行。
    /// 全部动作在环境事务内完成；check 行 FOR UPDATE 防止同一 check 并发上传拿到相同版本号。
    /// </summary>
    public async Task<Result<AssetCheckReportUploadResult>> UploadReportAsync(
        long checkId,
        string fileName,
        byte[] data,
        int userId,
        CancellationToken ct = default)
    {
        if (data.Length == 0)
        {
            return Result.Failure<AssetCheckReportUploadResult>(
                ErrorCodes.VALIDATION_FAILED, "报告文件不能为空");
        }

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        var checkResult = await LoadCheckAsync(checkId, forUpdate: true, ct);
        if (checkResult.IsFailure)
        {
            return Result.Failure<AssetCheckReportUploadResult>(
                checkResult.ErrorCode!, checkResult.Message!);
        }

        var check = checkResult.Value;
        var headIdCard = (check.HeadIdCard ?? string.Empty).Trim();

        var versionResult = await database.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(version), 0) + 1 FROM nc_biz_asset_check_reports WHERE check_id = $1",
            ct,
            checkId);
        if (versionResult.IsFailure)
        {
            return Result.Failure<AssetCheckReportUploadResult>(
                versionResult.ErrorCode!, versionResult.Message!);
        }

        var version = versionResult.Value <= 0 ? 1 : versionResult.Value;

        // 每户只允许一条 is_current（ux_check_reports_current_per_head 为 head_id_card 唯一索引），
        // 因此同户其他成员行上的当前报告也一并转历史；无 head_id_card 的旧数据按 check_id 兜底。
        const string supersedeSql = @"
            UPDATE nc_biz_asset_check_reports
            SET is_current = FALSE, superseded_at = NOW()
            WHERE is_current
              AND (head_id_card = $1 OR check_id = $2)";

        var supersedeResult = await database.ExecuteNonQueryAsync(
            supersedeSql, ct, headIdCard, checkId);
        if (supersedeResult.IsFailure)
        {
            return Result.Failure<AssetCheckReportUploadResult>(
                supersedeResult.ErrorCode!, supersedeResult.Message!);
        }

        var fileHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        const string insertSql = @"
            INSERT INTO nc_biz_asset_check_reports
                (check_id, batch_id, applicant_name, applicant_id_card, report_file_name,
                 report_data, file_hash, is_valid, report_date, notes, created_at, created_by,
                 head_id_card, version, is_current, report_size)
            VALUES ($1, $2, $3, $4, $5, $6, $7, TRUE, NOW(), NULL, NOW(), $8, $9, $10, TRUE, $11)
            RETURNING id";

        var insertResult = await database.ExecuteScalarAsync<long?>(
            insertSql,
            ct,
            checkId,
            check.BatchId ?? string.Empty,
            check.ApplicantName ?? string.Empty,
            check.ApplicantIdCard ?? string.Empty,
            fileName,
            data,
            fileHash,
            userId,
            headIdCard,
            version,
            data.Length);

        if (insertResult.IsFailure)
        {
            return Result.Failure<AssetCheckReportUploadResult>(
                insertResult.ErrorCode!, insertResult.Message!);
        }

        if (insertResult.Value is not { } reportId)
        {
            return Result.Failure<AssetCheckReportUploadResult>(
                ErrorCodes.UNKNOWN_ERROR, "报告写入失败：未返回记录 ID");
        }

        await tx.CommitAsync(ct);
        return Result.Success(new AssetCheckReportUploadResult(reportId, version));
    }

    /// <summary>读取报告原始字节，供 PDF/图片预览与下载。</summary>
    public async Task<Result<AssetCheckReportContent>> GetReportContentAsync(
        long reportId,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT report_file_name, report_data
            FROM nc_biz_asset_check_reports
            WHERE id = $1
            LIMIT 1";

        var rowResult = await database.QuerySingleAsync<ReportContentRow>(sql, ct, reportId);
        if (rowResult.IsFailure)
        {
            return Result.Failure<AssetCheckReportContent>(
                rowResult.ErrorCode!, rowResult.Message!);
        }

        var row = rowResult.Value;
        if (row is null || row.ReportData is not { Length: > 0 })
        {
            return Result.Failure<AssetCheckReportContent>(
                ErrorCodes.NOT_FOUND, "报告文件不存在");
        }

        var fileName = string.IsNullOrWhiteSpace(row.ReportFileName)
            ? $"report-{reportId}"
            : row.ReportFileName!;

        return Result.Success(new AssetCheckReportContent(
            row.ReportData, ResolveContentType(fileName), fileName));
    }

    private async Task<Result<CheckRow>> LoadCheckAsync(
        long checkId,
        bool forUpdate,
        CancellationToken ct)
    {
        var result = await database.QuerySingleAsync<CheckRow>(
            forUpdate ? LoadCheckForUpdateSql : LoadCheckSql, ct, checkId);

        if (result.IsFailure)
        {
            return Result.Failure<CheckRow>(result.ErrorCode!, result.Message!);
        }

        return result.Value is null
            ? Result.Failure<CheckRow>(ErrorCodes.NOT_FOUND, "核对记录不存在")
            : Result.Success(result.Value);
    }

    /// <summary>
    /// 扩展名 → 数据库 image_format。表约束只接受 webp/jpeg/png，
    /// 因此 .jpg/.jpeg 统一存 jpeg，未知格式按 JPEG 兼容（与 PhotoFormatConstants 一致）。
    /// </summary>
    private static string ResolvePhotoFormat(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => PhotoFormatConstants.Png,
            ".webp" => PhotoFormatConstants.WebP,
            _ => PhotoFormatConstants.Jpeg,
        };

    private static string ResolveContentType(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" or ".jpe" => "image/jpeg",
            _ => "application/octet-stream",
        };

    private sealed class CheckRow
    {
        public long Id { get; set; }
        public string? BatchId { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? HeadIdCard { get; set; }
    }

    private sealed class ArchivePhotoRow
    {
        public long Id { get; set; }
        public string? PhotoCategory { get; set; }
        public string? PhotoName { get; set; }
        public string? ImageFormat { get; set; }
        public long FileSize { get; set; }
        public byte[]? PhotoData { get; set; }
    }

    private sealed class CheckReportRow
    {
        public long Id { get; set; }
        public string? ReportFileName { get; set; }
        public DateTime? ReportDate { get; set; }
        public int Version { get; set; }
        public bool IsCurrent { get; set; }
        public long? Size { get; set; }
    }

    private sealed class ReportContentRow
    {
        public string? ReportFileName { get; set; }
        public byte[]? ReportData { get; set; }
    }
}

public sealed record AssetCheckPhotoListResponse(IReadOnlyList<AssetCheckPhotoItem> Photos);

public sealed record AssetCheckPhotoItem(
    long Id,
    string Category,
    string Name,
    string Format,
    long Size,
    string Base64);

public sealed record AssetCheckReportListResponse(IReadOnlyList<AssetCheckReportItem> Reports);

public sealed record AssetCheckReportItem(
    long Id,
    string FileName,
    DateTime? ReportDate,
    int Version,
    bool IsCurrent,
    long Size);

public sealed record AssetCheckReportUploadResult(long Id, int Version);

public sealed record AssetCheckReportContent(byte[] Data, string ContentType, string FileName);
