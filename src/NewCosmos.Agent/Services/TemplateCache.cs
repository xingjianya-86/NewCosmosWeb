using System.Security.Cryptography;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 模板本地缓存：<c>%TEMP%\NewCosmos\TemplateCache\{templateId}-{templateHash}.{ext}</c>。
/// 同 templateId + 同 hash 直接复用（不重复解码/写盘）；模板是公开文档，不同于含个人信息的
/// 渲染产物，任务完成后<b>不删除</b>。目录总量超过 200MB 时按最后写入时间清理最旧文件。
/// </summary>
public sealed class TemplateCache
{
    /// <summary>缓存目录上限（超过后清理到高水位以下）。</summary>
    public const long MaxTotalBytes = 200L * 1024 * 1024;

    /// <summary>清理目标水位：留出余量，避免每个任务都触发一次清理。</summary>
    private const long TrimTargetBytes = 160L * 1024 * 1024;

    private readonly ILoggerService _logger;
    private readonly string _root;

    public TemplateCache(ILoggerService logger, string? rootDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _root = Path.GetFullPath(
            string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Path.GetTempPath(), "NewCosmos", "TemplateCache")
                : Environment.ExpandEnvironmentVariables(rootDirectory));
    }

    public string Root => _root;

    /// <summary>
    /// 命中缓存时返回模板路径（不校验文件内容）；未命中返回 null。
    /// 供 Agent 在 payload 已带 hash 时跳过 Base64 解码/写盘。
    /// </summary>
    public string? TryGetPath(long? templateId, string? templateHash, string fileType)
    {
        var hash = NormalizeHash(templateHash);
        if (hash is null) return null;

        var path = Path.Combine(_root, $"{SanitizeId(templateId)}-{hash}.{NormalizeExtension(fileType)}");
        return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    /// <summary>
    /// 确保模板已在缓存目录中，返回可用的模板文件路径。
    /// <paramref name="templateHash"/> 为空时按模板字节现算 SHA256（服务端 file_hash 缺失的兜底）。
    /// </summary>
    public async Task<(string Path, bool CacheHit)> EnsureAsync(
        long? templateId,
        string? templateHash,
        string fileType,
        byte[]? templateBytes,
        CancellationToken ct)
    {
        var extension = NormalizeExtension(fileType);
        var hash = NormalizeHash(templateHash);

        if (hash is null)
        {
            if (templateBytes is not { Length: > 0 })
                throw new BusinessException("JOB_PAYLOAD_INVALID", "任务缺少模板文件且无模板哈希，无法命中缓存");

            hash = ComputeSha256(templateBytes);
        }

        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{SanitizeId(templateId)}-{hash}.{extension}");

        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            _logger.Info($"[TemplateCache] 命中缓存: Template={templateId?.ToString() ?? "-"}, Hash={ShortHash(hash)}");
            return (path, true);
        }

        if (templateBytes is not { Length: > 0 })
            throw new BusinessException("JOB_PAYLOAD_INVALID", "任务模板文件为空，无法写入缓存");

        // 原子落盘：先写临时文件再改名，避免进程中断留下半截模板被后续任务复用。
        var tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(tempPath, templateBytes, ct).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }

        _logger.Info(
            $"[TemplateCache] 已缓存模板: Template={templateId?.ToString() ?? "-"}, Hash={ShortHash(hash)}, Bytes={templateBytes.Length}");
        TrimIfOverLimit();
        return (path, false);
    }

    /// <summary>目录超过上限时按最后写入时间从旧到新删除，直到低于目标水位。</summary>
    public void TrimIfOverLimit()
    {
        try
        {
            var files = new DirectoryInfo(_root)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .ToList();

            // 顺带清理异常中断遗留的临时文件（超过 1 小时）。
            var staleBefore = DateTime.UtcNow.AddHours(-1);
            foreach (var stale in files.Where(f => f.Name.Contains(".tmp-", StringComparison.Ordinal)
                                                   && f.LastWriteTimeUtc < staleBefore))
            {
                TryDelete(stale.FullName);
                files.Remove(stale);
            }

            var total = files.Sum(f => f.Length);
            if (total <= MaxTotalBytes) return;

            long removed = 0;
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total - removed <= TrimTargetBytes) break;
                var length = file.Length;
                TryDelete(file.FullName);
                removed += length;
            }

            _logger.Info($"[TemplateCache] 缓存超限已清理: 回收 {removed / 1024 / 1024}MB，当前约 {(total - removed) / 1024 / 1024}MB");
        }
        catch (Exception ex)
        {
            // 清理是尽力而为，不影响任务执行。
            _logger.Warn($"[TemplateCache] 缓存清理失败: {ex.Message}");
        }
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string? NormalizeHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;

        var trimmed = hash.Trim().ToLowerInvariant();
        foreach (var c in trimmed)
        {
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c)) return null;
        }

        return trimmed.Length is >= 16 and <= 128 ? trimmed : null;
    }

    private static string NormalizeExtension(string fileType)
    {
        var value = (fileType ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return value switch
        {
            "docx" or "word" => "docx",
            "xlsx" or "excel" => "xlsx",
            _ => throw new BusinessException(
                ErrorCodes.FILE_FORMAT_ERROR,
                $"不支持的模板类型: {fileType ?? "-"}（仅支持 docx/xlsx）")
        };
    }

    private static string SanitizeId(long? templateId)
        => templateId is > 0 ? templateId.Value.ToString() : "0";

    private static string ShortHash(string hash)
        => hash.Length <= 12 ? hash : hash[..12];

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 文件被占用：下次清理再试
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足：不中断任务
        }
    }
}
