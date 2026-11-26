using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Services.UserManagement;

/// <summary>当前用户的数据范围快照：IsSuperAdmin=true 时不做组织过滤。</summary>
public sealed record UserAdminScope(bool IsSuperAdmin, IReadOnlyList<int> OrganizationIds);

/// <summary>用户角色变更数量。</summary>
public sealed record UserRoleChange(int Added, int Removed)
{
    public bool HasChanges => Added > 0 || Removed > 0;
}

/// <summary>
/// 用户/角色/权限/在线会话端点的共享逻辑：
/// 数据范围解析、目标用户可管理判定、角色批量查询、角色变更联动（缓存失效 + 踢会话）、
/// Core 错误码到 HTTP 状态码的统一映射。
/// </summary>
public static class UserAdminService
{
    public const string SuperAdminRoleCode = "SUPER_ADMIN";
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;
    public const int MaxKeywordLength = 100;

    /// <summary>分页参数归一：page 1 起，pageSize 默认 20、上限 200。</summary>
    public static (int PageIndex, int PageSize) NormalizePaging(int? page, int? pageSize)
    {
        var index = page is null or < 1 ? 1 : page.Value;
        var size = Math.Clamp(pageSize is null or <= 0 ? DefaultPageSize : pageSize.Value, 1, MaxPageSize);
        return (index, size);
    }

    public static bool IsSuperAdminRole(string? roleCode)
        => string.Equals(roleCode, SuperAdminRoleCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>解析当前用户是否超管及可管理组织集合（超管返回全部组织 id，用于 CanBeManaged 标注）。</summary>
    public static async Task<UserAdminScope> ResolveScopeAsync(
        IDataScopeService dataScope,
        int userId,
        CancellationToken ct)
    {
        var isSuperAdmin = await dataScope.IsSuperAdminAsync(userId, ct);
        var organizationIds = await dataScope.GetManageableOrganizationIdsAsync(userId, ct);
        return new UserAdminScope(isSuperAdmin, organizationIds ?? new List<int>());
    }

    /// <summary>
    /// 目标用户是否在当前管理员可管理组织范围内（只看组织边界，不判断 SUPER_ADMIN 保护）。
    /// 写操作先用本方法区分 404（越权/不存在），再对超管目标返回 409（受保护）。
    /// </summary>
    public static bool IsUserInScope(UserAdminScope scope, User user)
        => scope.IsSuperAdmin
            || (user.OrganizationId is { } organizationId
                && scope.OrganizationIds.Contains(organizationId));

    /// <summary>
    /// 目标用户是否可被当前管理员操作：
    /// 超管可管理全部；非超管仅限可管理组织内，且不能操作 SUPER_ADMIN 角色用户。
    /// </summary>
    public static bool CanManageUser(UserAdminScope scope, User user, bool targetIsSuperAdmin)
        => scope.IsSuperAdmin
            || (!targetIsSuperAdmin && IsUserInScope(scope, user));

    /// <summary>一次批量查询多个用户的角色（避免列表 N+1）。</summary>
    public static async Task<Result<Dictionary<int, List<SystemUserRoleDto>>>> GetRolesForUsersAsync(
        IDatabaseService database,
        IReadOnlyCollection<int> userIds,
        CancellationToken ct)
    {
        var map = new Dictionary<int, List<SystemUserRoleDto>>();
        if (userIds.Count == 0)
        {
            return Result.Success(map);
        }

        const string sql = @"
SELECT ur.user_id, r.id AS role_id, r.name, r.code
FROM nc_sys_user_roles ur
INNER JOIN nc_sys_roles r ON r.id = ur.role_id
WHERE ur.user_id = ANY($1)
ORDER BY ur.user_id, r.level, r.id";

        var ids = userIds.Distinct().ToArray();
        var result = await database.QueryAsync<UserRoleRow>(sql, ct, ids);
        if (result.IsFailure)
        {
            return Result.Failure<Dictionary<int, List<SystemUserRoleDto>>>(
                result.ErrorCode!, result.Message!);
        }

        foreach (var row in result.Value ?? new List<UserRoleRow>())
        {
            if (!map.TryGetValue(row.UserId, out var roles))
            {
                roles = new List<SystemUserRoleDto>();
                map[row.UserId] = roles;
            }

            roles.Add(new SystemUserRoleDto(row.RoleId, row.Name, row.Code));
        }

        return Result.Success(map);
    }

    /// <summary>查询角色下全部 user_id（权限/数据范围变更后逐个踢会话）。</summary>
    public static async Task<Result<List<int>>> GetRoleUserIdsAsync(
        IDatabaseService database,
        int roleId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT user_id
FROM nc_sys_user_roles
WHERE role_id = $1
ORDER BY user_id";

        return await database.QueryAsync<int>(sql, ct, roleId);
    }

    /// <summary>启用的超级管理员账号数（停用/删除前保护最后一个超管）。</summary>
    public static async Task<Result<int>> CountActiveSuperAdminsAsync(
        IDatabaseService database,
        CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(DISTINCT u.id)::int
FROM nc_sys_users u
INNER JOIN nc_sys_user_roles ur ON ur.user_id = u.id
INNER JOIN nc_sys_roles r ON r.id = ur.role_id
WHERE u.is_active = TRUE AND r.code = $1";

        var result = await database.ExecuteScalarAsync<int>(sql, ct, SuperAdminRoleCode);
        if (result.IsFailure)
        {
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value);
    }

    /// <summary>
    /// 对比现有角色与目标角色，通过 Core 唯一入口 AssignRoleAsync/RemoveRoleAsync 落库。
    /// Core 会在每次变更后踢出该用户会话；调用方完成事务提交后再执行缓存失效与保留当前会话的兜底踢出。
    /// </summary>
    public static async Task<Result<UserRoleChange>> ApplyRoleChangesAsync(
        IUserService users,
        int userId,
        IReadOnlyCollection<int> currentRoleIds,
        IReadOnlyCollection<int> desiredRoleIds,
        string assignedBy,
        CancellationToken ct)
    {
        var current = currentRoleIds.ToHashSet();
        var desired = desiredRoleIds.ToHashSet();
        var toAdd = desired.Except(current).ToList();
        var toRemove = current.Except(desired).ToList();

        foreach (var roleId in toRemove)
        {
            var removeResult = await users.RemoveRoleAsync(userId, roleId, ct);
            if (removeResult.IsFailure)
            {
                return Result.Failure<UserRoleChange>(removeResult.ErrorCode!, removeResult.Message!);
            }
        }

        foreach (var roleId in toAdd)
        {
            var assignResult = await users.AssignRoleAsync(userId, roleId, assignedBy, ct);
            if (assignResult.IsFailure)
            {
                return Result.Failure<UserRoleChange>(assignResult.ErrorCode!, assignResult.Message!);
            }
        }

        return Result.Success(new UserRoleChange(toAdd.Count, toRemove.Count));
    }

    /// <summary>
    /// 用户角色变更后：失效权限/数据范围缓存，并踢出该用户其他会话（保留操作者当前会话）。
    /// </summary>
    public static async Task<int> InvalidateAndKickUserAsync(
        INewPermissionService permissions,
        IDataScopeService dataScopes,
        IUserSessionService sessions,
        ILoggerService logger,
        int userId,
        int? operatorUserId,
        Guid? exceptSessionId,
        CancellationToken ct)
    {
        await permissions.InvalidateUserCacheAsync(userId);
        await dataScopes.InvalidateUserCacheAsync(userId);

        var revokeResult = await sessions.RevokeAllForUserAsync(
            userId,
            SessionConstants.EndReasonPermissionsChanged,
            operatorUserId,
            exceptSessionId,
            ct);

        if (revokeResult.IsFailure)
        {
            logger.Warn($"角色变更后吊销用户会话失败: UserId={userId}, {revokeResult.ErrorCode} {revokeResult.Message}");
            return 0;
        }

        return revokeResult.Value;
    }

    /// <summary>逐个结束角色下全部用户的会话（角色权限/数据范围变更）。</summary>
    public static async Task<int> KickUsersAsync(
        IUserSessionService sessions,
        ILoggerService logger,
        IReadOnlyCollection<int> userIds,
        int? operatorUserId,
        Guid? exceptSessionId,
        CancellationToken ct)
    {
        var total = 0;
        foreach (var userId in userIds.Distinct())
        {
            var revokeResult = await sessions.RevokeAllForUserAsync(
                userId,
                SessionConstants.EndReasonPermissionsChanged,
                operatorUserId,
                exceptSessionId,
                ct);

            if (revokeResult.IsFailure)
            {
                logger.Warn($"吊销用户会话失败: UserId={userId}, {revokeResult.ErrorCode} {revokeResult.Message}");
                continue;
            }

            total += revokeResult.Value;
        }

        return total;
    }

    /// <summary>当前操作者名称（审计字段用，不接收前端传入）。</summary>
    public static string GetActorName(HttpContext context)
    {
        var user = context.GetCurrentUser();
        if (!string.IsNullOrWhiteSpace(user?.Username))
        {
            return user.Username;
        }

        return context.GetUserId()?.ToString() ?? "system";
    }

    /// <summary>
    /// Core Result 错误码到 HTTP 的统一映射：重复/占用/受保护 409，找不到 404，参数 400，其余沿用 ApiErrors。
    /// </summary>
    public static IResult WriteFailure(HttpContext context, string? errorCode, string? message)
    {
        var normalized = errorCode?.Trim().ToUpperInvariant();

        return normalized switch
        {
            "DUPLICATE_USERNAME" => Conflict(context, "用户名已存在"),
            "DUPLICATE_PHONE" => Conflict(context, "手机号已存在"),
            "DUPLICATE_ID_CARD" => Conflict(context, "身份证号已存在"),
            "DUPLICATE_CODE" => Conflict(context, "角色代码已存在"),
            "ROLE_IN_USE" => Conflict(context, "该角色正在被用户使用，无法删除"),
            "DB_UNIQUE_VIOLATION" => Conflict(context, "数据重复，请检查用户名、手机号或角色代码"),
            "USER_NOT_FOUND" or "ROLE_NOT_FOUND" or "NOT_FOUND" =>
                NotFound(context, string.IsNullOrWhiteSpace(message) ? "请求的资源不存在" : message),
            "VALIDATION_FAILED" or "VALIDATION_ERROR" or "REQUIRED_FIELD_MISSING"
                or "INVALID_PHONE" or "INVALID_ID_CARD" =>
                Invalid(context, string.IsNullOrWhiteSpace(message) ? "请求参数校验失败" : message),
            _ => WriteMapped(context, errorCode, message)
        };
    }

    private static IResult WriteMapped(HttpContext context, string? errorCode, string? message)
    {
        var mapped = ApiErrors.Map(errorCode, message);
        return ApiErrors.Write(context, mapped.Status, mapped.Code, mapped.Message);
    }

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict, ApiErrorCodes.ValidationError, message);

    private sealed class UserRoleRow
    {
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
