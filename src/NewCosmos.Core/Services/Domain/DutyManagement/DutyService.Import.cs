using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Import;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace NewCosmos.Services.Domain.DutyManagement;

/// <summary>值班管理：成员批量导入（队列名单模式：每组一列，从上到下即轮转顺序）。</summary>
public partial class DutyService
{
    /// <summary>组列头 → 组编码（列头即 DutyConstants.Groups.DisplayName）</summary>
    private static readonly Dictionary<string, string> MemberGroupColumns = new()
    {
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.LEADER)] = DutyConstants.Groups.LEADER,
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.MIDDLE)] = DutyConstants.Groups.MIDDLE,
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.MALE)] = DutyConstants.Groups.MALE,
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.FEMALE)] = DutyConstants.Groups.FEMALE,
    };

    /// <summary>轮转设置页的线列头 → 日期类型（列头文本 → DateTypes 常量）</summary>
    private static readonly Dictionary<string, string> DateTypeColumnHeaders = new()
    {
        [DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.WORKDAY)] = DutyConstants.DateTypes.WORKDAY,
        [DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.RESTDAY)] = DutyConstants.DateTypes.RESTDAY,
        [DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.HOLIDAY)] = DutyConstants.DateTypes.HOLIDAY,
    };

    #region 解析名单

    /// <summary>
    /// 解析名单文件（预览/执行共用）：Sheet1 第 1 行为「序号 + 组名」交替配对的 8 列布局。
    /// 序号 = 该成员在该组的轮转顺序（sort_order，按值升序；允许跳号，组内不得重复、必填正整数）。
    /// </summary>
    private Result<(List<DutyGroupRoster> Rosters, List<string> Errors)> ParseMemberRosterSheet(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(ErrorCodes.FILE_NOT_FOUND, "导入文件不存在");
        if (!SheetReaderFactory.IsExcelFile(filePath))
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(ErrorCodes.FILE_FORMAT_ERROR, "仅支持 .xlsx / .xls 文件");

        IExcelSheetReader reader;
        try
        {
            reader = SheetReaderFactory.Create(filePath);
        }
        catch (Exception ex)
        {
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(
                ErrorCodes.FILE_FORMAT_ERROR, $"导入文件解析失败：{ex.Message}");
        }
        using var readerScope = reader;
        if (reader.IsEmpty)
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(ErrorCodes.FILE_FORMAT_ERROR, "导入文件内容为空");

        var rosters = new List<DutyGroupRoster>();
        var errors = new List<string>();
        var recognizedColumns = 0;

        // 列头配对：识别「序号」列 + 紧随的组名列
        var pairings = new List<(int SortCol, int NameCol, string GroupCode, string GroupName)>();
        int? pendingSortCol = null;
        for (var col = 1; col <= reader.ColumnCount; col++)
        {
            var header = reader.GetCellText(1, col).Trim();
            if (string.IsNullOrWhiteSpace(header)) continue;

            if (header == "序号")
            {
                if (pendingSortCol.HasValue)
                    errors.Add("模板布局错误：连续出现两个「序号」列（缺少配对的组名列）");
                pendingSortCol = col;
                continue;
            }

            if (MemberGroupColumns.TryGetValue(header, out var groupCode))
            {
                if (!pendingSortCol.HasValue)
                {
                    errors.Add($"模板布局错误：组列「{header}」缺少配对的「序号」列，请使用「下载导入模板」生成的标准模板");
                }
                else
                {
                    pairings.Add((pendingSortCol.Value, col, groupCode, header));
                    pendingSortCol = null;
                    recognizedColumns++;
                }
                continue;
            }

            errors.Add($"无法识别的列「{header}」（模板仅支持「序号 + 组名」配对布局，请使用标准模板）");
        }

        if (recognizedColumns == 0)
        {
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(
                ErrorCodes.VALIDATION_FAILED, "未识别到任何组名单列（表头须为：序号+领导组 / 序号+中层管理组 / 序号+男生组 / 序号+女生组）");
        }

        foreach (var (sortCol, nameCol, groupCode, groupName) in pairings)
        {
            var roster = new DutyGroupRoster { GroupCode = groupCode };
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var seenSeqs = new HashSet<int>();

            for (var row = 2; row <= reader.RowCount; row++)
            {
                var seqText = reader.GetCellText(row, sortCol).Trim();
                var name = reader.GetCellText(row, nameCol).Trim();

                if (string.IsNullOrWhiteSpace(seqText) && string.IsNullOrWhiteSpace(name))
                    continue;   // 行级空档：跳过继续

                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add($"「{groupName}」第 {row} 行：姓名为空（序号「{seqText}」无对应姓名）");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(seqText))
                {
                    errors.Add($"「{groupName}」第 {row} 行：序号为空（姓名「{name}」缺少序号）");
                    continue;
                }
                if (!int.TryParse(seqText, out var seq) || seq <= 0)
                {
                    errors.Add($"「{groupName}」第 {row} 行：序号无效「{seqText}」（须为正整数）");
                    continue;
                }
                if (name.Length > 50)
                {
                    errors.Add($"「{groupName}」第 {row} 行姓名超长（{name.Length} 字，上限 50）");
                    continue;
                }

                if (!seenNames.Add(name))
                {
                    errors.Add($"「{groupName}」第 {row} 行姓名「{name}」在该列内重复");
                    continue;
                }
                if (!seenSeqs.Add(seq))
                {
                    errors.Add($"「{groupName}」第 {row} 行序号 {seq} 在该列内重复（姓名「{name}」）");
                    continue;
                }

                roster.Entries.Add(new DutyRosterEntry { Seq = seq, Name = name });
            }

            rosters.Add(roster);
        }

        return Result.Success((rosters, errors));
    }

    /// <summary>
    /// 解析「轮转设置」工作表（Sheet2，可缺省）：起始日期（全局）+ 每组三条轮转线分别的当前序列。
    /// 每条线留空 = 该线从名单头开始；当前序列成员须在该组名单内（校验在调用方）。
    /// </summary>
    private Result<(DateTime? AnchorDate, Dictionary<(string Group, string DateType), string> CurrentSeqByLine)>
        ParseRotationSettings(string filePath)
    {
        try
        {
            ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
            using var package = new ExcelPackage(new FileInfo(filePath));
            if (package.Workbook.Worksheets.Count < 2)
            {
                return Result.Success<(DateTime?, Dictionary<(string, string), string>)>(
                    (null, new Dictionary<(string, string), string>()));   // 无设置页：纯名单导入
            }

            var sheet = package.Workbook.Worksheets[1];
            var currentSeqByLine = new Dictionary<(string Group, string DateType), string>();
            DateTime? anchorDate = null;
            var lineColumns = new Dictionary<int, string>();   // 列号 → 日期类型

            for (var row = 1; row <= sheet.Dimension?.End.Row; row++)
            {
                var key = sheet.Cells[row, 1].Text?.Trim() ?? string.Empty;

                if (key == "起始日期")
                {
                    var value = sheet.Cells[row, 2].Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(value) && !DateTime.TryParse(value, out _))
                    {
                        return Result.Failure<(DateTime?, Dictionary<(string, string), string>)>(
                            ErrorCodes.VALIDATION_FAILED, $"轮转设置：起始日期无效「{value}」（格式：yyyy-MM-dd）");
                    }
                    if (!string.IsNullOrWhiteSpace(value))
                        anchorDate = DateTime.Parse(value).Date;
                    continue;
                }

                if (key == "组名")
                {
                    lineColumns.Clear();
                    for (var col = 2; col <= sheet.Dimension.End.Column; col++)
                    {
                        var header = sheet.Cells[row, col].Text?.Trim() ?? string.Empty;
                        var dateType = DateTypeColumnHeaders.FirstOrDefault(kv => kv.Value == header).Key;
                        if (dateType != null)
                            lineColumns[col] = dateType;
                    }
                    continue;
                }

                if (MemberGroupColumns.TryGetValue(key, out var groupCode))
                {
                    foreach (var (col, dateType) in lineColumns)
                    {
                        var value = sheet.Cells[row, col].Text?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(value))
                            currentSeqByLine[(groupCode, dateType)] = value;
                    }
                }
                // 其他行（说明等）忽略
            }

            return Result.Success((anchorDate, currentSeqByLine));
        }
        catch (Exception ex)
        {
            return Result.Failure<(DateTime?, Dictionary<(string, string), string>)>(
                ErrorCodes.FILE_FORMAT_ERROR, $"轮转设置解析失败：{ex.Message}");
        }
    }

    #endregion

    #region 预览与执行

    public async Task<Result<DutyMemberImportPreview>> PreviewMemberImportAsync(string filePath, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyMemberImportPreview>(ensureResult.ErrorCode!, ensureResult.Message!);

        var parseResult = ParseMemberRosterSheet(filePath);
        if (!parseResult.IsSuccess)
            return Result.Failure<DutyMemberImportPreview>(parseResult.ErrorCode!, parseResult.Message!);

        var (rosters, parseErrors) = parseResult.Value;

        // 与库内现状做差异汇总（新增/对齐/移出）
        var membersResult = await GetMembersAsync(ct);
        if (!membersResult.IsSuccess)
            return Result.Failure<DutyMemberImportPreview>(membersResult.ErrorCode!, membersResult.Message!);
        var existingMembers = membersResult.Value ?? new List<DutyMemberView>();
        var existingByName = existingMembers
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var preview = new DutyMemberImportPreview();
        var excelMembers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var roster in rosters)
        {
            preview.Rosters.Add(roster);
            foreach (var entry in roster.Entries)
            {
                excelMembers.Add(entry.Name);
                if (!existingByName.ContainsKey(entry.Name)) preview.NewCount++;
                else preview.UpdateCount++;
            }

            if (roster.Entries.Count == 0)
                preview.ClearedGroupNames.Add(roster.GroupName);
        }

        // 库内在组但名单未列的 (成员, 组) 计数
        foreach (var member in existingMembers)
        {
            foreach (var g in member.Groups)
            {
                var roster = rosters.FirstOrDefault(r => r.GroupCode == g.GroupCode);
                if (roster == null || !roster.Entries.Any(e => e.Name == member.Name))
                    preview.RemoveCount++;
            }
        }

        preview.TotalMembers = excelMembers.Count;
        preview.Errors.AddRange(parseErrors);

        // 轮转设置（起始日期 + 各线当前序列）
        var settingsResult = ParseRotationSettings(filePath);
        if (!settingsResult.IsSuccess)
            return Result.Failure<DutyMemberImportPreview>(settingsResult.ErrorCode!, settingsResult.Message!);
        var (anchorDate, currentSeqByLine) = settingsResult.Value;
        preview.AnchorDate = anchorDate;
        foreach (var (key, memberName) in currentSeqByLine)
        {
            if (!preview.CurrentSeqByLine.TryGetValue(key.Group, out var lines))
            {
                lines = new Dictionary<string, string>();
                preview.CurrentSeqByLine[key.Group] = lines;
            }
            lines[key.DateType] = memberName;

            var roster = rosters.FirstOrDefault(r => r.GroupCode == key.Group);
            if (roster == null || !roster.Entries.Any(e => e.Name == memberName))
            {
                preview.Errors.Add($"轮转设置：{DutyConstants.Groups.DisplayName(key.Group)} 的{DutyConstants.DateTypes.DisplayName(key.DateType)}当前序列「{memberName}」不在该组名单内");
            }
        }

        LogInfo($"值班成员导入预览: {preview.SummaryText}，校验错误 {preview.Errors.Count} 条");
        return Result.Success(preview);
    }

    public async Task<Result<DutyMemberImportResult>> ImportMembersAsync(string filePath, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyMemberImportResult>(ensureResult.ErrorCode!, ensureResult.Message!);

        var parseResult = ParseMemberRosterSheet(filePath);
        if (!parseResult.IsSuccess)
            return Result.Failure<DutyMemberImportResult>(parseResult.ErrorCode!, parseResult.Message!);
        var (rosters, parseErrors) = parseResult.Value;

        if (rosters.All(r => r.Entries.Count == 0))
            return Result.Failure<DutyMemberImportResult>(ErrorCodes.VALIDATION_FAILED, "名单为空：四个组的名单列均无成员");

        // 对齐语义下名单必须无歧义：存在解析错误（序号/姓名/重复等）时整体拒绝
        if (parseErrors.Count > 0)
        {
            return Result.Failure<DutyMemberImportResult>(ErrorCodes.VALIDATION_FAILED,
                $"导入文件存在 {parseErrors.Count} 条校验错误，请修正后重试：{parseErrors.First()}");
        }

        // 轮转设置（锚点 + 各线当前序列）：当前序列成员必须在该组名单内
        var settingsResult = ParseRotationSettings(filePath);
        if (!settingsResult.IsSuccess)
            return Result.Failure<DutyMemberImportResult>(settingsResult.ErrorCode!, settingsResult.Message!);
        var (anchorDate, currentSeqByLine) = settingsResult.Value;
        foreach (var (key, memberName) in currentSeqByLine)
        {
            var roster = rosters.FirstOrDefault(r => r.GroupCode == key.Group);
            if (roster == null || !roster.Entries.Any(e => e.Name == memberName))
            {
                return Result.Failure<DutyMemberImportResult>(ErrorCodes.VALIDATION_FAILED,
                    $"轮转设置：{DutyConstants.Groups.DisplayName(key.Group)} 的{DutyConstants.DateTypes.DisplayName(key.DateType)}当前序列「{memberName}」不在该组名单内");
            }
        }

        // 名单 → 成员视角：name → (组 → 显式序号)
        var excelMemberGroups = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var roster in rosters)
        {
            foreach (var entry in roster.Entries)
            {
                if (!excelMemberGroups.TryGetValue(entry.Name, out var groups))
                {
                    groups = new Dictionary<string, int>();
                    excelMemberGroups[entry.Name] = groups;
                }
                if (!groups.ContainsKey(roster.GroupCode))
                    groups[roster.GroupCode] = entry.Seq;
            }
        }

        // 库内现状
        var membersResult = await GetMembersAsync(ct);
        if (!membersResult.IsSuccess)
            return Result.Failure<DutyMemberImportResult>(membersResult.ErrorCode!, membersResult.Message!);
        var existingMembers = membersResult.Value ?? new List<DutyMemberView>();
        var existingByName = existingMembers
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var result = new DutyMemberImportResult();

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            // 1. 名单成员：新增 / 对齐归属与位次
            foreach (var (name, targetGroups) in excelMemberGroups)
            {
                var existing = existingByName.TryGetValue(name, out var found) ? found : null;

                if (existing == null)
                {
                    var insertResult = await _dbService.ExecuteScalarAsync<long>(@"
                        INSERT INTO nc_duty_members (name, is_active, sort_order, created_at, updated_at)
                        VALUES ($1, TRUE, COALESCE((SELECT MAX(sort_order) + 1 FROM nc_duty_members), 0), NOW(), NOW())
                        RETURNING id", ct, name);
                    insertResult.ThrowIfFailure("新增成员");
                    var memberId = insertResult.Value;

                    foreach (var (group, seq) in targetGroups)
                    {
                        var g = await _dbService.ExecuteNonQueryAsync(
                            "INSERT INTO nc_duty_member_groups (member_id, group_code, sort_order) VALUES ($1, $2, $3)",
                            ct, memberId, group, seq);
                        g.ThrowIfFailure("写入组归属");
                    }
                    result.NewCount++;
                    continue;
                }

                // 对齐：目标组集 vs 现有组集
                var existingGroupCodes = existing.Groups.Select(g => g.GroupCode).ToHashSet();
                var targetGroupCodes = targetGroups.Keys.ToHashSet();

                // 移除：现有但不在名单中的组（全量对齐语义）
                foreach (var removedGroup in existingGroupCodes.Where(c => !targetGroupCodes.Contains(c)))
                {
                    var d = await _dbService.ExecuteNonQueryAsync(
                        "DELETE FROM nc_duty_member_groups WHERE member_id = $1 AND group_code = $2",
                        ct, existing.Id, removedGroup);
                    d.ThrowIfFailure("移除组归属");
                    result.RemoveCount++;
                }

                // 新增：名单中有但成员尚不属于的组
                foreach (var (group, seq) in targetGroups.Where(kv => !existingGroupCodes.Contains(kv.Key)))
                {
                    var g = await _dbService.ExecuteNonQueryAsync(
                        "INSERT INTO nc_duty_member_groups (member_id, group_code, sort_order) VALUES ($1, $2, $3)",
                        ct, existing.Id, group, seq);
                    g.ThrowIfFailure("写入组归属");
                }

                // 位次修正：已在目标组但位次变化（仅差异才写库，重复导入幂等）
                foreach (var (group, seq) in targetGroups.Where(kv => existingGroupCodes.Contains(kv.Key)))
                {
                    var currentSeq = existing.Groups.First(g => g.GroupCode == group).SortOrder;
                    if (currentSeq != seq)
                    {
                        var u = await _dbService.ExecuteNonQueryAsync(
                            "UPDATE nc_duty_member_groups SET sort_order = $3 WHERE member_id = $1 AND group_code = $2",
                            ct, existing.Id, group, seq);
                        u.ThrowIfFailure("更新组内顺序");
                    }
                }

                result.UpdateCount++;
            }

            // 2. 库内成员不在名单中 → 从其所有组移除（只摘组不删人）
            var removePairs = new List<(long MemberId, string GroupCode)>();
            foreach (var member in existingMembers)
            {
                if (excelMemberGroups.ContainsKey(member.Name)) continue;
                foreach (var g in member.Groups)
                    removePairs.Add((member.Id, g.GroupCode));
            }

            if (removePairs.Count > 0)
            {
                var removeMemberIds = removePairs.Select(p => p.MemberId).ToArray();
                var removeGroupCodes = removePairs.Select(p => p.GroupCode).ToArray();
                var d = await _dbService.ExecuteNonQueryAsync(
                    @"DELETE FROM nc_duty_member_groups
                      WHERE (member_id, group_code) IN (SELECT * FROM unnest($1::bigint[], $2::text[]))",
                    ct, removeMemberIds, removeGroupCodes);
                d.ThrowIfFailure("移除组归属");
                result.RemoveCount += removePairs.Count;
            }

            // 3. 轮转锚点：有起始日期时，按轮转线分别设置游标为「当前序列的前一位」
            if (anchorDate.HasValue)
            {
                foreach (var group in DutyConstants.Groups.DisplayOrder)
                {
                    var roster = rosters.FirstOrDefault(r => r.GroupCode == group);
                    var ordered = roster?.Entries.OrderBy(e => e.Seq).ToList() ?? new List<DutyRosterEntry>();

                    foreach (var dateType in new[] { DutyConstants.DateTypes.WORKDAY, DutyConstants.DateTypes.RESTDAY, DutyConstants.DateTypes.HOLIDAY })
                    {
                        long? lastMemberId = null;
                        var lineKey = (group, dateType);
                        if (currentSeqByLine.TryGetValue(lineKey, out var currentName) && roster != null)
                        {
                            var idx = ordered.FindIndex(e => e.Name == currentName);
                            if (idx > 0)
                            {
                                var prev = existingByName.TryGetValue(ordered[idx - 1].Name, out var prevFound)
                                    ? prevFound
                                    : null;
                                if (prev == null)
                                {
                                    // 前一位是本次新增成员：取其新 ID
                                    var idResult = await _dbService.ExecuteScalarAsync<long>(
                                        "SELECT id FROM nc_duty_members WHERE name = $1 LIMIT 1", ct, ordered[idx - 1].Name);
                                    idResult.ThrowIfFailure("定位当前序列前一位");
                                    lastMemberId = idResult.Value;
                                }
                                else
                                {
                                    lastMemberId = prev.Id;
                                }
                            }
                            // idx == 0 → lastMemberId 保持 NULL（从名单头开始）
                        }

                        var upsertResult = await _dbService.ExecuteNonQueryAsync(@"
                            INSERT INTO nc_duty_rotation_state (group_code, date_type, last_member_id, updated_at)
                            VALUES ($1, $2, $3, NOW())
                            ON CONFLICT (group_code, date_type)
                            DO UPDATE SET last_member_id = EXCLUDED.last_member_id, updated_at = NOW()",
                            ct, group, dateType, lastMemberId);
                        upsertResult.ThrowIfFailure("设置轮转游标");
                    }
                }

                // 锚点日期持久化（生成该月值班表时自动从此日起）
                var anchorResult = await _dbService.ExecuteNonQueryAsync(@"
                    INSERT INTO nc_duty_settings (setting_key, setting_value, remark, updated_at)
                    VALUES ($1, $2, NOW(), NOW())
                    ON CONFLICT (setting_key)
                    DO UPDATE SET setting_value = EXCLUDED.setting_value, updated_at = NOW()",
                    ct, DutyConstants.SettingKeys.IMPORT_ANCHOR_DATE, anchorDate.Value.ToString("yyyy-MM-dd"));
                anchorResult.ThrowIfFailure("写入轮转锚点");

                // 锚点月校准起点固化（12 条线：起点=当前序列的前一位；锚点月重生成恒定从此起，结果确定）
                foreach (var group in DutyConstants.Groups.DisplayOrder)
                {
                    foreach (var dateType in new[] { DutyConstants.DateTypes.WORKDAY, DutyConstants.DateTypes.RESTDAY, DutyConstants.DateTypes.HOLIDAY })
                    {
                        long? anchorLast = null;
                        var lineKey = (group, dateType);
                        if (currentSeqByLine.TryGetValue(lineKey, out var currentName))
                        {
                            var roster = rosters.FirstOrDefault(r => r.GroupCode == group);
                            var ordered = roster?.Entries.OrderBy(e => e.Seq).ToList() ?? new List<DutyRosterEntry>();
                            var idx = ordered.FindIndex(e => e.Name == currentName);
                            if (idx > 0)
                            {
                                var prevName = ordered[idx - 1].Name;
                                if (existingByName.TryGetValue(prevName, out var prevFound))
                                {
                                    anchorLast = prevFound.Id;
                                }
                                else
                                {
                                    var idResult = await _dbService.ExecuteScalarAsync<long>(
                                        "SELECT id FROM nc_duty_members WHERE name = $1 LIMIT 1", ct, prevName);
                                    idResult.ThrowIfFailure("定位锚点校准起点");
                                    anchorLast = idResult.Value;
                                }
                            }
                            // idx == 0 → NULL（从名单头开始）
                        }

                        var anchorStartKey = DutyConstants.SettingKeys.AnchorStartKey(group, dateType);
                        var anchorStartResult = await _dbService.ExecuteNonQueryAsync(@"
                            INSERT INTO nc_duty_settings (setting_key, setting_value, remark, updated_at)
                            VALUES ($1, $2, $3, NOW())
                            ON CONFLICT (setting_key)
                            DO UPDATE SET setting_value = EXCLUDED.setting_value, updated_at = NOW()",
                            ct, anchorStartKey, anchorLast?.ToString() ?? string.Empty,
                            $"锚点月校准起点：{group}/{dateType}");
                        anchorStartResult.ThrowIfFailure("写入锚点校准起点");
                    }
                }

                result.AnchorDate = anchorDate.Value;
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "批量导入值班成员名单");
            return Result.FromException<DutyMemberImportResult>(ex);
        }

        result.Success = true;
        result.Message = result.SummaryText;
        Logger.LogBusiness("批量导入值班成员名单",
            ("File", Path.GetFileName(filePath)),
            ("New", result.NewCount),
            ("Updated", result.UpdateCount),
            ("Removed", result.RemoveCount));
        return Result.Success(result);
    }

    #endregion

    #region 导入模板

    public async Task<Result<byte[]>> BuildMemberImportTemplateAsync(CancellationToken ct = default)
    {
        try
        {
            ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");

            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("数据");
            // 8 列布局：序号 | 组名 × 4（序号 = 该组轮转顺序，按值升序；组内唯一、允许跳号）
            for (var pair = 0; pair < 4; pair++)
            {
                var groupCode = DutyConstants.Groups.DisplayOrder[pair];
                var sortCell = sheet.Cells[1, pair * 2 + 1];
                sortCell.Value = "序号";
                sortCell.Style.Font.Bold = true;
                sortCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                var nameCell = sheet.Cells[1, pair * 2 + 2];
                nameCell.Value = DutyConstants.Groups.DisplayName(groupCode);
                nameCell.Style.Font.Bold = true;
                nameCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                sheet.Column(pair * 2 + 1).Width = 8;
                sheet.Column(pair * 2 + 2).Width = 16;
            }

            var settings = package.Workbook.Worksheets.Add("轮转设置");
            settings.Cells[1, 1].Value = "起始日期";
            settings.Cells[1, 1].Style.Font.Bold = true;
            settings.Cells[1, 2].Style.Numberformat.Format = "yyyy-mm-dd";

            settings.Cells[2, 1].Value = "说明：起始日期（选填）= 从该日起按名单轮转，导入后生成该月值班表将自动从此日起。";
            settings.Cells[2, 1].Style.WrapText = true;
            settings.Cells[3, 1].Value = "各线「当前序列」= 下个值班的人（须在该组名单内）。每组有三条独立轮转线（工作日/休息日/法定节假日），可分别指定；留空的线从名单头开始。中层管理组仅法定节假日线参与值班。";

            settings.Cells[5, 1].Value = "组名";
            settings.Cells[5, 2].Value = DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.WORKDAY) + "序列";
            settings.Cells[5, 3].Value = DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.RESTDAY) + "序列";
            settings.Cells[5, 4].Value = DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.HOLIDAY) + "序列";
            for (var col = 1; col <= 4; col++)
            {
                settings.Cells[5, col].Style.Font.Bold = true;
                settings.Cells[5, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            for (var i = 0; i < DutyConstants.Groups.DisplayOrder.Length; i++)
            {
                settings.Cells[6 + i, 1].Value = DutyConstants.Groups.DisplayName(DutyConstants.Groups.DisplayOrder[i]);
            }

            settings.Column(1).Width = 16;
            settings.Column(2).Width = 14;
            settings.Column(3).Width = 14;
            settings.Column(4).Width = 14;

            var guideHelp = package.Workbook.Worksheets.Add("填写说明");
            var guideLines = new[]
            {
                "值班成员名单批量导入模板 - 填写说明",
                "",
                "1. 「数据」工作表为 8 列：每组的「序号 + 姓名」成对填写。序号就是该成员在本组的值班轮转顺序",
                "   （按序号从小到大依次轮转，序号小的先值班），同组内序号不得重复，建议按 1、2、3… 连续填写；允许跳号（如 10、20、30）以便后续插队。",
                "2. 序号为必填项：只填姓名不填序号、或只填序号不填姓名的行会报错。",
                "3. 同一个人可以出现在多个组的名单里（如中层管理组成员同时属男生组），表示一人多组；各组的序号相互独立。",
                "4. 名单中间可以留空档：读取时跳过整行空档继续。",
                "5. 全量对齐：以本次 Excel 名单为准——名单里没有列到的库内成员将从其所在组移除（只摘组不删人）；",
                "   某组整列为空 = 该组名单将被清空，导入前会强提醒确认。",
                "6. 电话/入职离职时间/启用/备注等字段不在名单模板内，请导入后在成员列表中单条编辑维护。",
                "7. 本页为说明页，导入时不会读取；请仅在「数据」工作表填写名单。",
            };
            for (var i = 0; i < guideLines.Length; i++)
            {
                guideHelp.Cells[i + 1, 1].Value = guideLines[i];
                if (i == 0) guideHelp.Cells[i + 1, 1].Style.Font.Bold = true;
            }
            guideHelp.Column(1).Width = 110;

            var bytes = await package.GetAsByteArrayAsync(ct);
            LogInfo($"值班成员导入模板生成完成（{bytes.Length} 字节）");
            return Result.Success(bytes);
        }
        catch (Exception ex)
        {
            LogException(ex, "生成值班成员导入模板");
            return Result.FromException<byte[]>(ex);
        }
    }

    #endregion
}
