// R32-MWG:锁住移植过来的 Layout v2 自建区块层(WidgetRegistry / WidgetCommon / WidgetUI /
// BasicWidgets / ShortcutWidget / PlaytimeWidget / DangerousWidgets / PluginBlocks),mac(Avalonia)树落地件。
//   底稿(逐行) = Linux 同层落地件 wt-linux-widgets @d6bebcb 的 tests/TipCL.Tests/App/WidgetTests.cs
//                 (938 行;正文实测 [Fact] 25 条 —— 与 progress/R32-PLAN 记的「Linux WidgetTests 25 条」一致)。
//                 本文件逐方法名 1:1 对齐这 25 条,另加 2 条 mac 专有守卫(共 27 条):count 口径 = 每个
//                 [Fact] 计 1;本文件不含 [Theory]。
//
// 为什么需要这些用例:这 8 个文件里只有 WidgetCommon 是纯模型门禁(可在无 Avalonia 的宿主直接跑),
// 其余都造 Avalonia 控件 —— 但本仓 src/TipCL.App/TipCL.App.csproj 里有 InternalsVisibleTo=TipCL.Tests,
// 所以 internal 渲染器也能直接断言。覆盖点分四类:
//   1 纯模型门禁:settings 取值只认 JSON 类型、危险类型开关只认真布尔、web 地址准入、button 动作白名单;
//   2 注册与查找:WidgetRegistry 的生命周期(幂等注册 / 干净注销)、CanBuild 只认领 custom:/plugin:、
//     Register 之后 LayoutEngine.IsUnavailable 真的转为「可用」(证明这根口子接得上);
//   3 渲染语义:每类区块的关键输出(标题 / 引导语 / 时间格式 / 截断 / 占位),以及「查不到定义 / 插件卸载
//     时块保留、只换占位、绝不删数据」这条铁律;
//   4 降级守卫:DangerousWidgets 的 web 块在 mac 上恒走「当前系统不能内嵌网页 + 在浏览器打开」,
//     渲染树里**不准**出现 WebView/HwndHost 类型;源码里**不准**出现 WebView2 专属类名
//     (GOAL.md 34 / D-154:mac 不内嵌浏览器)。
//
// 与 Linux 底稿的机械差异(断言 / 方法名 / 数字一字未改):
//   T-1 文件头:Linux 底稿的中文说明 -> 本头注(补 mac 侧差异条目)。
//   T-2 namespace:底稿的 TipCL.Tests.App -> mac 的 TipCL.Tests(本目录既有测试一致)。
//   T-3 using 增减:去掉底稿的 TipCL.App.Pages.Layout(底稿路径 Pages/Layout/Widgets/*)
//       -> 加 TipCL.Pages(mac 是扁平 Pages/,R32-D7);加 TipCL.Services(底稿用 I18nCore.Tr,
//       mac 的词表门面是 TipCL.Services.I18n 的 L(key));Avalonia.Controls 原样保留,
//       另加 Avalonia.Layout(第 26 条断言 HorizontalAlignment.Stretch)。
//   T-4 无头会话门面:TestApp.Run( -> AvaloniaSession.Run(mac 的无头会话类,同测试程序集
//       AvaloniaSession.cs;Linux 侧对等物 = tests/TipCL.Tests/App/TestApp.cs)。
//   T-5 临时目录:Path.GetTempPath() -> TestHome.Root(mac 红线:测试临时文件必须落在仓库内,
//       R32-BRIEF §4;仍各自拼 Guid 子目录,隔离语义与实际路径长度无关)。
//   T-6 词表现算:T(key) = I18nCore.Tr(key) -> I18n.L(key)(沿用 mac 既有用例 AskCardTests 的做法:
//       期望文案不写死中文,避免词表改动后假绿);带参数版 T(key, args) = I18n.L(key, args)
//       (底稿用 Py.Fmt(T(key), args);mac 的 I18n.L 自带 string.Format 重载,语义等价)。
//   T-7 源码级守卫的路径与符号:Linux 底稿的 8 个路径是 Pages/Layout/Widgets/*,mac 是扁平的
//       Pages/*;底稿禁止 TipCL.Services 的守卫在 mac 上不成立(mac 本来就有这个命名空间),
//       改为 mac 的对应红线守卫:命名空间必须是扁平的 TipCL.Pages、树里不许冒出 Pages/Layout
//       或 Shell/ 子目录(R32-D7)。WebView 类名的禁止清单保留,另加 EditAwareShell / WebShell
//       (mac 上这两个名字只允许出现在注释里)。
//   T-8 类名:加包号前缀 R32Mc,方法名 / 断言 / 数字一字未改。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

public class R32McWidgetTests : IDisposable
{
	// ————————————————————————— 夹具 —————————————————————————

	/// <summary>词表现算:期望文案不写死,避免词表改动后用例变成假绿 / 假红。</summary>
	private static string T(string key)
	{
		return I18n.L(key);
	}

	/// <summary>带参数版(底稿的 Py.Fmt(T(key), args);mac 的 I18n.L 有等价重载)。</summary>
	private static string T(string key, params object?[] args)
	{
		return I18n.L(key, args);
	}

	/// <summary>造一个自建块定义(settings 可空)。</summary>
	private static WidgetDef MakeWidget(string id, string type, string title = "", Dictionary<string, object?>? settings = null)
	{
		return new WidgetDef
		{
			Id = id,
			Type = type,
			Title = title,
			Settings = settings ?? new Dictionary<string, object?>()
		};
	}

	/// <summary>把渲染树里的文本与控件类型全收集出来(断言只看这两样,不依赖可视树布局)。</summary>
	private static void Walk(Control? node, List<string> texts, List<string> types)
	{
		if (node == null)
		{
			return;
		}
		types.Add(node.GetType().FullName ?? node.GetType().Name);
		if (node is TextBlock tb)
		{
			if (!string.IsNullOrEmpty(tb.Text))
			{
				texts.Add(tb.Text!);
			}
			return;
		}
		if (node is Panel panel)
		{
			foreach (Control child in panel.Children)
			{
				Walk(child, texts, types);
			}
			return;
		}
		if (node is ContentControl content)
		{
			if (content.Content is Control inner)
			{
				Walk(inner, texts, types);
			}
			else if (content.Content is string s && s.Length > 0)
			{
				texts.Add(s);
			}
			return;
		}
		if (node is Decorator decorator)
		{
			Walk(decorator.Child, texts, types);
		}
	}

	private static List<string> Texts(Control root)
	{
		List<string> texts = new List<string>();
		Walk(root, texts, new List<string>());
		return texts;
	}

	private static List<string> Types(Control root)
	{
		List<string> texts = new List<string>();
		List<string> types = new List<string>();
		Walk(root, texts, types);
		return types;
	}

	private static string Dump(List<string> items)
	{
		return "[" + string.Join(" | ", items) + "]";
	}

	private static void AssertHasText(List<string> texts, string expected)
	{
		Assert.True(texts.Exists(t => string.Equals(t, expected, StringComparison.Ordinal)),
			"期望文本 <" + expected + "> 不在渲染结果里:" + Dump(texts));
	}

	/// <summary>按顺序逐元素比较字符串序列(不用 Assert.Equal 的重载,避免歧义 / 引用相等)。</summary>
	private static void AssertSameSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
	{
		Assert.Equal(expected.Count, actual.Count);
		for (int i = 0; i < expected.Count; i++)
		{
			Assert.Equal(expected[i], actual[i]);
		}
	}

	/// <summary>渲染树里是否出现网页宿主 / 原生窗口宿主类型(mac 上必须恒 false)。</summary>
	private static bool HasWebHost(List<string> types)
	{
		return types.Exists(t =>
			t.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
			t.Contains("HwndHost", StringComparison.OrdinalIgnoreCase));
	}

	private static void AssertNoWebHost(Control root)
	{
		List<string> types = Types(root);
		Assert.False(HasWebHost(types), "渲染树里出现了网页宿主类型:" + Dump(types));
	}

	// ————— 数据根隔离(mac 红线:临时目录必须落在仓库内 -> TestHome.Root) —————

	private static string NewDir(string tag)
	{
		return Path.Combine(TestHome.Root, "tipcl_widgets_" + tag + "_" + Guid.NewGuid().ToString("N"));
	}

	private static void UseDir(string dir)
	{
		Directory.CreateDirectory(dir);
		LayoutStoreV2.DirOverride = dir;
		LayoutStoreV2.V1Bridge = null;
		LayoutStoreV2.ResetCache();
	}

	private static void ReleaseDir(string dir)
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		LayoutStoreV2.ResetCache();
		try
		{
			Directory.Delete(dir, recursive: true);
		}
		catch (Exception)
		{
			// 收尾尽力而为,不影响断言
		}
	}

	public void Dispose()
	{
		// 收尾:注销工厂 + 清空注册表(布局存储的 DirOverride 由各用例自己 finally 还原)
		WidgetRegistry.Unregister();
		LayoutContentRegistry.Clear();
	}

	// ————————————————————————— 1 纯模型门禁 —————————————————————————

	[Fact]
	public void WidgetCommon_SettingsReaders_OnlyAcceptMatchingJsonTypes()
	{
		Dictionary<string, object?> s = new Dictionary<string, object?>
		{
			["text"] = "hello",
			["flagTrue"] = true,
			["flagStr"] = "true",
			["numLong"] = 7L,
			["numInt"] = 8,
			["numDouble"] = 1.5,
			["nan"] = double.NaN,
			["inf"] = double.PositiveInfinity
		};

		// Str:只认 JSON 字符串
		Assert.Equal("hello", WidgetCommon.Str(s, "text", "d"));
		Assert.Equal("d", WidgetCommon.Str(s, "numLong", "d"));
		Assert.Equal("d", WidgetCommon.Str(s, "flagTrue", "d"));
		Assert.Equal("d", WidgetCommon.Str(s, "missing", "d"));
		Assert.Equal("d", WidgetCommon.Str(null, "text", "d"));

		// Flag:只认 JSON 布尔
		Assert.True(WidgetCommon.Flag(s, "flagTrue", false));
		Assert.False(WidgetCommon.Flag(s, "flagStr", false));
		Assert.True(WidgetCommon.Flag(s, "missing", true));

		// Num:整数 / 浮点都收,NaN / Infinity / 非数字一律退回默认
		Assert.Equal(7.0, WidgetCommon.Num(s, "numLong", -1.0));
		Assert.Equal(8.0, WidgetCommon.Num(s, "numInt", -1.0));
		Assert.Equal(1.5, WidgetCommon.Num(s, "numDouble", -1.0));
		Assert.Equal(-1.0, WidgetCommon.Num(s, "nan", -1.0));
		Assert.Equal(-1.0, WidgetCommon.Num(s, "inf", -1.0));
		Assert.Equal(-1.0, WidgetCommon.Num(s, "text", -1.0));
		Assert.Equal(3.0, WidgetCommon.Num(null, "x", 3.0));

		Assert.Equal(2048, WidgetCommon.UrlMaxLength);
	}

	[Fact]
	public void WidgetCommon_IsEnabled_RequiresDangerousTypeAndExplicitTrue()
	{
		WidgetDef web = MakeWidget("w-gate", LayoutSchema.WidgetWeb);

		// Q5 默认关闭
		Assert.False(WidgetCommon.IsEnabled(web));

		// 字符串 "true" 不算数(必须是 JSON 布尔)
		web.Settings[WidgetCommon.EnabledKey] = "true";
		Assert.False(WidgetCommon.IsEnabled(web));

		web.Settings[WidgetCommon.EnabledKey] = true;
		Assert.True(WidgetCommon.IsEnabled(web));

		// 非危险类型上写开关无效
		WidgetDef note = MakeWidget("w-note-gate", LayoutSchema.WidgetNote);
		note.Settings[WidgetCommon.EnabledKey] = true;
		Assert.False(WidgetCommon.IsEnabled(note));

		Assert.False(WidgetCommon.IsEnabled(null));
	}

	[Fact]
	public void WidgetCommon_TryAcceptWebUrl_AcceptsHttpsAndNormalizes()
	{
		Assert.True(WidgetCommon.TryAcceptWebUrl("https://example.com/farm", out string url, out string reason));
		Assert.Equal("https://example.com/farm", url);
		Assert.Equal("", reason);

		// 前后空白先 Trim,再交给 Uri 归一
		Assert.True(WidgetCommon.TryAcceptWebUrl("  https://example.com  ", out string trimmed, out _));
		Assert.Equal("https://example.com/", trimmed);

		Assert.True(WidgetCommon.TryAcceptWebUrl("https://example.com:8443/a?b=c#d", out string complex, out _));
		Assert.Equal("https://example.com:8443/a?b=c#d", complex);
	}

	[Fact]
	public void WidgetCommon_TryAcceptWebUrl_RejectsUnsafeAndMalformedInput()
	{
		// 断言只认「false + 归一结果为空 + 中文原因」,不猜内部状态
		void AssertRejected(string? input, string expectedReason)
		{
			Assert.False(WidgetCommon.TryAcceptWebUrl(input, out string normalized, out string reason),
				"本应被拒绝的地址被接受了:<" + (input ?? "<null>") + ">");
			Assert.Equal("", normalized);
			Assert.Equal(expectedReason, reason);
		}

		AssertRejected("", T("没有填写网页地址"));
		AssertRejected("   ", T("没有填写网页地址"));
		AssertRejected(null, T("没有填写网页地址"));

		// 没有 "://" 的一律先撞「必须以 https:// 开头」(所以 javascript: 也走这条)
		AssertRejected("example.com", T("网页地址必须以 https:// 开头"));
		AssertRejected("javascript:alert(1)", T("网页地址必须以 https:// 开头"));
		AssertRejected("data:text/html,<b>x</b>", T("网页地址必须以 https:// 开头"));

		// 带 "://" 的本地文件 / 脚本方案被单独点名
		AssertRejected("file:///etc/passwd", T("只允许 https 网页,禁止本地文件与脚本地址"));
		AssertRejected("data://x", T("只允许 https 网页,禁止本地文件与脚本地址"));

		// 非 https 的绝对地址(只有 http 会走到最后一条协议判定)
		AssertRejected("http://example.com", T("只允许 https 网页(http 与其它协议一律拒绝)"));

		// 超长
		AssertRejected("https://" + new string('a', 2100), T("网页地址太长"));

		// 缺主机 / 解析不出来:只断言被拒 + 原因非空(具体原因取决于 Uri 解析细节)
		foreach (string malformed in new[] { "https://", "https://   " })
		{
			Assert.False(WidgetCommon.TryAcceptWebUrl(malformed, out string normalized, out string reason),
				"本应被拒绝:<" + malformed + ">");
			Assert.Equal("", normalized);
			Assert.True(reason.Length > 0, "拒绝时必须给出原因:<" + malformed + ">");
		}
	}

	[Fact]
	public void WidgetCommon_TryParseAction_AcceptsWhitelistedInternalActions()
	{
		Assert.True(WidgetCommon.TryParseAction("  page:launch  ", out string kind, out string arg1, out string arg2, out string reason));
		Assert.Equal("page", kind);
		Assert.Equal("launch", arg1);
		Assert.Equal("", arg2);
		Assert.Equal("", reason);

		Assert.True(WidgetCommon.TryParseAction("folder:data", out kind, out arg1, out _, out _));
		Assert.Equal("folder", kind);
		Assert.Equal("data", arg1);

		// 目录名不区分大小写
		Assert.True(WidgetCommon.TryParseAction("folder:DATA", out kind, out arg1, out _, out _));
		Assert.Equal("folder", kind);
		Assert.Equal("DATA", arg1);

		Assert.True(WidgetCommon.TryParseAction("rpc:list_instances", out kind, out arg1, out _, out _));
		Assert.Equal("rpc", kind);
		Assert.Equal("list_instances", arg1);

		Assert.True(WidgetCommon.TryParseAction("launch:MyWorld", out kind, out arg1, out arg2, out _));
		Assert.Equal("launch", kind);
		Assert.Equal("MyWorld", arg1);
		Assert.Equal("", arg2);

		Assert.True(WidgetCommon.TryParseAction("launch:MyWorld|1.20.1", out kind, out arg1, out arg2, out _));
		Assert.Equal("launch", kind);
		Assert.Equal("MyWorld", arg1);
		Assert.Equal("1.20.1", arg2);

		// 版本留空("x|")在冻结件里也是允许的
		Assert.True(WidgetCommon.TryParseAction("launch:MyWorld|", out kind, out arg1, out arg2, out _));
		Assert.Equal("MyWorld", arg1);
		Assert.Equal("", arg2);
	}

	[Fact]
	public void WidgetCommon_TryParseAction_RejectsEverythingElse()
	{
		// 只断言「被拒 + 原因」,不断言 kind(冻结件在 launch 分支里会先填 kind 再判空)
		void AssertRejected(string? input, string expectedReason)
		{
			Assert.False(WidgetCommon.TryParseAction(input, out _, out _, out _, out string reason),
				"本应被拒绝的动作被接受了:<" + (input ?? "<null>") + ">");
			Assert.Equal(expectedReason, reason);
		}

		AssertRejected("", T("没有填写按钮动作"));
		AssertRejected("   ", T("没有填写按钮动作"));
		AssertRejected(null, T("没有填写按钮动作"));

		AssertRejected("page", T("按钮动作要写成 类型:参数(例如 page:launch)"));
		AssertRejected(":launch", T("按钮动作要写成 类型:参数(例如 page:launch)"));

		// 任意命令被点名拒绝
		AssertRejected("shell:rm -rf /", T("不支持的按钮动作:{0}(只允许 page / launch / folder / rpc)", "shell"));

		AssertRejected("page:", T("按钮动作缺少参数"));
		AssertRejected("page:   ", T("按钮动作缺少参数"));
		AssertRejected("rpc:", T("按钮动作缺少参数"));

		// folder 只允许 data,任意路径一律拒绝
		AssertRejected("folder:other", T("只允许打开启动器数据目录(folder:data)"));
		AssertRejected(@"folder:C:\Windows", T("只允许打开启动器数据目录(folder:data)"));

		// rpc 白名单
		AssertRejected("rpc:launch_game", T("这个 RPC 不在白名单里:{0}", "launch_game"));
	}

	// ————————————————————————— 2 注册与查找 —————————————————————————

	[Fact]
	public void WidgetRegistry_Lifecycle_RegisterIsIdempotent_UnregisterIsClean()
	{
		WidgetRegistry.Unregister(); // 先清干净,免得上一条用例留了残留
		Assert.False(WidgetRegistry.Registered);

		int before = LayoutContentRegistry.Count;

		Assert.True(WidgetRegistry.EnsureRegistered(), "首次注册应返回 true");
		Assert.True(WidgetRegistry.Registered);
		Assert.Equal(before + 1, LayoutContentRegistry.Count);

		// 幂等:第二次不再注册
		Assert.False(WidgetRegistry.EnsureRegistered(), "重复注册应返回 false");
		Assert.Equal(before + 1, LayoutContentRegistry.Count);

		WidgetRegistry.Unregister();
		Assert.False(WidgetRegistry.Registered);
		Assert.Equal(before, LayoutContentRegistry.Count);

		// 再注销无副作用
		WidgetRegistry.Unregister();
		Assert.Equal(before, LayoutContentRegistry.Count);
	}

	[Fact]
	public void WidgetRegistry_CanBuild_OnlyClaimsCustomAndPlugin()
	{
		WidgetRegistry registry = new WidgetRegistry();

		Assert.False(registry.CanBuild("hero"));
		Assert.False(registry.CanBuild(""));
		Assert.False(registry.CanBuild(null!));

		Assert.True(registry.CanBuild("custom:w1"));
		Assert.True(registry.CanBuild("plugin:some-plugin:card"));

		// 前缀就算后面没内容(Classify 只看前缀)也归本工厂;能不能造出内容是另一回事
		Assert.True(registry.CanBuild("custom:"), "custom: 前缀一律归本工厂");
	}

	[Fact]
	public void WidgetRegistry_Constants_MatchFrozenWhitelists()
	{
		Assert.Equal("block", WidgetRegistry.PluginKindBlock);

		AssertSameSequence(
			new[]
			{
				LayoutSchema.WidgetNote,
				LayoutSchema.WidgetShortcut,
				LayoutSchema.WidgetClock,
				LayoutSchema.WidgetDivider,
				LayoutSchema.WidgetPlaytime
			},
			WidgetRegistry.SafeTypes);

		// 危险类型不在「可直接添加」白名单里
		Assert.True(Array.IndexOf(WidgetRegistry.SafeTypes, LayoutSchema.WidgetWeb) < 0,
			"web 不该在 SafeTypes 里:" + Dump(WidgetRegistry.SafeTypes.ToList()));
		Assert.True(Array.IndexOf(WidgetRegistry.SafeTypes, LayoutSchema.WidgetButton) < 0,
			"button 不该在 SafeTypes 里:" + Dump(WidgetRegistry.SafeTypes.ToList()));

		Assert.Equal("enabled", WidgetCommon.EnabledKey);
		AssertSameSequence(new[] { "page", "launch", "folder", "rpc" }, WidgetCommon.ActionKinds);
		AssertSameSequence(new[] { "get_launcher_state", "list_instances", "plugins_list" }, WidgetCommon.RpcAllowList);
	}

	[Fact]
	public void WidgetRegistry_Registration_MakesCustomAndPluginBlocksAvailableToEngine()
	{
		WidgetRegistry.Unregister();
		try
		{
			// 没注册工厂时,L0 会把自建 / 插件块判成「不可用」(数据不删,显示占位)
			Assert.True(LayoutEngine.IsUnavailable("custom:nope"));
			Assert.True(LayoutEngine.IsUnavailable("plugin:nope:nope"));

			WidgetRegistry.EnsureRegistered();

			Assert.False(LayoutEngine.IsUnavailable("custom:nope"));
			Assert.False(LayoutEngine.IsUnavailable("plugin:nope:nope"));
			Assert.False(LayoutEngine.IsUnavailable("custom:"));

			// 内置块与工厂无关,永远可用
			Assert.False(LayoutEngine.IsUnavailable("hero"));
		}
		finally
		{
			WidgetRegistry.Unregister();
		}

		Assert.True(LayoutEngine.IsUnavailable("custom:nope"));
	}

	[Fact]
	public void WidgetRegistry_FindWidget_IsNullSafeAndReadsLayoutDoc()
	{
		string dir = NewDir("find");
		UseDir(dir);
		try
		{
			// 空文档:任何查询都返回 null,不抛
			Assert.Null(WidgetRegistry.FindWidgetById("w-missing"));
			Assert.Null(WidgetRegistry.FindWidgetById(""));
			Assert.Null(WidgetRegistry.FindWidget(null!));
			Assert.Null(WidgetRegistry.FindWidget("hero"));
			Assert.Null(WidgetRegistry.FindWidget("custom:w-missing"));
			Assert.Empty(WidgetRegistry.AllWidgets());

			LayoutV2 doc = LayoutV2.CreateDefault();
			doc.Layout.Widgets.Add(MakeWidget("w-one", LayoutSchema.WidgetNote, "便签一"));
			doc.Layout.Widgets.Add(MakeWidget("w-two", LayoutSchema.WidgetClock));
			Assert.True(LayoutStoreV2.Save(doc));

			WidgetDef? found = WidgetRegistry.FindWidgetById("w-one");
			Assert.NotNull(found);
			Assert.Equal("便签一", found!.Title);
			Assert.Equal(LayoutSchema.WidgetNote, found.Type);

			WidgetDef? viaBlock = WidgetRegistry.FindWidget("custom:w-two");
			Assert.NotNull(viaBlock);
			Assert.Equal("w-two", viaBlock!.Id);

			Assert.Equal(2, WidgetRegistry.AllWidgets().Count);
			Assert.Null(WidgetRegistry.FindWidgetById("w-three"));
		}
		finally
		{
			ReleaseDir(dir);
		}
	}

	// ————————————————————————— 3 渲染语义 —————————————————————————

	[Fact]
	public void WidgetRegistry_Build_UnknownCustomWidget_ShowsHonestPlaceholder()
	{
		string dir = NewDir("missing");
		UseDir(dir);
		try
		{
			string blockId = "custom:gone-" + Guid.NewGuid().ToString("N").Substring(0, 8);
			AvaloniaSession.Run(() =>
			{
				Control root = (Control)new WidgetRegistry().Build(blockId, "pages:launch")!;
				Assert.NotNull(root);

				List<string> texts = Texts(root);
				AssertHasText(texts, T("自建区块不可用"));
				AssertHasText(texts, T("布局文档里找不到 {0} 的定义;可以重新添加同名区块。", blockId));
				AssertNoWebHost(root);
			});
		}
		finally
		{
			ReleaseDir(dir);
		}
	}

	[Fact]
	public void WidgetRegistry_Build_PluginBlock_MissingContributionKeepsBlockWithNotice()
	{
		string plugin = "only-plugin-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		AvaloniaSession.Run(() =>
		{
			// 插件被禁用 / 卸载:块**保留**,只换成「不可用」占位(绝不删用户数据)
			Control root = (Control)new WidgetRegistry().Build("plugin:" + plugin + ":card", "pages:launch")!;
			Assert.NotNull(root);

			List<string> texts = Texts(root);
			AssertHasText(texts, T("插件区块不可用"));
			AssertHasText(texts, T("提供它的插件被禁用或卸载了;可以在编辑布局里移除这个区块。"));
			AssertNoWebHost(root);

			// 块 id 不合法(缺第二段)→ 返回 null,交给上层通用占位
			Assert.Null(new WidgetRegistry().Build("plugin:" + plugin, "pages:launch"));
		});
	}

	[Fact]
	public void WidgetRegistry_Build_KnownWidget_RendersByType_FromLayoutDoc()
	{
		string dir = NewDir("known");
		UseDir(dir);
		try
		{
			string noteId = "w-note-" + Guid.NewGuid().ToString("N").Substring(0, 6);
			string clockId = "w-clock-" + Guid.NewGuid().ToString("N").Substring(0, 6);

			LayoutV2 doc = LayoutV2.CreateDefault();
			doc.Layout.Widgets.Add(MakeWidget(noteId, LayoutSchema.WidgetNote, "我的便签",
				new Dictionary<string, object?> { ["text"] = "便签正文" }));
			doc.Layout.Widgets.Add(MakeWidget(clockId, LayoutSchema.WidgetClock,
				settings: new Dictionary<string, object?> { ["format"] = "HH:mm" }));
			Assert.True(LayoutStoreV2.Save(doc));

			AvaloniaSession.Run(() =>
			{
				WidgetRegistry registry = new WidgetRegistry();

				Control note = (Control)registry.Build("custom:" + noteId, "pages:launch")!;
				Assert.NotNull(note);
				List<string> noteTexts = Texts(note);
				AssertHasText(noteTexts, "我的便签");
				AssertHasText(noteTexts, "便签正文");

				Control clock = (Control)registry.Build("custom:" + clockId, "pages:launch")!;
				Assert.NotNull(clock);
				List<string> clockTexts = Texts(clock);
				AssertHasText(clockTexts, T("时钟"));
				Assert.True(clockTexts.Exists(t => Regex.IsMatch(t, @"^\d{2}:\d{2}$")),
					"时钟应渲染出 HH:mm:" + Dump(clockTexts));
			});
		}
		finally
		{
			ReleaseDir(dir);
		}
	}

	[Fact]
	public void WidgetUI_Build_DispatchesByType_UnknownTypeReportedHonestly()
	{
		AvaloniaSession.Run(() =>
		{
			// null 定义
			Control fromNull = WidgetUI.Build(null!, "pages:launch");
			List<string> nullTexts = Texts(fromNull);
			AssertHasText(nullTexts, T("自建区块不可用"));
			AssertHasText(nullTexts, T("布局文档里没有这个区块的定义。"));

			// 未知类型:如实报出类型名,不静默吞掉
			WidgetDef unknown = MakeWidget("w-unknown", "quantum");
			Control fromUnknown = WidgetUI.Build(unknown, "pages:launch");
			List<string> unknownTexts = Texts(fromUnknown);
			AssertHasText(unknownTexts, T("自建区块不可用"));
			AssertHasText(unknownTexts, T("未知的区块类型:{0}", "quantum"));

			// 每类都有渲染器
			Assert.NotNull(BasicWidgets.Note(MakeWidget("w-n", LayoutSchema.WidgetNote)));
			Assert.NotNull(BasicWidgets.Clock(MakeWidget("w-c", LayoutSchema.WidgetClock)));
			Assert.NotNull(BasicWidgets.Divider(MakeWidget("w-d", LayoutSchema.WidgetDivider)));
			Assert.NotNull(ShortcutWidget.Build(MakeWidget("w-s", LayoutSchema.WidgetShortcut)));
			Assert.NotNull(PlaytimeWidget.Build(MakeWidget("w-p", LayoutSchema.WidgetPlaytime)));

			// 类型中文名
			Assert.Equal(T("便签"), WidgetUI.TypeLabel(LayoutSchema.WidgetNote));
			Assert.Equal(T("快捷启动"), WidgetUI.TypeLabel(LayoutSchema.WidgetShortcut));
			Assert.Equal(T("时钟"), WidgetUI.TypeLabel(LayoutSchema.WidgetClock));
			Assert.Equal(T("分隔线"), WidgetUI.TypeLabel(LayoutSchema.WidgetDivider));
			Assert.Equal(T("游戏时长"), WidgetUI.TypeLabel(LayoutSchema.WidgetPlaytime));
			Assert.Equal(T("网页"), WidgetUI.TypeLabel(LayoutSchema.WidgetWeb));
			Assert.Equal(T("按钮"), WidgetUI.TypeLabel(LayoutSchema.WidgetButton));
			Assert.Equal(T("自建区块"), WidgetUI.TypeLabel("quantum"));
			Assert.Equal(T("自建区块"), WidgetUI.TypeLabel(null!));

			// 标题:有用户标题用用户的,否则退回类型名
			Assert.Equal("我的标题", WidgetUI.TitleOf(MakeWidget("w-t", LayoutSchema.WidgetNote, "我的标题")));
			Assert.Equal(T("时钟"), WidgetUI.TitleOf(MakeWidget("w-t2", LayoutSchema.WidgetClock)));

			// 引用了一个文档里没有的定义
			Control missing = WidgetUI.MissingWidget("custom:w-gone");
			AssertHasText(Texts(missing), T("自建区块不可用"));
		});
	}

	[Fact]
	public void BasicWidgets_Note_EmptyGuides_LongTextTruncated()
	{
		AvaloniaSession.Run(() =>
		{
			// 空便签给引导
			Control empty = BasicWidgets.Note(MakeWidget("w-note1", LayoutSchema.WidgetNote));
			AssertHasText(Texts(empty), T("在布局 JSON 里给这个区块填 settings.text 即可写便签。"));

			// 全空白同样算空
			Control blank = BasicWidgets.Note(MakeWidget("w-note2", LayoutSchema.WidgetNote,
				settings: new Dictionary<string, object?> { ["text"] = "   " }));
			AssertHasText(Texts(blank), T("在布局 JSON 里给这个区块填 settings.text 即可写便签。"));

			// 超长截断成 4000 字符 + "..."
			Control longNote = BasicWidgets.Note(MakeWidget("w-note3", LayoutSchema.WidgetNote,
				settings: new Dictionary<string, object?> { ["text"] = new string('あ', 5000) }));
			List<string> texts = Texts(longNote);
			Assert.True(texts.Exists(t => t.Length == 4003 && t.EndsWith("...", StringComparison.Ordinal)),
				"便签应截断成 4000 字符 + \"...\":" + Dump(texts.Select(t => t.Length.ToString()).ToList()));
		});
	}

	[Fact]
	public void BasicWidgets_Clock_ShowsTimeAndSurvivesBadFormat()
	{
		AvaloniaSession.Run(() =>
		{
			Control clock = BasicWidgets.Clock(MakeWidget("w-clock", LayoutSchema.WidgetClock,
				settings: new Dictionary<string, object?> { ["format"] = "HH:mm" }));
			List<string> texts = Texts(clock);
			AssertHasText(texts, T("时钟"));
			Assert.True(texts.Exists(t => Regex.IsMatch(t, @"^\d{2}:\d{2}$")), "应是 HH:mm:" + Dump(texts));

			// 自定义格式
			Control date = BasicWidgets.Clock(MakeWidget("w-clock2", LayoutSchema.WidgetClock,
				settings: new Dictionary<string, object?> { ["format"] = "yyyy-MM-dd" }));
			List<string> dateTexts = Texts(date);
			Assert.True(dateTexts.Exists(t => Regex.IsMatch(t, @"^\d{4}-\d{2}-\d{2}$")),
				"应渲染 yyyy-MM-dd:" + Dump(dateTexts));

			// 超长格式串:退回 HH:mm,不抛
			Control tooLong = BasicWidgets.Clock(MakeWidget("w-clock3", LayoutSchema.WidgetClock,
				settings: new Dictionary<string, object?> { ["format"] = new string('y', 40) }));
			Assert.True(Texts(tooLong).Exists(t => Regex.IsMatch(t, @"^\d{2}:\d{2}$")),
				"超长格式应退回 HH:mm:" + Dump(Texts(tooLong)));

			// 非法格式串:绝不因为用户输入抛异常
			Control bad = BasicWidgets.Clock(MakeWidget("w-clock4", LayoutSchema.WidgetClock,
				settings: new Dictionary<string, object?> { ["format"] = "'" }));
			AssertHasText(Texts(bad), T("时钟"));
		});
	}

	[Fact]
	public void BasicWidgets_Divider_RendersSeparator()
	{
		AvaloniaSession.Run(() =>
		{
			Control divider = BasicWidgets.Divider(MakeWidget("w-div", LayoutSchema.WidgetDivider));
			Assert.NotNull(divider);
			Assert.True(Types(divider).Exists(t => t.Contains("Border", StringComparison.Ordinal)),
				"分隔线应复用 Ui.Sep() 的 1px Border:" + Dump(Types(divider)));
			AssertNoWebHost(divider);
		});
	}

	[Fact]
	public void ShortcutWidget_GuidesOnEmptyInstance_RejectsOverlong_ConfiguredRendersHint()
	{
		AvaloniaSession.Run(() =>
		{
			// 没填实例
			Control empty = ShortcutWidget.Build(MakeWidget("w-sc1", LayoutSchema.WidgetShortcut, "快捷"));
			List<string> emptyTexts = Texts(empty);
			AssertHasText(emptyTexts, T("快捷启动没配置实例"));
			AssertHasText(emptyTexts, T("在布局 JSON 里给这个区块填 settings.instance(可选 settings.version)。"));
			AssertNoWebHost(empty);

			// 实例 / 版本超长
			Control tooLong = ShortcutWidget.Build(MakeWidget("w-sc2", LayoutSchema.WidgetShortcut,
				settings: new Dictionary<string, object?> { ["instance"] = new string('i', 129) }));
			List<string> tooLongTexts = Texts(tooLong);
			AssertHasText(tooLongTexts, T("快捷启动配置太长"));
			AssertHasText(tooLongTexts, T("实例名 / 版本号都不应超过 128 个字符。"));

			// 配好了:标题 + 类型名 + 实例 · 版本
			Control ok = ShortcutWidget.Build(MakeWidget("w-sc3", LayoutSchema.WidgetShortcut, "我的世界",
				new Dictionary<string, object?> { ["instance"] = "MyWorld", ["version"] = "1.20.1" }));
			List<string> okTexts = Texts(ok);
			AssertHasText(okTexts, "我的世界");
			AssertHasText(okTexts, T("快捷启动"));
			AssertHasText(okTexts, "MyWorld · 1.20.1");
			AssertNoWebHost(ok);

			// 没给版本就只有实例名
			Control noVersion = ShortcutWidget.Build(MakeWidget("w-sc4", LayoutSchema.WidgetShortcut,
				settings: new Dictionary<string, object?> { ["instance"] = "MyWorld" }));
			AssertHasText(Texts(noVersion), "MyWorld");
		});
	}

	[Fact]
	public void PlaytimeWidget_Build_RendersWithoutReadingRealData()
	{
		AvaloniaSession.Run(() =>
		{
			// 指定一个不存在的实例:只读,读不到就是 0 / 占位,不能抛
			Control one = PlaytimeWidget.Build(MakeWidget("w-pt1", LayoutSchema.WidgetPlaytime, "时长",
				new Dictionary<string, object?> { ["instance"] = "no-such-instance-" + Guid.NewGuid().ToString("N") }));
			List<string> oneTexts = Texts(one);
			AssertHasText(oneTexts, "时长");
			Assert.True(oneTexts.Count >= 2, "卡片应有标题 + 内容:" + Dump(oneTexts));
			AssertNoWebHost(one);

			// 不给实例 = 全部实例累计
			Control all = PlaytimeWidget.Build(MakeWidget("w-pt2", LayoutSchema.WidgetPlaytime, "总时长"));
			List<string> allTexts = Texts(all);
			AssertHasText(allTexts, "总时长");
			Assert.True(allTexts.Count >= 2, "卡片应有标题 + 内容:" + Dump(allTexts));

			// 宿主收到 game_exited 时的刷新入口:没有卡片在界面上也不该抛
			PlaytimeWidget.NotifyChanged();
		});
	}

	// ————————————————————————— 4 危险类型降级 —————————————————————————

	[Fact]
	public void DangerousWidgets_Web_Enabled_DegradesToOpenInBrowser_NoWebHostInTree()
	{
		AvaloniaSession.Run(() =>
		{
			WidgetDef widget = MakeWidget("w-web", LayoutSchema.WidgetWeb, "农场",
				new Dictionary<string, object?>
				{
					[WidgetCommon.EnabledKey] = true,
					["url"] = "https://example.com/farm"
				});

			Control root = DangerousWidgets.Build(widget);
			List<string> texts = Texts(root);

			// mac 结构化地没有 WebView2:恒走冻结件本来就有的「不能内嵌网页」兜底分支
			AssertHasText(texts, T("当前系统不能内嵌网页"));
			AssertHasText(texts, T("在浏览器中打开"));
			Assert.False(HasWebHost(Types(root)), "渲染树里不准出现网页宿主:" + Dump(Types(root)));
		});
	}

	[Fact]
	public void DangerousWidgets_DisabledByDefault_KeepsSettingsAndShowsDisabledPlaceholder()
	{
		AvaloniaSession.Run(() =>
		{
			WidgetDef widget = MakeWidget("w-web-off", LayoutSchema.WidgetWeb, "农场",
				new Dictionary<string, object?> { ["url"] = "https://example.com" });

			// Q5 默认关闭
			Assert.False(WidgetCommon.IsEnabled(widget));

			Control root = DangerousWidgets.Build(widget);
			List<string> texts = Texts(root);
			AssertHasText(texts, T("已禁用的自建区块"));
			Assert.True(texts.Exists(t => t.Contains("enabled", StringComparison.Ordinal)),
				"占位应告诉用户开关名:" + Dump(texts));

			// 铁律:占位只影响显示,绝不动布局文档里的数据
			Assert.Equal("https://example.com", widget.Settings["url"]);
			Assert.False(widget.Settings.ContainsKey(WidgetCommon.EnabledKey));
		});
	}

	[Fact]
	public void DangerousWidgets_Button_Enabled_ParsesWhitelistAction_RejectsOtherwise()
	{
		AvaloniaSession.Run(() =>
		{
			// 白名单动作:标题 + 类型名 + kind:arg1
			WidgetDef ok = MakeWidget("w-btn1", LayoutSchema.WidgetButton, "去启动页",
				new Dictionary<string, object?>
				{
					[WidgetCommon.EnabledKey] = true,
					["action"] = "page:launch"
				});
			Control okRoot = DangerousWidgets.Build(ok);
			List<string> okTexts = Texts(okRoot);
			AssertHasText(okTexts, "去启动页");
			AssertHasText(okTexts, T("按钮"));
			AssertHasText(okTexts, "page:launch");
			AssertNoWebHost(okRoot);

			// 命令注入:拒绝并给出中文原因
			WidgetDef bad = MakeWidget("w-btn2", LayoutSchema.WidgetButton, "危险",
				new Dictionary<string, object?>
				{
					[WidgetCommon.EnabledKey] = true,
					["action"] = "shell:rm -rf /"
				});
			List<string> badTexts = Texts(DangerousWidgets.Build(bad));
			AssertHasText(badTexts, T("按钮动作不合法"));
			AssertHasText(badTexts, T("不支持的按钮动作:{0}(只允许 page / launch / folder / rpc)", "shell"));
		});
	}

	// ————————————————————————— 5 mac 专有守卫(不在底稿 25 条内) —————————————————————————

	/// <summary>
	/// R32-MWG 新增(mac 专有,底稿没有):占位卡在 mac 上必须走 UiExt.Stretch(mac 没有 WPF 的
	/// HorizontalAlignment=Stretch 赋值式写法,WidgetUI.Placeholder 用 card.Stretch())。这条锁住的是
	/// 「占位卡真的是撑满宽度的 Border」—— 一类退化(Grid 里被挤成最小宽度)单靠文本断言看不出来。
	/// </summary>
	[Fact]
	public void WidgetUI_Placeholder_IsFullWidthCard_OnMac()
	{
		AvaloniaSession.Run(() =>
		{
			Control root = WidgetUI.Placeholder("标题", "提示");
			Assert.True(Types(root).Exists(t => t.Contains("Border", StringComparison.Ordinal)),
				"占位应是 Ui.Card 产出的 Border:" + Dump(Types(root)));
			Assert.Equal(HorizontalAlignment.Stretch, root.HorizontalAlignment);
		});
	}

	/// <summary>
	/// R32-MWG 新增(mac 专有,底稿没有;对应 R32-D7):自建区块层必须落在**扁平的** Pages/ 下,
	/// 命名空间是 <c>TipCL.Pages</c>;树里不许出现 <c>Pages/Layout/</c> 或 <c>Shell/</c> 子目录
	/// (Linux 结构与 Windows <c>Pages\Layout\</c> 结构都不许照抄进 mac 树)。
	/// </summary>
	[Fact]
	public void WidgetsLandFlatUnderMacPages_NoLayoutOrShellSubdirectory()
	{
		string? root = FindRepoRoot();
		Assert.False(root == null, "找不到源码树根(src/TipCL.App 所在层)");

		string pages = Path.Combine(root!, "src", "TipCL.App", "Pages");
		Assert.False(Directory.Exists(Path.Combine(pages, "Layout")),
			"mac 树的 Pages 是扁平的(R32-D7):不许出现 Pages/Layout 子目录");
		Assert.False(Directory.Exists(Path.Combine(root!, "src", "TipCL.App", "Shell")),
			"mac 树没有 Shell/ 目录(R32-D7):壳/接线文件直接落在 Pages/ 与根目录");
	}

	// ————————————————————————— 6 源码级降级守卫 —————————————————————————

	/// <summary>8 个移植文件(mac 树里是扁平的 Pages/*)。</summary>
	private static readonly string[] PortedFiles =
	{
		"src/TipCL.App/Pages/WidgetRegistry.cs",
		"src/TipCL.App/Pages/WidgetCommon.cs",
		"src/TipCL.App/Pages/WidgetUI.cs",
		"src/TipCL.App/Pages/BasicWidgets.cs",
		"src/TipCL.App/Pages/ShortcutWidget.cs",
		"src/TipCL.App/Pages/PlaytimeWidget.cs",
		"src/TipCL.App/Pages/DangerousWidgets.cs",
		"src/TipCL.App/Pages/PluginBlocks.cs"
	};

	/// <summary>从测试输出目录往上找源码树根(含 src/TipCL.App 的那层)。</summary>
	private static string? FindRepoRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, "src", "TipCL.App")))
			{
				return dir.FullName;
			}
		}
		return null;
	}

	/// <summary>去掉行注释与块注释,只留真正会被编译的代码。</summary>
	private static string StripComments(string source)
	{
		string noBlock = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
		return Regex.Replace(noBlock, @"//[^\n]*", " ");
	}

	[Fact]
	public void PortedWidgetSources_HaveNoWebViewOrHwndHostReference()
	{
		string? root = FindRepoRoot();
		Assert.False(root == null, "找不到源码树根(src/TipCL.App 所在层)");

		// 降级点只允许出现在注释里说明「mac 没有 WebView2」,代码里不准真去引用这些类。
		// NativePluginUi **不**在禁止清单里:mac 树本来就有它(TipCL.Pages.Plugins),PluginBlocks 要用。
		string[] forbidden =
		{
			"WebView2", "WebViewHost", "HwndHost", "CoreWebView2",
			"Microsoft.Web", "PluginWebView", "NativeWebView", "WebShell", "EditAwareShell"
		};

		foreach (string relative in PortedFiles)
		{
			string full = Path.Combine(root!, relative.Replace('/', Path.DirectorySeparatorChar));
			Assert.True(File.Exists(full), "移植文件不存在:" + relative);

			string code = StripComments(File.ReadAllText(full, Encoding.UTF8));
			foreach (string token in forbidden)
			{
				Assert.False(code.Contains(token, StringComparison.Ordinal),
					relative + " 的代码里出现 WebView2 专属类名 / 编辑态宿主名:" + token);
			}
		}
	}

	[Fact]
	public void PortedWidgetSources_DeclareFlatMacNamespace_NoNestedLayoutNamespace()
	{
		string? root = FindRepoRoot();
		Assert.False(root == null, "找不到源码树根(src/TipCL.App 所在层)");

		foreach (string relative in PortedFiles)
		{
			string full = Path.Combine(root!, relative.Replace('/', Path.DirectorySeparatorChar));
			Assert.True(File.Exists(full), "移植文件不存在:" + relative);

			string code = StripComments(File.ReadAllText(full, Encoding.UTF8));
			Assert.True(code.Contains("namespace TipCL.Pages;", StringComparison.Ordinal),
				relative + " 应声明 mac 的扁平命名空间 TipCL.Pages(R32-D7)");
			Assert.False(code.Contains("namespace TipCL.Pages.Layout", StringComparison.Ordinal),
				relative + " 的代码里出现了 TipCL.Pages.Layout(mac 树没有 Pages/Layout 层)");
		}
	}
}
