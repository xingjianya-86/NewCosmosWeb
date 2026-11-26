using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 待归属照片池实现（移植自 PC 端 ArchivePhotoPendingService）。
/// 与 PC 端差异：
/// ① 用户上下文取 <see cref="CurrentUserContext"/>（Web 请求级 AsyncLocal），不再依赖 MAUI App 静态属性；
/// ② 归属成功后**删除**待归属行（Web 端点契约「写 nc_biz_archive_photos，删除 pending」），
///    PC 端保留 status='Assigned' 审计痕迹；
/// ③ 归属支持姓名/类别/申请 ID 覆盖（Web 端点请求体可选字段）；
/// ④ 图片压缩放线程池执行，避免占用请求线程。
/// </summary>
public class ArchivePhotoPendingService : BaseService, IArchivePhotoPendingService
{
    protected override string ServiceName => "ArchivePhotoPendingService";

    private readonly IDatabaseService _db;
    private readonly IArchivePhotoService _photoService;

    public ArchivePhotoPendingService(IDatabaseService db, ILoggerService logger, IArchivePhotoService photoService)
        : base(logger)
    {
        _db = db;
        _photoService = photoService;
    }

    /// <summary>列表/元数据查询共用的列清单（不含 photo_data，列表不拉大字段）。</summary>
    private const string MetaColumns =
        "id, image_format, photo_category, photo_name, file_size, recognized_name, recognized_id_card, " +
        "source, status, assigned_photo_id, assigned_at, created_by, created_by_name, created_at";

    public async Task<Result<long>> SavePendingAsync(byte[] data, string category, string? photoName,
        string? recognizedName, string? recognizedIdCard, string? source, CancellationToken ct = default)
    {
        if (data == null || data.Length == 0)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片内容为空");

        var cat = string.IsNullOrWhiteSpace(category) ? PhotoCategoryConstants.OTHER_CERT : category.Trim();
        var compressed = await Task.Run(() => ImageCompressor.CompressToWebP(data, cat), ct);
        if (compressed.IsFailure)
            return Result.Failure<long>(compressed.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                compressed.Message ?? "照片压缩失败");

        var photo = compressed.Value;

        const string sql = @"
            INSERT INTO nc_biz_archive_photo_pending
                (photo_data, image_format, photo_category, photo_name, file_size,
                 recognized_name, recognized_id_card, source, status,
                 created_by, created_by_name, created_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,'Pending',$9,$10,NOW())
            RETURNING id";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            photo.Data, photo.Format, cat, photoName, photo.Data.LongLength,
            recognizedName, recognizedIdCard, source,
            CurrentUserContext.UserId, CurrentUserContext.FullName);
        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "待归属照片保存失败");
        if (result.Value <= 0)
            return Result.Failure<long>(ErrorCodes.DB_QUERY_ERROR, "待归属照片保存后未返回 ID");

        var pendingId = result.Value;

        Logger.LogBusiness("待归属照片保存",
            ("PendingId", pendingId),
            ("Category", cat),
            ("Size", photo.Data.Length),
            ("RecognizedIdCard", DataMasker.MaskIdCard(recognizedIdCard ?? string.Empty)));
        return Result.Success(pendingId);
    }

    public async Task<Result<PagedResult<PendingArchivePhoto>>> GetPendingPagedAsync(
        string? keyword, string? category, int pageIndex, int pageSize,
        PendingPhotoScopeFilter? scope = null,
        CancellationToken ct = default)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        const string where = @"
WHERE status = 'Pending'
  AND ($1::text = '' OR photo_category = $1::text)
  AND ($2::text = ''
       OR COALESCE(recognized_name, '') ILIKE '%' || $2::text || '%'
       OR COALESCE(recognized_id_card, '') ILIKE '%' || $2::text || '%'
       OR COALESCE(photo_name, '') ILIKE '%' || $2::text || '%')";

        // 受限范围：识别身份证在可管理集合内；未识别照片仅上传人本人或可管理组织成员可见。
        const string restrictedClause = @"
  AND ((COALESCE(recognized_id_card, '') <> '' AND recognized_id_card = ANY($3::text[]))
       OR (COALESCE(recognized_id_card, '') = '' AND (
              created_by = $4
              OR created_by IN (
                  SELECT u.id FROM nc_sys_users u WHERE u.organization_id = ANY($5::int[])
              )
          )))";

        var cat = category?.Trim() ?? string.Empty;
        var kw = keyword?.Trim() ?? string.Empty;

        string countSql;
        string listSql;
        object?[] countParams;
        object?[] listParams;

        if (scope != null)
        {
            var cards = scope.AllowedIdCards.ToArray();
            var sharedOrgs = scope.SharedOrganizationIds.ToArray();
            countSql = "SELECT COUNT(*) FROM nc_biz_archive_photo_pending" + where + restrictedClause;
            listSql = "SELECT " + MetaColumns + " FROM nc_biz_archive_photo_pending" + where + restrictedClause +
                      " ORDER BY created_at DESC, id DESC LIMIT $6 OFFSET $7";
            countParams = [cat, kw, cards, scope.CurrentUserId, sharedOrgs];
            listParams = [cat, kw, cards, scope.CurrentUserId, sharedOrgs, pageSize, (pageIndex - 1) * pageSize];
        }
        else
        {
            countSql = "SELECT COUNT(*) FROM nc_biz_archive_photo_pending" + where;
            listSql = "SELECT " + MetaColumns + " FROM nc_biz_archive_photo_pending" + where +
                      " ORDER BY created_at DESC, id DESC LIMIT $3 OFFSET $4";
            countParams = [cat, kw];
            listParams = [cat, kw, pageSize, (pageIndex - 1) * pageSize];
        }

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, countParams);
        if (countResult.IsFailure)
        {
            LogError($"待归属照片计数失败: {countResult.Message}");
            return Result.Failure<PagedResult<PendingArchivePhoto>>(
                countResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, countResult.Message ?? "待归属照片计数失败");
        }

        var listResult = await _db.QueryAsync<PendingArchivePhoto>(listSql, ct, listParams);
        if (listResult.IsFailure)
        {
            LogError($"待归属照片查询失败: {listResult.Message}");
            return Result.Failure<PagedResult<PendingArchivePhoto>>(
                listResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, listResult.Message ?? "待归属照片查询失败");
        }

        return Result.Success(PagedResult<PendingArchivePhoto>.FromList(
            listResult.Value ?? new List<PendingArchivePhoto>(),
            pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<PendingArchivePhoto?>> GetPendingMetaAsync(long id, CancellationToken ct = default)
    {
        // JOIN 上传人组织：受限范围判断「未识别照片」可见性用（CreatedByOrganizationId 非本表列）。
        const string sql = @"
SELECT p.id, p.image_format, p.photo_category, p.photo_name, p.file_size,
       p.recognized_name, p.recognized_id_card, p.source, p.status,
       p.assigned_photo_id, p.assigned_at, p.created_by, p.created_by_name, p.created_at,
       u.organization_id AS created_by_organization_id
FROM nc_biz_archive_photo_pending p
LEFT JOIN nc_sys_users u ON u.id = p.created_by
WHERE p.id = $1 AND p.status = 'Pending'";

        var result = await _db.QuerySingleAsync<PendingArchivePhoto?>(sql, ct, id);
        if (result.IsFailure)
        {
            LogError($"待归属照片元数据查询失败: Id={id}, {result.Message}");
            return Result.Failure<PendingArchivePhoto?>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "读取待归属照片失败");
        }

        return Result.Success(result.Value);
    }

    public async Task<Result<byte[]>> GetPendingDataAsync(long id, CancellationToken ct = default)
    {
        var result = await _db.QuerySingleAsync<byte[]>(
            "SELECT photo_data FROM nc_biz_archive_photo_pending WHERE id = $1 AND status = 'Pending'", ct, id);
        if (result.IsFailure)
            return Result.Failure<byte[]>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "读取待归属照片失败");
        if (result.Value is not { Length: > 0 })
            return Result.Failure<byte[]>(ErrorCodes.RECORD_NOT_FOUND, "待归属照片不存在或已归属");

        return Result.Success(result.Value);
    }

    public async Task<Result<bool>> DeletePendingAsync(long id, CancellationToken ct = default)
    {
        var result = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_archive_photo_pending WHERE id = $1 AND status = 'Pending'", ct, id);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "删除待归属照片失败");
        if (result.Value == 0)
            return Result.Failure<bool>(ErrorCodes.RECORD_NOT_FOUND, "待归属照片不存在或已归属");

        Logger.LogBusiness("删除待归属照片", ("PendingId", id));
        return Result.Success(true);
    }

    public async Task<Result<int>> CountPendingAsync(CancellationToken ct = default)
    {
        var result = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_archive_photo_pending WHERE status = 'Pending'", ct);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "统计待归属照片失败");

        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<BatchPhotoInsertResult>> AssignPendingAsync(
        IReadOnlyList<long> ids, string idCard,
        string? personName = null, string? categoryOverride = null, long? applicationId = null,
        CancellationToken ct = default)
    {
        if (ids == null || ids.Count == 0)
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.VALIDATION_FAILED, "请选择待归属照片");

        if (!string.IsNullOrWhiteSpace(categoryOverride)
            && !PhotoCategoryConstants.All.Any(item =>
                string.Equals(item.Code, categoryOverride.Trim(), StringComparison.Ordinal)))
        {
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.VALIDATION_FAILED, "照片类别不存在");
        }

        var ownerResult = await _photoService.ResolveOwnerAsync(idCard, ct);
        if (ownerResult.IsFailure)
            return Result.Failure<BatchPhotoInsertResult>(ownerResult.ErrorCode!, ownerResult.Message!);

        var owner = ownerResult.Value;
        if (owner == null || string.IsNullOrWhiteSpace(owner.IdCard))
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.RECORD_NOT_FOUND,
                "该身份证不在低收入/高龄/临时救助/资产核查档案中，请先建立申请");

        // 可选覆盖：姓名（显示/命名用）、申请 ID（追溯用）；类别为空沿用待归属行原类别。
        if (!string.IsNullOrWhiteSpace(personName))
            owner.PersonName = personName.Trim();
        if (applicationId is > 0)
            owner.ApplicationId = applicationId;

        var idArray = ids.Distinct().ToArray();
        var rowsResult = await _db.QueryAsync<PendingPhotoDataRow>(
            @"SELECT id, photo_category, photo_data
              FROM nc_biz_archive_photo_pending
              WHERE id = ANY($1::bigint[]) AND status = 'Pending'", ct, (object)idArray);
        if (rowsResult.IsFailure)
            return Result.Failure<BatchPhotoInsertResult>(rowsResult.ErrorCode!, rowsResult.Message!);

        var rows = rowsResult.Value ?? new List<PendingPhotoDataRow>();
        if (rows.Count == 0)
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.RECORD_NOT_FOUND,
                "所选待归属照片不存在或已归属");

        var insertedIds = new List<long>();
        string? firstError = null;
        var succeeded = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var category = string.IsNullOrWhiteSpace(categoryOverride)
                ? row.PhotoCategory
                : categoryOverride.Trim();

            var insert = await _photoService.InsertPhotosAsync(
                owner, category, new[] { row.PhotoData }, null, ct);
            if (insert.IsFailure)
            {
                firstError ??= insert.Message;
                LogWarn($"待归属照片归属失败: PendingId={row.Id}, {insert.ErrorCode} {insert.Message}");
                continue;
            }

            if (insert.Value == null || insert.Value.PhotoIds.Count == 0)
            {
                firstError ??= "照片入库未返回 ID";
                continue;
            }

            var newId = insert.Value.PhotoIds[0];

            // 归属成功后删除待归属行（重复提交不会二次入库）。
            var delete = await _db.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_archive_photo_pending WHERE id = $1 AND status = 'Pending'", ct, row.Id);
            if (delete.IsFailure)
            {
                firstError ??= delete.Message;
                LogWarn($"待归属照片删除失败（照片已入库）: PendingId={row.Id}, PhotoId={newId}, {delete.Message}");
            }
            else if (delete.Value == 0)
            {
                LogWarn($"待归属照片已不存在（可能被并发归属）: PendingId={row.Id}, PhotoId={newId}");
            }

            insertedIds.Add(newId);
            succeeded++;
        }

        Logger.LogBusiness("待归属照片归属",
            ("IdCard", DataMasker.MaskIdCard(idCard)),
            ("Requested", idArray.Length),
            ("Succeeded", succeeded));

        return Result.Success(new BatchPhotoInsertResult
        {
            Total = idArray.Length,
            Succeeded = succeeded,
            FirstError = firstError,
            PhotoIds = insertedIds
        });
    }

    /// <summary>归属取数行（带可写属性才可映射；禁止改回元组查询）。</summary>
    private sealed class PendingPhotoDataRow
    {
        public long Id { get; set; }
        public string PhotoCategory { get; set; } = PhotoCategoryConstants.OTHER_CERT;
        public byte[] PhotoData { get; set; } = Array.Empty<byte>();
    }
}
