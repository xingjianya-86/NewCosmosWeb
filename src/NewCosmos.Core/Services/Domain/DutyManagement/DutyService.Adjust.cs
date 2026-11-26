using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.DutyManagement;

/// <summary>值班管理：班务调整（串班/代班）保存、撤销、改期与生成期重放。</summary>
public partial class DutyService
{
    #region 查询与重放状态

    public async Task<Result<List<DutyShiftChangeView>>> GetShiftChangesAsync(string? changeType, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyShiftChangeView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var result = await _dbService.QueryAsync<DutyShiftChangeView>(@"
            SELECT sc.id, sc.change_type, sc.from_member_id, sc.from_member_name, sc.duty_date,
                   sc.to_member_id, sc.to_member_name, sc.to_duty_date, sc.group_code, sc.reason,
                   sc.is_cancelled, sc.cancelled_at, sc.created_at,
                   COALESCE(fm.phone, '') AS from_member_phone,
                   COALESCE(tm.phone, '') AS to_member_phone
            FROM nc_duty_shift_changes sc
            LEFT JOIN nc_duty_members fm ON fm.id = sc.from_member_id
            LEFT JOIN nc_duty_members tm ON tm.id = sc.to_member_id
            WHERE ($1::character varying IS NULL OR sc.change_type = $1::character varying)
            ORDER BY sc.created_at DESC, sc.id DESC",
            ct, (object?)changeType ?? DBNull.Value);
        if (!result.IsSuccess)
            return Result.Failure<List<DutyShiftChangeView>>(result.ErrorCode!, result.Message!);

        var changes = result.Value ?? new List<DutyShiftChangeView>();
        await ApplyReplayStatusAsync(changes, ct);
        return Result.Success(changes);
    }

    /// <summary>
    /// 批量计算班务调整的重放状态（列表展示）：一次 ANY 批查班次后逐条判定，
    /// 已生效/待生效（缺对方月份或待重建）/已失效（轮转已变化）/已撤销。
    /// </summary>
    private async Task ApplyReplayStatusAsync(List<DutyShiftChangeView> changes, CancellationToken ct)
    {
        foreach (var c in changes.Where(x => x.IsCancelled))
        {
            c.ReplayStatus = DutyConstants.ChangeReplayStatuses.Cancelled;
            c.StatusText = "已撤销";
            c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
        }

        var active = changes.Where(x => !x.IsCancelled).ToList();
        if (active.Count == 0) return;

        var dates = active
            .SelectMany(c => new[] { c.DutyDate.Date, c.ToDutyDate?.Date ?? default })
            .Where(d => d != default)
            .Distinct()
            .ToArray();

        var rowResult = await _dbService.QueryAsync<DutySchedule>(@"
            SELECT duty_date, group_code, member_id, rotation_member_id
            FROM nc_duty_schedules
            WHERE duty_date = ANY(string_to_array($1, ',')::date[])",
            ct, string.Join(",", dates.Select(d => d.ToString("yyyy-MM-dd"))));
        if (!rowResult.IsSuccess)
        {
            LogWarn($"班务调整状态计算失败（班次批查）：{rowResult.Message}");
            foreach (var c in active)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.PendingRebuild;
                c.StatusText = "待生效（状态待刷新）";
                c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
            }
            return;
        }

        var rows = (rowResult.Value ?? new List<DutySchedule>())
            .GroupBy(s => (s.DutyDate.Date, s.GroupCode))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var c in active)
        {
            var missing = new List<string>();
            if (!rows.TryGetValue((c.DutyDate.Date, c.GroupCode), out var fromRow))
                missing.Add($"{c.DutyDate:yyyy-MM}");
            DutySchedule? toRow = null;
            if (c.ChangeType == DutyConstants.ChangeTypes.SWAP && c.ToDutyDate.HasValue
                && !rows.TryGetValue((c.ToDutyDate.Value.Date, c.GroupCode), out toRow))
            {
                missing.Add($"{c.ToDutyDate.Value:yyyy-MM}");
            }

            if (missing.Count > 0)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.PendingUnbuilt;
                c.StatusText = $"待生效（缺 {string.Join("、", missing.Distinct().OrderBy(x => x))}）";
                c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
                continue;
            }

            bool applied;
            bool rotationMatched;
            if (c.ChangeType == DutyConstants.ChangeTypes.SWAP)
            {
                applied = c.FromMemberId.HasValue && c.ToMemberId.HasValue
                          && fromRow!.MemberId == c.ToMemberId && toRow!.MemberId == c.FromMemberId;
                var fromRot = fromRow!.RotationMemberId ?? fromRow.MemberId;
                var toRot = toRow!.RotationMemberId ?? toRow.MemberId;
                rotationMatched = fromRot == c.FromMemberId && toRot == c.ToMemberId;
            }
            else
            {
                applied = c.ToMemberId.HasValue && fromRow!.MemberId == c.ToMemberId;
                var fromRot = fromRow!.RotationMemberId ?? fromRow.MemberId;
                rotationMatched = fromRot == c.FromMemberId;
            }

            if (applied)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.Applied;
                c.StatusText = "已生效";
            }
            else if (rotationMatched)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.PendingRebuild;
                c.StatusText = "待生效（重新生成后生效）";
            }
            else
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.Invalid;
                c.StatusText = "已失效（轮转已变化，需改期或撤销）";
            }
            c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
        }
    }

    #endregion

    #region 新增串班/代班

    public async Task<Result> SaveShiftChangeAsync(DutyShiftChangeSave change, CancellationToken ct = default)
    {
        if (change is null || !DutyConstants.ChangeTypes.IsValid(change.ChangeType))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "班务调整类型无效");
        if (change.FromMemberId <= 0 || change.ToMemberId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "班务调整人员无效");
        if (change.FromMemberId == change.ToMemberId)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "顶班人员不能与原班人员相同");
        if (change.DutyDate.Date < DateTime.Today)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "仅支持调整今天及以后的班次");

        var isSwap = change.ChangeType == DutyConstants.ChangeTypes.SWAP;
        var toDutyDate = change.ToDutyDate?.Date;
        if (isSwap)
        {
            if (toDutyDate is null)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "串班需选择对方的班次日期");
            if (toDutyDate.Value < DateTime.Today)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "仅支持调整今天及以后的班次");
            if (toDutyDate.Value == change.DutyDate.Date)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "串班的两个班次不能是同一天");
        }

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        // 甲方的班次必须存在且属于甲方
        var fromScheduleResult = await _dbService.QuerySingleAsync<DutySchedule>(@"
            SELECT id, duty_date, date_type, group_code, member_id, member_name, year, month
            FROM nc_duty_schedules
            WHERE duty_date = $1 AND member_id = $2
            LIMIT 1", ct, change.DutyDate.Date, change.FromMemberId);
        if (!fromScheduleResult.IsSuccess)
            return Result.Failure(fromScheduleResult.ErrorCode!, fromScheduleResult.Message!);
        if (fromScheduleResult.Value is null)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_NOT_FOUND, "该日期没有此人的值班班次");
        var fromSchedule = fromScheduleResult.Value;

        // 占用校验：同一班次同一时间只允许一条生效调整（防重复创建互相覆盖；改期走 Reschedule）
        var occupiedFrom = await CountActiveChangesOnDateAsync(fromSchedule.DutyDate, fromSchedule.GroupCode, null, ct);
        if (!occupiedFrom.IsSuccess)
            return Result.Failure(occupiedFrom.ErrorCode!, occupiedFrom.Message!);
        if (occupiedFrom.Value > 0)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED, "甲方该日期的班次已有一条生效的调整记录，请先撤销或改期");

        DutySchedule? toSchedule = null;
        if (isSwap)
        {
            // 乙方的班次必须存在且属于乙方（被换出的班日）
            var toScheduleResult = await _dbService.QuerySingleAsync<DutySchedule>(@"
                SELECT id, duty_date, date_type, group_code, member_id, member_name, year, month
                FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2
                LIMIT 1", ct, toDutyDate!.Value, change.ToMemberId);
            if (!toScheduleResult.IsSuccess)
                return Result.Failure(toScheduleResult.ErrorCode!, toScheduleResult.Message!);
            if (toScheduleResult.Value is null)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_NOT_FOUND, "对方在该日期没有值班班次，无法对调");
            toSchedule = toScheduleResult.Value;

            var occupiedTo = await CountActiveChangesOnDateAsync(toSchedule.DutyDate, toSchedule.GroupCode, null, ct);
            if (!occupiedTo.IsSuccess)
                return Result.Failure(occupiedTo.ErrorCode!, occupiedTo.Message!);
            if (occupiedTo.Value > 0)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED, "乙方该日期的班次已有一条生效的调整记录，请先撤销或改期");

            // 串班限同组：两个班次必须同组
            if (toSchedule.GroupCode != fromSchedule.GroupCode)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "串班的两个班次必须属于同一值班组");
        }

        // 乙方人员：存在 + 启用 + 当日可用（入职/离职边界）
        var memberResult = await _dbService.QuerySingleAsync<DutyMemberView>(
            "SELECT id, name, is_active, join_date, exit_date FROM nc_duty_members WHERE id = $1",
            ct, change.ToMemberId);
        if (!memberResult.IsSuccess)
            return Result.Failure(memberResult.ErrorCode!, memberResult.Message!);
        if (memberResult.Value is null || !memberResult.Value.IsActive)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员不存在或已停用");
        var toMember = memberResult.Value;
        if (toMember.JoinDate.HasValue && toMember.JoinDate.Value > change.DutyDate.Date)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员在调整日期尚未入职");
        if (toMember.ExitDate.HasValue && toMember.ExitDate.Value < change.DutyDate.Date)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员在调整日期已离职");

        // 同组校验：乙方必须与甲方班次同组
        var inGroupResult = await _dbService.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_duty_member_groups WHERE member_id = $1 AND group_code = $2",
            ct, change.ToMemberId, fromSchedule.GroupCode);
        if (!inGroupResult.IsSuccess || inGroupResult.Value == 0)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员与原班人员不在同一值班组，串班/代班限同组");

        // 请假校验：调整后实际顶班者不得处于请假期间
        var leaveResult = await LoadLeaveRangesAsync(ct);
        if (leaveResult.IsFailure)
            return Result.Failure(leaveResult.ErrorCode!, leaveResult.Message!);
        var leaveRanges = leaveResult.Value;

        if (IsOnLeave(leaveRanges, change.ToMemberId, change.DutyDate.Date))
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID,
                $"{toMember.Name} 在 {change.DutyDate:yyyy-MM-dd} 处于请假期间，无法{(isSwap ? "串班" : "代班")}");
        if (isSwap && IsOnLeave(leaveRanges, change.FromMemberId, toDutyDate!.Value))
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID,
                $"{fromSchedule.MemberName} 在 {toDutyDate:yyyy-MM-dd} 处于请假期间，无法串班");

        // 互占校验（串班）：对调后双方各值一天一班
        if (isSwap)
        {
            var busyResult = await _dbService.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2 AND id <> $3",
                ct, change.DutyDate.Date, change.ToMemberId, fromSchedule.Id);
            if (!busyResult.IsSuccess)
                return Result.Failure(busyResult.ErrorCode!, busyResult.Message!);
            if (busyResult.Value > 0)
                return Result.Failure(ErrorCodes.DUTY_MEMBER_CONFLICT, "对方在该日期已有其他班次，无法对调");

            var busyBackResult = await _dbService.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2 AND id <> $3",
                ct, toDutyDate!.Value, change.FromMemberId, toSchedule!.Id);
            if (!busyBackResult.IsSuccess)
                return Result.Failure(busyBackResult.ErrorCode!, busyBackResult.Message!);
            if (busyBackResult.Value > 0)
                return Result.Failure(ErrorCodes.DUTY_MEMBER_CONFLICT, "转让人在对方班次日期已有其他班次，无法对调");
        }

        var typeName = DutyConstants.ChangeTypes.DisplayName(change.ChangeType);
        var reason = change.Reason?.Trim() ?? string.Empty;
        var fromName = fromSchedule.MemberName;

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            // 留痕（from/to 姓名快照，成员删除后历史仍可显示）
            var insertResult = await _dbService.ExecuteNonQueryAsync(@"
                INSERT INTO nc_duty_shift_changes
                    (change_type, from_member_id, from_member_name, duty_date, to_member_id, to_member_name, to_duty_date, group_code, reason, is_cancelled, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, FALSE, NOW())",
                ct, change.ChangeType, change.FromMemberId, fromName, change.DutyDate.Date,
                change.ToMemberId, toMember.Name, isSwap ? toDutyDate : null, fromSchedule.GroupCode, reason);
            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return insertResult;
            }

            if (isSwap)
            {
                // 相互对调：甲方班日改乙方上，乙方班日改甲方上（两班均留痕）
                var remark = $"串班：{fromName}({change.DutyDate:MM-dd}) ↔ {toMember.Name}({toDutyDate:MM-dd}){(string.IsNullOrEmpty(reason) ? string.Empty : $"（{reason}）")}";
                var updateA = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct, change.ToMemberId, toMember.Name, remark, fromSchedule.Id);
                if (updateA.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateA;
                }

                var updateB = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct, change.FromMemberId, fromName, remark, toSchedule!.Id);
                if (updateB.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateB;
                }
            }
            else
            {
                // 代班：代班人顶上一次（单班次变更）
                var updateResult = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct,
                    change.ToMemberId, toMember.Name,
                    $"代班：{toMember.Name} 代 {fromName}{(string.IsNullOrEmpty(reason) ? string.Empty : $"（{reason}）")}",
                    fromSchedule.Id);
                if (updateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateResult;
                }
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Result.FromException(ex);
        }

        Logger.LogBusiness($"值班{typeName}",
            ("Date", isSwap ? $"{change.DutyDate:yyyy-MM-dd}↔{toDutyDate:yyyy-MM-dd}" : change.DutyDate.ToString("yyyy-MM-dd")),
            ("Group", fromSchedule.GroupCode),
            ("From", DataMasker.MaskName(fromName)),
            ("To", DataMasker.MaskName(toMember.Name)),
            ("Reason", reason));
        return Result.Success();
    }

    #endregion

    #region 撤销与改期

    public async Task<Result> CancelShiftChangeAsync(long changeId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var changeResult = await _dbService.QuerySingleAsync<DutyShiftChange>(
            "SELECT id, change_type, from_member_id, from_member_name, duty_date, to_member_id, to_member_name, to_duty_date, group_code, is_cancelled FROM nc_duty_shift_changes WHERE id = $1",
            ct, changeId);
        if (!changeResult.IsSuccess)
            return Result.Failure(changeResult.ErrorCode!, changeResult.Message!);
        if (changeResult.Value is null)
            return Result.Failure(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "未找到班务调整记录");
        if (changeResult.Value.IsCancelled)
            return Result.Failure(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "该记录已撤销，不能重复撤销");

        var cancelResult = await _dbService.ExecuteNonQueryAsync(@"
            UPDATE nc_duty_shift_changes
            SET is_cancelled = TRUE, cancelled_at = NOW()
            WHERE id = $1", ct, changeId);
        if (cancelResult.IsFailure) return cancelResult;

        Logger.LogBusiness("撤销班务调整",
            ("ChangeId", changeId),
            ("Type", DutyConstants.ChangeTypes.DisplayName(changeResult.Value.ChangeType)),
            ("From", DataMasker.MaskName(changeResult.Value.FromMemberName)),
            ("To", DataMasker.MaskName(changeResult.Value.ToMemberName)),
            ("Date", changeResult.Value.DutyDate.ToString("yyyy-MM-dd")));

        // 强制重生成受影响月份（跨月对调：甲方班日月 ∪ 乙方班日月）：
        // 被撤记录不再重放，其余未撤销调整自动重放，班次自然恢复
        var rebuildDates = new List<DateTime> { changeResult.Value.DutyDate };
        if (changeResult.Value.ToDutyDate.HasValue)
            rebuildDates.Add(changeResult.Value.ToDutyDate.Value);
        var rebuildResult = await RebuildMonthsOnDatesAsync(rebuildDates, ct);
        return rebuildResult.IsFailure ? rebuildResult : Result.Success();
    }

    public async Task<Result> RescheduleShiftChangeAsync(long changeId, DateTime newDate, CancellationToken ct = default)
    {
        if (newDate.Date < DateTime.Today)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "仅支持改期到今天及以后");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var changeResult = await _dbService.QuerySingleAsync<DutyShiftChange>(
            "SELECT id, change_type, from_member_id, from_member_name, duty_date, to_member_id, to_member_name, to_duty_date, group_code, is_cancelled FROM nc_duty_shift_changes WHERE id = $1",
            ct, changeId);
        if (!changeResult.IsSuccess)
            return Result.Failure(changeResult.ErrorCode!, changeResult.Message!);
        var change = changeResult.Value;
        if (change is null || change.IsCancelled)
            return Result.Failure(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "未找到班务调整记录，或记录已撤销");

        var isSwap = change.ChangeType == DutyConstants.ChangeTypes.SWAP;
        // 改期目标端：串班=乙方的班日（乙方换一天出班）；代班=被代班日（甲方换一天被代）
        var releaserId = (isSwap ? change.ToMemberId : change.FromMemberId).GetValueOrDefault();   // 换出班次的人
        var receiverId = (isSwap ? change.FromMemberId : change.ToMemberId).GetValueOrDefault();   // 顶上的人
        var oldDate = (isSwap ? change.ToDutyDate : change.DutyDate).GetValueOrDefault();
        if (releaserId <= 0 || receiverId <= 0 || oldDate == default)
            return Result.Failure(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "班务调整记录缺少必要人员或日期");

        if (newDate.Date == oldDate.Date)
            return Result.Success();   // 日期未变化

        // 对端班日不可与改期目标重合（串班：不能把乙方的班换到甲方的班日上）
        if (isSwap && newDate.Date == change.DutyDate.Date)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "改期后的班日不能与甲方的班日为同一天");

        // 新日期必须存在换出人的班次（该班次将被顶班人顶上）
        var newScheduleResult = await _dbService.QuerySingleAsync<DutySchedule>(@"
            SELECT id, duty_date, date_type, group_code, member_id, member_name, year, month
            FROM nc_duty_schedules
            WHERE duty_date = $1 AND member_id = $2
            LIMIT 1", ct, newDate.Date, releaserId);
        if (!newScheduleResult.IsSuccess)
            return Result.Failure(newScheduleResult.ErrorCode!, newScheduleResult.Message!);
        if (newScheduleResult.Value is null)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_NOT_FOUND, "对方在新日期没有值班班次，无法改期");
        var newSchedule = newScheduleResult.Value;
        if (newSchedule.GroupCode != change.GroupCode)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "新日期的班次与原调整不属于同一值班组");

        // 占用校验：新日期班次未被其他生效调整占用（排除本记录自身）
        var occupied = await CountActiveChangesOnDateAsync(newSchedule.DutyDate, newSchedule.GroupCode, changeId, ct);
        if (!occupied.IsSuccess)
            return Result.Failure(occupied.ErrorCode!, occupied.Message!);
        if (occupied.Value > 0)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED, "新日期的班次已有一条生效的调整记录，请先撤销或改期");

        // 串班互占校验：改期后顶班人（甲方）在新日期不能已有其他班次（对调后一人一天一班）
        if (isSwap)
        {
            var otherDate = change.DutyDate.Date;
            var busyResult = await _dbService.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2 AND id <> $3",
                ct, newDate.Date, receiverId, newSchedule.Id);
            if (!busyResult.IsSuccess)
                return Result.Failure(busyResult.ErrorCode!, busyResult.Message!);
            if (busyResult.Value > 0 && otherDate != newDate.Date)
                return Result.Failure(ErrorCodes.DUTY_MEMBER_CONFLICT, "顶班人在新日期已有其他班次，无法改期");
        }

        // 更新记录日期（SWAP=to_duty_date；SUB=duty_date）
        var updateSql = isSwap
            ? "UPDATE nc_duty_shift_changes SET to_duty_date = $1 WHERE id = $2"
            : "UPDATE nc_duty_shift_changes SET duty_date = $1 WHERE id = $2";
        var updateResult = await _dbService.ExecuteNonQueryAsync(updateSql, ct, newDate.Date, changeId);
        if (updateResult.IsFailure) return updateResult;

        Logger.LogBusiness("班务调整改期",
            ("ChangeId", changeId),
            ("Type", DutyConstants.ChangeTypes.DisplayName(change.ChangeType)),
            ("OldDate", oldDate.ToString("yyyy-MM-dd")),
            ("NewDate", newDate.Date.ToString("yyyy-MM-dd")),
            ("From", DataMasker.MaskName(change.FromMemberName)),
            ("To", DataMasker.MaskName(change.ToMemberName)));

        // 重生成新旧日期所在月：新日期按记录重放应用（顶班人顶上），旧日期恢复生成值
        var rebuildResult = await RebuildMonthsOnDatesAsync(
            new[] { oldDate, newDate, change.DutyDate, change.ToDutyDate ?? change.DutyDate }, ct);
        return rebuildResult.IsFailure ? rebuildResult : Result.Success();
    }

    public async Task<Result<List<DutyFutureSchedule>>> GetMemberFutureSchedulesAsync(long memberId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyFutureSchedule>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var result = await _dbService.QueryAsync<DutyFutureSchedule>(@"
            SELECT id AS schedule_id, duty_date, date_type, group_code, member_id, member_name, is_adjusted
            FROM nc_duty_schedules
            WHERE member_id = $1 AND duty_date >= $2
            ORDER BY duty_date", ct, memberId, DateTime.Today);
        if (result.IsFailure)
            return Result.Failure<List<DutyFutureSchedule>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<DutyFutureSchedule>());
    }

    /// <summary>统计某日期班次上生效的班务调整记录数（同组同日唯一班次；excludeChangeId 用于改期排除自身）</summary>
    private async Task<Result<long>> CountActiveChangesOnDateAsync(DateTime dutyDate, string groupCode, long? excludeChangeId, CancellationToken ct)
    {
        var result = await _dbService.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM nc_duty_shift_changes
            WHERE is_cancelled = FALSE AND group_code = $1
              AND (duty_date = $2 OR to_duty_date = $2)
              AND ($3::bigint IS NULL OR id <> $3)",
            ct, groupCode, dutyDate.Date, (object?)excludeChangeId ?? DBNull.Value);
        return result.IsSuccess ? Result.Success(result.Value) : Result.Failure<long>(result.ErrorCode!, result.Message!);
    }

    /// <summary>
    /// 强制重生成给定日期覆盖的、已生成过班次的月份（日期去重后按月执行；未生成月不触碰）。
    /// </summary>
    private async Task<Result> RebuildMonthsOnDatesAsync(IEnumerable<DateTime> dates, CancellationToken ct)
    {
        var monthSet = new SortedSet<(int Year, int Month)>();
        foreach (var d in dates)
        {
            if (d == default) continue;
            monthSet.Add((d.Year, d.Month));
        }

        foreach (var (y, m) in monthSet)
        {
            var countResult = await _dbService.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, y, m);
            if (!countResult.IsSuccess)
                return Result.Failure(countResult.ErrorCode!, countResult.Message!);
            if (countResult.Value == 0) continue;   // 未生成月不触碰

            var genResult = await GenerateMonthAsync(y, m, force: true, ct);
            if (genResult.IsFailure)
                return Result.Failure(genResult.ErrorCode!, $"重生成 {y}-{m:D2} 值班表失败：{genResult.Message}");
            LogInfo($"班务调整联动：{y}-{m:D2} 值班表已强制重生成");
        }
        return Result.Success();
    }

    #endregion

    #region 生成期重放

    /// <summary>班务调整重放结果：生效/待生效/跳过统计 + 待生效缺月份（不落库）</summary>
    private sealed class ShiftReplayOutcome
    {
        public int AppliedCount { get; set; }
        public int PendingCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> MissingMonths { get; } = new();

        public void AddMissing(DateTime date)
        {
            var text = $"{date:yyyy-MM}";
            if (!MissingMonths.Contains(text))
                MissingMonths.Add(text);
        }
    }

    /// <summary>
    /// 重放指定月份的未撤销串班/代班记录到已生成班次（生成事务内调用）。
    /// 链式合理性：串班两班成对预检（两班"轮转消耗人"必须分别为甲/乙方）都匹配才成对替换；
    /// 代班单班校验（轮转消耗人=被代者）才替换；不匹配则整条跳过。
    /// </summary>
    private async Task<ShiftReplayOutcome> ApplyShiftChangesAsync(int year, int month, CancellationToken ct)
    {
        var outcome = new ShiftReplayOutcome();
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var changesResult = await _dbService.QueryAsync<DutyShiftChange>(@"
            SELECT id, change_type, from_member_id, from_member_name, duty_date,
                   to_member_id, to_member_name, to_duty_date, group_code, reason
            FROM nc_duty_shift_changes
            WHERE is_cancelled = FALSE
              AND ((duty_date >= $1 AND duty_date < $2) OR (to_duty_date IS NOT NULL AND to_duty_date >= $1 AND to_duty_date < $2))
            ORDER BY duty_date, id", ct, monthStart, monthEnd);
        if (!changesResult.IsSuccess)
        {
            LogWarn($"班务调整重放查询失败：{changesResult.Message}");
            return outcome;   // 查询失败不阻断生成（班次保持生成结果）
        }
        var changes = changesResult.Value ?? new List<DutyShiftChange>();
        if (changes.Count == 0) return outcome;

        var leaveResult = await LoadLeaveRangesAsync(ct);
        var leaveRanges = leaveResult.IsSuccess
            ? leaveResult.Value
            : new List<(long MemberId, DateTime Start, DateTime End)>();
        if (!leaveResult.IsSuccess)
            LogWarn($"班务调整重放：请假区间加载失败，本次重放不校验请假（{leaveResult.Message}）");

        foreach (var c in changes)
        {
            var typeName = DutyConstants.ChangeTypes.DisplayName(c.ChangeType);
            var remark = $"{typeName}：{(c.ChangeType == DutyConstants.ChangeTypes.SWAP
                ? $"{c.FromMemberName}({c.DutyDate:MM-dd}) ↔ {c.ToMemberName}({c.ToDutyDate:MM-dd})"
                : $"{c.ToMemberName} 代 {c.FromMemberName}")}{(string.IsNullOrEmpty(c.Reason) ? string.Empty : $"（{c.Reason}）")}";

            if (c.ChangeType == DutyConstants.ChangeTypes.SWAP)
            {
                // 串班成对预检：两班的"轮转消耗人"（rotation_member_id）必须分别为甲/乙方
                var pairResult = await _dbService.QueryAsync<DutySchedule>(@"
                    SELECT id, duty_date, member_id, rotation_member_id FROM nc_duty_schedules
                    WHERE (duty_date = $1 AND group_code = $3)
                       OR (duty_date = $2 AND group_code = $3)",
                    ct, c.DutyDate, c.ToDutyDate!.Value, c.GroupCode);
                var pair = pairResult.Value ?? new List<DutySchedule>();
                var fromSchedule = pair.FirstOrDefault(s => s.DutyDate == c.DutyDate.Date);
                var toSchedule = pair.FirstOrDefault(s => s.DutyDate == c.ToDutyDate.Value);

                if (fromSchedule is null || toSchedule is null)
                {
                    if (fromSchedule is null) outcome.AddMissing(c.DutyDate);
                    if (toSchedule is null) outcome.AddMissing(c.ToDutyDate.Value);
                    outcome.PendingCount++;
                    LogWarn($"班务调整重放待生效（对方月份未生成）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                    continue;
                }

                var fromRot = fromSchedule.RotationMemberId ?? fromSchedule.MemberId;
                var toRot = toSchedule.RotationMemberId ?? toSchedule.MemberId;
                if (fromRot != c.FromMemberId || toRot != c.ToMemberId)
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（对调双方班次状态已变化）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                    continue;
                }

                if ((c.ToMemberId.HasValue && IsOnLeave(leaveRanges, c.ToMemberId.Value, c.DutyDate.Date))
                    || (c.FromMemberId.HasValue && c.ToDutyDate.HasValue
                        && IsOnLeave(leaveRanges, c.FromMemberId.Value, c.ToDutyDate.Value)))
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（命中请假区间）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                    continue;
                }

                var updateA = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct, c.ToMemberId, c.ToMemberName, remark, fromSchedule.Id);
                var updateB = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct, c.FromMemberId, c.FromMemberName, remark, toSchedule.Id);
                if (updateA.IsFailure || updateB.IsFailure)
                {
                    LogWarn($"班务调整重放失败：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                }
                else
                {
                    outcome.AppliedCount++;
                }
            }
            else
            {
                // 代班：单班校验（轮转消耗人=被代者）
                if (c.ToMemberId.HasValue && IsOnLeave(leaveRanges, c.ToMemberId.Value, c.DutyDate.Date))
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（代班人处于请假期间）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd} " +
                            $"{DataMasker.MaskName(c.FromMemberName)}→{DataMasker.MaskName(c.ToMemberName)}");
                    continue;
                }

                var updateResult = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE duty_date = $4 AND group_code = $5 AND COALESCE(rotation_member_id, member_id) = $6",
                    ct, c.ToMemberId, c.ToMemberName, remark, c.DutyDate, c.GroupCode, c.FromMemberId);
                if (updateResult.IsFailure)
                {
                    LogWarn($"班务调整重放失败：changeId={c.Id} {c.DutyDate:yyyy-MM-dd} {typeName}，{updateResult.Message}");
                }
                else if (updateResult.Value == 0)
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（班次不存在或被代者已变化）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd} " +
                            $"{DataMasker.MaskName(c.FromMemberName)}→{DataMasker.MaskName(c.ToMemberName)}");
                }
                else
                {
                    outcome.AppliedCount++;
                }
            }
        }

        return outcome;
    }

    #endregion
}
