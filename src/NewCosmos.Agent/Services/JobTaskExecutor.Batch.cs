using NewCosmos.Agent.Models;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 批量模板任务执行（JobKind=TemplateBatch）：
/// 模板一次准备（同 hash 复用缓存）→ 逐 part 渲染 → 按 mode 预览合并 / 打印 / 保存 / 打开输出目录。
/// 日志只记录模式、页数、模板数、耗时等统计，不写字段值/姓名/身份证等个人信息。
/// </summary>
public sealed partial class JobTaskExecutor
{
    private const string BatchModePreview = "preview";
    private const string BatchModePrint = "print";
    private const string BatchModeSave = "save";
    private const string BatchModeOpenFolder = "open-folder";

    /// <summary>批量任务中的一个模板来源（本地缓存路径 + 渲染参数）。</summary>
    private readonly record struct BatchTemplateSource(
        string Path,
        string FileType,
        string? ConfigJson,
        string? FileName);

    private async Task<string?> ExecuteBatchAsync(
        AgentJob job,
        AgentJobPayload payload,
        AgentBatchPayload batch,
        CancellationToken ct)
    {
        var jobNo = FirstNonEmpty(job.JobNo, $"job-{job.Id}")!;
        var mode = NormalizeBatchMode(batch.Mode);
        var parts = (batch.Parts ?? new List<AgentBatchPart>())
            .Where(part => part is not null)
            .ToList();
        var templates = batch.Templates ?? new List<AgentBatchTemplate>();

        _logger.Info(
            $"[JobTask] 批量任务: No={jobNo}, Mode={mode}, Parts={parts.Count}, Templates={templates.Count}, " +
            $"Category={OutputFolderService.SanitizeSegment(batch.Category, OutputFolderService.DefaultCategory)}, " +
            $"Period={(string.IsNullOrWhiteSpace(batch.Period) ? "-" : batch.Period.Trim())}");

        if (mode == BatchModeOpenFolder)
        {
            var folder = OutputFolderService.EnsureBatchFolder(_outputRoot, batch.Category, batch.Period);
            _files.OpenWithShell(folder);
            _logger.Info($"[JobTask] 批量任务已打开输出目录: No={jobNo}");
            return null;
        }

        if (parts.Count == 0)
            throw new BusinessException("JOB_PAYLOAD_INVALID", "批量任务没有可渲染的页（parts 为空）");

        var sources = await PrepareBatchTemplatesAsync(templates, ct).ConfigureAwait(false);

        var printerName = FirstNonEmpty(payload.PrinterName, job.PrinterName);
        var copies = payload.Copies > 0 ? payload.Copies : Math.Max(1, job.Copies);
        var isDuplex = payload.IsDuplex || job.IsDuplex;

        return mode switch
        {
            BatchModePreview => await PreviewBatchAsync(jobNo, parts, sources, ct).ConfigureAwait(false),
            BatchModePrint => await PrintBatchAsync(
                jobNo, parts, sources, printerName, copies, isDuplex, ct).ConfigureAwait(false),
            BatchModeSave => await SaveBatchAsync(jobNo, batch, parts, sources, ct).ConfigureAwait(false),
            _ => throw new BusinessException("JOB_PAYLOAD_INVALID", $"不支持的批量任务模式: {batch.Mode}")
        };
    }

    /// <summary>
    /// 准备全部模板：每个模板只按 templateId 落一次缓存（同 hash 命中不重复写盘）。
    /// 服务端未下发字节且本地无缓存时 <see cref="TemplateCache.EnsureAsync"/> 会抛业务异常。
    /// </summary>
    private async Task<Dictionary<long, BatchTemplateSource>> PrepareBatchTemplatesAsync(
        IReadOnlyList<AgentBatchTemplate> templates,
        CancellationToken ct)
    {
        var result = new Dictionary<long, BatchTemplateSource>();
        foreach (var template in templates)
        {
            if (template is null || template.TemplateId <= 0)
                continue;

            var fileType = _renderer.ResolveFileType(template.FileType, template.FileName);
            var bytes = string.IsNullOrWhiteSpace(template.FileBase64)
                ? null
                : DecodeBase64(template.FileBase64);

            // 哈希缺失时 EnsureAsync 按模板字节现算 SHA256（与单模板任务一致）。
            var ensured = await _templates
                .EnsureAsync(template.TemplateId, template.TemplateHash, fileType, bytes, ct)
                .ConfigureAwait(false);

            if (result.ContainsKey(template.TemplateId))
            {
                _logger.Warn($"[JobTask] 批量任务模板重复下发，使用最后一份: Template={template.TemplateId}");
            }

            result[template.TemplateId] = new BatchTemplateSource(
                ensured.Path, fileType, template.ConfigJson, template.FileName);
        }

        return result;
    }

    /// <summary>预览：逐 part 渲染 PDF → 真合并为一个 PDF → 系统阅读器打开（保留最近 N 个）。</summary>
    private async Task<string?> PreviewBatchAsync(
        string jobNo,
        IReadOnlyList<AgentBatchPart> parts,
        IReadOnlyDictionary<long, BatchTemplateSource> sources,
        CancellationToken ct)
    {
        _files.EnsureRoot();

        var mergedPath = _files.GetPdfPath(jobNo);
        var partPaths = new List<string>(parts.Count);

        try
        {
            var partPdfs = new List<byte[]>(parts.Count);
            for (var index = 0; index < parts.Count; index++)
            {
                var part = parts[index];
                var source = ResolvePartTemplate(sources, part);

                var partPath = _files.GetPdfPath($"{jobNo}_p{index + 1}");
                partPaths.Add(partPath);

                await _renderer
                    .RenderPdfAsync(
                        source.Path,
                        source.FileType,
                        ToFields(part),
                        ToRows(part),
                        images: null,
                        templateConfigJson: source.ConfigJson,
                        tableStartMarker: null,
                        tableEndMarker: null,
                        pdfPath: partPath,
                        ct: ct)
                    .ConfigureAwait(false);

                partPdfs.Add(await File.ReadAllBytesAsync(partPath, ct).ConfigureAwait(false));
                _files.TryDelete(partPath);
            }

            var merged = PdfMergeHelper.Merge(partPdfs);
            if (merged.Length == 0)
                throw new BusinessException("JOB_PAYLOAD_INVALID", "批量预览合并失败：未生成 PDF 内容");

            await File.WriteAllBytesAsync(mergedPath, merged, ct).ConfigureAwait(false);
        }
        catch
        {
            _files.TryDelete(partPaths.ToArray());
            _files.TryDelete(mergedPath);
            throw;
        }

        _logger.Info(
            $"[JobTask] 批量预览 PDF 已生成: No={jobNo}, Parts={parts.Count}, Bytes={new FileInfo(mergedPath).Length}");
        _files.OpenWithShell(mergedPath);
        _logger.Info($"[JobTask] 已调用系统默认阅读器打开批量预览: No={jobNo}");

        // 与单模板预览一致：不立即删除（用户可能还开着），只保留最近 N 个。
        _files.TrimPreviewFiles(_previewKeepCount);
        return null;
    }

    /// <summary>打印：逐 part 渲染源文档 + PDF，再交 Office COM 打印（份数/双面由任务参数决定）。</summary>
    private async Task<string?> PrintBatchAsync(
        string jobNo,
        IReadOnlyList<AgentBatchPart> parts,
        IReadOnlyDictionary<long, BatchTemplateSource> sources,
        string? printerName,
        int copies,
        bool isDuplex,
        CancellationToken ct)
    {
        _files.EnsureRoot();

        var tempPaths = new List<string>(parts.Count * 2);
        string? combinedWarning = null;

        try
        {
            for (var index = 0; index < parts.Count; index++)
            {
                var part = parts[index];
                var source = ResolvePartTemplate(sources, part);

                var partNo = $"{jobNo}_p{index + 1}";
                var partSource = _files.GetRenderedSourcePath(partNo, source.FileType);
                var partPdf = _files.GetPdfPath(partNo);
                tempPaths.Add(partSource);
                tempPaths.Add(partPdf);

                await _renderer
                    .RenderSourceAsync(
                        source.Path,
                        source.FileType,
                        ToFields(part),
                        ToRows(part),
                        images: null,
                        templateConfigJson: source.ConfigJson,
                        tableStartMarker: null,
                        tableEndMarker: null,
                        outputPath: partSource,
                        ct: ct)
                    .ConfigureAwait(false);

                await _renderer
                    .ExportPdfFromSourceAsync(partSource, source.FileType, partPdf, ct)
                    .ConfigureAwait(false);

                var warning = await _printer
                    .PrintAsync(partPdf, partSource, source.FileType, printerName, copies, isDuplex, ct)
                    .ConfigureAwait(false);

                combinedWarning = CombineWarnings(combinedWarning, warning);

                // 每页打印完立即清理，避免含个人信息的文书在 %TEMP% 堆积。
                _files.TryDelete(partSource, partPdf);
            }

            _logger.Info(
                $"[JobTask] 批量打印完成并已清理渲染产物: No={jobNo}, Parts={parts.Count}, " +
                $"Copies={copies}, Duplex={isDuplex}");
            return combinedWarning;
        }
        catch
        {
            _files.TryDelete(tempPaths.ToArray());
            throw;
        }
    }

    /// <summary>
    /// 保存：逐 part 渲染源 xlsx + PDF，落到 <c>{OutputRoot}\{category}\{period}</c>，
    /// 文件名优先 part.FileName，同名自动加 <c>_2</c>、<c>_3</c>；完成后打开目录。
    /// </summary>
    private async Task<string?> SaveBatchAsync(
        string jobNo,
        AgentBatchPayload batch,
        IReadOnlyList<AgentBatchPart> parts,
        IReadOnlyDictionary<long, BatchTemplateSource> sources,
        CancellationToken ct)
    {
        var folder = OutputFolderService.EnsureBatchFolder(_outputRoot, batch.Category, batch.Period);
        var usedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var source = ResolvePartTemplate(sources, part);

            var baseName = BuildUniqueBaseName(
                folder,
                FirstNonEmpty(part.FileName, source.FileName),
                $"{jobNo}_p{index + 1}",
                source.FileType,
                usedBaseNames);

            var sourcePath = Path.Combine(folder, $"{baseName}.{source.FileType}");
            var pdfPath = Path.Combine(folder, $"{baseName}.pdf");

            try
            {
                await _renderer
                    .RenderSourceAsync(
                        source.Path,
                        source.FileType,
                        ToFields(part),
                        ToRows(part),
                        images: null,
                        templateConfigJson: source.ConfigJson,
                        tableStartMarker: null,
                        tableEndMarker: null,
                        outputPath: sourcePath,
                        ct: ct)
                    .ConfigureAwait(false);

                await _renderer
                    .ExportPdfFromSourceAsync(sourcePath, source.FileType, pdfPath, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // 该页只落了一半（如源文件已写、PDF 导出失败）：清理本页残留，前面已成功的页保留。
                _files.TryDelete(sourcePath, pdfPath);
                throw;
            }
        }

        _logger.Info($"[JobTask] 批量保存完成: No={jobNo}, Parts={parts.Count}, Folder={folder}");
        _files.OpenWithShell(folder);
        _logger.Info($"[JobTask] 已打开批量保存输出目录: No={jobNo}");
        return null;
    }

    /// <summary>按 part.TemplateId 找模板；只有单模板负载时容错回退（契约要求 part 必带 templateId）。</summary>
    private BatchTemplateSource ResolvePartTemplate(
        IReadOnlyDictionary<long, BatchTemplateSource> sources,
        AgentBatchPart part)
    {
        if (part is not null && part.TemplateId > 0 && sources.TryGetValue(part.TemplateId, out var source))
            return source;

        if (sources.Count == 1)
            return sources.Values.First();

        throw new BusinessException(
            "JOB_PAYLOAD_INVALID",
            $"批量任务页引用了未下发的模板: TemplateId={(part?.TemplateId.ToString() ?? "-")}");
    }

    private static IReadOnlyDictionary<string, string> ToFields(AgentBatchPart part)
        => part.Fields ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static IReadOnlyList<Dictionary<string, string>> ToRows(AgentBatchPart part)
        => part.TableRows ?? new List<Dictionary<string, string>>();

    /// <summary>同名文件不覆盖：{base}、{base}_2、{base}_3…（同时检查磁盘已有源文件/PDF）。</summary>
    private static string BuildUniqueBaseName(
        string folder,
        string? preferred,
        string fallback,
        string fileType,
        HashSet<string> usedBaseNames)
    {
        var raw = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();
        var baseName = OutputFolderService.SanitizeSegment(
            Path.GetFileNameWithoutExtension(raw),
            OutputFolderService.SanitizeSegment(fallback, "report"));
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "report";

        var candidate = baseName;
        var suffix = 2;
        while (usedBaseNames.Contains(candidate)
               || File.Exists(Path.Combine(folder, $"{candidate}.{fileType}"))
               || File.Exists(Path.Combine(folder, $"{candidate}.pdf")))
        {
            candidate = $"{baseName}_{suffix}";
            suffix++;
        }

        usedBaseNames.Add(candidate);
        return candidate;
    }

    private static string NormalizeBatchMode(string? mode)
    {
        var value = (mode ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
        return value switch
        {
            "" or BatchModePreview => BatchModePreview,
            BatchModePrint => BatchModePrint,
            BatchModeSave => BatchModeSave,
            BatchModeOpenFolder or "openfolder" or "open" => BatchModeOpenFolder,
            _ => value
        };
    }
}
