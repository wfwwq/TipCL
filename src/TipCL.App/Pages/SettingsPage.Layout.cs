using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Controls;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

// 来源:Windows 版冻结件 wt-r31-merge @0a304a7
//       windos/src/TipCL/Pages/SettingsLayoutPage.cs 的**界面部分**(:1-802;之后 804+ 是
//       LayoutBlockEntryCatalog / LayoutProfiles / LayoutJsonEditor,本包已各自成文件移植)。
//
// 移植方式:逐字等价(布局视图 7 个小节 a-g 与 JSON 编辑器整段照搬),命名空间
//   `TipCL.Pages.Layout` → `TipCL.Pages`(mac 树 Pages 平铺,D7);WPF→Avalonia 的类型/API 替换见下表。
//
// [WPF→Avalonia 降级表](逐条,均已在本包实测)
//   1) UIElement / FrameworkElement → Control;DependencyObject → Visual(object 侧走 ILogical)。
//   2) `Checked` / `Unchecked` 两事件在 Avalonia 11.3 已 obsolescent ⇒ 合成一个 `IsCheckedChanged`,
//      内部读 `IsChecked == true`。语义在两态控件上等价(Windows 侧这两个分支本来就只写 true/false)。
//   3) `x.ToolTip = y` → `x.Tip(y)`(UiExt)。
//   4) `ToolTipService.SetShowOnDisabled(visible, true)` **Avalonia 无对应 API**(实测
//      `strings Avalonia.Controls.dll (11.3.23/net8.0) | grep -c ShowOnDisabled` = 0)⇒ 去掉。
//      后果:设置页签那个"始终显示"的开关被禁用时,悬停不弹提示(开关本身仍禁用,文案仍写在 Tip 上,
//      只在启用态可见)。这是 mac 侧唯一一处**看得见的功能缺失**,其余逐像素一致。
//   5) `base.Dispatcher.InvokeAsync(Action, DispatcherPriority.Loaded)` →
//      `Dispatcher.UIThread.Post(Action, DispatcherPriority.Loaded)`(Avalonia 的 Control 没有 Dispatcher 属性)。
//   6) `VisualTreeHelper.GetChildrenCount/GetChild` → `Visual.GetVisualChildren()`(Avalonia.VisualTree);
//      `LogicalTreeHelper.GetChildren` → `ILogical.GetLogicalChildren()`(Avalonia.LogicalTree)。
//   7) `SplitRatioStore.Read/Clear(pageId, splitId)` → mac 既有等价物
//      `PageLayoutHost.Read/Clear(PageBase.Win?.Prefs, pageId, splitId)`
//      (Controls/PageLayoutHost.cs:430/518;先例 Pages/Plugins/PluginsPage.Installed.cs:74)。
//      **不重复搬 SplitRatioStore**,避免两套比例读写口打架(见 SplitCatalog.cs 头注释)。
//   8) `TextWrapping.Wrap` 需 `using Avalonia.Media`(同名枚举)。
//   9) `Ui.Btn/Ui.IconBtn` 的 click 形参是 `EventHandler<RoutedEventArgs>?` ⇒ Windows 的 `delegate { ... }`
//      逐字可编译(mac 侧既有写法)。

/// <summary>设置页:第 8 个视图「布局」(L5)。视图装配 + 各小节行 + 导入导出 + JSON 编辑器。</summary>
public sealed partial class SettingsPage
{
	/// <summary>「布局」视图在视图表/左导航里的下标(第 8 个)。</summary>
	internal const int LayoutView = 7;

	private bool _layoutRebuilding;

	// JSON 编辑器的草稿:重建视图时保留用户还没应用的文本
	private string _layoutJsonText = "";

	private SPanel? _layoutJsonIssues;

	// ————— 视图装配 —————

	/// <summary>构「布局」视图(每次切到该视图都重建,保证读到最新状态)。</summary>
	private Control BuildLayoutView()
	{
		_layoutRebuilding = true;
		try
		{
			SPanel panel = Ui.V(10.0);
			panel.Children.Add(PTitle(I18n.L("布局")));
			panel.Children.Add(Ui.Muted(I18n.L("默认布局 = 各页现状;这里的改动即时写进 ui_layout_v2.json,老版本程序照旧读得懂")).M(0.0, 0.0, 0.0, 4.0));
			panel.Children.Add(LayoutSection(I18n.L("外观与密度"), LayoutAppearanceRows()));
			panel.Children.Add(LayoutSection(I18n.L("页签与标题栏"), LayoutShellRows()));
			panel.Children.Add(LayoutSection(I18n.L("页面区块"), LayoutBlockRows()));
			panel.Children.Add(LayoutSection(I18n.L("分栏比例"), LayoutSplitRows()));
			panel.Children.Add(LayoutSection(I18n.L("自建区块"), LayoutWidgetRows()));
			panel.Children.Add(LayoutSection(I18n.L("布局方案"), LayoutProfileRows()));
			panel.Children.Add(LayoutJsonCard());
			return ScrollBody(panel);
		}
		finally
		{
			_layoutRebuilding = false;
		}
	}

	/// <summary>
	/// [layout-fix] 规格 §4-能力1:公开的「跳到 设置 → 布局」入口(壳层右键菜单的「布局设置...」走这里)。
	/// 刻意做成**实例方法**、只调一次已有的 <see cref="Select"/> —— 不动任何静态初始化。
	/// </summary>
	public void OpenLayoutView()
	{
		Select(LayoutView);
	}

	/// <summary>重建「布局」视图;当前正显示它时同步换掉宿主内容。</summary>
	private void RefreshLayoutView()
	{
		object? current = _view?.Content;
		Control? old = _views[LayoutView];
		Control fresh = BuildLayoutView();
		_views[LayoutView] = fresh;
		if (_view != null && ReferenceEquals(current, old))
		{
			_view.Content = fresh;
		}
	}

	// 一个小节 = 分组标题 + 卡片(行与行之间有通到卡片两端的分隔线,与设置页其它小节同一套视觉)
	private static Control LayoutSection(string title, params Control[] rows)
	{
		return Ui.V(10.0, Gt(title), Ui.Card(Rows(rows)));
	}

	// ————— a 外观与密度 —————

	private Control[] LayoutAppearanceRows()
	{
		ShellLayout shell = ShellTabs.CurrentShell();
		ComboBox density = Ui.Combo(new string[3] { I18n.L("紧凑"), I18n.L("标准"), I18n.L("宽松") }, DensityLabel(shell.Density), 150.0);
		density.SelectionChanged += delegate
		{
			if (_layoutRebuilding)
			{
				return;
			}
			WriteLayout(delegate(LayoutV2 doc)
			{
				doc.Layout.Shell.Density = DensityKey(density.SelectedIndex);
			}, I18n.L("布局已保存"));
		};
		return new Control[1]
		{
			Ui.Field(I18n.L("密度"), density, I18n.L("只影响区块间距档(紧凑 6 / 标准 10 / 宽松 14)"))
		};
	}

	private static string DensityKey(int index)
	{
		return index switch
		{
			0 => LayoutSchema.DensityCompact,
			2 => LayoutSchema.DensityLoose,
			_ => LayoutSchema.DensityStandard
		};
	}

	private static string DensityLabel(string? density)
	{
		return density switch
		{
			LayoutSchema.DensityCompact => I18n.L("紧凑"),
			LayoutSchema.DensityLoose => I18n.L("宽松"),
			_ => I18n.L("标准")
		};
	}

	// ————— b 页签与标题栏(Q1 只做顶栏页签 + 标题栏元素显隐) —————

	private Control[] LayoutShellRows()
	{
		ShellLayout shell = ShellTabs.CurrentShell();
		List<Control> rows = new List<Control>();
		List<string> order = ShellTabs.OrderedKeys(shell);
		foreach (string key in ShellTabs.BuiltinKeys)
		{
			string tab = key;
			int index = order.IndexOf(tab);
			CheckBox visible = Ui.Switch(ShellTabs.IsVisible(shell, tab));
			if (!ShellTabs.CanHide(tab))
			{
				visible.IsEnabled = false;
				visible.Tip(I18n.L("「设置」页签始终显示,否则没有入口改回来"));
				// [降级] Windows 这里还有 ToolTipService.SetShowOnDisabled(visible, true);
				// Avalonia 11.3.23 没有对应 API(实测 strings 命中数 0)⇒ 禁用态不弹提示。
			}
			// [差异 2] WPF 的 Checked / Unchecked 两分支合成为 IsCheckedChanged(两态控件语义等价)
			visible.IsCheckedChanged += delegate
			{
				if (!_layoutRebuilding)
				{
					ShellTabs.SetTabVisible(tab, visible: visible.IsChecked == true);
					RefreshLayoutView();
				}
			};
			Button up = Ui.IconBtn("\ue70e", I18n.L("上移"), null, 12.0);
			Button down = Ui.IconBtn("\ue70d", I18n.L("下移"), null, 12.0);
			up.IsEnabled = index > 0;
			down.IsEnabled = index >= 0 && index < order.Count - 1;
			up.Click += delegate
			{
				MoveTab(order, tab, -1);
			};
			down.Click += delegate
			{
				MoveTab(order, tab, 1);
			};
			rows.Add(Ui.Field(ShellTabs.Label(tab), Ui.H(8.0, visible, up, down), I18n.L("页签顺序与显隐")));
		}
		TitlebarLayout bar = shell.Titlebar ?? new TitlebarLayout();
		rows.Add(TitlebarRow(I18n.L("桥接"), bar.Bridge, delegate(bool on)
		{
			ShellTabs.SetTitlebar(bridge: on);
		}));
		rows.Add(TitlebarRow(I18n.L("AI"), bar.Ai, delegate(bool on)
		{
			ShellTabs.SetTitlebar(ai: on);
		}));
		rows.Add(TitlebarRow(I18n.L("主题"), bar.Theme, delegate(bool on)
		{
			ShellTabs.SetTitlebar(theme: on);
		}));
		rows.Add(TitlebarRow(I18n.L("账号"), bar.Account, delegate(bool on)
		{
			ShellTabs.SetTitlebar(account: on);
		}));
		return rows.ToArray();
	}

	private Control TitlebarRow(string label, bool value, Action<bool> write)
	{
		CheckBox box = Ui.Switch(value);
		box.IsCheckedChanged += delegate
		{
			if (!_layoutRebuilding)
			{
				write(box.IsChecked == true);
			}
		};
		return Ui.Field(I18n.L("标题栏:{0}", label), box, I18n.L("只隐藏 / 显示,不改形态"));
	}

	private void MoveTab(List<string> order, string key, int direction)
	{
		List<string> list = new List<string>(order);
		int index = list.IndexOf(key);
		int target = index + direction;
		if (index < 0 || target < 0 || target >= list.Count)
		{
			return;
		}
		list.RemoveAt(index);
		list.Insert(target, key);
		ShellTabs.SetTabs(list);
		RefreshLayoutView();
	}

	// ————— c 页面区块入口(复用 LayoutEditor.EnterEdit) —————

	private Control[] LayoutBlockRows()
	{
		List<Control> rows = new List<Control>();
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			string pageId = entry.PageId;
			string navId = entry.NavId;
			int view = entry.SettingsView;
			Button open = Ui.Btn(I18n.L("自定义布局..."), BtnKind.Chip, delegate
			{
				OpenBlockEditor(pageId, navId, view);
			});
			rows.Add(Ui.Field(I18n.L(entry.Label), open, I18n.L("跳到该页并进入区块编辑(完成编辑后自动保存)")));
		}
		rows.Add(Ui.Field(I18n.L("单块页"), Ui.Muted(I18n.L("单块页(下载与网络 / 反馈与关于)也能进编辑态:可以调外观、标题或先隐藏")), ""));
		return rows.ToArray();
	}

	// 跳到目标页 → 找到那一页的 BlockHost → EnterEdit(等价于点右键菜单里的「自定义布局...」)
	private void OpenBlockEditor(string pageId, string navId, int settingsView)
	{
		MainWindow? win = PageBase.Win;
		if (win == null)
		{
			return;
		}
		if (settingsView >= 0)
		{
			Select(settingsView);
		}
		if (navId.Length > 0 && navId != Id)
		{
			win.Navigate(navId);
		}
		// 等页面挂进可视树再找宿主(BlockHost 在页面构造时就已建好,这里只是等一次布局)
		Dispatcher.UIThread.Post(delegate
		{
			EnterBlockEdit(pageId);
		}, DispatcherPriority.Loaded);
	}

	private void EnterBlockEdit(string pageId)
	{
		MainWindow? win = PageBase.Win;
		BlockHost? host = ((win != null) ? FindBlockHost(win, pageId) : null);
		if (host != null && host.Editor.CanEdit)
		{
			host.Editor.EnterEdit();
			PageBase.Toast(I18n.L("已进入自定义布局"), I18n.L("完成编辑后自动保存"), ToastKind.Success);
			return;
		}
		PageBase.Toast(I18n.L("没找到可编辑的区块"), I18n.L("可以在页面上右键「自定义布局...」"), ToastKind.Warning);
	}

	// 视觉树优先(页面已渲染),找不到再退回逻辑树(ElementName/Content 挂载的场合)
	private static BlockHost? FindBlockHost(Visual root, string pageId)
	{
		BlockHost? byVisual = FindBlockHostVisual(root, pageId, 0);
		if (byVisual != null)
		{
			return byVisual;
		}
		return FindBlockHostLogical(root, pageId, 0);
	}

	private static BlockHost? FindBlockHostVisual(Visual root, string pageId, int depth)
	{
		if (depth > 24)
		{
			return null;
		}
		if (root is BlockHost host && host.Editor.State.PageId == pageId)
		{
			return host;
		}
		foreach (Visual child in root.GetVisualChildren())
		{
			BlockHost? found = FindBlockHostVisual(child, pageId, depth + 1);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	private static BlockHost? FindBlockHostLogical(object? root, string pageId, int depth)
	{
		if (depth > 24 || root == null)
		{
			return null;
		}
		if (root is BlockHost host && host.Editor.State.PageId == pageId)
		{
			return host;
		}
		if (root is ILogical logical)
		{
			foreach (ILogical child in logical.GetLogicalChildren())
			{
				BlockHost? found = FindBlockHostLogical(child, pageId, depth + 1);
				if (found != null)
				{
					return found;
				}
			}
		}
		return null;
	}

	// ————— d 分栏比例(现状表直接来自 SplitCatalog,视图里不重复硬编码) —————

	private Control[] LayoutSplitRows()
	{
		List<Control> rows = new List<Control>();
		foreach (SplitSite site in SplitCatalog.All)
		{
			SplitSite current = site;
			string where = "#" + site.Index + "  " + site.PageId + " / " + site.SplitId;
			double? stored = PageLayoutHost.Read(PageBase.Win?.Prefs, site.PageId, site.SplitId);
			string now = SplitNowText(site);
			if (!site.IsTwoColumn)
			{
				// 说明(为什么这处不是可拖分栏)放进提示:表格里保持一行,长文案不把行撑高
				Control plain = Ui.Field(where, Ui.Small(now), SplitKindLabel(site.Kind));
				plain.Tip(site.Note);
				rows.Add(plain);
				continue;
			}
			Button reset = Ui.Btn(I18n.L("恢复默认"), BtnKind.Chip, delegate
			{
				if (PageLayoutHost.Clear(PageBase.Win?.Prefs, current.PageId, current.SplitId))
				{
					PageBase.Toast(I18n.L("已恢复默认"), "", ToastKind.Success);
					RefreshLayoutView();
				}
			});
			reset.IsEnabled = stored.HasValue;
			string cur = (stored.HasValue ? I18n.L("当前 {0:P0}", stored.Value) : I18n.L("当前 = 现状"));
			SPanel right = Ui.H(8.0, Ui.Small(now).VCenter(), Ui.Small(cur).VCenter(), reset);
			Control row = Ui.Field(where, right, SplitKindLabel(site.Kind));
			row.Tip(site.Note);
			rows.Add(row);
		}
		Button resetAll = Ui.Btn(I18n.L("全部复位"), BtnKind.Normal, delegate
		{
			ResetAllSplits();
		});
		rows.Add(Ui.Field(I18n.L("分栏比例"), resetAll, I18n.L("复位只删掉存储比例,回到各页现状列定义")));
		return rows.ToArray();
	}

	private void ResetAllSplits()
	{
		int count = 0;
		foreach (SplitSite site in SplitCatalog.All)
		{
			if (site.IsTwoColumn && PageLayoutHost.Clear(PageBase.Win?.Prefs, site.PageId, site.SplitId))
			{
				count++;
			}
		}
		PageBase.Toast((count > 0) ? I18n.L("已恢复默认") : I18n.L("没有需要复位的分栏"), (count > 0) ? I18n.L("已复位 {0} 处", count) : "", ToastKind.Success);
		RefreshLayoutView();
	}

	// 现状文本:比例 / 像素 / 留白,全部来自 SplitCatalog 的声明
	private static string SplitNowText(SplitSite site)
	{
		if (!site.IsTwoColumn)
		{
			return I18n.L("没有左右分栏");
		}
		string size = ((site.DefaultPixels > 0.0) ? I18n.L("{0}px", site.DefaultPixels) : I18n.L("星号 {0:P0}", site.DefaultRatio));
		string side = ((site.PixelsSide == "right") ? I18n.L("右栏") : I18n.L("左栏"));
		return I18n.L("现状:{0} {1} · 留白 {2}px", side, size, site.Gap);
	}

	private static string SplitKindLabel(string? kind)
	{
		return kind switch
		{
			SplitKind.TwoColumn => I18n.L("左右两栏"),
			SplitKind.Rows => I18n.L("纵向分行"),
			SplitKind.ListRow => I18n.L("列表行内列宽"),
			SplitKind.GridCols => I18n.L("网格列数"),
			SplitKind.HeightClamp => I18n.L("限宽限高"),
			_ => I18n.L("单列")
		};
	}

	// ————— e 自建区块(L6 落地后接在这里) —————

	private Control[] LayoutWidgetRows()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		int count = doc.Layout.Widgets?.Count ?? 0;
		Button add = Ui.Btn(I18n.L("添加自建区块"), BtnKind.Normal, null);
		add.IsEnabled = false;
		return new Control[2]
		{
			Ui.Field(I18n.L("自建区块"), Ui.H(8.0, Ui.Small(I18n.L("已保存 {0} 个", count)).VCenter(), add), I18n.L("便签 / 快捷启动 / 时钟 / 分隔 / 时长概览由下一包(L6)落地后在这里增删改")),
			Ui.Field(I18n.L("危险类型"), Ui.Muted(I18n.L("网页 / 按钮默认关闭,需显式开启(L6)")), "")
		};
	}

	// ————— f 整机布局方案 —————

	private Control[] LayoutProfileRows()
	{
		List<Control> rows = new List<Control>();
		Button create = Ui.Btn(I18n.L("新建布局方案"), BtnKind.Primary, delegate
		{
			PageBase.Run(NewProfileAsync);
		});
		Button import = Ui.Btn(I18n.L("导入布局"), BtnKind.Chip, delegate
		{
			PageBase.Run(ImportLayoutFileAsync);
		});
		Button export = Ui.Btn(I18n.L("导出布局"), BtnKind.Chip, delegate
		{
			PageBase.Run(ExportLayoutAsync);
		});
		string active = LayoutProfiles.ActiveName();
		string activeText = ((active.Length > 0) ? I18n.L("当前方案:{0}", active) : I18n.L("当前是直接编辑态"));
		rows.Add(Ui.Field(I18n.L("布局方案"), Ui.H(8.0, create, import, export), activeText));
		List<string> names = LayoutProfiles.Names();
		if (names.Count == 0)
		{
			rows.Add(Ui.Field(I18n.L("还没有方案"), Ui.Muted(I18n.L("点「新建布局方案」把当前这套布局存下来")), ""));
			return rows.ToArray();
		}
		foreach (string name in names)
		{
			rows.Add(ProfileRow(name, name == active));
		}
		return rows.ToArray();
	}

	private Control ProfileRow(string name, bool active)
	{
		SPanel title = Ui.H(8.0, Ui.Txt(name, 13.5, bold: true).VCenter());
		if (active)
		{
			title.Children.Add(Ui.Tag(I18n.L("当前"), "B.OnAccentStrong", "B.AccentStrong").VCenter());
		}
		Button apply = Ui.Btn(I18n.L("应用"), BtnKind.Soft, delegate
		{
			ApplyProfile(name);
		});
		Button rename = Ui.Btn(I18n.L("重命名"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => RenameProfileAsync(name));
		});
		Button copy = Ui.Btn(I18n.L("复制一份"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => DuplicateProfileAsync(name));
		});
		Button del = Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteProfileAsync(name));
		});
		Grid row = Ui.G(null, "*,Auto");
		row.Add(title.VCenter(), 0);
		row.Add(Ui.H(6.0, apply, rename, copy, del), 0, 1);
		return row;
	}

	private async Task NewProfileAsync()
	{
		string? name = await Dlg.Prompt(I18n.L("新建布局方案"), I18n.L("方案名"), I18n.L("我的布局 {0}", DateTime.Now.ToString("MM-dd HH:mm")));
		if (name == null)
		{
			return;
		}
		string error = LayoutProfiles.Create(name, fromCurrent: true);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("布局方案「{0}」已保存", name), I18n.L("以当前布局为基线"), ToastKind.Success);
		RefreshLayoutView();
	}

	private void ApplyProfile(string name)
	{
		string error = LayoutProfiles.Apply(name, out string kept);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("布局方案「{0}」已应用", name), (kept.Length > 0) ? I18n.L("原来的布局已存为方案「{0}」,可随时切回", kept) : "", ToastKind.Success);
		RefreshLayoutView();
	}

	private async Task RenameProfileAsync(string name)
	{
		string? next = await Dlg.Prompt(I18n.L("重命名"), I18n.L("方案名"), name);
		if (next == null || next == name)
		{
			return;
		}
		string error = LayoutProfiles.Rename(name, next);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		RefreshLayoutView();
	}

	private async Task DuplicateProfileAsync(string name)
	{
		string? next = await Dlg.Prompt(I18n.L("复制一份"), I18n.L("方案名"), I18n.L("{0} 副本", name));
		if (next == null)
		{
			return;
		}
		string error = LayoutProfiles.Duplicate(name, next);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		RefreshLayoutView();
	}

	private async Task DeleteProfileAsync(string name)
	{
		if (!await Dlg.Confirm(I18n.L("删除"), I18n.L("删除布局方案「{0}」?", name), I18n.L("删除"), I18n.L("取消"), danger: true))
		{
			return;
		}
		string error = LayoutProfiles.Delete(name);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("布局方案「{0}」已删除", name), "", ToastKind.Success);
		RefreshLayoutView();
	}

	// ————— f2 导入 / 导出 —————

	private async Task ExportLayoutAsync()
	{
		int choice = await Dlg.Choose(I18n.L("导出布局"), Ui.Muted(I18n.L("整机方案含页签 / 区块 / 分栏比例 / 自建区块;兼容 v1 的页面布局只含每页区块的顺序与显隐")), new string[3]
		{
			I18n.L("取消"),
			I18n.L("导出整机方案(v2)"),
			I18n.L("导出兼容 v1 的页面布局")
		}, 560.0);
		if (choice == 1)
		{
			string text = LayoutStoreV2.ExportJson(LayoutStoreV2.Load());
			string? path = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), "tipcl-layout-v2.json", I18n.L("导出整机方案"));
			if (path != null && WriteTextFile(path, text))
			{
				PageBase.Toast(I18n.L("导出布局"), path, ToastKind.Success);
			}
			return;
		}
		if (choice == 2)
		{
			string text = PageLayoutStore.ExportJson(LayoutV1Projection.ProjectV1(LayoutStoreV2.Load()));
			string? path2 = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), "tipcl-page-layout.json", I18n.L("导出兼容 v1 的页面布局"));
			if (path2 != null && WriteTextFile(path2, text))
			{
				PageBase.Toast(I18n.L("导出布局"), path2, ToastKind.Success);
			}
		}
	}

	private async Task ImportLayoutFileAsync()
	{
		string? path = Dlg.PickFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), I18n.L("导入布局"));
		if (path == null)
		{
			return;
		}
		string text;
		try
		{
			text = File.ReadAllText(path);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局读取失败"), ex.Message, ToastKind.Error);
			return;
		}
		LayoutV2? whole = LayoutV2.FromJsonText(text);
		if (whole != null)
		{
			int choice = await Dlg.Choose(I18n.L("导入布局"), Ui.Muted(I18n.L("这是一份整机方案(含页签 / 区块 / 分栏比例)")), new string[3]
			{
				I18n.L("取消"),
				I18n.L("应用为当前布局"),
				I18n.L("存为方案")
			}, 560.0);
			if (choice == 1)
			{
				string error = LayoutProfiles.ApplySnapshot(whole.Layout, out string kept);
				if (error.Length > 0)
				{
					PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
					return;
				}
				PageBase.Toast(I18n.L("布局已应用"), (kept.Length > 0) ? I18n.L("原来的布局已存为方案「{0}」,可随时切回", kept) : System.IO.Path.GetFileName(path), ToastKind.Success);
				RefreshLayoutView();
			}
			else if (choice == 2)
			{
				string? name = await Dlg.Prompt(I18n.L("存为方案"), I18n.L("方案名"), System.IO.Path.GetFileNameWithoutExtension(path));
				if (name == null)
				{
					return;
				}
				string error2 = LayoutProfiles.CreateFrom(name, whole.Layout);
				if (error2.Length > 0)
				{
					PageBase.Toast(I18n.L("布局未能保存"), error2, ToastKind.Warning);
					return;
				}
				PageBase.Toast(I18n.L("布局方案「{0}」已保存", name), "", ToastKind.Success);
				RefreshLayoutView();
			}
			return;
		}
		// 兼容 v1 的页面布局:把每页的 {id,hidden} 与顺序并进当前文档的 pages
		try
		{
			IReadOnlyDictionary<string, PageLayoutDoc> pages = PageLayoutStore.ParseImport(text);
			LayoutV2 doc = LayoutStoreV2.Load();
			int num = LayoutProfiles.MergeV1Pages(doc, pages);
			LayoutStoreV2.Save(doc);
			PageBase.Toast(I18n.L("已导入 {0} 个页面的布局(下次打开对应页面生效)", num), System.IO.Path.GetFileName(path), ToastKind.Success);
			RefreshLayoutView();
		}
		catch (Exception ex2)
		{
			PageBase.Toast(I18n.L("不是有效的布局文件"), ex2.Message, ToastKind.Error);
		}
	}

	private static bool WriteTextFile(string path, string text)
	{
		try
		{
			File.WriteAllText(path, text);
			return true;
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
			return false;
		}
	}

	// ————— g 高级:布局 JSON 编辑器 —————

	private Border LayoutJsonCard()
	{
		// 「布局 JSON」框必须是**可编辑**的:Ui.LogBox() 的 Input.Log 样式把 IsReadOnly 钉成 True
		// (那是给日志用的只读框)⇒ 这里显式放开;AcceptsTab:敲 Tab 插入缩进而不是跳焦点。
		TextBox box = Ui.LogBox();
		box.IsReadOnly = false;
		box.AcceptsTab = true;
		box.Height = 220.0;
		box.Text = ((_layoutJsonText.Length > 0) ? _layoutJsonText : LayoutJsonEditor.Text(LayoutStoreV2.Load()));
		box.TextChanged += delegate
		{
			_layoutJsonText = box.Text ?? "";
		};
		_layoutJsonIssues = Ui.V(4.0);
		Button check = Ui.Btn(I18n.L("校验"), BtnKind.Normal, delegate
		{
			InspectJson(box, apply: false);
		});
		Button apply = Ui.Btn(I18n.L("应用"), BtnKind.Primary, delegate
		{
			InspectJson(box, apply: true);
		});
		Button reload = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			_layoutJsonText = "";
			box.Text = LayoutJsonEditor.Text(LayoutStoreV2.Load());
			RenderJsonIssues(new List<LayoutJsonIssue>());
		});
		reload.Tip(I18n.L("重新载入当前布局"));
		return Ui.Card(Ui.V(8.0, Ui.Field(I18n.L("布局 JSON"), Ui.V(8.0, box, Ui.H(8.0, check, apply, reload)), I18n.L("可以直接编辑 / 粘贴;点「校验」看结果,校验通过才会落盘")), _layoutJsonIssues));
	}

	private void InspectJson(TextBox box, bool apply)
	{
		string text = box.Text ?? "";
		LayoutV2? doc;
		List<LayoutJsonIssue> issues = (apply ? LayoutJsonEditor.Apply(text, out doc) : LayoutJsonEditor.Inspect(text, out doc));
		RenderJsonIssues(issues);
		bool failed = issues.Any((LayoutJsonIssue i) => i.Error);
		if (failed)
		{
			PageBase.Toast(I18n.L("布局 JSON 有错误"), I18n.L("看下面的具体字段"), ToastKind.Error);
			return;
		}
		if (apply)
		{
			_layoutJsonText = "";
			PageBase.Toast(I18n.L("布局已保存"), (issues.Count > 0) ? I18n.L("有 {0} 处按契约钳位 / 忽略", issues.Count) : "", ToastKind.Success);
			RefreshLayoutView();
		}
		else
		{
			PageBase.Toast(I18n.L("校验通过"), (issues.Count > 0) ? I18n.L("有 {0} 处按契约钳位 / 忽略", issues.Count) : "", ToastKind.Success);
		}
	}

	private void RenderJsonIssues(List<LayoutJsonIssue> issues)
	{
		if (_layoutJsonIssues == null)
		{
			return;
		}
		_layoutJsonIssues.Children.Clear();
		foreach (LayoutJsonIssue issue in issues)
		{
			TextBlock line = Ui.Txt((issue.Path.Length > 0) ? (issue.Path + ": " + issue.Message) : issue.Message, 12.0, bold: false, issue.Error ? "B.Danger" : "B.Warn");
			line.TextWrapping = TextWrapping.Wrap;
			_layoutJsonIssues.Children.Add(line);
		}
	}

	// ————— 共用:落盘唯一入口 = LayoutStoreV2 —————

	// load → 改 → Save;写不进去只提示,不崩界面(与 PageLayoutStore 的既有范式一致)
	private static void WriteLayout(Action<LayoutV2> edit, string toast)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			edit(doc);
			LayoutStoreV2.Save(doc);
			PageBase.Toast(toast, "", ToastKind.Success);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
		}
	}
}