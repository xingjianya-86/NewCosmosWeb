using NewCosmos.Constants;
using NewCosmos.Models.Enums;

namespace NewCosmos.Core.Tests.Domain;

/// <summary>
/// 异地协助状态扩展测试：新增状态的枚举映射、可编辑性、终态判定。
/// </summary>
public class RemoteAssistanceStatusTests
{
    [Fact]
    public void FromCode_远程协助办理中_映射正确()
    {
        var status = ApplicationStatusExtensions.FromCode(
            ApplicationStatusCodes.REMOTE_ASSISTANCE_DRAFT);
        Assert.Equal(ApplicationStatus.RemoteAssistanceDraft, status);
    }

    [Fact]
    public void FromCode_远程协助已办结_映射正确()
    {
        var status = ApplicationStatusExtensions.FromCode(
            ApplicationStatusCodes.REMOTE_ASSISTANCE_COMPLETED);
        Assert.Equal(ApplicationStatus.RemoteAssistanceCompleted, status);
    }

    [Fact]
    public void GetDescription_远程协助办理中_返回正确文案()
    {
        var desc = ApplicationStatus.RemoteAssistanceDraft.GetDescription();
        Assert.Equal("异地协助办理中", desc);
    }

    [Fact]
    public void GetDescription_远程协助已办结_返回正确文案()
    {
        var desc = ApplicationStatus.RemoteAssistanceCompleted.GetDescription();
        Assert.Equal("异地协助已办结", desc);
    }

    [Fact]
    public void GetCode_远程协助办理中_返回正确代码()
    {
        var code = ApplicationStatus.RemoteAssistanceDraft.GetCode();
        Assert.Equal("RemoteAssistanceDraft", code);
    }

    [Fact]
    public void GetCode_远程协助已办结_返回正确代码()
    {
        var code = ApplicationStatus.RemoteAssistanceCompleted.GetCode();
        Assert.Equal("RemoteAssistanceCompleted", code);
    }

    [Fact]
    public void IsEditable_远程协助办理中_可编辑()
    {
        Assert.True(ApplicationStatus.RemoteAssistanceDraft.IsEditable());
    }

    [Fact]
    public void IsEditable_远程协助已办结_不可编辑()
    {
        Assert.False(ApplicationStatus.RemoteAssistanceCompleted.IsEditable());
    }

    [Fact]
    public void IsEditable_草稿_可编辑()
    {
        Assert.True(ApplicationStatus.Draft.IsEditable());
    }

    [Fact]
    public void IsFinal_远程协助已办结_是终态()
    {
        Assert.True(ApplicationStatus.RemoteAssistanceCompleted.IsFinal());
    }

    [Fact]
    public void IsFinal_远程协助办理中_不是终态()
    {
        Assert.False(ApplicationStatus.RemoteAssistanceDraft.IsFinal());
    }

    [Fact]
    public void IsActive_远程协助已办结_不是活动状态()
    {
        Assert.False(ApplicationStatus.RemoteAssistanceCompleted.IsActive());
    }

    [Fact]
    public void IsActive_远程协助办理中_不是活动状态()
    {
        Assert.False(ApplicationStatus.RemoteAssistanceDraft.IsActive());
    }

    [Fact]
    public void 业务类型常量_低收入_值正确()
    {
        Assert.Equal("LowIncome", ApplicationTypeConstants.LOW_INCOME);
    }

    [Fact]
    public void 业务类型常量_异地协助_值正确()
    {
        Assert.Equal("RemoteAssistance", ApplicationTypeConstants.REMOTE_ASSISTANCE);
    }

    [Fact]
    public void 状态码_远程协助办理中_值正确()
    {
        Assert.Equal("RemoteAssistanceDraft", ApplicationStatusCodes.REMOTE_ASSISTANCE_DRAFT);
    }

    [Fact]
    public void 状态码_远程协助已办结_值正确()
    {
        Assert.Equal("RemoteAssistanceCompleted", ApplicationStatusCodes.REMOTE_ASSISTANCE_COMPLETED);
    }
}
