using NewCosmos.Models.Entities;

namespace NewCosmos.Server.Contracts;

#region 高龄津贴发放（只读查询）

/// <summary>
/// 高龄模块概览。统计口径全部来自 Core；某一统计块失败时对应字段为 null，前端显示为「—」。
/// 命名避开首页 DashboardContracts 的 ElderlySummaryDto（四个统计块共用类型，字段不可空）。
/// </summary>
public sealed record ElderlyModuleSummaryDto(
    int? ActiveCount,
    int? MonthlyNew,
    int? PendingNew,
    int? PendingTransfer);

/// <summary>高龄登记列表项。身份证强制脱敏，完整证件只在详情返回。</summary>
public sealed record ElderlyApplicationListItemDto(
    long Id,
    string ApplicationNo,
    string Name,
    string IdCardMasked,
    string Gender,
    DateOnly? BirthDate,
    int? Age,
    string Category,
    string CategoryText,
    string Status,
    string StatusText,
    DateTime? ApplyDate,
    DateTime? ConfirmedAt,
    string IssueStartMonth,
    decimal IssueAmount);

public sealed record ElderlyApplicationListResponse(
    IReadOnlyList<ElderlyApplicationListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>高龄登记详情（只读）。完整身份证/银行卡仅限本接口，不写日志。</summary>
public sealed record ElderlyApplicationDetailDto(
    long Id,
    string ApplicationNo,
    string Name,
    string IdCard,
    string Gender,
    DateOnly? BirthDate,
    int? Age,
    string Phone,
    string HukouAddress,
    string HukouVillage,
    string HukouDetailAddress,
    string FamilyAddress,
    string DetailAddress,
    string Category,
    string CategoryText,
    bool IsCategoryManual,
    string IdentityFlag,
    string IdentitySource,
    decimal IssueAmount,
    string IssueStartMonth,
    string BankName,
    string BankAccount,
    string AgentName,
    string AgentRelation,
    string AgentReceiveName,
    string AgentReceiveRelation,
    string AgentReceiveBankName,
    string AgentReceiveBankAccount,
    string AgentReceiveReason,
    string PaybackStartMonth,
    string PaybackEndMonth,
    int PaybackMonths,
    decimal PaybackAmount,
    string PaybackReason,
    bool IsSpecialCase,
    string SpecialReason,
    string Status,
    string StatusText,
    DateTime? ApplyDate,
    DateTime? ConfirmedAt,
    string ConfirmedBy,
    DateTime? DueStopDate,
    DateTime? ActualStopDate,
    DateTime? StoppedAt,
    string StoppedBy,
    string StopReason,
    string StopReasonText,
    bool IsRecover,
    string RecoverStartMonth,
    string RecoverEndMonth,
    decimal RecoverAmount,
    string Remark,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

/// <summary>下月待办明细项（姓名/身份证脱敏）。</summary>
public sealed record ElderlyPendingItemDto(
    string IdCardMasked,
    string Name,
    int Age,
    string Type,
    string TypeText,
    string CurrentCategory,
    string CurrentCategoryText,
    string SuggestedAction,
    string StatusLabel,
    string MemberRole,
    string Gender,
    DateOnly? BirthDate,
    long ElderlyApplicationId,
    long ReviewId,
    string SourceApplicationNo,
    string ElderlyApplicationNo);

public sealed record ElderlyPendingResponse(
    int PendingNew,
    int PendingTransfer,
    IReadOnlyList<ElderlyPendingItemDto> Items);

/// <summary>高龄类别复核列表项。</summary>
public sealed record ElderlyReviewListItemDto(
    long Id,
    string ReviewNo,
    string Name,
    string IdCardMasked,
    string TriggerSource,
    string TriggerText,
    string TriggerReason,
    string OldCategory,
    string OldCategoryText,
    string NewCategory,
    string NewCategoryText,
    string CategoryChangeText,
    string ReviewResult,
    string ResultText,
    string Status,
    string StatusText,
    string EffectiveMonth,
    string IssueStartMonth,
    string ReviewedBy,
    DateTime? ReviewedAt,
    DateTime? CreatedAt);

public sealed record ElderlyReviewListResponse(
    IReadOnlyList<ElderlyReviewListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

#endregion

#region 高龄津贴登记（表单/写接口/评估/导入名册/OCR）

/// <summary>
/// 高龄登记表单请求体（创建/更新共用）。
/// 字段与官方登记表一一对应；评估结果、补发分段、区划 ID 均由服务端重新计算，不接受前端写入。
/// </summary>
public sealed class ElderlyApplicationFormRequest
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public DateTime? BirthDate { get; set; }
    public DateTime? ApplyDate { get; set; }

    public string HukouProvince { get; set; } = string.Empty;
    public string HukouCity { get; set; } = string.Empty;
    public string HukouCounty { get; set; } = string.Empty;
    public string HukouTown { get; set; } = string.Empty;
    public string HukouVillage { get; set; } = string.Empty;
    public string HukouDetailAddress { get; set; } = string.Empty;

    public string FamilyProvince { get; set; } = string.Empty;
    public string FamilyCity { get; set; } = string.Empty;
    public string FamilyCounty { get; set; } = string.Empty;
    public string FamilyTown { get; set; } = string.Empty;
    public string FamilyVillage { get; set; } = string.Empty;
    public string DetailAddress { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;
    public string BankAccount { get; set; } = string.Empty;

    public string AgentName { get; set; } = string.Empty;
    public string AgentRelation { get; set; } = string.Empty;
    public string AgentReceiveName { get; set; } = string.Empty;
    public string AgentReceiveRelation { get; set; } = string.Empty;
    public string AgentReceiveBankName { get; set; } = string.Empty;
    public string AgentReceiveBankAccount { get; set; } = string.Empty;
    public string AgentReceiveReason { get; set; } = string.Empty;

    /// <summary>享受类别（IsCategoryManual=true 时生效）</summary>
    public string Category { get; set; } = string.Empty;
    public bool IsCategoryManual { get; set; }

    /// <summary>补发起算月（yyyy-MM，特殊情况人工指定）</summary>
    public string PaybackStartMonth { get; set; } = string.Empty;

    /// <summary>补发止算月（yyyy-MM，特殊情况人工指定）</summary>
    public string PaybackEndMonth { get; set; } = string.Empty;
    public string PaybackReason { get; set; } = string.Empty;
    public string SpecialReason { get; set; } = string.Empty;
    public bool IsSpecialCase { get; set; }
}

/// <summary>
/// 高龄登记表单回填：档案全字段 + 户籍/家庭四级区划名称 + 补发分段明细。
/// </summary>
public sealed class ElderlyApplicationFormResponse
{
    public ElderlyApplication Application { get; init; } = new();

    public string HukouProvince { get; init; } = string.Empty;
    public string HukouCity { get; init; } = string.Empty;
    public string HukouCounty { get; init; } = string.Empty;
    public string HukouTown { get; init; } = string.Empty;
    public string HukouVillage { get; init; } = string.Empty;

    public string FamilyProvince { get; init; } = string.Empty;
    public string FamilyCity { get; init; } = string.Empty;
    public string FamilyCounty { get; init; } = string.Empty;
    public string FamilyTown { get; init; } = string.Empty;
    public string FamilyVillage { get; init; } = string.Empty;

    public IReadOnlyList<ElderlyPaybackSegment> Segments { get; init; } = Array.Empty<ElderlyPaybackSegment>();
}

/// <summary>新建高龄登记返回：主键 + 申请编号。</summary>
public sealed record ElderlyApplicationCreatedDto(long Id, string ApplicationNo);

/// <summary>特殊情况补发试算请求（人工指定起止月）。</summary>
public sealed record ElderlySpecialEvaluationRequest(
    string? IdCard,
    string? StartMonth,
    string? EndMonth);

/// <summary>身份证 OCR 识别结果：姓名/证号/性别/出生日期/住址 + 原始字段表。</summary>
public sealed record ElderlyOcrResponse(
    string Name,
    string IdCard,
    string Gender,
    DateTime? BirthDate,
    string Address,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>当前账号经办机构区划（高龄表单默认值与户籍锁定）。</summary>
public sealed record ElderlyOperatorAddressDto(
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
    int LockLevel,
    string ScopeDescription,
    IReadOnlyList<ElderlyOperatorVillageDto> Villages);

public sealed record ElderlyOperatorVillageDto(long Id, string Name, string Code);

/// <summary>停发办理请求（当前库在享人员或导入名册建档并停发共用）。</summary>
public sealed class ElderlyStopRequest
{
    public DateTime? DueStopDate { get; init; }

    /// <summary>停发原因 key；选择“其他”时传 OTHER，实际原因取 OtherReasonText。</summary>
    public string? StopReason { get; init; }

    public DateTime? DeathDate { get; init; }

    public string? OtherReasonText { get; init; }

    public bool IsRecover { get; init; }

    public string? RecoverStartMonth { get; init; }

    public string? RecoverEndMonth { get; init; }

    public decimal? RecoverAmount { get; init; }

    public string? Remark { get; init; }
}

#endregion
