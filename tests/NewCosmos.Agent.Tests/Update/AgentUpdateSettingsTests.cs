using NewCosmos.Agent.Configuration;

namespace NewCosmos.Agent.Tests.Update;

/// <summary>自动更新配置项的默认值与 Normalize 钳位/回退。</summary>
public class AgentUpdateSettingsTests
{
    private static readonly string ConfigPath =
        Path.Combine(Path.GetTempPath(), "NewCosmosAgentTests", "agent.json");

    [Fact]
    public void AutoUpdate_DefaultsToEnabled()
    {
        Assert.True(new AgentSettings().AutoUpdate);
        Assert.Equal(6, new AgentSettings().UpdateCheckIntervalHours);
        Assert.Equal(120, new AgentSettings().UpdateFirstCheckDelaySeconds);
        Assert.Equal(string.Empty, new AgentSettings().UpdateManifestUrl);
    }

    [Fact]
    public void Normalize_DerivesManifestUrl_FromServerBaseUrl()
    {
        var settings = new AgentSettings { ServerBaseUrl = "https://cosmos-static.sua17.cn:20000/" };

        settings.Normalize(ConfigPath);

        // ServerBaseUrl 末尾的 / 已去掉，拼接后不能出现双斜杠。
        Assert.Equal(
            "https://cosmos-static.sua17.cn:20000/downloads/windows/agent-latest.json",
            settings.UpdateManifestUrl);
    }

    [Fact]
    public void Normalize_KeepsExplicitManifestUrl()
    {
        var settings = new AgentSettings
        {
            ServerBaseUrl = "https://cosmos-static.sua17.cn:20000",
            UpdateManifestUrl = "http://127.0.0.1:8080/agent-latest.json "
        };

        settings.Normalize(ConfigPath);

        Assert.Equal("http://127.0.0.1:8080/agent-latest.json", settings.UpdateManifestUrl);
    }

    [Fact]
    public void Normalize_LeavesManifestUrlEmpty_WhenServerBaseUrlMissing()
    {
        // 两者都空 → 保持空串：UpdateService 记 Debug 后跳过检查，而不是拼出相对地址报错。
        var settings = new AgentSettings { ServerBaseUrl = "  " };

        settings.Normalize(ConfigPath);

        Assert.Equal(string.Empty, settings.UpdateManifestUrl);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(-5, 6)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(168, 168)]
    [InlineData(999, 168)]
    public void Normalize_ClampsUpdateCheckIntervalHours(int input, int expected)
    {
        var settings = new AgentSettings { UpdateCheckIntervalHours = input };

        settings.Normalize(ConfigPath);

        Assert.Equal(expected, settings.UpdateCheckIntervalHours);
    }

    [Theory]
    [InlineData(-1, 120)]
    [InlineData(0, 0)] // 0 是合法值：启动后立即检查
    [InlineData(120, 120)]
    [InlineData(3601, 3600)]
    public void Normalize_ClampsUpdateFirstCheckDelaySeconds(int input, int expected)
    {
        var settings = new AgentSettings { UpdateFirstCheckDelaySeconds = input };

        settings.Normalize(ConfigPath);

        Assert.Equal(expected, settings.UpdateFirstCheckDelaySeconds);
    }
}
