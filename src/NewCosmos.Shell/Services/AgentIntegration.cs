using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace NewCosmos.Shell.Services;

public sealed record AgentOperationResult(bool Success, string Message);

public sealed record AgentStatus(
    bool IsRunning,
    string? ExecutablePath,
    string? ExecutableSource,
    string? Version);

/// <summary>
/// 壳与 NewCosmos.Agent 的边界：只做定位、状态展示、启动/重启、打开日志/配置/输出目录。
/// 不引用 Agent 程序集，不执行 Office COM。
/// </summary>
public sealed class AgentIntegration
{
    public const string ProcessName = "NewCosmos.Agent";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "NewCosmos.Agent";

    private readonly ShellOptions _options;

    public AgentIntegration(ShellOptions options) => _options = options;

    public string ConfigPath
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("NC_AGENT_CONFIG");
            var path = string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NewCosmos", "agent.json")
                : overridden;
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
    }

    /// <summary>Agent 日志目录：%APPDATA%\NewCosmos\Logs（Serilog 按日滚动）。</summary>
    public string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NewCosmos", "Logs");

    public string DefaultOutputRoot
    {
        get
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, "Documents", "NewCosmos", "输出");
        }
    }

    public string DownloadsDirectory
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                if (key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string value
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()));
                }
            }
            catch
            {
                // 注册表不可读时退回默认路径。
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
        }
    }

    public bool IsRunning()
    {
        var processes = FindRunningProcesses();
        try
        {
            return processes.Count > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public AgentStatus GetStatus()
    {
        var exe = FindExecutable(out var source);
        string? version = null;
        if (exe is not null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                version = string.IsNullOrWhiteSpace(info.ProductVersion)
                    ? info.FileVersion
                    : info.ProductVersion;
            }
            catch
            {
                // 版本读取失败不影响状态展示。
            }
        }

        return new AgentStatus(IsRunning(), exe, source, version);
    }

    /// <summary>
    /// 查找 Agent 可执行文件。
    /// 优先级：shell.json agentPath → HKCU Run 自启项 → %LOCALAPPDATA%\Programs\NewCosmos Agent
    /// → 壳同目录 → 壳目录\Agent。
    /// </summary>
    public string? FindExecutable(out string? source)
    {
        source = null;

        foreach (var (candidate, label) in EnumerateCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
                if (!Path.IsPathRooted(expanded))
                {
                    var baseDir = AppContext.BaseDirectory;
                    expanded = Path.Combine(baseDir, expanded);
                }

                expanded = Path.GetFullPath(expanded);
                if (Directory.Exists(expanded))
                    expanded = Path.Combine(expanded, "NewCosmos.Agent.exe");

                if (File.Exists(expanded))
                {
                    source = label;
                    return expanded;
                }
            }
            catch
            {
                // 无效候选路径继续查找下一个。
            }
        }

        return null;
    }

    private IEnumerable<(string? Path, string Label)> EnumerateCandidates()
    {
        if (!string.IsNullOrWhiteSpace(_options.AgentPath))
            yield return (_options.AgentPath, "shell.json agentPath");

        var registryCommand = ReadRunValue();
        var registryExe = ExtractExecutable(registryCommand);
        if (registryExe is not null)
            yield return (registryExe, @"HKCU\...\Run\NewCosmos.Agent");

        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "NewCosmos Agent", "NewCosmos.Agent.exe");
        yield return (localPrograms, "%LOCALAPPDATA%\\Programs\\NewCosmos Agent");

        yield return (Path.Combine(AppContext.BaseDirectory, "NewCosmos.Agent.exe"), "壳同目录");
        yield return (Path.Combine(AppContext.BaseDirectory, "Agent", "NewCosmos.Agent.exe"), "壳目录\\Agent");
    }

    private static string? ReadRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从 Run 值（"&lt;exe&gt;" 或 "&lt;exe&gt; worker"）中取出 exe 路径。</summary>
    internal static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        string path;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end < 2) return null;
            path = text[1..end];
        }
        else
        {
            var exeIndex = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                path = text[..(exeIndex + 4)];
            }
            else
            {
                var space = text.IndexOf(' ');
                path = space < 0 ? text : text[..space];
            }
        }

        path = path.Trim();
        return path.Length == 0 ? null : path;
    }

    /// <summary>无参数启动 Agent（默认即托盘 + 心跳 + 任务轮询）。</summary>
    public AgentOperationResult Start()
    {
        if (IsRunning())
            return new AgentOperationResult(true, "Agent 已在运行。");

        var exe = FindExecutable(out var source);
        if (exe is null)
        {
            return new AgentOperationResult(false,
                "未找到 NewCosmos.Agent.exe。\r\n\r\n已查找：\r\n" +
                "• shell.json 的 agentPath\r\n" +
                "• HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\NewCosmos.Agent\r\n" +
                "• %LOCALAPPDATA%\\Programs\\NewCosmos Agent\\NewCosmos.Agent.exe\r\n" +
                "• 壳同目录 / Agent 子目录");
        }

        try
        {
            var startInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            };
            Process.Start(startInfo);
            return new AgentOperationResult(true, $"已启动 Agent（{source}）：\r\n{exe}");
        }
        catch (Exception ex)
        {
            return new AgentOperationResult(false, $"启动 Agent 失败：{ex.Message}\r\n{exe}");
        }
    }

    /// <summary>先结束已在运行的 Agent（托盘进程无主窗口，必要时强杀），再重新启动。</summary>
    public AgentOperationResult Restart()
    {
        var processes = FindRunningProcesses();
        foreach (var process in processes)
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(1500))
                    process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Shell] 结束 Agent 进程失败: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        if (processes.Count > 0)
            Thread.Sleep(400);

        return Start();
    }

    /// <summary>读取 agent.json 的 outputRoot；缺省 %USERPROFILE%\Documents\NewCosmos\输出。</summary>
    public string ResolveOutputRoot()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!property.Name.Equals("outputRoot", StringComparison.OrdinalIgnoreCase)
                            || property.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var configured = property.Value.GetString();
                        if (string.IsNullOrWhiteSpace(configured)) break;

                        var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
                        if (!Path.IsPathRooted(expanded))
                        {
                            expanded = Path.Combine(
                                Path.GetDirectoryName(ConfigPath) ?? AppContext.BaseDirectory,
                                expanded);
                        }

                        return Path.GetFullPath(expanded);
                    }
                }
            }
        }
        catch
        {
            // 配置损坏/无权限时退回默认目录。
        }

        return DefaultOutputRoot;
    }

    private static List<Process> FindRunningProcesses()
    {
        var list = new List<Process>();
        try
        {
            foreach (var process in Process.GetProcessesByName(ProcessName))
            {
                try
                {
                    if (process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }
                }
                catch
                {
                    // 无法读取进程状态（权限/竞争退出），仍按运行中处理。
                }

                list.Add(process);
            }
        }
        catch
        {
            // 枚举失败时按未运行处理。
        }

        return list;
    }
}
