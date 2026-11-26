namespace NewCosmos.Agent.Services;

/// <summary>
/// 控制台输出（面向运维人员）；文件日志仍走 ILoggerService。
/// 托盘模式没有控制台，这里自动静默，避免 GUI(WinExe) 因无效句柄抛异常。
/// </summary>
public static class AgentConsole
{
    public static void Info(string message) => Write(message, ConsoleColor.Gray);

    public static void Success(string message) => Write(message, ConsoleColor.Green);

    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);

    public static void Error(string message) => Write(message, ConsoleColor.Red);

    private static void Write(string message, ConsoleColor color)
    {
        if (!ConsoleHost.HasOutput) return;

        var colorChanged = false;
        try
        {
            // 仅真实附着控制台时设置颜色；MessageBox 暂存缓冲不支持颜色。
            if (ConsoleHost.HasConsole)
            {
                Console.ForegroundColor = color;
                colorChanged = true;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        catch (Exception)
        {
            // 控制台被关闭/重定向：输出失败不能影响业务流程，日志仍写文件。
        }
        finally
        {
            if (colorChanged)
            {
                try
                {
                    Console.ResetColor();
                }
                catch (Exception)
                {
                    // 忽略
                }
            }
        }
    }
}
