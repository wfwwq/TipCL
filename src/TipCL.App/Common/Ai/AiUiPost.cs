// AI2-stability:AI 页 / AI 修复视图的「回到界面线程」统一走这里。
//
// AiAgent 在后台线程跑,事件通过 Dispatcher.UIThread.Post 回到界面线程更新气泡 / 步骤行。两种时候这一步必须丢弃:
//   1) 界面已经没了:Application.Current 为空 —— 进程退出途中,或无头测试会话在 Dispatcher.ResetForUnitTests
//      里冲刷上一轮遗留的队列(这时建 AiBubble,Res.Bind 拿到空的资源宿主,抛 ArgumentNullException;
//      DlgCloseTests 偶发失败就是这个);
//   2) 页面挂过的窗口已经关了(页面已卸载):再往它上面加气泡没人看得见,还可能碰到已释放的渲染资源。
// 页面只是切到别的页(从视觉树上摘下、窗口还在)不算卸载 —— 对话在后台照常跑,回来能看到结果。
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TipCL.App.Common.Ai;

/// <summary>带「界面还在不在」检查的 Post。</summary>
internal static class AiUiPost
{
    private sealed class State
    {
        public volatile bool HostClosed;

        public bool Subscribed;

        public Window? Host;
    }

    private static readonly ConditionalWeakTable<Control, State> States = new();

    private static int _dropped;

    /// <summary>因界面已不在而丢弃的回调数(测试 / 排查用)。</summary>
    public static int DroppedCount => Volatile.Read(ref _dropped);

    /// <summary>界面层还活着(Application 还在)。</summary>
    public static bool UiAlive => Application.Current != null;

    /// <summary>
    /// 跟踪页面挂在哪个窗口上:窗口关闭 ⇒ 视为页面已卸载;重新挂到(新的)窗口上 ⇒ 恢复。
    /// 页面构造时调一次即可(幂等)。
    /// </summary>
    public static void Track(Control owner)
    {
        State state = States.GetValue(owner, _ => new State());
        if (state.Subscribed)
        {
            return;
        }
        state.Subscribed = true;
        owner.AttachedToVisualTree += (_, _) => Attach(owner, state);
        if (TopLevel.GetTopLevel(owner) is Window)
        {
            Attach(owner, state);
        }
    }

    private static void Attach(Control owner, State state)
    {
        if (TopLevel.GetTopLevel(owner) is not Window w || ReferenceEquals(state.Host, w))
        {
            return;
        }
        state.Host = w;
        state.HostClosed = false;
        w.Closed += (_, _) =>
        {
            if (ReferenceEquals(state.Host, w))
            {
                state.HostClosed = true;
            }
        };
    }

    /// <summary>页面挂过的窗口是否已关闭(没跟踪过 / 从没挂上窗口 = 否)。</summary>
    public static bool IsUnloaded(Control? owner)
        => owner != null && States.TryGetValue(owner, out State? s) && s.HostClosed;

    /// <summary>投递到界面线程;执行时界面已不在或页面已卸载就丢弃(计数),否则照常执行。</summary>
    public static void Post(Control? owner, Action action, DispatcherPriority? priority = null)
    {
        void Run()
        {
            if (!UiAlive || IsUnloaded(owner))
            {
                Interlocked.Increment(ref _dropped);
                return;
            }
            action();
        }
        if (priority is DispatcherPriority p)
        {
            Dispatcher.UIThread.Post(Run, p);
        }
        else
        {
            Dispatcher.UIThread.Post(Run);
        }
    }
}
