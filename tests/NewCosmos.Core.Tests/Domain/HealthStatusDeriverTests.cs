using NewCosmos.Constants;
using NewCosmos.Services.Domain.SocialAssistance;
using Xunit;

namespace NewCosmos.Core.Tests;

/// <summary>
/// 身体状况派生规则（HealthStatusDeriver）——与 web/src/views/applications/form 的前端实现
/// 逐字一致的后端锚点。锁点：6 种推导分支、重病地板、覆盖守卫（历史值原样保留）、
/// 年龄 null 视为未满 60、大小写/空白容错。
/// </summary>
public class HealthStatusDeriverTests
{
    private static string Derive(
        string? diseaseCategory = null,
        string? disabilityType = null,
        string? disabilityLevel = null,
        int? age = null,
        bool isSevereDisease = false,
        string? currentValue = "")
        => HealthStatusDeriver.Derive(
            diseaseCategory, disabilityType, disabilityLevel, age, isSevereDisease, currentValue);

    // ── 6 种推导分支 ──

    [Xunit.Fact]
    public void 重病加重度残疾_重病且重残()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY,
            Derive("高血压", "Vision", "Level1", 45));

    [Xunit.Fact]
    public void 仅疾病_重病()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE,
            Derive("高血压", disabilityLevel: "Level3", age: 45));

    [Xunit.Fact]
    public void 仅重度残疾_重残()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISABILITY,
            Derive(disabilityType: "Vision", disabilityLevel: "Level2", age: 45));

    [Xunit.Fact]
    public void 非重度残疾_一般()
        => Assert.Equal(
            HealthStatusConstants.FAIR_OR_WEAK,
            Derive(disabilityType: "Vision", disabilityLevel: "Level4", age: 30));

    [Theory]
    [InlineData(60)]
    [InlineData(63)]
    [InlineData(90)]
    public void 无病无残且达到高龄阈值_一般(int age)
        => Assert.Equal(HealthStatusConstants.FAIR_OR_WEAK, Derive(age: age));

    [Theory]
    [InlineData(59)]
    [InlineData(30)]
    public void 无病无残未达高龄阈值_健康(int age)
        => Assert.Equal(HealthStatusConstants.HEALTHY, Derive(age: age));

    [Xunit.Fact]
    public void 无任何疾病视为无病_健康()
        => Assert.Equal(
            HealthStatusConstants.HEALTHY,
            Derive(DefaultValuesConstants.DISEASE_CATEGORY_KEY, age: 45));

    [Xunit.Fact]
    public void 无任何疾病视为无病_有非重度残疾仍是一般()
        => Assert.Equal(
            HealthStatusConstants.FAIR_OR_WEAK,
            Derive(
                DefaultValuesConstants.DISEASE_CATEGORY_KEY,
                disabilityType: "Vision",
                disabilityLevel: "Level3",
                age: 45));

    // ── 地板规则 ──

    [Xunit.Fact]
    public void 勾选重病且结果为健康_抬升为重病()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE,
            Derive(age: 30, isSevereDisease: true));

    [Xunit.Fact]
    public void 勾选重病且结果为一般_抬升为重病()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE,
            Derive(age: 70, isSevereDisease: true));

    [Xunit.Fact]
    public void 勾选重病但已是重残_不降级()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISABILITY,
            Derive(disabilityType: "Vision", disabilityLevel: "Level1", age: 45, isSevereDisease: true));

    [Xunit.Fact]
    public void 勾选重病且重病且重残_保持不变()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY,
            Derive("高血压", "Vision", "Level2", 45, isSevereDisease: true));

    // ── 覆盖守卫：历史非标准值原样保留 ──

    [Theory]
    [InlineData("患病")]
    [InlineData("残疾")]
    [InlineData("其他")]
    [InlineData("Other")]
    [InlineData("既往重病史")]
    public void 当前值非标准档_原样保留不派生(string currentValue)
    {
        // 输入本应派生为“健康”，但当前值是历史值 → 一个字都不许动
        Assert.Equal(
            currentValue,
            Derive(disabilityType: null, disabilityLevel: null, age: 30, currentValue: currentValue));
    }

    [Xunit.Fact]
    public void 当前值为标准档_被派生覆盖()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE,
            Derive("高血压", currentValue: "一般"));

    [Xunit.Fact]
    public void 当前值为空或空白_按派生覆盖()
    {
        Assert.Equal(HealthStatusConstants.HEALTHY, Derive(age: 30, currentValue: ""));
        Assert.Equal(HealthStatusConstants.HEALTHY, Derive(age: 30, currentValue: null));
        Assert.Equal(HealthStatusConstants.FAIR_OR_WEAK, Derive(age: 65, currentValue: "   "));
    }

    [Xunit.Fact]
    public void 当前值为中文标准档_被派生覆盖()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISABILITY,
            Derive(disabilityType: "视力", disabilityLevel: "一级", age: 45, currentValue: "健康"));

    // ── 年龄 / 容错 ──

    [Xunit.Fact]
    public void 年龄为空_视为未满60_健康()
        => Assert.Equal(HealthStatusConstants.HEALTHY, Derive(age: null));

    [Xunit.Fact]
    public void 输入带空白_归一后派生()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY,
            Derive(" 高血压 ", " 视力 ", " 二级 ", age: 45, currentValue: " 一般 "));

    [Xunit.Fact]
    public void 残疾等级大小写不敏感_level1也算重度()
        => Assert.Equal(
            HealthStatusConstants.SEVERE_DISABILITY,
            Derive(disabilityType: "vision", disabilityLevel: "LEVEL1", age: 45));

    [Xunit.Fact]
    public void 当前值大小写不敏感_仍视为标准档并被覆盖()
        => Assert.Equal(
            HealthStatusConstants.HEALTHY,
            Derive(age: 30, currentValue: "healthy "));
}
