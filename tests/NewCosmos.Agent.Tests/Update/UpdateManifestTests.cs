using NewCosmos.Agent.Services.Update;

namespace NewCosmos.Agent.Tests.Update;

/// <summary>清单解析：正常 / 坏 JSON / 缺字段 / 非 http url / 可选字段缺省。</summary>
public class UpdateManifestTests
{
    private const string ValidJson = """
        {
          "version": "2.0.20261130",
          "url": "https://cosmos-static.sua17.cn:20000/downloads/windows/NewCosmosAgentSetup_2.0.20261130.exe",
          "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
          "notes": "自动更新上线",
          "publishedAt": "2026-11-28T12:00:00+08:00"
        }
        """;

    [Fact]
    public void Parse_ValidManifest_ReturnsSuccessWithAllFields()
    {
        var result = AgentUpdateManifest.Parse(ValidJson);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("2.0.20261130", result.Value.Version);
        Assert.StartsWith("https://", result.Value.Url);
        Assert.Equal(64, result.Value.Sha256.Length);
        Assert.Equal("自动更新上线", result.Value.Notes);
        Assert.Equal("2026-11-28T12:00:00+08:00", result.Value.PublishedAt);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_MatchingPowerShellOutputAndHandEditedFiles()
    {
        var result = AgentUpdateManifest.Parse(
            """{ "Version": "2.0.20261130", "Url": "http://127.0.0.1:8080/a.exe", "Sha256": "ab" }""");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("2.0.20261130", result.Value.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json }")]
    [InlineData("[1,2,3]")]
    public void Parse_InvalidJson_Fails(string? json)
    {
        var result = AgentUpdateManifest.Parse(json);

        Assert.True(result.IsFailure);
        Assert.Equal(AgentUpdateManifest.ErrorCodeInvalidJson, result.ErrorCode);
    }

    [Theory]
    [InlineData("""{ "url": "https://x/a.exe", "sha256": "ab" }""")]
    [InlineData("""{ "version": "2.0", "sha256": "ab" }""")]
    [InlineData("""{ "version": "2.0", "url": "https://x/a.exe" }""")]
    [InlineData("""{ "version": " ", "url": "https://x/a.exe", "sha256": "ab" }""")]
    public void Parse_MissingRequiredField_Fails(string json)
    {
        var result = AgentUpdateManifest.Parse(json);

        Assert.True(result.IsFailure);
        Assert.Equal(AgentUpdateManifest.ErrorCodeMissingField, result.ErrorCode);
    }

    [Theory]
    [InlineData("ftp://cosmos-static.sua17.cn/a.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("/downloads/windows/a.exe")]
    [InlineData("cosmos-static.sua17.cn/a.exe")]
    public void Parse_NonHttpAbsoluteUrl_Fails(string url)
    {
        var result = AgentUpdateManifest.Parse(
            $$"""{ "version": "2.0", "url": "{{url}}", "sha256": "ab" }""");

        Assert.True(result.IsFailure);
        Assert.Equal(AgentUpdateManifest.ErrorCodeInvalidUrl, result.ErrorCode);
    }

    [Fact]
    public void Parse_OptionalFieldsMayBeOmitted()
    {
        var result = AgentUpdateManifest.Parse(
            """{ "version": "2.0.20261130", "url": "https://x/a.exe", "sha256": "ab" }""");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(string.Empty, result.Value.Notes);
        Assert.Equal(string.Empty, result.Value.PublishedAt);
    }

    [Fact]
    public void Parse_NullOptionalFields_DegradeToEmptyString()
    {
        var result = AgentUpdateManifest.Parse(
            """{ "version": "2.0.20261130", "url": "https://x/a.exe", "sha256": "ab", "notes": null, "publishedAt": null }""");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(string.Empty, result.Value.Notes);
        Assert.Equal(string.Empty, result.Value.PublishedAt);
    }

    [Fact]
    public void Parse_TrimsSurroundingWhitespace()
    {
        var result = AgentUpdateManifest.Parse(
            """{ "version": " 2.0.20261130 ", "url": " https://x/a.exe ", "sha256": " ab " }""");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("2.0.20261130", result.Value.Version);
        Assert.Equal("https://x/a.exe", result.Value.Url);
    }
}
