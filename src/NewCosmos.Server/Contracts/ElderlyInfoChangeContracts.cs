namespace NewCosmos.Server.Contracts;

/// <summary>
/// 高龄年度认证·信息变更 Web API 合同 DTO。
/// 字段/状态以 docs/API_CONTRACT_ELDERLY_INFO_CHANGE.md 为准，前端不得自行拼装。
/// </summary>
public sealed record ElderlyInfoChangeDetailDto(
    long Id,
    string FieldKey,
    string FieldLabel,
    string Risk,
    string RiskText,
    string OldValue,
    string NewValue);

public sealed record ElderlyInfoChangeRecordDto(
    long Id,
    string ChangeNo,
    long? CampaignId,
    long? TaskId,
    long? ApplicationId,
    string IdCard,
    string Name,
    string ChangeType,
    string ChangeTypeText,
    string Reason,
    string Status,
    string StatusText,
    string SourceType,
    string SourceTypeText,
    string SourceFile,
    int? SourceRow,
    string EffectiveMonth,
    DateTime? AppliedAt,
    string AppliedBy,
    DateTime? ReviewedAt,
    string ReviewedBy,
    string ReviewOpinion,
    DateTime? CreatedAt,
    string CreatedBy,
    DateTime? UpdatedAt,
    IReadOnlyList<ElderlyInfoChangeDetailDto> Details);

public sealed record ElderlyInfoChangeListResponse(
    IReadOnlyList<ElderlyInfoChangeRecordDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

public sealed class ElderlyInfoChangeDetailRequest
{
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>可空：空时服务端取申请主表当前值。</summary>
    public string? OldValue { get; set; }

    public string NewValue { get; set; } = string.Empty;
}

public sealed class ElderlyInfoChangeCreateRequest
{
    public string Reason { get; set; } = string.Empty;

    /// <summary>Manual / CertificationImport / Certification。</summary>
    public string SourceType { get; set; } = "Manual";

    public string? SourceFile { get; set; }

    public int? SourceRow { get; set; }

    /// <summary>银行卡等生效月，yyyy-MM；可空。</summary>
    public string? EffectiveMonth { get; set; }

    public List<ElderlyInfoChangeDetailRequest> Details { get; set; } = new();
}

public sealed class ElderlyInfoChangeApproveRequest
{
    public string? ReviewOpinion { get; set; }

    public string? EffectiveMonth { get; set; }
}

public sealed class ElderlyInfoChangeRejectRequest
{
    public string? ReviewOpinion { get; set; }
}

public sealed record ElderlyInfoChangeFieldOptionDto(
    string FieldKey,
    string Label,
    string Risk,
    string RiskText);
