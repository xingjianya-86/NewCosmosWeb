namespace NewCosmos.Server.Contracts;

#region 数据导入

/// <summary>
/// 导入类型能力描述（对应 PC <c>DataImportViewModel.LoadImportTypes</c> 的可见信息）。
/// </summary>
public sealed record ImportTypeDto(
    string Code,
    string Name,
    bool Combined,
    bool SupportsScope,
    bool HasTemplate,
    string FamilyPattern,
    string PersonPattern);

/// <summary>列映射：源列 → 目标字段（未识别时 <see cref="TargetField"/> 为空）。</summary>
public sealed record ImportColumnMappingDto(
    string SourceColumn,
    string TargetField,
    bool IsMapped);

/// <summary>本次预览涉及的文件（role：家庭 / 人员 / 单文件）。</summary>
public sealed record ImportPreviewFileDto(
    string FileName,
    string Role,
    string SizeText);

/// <summary>组合导入自动匹配到的家庭 / 人员文件。</summary>
public sealed record ImportMatchedFilesDto(
    string? FamilyFileName,
    string? PersonFileName);

/// <summary>
/// 数据导入预览结果。组合类型返回家庭 / 人员两套列与行；单文件类型只填 <see cref="PersonColumns"/> /
/// <see cref="PersonRows"/>。行数据为「表头 → 单元格文本」，保留 Excel 原始列名。
/// </summary>
public sealed record DataImportPreviewResponse(
    bool Success,
    string? ErrorMessage,
    int FamilyTotalRows,
    int PersonTotalRows,
    IReadOnlyList<ImportColumnMappingDto> FamilyColumns,
    IReadOnlyList<ImportColumnMappingDto> PersonColumns,
    IReadOnlyList<Dictionary<string, object?>> FamilyRows,
    IReadOnlyList<Dictionary<string, object?>> PersonRows,
    IReadOnlyList<ImportPreviewFileDto> Files,
    ImportMatchedFilesDto? Matched);

/// <summary>启动导入任务后的 202 响应。</summary>
public sealed record ImportJobStartedDto(string JobId);

/// <summary>未关联人员（前端结果表格）。</summary>
public sealed record ImportUnlinkedPersonDto(
    string Name,
    string IdCard,
    string HeadIdCard,
    int RowNumber);

/// <summary>
/// 导入任务结果汇总（供 <c>GET /jobs/{jobId}</c> 读取，前端「导入结果」卡片使用）。
/// 任务执行期间 <see cref="Success"/> 为 null。
/// </summary>
public sealed record ImportJobSummaryDto(
    string JobId,
    string ImportType,
    string ImportTypeName,
    bool? Success,
    bool Canceled,
    string? Error,
    int FamilyCount,
    int PersonCount,
    int LinkedCount,
    int UnlinkedCount,
    int ErrorCount,
    int WarningCount,
    int DurationSeconds,
    IReadOnlyList<ImportUnlinkedPersonDto> UnlinkedPersons);

#endregion
