namespace NewCosmos.Server.Contracts;

/// <summary>补打域描述。</summary>
public sealed record ReprintDomainDto(
    string Key,
    string Name,
    string Mode,
    string MonthWindow,
    string PrintPermission);

/// <summary>按身份证聚合的补打人员。</summary>
public sealed record ReprintPersonDto(
    string Key,
    string Name,
    string IdCard,
    string IdCardMasked,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> DomainNames,
    DateTime? LatestTime,
    int Count);

/// <summary>人员在某域下的命中情况。</summary>
public sealed record ReprintPersonDomainDto(
    string Domain,
    string Name,
    int Count,
    DateTime? LatestTime);

/// <summary>补打记录列表项。</summary>
public sealed record ReprintRecordDto(
    string Domain,
    long Id,
    string Name,
    string IdCard,
    string IdCardMasked,
    string No,
    string Status,
    DateTime BusinessTime,
    string Extra,
    string Classification,
    string ChainType);

/// <summary>补打记录详情（含字段与表格行）。</summary>
public sealed record ReprintRecordDetailDto(
    string Domain,
    long Id,
    string Name,
    string IdCard,
    string IdCardMasked,
    string No,
    string Status,
    string Classification,
    DateTime BusinessTime,
    string Extra,
    string Mode,
    Dictionary<string, string> Fields,
    List<Dictionary<string, string>> TableRows);

/// <summary>补打模板选项。</summary>
public sealed record ReprintTemplateDto(
    long Id,
    string Name,
    string DocType,
    string FileType,
    IReadOnlyList<string> Categories,
    bool IsApplicable,
    bool IsDefaultSelected)
{
    /// <summary>兼容旧契约（templateId），与 id 同值。</summary>
    public long TemplateId => Id;

    /// <summary>前端预选状态（与 defaultSelected 同值）。</summary>
    public bool IsSelected => IsDefaultSelected;
}

/// <summary>打印留痕项。</summary>
public sealed record ReprintHistoryDto(
    long Id,
    string TemplateName,
    string PrinterName,
    DateTime CreatedAt,
    string OperatorName,
    string Status,
    string Remark,
    string BatchNo,
    bool HasPdf,
    long PdfSize,
    string OriginalUrl);

/// <summary>POST /api/reprint/jobs 请求体。</summary>
public sealed record ReprintJobRequest(
    string? Domain,
    long? Id,
    string? Mode,
    string? Source,
    List<long>? TemplateIds,
    int? Copies,
    bool? IsDuplex,
    long? PrinterBindingId,
    string? PrinterName);

/// <summary>POST /api/reprint/batch 请求体。</summary>
public sealed record ReprintBatchRequest(
    string? Domain,
    int? Year,
    int? Month,
    List<long>? TemplateIds,
    string? Mode,
    int? Copies,
    bool? IsDuplex,
    long? PrinterBindingId,
    string? PrinterName,
    string? Keyword = null);

/// <summary>原样补打响应（历史 PDF 由 /original 端点下载/预览）。</summary>
public sealed record ReprintOriginalResponse(
    string Domain,
    long Id,
    string Mode,
    string Source,
    string OriginalUrl,
    IReadOnlyList<ReprintHistoryDto> History);

/// <summary>POST /api/agent/jobs/{id}/records 请求体。</summary>
public sealed record AgentPrintRecordsRequest(List<AgentPrintRecordInput>? Records);

/// <summary>Agent 回传的单份打印产物。</summary>
public sealed record AgentPrintRecordInput(
    string? TemplateName,
    string? FileName,
    string? FileType,
    string? PdfBase64,
    string? SourceBase64,
    string? SourceType,
    long? PdfSize,
    long? SourceSize);
