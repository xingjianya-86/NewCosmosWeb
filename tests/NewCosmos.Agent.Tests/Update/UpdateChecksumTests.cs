using NewCosmos.Agent.Services.Update;

namespace NewCosmos.Agent.Tests.Update;

/// <summary>SHA256 比对纯函数 + 安装包文件名/缓存穿透的确定性行为。</summary>
public class UpdateChecksumTests
{
    // "abc" 的标准 SHA256（十六进制大写，ComputeSha256Async 的返回格式）。
    private const string AbcSha256Upper =
        "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    [Fact]
    public void Sha256Matches_IgnoresCase()
    {
        Assert.True(AgentUpdateService.Sha256Matches(AbcSha256Upper, AbcSha256Upper.ToLowerInvariant()));
        Assert.True(AgentUpdateService.Sha256Matches(AbcSha256Upper.ToLowerInvariant(), AbcSha256Upper));
    }

    [Fact]
    public void Sha256Matches_IgnoresPrefixAndWhitespace()
    {
        Assert.True(AgentUpdateService.Sha256Matches("0x" + AbcSha256Upper, " " + AbcSha256Upper + " "));
    }

    [Fact]
    public void Sha256Matches_RejectsDifferentDigest()
    {
        var other = new string('a', 64);
        Assert.False(AgentUpdateService.Sha256Matches(AbcSha256Upper, other));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(AbcSha256Upper, "")]
    [InlineData("", AbcSha256Upper)]
    [InlineData("ab", "ab")] // 过短：绝不能因为"两边一样"就放行
    [InlineData(AbcSha256Upper, "short")]
    public void Sha256Matches_RejectsEmptyOrMalformedDigest(string? expected, string? actual)
    {
        Assert.False(AgentUpdateService.Sha256Matches(expected, actual));
    }

    [Fact]
    public void NormalizeSha256_KeepsOnlyHexDigits()
    {
        Assert.Equal("0123ABCDEF", AgentUpdateService.NormalizeSha256("0x0123 abc-def"));
        Assert.Equal(string.Empty, AgentUpdateService.NormalizeSha256(null));
    }

    [Fact]
    public async Task ComputeSha256Async_ReturnsUppercaseHex_MatchingKnownVector()
    {
        var path = Path.Combine(Path.GetTempPath(), "NewCosmosAgentTests", "abc.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "abc");

        try
        {
            var actual = await AgentUpdateService.ComputeSha256Async(path);
            Assert.Equal(AbcSha256Upper, actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("2.0.20261130", "NewCosmosAgentSetup_2.0.20261130.exe")]
    [InlineData("", "NewCosmosAgentSetup_unknown.exe")]
    [InlineData(null, "NewCosmosAgentSetup_unknown.exe")]
    public void BuildInstallerFileName_IsPathSafe(string? version, string expected)
    {
        var name = AgentUpdateDefaults.BuildInstallerFileName(version);

        Assert.Equal(expected, name);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, name);
    }

    [Fact]
    public void BuildInstallerFileName_StripsPathTraversalCharacters()
    {
        var name = AgentUpdateDefaults.BuildInstallerFileName("../../evil.exe");

        // 路径分隔符不在白名单内，全部被剔除，无法逃出更新缓存目录。
        Assert.Equal("NewCosmosAgentSetup_....evil.exe.exe", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.Equal(name, Path.GetFileName(name));
    }

    [Fact]
    public void WithCacheBuster_AppendsTimestampQuery()
    {
        var url = AgentUpdateDefaults.WithCacheBuster("https://x/agent-latest.json");

        Assert.StartsWith("https://x/agent-latest.json?t=", url);
        Assert.Equal(1, url.Count(c => c == '?')); // 只加一个查询参数
    }

    [Fact]
    public void WithCacheBuster_UsesAmpersand_WhenQueryAlreadyPresent()
    {
        var url = AgentUpdateDefaults.WithCacheBuster("https://x/agent-latest.json?v=1");

        Assert.StartsWith("https://x/agent-latest.json?v=1&t=", url);
    }

    [Fact]
    public void WithCacheBuster_HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, AgentUpdateDefaults.WithCacheBuster(null));
        Assert.Equal(string.Empty, AgentUpdateDefaults.WithCacheBuster("  "));
    }

    [Fact]
    public void ManifestPathSuffix_MatchesPublishedLocation()
    {
        Assert.Equal("/downloads/windows/agent-latest.json", AgentUpdateDefaults.ManifestPathSuffix);
    }
}
