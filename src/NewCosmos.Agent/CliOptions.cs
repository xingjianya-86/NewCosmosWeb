namespace NewCosmos.Agent;

/// <summary>Agent 命令行参数。</summary>
public sealed class CliOptions
{
    public string Command { get; private set; } = "heartbeat";

    public bool Help { get; private set; }

    public bool ShowVersion { get; private set; }

    public string? ConfigPath { get; private set; }

    public string? Server { get; private set; }

    public string? RegisterToken { get; private set; }

    public string? Template { get; private set; }

    public string? Data { get; private set; }

    public string? Out { get; private set; }

    public string? MachineName { get; private set; }

    public int? HeartbeatSeconds { get; private set; }

    public static bool TryParse(string[] args, out CliOptions options, out string? error)
    {
        options = new CliOptions();
        error = null;
        var commandSet = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg)) continue;

            // 支持 --key=value
            string? inlineValue = null;
            var equalsIndex = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && equalsIndex > 2)
            {
                inlineValue = arg[(equalsIndex + 1)..];
                arg = arg[..equalsIndex];
            }

            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "-?":
                    options.Help = true;
                    break;

                case "--version":
                    options.ShowVersion = true;
                    break;

                case "--register":
                    if (!SetCommand(options, "register", ref commandSet, out error)) return false;
                    break;

                case "--heartbeat":
                    if (!SetCommand(options, "heartbeat", ref commandSet, out error)) return false;
                    break;

                case "--heartbeat-once":
                    if (!SetCommand(options, "heartbeat-once", ref commandSet, out error)) return false;
                    break;

                case "--render":
                    if (!SetCommand(options, "render", ref commandSet, out error)) return false;
                    break;

                case "--printers":
                    if (!SetCommand(options, "printers", ref commandSet, out error)) return false;
                    break;

                case "--probe-office":
                    if (!SetCommand(options, "probe-office", ref commandSet, out error)) return false;
                    break;

                case "--config":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var config, out error)) return false;
                    options.ConfigPath = config;
                    break;

                case "--server":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var server, out error)) return false;
                    options.Server = server;
                    break;

                case "--register-token":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var token, out error)) return false;
                    options.RegisterToken = token;
                    break;

                case "--template":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var template, out error)) return false;
                    options.Template = template;
                    break;

                case "--data":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var data, out error)) return false;
                    options.Data = data;
                    break;

                case "--out":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var output, out error)) return false;
                    options.Out = output;
                    break;

                case "--machine-name":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var machine, out error)) return false;
                    options.MachineName = machine;
                    break;

                case "--heartbeat-seconds":
                    if (!TryReadValue(args, ref i, inlineValue, arg, out var secondsText, out error)) return false;
                    if (!int.TryParse(secondsText, out var seconds) || seconds < 5 || seconds > 3600)
                    {
                        error = "--heartbeat-seconds 必须是 5..3600 的整数";
                        return false;
                    }
                    options.HeartbeatSeconds = seconds;
                    break;

                default:
                    // 允许直接写命令名（register / heartbeat / render ...）
                    var tokenCommand = arg.ToLowerInvariant();
                    if (tokenCommand is "register" or "heartbeat" or "heartbeat-once" or "render" or "printers" or "probe-office")
                    {
                        if (!SetCommand(options, tokenCommand, ref commandSet, out error)) return false;
                        break;
                    }

                    error = $"未知参数: {args[i]}（--help 查看用法）";
                    return false;
            }
        }

        return true;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
NewCosmos.Agent — Windows 固定 PC 的 Office/打印代理（P0）

用法:
  NewCosmos.Agent.exe [命令] [选项]

命令（缺省 = heartbeat 循环）:
  --register            向服务端注册本机，保存 deviceKey 后退出
  --heartbeat           持续心跳（默认 30 秒，Ctrl+C 退出）
  --heartbeat-once      只发一次心跳
  --printers            列出本机打印机（默认打印机标 [默认]）
  --probe-office        探测 Microsoft Office / WPS 可用性
  --render              用 Office 模板渲染一次 PDF（真实 COM）

渲染选项:
  --template <path>     模板文件（.docx / .xlsx）
  --data <json|path>    字段 JSON 文件或 JSON 文本
  --out <pdf>           输出 PDF 路径

通用选项:
  --config <path>       agent.json 路径（默认 %APPDATA%\NewCosmos\agent.json）
  --server <url>        服务端 BaseUrl（覆盖配置）
  --register-token <t>  一次性注册令牌（覆盖配置；更推荐环境变量）
  --machine-name <name> 上报机器名（默认 Environment.MachineName）
  --heartbeat-seconds <n> 心跳间隔 5..3600（默认 30）
  --help                显示本帮助
  --version             显示版本

环境变量:
  NC_AGENT_CONFIG          等同于 --config
  NC_AGENT_SERVER_URL      等同于 --server
  NC_AGENT_REGISTER_TOKEN  等同于 --register-token（推荐部署时注入）

示例:
  NewCosmos.Agent.exe --server https://cosmos-static.sua17.cn:20000 --register
  NewCosmos.Agent.exe --heartbeat-once
  NewCosmos.Agent.exe --render --template C:\tpl\低保审核审批表.docx --data C:\tpl\data.json --out C:\out\result.pdf

说明:
  - 仓库/源码不包含任何密码或令牌；注册令牌只从环境变量/配置文件/命令行读取。
  - deviceKey 默认 DPAPI(CurrentUser) 加密；DPAPI 不可用时降级为当前用户 ACL 文件并记警告。
  - 任务队列认领（/api/agent/jobs/*）服务端 P0 尚未提供，当前只做注册与心跳。
""");
    }

    private static bool SetCommand(CliOptions options, string command, ref bool commandSet, out string? error)
    {
        error = null;
        if (commandSet && !string.Equals(options.Command, command, StringComparison.Ordinal))
        {
            error = $"命令冲突: 已指定 {options.Command}，又出现 {command}";
            return false;
        }

        options.Command = command;
        commandSet = true;
        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string? inlineValue,
        string optionName,
        out string? value,
        out string? error)
    {
        error = null;
        if (inlineValue != null)
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = null;
            error = $"{optionName} 缺少参数值";
            return false;
        }

        value = args[++index];
        return true;
    }
}
