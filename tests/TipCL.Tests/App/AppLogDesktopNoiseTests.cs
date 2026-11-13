// R21f:未观察任务异常的分流 —— 「桌面没有 AppMenu 服务」这条已知噪声降级成**一条** WARN,
// 其余任何未观察异常照旧按 ERROR 落盘。
//
// 背景(盒子实测):XFCE 上没有 com.canonical.AppMenu.Registrar 这个 DBus 服务,而 Avalonia 的
// X11/FreeDesktop 集成每开一个新窗口都会去注册它 ⇒ 一次会话落 24 条同一条消息的 unhandled(Task),
// 把「0 unhandled」这个信号彻底淹掉。判定口径收得很紧(类型名 + 两条消息片段都要满足)。
//
// 正向路径(真的 Tmds.DBus.Protocol.DBusException)在单测里构造不出来 —— 那是 Avalonia 的传递依赖,
// 我们没有直接引用,也不该为了一个日志用例硬绑它;所以正向由**真机会话**验证:开过浮层/对话框之后,
// 日志里应出现**恰好一条**带「已知无害」的 WARN,且 ERROR 计数为 0。这里钉的是「不许放宽」这一侧。
using System;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

public class AppLogDesktopNoiseTests
{
    /// <summary>长得像 DBus 异常、消息也带那两条片段,但类型名不对 —— 不许放过。</summary>
    private sealed class FakeDbusException : Exception
    {
        public FakeDbusException(string message) : base(message) { }
    }

    [Fact]
    public void PlainExceptions_AreNotBenign()
    {
        Assert.False(AppLog.IsBenignDesktopNoise(null));
        Assert.False(AppLog.IsBenignDesktopNoise(new InvalidOperationException("Collection was modified")));
        Assert.False(AppLog.IsBenignDesktopNoise(new AggregateException(new InvalidOperationException("boom"))));
    }

    [Fact]
    public void DbusShapedButWrongType_IsNotBenign()
    {
        Assert.False(AppLog.IsBenignDesktopNoise(new AggregateException(
            new FakeDbusException("ServiceUnknown: The name com.canonical.AppMenu.Registrar was not provided"))));
    }

    [Fact]
    public void MixedAggregate_IsNotBenign()
    {
        // 只要有一条内层不满足,整包就不放过(宁可多报,不可漏报)
        Assert.False(AppLog.IsBenignDesktopNoise(new AggregateException(
            new InvalidOperationException("ServiceUnknown com.canonical.AppMenu.Registrar"),
            new FakeDbusException("whatever"))));
    }

    [Fact]
    public void MissingMessageFragments_AreNotBenign()
    {
        Assert.False(AppLog.IsBenignDesktopNoise(new AggregateException(new FakeDbusException("ServiceUnknown: something else"))));
        Assert.False(AppLog.IsBenignDesktopNoise(new AggregateException(new FakeDbusException("com.canonical.AppMenu.Registrar missing"))));
        Assert.False(AppLog.IsBenignDesktopNoise(new AggregateException(new FakeDbusException(""))));
    }

    /// <summary>
    /// 口径必须**精确到类型名**:这正是「不放宽」的那一半(另一侧:内层全满足才放过)。
    /// 用一条同形但类型不对的异常证明「类型名不匹配就拒绝」,顺带固定被接受的那个名字。
    /// </summary>
    [Fact]
    public void AcceptedTypeName_IsTheTmdsOne_NotSomethingElse()
    {
        Assert.NotEqual("Tmds.DBus.Protocol.DBusException", typeof(FakeDbusException).FullName);
        Assert.False(AppLog.IsBenignDesktopNoise(new AggregateException(
            new FakeDbusException("ServiceUnknown com.canonical.AppMenu.Registrar"))));
    }
}
