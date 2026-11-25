using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Server.Services;

/// <summary>
/// 渐退期选中户的家庭成员明细（只读）。
/// 口径与旧版 <c>GracePeriodExpiringListViewModel.LoadMembersAsync</c> 一致：
/// 户主 + 共同生活成员，排除赡养（MemberCategory=Support）与已登记死亡成员，户主在前；
/// 取数复用 Core <see cref="IFamilyMemberService"/>，关系文本按 FamilyRelationships 字典解析。
/// </summary>
public static class GracePeriodMemberQueryService
{
    public static async Task<Result<GracePeriodMembersResponse>> ListAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IFamilyMemberService familyMembers,
        ILoggerService logger,
        int userId,
        long applicationId,
        CancellationToken ct = default)
    {
        try
        {
            // 先校验档案在数据范围内：越权时不泄露申请是否存在，统一按未找到处理。
            var scopeResult = await GracePeriodQueryService.IsApplicationInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeResult.IsFailure)
            {
                return Result.Failure<GracePeriodMembersResponse>(
                    scopeResult.ErrorCode!, scopeResult.Message!);
            }

            if (!scopeResult.Value)
            {
                return Result.Failure<GracePeriodMembersResponse>(
                    ErrorCodes.NOT_FOUND, "申请档案不存在或不在数据范围内");
            }

            var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (membersResult.IsFailure)
            {
                logger.Warn(
                    $"渐退期成员加载失败: ApplicationId={applicationId}, " +
                    $"{membersResult.ErrorCode} {membersResult.Message}");
                return Result.Failure<GracePeriodMembersResponse>(
                    membersResult.ErrorCode!, membersResult.Message!);
            }

            var deathResult = await familyMembers.GetDeadIdCardsByApplicationIdAsync(applicationId, ct);
            if (deathResult.IsFailure)
            {
                logger.Warn(
                    $"渐退期死亡成员过滤失败: ApplicationId={applicationId}, " +
                    $"{deathResult.ErrorCode} {deathResult.Message}");
                return Result.Failure<GracePeriodMembersResponse>(
                    deathResult.ErrorCode!, deathResult.Message!);
            }

            var deathIdCards = new HashSet<string>(
                deathResult.Value ?? [], StringComparer.Ordinal);

            // 与旧版同序：先排除赡养/死亡，再户主优先、其余按 id。
            var eligible = (membersResult.Value ?? [])
                .Where(m => !string.Equals(
                    m.MemberCategory, MemberCategoryConstants.SUPPORT,
                    StringComparison.OrdinalIgnoreCase))
                .Where(m => !deathIdCards.Contains(m.IdCard ?? string.Empty))
                .OrderByDescending(m => m.IsHouseholdHead)
                .ThenBy(m => m.Id)
                .ToList();

            var relationshipDisplays = await LoadRelationshipDisplaysAsync(
                database, logger, eligible, ct);

            var items = new List<GracePeriodMemberDto>(eligible.Count);
            foreach (var member in eligible)
            {
                var relationship = member.RelationshipToHead?.Trim() ?? string.Empty;
                var relationshipText = relationship.Length == 0
                    ? "-"
                    : relationshipDisplays.GetValueOrDefault(relationship, relationship);

                items.Add(new GracePeriodMemberDto(
                    member.Id,
                    member.Name ?? string.Empty,
                    relationship,
                    relationshipText,
                    member.Age,
                    member.IdCard ?? string.Empty,
                    member.AnnualIncome));
            }

            logger.Info($"渐退期成员明细: ApplicationId={applicationId}, 成员{items.Count}人");
            return Result.Success(new GracePeriodMembersResponse(applicationId, items));
        }
        catch (Exception ex)
        {
            logger.Error($"查询渐退期成员明细异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<GracePeriodMembersResponse>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    /// <summary>
    /// 一次批查关系字典（= ANY($2)，不做逐行查询）。
    /// 字典读取失败时降级为原始代码（与旧版字典未命中同表现），不影响成员名单本身。
    /// </summary>
    private static async Task<Dictionary<string, string>> LoadRelationshipDisplaysAsync(
        IDatabaseService database,
        ILoggerService logger,
        IReadOnlyCollection<FamilyMember> members,
        CancellationToken ct)
    {
        var displays = new Dictionary<string, string>(StringComparer.Ordinal);
        var codes = members
            .Select(m => m.RelationshipToHead?.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (codes.Length == 0)
        {
            return displays;
        }

        var dictResult = await database.QueryAsync<RelationshipDictRow>(
            "SELECT item_key, item_value FROM nc_dict_items " +
            "WHERE category = $1 AND item_key = ANY($2) AND is_active LIMIT 200",
            ct,
            DictionaryTypeCodes.FamilyRelationships,
            codes);

        if (dictResult.IsFailure)
        {
            logger.Warn(
                $"渐退期关系字典读取失败，按原码展示: {dictResult.ErrorCode} {dictResult.Message}");
            return displays;
        }

        foreach (var row in dictResult.Value ?? [])
        {
            if (string.IsNullOrWhiteSpace(row.ItemKey))
            {
                continue;
            }

            displays[row.ItemKey.Trim()] = row.ItemValue?.Trim() ?? row.ItemKey.Trim();
        }

        return displays;
    }
}

/// <summary>Core 反射映射需要 public 可写属性与公共无参构造函数。</summary>
public sealed class RelationshipDictRow
{
    public string? ItemKey { get; set; }
    public string? ItemValue { get; set; }
}
