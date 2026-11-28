using System.Diagnostics;
using System.Reflection;
using NewCosmos.Models.Results;

namespace NewCosmos.Agent.Services.Update;

/// <summary>
/// 真实安装执行：用 cmd.exe /c 包装启动 Inno 安装包（/SILENT /NORESTART）。
///
/// 为什么必须用 cmd 包装而不是直接 Process.Start(setup)：
/// <code>
/// start /wait "" "&lt;setup&gt;" /SILENT /NORESTART &amp; if errorlevel 1 start "" "&lt;当前 Agent&gt;"
/// </code>
/// <list type="bullet">
/// <item>安装成功（errorlevel 0）→ 由安装脚本 [Run] 段启动新版 Agent；</item>
/// <item>安装失败（errorlevel ≥ 1，旧文件未被替换）→ 同一条命令里把**旧版** Agent 拉起来，
/// Agent 不会因升级失败而"变砖"；</item>
/// <item><c>start /wait</c> 让等待发生在 cmd 内部，本方法立即返回——Agent 需要先退出才能让出文件锁。</item>
/// </list>
///
/// 引号细节：路径含空格（安装目录是 <c>NewCosmos Agent</c>），而 <c>start</c> 的第一个引号参数
/// 被当作窗口标题，所以标题必须显式给空串 <c>""</c>，否则第一个路径会被吃掉当标题。
/// </summary>
public static class ProcessAgentInstaller
{
    public static Result Launch(string setupPath)
    {
        if (string.IsNullOrWhiteSpace(setupPath) || !File.Exists(setupPath))
            return Result.Failure("AGENT_UPDATE_SETUP_MISSING", $"安装包不存在: {setupPath}");

        // 当前 Agent 自身路径：升级失败时的兜底重启目标。
        var selfPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(selfPath))
            selfPath = Assembly.GetEntryAssembly()?.Location ?? string.Empty;

        var command = $"start /wait \"\" \"{setupPath}\" /SILENT /NORESTART"
            + (string.IsNullOrWhiteSpace(selfPath)
                ? string.Empty
                : $" & if errorlevel 1 start \"\" \"{selfPath}\"");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                // 不用 ShellExecute：不弹黑窗、不依赖关联程序；安装等待在 cmd 内部完成。
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return Result.Failure("AGENT_UPDATE_LAUNCH_FAILED", "无法启动 cmd.exe 执行安装");

            // 不等待：安装器 CloseApplications 会处理仍在退出中的旧进程（见 UpdateService 竞态说明）。
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure("AGENT_UPDATE_LAUNCH_FAILED", $"启动安装程序失败: {ex.Message}");
        }
    }
}
