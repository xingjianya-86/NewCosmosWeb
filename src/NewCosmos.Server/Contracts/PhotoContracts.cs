namespace NewCosmos.Server.Contracts;

#region 照片补全 / 照片中心

/// <summary>
/// 照片补全候选（人员维度）：同一身份证跨业务域聚合为一行。
/// 身份证按 PC 端照片管理口径返回完整值，仅用于已在档案中的人员定位。
/// </summary>
public sealed record PhotoBackfillCandidateDto(
    string IdCard,
    string PersonName,
    string BusinessTypes,
    string BusinessTypeNames,
    long BusinessId,
    int PhotoCount,
    string PhotoStateText,
    bool HasPhoto,
    int RequiredCount,
    string Status);

/// <summary>
/// 补全候选分页响应。<see cref="ScopeRestricted"/> 表示当前用户不是全量数据范围；
/// <see cref="ScopeTruncated"/> 表示受限范围下扫描候选达到安全上限（结果只含已扫描部分）。
/// </summary>
public sealed record PhotoBackfillListResponse(
    IReadOnlyList<PhotoBackfillCandidateDto> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool ScopeRestricted,
    bool ScopeTruncated,
    string? ScopeNote);

/// <summary>照片元数据行（列表/详情共用，绝不含 photo_data 二进制）。</summary>
public sealed record PhotoListItemDto(
    long Id,
    string IdCard,
    long? ApplicationId,
    string Category,
    string CategoryName,
    string? PhotoName,
    long FileSize,
    string ImageFormat,
    DateTime CreatedAt,
    string? CreatedByName);

public sealed record PhotoListResponse(
    IReadOnlyList<PhotoListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>照片归属人（档案人员）。</summary>
public sealed record PhotoOwnerDto(
    string IdCard,
    string PersonName,
    string BusinessTypes,
    string BusinessTypeNames,
    long? BusinessId,
    int PhotoCount,
    bool HasPhoto);

public sealed record PhotoCategoryCountDto(
    string Category,
    string CategoryName,
    int Count);

/// <summary>
/// 按人查看：归属人 + 照片元数据 + 各类别计数 + 缺失类别。
/// <see cref="MissingCategories"/> 按业务域（或归属人所属域并集）的常用证件类别计算。
/// </summary>
public sealed record PhotoPersonResponse(
    PhotoOwnerDto Owner,
    IReadOnlyList<PhotoListItemDto> Photos,
    IReadOnlyList<PhotoCategoryCountDto> CategoryCounts,
    IReadOnlyList<PhotoCategoryOptionDto> MissingCategories);

/// <summary>上传成功返回：新照片 ID / 命名 / 入库后字节数。</summary>
public sealed record PhotoUploadResponse(
    long Id,
    string PhotoName,
    long FileSize);

/// <summary>照片中心身份证 OCR 结果。</summary>
public sealed record PhotoOcrResponse(
    string Name,
    string IdCard,
    string Gender,
    string Address);

/// <summary>照片推送打印请求。</summary>
public sealed record PhotoPrintRequest(
    List<long>? PhotoIds,
    string? Layout,
    long? BindingId,
    string? PrinterName);

/// <summary>照片推送打印结果：成功张数 + 打印任务号 + 逐项失败原因。</summary>
public sealed record PhotoPrintResponse(
    int OkCount,
    IReadOnlyList<string> JobNos,
    IReadOnlyList<string> Failures);

/// <summary>待归属照片（照片中心可选模块；服务未移植时 available=false）。</summary>
public sealed record PhotoPendingResponse(
    IReadOnlyList<PhotoListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    bool Available,
    string Message);

public sealed record PhotoCategoryOptionDto(string Code, string Name);

public sealed record PhotoBusinessDomainOptionDto(string Code, string Name);

public sealed record PhotoLayoutOptionDto(string Code, string Name);

/// <summary>照片类别 / 业务域 / 打印版式选项。</summary>
public sealed record PhotoCategoriesResponse(
    IReadOnlyList<PhotoCategoryOptionDto> Categories,
    IReadOnlyList<PhotoBusinessDomainOptionDto> BusinessDomains,
    IReadOnlyList<PhotoLayoutOptionDto> Layouts);

#endregion
