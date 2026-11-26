using System.Diagnostics;
using System.IO;

namespace NewCosmos.Shell.Services;

/// <summary>打开目录/文件的统一入口；失败返回可展示的错误，不抛异常。</summary>
public static class ShellLauncher
{
    public static bool OpenFolder(string path, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool OpenFile(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                error = $"文件不存在：{path}";
                return false;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool OpenUrl(string url, out string? error)
    {
        error = null;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
