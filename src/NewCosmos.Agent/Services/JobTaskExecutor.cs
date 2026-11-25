using NewCosmos.Agent.Models;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 单个任务执行：模板落盘 → 渲染 PDF → 预览或打印 → 清理临时文件。
/// 不访问数据库，也不把字段值/身份证/姓名写入日志。
/// </summary>
public sealed class JobTaskExecutor : IJobTaskExecutor
{
    private readonly ITemplateJobRenderer _renderer;
    private readonly IJobPrinter _printer;
    private readonly JobFileStore _files;
    private readonly ILoggerService _logger;
    private readonly int _previewKeepCount;

    public JobTaskExecutor(
        ITemplateJobRenderer renderer,
        IJobPrinter printer,
        JobFileStore files,
        ILoggerService logger,
        int previewKeepCount)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _printer = printer ?? throw new ArgumentNullException(nameof(printer));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _previewKeepCount = previewKeepCount < 1 ? 1 : previewKeepCount;
    }

    public async Task ExecuteAsync(AgentJob job, AgentJobPayload payload, CancellationToken ct)
    {
        if (payload == null)
            throw new BusinessException("JOB_PAYLOAD_INVALID", "任务负载为空");

        if (string.IsNullOrWhiteSpace(payload.TemplateFileBase64))
        {
            throw new BusinessException(
                "JOB_PAYLOAD_INVALID",
                $"任务缺少模板文件（JobKind={FirstNonEmpty(job.JobKind, payload.JobKind) ?? "-"}）；Agent 目前仅支持模板任务");
        }

        var jobNo = FirstNonEmpty(job.JobNo, $"job-{job.Id}")!;
        var fileType = _renderer.ResolveFileType(payload.TemplateFileType, payload.TemplateFileName);
        var templateBytes = DecodeBase64(payload.TemplateFileBase64);
        var isPreview = IsPreview(job.JobKind) || IsPreview(payload.JobKind);

        _files.EnsureRoot();
        var templatePath = _files.GetTemplatePath(jobNo, fileType);
        var sourcePath = _files.GetRenderedSourcePath(jobNo, fileType);
        var pdfPath = _files.GetPdfPath(jobNo);

        var fields = payload.Fields ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var tableRows = payload.TableRows ?? new List<Dictionary<string, string>>();

        try
        {
            await File.WriteAllBytesAsync(templatePath, templateBytes, ct).ConfigureAwait(false);
            _logger.Info(
                $"[JobTask] 模板已落盘: No={jobNo}, Type={fileType}, Bytes={templateBytes.Length}, " +
                $"Fields={fields.Count}, Rows={tableRows.Count}, JobKind={(isPreview ? "Preview" : "Print")}");

            if (isPreview)
            {
                await _renderer
                    .RenderPdfAsync(
                        templatePath, fileType, fields, tableRows,
                        payload.TemplateConfigJson, payload.TableStartMarker, payload.TableEndMarker, pdfPath, ct)
                    .ConfigureAwait(false);

                _logger.Info($"[JobTask] 预览 PDF 已生成: No={jobNo}, Bytes={new FileInfo(pdfPath).Length}");
                _files.OpenWithShell(pdfPath);
                _logger.Info($"[JobTask] 已调用系统默认阅读器打开预览: No={jobNo}");

                // 预览不立即删除（用户可能还开着），只保留最近 N 个。
                _files.TrimPreviewFiles(_previewKeepCount);
                _files.TryDelete(templatePath);
                return;
            }

            await _renderer
                .RenderSourceAsync(
                    templatePath, fileType, fields, tableRows,
                    payload.TemplateConfigJson, payload.TableStartMarker, payload.TableEndMarker, sourcePath, ct)
                .ConfigureAwait(false);

            // 打印任务同样先生成 PDF（留档/降级打印用），再走打印。
            await _renderer
                .ExportPdfFromSourceAsync(sourcePath, fileType, pdfPath, ct)
                .ConfigureAwait(false);

            var printerName = FirstNonEmpty(payload.PrinterName, job.PrinterName);
            var copies = payload.Copies > 0 ? payload.Copies : Math.Max(1, job.Copies);
            var isDuplex = payload.IsDuplex || job.IsDuplex;

            await _printer
                .PrintAsync(pdfPath, sourcePath, fileType, printerName, copies, isDuplex, ct)
                .ConfigureAwait(false);

            // 打印成功：模板/渲染源/PDF 全部清理（含个人敏感信息）。
            _files.TryDelete(templatePath, sourcePath, pdfPath);
            _logger.Info($"[JobTask] 打印完成并已清理临时文件: No={jobNo}, Copies={copies}, Duplex={isDuplex}");
        }
        catch
        {
            // 失败同样清理本地敏感文件，避免 %TEMP% 长期堆积文书。
            _files.TryDelete(templatePath, sourcePath, pdfPath);
            throw;
        }
    }

    private static bool IsPreview(string? jobKind)
        => string.Equals(jobKind?.Trim(), "Preview", StringComparison.OrdinalIgnoreCase);

    private static byte[] DecodeBase64(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length == 0)
                throw new BusinessException("JOB_PAYLOAD_INVALID", "模板文件为空");
            return bytes;
        }
        catch (FormatException ex)
        {
            throw new BusinessException("JOB_PAYLOAD_INVALID", "模板 Base64 数据损坏，无法解码", ex);
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
