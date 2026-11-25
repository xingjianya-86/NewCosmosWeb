using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 家庭经济状况核对（资产核查）推送打印的数据装配。
/// <para>
/// checkId 是 <c>nc_biz_asset_checks.id</c>（某一条成员行）；按 batch_id 取同批次全部人员，
/// 依旧版 <c>QuickAssetVerificationViewModel.ProcessPrintResultAsync</c> 口径装配字段：
/// 户主/申请人、经办人（nc_biz_asset_check_operators）、代理人（nc_biz_asset_check_agents）、
/// 人数、报告/调查日期、区县乡镇村与通知编号。
/// </para>
/// <para>
/// 输出键一律使用 <see cref="FieldKeys"/> 常量（PC Agent 按模板 config 的 fieldKey 映射到占位符）；
/// 缺失数据直接跳过，不臆造、不报错。
/// </para>
/// </summary>
public sealed class AssetCheckPrintDataService(IDatabaseService database)
{
    private const int MaxBatchMembers = 200;

    public async Task<Result<AssetCheckPrintPayload>> BuildAsync(
        long checkId,
        CancellationToken ct = default)
    {
        if (checkId <= 0)
        {
            return Result.Failure<AssetCheckPrintPayload>(
                ErrorCodes.VALIDATION_FAILED, "核对记录 id 无效");
        }

        var requestedResult = await database.QuerySingleAsync<AssetCheckPrintRow>(
            @"SELECT v.id, v.batch_id, v.applicant_name, v.applicant_id_type, v.applicant_id_card,
                     v.relationship,
                     COALESCE((SELECT i.item_value FROM nc_dict_items i
                               WHERE i.category = 'FamilyRelationships'
                                 AND i.item_key = v.relationship AND i.is_active LIMIT 1),
                              NULLIF(v.relationship, '')) AS relationship_display,
                     COALESCE((SELECT i.item_value FROM nc_dict_items i
                               WHERE i.category = 'IdTypes'
                                 AND i.item_key = v.applicant_id_type AND i.is_active LIMIT 1),
                              NULLIF(v.applicant_id_type, '')) AS id_type_display,
                     v.is_head, v.head_id_card, v.family_address, v.community,
                     v.application_reason,
                     COALESCE((SELECT i.item_value FROM nc_dict_items i
                               WHERE i.category = 'ApplicationReasons'
                                 AND i.item_key = v.application_reason AND i.is_active LIMIT 1),
                              NULLIF(v.application_reason, '')) AS application_reason_display,
                     v.application_date, v.contact_phone, v.status
              FROM nc_biz_asset_checks v
              WHERE v.id = $1 AND v.deleted_at IS NULL
              LIMIT 1",
            ct, checkId);

        if (requestedResult.IsFailure)
        {
            return Result.Failure<AssetCheckPrintPayload>(
                requestedResult.ErrorCode!, requestedResult.Message!);
        }

        var requested = requestedResult.Value;
        if (requested is null)
        {
            return Result.Failure<AssetCheckPrintPayload>(
                ErrorCodes.RECORD_NOT_FOUND, "核对记录不存在");
        }

        var members = new List<AssetCheckPrintRow> { requested };
        if (!string.IsNullOrWhiteSpace(requested.BatchId))
        {
            var memberResult = await database.QueryAsync<AssetCheckPrintRow>(
                @"SELECT v.id, v.batch_id, v.applicant_name, v.applicant_id_type, v.applicant_id_card,
                         v.relationship,
                         COALESCE((SELECT i.item_value FROM nc_dict_items i
                                   WHERE i.category = 'FamilyRelationships'
                                     AND i.item_key = v.relationship AND i.is_active LIMIT 1),
                                  NULLIF(v.relationship, '')) AS relationship_display,
                         COALESCE((SELECT i.item_value FROM nc_dict_items i
                                   WHERE i.category = 'IdTypes'
                                     AND i.item_key = v.applicant_id_type AND i.is_active LIMIT 1),
                                  NULLIF(v.applicant_id_type, '')) AS id_type_display,
                         v.is_head, v.head_id_card, v.family_address, v.community,
                         v.application_reason,
                         COALESCE((SELECT i.item_value FROM nc_dict_items i
                                   WHERE i.category = 'ApplicationReasons'
                                     AND i.item_key = v.application_reason AND i.is_active LIMIT 1),
                                  NULLIF(v.application_reason, '')) AS application_reason_display,
                         v.application_date, v.contact_phone, v.status
                  FROM nc_biz_asset_checks v
                  WHERE v.batch_id = $1 AND v.deleted_at IS NULL
                  ORDER BY v.is_head DESC, v.id ASC
                  LIMIT " + MaxBatchMembers.ToString(CultureInfo.InvariantCulture),
                ct, requested.BatchId);

            if (memberResult.IsFailure)
            {
                return Result.Failure<AssetCheckPrintPayload>(
                    memberResult.ErrorCode!, memberResult.Message!);
            }

            if (memberResult.Value is { Count: > 0 } rows)
            {
                members = rows;
            }
        }

        // 户主行优先；缺失时退回当前打开的成员行（不臆造户主）。
        var head = members.FirstOrDefault(row => row.IsHead) ?? requested;
        var applicant = members.FirstOrDefault(row => row.Id == checkId) ?? head;

        var (agent, operatorRow) = await LoadBatchContextAsync(requested.BatchId, ct);
        var organization = operatorRow?.OperatorUserId is > 0
            ? await LoadOrganizationAsync(operatorRow.OperatorUserId!.Value, ct)
            : null;
        var region = await LoadRegionAsync(head.Community, ct);

        var payload = new AssetCheckPrintPayload();

        // ── 户主 / 申请人 ──
        Put(payload.Fields, FieldKeys.APPLICANT_NAME, head.ApplicantName);
        Put(payload.Fields, FieldKeys.APPLICANT_ID_CARD, head.ApplicantIdCard);
        Put(payload.Fields, FieldKeys.APPLICANT_ID_TYPE, head.IdTypeDisplay);
        Put(payload.Fields, FieldKeys.HEAD_ID_CARD, FirstNonEmpty(head.HeadIdCard, head.ApplicantIdCard));
        Put(payload.Fields, FieldKeys.FAMILY_ADDRESS, head.FamilyAddress);
        Put(payload.Fields, FieldKeys.FAMILY_DETAIL_ADDRESS,
            ResolveDetailAddress(head.FamilyAddress, organization?.CountyName, organization?.TownName, region));
        Put(payload.Fields, FieldKeys.APPLICATION_REASON,
            FirstNonEmpty(head.ApplicationReasonDisplay, head.ApplicationReason));
        Put(payload.Fields, FieldKeys.APPLICATION_DATE, FormatDate(head.ApplicationDate));
        Put(payload.Fields, FieldKeys.CONTACT_PHONE,
            FirstNonEmpty(head.ContactPhone, applicant.ContactPhone));

        // ── 经办人 ──
        Put(payload.Fields, FieldKeys.OPERATOR_NAME,
            FirstNonEmpty(operatorRow?.OperatorName, organization?.OrganizationName));
        Put(payload.Fields, FieldKeys.OPERATOR_UNIT,
            FirstNonEmpty(operatorRow?.OperatorUnitName, organization?.OrganizationName));
        Put(payload.Fields, FieldKeys.OPERATOR_UNIT_PHONE, organization?.OrganizationPhone);

        // ── 人数 / 日期 ──
        var memberCount = members.Count.ToString(CultureInfo.InvariantCulture);
        Put(payload.Fields, FieldKeys.APPLICANT_COUNT, memberCount);
        Put(payload.Fields, FieldKeys.HEAD_FAMILY_SIZE, memberCount);
        Put(payload.Fields, FieldKeys.REPORT_DATE, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        // 旧版 ResolveAssetSurveyDate：核查流程无调查记录 → 以申请日兜底，格式 yyyy年M月d日。
        Put(payload.Fields, FieldKeys.SURVEY_DATE,
            head.ApplicationDate == default
                ? null
                : head.ApplicationDate.ToString("yyyy年M月d日", CultureInfo.InvariantCulture));

        // ── 区划 ──
        Put(payload.Fields, FieldKeys.DISTRICT,
            FirstNonEmpty(organization?.CountyName, region?.CountyName));
        Put(payload.Fields, FieldKeys.TOWN, FirstNonEmpty(organization?.TownName, region?.TownName));
        Put(payload.Fields, FieldKeys.COMMUNITY, region?.VillageName);

        // ── 通知编号：batch 前 8 位，稳定可复现（旧版随机号不回放） ──
        Put(payload.Fields, FieldKeys.NOTICE_NUMBER, BuildNoticeNumber(requested));

        // ── 区县民政局 / 电话（旧版取经办机构上级组织） ──
        Put(payload.Fields, FieldKeys.DISTRICT_CIVIL_BUREAU, organization?.ParentName);
        Put(payload.Fields, FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE, organization?.ParentPhone);

        // ── 代理人 ──
        var hasAgent = agent is not null;
        var agentRelationDisplay = FirstNonEmpty(agent?.AgentRelationshipDisplay, agent?.AgentRelationship);
        Put(payload.Fields, FieldKeys.IS_AGENT, hasAgent ? "是" : "否");
        if (agent is not null)
        {
            Put(payload.Fields, FieldKeys.AGENT_NAME, agent.AgentName);
            Put(payload.Fields, FieldKeys.AGENT_ID_CARD, agent.AgentIdCard);
            Put(payload.Fields, FieldKeys.AGENT_ID_TYPE,
                FirstNonEmpty(agent.AgentIdTypeDisplay, agent.AgentIdType));
            Put(payload.Fields, FieldKeys.AGENT_RELATION, agentRelationDisplay);
        }

        // 模板1 授权承诺书家庭成员索引槽位（户主恒为 1）+ 代理字段（无代理写空串，与旧版一致）。
        var slots = new List<FamilyFieldBuilder.FamilySlot>();
        foreach (var member in members.Take(6))
        {
            slots.Add(new FamilyFieldBuilder.FamilySlot(
                member.ApplicantName ?? string.Empty,
                member.ApplicantIdCard ?? string.Empty,
                member.IdTypeDisplay ?? string.Empty,
                member.RelationshipDisplay ?? string.Empty));
        }

        if (slots.Count > 0)
        {
            FamilyFieldBuilder.Build(
                payload.Fields,
                slots,
                hasAgent,
                agent?.AgentName,
                agent?.AgentIdCard,
                agent?.AgentIdTypeDisplay,
                agentRelationDisplay);
        }

        // ── 同批次人员表 ──
        foreach (var member in members)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            Put(row, FieldKeys.FAMILY_MEMBER_NAME, member.ApplicantName);
            Put(row, FieldKeys.FAMILY_MEMBER_ID_CARD, member.ApplicantIdCard);
            Put(row, FieldKeys.FAMILY_MEMBER_RELATION,
                FirstNonEmpty(member.RelationshipDisplay, member.Relationship));
            Put(row, FieldKeys.FAMILY_MEMBER_CERT_TYPE,
                FirstNonEmpty(member.IdTypeDisplay, member.ApplicantIdType));
            Put(row, FieldKeys.FAMILY_MEMBER_ADDRESS, member.FamilyAddress);
            if (row.Count > 0)
            {
                payload.TableRows.Add(row);
            }
        }

        return Result.Success(payload);
    }

    private async Task<(AssetCheckAgentRow? Agent, AssetCheckOperatorRow? Operator)> LoadBatchContextAsync(
        string? batchId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(batchId))
        {
            return (null, null);
        }

        var operatorResult = await database.QuerySingleAsync<AssetCheckOperatorRow>(
            @"SELECT operator_name, operator_unit_name, operator_user_id
              FROM nc_biz_asset_check_operators
              WHERE batch_id = $1
              LIMIT 1",
            ct, batchId);

        // 经办人缺失不阻断打印；代理同样按“无代理”降级。
        var agentResult = await database.QuerySingleAsync<AssetCheckAgentRow>(
            @"SELECT a.agent_name, a.agent_id_type, a.agent_id_card, a.agent_relationship,
                     COALESCE((SELECT i.item_value FROM nc_dict_items i
                               WHERE i.category = 'FamilyRelationships'
                                 AND i.item_key = a.agent_relationship AND i.is_active LIMIT 1),
                              NULLIF(a.agent_relationship, '')) AS agent_relationship_display,
                     COALESCE((SELECT i.item_value FROM nc_dict_items i
                               WHERE i.category = 'IdTypes'
                                 AND i.item_key = a.agent_id_type AND i.is_active LIMIT 1),
                              NULLIF(a.agent_id_type, '')) AS agent_id_type_display
              FROM nc_biz_asset_check_agents a
              WHERE a.batch_id = $1
              LIMIT 1",
            ct, batchId);

        return (
            agentResult.IsSuccess ? agentResult.Value : null,
            operatorResult.IsSuccess ? operatorResult.Value : null);
    }

    private async Task<AssetCheckOrganizationRow?> LoadOrganizationAsync(
        int userId,
        CancellationToken ct)
    {
        var result = await database.QuerySingleAsync<AssetCheckOrganizationRow>(
            @"SELECT COALESCE(o.name, '') AS organization_name,
                     COALESCE(o.phone, '') AS organization_phone,
                     COALESCE(rc.county_name, '') AS county_name,
                     COALESCE(rt.town_name, '') AS town_name,
                     COALESCE(p.name, '') AS parent_name,
                     COALESCE(p.phone, '') AS parent_phone
              FROM nc_sys_users u
              LEFT JOIN nc_sys_organizations o ON o.id = u.organization_id
              LEFT JOIN nc_regions_counties rc ON rc.id = o.county_id
              LEFT JOIN nc_regions_towns rt ON rt.id = o.town_id
              LEFT JOIN nc_sys_organizations p ON p.id = o.parent_id
              WHERE u.id = $1
              LIMIT 1",
            ct, userId);

        return result.IsSuccess ? result.Value : null;
    }

    private async Task<AssetCheckRegionRow?> LoadRegionAsync(
        string? community,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(community))
        {
            return null;
        }

        var result = await database.QuerySingleAsync<AssetCheckRegionRow>(
            @"SELECT COALESCE(v.village_name, '') AS village_name,
                     COALESCE(t.town_name, '') AS town_name,
                     COALESCE(c.county_name, '') AS county_name
              FROM nc_regions_villages v
              LEFT JOIN nc_regions_towns t ON t.id = v.town_id
              LEFT JOIN nc_regions_counties c ON c.id = t.county_id
              WHERE v.village_code = $1
              LIMIT 1",
            ct, community.Trim());

        return result.IsSuccess ? result.Value : null;
    }

    private static string? ResolveDetailAddress(
        string? familyAddress,
        string? countyName,
        string? townName,
        AssetCheckRegionRow? region)
    {
        if (string.IsNullOrWhiteSpace(familyAddress))
        {
            return null;
        }

        var address = familyAddress.Trim();
        var villageName = region?.VillageName;
        var prefixes = new[]
        {
            JoinParts(countyName, townName, villageName),
            JoinParts(townName, villageName),
            JoinParts(villageName),
        };

        foreach (var prefix in prefixes)
        {
            if (!string.IsNullOrEmpty(prefix)
                && address.StartsWith(prefix, StringComparison.Ordinal)
                && address.Length > prefix.Length)
            {
                return address[prefix.Length..].TrimStart(' ', '/', '-', '\\', '，', ',');
            }
        }

        return address;
    }

    private static string? JoinParts(params string?[] parts)
    {
        var joined = string.Concat(parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string BuildNoticeNumber(AssetCheckPrintRow row)
    {
        var batch = row.BatchId?.Trim();
        if (!string.IsNullOrEmpty(batch))
        {
            return batch.Length <= 8 ? $"ASSET-{batch}" : $"ASSET-{batch[..8]}";
        }

        return $"ASSET-{row.Id.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string? FormatDate(DateTime value)
        => value == default ? null : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Put(Dictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>核对推送打印数据快照（字段键 = FieldKeys，表格行 = 同批次人员）。</summary>
public sealed class AssetCheckPrintPayload
{
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    public List<Dictionary<string, string>> TableRows { get; set; } = new();
}

internal sealed class AssetCheckPrintRow
{
    public long Id { get; set; }
    public string? BatchId { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdType { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Relationship { get; set; }
    public string? RelationshipDisplay { get; set; }
    public string? IdTypeDisplay { get; set; }
    public bool IsHead { get; set; }
    public string? HeadIdCard { get; set; }
    public string? FamilyAddress { get; set; }
    public string? Community { get; set; }
    public string? ApplicationReason { get; set; }
    public string? ApplicationReasonDisplay { get; set; }
    public DateTime ApplicationDate { get; set; }
    public string? ContactPhone { get; set; }
    public string? Status { get; set; }
}

internal sealed class AssetCheckAgentRow
{
    public string? AgentName { get; set; }
    public string? AgentIdType { get; set; }
    public string? AgentIdCard { get; set; }
    public string? AgentRelationship { get; set; }
    public string? AgentRelationshipDisplay { get; set; }
    public string? AgentIdTypeDisplay { get; set; }
}

internal sealed class AssetCheckOperatorRow
{
    public string? OperatorName { get; set; }
    public string? OperatorUnitName { get; set; }
    public int? OperatorUserId { get; set; }
}

internal sealed class AssetCheckOrganizationRow
{
    public string? OrganizationName { get; set; }
    public string? OrganizationPhone { get; set; }
    public string? CountyName { get; set; }
    public string? TownName { get; set; }
    public string? ParentName { get; set; }
    public string? ParentPhone { get; set; }
}

internal sealed class AssetCheckRegionRow
{
    public string? VillageName { get; set; }
    public string? TownName { get; set; }
    public string? CountyName { get; set; }
}
