using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b(批4 块1):Windows 版 <c>Pages\Plugins\PluginsPage.Installed.cs</c>(11835 字节 / 369 行 / WPF / net48)到
/// Avalonia 11 / net8 的移植 —— 「已安装」页签那一屏:三列网格(每格一张插件卡)+ 卡片上的 ⋯ 菜单
/// (更新 / 设置 / 重新加载 / 打开数据目录 / 查看日志 / 权限 / 打开开发文件夹 / 主页 / 卸载)。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>SplitRatioStore.PageColumns("plugins", 3, 1, 6)</c> → **本块先固定 3 列**(见下面那条**临时功能缺口**注释);</item>
/// <item><c>BrushConverter().ConvertFromString("#RRGGBB")</c> → <c>new SolidColorBrush(Color.Parse(...))</c>
///   (Avalonia 没有 <c>BrushConverter</c>;配色表 <c>LogoPalette</c> 与 hash 判据逐字保留);</item>
/// <item><c>ToolTip = …</c> → <c>Tip(…)</c>(附加属性,本仓统一走 UiExt);<c>ContextMenu.PlacementTarget + IsOpen</c> → <c>Open(anchor)</c>(D-124);</item>
/// <item><c>Visibility.Collapsed/Visible</c> → <c>IsVisible</c>;<c>TextBox.ScrollToEnd()</c> → 模板里那根 <c>ScrollViewer</c>
///   (与 <c>TasksPage.cs</c> 同一套 D-128 口径,不新造);</item>
/// <item><c>FontWeights.ExtraBold</c> → <c>FontWeight.ExtraBold</c>;<c>FrameworkElement</c> → <c>Control</c>;
///   <c>AddItem(ItemsControl menu, …)</c> → <c>AddItem(ContextMenu menu, …)</c>(本文件是它的归属地,主文件里那份重复的已删)。</item>
/// </list>
/// </summary>
public sealed partial class PluginsPage
{
	private readonly UniformGrid _installedHost;

	private List<PluginInfo> _installed = new List<PluginInfo>();

	private readonly Dictionary<string, List<ProgressBar>> _bars = new Dictionary<string, List<ProgressBar>>();

	private async Task LoadInstalledAsync()
	{
		if (!AppServices.Ready)
		{
			return;
		}

		// 后端还不支持插件时(旧桥 / 内核未就绪)不报错,只显示空列表
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("plugins_list");
		_installed = J.Items(r).Select(PluginInfo.From).Where((PluginInfo p) => p.Id.Length > 0).ToList();
		RenderInstalled();
	}

	private void RenderInstalled()
	{
		_installedHost.Children.Clear();
		foreach (List<ProgressBar> list in _bars.Values)
		{
			list.RemoveAll((ProgressBar b) => (b.Tag as string) == "installed");
		}

		// 【临时功能缺口(被跟踪 · 见 DECISIONS **D-165**;号是当场读 DECISIONS 现最大号后取的】
		// 为什么:mac 侧**没有** SplitRatioStore —— 它是 Windows「布局 v2 文档」那套
		//   (Windows `Pages\Layout\PageLayoutHost.cs:294`,LayoutStoreV2/LayoutSchema/SplitState,把每页分栏比例持久化进布局文档)。
		//   mac 的 UiPrefs 是另一套存储(只有 SideWidth/Dark/Anim/Expanded/VmFavorites…),没有布局文档、也没有分栏比例。
		// 正解:等布局包落地后,这一行改为
		//   `_installedHost.Columns = (_installed.Count == 0) ? 1 : SplitRatioStore.PageColumns("plugins", 3, 1, 6);`
		//   (与 Windows `PluginsPage.Installed.cs:43` 逐字一致)。
		// 用户可见后果:插件市场/已安装网格**固定 3 列,用户不可调**。
		// **保留原版行为**:"空列表时 1 列"与存储无关,照搬。
		// 【C9a】列数改为**同族的列数存储**(键 = pageId"plugins",夹到 1..6;Windows 原文:
		//   `SplitRatioStore.PageColumns("plugins", 3, 1, 6)` —— 默认 3、只在页面声明格子模式时用存储值)。
		// 原先这里是固定 3 列(见上面那条**临时功能缺口**注释)⇒ 该缺口由 C9a 的列数存储关闭(D-165)。
		// **保留原版行为**:"空列表时 1 列"与存储无关,照搬(D-165 收口时不动这一支)。
		_installedHost.Columns = (_installed.Count == 0) ? 1 : TipCL.Controls.PageLayoutHost.PageColumns(PageBase.Win?.Prefs, "plugins", 3, 1, 6);
		if (_installed.Count == 0)
		{
			SPanel empty = Ui.Empty("\uea86", I18n.L("还没有安装插件"), I18n.L("去插件市场看看,或者把插件 zip 拖进这个页面。"));
			_installedHost.Children.Add(empty);
			return;
		}

		foreach (PluginInfo p in _installed.OrderBy((PluginInfo x) => x.DisplayName, StringComparer.CurrentCulture))
		{
			_installedHost.Children.Add(InstalledCard(p));
		}

		if (Motion.Enabled)
		{
			Motion.Stagger(_installedHost, 22.0, 180.0, 8.0);
		}
	}

	private static Border StateBadge(PluginInfo p)
	{
		if (!p.Enabled && p.State != "restart_required")
		{
			return Ui.Tag(I18n.L("已禁用"));
		}

		return p.State switch
		{
			"running" => Ui.Tag(I18n.L("运行中"), "B.Ok", "B.AccentSoft"),
			"error" => Ui.Tag(I18n.L("出错"), "B.Danger", "B.DangerSoft"),
			"restart_required" => Ui.Tag(I18n.L("重启后生效"), "B.Warn", "B.WarnSoft"),
			"loading" => Ui.Tag(I18n.L("加载中"), "B.Info", "B.InfoSoft"),
			"stopped" => Ui.Tag(I18n.L("已停止")),
			_ => Ui.Tag((p.State.Length > 0) ? p.State : I18n.L("未知")),
		};
	}

	// 效果图里每个插件 logo 底色不同:按 id 从固定调色板取色(与主题无关,配白字明暗都成立)
	internal static readonly string[] LogoPalette = new string[6] { "#7C5CFF", "#F59E0B", "#64748B", "#0EA5E9", "#10B981", "#E0559D" };

	private static IBrush LogoBrush(string id)
	{
		int hash = 0;
		foreach (char c in id)
		{
			hash = (hash * 31) + c;
		}

		return new SolidColorBrush(Color.Parse(LogoPalette[Math.Abs(hash) % LogoPalette.Length]));
	}

	private Border InstalledCard(PluginInfo p)
	{
		string n = p.DisplayName.Trim();
		TextBlock letter = Ui.Txt((n.Length > 0) ? char.ToUpperInvariant(n[0]).ToString() : "?", 14.0, bold: true, "B.OnAccentStrong");
		letter.FontWeight = FontWeight.ExtraBold;
		Border logo = Ui.Pane(letter.Center().VCenter(), "B.AccentStrong", 9.0, 0.0);
		logo.Background = LogoBrush(p.Id.Length > 0 ? p.Id : n);
		logo.Width = 36.0;
		logo.Height = 36.0;
		TextBlock name = Ui.Txt(p.DisplayName, 15.0, bold: true).Trim();
		name.FontWeight = FontWeight.ExtraBold;
		name.Tip(p.Id);
		CheckBox sw = Ui.Switch(p.Enabled);
		sw.Tip(p.Enabled ? I18n.L("点击禁用") : I18n.L("点击启用"));
		sw.Click += delegate
		{
			bool on = sw.IsChecked == true;
			PageBase.Run(async delegate
			{
				try
				{
					await PageBase.Api.CallAsync("plugins_set_enabled", new
					{
						id = p.Id,
						enabled = on
					});
				}
				catch
				{
					sw.IsChecked = !on;
					throw;
				}

				await PluginUi.RefreshAsync();
				await LoadInstalledAsync();
			}, on ? I18n.L("启用失败") : I18n.L("禁用失败"));
		};
		Grid top = Ui.G(null, "Auto,*,Auto");
		top.Add(logo, 0);
		top.Add(name.M(10.0, 0.0, 8.0, 0.0).VCenter(), 0, 1);
		top.Add(sw.VCenter(), 0, 2);
		TextBlock desc = Ui.Muted(p.Description).Wrap();
		desc.FontSize = 12.5;
		desc.Height = 36.0;
		desc.TextTrimming = TextTrimming.CharacterEllipsis;
		desc.Margin = new Avalonia.Thickness(0.0, 10.0, 0.0, 12.0);
		Button more = Ui.IconBtn("\ue712", I18n.L("更多"));
		ContextMenu menu = new ContextMenu();
		if (p.UpdateAvailable)
		{
			AddItem(menu, I18n.L("更新"), "\ue896", delegate
			{
				PageBase.Run(() => StartPluginTaskAsync("plugins_install", new { id = p.Id }, p.Id, more), I18n.L("更新失败"));
			});
		}

		AddItem(menu, I18n.L("设置"), "\ue713", delegate
		{
			ShowSettings(p, forceAuto: false);
		});
		if (p.Enabled)
		{
			AddItem(menu, I18n.L("重新加载"), "\ue72c", delegate
			{
				PageBase.Run(async delegate
				{
					await PageBase.Api.CallAsync("plugins_reload", new { id = p.Id });
					PageBase.Toast(I18n.L("已重新加载"), p.DisplayName, ToastKind.Success);
					await PluginUi.RefreshAsync();
					await LoadInstalledAsync();
				}, I18n.L("重新加载失败"));
			});
		}

		AddItem(menu, I18n.L("打开数据目录"), "\ue8b7", delegate
		{
			OpenFolder(PluginDataDir(p.Id));
		});
		AddItem(menu, I18n.L("查看日志"), "\ue9d5", delegate
		{
			PageBase.Run(() => ShowLogAsync(p.Id, p.DisplayName));
		});
		AddItem(menu, I18n.L("权限"), "\uea18", delegate
		{
			ShowPermissions(p);
		});
		if (p.Source == "dev" && p.DevPath.Length > 0)
		{
			AddItem(menu, I18n.L("打开开发文件夹"), "\ue838", delegate
			{
				OpenFolder(p.DevPath);
			});
		}

		if (p.Homepage.Length > 0)
		{
			AddItem(menu, I18n.L("插件主页"), "\ue8a7", delegate
			{
				Ui.OpenUrl(p.Homepage);
			});
		}

		menu.Items.Add(new Separator());
		AddItem(menu, (p.Source == "dev") ? I18n.L("取消链接") : I18n.L("卸载"), "\ue74d", delegate
		{
			PageBase.Run(() => UninstallAsync(p), I18n.L("卸载失败"));
		});
		more.Click += delegate
		{
			// 【D-124】WPF 的 PlacementTarget + IsOpen = true → Avalonia 的 Open(anchor)
			menu.Open(more);
		};
		Grid foot = Ui.G(null, "*,Auto");
		foot.Add(Ui.H(6.0, Ui.Muted((p.Source == "dev") ? I18n.L("开发链接") : ((p.Version.Length > 0) ? ("v" + p.Version) : "")).VCenter(), (p.State == "running" && p.Enabled) ? null : StateBadge(p), (p.Source == "dev") ? Ui.Tag(I18n.L("开发"), "B.Warn", "B.WarnSoft").Tip(p.DevPath) : null, (p.Source == "local") ? Ui.Tag(I18n.L("本地")) : null, (p.Runtime == "dotnet") ? Ui.Tag(".NET") : null, p.UpdateAvailable ? Ui.Tag(I18n.L("有更新"), "B.Warn", "B.WarnSoft") : null).VCenter(), 0);
		foot.Add(more.VCenter(), 0, 1);
		SPanel body = Ui.V(0.0, top, desc, foot);
		if (p.Error.Length > 0)
		{
			body.Children.Add(Ui.Txt(p.Error, 12.0, bold: false, "B.Danger").Wrap().M(0.0, 6.0, 0.0, 0.0));
		}

		if (p.State == "restart_required")
		{
			body.Children.Add(Ui.Small(I18n.L(".NET 插件的启用、禁用、更新和卸载要重启 TipCL 才生效")).Wrap().M(0.0, 6.0, 0.0, 0.0));
		}

		ProgressBar bar = Ui.Prog();
		bar.Height = 4.0;
		bar.Tag = "installed";
		bar.IsVisible = false;
		Track(p.Id, bar);
		body.Children.Add(bar.M(0.0, 6.0, 0.0, 0.0));
		Border card = Ui.Card(body, 16.0);
		card.Padding = new Avalonia.Thickness(18.0, 16.0, 18.0, 16.0);
		// UniformGrid 单元格会把子元素拉满整格高度;卡片要按内容自适应(贴效果图)
		card.VerticalAlignment = VerticalAlignment.Top;
		card.Margin = new Avalonia.Thickness(7.0);
		return card;
	}

	/// <summary>⋯ 菜单项(**Windows 把它放在本 partial 里**,所以这是它的归属地;主文件里那份重复的已删)。
	/// mac 差异:<c>ItemsControl</c> → <c>ContextMenu</c>;图标照 Windows 走 <c>MenuItem.Icon</c>。</summary>
	internal static void AddItem(ContextMenu menu, string header, string glyph, Action act)
	{
		MenuItem mi = new MenuItem
		{
			Header = header,
			Icon = Ui.Glyph(glyph, 13.0),
		};
		mi.Click += delegate
		{
			act();
		};
		menu.Items.Add(mi);
	}

	private void Track(string plugin, ProgressBar bar)
	{
		if (!_bars.TryGetValue(plugin, out List<ProgressBar>? list))
		{
			list = (_bars[plugin] = new List<ProgressBar>());
		}

		list.Add(bar);
		string? running = _taskPlugin.FirstOrDefault((KeyValuePair<string, string> kv) => kv.Value == plugin).Key;
		if (running != null)
		{
			ApplyProgress(bar, TaskStore.Get(running), done: false);
		}
	}

	private void SyncProgress(string plugin, string taskId, bool done = false)
	{
		if (!_bars.TryGetValue(plugin, out List<ProgressBar>? list))
		{
			return;
		}

		TaskRow? row = TaskStore.Get(taskId);
		foreach (ProgressBar bar in list)
		{
			ApplyProgress(bar, row, done);
		}
	}

	private static void ApplyProgress(ProgressBar bar, TaskRow? row, bool done)
	{
		if (done)
		{
			bar.IsVisible = false;
			return;
		}

		bar.IsVisible = true;
		bar.IsIndeterminate = row?.Indeterminate ?? true;
		if (row != null && !row.Indeterminate)
		{
			Motion.Progress(bar, row.Progress);
		}

		string? status = row?.Status;
		if (!string.IsNullOrEmpty(status))
		{
			bar.Tip(status);
		}
	}

	private async Task UninstallAsync(PluginInfo p)
	{
		CheckBox del = Ui.Check(I18n.L("同时删除数据(插件数据目录和保存的凭据)"));
		SPanel body = Ui.V(10.0, Ui.Muted((p.Source == "dev") ? I18n.L("取消链接后开发文件夹本身不会被删除。") : I18n.L("会删除插件文件;之后可以从市场重新安装。")).Wrap(), del);
		body.MinWidth = 360.0;
		if (await Dlg.Ask(I18n.L("卸载「{0}」?", p.DisplayName), body, (p.Source == "dev") ? I18n.L("取消链接") : I18n.L("卸载"), null, danger: true))
		{
			bool deleteData = del.IsChecked == true;
			await PageBase.Api.CallAsync("plugins_uninstall", new
			{
				id = p.Id,
				delete_data = deleteData
			});
			PageBase.Toast(I18n.L("已卸载"), p.DisplayName, ToastKind.Success);
			await PluginUi.RefreshAsync();
			await LoadInstalledAsync();
		}
	}

	private void ShowPermissions(PluginInfo p)
	{
		List<string> perms = PluginPerms.Effective((p.Granted.Count > 0) ? p.Granted : p.Permissions, p.Runtime);
		ScrollViewer sv = new ScrollViewer
		{
			Content = PluginPerms.List(perms),
			MaxHeight = Ui.WindowPctOf(0.5, 240.0),
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		};
		Dlg.Panel(I18n.L("「{0}」的权限", p.DisplayName), sv, 560.0);
	}

	internal static async Task ShowLogAsync(string id, string title)
	{
		TextBox box = Ui.LogBox();
		box.Height = 380.0;
		box.MinWidth = 640.0;
		box.Text = await ReadLogAsync(id);
		ScrollLogToEnd(box);
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, null, "\ue72c");
		refresh.Click += delegate
		{
			PageBase.Run(async delegate
			{
				box.Text = await ReadLogAsync(id);
				ScrollLogToEnd(box);
			});
		};
		Dlg.Panel(I18n.L("{0} · 日志", title), Ui.V(8.0, refresh.Left(), box), 860.0);
	}

	/// <summary>【Windows 源】<c>box.ScrollToEnd()</c> —— Avalonia 的 TextBox 没有它(D-128),
	/// 等价物是它模板里那根 <c>ScrollViewer</c>(与 <c>TasksPage.cs</c> 同一套口径;取不到就不滚)。</summary>
	private static void ScrollLogToEnd(TextBox box)
	{
		box.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
	}

	internal static async Task<string> ReadLogAsync(string id, int lines = 400)
	{
		JsonElement r = await PageBase.Api.CallAsync("plugins_log", new { id, lines });
		string text = r.ValueKind switch
		{
			JsonValueKind.String => r.GetString() ?? "",
			JsonValueKind.Array => string.Join("\n", from x in r.EnumerateArray()
				select (x.ValueKind == JsonValueKind.String) ? x.GetString() : x.GetRawText()),
			JsonValueKind.Object => J.Str(r, "text", J.Str(r, "log")),
			_ => "",
		};
		if (text.Length != 0)
		{
			return text;
		}

		return I18n.L("(日志是空的)");
	}

	private async Task InstallLocalAsync(string path, bool dev, Control? source)
	{
		string label = System.IO.Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
		JsonElement r = await PageBase.Api.CallAsync("plugins_install_local", new { path, dev });
		string taskId = J.TaskId(r);
		if (taskId.Length > 0)
		{
			_taskPlugin[taskId] = label;
			if (source != null)
			{
				PageBase.Win?.FlyToTasks(source, I18n.L("已加入队列"));
			}

			PageBase.Toast(dev ? I18n.L("正在链接开发文件夹") : I18n.L("正在安装插件"), label);
		}
		else
		{
			PageBase.Toast(dev ? I18n.L("已链接开发文件夹") : I18n.L("插件已安装"), J.Str(r, "id", label), ToastKind.Success);
			await PluginUi.RefreshAsync();
			await LoadInstalledAsync();
		}
	}
}
