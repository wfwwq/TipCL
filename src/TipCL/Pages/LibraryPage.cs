using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 实例库（2026-11 效果图）：左栏文件夹列表（420 宽）+ 右栏版本选择（按加载器分组折叠）。
// 数据/行为与 VersionManagePage 同源：get_instances / get_version_rows（当前实例）/ 收藏走 Prefs.VmFavorites；
// 双击版本 = 设默认实例后跳启动页；行尾 收藏 / 卸载 / 版本设置。
public sealed class LibraryPage : PageBase
{
	private sealed class Ver
	{
		public string Id = "";

		public string Loader = "";

		public string Mc = "";

		public string Type = "release";

		public bool Isolated;

		public int Mods;

		public string IsolationLabel = "";

		public string Java = "";
	}

	private sealed class Group
	{
		public string Key = "";

		public bool Open;

		public Border Card;

		public StackPanel Body;

		public TextBlock Chevron;

		public FrameworkElement Folded;
	}

	private sealed class FolderRow
	{
		public InstanceInfo Info = new InstanceInfo();

		public Border Root;

		public bool Hover;

		public void Paint(bool selected)
		{
			Root.SetResourceReference(Border.BackgroundProperty, (object)((selected || Hover) ? "B.AccentSoft" : "B.Paper2"));
			Root.SetResourceReference(Border.BorderBrushProperty, (object)(selected ? "B.Accent" : (Hover ? "B.AccentLite" : "B.Line")));
		}
	}

	private static readonly string[] LoaderKeys = new string[6] { "fabric", "forge", "quilt", "neoforge", "optifine", "liteloader" };

	private static readonly string[] LoaderNames = new string[6] { "Fabric", "Forge", "Quilt", "NeoForge", "OptiFine", "LiteLoader" };

	private static readonly Regex _SnapRx = new Regex("^\\d+w\\d+[a-z]", RegexOptions.IgnoreCase);

	private static readonly (string Key, string Title, bool Open)[] Defs = new(string, string, bool)[7]
	{
		("fabric", I18n.L("Fabric 版本"), true),
		("forge", I18n.L("Forge 版本"), true),
		("quilt", I18n.L("Quilt 版本"), true),
		("neoforge", I18n.L("NeoForge 版本"), true),
		("optifine", I18n.L("OptiFine 版本"), true),
		("liteloader", I18n.L("LiteLoader 版本"), true),
		("vanilla", I18n.L("原版版本"), true)
	};

	private readonly StackPanel _folders = new StackPanel();

	private readonly TextBlock _folderPath = Ui.Small("").Trim<TextBlock>();

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBox _search = Ui.Input(I18n.L("过滤版本名"), "", 170.0);

	private readonly StackPanel _groupsHost = new StackPanel();

	private readonly SPanel _empty = Ui.Empty("\ue80a", I18n.L("这个文件夹还没有版本"), I18n.L("点左侧「添加或导入」，或到「下载」页装一个"));

	private readonly List<FolderRow> _folderRows = new List<FolderRow>();

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private List<Ver> _vers = new List<Ver>();

	private string _folder = "";

	private string _defaultInstance = "";

	private string _rootPath = "";

	private static readonly List<string> _noFavs = new List<string>();

	public override string Title => I18n.L("实例库");

	public LibraryPage()
	{
		// 左栏：文件夹列表卡
		Button add = Ui.Btn(I18n.L("添加已有文件夹"), BtnKind.Chip, delegate
		{
			Run(AddFolderAsync, I18n.L("导入失败"));
		}, "\ue8b7");
		add.Stretch<Button>();
		Button pack = Ui.Btn(I18n.L("导入整合包"), BtnKind.Chip, delegate
		{
			Run(ImportPackAsync);
		}, "\ue7b8");
		pack.Stretch<Button>();
		Button create = Ui.Btn(I18n.L("新建实例"), BtnKind.Soft, delegate
		{
			Run(CreateAsync, I18n.L("创建失败"));
		}, "\ue710");
		create.Stretch<Button>();
		Button migrate = Ui.Btn(I18n.L("导入官方启动器"), BtnKind.Chip, delegate
		{
			Run(MigrateAsync);
		}, "\ue8b5");
		migrate.Stretch<Button>();
		_folderPath.SetResourceReference(TextBlock.ForegroundProperty, (object)"B.InkMuted");
		TextBlock h = Ui.Txt(I18n.L("文件夹列表"), 18.0, bold: true);
		h.FontWeight = FontWeights.ExtraBold;
		Border left = Ui.Card(Ui.V(10.0, h, _folderPath, _folders, Ui.Sep(), Ui.Txt(I18n.L("添加或导入"), 13.0, bold: true), add, pack, create, migrate), 14.0);
		// 左栏随窗口比例伸缩，窄窗不挤右栏（2026-11-07 用户反馈：420 固定宽在窄窗占比过大）
		left.MinWidth = 300.0;
		left.MaxWidth = 380.0;
		left.VerticalAlignment = VerticalAlignment.Top;
		// 右栏头部两行：第一行 标题+计数+搜索+刷新；第二行 副说明完整换行显示
		TextBlock h2 = Ui.Txt(I18n.L("版本选择"), 21.0, bold: true);
		h2.FontWeight = FontWeights.ExtraBold;
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			Run(LoadAsync);
		}, "\ue72c");
		_search.TextChanged += delegate
		{
			RenderGroups();
		};
		Grid head = Ui.G("Auto,Auto", "Auto,Auto,*,Auto,Auto");
		TextBlock subHead = Ui.Txt(I18n.L("双击版本直接启动；行尾可收藏/删除/设置"), 12.5, bold: false, "B.InkMuted");
		subHead.TextWrapping = TextWrapping.Wrap;
		head.Add(h2.VCenter(), 0, 0);
		head.Add(_count.VCenter().M(10.0, 0.0, 10.0, 0.0), 0, 1);
		head.Add(_search.VCenter(), 0, 3);
		head.Add(refresh.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 4);
		head.Add(subHead, 1, 0, 1, 5);
		_empty.Show(shown: false);
		SPanel right = Ui.V(12.0, head, _groupsHost, _empty);
		right.VerticalAlignment = VerticalAlignment.Top;
		Grid body = Ui.G(null, "*,18,2*");
		body.Add(left, 0);
		body.Add(right, 0, 2);
		base.Content = PageBase.ScrollBody(body);
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = _folder,
			PreferKind = "modpack"
		});
	}

	protected override async Task LoadAsync()
	{
		_instances = (await Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_defaultInstance = await Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = ""
		}, "");
		_rootPath = (await Api.TryCallAsync("game_root_path", null, "")) ?? "";
		if (!_instances.Any((InstanceInfo i) => i.Name == _folder))
		{
			_folder = (_instances.Any((InstanceInfo i) => i.Name == _defaultInstance) ? _defaultInstance : (_instances.FirstOrDefault()?.Name ?? ""));
		}
		await EnsureCurrentAsync();
		await ReloadVersionsAsync();
		RenderFolders();
		RenderGroups();
	}

	public override async Task RefreshAsync()
	{
		await LoadAsync();
	}

	// 与 VersionManagePage 一致：get_version_rows 返回当前（默认）实例的版本，切文件夹先设默认
	private async Task EnsureCurrentAsync()
	{
		if (!string.IsNullOrEmpty(_folder) && _folder != _defaultInstance)
		{
			await InstancePage.SetDefaultAsync(_folder);
			_defaultInstance = _folder;
		}
	}

	private async Task ReloadVersionsAsync()
	{
		List<VersionRowDto> source = (await Api.TryCallAsync("get_version_rows", new
		{
			include_hidden = false
		}, new List<VersionRowDto>())) ?? new List<VersionRowDto>();
		_vers = source.Select((VersionRowDto r) => new Ver
		{
			Id = r.Id,
			Loader = r.Loader,
			Mc = r.Mc,
			Type = InferType(r.Id),
			Isolated = r.Isolated,
			Mods = r.Mods,
			IsolationLabel = r.IsolationLabel,
			Java = r.Java
		}).ToList();
	}

	private void SelectFolder(string name)
	{
		if (!(_folder == name))
		{
			_folder = name;
			UpdateFolderPath();
			PaintFolders();
			Run(async delegate
			{
				await EnsureCurrentAsync();
				await ReloadVersionsAsync();
				RenderFolders();
				RenderGroups();
			});
		}
	}

	private string FolderPathFor(string name)
	{
		string known = _instances.FirstOrDefault((InstanceInfo i) => i.Name == name)?.Path;
		if (!string.IsNullOrEmpty(known))
		{
			return known;
		}
		if (string.IsNullOrEmpty(_rootPath))
		{
			return "";
		}
		return _rootPath.TrimEnd('\\', '/') + "\\" + name;
	}

	private void RenderFolders()
	{
		_folders.Children.Clear();
		_folderRows.Clear();
		foreach (InstanceInfo instance in _instances)
		{
			FolderRow folderRow = BuildFolderRow(instance);
			_folderRows.Add(folderRow);
			_folders.Children.Add(folderRow.Root);
		}
		UpdateFolderPath();
		PaintFolders();
	}

	private void UpdateFolderPath()
	{
		string text = FolderPathFor(_folder);
		_folderPath.Text = ((text.Length > 0) ? text : I18n.L("当前文件夹"));
		_folderPath.ToolTip = ((text.Length > 0) ? text : null);
		ToolTipService.SetShowDuration(_folderPath, 60000);
	}

	private FolderRow BuildFolderRow(InstanceInfo info)
	{
		string instName = info.Name;
		TextBlock name = Ui.Txt(instName, 13.5, bold: true).Trim<TextBlock>();
		TextBlock count = Ui.Small(I18n.L("{0} 个版本", info.Versions)).Trim<TextBlock>();
		SPanel mid = Ui.V(1.0, name, count);
		mid.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
		Border icon = new Border
		{
			Width = 32.0,
			Height = 32.0,
			CornerRadius = new CornerRadius(8.0)
		};
		icon.SetResourceReference(Border.BackgroundProperty, (object)"B.AccentSoft");
		icon.Child = Ui.Glyph("\ue8b7", 14.0, "B.AccentText").Center<TextBlock>();
		SPanel tail = Ui.H(6.0);
		tail.VerticalAlignment = VerticalAlignment.Center;
		if (instName == _defaultInstance)
		{
			tail.Children.Add(Ui.Tag(I18n.L("默认"), "B.OnAccentStrong", "B.AccentStrong"));
		}
		tail.Children.Add(Ui.IconBtn("\ue712", I18n.L("实例操作"), delegate(object s, RoutedEventArgs _)
		{
			FolderMenu((FrameworkElement)s, instName);
		}, 13.0));
		Grid g = Ui.G(null, "Auto,*,Auto");
		g.Add(icon.VCenter<Border>(), 0);
		g.Add(mid.VCenter<SPanel>(), 0, 1);
		g.Add(tail, 0, 2);
		Border root = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(10.0, 9.0, 10.0, 9.0),
			BorderThickness = new Thickness(1.0),
			Child = g,
			Cursor = Cursors.Hand,
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		root.ToolTip = FolderPathFor(instName);
		FolderRow row = new FolderRow
		{
			Info = info,
			Root = root
		};
		root.MouseEnter += delegate
		{
			row.Hover = true;
			row.Paint(instName == _folder);
		};
		root.MouseLeave += delegate
		{
			row.Hover = false;
			row.Paint(instName == _folder);
		};
		root.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			if (!HitButton(e.OriginalSource as DependencyObject))
			{
				SelectFolder(instName);
			}
		};
		return row;
	}

	private void PaintFolders()
	{
		foreach (FolderRow folderRow in _folderRows)
		{
			folderRow.Paint(folderRow.Info.Name == _folder);
		}
	}

	private void RenderGroups()
	{
		_groupsHost.Children.Clear();
		string q = _search.Text?.Trim() ?? "";
		Dictionary<string, List<Ver>> map = new Dictionary<string, List<Ver>>();
		foreach (Ver ver in _vers)
		{
			if (q.Length == 0 || BclExtensions.Contains(ver.Id, q, StringComparison.OrdinalIgnoreCase))
			{
				string key = GroupKeyOf(ver);
				if (!map.TryGetValue(key, out var value))
				{
					value = (map[key] = new List<Ver>());
				}
				value.Add(ver);
			}
		}
		int total = 0;
		List<UIElement> cards = new List<UIElement>();
		(string, string, bool)[] defs = Defs;
		for (int i = 0; i < defs.Length; i++)
		{
			var (key2, title, def) = defs[i];
			if (!map.TryGetValue(key2, out var list) || list.Count == 0)
			{
				continue;
			}
			bool searching = q.Length > 0;
			Group group = BuildGroup(key2, title, list.Count, searching || PrefsOpen(key2, def));
			int ri = 0;
			foreach (Ver item in list.OrderBy((Ver v) => v.Id, StringComparer.OrdinalIgnoreCase))
			{
				UIElement row = BuildVerRow(item);
				if (ri++ > 0 && row is Border rb)
				{
					rb.BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0);
					rb.SetResourceReference(Border.BorderBrushProperty, (object)"B.RowLine");
				}
				group.Body.Children.Add(row);
			}
			ApplyOpen(group, group.Open);
			_groupsHost.Children.Add(group.Card);
			cards.Add(group.Card);
			total += list.Count;
		}
		_empty.Show(total == 0);
		_count.Text = I18n.L("共 {0} 个版本", total);
		if (cards.Count > 0)
		{
			Motion.StaggerItems(cards);
		}
	}

	private Group BuildGroup(string key, string title, int count, bool open)
	{
		Group group = new Group
		{
			Key = key,
			Open = open
		};
		TextBlock head = Ui.Txt(title + " (" + count + ")", 13.5, bold: true, "B.AccentText").Trim<TextBlock>();
		string loaderKey = (LoaderKeys.Contains(key) ? key : "");
		UIElement icon = LoaderIcons.Tile(loaderKey, title, 22.0);
		TextBlock chev = Ui.Glyph("\ue70d", 12.0, "B.InkMuted");
		TextBlock folded = Ui.Small(I18n.L("已折叠"));
		folded.SetResourceReference(TextBlock.ForegroundProperty, (object)"B.InkFaint");
		SPanel bar = Ui.H(8.0, icon, head, Ui.Spring(), folded, chev);
		bar.VerticalAlignment = VerticalAlignment.Center;
		Border hd = new Border
		{
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			Cursor = Cursors.Hand,
			Child = bar
		};
		StackPanel body = new StackPanel
		{
			Margin = new Thickness(8.0, 2.0, 8.0, 8.0)
		};
		Border sep = Ui.Sep();
		hd.MouseEnter += delegate
		{
			hd.SetResourceReference(Border.BackgroundProperty, (object)"B.Paper2");
		};
		hd.MouseLeave += delegate
		{
			hd.Background = null;
		};
		hd.MouseLeftButtonUp += delegate
		{
			ToggleGroup(group);
		};
		Border card = Ui.Card(Ui.V(0.0, hd, sep, body), 0.0);
		card.Margin = new Thickness(0.0, 0.0, 0.0, 10.0);
		group.Card = card;
		group.Body = body;
		group.Chevron = chev;
		group.Folded = folded;
		return group;
	}

	private void ApplyOpen(Group g, bool open)
	{
		g.Open = open;
		g.Body.Visibility = (open ? Visibility.Visible : Visibility.Collapsed);
		g.Folded.Visibility = (open ? Visibility.Collapsed : Visibility.Visible);
		g.Chevron.Text = (open ? "\ue70e" : "\ue70d");
	}

	private void ToggleGroup(Group g)
	{
		ApplyOpen(g, !g.Open);
		MainWindow win = Win;
		if (win?.Prefs != null)
		{
			win.Prefs.Expanded["lib:" + g.Key] = g.Open;
			win.SaveUiPrefs();
		}
	}

	private bool PrefsOpen(string key, bool def)
	{
		MainWindow win = Win;
		if (win?.Prefs != null && win.Prefs.Expanded.TryGetValue("lib:" + key, out var value))
		{
			return value;
		}
		return def;
	}

	private UIElement BuildVerRow(Ver v)
	{
		string inst = _folder;
		string id = v.Id;
		bool fav = IsFav(id);
		SPanel tags = Ui.H(6.0);
		if (v.Type == "snapshot")
		{
			tags.Children.Add(Ui.Tag(I18n.L("快照")));
		}
		if (v.Mods > 0)
		{
			tags.Children.Add(Ui.Tag(I18n.L("模组 {0}", v.Mods)));
		}
		if (v.Isolated && !string.IsNullOrEmpty(v.IsolationLabel))
		{
			tags.Children.Add(Ui.Tag(v.IsolationLabel));
		}
		SPanel nameRow = Ui.H(6.0, Ui.Txt(id, 15.0, bold: true).Trim<TextBlock>());
		if (tags.Children.Count > 0)
		{
			nameRow.Children.Add(tags);
		}
		string sub = SubLine(v);
		TextBlock subTb = Ui.Txt(sub, 12.5, bold: false, "B.InkMuted").Trim<TextBlock>();
		SPanel mid = Ui.V(2.0, nameRow, subTb);
		Button star = new Button
		{
			Style = Ui.S("Btn.Icon"),
			Content = Ui.Glyph(fav ? "\ue735" : "\ue734", 13.0, fav ? "B.Accent" : null),
			ToolTip = I18n.L(fav ? "取消收藏" : "收藏")
		};
		star.Click += delegate
		{
			ToggleFav(v);
		};
		Button del = Ui.IconBtn("\ue74d", I18n.L("卸载这个版本"), delegate
		{
			Run(() => UninstallAsync(inst, id), I18n.L("卸载失败"));
		}, 13.0);
		Button gear = Ui.IconBtn("\ue713", I18n.L("版本设置"), delegate
		{
			Run(async delegate
			{
				await VersionSetupDialog.ShowAsync(inst, id);
				await LoadAsync();
			});
		}, 13.0);
		StackPanel tail = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		tail.Children.Add(star);
		tail.Children.Add(del);
		tail.Children.Add(gear);
		Grid g = Ui.G(null, "Auto,*,Auto");
		g.Add(LoaderIcons.Tile(v.Loader.ToLowerInvariant(), id, 36.0).VCenter<ThumbTile>(), 0);
		g.Add(mid.VCenter<SPanel>().M<SPanel>(10.0, 0.0, 10.0, 0.0), 0, 1);
		g.Add(tail, 0, 2);
		Border row = new Border
		{
			MinHeight = 72.0,
			Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
			Background = Brushes.Transparent,
			Cursor = Cursors.Hand,
			Child = g
		};
		row.MouseEnter += delegate
		{
			row.SetResourceReference(Border.BackgroundProperty, (object)"B.RowHover");
		};
		row.MouseLeave += delegate
		{
			row.Background = Brushes.Transparent;
		};
		row.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (!HitButton(e.OriginalSource as DependencyObject) && e.ClickCount == 2)
			{
				e.Handled = true;
				Run(() => LaunchVerAsync(inst, id));
			}
		};
		return row;
	}

	private static string SubLine(Ver v)
	{
		string type = v.Type switch
		{
			"release" => I18n.L("正式版"),
			"snapshot" => I18n.L("快照"),
			"old_beta" => I18n.L("旧测试"),
			"old_alpha" => I18n.L("旧阿尔法"),
			_ => "",
		};
		string s = ((type.Length > 0) ? (type + " " + v.Mc) : v.Mc);
		if (LoaderNames.Contains(v.Loader))
		{
			s = s + "，" + v.Loader;
		}
		if (!string.IsNullOrWhiteSpace(v.Java))
		{
			s = s + " · Java " + v.Java;
		}
		return s;
	}

	private static string InferType(string id)
	{
		string text = id.ToLowerInvariant();
		if (text.StartsWith("rd-") || text.StartsWith("a1.") || text.StartsWith("c0.") || text.StartsWith("inf-") || text.StartsWith("alpha"))
		{
			return "old_alpha";
		}
		if (text.StartsWith("b1."))
		{
			return "old_beta";
		}
		if (_SnapRx.IsMatch(text) || text.Contains("-pre") || text.Contains("-rc"))
		{
			return "snapshot";
		}
		return "release";
	}

	private string GroupKeyOf(Ver v)
	{
		int num = Array.IndexOf(LoaderNames, v.Loader);
		if (num >= 0)
		{
			return LoaderKeys[num];
		}
		return "vanilla";
	}

	private List<string> Favs()
	{
		Dictionary<string, List<string>> prefs = Win?.Prefs.VmFavorites;
		if (prefs == null)
		{
			return _noFavs;
		}
		if (!prefs.TryGetValue(_folder, out var value))
		{
			value = new List<string>();
			prefs[_folder] = value;
		}
		return value;
	}

	private bool IsFav(string id)
	{
		return Favs().Contains(id);
	}

	private void ToggleFav(Ver v)
	{
		List<string> list = Favs();
		if (!list.Remove(v.Id))
		{
			list.Add(v.Id);
		}
		Win?.SaveUiPrefs();
		RenderGroups();
	}

	private async Task LaunchVerAsync(string inst, string ver)
	{
		await InstancePage.SetDefaultAsync(inst);
		_defaultInstance = inst;
		if (Win?.GetPage("launch") is LaunchPage launchPage)
		{
			launchPage.PreferInstance(inst, ver);
		}
		Win?.Navigate("launch");
	}

	private async Task UninstallAsync(string inst, string ver)
	{
		if (await Dlg.Confirm(I18n.L("卸载版本"), I18n.L("确定卸载「{0}」？该版本目录下的独立模组与存档会一并删除。", ver), I18n.L("卸载"), I18n.L("取消"), danger: true))
		{
			await Api.CallAsync("uninstall_version", new
			{
				spec = inst + " / " + ver
			});
			await LoadAsync();
			Toast(I18n.L("已卸载"), ver, ToastKind.Success);
		}
	}

	private void FolderMenu(FrameworkElement anchor, string instance)
	{
		ContextMenu menu = new ContextMenu();
		Item(I18n.L("打开文件夹"), async delegate
		{
			await Api.CallAsync("open_instance_folder", new
			{
				name = instance
			});
		});
		Item(I18n.L("设为默认实例"), async delegate
		{
			_folder = instance;
			await EnsureCurrentAsync();
			await LoadAsync();
			Toast(I18n.L("默认实例"), I18n.L("之后的启动将默认使用「{0}」", instance), ToastKind.Success);
		});
		Item(I18n.L("选择 Java…"), () => PickJavaAsync(instance));
		Item(I18n.L("管理模组"), async delegate
		{
			await ModsDialog.ShowAsync(instance);
		});
		Item(I18n.L("存档管理…"), async delegate
		{
			await SavesDialog.ShowAsync(instance, "");
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名…"), async delegate
		{
			string text = await Dlg.Prompt(I18n.L("重命名实例"), I18n.L("新名称"), instance);
			if (!string.IsNullOrWhiteSpace(text) && !(text == instance))
			{
				await Api.CallAsync("rename_instance", new
				{
					name = instance,
					new_name = text
				});
				await LoadAsync();
			}
		});
		Item(I18n.L("删除实例"), async delegate
		{
			if (await Dlg.Confirm(I18n.L("删除实例"), I18n.L("「{0}」下的版本、模组、存档都会被删除，且不可恢复。", instance), I18n.L("永久删除"), I18n.L("取消"), danger: true))
			{
				await Api.CallAsync("delete_instance", new
				{
					name = instance
				});
				await LoadAsync();
				Toast(I18n.L("已删除"), instance, ToastKind.Success);
			}
		});
		menu.PlacementTarget = anchor;
		menu.IsOpen = true;
		void Item(string head, Func<Task> act)
		{
			MenuItem mi = new MenuItem
			{
				Header = head
			};
			mi.Click += delegate
			{
				Run(act);
			};
			menu.Items.Add(mi);
		}
	}

	private async Task PickJavaAsync(string instance)
	{
		List<JavaOption> opts = (await Api.TryCallAsync("java_combo_options", new
		{
			instance = instance,
			scan_system = true
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (opts.Count == 0)
		{
			Toast(I18n.L("没找到 Java"), I18n.L("去 Java 页下载一个"), ToastKind.Warning);
			return;
		}
		string selected = await Api.TryCallAsync("java_combo_label_for", new
		{
			instance = instance,
			options = opts
		}, I18n.L("自动选择"));
		ComboBox combo = Ui.Combo(opts.Select((JavaOption o) => o.Label), selected);
		string cur = (await Api.TryCallAsync("instance_java_label", new
		{
			name = instance
		}, "")) ?? "";
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("留「自动选择」由启动器按版本匹配")), (string.IsNullOrEmpty(cur) ? null : Ui.Small(I18n.L("当前记录：") + cur)), combo);
		if (await Dlg.Ask(I18n.L("为「{0}」选择 Java", instance), body, I18n.L("保存")))
		{
			string java = opts.FirstOrDefault((JavaOption o) => o.Label == combo.Str())?.Value ?? I18n.L("自动选择");
			await Api.CallAsync("set_instance_java", new
			{
				name = instance,
				java = java
			});
			await LoadAsync();
		}
	}

	private async Task AddFolderAsync()
	{
		string path = Dlg.PickFolder(I18n.L("选择一个已有的游戏文件夹（含 versions 的 .minecraft）"));
		if (!string.IsNullOrWhiteSpace(path))
		{
			string fileName = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
			string name = await Dlg.Prompt(I18n.L("导入为文件夹"), I18n.L("显示名称"), fileName);
			if (!string.IsNullOrWhiteSpace(name))
			{
				await Api.StartTaskAsync("import_existing_folder", new
				{
					path = path,
					name = name
				});
				Toast(I18n.L("已开始导入"), I18n.L("导入进度看「下载任务」"), ToastKind.Success);
			}
		}
	}

	private async Task ImportPackAsync()
	{
		string file = Dlg.PickFile(I18n.L("整合包 (*.mrpack;*.zip)|*.mrpack;*.zip"), I18n.L("导入整合包"));
		if (file != null && Win != null)
		{
			await FileDrop.ImportModpackAsync(Win, file);
		}
	}

	private async Task CreateAsync()
	{
		string name = await Dlg.Prompt(I18n.L("新建实例"), I18n.L("实例名称（会创建独立的 .minecraft）"), "", I18n.L("例如：1.20.1 生存"));
		if (!string.IsNullOrWhiteSpace(name))
		{
			await Api.CallAsync("create_instance", new
			{
				name = name
			});
			await LoadAsync();
			Toast(I18n.L("已创建"), name, ToastKind.Success);
		}
	}

	private async Task MigrateAsync()
	{
		if (!(await Api.TryCallAsync("detect_official_launcher", null, fallback: false)))
		{
			Toast(I18n.L("没找到官方启动器"), I18n.L("未检测到 .minecraft 目录"), ToastKind.Warning);
			return;
		}
		string dir = await Api.TryCallAsync("official_launcher_dir", null, "");
		List<string> list = (await Api.TryCallAsync("scan_official_versions", null, new List<string>())) ?? new List<string>();
		TextBox target = Ui.Input("default", "default");
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("检测到：{0}", dir)), Ui.Muted(I18n.L("可导入 {0} 个版本：{1}{2}", list.Count, string.Join(I18n.L("、"), list.Take(8)), (list.Count > 8) ? " …" : "")), Ui.Txt(I18n.L("导入到实例"), 12.0, bold: false, "B.InkMuted"), target);
		if (await Dlg.Ask(I18n.L("导入官方启动器"), body, I18n.L("开始导入")))
		{
			await Api.StartTaskAsync("migrate_official_launcher", new
			{
				instance = (target.Text?.Trim() ?? "default")
			});
			Toast(I18n.L("已开始导入"), I18n.L("进度看下载任务"));
		}
	}

	private static bool HitButton(DependencyObject? d)
	{
		while (d != null)
		{
			if (d is Button)
			{
				return true;
			}
			d = VisualTreeHelper.GetParent(d);
		}
		return false;
	}
}
