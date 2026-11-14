using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TipCL;
using TipCL.Pages;
using TipCL.Pages.Plugins;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C7b 收尾的三条用例(原文件 `C7bSettingsCardTests.cs` **从未提交、盘上也不存在**,见 `cb015309.report.md` §4 U7;
/// 这三处覆盖点当时没有被别的用例替代)。
///
/// 驱动口径(与 C7b 既有用例同一套):假后端 <see cref="C3cFakeApi"/> 按方法名路由 +
/// <see cref="C5bLayerHost"/> 把卡片挂进真窗口(卡片那两个随改随存的分支都要 `Loaded` 走一遍) +
/// 找按钮一律走 `AutomationProperties.Name`(带图标的按钮 `Content` 是图文面板)。
/// </summary>
public class C7bSettingsCardTests
{
	/// <summary>一个"假插件主页":只记下自己被调了什么(证明卡片真的走到了 `<see cref="IPluginHostPage"/>` 上)。</summary>
	private sealed class FakeHostPage : PageBase, IPluginHostPage
	{
		public override string Title => "假插件主页";

		public int Refreshes;

		public readonly List<string> Tabs = new List<string>();

		public void RefreshMarketSoon()
		{
			Refreshes++;
		}

		public void ShowTab(string tab)
		{
			Tabs.Add(tab);
		}
	}

	/// <summary>`get_settings` 的回显:三个镜像,优先级 1/2/3(= 列表顺序),第二个是关着的。</summary>
	private const string ThreeMirrors =
		"{\"plugin_mirrors\":[" +
		"{\"id\":\"m1\",\"name\":\"镜像一\",\"raw_prefix\":\"https://r1.example/gh\",\"download_prefix\":\"https://d1.example/gh\",\"enabled\":true,\"priority\":1}," +
		"{\"id\":\"m2\",\"name\":\"镜像二\",\"raw_prefix\":\"https://r2.example/gh\",\"download_prefix\":\"https://d2.example/gh\",\"enabled\":false,\"priority\":2}," +
		"{\"id\":\"m3\",\"name\":\"镜像三\",\"raw_prefix\":\"https://r3.example/gh\",\"download_prefix\":\"https://d3.example/gh\",\"enabled\":true,\"priority\":3}]}";

	/// <summary>卡片里自动化名 == <paramref name="name"/> 的按钮(按树的顺序,也就是渲染顺序)。</summary>
	private static List<Button> NamedButtons(Control root, string name)
	{
		return C5bFiles.Descendants(root).OfType<Button>()
			.Where(b => string.Equals(AutomationProperties.GetName(b), I18n.L(name), StringComparison.Ordinal))
			.ToList();
	}

	/// <summary>三行镜像的标题文案(按树顺序 = 渲染顺序)。</summary>
	private static List<string> MirrorLabels(Control card)
	{
		return C5bFiles.Texts(card).Select(t => t.Text ?? "")
			.Where(s => s.StartsWith("镜像", StringComparison.Ordinal))
			.ToList();
	}

	/// <summary>
	/// 「允许安装未审核插件」那一行的开关:先在文案上认行,再取"最近的那个只含一个 `CheckBox` 的祖先"
	/// (镜像行里那些开关都在另一个分支上,不会串进来)。
	/// </summary>
	private static CheckBox UnverifiedSwitch(Control card)
	{
		TextBlock label = C5bFiles.Texts(card)
			.First(t => string.Equals(t.Text, I18n.L("允许安装未审核插件"), StringComparison.Ordinal));
		Control row = label.GetVisualAncestors().OfType<Control>()
			.First(c => C5bFiles.Descendants(c).OfType<CheckBox>().Count() == 1);
		return C5bFiles.Descendants(row).OfType<CheckBox>().Single();
	}

	/// <summary>
	/// 建卡并挂到浮层宿主上,泵到 `Loaded` 那条异步活儿**跑完**。
	///
	/// 判据为什么是"镜像行已渲染"而不是"`get_settings` 已到达":`Loaded` 里的顺序是
	/// **先回显设置(`unverified.IsChecked = ...`)、再 `Sync()` + `RenderMirrors()`** ⇒
	/// 只等请求到达就放行,会在回显**之前**继续跑,后面自己改的开关状态会被回显悄悄覆盖(本条用例踩过)。
	/// </summary>
	private static Border MountCard(C5bLayerHost harness, C3cFakeApi api)
	{
		Border card = PluginSettingsCard.Build();
		C5bFiles.Mount(harness, card);
		C5bFiles.Pump(() => api.Count("get_settings") >= 1 && NamedButtons(card, "下移").Count > 0);
		return card;
	}

	/// <summary>
	/// 镜像「下移」:保存的 payload 形状(`data.plugin_mirrors[]`,六个键与值类型)+ **顺序真的换了**
	/// (交换的是 priority,id/name/enabled 跟着各自的对象走),界面也按新顺序重排。
	/// </summary>
	[Fact]
	public void Mirror_move_saves_the_json_shape_and_really_swaps_the_order()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = C5bScope.Capture();
			using var toasts = new C3cToasts();
			using var api = new C3cFakeApi()
				.Route("get_settings", ThreeMirrors)
				.Route("save_settings", "null");
			using var harness = C5bLayerHost.Show();
			Border card = MountCard(harness, api);
			Assert.Equal(new[] { "镜像一", "镜像二", "镜像三" }, MirrorLabels(card).ToArray());

			List<Button> downs = NamedButtons(card, "下移");
			Assert.True(downs[0].IsEnabled, "第一行的「下移」必须可用");
			Assert.False(downs[2].IsEnabled, "最后一行的「下移」必须不可用");
			C5bFiles.Click(downs[0]);
			C5bFiles.Pump(() => api.Count("save_settings") >= 1);

			using JsonDocument doc = JsonDocument.Parse(api.ParamsOf("save_settings"));
			JsonElement mirrors = doc.RootElement.GetProperty("data").GetProperty("plugin_mirrors");
			Assert.Equal(JsonValueKind.Array, mirrors.ValueKind);
			List<JsonElement> rows = mirrors.EnumerateArray().ToList();
			Assert.Equal(3, rows.Count);
			// 六个键一字不差(Windows 那边是 PyDict.Of("id", m.Id, ...),JSON 形状逐字一致),
			// 而且 enabled 是布尔、priority 是数字(不是字符串)
			Assert.Equal(
				new[] { "download_prefix", "enabled", "id", "name", "priority", "raw_prefix" },
				rows[0].EnumerateObject().Select(p => p.Name).OrderBy(s => s, StringComparer.Ordinal).ToArray());
			Assert.Equal(JsonValueKind.String, rows[0].GetProperty("id").ValueKind);
			Assert.Equal(JsonValueKind.String, rows[0].GetProperty("name").ValueKind);
			Assert.Equal(JsonValueKind.String, rows[0].GetProperty("raw_prefix").ValueKind);
			Assert.Equal(JsonValueKind.String, rows[0].GetProperty("download_prefix").ValueKind);
			Assert.Equal(JsonValueKind.True, rows[0].GetProperty("enabled").ValueKind);
			Assert.Equal(JsonValueKind.Number, rows[0].GetProperty("priority").ValueKind);
			// **顺序真的换了**:payload 里仍是那三行(字段列表的顺序),换的是 **priority 那对值**
			// —— m1: 1 → 2、m2: 2 → 1(后端按 priority 排序,所以这就是"下移"落到后端的形态)
			Assert.Equal(new[] { "m1", "m2", "m3" }, rows.Select(r => r.GetProperty("id").GetString()).ToArray());
			Assert.Equal(new[] { 2L, 1L, 3L }, rows.Select(r => r.GetProperty("priority").GetInt64()).ToArray());
			// 交换的是值不是对象:id / name / enabled 都跟着各自的对象走(m2 是关着的那个)
			Assert.Equal(new[] { "镜像一", "镜像二", "镜像三" }, rows.Select(r => r.GetProperty("name").GetString()).ToArray());
			Assert.Equal(new[] { "https://r1.example/gh", "https://r2.example/gh", "https://r3.example/gh" },
				rows.Select(r => r.GetProperty("raw_prefix").GetString()).ToArray());
			Assert.Equal(new[] { true, false, true }, rows.Select(r => r.GetProperty("enabled").GetBoolean()).ToArray());
			// 界面按新 priority 重排(RenderMirrors 每次都按 priority 再排一遍)
			C5bFiles.Pump(() => MirrorLabels(card).SequenceEqual(new[] { "镜像二", "镜像一", "镜像三" }));
		});
	}

	/// <summary>
	/// 未审核开关的两条路:打开 ⇒ **先弹确认卡**(危险按钮),点「仍然允许」才真的存 `true`;
	/// 同一开关再来一次、点「取消」⇒ 开关**自己弹回 false** 且**一个字节都不发**。
	/// </summary>
	[Fact]
	public void Unverified_toggle_confirms_before_saving_and_cancel_rolls_the_switch_back()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = C5bScope.Capture();
			using var toasts = new C3cToasts();
			using var api = new C3cFakeApi()
				.Route("get_settings", "{}")
				.Route("save_settings", "null");
			using var harness = C5bLayerHost.Show();
			Border card = MountCard(harness, api);
			CheckBox allow = UnverifiedSwitch(card);
			Assert.False(allow.IsChecked == true);

			// —— 确认那条
			allow.IsChecked = true;
			C5bFiles.Click(allow);
			Control dialog = C5bFiles.AnswerDialog(I18n.L("仍然允许"));
			Assert.True(C5bFiles.HasText(dialog, I18n.L("允许安装未审核插件？")), "弹出来的必须是真的那条确认卡");
			Assert.True(C5bFiles.HasText(dialog,
				I18n.L("未审核的插件没人检查过来源和代码。Node 插件拥有完整的电脑权限，只安装你信任的作者发布的插件。")));
			C5bFiles.Pump(() => api.Count("save_settings") >= 1);
			Assert.Contains("\"plugin_allow_unverified\":true", api.ParamsOf("save_settings"), StringComparison.Ordinal);
			Assert.True(allow.IsChecked == true, "确认之后开关保持打开");

			// —— 取消那条:开关回弹 + 不发请求
			int saves = api.Count("save_settings");
			allow.IsChecked = true;
			C5bFiles.Click(allow);
			C5bFiles.AnswerDialog(I18n.L("取消"));
			C5bFiles.Settle(6);
			Assert.False(allow.IsChecked == true, "取消之后开关必须弹回去");
			Assert.Equal(saves, api.Count("save_settings"));
		});
	}

	/// <summary>
	/// `IPluginHostPage.RefreshMarketSoon` 的两个口径合一条:
	/// 1**驱动**:镜像保存一次 ⇒ 假主页被**恰好调一次**(不是 0 次,也不是 2 次);再来一次保存 ⇒ 变 2(是"每次保存一次");
	/// 2**源码**:该卡片里这条调用点**只有一处活的**(注释里那句按 C1a 的剥注释口径排除,免得"再加一处接线"偷偷溜过)。
	/// </summary>
	[Fact]
	public void Refresh_market_soon_is_called_once_per_save_and_has_one_live_call_site()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = C5bScope.Capture();
			using var toasts = new C3cToasts();
			using var api = new C3cFakeApi()
				.Route("get_settings", ThreeMirrors)
				.Route("save_settings", "null");
			using var harness = C5bLayerHost.Show();
			IReadOnlyDictionary<string, Func<PageBase>> snapshot = PageRegistry.Snapshot();
			FakeHostPage fake = new FakeHostPage();
			MainWindow shell = new MainWindow(new UiPrefs());
			try
			{
				PageRegistry.Register(PluginsPageRegistration.PageId, () => fake);
				// 【Lead 的口径】这个调用点是两层 as 串联 ⇒ 用例必须显式断言"解析成功",
				// 否则任一环失败都会让整条调用静默 no-op、而用例只看起来"没反应"
				Assert.True(ReferenceEquals(shell, AppServices.Window as MainWindow),
					"第一环解析失败:AppServices.Window 不是这个 MainWindow(调用点会被 `as` 静默吞掉)");
				Assert.NotNull(shell.GetPage(PluginsPageRegistration.PageId) as IPluginHostPage);

				Border card = MountCard(harness, api);
				C5bFiles.Click(NamedButtons(card, "下移")[0]);
				C5bFiles.Pump(() => fake.Refreshes >= 1);
				Assert.Equal(1, fake.Refreshes);

				// 「恢复默认」也是一次镜像保存(整份列表重写)⇒ 再加一次,证明它是"每次保存一次"
				C5bFiles.Click(C5bFiles.ByText(card, I18n.L("恢复默认")));
				C5bFiles.Pump(() => fake.Refreshes >= 2);
				Assert.Equal(2, fake.Refreshes);
			}
			finally
			{
				PageRegistry.Restore(snapshot);
				shell.UnhookPlugins();
				shell.Close();
			}

			string source = File.ReadAllText(Path.Combine(
				C1aFixtures.RepoRoot(), "src", "TipCL.App", "Pages", "Plugins", "PluginSettingsCard.cs"));
			string live = C1aPaletteTests.StripComments(source);
			// Assert.Single 而不是 Assert.Equal(1, ...Count)(后者会带出 xUnit2013 警告)
			Assert.Single(Regex.Matches(live, "\\.RefreshMarketSoon\\(", RegexOptions.CultureInvariant));
		});
	}
}
