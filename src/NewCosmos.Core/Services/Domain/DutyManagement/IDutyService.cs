using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.DutyManagement;

/// <summary>
/// 值班管理服务：成员分组维护、月度值班表轮转生成（每组 × 每日期类型独立轮转线）、
/// 班务调整（串班/代班）、请假联动、轮转锚点/游标、功能设置、成员批量导入与打印字段装配。
/// <para>
/// 业务口径唯一权威：legacy/NewCosmos.Desktop/Services/System/DutyService.cs 与 DutyService.Schedule.cs。
/// 本服务只做领域规则与数据库持久化；模板渲染/打印/导出执行由 Windows Agent 完成。
/// </para>
/// </summary>
public interface IDutyService
{
    /// <summary>确保值班相关表存在（幂等，缺表时按 Schema YAML 建表）</summary>
    Task<Result> EnsureTablesExistAsync(CancellationToken ct = default);

    #region 成员管理

    /// <summary>获取全部成员（含分组归属）</summary>
    Task<Result<List<DutyMemberView>>> GetMembersAsync(CancellationToken ct = default);

    /// <summary>新建/编辑成员（含分组归属、入职/离职时间；Id=0 新建）</summary>
    Task<Result> SaveMemberAsync(DutyMemberSave member, CancellationToken ct = default);

    /// <summary>启用/停用成员（停用后不再参与轮转，历史班次快照保留）</summary>
    Task<Result> SetMemberActiveAsync(long memberId, bool isActive, CancellationToken ct = default);

    /// <summary>切换成员启用状态，返回切换后的状态</summary>
    Task<Result<bool>> ToggleMemberActiveAsync(long memberId, CancellationToken ct = default);

    /// <summary>删除成员（班次 member_name 快照保留，轮转游标置空）</summary>
    Task<Result> DeleteMemberAsync(long memberId, CancellationToken ct = default);

    /// <summary>组内顺序上移/下移（direction: -1=上移，1=下移）</summary>
    Task<Result> MoveMemberAsync(long memberId, string groupCode, int direction, CancellationToken ct = default);

    #endregion

    #region 请假管理

    /// <summary>获取请假记录（memberId 为空时取全部）</summary>
    Task<Result<List<DutyLeaveView>>> GetLeavesAsync(long? memberId, CancellationToken ct = default);

    /// <summary>
    /// 新增请假记录（区间含首尾；生成值班表时区间内顺延跳过并消耗轮次；允许补登记过去日期，须填事由）。
    /// 返回被删除排班数据的已生成月份（yyyy-MM，升序；不自动重新生成）。
    /// </summary>
    Task<Result<List<string>>> SaveLeaveAsync(DutyLeaveSave leave, CancellationToken ct = default);

    /// <summary>删除请假记录；返回被删除排班数据的已生成月份（yyyy-MM，升序）</summary>
    Task<Result<List<string>>> DeleteLeaveAsync(long leaveId, CancellationToken ct = default);

    /// <summary>
    /// 预览请假增删将删除的已生成月份（yyyy-MM，升序）：请假开始月 → 最后已生成月。
    /// </summary>
    Task<Result<List<string>>> GetLeaveAffectedMonthsAsync(DateTime start, CancellationToken ct = default);

    #endregion

    #region 批量导入成员

    /// <summary>批量导入成员预览：解析 Excel + 行级校验 + 新增/更新计数（同名=更新）</summary>
    Task<Result<DutyMemberImportPreview>> PreviewMemberImportAsync(string filePath, CancellationToken ct = default);

    /// <summary>批量导入成员（全量对齐；存在解析错误时整体拒绝）</summary>
    Task<Result<DutyMemberImportResult>> ImportMembersAsync(string filePath, CancellationToken ct = default);

    /// <summary>生成批量导入模板 xlsx（数据表 + 轮转设置 + 填写说明）</summary>
    Task<Result<byte[]>> BuildMemberImportTemplateAsync(CancellationToken ct = default);

    #endregion

    #region 生成与查询

    /// <summary>获取月度值班表（未生成月份仅返回日期类型判定）</summary>
    Task<Result<DutyMonthSchedule>> GetMonthScheduleAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 按月生成值班表（force=true 时删除该月既有班次重新生成）。
    /// 起点取"目标月之前最近一条真实班次"，与生成顺序无关；结果返回下游已生成月份供一键重排。
    /// </summary>
    Task<Result<DutyGenerationSummary>> GenerateMonthAsync(int year, int month, bool force, CancellationToken ct = default);

    /// <summary>删除该月全部班次（便于重新生成）</summary>
    Task<Result> ClearMonthAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 补齐生成缺失月份（yyyy-MM 列表，升序逐月、已生成跳过、不 force 覆盖）。返回实际生成月份数。
    /// </summary>
    Task<Result<int>> GenerateMissingMonthsAsync(IReadOnlyList<string> months, CancellationToken ct = default);

    /// <summary>
    /// 按时间升序强制重生成给定月份（未生成月跳过，已生成月 force 覆盖）。返回实际重排月份数。
    /// </summary>
    Task<Result<int>> RegenerateMonthsAsync(IReadOnlyList<string> months, CancellationToken ct = default);

    /// <summary>重排指定月份之后所有已生成月份的班次（下游一键重排）。返回实际重排月份数。</summary>
    Task<Result<int>> RebuildDownstreamAsync(int year, int month, CancellationToken ct = default);

    #endregion

    #region 班务调整（串班/代班）

    /// <summary>获取班务调整记录（changeType 为空取全部；SWAP / SUBSTITUTE）</summary>
    Task<Result<List<DutyShiftChangeView>>> GetShiftChangesAsync(string? changeType, CancellationToken ct = default);

    /// <summary>新增串班/代班（串班=相互对调两天的班/代班=单向顶班一次；限同组、限今天及以后；即时留痕）</summary>
    Task<Result> SaveShiftChangeAsync(DutyShiftChangeSave change, CancellationToken ct = default);

    /// <summary>撤销班务调整（标记已撤销并强制重生成涉及月份，其余未撤销调整自动重放）</summary>
    Task<Result> CancelShiftChangeAsync(long changeId, CancellationToken ct = default);

    /// <summary>改期：串班修改乙方班日 / 代班修改被代班日（更新记录后重生成新旧日期所在月）</summary>
    Task<Result> RescheduleShiftChangeAsync(long changeId, DateTime newDate, CancellationToken ct = default);

    /// <summary>获取某成员今天及以后的班次列表（串班/代班选班用）</summary>
    Task<Result<List<DutyFutureSchedule>>> GetMemberFutureSchedulesAsync(long memberId, CancellationToken ct = default);

    #endregion

    #region 轮转游标

    /// <summary>查看全部轮转线游标状态</summary>
    Task<Result<List<DutyRotationStateView>>> GetRotationStatesAsync(CancellationToken ct = default);

    /// <summary>重置指定轮转线（删除游标记录，下次生成从头轮转）</summary>
    Task<Result> ResetRotationAsync(string groupCode, string dateType, CancellationToken ct = default);

    #endregion

    #region 设置

    /// <summary>休息日（周六/周日）带班领导两天同人开关</summary>
    Task<Result<bool>> GetRestdayLeaderSamePairAsync(CancellationToken ct = default);

    /// <summary>保存休息日带班领导开关</summary>
    Task<Result> SaveRestdayLeaderSamePairAsync(bool enabled, CancellationToken ct = default);

    #endregion
}
