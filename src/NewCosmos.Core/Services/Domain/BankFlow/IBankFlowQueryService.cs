using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.BankFlow;

/// <summary>银行流水核对数据查询：查看页分页/筛选/导出 + 经济状况界面按身份证带出（取最新批次）。</summary>
public interface IBankFlowQueryService
{
    /// <summary>批次列表（查看页筛选下拉；含人数/超阈值/存疑统计）</summary>
    Task<Result<List<BankFlowImport>>> GetImportsAsync(CancellationToken ct = default);

    /// <summary>分页查询人员汇总（keyword 匹配姓名/身份证/地址；exceedOnly 仅超阈值；importId 空=全部批次）</summary>
    Task<Result<PagedResult<BankFlowRecord>>> SearchRecordsAsync(
        string? keyword, long? importId, bool exceedOnly, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 按身份证从最新批次带出人员汇总 + 每银行明细（经济状况界面「从核对数据带入」）。
    /// 返回 key=身份证（大写）；未命中的身份证不出现在字典中，由调用方比对提示。
    /// </summary>
    Task<Result<Dictionary<string, BankFlowPersonData>>> GetByIdCardsAsync(
        IReadOnlyCollection<string> idCards, CancellationToken ct = default);

    /// <summary>
    /// 超阈值红字提示（仅提示，不自动决策）：阈值 = 月低保标准（Rural/Urban 两档）
    /// × <see cref="Constants.BankFlowConstants.MonthsFor"/>（分类档位 30/36）× max(1, 家庭人数)，
    /// 文案套 <see cref="Constants.BankFlowConstants.BalanceExceedFormat"/>。
    /// 标准配置缺失或未超阈值返回空串（不误导，禁止硬编码回退值）；公式单点在 Core。
    /// </summary>
    Task<string> BuildExceedHintAsync(
        decimal totalBalance,
        int familySize,
        string? classificationCode,
        string? hukouType,
        CancellationToken ct = default);

    /// <summary>导出当前筛选结果为 xlsx（列与核对表口径一致，含每银行明细摘要）</summary>
    Task<Result<byte[]>> ExportAsync(string? keyword, long? importId, bool exceedOnly, CancellationToken ct = default);
}

/// <summary>单个申请人的核对数据（汇总 + 每银行明细）</summary>
public class BankFlowPersonData
{
    public BankFlowRecord Record { get; set; } = new();
    public List<BankFlowItem> Items { get; set; } = new();
    public string BatchNo { get; set; } = string.Empty;
}
