using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// 端点级权限校验：先确认登录会话，再查权限中心。
/// 返回 null 表示放行；否则返回可直接 return 的 IResult（401/403）。
/// </summary>
public static class EndpointSecurity
{
    public static async Task<IResult?> RequirePermissionAsync(
        HttpContext context,
        INewPermissionService permissions,
        string permissionCode,
        CancellationToken ct = default)
    {
        var userId = context.GetUserId();
        if (userId is null)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired,
                "请先登录");
        }

        if (!await permissions.HasPermissionAsync(userId.Value, permissionCode, ct))
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "没有权限执行该操作");
        }

        return null;
    }

    /// <summary>
    /// 任一权限命中即放行（用于跨业务共享数据，如档案照片：低收入/高龄/临时救助均可查看）。
    /// 返回 null 表示放行；否则 401/403。
    /// </summary>
    public static async Task<IResult?> RequireAnyPermissionAsync(
        HttpContext context,
        INewPermissionService permissions,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken ct = default)
    {
        var userId = context.GetUserId();
        if (userId is null)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired,
                "请先登录");
        }

        foreach (var code in permissionCodes)
        {
            if (await permissions.HasPermissionAsync(userId.Value, code, ct))
            {
                return null;
            }
        }

        return ApiErrors.Write(
            context,
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden,
            "没有权限执行该操作");
    }
}
