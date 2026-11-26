using System.Text.Json.Serialization;

namespace NewCosmos.Agent.Models;

/// <summary>
/// POST /api/agent/jobs/claim 请求体。
/// cachedTemplates 为 Agent 本地模板缓存清单（最近使用优先），服务端用它识别已缓存模板；
/// 老版本 Agent 不传时服务端按原逻辑下发完整模板字节。
/// </summary>
public sealed class AgentJobClaimRequest
{
    [JsonPropertyName("max")]
    public int Max { get; set; } = 1;

    /// <summary>当前已缓存模板（templateId + templateHash），最多 <see cref="MaxCachedTemplates"/> 条。</summary>
    [JsonPropertyName("cachedTemplates")]
    public List<AgentCachedTemplateRef>? CachedTemplates { get; set; }

    public const int MaxCachedTemplates = 64;
}

/// <summary>Agent 本地已缓存模板标识（claim 上报 / payload 请求用）。</summary>
public sealed class AgentCachedTemplateRef
{
    [JsonPropertyName("templateId")]
    public long TemplateId { get; set; }

    [JsonPropertyName("templateHash")]
    public string? TemplateHash { get; set; }
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
    /// <summary>模板字节 Base64；服务端判定 Agent 已缓存同 hash 模板时为空（<see cref="Cached"/>=true）。</summary>
    public string? TemplateFileBase64 { get; set; }

    /// <summary>
    /// 服务端是否按 cachedTemplateHash 命中裁剪：true 时不返回 templateFileBase64，
    /// Agent 直接用本地 <see cref="TemplateCache"/> 中的模板文件。
    /// </summary>
    public bool Cached { get; set; }

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
    /// 模板文件 SHA256（服务端 nc_biz_templates.file_hash；缺失时服务端按 file_data 现算）。
    /// Agent 用 {templateId}-{templateHash} 做本地缓存键：同 hash 复用，不重复写盘。
    /// </summary>
    public string? TemplateHash { get; set; }

    /// <summary>
    /// 图片占位符数据（当前只有 PHOTO_*；SIGNATURE_* 签名系统未设计，Agent 跳过）。
    /// Placeholder 与模板 config_json 的 placeholder 一致，Agent 以它为 key 调 ReplaceImageFields。
    /// </summary>
    public List<PrintImagePayload> Images { get; set; } = new();

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

    /// <summary>
    /// 批量模板任务（JobKind=TemplateBatch）：一次渲染多份模板/多页并合并预览、打印或保存。
    /// 非空时 Agent 走批量执行路径，忽略上面的单模板字段。
    /// </summary>
    public AgentBatchPayload? Batch { get; set; }
}

/// <summary>
/// 批量模板任务负载（payload.batch）。
/// <para>
/// templates 每个模板只出现一次（带模板字节/哈希）；parts 每个元素代表一页/一份渲染产物
/// （高龄月报每页 13 人；满 90 岁一人一份）。mode=open-folder 时 parts 可为空。
/// </para>
/// </summary>
public sealed class AgentBatchPayload
{
    /// <summary>preview / print / save / open-folder。</summary>
    public string Mode { get; set; } = "preview";

    /// <summary>输出分类目录名，默认「高龄月报」。</summary>
    public string Category { get; set; } = "高龄月报";

    /// <summary>期间（月报为 yyyyMM，如 202611），用于输出目录。</summary>
    public string Period { get; set; } = "";

    public List<AgentBatchTemplate> Templates { get; set; } = new();

    public List<AgentBatchPart> Parts { get; set; } = new();
}

/// <summary>批量任务中的模板（每个模板只出现一次，模板字节只下发一次）。</summary>
public sealed class AgentBatchTemplate
{
    public long TemplateId { get; set; }

    public string FileName { get; set; } = "";

    public string FileType { get; set; } = "xlsx";

    public string FileBase64 { get; set; } = "";

    public string? ConfigJson { get; set; }

    public string? TemplateHash { get; set; }
}

/// <summary>批量任务中的一页/一份渲染产物（fields 为 FieldKey → 值，由模板 configJson 映射占位符）。</summary>
public sealed class AgentBatchPart
{
    public long TemplateId { get; set; }

    /// <summary>保存到输出目录时的文件名（不含扩展名或带扩展名均可；为空时按任务号兜底）。</summary>
    public string? FileName { get; set; }

    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    public List<Dictionary<string, string>> TableRows { get; set; } = new();
}

/// <summary>POST /api/agent/jobs/{id}/fail 请求体。</summary>
public sealed class AgentJobFailRequest
{
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 单个图片占位符（payload.images 元素）。
/// Placeholder = 模板占位符原文（如 {申请人照片}）；FieldKey = PHOTO_* 业务键。
/// </summary>
public sealed class PrintImagePayload
{
    public string? Placeholder { get; set; }

    /// <summary>图片字节 Base64（WebP/JPEG/PNG；Word 引擎内部会转码）。</summary>
    public string? Base64 { get; set; }

    public string? FileName { get; set; }

    public string? FieldKey { get; set; }
}

/// <summary>POST /api/agent/jobs/{id}/complete 请求体（可选）：message 作为警告文案回写。</summary>
public sealed class AgentJobCompleteRequest
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// POST /api/agent/jobs/{id}/records 请求体：打印/保存成功后回传实际生成的文件，
/// 服务端写入 nc_biz_print_records，供历史「原样补打」。
/// </summary>
public sealed class AgentJobRecordsRequest
{
    [JsonPropertyName("records")]
    public List<AgentPrintRecord> Records { get; set; } = new();
}

/// <summary>
/// 一份实际生成的文书（源文件 + PDF）。批量任务每个 part 一条；
/// 字段名与服务端契约一致（camelCase）。
/// </summary>
public sealed class AgentPrintRecord
{
    /// <summary>模板名称（不含扩展名），用于历史记录分组展示。</summary>
    [JsonPropertyName("templateName")]
    public string? TemplateName { get; set; }

    /// <summary>最终落盘文件名（含扩展名；打印模式按模板/任务号构造）。</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>源文件类型：xlsx / docx。</summary>
    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = string.Empty;

    /// <summary>PDF 字节 Base64。</summary>
    [JsonPropertyName("pdfBase64")]
    public string PdfBase64 { get; set; } = string.Empty;

    /// <summary>渲染后源文件字节 Base64（docx/xlsx）。</summary>
    [JsonPropertyName("sourceBase64")]
    public string? SourceBase64 { get; set; }

    /// <summary>源文件类型（与 fileType 一致，服务端按源/PDF 分列存储）。</summary>
    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("pdfSize")]
    public long PdfSize { get; set; }

    [JsonPropertyName("sourceSize")]
    public long SourceSize { get; set; }
}
