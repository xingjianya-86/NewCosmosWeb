namespace NewCosmos.Server.Contracts;

#region 标准配置管理

/// <summary>
/// 标准配置输出：对应 Core <c>ConfigStandard</c>（按需求只暴露页面用到的字段）。
/// EffectiveStartDate/EffectiveEndDate 为日期（date 列），时间部分无意义。
/// </summary>
public sealed record StandardConfigDto(
    long Id,
    string StandardType,
    string StandardName,
    string? HukouType,
    string? SupportMode,
    decimal StandardValue,
    string Unit,
    DateTime EffectiveStartDate,
    DateTime? EffectiveEndDate,
    int Version,
    bool IsActive,
    string? Description,
    DateTime? UpdatedAt);

/// <summary>
/// 标准配置新增/编辑请求：对应 Core <c>ConfigStandard</c> 的可写字段。
/// Id/CreatedBy/CreatedAt/UpdatedAt 由服务端赋值；Version 为乐观并发版本号（编辑时回传列表值）。
/// StandardType 仅在新增时生效，编辑接口不修改标准类型。
/// </summary>
public sealed record StandardConfigRequest(
    string? StandardType,
    string? StandardName,
    string? HukouType,
    string? SupportMode,
    decimal? StandardValue,
    string? Unit,
    DateTime? EffectiveStartDate,
    DateTime? EffectiveEndDate,
    int? Version,
    bool? IsActive,
    string? Description);

/// <summary>标准配置总览：对应 Core <c>StandardConfigOverview</c>，全部字段原样返回。</summary>
public sealed record StandardOverviewDto(
    int IncomeStandardsCount,
    int SubsidyStandardsCount,
    int DestituteStandardsCount,
    int SystemConfigsCount,
    decimal RuralIncomeStandard,
    decimal UrbanIncomeStandard,
    decimal RuralSubsidyAmount,
    decimal UrbanSubsidyAmount,
    int ConfigStandardsCount);

/// <summary>新增标准配置结果：返回数据库自增 Id。</summary>
public sealed record StandardCreatedResponse(long Id, string Message);

/// <summary>标准配置写操作结果（编辑/删除/同步/初始化）。</summary>
public sealed record StandardActionResult(string Message);

#endregion
