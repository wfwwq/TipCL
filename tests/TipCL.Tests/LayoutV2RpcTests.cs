using System;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// 对外开放的布局 v2 RPC(<c>BackendApi.layout_v2_get</c> / <c>layout_v2_apply</c>)。
///
/// 设计 §5.1 要求的那格能力「供 AI / 插件 / 未来端口」:AI 那条路本来就有(LayoutTools),
/// 缺的是**能被外部按方法名调到**。这里钉三件事:
/// <list type="number">
/// <item><b>真的对外暴露</b> —— 经 <see cref="RpcDispatcher"/> 按名字找得到、调得动
///   (桥的 <c>/rpc</c> 与插件的 <c>ctx.Launcher</c> 走的都是这个派发器);</item>
/// <item><b>语义与 AI 工具是同一份</b> —— 同一入参下返回值与 <see cref="LayoutTools"/> 逐字相同
///   (不是"另造一套差不多"的 RPC);</item>
/// <item><b>真能读能改</b> —— apply 落盘 + 自动备份 + get 立刻反映;失败不写盘。</item>
/// </list>
/// 与 <c>LayoutWidgetTests</c> 同一套隔离:全部落盘走 <c>DirOverride</c> 临时目录,绝不写用户真实数据目录。
/// </summary>
public class LayoutV2RpcTests : IDisposable
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_layout_rpc_tests_" + Guid.NewGuid().ToString("N"));

	private readonly BackendApi _api;

	public LayoutV2RpcTests()
	{
		_api = new BackendApi(new EventBus());
		UseDir();
	}

	/// <summary>每个用例开头都重设一次:这几个是进程级静态(别的布局用例也碰),不假设没人动过。</summary>
	private void UseDir()
	{
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
	}

	public void Dispose()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
		try
		{
			if (Directory.Exists(_tmp))
			{
				Directory.Delete(_tmp, recursive: true);
			}
		}
		catch (Exception)
		{
		}
	}

	private static string RepoFile(string rel)
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir!.FullName, rel.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static string[] BackupProfiles(LayoutV2 doc)
	{
		return doc.Profiles.Keys.Where((string k) => k.StartsWith(LayoutTools.BackupPrefix, StringComparison.Ordinal)).ToArray();
	}

	// ————— 1. 对外暴露:反射派发器按名字找得到、调得动 —————

	[Fact]
	public void BothLayoutMethodsAreExposedToTheRpcDispatcher()
	{
		RpcDispatcher dispatcher = new RpcDispatcher(_api);

		Assert.True(dispatcher.Has("layout_v2_get"), "layout_v2_get 没被派发器收进来");
		Assert.True(dispatcher.Has("layout_v2_apply"), "layout_v2_apply 没被派发器收进来");
		// 名字必须全小写(RpcDispatcher.IsExposedName):大写开头的会被静默跳过,所以这里显式钉一次
		Assert.Contains("layout_v2_get", dispatcher.MethodNames);
		Assert.Contains("layout_v2_apply", dispatcher.MethodNames);

		// 经派发器调一次(桥的 /rpc 与插件 ctx.Launcher 走的就是这条):拿到的是字符串结果
		object? viaDispatcher = dispatcher.Call("layout_v2_get", new PyDict());
		Assert.IsType<string>(viaDispatcher);
	}

	// ————— 2. 与 AI 工具同一份语义(逐字相同),不是第二套实现 —————

	[Fact]
	public void GetIsByteIdenticalToTheAiToolResult()
	{
		RpcDispatcher dispatcher = new RpcDispatcher(_api);

		Assert.Equal(LayoutTools.Get(), (string)dispatcher.Call("layout_v2_get", new PyDict())!);
		Assert.Equal(LayoutTools.Get(), (string)_api.layout_v2_get()!);
	}

	[Fact]
	public void ApplyFailureIsByteIdenticalToTheAiToolResultAndWritesNothing()
	{
		UseDir();
		PyDict args = PyDict.Of("profile", "没有这个方案");

		string viaRpc = (string)_api.layout_v2_apply(args)!;

		Assert.Equal(LayoutTools.Apply(args), viaRpc);
		Assert.Contains("没有这个方案", viaRpc, StringComparison.Ordinal);
		// 失败不该落盘:没有任何"AI 备份"方案
		Assert.Empty(BackupProfiles(LayoutStoreV2.Load()));
	}

	[Fact]
	public void ApplyWithNothingRecognizedExplainsItselfInsteadOfWriting()
	{
		UseDir();
		string text = (string)_api.layout_v2_apply(new PyDict())!;

		Assert.Contains("没有识别到要修改的内容", text, StringComparison.Ordinal);
		Assert.Empty(BackupProfiles(LayoutStoreV2.Load()));
	}

	// ————— 3. 真能读能改:落盘 + 自动备份 + get 立刻反映 —————

	[Fact]
	public void ApplyWritesTheLayoutBacksItUpAndGetReflectsIt()
	{
		UseDir();
		string before = (string)_api.layout_v2_get()!;
		// 全新数据目录里可能还没有 launch 这一页(默认文档是空的),所以按"没有就是空模式"取
		LayoutBody beforeBody = LayoutStoreV2.Load().Layout;
		bool beforeHasLaunch = beforeBody.Pages.TryGetValue("launch", out PageLayoutV2? beforePage);
		string beforeMode = beforeHasLaunch ? beforePage!.Mode : "";

		string result = (string)_api.layout_v2_apply(PyDict.Of("page", "launch", "mode", "grid", "cols", 3L))!;

		Assert.Contains("已更新布局", result, StringComparison.Ordinal);
		string[] backups = BackupProfiles(LayoutStoreV2.Load());
		Assert.Single(backups);
		Assert.Contains(backups[0], result, StringComparison.Ordinal); // 返回值里点了备份方案名
		Assert.NotEqual(before, (string)_api.layout_v2_get()!);        // 只读口立刻看到新布局
		// 真落盘了
		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.True(doc.Layout.Pages.TryGetValue("launch", out PageLayoutV2? nowPage), "apply 之后 launch 页应该存在");
		Assert.Equal(LayoutSchema.ModeGrid, nowPage!.Mode);
		// 备份那一份存的是**改动前**的布局(所以"切回去"才成立):改动前没有 launch 页,备份里也就不该有
		bool backupHasLaunch = doc.Profiles[backups[0]].Pages.TryGetValue("launch", out PageLayoutV2? backupPage);
		Assert.Equal(beforeHasLaunch, backupHasLaunch);
		if (backupHasLaunch)
		{
			Assert.Equal(beforeMode, backupPage!.Mode);
		}
		Assert.NotEqual(beforeMode, nowPage.Mode);
	}

	// ————— 4. 插件那条路没被关掉(LauncherDeny 是按源码断言的门禁,与 LayoutNativeBlockTests 同一手法) —————

	[Fact]
	public void PluginLauncherGateDoesNotDenyTheLayoutRpc()
	{
		string src = RepoFile("src/TipCL/Core/Plugins/PluginHostApi.cs");
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
