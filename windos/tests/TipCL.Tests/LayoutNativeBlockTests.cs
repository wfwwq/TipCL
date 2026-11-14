using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TipCL.Plugins;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// P3:把插件 kind <c>block</c> 的**弱证据**(代码审查级)变成机器证据。
/// 能验的部分:
/// <list type="number">
/// <item>注册表层 —— <see cref="PluginUiRegistry"/> 就是界面读「原生插件入口」的唯一入口,
/// 按 <c>(plugin, kind, id)</c> 三元组查找/释放(这条路径不碰 WPF,见类型注释)。</item>
/// <item>白名单一致性 —— 插件 kind 白名单**三处同值**(<c>PluginManifest.UiKinds</c>、
/// <c>PluginHostApi</c> 的 <c>ui.register</c>、<c>PluginManager</c> 的 view 校验),漏一处就会
/// 「文档里能写、运行时被拒」;这里按源码文本核对三处集合完全一致且都含 block
/// (与既有 StaticInitOrderTests 同一套「按源码断言」的做法)。</item>
/// <item>接线事实 —— <c>DotNetPluginContext.RegisterBlock</c> 确实以 <c>Kind = "block"</c> 注册进注册表。</item>
/// </list>
/// 验不了的部分(如实记账,不在这里假装覆盖):<c>DotNetPluginContext.RegisterBlock</c> 的签名带
/// <c>Func&lt;FrameworkElement&gt;</c> 且要先过 <c>PluginManager.RequirePermission</c>,net8 测试宿主既加载不了
/// net48 的 WPF 程序集、也没有真实插件上下文 → 「真插件注册 → 布局页渲染 → 卸载后显示不可用」这段
/// 只能真机/人工验(见 P3 报告「还差哪一段」)。
/// 测试用例自己收拾现场:注册表是进程级静态状态,所有用例都用唯一插件 id 并在 finally 里清干净。
/// </summary>
public class LayoutNativeBlockTests
{
	private const string TestPlugin = "p3-native-block-test";

	private const string TestKind = "block";

	private const string TestId = "hello";

	private static NativeUiEntry NewEntry(string plugin, string kind, string id, string label)
	{
		return new NativeUiEntry
		{
			Plugin = plugin,
			Kind = kind,
			Id = id,
			Label = label
		};
	}

	// ————— 1. 注册 → 按 (plugin, "block", id) 找得到 → 释放后找不到 —————

	[Fact]
	public void BlockEntryIsFoundByPluginKindAndIdThenGoneAfterDispose()
	{
		PluginUiRegistry.UnregisterPlugin(TestPlugin);
		try
		{
			NativeUiEntry entry = NewEntry(TestPlugin, TestKind, TestId, "Hello block");
			Assert.Null(PluginUiRegistry.Find(TestPlugin, TestKind, TestId));

			int changed = 0;
			Action onChanged = delegate
			{
				changed++;
			};
			PluginUiRegistry.Changed += onChanged;
			IDisposable handle;
			try
			{
				handle = PluginUiRegistry.Register(entry);
			}
			finally
			{
				PluginUiRegistry.Changed -= onChanged;
			}

			try
			{
				// 三元组命中,且拿到的就是注册的那个实例
				NativeUiEntry? found = PluginUiRegistry.Find(TestPlugin, TestKind, TestId);
				Assert.NotNull(found);
				Assert.Same(entry, found);
				Assert.Equal("Hello block", found!.Label);
				Assert.Contains(PluginUiRegistry.Entries, (NativeUiEntry e) => ReferenceEquals(e, entry));
				Assert.Equal(1, changed); // 注册触发一次 Changed

				// 三元组任一项不对就不该命中(kind 是键的一部分:block 不会与 page/dashCard/aiView 混)
				Assert.Null(PluginUiRegistry.Find(TestPlugin, "page", TestId));
				Assert.Null(PluginUiRegistry.Find(TestPlugin, "dashCard", TestId));
				Assert.Null(PluginUiRegistry.Find(TestPlugin, "aiView", TestId));
				Assert.Null(PluginUiRegistry.Find(TestPlugin, TestKind, "other-id"));
				Assert.Null(PluginUiRegistry.Find("other-plugin", TestKind, TestId));
			}
			finally
			{
				handle.Dispose();
			}

			// 释放后:查找落空,快照里也没有了
			Assert.Null(PluginUiRegistry.Find(TestPlugin, TestKind, TestId));
			Assert.DoesNotContain(PluginUiRegistry.Entries, (NativeUiEntry e) => e.Plugin == TestPlugin);
		}
		finally
		{
			PluginUiRegistry.UnregisterPlugin(TestPlugin);
		}
	}

	// ————— 2. 同三元组重注册 = 替换,而不是塞两条;释放旧句柄不该带走新条目 —————

	[Fact]
	public void RegisteringSameTripleReplacesAndDisposingOldHandleKeepsTheNewOne()
	{
		PluginUiRegistry.UnregisterPlugin(TestPlugin);
		try
		{
			NativeUiEntry first = NewEntry(TestPlugin, TestKind, TestId, "first");
			NativeUiEntry second = NewEntry(TestPlugin, TestKind, TestId, "second");
			using (IDisposable h1 = PluginUiRegistry.Register(first))
			{
				using IDisposable h2 = PluginUiRegistry.Register(second);

				Assert.Single(PluginUiRegistry.Entries, (NativeUiEntry e) => e.Plugin == TestPlugin);
				Assert.Same(second, PluginUiRegistry.Find(TestPlugin, TestKind, TestId));

				// 释放**被替换掉的**旧句柄:不该把新条目一起删掉(插件重载时会走这条路径)
				h1.Dispose();
				Assert.Same(second, PluginUiRegistry.Find(TestPlugin, TestKind, TestId));
			}
			Assert.Null(PluginUiRegistry.Find(TestPlugin, TestKind, TestId));
		}
		finally
		{
			PluginUiRegistry.UnregisterPlugin(TestPlugin);
		}
	}

	// ————— 3. 插件卸载:该插件的全部入口(含 block)一起消失,不误伤别的插件 —————

	[Fact]
	public void UnregisterPluginDropsItsBlockEntryAndLeavesOtherPluginsAlone()
	{
		const string Other = "p3-other-plugin-test";
		PluginUiRegistry.UnregisterPlugin(TestPlugin);
		PluginUiRegistry.UnregisterPlugin(Other);
		try
		{
			using (PluginUiRegistry.Register(NewEntry(TestPlugin, TestKind, TestId, "block")))
			using (PluginUiRegistry.Register(NewEntry(TestPlugin, "page", "p1", "page")))
			using (PluginUiRegistry.Register(NewEntry(Other, TestKind, TestId, "other block")))
			{
				Assert.NotNull(PluginUiRegistry.Find(TestPlugin, TestKind, TestId));
				Assert.NotNull(PluginUiRegistry.Find(TestPlugin, "page", "p1"));
				Assert.NotNull(PluginUiRegistry.Find(Other, TestKind, TestId));

				PluginUiRegistry.UnregisterPlugin(TestPlugin);

				Assert.Null(PluginUiRegistry.Find(TestPlugin, TestKind, TestId));
				Assert.Null(PluginUiRegistry.Find(TestPlugin, "page", "p1"));
				Assert.DoesNotContain(PluginUiRegistry.Entries, (NativeUiEntry e) => e.Plugin == TestPlugin);
				// 别的插件不受影响 —— 布局块靠这个语义「插件卸载后块保留、只显示不可用」
				Assert.NotNull(PluginUiRegistry.Find(Other, TestKind, TestId));
			}
		}
		finally
		{
			PluginUiRegistry.UnregisterPlugin(TestPlugin);
			PluginUiRegistry.UnregisterPlugin(Other);
		}
	}

	// ————— 4. 白名单三处同值且含 block(D-31 记的「改一处必须同步三处」) —————

	private static string Src(string rel)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir!.FullName, rel));
	}

	/// <summary>
	/// 从锚点之后抓第一个字符串字面量集合(<c>HashSet&lt;string&gt; { ... }</c> 或 <c>new[] { ... }</c>)。
	/// <paramref name="afterMarker"/> 用来跳过锚点之后的无关花括号(例如 <c>case "ui.register": {</c> 那层块),
	/// 先定位到它再取花括号。
	/// </summary>
	private static HashSet<string> LiteralsAfter(string src, string anchor, string? afterMarker = null)
	{
		int at = src.IndexOf(anchor, StringComparison.Ordinal);
		Assert.True(at > 0, "找不到锚点:" + anchor);
		if (afterMarker != null)
		{
			int marker = src.IndexOf(afterMarker, at, StringComparison.Ordinal);
			Assert.True(marker > at, "锚点后找不到标记:" + afterMarker);
			at = marker;
		}
		int open = src.IndexOf('{', at);
		int close = src.IndexOf('}', open);
		Assert.True(open > 0 && close > open, "锚点后找不到字面量集合:" + anchor);
		return QuotedLiterals(src.Substring(open + 1, close - open - 1));
	}

	/// <summary>抓锚点所在行里的 <c>kind == "x"</c> 比较链(该行还可能有 <c>spec.Get("view")</c> 之类,不能整行收字面量)。</summary>
	private static HashSet<string> KindComparisonsOnLineOf(string src, string anchor)
	{
		int at = src.IndexOf(anchor, StringComparison.Ordinal);
		Assert.True(at > 0, "找不到锚点:" + anchor);
		int start = src.LastIndexOf('\n', at) + 1;
		int end = src.IndexOf('\n', at);
		if (end < 0)
		{
			end = src.Length;
		}
		string line = src.Substring(start, end - start);
		HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
		foreach (Match m in Regex.Matches(line, "kind\\s*==\\s*\"([A-Za-z][A-Za-z0-9]*)\""))
		{
			set.Add(m.Groups[1].Value);
		}
		Assert.NotEmpty(set);
		return set;
	}

	private static HashSet<string> QuotedLiterals(string body)
	{
		HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
		foreach (Match m in Regex.Matches(body, "\"([A-Za-z][A-Za-z0-9]*)\""))
		{
			set.Add(m.Groups[1].Value);
		}
		Assert.NotEmpty(set);
		return set;
	}

	private static string Join(HashSet<string> set)
	{
		return string.Join(",", set.OrderBy<string, string>((string s) => s, StringComparer.Ordinal));
	}

	[Fact]
	public void PluginKindWhitelistContainsBlockAndItsCopiesStayInSync()
	{
		// 全量白名单两处必须同值:漏一处就是「清单里写了 block、运行时被拒」或反之(D-31 记的坑)
		HashSet<string> manifest = LiteralsAfter(Src("src/TipCL/Core/Plugins/PluginManifest.cs"), "UiKinds");
		HashSet<string> hostApi = LiteralsAfter(Src("src/TipCL/Core/Plugins/PluginHostApi.cs"), "ui.register", "new[");
		Assert.Contains("block", manifest);
		Assert.Contains("block", hostApi);
		Assert.True(manifest.SetEquals(hostApi),
			"PluginManifest.UiKinds 与 PluginHostApi 的 kind 白名单不一致:[" + Join(manifest) + "] vs [" + Join(hostApi) + "]");

		// 第三处是 PluginManager 的「必须声明 view 的 kind」比较链:它是白名单的子集,且必须含 block
		HashSet<string> mustHaveView = KindComparisonsOnLineOf(Src("src/TipCL/Core/Plugins/PluginManager.cs"), "dashCard");
		Assert.Contains("block", mustHaveView);
		Assert.True(manifest.IsSupersetOf(mustHaveView),
			"PluginManager 的 view 校验集合不在白名单内:[" + Join(mustHaveView) + "] vs [" + Join(manifest) + "]");
		// 有意为之的差异只有一个:action 不需要 view
		Assert.Equal(new string[1] { "action" }, manifest.Except(mustHaveView).ToArray());
	}

	// ————— 5. 接线事实:原生插件的 block 入口以 Kind = "block" 进注册表 —————

	[Fact]
	public void NativePluginContextRegistersBlockKindThroughTheRegistry()
	{
		string ctx = Src("src/TipCL/Core/Plugins/DotNetPluginContext.cs");
		int at = ctx.IndexOf("RegisterBlock", StringComparison.Ordinal);
		Assert.True(at > 0, "DotNetPluginContext 里没有 RegisterBlock");
		string body = ctx.Substring(at, Math.Min(900, ctx.Length - at));
		Assert.Contains("PluginUiRegistry.Register", body);
		Assert.Matches("Kind\\s*=\\s*\"block\"", body);
		// 释放路径也要走注册表的注销(否则卸载插件后入口会留在列表里)
		Assert.Contains("Track(d.Dispose", body);
	}
}
