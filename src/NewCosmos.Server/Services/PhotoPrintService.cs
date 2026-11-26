using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;

namespace NewCosmos.Server.Services;

/// <summary>照片元数据行（取字节前先定位归属人/类别/格式；不查询 photo_data）。</summary>
public sealed class PhotoImageRow
{
    public long Id { get; set; }
    public string? ApplicantIdCard { get; set; }
    public long? ApplicationId { get; set; }
    public string? PhotoCategory { get; set; }
    public string? PhotoName { get; set; }
    public long FileSize { get; set; }
    public string? ImageFormat { get; set; }
    public int? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>照片表元数据查询单点（列表/内容/导出/打印共用，禁止各自另抄列清单）。</summary>
public static class PhotoImageSql
{
    public const string SelectColumns =
        "id, applicant_id_card, application_id, photo_category, photo_name, " +
        "file_size, image_format, created_by, created_by_name, created_at";

    public const string SelectById =
        "SELECT " + SelectColumns + " FROM nc_biz_archive_photos WHERE id = $1";

    public const string SelectByIds =
        "SELECT " + SelectColumns + " FROM nc_biz_archive_photos WHERE id = ANY($1::bigint[])";
}

/// <summary>
/// 照片推送打印（照片中心）：逐张取图 → 组 <see cref="PrintJobRequest"/> → 入队。
/// <para>
/// 与 Office 的 <c>PhotoPrintPushHelper</c> 同一契约（BusinessType=ArchivePhoto、
/// JobKind=Photo、版式按类别默认），Server 因不引用 Office 故在此做最小装配；
/// 单张失败不中断其余，逐项收集失败原因。
/// </para>
/// </summary>
public sealed class PhotoPrintService(
    IArchivePhotoService photos,
    ArchivePrintQueueService queue,
    IDatabaseService database,
    ILoggerService logger)
{
    public async Task<PhotoPrintResponse> PushAsync(
        int userId,
        string requestedByName,
        IReadOnlyList<long> photoIds,
        string? layout,
        PrintJobTarget? target,
        Func<string?, bool> canAccess,
        CancellationToken ct = default)
    {
        var okCount = 0;
        var jobNos = new List<string>();
        var failures = new List<string>();

        var distinctIds = photoIds.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return new PhotoPrintResponse(0, jobNos, ["未选择照片"]);
        }

        var rowsResult = await database.QueryAsync<PhotoImageRow>(
            PhotoImageSql.SelectByIds, ct, (object)distinctIds);
        if (rowsResult.IsFailure)
        {
            logger.Error($"照片打印读取元数据失败: {rowsResult.ErrorCode} {rowsResult.Message}");
            return new PhotoPrintResponse(0, jobNos, ["读取照片信息失败"]);
        }

        var rows = (rowsResult.Value ?? [])
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var ownerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var photoId in distinctIds)
        {
            if (!rows.TryGetValue(photoId, out var row))
            {
                failures.Add($"照片 #{photoId}：不存在或已删除");
                continue;
            }

            var idCard = row.ApplicantIdCard?.Trim() ?? string.Empty;
            if (!canAccess(idCard))
            {
                failures.Add($"照片 #{photoId}：无权访问");
                continue;
            }

            var label = string.IsNullOrWhiteSpace(row.PhotoName)
                ? $"照片 #{photoId}"
                : row.PhotoName!;

            try
            {
                var data = await photos.GetPhotoDataAsync(photoId, ct);
                if (data.IsFailure || data.Value is not { Length: > 0 })
                {
                    failures.Add($"{label}：读取照片失败");
                    continue;
                }

                var applicantName = await ResolveOwnerNameAsync(idCard, ownerNames, ct);
                var category = string.IsNullOrWhiteSpace(row.PhotoCategory)
                    ? PhotoCategoryConstants.OTHER_CERT
                    : row.PhotoCategory!;

                var request = new PrintJobRequest
                {
                    BusinessType = PrintBusinessTypes.ArchivePhoto,
                    BusinessId = row.ApplicationId,
                    Classification = PhotoCategoryConstants.GetDisplayName(category),
                    ApplicantName = applicantName,
                    ApplicantIdCard = idCard,
                    PrinterName = target?.PrinterName,
                    Copies = 1,
                    IsDuplex = false,
                    JobKind = PrintJobConstants.JobKindPhoto,
                    PhotoData = data.Value,
                    PhotoLayout = layout ?? PhotoLayoutConstants.DefaultForCategory(category),
                    PhotoCategory = category,
                    RequestedBy = userId,
                    RequestedByName = requestedByName,
                    TargetUserId = target?.UserId,
                    TargetDeviceId = target?.DeviceId,
                    TargetPrinter = target?.PrinterName,
                };

                var enqueue = await queue.EnqueueAsync(request, ct);
                if (enqueue.IsSuccess)
                {
                    okCount++;
                    jobNos.Add(enqueue.Value?.JobNo ?? string.Empty);
                }
                else
                {
                    failures.Add($"{label}：{enqueue.Message}");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warn($"照片打印异常: PhotoId={photoId}, {ex.Message}");
                failures.Add($"{label}：{ex.Message}");
            }
        }

        logger.LogBusiness("照片推送打印",
            ("Requested", distinctIds.Length),
            ("Succeeded", okCount),
            ("Failed", failures.Count),
            ("OperatorId", userId));
        return new PhotoPrintResponse(okCount, jobNos, failures);
    }

    private async Task<string> ResolveOwnerNameAsync(
        string idCard,
        Dictionary<string, string> cache,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idCard))
        {
            return string.Empty;
        }

        if (cache.TryGetValue(idCard, out var cached))
        {
            return cached;
        }

        var owner = await photos.ResolveOwnerAsync(idCard, ct);
        var name = owner.IsSuccess ? owner.Value?.PersonName?.Trim() ?? string.Empty : string.Empty;
        cache[idCard] = name;
        return name;
    }
}
