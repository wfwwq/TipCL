using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

public sealed class VersionManagePage : PageBase
{
	private sealed class Ver
	{
		public string Id = "";

		public string Loader = "";

		public string Mc = "";

		public string Type = "release";

		public bool Hidden;

		public bool Isolated;

		public int Mods;

		public string IsolationLabel = "";
	}

	private sealed class Group
	{
		public string Key = "";

		public bool Open;

		public Border Card;

		public StackPanel Body;

		public TextBlock HeadText;

		public TextBlock Chevron;

		public Border Sep;
	}

	private sealed class FolderRow
	{
		public InstanceInfo Info = new InstanceInfo();

		public Border Root;

		public bool Hover;

		public void Paint(bool selected)
		{
			((FrameworkElement)Root).SetResourceReference(Border.BackgroundProperty, (object)((selected || Hover) ? "B.AccentSoft" : "B.Paper2"));
			((FrameworkElement)Root).SetResourceReference(Border.BorderBrushProperty, (object)(selected ? "B.Accent" : (Hover ? "B.AccentLite" : "B.Line")));
		}
	}

	private const string ChevUp = "\ue70e";

	private static readonly string[] LoaderNames = new string[6] { "Fabric", "Forge", "Quilt", "NeoForge", "OptiFine", "LiteLoader" };

	private static readonly Regex _SnapRx = new Regex("^\\d+w\\d+[a-z]", RegexOptions.IgnoreCase);

	private static readonly (string Key, string Title, bool Open)[] Defs = new(string, string, bool)[10]
	{
		("fav", I18n.L("收藏的版本"), true),
		("fabric", I18n.L("Fabric 版本"), true),
		("forge", I18n.L("Forge 版本"), true),
		("quilt", I18n.L("Quilt 版本"), true),
		("neoforge", I18n.L("NeoForge 版本"), true),
		("optifine", I18n.L("OptiFine 版本"), true),
		("liteloader", I18n.L("LiteLoader 版本"), true),
		("vanilla", I18n.L("原版版本"), true),
		("uncommon", I18n.L("不常用版本"), false),
		("hidden", I18n.L("隐藏的版本"), false)
	};

	private readonly StackPanel _folders = new StackPanel();

	private readonly TextBlock _folderPath = Ui.Small("").Trim<TextBlock>();

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBox _search = Ui.Input(I18n.L("过滤版本名"), "", 170.0);

	private readonly StackPanel _groupsHost = new StackPanel();

	private readonly SPanel _empty = Ui.Empty("\ue80a", I18n.L("这个文件夹还没有版本"), I18n.L("点左侧「添加或导入」，或到「原版游戏」页装一个"));

	private readonly List<FolderRow> _folderRows = new List<FolderRow>();

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private List<Ver> _vers = new List<Ver>();

	private string _folder = "";

	private string _defaultInstance = "";

	private string _rootPath = "";

	private static readonly List<string> _noFavs = new List<string>();

	public override string Title => I18n.L("版本管理");

	public VersionManagePage()
	{
		Button val = Ui.Btn(I18n.L("添加已有文件夹"), BtnKind.Soft, (RoutedEventHandler)delegate
		{
			PageBase.Run(AddFolderAsync, I18n.L("导入失败"));
		}, "\ue8b7");
		val.Stretch<Button>();
		Button val2 = Ui.Btn(I18n.L("导入整合包"), BtnKind.Soft, (RoutedEventHandler)delegate
		{
			PageBase.Run(ImportPackAsync);
		}, "\ue7b8");
		val2.Stretch<Button>();
		Button val3 = Ui.Btn(I18n.L("新建实例"), BtnKind.Soft, (RoutedEventHandler)delegate
		{
			PageBase.Run(CreateAsync, I18n.L("创建失败"));
		}, "\ue710");
		val3.Stretch<Button>();
		Button val4 = Ui.Btn(I18n.L("导入官方启动器"), BtnKind.Chip, (RoutedEventHandler)delegate
		{
			PageBase.Run(MigrateAsync);
		}, "\ue8b5");
		val4.Stretch<Button>();
		((FrameworkElement)_folderPath).SetResourceReference(TextBlock.ForegroundProperty, (object)"B.InkMuted");
		Border el = Ui.Card((UIElement?)(object)Ui.V(10.0, (UIElement)Ui.H3(I18n.L("文件夹列表")), (UIElement)_folderPath, (UIElement)_folders, (UIElement)Ui.Sep(), (UIElement)Ui.Txt(I18n.L("添加或导入"), 13.0, bold: true), (UIElement)val, (UIElement)val2, (UIElement)val3, (UIElement)val4), 14.0);
		Button val5 = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, (RoutedEventHandler)delegate
		{
			PageBase.Run(LoadAsync);
		}, "\ue72c");
		((TextBoxBase)_search).TextChanged += (TextChangedEventHandler)delegate
		{
			RenderGroups();
		};
		Grid val6 = Ui.Section(I18n.L("版本选择"), I18n.L("双击版本直接启动；卡片右侧：收藏 / 卸载 / 设置"), (UIElement?)(object)Ui.H(8.0, (UIElement)_count.VCenter<TextBlock>(), (UIElement)_search.VCenter<TextBox>(), (UIElement)val5));
		_empty.Show<SPanel>(shown: false);
		SPanel el2 = Ui.V(12.0, (UIElement)val6, (UIElement)_groupsHost, (UIElement)_empty);
		// 左右两栏之间留 18px 间距（与其他页面卡片间距一致），两栏都顶对齐
		((FrameworkElement)el).VerticalAlignment = VerticalAlignment.Top;
		((FrameworkElement)el2).VerticalAlignment = VerticalAlignment.Top;
		Grid val7 = Ui.G(null, "272,18,*");
		val7.Add((UIElement)(object)el, 0);
		val7.Add((UIElement)(object)el2, 0, 2);
		((ContentControl)this).Content = PageBase.ScrollBody((UIElement)val7);
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = _folder,
			PreferKind = "modpack"
		});
	}

	protected override async Task LoadAsync()
	{
		_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_defaultInstance = await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = ""
		}, "");
		_rootPath = (await PageBase.Api.TryCallAsync("game_root_path", null, "")) ?? "";
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
		List<VersionRowDto> source = (await PageBase.Api.TryCallAsync("get_version_rows", new
		{
			include_hidden = true
		}, new List<VersionRowDto>())) ?? new List<VersionRowDto>();
		_vers = source.Select((VersionRowDto r) => new Ver
		{
			Id = r.Id,
			Loader = r.Loader,
			Mc = r.Mc,
			Type = InferType(r.Id),
			Hidden = r.Hidden,
			Isolated = r.Isolated,
			Mods = r.Mods,
			IsolationLabel = r.IsolationLabel
		}).ToList();
	}

	private void SelectFolder(string name)
	{
		if (!(_folder == name))
		{
			_folder = name;
			UpdateFolderPath();
			PaintFolders();
			PageBase.Run(async delegate
			{
				await EnsureCurrentAsync();
				await ReloadVersionsAsync();
				RenderFolders();
				RenderGroups();
			});
		}
	}

	private static string RootNameOf(string rootPath)
	{
		string text = rootPath.TrimEnd('\\', '/');
		int val = text.LastIndexOf('\\');
		int val2 = text.LastIndexOf('/');
		int num = Math.Max(val, val2);
		if (num < 0)
		{
			return text;
		}
		return text.Substring(num + 1);
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
		if (string.IsNullOrEmpty(name) || name == RootNameOf(_rootPath))
		{
			return _rootPath;
		}
		return _rootPath.TrimEnd('\\', '/') + "\\" + name;
	}

	private void RenderFolders()
	{
		((Panel)_folders).Children.Clear();
		_folderRows.Clear();
		foreach (InstanceInfo instance in _instances)
		{
			FolderRow folderRow = BuildFolderRow(instance);
			_folderRows.Add(folderRow);
			((Panel)_folders).Children.Add((UIElement)(object)folderRow.Root);
		}
		UpdateFolderPath();
		PaintFolders();
	}

	private void UpdateFolderPath()
	{
		string text = FolderPathFor(_folder);
		_folderPath.Text = ((text.Length > 0) ? (I18n.L("当前文件夹 ") + text) : I18n.L("当前文件夹"));
		// 路径太长会被截断，悬停显示完整路径
		((FrameworkElement)_folderPath).ToolTip = ((text.Length > 0) ? text : null);
		ToolTipService.SetShowDuration(_folderPath, 60000);
	}

	private FolderRow BuildFolderRow(InstanceInfo info)
	{
		string instName = info.Name;
		TextBlock val = Ui.Txt(instName, 13.0, bold: true).Trim<TextBlock>();
		TextBlock val2 = Ui.Small(I18n.L("{0} 个版本", info.Versions)).Trim<TextBlock>();
		SPanel sPanel = Ui.V(1.0, (UIElement)val, (UIElement)val2);
		((FrameworkElement)sPanel).Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
		Border val3 = new Border
		{
			Width = 32.0,
			Height = 32.0,
			CornerRadius = new CornerRadius(8.0)
		};
		((FrameworkElement)val3).SetResourceReference(Border.BackgroundProperty, (object)"B.AccentSoft");
		((Decorator)val3).Child = (UIElement)(object)Ui.Glyph("\ue8b7", 14.0, "B.AccentText").Center<TextBlock>();
		SPanel sPanel2 = Ui.H(4.0);
		((FrameworkElement)sPanel2).VerticalAlignment = (VerticalAlignment)1;
		if (instName == _defaultInstance)
		{
			((Panel)sPanel2).Children.Add((UIElement)(object)Ui.Tag(I18n.L("默认"), "B.OnAccentStrong", "B.AccentStrong"));
		}
		((Panel)sPanel2).Children.Add((UIElement)(object)Ui.IconBtn("\ue712", I18n.L("实例操作"), (RoutedEventHandler)delegate(object s, RoutedEventArgs _)
		{
			FolderMenu((FrameworkElement)s, instName);
		}, 13.0));
		Grid val4 = Ui.G(null, "Auto,*,Auto");
		val4.Add((UIElement)(object)val3.VCenter<Border>(), 0);
		val4.Add((UIElement)(object)sPanel.VCenter<SPanel>(), 0, 1);
		val4.Add((UIElement)(object)sPanel2, 0, 2);
		Border root = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(9.0, 8.0, 9.0, 8.0),
			BorderThickness = new Thickness(1.0),
			Child = (UIElement)(object)val4,
			Cursor = Cursors.Hand,
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		((FrameworkElement)root).ToolTip = FolderPathFor(instName);
		FolderRow row = new FolderRow
		{
			Info = info,
			Root = root
		};
		((UIElement)root).MouseEnter += (MouseEventHandler)delegate
		{
			row.Hover = true;
			row.Paint(instName == _folder);
		};
		((UIElement)root).MouseLeave += (MouseEventHandler)delegate
		{
			row.Hover = false;
			row.Paint(instName == _folder);
		};
		((UIElement)root).MouseLeftButtonUp += (MouseButtonEventHandler)delegate(object _, MouseButtonEventArgs e)
		{
			object originalSource2 = ((RoutedEventArgs)e).OriginalSource;
			if (!HitButton((DependencyObject?)((originalSource2 is DependencyObject) ? originalSource2 : null)))
			{
				SelectFolder(instName);
			}
		};
		((UIElement)root).MouseRightButtonUp += (MouseButtonEventHandler)delegate(object _, MouseButtonEventArgs e)
		{
			object originalSource = ((RoutedEventArgs)e).OriginalSource;
			if (!HitButton((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null)))
			{
				((RoutedEventArgs)e).Handled = true;
				SelectFolder(instName);
				FolderMenu((FrameworkElement)(object)root, instName);
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
		((Panel)_groupsHost).Children.Clear();
		string text = _search.Text?.Trim() ?? "";
		Dictionary<string, List<Ver>> dictionary = new Dictionary<string, List<Ver>>();
		foreach (Ver ver in _vers)
		{
			if (text.Length == 0 || BclExtensions.Contains(ver.Id, text, StringComparison.OrdinalIgnoreCase))
			{
				string key = GroupKeyOf(ver);
				if (!dictionary.TryGetValue(key, out var value))
				{
					List<Ver> list2 = (dictionary[key] = new List<Ver>());
					value = list2;
				}
				value.Add(ver);
			}
		}
		bool flag = text.Length > 0;
		int num = 0;
		List<UIElement> list3 = new List<UIElement>();
		(string, string, bool)[] defs = Defs;
		for (int i = 0; i < defs.Length; i++)
		{
			var (key2, text2, def) = defs[i];
			if (!dictionary.TryGetValue(key2, out var value2) || value2.Count == 0)
			{
				continue;
			}
			Group group = BuildGroup(key2, flag || PrefsOpen(key2, def));
			group.HeadText.Text = text2 + " (" + value2.Count + ")";
			foreach (Ver item in value2)
			{
				((Panel)group.Body).Children.Add(BuildVerRow(item));
			}
			ApplyOpen(group, group.Open);
			((Panel)_groupsHost).Children.Add((UIElement)(object)group.Card);
			list3.Add((UIElement)(object)group.Card);
			num += value2.Count;
		}
		_empty.Show<SPanel>(num == 0);
		_count.Text = I18n.L("共 {0} 个版本", num);
		if (list3.Count > 0)
		{
			Motion.StaggerItems(list3);
		}
	}

	private Group BuildGroup(string key, bool open)
	{
		Group group = new Group
		{
			Key = key,
			Open = open
		};
		TextBlock val = Ui.Txt("", 13.5, bold: true, "B.AccentText");
		TextBlock val2 = Ui.Glyph("\ue70d", 12.0, "B.InkMuted");
		SPanel sPanel = Ui.H(8.0, (UIElement)val, (UIElement)val2);
		((FrameworkElement)sPanel).VerticalAlignment = (VerticalAlignment)1;
		Border border = new Border
		{
			Padding = new Thickness(12.0, 9.0, 12.0, 9.0),
			Cursor = Cursors.Hand,
			Child = (UIElement)(object)sPanel
		};
		StackPanel val3 = new StackPanel
		{
			Margin = new Thickness(8.0, 2.0, 8.0, 8.0)
		};
		Border val4 = Ui.Sep();
		((UIElement)border).MouseEnter += (MouseEventHandler)delegate
		{
			((FrameworkElement)border).SetResourceReference(Border.BackgroundProperty, (object)"B.Paper2");
		};
		((UIElement)border).MouseLeave += (MouseEventHandler)delegate
		{
			border.Background = null;
		};
		((UIElement)border).MouseLeftButtonUp += (MouseButtonEventHandler)delegate
		{
			ToggleGroup(group);
		};
		Border card = Ui.Card((UIElement?)(object)Ui.V(0.0, (UIElement)border, (UIElement)val4, (UIElement)val3), 0.0);
		group.Card = card;
		group.Body = val3;
		group.HeadText = val;
		group.Chevron = val2;
		group.Sep = val4;
		return group;
	}

	private void ApplyOpen(Group g, bool open)
	{
		g.Open = open;
		((UIElement)g.Body).Visibility = (Visibility)((!open) ? 2 : 0);
		((UIElement)g.Sep).Visibility = (Visibility)((!open) ? 2 : 0);
		g.Chevron.Text = (open ? "\ue70e" : "\ue70d");
	}

	private void ToggleGroup(Group g)
	{
		ApplyOpen(g, !g.Open);
		MainWindow win = PageBase.Win;
		if (win?.Prefs != null)
		{
			win.Prefs.Expanded["vm:" + g.Key] = g.Open;
			win.SaveUiPrefs();
		}
	}

	private bool PrefsOpen(string key, bool def)
	{
		MainWindow win = PageBase.Win;
		if (win?.Prefs != null && win.Prefs.Expanded.TryGetValue("vm:" + key, out var value))
		{
			return value;
		}
		return def;
	}

	private UIElement BuildVerRow(Ver v)
	{
		string inst = _folder;
		string id = v.Id;
		bool flag = IsFav(id);
		SPanel sPanel = Ui.H(6.0);
		if (v.Mods > 0)
		{
			((Panel)sPanel).Children.Add((UIElement)(object)Ui.Tag(I18n.L("模组 {0}", v.Mods)));
		}
		if (v.Isolated && !string.IsNullOrEmpty(v.IsolationLabel))
		{
			((Panel)sPanel).Children.Add((UIElement)(object)Ui.Tag(v.IsolationLabel));
		}
		SPanel sPanel2 = Ui.H(6.0, (UIElement)Ui.Txt(id, 13.0, bold: true).Trim<TextBlock>());
		if (((Panel)sPanel).Children.Count > 0)
		{
			((Panel)sPanel2).Children.Add((UIElement)(object)sPanel);
		}
		TextBlock val = Ui.Small(SubLine(v)).Trim<TextBlock>();
		SPanel el = Ui.V(1.0, (UIElement)sPanel2, (UIElement)val);
		Button val2 = new Button
		{
			Style = Ui.S("Btn.Icon"),
			Content = Ui.Glyph(flag ? "\ue735" : "\ue734", 13.0, flag ? "B.Accent" : null),
			ToolTip = I18n.L(flag ? "取消收藏" : "收藏")
		};
		((ButtonBase)val2).Click += (RoutedEventHandler)delegate
		{
			ToggleFav(v);
		};
		Button val3 = Ui.IconBtn("\ue74d", I18n.L("卸载这个版本"), (RoutedEventHandler)delegate
		{
			PageBase.Run(() => UninstallAsync(inst, id), I18n.L("卸载失败"));
		}, 13.0);
		Button val4 = Ui.IconBtn("\ue713", I18n.L("版本设置"), (RoutedEventHandler)delegate
		{
			PageBase.Run(async delegate
			{
				await VersionSetupDialog.ShowAsync(inst, id);
				await LoadAsync();
			});
		}, 13.0);
		StackPanel val5 = new StackPanel
		{
			Orientation = (Orientation)0
		};
		((Panel)val5).Children.Add((UIElement)(object)val2);
		((Panel)val5).Children.Add((UIElement)(object)val3);
		((Panel)val5).Children.Add((UIElement)(object)val4);
		Grid val6 = Ui.G(null, "Auto,*,Auto");
		val6.Add((UIElement)(object)new ThumbTile(id, 34.0, 8.0).VCenter<ThumbTile>(), 0);
		val6.Add((UIElement)(object)el.VCenter<SPanel>().M<SPanel>(9.0, 0.0, 9.0, 0.0), 0, 1);
		val6.Add((UIElement)(object)val5, 0, 2);
		Border border = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(8.0, 7.0, 8.0, 7.0),
			Child = (UIElement)(object)val6,
			Cursor = Cursors.Hand,
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		((UIElement)border).MouseEnter += (MouseEventHandler)delegate
		{
			((FrameworkElement)border).SetResourceReference(Border.BackgroundProperty, (object)"B.Paper2");
		};
		((UIElement)border).MouseLeave += (MouseEventHandler)delegate
		{
			border.Background = null;
		};
		((UIElement)border).MouseLeftButtonDown += (MouseButtonEventHandler)delegate(object _, MouseButtonEventArgs e)
		{
			object originalSource2 = ((RoutedEventArgs)e).OriginalSource;
			if (!HitButton((DependencyObject?)((originalSource2 is DependencyObject) ? originalSource2 : null)) && e.ClickCount == 2)
			{
				((RoutedEventArgs)e).Handled = true;
				PageBase.Run(() => LaunchVerAsync(inst, id));
			}
		};
		((UIElement)border).MouseRightButtonUp += (MouseButtonEventHandler)delegate(object _, MouseButtonEventArgs e)
		{
			object originalSource = ((RoutedEventArgs)e).OriginalSource;
			if (!HitButton((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null)))
			{
				((RoutedEventArgs)e).Handled = true;
				VerMenu((FrameworkElement)(object)border, v);
			}
		};
		return (UIElement)(object)border;
	}

	private static string SubLine(Ver v)
	{
		string text = v.Type switch
		{
			"release" => I18n.L("正式版"), 
			"snapshot" => I18n.L("快照"), 
			"old_beta" => I18n.L("旧测试"), 
			"old_alpha" => I18n.L("旧阿尔法"), 
			_ => "", 
		};
		string text2 = ((text.Length > 0) ? (text + " " + v.Mc) : v.Mc);
		if (LoaderNames.Contains(v.Loader))
		{
			string text3 = LoaderVerOf(v.Id, v.Loader);
			text2 = text2 + "，" + v.Loader + ((text3.Length > 0) ? (" " + text3) : "");
		}
		return text2;
	}

	private static string LoaderVerOf(string id, string loader)
	{
		int num = id.IndexOf(loader, StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return "";
		}
		string text = id.Substring(num + loader.Length).Trim(' ', '-', '_', ':');
		if (text.Length == 0 || text.Length > 24)
		{
			return "";
		}
		return text;
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
		if (v.Hidden)
		{
			return "hidden";
		}
		if (IsFav(v.Id))
		{
			return "fav";
		}
		switch (v.Loader)
		{
		case "Fabric":
			return "fabric";
		case "Forge":
			return "forge";
		case "Quilt":
			return "quilt";
		case "NeoForge":
			return "neoforge";
		case "OptiFine":
			return "optifine";
		case "LiteLoader":
			return "liteloader";
		default:
			if (!(v.Type == "release"))
			{
				return "uncommon";
			}
			return "vanilla";
		}
	}

	private List<string> Favs()
	{
		Dictionary<string, List<string>> dictionary = PageBase.Win?.Prefs.VmFavorites;
		if (dictionary == null)
		{
			return _noFavs;
		}
		if (!dictionary.TryGetValue(_folder, out var value))
		{
			value = new List<string>();
			dictionary[_folder] = value;
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
		PageBase.Win?.SaveUiPrefs();
		RenderGroups();
	}

	private async Task LaunchVerAsync(string inst, string ver)
	{
		await InstancePage.SetDefaultAsync(inst);
		_defaultInstance = inst;
		if (PageBase.Win?.GetPage("launch") is LaunchPage launchPage)
		{
			launchPage.PreferInstance(inst, ver);
		}
		PageBase.Win?.Navigate("launch");
	}

	private async Task UninstallAsync(string inst, string ver)
	{
		if (await Dlg.Confirm(I18n.L("卸载版本"), I18n.L("确定卸载「{0}」？该版本目录下的独立模组与存档会一并删除。", ver), I18n.L("卸载"), I18n.L("取消"), danger: true))
		{
			await PageBase.Api.CallAsync("uninstall_version", new
			{
				spec = inst + " / " + ver
			});
			await LoadAsync();
			PageBase.Toast(I18n.L("已卸载"), ver, ToastKind.Success);
		}
	}

	private void VerMenu(FrameworkElement anchor, Ver v)
	{
		string inst = _folder;
		string id = v.Id;
		ContextMenu menu = new ContextMenu();
		Item(I18n.L("打开版本文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_version_folder", new
			{
				instance = inst,
				version = id,
				which = "root"
			});
		});
		Item(I18n.L("打开游戏文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_version_folder", new
			{
				instance = inst,
				version = id,
				which = "game"
			});
		});
		Item(I18n.L("存档管理…"), async delegate
		{
			await SavesDialog.ShowAsync(inst, id);
		});
		((ItemsControl)menu).Items.Add((object)new Separator());
		Item(I18n.L("重命名"), async delegate
		{
			string text4 = await Dlg.Prompt(I18n.L("重命名版本"), I18n.L("新版本 ID"), id);
			if (!string.IsNullOrWhiteSpace(text4) && !(text4 == id))
			{
				await PageBase.Api.CallAsync("rename_version", new
				{
					instance = inst,
					version = id,
					new_id = text4
				});
				await LoadAsync();
			}
		});
		Item(I18n.L("复制一份"), async delegate
		{
			string text3 = await Dlg.Prompt(I18n.L("复制版本"), I18n.L("新版本 ID"), id + "-copy");
			if (!string.IsNullOrWhiteSpace(text3))
			{
				await PageBase.Api.CallAsync("copy_version", new
				{
					instance = inst,
					version = id,
					new_id = text3
				});
				await LoadAsync();
			}
		});
		Item(v.Hidden ? I18n.L("取消隐藏") : I18n.L("在列表中隐藏"), async delegate
		{
			await PageBase.Api.CallAsync("hide_version", new
			{
				instance = inst,
				version = id,
				hidden = !v.Hidden
			});
			await LoadAsync();
			PageBase.Toast(v.Hidden ? I18n.L("已取消隐藏") : I18n.L("已隐藏"), id, ToastKind.Success);
		});
		Item(I18n.L("创建桌面快捷方式"), async delegate
		{
			string text2 = await PageBase.Api.CallAsync<string>("create_desktop_shortcut", new
			{
				instance = inst,
				version = id
			});
			await Dlg.Alert(I18n.L("已创建"), I18n.L("桌面快捷方式：\n{0}\n\n双击即可直接启动该版本。", text2));
		});
		Item(I18n.L("导出启动脚本"), async delegate
		{
			string text = Dlg.SaveFile(I18n.L("批处理 (*.bat)|*.bat"), "launch-" + inst + "-" + id + ".bat", I18n.L("导出启动脚本"));
			if (text != null)
			{
				await PageBase.Api.StartTaskAsync("export_launch_script", new
				{
					instance = inst,
					version = id,
					dest = text
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		Item(I18n.L("修复（补全缺失文件）"), async delegate
		{
			await PageBase.Api.StartTaskAsync("repair_version", new
			{
				instance = inst,
				version = id
			});
			PageBase.Win?.FlyToTasks(anchor, I18n.L("修复中"));
		});
		((ItemsControl)menu).Items.Add((object)new Separator());
		Item(I18n.L("卸载这个版本"), () => UninstallAsync(inst, id));
		menu.PlacementTarget = (UIElement)(object)anchor;
		menu.IsOpen = true;
		void Item(string head, Func<Task> act)
		{
			MenuItem val = new MenuItem
			{
				Header = head
			};
			val.Click += (RoutedEventHandler)delegate
			{
				PageBase.Run(act);
			};
			((ItemsControl)menu).Items.Add((object)val);
		}
	}

	private void FolderMenu(FrameworkElement anchor, string instance)
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
			_folder = instance;
			await EnsureCurrentAsync();
			await LoadAsync();
			PageBase.Toast(I18n.L("默认实例"), I18n.L("之后的启动将默认使用「{0}」", instance), ToastKind.Success);
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
		Item(I18n.L("导出为整合包"), async delegate
		{
			string text2 = Dlg.SaveFile(I18n.L("Modrinth 整合包 (*.mrpack)|*.mrpack"), instance + ".mrpack", I18n.L("导出整合包"));
			if (text2 != null)
			{
				await PageBase.Api.StartTaskAsync("export_modpack", new
				{
					instance = instance,
					dest = text2
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		((ItemsControl)menu).Items.Add((object)new Separator());
		Item(I18n.L("重命名…"), async delegate
		{
			string text = await Dlg.Prompt(I18n.L("重命名实例"), I18n.L("新名称"), instance);
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
			if (await Dlg.Confirm(I18n.L("删除实例"), I18n.L("「{0}」下的版本、模组、存档都会被删除，且不可恢复。", instance), I18n.L("永久删除"), I18n.L("取消"), danger: true))
			{
				await PageBase.Api.CallAsync("delete_instance", new
				{
					name = instance
				});
				await LoadAsync();
				PageBase.Toast(I18n.L("已删除"), instance, ToastKind.Success);
			}
		});
		menu.PlacementTarget = (UIElement)(object)anchor;
		menu.IsOpen = true;
		void Item(string head, Func<Task> act)
		{
			MenuItem val = new MenuItem
			{
				Header = head
			};
			val.Click += (RoutedEventHandler)delegate
			{
				PageBase.Run(act);
			};
			((ItemsControl)menu).Items.Add((object)val);
		}
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
		}, I18n.L("自动选择"));
		ComboBox combo = Ui.Combo(opts.Select((JavaOption o) => o.Label), selected);
		string text = (await PageBase.Api.TryCallAsync("instance_java_label", new
		{
			name = instance
		}, "")) ?? "";
		SPanel body = Ui.V(8.0, (UIElement)Ui.Muted(I18n.L("留「自动选择」由启动器按版本匹配")), (UIElement)(string.IsNullOrEmpty(text) ? null : Ui.Small(I18n.L("当前记录：") + text)), (UIElement)combo);
		if (await Dlg.Ask(I18n.L("为「{0}」选择 Java", instance), (UIElement)(object)body, I18n.L("保存")))
		{
			string java = opts.FirstOrDefault((JavaOption o) => o.Label == combo.Str())?.Value ?? I18n.L("自动选择");
			await PageBase.Api.CallAsync("set_instance_java", new
			{
				name = instance,
				java = java
			});
			await LoadAsync();
		}
	}

	private async Task AddFolderAsync()
	{
		string text = Dlg.PickFolder(I18n.L("选择一个已有的游戏文件夹（含 versions 的 .minecraft）"));
		if (!string.IsNullOrWhiteSpace(text))
		{
			string fileName = Path.GetFileName(text.TrimEnd('\\', '/'));
			string text2 = await Dlg.Prompt(I18n.L("导入为文件夹"), I18n.L("显示名称"), fileName);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				await PageBase.Api.StartTaskAsync("import_existing_folder", new
				{
					path = text,
					name = text2
				});
				PageBase.Toast(I18n.L("已开始导入"), I18n.L("导入进度看「下载任务」"), ToastKind.Success);
			}
		}
	}

	private async Task ImportPackAsync()
	{
		string text = Dlg.PickFile(I18n.L("整合包 (*.mrpack;*.zip)|*.mrpack;*.zip"), I18n.L("导入整合包"));
		if (text != null && PageBase.Win != null)
		{
			await FileDrop.ImportModpackAsync(PageBase.Win, text);
		}
	}

	private async Task CreateAsync()
	{
		string text = await Dlg.Prompt(I18n.L("新建实例"), I18n.L("实例名称（会创建独立的 .minecraft）"), "", I18n.L("例如：1.20.1 生存"));
		if (!string.IsNullOrWhiteSpace(text))
		{
			await PageBase.Api.CallAsync("create_instance", new
			{
				name = text
			});
			await LoadAsync();
			PageBase.Toast(I18n.L("已创建"), text, ToastKind.Success);
		}
	}

	private async Task MigrateAsync()
	{
		if (!(await PageBase.Api.TryCallAsync("detect_official_launcher", null, fallback: false)))
		{
			PageBase.Toast(I18n.L("没找到官方启动器"), I18n.L("未检测到 .minecraft 目录"), ToastKind.Warning);
			return;
		}
		string dir = await PageBase.Api.TryCallAsync("official_launcher_dir", null, "");
		List<string> list = (await PageBase.Api.TryCallAsync("scan_official_versions", null, new List<string>())) ?? new List<string>();
		TextBox target = Ui.Input("default", "default");
		SPanel body = Ui.V(8.0, (UIElement)Ui.Muted(I18n.L("检测到：{0}", dir)), (UIElement)Ui.Muted(I18n.L("可导入 {0} 个版本：{1}{2}", list.Count, string.Join(I18n.L("、"), list.Take(8)), (list.Count > 8) ? " …" : "")), (UIElement)Ui.Txt(I18n.L("导入到实例"), 12.0, bold: false, "B.InkMuted"), (UIElement)target);
		if (await Dlg.Ask(I18n.L("导入官方启动器"), (UIElement)(object)body, I18n.L("开始导入")))
		{
			await PageBase.Api.StartTaskAsync("migrate_official_launcher", new
			{
				instance = (target.Text?.Trim() ?? "default")
			});
			PageBase.Toast(I18n.L("已开始导入"), I18n.L("进度看下载任务"));
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
