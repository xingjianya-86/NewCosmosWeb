namespace NewCosmos.Agent.Services;

/// <summary>控制台输出（面向运维人员）；文件日志仍走 ILoggerService。</summary>
public static class AgentConsole
{
    public static void Info(string message) => Write(message, ConsoleColor.Gray);

    public static void Success(string message) => Write(message, ConsoleColor.Green);

    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);

    public static void Error(string message) => Write(message, ConsoleColor.Red);

    private static void Write(string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
