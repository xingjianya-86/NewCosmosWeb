using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Server.Services;

/// <summary>
/// 打印机按账户绑定。所有查询都带 user_id，服务端不接受前端传入的 user_id/org_id。
/// </summary>
public sealed class PrinterBindingService(
    IDatabaseService database,
    IUserSessionService sessions,
    AgentDeviceService devices,
    ILoggerService logger)
{
    private const string BindingSelect = @"SELECT b.id,
                                                  b.device_id,
                                                  d.machine_name AS device_name,
                                                  b.printer_name,
                                                  b.is_default,
                                                  (d.last_seen_at IS NOT NULL
                                                   AND d.last_seen_at >= now() - make_interval(secs => $2::double precision)) AS online,
                                                  d.last_seen_at
                                           FROM nc_sys_printer_bindings b
                                           INNER JOIN nc_sys_print_devices d ON d.id = b.device_id
                                           WHERE b.user_id = $1";

    public async Task<Result<List<PrinterBindingDto>>> ListAsync(int userId, CancellationToken ct = default)
    {
        var policy = await sessions.GetPolicyAsync(ct);
        var result = await database.QueryAsync<PrinterBindingDto>(
            BindingSelect + " ORDER BY b.is_default DESC, b.created_at ASC, b.id ASC",
            ct,
            userId,
            policy.OnlineWindowSeconds);

        if (result.IsFailure)
        {
            return Result.Failure<List<PrinterBindingDto>>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value ?? []);
    }

    /// <summary>可选设备列表：只给公开 ID/机器名/打印机名，不暴露 deviceKey。</summary>
    public async Task<Result<List<PrinterDeviceDto>>> ListDevicesAsync(CancellationToken ct = default)
    {
        var policy = await sessions.GetPolicyAsync(ct);
        var result = await devices.ListAsync(policy.OnlineWindowSeconds, ct);
        if (result.IsFailure)
        {
            return Result.Failure<List<PrinterDeviceDto>>(result.ErrorCode!, result.Message!);
        }

        var list = new List<PrinterDeviceDto>();
        foreach (var device in result.Value ?? [])
        {
            if (!AgentPrinterNormalizer.TryReadNames(device.PrintersJson, out var names))
            {
                logger.Warn($"设备上报的 printers_json 无法解析: DeviceId={device.Id}");
                names = [];
            }

            list.Add(new PrinterDeviceDto(
                device.Id,
                device.MachineName,
                device.Online,
                device.LastSeenAt,
                names));
        }

        return Result.Success(list);
    }

    public async Task<Result<PrinterBindingDto>> BindPrinterAsync(
        int userId,
        BindPrinterRequest request,
        CancellationToken ct = default)
    {
        var requestedPrinterName = request.PrinterName?.Trim();
        if (string.IsNullOrEmpty(requestedPrinterName))
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.ValidationError, "printerName 不能为空");
        }

        if (requestedPrinterName.Length > AgentPrinterNormalizer.MaxNameLength)
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.ValidationError,
                $"printerName 不能超过 {AgentPrinterNormalizer.MaxNameLength} 个字符");
        }

        if (request.DeviceId is not > 0)
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.ValidationError, "deviceId 必须提供");
        }

        var requestedDeviceId = request.DeviceId.Value;

        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        var lockResult = await AcquireUserLockAsync(userId, ct);
        if (lockResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(lockResult.ErrorCode!, lockResult.Message!);
        }

        var deviceResult = await devices.GetByIdAsync(requestedDeviceId, ct);
        if (deviceResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(deviceResult.ErrorCode!, deviceResult.Message!);
        }

        var device = deviceResult.Value;
        if (device is null)
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.PrinterDeviceNotFound, "Agent 设备不存在");
        }

        if (device.LastSeenAt is null)
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.PrinterDeviceNotFound, "Agent 设备尚未上线");
        }

        if (!AgentPrinterNormalizer.TryReadNames(device.PrintersJson, out var reportedNames))
        {
            logger.Warn($"设备上报的 printers_json 无法解析: DeviceId={device.Id}");
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.PrinterNameNotReported,
                "设备上报的打印机列表无效，请等待 Agent 心跳后重试");
        }

        var printerName = reportedNames.FirstOrDefault(name =>
            string.Equals(name, requestedPrinterName, StringComparison.OrdinalIgnoreCase));
        if (printerName is null)
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.PrinterNameNotReported,
                "打印机不在 Agent 上报列表中");
        }

        var existingResult = await database.QuerySingleAsync<BindingRow>(
            @"SELECT id, is_default
              FROM nc_sys_printer_bindings
              WHERE user_id = $1 AND device_id = $2 AND printer_name = $3",
            ct,
            userId,
            device.Id,
            printerName);

        if (existingResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(existingResult.ErrorCode!, existingResult.Message!);
        }

        var existing = existingResult.Value;

        var countResult = await database.ExecuteScalarAsync<long?>(
            "SELECT COUNT(*) FROM nc_sys_printer_bindings WHERE user_id = $1",
            ct,
            userId);
        if (countResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(countResult.ErrorCode!, countResult.Message!);
        }

        var existingCount = countResult.Value ?? 0;
        var makeDefault = request.IsDefault == true
                          || existing?.IsDefault == true
                          || (existing is null && existingCount == 0);

        if (makeDefault)
        {
            var clearResult = await ClearDefaultAsync(userId, ct);
            if (clearResult.IsFailure)
            {
                return Result.Failure<PrinterBindingDto>(clearResult.ErrorCode!, clearResult.Message!);
            }
        }

        long bindingId;
        if (existing is null)
        {
            var insertResult = await database.ExecuteScalarAsync<long?>(
                @"INSERT INTO nc_sys_printer_bindings
                      (user_id, device_id, printer_name, is_default, created_at, updated_at)
                  VALUES ($1, $2, $3, $4, now(), now())
                  RETURNING id",
                ct,
                userId,
                device.Id,
                printerName,
                makeDefault);

            if (insertResult.IsFailure)
            {
                return Result.Failure<PrinterBindingDto>(insertResult.ErrorCode!, insertResult.Message!);
            }

            if (insertResult.Value is not { } insertedId)
            {
                return Result.Failure<PrinterBindingDto>(ApiErrorCodes.DbError, "创建打印机绑定失败");
            }

            bindingId = insertedId;
        }
        else
        {
            var updateResult = await database.ExecuteNonQueryAsync(
                @"UPDATE nc_sys_printer_bindings
                  SET is_default = $1, updated_at = now()
                  WHERE id = $2 AND user_id = $3",
                ct,
                makeDefault,
                existing.Id,
                userId);

            if (updateResult.IsFailure)
            {
                return Result.Failure<PrinterBindingDto>(updateResult.ErrorCode!, updateResult.Message!);
            }

            bindingId = existing.Id;
        }

        var bindingResult = await GetBindingAsync(userId, bindingId, ct);
        if (bindingResult.IsFailure)
        {
            return bindingResult;
        }

        await transaction.CommitAsync(ct);
        return bindingResult;
    }

    public async Task<Result> DeleteAsync(int userId, long bindingId, CancellationToken ct = default)
    {
        if (bindingId <= 0)
        {
            return Result.Failure(ApiErrorCodes.ValidationError, "绑定 ID 无效");
        }

        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        var lockResult = await AcquireUserLockAsync(userId, ct);
        if (lockResult.IsFailure)
        {
            return lockResult;
        }

        var bindingResult = await database.QuerySingleAsync<BindingRow>(
            "SELECT id, is_default FROM nc_sys_printer_bindings WHERE id = $1 AND user_id = $2",
            ct,
            bindingId,
            userId);

        if (bindingResult.IsFailure)
        {
            return Result.Failure(bindingResult.ErrorCode!, bindingResult.Message!);
        }

        var binding = bindingResult.Value;
        if (binding is null)
        {
            return Result.Failure(ApiErrorCodes.PrinterNotFound, "打印机绑定不存在");
        }

        var deleteResult = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_sys_printer_bindings WHERE id = $1 AND user_id = $2",
            ct,
            bindingId,
            userId);

        if (deleteResult.IsFailure)
        {
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);
        }

        if (binding.IsDefault)
        {
            // 删除默认后把最早一条设为默认（同账号默认唯一由部分唯一索引兜底）。
            var promoteResult = await database.ExecuteNonQueryAsync(
                @"UPDATE nc_sys_printer_bindings
                  SET is_default = TRUE, updated_at = now()
                  WHERE id = (SELECT id
                              FROM nc_sys_printer_bindings
                              WHERE user_id = $1
                              ORDER BY created_at ASC, id ASC
                              LIMIT 1)",
                ct,
                userId);

            if (promoteResult.IsFailure)
            {
                return Result.Failure(promoteResult.ErrorCode!, promoteResult.Message!);
            }
        }

        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PrinterBindingDto>> SetDefaultAsync(
        int userId,
        long bindingId,
        CancellationToken ct = default)
    {
        if (bindingId <= 0)
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.ValidationError, "绑定 ID 无效");
        }

        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        var lockResult = await AcquireUserLockAsync(userId, ct);
        if (lockResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(lockResult.ErrorCode!, lockResult.Message!);
        }

        var existsResult = await database.QuerySingleAsync<BindingRow>(
            "SELECT id, is_default FROM nc_sys_printer_bindings WHERE id = $1 AND user_id = $2",
            ct,
            bindingId,
            userId);

        if (existsResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(existsResult.ErrorCode!, existsResult.Message!);
        }

        if (existsResult.Value is null)
        {
            return Result.Failure<PrinterBindingDto>(ApiErrorCodes.PrinterNotFound, "打印机绑定不存在");
        }

        var clearResult = await ClearDefaultAsync(userId, ct);
        if (clearResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(clearResult.ErrorCode!, clearResult.Message!);
        }

        var updateResult = await database.ExecuteNonQueryAsync(
            @"UPDATE nc_sys_printer_bindings
              SET is_default = TRUE, updated_at = now()
              WHERE id = $1 AND user_id = $2",
            ct,
            bindingId,
            userId);

        if (updateResult.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(updateResult.ErrorCode!, updateResult.Message!);
        }

        var bindingResult = await GetBindingAsync(userId, bindingId, ct);
        if (bindingResult.IsFailure)
        {
            return bindingResult;
        }

        await transaction.CommitAsync(ct);
        return bindingResult;
    }

    private async Task<Result<PrinterBindingDto>> GetBindingAsync(
        int userId, long bindingId, CancellationToken ct)
    {
        var result = await database.QuerySingleAsync<PrinterBindingDto>(
            BindingSelect + " AND b.id = $3",
            ct,
            userId,
            // 在线窗口与列表一致；这里只用于输出 online 标志。
            (await sessions.GetPolicyAsync(ct)).OnlineWindowSeconds,
            bindingId);

        if (result.IsFailure)
        {
            return Result.Failure<PrinterBindingDto>(result.ErrorCode!, result.Message!);
        }

        return result.Value is null
            ? Result.Failure<PrinterBindingDto>(ApiErrorCodes.PrinterNotFound, "打印机绑定不存在")
            : Result.Success(result.Value);
    }

    private Task<Result<int>> ClearDefaultAsync(int userId, CancellationToken ct)
        => database.ExecuteNonQueryAsync(
            @"UPDATE nc_sys_printer_bindings
              SET is_default = FALSE, updated_at = now()
              WHERE user_id = $1 AND is_default",
            ct,
            userId);

    private Task<Result<int>> AcquireUserLockAsync(int userId, CancellationToken ct)
        => database.ExecuteNonQueryAsync(
            "SELECT pg_advisory_xact_lock($1::bigint)",
            ct,
            // 与 Core 会话服务（直接使用 userId）错开命名空间，避免登录/绑定互相等待。
            700_000_000L + userId);

    /// <summary>绑定行的最小字段。</summary>
    public sealed class BindingRow
    {
        public long Id { get; set; }
        public bool IsDefault { get; set; }
    }
}
