using System.Diagnostics;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 打印任务临时文件管理（%TEMP%\NewCosmos\PrintJobs）：
/// 统一定位模板 / 渲染源文件 / PDF，控制预览文件保留数量，打印成功后清理。
/// </summary>
public sealed class JobFileStore
{
    private readonly string _root;

    public JobFileStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("打印任务目录不能为空", nameof(rootDirectory));

        _root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootDirectory));
    }

    public string Root => _root;

    public void EnsureRoot() => Directory.CreateDirectory(_root);

    public string GetTemplatePath(string jobNo, string extension)
        => Path.Combine(_root, $"{Sanitize(jobNo)}.{NormalizeExtension(extension)}");

    public string GetRenderedSourcePath(string jobNo, string extension)
        => Path.Combine(_root, $"{Sanitize(jobNo)}.rendered.{NormalizeExtension(extension)}");

    public string GetPdfPath(string jobNo)
        => Path.Combine(_root, $"{Sanitize(jobNo)}.pdf");

    /// <summary>用系统默认 PDF 阅读器打开预览；无关联程序时抛 Win32Exception。</summary>
    public void OpenWithShell(string filePath)
    {
        Process.Start(new ProcessStartInfo(filePath)
        {
            UseShellExecute = true
        });
    }

    /// <summary>
    /// 预览成功后保留最近 <paramref name="keepCount"/> 个 PDF（含刚生成的），
    /// 更早的尽力删除；文件仍被阅读器占用时忽略失败，下次清理再试。
    /// </summary>
    public void TrimPreviewFiles(int keepCount)
    {
        if (keepCount < 1) keepCount = 1;

        try
        {
            var previews = Directory
                .EnumerateFiles(_root, "*.pdf", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            foreach (var info in previews.Skip(keepCount))
                TryDelete(info.FullName);
        }
        catch (Exception)
        {
            // 清理是尽力而为，不影响任务结果。
        }
    }

    public void TryDelete(params string?[] paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // 文件仍被 Office/PDF 阅读器占用：留给后续清理。
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足：留给运维处理，不中断轮询。
        }
    }

    private static string Sanitize(string jobNo)
    {
        var value = string.IsNullOrWhiteSpace(jobNo) ? "job" : jobNo.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "job" : sanitized;
    }

    private static string NormalizeExtension(string extension)
    {
        var value = (extension ?? string.Empty).Trim().TrimStart('.');
        return value.ToLowerInvariant() switch
        {
            "docx" or "word" => "docx",
            "xlsx" or "excel" => "xlsx",
            _ => throw new NotSupportedException($"不支持的模板类型: {extension}")
        };
    }
}
