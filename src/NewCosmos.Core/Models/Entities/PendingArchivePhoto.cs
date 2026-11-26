using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 待归属照片（nc_biz_archive_photo_pending）：身份证未定时先存，
/// 拿到身份证并确认人员已建档后再归属到 nc_biz_archive_photos。
/// 照片属公民身份敏感信息：存库 bytea（内网 PostgreSQL），新采集统一 WebP（单张 ≤500KB）。
/// </summary>
public class PendingArchivePhoto
{
    public long Id { get; set; }

    /// <summary>照片二进制（WebP）；列表查询不取该列。</summary>
    public byte[] PhotoData { get; set; } = Array.Empty<byte>();

    /// <summary>照片格式（webp/jpeg/png）。</summary>
    public string ImageFormat { get; set; } = PhotoFormatConstants.WebP;

    /// <summary>照片类别（PhotoCategoryConstants）。</summary>
    public string PhotoCategory { get; set; } = PhotoCategoryConstants.OTHER_CERT;

    /// <summary>照片备注/原名。</summary>
    public string? PhotoName { get; set; }

    /// <summary>照片字节数。</summary>
    public long FileSize { get; set; }

    /// <summary>OCR 识别姓名（可空）。</summary>
    public string? RecognizedName { get; set; }

    /// <summary>OCR 识别身份证号（可空）。</summary>
    public string? RecognizedIdCard { get; set; }

    /// <summary>来源（拍照/相册/拖入等）。</summary>
    public string? Source { get; set; }

    /// <summary>Pending 待归属 / Assigned 已归属。</summary>
    public string Status { get; set; } = PendingPhotoStatus.Pending;

    /// <summary>归属后 nc_biz_archive_photos.id。</summary>
    public long? AssignedPhotoId { get; set; }

    /// <summary>归属时间。</summary>
    public DateTime? AssignedAt { get; set; }

    /// <summary>创建人 ID（nc_sys_users.id）。</summary>
    public int? CreatedBy { get; set; }

    /// <summary>创建人姓名。</summary>
    public string? CreatedByName { get; set; }

    /// <summary>
    /// 上传人所属组织 ID（仅元数据 JOIN nc_sys_users 时填充，非本表列）。
    /// 数据范围受限时用于判断「未识别照片」是否在同组织可管理范围内。
    /// </summary>
    public int? CreatedByOrganizationId { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>类别中文名（列表展示用）。</summary>
    public string PhotoCategoryName => PhotoCategoryConstants.GetDisplayName(PhotoCategory);

    /// <summary>列表显示名：优先识别姓名，其次照片原名，最后回退「待归属照片 #Id」。</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(RecognizedName)
        ? RecognizedName!
        : (!string.IsNullOrWhiteSpace(PhotoName) ? PhotoName! : $"待归属照片 #{Id}");
}

/// <summary>待归属照片状态常量（与迁移脚本 status 取值一致）。</summary>
public static class PendingPhotoStatus
{
    public const string Pending = "Pending";
    public const string Assigned = "Assigned";
}
