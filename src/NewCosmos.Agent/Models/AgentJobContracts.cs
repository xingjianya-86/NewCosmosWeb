using System.Text.Json.Serialization;

namespace NewCosmos.Agent.Models;

/// <summary>
/// POST /api/agent/jobs/claim 请求体。
/// </summary>
public sealed class AgentJobClaimRequest
{
    [JsonPropertyName("max")]
    public int Max { get; set; } = 1;
}

/// <summary>
/// 服务端下发的打印/预览任务（POST /api/agent/jobs/claim 数组元素）。
/// 字段名与服务端契约保持一致，反序列化时使用大小写不敏感策略。
/// </summary>
public sealed class AgentJob
{
    public long Id { get; set; }

    public string JobNo { get; set; } = string.Empty;

    /// <summary>Preview / Template / Photo 等；Agent 仅区分 Preview 与其他（打印）。</summary>
    public string JobKind { get; set; } = "Template";

    public long? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public string? PrinterName { get; set; }

    public int Copies { get; set; } = 1;

    public bool IsDuplex { get; set; }

    public string? BusinessType { get; set; }

    public long? BusinessId { get; set; }
}

/// <summary>
/// GET /api/agent/jobs/{id}/payload 响应体。
/// 模板字节由服务端下发，Agent 不访问数据库。
/// </summary>
public sealed class AgentJobPayload
{
    public string TemplateFileBase64 { get; set; } = string.Empty;

    public string? TemplateFileName { get; set; }

    /// <summary>docx / word / xlsx / excel，或对应的 MIME 类型。</summary>
    public string? TemplateFileType { get; set; }

    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    public List<Dictionary<string, string>> TableRows { get; set; } = new();

    public string JobKind { get; set; } = "Template";

    public string? PrinterName { get; set; }

    public int Copies { get; set; } = 1;

    public bool IsDuplex { get; set; }

    public string? ApplicantName { get; set; }

    public string? ApplicantIdCard { get; set; }

    /// <summary>
    /// 模板 config_json（服务端附加字段）：包含字段/表格的 FieldKey → 占位符映射。
    /// Agent 必须按它映射，否则 fields/tableRows 是 FieldKeys，无法直接替换模板占位符。
    /// </summary>
    public string? TemplateConfigJson { get; set; }

    /// <summary>
    /// 可选：表格定位标记。当前服务端契约未下发，Agent 会按首行首个占位符推断；
    /// 若后续契约补充这两个字段则直接使用，避免误判。
    /// </summary>
    public string? TableStartMarker { get; set; }

    public string? TableEndMarker { get; set; }
}

/// <summary>POST /api/agent/jobs/{id}/fail 请求体。</summary>
public sealed class AgentJobFailRequest
{
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;
}
