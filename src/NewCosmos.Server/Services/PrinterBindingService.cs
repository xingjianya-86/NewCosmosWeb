using System.Globalization;
using System.Security.Cryptography;
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

    // ───────────────────────── 绑定码（Agent 按码绑定） ─────────────────────────

    /// <summary>绑定码长度固定 6 位数字。</summary>
    private const int BindCodeLength = 6;

    /// <summary>绑定码有效期（分钟）；与写入 SQL 的 interval 保持一致。</summary>
    private const int BindCodeLifetimeMinutes = 5;

    /// <summary>
    /// 生成一次性绑定码（登录用户调用）。同一账号旧的未使用码立即作废，避免多码并存。
    /// </summary>
    public async Task<Result<PrinterBindCodeResponse>> CreateBindCodeAsync(
        int userId,
        CancellationToken ct = default)
    {
        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        var lockResult = await AcquireUserLockAsync(userId, ct);
        if (lockResult.IsFailure)
        {
            return Result.Failure<PrinterBindCodeResponse>(lockResult.ErrorCode!, lockResult.Message!);
        }

        var invalidateResult = await database.ExecuteNonQueryAsync(
            @"UPDATE nc_sys_printer_bind_codes
              SET used_at = now()
              WHERE user_id = $1 AND used_at IS NULL",
            ct,
            userId);
        if (invalidateResult.IsFailure)
        {
            return Result.Failure<PrinterBindCodeResponse>(
                invalidateResult.ErrorCode!, invalidateResult.Message!);
        }

        // 极小概率撞上其他账号仍有效的码：生成时避让，避免按码绑到别人账号。
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = RandomNumberGenerator
                .GetInt32(0, 1_000_000)
                .ToString("D6", CultureInfo.InvariantCulture);

            var activeResult = await database.ExecuteScalarAsync<long?>(
                @"SELECT COUNT(*)
                  FROM nc_sys_printer_bind_codes
                  WHERE code = $1 AND used_at IS NULL AND expires_at > now()",
                ct,
                code);
            if (activeResult.IsFailure)
            {
                return Result.Failure<PrinterBindCodeResponse>(
                    activeResult.ErrorCode!, activeResult.Message!);
            }

            if (activeResult.Value is > 0)
            {
                continue;
            }

            var insertResult = await database.ExecuteScalarAsync<DateTime?>(
                @"INSERT INTO nc_sys_printer_bind_codes (code, user_id, expires_at, created_at)
                  VALUES ($1, $2, now() + make_interval(mins => $3), now())
                  RETURNING expires_at",
                ct,
                code,
                userId,
                BindCodeLifetimeMinutes);
            if (insertResult.IsFailure)
            {
                return Result.Failure<PrinterBindCodeResponse>(
                    insertResult.ErrorCode!, insertResult.Message!);
            }

            if (insertResult.Value is not { } expiresAt)
            {
                return Result.Failure<PrinterBindCodeResponse>(
                    ApiErrorCodes.DbError, "生成绑定码失败：未返回过期时间");
            }

            await transaction.CommitAsync(ct);
            logger.Info($"打印机绑定码已生成: UserId={userId}, ExpiresAt={expiresAt:O}");
            return Result.Success(new PrinterBindCodeResponse(code, expiresAt));
        }

        return Result.Failure<PrinterBindCodeResponse>(ApiErrorCodes.DbError, "生成绑定码失败，请重试");
    }

    /// <summary>
    /// Agent 按绑定码绑定打印机：设备密钥校验 + 绑定码兑换 + 绑定 upsert + 码置为已使用，同一事务。
    /// 重复绑定同一打印机做 upsert，不报错。
    /// </summary>
    public async Task<Result<PrinterBindingsResponse>> BindByCodeAsync(
        Guid deviceKey,
        BindPrinterByCodeRequest request,
        CancellationToken ct = default)
    {
        var code = request.Code?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length != BindCodeLength || !code.All(char.IsAsciiDigit))
        {
            return Result.Failure<PrinterBindingsResponse>(
                ApiErrorCodes.ValidationError, "绑定码必须是 6 位数字");
        }

        if (!AgentPrinterNormalizer.TryNormalize(request.Printers, out var requestedPrinters, out var printerError))
        {
            return Result.Failure<PrinterBindingsResponse>(ApiErrorCodes.ValidationError, printerError!);
        }

        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        var deviceResult = await database.QuerySingleAsync<PrinterDeviceRow>(
            @"SELECT id, device_key, machine_name, printers_json::text AS printers_json, last_seen_at
              FROM nc_sys_print_devices
              WHERE device_key = $1",
            ct,
            deviceKey);
        if (deviceResult.IsFailure)
        {
            return Result.Failure<PrinterBindingsResponse>(
                deviceResult.ErrorCode!, deviceResult.Message!);
        }

        var device = deviceResult.Value;
        if (device is null)
        {
            return Result.Failure<PrinterBindingsResponse>(
                ApiErrorCodes.AuthRequired, "设备不存在或已失效");
        }

        // FOR UPDATE：同一个码只允许一个 Agent 成功兑换。
        var codeResult = await database.QuerySingleAsync<BindCodeRow>(
            @"SELECT id, user_id, expires_at, used_at, (expires_at > now()) AS not_expired
              FROM nc_sys_printer_bind_codes
              WHERE code = $1 AND used_at IS NULL
              ORDER BY id DESC
              LIMIT 1
              FOR UPDATE",
            ct,
            code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<PrinterBindingsResponse>(
                codeResult.ErrorCode!, codeResult.Message!);
        }

        var bindCode = codeResult.Value;
        if (bindCode is null)
        {
            return Result.Failure<PrinterBindingsResponse>(
                ApiErrorCodes.NotFound, "绑定码无效或已被使用，请在网页端重新生成");
        }

        if (!bindCode.NotExpired)
        {
            return Result.Failure<PrinterBindingsResponse>(
                ApiErrorCodes.ValidationError, "绑定码已过期，请在网页端重新生成");
        }

        // 没有显式传 printers 时，使用设备最近一次心跳/注册上报的列表。
        List<AgentPrinterReport> printers;
        if (requestedPrinters.Count > 0)
        {
            printers = requestedPrinters;
        }
        else if (TryReadReportedPrinters(device.PrintersJson, out var reported))
        {
            printers = reported;
        }
        else
        {
            logger.Warn($"设备上报的 printers_json 无法解析: DeviceId={device.Id}");
            return Result.Failure<PrinterBindingsResponse>(
                ApiErrorCodes.ValidationError, "设备上报的打印机列表无效，请等待 Agent 心跳后重试");
        }

        if (printers.Count == 0)
        {
            return Result.Failure<PrinterBindingsResponse>(
                ApiErrorCodes.ValidationError, "设备未上报打印机，请先在 Agent 中选择要绑定的打印机");
        }

        var defaultCountResult = await database.ExecuteScalarAsync<long?>(
            "SELECT COUNT(*) FROM nc_sys_printer_bindings WHERE user_id = $1 AND is_default",
            ct,
            bindCode.UserId);
        if (defaultCountResult.IsFailure)
        {
            return Result.Failure<PrinterBindingsResponse>(
                defaultCountResult.ErrorCode!, defaultCountResult.Message!);
        }

        var hasDefault = (defaultCountResult.Value ?? 0) > 0;
        var requestedDefault = printers
            .Where(printer => printer.IsDefault)
            .Select(printer => printer.Name!.Trim())
            .FirstOrDefault();

        // 未指定默认：账号已有默认则保持，没有则把本次第一台设为默认。
        var defaultPrinterName = requestedDefault ?? (hasDefault ? null : printers[0].Name!.Trim());

        if (defaultPrinterName is not null)
        {
            var clearResult = await ClearDefaultAsync(bindCode.UserId, ct);
            if (clearResult.IsFailure)
            {
                return Result.Failure<PrinterBindingsResponse>(
                    clearResult.ErrorCode!, clearResult.Message!);
            }
        }

        var bindingIds = new List<long>(printers.Count);
        foreach (var printer in printers)
        {
            var printerName = printer.Name!.Trim();
            var isDefault = defaultPrinterName is not null
                            && string.Equals(printerName, defaultPrinterName, StringComparison.OrdinalIgnoreCase);

            // 账号已有默认且本次未声明默认：不改变现有 is_default，只刷新 updated_at。
            const string preserveDefaultSql = @"INSERT INTO nc_sys_printer_bindings
                      (user_id, device_id, printer_name, is_default, created_at, updated_at)
                  VALUES ($1, $2, $3, FALSE, now(), now())
                  ON CONFLICT (user_id, device_id, printer_name)
                  DO UPDATE SET updated_at = now()
                  RETURNING id";

            const string setDefaultSql = @"INSERT INTO nc_sys_printer_bindings
                      (user_id, device_id, printer_name, is_default, created_at, updated_at)
                  VALUES ($1, $2, $3, $4, now(), now())
                  ON CONFLICT (user_id, device_id, printer_name)
                  DO UPDATE SET is_default = EXCLUDED.is_default, updated_at = now()
                  RETURNING id";

            var upsertResult = defaultPrinterName is null
                ? await database.ExecuteScalarAsync<long?>(
                    preserveDefaultSql, ct, bindCode.UserId, device.Id, printerName)
                : await database.ExecuteScalarAsync<long?>(
                    setDefaultSql, ct, bindCode.UserId, device.Id, printerName, isDefault);

            if (upsertResult.IsFailure)
            {
                return Result.Failure<PrinterBindingsResponse>(
                    upsertResult.ErrorCode!, upsertResult.Message!);
            }

            if (upsertResult.Value is not { } bindingId)
            {
                return Result.Failure<PrinterBindingsResponse>(
                    ApiErrorCodes.DbError, "写入打印机绑定失败：未返回绑定 ID");
            }

            bindingIds.Add(bindingId);
        }

        var markUsedResult = await database.ExecuteNonQueryAsync(
            @"UPDATE nc_sys_printer_bind_codes
              SET used_at = now()
              WHERE id = $1 AND used_at IS NULL",
            ct,
            bindCode.Id);
        if (markUsedResult.IsFailure)
        {
            return Result.Failure<PrinterBindingsResponse>(
                markUsedResult.ErrorCode!, markUsedResult.Message!);
        }

        var policy = await sessions.GetPolicyAsync(ct);
        var bindingsResult = await database.QueryAsync<PrinterBindingDto>(
            BindingSelect + @" AND b.id = ANY($3::bigint[])
                               ORDER BY b.is_default DESC, b.created_at ASC, b.id ASC",
            ct,
            bindCode.UserId,
            policy.OnlineWindowSeconds,
            bindingIds.ToArray());
        if (bindingsResult.IsFailure)
        {
            return Result.Failure<PrinterBindingsResponse>(
                bindingsResult.ErrorCode!, bindingsResult.Message!);
        }

        await transaction.CommitAsync(ct);
        logger.Info(
            $"Agent 按码绑定打印机: UserId={bindCode.UserId}, DeviceId={device.Id}, Count={bindingIds.Count}");

        return Result.Success(new PrinterBindingsResponse(bindingsResult.Value ?? []));
    }

    /// <summary>
    /// 解析打印任务目标：bindingId 优先；否则按 printerName 找当前账号绑定；
    /// 都没传时退回当前账号默认绑定（预览兼容旧前端），没有绑定则返回 null 目标。
    /// </summary>
    public async Task<Result<PrintJobTarget?>> ResolveTargetAsync(
        int userId,
        long? bindingId,
        string? printerName,
        CancellationToken ct = default)
    {
        if (bindingId is > 0)
        {
            var bindingResult = await database.QuerySingleAsync<PrintJobBindingRow>(
                @"SELECT id, device_id, printer_name
                  FROM nc_sys_printer_bindings
                  WHERE id = $1 AND user_id = $2",
                ct,
                bindingId.Value,
                userId);
            if (bindingResult.IsFailure)
            {
                return Result.Failure<PrintJobTarget?>(
                    bindingResult.ErrorCode!, bindingResult.Message!);
            }

            return bindingResult.Value is { } binding
                ? Result.Success<PrintJobTarget?>(
                    new PrintJobTarget(userId, binding.DeviceId, binding.PrinterName))
                : Result.Failure<PrintJobTarget?>(
                    ApiErrorCodes.PrinterNotFound, "打印机绑定不存在，请重新选择打印机");
        }

        var requestedName = printerName?.Trim();
        if (!string.IsNullOrEmpty(requestedName))
        {
            var bindingResult = await database.QuerySingleAsync<PrintJobBindingRow>(
                @"SELECT id, device_id, printer_name
                  FROM nc_sys_printer_bindings
                  WHERE user_id = $1 AND lower(printer_name) = lower($2)
                  ORDER BY is_default DESC, id ASC
                  LIMIT 1",
                ct,
                userId,
                requestedName);
            if (bindingResult.IsFailure)
            {
                return Result.Failure<PrintJobTarget?>(
                    bindingResult.ErrorCode!, bindingResult.Message!);
            }

            return bindingResult.Value is { } binding
                ? Result.Success<PrintJobTarget?>(
                    new PrintJobTarget(userId, binding.DeviceId, binding.PrinterName))
                : Result.Failure<PrintJobTarget?>(
                    ApiErrorCodes.ValidationError,
                    $"打印机「{requestedName}」未绑定，请先在「我的打印机」中绑定后再打印");
        }

        var defaultResult = await database.QuerySingleAsync<PrintJobBindingRow>(
            @"SELECT id, device_id, printer_name
              FROM nc_sys_printer_bindings
              WHERE user_id = $1
              ORDER BY is_default DESC, created_at ASC, id ASC
              LIMIT 1",
            ct,
            userId);
        if (defaultResult.IsFailure)
        {
            return Result.Failure<PrintJobTarget?>(
                defaultResult.ErrorCode!, defaultResult.Message!);
        }

        if (defaultResult.Value is { } defaultBinding)
        {
            return Result.Success<PrintJobTarget?>(
                new PrintJobTarget(userId, defaultBinding.DeviceId, defaultBinding.PrinterName));
        }

        logger.Warn($"打印任务未指定打印机且当前账号无绑定: UserId={userId}，任务将没有目标设备");
        return Result.Success<PrintJobTarget?>(null);
    }

    private static bool TryReadReportedPrinters(
        string? printersJson,
        out List<AgentPrinterReport> printers)
    {
        printers = [];
        if (string.IsNullOrWhiteSpace(printersJson))
        {
            return true;
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<AgentPrinterReport>>(
                printersJson, AgentPrinterNormalizer.JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            printers = parsed
                .Where(printer => !string.IsNullOrWhiteSpace(printer.Name))
                .Select(printer => new AgentPrinterReport(printer.Name!.Trim(), printer.IsDefault))
                .DistinctBy(printer => printer.Name!, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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

    /// <summary>绑定码最小字段（not_expired 由数据库时钟判定，避免应用/库时钟不一致）。</summary>
    public sealed class BindCodeRow
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public bool NotExpired { get; set; }
    }

    /// <summary>任务目标解析用的绑定行。</summary>
    public sealed class PrintJobBindingRow
    {
        public long Id { get; set; }
        public long DeviceId { get; set; }
        public string PrinterName { get; set; } = string.Empty;
    }
}

/// <summary>打印任务固化的目标：账号 + Agent 设备 + 打印机名。</summary>
public sealed record PrintJobTarget(int UserId, long DeviceId, string PrinterName);
