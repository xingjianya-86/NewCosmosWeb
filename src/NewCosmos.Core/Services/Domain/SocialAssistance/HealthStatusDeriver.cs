using NewCosmos.Constants;
using NewCosmos.Helpers;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 身体状况（健康状况）派生规则——与前端 <c>web/src/views/applications/form</c> 逐字一致的单一实现。
/// <para>
/// 输入：一级疾病（diseaseCategory）、残疾类型（disabilityType）、残疾等级（disabilityLevel）、
/// 年龄（age，取自身份证）、是否重病勾选（isSevereDisease）；输出为 Core key
/// （<see cref="HealthStatusConstants"/>）。
/// </para>
/// <para>
/// 推导顺序：重病且重残 → 重病 → 重残 → 一般（有残疾）/一般（年龄 ≥
/// <see cref="ClassificationConstants.ELDERLY_AGE"/>）→ 健康；地板：勾选重病时
/// 健康/一般 一律抬到 重病（不降级已有的重残/重病且重残）。
/// </para>
/// <para>
/// 覆盖守卫：只有“当前值”是标准档（空值或 5 个业务档的 Core key/中文显示值）时才允许派生覆盖；
/// 历史非标准值（患病/残疾/其他/Other 等）原样保留，对齐
/// <c>web/src/views/applications/form/options.ts</c> 的既有口径注释。
/// </para>
/// </summary>
public static class HealthStatusDeriver
{
    /// <summary>
    /// 允许被派生覆盖的“当前值”白名单：空值 + 5 个业务档的 Core key。
    /// 输入先经 <see cref="DictValueMapper.ToCoreHealthStatus"/> 归一（英文 Core key / 旧显示值 /
    /// 中文历史值都会折算成 key），故这里只需 key；不含 患病/残疾/其他/Other 等历史值。
    /// </summary>
    private static readonly HashSet<string> OverridableValues = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
        HealthStatusConstants.HEALTHY,
        HealthStatusConstants.FAIR_OR_WEAK,
        HealthStatusConstants.SEVERE_DISEASE,
        HealthStatusConstants.SEVERE_DISABILITY,
        HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY
    };

    /// <summary>
    /// 按共享规则派生身体状况。
    /// </summary>
    /// <param name="diseaseCategory">一级疾病（中文 ItemKey；空或“无任何疾病”视为无病）</param>
    /// <param name="disabilityType">残疾类型（Core key 或中文历史值，非空即算有残疾）</param>
    /// <param name="disabilityLevel">残疾等级（Level1/一级…两套值都认）</param>
    /// <param name="age">年龄（身份证解析失败/null 视为未满 60）</param>
    /// <param name="isSevereDisease">是否重病勾选（地板规则输入）</param>
    /// <param name="currentValue">当前健康状况（表单值或 Core key）；非标准历史值原样返回</param>
    /// <returns>Core key（HealthStatusConstants.*）</returns>
    public static string Derive(
        string? diseaseCategory,
        string? disabilityType,
        string? disabilityLevel,
        int? age,
        bool isSevereDisease,
        string? currentValue)
    {
        // 覆盖守卫：当前值非标准档 → 原样保留（不派生、不归一）。
        // 先经 DictValueMapper 归一到 Core key：英文 Core key / 旧显示值（健康或良好…）/ 中文历史值
        // 都会折算成 key；未知历史值（患病/残疾等）归一后仍不在白名单 → 返回原始值。
        var current = DictValueMapper.ToCoreHealthStatus(currentValue);
        if (!OverridableValues.Contains(current))
        {
            return currentValue ?? string.Empty;
        }

        var hasDisease = !string.IsNullOrWhiteSpace(diseaseCategory)
            && !string.Equals(
                diseaseCategory.Trim(),
                DefaultValuesConstants.DISEASE_CATEGORY_KEY,
                StringComparison.Ordinal);
        var hasDisability = !string.IsNullOrWhiteSpace(disabilityType);
        var hasSevereDisability = hasDisability
            && ClassificationConstants.DisabilityLevel.IsSevereLevel(disabilityLevel);

        var derived = (hasDisease, hasSevereDisability) switch
        {
            (true, true) => HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY,
            (true, false) => HealthStatusConstants.SEVERE_DISEASE,
            (false, true) => HealthStatusConstants.SEVERE_DISABILITY,
            // 有残疾但非重度 → 一般；无残疾按高龄阈值（age 为 null 时视为未满 60）
            (false, false) when hasDisability => HealthStatusConstants.FAIR_OR_WEAK,
            (false, false) when age >= ClassificationConstants.ELDERLY_AGE
                => HealthStatusConstants.FAIR_OR_WEAK,
            _ => HealthStatusConstants.HEALTHY
        };

        // 地板：勾选重病时，健康/一般 一律抬到 重病（重残/重病且重残不受影响）。
        if (isSevereDisease
            && derived is HealthStatusConstants.HEALTHY or HealthStatusConstants.FAIR_OR_WEAK)
        {
            derived = HealthStatusConstants.SEVERE_DISEASE;
        }

        return derived;
    }
}
