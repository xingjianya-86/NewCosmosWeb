using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>Agent 设备注册/心跳与 printer 上报的持久化。</summary>
public sealed class AgentDeviceService(IDatabaseService database)
{
    public async Task<Result<long>> RegisterAsync(
        Guid deviceKey,
        string machineName,
        string agentVersion,
        IReadOnlyCollection<AgentPrinterReport> printers,
        CancellationToken ct = default)
    {
        var result = await database.ExecuteScalarAsync<long?>(
            @"INSERT INTO nc_sys_print_devices
                  (device_key, machine_name, agent_version, printers_json, last_seen_at, is_active, created_at, updated_at)
              VALUES ($1, $2, $3, $4::jsonb, now(), true, now(), now())
              RETURNING id",
            ct,
            deviceKey,
            machineName,
            agentVersion,
            AgentPrinterNormalizer.Serialize(printers));

        if (result.IsFailure)
        {
            return Result.Failure<long>(result.ErrorCode!, result.Message!);
        }

        return result.Value is { } id
            ? Result.Success(id)
            : Result.Failure<long>(ApiErrorCodes.DbError, "创建设备失败：未返回设备 ID");
    }

    public async Task<Result<int>> HeartbeatAsync(
        Guid deviceKey,
        string agentVersion,
        IReadOnlyCollection<AgentPrinterReport> printers,
        IReadOnlyCollection<string> capabilities,
        CancellationToken ct = default)
    {
        return await database.ExecuteNonQueryAsync(
            @"UPDATE nc_sys_print_devices
              SET agent_version = $1,
                  printers_json = $2::jsonb,
                  capabilities_json = $4::jsonb,
                  last_seen_at = now(),
                  updated_at = now()
              WHERE device_key = $3 AND is_active",
            ct,
            agentVersion,
            AgentPrinterNormalizer.Serialize(printers),
            deviceKey,
            AgentCapabilityNormalizer.Serialize(capabilities));
    }

    public async Task<Result<PrinterDeviceRow>> GetByIdAsync(long deviceId, CancellationToken ct = default)
    {
        return await database.QuerySingleAsync<PrinterDeviceRow>(
            @"SELECT id, device_key, machine_name, printers_json::text AS printers_json,
                     capabilities_json::text AS capabilities_json, last_seen_at
              FROM nc_sys_print_devices
              WHERE id = $1 AND is_active",
            ct,
            deviceId);
    }

    public async Task<Result<List<PrinterDeviceRow>>> ListAsync(
        double onlineWindowSeconds,
        CancellationToken ct = default)
    {
        return await database.QueryAsync<PrinterDeviceRow>(
            @"SELECT id, machine_name, printers_json::text AS printers_json,
                     capabilities_json::text AS capabilities_json, last_seen_at,
                     (last_seen_at IS NOT NULL
                      AND last_seen_at >= now() - make_interval(secs => $1)) AS online
              FROM nc_sys_print_devices
              WHERE is_active
              ORDER BY last_seen_at DESC NULLS LAST, id DESC
              LIMIT 200",
            ct,
            onlineWindowSeconds);
    }
}

/// <summary>Core 反射映射需要 public 可写属性与公共无参构造函数。</summary>
public sealed class PrinterDeviceRow
{
    public long Id { get; set; }
    public Guid DeviceKey { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string PrintersJson { get; set; } = "[]";

    /// <summary>本机操作能力码数组 JSON（nc_sys_print_devices.capabilities_json）。</summary>
    public string CapabilitiesJson { get; set; } = "[]";

    public DateTime? LastSeenAt { get; set; }
    public bool Online { get; set; }
}
