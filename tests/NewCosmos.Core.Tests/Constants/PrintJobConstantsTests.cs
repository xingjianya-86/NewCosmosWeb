using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Core.Tests;

/// <summary>
/// 打印任务类型常量（本机操作指令通道 LocalOp 属于 Core 业务常量，三端共用）。
/// </summary>
public class PrintJobConstantsTests
{
    [Fact]
    public void JobKindLocalOp_IsLocalOp()
    {
        Assert.Equal("LocalOp", PrintJobConstants.JobKindLocalOp);
    }

    [Theory]
    [InlineData(PrintJobConstants.JobKindLocalOp, "本机操作")]
    [InlineData(PrintJobConstants.JobKindPhoto, "照片")]
    [InlineData(PrintJobConstants.JobKindPreview, "预览")]
    [InlineData(PrintJobConstants.JobKindTemplateBatch, "批量文书")]
    [InlineData(PrintJobConstants.JobKindTemplate, "模板文书")]
    [InlineData("Unknown", "模板文书")]
    [InlineData(null, "模板文书")]
    public void GetJobKindName_KnownKinds_ReturnsDisplayName(string? jobKind, string expected)
    {
        Assert.Equal(expected, PrintJobConstants.GetJobKindName(jobKind));
    }

    [Fact]
    public void PrintJob_LocalOp_DisplayTitleShowsOp()
    {
        var job = new PrintJob
        {
            JobKind = PrintJobConstants.JobKindLocalOp,
            TemplateName = "zerotier.join"
        };

        Assert.Equal("本机操作·zerotier.join", job.DisplayTitle);
        Assert.Equal("本机操作", job.JobKindName);
    }

    [Fact]
    public void PrintJob_LocalOpWithoutOp_FallsBackToUnknown()
    {
        var job = new PrintJob { JobKind = PrintJobConstants.JobKindLocalOp };

        Assert.Equal("本机操作·未知指令", job.DisplayTitle);
    }
}
