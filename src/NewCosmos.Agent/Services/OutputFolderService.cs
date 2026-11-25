using System.Diagnostics;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 批量模板任务的输出目录：<c>{OutputRoot}\{分类}\{期间}\</c>
/// （如 <c>%USERPROFILE%\Documents\NewCosmos\高龄月报\202611</c>）。
/// 目录名做最小清洗，避免服务端下发的 category/period 含路径分隔符或非法字符。
/// </summary>
public static class OutputFolderService
{
    /// <summary>托盘「打开输出目录」与批量保存的默认分类。</summary>
    public const string DefaultCategory = "高龄月报";

    /// <summary>解析输出根：空值回退 Documents\NewCosmos，展开环境变量后转绝对路径。</summary>
    public static string ResolveRoot(string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.GetFullPath(string.IsNullOrWhiteSpace(userProfile)
                ? Path.Combine(Path.GetTempPath(), "NewCosmos", "Output")
                : Path.Combine(userProfile, "Documents", "NewCosmos"));
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputRoot.Trim()));
    }

    /// <summary>分类目录 <c>{OutputRoot}\{category}</c>（不存在时创建）。</summary>
    public static string EnsureCategoryFolder(string? outputRoot, string? category)
    {
        var folder = Path.Combine(ResolveRoot(outputRoot), SanitizeSegment(category, DefaultCategory));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>期间目录 <c>{OutputRoot}\{category}\{period}</c>；period 为空时退回分类目录。</summary>
    public static string EnsureBatchFolder(string? outputRoot, string? category, string? period)
    {
        var categoryFolder = EnsureCategoryFolder(outputRoot, category);
        if (string.IsNullOrWhiteSpace(period))
            return categoryFolder;

        var folder = Path.Combine(categoryFolder, SanitizeSegment(period, "未定期"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>用资源管理器打开目录；目录不存在时先创建。</summary>
    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    /// <summary>清洗单级目录/文件名片段：替换非法字符、去掉首尾点与空格。</summary>
    public static string SanitizeSegment(string? segment, string fallback)
    {
        var value = (segment ?? string.Empty).Trim();
        if (value.Length == 0)
            return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c)
            .ToArray();

        var sanitized = new string(chars).Trim('.', ' ');
        return sanitized.Length == 0 ? fallback : sanitized;
    }
}
