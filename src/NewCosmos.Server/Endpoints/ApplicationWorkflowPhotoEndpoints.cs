using NewCosmos.Constants;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Database;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 业务申请工作流「照片」接口：按申请当前数据范围校验后，
/// 取该申请人（身份证匹配）在 nc_biz_archive_photos 中的照片。
/// 照片按人跨申请归档，故先解析申请主表的 applicant_id_card 再查询。
/// </summary>
public static class ApplicationWorkflowPhotoEndpoints
{
    /// <summary>单次最多返回的照片数（与旧版照片查询一致，避免 Base64 响应过大）。</summary>
    private const int MaxPhotos = 50;

    public static IEndpointRouteBuilder MapApplicationWorkflowPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/social-assistance/applications/{id:long}/photos", GetPhotosAsync);
        return app;
    }

    private static async Task<IResult> GetPhotosAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder scopeBuilder,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        // 数据范围闸：越权与不存在统一 404，不泄露申请是否存在。
        var parameters = new List<object?> { id };
        var scope = await scopeBuilder.BuildAsync(
            userId, DataScopeTarget.Applications, "a", parameters.Count + 1, ct);
        var applicationSql =
            "SELECT a.applicant_id_card FROM nc_biz_applications a " +
            "WHERE a.id = $1 AND a.deleted_at IS NULL";
        if (!scope.IsEmpty)
        {
            applicationSql += " AND " + scope.Sql;
            parameters.AddRange(scope.Parameters);
        }
        applicationSql += " LIMIT 1";

        var idCardResult = await database.QueryAsync<ApplicationPhotoSubjectRow>(
            applicationSql, ct, parameters.ToArray());
        if (idCardResult.IsFailure)
        {
            return ApiErrors.FromResult(context, idCardResult);
        }

        var subject = (idCardResult.Value ?? []).FirstOrDefault();
        if (subject is null)
        {
            // 不存在与越权同响应，避免泄露申请是否存在。
            return ApiErrors.Write(
                context,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "申请不存在或无权访问");
        }

        var idCard = subject.ApplicantIdCard?.Trim();
        if (string.IsNullOrEmpty(idCard))
        {
            return Results.Json(new ApplicationWorkflowPhotoListResponse([]));
        }

        var photosResult = await database.QueryAsync<WorkflowPhotoRow>(
            @"SELECT id, photo_category, photo_name, image_format, photo_data
              FROM nc_biz_archive_photos
              WHERE applicant_id_card = $1 AND photo_data IS NOT NULL
              ORDER BY id DESC
              LIMIT $2",
            ct,
            idCard,
            MaxPhotos);
        if (photosResult.IsFailure)
        {
            return ApiErrors.FromResult(context, photosResult);
        }

        var photos = new List<ApplicationWorkflowPhotoDto>();
        foreach (var row in photosResult.Value ?? [])
        {
            var data = row.PhotoData;
            if (data is not { Length: > 0 })
            {
                continue;
            }

            // 格式字段为空按 jpeg；jpg/未知值同样归一为 jpeg（与 PhotoFormatConstants 口径一致）。
            var format = (row.ImageFormat ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                PhotoFormatConstants.WebP => PhotoFormatConstants.WebP,
                PhotoFormatConstants.Png => PhotoFormatConstants.Png,
                _ => PhotoFormatConstants.Jpeg
            };
            var dataUrl =
                $"data:{PhotoFormatConstants.GetMimeType(format)};base64,{Convert.ToBase64String(data)}";

            photos.Add(new ApplicationWorkflowPhotoDto(
                row.Id,
                row.PhotoCategory ?? string.Empty,
                string.IsNullOrWhiteSpace(row.PhotoName) ? $"照片 #{row.Id}" : row.PhotoName!,
                format,
                dataUrl));
        }

        return Results.Json(new ApplicationWorkflowPhotoListResponse(photos));
    }

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");

    private sealed class ApplicationPhotoSubjectRow
    {
        public string? ApplicantIdCard { get; set; }
    }

    private sealed class WorkflowPhotoRow
    {
        public long Id { get; set; }
        public string? PhotoCategory { get; set; }
        public string? PhotoName { get; set; }
        public string? ImageFormat { get; set; }
        public byte[]? PhotoData { get; set; }
    }
}
