using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Reporting;

/// <summary>
/// Office 侧最小打印契约（从 legacy IPrintService 裁剪）：
/// 只保留 <see cref="Printing.PrintExecuteService"/> 生成预览 PDF 需要的两个方法。
/// 月报、档案打印等编排仍留在服务端/后续 Agent 任务层。
/// </summary>
public interface IPrintService
{
    /// <summary>生成 PDF（基于模板引擎）。</summary>
    Task<Result<byte[]>> GeneratePdfAsync(
        long templateId,
        Dictionary<string, string> fields,
        CancellationToken ct = default);

    /// <summary>生成 PDF（基于模板引擎 + 表格数据）。</summary>
    Task<Result<byte[]>> GeneratePdfWithTableAsync(
        long templateId,
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> tableRows,
        CancellationToken ct = default,
        IReadOnlyCollection<string>? removeParagraphPlaceholders = null);
}
