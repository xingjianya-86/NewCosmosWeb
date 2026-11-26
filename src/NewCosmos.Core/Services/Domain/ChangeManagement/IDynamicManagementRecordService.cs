using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ChangeManagement;

/// <summary>
/// 社会救助对象动态管理记录（补打最小子集）：
/// 只保留服务端补打需要的字段装配、原始变更档案检索与档案链归一，
/// 不包含 PC 端的导出/打印/PDF 合并能力（服务端打印统一走队列）。
/// </summary>
public interface IDynamicManagementRecordService
{
    /// <summary>装配单户动态管理记录模板字段（口径对齐 PC DynamicManagementRecordService.BuildFieldsAsync）。</summary>
    Task<Result<Dictionary<string, string>>> BuildFieldsAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 变更档案搜索（不归一，逐档案返回：含历史旧档案与现役档案，供补打中心旧/新选择）。
    /// </summary>
    Task<Result<List<ChangedArchiveSummary>>> SearchChangedArchivesRawAsync(
        string? keyword,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>档案链归一：沿 original_application_id 递归到链尾现役档案（链尾已删 → 显式失败）。</summary>
    Task<Result<long>> NormalizeToLatestApplicationIdAsync(long applicationId, CancellationToken ct = default);
}

/// <summary>
/// 有变更记录的档案摘要（含历史版本标识 status / chain_type）。
/// 必须保留无参构造：数据库行映射依赖 Activator.CreateInstance&lt;T&gt;()。
/// </summary>
public class ChangedArchiveSummary
{
    public long ApplicationId { get; set; }
    public string HeadName { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string ApplicationNo { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;

    /// <summary>档案状态（Stopped=历史档案，其余=现役档案）。</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>档案链类型（chain_type，用于补打中心版本标签）。</summary>
    public string ChainType { get; set; } = string.Empty;

    public DateTime LastChangeDate { get; set; }
}
