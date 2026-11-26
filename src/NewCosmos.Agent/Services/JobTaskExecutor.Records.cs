using NewCosmos.Agent.Models;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 打印/保存成功后的文件回传（POST /api/agent/jobs/{id}/records）：
/// 打印模式在临时源/PDF 被删除<i>之前</i>读取字节，打印成功后逐条上传；
/// 保存模式直接从输出目录读取。逐 part 串行读取/上传，避免一次性生成巨量 Base64。
/// 上传失败只记 Warn，不影响任务完成结果。
/// </summary>
public sealed partial class JobTaskExecutor
{
    /// <summary>
    /// 读取一份文书的源文件 + PDF 并构造回传记录（字节此时已编码，待打印成功后再上传）。
    /// 读取失败返回 null（降级为不回传）；只有 Agent 取消才向上抛出。
    /// </summary>
    private async Task<AgentPrintRecord?> TryReadRecordAsync(
        string? templateName,
        string fileName,
        string sourceType,
        string sourcePath,
        string pdfPath,
        CancellationToken ct)
    {
        try
        {
            var pdfBytes = File.Exists(pdfPath)
                ? await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false)
                : null;

            if (pdfBytes is not { Length: > 0 })
            {
                _logger.Warn("[JobTask] 打印记录回传跳过：PDF 不存在或为空");
                return null;
            }

            byte[]? sourceBytes = null;
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                sourceBytes = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
            }

            return BuildRecord(templateName, fileName, sourceType, pdfBytes, sourceBytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 读文件失败（占用/权限等）不应影响已经完成的打印/保存。
            _logger.Warn(
                $"[JobTask] 读取回传文件失败，本份不回传（不影响任务）: {HttpAgentClient.Truncate(ex.Message, 300)}");
            return null;
        }
    }

    private static AgentPrintRecord BuildRecord(
        string? templateName,
        string fileName,
        string sourceType,
        byte[] pdfBytes,
        byte[]? sourceBytes)
    {
        var hasSource = sourceBytes is { Length: > 0 };
        return new AgentPrintRecord
        {
            TemplateName = ResolveTemplateName(templateName),
            FileName = fileName,
            FileType = sourceType,
            PdfBase64 = Convert.ToBase64String(pdfBytes),
            PdfSize = pdfBytes.LongLength,
            SourceBase64 = hasSource ? Convert.ToBase64String(sourceBytes!) : null,
            SourceType = hasSource ? sourceType : null,
            SourceSize = hasSource ? sourceBytes!.LongLength : 0
        };
    }

    /// <summary>
    /// 上传一条记录。任何失败（含取消）都只记 Warn：任务已经打印/保存成功，
    /// 不能因为回传失败让服务端按 fail 重试而重复打印。
    /// </summary>
    private async Task UploadRecordAsync(long jobId, AgentPrintRecord? record, CancellationToken ct)
    {
        if (record is null) return;

        if (_jobClient is null || string.IsNullOrWhiteSpace(_serverBaseUrl) || string.IsNullOrWhiteSpace(_deviceKey))
        {
            _logger.Warn($"[JobTask] 未配置任务客户端/设备密钥，跳过打印记录回传: Job={jobId}");
            return;
        }

        try
        {
            await _jobClient
                .UploadPrintRecordsAsync(_serverBaseUrl!, _deviceKey!, jobId, new[] { record }, ct)
                .ConfigureAwait(false);

            _logger.Info(
                $"[JobTask] 打印记录已回传: Job={jobId}, Type={record.FileType}, " +
                $"PdfBytes={record.PdfSize}, SourceBytes={record.SourceSize}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Warn($"[JobTask] 打印记录回传被取消，任务仍视为完成: Job={jobId}");
        }
        catch (Exception ex)
        {
            _logger.Warn(
                $"[JobTask] 打印记录回传失败（不影响任务完成）: Job={jobId}, " +
                $"Error={HttpAgentClient.Truncate(ex.Message, 300)}");
        }
    }

    /// <summary>回传文件名：优先最终落盘名，清洗非法字符并强制实际扩展名。</summary>
    private static string BuildRecordFileName(string? preferred, string fallback, string fileType)
    {
        var raw = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();
        var name = Path.GetFileName(raw);
        var baseName = OutputFolderService.SanitizeSegment(
            Path.GetFileNameWithoutExtension(name),
            OutputFolderService.SanitizeSegment(fallback, "record"));

        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "record";

        return $"{baseName}.{fileType}";
    }

    /// <summary>模板名称统一去掉扩展名（契约示例：档案_封面）。</summary>
    private static string? ResolveTemplateName(string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName)) return null;

        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(templateName.Trim()));
        return string.IsNullOrWhiteSpace(baseName) ? null : baseName;
    }
}
