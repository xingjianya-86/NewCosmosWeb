namespace NewCosmos.Server.Contracts;

#region 业务申请工作流（只读）

/// <summary>
/// 五阶段计数。资产核查两个计数在用户无 ASSET_VIEW 时为 null；
/// 查询失败时对应字段也为 null，前端显示「—」而不是假 0。
/// </summary>
public sealed record WorkflowSummaryDto(
    bool AssetAccess,
    int? SubmittedAssetChecks,
    int? AssetChecksWithReport,
    int? Drafts,
    int? ArchiveBuilt,
    int? Archived);

/// <summary>
/// 工作流卡片。资产核查 kind=asset，申请 kind=application。
/// </summary>
public sealed record WorkflowItemDto(
    string Key,
    string Kind,
    long Id,
    string Name,
    string IdCardMasked,
    string Status,
    string StatusText,
    string StatusTone,
    string Relation,
    string ApplicationNo,
    string FamilyAddress,
    string Community,
    DateTime? CreatedAt,
    bool IsSingleRescue,
    bool HasGraceNote,
    string? GraceNote,
    bool HasBatchHint,
    string? BatchHintText);

public sealed record WorkflowListResponse(
    IReadOnlyList<WorkflowItemDto> Items,
    int Tab,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

#endregion
