using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TipCL.Pages.Layout;
using TipCL.Services;

namespace TipCL.Pages;

// L5(设计 §4.C 的「布局」视图 + §4.G 的 JSON 编辑器):设置页第 8 个视图,7 个小节 ——
//   a 外观与密度 / b 页签与标题栏 / c 页面区块入口 / d 分栏比例(L4 的 SplitCatalog 直接渲染成表)/
//   e 自建区块(位置先留好,L6 落地后接)/ f 整机布局方案(增删改查 + 导入导出)/ g 高级:布局 JSON 编辑器。
// 三条硬规矩:
//   1) 落盘唯一入口 = LayoutStoreV2(load → 改 → Save);本文件绝不自己写布局文件(导出到用户选定的路径除外);
//   2) 方案应用 = **原子替换** layout(整个 LayoutBody 换掉)再 Save,v1 由 v2 单向写通(不往 v1 塞新字段);
//   3) 本视图自带 18px 左留白(PageBase.ScrollBody 的 SPanel Margin = 18,14,18,16)——L4 把设置页左导航改成
//      分栏后,_view.Margin = -18 归位、分隔条正好占住视图左边那 18px,新视图不留白就会被分隔条盖住。
//
// 纯逻辑(LayoutProfiles / LayoutJsonEditor)刻意零 WPF 依赖:net8 测试宿主能直接断言方案增删改查、
// 原子替换、导入校验与 JSON 编辑器的逐字段错误(见 tests/TipCL.Tests/LayoutProfileTests.cs)。
public sealed partial class SettingsPage
{
	/// <summary>「布局」视图在视图表/左导航里的下标(第 8 个)。</summary>
	internal const int LayoutView = 7;

	// 页面区块入口表在 LayoutBlockEntryCatalog(纯数据、零 WPF 依赖,测试直接断言)。
	// L5b:D1 把 LayoutEditor.CanEdit 从 specs ≥ 2 放宽到 ≥ 1,单块页(settings:network / settings:about)
	// 也能进编辑态,所以表里现在就列全 12 个 BlockHost 宿主(按钮仍以 host.Editor.CanEdit 为最终门槛)。

	private bool _layoutRebuilding;

	// JSON 编辑器的草稿:重建视图时保留用户还没应用的文本
	private string _layoutJsonText = "";

	private SPanel? _layoutJsonIssues;

	// ————— 视图装配 —————

	/// <summary>构「布局」视图(每次切到该视图都重建,保证读到最新状态)。</summary>
	private UIElement BuildLayoutView()
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

	/// <summary>重建「布局」视图;当前正显示它时同步换掉宿主内容。</summary>
	private void RefreshLayoutView()
	{
		object? current = _view?.Content;
		UIElement? old = _views[LayoutView];
		UIElement fresh = BuildLayoutView();
		_views[LayoutView] = fresh;
		if (_view != null && ReferenceEquals(current, old))
		{
			_view.Content = fresh;
		}
	}

	// 一个小节 = 分组标题 + 卡片(行与行之间有通到卡片两端的分隔线,与设置页其它小节同一套视觉)
	private static UIElement LayoutSection(string title, params FrameworkElement[] rows)
	{
		return Ui.V(10.0, Gt(title), Ui.Card(Rows(rows)));
	}

	// ————— a 外观与密度 —————

	private FrameworkElement[] LayoutAppearanceRows()
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
		return new FrameworkElement[1]
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

	private FrameworkElement[] LayoutShellRows()
	{
		ShellLayout shell = ShellTabs.CurrentShell();
		List<FrameworkElement> rows = new List<FrameworkElement>();
		List<string> order = ShellTabs.OrderedKeys(shell);
		foreach (string key in ShellTabs.BuiltinKeys)
		{
			string tab = key;
			int index = order.IndexOf(tab);
			CheckBox visible = Ui.Switch(ShellTabs.IsVisible(shell, tab));
			visible.Checked += delegate
			{
				if (!_layoutRebuilding)
				{
					ShellTabs.SetTabVisible(tab, visible: true);
					RefreshLayoutView();
				}
			};
			visible.Unchecked += delegate
			{
				if (!_layoutRebuilding)
				{
					ShellTabs.SetTabVisible(tab, visible: false);
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

	private FrameworkElement TitlebarRow(string label, bool value, Action<bool> write)
	{
		CheckBox box = Ui.Switch(value);
		box.Checked += delegate
		{
			if (!_layoutRebuilding)
			{
				write(obj: true);
			}
		};
		box.Unchecked += delegate
		{
			if (!_layoutRebuilding)
			{
				write(obj: false);
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

	private FrameworkElement[] LayoutBlockRows()
	{
		List<FrameworkElement> rows = new List<FrameworkElement>();
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
		base.Dispatcher.InvokeAsync((Action)delegate
		{
			EnterBlockEdit(pageId);
		}, System.Windows.Threading.DispatcherPriority.Loaded);
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
	private static BlockHost? FindBlockHost(DependencyObject root, string pageId)
	{
		BlockHost? byVisual = FindBlockHostVisual(root, pageId, 0);
		if (byVisual != null)
		{
			return byVisual;
		}
		return FindBlockHostLogical(root, pageId, 0);
	}

	private static BlockHost? FindBlockHostVisual(DependencyObject root, string pageId, int depth)
	{
		if (depth > 24 || root == null)
		{
			return null;
		}
		if (root is BlockHost host && host.Editor.State.PageId == pageId)
		{
			return host;
		}
		int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			BlockHost? found = FindBlockHostVisual(System.Windows.Media.VisualTreeHelper.GetChild(root, i), pageId, depth + 1);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	private static BlockHost? FindBlockHostLogical(object root, string pageId, int depth)
	{
		if (depth > 24 || root == null)
		{
			return null;
		}
		if (root is BlockHost host && host.Editor.State.PageId == pageId)
		{
			return host;
		}
		if (root is DependencyObject dep)
		{
			foreach (object child in LogicalTreeHelper.GetChildren(dep))
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

	// ————— d 分栏比例(现状表直接来自 L4 的 SplitCatalog,视图里不重复硬编码) —————

	private FrameworkElement[] LayoutSplitRows()
	{
		List<FrameworkElement> rows = new List<FrameworkElement>();
		foreach (SplitSite site in SplitCatalog.All)
		{
			SplitSite current = site;
			string where = "#" + site.Index + "  " + site.PageId + " / " + site.SplitId;
			double? stored = SplitRatioStore.Read(site.PageId, site.SplitId);
			string now = SplitNowText(site);
			if (!site.IsTwoColumn)
			{
				// 说明(为什么这处不是可拖分栏)放进 ToolTip:表格里保持一行,长文案不把行撑高
				FrameworkElement plain = Ui.Field(where, Ui.Small(now), SplitKindLabel(site.Kind));
				plain.ToolTip = site.Note;
				rows.Add(plain);
				continue;
			}
			Button reset = Ui.Btn(I18n.L("恢复默认"), BtnKind.Chip, delegate
			{
				if (SplitRatioStore.Clear(current.PageId, current.SplitId))
				{
					PageBase.Toast(I18n.L("已恢复默认"), "", ToastKind.Success);
					RefreshLayoutView();
				}
			});
			reset.IsEnabled = stored.HasValue;
			string cur = (stored.HasValue ? I18n.L("当前 {0:P0}", stored.Value) : I18n.L("当前 = 现状"));
			SPanel right = Ui.H(8.0, Ui.Small(now).VCenter(), Ui.Small(cur).VCenter(), reset);
			FrameworkElement row = Ui.Field(where, right, SplitKindLabel(site.Kind));
			row.ToolTip = site.Note;
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
			if (site.IsTwoColumn && SplitRatioStore.Clear(site.PageId, site.SplitId))
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

	private FrameworkElement[] LayoutWidgetRows()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		int count = doc.Layout.Widgets?.Count ?? 0;
		Button add = Ui.Btn(I18n.L("添加自建区块"), BtnKind.Normal, null);
		add.IsEnabled = false;
		return new FrameworkElement[2]
		{
			Ui.Field(I18n.L("自建区块"), Ui.H(8.0, Ui.Small(I18n.L("已保存 {0} 个", count)).VCenter(), add), I18n.L("便签 / 快捷启动 / 时钟 / 分隔 / 时长概览由下一包(L6)落地后在这里增删改")),
			Ui.Field(I18n.L("危险类型"), Ui.Muted(I18n.L("网页 / 按钮默认关闭,需显式开启(L6)")), "")
		};
	}

	// ————— f 整机布局方案 —————

	private FrameworkElement[] LayoutProfileRows()
	{
		List<FrameworkElement> rows = new List<FrameworkElement>();
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

	private FrameworkElement ProfileRow(string name, bool active)
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

	private async System.Threading.Tasks.Task NewProfileAsync()
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
		string error = LayoutProfiles.Apply(name);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("布局已应用,可直接「另存为方案」保留"), name, ToastKind.Success);
		RefreshLayoutView();
	}

	private async System.Threading.Tasks.Task RenameProfileAsync(string name)
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

	private async System.Threading.Tasks.Task DuplicateProfileAsync(string name)
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

	private async System.Threading.Tasks.Task DeleteProfileAsync(string name)
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

	private async System.Threading.Tasks.Task ExportLayoutAsync()
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
			string text = PageLayoutStore.ExportJson(LayoutStoreV2.ProjectV1(LayoutStoreV2.Load()));
			string? path2 = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), "tipcl-page-layout.json", I18n.L("导出兼容 v1 的页面布局"));
			if (path2 != null && WriteTextFile(path2, text))
			{
				PageBase.Toast(I18n.L("导出布局"), path2, ToastKind.Success);
			}
		}
	}

	private async System.Threading.Tasks.Task ImportLayoutFileAsync()
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
				string error = LayoutProfiles.ApplySnapshot(whole.Layout);
				if (error.Length > 0)
				{
					PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
					return;
				}
				PageBase.Toast(I18n.L("布局已应用,可直接「另存为方案」保留"), System.IO.Path.GetFileName(path), ToastKind.Success);
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
		TextBox box = Ui.LogBox();
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
		reload.ToolTip = I18n.L("重新载入当前布局");
		return Ui.Card(Ui.V(8.0, Ui.Field(I18n.L("布局 JSON"), Ui.V(8.0, box, Ui.H(8.0, check, apply, reload)), I18n.L("校验通过才会落盘;非法时按字段给出原因")), _layoutJsonIssues));
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

/// <summary>
/// 设置页「布局」视图里「页面区块」小节的入口表(纯数据、零 WPF 依赖,net8 测试宿主可直接断言):
/// 覆盖全仓 12 个 BlockHost 宿主。D1 把 <c>LayoutEditor.CanEdit</c> 从 specs ≥ 2 放宽到 ≥ 1 之后,
/// 单块页(settings:network / settings:about)同样能进编辑态,所以表里一并列出
/// —— 单块页能调的东西少(外观 / 标题 / 先隐藏),但入口不该缺。
/// </summary>
public static class LayoutBlockEntryCatalog
{
	/// <summary>一个页面区块入口:pageId + 导航 id + 设置页视图下标(-1 = 不在设置页里)+ 文案 key。</summary>
	public sealed class Entry
	{
		/// <summary>布局文档里的 pageId(与 BlockHost 构造时传的 pageId 一致)。</summary>
		public string PageId { get; set; } = "";

		/// <summary>MainWindow 的导航 id(先跳到该页,再找它的 BlockHost)。</summary>
		public string NavId { get; set; } = "";

		/// <summary>该页在设置页里时先切到哪个视图;≥ 0 表示先 Select(该视图)再找宿主,-1 = 不在设置页里。</summary>
		public int SettingsView { get; set; } = -1;

		/// <summary>入口文案的 I18n key(复用既有导航文案)。</summary>
		public string Label { get; set; } = "";

		/// <summary>是否单块页(只有一个块:能调外观 / 标题 / 隐藏,不能重排)。</summary>
		public bool SingleBlock { get; set; }
	}

	/// <summary>12 个宿主:设置页内 7 个 + 顶栏导航 5 个。</summary>
	public static readonly Entry[] All = new Entry[12]
	{
		new Entry
		{
			PageId = "settings:general",
			NavId = "settings",
			SettingsView = 0,
			Label = "通用"
		},
		new Entry
		{
			PageId = "settings:network",
			NavId = "settings",
			SettingsView = 3,
			Label = "下载与网络",
			SingleBlock = true
		},
		new Entry
		{
			PageId = "settings:about",
			NavId = "settings",
			SettingsView = 6,
			Label = "反馈与关于",
			SingleBlock = true
		},
		new Entry
		{
			PageId = "java",
			NavId = "settings",
			SettingsView = 2,
			Label = "Java"
		},
		new Entry
		{
			PageId = "servers",
			NavId = "settings",
			SettingsView = 4,
			Label = "服务器"
		},
		new Entry
		{
			PageId = "playtime",
			NavId = "settings",
			SettingsView = 5,
			Label = "游戏时长"
		},
		new Entry
		{
			PageId = "feedback",
			NavId = "settings",
			SettingsView = 6,
			Label = "反馈与帮助"
		},
		new Entry
		{
			PageId = "launch",
			NavId = "launch",
			Label = "启动"
		},
		new Entry
		{
			PageId = "download",
			NavId = "download",
			Label = "下载"
		},
		new Entry
		{
			PageId = "account",
			NavId = "account",
			Label = "账号"
		},
		new Entry
		{
			PageId = "tasks",
			NavId = "tasks",
			Label = "下载任务"
		},
		new Entry
		{
			PageId = "multiplayer",
			NavId = "multiplayer",
			Label = "联机"
		}
	};

	/// <summary>单块页的 pageId 列表(报告与测试用)。</summary>
	public static List<string> SingleBlockPageIds()
	{
		List<string> list = new List<string>();
		foreach (Entry entry in All)
		{
			if (entry.SingleBlock)
			{
				list.Add(entry.PageId);
			}
		}
		return list;
	}

	/// <summary>按 pageId 取入口(没有返回 null)。</summary>
	public static Entry? Find(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		foreach (Entry entry in All)
		{
			if (entry.PageId == pageId)
			{
				return entry;
			}
		}
		return null;
	}
}

/// <summary>
/// 整机布局方案(设计 §4.C):纯逻辑、零 WPF 依赖,落盘一律走 LayoutStoreV2。
/// 方法返回 "" = 成功,否则是给用户看的中文错误;应用方案 = 原子替换整个 layout 再 Save。
/// </summary>
public static class LayoutProfiles
{
	/// <summary>方案名长度上限(防御:避免超长 key 撑爆 JSON);界面与校验共用同一个数。</summary>
	public const int ProfileNameMax = 40;

	/// <summary>方案名列表(文档顺序)。</summary>
	public static List<string> Names()
	{
		try
		{
			return LayoutStoreV2.Load().Profiles.Keys.ToList();
		}
		catch
		{
			return new List<string>();
		}
	}

	/// <summary>方案个数。</summary>
	public static int Count()
	{
		return Names().Count;
	}

	/// <summary>当前激活的方案名(空 = 直接编辑态)。</summary>
	public static string ActiveName()
	{
		try
		{
			return LayoutStoreV2.Load().ActiveProfile ?? "";
		}
		catch
		{
			return "";
		}
	}

	/// <summary>取方案快照(null = 不存在)。</summary>
	public static LayoutBody? Snapshot(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		try
		{
			return LayoutStoreV2.Load().GetProfile(name);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>方案名校验:"" = 可用。</summary>
	public static string ValidateName(string? name, string? ignoreExisting)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return I18n.L("方案名不能为空");
		}
		string trimmed = name.Trim();
		if (trimmed.Length > ProfileNameMax)
		{
			return I18n.L("方案名太长(最多 {0} 个字)", ProfileNameMax);
		}
		if (trimmed != ignoreExisting && Names().Contains(trimmed))
		{
			return I18n.L("方案「{0}」已经存在", trimmed);
		}
		return "";
	}

	/// <summary>新建方案:fromCurrent = true 时以当前 layout 为基线(§4.C「新建(以当前为基线)」)。</summary>
	public static string Create(string? name, bool fromCurrent)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			string error = ValidateName(name, null);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = name!.Trim();
			doc.Profiles[trimmed] = (fromCurrent ? doc.Layout.Clone() : new LayoutBody());
			doc.ActiveProfile = trimmed;
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>用给定快照建方案(导入整机方案时用)。</summary>
	public static string CreateFrom(string? name, LayoutBody? body)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			string error = ValidateName(name, null);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = name!.Trim();
			doc.Profiles[trimmed] = ((body != null) ? body.Clone() : new LayoutBody());
			doc.ActiveProfile = trimmed;
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>应用方案:原子替换 layout(整块换掉)并把 active_profile 记成它。</summary>
	public static string Apply(string? name)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutBody? body = doc.GetProfile(name ?? "");
			if (body == null)
			{
				return I18n.L("布局方案「{0}」不存在", name ?? "");
			}
			doc.Layout = body.Clone();
			doc.ActiveProfile = name!.Trim();
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>直接替换当前布局(导入整机方案):原子替换 + 回到直接编辑态(active_profile 清空)。</summary>
	public static string ApplySnapshot(LayoutBody? body)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout = ((body != null) ? body.Clone() : new LayoutBody());
			doc.ActiveProfile = "";
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>重命名方案(被激活的方案跟着改 active_profile)。</summary>
	public static string Rename(string? oldName, string? newName)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (string.IsNullOrEmpty(oldName) || !doc.Profiles.ContainsKey(oldName))
			{
				return I18n.L("布局方案「{0}」不存在", oldName ?? "");
			}
			string error = ValidateName(newName, oldName);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = newName!.Trim();
			if (trimmed == oldName)
			{
				return "";
			}
			// 保序重建:方案列表的显示顺序不能因为改名而跳
			Dictionary<string, LayoutBody> rebuilt = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, LayoutBody> kv in doc.Profiles)
			{
				rebuilt[(kv.Key == oldName) ? trimmed : kv.Key] = kv.Value;
			}
			doc.Profiles = rebuilt;
			if (doc.ActiveProfile == oldName)
			{
				doc.ActiveProfile = trimmed;
			}
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>复制方案。</summary>
	public static string Duplicate(string? name, string? newName)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutBody? body = doc.GetProfile(name ?? "");
			if (body == null)
			{
				return I18n.L("布局方案「{0}」不存在", name ?? "");
			}
			string error = ValidateName(newName, null);
			if (error.Length > 0)
			{
				return error;
			}
			doc.Profiles[newName!.Trim()] = body.Clone();
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>删除方案(删的是当前激活方案时,active_profile 回到直接编辑态)。</summary>
	public static string Delete(string? name)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (string.IsNullOrEmpty(name) || !doc.Profiles.Remove(name))
			{
				return I18n.L("布局方案「{0}」不存在", name ?? "");
			}
			if (doc.ActiveProfile == name)
			{
				doc.ActiveProfile = "";
			}
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>
	/// 把 v1 页面布局(冻结格式,只有 {id,hidden} 与顺序)并进当前文档的 pages:按 pageId 覆盖,
	/// 其余页不动;不落盘(调用方决定)。返回并入的页数。
	/// </summary>
	public static int MergeV1Pages(LayoutV2? doc, IReadOnlyDictionary<string, PageLayoutDoc>? pages)
	{
		if (doc?.Layout == null || pages == null)
		{
			return 0;
		}
		int num = 0;
		foreach (KeyValuePair<string, PageLayoutDoc> kv in pages)
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			LayoutV2 migrated = LayoutStoreV2.FromV1(new KeyValuePair<string, PageLayoutDoc>[1]
			{
				kv
			});
			if (migrated.Layout.Pages.TryGetValue(kv.Key, out PageLayoutV2? page) && page != null)
			{
				doc.Layout.Pages[kv.Key] = page;
				num++;
			}
		}
		return num;
	}
}

/// <summary>布局 JSON 编辑器的一条问题(具体字段路径 + 中文原因);Error = 阻止落盘的硬错误。</summary>
public sealed class LayoutJsonIssue
{
	/// <summary>是否硬错误(错误 = 不应用;警告 = 已按契约钳位 / 忽略后照常应用)。</summary>
	public bool Error { get; set; }

	/// <summary>字段路径(如 <c>layout.splits["library"]["folders"]</c>);空串表示整份文档的问题。</summary>
	public string Path { get; set; } = "";

	/// <summary>中文原因。</summary>
	public string Message { get; set; } = "";
}

/// <summary>
/// 布局 JSON 编辑器(设计 §4.G)的校验与应用:纯逻辑、零 WPF 依赖。
/// 校验按 §3.2 的契约逐字段来:version/kind 门禁不通过 = 硬错误;白名单外的取值 / 越界数值 =
/// 警告(会由 LayoutSchema.Sanitize 钳位或忽略),错误时**不落盘**。
/// </summary>
public static class LayoutJsonEditor
{
	/// <summary>编辑器文本上限(防御:超大粘贴直接把界面拖死)。</summary>
	public const int MaxTextLength = 524288;

	private static readonly string[] KnownTopKeys = new string[7] { "version", "kind", "saved_at", "app", "active_profile", "layout", "profiles" };

	/// <summary>当前文档的 JSON 文本(编辑器的初始内容)。</summary>
	public static string Text(LayoutV2? doc)
	{
		try
		{
			LayoutV2 body = ((doc != null) ? doc : LayoutStoreV2.Load());
			return body.ToJson();
		}
		catch
		{
			return "{}";
		}
	}

	/// <summary>只校验(不落盘)。返回的问题里只要有一条 Error 就不该应用。</summary>
	public static List<LayoutJsonIssue> Inspect(string? json, out LayoutV2? doc)
	{
		doc = null;
		List<LayoutJsonIssue> issues = new List<LayoutJsonIssue>();
		string text = json ?? "";
		if (text.Trim().Length == 0)
		{
			issues.Add(Error("", I18n.L("内容是空的")));
			return issues;
		}
		if (text.Length > MaxTextLength)
		{
			issues.Add(Error("", I18n.L("内容太长(超过 {0} KB)", MaxTextLength / 1024)));
			return issues;
		}
		JsonDocument parsed;
		try
		{
			parsed = JsonDocument.Parse(text);
		}
		catch (JsonException ex)
		{
			issues.Add(Error("", I18n.L("不是合法的 JSON:{0}", ex.Message)));
			return issues;
		}
		catch (Exception ex2)
		{
			issues.Add(Error("", I18n.L("不是合法的 JSON:{0}", ex2.Message)));
			return issues;
		}
		try
		{
			JsonElement root = parsed.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				issues.Add(Error("", I18n.L("最外层必须是 JSON 对象")));
				return issues;
			}
			string kind = Text(root, "kind");
			if (kind.Length == 0)
			{
				issues.Add(Error("kind", I18n.L("缺少必填字段 kind(应为 {0})", LayoutSchema.Kind)));
			}
			else if (kind != LayoutSchema.Kind)
			{
				issues.Add(Error("kind", I18n.L("kind 必须是 {0}(当前 {1})", LayoutSchema.Kind, kind)));
			}
			if (!root.TryGetProperty("version", out JsonElement version))
			{
				issues.Add(Error("version", I18n.L("缺少必填字段 version(应为 {0})", LayoutSchema.CurrentVersion)));
			}
			else if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int versionValue) || versionValue != LayoutSchema.CurrentVersion)
			{
				issues.Add(Error("version", I18n.L("version 必须是 {0}(当前 {1})", LayoutSchema.CurrentVersion, version.ToString())));
			}
			if (issues.Any((LayoutJsonIssue i) => i.Error))
			{
				return issues;
			}
			foreach (JsonProperty prop in root.EnumerateObject())
			{
				if (!KnownTopKeys.Contains(prop.Name))
				{
					issues.Add(Warn(prop.Name, I18n.L("未知字段:保留但不生效")));
				}
			}
			if (LayoutJson.TryObject(root, "layout", out JsonElement layout))
			{
				InspectBody(layout, "layout", issues);
			}
			else
			{
				issues.Add(Warn("layout", I18n.L("缺少 layout:应用后会得到一份空布局")));
			}
			if (LayoutJson.TryObject(root, "profiles", out JsonElement profiles))
			{
				foreach (JsonProperty item in profiles.EnumerateObject())
				{
					if (item.Value.ValueKind == JsonValueKind.Object)
					{
						InspectBody(item.Value, "profiles[\"" + item.Name + "\"]", issues);
					}
				}
			}
			else if (root.TryGetProperty("profiles", out JsonElement rawProfiles) && rawProfiles.ValueKind != JsonValueKind.Object)
			{
				issues.Add(Warn("profiles", I18n.L("必须是对象:已按空处理")));
			}
			doc = LayoutV2.FromJsonText(text);
			if (doc == null)
			{
				issues.Add(Error("", I18n.L("布局文件无效")));
			}
		}
		finally
		{
			parsed.Dispose();
		}
		return issues;
	}

	/// <summary>校验 + 落盘:没有硬错误时 LayoutStoreV2.Save;返回问题列表(Error 存在即未落盘)。</summary>
	public static List<LayoutJsonIssue> Apply(string? json, out LayoutV2? doc)
	{
		List<LayoutJsonIssue> issues = Inspect(json, out doc);
		if (issues.Any((LayoutJsonIssue i) => i.Error))
		{
			return issues;
		}
		try
		{
			LayoutStoreV2.Save(((doc != null) ? doc : LayoutStoreV2.Load()));
		}
		catch (Exception ex)
		{
			issues.Add(Error("", ex.Message));
		}
		return issues;
	}

	// ————— LAYOUT 对象逐字段检查 —————

	private static void InspectBody(JsonElement body, string path, List<LayoutJsonIssue> issues)
	{
		if (LayoutJson.TryObject(body, "shell", out JsonElement shell))
		{
			InspectShell(shell, path + ".shell", issues);
		}
		if (LayoutJson.TryObject(body, "pages", out JsonElement pages))
		{
			foreach (JsonProperty page in pages.EnumerateObject())
			{
				if (page.Value.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(path + ".pages[\"" + page.Name + "\"]", I18n.L("必须是对象:该页已忽略")));
					continue;
				}
				InspectPage(page.Value, path + ".pages[\"" + page.Name + "\"]", issues);
			}
		}
		if (LayoutJson.TryObject(body, "splits", out JsonElement splits))
		{
			foreach (JsonProperty page2 in splits.EnumerateObject())
			{
				if (page2.Value.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(path + ".splits[\"" + page2.Name + "\"]", I18n.L("必须是对象:该页分栏已忽略")));
					continue;
				}
				foreach (JsonProperty split in page2.Value.EnumerateObject())
				{
					string where = path + ".splits[\"" + page2.Name + "\"][\"" + split.Name + "\"]";
					if (split.Value.ValueKind != JsonValueKind.Number)
					{
						issues.Add(Warn(where, I18n.L("比例必须是数字:该处已忽略")));
						continue;
					}
					double ratio = split.Value.GetDouble();
					if (ratio < LayoutSchema.RatioMin || ratio > LayoutSchema.RatioMax)
					{
						issues.Add(Warn(where, I18n.L("比例夹到 {0}..{1}(当前 {2})", LayoutSchema.RatioMin, LayoutSchema.RatioMax, ratio)));
					}
				}
			}
		}
		if (LayoutJson.TryArray(body, "widgets", out JsonElement widgets))
		{
			int index = 0;
			foreach (JsonElement widget in widgets.EnumerateArray())
			{
				string where2 = path + ".widgets[" + index + "]";
				index++;
				if (widget.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(where2, I18n.L("必须是对象:该自建区块已忽略")));
					continue;
				}
				string type = Text(widget, "type");
				if (type.Length > 0 && !LayoutSchema.IsKnownWidgetType(type))
				{
					issues.Add(Warn(where2 + ".type", I18n.L("未知类型 {0}:块会保留为「不可用」", type)));
				}
			}
		}
	}

	private static void InspectShell(JsonElement shell, string path, List<LayoutJsonIssue> issues)
	{
		CheckChoice(shell, "density", new string[3] { LayoutSchema.DensityCompact, LayoutSchema.DensityStandard, LayoutSchema.DensityLoose }, path, issues);
		CheckChoice(shell, "nav_mode", new string[2] { LayoutSchema.NavModeTabs, LayoutSchema.NavModeSidebar }, path, issues);
		CheckChoice(shell, "nav_side", new string[2] { LayoutSchema.NavSideLeft, LayoutSchema.NavSideRight }, path, issues);
		CheckChoice(shell, "nav_style", new string[2] { LayoutSchema.NavStyleCompact, LayoutSchema.NavStyleGrouped }, path, issues);
		if (shell.TryGetProperty("nav_width", out JsonElement navWidth) && navWidth.ValueKind == JsonValueKind.Number)
		{
			double width = navWidth.GetDouble();
			if (width < LayoutSchema.NavWidthMin || width > LayoutSchema.NavWidthMax)
			{
				issues.Add(Warn(path + ".nav_width", I18n.L("侧栏宽夹到 {0}..{1}(当前 {2})", LayoutSchema.NavWidthMin, LayoutSchema.NavWidthMax, width)));
			}
		}
		if (shell.TryGetProperty("tabs", out JsonElement tabs))
		{
			if (tabs.ValueKind != JsonValueKind.Array)
			{
				issues.Add(Warn(path + ".tabs", I18n.L("必须是数组:已回落到默认页签")));
			}
			else
			{
				foreach (JsonElement tab in tabs.EnumerateArray())
				{
					string key = ((tab.ValueKind == JsonValueKind.String) ? (tab.GetString() ?? "") : "");
					if (!LayoutSchema.IsKnownTab(key))
					{
						issues.Add(Warn(path + ".tabs", I18n.L("未知页签 {0}:已丢弃", key)));
					}
				}
			}
		}
	}

	private static void InspectPage(JsonElement page, string path, List<LayoutJsonIssue> issues)
	{
		if (page.TryGetProperty("version", out JsonElement version))
		{
			if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int num) || num != LayoutSchema.CurrentVersion)
			{
				issues.Add(Warn(path + ".version", I18n.L("单页 version 必须是 {0}:该页已按默认处理", LayoutSchema.CurrentVersion)));
			}
		}
		if (page.TryGetProperty("mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String)
		{
			string text = mode.GetString() ?? "";
			if (!LayoutSchema.IsKnownMode(text))
			{
				issues.Add(Warn(path + ".mode", I18n.L("未知模式 {0}:已按 stack 处理", text)));
			}
		}
		if (page.TryGetProperty("cols", out JsonElement cols) && cols.ValueKind == JsonValueKind.Number && cols.TryGetInt32(out int colValue))
		{
			if (LayoutSchema.SnapCols(colValue) != colValue)
			{
				issues.Add(Warn(path + ".cols", I18n.L("列数只认 2/3/4/6/12:已吸附到 {0}", LayoutSchema.SnapCols(colValue))));
			}
		}
		CheckRange(page, "gap", LayoutSchema.GapMin, LayoutSchema.GapMax, path, issues);
		CheckRange(page, "row_h", LayoutSchema.RowHMin, LayoutSchema.RowHMax, path, issues);
		if (LayoutJson.TryArray(page, "blocks", out JsonElement blocks))
		{
			int index = 0;
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonElement block in blocks.EnumerateArray())
			{
				string where = path + ".blocks[" + index + "]";
				index++;
				if (block.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(where, I18n.L("必须是对象:该块已忽略")));
					continue;
				}
				string id = Text(block, "id");
				if (id.Length == 0)
				{
					issues.Add(Warn(where + ".id", I18n.L("缺少块 id:该块已忽略")));
				}
				else if (!seen.Add(id))
				{
					issues.Add(Warn(where + ".id", I18n.L("块 id 重复({0}):重复的那块已忽略", id)));
				}
				if (block.TryGetProperty("style", out JsonElement style) && style.ValueKind == JsonValueKind.String)
				{
					string styleText = style.GetString() ?? "";
					if (!LayoutSchema.IsKnownStyle(styleText))
					{
						issues.Add(Warn(where + ".style", I18n.L("未知外观 {0}:已按 plain 处理", styleText)));
					}
				}
				if (block.TryGetProperty("hidden", out JsonElement hidden) && hidden.ValueKind != JsonValueKind.True && hidden.ValueKind != JsonValueKind.False)
				{
					issues.Add(Warn(where + ".hidden", I18n.L("只认 true / false:已按 false 处理")));
				}
				CheckRange(block, "min_h", LayoutSchema.MinHMin, LayoutSchema.MinHMax, where, issues);
			}
		}
	}

	private static void CheckChoice(JsonElement obj, string name, string[] allowed, string path, List<LayoutJsonIssue> issues)
	{
		if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return;
		}
		string text = value.GetString() ?? "";
		if (!allowed.Contains(text))
		{
			issues.Add(Warn(path + "." + name, I18n.L("取值不合法({0}):已回落到 {1}", text, allowed[0])));
		}
	}

	private static void CheckRange(JsonElement obj, string name, double min, double max, string path, List<LayoutJsonIssue> issues)
	{
		if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
		{
			return;
		}
		double num = value.GetDouble();
		if (num < min || num > max)
		{
			issues.Add(Warn(path + "." + name, I18n.L("取值超出 {0}..{1}:已夹取(当前 {2})", min, max, num)));
		}
	}

	private static string Text(JsonElement obj, string name)
	{
		if (obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? "";
		}
		return "";
	}

	private static LayoutJsonIssue Error(string path, string message)
	{
		return new LayoutJsonIssue
		{
			Error = true,
			Path = path,
			Message = message
		};
	}

	private static LayoutJsonIssue Warn(string path, string message)
	{
		return new LayoutJsonIssue
		{
			Error = false,
			Path = path,
			Message = message
		};
	}
}
