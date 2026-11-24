using NewCosmos.Constants;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Services;

/// <summary>业务表的数据范围目标。</summary>
public enum DataScopeTarget
{
    Applications,
    ElderlyApplications,
    AssetChecks
}

/// <summary>
/// 已拼好的数据范围条件。<see cref="Sql"/> 为空表示不限制（全部数据）。
/// 参数占位符从调用方传入的 startIndex 开始编号，调用方需把 Parameters 追加到自己的参数列表。
/// </summary>
public sealed record DataScopeClause(string Sql, IReadOnlyList<object?> Parameters)
{
    public static readonly DataScopeClause Empty = new(string.Empty, Array.Empty<object?>());

    public bool IsEmpty => string.IsNullOrWhiteSpace(Sql);
}

/// <summary>
/// 把“当前用户可管理的机构（乡镇/村）”翻译成业务表可用的 SQL 条件。
/// 数据范围口径来自 Core 的 IDataScopeService；这里只做区域维度映射，不重写角色判定。
/// </summary>
public sealed class DataScopeFilterBuilder(
    IDataScopeService dataScope,
    IDatabaseService database,
    ILoggerService logger)
{
    public async Task<DataScopeClause> BuildAsync(
        int userId,
        DataScopeTarget target,
        string alias,
        int startIndex,
        CancellationToken ct = default)
    {
        var scope = await dataScope.GetEffectiveScopeAsync(userId, ct);
        if (string.Equals(scope.Scope, DataScopeConstants.ALL, StringComparison.OrdinalIgnoreCase))
        {
            return DataScopeClause.Empty;
        }

        if (string.Equals(scope.Scope, DataScopeConstants.SELF, StringComparison.OrdinalIgnoreCase))
        {
            return await BuildSelfClauseAsync(userId, target, alias, startIndex, ct);
        }

        if (scope.OrganizationId is not { } organizationId || organizationId <= 0)
        {
            return Deny();
        }

        var organizationIds = await dataScope.GetManageableOrganizationIdsAsync(userId, ct);
        if (organizationIds.Count == 0)
        {
            return Deny();
        }

        var orgsResult = await database.QueryAsync<OrganizationScopeRow>(
            "SELECT id, name, town_id, village_id FROM nc_sys_organizations WHERE id = ANY($1)",
            ct,
            organizationIds.ToArray());

        if (orgsResult.IsFailure)
        {
            logger.Error(
                $"读取数据范围机构失败，按拒绝处理: UserId={userId}, {orgsResult.ErrorCode} {orgsResult.Message}");
            return Deny();
        }

        var orgs = orgsResult.Value ?? [];
        var allTownIds = orgs.Select(x => x.TownId).Where(x => x is > 0).Select(x => x!.Value).Distinct().ToArray();
        var villageIds = orgs.Select(x => x.VillageId).Where(x => x is > 0).Select(x => x!.Value).Distinct().ToArray();

        // ORG 范围：村经办人只应看到本村，不能因为机构同时带 town_id 就放大到整镇。
        // ORG_AND_CHILDREN / 乡镇机构无 village_id 时，才按乡镇展开到全部村。
        var isOrgScope = string.Equals(scope.Scope, DataScopeConstants.ORG, StringComparison.OrdinalIgnoreCase);
        var includeTownScope = !isOrgScope || villageIds.Length == 0;
        var townIds = includeTownScope ? allTownIds : Array.Empty<int>();

        var townNames = Array.Empty<string>();
        var villageKeywords = Array.Empty<string>();
        var villageCodes = Array.Empty<string>();

        if (townIds.Length > 0)
        {
            var townsResult = await database.QueryAsync<TownRow>(
                "SELECT id, town_name FROM nc_regions_towns WHERE id = ANY($1)",
                ct,
                townIds);
            if (townsResult.IsSuccess)
            {
                townNames = (townsResult.Value ?? [])
                    .Select(x => x.TownName?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
        }

        if (townIds.Length > 0 || villageIds.Length > 0)
        {
            // 村经办人只查本村；乡镇/下级范围才按 town_id 展开全部村。
            var villageSql = includeTownScope
                ? "SELECT id, town_id, village_name, village_code FROM nc_regions_villages " +
                  "WHERE id = ANY($1) OR town_id = ANY($2)"
                : "SELECT id, town_id, village_name, village_code FROM nc_regions_villages " +
                  "WHERE id = ANY($1)";
            var villagesResult = includeTownScope
                ? await database.QueryAsync<VillageRow>(villageSql, ct, villageIds, townIds)
                : await database.QueryAsync<VillageRow>(villageSql, ct, villageIds);
            if (villagesResult.IsSuccess)
            {
                var villages = villagesResult.Value ?? [];
                villageKeywords = villages
                    .Select(x => NormalizeVillageKeyword(x.VillageName))
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                villageCodes = villages
                    .Select(x => x.VillageCode?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
        }

        return target switch
        {
            DataScopeTarget.Applications => BuildApplicationClause(
                alias, startIndex, townIds, villageIds, townNames, villageKeywords),
            DataScopeTarget.ElderlyApplications => BuildElderlyClause(
                alias, startIndex, townIds, villageIds, villageKeywords),
            DataScopeTarget.AssetChecks => BuildAssetClause(alias, startIndex, villageCodes),
            _ => Deny()
        };
    }

    private async Task<DataScopeClause> BuildSelfClauseAsync(
        int userId,
        DataScopeTarget target,
        string alias,
        int startIndex,
        CancellationToken ct)
    {
        // 资产核对表没有 created_by 列，SELF 范围无法安全归属，按拒绝处理。
        if (target == DataScopeTarget.AssetChecks)
        {
            return Deny();
        }

        var usernameResult = await database.ExecuteScalarAsync<string?>(
            "SELECT username FROM nc_sys_users WHERE id = $1 LIMIT 1",
            ct,
            userId);

        var username = usernameResult.IsSuccess ? usernameResult.Value?.Trim() : null;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Deny();
        }

        var column = target == DataScopeTarget.Applications ? "created_by" : "created_by";
        return new DataScopeClause(
            $"{alias}.{column} = ${startIndex}",
            new object?[] { username });
    }

    private static DataScopeClause BuildApplicationClause(
        string alias,
        int startIndex,
        int[] townIds,
        int[] villageIds,
        string[] townNames,
        string[] villageKeywords)
    {
        var conditions = new List<string>();
        var parameters = new List<object?>();
        var index = startIndex;

        if (townIds.Length > 0)
        {
            conditions.Add($"{alias}.town_id = ANY(${index})");
            parameters.Add(townIds);
            index++;
        }

        if (villageIds.Length > 0)
        {
            conditions.Add($"{alias}.village_id = ANY(${index})");
            parameters.Add(villageIds);
            index++;
        }

        if (townNames.Length > 0)
        {
            conditions.Add($"{alias}.town = ANY(${index})");
            parameters.Add(townNames);
            index++;
        }

        if (villageKeywords.Length > 0)
        {
            conditions.Add($"{alias}.community ILIKE ANY(${index})");
            parameters.Add(villageKeywords.Select(x => $"%{x}%").ToArray());
        }

        return conditions.Count == 0 ? Deny() : new DataScopeClause(
            "(" + string.Join(" OR ", conditions) + ")",
            parameters);
    }

    private static DataScopeClause BuildElderlyClause(
        string alias,
        int startIndex,
        int[] townIds,
        int[] villageIds,
        string[] villageKeywords)
    {
        var conditions = new List<string>();
        var parameters = new List<object?>();
        var index = startIndex;

        if (townIds.Length > 0)
        {
            conditions.Add($"{alias}.hukou_town_id = ANY(${index})");
            parameters.Add(townIds);
            index++;
        }

        if (villageIds.Length > 0)
        {
            conditions.Add($"{alias}.hukou_village_id = ANY(${index})");
            parameters.Add(villageIds);
            index++;
        }

        // 高龄档案的村 ID 基本齐全，优先按 hukou_village_id 精确过滤；
        // 仅当没有村 ID（乡镇级/历史脏数据）时才退回地址文本匹配，避免把同镇其他村带进来。
        if (villageKeywords.Length > 0 && villageIds.Length == 0)
        {
            conditions.Add(
                $"({alias}.hukou_address ILIKE ANY(${index}) OR {alias}.family_address ILIKE ANY(${index}))");
            parameters.Add(villageKeywords.Select(x => $"%{x}%").ToArray());
        }

        return conditions.Count == 0 ? Deny() : new DataScopeClause(
            "(" + string.Join(" OR ", conditions) + ")",
            parameters);
    }

    private static DataScopeClause BuildAssetClause(
        string alias,
        int startIndex,
        string[] villageCodes)
    {
        if (villageCodes.Length == 0)
        {
            return Deny();
        }

        return new DataScopeClause(
            $"{alias}.community = ANY(${startIndex})",
            new object?[] { villageCodes });
    }

    /// <summary>村委会名称归一为可匹配片段：柳西村委会 / 柳西村村民委员会 → 柳西。</summary>
    private static string NormalizeVillageKeyword(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        value = value.Replace("村民委员会", string.Empty, StringComparison.Ordinal)
            .Replace("居民委员会", string.Empty, StringComparison.Ordinal)
            .Replace("村委会", string.Empty, StringComparison.Ordinal)
            .Replace("居委会", string.Empty, StringComparison.Ordinal);

        return value.TrimEnd('村').Trim();
    }

    private static DataScopeClause Deny() => new("1 = 0", Array.Empty<object?>());
}

public sealed class OrganizationScopeRow
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }
}

public sealed class TownRow
{
    public int Id { get; set; }
    public string? TownName { get; set; }
}

public sealed class VillageRow
{
    public int Id { get; set; }
    public int TownId { get; set; }
    public string? VillageName { get; set; }
    public string? VillageCode { get; set; }
}
