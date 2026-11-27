using System.Globalization;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 本机操作指令通道（LocalOp）下发端点：复用打印任务队列做
/// 「下发 → Agent 认领 → 执行 → 回传」，与 <c>TemplateBatch</c> 的
/// <c>open-folder</c>（非打印本机操作）同一套通道语义。
/// <para>
/// 本端点只负责入队；公文字体安装、ZeroTier 接入等具体执行逻辑由 Agent 的
/// <c>LocalOpExecutor</c> 按 <c>fields.op</c> 分发执行（见 Agent README §12）。
/// 结果查询复用 <c>GET /api/print/jobs</c>：成功消息带 <c>RESULT:</c> 前缀 + JSON，失败走 error_message。
/// </para>
/// </summary>
public static class LocalOpEndpoints
{
    /// <summary>op 名最大长度（执行器按 op 分发，只可能是短标识）。</summary>
    private const int MaxOpLength = 100;

    /// <summary>args 序列化后的最大字符数：本机操作参数是小结构，超长直接拒绝。</summary>
    private const int MaxArgsJsonLength = 4000;

    private static readonly JsonSerializerOptions ArgsJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapLocalOpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/devices/{deviceId}/local-ops", CreateAsync);
        return app;
    }

    /// <summary>
    /// POST /api/devices/{deviceId}/local-ops
    /// body: { "op": "zerotier.join", "args": { ... } } —— args 可省略。
    /// <para>
    /// 权限：打印机绑定类端点（<c>PrinterEndpoints</c>）只校验登录、没有权限码，
    /// 而本机操作是改机器状态的运维动作，故取「系统运维」域的
    /// <see cref="PermissionCodes.SYSTEM_BACKUP"/>（现有运维类权限码中唯一默认只给超级管理员的机器维护入口）。
    /// </para>
    /// <para>
    /// <b>经评估保持 <c>SYSTEM_BACKUP</c>，不新增 <c>SYSTEM_MANAGE</c>（已决策关闭）</b>，理由：
    /// ① 无权限收益——<c>SYSTEM_BACKUP</c> 在默认矩阵中只授 SUPER_ADMIN，与 <c>SYSTEM_MANAGE</c>
    ///    的目标授权范围完全一致，换码只有命名差别；
    /// ② 生效路径不可控——Web 服务端没有任何调用点触发
    ///    <c>NewPermissionService.InitializeDefaultPermissionsAsync</c>（唯一调用方是 legacy 桌面端
    ///    <c>MainViewModel</c>），仅「加码 + 递增 <c>MatrixVersion</c>」不会把新码写入
    ///    <c>nc_sys_permissions</c>、也不会授给任何角色，端点改码后将全员 403；
    ///    若为生效补上启动调用，则会触发该方法的一次性重建——清空 <c>nc_perm_user_permissions</c>
    ///    全部用户直授、并按目录重写 6 个默认角色的映射，覆盖管理员在权限矩阵页对默认角色的自定义勾选；
    /// ③ 前端不依赖具体权限码——权限矩阵 UI 自动读 <c>GET /api/system/permissions</c>，
    ///    网络接入页只做文字提示，换码无前端收益。
    /// </para>
    /// </summary>
    private static async Task<IResult> CreateAsync(
        string deviceId,
        LocalOpRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        AgentDeviceService devices,
        ArchivePrintQueueService queue,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.SYSTEM_BACKUP, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!long.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return InvalidRequest(context, "设备 ID 无效");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Op))
        {
            return InvalidRequest(context, "op 不能为空");
        }

        var op = request.Op.Trim();
        if (op.Length > MaxOpLength)
        {
            return InvalidRequest(context, $"op 不能超过 {MaxOpLength} 个字符");
        }

        // op 白名单（Core 常量，与 Agent LocalOpExecutor 分发共用）：未知 op 直接 400，不入队。
        if (!LocalOpConstants.IsKnownOp(op))
        {
            return InvalidRequest(context, $"不支持的本机操作指令: {op}");
        }

        var argsResult = SerializeArgs(request.Args);
        if (argsResult.Error is not null)
        {
            return InvalidRequest(context, argsResult.Error);
        }

        // 目标设备必须存在且启用（GetByIdAsync 已带 is_active 条件）。
        var deviceResult = await devices.GetByIdAsync(id, ct);
        if (deviceResult.IsFailure)
        {
            return ApiErrors.FromResult(context, deviceResult);
        }

        if (deviceResult.Value is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status404NotFound,
                ApiErrorCodes.PrinterDeviceNotFound, "Agent 设备不存在");
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        // op/args 走 fields_json 下发给 Agent；args 是 JSON 文本（队列字段是 string 字典）。
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["op"] = op,
            ["requestedByUserId"] = user.Id.ToString(CultureInfo.InvariantCulture)
        };
        if (argsResult.Json is not null)
        {
            fields["args"] = argsResult.Json;
        }

        var jobRequest = new PrintJobRequest
        {
            // LocalOp 不是打印业务，业务类型与任务类型同名，便于留痕归类。
            BusinessType = PrintJobConstants.JobKindLocalOp,
            JobKind = PrintJobConstants.JobKindLocalOp,
            // 无模板：TemplateBatch 的 null 先例（EnqueueAsync 对 TemplateId == null 完全容忍，
            // status 走 Pending 而非 BatchPending，claim 可直接认领）。
            TemplateId = null,
            TemplateName = op,
            TargetUserId = user.Id,
            TargetDeviceId = id,
            Fields = fields,
            RequestedBy = user.Id,
            RequestedByName = user.FullName ?? user.Username
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
        {
            return ApiErrors.FromResult(context, enqueueResult);
        }

        var job = enqueueResult.Value;
        // 日志脱敏：只记设备/指令名/任务号，不记 args 原文（args 可能含网络凭据、令牌）。
        logger.Info($"本机操作入队: DeviceId={id}, Op={op}, JobNo={job.JobNo}, UserId={user.Id}");

        return Results.Json(new LocalOpCreatedResponse(job.Id, job.JobNo));
    }

    /// <summary>
    /// args（任意 JSON）→ 单行 JSON 文本。只做长度与类型校验，不解析业务字段。
    /// 非对象/数组的标量（字符串、数字）也允许，执行器自行解释。
    /// </summary>
    private static (string? Json, string? Error) SerializeArgs(object? args)
    {
        if (args is null)
        {
            return (null, null);
        }

        // object 反序列化得到 JsonElement；Undefined 只可能来自手工构造的默认值，按"未传"处理。
        if (args is JsonElement element && element.ValueKind == JsonValueKind.Undefined)
        {
            return (null, null);
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(args, ArgsJsonOptions);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return (null, "args 无法序列化为 JSON");
        }

        if (json.Length > MaxArgsJsonLength)
        {
            return (null, $"args 不能超过 {MaxArgsJsonLength} 个字符");
        }

        return (json, null);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");
}
