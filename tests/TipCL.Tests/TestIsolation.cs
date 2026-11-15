using System;
using System.IO;
using System.Runtime.CompilerServices;
using TipCL.Core.Launcher;

namespace TipCL.Tests;

/// <summary>
/// 整个测试程序集用一个独立的临时数据根。不隔离的话 ROOT 是 %LOCALAPPDATA%\TipCL ——
/// 与 Windows 版 TipCL 共用的真实用户目录,测试会改写其中的 config.json / accounts.json / 实例。
/// 必须在任何测试代码读 Utils.ROOT 或 Config.CONFIG 之前生效,故用模块初始化器。
/// 目录由本进程创建:低完整性级别的沙箱里,进程只能写自己建的目录。
/// </summary>
internal static class TestIsolation
{
#pragma warning disable CA2255 // 测试程序集正是模块初始化器的适用场景
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"tipcl-tests-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("TIPCL_HOME", dir);
        Utils.SetRoot(dir);
        // R31-CIFLAKE:同一时刻把产品的"求和自身路径"注入点钉在隔离根内(见 TestWriteGuard 的说明)。
        // 必须在任何测试体之前:泄漏出来的 `update_staged` 反应是 Post 到 UI 线程的续体,
        // 可能在"发出事件的用例"结束之后才由别的用例的 pump 落地。
        TestWriteGuard.Install(dir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception) { }
        };
    }
}
