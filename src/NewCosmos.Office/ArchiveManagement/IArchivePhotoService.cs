using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// Office 侧只读档案照片契约（从 legacy IArchivePhotoService 裁剪）：
/// 打印执行只需要"按人查照片元数据、按 Id 取照片字节"，采集/补传/水印等仍归服务端。
/// </summary>
public interface IArchivePhotoService
{
    /// <summary>列出指定人的照片元数据（不含 photo_data 二进制）。</summary>
    Task<Result<List<ArchivePhoto>>> GetByPersonAsync(
        string applicantIdCard, string? category = null, CancellationToken ct = default);

    /// <summary>取回单张照片的二进制（打印嵌入用）。</summary>
    Task<Result<byte[]>> GetPhotoDataAsync(long photoId, CancellationToken ct = default);
}
