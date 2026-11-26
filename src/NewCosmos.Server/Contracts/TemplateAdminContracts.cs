namespace NewCosmos.Server.Contracts;

/// <summary>新增自定义分类节点请求（parentPath 为空表示挂在根下）。</summary>
public sealed record TemplateCategoryCreateRequest(string? ParentPath, string? Label);

/// <summary>重命名自定义分类节点请求。</summary>
public sealed record TemplateCategoryUpdateRequest(string? Label);
