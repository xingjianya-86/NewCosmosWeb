using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services.UserManagement;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using RoleEntity = NewCosmos.Models.Entities.UserManagement.Role;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 用户权限管理接口：用户 / 角色 / 权限目录 / 在线会话。
/// <para>
/// 对齐 PC <c>UserManagementPage</c>、<c>UserEditorViewModel</c>、<c>OnlineSessionsViewModel</c>。
/// 功能权限由 <see cref="EndpointSecurity"/> 统一校验；数据范围由 Core <see cref="IDataScopeService"/> 解析，
/// 非超管仅能操作可管理组织内的用户/会话，越权目标一律 404。
/// </para>
/// <para>
/// 业务读写全部走 Core <see cref="IUserService"/> / <see cref="IRoleService"/> /
/// <see cref="IUserSessionService"/>；本层只做参数校验、数据范围、审计日志与 DTO 映射。
/// </para>
/// </summary>
public static class UserManagementEndpoints
{
    public static IEndpointRouteBuilder MapUserManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var system = app.MapGroup("/api/system");

        var users = system.MapGroup("/users");
        users.MapGet("/", GetUsersAsync);
        users.MapGet("/stats", GetStatsAsync);
        users.MapGet("/options", GetOptionsAsync);
        users.MapGet("/my-permissions", GetMyPermissionsAsync);
        users.MapGet("/{id:int}", GetUserAsync);
        users.MapPost("/", CreateUserAsync);
        users.MapPut("/{id:int}", UpdateUserAsync);
        users.MapDelete("/{id:int}", DeleteUserAsync);
        users.MapPost("/{id:int}/reset-password", ResetPasswordAsync);
        users.MapPost("/{id:int}/toggle-status", ToggleStatusAsync);

        var roles = system.MapGroup("/roles");
        roles.MapGet("/", GetRolesAsync);
        roles.MapPost("/", CreateRoleAsync);
        roles.MapPut("/{id:int}", UpdateRoleAsync);
        roles.MapDelete("/{id:int}", DeleteRoleAsync);
        roles.MapGet("/{id:int}/permissions", GetRolePermissionsAsync);
        roles.MapPut("/{id:int}/permissions", SaveRolePermissionsAsync);

        system.MapGet("/permissions", GetPermissionsAsync);

        var sessions = system.MapGroup("/sessions");
        sessions.MapGet("/", GetSessionsAsync);
        sessions.MapPost("/kick-all", KickAllAsync);
        sessions.MapPost("/{sessionId:guid}/kick", KickSessionAsync);

        return app;
    }

    // ───────────────────────────── 用户查询 ─────────────────────────────

    /// <summary>GET /api/system/users — 数据范围内的用户分页列表（含角色与可管理标记）。</summary>
    private static async Task<IResult> GetUsersAsync(
        HttpContext context,
        INewPermissionService permissions,
        IUserService users,
        IDataScopeService dataScope,
        IDatabaseService database,
        CancellationToken ct,
        string? keyword,
        string? status,
        string? role,
        int? page,
        int? pageSize)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (!TryParseStatus(status, out var isActive))
        {
            return Invalid(context, "status 参数无效，可选值：all / active / inactive");
        }

        var normalizedKeyword = keyword?.Trim();
        if (normalizedKeyword is { Length: > UserAdminService.MaxKeywordLength })
        {
            return Invalid(context, $"关键字长度不能超过 {UserAdminService.MaxKeywordLength} 个字符");
        }

        var roleCode = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        var (pageIndex, size) = UserAdminService.NormalizePaging(page, pageSize);

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && scope.OrganizationIds.Count == 0)
        {
            return EmptyUserPage(pageIndex, size);
        }

        var result = await users.GetPagedAsync(
            pageIndex,
            size,
            normalizedKeyword!,
            isActive,
            roleCode ?? string.Empty,
            scope.IsSuperAdmin ? null : scope.OrganizationIds,
            ct);

        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var pageResult = result.Value;
        var pageItems = pageResult?.Items ?? new List<User>();
        var roleMapResult = await UserAdminService.GetRolesForUsersAsync(
            database, pageItems.Select(item => item.Id).ToList(), ct);
        if (roleMapResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, roleMapResult.ErrorCode, roleMapResult.Message);
        }

        var roleMap = roleMapResult.Value;
        var items = pageItems
            .Select(user =>
            {
                var roles = roleMap.TryGetValue(user.Id, out var userRoles)
                    ? userRoles
                    : new List<SystemUserRoleDto>();
                var targetIsSuperAdmin = roles.Any(r => UserAdminService.IsSuperAdminRole(r.Code));
                var canBeManaged = UserAdminService.CanManageUser(scope, user, targetIsSuperAdmin);
                return MapUser(user, roles, canBeManaged);
            })
            .ToList();

        return Results.Json(new SystemUserPageResponse(
            items,
            pageResult?.TotalCount ?? items.Count,
            pageResult?.PageIndex ?? pageIndex,
            pageResult?.PageSize ?? size));
    }

    /// <summary>GET /api/system/users/{id} — 用户详情（范围外返回 404）。</summary>
    private static async Task<IResult> GetUserAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IUserService users,
        IDataScopeService dataScope,
        IDatabaseService database,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (id <= 0)
        {
            return NotFound(context, "用户不存在");
        }

        var userResult = await users.GetByIdAsync(id, ct);
        if (userResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, userResult.ErrorCode, userResult.Message);
        }

        if (!userResult.HasValue || userResult.Value is not { } user)
        {
            return NotFound(context, "用户不存在");
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!UserAdminService.IsUserInScope(scope, user))
        {
            return NotFound(context, "用户不存在");
        }

        var rolesResult = await UserAdminService.GetRolesForUsersAsync(database, new[] { user.Id }, ct);
        if (rolesResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, rolesResult.ErrorCode, rolesResult.Message);
        }

        var roles = rolesResult.Value.TryGetValue(user.Id, out var userRoles)
            ? userRoles
            : new List<SystemUserRoleDto>();
        var targetIsSuperAdmin = roles.Any(r => UserAdminService.IsSuperAdminRole(r.Code));

        return Results.Json(MapUser(
            user,
            roles,
            canBeManaged: UserAdminService.CanManageUser(scope, user, targetIsSuperAdmin)));
    }

    /// <summary>GET /api/system/users/stats — 页头统计（非超管统计其可管理组织内启用用户）。</summary>
    private static async Task<IResult> GetStatsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        IDataScopeService dataScope,
        IDatabaseService database,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);

        var activeUsers = 0;
        if (scope.IsSuperAdmin)
        {
            var countResult = await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::int FROM nc_sys_users WHERE is_active = TRUE", ct);
            if (countResult.IsFailure)
            {
                return ApiErrors.FromResult(context, countResult);
            }

            activeUsers = countResult.Value;
        }
        else if (scope.OrganizationIds.Count > 0)
        {
            var countResult = await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::int FROM nc_sys_users WHERE is_active = TRUE AND organization_id = ANY($1)",
                ct,
                scope.OrganizationIds.ToArray());
            if (countResult.IsFailure)
            {
                return ApiErrors.FromResult(context, countResult);
            }

            activeUsers = countResult.Value;
        }

        var allRoles = await roles.GetAllRolesAsync(ct);
        return Results.Json(new SystemUserStatsResponse(activeUsers, allRoles.Count));
    }

    /// <summary>GET /api/system/users/options — 用户编辑器下拉：角色、可管理组织、启用岗位。</summary>
    private static async Task<IResult> GetOptionsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        IOrganizationService organizations,
        IDictionaryService dictionaries,
        IDataScopeService dataScope,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        var allRoles = await roles.GetAllRolesAsync(ct);
        var roleOptions = allRoles
            .Select(role => new SystemRoleOptionDto(role.Id, role.Name, role.Code))
            .ToList();

        var organizationsResult = await organizations.GetAllAsync(ct);
        if (organizationsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, organizationsResult);
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        var allowedOrganizationIds = scope.OrganizationIds.ToHashSet();
        var organizationOptions = (organizationsResult.Value ?? new List<Organization>())
            .Where(org => scope.IsSuperAdmin || allowedOrganizationIds.Contains(org.Id))
            .OrderBy(org => org.Name)
            .Select(org => new SystemOrganizationOptionDto(org.Id, org.Name))
            .ToList();

        var positionsResult = await dictionaries.GetItemsByCategoryAsync(DictionaryTypeCodes.Positions, ct);
        if (positionsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, positionsResult);
        }

        var positions = (positionsResult.Value ?? new List<DictItemView>())
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .Select(item => item.ItemKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Results.Json(new SystemUserOptionsResponse(roleOptions, organizationOptions, positions));
    }

    /// <summary>GET /api/system/users/my-permissions — 当前登录用户权限码与超管标记（登录即可）。</summary>
    private static async Task<IResult> GetMyPermissionsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDataScopeService dataScope,
        CancellationToken ct)
    {
        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        var permissionCodes = await permissions.GetUserPermissionCodesAsync(currentUserId, ct);
        var isSuperAdmin = await dataScope.IsSuperAdminAsync(currentUserId, ct);

        return Results.Json(new SystemMyPermissionsResponse(
            (permissionCodes ?? Array.Empty<string>())
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToList(),
            isSuperAdmin));
    }

    // ───────────────────────────── 用户写入 ─────────────────────────────

    /// <summary>POST /api/system/users — 创建用户并分配角色（事务内完成）。</summary>
    private static async Task<IResult> CreateUserAsync(
        SystemUserCreateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IUserService users,
        IRoleService roles,
        IDataScopeService dataScope,
        IUserSessionService sessions,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_CREATE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var username = request.Username?.Trim() ?? string.Empty;
        if (username.Length == 0)
        {
            return Invalid(context, "用户名不能为空");
        }

        if (username.Length > 50)
        {
            return Invalid(context, "用户名长度不能超过 50 个字符");
        }

        var password = request.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            return Invalid(context, "密码不能为空");
        }

        if (password.Length > 100)
        {
            return Invalid(context, "密码长度不能超过 100 个字符");
        }

        var phone = request.Phone?.Trim() ?? string.Empty;
        if (phone.Length == 0)
        {
            return Invalid(context, "手机号不能为空");
        }

        if (phone.Length > 20)
        {
            return Invalid(context, "手机号长度不能超过 20 个字符");
        }

        var fullName = request.FullName?.Trim() ?? string.Empty;
        if (fullName.Length > 100)
        {
            return Invalid(context, "姓名长度不能超过 100 个字符");
        }

        var identityCard = request.IdentityCard?.Trim() ?? string.Empty;
        if (identityCard.Length > 18)
        {
            return Invalid(context, "身份证号长度不能超过 18 个字符");
        }

        var position = request.Position?.Trim() ?? string.Empty;
        if (position.Length > 100)
        {
            return Invalid(context, "岗位长度不能超过 100 个字符");
        }

        var roleIds = (request.RoleIds ?? new List<int>()).Distinct().ToList();
        var (roleError, selectedRoles) = await ResolveRolesAsync(roles, roleIds, ct);
        if (roleError is not null)
        {
            return Invalid(context, roleError);
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && selectedRoles.Any(r => UserAdminService.IsSuperAdminRole(r.Code)))
        {
            return Conflict(context, "不能分配超级管理员角色");
        }

        if (ValidateOrganizationScope(context, scope, request.OrganizationId) is { } organizationDenied)
        {
            return organizationDenied;
        }

        var actor = UserAdminService.GetActorName(context);
        var existsResult = await users.CheckUsernameExistsAsync(username, null, ct);
        if (existsResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, existsResult.ErrorCode, existsResult.Message);
        }

        if (existsResult.Value)
        {
            return Conflict(context, "用户名已存在");
        }

        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        var createResult = await users.CreateAsync(new UserCreateRequest
        {
            Username = username,
            Password = password,
            FullName = fullName,
            Phone = phone,
            IdentityCard = identityCard,
            Position = position,
            OrganizationId = request.OrganizationId,
            DefaultRoleId = null,
            CreatedBy = actor
        }, ct);

        if (createResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, createResult.ErrorCode, createResult.Message);
        }

        var userId = createResult.Value;
        foreach (var role in selectedRoles)
        {
            var assignResult = await users.AssignRoleAsync(userId, role.Id, actor, ct);
            if (assignResult.IsFailure)
            {
                return UserAdminService.WriteFailure(context, assignResult.ErrorCode, assignResult.Message);
            }
        }

        await transaction.CommitAsync(ct);

        await permissions.InvalidateUserCacheAsync(userId);
        await dataScope.InvalidateUserCacheAsync(userId);

        logger.LogSecurity("创建用户",
            ("UserId", userId),
            ("Username", username),
            ("RoleCount", selectedRoles.Count),
            ("Operator", actor));

        return Results.Json(
            new SystemUserCreatedResponse(userId, "用户已创建"),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>PUT /api/system/users/{id} — 编辑用户资料/状态并按差异同步角色。</summary>
    private static async Task<IResult> UpdateUserAsync(
        int id,
        SystemUserUpdateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IUserService users,
        IRoleService roles,
        IDataScopeService dataScope,
        IUserSessionService sessions,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_EDIT, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var (target, targetRoles, loadError) = await LoadTargetUserAsync(
            context, users, dataScope, database, id, currentUserId, ct);
        if (loadError is not null)
        {
            return loadError;
        }

        var targetIsSuperAdmin = targetRoles.Any(r => UserAdminService.IsSuperAdminRole(r.Code));
        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);

        if (!scope.IsSuperAdmin && targetIsSuperAdmin)
        {
            return Conflict(context, "不能编辑超级管理员用户");
        }

        var currentRoleIds = targetRoles.Select(r => r.Id).ToList();
        var desiredRoleIds = currentRoleIds;
        var desiredIsSuperAdmin = targetIsSuperAdmin;
        if (request.RoleIds is not null)
        {
            var requestedRoleIds = request.RoleIds.Distinct().ToList();
            var (roleError, selectedRoles) = await ResolveRolesAsync(roles, requestedRoleIds, ct);
            if (roleError is not null)
            {
                return Invalid(context, roleError);
            }

            if (!scope.IsSuperAdmin && selectedRoles.Any(r => UserAdminService.IsSuperAdminRole(r.Code)))
            {
                return Conflict(context, "不能分配超级管理员角色");
            }

            desiredRoleIds = selectedRoles.Select(r => r.Id).ToList();
            desiredIsSuperAdmin = selectedRoles.Any(r => UserAdminService.IsSuperAdminRole(r.Code));
        }

        var roleDiff = new UserRoleChange(
            desiredRoleIds.Except(currentRoleIds).Count(),
            currentRoleIds.Except(desiredRoleIds).Count());
        if (id == currentUserId && roleDiff.HasChanges)
        {
            return Conflict(context, "不能修改自己的角色，请由其他管理员操作");
        }

        var nextIsActive = request.IsActive ?? target.IsActive;
        if (!nextIsActive && id == currentUserId)
        {
            return Conflict(context, "不能停用当前登录账号");
        }

        var losesSuperAdminRole = targetIsSuperAdmin && !desiredIsSuperAdmin;
        if (target.IsActive
            && ((!nextIsActive && targetIsSuperAdmin) || losesSuperAdminRole))
        {
            var message = losesSuperAdminRole
                ? "不能移除最后一个启用超级管理员的超级管理员角色"
                : "不能停用最后一个启用的超级管理员";
            var lastSuperAdminDenied = await EnsureNotLastActiveSuperAdminAsync(
                context, database, message, ct);
            if (lastSuperAdminDenied is not null)
            {
                return lastSuperAdminDenied;
            }
        }

        if (ValidateOrganizationScope(context, scope, request.OrganizationId) is { } organizationDenied)
        {
            return organizationDenied;
        }

        var fullName = request.FullName is null ? target.FullName : request.FullName.Trim();
        if (fullName.Length > 100)
        {
            return Invalid(context, "姓名长度不能超过 100 个字符");
        }

        var phone = request.Phone is null ? target.Phone : request.Phone.Trim();
        if (phone.Length > 20)
        {
            return Invalid(context, "手机号长度不能超过 20 个字符");
        }

        var identityCard = request.IdentityCard is null ? target.IdentityCard : request.IdentityCard.Trim();
        if (identityCard.Length > 18)
        {
            return Invalid(context, "身份证号长度不能超过 18 个字符");
        }

        var position = request.Position is null ? target.Position : request.Position.Trim();
        if (position.Length > 100)
        {
            return Invalid(context, "岗位长度不能超过 100 个字符");
        }

        var actor = UserAdminService.GetActorName(context);

        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        var updateResult = await users.UpdateAsync(new UserUpdateRequest
        {
            UserId = id,
            FullName = fullName,
            Phone = phone,
            IdentityCard = identityCard,
            Position = position,
            OrganizationId = request.OrganizationId,
            IsActive = nextIsActive,
            UpdatedBy = actor
        }, ct);

        if (updateResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, updateResult.ErrorCode, updateResult.Message);
        }

        if (roleDiff.HasChanges)
        {
            var applyResult = await UserAdminService.ApplyRoleChangesAsync(
                users, id, currentRoleIds, desiredRoleIds, actor, ct);
            if (applyResult.IsFailure)
            {
                return UserAdminService.WriteFailure(context, applyResult.ErrorCode, applyResult.Message);
            }
        }

        await transaction.CommitAsync(ct);

        var kicked = 0;
        if (roleDiff.HasChanges)
        {
            kicked = await UserAdminService.InvalidateAndKickUserAsync(
                permissions,
                dataScope,
                sessions,
                logger,
                id,
                currentUserId,
                context.GetSessionId(),
                ct);
        }

        logger.LogSecurity("编辑用户",
            ("UserId", id),
            ("IsActive", nextIsActive),
            ("RolesAdded", roleDiff.Added),
            ("RolesRemoved", roleDiff.Removed),
            ("KickedSessions", kicked),
            ("Operator", actor));

        return Results.Json(new SystemUserActionResult(roleDiff.HasChanges
            ? "用户已更新，角色已同步，该用户需重新登录"
            : "用户已更新"));
    }

    /// <summary>DELETE /api/system/users/{id} — 停用用户并踢出全部会话。</summary>
    private static async Task<IResult> DeleteUserAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IUserService users,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_DELETE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (id == currentUserId)
        {
            return Conflict(context, "不能删除当前登录账号");
        }

        var (target, targetRoles, loadError) = await LoadTargetUserAsync(
            context, users, dataScope, database, id, currentUserId, ct);
        if (loadError is not null)
        {
            return loadError;
        }

        var targetIsSuperAdmin = targetRoles.Any(r => UserAdminService.IsSuperAdminRole(r.Code));
        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && targetIsSuperAdmin)
        {
            return Conflict(context, "不能删除超级管理员用户");
        }

        if (target.IsActive && targetIsSuperAdmin)
        {
            var lastSuperAdminDenied = await EnsureNotLastActiveSuperAdminAsync(
                context, database, "不能删除最后一个启用的超级管理员", ct);
            if (lastSuperAdminDenied is not null)
            {
                return lastSuperAdminDenied;
            }
        }

        var deleteResult = await users.DeleteAsync(id, ct);
        if (deleteResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, deleteResult.ErrorCode, deleteResult.Message);
        }

        logger.LogSecurity("删除用户",
            ("UserId", id),
            ("Username", target.Username),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemUserActionResult("用户已停用并强制下线"));
    }

    /// <summary>POST /api/system/users/{id}/reset-password — 管理员重置密码（6-20 位）。</summary>
    private static async Task<IResult> ResetPasswordAsync(
        int id,
        SystemUserResetPasswordRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IUserService users,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_RESET_PASSWORD, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var newPassword = request.NewPassword ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 6 or > 20)
        {
            return Invalid(context, "新密码长度必须为 6-20 位");
        }

        var (target, targetRoles, loadError) = await LoadTargetUserAsync(
            context, users, dataScope, database, id, currentUserId, ct);
        if (loadError is not null)
        {
            return loadError;
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && targetRoles.Any(r => UserAdminService.IsSuperAdminRole(r.Code)))
        {
            return Conflict(context, "不能重置超级管理员用户的密码");
        }

        var resetResult = await users.ResetPasswordAsync(
            id, newPassword, UserAdminService.GetActorName(context), ct);
        if (resetResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, resetResult.ErrorCode, resetResult.Message);
        }

        logger.LogSecurity("重置用户密码",
            ("UserId", id),
            ("Username", target.Username),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemUserActionResult("密码已重置，该用户需使用新密码重新登录"));
    }

    /// <summary>POST /api/system/users/{id}/toggle-status — 启用/停用用户。</summary>
    private static async Task<IResult> ToggleStatusAsync(
        int id,
        SystemUserToggleStatusRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IUserService users,
        IDataScopeService dataScope,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_MANAGE_STATUS, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (request?.IsActive is not { } requestedActive)
        {
            return Invalid(context, "isActive 参数不能为空");
        }

        var (target, targetRoles, loadError) = await LoadTargetUserAsync(
            context, users, dataScope, database, id, currentUserId, ct);
        if (loadError is not null)
        {
            return loadError;
        }

        var targetIsSuperAdmin = targetRoles.Any(r => UserAdminService.IsSuperAdminRole(r.Code));
        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && targetIsSuperAdmin)
        {
            return Conflict(context, "不能操作超级管理员用户");
        }

        if (!requestedActive && id == currentUserId)
        {
            return Conflict(context, "不能停用当前登录账号");
        }

        if (!requestedActive && target.IsActive && targetIsSuperAdmin)
        {
            var lastSuperAdminDenied = await EnsureNotLastActiveSuperAdminAsync(
                context, database, "不能停用最后一个启用的超级管理员", ct);
            if (lastSuperAdminDenied is not null)
            {
                return lastSuperAdminDenied;
            }
        }

        var statusResult = requestedActive
            ? await users.EnableAsync(id, ct)
            : await users.DisableAsync(id, ct);

        if (statusResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, statusResult.ErrorCode, statusResult.Message);
        }

        logger.LogSecurity("切换用户状态",
            ("UserId", id),
            ("IsActive", requestedActive),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemUserActionResult(requestedActive ? "用户已启用" : "用户已停用并强制下线"));
    }

    // ───────────────────────────── 角色与权限 ─────────────────────────────

    /// <summary>GET /api/system/roles — 角色列表（含用户数聚合）。</summary>
    private static async Task<IResult> GetRolesAsync(
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        IDatabaseService database,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.ROLE_VIEW, ct) is { } denied)
        {
            return denied;
        }

        var allRoles = await roles.GetAllRolesAsync(ct);

        const string countSql = @"
SELECT role_id, COUNT(*)::int AS user_count
FROM nc_sys_user_roles
GROUP BY role_id";
        var countResult = await database.QueryAsync<RoleUserCountRow>(countSql, ct);
        if (countResult.IsFailure)
        {
            return ApiErrors.FromResult(context, countResult);
        }

        var counts = (countResult.Value ?? new List<RoleUserCountRow>())
            .GroupBy(row => row.RoleId)
            .ToDictionary(group => group.Key, group => group.First().UserCount);

        var items = allRoles
            .Select(role => new SystemRoleDto(
                role.Id,
                role.Name,
                role.Code,
                role.Description,
                role.Level,
                role.DataScope,
                counts.GetValueOrDefault(role.Id)))
            .ToList();

        return Results.Json(items);
    }

    /// <summary>POST /api/system/roles — 新增角色，code 自动转大写。</summary>
    private static async Task<IResult> CreateRoleAsync(
        SystemRoleCreateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        IDataScopeService dataScope,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.ROLE_MANAGE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Invalid(context, "角色名称不能为空");
        }

        if (name.Length > 100)
        {
            return Invalid(context, "角色名称长度不能超过 100 个字符");
        }

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length == 0)
        {
            return Invalid(context, "角色代码不能为空");
        }

        if (code.Length > 50)
        {
            return Invalid(context, "角色代码长度不能超过 50 个字符");
        }

        if (code.Any(char.IsWhiteSpace))
        {
            return Invalid(context, "角色代码不能包含空白字符");
        }

        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length > 2000)
        {
            return Invalid(context, "角色描述长度不能超过 2000 个字符");
        }

        var level = request.Level ?? 0;
        if (level is < 0 or > 999)
        {
            return Invalid(context, "角色等级必须在 0-999 之间");
        }

        var dataScopeValue = NormalizeDataScope(request.DataScope);
        if (dataScopeValue is null)
        {
            return Invalid(context, "数据范围无效，可选值：SELF / ORG / ORG_AND_CHILDREN / ALL");
        }

        var isSuperAdmin = await dataScope.IsSuperAdminAsync(currentUserId, ct);
        if (!isSuperAdmin && UserAdminService.IsSuperAdminRole(code))
        {
            return Conflict(context, "不能创建超级管理员角色");
        }

        var existing = await roles.GetRoleByCodeAsync(code, ct);
        if (existing is not null)
        {
            return Conflict(context, "角色代码已存在");
        }

        var createResult = await roles.CreateRoleAsync(new RoleCreateRequest
        {
            Name = name,
            Code = code,
            Description = description,
            Level = level,
            DataScope = dataScopeValue,
            CreatedBy = UserAdminService.GetActorName(context)
        }, ct);

        if (createResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, createResult.ErrorCode, createResult.Message);
        }

        logger.LogSecurity("创建角色",
            ("RoleId", createResult.Value),
            ("Code", code),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(
            new SystemRoleCreatedResponse(createResult.Value, "角色已创建"),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>PUT /api/system/roles/{id} — 编辑角色；数据范围/等级变化时失效缓存并踢出该角色用户。</summary>
    private static async Task<IResult> UpdateRoleAsync(
        int id,
        SystemRoleUpdateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        IDataScopeService dataScope,
        IUserSessionService sessions,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.ROLE_MANAGE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var existing = await roles.GetRoleByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(context, "角色不存在");
        }

        var isSuperAdmin = await dataScope.IsSuperAdminAsync(currentUserId, ct);
        if (!isSuperAdmin && UserAdminService.IsSuperAdminRole(existing.Code))
        {
            return Conflict(context, "不能编辑超级管理员角色");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Invalid(context, "角色名称不能为空");
        }

        if (name.Length > 100)
        {
            return Invalid(context, "角色名称长度不能超过 100 个字符");
        }

        var description = request.Description is null
            ? existing.Description
            : request.Description.Trim();
        if (description.Length > 2000)
        {
            return Invalid(context, "角色描述长度不能超过 2000 个字符");
        }

        var level = request.Level ?? existing.Level;
        if (level is < 0 or > 999)
        {
            return Invalid(context, "角色等级必须在 0-999 之间");
        }

        string dataScopeValue;
        if (string.IsNullOrWhiteSpace(request.DataScope))
        {
            dataScopeValue = existing.DataScope;
        }
        else
        {
            dataScopeValue = NormalizeDataScope(request.DataScope)
                ?? string.Empty;
            if (dataScopeValue.Length == 0)
            {
                return Invalid(context, "数据范围无效，可选值：SELF / ORG / ORG_AND_CHILDREN / ALL");
            }
        }

        var accessBoundaryChanged =
            !string.Equals(existing.DataScope, dataScopeValue, StringComparison.OrdinalIgnoreCase)
            || existing.Level != level;

        var updateResult = await roles.UpdateRoleAsync(new RoleUpdateRequest
        {
            RoleId = id,
            Name = name,
            Description = description,
            Level = level,
            DataScope = dataScopeValue,
            UpdatedBy = UserAdminService.GetActorName(context)
        }, ct);

        if (updateResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, updateResult.ErrorCode, updateResult.Message);
        }

        var message = "角色已更新";
        if (accessBoundaryChanged)
        {
            await permissions.InvalidateAllCacheAsync();
            await dataScope.InvalidateAllCacheAsync();

            var userIdsResult = await UserAdminService.GetRoleUserIdsAsync(database, id, ct);
            if (userIdsResult.IsFailure)
            {
                logger.Warn($"角色数据范围变更后查询用户失败: RoleId={id}, {userIdsResult.ErrorCode}");
            }
            else
            {
                var kicked = await UserAdminService.KickUsersAsync(
                    sessions,
                    logger,
                    userIdsResult.Value,
                    currentUserId,
                    context.GetSessionId(),
                    ct);
                message = $"角色已更新，影响 {userIdsResult.Value.Count} 个用户，已结束 {kicked} 个在线会话";
            }
        }

        logger.LogSecurity("编辑角色",
            ("RoleId", id),
            ("DataScope", dataScopeValue),
            ("Level", level),
            ("AccessBoundaryChanged", accessBoundaryChanged),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemRoleActionResult(message));
    }

    /// <summary>DELETE /api/system/roles/{id} — 删除角色；SUPER_ADMIN 禁止删除，被占用返回 409。</summary>
    private static async Task<IResult> DeleteRoleAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.ROLE_DELETE, ct) is { } denied)
        {
            return denied;
        }

        var existing = await roles.GetRoleByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(context, "角色不存在");
        }

        if (UserAdminService.IsSuperAdminRole(existing.Code))
        {
            return Conflict(context, "超级管理员角色禁止删除");
        }

        var deleteResult = await roles.DeleteRoleAsync(id, ct);
        if (deleteResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, deleteResult.ErrorCode, deleteResult.Message);
        }

        logger.LogSecurity("删除角色",
            ("RoleId", id),
            ("Code", existing.Code),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemRoleActionResult("角色已删除"));
    }

    /// <summary>GET /api/system/roles/{id}/permissions — 角色已有权限码。</summary>
    private static async Task<IResult> GetRolePermissionsAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.ROLE_VIEW, ct) is { } denied)
        {
            return denied;
        }

        var existing = await roles.GetRoleByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(context, "角色不存在");
        }

        var result = await roles.GetRolePermissionsAsync(id, ct);
        if (result.IsFailure)
        {
            return UserAdminService.WriteFailure(context, result.ErrorCode, result.Message);
        }

        var codes = (result.Value ?? Array.Empty<string>())
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        return Results.Json(new SystemRolePermissionsResponse(id, codes));
    }

    /// <summary>PUT /api/system/roles/{id}/permissions — 保存角色权限并踢出该角色全部用户会话。</summary>
    private static async Task<IResult> SaveRolePermissionsAsync(
        int id,
        SystemRolePermissionsUpdateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IRoleService roles,
        IDataScopeService dataScope,
        IUserSessionService sessions,
        IDatabaseService database,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.ROLE_MANAGE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (request?.Permissions is null)
        {
            return Invalid(context, "permissions 参数不能为空");
        }

        var existing = await roles.GetRoleByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(context, "角色不存在");
        }

        var isSuperAdmin = await dataScope.IsSuperAdminAsync(currentUserId, ct);
        if (!isSuperAdmin && UserAdminService.IsSuperAdminRole(existing.Code))
        {
            return Conflict(context, "不能修改超级管理员角色的权限");
        }

        var definitions = await permissions.GetAllPermissionDefinitionsAsync(ct);
        var canonicalCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            canonicalCodes[definition.Code] = definition.Code;
        }

        var normalizedPermissions = new List<string>();
        foreach (var rawCode in request.Permissions)
        {
            var code = rawCode?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                continue;
            }

            if (!canonicalCodes.TryGetValue(code, out var canonicalCode))
            {
                return Invalid(context, $"权限码不存在：{code}");
            }

            normalizedPermissions.Add(canonicalCode);
        }

        normalizedPermissions = normalizedPermissions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var saveResult = await roles.SyncRolePermissionsAsync(id, normalizedPermissions, ct);
        if (saveResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, saveResult.ErrorCode, saveResult.Message);
        }

        await permissions.InvalidateAllCacheAsync();
        await dataScope.InvalidateAllCacheAsync();

        var message = "权限已保存";
        var userIdsResult = await UserAdminService.GetRoleUserIdsAsync(database, id, ct);
        if (userIdsResult.IsFailure)
        {
            logger.Warn($"角色权限保存后查询用户失败: RoleId={id}, {userIdsResult.ErrorCode}");
        }
        else
        {
            var kicked = await UserAdminService.KickUsersAsync(
                sessions,
                logger,
                userIdsResult.Value,
                currentUserId,
                context.GetSessionId(),
                ct);
            message = $"权限已保存，影响 {userIdsResult.Value.Count} 个用户，已结束 {kicked} 个在线会话";
        }

        logger.LogSecurity("保存角色权限",
            ("RoleId", id),
            ("Code", existing.Code),
            ("PermissionCount", normalizedPermissions.Count),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemRoleActionResult(message));
    }

    /// <summary>GET /api/system/permissions — 权限目录（按 sortOrder 排序）。</summary>
    private static async Task<IResult> GetPermissionsAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.ROLE_VIEW, ct) is { } denied)
        {
            return denied;
        }

        var definitions = await permissions.GetAllPermissionDefinitionsAsync(ct);
        var items = definitions
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Code, StringComparer.Ordinal)
            .Select(definition => new SystemPermissionDefinitionDto(
                definition.Code,
                definition.Name,
                definition.Module,
                definition.Description,
                definition.SortOrder))
            .ToList();

        return Results.Json(items);
    }

    // ───────────────────────────── 在线会话 ─────────────────────────────

    /// <summary>GET /api/system/sessions — 数据范围内的活动会话，按最近活跃倒序。</summary>
    private static async Task<IResult> GetSessionsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IUserSessionService sessions,
        IDataScopeService dataScope,
        IDatabaseService database,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && scope.OrganizationIds.Count == 0)
        {
            return Results.Json(new SystemOnlineSessionsResponse(
                Array.Empty<SystemOnlineSessionDto>(), 0));
        }

        var result = await sessions.ListActiveAsync(
            scope.IsSuperAdmin ? null : scope.OrganizationIds, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var records = result.Value ?? new List<Models.Entities.UserSessionRecord>();
        var roleMapResult = await UserAdminService.GetRolesForUsersAsync(
            database, records.Select(record => record.UserId).Distinct().ToList(), ct);
        if (roleMapResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, roleMapResult.ErrorCode, roleMapResult.Message);
        }

        var roleMap = roleMapResult.Value;
        var items = records
            .OrderByDescending(record => record.LastSeenAt)
            .ThenByDescending(record => record.LoginAt)
            .Select(record =>
            {
                var roleName = roleMap.TryGetValue(record.UserId, out var roles) && roles.Count > 0
                    ? string.Join("、", roles.Select(role => role.Name))
                    : string.Empty;

                return new SystemOnlineSessionDto(
                    record.SessionId,
                    record.UserId,
                    record.Username,
                    record.FullName,
                    record.OrganizationName ?? string.Empty,
                    roleName,
                    record.DeviceKind,
                    record.DeviceName,
                    record.AppVersion,
                    record.LoginAt,
                    record.LastSeenAt,
                    record.IsOnline,
                    record.Status);
            })
            .ToList();

        return Results.Json(new SystemOnlineSessionsResponse(items, items.Count));
    }

    /// <summary>POST /api/system/sessions/{sessionId}/kick — 强制单个会话下线。</summary>
    private static async Task<IResult> KickSessionAsync(
        Guid sessionId,
        HttpContext context,
        INewPermissionService permissions,
        IUserSessionService sessions,
        IDataScopeService dataScope,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_MANAGE_STATUS, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        if (sessionId == Guid.Empty)
        {
            return NotFound(context, "会话不存在或已失效");
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && scope.OrganizationIds.Count == 0)
        {
            return NotFound(context, "会话不存在或已失效");
        }

        var listResult = await sessions.ListActiveAsync(
            scope.IsSuperAdmin ? null : scope.OrganizationIds, ct);
        if (listResult.IsFailure)
        {
            return ApiErrors.FromResult(context, listResult);
        }

        var target = (listResult.Value ?? new List<Models.Entities.UserSessionRecord>())
            .FirstOrDefault(record => record.SessionId == sessionId);
        if (target is null)
        {
            return NotFound(context, "会话不存在或已失效");
        }

        var revokeResult = await sessions.RevokeAsync(
            sessionId, SessionConstants.EndReasonAdminKick, currentUserId, ct);
        if (revokeResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, revokeResult.ErrorCode, revokeResult.Message);
        }

        logger.LogSecurity("管理员强制下线会话",
            ("TargetUserId", target.UserId),
            ("SessionId", sessionId),
            ("DeviceKind", target.DeviceKind),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemUserActionResult("已强制下线"));
    }

    /// <summary>POST /api/system/sessions/kick-all — 数据范围内全部下线（保留当前会话）。</summary>
    private static async Task<IResult> KickAllAsync(
        HttpContext context,
        INewPermissionService permissions,
        IUserSessionService sessions,
        IDataScopeService dataScope,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await RequireAsync(context, permissions, PermissionCodes.USER_MANAGE_STATUS, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } currentUserId)
        {
            return Unauthorized(context);
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!scope.IsSuperAdmin && scope.OrganizationIds.Count == 0)
        {
            return Results.Json(new SystemKickAllSessionsResponse("当前管理范围内没有在线会话", 0));
        }

        var revokeResult = await sessions.RevokeAllActiveAsync(
            SessionConstants.EndReasonAdminKick,
            currentUserId,
            context.GetSessionId(),
            scope.IsSuperAdmin ? null : scope.OrganizationIds,
            ct);

        if (revokeResult.IsFailure)
        {
            return UserAdminService.WriteFailure(context, revokeResult.ErrorCode, revokeResult.Message);
        }

        logger.LogSecurity("管理员全部强制下线",
            ("Count", revokeResult.Value),
            ("Operator", UserAdminService.GetActorName(context)));

        return Results.Json(new SystemKickAllSessionsResponse(
            $"已结束 {revokeResult.Value} 个会话",
            revokeResult.Value));
    }

    // ───────────────────────────── 共享校验 / 映射 ─────────────────────────────

    /// <summary>加载目标用户并校验数据范围（越权返回 404，与「不存在」不可区分）。</summary>
    private static async Task<(User User, List<SystemUserRoleDto> Roles, IResult? Error)> LoadTargetUserAsync(
        HttpContext context,
        IUserService users,
        IDataScopeService dataScope,
        IDatabaseService database,
        int userId,
        int currentUserId,
        CancellationToken ct)
    {
        if (userId <= 0)
        {
            return (null!, new List<SystemUserRoleDto>(), NotFound(context, "用户不存在"));
        }

        var userResult = await users.GetByIdAsync(userId, ct);
        if (userResult.IsFailure)
        {
            return (null!, new List<SystemUserRoleDto>(),
                UserAdminService.WriteFailure(context, userResult.ErrorCode, userResult.Message));
        }

        if (!userResult.HasValue || userResult.Value is not { } user)
        {
            return (null!, new List<SystemUserRoleDto>(), NotFound(context, "用户不存在"));
        }

        var scope = await UserAdminService.ResolveScopeAsync(dataScope, currentUserId, ct);
        if (!UserAdminService.IsUserInScope(scope, user))
        {
            return (null!, new List<SystemUserRoleDto>(), NotFound(context, "用户不存在"));
        }

        var rolesResult = await UserAdminService.GetRolesForUsersAsync(database, new[] { user.Id }, ct);
        if (rolesResult.IsFailure)
        {
            return (null!, new List<SystemUserRoleDto>(),
                UserAdminService.WriteFailure(context, rolesResult.ErrorCode, rolesResult.Message));
        }

        var roles = rolesResult.Value.TryGetValue(user.Id, out var userRoles)
            ? userRoles
            : new List<SystemUserRoleDto>();

        return (user, roles, null);
    }

    /// <summary>停用/删除超管前确认不是最后一个启用的超管。</summary>
    private static async Task<IResult?> EnsureNotLastActiveSuperAdminAsync(
        HttpContext context,
        IDatabaseService database,
        string message,
        CancellationToken ct)
    {
        var countResult = await UserAdminService.CountActiveSuperAdminsAsync(database, ct);
        if (countResult.IsFailure)
        {
            return ApiErrors.FromResult(context, countResult);
        }

        return countResult.Value <= 1 ? Conflict(context, message) : null;
    }

    /// <summary>校验要分配的角色 id 是否全部存在。</summary>
    private static async Task<(string? Error, List<RoleEntity> Roles)> ResolveRolesAsync(
        IRoleService roles,
        IReadOnlyCollection<int> roleIds,
        CancellationToken ct)
    {
        if (roleIds.Count == 0)
        {
            return (null, new List<RoleEntity>());
        }

        var allRoles = await roles.GetAllRolesAsync(ct);
        var roleMap = allRoles.ToDictionary(role => role.Id);
        var selected = new List<RoleEntity>();

        foreach (var roleId in roleIds.Distinct())
        {
            if (roleId <= 0)
            {
                return ("角色 ID 无效", new List<RoleEntity>());
            }

            if (!roleMap.TryGetValue(roleId, out var role))
            {
                return ($"角色不存在：{roleId}", new List<RoleEntity>());
            }

            selected.Add(role);
        }

        return (null, selected);
    }

    /// <summary>校验组织是否在当前用户可管理范围内（非超管越权 403）。</summary>
    private static IResult? ValidateOrganizationScope(
        HttpContext context,
        UserAdminScope scope,
        int? organizationId)
    {
        if (organizationId is null)
        {
            return null;
        }

        if (organizationId <= 0)
        {
            return Invalid(context, "组织 ID 无效");
        }

        if (!scope.IsSuperAdmin && !scope.OrganizationIds.Contains(organizationId.Value))
        {
            return Forbidden(context, "无权管理该组织");
        }

        return null;
    }

    private static SystemUserDto MapUser(
        User user,
        IReadOnlyList<SystemUserRoleDto> roles,
        bool canBeManaged)
        => new(
            user.Id,
            user.Username,
            user.FullName,
            user.Phone,
            user.IdentityCard,
            user.Position,
            user.OrganizationId,
            user.OrganizationName,
            user.IsActive,
            roles,
            canBeManaged,
            user.CreatedAt);

    private static bool TryParseStatus(string? status, out bool? isActive)
    {
        switch ((status ?? "all").Trim().ToLowerInvariant())
        {
            case "":
            case "all":
                isActive = null;
                return true;
            case "active":
                isActive = true;
                return true;
            case "inactive":
                isActive = false;
                return true;
            default:
                isActive = null;
                return false;
        }
    }

    private static string? NormalizeDataScope(string? dataScope)
    {
        if (string.IsNullOrWhiteSpace(dataScope))
        {
            return DataScopeConstants.SELF;
        }

        var normalized = dataScope.Trim().ToUpperInvariant();
        return DataScopeConstants.OrderedScopes.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : null;
    }

    private static IResult EmptyUserPage(int pageIndex, int pageSize)
        => Results.Json(new SystemUserPageResponse(
            Array.Empty<SystemUserDto>(), 0, pageIndex, pageSize));

    private static Task<IResult?> RequireAsync(
        HttpContext context,
        INewPermissionService permissions,
        string permissionCode,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(context, permissions, permissionCode, ct);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict, ApiErrorCodes.ValidationError, message);

    private static IResult Forbidden(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, message);

    private sealed class RoleUserCountRow
    {
        public int RoleId { get; set; }
        public int UserCount { get; set; }
    }
}
