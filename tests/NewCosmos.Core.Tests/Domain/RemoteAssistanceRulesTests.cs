using NewCosmos.Constants;
using NewCosmos.Services.Domain.SocialAssistance;
using Xunit;

namespace NewCosmos.Core.Tests.Domain;

/// <summary>
/// 异地协助准入规则（方案 A）：本地在享拒绝 / 停保允许并标记户籍迁出 /
/// 其它状态拒绝 / 无档案允许。
/// </summary>
public class RemoteAssistanceRulesTests
{
    [Fact]
    public void 无本地档案_允许()
    {
        Assert.Equal(RemoteAssistanceGate.Allowed, RemoteAssistanceRules.Resolve([]));
        Assert.Equal(RemoteAssistanceGate.Allowed, RemoteAssistanceRules.Resolve(null));
    }

    [Fact]
    public void 在享或已完结_拒绝()
    {
        Assert.Equal(
            RemoteAssistanceGate.BlockedActive,
            RemoteAssistanceRules.Resolve([ApplicationStatusCodes.APPROVED]));
        Assert.Equal(
            RemoteAssistanceGate.BlockedActive,
            RemoteAssistanceRules.Resolve([ApplicationStatusCodes.COMPLETED, ApplicationStatusCodes.STOPPED]));
    }

    [Fact]
    public void 仅停保_允许并标记户籍迁出()
    {
        Assert.Equal(
            RemoteAssistanceGate.AllowedHukouMovedOut,
            RemoteAssistanceRules.Resolve([ApplicationStatusCodes.STOPPED]));
    }

    [Fact]
    public void 其它未闭环状态_拒绝()
    {
        Assert.Equal(
            RemoteAssistanceGate.BlockedLocalArchive,
            RemoteAssistanceRules.Resolve([ApplicationStatusCodes.DRAFT]));
        Assert.Equal(
            RemoteAssistanceGate.BlockedLocalArchive,
            RemoteAssistanceRules.Resolve([ApplicationStatusCodes.SUBMITTED]));
    }

    [Fact]
    public void 状态空白与大小写_容错()
    {
        Assert.Equal(
            RemoteAssistanceGate.Allowed,
            RemoteAssistanceRules.Resolve(["  ", ""]));
        Assert.Equal(
            RemoteAssistanceGate.BlockedActive,
            RemoteAssistanceRules.Resolve(["approved"]));
    }

    [Fact]
    public void 标记显示名()
    {
        Assert.Equal("户籍迁出", RemoteAssistanceConstants.MarkerDisplay(RemoteAssistanceConstants.MarkerHukouMovedOut));
        Assert.Null(RemoteAssistanceConstants.MarkerDisplay(null));
        Assert.Null(RemoteAssistanceConstants.MarkerDisplay("Other"));
    }
}
