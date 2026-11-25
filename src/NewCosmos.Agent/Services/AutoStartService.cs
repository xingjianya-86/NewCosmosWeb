using Microsoft.Win32;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 开机自启（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）。
/// 只写当前用户注册表，不需要管理员权限；值为当前 exe 绝对路径（tray 模式）
/// 或 "<exe> worker"（纯控制台轮询模式）。
/// </summary>
public static class AutoStartService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "NewCosmos.Agent";

    /// <summary>自启命令行：tray = 无参数（默认托盘）；worker = exe + worker。</summary>
    public static string BuildCommand(string mode)
    {
        var exePath = ResolveExecutablePath();
        var quoted = $"\"{exePath}\"";
        return string.Equals(mode, "worker", StringComparison.OrdinalIgnoreCase)
            ? quoted + " worker"
            : quoted;
    }

    public static bool Install(string mode, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = $"无法打开注册表项 HKCU\\{RunKeyPath}";
                return false;
            }

            var command = BuildCommand(mode);
            key.SetValue(ValueName, command, RegistryValueKind.String);

            AgentConsole.Success($"已设置开机自启（{mode}）: {command}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool Uninstall(out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                AgentConsole.Info("开机自启项不存在（无需删除）");
                return true;
            }

            if (key.GetValue(ValueName) is null)
            {
                AgentConsole.Info("开机自启项不存在（无需删除）");
                return true;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            AgentConsole.Success("已删除开机自启项");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>当前进程可执行文件路径（单文件发布/框架依赖均可用）。</summary>
    public static string ResolveExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            return Path.GetFullPath(processPath);

        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length > 0 && !string.IsNullOrWhiteSpace(commandLine[0]))
            return Path.GetFullPath(commandLine[0]);

        return Path.Combine(AppContext.BaseDirectory, "NewCosmos.Agent.exe");
    }
}
