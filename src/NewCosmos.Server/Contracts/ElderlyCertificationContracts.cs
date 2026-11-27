namespace NewCosmos.Server.Contracts;

/// <summary>
/// 高龄年度认证 Web API 合同（docs/API_CONTRACT_ELDERLY_CERTIFICATION.md）的请求/响应 DTO。
/// 状态 key、字段名以合同为准；中文显示名一律由服务端映射，前端不自行拼装。
/// </summary>
public static class ElderlyCertificationApiCodes
{
    /// <summary>预览临时目录不存在 / 超过 24h / 服务重启导致会话丢失。</summary>
    public const string PreviewExpired = "PREVIEW_EXPIRED";

    /// <summary>预览后 Excel/ZIP 哈希变化，必须重新预览。</summary>
    public const string ImportFileChanged = "IMPORT_FILE_CHANGED";

    /// <summary>名单状态已变化，需要重新预览/人工确认。</summary>
    public const string Conflict = "CONFLICT";
}

// ───────────────────────────── 2.1 批次 / 统计 ─────────────────────────────

public sealed record ElderlyCertCampaignDto(
    long Id,
    string CampaignNo,
    int Year,
    int Round,
    string Name,
    string DisplayName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? ValidUntil,
    string Status,
    string StatusText,
    bool RequireEvidence,
    int DeadlineStopOffset,
    DateTime? RosterGeneratedAt,
    DateTime? ClosedAt,
    string Remark);

/// <summary>POST /campaigns 请求体。同年度同轮次的唯一性由 Core 校验。</summary>
public sealed class ElderlyCertCampaignCreateRequest
{
    public int Year { get; set; }

    public int Round { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public bool RequireEvidence { get; set; } = true;
}

/// <summary>POST /campaigns 响应：仅返回主键。</summary>
public sealed record ElderlyCertCampaignCreatedDto(long Id);

/// <summary>POST /campaigns/{id}/roster 响应：本次新增任务数。</summary>
public sealed record ElderlyCertRosterResultDto(int Added);

public sealed record ElderlyCertStatsDto(
    int Total,
    int Pending,
    int Verified,
    int Refused,
    int Failed,
    int Deceased,
    int MovedOut,
    int NotLocated,
    int Exempt,
    int Stopped,
    int Invalid,
    int EvidenceIssue,
    int StopCandidate,
    decimal CompletionRate);

// ───────────────────────────── 2.2 名单 / 任务 / 凭证 ─────────────────────────────

public sealed record ElderlyCertTaskListItemDto(
    long Id,
    long CampaignId,
    string IdCard,
    string Name,
    string Gender,
    DateOnly? BirthDate,
    int? AgeSnapshot,
    string Category,
    string CategoryText,
    decimal? IssueAmount,
    long? ApplicationId,
    string ApplicationNo,
    string SourceKind,
    string HukouVillage,
    string HukouAddress,
    string FamilyAddress,
    string Phone,
    string CertStatus,
    string CertStatusText,
    string VerifyMethod,
    string VerifyMethodText,
    DateOnly? VerifiedAt,
    string VerifiedBy,
    string ResultNote,
    DateOnly? DeathDate,
    int ExpectedEvidenceCount,
    int EvidenceCount,
    string EvidenceStatus,
    string EvidenceStatusText,
    string StopDecision,
    string StopDecisionText,
    DateOnly? FollowUpDate);

public sealed record ElderlyCertTaskListResponse(
    IReadOnlyList<ElderlyCertTaskListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>
/// 单条任务详情：在列表字段基础上补充完整户籍/家庭区划 ID、代办人、银行、来源与停发快照。
/// 完整身份证只在该详情接口返回；列表同合同示例。
/// </summary>
public sealed record ElderlyCertTaskDetailDto(
    long Id,
    long CampaignId,
    string IdCard,
    string Name,
    string Gender,
    DateOnly? BirthDate,
    int? AgeSnapshot,
    string Category,
    string CategoryText,
    string IdentityFlag,
    decimal? IssueAmount,
    long? ApplicationId,
    string ApplicationNo,
    string SourceKind,
    int? HukouCityId,
    int? HukouCountyId,
    int? HukouTownId,
    int? HukouVillageId,
    string HukouVillage,
    string HukouAddress,
    int? FamilyCityId,
    int? FamilyCountyId,
    int? FamilyTownId,
    int? FamilyVillageId,
    string FamilyAddress,
    string Phone,
    string BankName,
    string BankAccount,
    string AgentName,
    string AgentRelation,
    DateTime? SnapshotAt,
    string CertStatus,
    string CertStatusText,
    string VerifyMethod,
    string VerifyMethodText,
    DateOnly? VerifiedAt,
    string VerifiedBy,
    string VerifierOrg,
    string VerifierPhone,
    string ResultNote,
    DateOnly? DeathDate,
    int ExpectedEvidenceCount,
    int EvidenceCount,
    string EvidenceStatus,
    string EvidenceStatusText,
    string StopDecision,
    string StopDecisionText,
    string StopReason,
    string StopReasonText,
    string StopNote,
    DateOnly? FollowUpDate,
    DateOnly? DueStopDate,
    DateOnly? ActualStopDate,
    long? StopApplicationId,
    DateTime? StoppedAt,
    string SourceBatchNo,
    string SourceFile,
    int? SourceRow,
    DateTime? SourceSubmittedAt,
    DateTime? LastImportedAt,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed class ElderlyCertResultUpdateRequest
{
    public string CertStatus { get; set; } = string.Empty;

    public string? VerifyMethod { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public string? VerifiedBy { get; set; }

    public string? ResultNote { get; set; }

    public DateTime? DeathDate { get; set; }
}

public sealed record ElderlyCertEvidenceDto(
    long Id,
    long? TaskId,
    int Seq,
    string EvidenceType,
    string EvidenceTypeText,
    string OriginalName,
    string DisplayName,
    string ImageFormat,
    long FileSize,
    string Sha256,
    string MatchStatus,
    string MatchMethod);

/// <summary>凭证图片内容：仅服务层内部使用，端点直接写二进制，不序列化为 JSON。</summary>
public sealed record ElderlyCertEvidenceContentDto(
    byte[] Data,
    string ContentType,
    string FileName);

// ───────────────────────────── 2.3 批量导入 ─────────────────────────────

public sealed class ElderlyCertImportApplyRequest
{
    public long PreviewId { get; set; }

    public bool AllowAdverseOverwrite { get; set; }

    /// <summary>为空表示应用全部可应用行；否则只应用指定 Excel 行号。</summary>
    public IReadOnlyList<int>? RowNumbers { get; set; }
}

public sealed record ElderlyCertImportRowDto(
    int RowNo,
    string IdCard,
    string Name,
    string Village,
    string CertStatus,
    string CertStatusText,
    string VerifyMethod,
    DateOnly? VerifiedAt,
    int ExpectedEvidenceCount,
    int MatchedEvidenceCount,
    string EvidenceStatus,
    string EvidenceStatusText,
    string MatchStatus,
    string MatchStatusText,
    string ErrorMessage,
    long? TaskId);

public sealed record ElderlyCertUnmatchedEntryDto(
    string EntryName,
    string FileName,
    long Length,
    string Extension);

public sealed record ElderlyCertImportPreviewResponse(
    long PreviewId,
    long CampaignId,
    string BatchNo,
    int TotalRows,
    int MatchedRows,
    int UnknownRows,
    int DuplicateRows,
    int NameMismatchRows,
    int InvalidRows,
    int ConflictRows,
    int EvidenceTotal,
    int EvidenceMatched,
    int EvidenceMissing,
    int EvidenceUnmatched,
    string Summary,
    IReadOnlyList<ElderlyCertImportRowDto> Rows,
    IReadOnlyList<ElderlyCertUnmatchedEntryDto> UnmatchedEntries,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record ElderlyCertImportApplyResponse(
    long BatchId,
    string BatchNo,
    int AppliedRows,
    int SkippedRows,
    int ErrorRows,
    int EvidenceInserted,
    int EvidenceSkipped,
    int EvidenceUnmatched,
    int InfoChangesCreated,
    int InfoChangesSkipped,
    int InfoChangeErrors,
    string Summary);

// ───────────────────────────── 2.4 停发预览 / 停发 ─────────────────────────────

public sealed record ElderlyCertStopPreviewItemDto(
    long Id,
    long CampaignId,
    string IdCard,
    string Name,
    string Gender,
    DateOnly? BirthDate,
    int? AgeSnapshot,
    string Category,
    string CategoryText,
    decimal? IssueAmount,
    string SourceKind,
    string HukouVillage,
    string HukouAddress,
    string FamilyAddress,
    string Phone,
    string CertStatus,
    string CertStatusText,
    string VerifyMethod,
    string VerifyMethodText,
    DateOnly? VerifiedAt,
    string VerifiedBy,
    string ResultNote,
    DateOnly? DeathDate,
    int ExpectedEvidenceCount,
    int EvidenceCount,
    string EvidenceStatus,
    string EvidenceStatusText,
    string SuggestedStopReason,
    string SuggestedStopReasonText,
    string SuggestedStopNote,
    DateOnly? DueStopDate,
    string StopDecision,
    string StopDecisionText,
    DateOnly? FollowUpDate,
    long? ApplicationId,
    string ApplicationNo);

public sealed record ElderlyCertStopPreviewResponse(
    IReadOnlyList<ElderlyCertStopPreviewItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>报表文件（Excel 字节 + 下载文件名），由端点直接返回，不进 JSON。</summary>
public sealed record ElderlyCertificationReportFileDto(byte[] Content, string FileName);

/// <summary>逐人停发请求：字段与现有高龄停发一致。</summary>
public sealed class ElderlyCertStopRequest
{
    public string? StopReason { get; set; }

    public DateTime? DueStopDate { get; set; }

    public DateTime? DeathDate { get; set; }

    public bool IsRecover { get; set; }

    public string? RecoverStartMonth { get; set; }

    public string? RecoverEndMonth { get; set; }

    public decimal? RecoverAmount { get; set; }

    public string? Remark { get; set; }

    /// <summary>停发原因选 OTHER 时的说明（兼容现有高龄停发表单）。</summary>
    public string? OtherReasonText { get; set; }
}

public sealed class ElderlyCertStopDecisionRequest
{
    /// <summary>Deferred=暂缓 / Excluded=排除 / Pending=重新进入待停发。</summary>
    public string Decision { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime? FollowUpDate { get; set; }
}
