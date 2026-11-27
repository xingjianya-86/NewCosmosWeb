using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>ZeroTier 单个网络的状态（listnetworks 解析结果）。</summary>
internal sealed class ZeroTierNetworkInfo
{
    public string Nwid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>OK / ACCESS_DENIED / NOT_FOUND / REQUESTING_CONFIGURATION …</summary>
    public string Status { get; set; } = string.Empty;

    public List<string> AssignedAddresses { get; set; } = new();
}

/// <summary>ZeroTier 运行状态（info + listnetworks 解析结果）。</summary>
internal sealed class ZeroTierStatusInfo
{
    public bool Installed { get; set; }

    public string NodeId { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public bool Online { get; set; }

    /// <summary>CLI 无法读取管理令牌（authtoken.secret）→ 需要在"接入"时提权授权一次。</summary>
    public bool TokenAccessDenied { get; set; }

    public List<ZeroTierNetworkInfo> Networks { get; set; } = new();
}

/// <summary>
/// ZeroTier 本机操作（LocalOp：<c>zerotier.status</c> / <c>zerotier.install</c> /
/// <c>zerotier.join</c> / <c>zerotier.leave</c>）。
/// <para>
/// 移植参照（只读参考，Agent 不能引用 legacy/桌面项目，故复制逻辑并适配）：
/// <c>legacy/NewCosmos.Desktop/Services/System/NetworkAccessService.cs</c>。
/// 适配点：配置读取（IConfigService / network.ini）改为 op args 覆盖 + 内置默认值
/// （Server 的 ServerConfigService 是空壳，CLI/token 路径走前端 args 或默认）；
/// Result 返回值改为 BusinessException；所有 zerotier-cli 调用统一 cmd.exe /c 无窗口重定向 + 20 秒超时。
/// </para>
/// <para>
/// 日志脱敏：只记 op/网络ID/退出码，不记令牌文件内容、不记 args 全文。
/// </para>
/// </summary>
internal static class LocalOpZeroTierOps
{
    /// <summary>ZeroTier CLI 搜索路径（未被 args.cliPath 覆盖时的内置默认）。</summary>
    private static readonly string[] DefaultCliPaths =
    {
        @"C:\Program Files (x86)\ZeroTier\One\zerotier-cli.bat",
        @"C:\Program Files\ZeroTier\One\zerotier-cli.bat",
    };

    /// <summary>ZeroTier 本地管理令牌默认路径（未被 args.tokenPath 覆盖时）。</summary>
    private const string DefaultTokenPath = @"C:\ProgramData\ZeroTier\One\authtoken.secret";

    /// <summary>zerotier-cli 单次执行超时：超时 kill 并返回明确错误。</summary>
    private const int CliTimeoutSeconds = 20;

    /// <summary>提权执行（msiexec / icacls）超时：静默安装本身可达数十秒，不适用 CLI 的 20 秒上限。</summary>
    private const int ElevatedTimeoutSeconds = 180;

    /// <summary>网络 ID / Moon ID：ZeroTier 固定 16 位十六进制（同时充当 cmd 行注入防护）。</summary>
    private static readonly Regex NetworkIdPattern = new("^[0-9a-fA-F]{16}$", RegexOptions.CultureInvariant);

    /// <summary>定位 zerotier-cli：args.cliPath（分号分隔多路径）优先，否则用内置默认。</summary>
    public static string? FindCli(string? overridePaths)
    {
        var candidates = string.IsNullOrWhiteSpace(overridePaths)
            ? DefaultCliPaths
            : overridePaths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>校验网络 ID（16 位十六进制）；同时挡掉引号/连接符等 cmd 注入字符。</summary>
    public static string RequireNetworkId(string? networkId, string fieldName)
    {
        var trimmed = networkId?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new BusinessException(ErrorCodes.VALIDATION_FAILED, $"{fieldName}不能为空");
        }

        if (!NetworkIdPattern.IsMatch(trimmed))
        {
            throw new BusinessException(ErrorCodes.VALIDATION_FAILED, $"{fieldName}格式无效（应为 16 位十六进制）");
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>校验本机路径参数（CLI / 令牌）：只做字符校验（引号/换行会破坏 cmd 命令行），存在性由调用方判定。</summary>
    private static string? ValidateLocalPath(string? path, string fieldName)
    {
        var trimmed = path?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
        {
            throw new BusinessException(ErrorCodes.VALIDATION_FAILED, $"{fieldName}包含非法字符");
        }

        return trimmed;
    }

    /// <summary>检测 ZeroTier：info（节点/版本/在线态）+ listnetworks（各网络状态与分配 IP）。</summary>
    public static async Task<ZeroTierStatusInfo> GetStatusAsync(LocalOpArgs args, ILoggerService logger, CancellationToken ct)
    {
        var status = new ZeroTierStatusInfo();
        var cli = FindCli(ValidateLocalPath(args.CliPath, "CLI 路径"));
        if (cli is null)
        {
            return status; // Installed=false：前端展示"未检测到 ZeroTier"
        }

        status.Installed = true;
        var tokenPath = ResolveTokenPath(args);

        var info = await RunCliAsync(cli, "info", ct).ConfigureAwait(false);
        if (info.ExitCode == 0)
        {
            // 200 info <nodeid> <version> <status>
            var parts = info.StdOut.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                status.NodeId = parts[2];
                status.Version = parts[3];
                status.Online = string.Equals(parts[^1], "ONLINE", StringComparison.OrdinalIgnoreCase);
            }
        }
        else if (IsTokenPermissionError(info))
        {
            status.TokenAccessDenied = true;
            logger.Warn("[LocalOp] ZeroTier CLI 无权读取管理令牌（需在“接入”时提权授权一次）");
        }

        var list = await RunCliAsync(cli, "listnetworks", ct).ConfigureAwait(false);
        if (list.ExitCode == 0)
        {
            status.Networks = ParseNetworks(list.StdOut);
        }
        else if (IsTokenPermissionError(list))
        {
            status.TokenAccessDenied = true;
        }

        // 令牌文件存在但不可读时，即使 CLI 输出没命中错误文案也标记为待授权，便于前端给出引导。
        if (!status.TokenAccessDenied && !IsTokenReadable(tokenPath))
        {
            status.TokenAccessDenied = true;
        }

        return status;
    }

    /// <summary>
    /// 安装随包分发的 ZeroTier（msiexec 静默安装，Verb=runas 提权）。
    /// 已安装时直接返回；缺少离线安装包时明确报错（该 msi 属"未随仓库分发"资产）。
    /// </summary>
    public static async Task InstallAsync(LocalOpArgs args, ILoggerService logger, CancellationToken ct)
    {
        if (FindCli(ValidateLocalPath(args.CliPath, "CLI 路径")) is not null)
        {
            logger.Info("[LocalOp] ZeroTier 已安装，跳过安装步骤");
            return;
        }

        var msi = FindBundledMsi();
        if (msi is null)
        {
            throw new BusinessException(
                ErrorCodes.FILE_NOT_FOUND,
                "缺少离线安装包，请手动安装（随包目录 Resources\\ZeroTier\\*.msi 不存在）");
        }

        logger.Info($"[LocalOp] 开始安装 ZeroTier: {Path.GetFileName(msi)}");
        var exitCode = await RunElevatedAsync(
                "msiexec.exe",
                $"/i \"{msi}\" /qn /norestart",
                ct)
            .ConfigureAwait(false);

        if (FindCli(ValidateLocalPath(args.CliPath, "CLI 路径")) is null)
        {
            throw new BusinessException(
                ErrorCodes.UNKNOWN_ERROR,
                $"ZeroTier 安装未完成（msiexec 退出码 {exitCode}），请重试或手动安装");
        }

        // 安装后顺带授予当前用户管理令牌读权限（令牌尚未生成时，接入会再次按需授权）。
        var tokenPath = ResolveTokenPath(args);
        if (File.Exists(tokenPath) && !IsTokenReadable(tokenPath))
        {
            try
            {
                await EnsureCliAccessAsync(tokenPath, logger, ct).ConfigureAwait(false);
            }
            catch (BusinessException ex)
            {
                logger.Warn($"[LocalOp] ZeroTier 安装后授权未完成（接入时将重试）: {ex.ErrorCode} {ex.Message}");
            }
        }

        logger.Info("[LocalOp] ZeroTier 安装完成");
    }

    /// <summary>加入网络；moonId 非空时同时 orbit（私有化加速，失败仅告警不影响接入）。</summary>
    public static async Task JoinAsync(string networkId, string? moonId, LocalOpArgs args, ILoggerService logger, CancellationToken ct)
    {
        var cli = FindCli(ValidateLocalPath(args.CliPath, "CLI 路径"))
            ?? throw new BusinessException(ErrorCodes.NOT_FOUND, "未检测到 ZeroTier，请先安装");

        var join = await RunCliAsync(cli, $"join {networkId}", ct).ConfigureAwait(false);
        if (IsTokenPermissionError(join))
        {
            // 管理令牌仅管理员可读：提权一次授予当前用户读权限后重试（用户动作触发，可接受 UAC）
            await EnsureCliAccessAsync(ResolveTokenPath(args), logger, ct).ConfigureAwait(false);
            join = await RunCliAsync(cli, $"join {networkId}", ct).ConfigureAwait(false);
        }

        if (join.TimedOut)
        {
            throw new BusinessException(ErrorCodes.UNKNOWN_ERROR, $"加入网络超时（>{CliTimeoutSeconds} 秒），已终止执行");
        }

        if (join.ExitCode != 0 || !join.StdOut.Contains("200", StringComparison.Ordinal))
        {
            logger.Warn($"[LocalOp] ZeroTier join 失败: exit={join.ExitCode}, out={FirstLine(join.StdOut)}");
            var hint = IsTokenPermissionError(join)
                ? "（管理权限未授权，请重试接入并同意管理员授权）"
                : string.Empty;
            throw new BusinessException(
                ErrorCodes.UNKNOWN_ERROR,
                $"加入网络失败：{FirstLine(join.StdErr, join.StdOut)}{hint}");
        }

        if (!string.IsNullOrWhiteSpace(moonId))
        {
            var orbit = await RunCliAsync(cli, $"orbit {moonId} {moonId}", ct).ConfigureAwait(false);
            if (orbit.ExitCode != 0)
            {
                logger.Warn($"[LocalOp] ZeroTier orbit 失败（不影响接入）: exit={orbit.ExitCode}");
            }
        }

        logger.Info($"[LocalOp] ZeroTier 加入网络: {networkId}");
    }

    /// <summary>退出网络（二次语义由前端确认框负责）。</summary>
    public static async Task LeaveAsync(string networkId, LocalOpArgs args, ILoggerService logger, CancellationToken ct)
    {
        var cli = FindCli(ValidateLocalPath(args.CliPath, "CLI 路径"))
            ?? throw new BusinessException(ErrorCodes.NOT_FOUND, "未检测到 ZeroTier");

        var leave = await RunCliAsync(cli, $"leave {networkId}", ct).ConfigureAwait(false);
        if (IsTokenPermissionError(leave))
        {
            await EnsureCliAccessAsync(ResolveTokenPath(args), logger, ct).ConfigureAwait(false);
            leave = await RunCliAsync(cli, $"leave {networkId}", ct).ConfigureAwait(false);
        }

        if (leave.TimedOut)
        {
            throw new BusinessException(ErrorCodes.UNKNOWN_ERROR, $"退出网络超时（>{CliTimeoutSeconds} 秒），已终止执行");
        }

        if (leave.ExitCode != 0)
        {
            throw new BusinessException(
                ErrorCodes.UNKNOWN_ERROR,
                $"退出网络失败：{FirstLine(leave.StdErr, leave.StdOut)}");
        }

        logger.Info($"[LocalOp] ZeroTier 退出网络: {networkId}");
    }

    /// <summary>
    /// 确保本机 CLI 可读取管理令牌（authtoken.secret，默认仅管理员可读）：
    /// 需要时以管理员提权（UAC）授予当前用户读权限；已可读时直接成功。
    /// 用户取消 UAC → BusinessException(CANCELLED)，前端按回传文案展示。
    /// </summary>
    public static async Task EnsureCliAccessAsync(string tokenPath, ILoggerService logger, CancellationToken ct)
    {
        if (!File.Exists(tokenPath))
        {
            throw new BusinessException(
                ErrorCodes.NOT_FOUND,
                "未找到 ZeroTier 管理令牌文件，请先安装并启动 ZeroTier");
        }

        if (IsTokenReadable(tokenPath))
        {
            return;
        }

        // 提权（UAC）为当前用户授予令牌读权限；输出重定向到临时文件（ShellExecute 无法直接捕获流）
        var user = WindowsIdentity.GetCurrent().Name;
        if (user.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
        {
            throw new BusinessException(ErrorCodes.UNKNOWN_ERROR, "当前用户名包含非法字符，无法发起授权");
        }

        var tempOut = Path.Combine(Path.GetTempPath(), $"newcosmos_zt_acl_{Guid.NewGuid():N}.txt");
        var arguments = $"/c \"\"icacls\" \"{tokenPath}\" /grant \"{user}:R\" > \"{tempOut}\" 2>&1\"";

        try
        {
            var exitCode = await RunElevatedAsync("cmd.exe", arguments, ct).ConfigureAwait(false);

            var output = string.Empty;
            try
            {
                output = File.Exists(tempOut) ? await File.ReadAllTextAsync(tempOut, ct).ConfigureAwait(false) : string.Empty;
            }
            catch (Exception ex)
            {
                // 读不到授权输出不影响结论（下面按令牌是否可读判定）；icacls 输出只含路径/账号，不含令牌内容
                logger.Debug($"[LocalOp] 读取 icacls 输出失败: {ex.Message}");
            }

            if (!IsTokenReadable(tokenPath))
            {
                logger.Warn($"[LocalOp] ZeroTier 令牌授权未生效: exit={exitCode}, out={FirstLine(output)}");
                throw new BusinessException(
                    ErrorCodes.ZEROTIER_TOKEN_DENIED,
                    "ZeroTier 接入权限授权失败（可手动以管理员身份执行 icacls 授权后重试）");
            }

            // 只记"已授权"这一事实与用户，不写令牌文件内容。
            logger.Info($"[LocalOp] 已授予 ZeroTier 管理令牌读权限: User={user}, Exit={exitCode}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempOut))
                {
                    File.Delete(tempOut);
                }
            }
            catch
            {
                // 临时文件删不掉无害
            }
        }
    }

    // ── 内部工具 ──

    private static string ResolveTokenPath(LocalOpArgs args)
    {
        var overridePath = ValidateLocalPath(args.TokenPath, "令牌路径");
        return string.IsNullOrWhiteSpace(overridePath) ? DefaultTokenPath : overridePath;
    }

    private static string? FindBundledMsi()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "ZeroTier");
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return Directory.EnumerateFiles(dir, "*.msi").FirstOrDefault();
    }

    /// <summary>当前用户是否可读取管理令牌</summary>
    private static bool IsTokenReadable(string tokenPath)
    {
        try
        {
            using var stream = File.Open(tokenPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.Length > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalOp] 管理令牌文件不可读: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>CLI 输出是否命中"管理令牌不可读"错误</summary>
    private static bool IsTokenPermissionError(CliResult result)
    {
        var text = result.StdErr + " " + result.StdOut;
        return text.Contains("authtoken", StringComparison.OrdinalIgnoreCase)
               || text.Contains("try again as root", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

    /// <summary>
    /// 统一执行 zerotier-cli：<c>cmd.exe /c</c> 无窗口 + 重定向 + 20 秒超时。
    /// 超时 kill（整棵进程树，含 .bat 的子进程）并返回 TimedOut=true，由调用方转成明确错误。
    /// </summary>
    private static async Task<CliResult> RunCliAsync(string cli, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{cli}\" {arguments}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return new CliResult(-1, string.Empty, "无法启动 zerotier-cli", false);
        }

        // 读取不挂 ct：进程被 kill 后管道关闭，读取自然结束，避免取消时读到半截输出。
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(CliTimeoutSeconds));
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 已退出 / 无权限：超时结论不依赖 kill 是否成功
            }

            // 读取任务不 await：进程已 kill、管道随后关闭，超时结论不依赖输出内容。
            // Agent 停止（外层 ct）：向上抛，由 JobWorker 回写"Agent 停止，任务中断"
            ct.ThrowIfCancellationRequested();
            return new CliResult(-1, string.Empty, $"zerotier-cli 执行超时（>{CliTimeoutSeconds} 秒），已终止", true);
        }

        return new CliResult(
            proc.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false),
            false);
    }

    /// <summary>
    /// 提权执行（Verb=runas）：安装 MSI / icacls 授权用。
    /// 无重定向（ShellExecute 不支持），需要输出的场景由调用方重定向到临时文件。
    /// 用户取消 UAC（Win32 1223）→ BusinessException(CANCELLED)。
    /// </summary>
    private static async Task<int> RunElevatedAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new BusinessException(ErrorCodes.UNKNOWN_ERROR, "无法启动提权程序");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new BusinessException(ErrorCodes.CANCELLED, "已取消管理员授权，操作未完成");
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BusinessException(ErrorCodes.UNKNOWN_ERROR, $"无法启动提权程序：{ex.Message}");
        }

        using (proc)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(ElevatedTimeoutSeconds));
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // 已退出 / 无权限
                }

                ct.ThrowIfCancellationRequested();
                throw new BusinessException(
                    ErrorCodes.UNKNOWN_ERROR,
                    $"提权操作超时（>{ElevatedTimeoutSeconds} 秒），已终止执行");
            }

            return proc.ExitCode;
        }
    }

    private static List<ZeroTierNetworkInfo> ParseNetworks(string output)
    {
        var list = new List<ZeroTierNetworkInfo>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // 200 listnetworks <nwid> <name> <mac> <status> <type> <dev> [ips...]
            if (p.Length < 8 || p[0] != "200" || p[1] != "listnetworks")
            {
                continue;
            }

            var state = new ZeroTierNetworkInfo
            {
                Nwid = p[2],
                Name = p[3],
                Status = p[5],
            };
            if (p.Length > 8)
            {
                state.AssignedAddresses = p[8].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            list.Add(state);
        }

        return list;
    }

    private static string FirstLine(params string[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return "未知错误";
    }
}
