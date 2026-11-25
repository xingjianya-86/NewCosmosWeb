using NewCosmos.Agent.Models;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 单个任务执行：模板取缓存（同 hash 复用）→ 渲染 PDF → 预览或打印 → 清理渲染产物。
/// 不访问数据库，也不把字段值/身份证/姓名写入日志。
/// <para>
/// 模板是公开文档，缓存到 %TEMP%\NewCosmos\TemplateCache 且任务完成后<b>不删除</b>；
/// 含个人信息的渲染源文件/PDF 仍在任务结束后立即清理。
/// </para>
/// </summary>
public sealed partial class JobTaskExecutor : IJobTaskExecutor
{
    private readonly ITemplateJobRenderer _renderer;
    private readonly IJobPrinter _printer;
    private readonly JobFileStore _files;
    private readonly TemplateCache _templates;
    private readonly ILoggerService _logger;
    private readonly int _previewKeepCount;
    private readonly string _outputRoot;

    public JobTaskExecutor(
        ITemplateJobRenderer renderer,
        IJobPrinter printer,
        JobFileStore files,
        TemplateCache templates,
        ILoggerService logger,
        int previewKeepCount,
        string? outputRoot = null)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _printer = printer ?? throw new ArgumentNullException(nameof(printer));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _previewKeepCount = previewKeepCount < 1 ? 1 : previewKeepCount;
        _outputRoot = OutputFolderService.ResolveRoot(outputRoot);
    }

    public async Task<string?> ExecuteAsync(AgentJob job, AgentJobPayload payload, CancellationToken ct)
    {
        if (payload == null)
            throw new BusinessException("JOB_PAYLOAD_INVALID", "任务负载为空");

        // 批量模板任务优先：一份负载内含多个模板/多页，走独立执行路径。
        if (payload.Batch is not null)
            return await ExecuteBatchAsync(job, payload, payload.Batch, ct).ConfigureAwait(false);

        var jobNo = FirstNonEmpty(job.JobNo, $"job-{job.Id}")!;
        var fileType = _renderer.ResolveFileType(payload.TemplateFileType, payload.TemplateFileName);
        var isPreview = IsPreview(job.JobKind) || IsPreview(payload.JobKind);

        var templatePath = _templates.TryGetPath(job.TemplateId, payload.TemplateHash, fileType);
        if (templatePath is null)
        {
            if (payload.Cached)
            {
                // 服务端按上报 hash 裁掉了模板字节，但本地缓存已被清理：本次按失败处理；
                // 用户重试时缓存清单不再包含该文件，服务端会重新下发完整模板。
                throw new BusinessException(
                    "JOB_TEMPLATE_CACHE_MISS",
                    "服务端判定模板已缓存，但本地缓存文件缺失，请重试任务");
            }

            if (string.IsNullOrWhiteSpace(payload.TemplateFileBase64))
            {
                throw new BusinessException(
                    "JOB_PAYLOAD_INVALID",
                    $"任务缺少模板文件（JobKind={FirstNonEmpty(job.JobKind, payload.JobKind) ?? "-"}）；Agent 目前仅支持模板任务");
            }

            var templateBytes = DecodeBase64(payload.TemplateFileBase64);
            var ensured = await _templates
                .EnsureAsync(job.TemplateId, payload.TemplateHash, fileType, templateBytes, ct)
                .ConfigureAwait(false);
            templatePath = ensured.Path;
        }

        _files.EnsureRoot();
        var sourcePath = _files.GetRenderedSourcePath(jobNo, fileType);
        var pdfPath = _files.GetPdfPath(jobNo);

        var fields = payload.Fields ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var tableRows = payload.TableRows ?? new List<Dictionary<string, string>>();
        var images = FilterImages(payload.Images);

        // 名册类模板用 FAMILY_MEMBER_NAME_1..N 索引槽承载一页；超过容量必须拆页，
        // 否则第 N+1 行起会被静默丢弃（旧版 PrintExecuteService 同样按槽位分页）。
        var slotCapacity = _renderer.GetIndexSlotCapacity(payload.TemplateConfigJson);
        var chunks = slotCapacity > 0 && tableRows.Count > slotCapacity
            ? ChunkRows(tableRows, slotCapacity)
            : null;

        var printerName = FirstNonEmpty(payload.PrinterName, job.PrinterName);
        var copies = payload.Copies > 0 ? payload.Copies : Math.Max(1, job.Copies);
        var isDuplex = payload.IsDuplex || job.IsDuplex;

        try
        {
            _logger.Info(
                $"[JobTask] 模板就绪: No={jobNo}, Type={fileType}, Template={job.TemplateId?.ToString() ?? "-"}, " +
                $"Fields={fields.Count}, Rows={tableRows.Count}, Images={images.Count}, " +
                $"Pages={(chunks?.Count ?? 1)}, JobKind={(isPreview ? "Preview" : "Print")}");

            if (isPreview)
            {
                if (chunks is null)
                {
                    await _renderer
                        .RenderPdfAsync(
                            templatePath, fileType, fields, tableRows, images,
                            payload.TemplateConfigJson, payload.TableStartMarker, payload.TableEndMarker, pdfPath, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await RenderMergedPreviewAsync(
                        templatePath, fileType, fields, chunks, images, payload, pdfPath, ct)
                        .ConfigureAwait(false);
                }

                _logger.Info($"[JobTask] 预览 PDF 已生成: No={jobNo}, Bytes={new FileInfo(pdfPath).Length}");
                _files.OpenWithShell(pdfPath);
                _logger.Info($"[JobTask] 已调用系统默认阅读器打开预览: No={jobNo}");

                // 预览不立即删除（用户可能还开着），只保留最近 N 个。
                _files.TrimPreviewFiles(_previewKeepCount);
                return null;
            }

            if (chunks is not null)
            {
                var pageWarning = await PrintChunksAsync(
                    templatePath, fileType, fields, chunks, images, payload,
                    jobNo, printerName, copies, isDuplex, ct)
                    .ConfigureAwait(false);

                _logger.Info(
                    $"[JobTask] 打印完成并已清理渲染产物: No={jobNo}, Pages={chunks.Count}, " +
                    $"Copies={copies}, Duplex={isDuplex}");
                return pageWarning;
            }

            await _renderer
                .RenderSourceAsync(
                    templatePath, fileType, fields, tableRows, images,
                    payload.TemplateConfigJson, payload.TableStartMarker, payload.TableEndMarker, sourcePath, ct)
                .ConfigureAwait(false);

            // 打印任务同样先生成 PDF（留档/降级打印用），再走打印。
            await _renderer
                .ExportPdfFromSourceAsync(sourcePath, fileType, pdfPath, ct)
                .ConfigureAwait(false);

            var warning = await _printer
                .PrintAsync(pdfPath, sourcePath, fileType, printerName, copies, isDuplex, ct)
                .ConfigureAwait(false);

            // 打印成功：只清理渲染源/PDF（含个人敏感信息）；模板缓存保留复用。
            _files.TryDelete(sourcePath, pdfPath);
            _logger.Info($"[JobTask] 打印完成并已清理渲染产物: No={jobNo}, Copies={copies}, Duplex={isDuplex}");
            return warning;
        }
        catch
        {
            // 失败同样清理本地敏感文件，避免 %TEMP% 长期堆积文书。
            _files.TryDelete(sourcePath, pdfPath);
            throw;
        }
    }

    /// <summary>名册拆页预览：每页渲染 PDF，合并成一个 PDF 再打开。</summary>
    private async Task RenderMergedPreviewAsync(
        string templatePath,
        string fileType,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<List<Dictionary<string, string>>> chunks,
        IReadOnlyList<PrintImagePayload> images,
        AgentJobPayload payload,
        string pdfPath,
        CancellationToken ct)
    {
        var partPaths = new List<string>(chunks.Count);
        try
        {
            var partPdfs = new List<byte[]>(chunks.Count);
            for (var index = 0; index < chunks.Count; index++)
            {
                var partPath = $"{pdfPath}.part{index + 1}.pdf";
                partPaths.Add(partPath);

                await _renderer
                    .RenderPdfAsync(
                        templatePath, fileType, fields, chunks[index], images,
                        payload.TemplateConfigJson, payload.TableStartMarker, payload.TableEndMarker,
                        partPath, ct)
                    .ConfigureAwait(false);

                partPdfs.Add(await File.ReadAllBytesAsync(partPath, ct).ConfigureAwait(false));
            }

            var merged = PdfMergeHelper.Merge(partPdfs);
            await File.WriteAllBytesAsync(pdfPath, merged, ct).ConfigureAwait(false);
            _files.TryDelete(partPaths.ToArray());
        }
        catch
        {
            _files.TryDelete(partPaths.ToArray());
            throw;
        }
    }

    /// <summary>
    /// 名册拆页打印：逐页渲染源 xlsx 并交给 Office COM 打印，
    /// 保证每页的打印机/份数/双面参数生效（旧版同样按页拆分后打印）。
    /// </summary>
    private async Task<string?> PrintChunksAsync(
        string templatePath,
        string fileType,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<List<Dictionary<string, string>>> chunks,
        IReadOnlyList<PrintImagePayload> images,
        AgentJobPayload payload,
        string jobNo,
        string? printerName,
        int copies,
        bool isDuplex,
        CancellationToken ct)
    {
        var partPaths = new List<string>(chunks.Count * 2);
        string? combinedWarning = null;

        try
        {
            for (var index = 0; index < chunks.Count; index++)
            {
                var partNo = $"{jobNo}_p{index + 1}";
                var partSource = _files.GetRenderedSourcePath(partNo, fileType);
                var partPdf = _files.GetPdfPath(partNo);
                partPaths.Add(partSource);
                partPaths.Add(partPdf);

                await _renderer
                    .RenderSourceAsync(
                        templatePath, fileType, fields, chunks[index], images,
                        payload.TemplateConfigJson, payload.TableStartMarker, payload.TableEndMarker,
                        partSource, ct)
                    .ConfigureAwait(false);

                await _renderer
                    .ExportPdfFromSourceAsync(partSource, fileType, partPdf, ct)
                    .ConfigureAwait(false);

                var warning = await _printer
                    .PrintAsync(partPdf, partSource, fileType, printerName, copies, isDuplex, ct)
                    .ConfigureAwait(false);

                combinedWarning = CombineWarnings(combinedWarning, warning);
                _files.TryDelete(partSource, partPdf);
            }

            return combinedWarning;
        }
        catch
        {
            _files.TryDelete(partPaths.ToArray());
            throw;
        }
    }

    private static List<List<Dictionary<string, string>>> ChunkRows(
        IReadOnlyList<Dictionary<string, string>> rows,
        int capacity)
    {
        var chunks = new List<List<Dictionary<string, string>>>();
        for (var index = 0; index < rows.Count; index += capacity)
        {
            chunks.Add(rows.Skip(index).Take(capacity).ToList());
        }

        return chunks;
    }

    private static string? CombineWarnings(string? existing, string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return next;
        }

        return existing.Contains(next, StringComparison.Ordinal)
            ? existing
            : $"{existing}; {next}";
    }

    /// <summary>只保留可用的照片图片（SIGNATURE_* 签名系统未设计，直接跳过）。</summary>
    private static List<PrintImagePayload> FilterImages(List<PrintImagePayload>? images)
    {
        if (images is not { Count: > 0 }) return [];

        return images
            .Where(image => image is { Placeholder.Length: > 0, Base64.Length: > 0 })
            .Where(image => !(image.FieldKey?.StartsWith("SIGNATURE_", StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
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
