namespace NewCosmos.Server.Contracts;

#region 用户管理

/// <summary>用户列表/详情中的角色摘要。</summary>
public sealed record SystemUserRoleDto(
    int Id,
    string Name,
    string Code);

/// <summary>
/// 用户列表/详情输出：对齐 PC <c>UserManagementPage</c> 与 <c>UserEditorViewModel</c>。
/// <c>CanBeManaged</c> 由服务端按数据范围与 SUPER_ADMIN 保护规则计算，前端不得自行推断。
/// </summary>
public sealed record SystemUserDto(
    int Id,
    string Username,
    string FullName,
    string Phone,
    string IdentityCard,
    string Position,
    int? OrganizationId,
    string OrganizationName,
    bool IsActive,
    IReadOnlyList<SystemUserRoleDto> Roles,
    bool CanBeManaged,
    DateTime CreatedAt);

/// <summary>用户分页响应：total 为满足筛选条件的总记录数。</summary>
public sealed record SystemUserPageResponse(
    IReadOnlyList<SystemUserDto> Items,
    int Total,
    int PageIndex,
    int PageSize);

/// <summary>新增用户请求。角色分配在创建成功后按 roleIds 批量执行，不接受前端传入审计字段。</summary>
public sealed record SystemUserCreateRequest(
    string? Username,
    string? Password,
    string? FullName,
    string? Phone,
    string? IdentityCard,
    string? Position,
    int? OrganizationId,
    List<int>? RoleIds);

/// <summary>编辑用户请求。roleIds 为 null 表示不改角色，空数组表示清空角色。</summary>
public sealed record SystemUserUpdateRequest(
    string? FullName,
    string? Phone,
    string? IdentityCard,
    string? Position,
    int? OrganizationId,
    bool? IsActive,
    List<int>? RoleIds);

/// <summary>管理员重置密码请求（6-20 位）。</summary>
public sealed record SystemUserResetPasswordRequest(string? NewPassword);

/// <summary>启用/停用用户请求。</summary>
public sealed record SystemUserToggleStatusRequest(bool? IsActive);

/// <summary>用户写操作结果（编辑/删除/重置密码/启停）。</summary>
public sealed record SystemUserActionResult(string Message);

/// <summary>用户创建结果。</summary>
public sealed record SystemUserCreatedResponse(int Id, string Message);

/// <summary>用户管理页头统计。</summary>
public sealed record SystemUserStatsResponse(int ActiveUsers, int RoleCount);

/// <summary>角色下拉项。</summary>
public sealed record SystemRoleOptionDto(int Id, string Name, string Code);

/// <summary>组织下拉项（仅当前用户可管理范围）。</summary>
public sealed record SystemOrganizationOptionDto(int Id, string Name);

/// <summary>用户编辑器下拉数据：角色、可管理组织、启用中的岗位字典项。</summary>
public sealed record SystemUserOptionsResponse(
    IReadOnlyList<SystemRoleOptionDto> Roles,
    IReadOnlyList<SystemOrganizationOptionDto> Organizations,
    IReadOnlyList<string> Positions);

/// <summary>当前登录用户的权限码集合与超管标记（登录即可访问）。</summary>
public sealed record SystemMyPermissionsResponse(
    IReadOnlyList<string> Permissions,
    bool IsSuperAdmin);

#endregion

#region 角色与权限

/// <summary>角色列表项：userCount 为 nc_sys_user_roles 聚合值。</summary>
public sealed record SystemRoleDto(
    int Id,
    string Name,
    string Code,
    string Description,
    int Level,
    string DataScope,
    int UserCount);

/// <summary>新增角色请求（code 由服务端统一转大写；level 数值越小权限越高）。</summary>
public sealed record SystemRoleCreateRequest(
    string? Name,
    string? Code,
    string? Description,
    int? Level,
    string? DataScope);

/// <summary>编辑角色请求（code 不可修改）。</summary>
public sealed record SystemRoleUpdateRequest(
    string? Name,
    string? Description,
    int? Level,
    string? DataScope);

/// <summary>角色创建结果。</summary>
public sealed record SystemRoleCreatedResponse(int Id, string Message);

/// <summary>角色写操作结果（编辑/删除/权限保存）。</summary>
public sealed record SystemRoleActionResult(string Message);

/// <summary>角色已有权限码。</summary>
public sealed record SystemRolePermissionsResponse(int RoleId, IReadOnlyList<string> Permissions);

/// <summary>保存角色权限请求；空数组合法（清空权限），null 视为参数错误。</summary>
public sealed record SystemRolePermissionsUpdateRequest(IReadOnlyList<string>? Permissions);

/// <summary>权限定义（对应 Core <c>PermissionDefinition</c>）。</summary>
public sealed record SystemPermissionDefinitionDto(
    string Code,
    string Name,
    string Module,
    string Description,
    int SortOrder);

#endregion

#region 在线会话

/// <summary>
/// 在线会话行：对应 Core <c>UserSessionRecord</c>，roleName 为服务端按用户角色聚合展示。
/// </summary>
public sealed record SystemOnlineSessionDto(
    Guid SessionId,
    int UserId,
    string Username,
    string FullName,
    string OrganizationName,
    string RoleName,
    string DeviceKind,
    string DeviceName,
    string AppVersion,
    DateTime LoginAt,
    DateTime LastSeenAt,
    bool IsOnline,
    string Status);

/// <summary>在线会话列表。</summary>
public sealed record SystemOnlineSessionsResponse(
    IReadOnlyList<SystemOnlineSessionDto> Items,
    int Total);

/// <summary>全部下线结果：count 为实际结束的会话数。</summary>
public sealed record SystemKickAllSessionsResponse(string Message, int Count);

#endregion
