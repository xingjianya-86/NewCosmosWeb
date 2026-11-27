using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Core.Tests;

/// <summary>
/// 本机操作（LocalOp）op 白名单：服务端入队 400 校验与 Agent 执行器分发共用同一份 Core 常量。
/// </summary>
public class LocalOpConstantsTests
{
    [Fact]
    public void KnownOps_ContainsExactlySixDocumentedOps()
    {
        // 与 Agent README §12.4 的 op 清单一一对应；新增 op 必须同步白名单 + 执行器分发分支。
        Assert.Equal(6, LocalOpConstants.KnownOps.Count);
        Assert.Contains(LocalOpConstants.OpFontCheck, LocalOpConstants.KnownOps);
        Assert.Contains(LocalOpConstants.OpFontInstall, LocalOpConstants.KnownOps);
        Assert.Contains(LocalOpConstants.OpZeroTierStatus, LocalOpConstants.KnownOps);
        Assert.Contains(LocalOpConstants.OpZeroTierInstall, LocalOpConstants.KnownOps);
        Assert.Contains(LocalOpConstants.OpZeroTierJoin, LocalOpConstants.KnownOps);
        Assert.Contains(LocalOpConstants.OpZeroTierLeave, LocalOpConstants.KnownOps);
    }

    [Theory]
    [InlineData("font.check")]
    [InlineData("font.install")]
    [InlineData("zerotier.status")]
    [InlineData("zerotier.install")]
    [InlineData("zerotier.join")]
    [InlineData("zerotier.leave")]
    public void IsKnownOp_KnownOps_ReturnsTrue(string op)
    {
        Assert.True(LocalOpConstants.IsKnownOp(op));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("font.unknown")]
    [InlineData("FONT.CHECK")]
    [InlineData("zerotier.join ")]
    public void IsKnownOp_UnknownOrMalformed_ReturnsFalse(string? op)
    {
        Assert.False(LocalOpConstants.IsKnownOp(op));
    }
}
