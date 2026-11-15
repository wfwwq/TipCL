using System;
using System.IO;
using System.Runtime.CompilerServices;
using TipCL.Core.Launcher;

namespace TipCL.Tests;

/// <summary>
/// 整个测试程序集用一个独立的临时数据根。不隔离的话根会落到真实用户目录
/// %LOCALAPPDATA%\TipCL —— 那是桌面版 TipCL 真在用的数据目录,跑测会改写里面的
/// config.json / accounts.json / 实例与世界存档(R1)。
///
/// 必须在任何测试代码第一次读 Utils.ROOT 之前生效,故用模块初始化器:Utils.ROOT 是
/// **惰性缓存的静态量**(Utils.cs 的 ROOT 属性),只有 ResolveRoot() 才每次都去读
/// TIPCL_HOME。谁先碰到它,进程的根就钉死在那儿;TestHome 只设环境变量,钉死之后再也
/// 改不动 —— 于是后续用例互相污染,还可能去写真实用户目录。
/// Linux 侧 tests/TipCL.Tests/TestIsolation.cs 早就有这一层,Windows 侧此前缺它。
/// 回归守卫见 TestIsolationTests.cs。
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
		// 环境变量与缓存的静态根**都要**设:Utils.ResolveRoot() 读前者,Utils.ROOT 读后者。
		// 只设一个就会出现「同一个进程里两条取根的路不一致」——产品代码里两条都在用
		// (例如 PhoneLink.StatePath 走 ResolveRoot(),其余多数走 ROOT)。
		Environment.SetEnvironmentVariable("TIPCL_HOME", dir);
		Utils.SetRoot(dir);
		AppDomain.CurrentDomain.ProcessExit += delegate
		{
			try { Directory.Delete(dir, recursive: true); }
			catch (Exception) { }
		};
	}
}
