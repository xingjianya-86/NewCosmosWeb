namespace NewCosmos.Server.Contracts;

#region 字典管理

/// <summary>
/// 字典分类输出：对应 Core <c>DictCategoryView</c>。
/// Status=seed/updated；Source=user/system；Action=add/update/delete。
/// UpdatedAt/UpdatedBy 对种子数据为空（Core 视图里是 0001-01-01 / 0，这里归一为 null）。
/// </summary>
public sealed record DictCategoryDto(
    int Id,
    string Category,
    string DisplayName,
    string Description,
    int SortOrder,
    bool IsActive,
    string Status,
    string Source,
    string Action,
    DateTime? UpdatedAt,
    int? UpdatedBy);

/// <summary>字典项输出：对应 Core <c>DictItemView</c>。</summary>
public sealed record DictItemDto(
    int Id,
    string Category,
    string ItemKey,
    string ItemValue,
    int SortOrder,
    bool IsActive,
    string Description,
    string Status,
    string Source,
    string Action,
    DateTime? UpdatedAt,
    int? UpdatedBy);

/// <summary>
/// 字典分类新增/编辑请求：对应 Core <c>DictCategoryUpdate</c> 的可写字段。
/// Id/Source/Action/CreatedAt/CreatedBy/UpdatedAt 由服务端赋值，不接受前端传入。
/// </summary>
public sealed record DictCategoryRequest(
    string? Category,
    string? DisplayName,
    string? Description,
    int? SortOrder,
    bool? IsActive);

/// <summary>
/// 字典项新增/编辑请求：对应 Core <c>DictItemUpdate</c> 的可写字段。
/// Id/Source/Action/CreatedAt/CreatedBy/UpdatedAt 由服务端赋值，不接受前端传入。
/// </summary>
public sealed record DictItemRequest(
    string? Category,
    string? ItemKey,
    string? ItemValue,
    int? SortOrder,
    bool? IsActive,
    string? Description);

/// <summary>新增字典分类/字典项结果：返回服务端分配的 Id。</summary>
public sealed record DictionaryCreatedResponse(int Id, string Message);

/// <summary>字典写操作结果（编辑/删除/同步/初始化）。</summary>
public sealed record DictionaryActionResult(string Message);

#endregion
