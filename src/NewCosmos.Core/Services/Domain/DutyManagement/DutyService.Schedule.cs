using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.DutyManagement;

/// <summary>值班管理：月度值班表查询与轮转生成核心。</summary>
public partial class DutyService
{
    #region 月度值班表查询

    public async Task<Result<DutyMonthSchedule>> GetMonthScheduleAsync(int year, int month, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure<DutyMonthSchedule>(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyMonthSchedule>(ensureResult.ErrorCode!, ensureResult.Message!);

        var scheduleResult = await _dbService.QueryAsync<DutySchedule>(@"
            SELECT s.id, s.duty_date, s.date_type, s.group_code, s.member_id, s.rotation_member_id, s.member_name,
                   COALESCE(m.phone, '') AS phone, s.year, s.month, s.is_adjusted, s.remark, s.created_at
            FROM nc_duty_schedules s
            LEFT JOIN nc_duty_members m ON m.id = s.member_id
            WHERE s.year = $1 AND s.month = $2
            ORDER BY s.duty_date", ct, year, month);
        if (!scheduleResult.IsSuccess)
            return Result.Failure<DutyMonthSchedule>(scheduleResult.ErrorCode!, scheduleResult.Message!);

        var schedules = scheduleResult.Value ?? new List<DutySchedule>();
        var scheduleLookup = schedules.ToLookup(s => (s.DutyDate.Date, s.GroupCode));
        var dateLookup = schedules.ToLookup(s => s.DutyDate.Date);

        var samePairResult = await GetRestdayLeaderSamePairAsync(ct);
        var monthSchedule = new DutyMonthSchedule
        {
            Year = year,
            Month = month,
            IsGenerated = schedules.Count > 0,
            RestdayLeaderSamePair = samePairResult.IsSuccess && samePairResult.Value
        };

        var days = DateTime.DaysInMonth(year, month);
        for (var day = 1; day <= days; day++)
        {
            var date = new DateTime(year, month, day);
            var daySchedules = dateLookup[date.Date].ToList();

            // 已固化班次的日期类型以生成时快照为准（节假日数据后续变动不影响历史展示）
            var dateType = daySchedules.Count > 0
                ? daySchedules[0].DateType
                : ClassifyDate(date);

            var row = new DutyDayRow
            {
                DutyDate = date,
                DateType = dateType,
                HolidayName = dateType == DutyConstants.DateTypes.HOLIDAY
                    ? _holidayService.GetLegalHolidayName(date) ?? string.Empty
                    : string.Empty
            };
            var required = RequiredGroups(dateType);

            foreach (var group in DutyConstants.Groups.DisplayOrder)
            {
                var schedule = scheduleLookup[(date, group)].FirstOrDefault();
                row.Cells.Add(new DutyScheduleCell
                {
                    ScheduleId = schedule?.Id ?? 0,
                    DutyDate = date,
                    MemberId = schedule?.MemberId,
                    GroupCode = group,
                    GroupName = DutyConstants.Groups.DisplayName(group),
                    MemberName = schedule?.MemberName ?? string.Empty,
                    Phone = schedule?.Phone ?? string.Empty,
                    IsAdjusted = schedule?.IsAdjusted ?? false,
                    IsRequired = required.Contains(group)
                });
            }

            monthSchedule.Days.Add(row);
        }

        return Result.Success(monthSchedule);
    }

    #endregion

    #region 月度值班表生成（轮转核心）

    public async Task<Result<DutyGenerationSummary>> GenerateMonthAsync(int year, int month, bool force, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyGenerationSummary>(ensureResult.ErrorCode!, ensureResult.Message!);

        var countResult = await _dbService.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, year, month);
        if (!countResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(countResult.ErrorCode!, countResult.Message!);
        if (countResult.Value > 0 && !force)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_SCHEDULE_EXISTS, $"{year} 年 {month} 月值班表已生成");

        // 加载轮转队列：组 → (成员ID, 姓名) 按 sort_order, id 排序（仅启用成员）
        var queueResult = await LoadActiveQueuesAsync(ct);
        if (!queueResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(queueResult.ErrorCode!, queueResult.Message!);
        var queues = queueResult.Value;

        if (queues.Values.Sum(q => q.Count) == 0)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_NO_MEMBERS, "没有可用的值班成员");

        // 加载轮转游标（仅作"该轮转线在目标月之外没有任何班次"时的兜底起点）
        var rotationResult = await _dbService.QueryAsync<DutyRotationState>(
            "SELECT id, group_code, date_type, last_member_id, updated_at FROM nc_duty_rotation_state", ct);
        if (!rotationResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(rotationResult.ErrorCode!, rotationResult.Message!);
        var rotationMap = (rotationResult.Value ?? new List<DutyRotationState>())
            .GroupBy(r => (r.GroupCode, r.DateType))
            .ToDictionary(g => g.Key, g => g.First().LastMemberId);

        // 请假区间（生成时逐日判定：区间内成员顺延跳过并消耗轮次）
        var leaveResult = await LoadLeaveRangesAsync(ct);
        if (!leaveResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(leaveResult.ErrorCode!, leaveResult.Message!);
        var leaveRanges = leaveResult.Value;

        var samePairResult = await GetRestdayLeaderSamePairAsync(ct);
        var sameLeaderPair = samePairResult.IsSuccess && samePairResult.Value;

        // 逐日推演（内存计算，事务内一次性落库）
        var summary = new DutyGenerationSummary();
        var pendingSchedules = new List<(DateTime Date, string DateType, string Group, long MemberId, string MemberName)>();
        var rotationUpdates = new Dictionary<(string Group, string DateType), long>();
        var initializedLines = new HashSet<(string Group, string DateType)>();
        var backfillFallbackUsed = false;

        var days = DateTime.DaysInMonth(year, month);
        var monthStart = new DateTime(year, month, 1);
        var startDay = 1;
        var isAnchorMonth = false;

        // 轮转锚点截断：导入设置的锚点落在本月时，仅从锚点日起生成
        //（锚点日当天即轮到"当前序列"成员；之前的日期不生成，保持空白/旧安排）
        var anchorSetting = await GetSettingValueAsync(DutyConstants.SettingKeys.IMPORT_ANCHOR_DATE, ct);
        if (anchorSetting.IsSuccess &&
            DateTime.TryParse(anchorSetting.Value, out var anchor) &&
            anchor.Year == year && anchor.Month == month)
        {
            isAnchorMonth = true;
            startDay = anchor.Day;
            summary.AnchorDay = startDay;
            LogInfo($"轮转锚点生效：{year}-{month:D2} 从 {startDay} 日起生成");
        }

        var factsResult = await LoadPreviousRotationFactsAsync(monthStart, ct);
        if (!factsResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(factsResult.ErrorCode!, factsResult.Message!);
        var previousFacts = factsResult.Value;

        var futureResult = await LoadFutureRotationLinesAsync(monthStart.AddMonths(1), ct);
        if (!futureResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(futureResult.ErrorCode!, futureResult.Message!);
        var futureLines = futureResult.Value;

        for (var day = startDay; day <= days; day++)
        {
            var date = new DateTime(year, month, day);
            var dateType = ClassifyDate(date);
            switch (dateType)
            {
                case DutyConstants.DateTypes.WORKDAY: summary.WorkdayCount++; break;
                case DutyConstants.DateTypes.RESTDAY: summary.RestdayCount++; break;
                default: summary.HolidayCount++; break;
            }

            var selected = new HashSet<long>();
            var required = RequiredGroups(dateType);

            foreach (var group in DutyConstants.Groups.SelectionOrder)
            {
                if (!required.Contains(group)) continue;

                // 开关规则：休息日的周日复用周六所选领导（不推进领导组休息日游标）
                // 复用前必须校验当日可用性（请假 / 入职离职 / 同日已出班）：命中则回退正常轮转
                if (group == DutyConstants.Groups.LEADER &&
                    dateType == DutyConstants.DateTypes.RESTDAY &&
                    sameLeaderPair &&
                    date.DayOfWeek == DayOfWeek.Sunday)
                {
                    var saturdayLeader = await TryGetSaturdayLeaderAsync(date.AddDays(-1), pendingSchedules, ct);
                    if (saturdayLeader.HasValue)
                    {
                        var saturdayMember = queues.TryGetValue(group, out var leaderQueue)
                            ? leaderQueue.FirstOrDefault(m => m.Id == saturdayLeader.Value.Id)
                            : null;
                        if (saturdayMember is not null &&
                            !selected.Contains(saturdayLeader.Value.Id) &&
                            saturdayMember.AvailableOn(date) &&
                            !IsOnLeave(leaveRanges, saturdayLeader.Value.Id, date))
                        {
                            pendingSchedules.Add((date, dateType, group, saturdayLeader.Value.Id, saturdayLeader.Value.Name));
                            continue;
                        }
                        summary.UnavailableSkippedCount++;
                        LogInfo($"休息日领导同人复用跳过：{saturdayLeader.Value.Name} 在 {date:yyyy-MM-dd} 请假/不可用，改按正常轮转顺延");
                    }
                    // 周六无领导班次（如领导组无人）时按正常轮转处理
                }

                if (!queues.TryGetValue(group, out var queue) || queue.Count == 0)
                {
                    LogWarn($"组 {group} 无启用成员，{date:yyyy-MM-dd} {DutyConstants.Groups.DisplayName(group)} 班次缺位");
                    continue;
                }

                // 起点语义（该线本月首次选人，四级回退）：
                //   ① 目标月之前最近一条真实班次
                //   ② 锚点月校准起点（仅锚点月且无前序班次时命中）
                //   ③ 轮转游标（仅该线在目标月之外没有任何班次时使用）
                //   ④ 名单头（游标为空；或回溯补生成更早月份时无未来依据）
                var lineKey = (group, dateType);
                long? lastMemberId;
                if (initializedLines.Add(lineKey))
                {
                    lastMemberId = previousFacts.TryGetValue(lineKey, out var prevFactId) ? prevFactId : null;
                    if (!lastMemberId.HasValue && isAnchorMonth)
                        lastMemberId = await GetAnchorStartMemberAsync(group, dateType, ct);
                    if (!lastMemberId.HasValue)
                    {
                        if (futureLines.Contains(lineKey))
                        {
                            backfillFallbackUsed = true;
                            LogWarn($"回溯补生成 {year}-{month:D2}：{group}/{dateType} 线无前序班次，按名单头开始");
                        }
                        else
                        {
                            rotationMap.TryGetValue(lineKey, out lastMemberId);
                        }
                    }
                    rotationMap[lineKey] = lastMemberId;
                }
                else
                {
                    rotationMap.TryGetValue(lineKey, out lastMemberId);
                }

                var startIndex = 0;
                if (lastMemberId.HasValue)
                {
                    var idx = queue.FindIndex(m => m.Id == lastMemberId.Value);
                    if (idx >= 0) startIndex = idx + 1;
                    // 起点人已被删除/停用时从头轮转（队列仅含启用成员，按 ID 定位，与姓名无关）
                }

                (long Id, string Name)? chosen = null;
                for (var i = 0; i < queue.Count; i++)
                {
                    var candidate = queue[(startIndex + i) % queue.Count];
                    // 节假日规则：中层干部组成员只在中层班值班，其他班组直接跳过（不消耗轮次）
                    if (dateType == DutyConstants.DateTypes.HOLIDAY &&
                        group != DutyConstants.Groups.MIDDLE &&
                        candidate.IsMiddle)
                    {
                        summary.UnavailableSkippedCount++;
                        continue;
                    }
                    // 同人一天一班冲突 / 当日请假 / 入职离职边界 → 顺延跳过（被跳过者视为已消耗轮次）
                    if (selected.Contains(candidate.Id) ||
                        !candidate.AvailableOn(date) ||
                        IsOnLeave(leaveRanges, candidate.Id, date))
                    {
                        summary.UnavailableSkippedCount++;
                        continue;
                    }
                    chosen = (candidate.Id, candidate.Name);
                    break;
                }

                if (chosen == null)
                {
                    LogWarn($"组 {group} 在 {date:yyyy-MM-dd} 全员冲突，班次缺位");
                    continue;
                }

                selected.Add(chosen.Value.Id);
                pendingSchedules.Add((date, dateType, group, chosen.Value.Id, chosen.Value.Name));
                // 同步推进内存游标
                rotationMap[(group, dateType)] = chosen.Value.Id;
                rotationUpdates[(group, dateType)] = chosen.Value.Id;
            }
        }

        if (backfillFallbackUsed)
        {
            summary.Warnings.Add(
                $"{year}-{month:D2} 早于已有班次（前序月份缺失），部分轮转线无接续依据，已按名单头开始；建议按时间顺序生成月份");
        }

        if (pendingSchedules.Count == 0)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_NO_MEMBERS, "未生成任何班次，请检查各组成员配置");

        var replay = new ShiftReplayOutcome();
        // force 重生成前快照的本月原有轮转事实（事务外用于判定结果是否变化）
        Dictionary<(DateTime Date, string Group), long>? previousRotation = null;

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            if (force)
            {
                var oldResult = await _dbService.QueryAsync<DutySchedule>(@"
                    SELECT duty_date, group_code, COALESCE(rotation_member_id, member_id) AS rotation_member_id
                    FROM nc_duty_schedules
                    WHERE year = $1 AND month = $2
                      AND (rotation_member_id IS NOT NULL OR member_id IS NOT NULL)", ct, year, month);
                if (oldResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<DutyGenerationSummary>(oldResult.ErrorCode!, oldResult.Message!);
                }
                previousRotation = (oldResult.Value ?? new List<DutySchedule>())
                    .Where(s => s.RotationMemberId.HasValue)
                    .GroupBy(s => (s.DutyDate.Date, s.GroupCode))
                    .ToDictionary(g => g.Key, g => g.First().RotationMemberId!.Value);

                var deleteResult = await _dbService.ExecuteNonQueryAsync(
                    "DELETE FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, year, month);
                if (deleteResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<DutyGenerationSummary>(deleteResult.ErrorCode!, deleteResult.Message!);
                }
            }

            // 多行 VALUES 批写；rotation_member_id = 轮转消耗事实，串班/代班等显示调整不覆盖该列
            var (scheduleValues, scheduleArgs) = MultiRowValuesBuilder.Build(
                pendingSchedules.Count, 7,
                i =>
                {
                    var s = pendingSchedules[i];
                    return new object?[] { s.Date, s.DateType, s.Group, s.MemberId, s.MemberName, year, month };
                },
                o => $"(${o}, ${o + 1}, ${o + 2}, ${o + 3}, ${o + 3}, ${o + 4}, ${o + 5}, ${o + 6})");
            var insertResult = await _dbService.ExecuteNonQueryAsync(@"
                INSERT INTO nc_duty_schedules (duty_date, date_type, group_code, member_id, rotation_member_id, member_name, year, month)
                VALUES " + scheduleValues + @"
                ON CONFLICT (duty_date, group_code) DO NOTHING", ct, scheduleArgs);
            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<DutyGenerationSummary>(insertResult.ErrorCode!, insertResult.Message!);
            }

            // 游标批量 upsert（锚点月冻结游标：起点恒定取锚点校准值，保证重生成结果确定）
            if (!isAnchorMonth && rotationUpdates.Count > 0)
            {
                var rotationList = rotationUpdates.ToList();
                var (rotationValues, rotationArgs) = MultiRowValuesBuilder.Build(
                    rotationList.Count, 3,
                    i => new object?[] { rotationList[i].Key.Group, rotationList[i].Key.DateType, rotationList[i].Value },
                    o => $"(${o}, ${o + 1}, ${o + 2}, NOW())");
                var upsertResult = await _dbService.ExecuteNonQueryAsync(@"
                    INSERT INTO nc_duty_rotation_state (group_code, date_type, last_member_id, updated_at)
                    VALUES " + rotationValues + @"
                    ON CONFLICT (group_code, date_type)
                    DO UPDATE SET last_member_id = EXCLUDED.last_member_id, updated_at = NOW()",
                    ct, rotationArgs);
                if (upsertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<DutyGenerationSummary>(upsertResult.ErrorCode!, upsertResult.Message!);
                }
            }

            // 重放未撤销的串班/代班调整（force 重生成不丢手工调整）
            replay = await ApplyShiftChangesAsync(year, month, ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "生成月度值班表");
            return Result.FromException<DutyGenerationSummary>(ex);
        }

        // 本月轮转结果是否变化（null=本月原本无数据；false=与原班次完全一致 → 无需提示下游重排）
        if (previousRotation is { Count: > 0 })
        {
            var currentRotation = pendingSchedules
                .GroupBy(s => (s.Date.Date, s.Group))
                .ToDictionary(g => g.Key, g => g.First().MemberId);
            summary.RotationChanged = !(previousRotation.Count == currentRotation.Count
                && previousRotation.All(kv => currentRotation.TryGetValue(kv.Key, out var v) && v == kv.Value));
        }

        // 下游已生成月份（供"一键重排"）
        var downstreamResult = await LoadDownstreamGeneratedMonthsAsync(year, month, ct);
        if (downstreamResult.IsFailure)
            LogWarn($"查询下游已生成月份失败，一键重排可能漏排后续月份: {downstreamResult.ErrorCode} {downstreamResult.Message}");
        else
            summary.DownstreamMonths.AddRange(downstreamResult.Value);

        summary.ScheduleCount = pendingSchedules.Count;
        replay.MissingMonths.Sort(StringComparer.Ordinal);
        summary.ReplayedChangeCount = replay.AppliedCount;
        summary.PendingChangeCount = replay.PendingCount;
        summary.PendingChangeMonths = replay.MissingMonths;
        summary.SkippedChangeCount = replay.SkippedCount;

        Logger.LogBusiness("生成月度值班表",
            ("Year", year),
            ("Month", month),
            ("ScheduleCount", summary.ScheduleCount),
            ("Workday", summary.WorkdayCount),
            ("Restday", summary.RestdayCount),
            ("Holiday", summary.HolidayCount),
            ("ConflictSkipped", summary.ConflictSkippedCount),
            ("SameLeaderPair", sameLeaderPair),
            ("ChangeApplied", replay.AppliedCount),
            ("ChangePending", replay.PendingCount),
            ("ChangeSkipped", replay.SkippedCount));

        return Result.Success(summary);
    }

    public async Task<Result<int>> GenerateMissingMonthsAsync(
        IReadOnlyList<string> months, CancellationToken ct = default)
    {
        if (months is null || months.Count == 0)
            return Result.Success(0);

        var parsed = ParseMonthList(months);
        if (!parsed.IsSuccess)
            return Result.Failure<int>(parsed.ErrorCode!, parsed.Message!);

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<int>(ensureResult.ErrorCode!, ensureResult.Message!);

        var from = new DateTime(parsed.Value[0].Year, parsed.Value[0].Month, 1);
        var to = new DateTime(parsed.Value[^1].Year, parsed.Value[^1].Month, 1);
        var generatedCount = 0;
        for (var m = from; m <= to; m = m.AddMonths(1))
        {
            var countResult = await _dbService.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, m.Year, m.Month);
            if (countResult.IsFailure)
                return Result.Failure<int>(countResult.ErrorCode!, countResult.Message!);
            if (countResult.Value > 0)
                continue;   // 已生成月跳过

            var genResult = await GenerateMonthAsync(m.Year, m.Month, force: false, ct);
            if (genResult.IsFailure)
                return Result.Failure<int>(genResult.ErrorCode!,
                    $"补齐生成 {m:yyyy-MM} 失败（已生成 {generatedCount} 个月）：{genResult.Message}");

            generatedCount++;
            LogInfo($"跨月串班/代班补齐：{m:yyyy-MM} 值班表已生成");
        }
        return Result.Success(generatedCount);
    }

    public async Task<Result<int>> RegenerateMonthsAsync(
        IReadOnlyList<string> months, CancellationToken ct = default)
    {
        if (months is null || months.Count == 0)
            return Result.Success(0);

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<int>(ensureResult.ErrorCode!, ensureResult.Message!);

        // 解析、去重并按时间升序（必须升序：每月起点取自前一月最近班次，乱序会断链）
        var monthSet = new SortedSet<(int Year, int Month)>();
        foreach (var raw in months)
        {
            var parts = (raw ?? string.Empty).Split('-');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var y) || y < 2000 || y > 2100
                || !int.TryParse(parts[1], out var m) || m < 1 || m > 12)
            {
                return Result.Failure<int>(ErrorCodes.VALIDATION_FAILED, $"月份格式无效：{raw}");
            }
            monthSet.Add((y, m));
        }

        var regeneratedCount = 0;
        foreach (var (y, m) in monthSet)
        {
            var countResult = await _dbService.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, y, m);
            if (countResult.IsFailure)
                return Result.Failure<int>(countResult.ErrorCode!, countResult.Message!);
            if (countResult.Value == 0)
                continue;   // 未生成月跳过

            var genResult = await GenerateMonthAsync(y, m, force: true, ct);
            if (genResult.IsFailure)
                return Result.Failure<int>(genResult.ErrorCode!,
                    $"重排 {y}-{m:D2} 失败（已重排 {regeneratedCount} 个月）：{genResult.Message}");

            regeneratedCount++;
            LogInfo($"下游一键重排：{y}-{m:D2} 值班表已强制重生成");
        }
        return Result.Success(regeneratedCount);
    }

    public async Task<Result<int>> RebuildDownstreamAsync(int year, int month, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure<int>(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var monthsResult = await LoadDownstreamGeneratedMonthsAsync(year, month, ct);
        if (monthsResult.IsFailure)
            return Result.Failure<int>(monthsResult.ErrorCode!, monthsResult.Message!);
        return await RegenerateMonthsAsync(monthsResult.Value, ct);
    }

    public async Task<Result> ClearMonthAsync(int year, int month, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync(
            "DELETE FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, year, month);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("清空月度值班表", ("Year", year), ("Month", month));
            return Result.Success();
        }
        return result;
    }

    /// <summary>解析 yyyy-MM 列表为去重升序元组；格式非法返回 VALIDATION_FAILED。</summary>
    private static Result<List<(int Year, int Month)>> ParseMonthList(IReadOnlyList<string> months)
    {
        var parsed = new List<(int Year, int Month)>();
        foreach (var raw in months)
        {
            var parts = (raw ?? string.Empty).Split('-');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var y) || y < 2000 || y > 2100
                || !int.TryParse(parts[1], out var m) || m < 1 || m > 12)
            {
                return Result.Failure<List<(int, int)>>(ErrorCodes.VALIDATION_FAILED, $"月份格式无效：{raw}");
            }
            if (!parsed.Contains((y, m)))
                parsed.Add((y, m));
        }
        parsed.Sort();
        return Result.Success(parsed);
    }

    /// <summary>
    /// 读取锚点月某轮转线的校准起点（key=duty_anchor_start_{组}_{类型}）；
    /// 值缺失或解析失败返回 null。锚点月起点恒定取此值 → 重生成结果确定。
    /// </summary>
    private async Task<long?> GetAnchorStartMemberAsync(string groupCode, string dateType, CancellationToken ct)
    {
        var key = DutyConstants.SettingKeys.AnchorStartKey(groupCode, dateType);
        var setting = await GetSettingValueAsync(key, ct);
        if (!setting.IsSuccess || string.IsNullOrWhiteSpace(setting.Value))
            return null;
        return long.TryParse(setting.Value, out var id) ? id : null;
    }

    /// <summary>
    /// 查找指定周六的带班领导：优先取本次生成事务内已推演的班次，
    /// 否则查库中已固化班次（覆盖跨月周末对：上月已生成、本月初的周日）。
    /// </summary>
    private async Task<(long Id, string Name)?> TryGetSaturdayLeaderAsync(
        DateTime saturday,
        List<(DateTime Date, string DateType, string Group, long MemberId, string MemberName)> pendingSchedules,
        CancellationToken ct)
    {
        var pending = pendingSchedules.FirstOrDefault(s =>
            s.Date == saturday && s.Group == DutyConstants.Groups.LEADER);
        if (pending.MemberId != 0)
            return (pending.MemberId, pending.MemberName);

        var result = await _dbService.QueryAsync<DutySchedule>(@"
            SELECT member_id, member_name
            FROM nc_duty_schedules
            WHERE duty_date = $1 AND group_code = $2", ct, saturday, DutyConstants.Groups.LEADER);
        var schedule = result.Value?.FirstOrDefault();
        return schedule == null ? null : (schedule.MemberId ?? 0, schedule.MemberName);
    }

    #endregion

    #region 加载轮转队列

    /// <summary>组编码 → 启用成员队列（按组内 sort_order, id 排序；入职/离职时间用于逐日可用性判定）</summary>
    private async Task<Result<Dictionary<string, List<DutyQueueMember>>>> LoadActiveQueuesAsync(CancellationToken ct)
    {
        var result = await _dbService.QueryAsync<DutyQueueRow>(@"
            SELECT g.group_code, m.id, m.name, m.join_date, m.exit_date,
                   EXISTS (SELECT 1 FROM nc_duty_member_groups g2
                           WHERE g2.member_id = m.id AND g2.group_code = 'MIDDLE') AS is_middle
            FROM nc_duty_member_groups g
            JOIN nc_duty_members m ON m.id = g.member_id
            WHERE m.is_active
            ORDER BY g.group_code, g.sort_order, m.id", ct);
        if (!result.IsSuccess)
            return Result.Failure<Dictionary<string, List<DutyQueueMember>>>(result.ErrorCode!, result.Message!);

        var queues = new Dictionary<string, List<DutyQueueMember>>();
        foreach (var row in result.Value ?? new List<DutyQueueRow>())
        {
            if (!queues.TryGetValue(row.GroupCode, out var list))
            {
                list = new List<DutyQueueMember>();
                queues[row.GroupCode] = list;
            }
            list.Add(new DutyQueueMember(row.Id, row.Name, row.JoinDate, row.ExitDate, row.IsMiddle));
        }
        return Result.Success(queues);
    }

    /// <summary>轮转队列成员（含入职/离职时间、是否中层干部，供逐日可用性判定与节假日排除）</summary>
    private sealed record DutyQueueMember(long Id, string Name, DateTime? JoinDate, DateTime? ExitDate, bool IsMiddle)
    {
        /// <summary>值班日当天是否可参与：已入职且未离职（按日精确判定）</summary>
        public bool AvailableOn(DateTime date)
        {
            if (JoinDate.HasValue && JoinDate.Value > date) return false;
            if (ExitDate.HasValue && ExitDate.Value < date) return false;
            return true;
        }
    }

    private sealed class DutyQueueRow
    {
        public string GroupCode { get; set; } = string.Empty;
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? JoinDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public bool IsMiddle { get; set; }
    }

    #endregion
}
