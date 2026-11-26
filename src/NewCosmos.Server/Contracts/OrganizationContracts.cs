namespace NewCosmos.Server.Contracts;

#region 经办机构管理

/// <summary>组织树响应：节点为当前用户数据范围内的树（父节点不在范围内时该节点按顶级返回）。</summary>
public sealed record OrganizationTreeResponse(
    List<OrganizationTreeNodeDto> Nodes,
    OrganizationStatsDto Stats);

/// <summary>组织树节点：对应 Core <c>OrganizationTreeNode</c>。</summary>
public sealed record OrganizationTreeNodeDto(
    int Id,
    string Name,
    string Abbreviation,
    int Depth,
    List<OrganizationTreeNodeDto> Children);

/// <summary>
/// 组织树统计：
/// <para>totalOrganizations=范围内机构总数；totalUsers=范围内机构关联用户数；</para>
/// <para>parentOrganizations=存在（范围内）下级的机构数；rootOrganizations=返回树中的顶级机构数；</para>
/// <para>specialCareInstitutionCount=范围内特困供养机构数。</para>
/// </summary>
public sealed record OrganizationStatsDto(
    int TotalOrganizations,
    int TotalUsers,
    int ParentOrganizations,
    int RootOrganizations,
    int SpecialCareInstitutionCount);

/// <summary>
/// 组织详情：字段与 PC <c>Organization</c> 对齐（区划名称由 Core 查询 JOIN 带出）。
/// UserCount 为该组织下的用户数（<c>IOrganizationService.GetUserCountAsync</c>）。
/// </summary>
public sealed record OrganizationDetailDto(
    int Id,
    string Name,
    string Abbreviation,
    string Principal,
    string PrincipalIdentityCard,
    string Phone,
    string Address,
    string PostalCode,
    int? ParentId,
    string ParentName,
    string InstitutionNature,
    string InstitutionType,
    string InstitutionCategory,
    bool IsSpecialCareInstitution,
    int? ProvinceId,
    int? CityId,
    int? CountyId,
    int? TownId,
    int? VillageId,
    string ProvinceName,
    string CityName,
    string CountyName,
    string TownName,
    string VillageName,
    int UserCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>上级组织下拉项（仅在当前用户可管理组织范围内）。</summary>
public sealed record OrganizationOptionDto(
    int Id,
    string Name,
    int? ParentId);

/// <summary>
/// 机构页下拉数据：可管理组织 + 机构编辑所需字典。
/// 机构页用户不一定拥有 DICTIONARY_VIEW，因此由本接口一并返回，避免前端另调字典接口。
/// 三个字典数组取启用项的 ItemKey，按 SortOrder 排序。
/// </summary>
public sealed record OrganizationOptionsResponse(
    List<OrganizationOptionDto> Organizations,
    List<string> InstitutionNatures,
    List<string> InstitutionTypes,
    List<string> InstitutionCategories);

/// <summary>
/// 组织新增/编辑请求：对应 Core <c>OrganizationSaveRequest</c> 的可写字段。
/// Id/审计字段由服务端赋值，不接受前端传入。
/// </summary>
public sealed record OrganizationSaveInput(
    string? Name,
    string? Abbreviation,
    string? Principal,
    string? PrincipalIdentityCard,
    string? Phone,
    string? Address,
    string? PostalCode,
    int? ParentId,
    string? InstitutionNature,
    string? InstitutionType,
    string? InstitutionCategory,
    bool IsSpecialCareInstitution,
    int? ProvinceId,
    int? CityId,
    int? CountyId,
    int? TownId,
    int? VillageId);

/// <summary>组织创建结果：返回新组织 Id。</summary>
public sealed record OrganizationCreatedResponse(int Id, string Message);

/// <summary>组织写操作结果（编辑/删除）。</summary>
public sealed record OrganizationActionResult(string Message);

/// <summary>组织导入预览响应：对应 Core <c>OrganizationCsvPreview</c>。</summary>
public sealed record OrganizationPreviewResponse(
    int TotalRows,
    List<OrganizationPreviewRowDto> PreviewRows,
    string? Error);

/// <summary>预览行：对应 Core <c>OrganizationPreviewRow</c>。</summary>
public sealed record OrganizationPreviewRowDto(
    int RowNumber,
    string Name,
    string Abbreviation,
    string Principal,
    string Phone,
    string Address,
    string Region);

/// <summary>组织导入结果：对应 Core <c>ImportResult</c> 的组织相关字段。</summary>
public sealed record OrganizationImportResponse(
    bool Success,
    int ImportedCount,
    int ErrorCount,
    List<string> Errors,
    List<string> Warnings,
    string Message);

#endregion
