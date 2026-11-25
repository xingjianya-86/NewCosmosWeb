using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 临时救助档案照片：按申请人身份证读写 nc_biz_archive_photos。
/// <para>
/// 临时救助主表 ID 与 nc_biz_applications.id 不是同一序列，照片 application_id 一律写 NULL，
/// 只按 applicant_id_card 聚合，避免把临时救助 ID 误当成低保申请 ID 取到别人的照片。
/// </para>
/// <para>
/// 所有读写先按当前登录用户数据范围校验临时救助记录（越权与不存在统一失败，不泄露存在性）。
/// SQL 只用 $1..$n、显式列、LIMIT。
/// </para>
/// </summary>
public sealed class TempReliefPhotoService(
    IDatabaseService database,
    DataScopeFilterBuilder scopeBuilder,
    ILoggerService logger)
{
    /// <summary>单次最多返回的照片数（避免 Base64 响应过大）。</summary>
    public const int MaxPhotos = 50;

    /// <summary>单张照片上限 10MB。</summary>
    public const long MaxPhotoBytes = 10L * 1024 * 1024;

    /// <summary>临时救助照片允许的类别（业务明确支持身份证 / 银行卡 / 病历）。</summary>
    private static readonly string[] AllowedCategories =
    {
        PhotoCategoryConstants.ID_CARD,
        PhotoCategoryConstants.BANK_CARD,
        PhotoCategoryConstants.MEDICAL_RECORD
    };

    public static bool IsAllowedCategory(string? category)
        => !string.IsNullOrWhiteSpace(category)
           && Array.IndexOf(AllowedCategories, category.Trim()) >= 0;

    /// <summary>
    /// 读取记录并做数据范围校验；越权与不存在统一返回同样的失败，端点映射 404。
    /// </summary>
    public async Task<Result<TempReliefPhotoSubject>> ResolveSubjectAsync(
        long applicationId, int userId, CancellationToken ct = default)
    {
        if (applicationId <= 0)
        {
            return Result.Failure<TempReliefPhotoSubject>(
                ErrorCodes.RECORD_NOT_FOUND, "临时救助申请不存在或无权访问");
        }

        var parameters = new List<object?> { applicationId };
        var scope = await scopeBuilder.BuildAsync(
            userId, DataScopeTarget.TempRelief, "a", parameters.Count + 1, ct);

        var sql =
            "SELECT a.id, a.applicant_name, a.applicant_id_card " +
            "FROM nc_biz_temp_relief_applications a " +
            "WHERE a.id = $1 AND a.deleted_at IS NULL";
        if (!scope.IsEmpty)
        {
            sql += " AND " + scope.Sql;
            parameters.AddRange(scope.Parameters);
        }
        sql += " LIMIT 1";

        var result = await database.QueryAsync<TempReliefPhotoSubject>(
            sql, ct, parameters.ToArray());
        if (result.IsFailure)
        {
            return Result.Failure<TempReliefPhotoSubject>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "读取临时救助申请失败");
        }

        var subject = (result.Value ?? []).FirstOrDefault();
        if (subject is null || string.IsNullOrWhiteSpace(subject.ApplicantIdCard))
        {
            return Result.Failure<TempReliefPhotoSubject>(
                ErrorCodes.RECORD_NOT_FOUND, "临时救助申请不存在或无权访问");
        }

        return Result.Success(subject);
    }

    public async Task<Result<IReadOnlyList<TempReliefPhotoDto>>> ListAsync(
        long applicationId,
        int userId,
        string? category,
        CancellationToken ct = default)
    {
        var subjectResult = await ResolveSubjectAsync(applicationId, userId, ct);
        if (subjectResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<TempReliefPhotoDto>>(
                subjectResult.ErrorCode!, subjectResult.Message!);
        }

        var subject = subjectResult.Value;
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (normalizedCategory is not null && !IsAllowedCategory(normalizedCategory))
        {
            return Result.Failure<IReadOnlyList<TempReliefPhotoDto>>(
                ErrorCodes.VALIDATION_FAILED, "照片类别仅支持身份证 / 银行卡 / 病历");
        }

        var result = normalizedCategory is null
            ? await database.QueryAsync<PhotoRow>(
                @"SELECT id, photo_category, photo_name, image_format, photo_data, file_size, created_at, created_by_name
                  FROM nc_biz_archive_photos
                  WHERE applicant_id_card = $1 AND photo_data IS NOT NULL AND photo_category = ANY($2::text[])
                  ORDER BY id DESC
                  LIMIT $3",
                ct, subject.ApplicantIdCard, AllowedCategories, MaxPhotos)
            : await database.QueryAsync<PhotoRow>(
                @"SELECT id, photo_category, photo_name, image_format, photo_data, file_size, created_at, created_by_name
                  FROM nc_biz_archive_photos
                  WHERE applicant_id_card = $1 AND photo_data IS NOT NULL AND photo_category = $2
                  ORDER BY id DESC
                  LIMIT $3",
                ct, subject.ApplicantIdCard, normalizedCategory, MaxPhotos);

        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<TempReliefPhotoDto>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "读取档案照片失败");
        }

        var photos = new List<TempReliefPhotoDto>();
        foreach (var row in result.Value ?? [])
        {
            if (row.PhotoData is not { Length: > 0 }) continue;

            var format = NormalizeFormat(row.ImageFormat);
            photos.Add(new TempReliefPhotoDto(
                row.Id,
                row.PhotoCategory ?? string.Empty,
                PhotoCategoryConstants.GetDisplayName(row.PhotoCategory),
                string.IsNullOrWhiteSpace(row.PhotoName) ? $"照片 #{row.Id}" : row.PhotoName!,
                format,
                $"data:{PhotoFormatConstants.GetMimeType(format)};base64,{Convert.ToBase64String(row.PhotoData)}",
                row.FileSize,
                row.CreatedAt,
                row.CreatedByName ?? string.Empty));
        }

        return Result.Success<IReadOnlyList<TempReliefPhotoDto>>(photos);
    }

    public async Task<Result<TempReliefPhotoUploadResponse>> AddAsync(
        long applicationId,
        int userId,
        string? userName,
        string? category,
        string? photoName,
        byte[] photoData,
        string? contentType,
        CancellationToken ct = default)
    {
        var subjectResult = await ResolveSubjectAsync(applicationId, userId, ct);
        if (subjectResult.IsFailure)
        {
            return Result.Failure<TempReliefPhotoUploadResponse>(
                subjectResult.ErrorCode!, subjectResult.Message!);
        }

        var normalizedCategory = (category ?? string.Empty).Trim();
        if (!IsAllowedCategory(normalizedCategory))
        {
            return Result.Failure<TempReliefPhotoUploadResponse>(
                ErrorCodes.VALIDATION_FAILED, "照片类别仅支持身份证 / 银行卡 / 病历");
        }

        if (photoData.Length == 0)
        {
            return Result.Failure<TempReliefPhotoUploadResponse>(
                ErrorCodes.VALIDATION_FAILED, "照片内容为空");
        }

        if (photoData.LongLength > MaxPhotoBytes)
        {
            return Result.Failure<TempReliefPhotoUploadResponse>(
                ErrorCodes.VALIDATION_FAILED, "照片不能超过 10MB");
        }

        var subject = subjectResult.Value;
        var format = ResolveFormat(contentType);
        var name = NormalizePhotoName(photoName, subject.ApplicantName, normalizedCategory);

        var result = await database.ExecuteScalarAsync<long>(
            @"INSERT INTO nc_biz_archive_photos
                  (photo_data, image_format, applicant_id_card, application_id, photo_category,
                   photo_name, file_size, created_by, created_by_name, created_at)
              VALUES ($1, $2, $3, NULL, $4, $5, $6, $7, $8, NOW())
              RETURNING id",
            ct,
            photoData,
            format,
            subject.ApplicantIdCard,
            normalizedCategory,
            name,
            photoData.LongLength,
            userId > 0 ? userId : null,
            userName ?? string.Empty);

        if (result.IsFailure)
        {
            return Result.Failure<TempReliefPhotoUploadResponse>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "保存档案照片失败");
        }

        logger.LogBusiness("临时救助档案照片上传",
            ("ApplicationId", applicationId),
            ("PhotoId", result.Value),
            ("Category", normalizedCategory),
            ("Applicant", DataMasker.MaskName(subject.ApplicantName ?? string.Empty)));

        return Result.Success(new TempReliefPhotoUploadResponse(
            result.Value, normalizedCategory, name, photoData.LongLength));
    }

    public async Task<Result> DeleteAsync(
        long applicationId,
        long photoId,
        int userId,
        CancellationToken ct = default)
    {
        if (photoId <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "照片 ID 无效");
        }

        var subjectResult = await ResolveSubjectAsync(applicationId, userId, ct);
        if (subjectResult.IsFailure)
        {
            return Result.Failure(subjectResult.ErrorCode!, subjectResult.Message!);
        }

        var subject = subjectResult.Value;
        var result = await database.ExecuteNonQueryAsync(
            @"DELETE FROM nc_biz_archive_photos
              WHERE id = $1 AND applicant_id_card = $2",
            ct, photoId, subject.ApplicantIdCard);

        if (result.IsFailure)
        {
            return Result.Failure(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "删除档案照片失败");
        }

        if (result.Value == 0)
        {
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "照片不存在或不属于该申请人");
        }

        logger.LogBusiness("临时救助档案照片删除",
            ("ApplicationId", applicationId),
            ("PhotoId", photoId),
            ("Applicant", DataMasker.MaskName(subject.ApplicantName ?? string.Empty)));

        return Result.Success();
    }

    private static string NormalizeFormat(string? format) => (format ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        PhotoFormatConstants.WebP => PhotoFormatConstants.WebP,
        PhotoFormatConstants.Png => PhotoFormatConstants.Png,
        _ => PhotoFormatConstants.Jpeg
    };

    private static string ResolveFormat(string? contentType) => (contentType ?? string.Empty).Trim().ToLowerInvariant()
        switch
        {
            "image/webp" => PhotoFormatConstants.WebP,
            "image/png" => PhotoFormatConstants.Png,
            _ => PhotoFormatConstants.Jpeg
        };

    private static string NormalizePhotoName(string? photoName, string? applicantName, string category)
    {
        var name = photoName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            return name.Length <= 200 ? name : name[..200];
        }

        var display = PhotoCategoryConstants.GetDisplayName(category);
        var owner = string.IsNullOrWhiteSpace(applicantName) ? "申请人" : applicantName.Trim();
        var generated = $"{owner}_{display}_{DateTime.Now:yyyyMMddHHmmss}";
        return generated.Length <= 200 ? generated : generated[..200];
    }

    private sealed class PhotoRow
    {
        public long Id { get; set; }
        public string? PhotoCategory { get; set; }
        public string? PhotoName { get; set; }
        public string? ImageFormat { get; set; }
        public byte[]? PhotoData { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
    }
}

/// <summary>照片归属主体（仅取数据范围校验所需的少量列）。</summary>
public sealed class TempReliefPhotoSubject
{
    public long Id { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
}
