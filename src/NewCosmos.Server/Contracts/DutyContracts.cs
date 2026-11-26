namespace NewCosmos.Server.Contracts;

#region 月度值班表

/// <summary>GET /api/duty/months/{year}/{month} 响应。</summary>
public sealed record DutyMonthResponse(
    int Year,
    int Month,
    bool Generated,
    int ShiftCount,
    IReadOnlyList<DutyDayDto> Days,
    DutySettingsDto Settings);

/// <summary>月度值班表一天：四个组的值班人姓名（空串=空缺）。</summary>
public sealed record DutyDayDto(
    string Date,
    string Week,
    string DateType,
    string Leader,
    string Middle,
    string Male,
    string Female,
    bool IsAdjusted);

public sealed record DutySettingsDto(bool RestdayLeaderSamePair);

public sealed record DutySettingsRequest(bool? RestdayLeaderSamePair);

/// <summary>POST /api/duty/months/{year}/{month}/generate 响应。</summary>
public sealed record DutyGenerationResponse(
    bool Generated,
    int ShiftCount,
    IReadOnlyList<string> Warnings,
    bool HasDownstream,
    bool HasPendingCrossMonth);

public sealed record DutyClearResponse(bool Cleared);

public sealed record DutyRebuildResponse(int Rebuilt);

public sealed record DutyFillMissingResponse(int Generated, string Month);

#endregion

#region 成员 / 请假

public sealed record DutyToggleResponse(bool IsActive);

public sealed record DutyLeaveAffectedMonthsResponse(IReadOnlyList<string> Months);

#endregion

#region 班务调整

public sealed record DutyRescheduleRequest(DateTime? NewDate);

#endregion

#region 节假日

public sealed record DutyHolidayRequest(DateTime? Date, string? DateType, string? Name);

#endregion

#region 导出 / 打印

public sealed record DutyExportRequest(int? StartYear, int? StartMonth, int? EndYear, int? EndMonth);

public sealed record DutyPrintRequest(
    int? Year,
    int? Month,
    int? Copies,
    bool? IsDuplex,
    long? PrinterBindingId,
    string? PrinterName);

/// <summary>值班表导出/打印入队响应（Agent TemplateBatch 任务）。</summary>
public sealed record DutyJobResponse(
    long Id,
    string JobNo,
    string Status,
    string JobKind,
    string Mode,
    long TemplateId,
    string TemplateName,
    int FileCount);

#endregion
