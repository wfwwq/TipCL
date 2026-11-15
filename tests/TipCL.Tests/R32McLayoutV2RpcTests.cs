using System;
using System.IO;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// R32-MC:Windows 的 <c>windos\tests\TipCL.Tests\LayoutV2RpcTests.cs</c>(5 例,分支
/// layout-shell-hooks @ fa1b3d7 + fix/r31-ui-audit @ 33b5fff)里**本包能搬的那一条**。
///
/// 另外 4 条**搬不了**,逐条原因(不是"懒得搬",是依赖不在本包地盘):
///   1 <c>BothLayoutMethodsAreExposedToTheRpcDispatcher</c> —— 要 <c>layout_v2_get</c>/<c>layout_v2_apply</c>
///     经 <c>RpcDispatcher</c> 按名找得到;这两个方法定义在 Windows 的 <c>Core\BackendApi.Layout.cs</c>
///     (L1 新增的 partial),**mac 树里没有这个文件**(实测:全树 grep <c>layout_v2</c> 零命中)。
///   2 <c>GetIsByteIdenticalToTheAiToolResult</c> —— 要 <c>Core\Launcher\Ai\LayoutTools.Get()</c> 对拍;
///     mac 树**没有** <c>Launcher\Ai\LayoutTools.cs</c>(那是 L1 的 AI 工具层,不是纯模型层)。
///   3 <c>ApplyFailureIsByteIdenticalToTheAiToolResultAndWritesNothing</c> —— 同上,要 <c>LayoutTools.Apply</c>
///     + <c>LayoutTools.BackupPrefix</c>。
///   4 <c>ApplyWithNothingRecognizedExplainsItselfInsteadOfWriting</c> —— 同上。
/// ⇒ 这 4 条的恢复条件 =「布局 v2 的 AI/RPC 接线包」把 <c>LayoutTools.cs</c> 与 <c>BackendApi.Layout.cs</c>
/// 搬进 mac 树(依赖关系记在 progress/R32-MC.md)。
///
/// 本文件搬的是第 5 条:它是**源码文本断言**(不依赖任何布局 v2 运行时),读
/// <c>src\TipCL.Core\Plugins\PluginHostApi.cs</c> 的 <c>LauncherDeny</c> 集合,钉住"插件那条路没被关掉"。
/// 移植差异只有两处路径:仓库根标记由 <c>TipCL.sln</c> 改成 <c>TipCL.Mac.sln</c>、被测文件路径由
/// <c>src/TipCL/Core/Plugins/PluginHostApi.cs</c> 改成 mac 树的 <c>src/TipCL.Core/Plugins/PluginHostApi.cs</c>;
/// **断言一字未改**。
/// 现状如实说明:mac 树还没有布局 v2 的 RPC,所以"不含 layout_v2"这一条目前是**前瞻性钉子**
/// (等接线包落地后它才真正保护布局 RPC 的可见性);"含 ai_confirm"这一条现在就有实效力。
/// </summary>
public class R32McLayoutV2RpcTests
{
	private static string RepoFile(string rel)
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.Mac.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir!.FullName, rel.Replace('/', Path.DirectorySeparatorChar)));
	}

	// ————— 插件那条路没被关掉(LauncherDeny 是按源码断言的门禁,与 LayoutNativeBlockTests 同一手法) —————

	[Fact]
	public void PluginLauncherGateDoesNotDenyTheLayoutRpc()
	{
		string src = RepoFile("src/TipCL.Core/Plugins/PluginHostApi.cs");
		int at = src.IndexOf("LauncherDeny = new HashSet<string>", StringComparison.Ordinal);
		Assert.True(at > 0, "找不到 LauncherDeny 定义");
		string line = src.Substring(at, Math.Min(400, src.Length - at));
		int end = line.IndexOf('}') + 1;
		line = (end > 0) ? line.Substring(0, end) : line;

		Assert.DoesNotContain("layout_v2", line, StringComparison.Ordinal);
		// 顺带钉住"拒的是哪些"(改动这条清单时这里会红,提醒同步考虑布局 RPC 的可见性)
		Assert.Contains("ai_confirm", line, StringComparison.Ordinal);
	}
}
