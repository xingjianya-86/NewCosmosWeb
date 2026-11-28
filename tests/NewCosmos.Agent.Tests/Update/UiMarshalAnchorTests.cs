using System.Drawing;
using Forms = System.Windows.Forms;

namespace NewCosmos.Agent.Tests.Update;

/// <summary>
/// 托盘跨线程封送锚点：隐藏 <see cref="Forms.Control"/> + 强制窗口句柄。
/// 托盘构造里这一步一旦抛异常，整个托盘都起不来，所以单独锁住该模式可用。
/// </summary>
public class UiMarshalAnchorTests
{
    [Fact]
    public void HiddenControl_CreatesHandle_AndSupportsInvoke()
    {
        using var anchor = new Forms.Control { Visible = false, Size = Size.Empty };
        _ = anchor.Handle;

        Assert.True(anchor.IsHandleCreated);

        // 同线程 Invoke 直接执行：验证封送机制本身可用（升级回调依赖它回到消息循环线程）。
        var executed = 0;
        anchor.Invoke(new Action(() => executed = 1));
        Assert.Equal(1, executed);

        // BeginInvoke 是托盘实际使用的方式，关键是不能抛（无消息循环时消息会排队，不在此断言送达）。
        anchor.BeginInvoke(new Action(() => executed = 2));
        Assert.True(executed >= 1);
    }
}
