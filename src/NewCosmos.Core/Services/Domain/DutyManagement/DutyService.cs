using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Domain.DutyManagement;

/// <summary>
/// 值班管理服务实现（可 partial）：本文件负责表结构、成员、请假、设置与通用取数；
/// 月度轮转生成见 DutyService.Schedule.cs，班务调整见 DutyService.Adjust.cs，批量导入见 DutyService.Import.cs。
/// </summary>
public partial class DutyService : BaseService, IDutyService
{
    protected override string ServiceName => "DutyService";

    private static readonly string[] DutyTables =
    {
        "nc_duty_members",
        "nc_duty_member_groups",
        "nc_duty_leaves",
        "nc_duty_rotation_state",
        "nc_duty_schedules",
        "nc_duty_settings",
        "nc_duty_shift_changes"
    };

    private readonly IDatabaseService _dbService;
    private readonly ISchemaSyncService _schemaSync;
    private readonly ISchemaService _schemaService;
    private readonly IHolidayService _holidayService;
    private bool _tablesReady;

    public DutyService(
        IDatabaseService dbService,
        ISchemaSyncService schemaSync,
        ISchemaService schemaService,
        IHolidayService holidayService,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _schemaSync = schemaSync;
        _schemaService = schemaService;
        _holidayService = holidayService;
    }

    #region Schema 管理

    public async Task<Result> EnsureTablesExistAsync(CancellationToken ct = default)
    {
        if (_tablesReady) return Result.Success();

        LogInfo("执行值班表结构检查");

        var existingResult = await _dbService.QueryAsync<string>(@"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = ANY(string_to_array($1, ','))",
            ct, string.Join(",", DutyTables));
        if (!existingResult.IsSuccess)
            return existingResult;

        var existingSet = (existingResult.Value ?? new List<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = DutyTables.Where(t => !existingSet.Contains(t)).ToList();

        // 逐表创建缺失表（已存在的表跳过重建，绝不破坏性重建）
        foreach (var table in missing)
        {
            LogInfo($"创建缺失表: {table}");
            var syncResult = await _schemaSync.SyncTableSchemaAsync(table, allowDrop: false, null, ct);
            if (syncResult.IsFailure)
            {
                LogError($"同步表结构失败: {table}, {syncResult.Message}");
                return syncResult;
            }
        }

        // 幂等补列：已存在的表按 YAML 权威定义对比实际列，缺失列自动补齐
        //（列名/类型/默认值均来自受控 YAML，非用户输入）
        foreach (var table in existingSet)
        {
            var schema = _schemaService.GetTableSchema(table);
            if (schema is null) continue;

            var colsResult = await _dbService.QueryAsync<string>(
                "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = $1",
                ct, table);
            if (!colsResult.IsSuccess)
                return colsResult;

            var existingCols = (colsResult.Value ?? new List<string>())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var col in schema.Columns.Where(c => !existingCols.Contains(c.Name)))
            {
                var defaultSql = string.IsNullOrWhiteSpace(col.Default) ? string.Empty : $" DEFAULT {col.Default}";
                var alterSql = $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {col.Name} {col.Type}{defaultSql}";
                var alterResult = await _dbService.ExecuteNonQueryAsync(alterSql, ct);
                if (alterResult.IsFailure)
                {
                    LogError($"补齐缺失列失败: {table}.{col.Name}, {alterResult.Message}");
                    return alterResult;
                }
                LogInfo($"补齐缺失列: {table}.{col.Name} {col.Type}");
            }
        }

        _tablesReady = true;
        LogInfo($"值班相关表结构就绪（本次新建 {missing.Count} 张）");
        return Result.Success();
    }

    #endregion

    #region 日期类型判定

    /// <summary>
    /// 判定值班日期类型：法定放假日→HOLIDAY；调休补班→WORKDAY；周六日→RESTDAY；其他→WORKDAY
    /// </summary>
    private string ClassifyDate(DateTime date)
    {
        if (_holidayService.IsLegalHoliday(date)) return DutyConstants.DateTypes.HOLIDAY;
        if (_holidayService.IsMakeupWorkday(date)) return DutyConstants.DateTypes.WORKDAY;
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return DutyConstants.DateTypes.RESTDAY;
        return DutyConstants.DateTypes.WORKDAY;
    }

    /// <summary>日期类型对应的出班组</summary>
    private static string[] RequiredGroups(string dateType) =>
        dateType == DutyConstants.DateTypes.HOLIDAY
            ? DutyConstants.Groups.HolidayRequired
            : DutyConstants.Groups.RegularRequired;

    #endregion

    #region 成员管理

    public async Task<Result<List<DutyMemberView>>> GetMembersAsync(CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyMemberView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var memberResult = await _dbService.QueryAsync<DutyMemberView>(@"
            SELECT id, name, phone, is_active, join_date, exit_date, sort_order, remark
            FROM nc_duty_members
            ORDER BY sort_order, id", ct);
        if (!memberResult.IsSuccess)
            return Result.Failure<List<DutyMemberView>>(memberResult.ErrorCode!, memberResult.Message!);

        var groupResult = await _dbService.QueryAsync<DutyGroupAssignment>(@"
            SELECT id, member_id, group_code, sort_order
            FROM nc_duty_member_groups", ct);
        if (!groupResult.IsSuccess)
            return Result.Failure<List<DutyMemberView>>(groupResult.ErrorCode!, groupResult.Message!);

        var groups = groupResult.Value ?? new List<DutyGroupAssignment>();
        var members = memberResult.Value ?? new List<DutyMemberView>();
        var groupLookup = groups.ToLookup(g => g.MemberId);
        foreach (var member in members)
        {
            member.Groups = groupLookup[member.Id]
                .OrderBy(g => g.SortOrder)
                .ToList();
        }

        return Result.Success(members);
    }

    public async Task<Result> SaveMemberAsync(DutyMemberSave member, CancellationToken ct = default)
    {
        if (member == null || string.IsNullOrWhiteSpace(member.Name))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "成员姓名不能为空");

        var requestedGroups = member.GroupCodes ?? new List<string>();
        var invalidGroup = requestedGroups.FirstOrDefault(g => !DutyConstants.Groups.IsValid(g));
        if (invalidGroup != null)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"无效的组编码: {invalidGroup}");

        var distinctGroups = requestedGroups.Distinct().ToList();
        if (distinctGroups.Count == 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请至少选择一个所属组");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            long memberId;
            if (member.Id == 0)
            {
                var insertResult = await _dbService.ExecuteScalarAsync<long>(@"
                    INSERT INTO nc_duty_members (name, phone, is_active, join_date, exit_date, sort_order, remark, created_at, updated_at)
                    VALUES ($1, $2, $3, $4, $5,
                            COALESCE((SELECT MAX(sort_order) + 1 FROM nc_duty_members), 0), $6, NOW(), NOW())
                    RETURNING id", ct,
                    member.Name.Trim(), member.Phone?.Trim() ?? string.Empty, member.IsActive,
                    member.JoinDate, member.ExitDate, member.Remark?.Trim() ?? string.Empty);
                if (insertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return insertResult;
                }
                memberId = insertResult.Value;
            }
            else
            {
                var existsResult = await _dbService.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM nc_duty_members WHERE id = $1", ct, member.Id);
                if (!existsResult.IsSuccess || existsResult.Value == 0)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure(ErrorCodes.DUTY_MEMBER_NOT_FOUND, "未找到要编辑的成员");
                }

                var updateResult = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_members
                    SET name = $2, phone = $3, is_active = $4, join_date = $5, exit_date = $6, remark = $7, updated_at = NOW()
                    WHERE id = $1", ct,
                    member.Id, member.Name.Trim(), member.Phone?.Trim() ?? string.Empty, member.IsActive,
                    member.JoinDate, member.ExitDate, member.Remark?.Trim() ?? string.Empty);
                if (updateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateResult;
                }
                memberId = member.Id;
            }

            // 分组归属：删除移除的组，新增缺失的组（新增组追加到该组轮转队列末尾）
            var existingResult = await _dbService.QueryAsync<DutyGroupAssignment>(
                "SELECT id, group_code FROM nc_duty_member_groups WHERE member_id = $1", ct, memberId);
            if (!existingResult.IsSuccess)
            {
                await tx.RollbackAsync(ct);
                return existingResult;
            }

            var existing = existingResult.Value ?? new List<DutyGroupAssignment>();
            var existingCodes = existing.Select(g => g.GroupCode).ToHashSet();

            foreach (var removed in existing.Where(g => !distinctGroups.Contains(g.GroupCode)))
            {
                var deleteResult = await _dbService.ExecuteNonQueryAsync(
                    "DELETE FROM nc_duty_member_groups WHERE id = $1", ct, removed.Id);
                if (deleteResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return deleteResult;
                }
            }

            foreach (var added in distinctGroups.Where(g => !existingCodes.Contains(g)))
            {
                var insertGroupResult = await _dbService.ExecuteNonQueryAsync(@"
                    INSERT INTO nc_duty_member_groups (member_id, group_code, sort_order)
                    VALUES ($1, $2,
                            COALESCE((SELECT MAX(sort_order) + 1 FROM nc_duty_member_groups WHERE group_code = $2), 0))",
                    ct, memberId, added);
                if (insertGroupResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return insertGroupResult;
                }
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "保存值班成员");
            return Result.FromException(ex);
        }

        Logger.LogBusiness("保存值班成员",
            ("MemberId", member.Id),
            ("Name", DataMasker.MaskName(member.Name.Trim())),
            ("Groups", string.Join(",", distinctGroups)));
        return Result.Success();
    }

    public async Task<Result> SetMemberActiveAsync(long memberId, bool isActive, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync(@"
            UPDATE nc_duty_members SET is_active = $2, updated_at = NOW() WHERE id = $1", ct, memberId, isActive);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("切换值班成员状态", ("MemberId", memberId), ("IsActive", isActive));
            return Result.Success();
        }
        return result;
    }

    public async Task<Result<bool>> ToggleMemberActiveAsync(long memberId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<bool>(ensureResult.ErrorCode!, ensureResult.Message!);

        var result = await _dbService.ExecuteScalarAsync<bool?>(
            "UPDATE nc_duty_members SET is_active = NOT COALESCE(is_active, TRUE), updated_at = NOW() WHERE id = $1 RETURNING is_active",
            ct, memberId);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);
        if (result.Value is null)
            return Result.Failure<bool>(ErrorCodes.DUTY_MEMBER_NOT_FOUND, "未找到值班成员");

        Logger.LogBusiness("切换值班成员状态", ("MemberId", memberId), ("IsActive", result.Value.Value));
        return Result.Success(result.Value.Value);
    }

    public async Task<Result> DeleteMemberAsync(long memberId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        // FK 级联：分组记录级联删除；班次 member_id 与轮转游标置空（member_name 快照保留）
        var result = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_duty_members WHERE id = $1", ct, memberId);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("删除值班成员", ("MemberId", memberId));
            return Result.Success();
        }
        return result;
    }

    public async Task<Result> MoveMemberAsync(long memberId, string groupCode, int direction, CancellationToken ct = default)
    {
        if (!DutyConstants.Groups.IsValid(groupCode))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "无效的组编码");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        // 组内按 sort_order, id 排序后交换相邻两项的 sort_order
        var listResult = await _dbService.QueryAsync<DutyGroupAssignment>(@"
            SELECT id, member_id, sort_order
            FROM nc_duty_member_groups
            WHERE group_code = $1
            ORDER BY sort_order, id", ct, groupCode);
        if (!listResult.IsSuccess)
            return listResult;

        var list = listResult.Value ?? new List<DutyGroupAssignment>();
        var index = list.FindIndex(g => g.MemberId == memberId);
        if (index < 0)
            return Result.Failure(ErrorCodes.DUTY_MEMBER_NOT_FOUND, "该成员不属于指定组");

        var target = direction < 0 ? index - 1 : index + 1;
        if (target < 0 || target >= list.Count)
            return Result.Success(); // 已在边界，无需移动

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            var swapFirst = await _dbService.ExecuteNonQueryAsync(
                "UPDATE nc_duty_member_groups SET sort_order = $2 WHERE id = $1", ct, list[target].Id, list[index].SortOrder);
            if (swapFirst.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return swapFirst;
            }
            var swapSecond = await _dbService.ExecuteNonQueryAsync(
                "UPDATE nc_duty_member_groups SET sort_order = $2 WHERE id = $1", ct, list[index].Id, list[target].SortOrder);
            if (swapSecond.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return swapSecond;
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "调整组内顺序");
            return Result.FromException(ex);
        }

        LogInfo($"组内顺序调整: {DutyConstants.Groups.DisplayName(groupCode)}, MemberId={memberId}, Direction={direction}");
        return Result.Success();
    }

    #endregion

    #region 请假管理

    public async Task<Result<List<DutyLeaveView>>> GetLeavesAsync(long? memberId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyLeaveView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        const string baseSql = @"
            SELECT l.id, l.member_id, m.name AS member_name, l.start_date, l.end_date, l.reason, l.created_at
            FROM nc_duty_leaves l
            JOIN nc_duty_members m ON m.id = l.member_id";

        Result<List<DutyLeaveView>> result = memberId.HasValue
            ? await _dbService.QueryAsync<DutyLeaveView>(
                baseSql + " WHERE l.member_id = $1 ORDER BY l.start_date, l.id", ct, memberId.Value)
            : await _dbService.QueryAsync<DutyLeaveView>(
                baseSql + " ORDER BY l.start_date, l.id", ct);
        if (result.IsFailure)
            return Result.Failure<List<DutyLeaveView>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<DutyLeaveView>());
    }

    public async Task<Result<List<string>>> SaveLeaveAsync(DutyLeaveSave leave, CancellationToken ct = default)
    {
        if (leave == null || leave.MemberId <= 0)
            return Result.Failure<List<string>>(ErrorCodes.VALIDATION_FAILED, "请假成员无效");
        if (leave.EndDate.Date < leave.StartDate.Date)
            return Result.Failure<List<string>>(ErrorCodes.DUTY_LEAVE_INVALID_RANGE, "结束日期不能早于开始日期");
        // 补登记过去日期：允许（会级联重排历史月份），但必须填写事由以便留痕
        if (leave.StartDate.Date < DateTime.Today && string.IsNullOrWhiteSpace(leave.Reason))
            return Result.Failure<List<string>>(ErrorCodes.DUTY_LEAVE_BACKFILL_REASON_REQUIRED, "补登记过去日期的请假必须填写事由");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<string>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var existsResult = await _dbService.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_duty_members WHERE id = $1", ct, leave.MemberId);
        if (!existsResult.IsSuccess || existsResult.Value == 0)
            return Result.Failure<List<string>>(ErrorCodes.DUTY_MEMBER_NOT_FOUND, "未找到请假成员");

        var result = await _dbService.ExecuteNonQueryAsync(@"
            INSERT INTO nc_duty_leaves (member_id, start_date, end_date, reason, created_at)
            VALUES ($1, $2, $3, $4, NOW())", ct,
            leave.MemberId, leave.StartDate.Date, leave.EndDate.Date, leave.Reason?.Trim() ?? string.Empty);
        if (result.IsFailure)
            return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);

        Logger.LogBusiness("新增值班请假",
            ("MemberId", leave.MemberId),
            ("Range", $"{leave.StartDate:yyyy-MM-dd}~{leave.EndDate:yyyy-MM-dd}"));

        // 请假改变轮转消耗（跨月接续以"目标月之前最近一条真实班次"为准），受影响月份旧数据不再有效：
        // 删除（不自动重新生成，由值班表页人工生成或界面选择"立即重新生成"）
        return await DeleteAffectedGeneratedMonthsAsync(leave.StartDate.Date, ct);
    }

    public async Task<Result<List<string>>> DeleteLeaveAsync(long leaveId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<string>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var leaveResult = await _dbService.QuerySingleAsync<DutyLeave>(
            "SELECT id, member_id, start_date, end_date FROM nc_duty_leaves WHERE id = $1", ct, leaveId);
        if (!leaveResult.IsSuccess)
            return Result.Failure<List<string>>(leaveResult.ErrorCode!, leaveResult.Message!);
        if (leaveResult.Value is null)
            return Result.Failure<List<string>>(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "未找到请假记录");

        var result = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_duty_leaves WHERE id = $1", ct, leaveId);
        if (result.IsFailure)
            return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);

        Logger.LogBusiness("删除值班请假记录", ("LeaveId", leaveId));

        // 撤销请假同样改变轮转消耗 → 删除受影响月份已生成数据（不自动重新生成）
        return await DeleteAffectedGeneratedMonthsAsync(leaveResult.Value.StartDate.Date, ct);
    }

    /// <summary>
    /// 删除请假增删影响范围内的已生成月份班次：从请假开始月到"最后已生成月"，逐月整月删除，**不自动重新生成**。
    /// </summary>
    private async Task<Result<List<string>>> DeleteAffectedGeneratedMonthsAsync(DateTime leaveStart, CancellationToken ct)
    {
        var monthsResult = await GetAffectedGeneratedMonthsAsync(leaveStart, ct);
        if (monthsResult.IsFailure)
            return Result.Failure<List<string>>(monthsResult.ErrorCode!, monthsResult.Message!);

        var deleted = new List<string>();
        foreach (var (y, m) in monthsResult.Value!)
        {
            var delResult = await _dbService.ExecuteNonQueryAsync(
                "DELETE FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, y, m);
            if (delResult.IsFailure)
                return Result.Failure<List<string>>(delResult.ErrorCode!, $"删除 {y}-{m:D2} 值班表数据失败：{delResult.Message}");
            deleted.Add($"{y}-{m:D2}");
            LogInfo($"请假联动：已删除 {y}-{m:D2} 值班表数据（等待人工重新生成）");
        }
        return Result.Success(deleted);
    }

    /// <summary>
    /// 计算请假增删影响的已生成月份：请假开始月 → 最后已生成月，仅返回实际已生成班次的月份（升序）。
    /// </summary>
    private async Task<Result<List<(int Year, int Month)>>> GetAffectedGeneratedMonthsAsync(
        DateTime leaveStart, CancellationToken ct)
    {
        var maxResult = await _dbService.ExecuteScalarAsync<long?>(
            "SELECT MAX(year * 100 + month) FROM nc_duty_schedules", ct);
        if (!maxResult.IsSuccess)
            return Result.Failure<List<(int, int)>>(maxResult.ErrorCode!, maxResult.Message!);
        if (maxResult.Value is not > 0)
            return Result.Success(new List<(int, int)>());

        var lastYear = (int)(maxResult.Value.Value / 100);
        var lastMonth = (int)(maxResult.Value.Value % 100);

        var months = new List<(int, int)>();
        var from = new DateTime(leaveStart.Year, leaveStart.Month, 1);
        var to = new DateTime(lastYear, lastMonth, 1);
        for (var m = from; m <= to; m = m.AddMonths(1))
        {
            var countResult = await _dbService.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, m.Year, m.Month);
            if (countResult.IsFailure)
                return Result.Failure<List<(int, int)>>(countResult.ErrorCode!, countResult.Message!);
            if (countResult.Value > 0)
                months.Add((m.Year, m.Month));
        }
        return Result.Success(months);
    }

    public async Task<Result<List<string>>> GetLeaveAffectedMonthsAsync(
        DateTime start, CancellationToken ct = default)
    {
        var monthsResult = await GetAffectedGeneratedMonthsAsync(start, ct);
        if (monthsResult.IsFailure)
            return Result.Failure<List<string>>(monthsResult.ErrorCode!, monthsResult.Message!);

        return Result.Success(monthsResult.Value!
            .Select(x => $"{x.Year}-{x.Month:D2}")
            .ToList());
    }

    /// <summary>加载全部请假区间（生成值班表用，内存判定）：memberId → 区间列表（含首尾）</summary>
    private async Task<Result<List<(long MemberId, DateTime Start, DateTime End)>>> LoadLeaveRangesAsync(CancellationToken ct)
    {
        var result = await _dbService.QueryAsync<DutyLeave>(
            "SELECT member_id, start_date, end_date FROM nc_duty_leaves", ct);
        if (!result.IsSuccess)
            return Result.Failure<List<(long, DateTime, DateTime)>>(result.ErrorCode!, result.Message!);

        var ranges = (result.Value ?? new List<DutyLeave>())
            .Select(l => (l.MemberId, l.StartDate.Date, l.EndDate.Date))
            .ToList();
        return Result.Success(ranges);
    }

    private static bool IsOnLeave(
        List<(long MemberId, DateTime Start, DateTime End)> leaveRanges, long memberId, DateTime date) =>
        leaveRanges.Any(r => r.MemberId == memberId && r.Start <= date && r.End >= date);

    #endregion

    #region 设置

    public async Task<Result<bool>> GetRestdayLeaderSamePairAsync(CancellationToken ct = default)
    {
        var valueResult = await GetSettingValueAsync(DutyConstants.SettingKeys.RESTDAY_LEADER_SAME_PAIR, ct);
        if (!valueResult.IsSuccess)
            return Result.Failure<bool>(valueResult.ErrorCode!, valueResult.Message!);
        return Result.Success(valueResult.Value == DutyConstants.SettingOn);
    }

    public async Task<Result> SaveRestdayLeaderSamePairAsync(bool enabled, CancellationToken ct = default)
    {
        return await SaveSettingAsync(
            DutyConstants.SettingKeys.RESTDAY_LEADER_SAME_PAIR,
            enabled ? DutyConstants.SettingOn : DutyConstants.SettingOff,
            "休息日（周六/周日）带班领导两天同人", ct);
    }

    private async Task<Result> SaveSettingAsync(string key, string value, string remark, CancellationToken ct)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync(@"
            INSERT INTO nc_duty_settings (setting_key, setting_value, remark, updated_at)
            VALUES ($1, $2, $3, NOW())
            ON CONFLICT (setting_key)
            DO UPDATE SET setting_value = EXCLUDED.setting_value, updated_at = NOW()",
            ct, key, value, remark);
        if (result.IsSuccess)
        {
            LogInfo($"保存值班设置: {key} = {value}");
            return Result.Success();
        }
        return result;
    }

    private async Task<Result<string>> GetSettingValueAsync(string key, CancellationToken ct)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<string>(ensureResult.ErrorCode!, ensureResult.Message!);

        var result = await _dbService.ExecuteScalarAsync<string>(
            "SELECT setting_value FROM nc_duty_settings WHERE setting_key = $1", ct, key);
        return result.IsSuccess
            ? Result.Success(result.Value ?? string.Empty)
            : Result.Failure<string>(result.ErrorCode!, result.Message!);
    }

    #endregion

    #region 轮转游标

    public async Task<Result<List<DutyRotationStateView>>> GetRotationStatesAsync(CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyRotationStateView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var result = await _dbService.QueryAsync<DutyRotationStateView>(@"
            SELECT r.group_code, r.date_type, r.last_member_id, m.name AS last_member_name, r.updated_at
            FROM nc_duty_rotation_state r
            LEFT JOIN nc_duty_members m ON m.id = r.last_member_id
            ORDER BY r.group_code, r.date_type", ct);
        if (result.IsFailure)
            return Result.Failure<List<DutyRotationStateView>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<DutyRotationStateView>());
    }

    public async Task<Result> ResetRotationAsync(string groupCode, string dateType, CancellationToken ct = default)
    {
        if (!DutyConstants.Groups.IsValid(groupCode) || !DutyConstants.DateTypes.IsValid(dateType))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "组编码或日期类型无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync(
            "DELETE FROM nc_duty_rotation_state WHERE group_code = $1 AND date_type = $2", ct, groupCode, dateType);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("重置值班轮转游标", ("Group", groupCode), ("DateType", dateType));
            return Result.Success();
        }
        return result;
    }

    #endregion

    #region 通用取数

    /// <summary>
    /// 加载目标月之前各轮转线最近一次真实消耗人（跨月、跨年、跨断档的唯一接续依据）。
    /// 读 rotation_member_id（轮转消耗事实，不随串班/代班变化；旧数据为空回退 member_id），按 ID 定位。
    /// </summary>
    private async Task<Result<Dictionary<(string Group, string DateType), long?>>> LoadPreviousRotationFactsAsync(
        DateTime monthStart, CancellationToken ct)
    {
        var result = await _dbService.QueryAsync<DutyRotationState>(@"
            SELECT DISTINCT ON (group_code, date_type)
                   group_code, date_type, COALESCE(rotation_member_id, member_id) AS last_member_id
            FROM nc_duty_schedules
            WHERE duty_date < $1::date AND member_id IS NOT NULL
            ORDER BY group_code, date_type, duty_date DESC", ct, monthStart);
        if (!result.IsSuccess)
        {
            LogWarn($"查询本月前最近班次失败（{monthStart:yyyy-MM}）：{result.Message}");
            return Result.Failure<Dictionary<(string, string), long?>>(result.ErrorCode!, result.Message!);
        }

        var facts = (result.Value ?? new List<DutyRotationState>())
            .Where(r => r.LastMemberId.HasValue)
            .GroupBy(r => (r.GroupCode, r.DateType))
            .ToDictionary(g => g.Key, g => g.First().LastMemberId);
        return Result.Success(facts);
    }

    /// <summary>
    /// 目标月之后是否已有各轮转线的班次（回溯补生成判定用）：
    /// 只有"该线在目标月之外没有任何班次"时才允许用全局游标；只有更晚班次时按名单头开始。
    /// </summary>
    private async Task<Result<HashSet<(string Group, string DateType)>>> LoadFutureRotationLinesAsync(
        DateTime nextMonthStart, CancellationToken ct)
    {
        var result = await _dbService.QueryAsync<DutyRotationState>(
            "SELECT DISTINCT group_code, date_type FROM nc_duty_schedules WHERE duty_date >= $1::date",
            ct, nextMonthStart);
        if (!result.IsSuccess)
            return Result.Failure<HashSet<(string, string)>>(result.ErrorCode!, result.Message!);

        var lines = (result.Value ?? new List<DutyRotationState>())
            .Select(r => (r.GroupCode, r.DateType))
            .ToHashSet();
        return Result.Success(lines);
    }

    /// <summary>本月之后已生成班次的月份（yyyy-MM，升序；供"下游一键重排"）</summary>
    private async Task<Result<List<string>>> LoadDownstreamGeneratedMonthsAsync(
        int year, int month, CancellationToken ct)
    {
        var result = await _dbService.QueryAsync<string>(@"
            SELECT DISTINCT to_char(make_date(year, month, 1), 'YYYY-MM')
            FROM nc_duty_schedules
            WHERE year > $1 OR (year = $1 AND month > $2)
            ORDER BY 1", ct, year, month);
        if (!result.IsSuccess)
            return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<string>());
    }

    #endregion
}
