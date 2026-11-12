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
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception) { }
        };
    }
}
