using System.Text.Json;
using NewCosmos.Agent.Models;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 本机操作参数（payload.fields.args 解析结果）。
/// 服务端只保证 args 是 ≤4000 字符的 JSON 文本，业务字段由前端按 op 传入：
/// <c>{ networkId?, moonId?, cliPath?, tokenPath? }</c>。
/// </summary>
internal sealed class LocalOpArgs
{
    public string? NetworkId { get; init; }

    public string? MoonId { get; init; }

    /// <summary>ZeroTier CLI 搜索路径覆盖（分号分隔多路径；空则用内置默认）。</summary>
    public string? CliPath { get; init; }

    /// <summary>ZeroTier 管理令牌路径覆盖（空则用内置默认）。</summary>
    public string? TokenPath { get; init; }

    public static LocalOpArgs Parse(IReadOnlyDictionary<string, string> fields)
    {
        if (!fields.TryGetValue("args", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return new LocalOpArgs();
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new LocalOpArgs();
            }

            return new LocalOpArgs
            {
                NetworkId = ReadString(document.RootElement, "networkId"),
                MoonId = ReadString(document.RootElement, "moonId"),
                CliPath = ReadString(document.RootElement, "cliPath"),
                TokenPath = ReadString(document.RootElement, "tokenPath"),
            };
        }
        catch (JsonException ex)
        {
            throw new BusinessException("LOCAL_OP_ARGS_INVALID", "本机操作参数不是合法 JSON", ex);
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// 本机操作执行器（LocalOp）：按 <c>payload.fields.op</c> 分发到公文字体 / ZeroTier 具体动作。
/// <para>
/// 通道层（入队 → 认领 → payload 下发）由服务端 <c>LocalOpEndpoints</c> 承担，本类只负责执行与回传：
/// </para>
/// <list type="bullet">
/// <item>成功 → 返回 <c>"RESULT:" + JSON</c>，经 <c>CompleteAsync</c> 落到任务 <c>error_message</c>，
/// 前端轮询 <c>GET /api/print/jobs</c> 后按前缀解析（改动最小、不新增端点，取舍见 docs/20261127 文档）。</item>
/// <item>失败 → 抛 <see cref="BusinessException"/>，由 <c>JobWorker</c> 走现有 fail 回传
/// （错误摘要 ≤500 由 <c>JobWorker.SummarizeError</c> 处理）。</item>
/// </list>
/// <para>
/// 日志脱敏：只记任务号与 op，不记 args 原文（args 可能含本机路径/令牌路径），
/// 不记 ZeroTier 令牌内容。
/// </para>
/// </summary>
internal static class LocalOpExecutor
{
    /// <summary>结构化结果前缀（与前端 <c>localOps.ts</c> 的 RESULT 常量一致）。</summary>
    public const string ResultPrefix = "RESULT:";

    /// <summary>
    /// 结果总长上限：服务端 <c>error_message</c> 存 2000、Agent <c>CompleteAsync</c> 再截到 1000，
    /// 这里主动控制在 900 以内，保证 JSON 不被截断、前端可稳定解析。
    /// </summary>
    private const int MaxResultChars = 900;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string?> ExecuteAsync(
        AgentJob job,
        AgentJobPayload payload,
        ILoggerService logger,
        CancellationToken ct)
    {
        var fields = payload.Fields ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var op = fields.TryGetValue("op", out var rawOp) ? rawOp?.Trim() : null;
        if (string.IsNullOrWhiteSpace(op))
        {
            throw new BusinessException("LOCAL_OP_INVALID", "本机操作缺少 op 指令名");
        }

        var jobNo = string.IsNullOrWhiteSpace(job.JobNo) ? $"job-{job.Id}" : job.JobNo.Trim();
        // 日志脱敏：只记任务号与 op，不记 args 原文。
        logger.Info($"[LocalOp] 执行本机操作: No={jobNo}, Op={op}");

        var args = LocalOpArgs.Parse(fields);
        ct.ThrowIfCancellationRequested();

        switch (op)
        {
            case LocalOpConstants.OpFontCheck:
            {
                var check = LocalOpFontOps.Check(logger);
                logger.Info(
                    $"[LocalOp] 公文字体检查完成: No={jobNo}, DirExists={check.DirExists}, " +
                    $"Total={check.Total}, Missing={check.Missing.Count}");
                return BuildResult(op, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["dirExists"] = check.DirExists,
                    ["total"] = check.Total,
                    ["missing"] = check.Missing.Select(item => item.FileName).ToList(),
                });
            }

            case LocalOpConstants.OpFontInstall:
            {
                var check = LocalOpFontOps.Check(logger);
                if (check.Missing.Count == 0)
                {
                    // 目录不存在或已全部就位：按成功回传，前端提示"无需安装"。
                    return BuildResult(op, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["installed"] = 0,
                        ["dirExists"] = check.DirExists,
                    });
                }

                var (installed, failures) = LocalOpFontOps.Install(check.Missing, logger);
                if (failures.Count > 0)
                {
                    // 部分成功按失败回传：带 x/n 成功、m 失败 + 逐条原因（前端展示后可手动兜底安装）。
                    var detail = string.Join("；", failures);
                    throw new BusinessException(
                        ErrorCodes.FILE_WRITE_ERROR,
                        $"已安装 {installed}/{check.Missing.Count} 个公文字体，{failures.Count} 个失败：{Truncate(detail, 360)}");
                }

                return BuildResult(op, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["installed"] = installed,
                    ["total"] = check.Total,
                });
            }

            case LocalOpConstants.OpZeroTierStatus:
            {
                var status = await LocalOpZeroTierOps.GetStatusAsync(args, logger, ct).ConfigureAwait(false);
                logger.Info(
                    $"[LocalOp] ZeroTier 状态检测完成: No={jobNo}, Installed={status.Installed}, " +
                    $"Online={status.Online}, Networks={status.Networks.Count}, TokenDenied={status.TokenAccessDenied}");
                return BuildResult(op, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["installed"] = status.Installed,
                    ["nodeId"] = status.NodeId,
                    ["version"] = status.Version,
                    ["online"] = status.Online,
                    ["tokenAccessDenied"] = status.TokenAccessDenied,
                    ["networks"] = status.Networks,
                });
            }

            case LocalOpConstants.OpZeroTierInstall:
            {
                await LocalOpZeroTierOps.InstallAsync(args, logger, ct).ConfigureAwait(false);
                return BuildResult(op, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["installed"] = true,
                });
            }

            case LocalOpConstants.OpZeroTierJoin:
            {
                var networkId = LocalOpZeroTierOps.RequireNetworkId(args.NetworkId, "网络ID");
                var moonId = string.IsNullOrWhiteSpace(args.MoonId)
                    ? null
                    : LocalOpZeroTierOps.RequireNetworkId(args.MoonId, "Moon ID");
                await LocalOpZeroTierOps.JoinAsync(networkId, moonId, args, logger, ct).ConfigureAwait(false);
                return BuildResult(op, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["nwid"] = networkId,
                    ["joined"] = true,
                });
            }

            case LocalOpConstants.OpZeroTierLeave:
            {
                var networkId = LocalOpZeroTierOps.RequireNetworkId(args.NetworkId, "网络ID");
                await LocalOpZeroTierOps.LeaveAsync(networkId, args, logger, ct).ConfigureAwait(false);
                return BuildResult(op, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["nwid"] = networkId,
                    ["left"] = true,
                });
            }

            default:
                throw new BusinessException("LOCAL_OP_UNKNOWN", $"未知的本机操作指令: {op}");
        }
    }

    /// <summary>
    /// 组装成功回传：<c>RESULT:</c> + 单行 JSON。
    /// 长清单（字体缺失文件名、网络列表）逐条裁剪并记 <c>omitted</c>，保证 JSON 完整可解析；
    /// 极端情况下兜底截断（前端解析失败时按原文展示，不丢信息）。
    /// </summary>
    private static string BuildResult(string op, Dictionary<string, object?> data)
    {
        data["op"] = op;
        var json = JsonSerializer.Serialize(data, JsonOptions);

        if (json.Length > MaxResultChars && data.TryGetValue("missing", out var missingObj) && missingObj is List<string> missing)
        {
            var omitted = 0;
            while (missing.Count > 0 && json.Length > MaxResultChars)
            {
                missing.RemoveAt(missing.Count - 1);
                omitted++;
                json = JsonSerializer.Serialize(data, JsonOptions);
            }

            if (omitted > 0)
            {
                data["omitted"] = omitted;
                json = JsonSerializer.Serialize(data, JsonOptions);
            }
        }

        if (json.Length > MaxResultChars)
        {
            json = json[..MaxResultChars];
        }

        return ResultPrefix + json;
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }
}

/// <summary>
/// 本机操作能力探测（心跳 <c>capabilities</c> 上报用）。
/// <para>
/// 真实探测：font = 随包公文字体目录存在 + 用户字体目录可写；zerotier = zerotier-cli 存在。
/// 探测结果缓存 5 分钟 —— 心跳默认 30 秒一次，不能每轮都做目录/文件 IO 重活。
/// 探测异常不上报该能力（宁可少报，也不能把不可用能力报出去）。
/// </para>
/// </summary>
internal static class LocalOpCapabilityProbe
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly object Gate = new();

    private static DateTime _refreshAfterUtc = DateTime.MinValue;

    private static List<string> _cached = new();

    public static IReadOnlyList<string> Resolve(ILoggerService logger)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (now < _refreshAfterUtc)
            {
                return _cached;
            }

            var capabilities = new List<string>();

            try
            {
                if (LocalOpFontOps.ProbeCapability())
                {
                    capabilities.Add("font");
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"[LocalOp] font 能力探测失败，本次不上报: {ex.Message}");
            }

            try
            {
                if (LocalOpZeroTierOps.FindCli(null) is not null)
                {
                    capabilities.Add("zerotier");
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"[LocalOp] zerotier 能力探测失败，本次不上报: {ex.Message}");
            }

            _cached = capabilities;
            _refreshAfterUtc = now.Add(CacheTtl);
            logger.Info(
                $"[LocalOp] 能力探测完成: [{string.Join(",", capabilities)}]（缓存 {CacheTtl.TotalMinutes:F0} 分钟）");
            return _cached;
        }
    }
}
