using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C5b:Windows 版 <c>Pages\FirstRunWizard.cs</c>(8667 字节 / 230 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 首次运行的「欢迎使用 TipCL」四步向导。它不是页面,是一个静态类(外壳在启动时调 <see cref="MaybeShowAsync"/> 决定弹不弹)。
///
/// 四步:1 三步概说 / 2 游戏目录 + 下载源 / 3 默认内存 + 版本隔离 / 4 五个容易错过的功能。
/// 收尾写 <c>set_game_dir</c>(目录真的换了才写)+ <c>save_settings</c>(下载源 / 内存 / 隔离 / first_run=false),
/// 然后 Toast「向导已完成」。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Slider.ValueChanged</c> 的载荷由 WPF 的 <c>RoutedPropertyChangedEventArgs&lt;double&gt;</c>
///   换成 Avalonia 的 <c>RangeBaseValueChangedEventArgs</c>(<c>e.NewValue</c> 同名同义);</item>
/// <item>隐藏 / 显示页面用 <c>Control.IsVisible</c>(WPF 的 <c>UIElement.Visibility</c>);</item>
/// <item><b>注入缝(请 Lead 裁决后落地)</b>:Windows 结尾那句
///   <c>AppServices.Window?.GetPage("settings") is SettingsPage page → page.RefreshAsync()</c>
///   依赖 <c>SettingsPage</c>,而它由 C5a 在另一个 teammate 手里移植 —— 本包**不能**凭空引用它。
///   所以这里留一条 <see cref="SettingsRefresh"/> 注入缝:接上了就调,没接就什么都不做
///   (与 D-119 / D-122 / D-131 同一口径:不假装刷新过,也不为它去改外壳);</item>
/// <item><see cref="MaybeShowAsync"/> 在 mac 侧**目前没有任何调用点**(Windows 是外壳启动时调),
///   本包不碰外壳,如实记成"外壳级缺口"交 Lead 统一处理;</item>
/// <item>局部变量名照 Windows 的 <c>text2</c> / <c>grid2</c> 那套反编译器风格**改成语义名**
///   (类型、顺序、判据、闭包捕获的时机一字不改)。</item>
/// </list>
/// </summary>
public static class FirstRunWizard
{
	/// <summary>
	/// 【注入缝】向导走完之后刷新「设置」页那一次。
	/// 未接线时为 <c>null</c> ⇒ 什么都不做(<c>SettingsPage</c> 由 C5a 移植,接上后由外壳/设置页赋值)。
	/// </summary>
	internal static Func<Task>? SettingsRefresh { get; set; }

	/// <summary>【Windows 源】<c>:14-21</c>:第四步的五个亮点(标题 + 正文)。</summary>
	private static readonly (string Title, string Body)[] Highlights = new(string, string)[5]
	{
		(I18n.L("把文件直接拖进窗口"), I18n.L("整合包、模组、资源包、光影、存档、皮肤、壁纸都认；认不准会问你一句，不用先找对页面。")),
		(I18n.L("侧栏是可以排的"), I18n.L("「游戏」「更多」里的子页拖到侧栏就固定成一级项，从侧栏拖回去就还原；排法和显隐在设置里。")),
		(I18n.L("换个背景"), I18n.L("设置页能设静态壁纸、mp4 动态壁纸，或者指一个文件夹轮播；模糊和遮罩也在那儿调。")),
		(I18n.L("AI 助手在侧栏「通用」组里"), I18n.L("崩溃日志看不懂、模组冲突理不清，可以直接问它。")),
		(I18n.L("这个向导随时能再看"), I18n.L("「设置 → 启动向导」点「重新运行」，目录、下载源、内存、隔离会再问一遍。")),
	};

	/// <summary>【Windows 源】<c>:23-28</c>:下载源三选(键 + 文案)。</summary>
	private static readonly (string Key, string Label)[] SrcOpts = new(string, string)[3]
	{
		("auto", I18n.L("自动（官方慢则 BMCLAPI）")),
		("official", I18n.L("仅官方")),
		("bmclapi", I18n.L("仅 BMCLAPI")),
	};

	/// <summary>【Windows 源】<c>:30-36</c>:隔离四选(键 + 文案)。</summary>
	private static readonly (string Key, string Label)[] IsoOpts = new(string, string)[4]
	{
		("all", I18n.L("完全独立（推荐）")),
		("mods", I18n.L("隔离 Mod 与配置")),
		("saves", I18n.L("隔离存档")),
		("none", I18n.L("不隔离")),
	};

	/// <summary>【Windows 源】<c>:38-44</c>:每一步下方那句提示。</summary>
	private static readonly string[] Hints = new string[4]
	{
		I18n.L("先把游戏目录和下载源定下来。"),
		I18n.L("版本都会装在这个目录里；下载源决定从哪儿拉文件。"),
		I18n.L("这两项只影响以后新建的版本，已经装好的不受影响。"),
		I18n.L("最后几句，都是容易错过的地方。"),
	};

	/// <summary>
	/// 【Windows 源】<c>:46-56</c>:<c>get_setting("first_run")</c> 为真才弹。
	/// mac 侧目前没有调用点(外壳级缺口,见类注释第 4 条)。
	/// </summary>
	public static async Task MaybeShowAsync()
	{
		if (await AppServices.Client.TryCallAsync("get_setting", new
		{
			key = "first_run",
			@default = false
		}, fallback: false))
		{
			await ShowAsync();
		}
	}

	/// <summary>【Windows 源】<c>:58-229</c>:四步向导本体(上一步 / 下一步 / 跳过向导;关掉浮层 = 跳过)。</summary>
	public static async Task ShowAsync()
	{
		BridgeClient api = AppServices.Client;
		Dictionary<string, JsonElement> settings = (await api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		TextBox gameDir = Ui.Input(I18n.L("游戏目录"), (Str("game_dir").Length > 0) ? Str("game_dir") : Str("root"));
		Button browse = Ui.Btn(I18n.L("浏览..."), BtnKind.Chip, delegate
		{
			string? folder = Dlg.PickFolder(I18n.L("选择游戏目录"));
			if (folder != null)
			{
				gameDir.Text = folder;
			}
		}, "\ue8b7");
		Grid dirRow = Ui.G(null, "*,Auto");
		dirRow.Add(gameDir, 0);
		dirRow.Add(browse.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		ComboBox source = Ui.Combo(SrcOpts.Select(((string Key, string Label) o) => o.Label));
		source.SelectedIndex = Math.Max(0, Array.FindIndex(SrcOpts, delegate((string Key, string Label) o)
		{
			string key = o.Key;
			string current = Str("download_source");
			return key == ((current != null && current.Length > 0) ? current : "auto");
		}));
		Slider memory = Ui.Sld(512.0, 32768.0, Clamp.Of(Int("default_memory_mb", 4096), 512, 32768), 256.0);
		TextBlock memoryLabel = Ui.Txt($"{(int)memory.Value} MB", 12.0, bold: true);
		memory.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
		{
			memoryLabel.Text = $"{(int)e.NewValue} MB";
		};
		Grid memoryRow = Ui.G(null, "*,Auto");
		memoryRow.Add(memory.VCenter(), 0);
		memoryRow.Add(memoryLabel.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		ComboBox isolation = Ui.Combo(IsoOpts.Select(((string Key, string Label) o) => o.Label));
		isolation.SelectedIndex = Math.Max(0, Array.FindIndex(IsoOpts, delegate((string Key, string Label) o)
		{
			string key = o.Key;
			string current = Str("default_isolation");
			return key == ((current != null && current.Length > 0) ? current : "all");
		}));
		SPanel intro = Ui.V(6.0, Ui.Txt(I18n.L("三步就能开始玩"), 14.0, bold: true), Ui.Muted(I18n.L("1. 选游戏目录和下载源")), Ui.Muted(I18n.L("2. 定默认内存和版本隔离")), Ui.Muted(I18n.L("3. 看一眼几个容易错过的功能")), Ui.Small(I18n.L("这些以后都能在设置里改；不想现在弄就点「跳过向导」。")).Wrap());
		SPanel dirPage = Ui.V(8.0, Ui.Txt(I18n.L("游戏目录（版本都装在这里）")), dirRow, Ui.Txt(I18n.L("文件下载源")), source, Ui.Small(I18n.L("国内网络建议保持「自动」：官方慢的时候会自己切到 BMCLAPI 镜像。")).Wrap());
		SPanel isoPage = Ui.V(8.0, Ui.Txt(I18n.L("默认内存")), memoryRow, Ui.Txt(I18n.L("新版本默认隔离")), isolation, Ui.Small(I18n.L("「完全独立」= 每个版本有自己的 mods / 配置 / 存档，换版本不会把上一个版本的模组拖进去。")).Wrap());
		SPanel highlightsPage = Ui.V(9.0);
		foreach ((string title, string bodyText) in Highlights)
		{
			highlightsPage.Children.Add(Ui.Txt(title, 13.0, bold: true));
			highlightsPage.Children.Add(Ui.Small(bodyText).Wrap());
		}

		Control[] pages = new Control[4] { intro, dirPage, isoPage, highlightsPage };
		TextBlock hint = Ui.Muted("");
		hint.TextWrapping = TextWrapping.Wrap;
		TextBlock step = Ui.Small("");
		Grid stage = new Grid
		{
			MinWidth = 470.0,
			MinHeight = 250.0,
		};
		foreach (Control page in pages)
		{
			page.IsVisible = false;
			stage.Children.Add(page);
		}

		Button back = Ui.Btn(I18n.L("上一步"));
		Button next = Ui.Btn(I18n.L("下一步"), BtnKind.Primary);
		Button skip = Ui.Btn(I18n.L("跳过向导"), BtnKind.Ghost);
		int index = 0;
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		SPanel body = Ui.V(12.0, hint, stage, Ui.Sep(), Ui.G(null, "Auto,*,Auto,Auto").Add(step.VCenter(), 0).Add(skip.Right().VCenter(), 0, 1)
			.Add(back.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2)
			.Add(next.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 3));
		Dlg.Layer? layer = null;
		layer = Dlg.Panel(I18n.L("欢迎使用 TipCL"), body, 560.0, delegate
		{
			// 点浮层右上角的关闭 = 跳过(与 Windows 的 onClose 回调同)
			tcs.TrySetResult(false);
		});
		back.Click += delegate
		{
			Show(index - 1);
		};
		next.Click += delegate
		{
			if (index < pages.Length - 1)
			{
				Show(index + 1);
			}
			else
			{
				tcs.TrySetResult(true);
				layer!.Close();
			}
		};
		skip.Click += delegate
		{
			tcs.TrySetResult(false);
			layer!.Close();
		};
		Show(0);
		if (!await tcs.Task)
		{
			// 【Windows 源】:跳过了也把 first_run 关掉,免得每次启动都弹
			await api.TryCallAsync<object>("save_settings", new
			{
				data = new
				{
					first_run = false
				}
			});
			return;
		}

		string typed = gameDir.Text?.Trim() ?? "";
		string current2 = ((Str("game_dir").Length > 0) ? Str("game_dir") : Str("root"));
		if (typed.Length > 0 && typed != current2)
		{
			try
			{
				await api.CallAsync<string>("set_game_dir", new
				{
					path = typed
				});
			}
			catch (Exception ex)
			{
				await Dlg.Alert(I18n.L("游戏目录没能设置"), I18n.L("{0}\n\n{1}\n\n已保留原来的目录,可到「设置」里重新选择。", typed, ex.Message));
			}
		}

		await api.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["download_source"] = SrcOpts[Math.Max(0, source.SelectedIndex)].Key,
				["default_memory_mb"] = (int)memory.Value,
				["default_isolation"] = IsoOpts[Math.Max(0, isolation.SelectedIndex)].Key,
				["first_run"] = false
			}
		});
		AppServices.Toast(I18n.L("向导已完成"), I18n.L("目录、下载源、内存、隔离已按你的选择写入"), ToastKind.Success);
		// 【注入缝】没接上就什么都不做(见类注释第 3 条):不假装刷新过设置页,也不为它去改外壳
		Func<Task>? refreshSettings = SettingsRefresh;
		if (refreshSettings != null)
		{
			await refreshSettings();
		}

		int Int(string key, int fallback)
		{
			if (!settings.TryGetValue(key, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
			{
				return fallback;
			}

			return number;
		}

		void Show(int i)
		{
			index = Clamp.Of(i, 0, pages.Length - 1);
			for (int j = 0; j < pages.Length; j++)
			{
				pages[j].IsVisible = (j == index);
			}

			hint.Text = Hints[index];
			step.Text = I18n.L("第 {0} / {1} 步", index + 1, pages.Length);
			back.IsEnabled = index > 0;
			next.Content = ((index == pages.Length - 1) ? I18n.L("开始使用") : I18n.L("下一步"));
			Motion.FadeIn(pages[index], 200.0, 8.0);
		}

		string Str(string key)
		{
			if (!settings.TryGetValue(key, out JsonElement value) || value.ValueKind != JsonValueKind.String)
			{
				return "";
			}

			return value.GetString() ?? "";
		}
	}
}
