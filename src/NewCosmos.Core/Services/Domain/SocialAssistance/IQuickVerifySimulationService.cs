using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 快速核验 · 模拟测算服务：在 Core 内集中实现旧版
/// QuickVerifyViewModel.Income.cs / Report.cs 的测算、标准读取、区间判定与报告文本生成。
/// 【铁律】Vue/Server 不得重算任何金额；低保标准只从 IStandardConfigService 读取，
/// 单价/比例只从 Constants 读取，缺失即显式失败（不回退过期硬编码值）。
/// </summary>
public interface IQuickVerifySimulationService
{
    /// <summary>
    /// 执行台账核验并返回展示模型（10 组 + 同名风险 + 姓名来源 + 带入建议）。
    /// 整体失败（DB 级）返回 Failure，部分失败保留在分组内（§7）。
    /// </summary>
    Task<Result<QuickVerifyVerificationView>> VerifyViewAsync(
        string idCard, string? name, CancellationToken ct = default);

    /// <summary>
    /// 读取测算所需标准（低保档、边缘上限、赡养费默认值、土地单价）。
    /// 标准未配置时返回 Success + StandardAvailable=false（页面提示，不伪造数值）；
    /// 数据库查询失败返回 Failure。
    /// </summary>
    Task<Result<QuickVerifyStandards>> GetStandardsAsync(
        string? hukouType, int? year, CancellationToken ct = default);

    /// <summary>
    /// 收入模拟测算：赡养费（默认低保标准×20%）、土地三用途、种植补贴 → 权威年值口径 → 标准区间。
    /// 输入非法或标准缺失返回 Failure，绝不输出臆造数值。
    /// </summary>
    Task<Result<QuickVerifySimulationResult>> SimulateAsync(
        QuickVerifySimulationInput input, CancellationToken ct = default);

    /// <summary>
    /// 生成三段式简易报告（被核算人 / 收入核算 / 台账核验），与旧版 Report.cs 同结构。
    /// includeIncome=false 时收入段标注"未核算"；includeVerification=true 时重新核验台账，
    /// 核验整体失败会写入 VerificationError 但报告仍可导出收入段（失败原因明示）。
    /// </summary>
    Task<Result<QuickVerifyReport>> BuildReportAsync(
        QuickVerifySimulationInput input,
        bool includeIncome,
        bool includeVerification,
        CancellationToken ct = default);
}
