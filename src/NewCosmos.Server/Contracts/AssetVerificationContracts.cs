namespace NewCosmos.Server.Contracts;

#region 家庭经济状况核对（只读查询）

/// <summary>
/// 核对模块首页统计。统计口径由 Core 的 IStatisticsService 提供；
/// 查询失败时四个字段均为 null，由前端降级显示。
/// </summary>
public sealed record AssetVerificationModuleSummaryDto(
    int? PendingReportCount,
    int? MonthlyCompleted,
    int? MonthlyNew,
    int? YearTotal);

/// <summary>
/// 核对任务列表行。身份证/联系电话在此层强制脱敏；
/// statusText 使用 Core 的 AssetCheckStatusConstants.GetDescription。
/// </summary>
public sealed record AssetVerificationTaskItemDto(
    long Id,
    string BatchId,
    string ArchiveName,
    string IdCardMasked,
    string Relationship,
    bool IsHead,
    string HeadIdCardMasked,
    string FamilyAddress,
    string Community,
    DateTime ApplicationDate,
    string Status,
    string StatusText,
    string ContactPhoneMasked);

public sealed record AssetVerificationTaskListResponse(
    IReadOnlyList<AssetVerificationTaskItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>核对详情：身份证/联系电话返回完整值（仅详情接口），禁止写入日志。</summary>
public sealed record AssetVerificationDetailDto(
    long Id,
    string BatchId,
    string ApplicantName,
    string ApplicantIdCard,
    string ApplicantIdType,
    string Relationship,
    bool IsHead,
    string HeadIdCard,
    string FamilyAddress,
    string Community,
    string ApplicationReason,
    DateTime ApplicationDate,
    string ContactPhone,
    string Status,
    string StatusText,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AssetVerificationMemberDto> Members);

/// <summary>同批次家庭成员（详情接口附带，只读）。</summary>
public sealed record AssetVerificationMemberDto(
    long Id,
    string ApplicantName,
    string IdCard,
    string Relationship,
    bool IsHead);

#endregion
