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

/// <summary>
/// Step3「从核对数据带入」预览（申请表单侧只读带出：申请人 + 共同生活成员身份证
/// 匹配最新核对批次）。只负责展示与汇总，落库的存款额由前端写入
/// economicDetails.financialAssets 后随 PUT /form 保存。
/// </summary>
public sealed record BankFlowImportPreviewDto(
    string BatchNo,
    decimal TotalBalance,
    decimal MaxFlow,
    int MatchedCount,
    int TotalCount,
    string Summary,
    string ExceedHint,
    bool HasMachineryExceed,
    IReadOnlyList<BankFlowMemberRowDto> Rows);

/// <summary>
/// 带出的成员核对流水明细行（命中/未命中都返回；未命中标注“核对数据中无此身份证”）。
/// 金额同时给原始值与展示值，展示口径与 <c>BankFlowRecord</c> 的 *Display 一致。
/// </summary>
public sealed record BankFlowMemberRowDto(
    string Name,
    string IdCard,
    bool Matched,
    decimal TotalBalance,
    string TotalBalanceDisplay,
    decimal MaxFlowAmount,
    string MaxFlowAmountDisplay,
    string BankSummary,
    bool IsExceed);

#endregion
