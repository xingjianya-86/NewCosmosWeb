namespace NewCosmos.Server.Contracts;

#region 近亲属备案

/// <summary>
/// 工作人员信息（新增/编辑请求与详情响应共用）。
/// CreatedAt/UpdatedAt 中 UpdatedAt 同时承担编辑时的版本号。
/// </summary>
public sealed record NearRelativeStaffDto(
    long Id,
    string StaffName,
    string StaffIdCard,
    string StaffPhone,
    string WorkUnit,
    string Position,
    string Town,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

/// <summary>救助对象明细行；档案来源字段由服务端按申请档案回填。</summary>
public sealed record NearRelativeLinkDto(
    long Id,
    long StaffId,
    long? ApplicationId,
    string Relation,
    string Name,
    string IdCard,
    string Gender,
    string BirthDate,
    string FamilyAddress,
    string HukouAddress,
    string ResidenceAddress,
    int? FamilySize,
    string HelpType,
    decimal? MonthAmount,
    decimal? ReportAmount,
    string StartTime,
    string FamilyRelation,
    string ApplyReason,
    string ApplyTime,
    string FamilyDifficulty,
    string EconomyInvestigate,
    string TownOpinion,
    string DynamicRecord,
    int SortOrder);

/// <summary>完整备案（详情 / 保存后回读）。</summary>
public sealed record NearRelativeEntryDto(
    NearRelativeStaffDto Staff,
    IReadOnlyList<NearRelativeLinkDto> Links);

/// <summary>名册行：身份证统一脱敏；备案时间取最近更新（无则创建时间）。</summary>
public sealed record NearRelativeListItemDto(
    long Id,
    string StaffName,
    string StaffIdCardMasked,
    string WorkUnit,
    string Position,
    string Town,
    int LinkCount,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

/// <summary>名册分页响应；Towns 供乡镇筛选下拉（不随筛选变化）。</summary>
public sealed record NearRelativeListResponse(
    IReadOnlyList<NearRelativeListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    long AllCount,
    int TotalPages,
    IReadOnlyList<string> Towns);

/// <summary>工作人员身份证命中既有有效备案时的摘要。</summary>
public sealed record NearRelativeExistingFilingDto(
    long Id,
    string StaffName,
    int LinkCount);

/// <summary>
/// 工作人员身份证查询：工作信息（系统用户 + 档案照料护理人合并）
/// 与既有备案摘要；未命中任何来源时 Staff 为 null。
/// </summary>
public sealed record NearRelativeStaffLookupResponse(
    NearRelativeStaffDto? Staff,
    bool FoundInUsers,
    bool FoundInCaregivers,
    bool FoundInDestituteArchive,
    bool HasAny,
    NearRelativeExistingFilingDto? Existing);

/// <summary>档案带出：按救助对象身份证 / 申请 ID 映射的对象行与来源提示。</summary>
public sealed record NearRelativeArchiveFillResponse(
    long ApplicationId,
    string ApplicationNo,
    string Status,
    string StatusDisplay,
    DateTime? ApplicationCreatedAt,
    NearRelativeLinkDto Link,
    bool OccupiedByOtherStaff,
    string OccupiedByStaffName);

/// <summary>新增 / 编辑请求体：一名工作人员 + N 名对象（对象行按 name 非空过滤）。</summary>
public sealed record NearRelativeSaveRequest(
    NearRelativeStaffDto? Staff,
    IReadOnlyList<NearRelativeLinkDto>? Links);

/// <summary>保存结果：新增或编辑后的备案 id。</summary>
public sealed record NearRelativeSaveResponse(long Id);

#endregion
