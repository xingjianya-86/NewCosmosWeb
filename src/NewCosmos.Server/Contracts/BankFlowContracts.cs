namespace NewCosmos.Server.Contracts;

#region 银行流水核对

/// <summary>核对导入批次（查看页批次筛选下拉）。</summary>
public sealed record BankFlowImportDto(
    long Id,
    string BatchNo,
    string SourceFileName,
    int TotalCount,
    int ExceedCount,
    int InvalidIdCardCount,
    decimal ExceedThreshold,
    DateTime CreatedAt);

/// <summary>
/// 核对人员汇总行。金额与农机功率同时给出原始值与展示值，
/// 展示口径与 PC <c>BankFlowRecord</c> 的 *Display 属性一致。
/// </summary>
public sealed record BankFlowRecordDto(
    long Id,
    int? RowNo,
    string PersonName,
    string IdCard,
    bool IdCardValid,
    string Relationship,
    string FamilyAddress,
    decimal TotalBalance,
    decimal MaxFlowAmount,
    string BankSummary,
    bool IsExceed,
    decimal? MachineryPower,
    string TotalBalanceDisplay,
    string MaxFlowAmountDisplay,
    string MachineryPowerDisplay,
    string IdCardValidDisplay);

/// <summary>核对记录分页响应（page 从 1 开始）。</summary>
public sealed record BankFlowRecordListResponse(
    IReadOnlyList<BankFlowRecordDto> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages);

#endregion
