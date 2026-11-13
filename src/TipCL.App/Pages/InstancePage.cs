using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C4:Windows 版 <c>Pages\InstancePage.cs</c>(16780 字节 / 560 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **实例管理页**(导航 id <c>instance</c>,导航键 <c>instance</c> 在 mac 的导航表里是「版本管理」那一行)。
///
/// 形状(逐条照抄 Windows):一张 <c>Ui.Section</c> 头(标题「实例」+ 副说明「每个实例是一份独立的 .minecraft,互不干扰」
/// + 计数 + 过滤框 + 打开游戏目录 / 导入官方启动器 / 刷新 / 新建实例)+ 一列实例卡
/// (圆角 10、描边 1、内边距 14/12,图标 40×40 圆角 10;名称 + 副行「整合包 版本」或 MC 版本;
/// 标签行 = 默认 / N 个版本 / Java 标签 / MC 版本;操作 = 启动 + 模组 + 存档 + 更多)。
///
/// 行为(与 Windows 一致):<c>get_instances</c> + <c>get_setting(default_instance)</c> 起表;
/// 过滤框 220ms 防抖后才重画(切页会 <see cref="OnHidden"/> 停表);
/// 键盘上下移动选中、回车启动(焦点在输入框里时全部不响应);
/// 双击卡 = 启动、右键卡 = 那份 12 项的菜单(含「挑着更新模组...」「后台检查模组更新」「导出为整合包」)。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;<c>Visibility</c> → <c>IsVisible</c>;</item>
/// <item><c>StackPanel.FocusVisualStyle = null</c> → <b>去掉</b>:Avalonia 11 的 <c>Control</c> 没有这个属性
///   (焦点可视化在 Avalonia 由主题与 <c>:focus</c> 伪类管),保留 <c>Focusable = true</c>;</item>
/// <item><c>PreviewKeyDown</c>(WPF 的隧道事件)→ <c>AddHandler(KeyDownEvent, ..., RoutingStrategies.Tunnel)</c>
///   (Avalonia 的 <c>KeyDown</c> 直接订阅只有冒泡段;隧道段要用 <c>AddHandler</c> 明确要);</item>
/// <item><c>Keyboard.FocusedElement</c>(WPF 静态入口)→ <c>TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()</c>
///   (Avalonia 把"当前焦点元素"交给焦点管理器;判据仍是"是不是 TextBox");</item>
/// <item><c>MouseLeftButtonDown</c> / <c>MouseRightButtonUp</c> → <c>PointerPressed</c> / <c>PointerReleased</c> +
///   <c>e.GetCurrentPoint(...).Properties.IsRightButtonPressed</c>(Avalonia 没有"右键按下/抬起"两个独立事件);</item>
/// <item><c>e.OriginalSource as DependencyObject</c> → <c>e.Source as Visual</c>,
///   <c>VisualTreeHelper.GetParent(d)</c> → <c>d.GetVisualParent()</c>(Avalonia 11 没有 VisualTreeHelper 这个类,
///   等价物是 <c>Avalonia.VisualTree</c> 的扩展方法;见 <see cref="FindButton"/>);</item>
/// <item><c>menu.PlacementTarget = anchor; menu.IsOpen = true;</c> → <c>menu.Open(anchor)</c>(见 D-124);
///   菜单本体拆成 <see cref="BuildRowMenu"/> 以便告知式驱动(同 D-115 / D-127 的口径);</item>
/// <item>状态机抽成 internal 成员(<see cref="Select"/> / <see cref="HandleNavKey"/>),事件处理器只做
///   "取坐标 / 取按键 / 判左右键"那一层 —— 用例因此**不构造 Pointer/Key 事件对象**(D-115)。</item>
/// </list>
/// </summary>
public sealed class InstancePage : PageBase
{
	/// <summary>一张实例卡(Windows 的私有 <c>Row</c>):记住根、名称、副行、标签行与"当时画的是哪份数据"。</summary>
	private sealed class Row
	{
		public InstanceInfo Info = new InstanceInfo();

		public bool Default;

		public bool Hover;

		public required Border Root { get; init; }

		public required TextBlock Name { get; init; }

		public required TextBlock Sub { get; init; }

		public required SPanel Chips { get; init; }

		public required string Key { get; init; }
	}

	// 【Windows 源】:Focusable=true(可键盘选中),FocusVisualStyle=null 在 Avalonia 没有对应(见类注释)
	private readonly StackPanel _list = new StackPanel
	{
		Focusable = true
	};

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBox _search = Ui.Input(I18n.L("过滤实例名"), "", 200.0);

	private readonly SPanel _empty = Ui.Empty("\ue80a", I18n.L("还没有实例"), I18n.L("点右上角「新建实例」建一个"));

	private readonly TextBlock _emptyTitle;

	/// <summary>过滤框的 220ms 防抖(Windows 是 <c>System.Windows.Threading.DispatcherTimer</c>,Avalonia 同名同义)。</summary>
	private readonly DispatcherTimer _debounce = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(220.0)
	};

	private readonly Dictionary<string, Row> _cards = new Dictionary<string, Row>();

	private readonly List<Row> _order = new List<Row>();

	private string? _selected;

	private List<InstanceInfo> _rows = new List<InstanceInfo>();

	private string _defaultInstance = "";

	public override string Title => I18n.L("实例");

	public InstancePage()
	{
		// 【Windows 源】:空态组件里第 2 个 TextBlock 是标题(第 1 个是字形),过滤没命中时改它的文案
		_emptyTitle = _empty.Children.OfType<TextBlock>().Skip(1).First();
		Button create = Ui.Btn(I18n.L("新建实例"), BtnKind.Primary, delegate
		{
			Run(CreateAsync);
		}, "\ue710");
		Button migrate = Ui.Btn(I18n.L("导入官方启动器"), BtnKind.Chip, delegate
		{
			Run(MigrateAsync);
		}, "\ue8b5");
		Button openDir = Ui.Btn(I18n.L("打开游戏目录"), BtnKind.Chip, delegate
		{
			Run(OpenGameDirAsync, I18n.L("打开失败"));
		}, "\ue8b7");
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			Run(LoadAsync);
		}, "\ue72c");
		Grid head = Ui.Section(I18n.L("实例"), I18n.L("每个实例是一份独立的 .minecraft,互不干扰"), Ui.H(8.0, _count.VCenter(), _search.VCenter(), openDir, migrate, refresh, create));
		_debounce.Tick += delegate
		{
			_debounce.Stop();
			Render();
		};
		_search.TextChanged += delegate
		{
			// 每次输入都重起表(防抖),最后只重画一次
			_debounce.Stop();
			_debounce.Start();
		};
		_empty.Show(shown: false);
		Border card = Ui.Card(Ui.V(14.0, head, _list, _empty), 18.0);
		base.Content = PageBase.ScrollBody(card);
		// 【Windows 源】:隧道段(PreviewKeyDown 的等价物)—— 焦点不在输入框里时接管上下/回车
		AddHandler(InputElement.KeyDownEvent, delegate(object? _, KeyEventArgs e)
		{
			if (HandleNavKey(e.Key))
			{
				e.Handled = true;
			}
		}, RoutingStrategies.Tunnel);
	}

	protected override async Task LoadAsync()
	{
		_rows = (await Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		string def = await Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = ""
		}, "");
		if (!string.IsNullOrEmpty(def))
		{
			_defaultInstance = def;
		}
		Render();
	}

	/// <summary>切页离开:把防抖表停掉(Windows 同,免得离开之后还重画一次)。</summary>
	public override void OnHidden()
	{
		_debounce.Stop();
	}

	/// <summary>
	/// 键盘导航的状态机(Windows 的 <c>PreviewKeyDown</c> 处理器体):焦点在输入框里就什么都不做;
	/// 下/上移动选中、回车启动选中的那个。返回 true = 这次按键被吃掉了。
	/// </summary>
	internal bool HandleNavKey(Key key)
	{
		// 【Windows 源】:!(Keyboard.FocusedElement is TextBox) —— Avalonia 走焦点管理器(见类注释)
		if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
		{
			return false;
		}
		switch (key)
		{
		case Key.Down:
			MoveSelection(1);
			return true;
		case Key.Up:
			MoveSelection(-1);
			return true;
		case Key.Enter:
			if (_selected != null && _cards.ContainsKey(_selected))
			{
				string inst = _selected;
				PageBase.Run(() => LaunchWithAsync(inst));
				return true;
			}
			return false;
		default:
			return false;
		}
	}

	private void Render()
	{
		string q = _search.Text?.Trim() ?? "";
		List<InstanceInfo> list = ((q.Length == 0) ? _rows : _rows.Where((InstanceInfo r) => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList());
		_count.Text = ((q.Length == 0) ? I18n.L("共 {0} 个", _rows.Count) : I18n.L("{0} / {1} 个", list.Count, _rows.Count));
		_empty.Show(list.Count == 0);
		// 过滤掉的行要真的从可视树里摘掉(不是只藏起来),所以先算"还要哪些"再删多余的
		_emptyTitle.Text = ((_rows.Count > 0) ? I18n.L("没有匹配的实例") : I18n.L("还没有实例"));
		HashSet<string> wanted = list.Select((InstanceInfo r) => r.Name).ToHashSet(StringComparer.Ordinal);
		foreach (string stale in _cards.Keys.Where((string k) => !wanted.Contains(k)).ToList())
		{
			_list.Children.Remove(_cards[stale].Root);
			_cards.Remove(stale);
		}
		List<Control> fresh = new List<Control>();
		_order.Clear();
		for (int i = 0; i < list.Count; i++)
		{
			InstanceInfo info = list[i];
			// 卡片按实例名复用(同一张卡只换内容),所以滚动位置与悬停状态不会因为一次刷新就丢
			if (!_cards.TryGetValue(info.Name, out Row card))
			{
				card = BuildRow(info);
				_cards[info.Name] = card;
				_list.Children.Add(card.Root);
				fresh.Add(card.Root);
			}
			Sync(card, info);
			_order.Add(card);
			// 顺序按过滤后的列表重排(原地 Move,不重建控件)
			int at = _list.Children.IndexOf(card.Root);
			if (at != i)
			{
				_list.Children.RemoveAt(at);
				_list.Children.Insert(i, card.Root);
			}
			card.Root.Margin = new Thickness(0.0, 0.0, 0.0, (i != list.Count - 1) ? 8 : 0);
		}
		_list.IsVisible = list.Count != 0;
		foreach (Row card in _order)
		{
			Paint(card);
		}
		if (fresh.Count > 0)
		{
			Motion.StaggerItems(fresh, 24.0, 200.0, 8.0);
		}
	}

	/// <summary>卡片内容与最新数据对齐(只在这些字段真的变了时才重写控件 —— Windows 同)。</summary>
	private void Sync(Row card, InstanceInfo info)
	{
		bool isDefault = info.Name == _defaultInstance;
		if (!(card.Info.Name == info.Name) || card.Default != isDefault || card.Info.Versions != info.Versions || !(card.Info.Pack == info.Pack) || !(card.Info.PackVersion == info.PackVersion) || !(card.Info.McVersion == info.McVersion) || !(card.Info.JavaLabel == info.JavaLabel))
		{
			card.Info = info;
			card.Default = isDefault;
			card.Name.Text = info.Name;
			// 【Windows 源】:副行优先显示「整合包 + 打包版本」,没有整合包才退到 MC 版本
			card.Sub.Text = (string.IsNullOrEmpty(info.Pack) ? info.Mc : (info.Pack + " " + info.PackVersion).Trim());
			card.Chips.Children.Clear();
			if (isDefault)
			{
				card.Chips.Children.Add(Ui.Tag(I18n.L("默认"), "B.OnAccentStrong", "B.AccentStrong"));
			}
			card.Chips.Children.Add(Ui.Tag(I18n.L("{0} 个版本", info.Versions)));
			if (!string.IsNullOrEmpty(info.JavaLabel))
			{
				card.Chips.Children.Add(Ui.Tag(info.JavaLabel, "B.AccentText", "B.AccentSoft"));
			}
			if (!string.IsNullOrEmpty(info.McVersion))
			{
				card.Chips.Children.Add(Ui.Tag("MC " + info.McVersion));
			}
		}
	}

	private Row BuildRow(InstanceInfo seed)
	{
		TextBlock name = Ui.Txt("", 14.5, bold: true).Trim();
		TextBlock sub = Ui.Muted("").Trim();
		sub.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
		SPanel chips = Ui.H(6.0);
		chips.Margin = new Thickness(12.0, 0.0, 12.0, 0.0);
		string instName = seed.Name;
		Button launch = Ui.Btn(I18n.L("启动"), BtnKind.Primary, delegate
		{
			PageBase.Run(() => LaunchWithAsync(instName));
		}, "\ue768");
		Button mods = Ui.IconBtn("\uea86", I18n.L("管理这个实例的模组"), delegate
		{
			PageBase.Run(async delegate
			{
				await ModsDialog.ShowAsync(instName);
			});
		});
		Button saves = Ui.IconBtn("\ue74e", I18n.L("存档管理"), delegate
		{
			PageBase.Run(async delegate
			{
				await SavesDialog.ShowAsync(instName, "");
			});
		});
		Button more = Ui.IconBtn("\ue712", I18n.L("更多"), delegate(object? s, RoutedEventArgs _)
		{
			Menu((Control)s!, instName);
		});
		SPanel tail = Ui.H(6.0, launch, mods, saves, more);
		SPanel mid = Ui.V(0.0, name, sub);
		mid.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);
		Grid grid = Ui.G(null, "Auto,*,Auto,Auto");
		grid.Add(Icon(seed), 0);
		grid.Add(mid.VCenter(), 0, 1);
		grid.Add(chips.VCenter(), 0, 2);
		grid.Add(tail.VCenter(), 0, 3);
		Border root = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(14.0, 12.0, 14.0, 12.0),
			BorderThickness = new Thickness(1.0),
			Child = grid,
			Cursor = new Cursor(StandardCursorType.Hand)
		};
		Row row = new Row
		{
			Root = root,
			Name = name,
			Sub = sub,
			Chips = chips,
			Key = instName
		};
		root.PointerEntered += delegate
		{
			row.Hover = true;
			Paint(row);
		};
		root.PointerExited += delegate
		{
			row.Hover = false;
			Paint(row);
		};
		root.PointerPressed += delegate(object? _, PointerPressedEventArgs e)
		{
			// 点在行尾的按钮上不算选中/双击(Windows 用 OriginalSource 往树上找 Button)
			if (!(e.Source is Visual d) || FindButton(d) == null)
			{
				Select(instName);
				_list.Focus();
				if (e.ClickCount == 2)
				{
					e.Handled = true;
					PageBase.Run(() => LaunchWithAsync(instName));
				}
			}
		};
		root.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
		{
			// 【Windows 源】:MouseRightButtonUp —— Avalonia 只有 PointerReleased,所以判"右键是否按着"
			if (e.GetCurrentPoint(root).Properties.IsRightButtonPressed)
			{
				if (!(e.Source is Visual d) || FindButton(d) == null)
				{
					Select(instName);
					e.Handled = true;
					Menu(root, instName);
				}
			}
		};
		Paint(row);
		return row;
	}

	private void Paint(Row row)
	{
		bool selected = row.Key == _selected;
		row.Root.Dyn(Border.BackgroundProperty, (selected || row.Hover) ? "B.AccentSoft" : "B.Paper2");
		row.Root.Dyn(Border.BorderBrushProperty, selected ? "B.Accent" : (row.Hover ? "B.AccentLite" : "B.Line"));
	}

	/// <summary>选中某个实例卡(同一个就什么都不做):只重画新旧两张卡,并把新的滚进视野。</summary>
	internal void Select(string? name)
	{
		if (!(_selected == name))
		{
			string? previous = _selected;
			_selected = name;
			if (previous != null && _cards.TryGetValue(previous, out Row old))
			{
				Paint(old);
			}
			if (name != null && _cards.TryGetValue(name, out Row now))
			{
				Paint(now);
				now.Root.BringIntoView();
			}
		}
	}

	private void MoveSelection(int delta)
	{
		if (_order.Count != 0)
		{
			int at = ((_selected == null) ? (-1) : _order.FindIndex((Row r) => r.Key == _selected));
			// 【Windows 源】:还没有选中时,向下选第一个、向上选最后一个;已有选中就夹在两端
			int index = ((at >= 0) ? Math.Max(0, Math.Min(_order.Count - 1, at + delta)) : ((delta <= 0) ? (_order.Count - 1) : 0));
			Select(_order[index].Key);
		}
	}

	/// <summary>启动某个实例:先设为默认实例,再让启动页优先用它,最后跳启动页(Windows 同;跳完还会重载自己)。</summary>
	private async Task LaunchWithAsync(string instance)
	{
		await SetDefaultAsync(instance);
		_defaultInstance = instance;
		if (PageBase.Win?.GetPage("launch") is LaunchPage launchPage)
		{
			launchPage.PreferInstance(instance);
		}
		PageBase.Win?.Navigate("launch");
		await LoadAsync();
	}

	/// <summary>
	/// 把某个实例设为默认实例(<c>save_settings</c> 的 <c>default_instance</c>)。
	/// 【Windows 源】<c>InstancePage.cs:357-366</c> —— <b>internal static</b>,因为
	/// <see cref="LibraryPage"/> 切文件夹时也用它(Windows 也是跨页调这个成员)。
	/// </summary>
	internal static Task SetDefaultAsync(string instance)
	{
		return PageBase.Api.CallAsync<object>("save_settings", new
		{
			data = new
			{
				default_instance = instance
			}
		});
	}

	/// <summary>这个可视链上有没有按钮(Windows 的 <c>FindButton</c>;Avalonia 走 <c>Visual.GetVisualParent()</c>)。</summary>
	private static Button? FindButton(Visual d)
	{
		while (d != null)
		{
			if (d is Button button)
			{
				return button;
			}
			d = d.GetVisualParent();
		}
		return null;
	}

	private static Border Icon(InstanceInfo info)
	{
		Border border = new Border
		{
			Width = 40.0,
			Height = 40.0,
			CornerRadius = new CornerRadius(10.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		// 有整合包就画整合包图标,否则画实例图标(Windows 同)
		border.Child = Ui.Glyph(string.IsNullOrEmpty(info.Pack) ? "\ue80a" : "\ue7b8", 17.0, "B.AccentText").Center();
		border.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		return border;
	}

	/// <summary>实例卡的「更多」菜单(与 Windows 逐项同序;第 4 项后有一条分隔线)。</summary>
	private void Menu(Control anchor, string instance)
	{
		BuildRowMenu(anchor, instance).Open(anchor);
	}

	/// <summary>
	/// C4 测试缝:建出那份 12 项的菜单并返回(**不弹出**),真实路径 <see cref="Menu"/> 只是
	/// <c>BuildRowMenu(...).Open(anchor)</c>。拆开的理由见 <see cref="LibraryPage.BuildFolderMenu"/>
	/// (Avalonia 的 <c>ContextMenu</c> 一旦 Open 就落进独立 popup 宿主,测试拿不到也点不到)。
	/// </summary>
	internal ContextMenu BuildRowMenu(Control anchor, string instance)
	{
		ContextMenu menu = new ContextMenu();
		Item(I18n.L("打开文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_folder", new
			{
				name = instance
			});
		});
		Item(I18n.L("设为默认实例"), async delegate
		{
			await SetDefaultAsync(instance);
			_defaultInstance = instance;
			await LoadAsync();
			PageBase.Toast(I18n.L("默认实例"), I18n.L("之后的启动将默认使用「{0}」", instance), ToastKind.Success);
		});
		Item(I18n.L("选择 Java..."), () => PickJavaAsync(instance));
		Item(I18n.L("挑着更新模组..."), () => ModsDialog.CheckUpdatesAsync(instance));
		Item(I18n.L("后台检查模组更新"), async delegate
		{
			await PageBase.Api.StartTaskAsync("start_mod_updates", new { instance });
			// C3d 之后外壳有真成员,直接调(不再需要注入缝,见 D-131)
			PageBase.Win?.FlyToTasks(anchor, I18n.L("检查更新"));
		});
		Item(I18n.L("导出为整合包"), async delegate
		{
			string? dest = Dlg.SaveFile(I18n.L("Modrinth 整合包 (*.mrpack)|*.mrpack"), instance + ".mrpack", I18n.L("导出整合包"));
			if (dest != null)
			{
				await PageBase.Api.StartTaskAsync("export_modpack", new
				{
					instance = instance,
					dest = dest
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名..."), async delegate
		{
			string? text = await Dlg.Prompt(I18n.L("重命名实例"), I18n.L("新名称"), instance);
			if (!string.IsNullOrWhiteSpace(text) && !(text == instance))
			{
				await PageBase.Api.CallAsync("rename_instance", new
				{
					name = instance,
					new_name = text
				});
				await LoadAsync();
			}
		});
		Item(I18n.L("删除实例"), async delegate
		{
			if (await Dlg.Confirm(I18n.L("删除实例"), I18n.L("「{0}」下的版本、模组、存档都会被删除,且不可恢复。", instance), I18n.L("永久删除"), I18n.L("取消"), danger: true))
			{
				await PageBase.Api.CallAsync("delete_instance", new
				{
					name = instance
				});
				await LoadAsync();
				PageBase.Toast(I18n.L("已删除"), instance, ToastKind.Success);
			}
		});
		void Item(string head, Func<Task> act)
		{
			MenuItem mi = new MenuItem
			{
				Header = head
			};
			mi.Click += delegate
			{
				PageBase.Run(act);
			};
			menu.Items.Add(mi);
		}
		return menu;
	}

	private async Task PickJavaAsync(string instance)
	{
		List<JavaOption> opts = (await PageBase.Api.TryCallAsync("java_combo_options", new
		{
			instance = instance,
			scan_system = true
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (opts.Count == 0)
		{
			PageBase.Toast(I18n.L("没找到 Java"), I18n.L("去 Java 页下载一个"), ToastKind.Warning);
			return;
		}
		string selected = await PageBase.Api.TryCallAsync("java_combo_label_for", new
		{
			instance = instance,
			options = opts
		}, "自动选择");
		ComboBox combo = Ui.Combo(opts.Select((JavaOption o) => o.Label), selected);
		string current = (await PageBase.Api.TryCallAsync("instance_java_label", new
		{
			name = instance
		}, "")) ?? "";
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("留「自动选择」由启动器按版本匹配")), string.IsNullOrEmpty(current) ? null : Ui.Small(I18n.L("当前记录:") + current), combo);
		if (await Dlg.Ask(I18n.L("为「{0}」选择 Java", instance), body, I18n.L("保存")))
		{
			// 【Windows 源】:找不到标签对应的项就写「自动选择」(不是空串)
			string java = opts.FirstOrDefault((JavaOption o) => o.Label == combo.Str())?.Value ?? "自动选择";
			await PageBase.Api.CallAsync("set_instance_java", new
			{
				name = instance,
				java = java
			});
			await LoadAsync();
		}
	}

	/// <summary>「打开游戏目录」:先问后端要根目录,再用 <c>open_media</c> 打开;失败如实 Toast 原因。</summary>
	private async Task OpenGameDirAsync()
	{
		string root = (await PageBase.Api.TryCallAsync("game_root_path", null, "")) ?? "";
		string name = (await PageBase.Api.TryCallAsync("game_root_name", null, "")) ?? "";
		bool failed = string.IsNullOrWhiteSpace(root);
		if (!failed)
		{
			failed = !await PageBase.Api.TryCallAsync("open_media", new
			{
				path = root
			}, fallback: false);
		}
		if (failed)
		{
			PageBase.Toast(I18n.L("打不开游戏目录"), string.IsNullOrWhiteSpace(root) ? I18n.L("后端没给出目录") : root, ToastKind.Warning);
		}
		else
		{
			PageBase.Toast(I18n.L("已打开"), string.IsNullOrEmpty(name) ? root : (name + " · " + root), ToastKind.Success);
		}
	}

	private async Task CreateAsync()
	{
		string? name = await Dlg.Prompt(I18n.L("新建实例"), I18n.L("实例名称(会创建独立的 .minecraft)"), "", I18n.L("例如:1.20.1 生存"));
		if (!string.IsNullOrWhiteSpace(name))
		{
			await PageBase.Api.CallAsync("create_instance", new { name });
			await LoadAsync();
			PageBase.Toast(I18n.L("已创建"), name, ToastKind.Success);
		}
	}

	private async Task MigrateAsync()
	{
		if (!await PageBase.Api.TryCallAsync("detect_official_launcher", null, fallback: false))
		{
			PageBase.Toast(I18n.L("没找到官方启动器"), I18n.L("未检测到 .minecraft 目录"), ToastKind.Warning);
			return;
		}
		string dir = await PageBase.Api.TryCallAsync("official_launcher_dir", null, "");
		List<string> versions = (await PageBase.Api.TryCallAsync("scan_official_versions", null, new List<string>())) ?? new List<string>();
		TextBox target = Ui.Input("default", "default");
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("检测到:{0}", dir)), Ui.Muted(I18n.L("可导入 {0} 个版本:{1}{2}", versions.Count, string.Join(I18n.L("、"), versions.Take(8)), (versions.Count > 8) ? " ..." : "")), Ui.Txt(I18n.L("导入到实例"), 12.0, bold: false, "B.InkMuted"), target);
		if (await Dlg.Ask(I18n.L("导入官方启动器"), body, I18n.L("开始导入")))
		{
			await PageBase.Api.StartTaskAsync("migrate_official_launcher", new
			{
				instance = (target.Text?.Trim() ?? "default")
			});
			PageBase.Toast(I18n.L("已开始导入"), I18n.L("进度看下载任务"));
		}
	}
}
