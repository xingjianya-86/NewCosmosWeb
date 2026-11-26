using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 待归属照片的数据范围过滤（服务端单点组装）：
/// 识别身份证须在 <see cref="AllowedIdCards"/> 内；未识别照片仅对上传人本人
/// 或 <see cref="SharedOrganizationIds"/> 内组织成员可见（SELF 范围传空集合）。
/// </summary>
public sealed record PendingPhotoScopeFilter(
    IReadOnlyCollection<string> AllowedIdCards,
    int CurrentUserId,
    IReadOnlyCollection<int> SharedOrganizationIds);

/// <summary>
/// 待归属照片池：无身份证先存照片，拿到身份证并确认人员已建档后归属到档案照片。
/// 移植自 PC 端同名服务（Services\Domain\ArchiveManagement\IArchivePhotoPendingService），
/// Web Core 版使用 <c>IDatabaseService</c> / <c>ILoggerService</c> / <c>CurrentUserContext</c>。
/// </summary>
public interface IArchivePhotoPendingService
{
    /// <summary>存入一张待归属照片（内部压缩为 WebP），返回记录 ID。</summary>
    Task<Result<long>> SavePendingAsync(byte[] data, string category, string? photoName,
        string? recognizedName, string? recognizedIdCard, string? source, CancellationToken ct = default);

    /// <summary>
    /// 分页查询待归属照片（可按关键词/类别过滤），不含 photo_data。
    /// <paramref name="scope"/> 非 null = 数据范围受限，按身份证集合 + 上传人/组织过滤
    /// （服务端单点过滤，避免逐页扫描）。
    /// </summary>
    Task<Result<PagedResult<PendingArchivePhoto>>> GetPendingPagedAsync(string? keyword, string? category,
        int pageIndex, int pageSize,
        PendingPhotoScopeFilter? scope = null,
        CancellationToken ct = default);

    /// <summary>取单条待归属照片元数据（不含 photo_data；内容端点校验格式与权限用）。</summary>
    Task<Result<PendingArchivePhoto?>> GetPendingMetaAsync(long id, CancellationToken ct = default);

    /// <summary>取回待归属照片二进制（预览用）。</summary>
    Task<Result<byte[]>> GetPendingDataAsync(long id, CancellationToken ct = default);

    /// <summary>删除一张待归属照片。</summary>
    Task<Result<bool>> DeletePendingAsync(long id, CancellationToken ct = default);

    /// <summary>统计待归属照片数量（页面徽标用）。</summary>
    Task<Result<int>> CountPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// 把待归属照片归属到已建档人员：按身份证解析归属人，逐张走
    /// <see cref="IArchivePhotoService.InsertPhotosAsync"/>（压缩/命名/体积校验与采集同源），
    /// 成功后写 nc_biz_archive_photos 并删除对应待归属行。
    /// <paramref name="personName"/> / <paramref name="categoryOverride"/> / <paramref name="applicationId"/>
    /// 为空时沿用档案/待归属记录原值。
    /// </summary>
    Task<Result<BatchPhotoInsertResult>> AssignPendingAsync(IReadOnlyList<long> ids, string idCard,
        string? personName = null, string? categoryOverride = null, long? applicationId = null,
        CancellationToken ct = default);
}
