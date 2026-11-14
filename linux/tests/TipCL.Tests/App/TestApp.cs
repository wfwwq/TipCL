using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

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
                return _session ??= StartSession();
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

    /// <summary>
    /// 起会话,并在**任何测试体之前**先做一次同步预热派发,把「本会话线程自己的 IDispatcherImpl」
    /// 发布到进程级根 locator 作用域当兜底(见 <see cref="PinSessionDispatcher"/>)。
    /// 预热体必须是同步的:此时兜底还没装上,异步体要走 PushFrame 那条路,而窗口里可能刚被塞进
    /// 一个非 IControlledDispatcherImpl 的调度器,那条路会抛 PlatformNotSupportedException。
    /// 预热只是加固:失败不改变用例行为(退回加固之前的样子),所以这里吞掉异常。
    /// </summary>
    private static HeadlessUnitTestSession StartSession()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        try
        {
            session.Dispatch(PinSessionDispatcher, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 加固失败不影响用例。
        }
        return session;
    }

    /// <summary>
    /// 把本线程这一份 <see cref="IDispatcherImpl"/> 绑进**根** locator 作用域,作为进程级兜底。
    ///
    /// 背景(这是偶发 <c>Call from invalid thread</c> 的真正来源):
    ///   * <c>Dispatcher.UIThread</c> 是**进程级缓存的静态实例**,它的线程亲和来自构造它的那份
    ///     <c>ManagedDispatcherImpl</c>(<c>CurrentThreadIsLoopThread =&gt; _loopThread ==
    ///     Thread.CurrentThread</c>,_loopThread 就是 new 它的线程);
    ///   * <c>HeadlessUnitTestSession</c> 每执行一次 Dispatch 都会先
    ///     <c>Dispatcher.ResetForUnitTests()</c>(把 <c>s_uiThread</c> 置空)再
    ///     <c>AppBuilder.SetupUnsafe()</c>。于是**每次派发边界都有一个 UIThread == null 的窗口**,
    ///     窗口里先读到 UIThread 的线程会成为「UI 线程」:
    ///       1) 最常见的赢家是会话语题自己 —— <c>SetupUnsafe</c> 早期就会读 UIThread
    ///          (Application.RegisterServices -&gt; AvaloniaObject.SetValue -&gt; VerifyAccess);
    ///       2) 其次是产品码的后台线程 —— <c>Common/Api.cs</c> 与 <c>Common/TaskStore.cs</c> 都在
    ///          任意线程上 <c>Dispatcher.UIThread.CheckAccess()/Post()</c>(下载、AI、插件、HTTP 重试
    ///          都这么回投主线程),它们可以恰好撞进这个窗口;
    ///   * 窗口里如果解析不到实现(根作用域空),装进去的是 <c>NullDispatcherImpl</c> —— 它不是
    ///     <c>IControlledDispatcherImpl</c>,之后 <c>PushFrame</c> 直接 PlatformNotSupportedException;
    ///     如果解析到的是别的线程(例如测试里另起的一次无头初始化)留下的实现,装进去的调度器就归
    ///     那个线程,会话线程随后在 <c>RenderLoop.Add</c> / <c>VerifyAccess</c> 上报
    ///     <c>Call from invalid thread</c>。
    ///
    /// 兜底办法:让**根作用域**里始终躺着一份「归会话线程所有」的实现。这样无论窗口里谁抢先创建
    /// UI 调度器,创建出来的调度器都归会话线程,VerifyAccess / PushFrame / Post 全部照常。
    /// 注意:会话每次派发仍会把**自己那份**实现绑进当前作用域(子作用域,优先级更高),所以这层兜底
    /// 只在「子作用域还没绑定」的那一瞬间生效,不改变正常路径。
    /// </summary>
    private static void PinSessionDispatcher()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        Type? locatorType = typeof(Dispatcher).Assembly.GetType("Avalonia.AvaloniaLocator");
        if (locatorType == null) return;

        object? scope = locatorType.GetProperty("Current", All)?.GetValue(null);
        if (scope == null) return;

        var parentField = locatorType.GetField("_parentScope", All);
        object root = scope;
        while (parentField?.GetValue(root) is object parent) root = parent;

        object? impl = locatorType.GetMethod("GetService", All, null, new[] { typeof(Type) }, null)
            ?.Invoke(scope, new object[] { typeof(IDispatcherImpl) });
        if (impl == null) return;

        if (locatorType.GetField("_registry", All)?.GetValue(root) is not Dictionary<Type, Func<object>> registry) return;
        registry[typeof(IDispatcherImpl)] = () => impl;
    }
}
