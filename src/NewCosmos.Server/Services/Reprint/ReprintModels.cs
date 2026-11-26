using NewCosmos.Models.Results;

namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 历史补打中心域键（与打印留痕 nc_biz_print_records.business_type、
/// 模板分类解析 <c>ArchiveCategoryResolver.GetReprintDomainCategories</c> 保持一致）。
/// </summary>
public static class ReprintDomains
{
    public const string FamilyApplication = "FamilyApplication";
    public const string TempRelief = "TempRelief";
    public const string ElderlyBenefits = "ElderlyBenefits";
    public const string AssetVerification = "AssetVerification";
    public const string DynamicManagementRecord = "DynamicManagementRecord";

    /// <summary>前端展示顺序（与旧版统一补打中心 Provider 顺序一致）。</summary>
    public static readonly string[] All =
    [
        FamilyApplication,
        TempRelief,
        ElderlyBenefits,
        AssetVerification,
        DynamicManagementRecord
    ];
}

/// <summary>
/// 按月检索时使用的业务时间口径（与旧版 <c>ReprintMonthWindow</c> 对齐）。
/// <para>NaturalMonth=自然月；BusinessProcess=B 线周期；EconomicReview=A 线经济复核周期；
/// TempReliefFull=C 线临时救助批次窗口。</para>
/// </summary>
public enum ReprintMonthWindow
{
    NaturalMonth,
    BusinessProcess,
    EconomicReview,
    TempReliefFull
}

/// <summary>
/// 补打域 Provider：按人/按月检索、取详情、列模板、装配打印数据。
/// 每个域一个实现，域键见 <see cref="ReprintDomains"/>。
/// </summary>
public interface IReprintProvider
{
    /// <summary>域键（= 留痕 business_type）。</summary>
    string DomainKey { get; }

    /// <summary>展示名（低收入人口 / 临时救助 / 普惠高龄 / 资产核查 / 动态管理档案）。</summary>
    string DisplayName { get; }

    /// <summary>补打模式：ArchiveSet / AssetVerification / DynamicRecord（与旧版一致）。</summary>
    string Mode { get; }

    /// <summary>按月检索使用的业务时间窗口。</summary>
    ReprintMonthWindow MonthWindow { get; }

    /// <summary>按姓名/身份证搜索，最多 <paramref name="limit"/> 条（关键词为空 = 最近名单）。</summary>
    Task<Result<List<ReprintRecordItem>>> SearchByPersonAsync(
        string? keyword,
        int limit,
        CancellationToken ct = default);

    /// <summary>按域自身月份窗口检索，单月最多 <paramref name="limit"/> 条。</summary>
    Task<Result<List<ReprintRecordItem>>> SearchByMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct = default);

    /// <summary>按记录 ID 取详情（含打印字段与表格行）。</summary>
    Task<Result<ReprintRecordDetail>> GetDetailAsync(
        long id,
        int userId,
        CancellationToken ct = default);

    /// <summary>按记录取可用模板（域分类命中，标记适用性）。</summary>
    Task<Result<List<ReprintTemplateOption>>> GetTemplatesAsync(
        long id,
        int userId,
        CancellationToken ct = default);

    /// <summary>装配模板打印数据（字段/表格行/分类），供入队使用。</summary>
    Task<Result<ReprintPrintData>> BuildPrintDataAsync(
        long id,
        int userId,
        CancellationToken ct = default);
}

/// <summary>统一补打列表项：跨域投影到同一人员/记录视图。</summary>
public sealed record ReprintRecordItem(
    string DomainKey,
    long BusinessId,
    string Name,
    string IdCard,
    string No,
    string Status,
    DateTime BusinessTime,
    string Extra,
    string Classification,
    string ChainType);

/// <summary>记录详情：列表项 + 打印字段 + 表格行。</summary>
public sealed record ReprintRecordDetail(
    string DomainKey,
    long BusinessId,
    string Name,
    string IdCard,
    string No,
    string Status,
    string Classification,
    DateTime BusinessTime,
    string Extra,
    string Mode,
    Dictionary<string, string> Fields,
    List<Dictionary<string, string>> TableRows);

/// <summary>模板可选项：模板元数据 + 本记录适用性（分类命中）。</summary>
public sealed record ReprintTemplateOption(
    long TemplateId,
    string Name,
    string DocType,
    string FileType,
    IReadOnlyList<string> Categories,
    bool IsApplicable,
    bool IsDefaultSelected);

/// <summary>模板打印数据装配结果。</summary>
public sealed record ReprintPrintData(
    string DomainKey,
    long BusinessId,
    string ApplicantName,
    string ApplicantIdCard,
    string? Classification,
    string Category,
    string? Status,
    Dictionary<string, string> Fields,
    List<Dictionary<string, string>> TableRows);
