using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace TipCL.Tests;

/// <summary>
/// C1b-fix:测试专用的 **Avalonia 会话线程**。
///
/// 为什么需要(这是 C1b 打回的真因):
/// <c>AppBuilder.SetupWithoutStarting()</c> 会把 Avalonia 的 <c>Dispatcher.UIThread</c> 绑在
/// **第一个调用它的那个线程**上,而 <c>AvaloniaObject.VerifyAccess()</c> 只认这一个线程。
/// xunit v2 走 VSTest 通道时每个测试类跑在自己的 worker 线程上,于是「哪个类先碰到 Avalonia,
/// 哪个类就独占 Dispatcher 线程」,其它类里一旦构造控件(`new Button()` 就会进
/// <c>AvaloniaObject..ctor</c> → <c>VerifyAccess</c>)必抛
/// <c>System.InvalidOperationException : Call from invalid thread</c>。
///
/// 这条路径**取决于线程调度**,所以同一份代码会时绿时红:C1b 的两次绿灯是运气,
/// 不是证据。本类把 Avalonia 的平台与 Dispatcher 固定到一个**后台线程**上,
/// 所有碰 Avalonia 的用例体都通过 <see cref="Run{T}(Func{T})"/> / <see cref="Run(Action)"/>
/// 在这个线程上执行 —— 平台线程与用例执行线程从此是同一个,<c>VerifyAccess</c> 恒过,
/// 两个通道(进程内执行器 / VSTest)也再没有差别。
///
/// 设计约束:
/// - **不引新依赖**:不用 <c>Avalonia.Headless.XUnit</c>(进程内执行器不认 xunit 的
///   collection / fixture 特性,见 D-05 与 A6),所以本类只靠一个后台线程 + 阻塞队列。
/// - **不依赖 xunit 的类夹具**:<c>IClassFixture</c> / <c>ICollectionFixture</c> 在本项目的
///   进程内执行器里不会被展开,所以这里是纯静态入口,直接反射调用也能工作。
/// - **异常原样透传**:用例体里抛的异常用 <see cref="ExceptionDispatchInfo"/> 在调用线程上
///   重新抛出,类型、消息与原始栈帧都保留 —— xunit 报的是真实断言消息,不是包装异常。
/// - **超时算失败**:用例体 30 秒不返回就抛 <see cref="TimeoutException"/>(消息里写明是会话超时),
///   绝不静默通过。
/// - 用例体必须**同步**:在会话线程上阻塞等一个又要把续体投回 Dispatcher 的 Task 会自锁,
///   本项目的 Avalonia 用例全是同步的,保持这个约定即可。
/// </summary>
internal static class AvaloniaSession
{
    /// <summary>会话线程上等待一个用例体的上限(Avalonia 用例都是毫秒级的,30 秒足够)。</summary>
    internal const int TimeoutMilliseconds = 30000;

    /// <summary>泵循环的空转间隔:每轮先取完队列、再跑一次 Dispatcher 的排队工作,然后歇这么久。</summary>
    private const int PumpIntervalMilliseconds = 2;

    private static readonly object Gate = new object();

    private static readonly BlockingCollection<Action> Queue = new BlockingCollection<Action>();

    private static readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);

    private static Thread? _thread;

    private static Exception? _startupError;

    /// <summary>起一次会话线程(幂等,加锁;后台线程,带名字,便于排查残留线程)。</summary>
    internal static void Ensure()
    {
        if (_thread is not null)
        {
            return;
        }

        lock (Gate)
        {
            if (_thread is not null)
            {
                return;
            }

            var thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "TipCL-Avalonia-Session",
            };
            thread.Start();

            if (!Ready.Wait(TimeoutMilliseconds))
            {
                throw new TimeoutException(
                    "Avalonia 会话线程 " + TimeoutMilliseconds + " ms 内没有就绪(AvaloniaSession.Ensure 超时)");
            }

            if (_startupError is not null)
            {
                ExceptionDispatchInfo.Capture(_startupError).Throw();
            }

            _thread = thread;
        }
    }

    /// <summary>会话线程的入口:先起 Avalonia 平台,再进泵循环。</summary>
    private static void ThreadMain()
    {
        try
        {
            AppBuilder.Configure<TipCL.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        catch (Exception ex)
        {
            // 起平台失败:把异常带回 Ensure() 的调用方(不要在这里吞掉)。
            _startupError = ex;
            Ready.Set();
            return;
        }

        Ready.Set();
        Pump();
    }

    /// <summary>泵循环:轮询队列(带超时,好让空闲时也跑 Dispatcher 的排队工作),直到队列被标记结束。</summary>
    private static void Pump()
    {
        while (true)
        {
            try
            {
                if (Queue.TryTake(out Action? item, PumpIntervalMilliseconds))
                {
                    item();
                }
            }
            catch (ObjectDisposedException)
            {
                // ObjectDisposedException 是 InvalidOperationException 的子类,必须排在前面。
                return;
            }
            catch (InvalidOperationException)
            {
                // 队列已 CompleteAdding 且为空 —— 正常收摊,不是错误。
                return;
            }
            catch (Exception ex)
            {
                // 单个工作项自己已经负责把异常交给调用方;这里兜住泵循环,免得线程直接死掉。
                Debug.WriteLine("AvaloniaSession 工作项异常: " + ex);
            }

            // 跑 Dispatcher 排队的工作(布局、绑定、样式、渲染定时器的回调都在这)。
            try
            {
                Dispatcher.UIThread.RunJobs();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AvaloniaSession RunJobs 异常: " + ex);
            }
        }
    }

    /// <summary>在会话线程上跑一段有返回值的用例体,并把结果带回来。</summary>
    internal static T Run<T>(Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Ensure();

        T result = default!;
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);

        Queue.Add(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(TimeoutMilliseconds))
        {
            throw new TimeoutException(
                "Avalonia 用例体在会话线程上 " + TimeoutMilliseconds + " ms 未返回(疑似死锁或卡在等待 Dispatcher);" +
                "见 PROGRESS.md 的 C1b-fix 小节");
        }

        if (error is not null)
        {
            // 原样重抛:保留异常类型、消息与原始栈(断言消息因此不会被包装掉)。
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result;
    }

    /// <summary>在会话线程上跑一段无返回值的用例体。</summary>
    internal static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Run<object?>(() =>
        {
            body();
            return null;
        });
    }
}
