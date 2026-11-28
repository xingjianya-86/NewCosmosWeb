using NewCosmos.Agent.Services.Update;

namespace NewCosmos.Agent.Tests.Update;

/// <summary>版本比较：分段数值比、段数不等补 0、非数字回退字符串比、空值视为最小。</summary>
public class UpdateVersionTests
{
    [Theory]
    [InlineData("2.0.20261128", "2.0.20261128", 0)]
    [InlineData("2.0.20261129", "2.0.20261128", 1)]
    [InlineData("2.0.20261127", "2.0.20261128", -1)]
    // 数值比较而非字符串比较："10" 必须大于 "9"（字符串比会得到相反结论）
    [InlineData("10.0", "9.0.9999", 1)]
    [InlineData("9.0", "10.0", -1)]
    // 段数不等补 0：缺段按 0 处理，不能因为"段少"就更大
    [InlineData("2.1", "2.0.999999", 1)]
    [InlineData("2.0", "2.0.1", -1)]
    [InlineData("2.0.0.0", "2.0", 0)]
    // null/空视为最小
    [InlineData("", "2.0", -1)]
    [InlineData(null, "2.0", -1)]
    [InlineData("2.0", "", 1)]
    [InlineData(null, null, 0)]
    // 非数字段走序字符串比较
    [InlineData("1.0-beta", "1.0", 1)]
    [InlineData("1.0", "1.0-beta", -1)]
    [InlineData("abc", "abd", -1)]
    [InlineData("abd", "abc", 1)]
    public void CompareVersion_ReturnsExpectedSign(string? candidate, string? current, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(AgentUpdateManifest.CompareVersion(candidate, current)));
    }

    [Fact]
    public void CompareVersion_IsSymmetric()
    {
        Assert.Equal(
            Math.Sign(AgentUpdateManifest.CompareVersion("2.0.20261130", "2.0.20261129")),
            -Math.Sign(AgentUpdateManifest.CompareVersion("2.0.20261129", "2.0.20261130")));
    }

    [Fact]
    public void IsNewerThan_OnlyTrueWhenManifestIsAhead()
    {
        var manifest = new AgentUpdateManifest { Version = "2.0.20261130" };

        Assert.True(manifest.IsNewerThan("2.0.20261129"));
        Assert.False(manifest.IsNewerThan("2.0.20261130"));
        Assert.False(manifest.IsNewerThan("2.0.20261201"));
        Assert.True(manifest.IsNewerThan(string.Empty));
    }
}
