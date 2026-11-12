using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;

namespace TipCL.Tests.App;

/// <summary>
/// 测试用的 Avalonia 无头会话。**所有碰 Avalonia 的测试体都要经 <see cref="Run"/> /
/// <see cref="RunAsync"/> 落到会话的专属 UI 线程上执行**。
///
/// 为什么必须这样:AvaloniaObject 构造有线程亲和(Dispatcher.VerifyAccess),而 TaskStore /
/// InProcessApi 的事件派发按 Dispatcher.UIThread.CheckAccess() 决定「同步执行还是 Post 到队列」。
/// 直接在线程池线程上跑,会随 xUnit 的线程调度随机出现两类失败:
///   1) 「Call from invalid thread」——控件在非 UI 线程构造;
///   2) 事件处理器还没跑,断言就拿到 null —— 事件被 Post 走了,没同步执行。
/// 这套问题在引入「构造主窗口即挂载 Application 级主题资源」后被放大,故统一走会话。
/// </summary>
internal static class TestApp
{
    private static readonly object Gate = new();

    private static HeadlessUnitTestSession? _session;

    private static HeadlessUnitTestSession Session
    {
        get
        {
            lock (Gate)
                return _session ??= HeadlessUnitTestSession.StartNew(typeof(TestApp));
        }
    }

    /// <summary>
    /// 会话的 AppBuilder 入口,<see cref="HeadlessUnitTestSession.StartNew"/> 按这个名字反射调用。
    /// 必须是真 Skia:默认的无头绘图是桩 —— Bitmap.Save 写出 0 字节,任何 PNG 都解码成 1×1,
    /// 皮肤合成这类读写位图的断言无从成立。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TipCL.App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>
    /// 在无头 UI 线程上执行测试体,并阻塞到它跑完;测试体里的异常原样抛回测试线程。
    /// Dispatch 只是把工作排进会话队列并返回 Task —— 不等它,测试会在测试体执行前就
    /// 「通过」,断言失败全被吞掉。
    /// </summary>
    public static void Run(Action body) =>
        Session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// 异步测试体版本,同样阻塞等待。返回的 Task 必然已完成:表达式体的 <c>void</c> 测试方法
    /// 会静默丢弃返回值,所以等待不能交给调用方。
    /// </summary>
    public static Task RunAsync(Func<Task> body)
    {
        Session.Dispatch(async () =>
        {
            await body();
            return true;
        }, CancellationToken.None).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }
}
