namespace NewCosmos.Server.Contracts;

#region 救助月报表

/// <summary>B 线统计周期（口径来自 IMonthlyReportService.GetCycleRangeAsync）。</summary>
public sealed record MonthlyCycleDto(string Start, string End, string Display);

/// <summary>月报筛选选项：可选年份 / 月份 / 有业务数据的乡镇 + 当前周期。</summary>
public sealed record MonthlyReportOptionsResponse(
    IReadOnlyList<int> Years,
    IReadOnlyList<int> Months,
    IReadOnlyList<string> Towns,
    int CurrentYear,
    int CurrentMonth,
    MonthlyCycleDto CurrentCycle);

/// <summary>月报生成历史行（对应 nc_biz_monthly_reports）。</summary>
public sealed record MonthlyReportHistoryItemDto(
    long Id,
    int Year,
    int Month,
    string Town,
    int TotalArchives,
    int NewArchives,
    int StoppedArchives,
    decimal TotalAmount,
    string Status,
    DateTime GeneratedAt,
    string GeneratedBy);

/// <summary>
/// 月报汇总：卡片计数（按乡镇过滤，口径同旧版 GetAddedRowsAsync 等）
/// + 全县在保/月保障金（GetStatisticsAsync，旧版同为全县口径）。
/// </summary>
public sealed record MonthlyReportSummaryResponse(
    int Year,
    int Month,
    string Town,
    MonthlyCycleDto Cycle,
    int AddedCount,
    int StoppedCount,
    int IncreaseCount,
    int DecreaseCount,
    int DeathCount,
    int TotalArchives,
    decimal TotalAmount,
    MonthlyReportHistoryItemDto? Generated);

/// <summary>新增救助明细行（字段顺序对齐旧版打印模板）。</summary>
public sealed record MonthlyAddedRowDto(
    long ApplicationId,
    string AuditDate,
    string Name,
    string IdCard,
    string Gender,
    string Age,
    string Address,
    string Town,
    string Community,
    string FamilySize,
    string Classification,
    string CategoryCode,
    bool IsRural,
    string ReasonDetail,
    string MaritalStatus,
    string HealthStatus,
    string BankAccount,
    decimal AnnualIncome,
    string Relation);

/// <summary>停保汇总行。</summary>
public sealed record MonthlyStoppedRowDto(
    string HeadName,
    string HeadIdCard,
    string HeadBirth,
    string FamilySize,
    string Address,
    string Category,
    string Classification,
    string MonthAmount,
    string StopReason,
    string StopDate,
    string EnjoyDate,
    string CategoryCode,
    string BizType,
    bool IsRural);

/// <summary>保障金增发/减发行（同一结构，两个数据集）。</summary>
public sealed record MonthlyAmountChangeRowDto(
    string HeadName,
    string HeadBirth,
    string FamilySize,
    string Address,
    string Category,
    string EnjoyDate,
    string OldAmount,
    string NewAmount,
    string Classification,
    string ChangeAmount,
    string Reason,
    int DecreasePerson,
    decimal ClassifiedSubsidyAmount,
    string OldFamilySize,
    string CategoryCode,
    bool IsRural);

/// <summary>分类施保金减发人员行（成员级）。</summary>
public sealed record MonthlyShiBaoRowDto(
    string HeadName,
    string MemberName,
    string Address,
    string Category,
    string EnjoyDate,
    string OriginalAmount,
    string OriginalClassified,
    string Reason,
    string DecreaseAmount,
    string CurrentAmount,
    string CurrentClassified,
    bool IsRural);

/// <summary>分类施保增发户（户级，每户一页）。</summary>
public sealed record MonthlyClassifiedAddRowDto(
    string ApplicantName,
    string ApplicantGender,
    string ApplicantBirthDate,
    string Nationality,
    string HealthStatus,
    string Classification,
    string FamilySize,
    string Phone,
    string Address,
    string ApplicantIdCard,
    string ClassifiedType,
    int Count,
    decimal Amount,
    bool IsRural,
    IReadOnlyList<MonthlyClassifiedMemberDto> Members);

/// <summary>分类施保增发户家庭成员 1~6。</summary>
public sealed record MonthlyClassifiedMemberDto(
    string Name,
    string Gender,
    string BirthDate,
    string Relation,
    string Health,
    decimal MonthlyIncome,
    decimal AnnualIncome);

/// <summary>人员变动_自然减员月报表行。</summary>
public sealed record MonthlyDeathRowDto(
    string HeadName,
    string HeadIdCard,
    string Address,
    string DeceasedName,
    string DeceasedDate,
    string EnjoyDate,
    string NewHeadName,
    string CategoryCode,
    bool IsRural);

/// <summary>退出对象兜底情况纠治行。</summary>
public sealed record MonthlyExitRectificationRowDto(
    string Name,
    string IdCard,
    string Category,
    string FamilySize,
    string StopMonth,
    string StopReason,
    string GracePeriod,
    string IntoDibao,
    string IntoTeku,
    string IntoEdge,
    string IntoRigid,
    string DirectExit);

/// <summary>临时救助新增汇总行。</summary>
public sealed record MonthlyTempReliefRowDto(
    string ReliefType,
    string Name,
    string IdCard,
    string Age,
    string Gender,
    string FamilySize,
    string Address,
    string Category,
    string Phone,
    string Amount,
    string AuditDate,
    string Reason,
    string SelfPay,
    string BankAccount);

/// <summary>
/// 一次返回指定分类下的全部报表数据集（前端按勾选表单取用），
/// 避免逐表单往返；不适用的数据集返回空数组。
/// </summary>
public sealed record MonthlyReportRowsResponse(
    int Year,
    int Month,
    string Town,
    string Category,
    IReadOnlyList<MonthlyAddedRowDto> Added,
    IReadOnlyList<MonthlyStoppedRowDto> Stopped,
    IReadOnlyList<MonthlyAmountChangeRowDto> Increase,
    IReadOnlyList<MonthlyAmountChangeRowDto> Decrease,
    IReadOnlyList<MonthlyShiBaoRowDto> ShiBaoReduction,
    IReadOnlyList<MonthlyClassifiedAddRowDto> ClassifiedAdds,
    IReadOnlyList<MonthlyDeathRowDto> Death,
    IReadOnlyList<MonthlyExitRectificationRowDto> ExitRectification,
    IReadOnlyList<MonthlyTempReliefRowDto> TempRelief);

/// <summary>生成/覆盖生成月报。Overwrite=true 时跳过“已存在”保护。</summary>
public sealed record MonthlyReportGenerateRequest(
    int Year,
    int Month,
    string? Town,
    bool? Overwrite);

/// <summary>生成结果（对应 MonthlyReport 实体）。</summary>
public sealed record MonthlyReportGenerateResponse(
    long Id,
    int Year,
    int Month,
    string Town,
    string Status,
    int TotalArchives,
    int NewArchives,
    int StoppedArchives,
    decimal TotalAmount,
    DateTime GeneratedAt);

/// <summary>会议记录（月报会议记录模板与一事一议模板共用，由 specialApproval 区分）。</summary>
public sealed record MonthlyMeetingResponse(
    long Id,
    int Year,
    int Month,
    string Town,
    string MeetingTime,
    string Host,
    string Recorder,
    string Attendees,
    string Absentees,
    string MemberInfo,
    string ApplyCategory,
    string MemberNames,
    string CreatedBy,
    DateTime CreatedAt);

/// <summary>保存会议记录（含出席/缺席历史，旧版 ConfirmAttendance 同口径）。</summary>
public sealed record MonthlyMeetingSaveRequest(
    int Year,
    int Month,
    string? Town,
    string? MeetingTime,
    string? Host,
    string? Recorder,
    string? Attendees,
    string? Absentees,
    string? ApplyCategory);

/// <summary>最新一条会议出席/缺席历史快照。</summary>
public sealed record MonthlyMeetingAttendanceResponse(
    long Id,
    int Year,
    int Month,
    string Town,
    string Attendees,
    string Absentees,
    string CreatedBy,
    DateTime CreatedAt);

#endregion
