using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C7a:Windows 版 <c>Pages\VersionManagePage.cs</c>(34490 字节 / 1110 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **版本管理页**(导航 id <c>instance</c>,侧栏那一行的标题就是「版本管理」;页面自己的 <see cref="Title"/> 也是它)。
///
/// <b>【C7a 更正,Lead 裁决】导航语义修正</b>:Windows 的 <c>MainWindow.xaml.cs:110</c> 是
/// <c>new NavDef("instance", I18n.L("版本管理"), …, () => new VersionManagePage())</c> —— 也就是说**原版里
/// <c>instance</c> 这个 id 对应的就是本页**;而 <c>InstancePage</c> 在原版**只出现在它自己的构造函数那一行**、
/// **没有任何构造点**(整个 Windows 工程扫过)⇒ 它在原版是**死代码**。C4 当时把 <c>instance</c> 注册成了
/// <c>InstancePage</c>,只对上了键名、没对上页面(那处偏差已由本包改正:<c>instance</c> → 本页,
/// <c>InstancePage</c> 不再注册但**保留文件与它的测试**)。
///
/// 形状(逐条照抄 Windows):左栏折叠卡(「文件夹列表」+ 当前文件夹路径 + 实例行 + 「添加或导入」四个按钮)
/// + 右栏(「版本选择」标题行,右侧是插件动作条 / 版本计数 / 过滤框 / 刷新)+ 十个分组的版本卡
/// (收藏 / Fabric / Forge / Quilt / NeoForge / OptiFine / LiteLoader / 原版 / 不常用 / 隐藏);
/// 版本行 = 缩略瓦片 + 版本号 + 标签行(模组数 / 隔离标签)+ 副行(类型 + MC + 加载器 + 加载器版本)
/// + 行尾(隔离标签 + 独立开关 + 收藏 / 卸载 / 版本设置)。
///
/// 行为(与 Windows 一致):<c>get_instances</c> + <c>get_setting(default_instance)</c> + <c>game_root_path</c> 起表,
/// 切文件夹先把它设成默认实例(<see cref="InstancePage.SetDefaultAsync"/>)再拉版本(<c>get_version_rows</c>,含隐藏);
/// 分组折叠状态记在偏好 <c>vm:&lt;key&gt;</c>;收藏按文件夹记在 <c>Prefs.VmFavorites</c>;
/// 两个右键菜单(版本 / 实例)与一套安装、迁移、卸载路径全部照抄。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Style = Ui.S("键")</c> → <c>StyleKeys.Apply(...)</c>;<c>SetResourceReference</c> → <c>.Dyn(...)</c>;
///   <c>Visibility</c> → <c>IsVisible</c>;<c>Cursors.Hand</c> → <c>new Cursor(StandardCursorType.Hand)</c>;</item>
/// <item><c>MouseEnter/Leave</c> → <c>PointerEntered/Exited</c>,<c>MouseLeftButtonUp/Down</c> →
///   <c>PointerReleased/Pressed</c>(双击用 <c>e.ClickCount == 2</c>),<c>MouseRightButtonUp</c> →
///   <c>PointerReleased</c> + <c>e.GetCurrentPoint(...).Properties.IsRightButtonPressed</c>;</item>
/// <item><c>VisualTreeHelper.GetParent</c> → <c>GetVisualParent()</c>(<c>Avalonia.VisualTree</c> 的扩展方法);</item>
/// <item><c>menu.PlacementTarget = anchor; menu.IsOpen = true;</c> → <c>menu.Open(anchor)</c>(D-124);
///   菜单本体拆成 <see cref="BuildVerMenu"/> / <see cref="BuildFolderMenu"/>,真实路径只是 <c>.Open(anchor)</c>(D-115/D-127);</item>
/// <item>开关的 <c>Checked/Unchecked</c> → <c>IsCheckedChanged</c>(Avalonia 11.3 把前两个标成过时 CS0618);</item>
/// <item><b>插件动作条走注入缝</b>(见 <see cref="ActionBarFactory"/>):Windows 直接
///   <c>new TipCL.Pages.Plugins.PluginActionBar("instance", …)</c>,而 mac 侧 `PluginUi` / `PluginActionBar`
///   由 C7b 落地(同一文件不能两个归属)⇒ 这里只留一条"可由外部一行接上"的缝;<b>没接缝时显示一句如实的说明</b>,
///   不做静默桩。这条缝等 C7b 的 <c>PluginUi</c>/<c>PluginActionBar</c> 落地后接。</item>
/// </list>
/// </summary>
public sealed class VersionManagePage : PageBase
{
	/// <summary>一个版本的展示态(Windows 的私有 <c>Ver</c>)。内部型可见是因为菜单/切换等**测试缝**要收它作参数
	/// (同 C6a 的 <c>CatalogPage.Mode</c>:缝一旦是 internal,参数类型就不能更私有,否则 CS0051)。</summary>
	internal sealed class Ver
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

	/// <summary>一张分组卡(Windows 的私有 <c>Group</c>)。理由同 <see cref="Ver"/>。</summary>
	internal sealed class Group
	{
		public string Key = "";

		public bool Open;

		public Border Card = null!;

		public StackPanel Body = null!;

		public TextBlock HeadText = null!;

		public TextBlock Chevron = null!;

		public Border Sep = null!;
	}

	/// <summary>左栏一行实例(Windows 的私有 <c>FolderRow</c>)。</summary>
	private sealed class FolderRow
	{
		public InstanceInfo Info = new InstanceInfo();

		public Border Root = null!;

		public bool Hover;

		public void Paint(bool selected)
		{
			Root.Dyn(Border.BackgroundProperty, (selected || Hover) ? "B.AccentSoft" : "B.Paper2");
			Root.Dyn(Border.BorderBrushProperty, selected ? "B.Accent" : (Hover ? "B.AccentLite" : "B.Line"));
		}
	}

	private const string ChevUp = "\ue70e";

	/// <summary>【Windows 源】<c>:72</c> 会显示加载器版本号的六个加载器名。</summary>
	private static readonly string[] LoaderNames = new string[6] { "Fabric", "Forge", "Quilt", "NeoForge", "OptiFine", "LiteLoader" };

	/// <summary>【Windows 源】<c>:74</c>:快照版本的 id 形状(<c>22w13a</c> 之类)。</summary>
	private static readonly Regex SnapRx = new Regex("^\\d+w\\d+[a-z]", RegexOptions.IgnoreCase);

	/// <summary>【Windows 源】<c>:76-88</c> 十个分组(键 / 标题 / 默认展开)。</summary>
	private static readonly (string Key, string Title, bool Open)[] Defs = new (string, string, bool)[10]
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
		("hidden", I18n.L("隐藏的版本"), false),
	};

	private readonly StackPanel _folders = new StackPanel();

	private readonly TextBlock _folderPath = Ui.Small("").Trim();

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBox _search = Ui.Input(I18n.L("过滤版本名"), "", 170.0);

	private readonly StackPanel _groupsHost = new StackPanel();

	private readonly SPanel _empty = Ui.Empty("\ue80a", I18n.L("这个文件夹还没有版本"), I18n.L("点左侧「添加或导入」,或到「下载」页装一个"));

	private readonly List<FolderRow> _folderRows = new List<FolderRow>();

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private List<Ver> _vers = new List<Ver>();

	private string _folder = "";

	/// <summary>实例详情页「设置」分段内嵌时置位:隐藏文件夹列表栏(与实例库重复),只留版本选择。</summary>
	private bool _folderPreset;

	private Grid? _embeddedGrid;

	private Border? _folderCard;

	private string _defaultInstance = "";

	private string _rootPath = "";

	private static readonly List<string> NoFavs = new List<string>();

	/// <summary>
	/// 【C7a 注入缝】「版本选择」标题行上的**插件动作条**(Windows 是
	/// <c>new TipCL.Pages.Plugins.PluginActionBar("instance", () =&gt; new { folder, instance })</c> ——
	/// kind <c>action</c> / where <c>instance</c> 的那些插件按钮)。
	///
	/// 为什么留缝:mac 侧的 <c>PluginUi</c>(Windows <c>Pages\Plugins\PluginUi.cs</c>,494 行)与
	/// <c>PluginActionBar</c> 由 **C7b** 落地(同一个文件不能有两个归属)。缝的形状是"外部一行接上":
	/// <c>VersionManagePage.ActionBarFactory = () =&gt; new PluginActionBar("instance", …);</c>
	/// (与 <c>CrashUi.AiFixLauncher</c> / <c>InstanceDetailPage.SettingsPageFactory</c> 同族)。
	/// **没接缝时这一格显示一句如实的说明**,不静默隐藏、不假装能用;接上之后那句说明自动消失。
	/// </summary>
	internal static Func<Control?>? ActionBarFactory { get; set; }

	public override string Title => I18n.L("版本管理");

	/// <summary>实例详情页「设置」分段内嵌本页时预选该实例;预选即视为嵌入态(Windows 同名属性)。</summary>
	public string Folder
	{
		set
		{
			_folder = value;
			_folderPreset = true;
		}
	}

	public VersionManagePage()
	{
		Button addFolder = Ui.Btn(I18n.L("添加已有文件夹"), BtnKind.Soft, delegate
		{
			PageBase.Run(AddFolderAsync, I18n.L("导入失败"));
		}, "\ue8b7");
		addFolder.Stretch();
		Button importPack = Ui.Btn(I18n.L("导入整合包"), BtnKind.Soft, delegate
		{
			PageBase.Run(ImportPackAsync);
		}, "\ue7b8");
		importPack.Stretch();
		Button create = Ui.Btn(I18n.L("新建实例"), BtnKind.Soft, delegate
		{
			PageBase.Run(CreateAsync, I18n.L("创建失败"));
		}, "\ue710");
		create.Stretch();
		Button migrate = Ui.Btn(I18n.L("导入官方启动器"), BtnKind.Chip, delegate
		{
			PageBase.Run(MigrateAsync);
		}, "\ue8b5");
		migrate.Stretch();
		_folderPath.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		Border folderCard = Ui.Card(Ui.V(10.0,
			Ui.H3(I18n.L("文件夹列表")),
			_folderPath,
			_folders,
			Ui.Sep(),
			Ui.Txt(I18n.L("添加或导入"), 13.0, bold: true),
			addFolder,
			importPack,
			create,
			migrate), 14.0);
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(LoadAsync);
		}, "\ue72c");
		_search.TextChanged += delegate
		{
			RenderGroups();
		};
		Grid section = Ui.Section(I18n.L("版本选择"), I18n.L("双击版本直接启动;卡片右侧:收藏 / 卸载 / 设置"),
			Ui.H(8.0, BuildActionBarArea(), _count.VCenter(), _search.VCenter(), refresh));
		_empty.Show(false);
		SPanel right = Ui.V(12.0, section, _groupsHost, _empty);
		// 左右两栏之间留 18px 间距(与其他页面卡片间距一致),两栏都顶对齐
		folderCard.VerticalAlignment = VerticalAlignment.Top;
		right.VerticalAlignment = VerticalAlignment.Top;
		Grid shell = Ui.G(null, "272,18,*");
		shell.Add(folderCard, 0);
		shell.Add(right, 0, 2);
		_embeddedGrid = shell;
		_folderCard = folderCard;
		base.Content = PageBase.ScrollBody(shell);
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = _folder,
			PreferKind = "modpack",
		});
	}

	/// <summary>
	/// 【C7a 注入缝的落地处】这一格要么是接缝给的插件动作条,要么是一句**如实的说明**
	/// (Windows 那一条 <c>PluginActionBar</c> 完全隐藏自己;mac 侧因为缝还没接,**不能**假装"没有插件动作")。
	/// </summary>
	private Control BuildActionBarArea()
	{
		Func<Control?>? factory = ActionBarFactory;
		Control? bar = factory?.Invoke();
		if (bar != null)
		{
			return bar;
		}
		return Ui.Small(I18n.L("插件动作条未接线(等 C7b 的 PluginUi / PluginActionBar)"))
			.VCenter();
	}

	/// <summary>【Windows 源】<c>:188-214</c>:实例 / 默认实例 / 游戏根目录起表;嵌入态收掉左栏。</summary>
	protected override async Task LoadAsync()
	{
		_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_defaultInstance = await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = "",
		}, "");
		_rootPath = (await PageBase.Api.TryCallAsync("game_root_path", null, "")) ?? "";
		if (!_instances.Any((InstanceInfo i) => i.Name == _folder))
		{
			_folder = _instances.Any((InstanceInfo i) => i.Name == _defaultInstance)
				? _defaultInstance
				: (_instances.FirstOrDefault()?.Name ?? "");
		}
		// 嵌入态(实例详情「设置」分段):文件夹已由详情页定死,隐藏文件夹列表栏(与实例库重复);
		// 若预选实例失效被回落,说明不是有效嵌入,恢复双栏
		bool embedded = _folderPreset && _instances.Any((InstanceInfo i) => i.Name == _folder);
		if (_embeddedGrid != null && _folderCard != null)
		{
			_embeddedGrid.ColumnDefinitions[0].Width = new GridLength(embedded ? 0.0 : 272.0);
			_embeddedGrid.ColumnDefinitions[1].Width = new GridLength(embedded ? 0.0 : 18.0);
			_folderCard.IsVisible = !embedded;
		}
		await EnsureCurrentAsync();
		await ReloadVersionsAsync();
		RenderFolders();
		RenderGroups();
	}

	/// <summary>【Windows 源】<c>:216-219</c>:刷新就是重跑 <c>LoadAsync</c>。</summary>
	public override async Task RefreshAsync()
	{
		await LoadAsync();
	}

	/// <summary>【Windows 源】<c>:221-228</c>:当前文件夹不是默认实例时先把它设成默认(跨页调 InstancePage)。</summary>
	private async Task EnsureCurrentAsync()
	{
		if (!string.IsNullOrEmpty(_folder) && _folder != _defaultInstance)
		{
			await InstancePage.SetDefaultAsync(_folder);
			_defaultInstance = _folder;
		}
	}

	/// <summary>【Windows 源】<c>:230-247</c>:拉版本行(含隐藏),类型由 id 现推。</summary>
	private async Task ReloadVersionsAsync()
	{
		List<VersionRowDto> source = (await PageBase.Api.TryCallAsync("get_version_rows", new
		{
			include_hidden = true,
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
			IsolationLabel = r.IsolationLabel,
		}).ToList();
	}

	/// <summary>【Windows 源】<c>:249-264</c>:切文件夹(同一个就什么都不做)。</summary>
	internal void SelectFolder(string name)
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

	/// <summary>【Windows 源】<c>:266-277</c>:路径最后一段(根目录名)。</summary>
	private static string RootNameOf(string rootPath)
	{
		string text = rootPath.TrimEnd('\\', '/');
		int back = text.LastIndexOf('\\');
		int slash = text.LastIndexOf('/');
		int at = Math.Max(back, slash);
		if (at < 0)
		{
			return text;
		}
		return text.Substring(at + 1);
	}

	/// <summary>【Windows 源】<c>:279-295</c>:这个实例的文件夹路径(优先实例自己给的,否则用根目录拼)。</summary>
	internal string FolderPathFor(string name)
	{
		string? known = _instances.FirstOrDefault((InstanceInfo i) => i.Name == name)?.Path;
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
		_folders.Children.Clear();
		_folderRows.Clear();
		foreach (InstanceInfo instance in _instances)
		{
			FolderRow row = BuildFolderRow(instance);
			_folderRows.Add(row);
			_folders.Children.Add(row.Root);
		}
		UpdateFolderPath();
		PaintFolders();
	}

	private void UpdateFolderPath()
	{
		string text = FolderPathFor(_folder);
		_folderPath.Text = (text.Length > 0) ? (I18n.L("当前文件夹 ") + text) : I18n.L("当前文件夹");
		// 路径太长会被截断,悬停显示完整路径(Windows 还调了 ToolTipService.SetShowDuration,
		// Avalonia 没有那个入口 —— 丢的只是"提示显示 60 秒",如实记在差异表里)
		ToolTip.SetTip(_folderPath, (text.Length > 0) ? text : null);
	}

	/// <summary>【Windows 源】<c>:320-393</c>:左栏一行 = 图标 + 名称 + 「N 个版本」+ 默认标签 + 实例操作钮。</summary>
	private FolderRow BuildFolderRow(InstanceInfo info)
	{
		string instName = info.Name;
		TextBlock name = Ui.Txt(instName, 13.0, bold: true).Trim();
		TextBlock sub = Ui.Small(I18n.L("{0} 个版本", info.Versions)).Trim();
		SPanel mid = Ui.V(1.0, name, sub);
		mid.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
		Border icon = new Border
		{
			Width = 32.0,
			Height = 32.0,
			CornerRadius = new CornerRadius(8.0),
		};
		icon.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		icon.Child = Ui.Glyph("\ue8b7", 14.0, "B.AccentText").Center();
		SPanel tail = Ui.H(4.0);
		tail.VerticalAlignment = VerticalAlignment.Center;
		if (instName == _defaultInstance)
		{
			tail.Children.Add(Ui.Tag(I18n.L("默认"), "B.OnAccentStrong", "B.AccentStrong"));
		}
		tail.Children.Add(Ui.IconBtn("\ue712", I18n.L("实例操作"), delegate(object? s, RoutedEventArgs _)
		{
			FolderMenu((Control)s!, instName);
		}, 13.0));
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(icon.VCenter(), 0);
		grid.Add(mid.VCenter(), 0, 1);
		grid.Add(tail, 0, 2);
		Border root = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(9.0, 8.0, 9.0, 8.0),
			BorderThickness = new Thickness(1.0),
			Child = grid,
			Cursor = new Cursor(StandardCursorType.Hand),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
		};
		ToolTip.SetTip(root, FolderPathFor(instName));
		FolderRow row = new FolderRow
		{
			Info = info,
			Root = root,
		};
		root.PointerEntered += delegate
		{
			row.Hover = true;
			row.Paint(instName == _folder);
		};
		root.PointerExited += delegate
		{
			row.Hover = false;
			row.Paint(instName == _folder);
		};
		root.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
		{
			if (e.GetCurrentPoint(root).Properties.IsRightButtonPressed)
			{
				if (!HitButton(e.Source as Visual))
				{
					e.Handled = true;
					SelectFolder(instName);
					FolderMenu(root, instName);
				}
				return;
			}
			if (!HitButton(e.Source as Visual))
			{
				SelectFolder(instName);
			}
		};
		return row;
	}

	private void PaintFolders()
	{
		foreach (FolderRow row in _folderRows)
		{
			row.Paint(row.Info.Name == _folder);
		}
	}

	/// <summary>【Windows 源】<c>:403-449</c>:按分组键聚合、出卡、计数与空态。</summary>
	internal void RenderGroups()
	{
		_groupsHost.Children.Clear();
		string q = _search.Text?.Trim() ?? "";
		Dictionary<string, List<Ver>> grouped = new Dictionary<string, List<Ver>>(StringComparer.Ordinal);
		foreach (Ver v in _vers)
		{
			if (q.Length == 0 || v.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
			{
				string key = GroupKeyOf(v);
				if (!grouped.TryGetValue(key, out List<Ver>? list))
				{
					list = new List<Ver>();
					grouped[key] = list;
				}
				list.Add(v);
			}
		}
		bool searching = q.Length > 0;
		int total = 0;
		List<Control> cards = new List<Control>();
		foreach ((string key2, string title, bool open) in Defs)
		{
			if (!grouped.TryGetValue(key2, out List<Ver>? rows) || rows.Count == 0)
			{
				continue;
			}
			Group group = BuildGroup(key2, searching || PrefsOpen(key2, open));
			group.HeadText.Text = title + " (" + rows.Count + ")";
			foreach (Ver v in rows)
			{
				group.Body.Children.Add(BuildVerRow(v));
			}
			ApplyOpen(group, group.Open);
			_groupsHost.Children.Add(group.Card);
			cards.Add(group.Card);
			total += rows.Count;
		}
		_empty.Show(total == 0);
		_count.Text = I18n.L("共 {0} 个版本", total);
		if (cards.Count > 0)
		{
			Motion.StaggerItems(cards);
		}
	}

	private Group BuildGroup(string key, bool open)
	{
		Group group = new Group
		{
			Key = key,
			Open = open,
		};
		TextBlock head = Ui.Txt("", 13.5, bold: true, "B.AccentText");
		TextBlock chevron = Ui.Glyph("\ue70d", 12.0, "B.InkMuted");
		SPanel headRow = Ui.H(8.0, head, chevron);
		headRow.VerticalAlignment = VerticalAlignment.Center;
		Border header = new Border
		{
			Padding = new Thickness(12.0, 9.0, 12.0, 9.0),
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = headRow,
		};
		StackPanel body = new StackPanel
		{
			Margin = new Thickness(8.0, 2.0, 8.0, 8.0),
		};
		Border sep = Ui.Sep();
		header.PointerEntered += delegate
		{
			header.Dyn(Border.BackgroundProperty, "B.Paper2");
		};
		header.PointerExited += delegate
		{
			header.Background = null;
		};
		header.PointerReleased += delegate
		{
			ToggleGroup(group);
		};
		Border card = Ui.Card(Ui.V(0.0, header, sep, body), 0.0);
		group.Card = card;
		group.Body = body;
		group.HeadText = head;
		group.Chevron = chevron;
		group.Sep = sep;
		return group;
	}

	/// <summary>【Windows 源】<c>:494-500</c>:展开/收起只切可见性与箭头。</summary>
	private static void ApplyOpen(Group g, bool open)
	{
		g.Open = open;
		g.Body.IsVisible = open;
		g.Sep.IsVisible = open;
		g.Chevron.Text = open ? "\ue70e" : "\ue70d";
	}

	/// <summary>【Windows 源】<c>:502-511</c>:折叠状态写进偏好(<c>vm:&lt;key&gt;</c>)并落盘。</summary>
	internal void ToggleGroup(Group g)
	{
		ApplyOpen(g, !g.Open);
		MainWindow? win = PageBase.Win;
		if (win?.Prefs != null)
		{
			win.Prefs.Expanded["vm:" + g.Key] = g.Open;
			win.SaveUiPrefs();
		}
	}

	/// <summary>折叠状态的读取(Windows <c>:513-521</c>);测试缝:按 key 读一次。</summary>
	internal bool PrefsOpen(string key, bool def)
	{
		MainWindow? win = PageBase.Win;
		if (win?.Prefs != null && win.Prefs.Expanded.TryGetValue("vm:" + key, out bool value))
		{
			return value;
		}
		return def;
	}

	/// <summary>【Windows 源】<c>:523-625</c>:一行版本(缩略瓦片 + 版本号 + 标签 + 副行 + 行尾四个控件)。</summary>
	private Control BuildVerRow(Ver v)
	{
		string inst = _folder;
		string id = v.Id;
		bool fav = IsFav(id);
		SPanel tags = Ui.H(6.0);
		if (v.Mods > 0)
		{
			tags.Children.Add(Ui.Tag(I18n.L("模组 {0}", v.Mods)));
		}
		if (v.Isolated && !string.IsNullOrEmpty(v.IsolationLabel))
		{
			tags.Children.Add(Ui.Tag(v.IsolationLabel, "B.AccentDeep", "B.AccentSoft"));
		}
		SPanel headRow = Ui.H(6.0, Ui.Txt(id, 13.0, bold: true).Trim());
		if (tags.Children.Count > 0)
		{
			headRow.Children.Add(tags);
		}
		TextBlock sub = Ui.Small(SubLine(v)).Trim();
		SPanel mid = Ui.V(1.0, headRow, sub);
		Button star = new Button
		{
			Content = Ui.Glyph(fav ? "\ue735" : "\ue734", 13.0, fav ? "B.Accent" : null),
		};
		StyleKeys.Apply(star, "Btn.Icon");
		ToolTip.SetTip(star, I18n.L(fav ? "取消收藏" : "收藏"));
		star.Click += delegate
		{
			ToggleFav(v);
		};
		Button uninstall = Ui.IconBtn("\ue74d", I18n.L("卸载这个版本"), delegate
		{
			PageBase.Run(() => UninstallAsync(inst, id), I18n.L("卸载失败"));
		}, 13.0);
		Button settings = Ui.IconBtn("\ue713", I18n.L("版本设置"), delegate
		{
			PageBase.Run(async delegate
			{
				await VersionSetupDialog.ShowAsync(inst, id);
				await LoadAsync();
			});
		}, 13.0);
		StackPanel tail = new StackPanel
		{
			Orientation = Orientation.Horizontal,
		};
		// WP-S4 追加:行尾隔离标签(隔离=主题色,共享=灰「大锅饭」)+ 独立开关
		tail.Children.Add(IsolationTag(v).VCenter());
		CheckBox isolationSwitch = Ui.Switch(v.Isolated);
		ToolTip.SetTip(isolationSwitch, I18n.L("开:这个版本用自己的 mods / config;关:与实例里其它版本共用那一份"));
		// 【Avalonia 差异】Windows 分别接 Checked / Unchecked;Avalonia 11.3 标成过时(CS0618)。
		isolationSwitch.IsCheckedChanged += delegate
		{
			bool on = isolationSwitch.IsChecked == true;
			PageBase.Run(() => ToggleIsolationVerAsync(id, isolated: on), I18n.L("切换失败"));
		};
		tail.Children.Add(isolationSwitch);
		tail.Children.Add(star);
		tail.Children.Add(uninstall);
		tail.Children.Add(settings);
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(new ThumbTile(id, 34.0, 8.0).VCenter(), 0);
		grid.Add(mid.VCenter().M(9.0, 0.0, 9.0, 0.0), 0, 1);
		grid.Add(tail, 0, 2);
		Border card = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(8.0, 7.0, 8.0, 7.0),
			Child = grid,
			Cursor = new Cursor(StandardCursorType.Hand),
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
		};
		card.PointerEntered += delegate
		{
			card.Dyn(Border.BackgroundProperty, "B.Paper2");
		};
		card.PointerExited += delegate
		{
			card.Background = null;
		};
		card.PointerPressed += delegate(object? _, PointerPressedEventArgs e)
		{
			// Windows:MouseLeftButtonDown + e.ClickCount == 2 ⇒ 双击启动
			if (!HitButton(e.Source as Visual) && e.ClickCount == 2)
			{
				e.Handled = true;
				PageBase.Run(() => LaunchVerAsync(inst, id));
			}
		};
		card.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
		{
			// Windows:MouseRightButtonUp ⇒ 右键菜单
			if (e.GetCurrentPoint(card).Properties.IsRightButtonPressed && !HitButton(e.Source as Visual))
			{
				e.Handled = true;
				VerMenu(card, v);
			}
		};
		return card;
	}

	/// <summary>【Windows 源】<c>:627-637</c>:隔离标签(隔离=自己的标签,共享=「大锅饭」)。</summary>
	private static Border IsolationTag(Ver v)
	{
		if (v.Isolated)
		{
			string label = string.IsNullOrEmpty(v.IsolationLabel) ? I18n.L("完全独立") : v.IsolationLabel;
			return Ui.Tag(label, "B.AccentDeep", "B.AccentSoft");
		}
		return Ui.Tag(I18n.L("大锅饭(与其他版本共用)"));
	}

	/// <summary>【Windows 源】<c>:640-661</c>:切独立/共用(转为独立时问一次要不要复制现有模组)。</summary>
	private async Task ToggleIsolationVerAsync(string version, bool isolated)
	{
		bool seed = false;
		if (isolated)
		{
			int choice = await Dlg.Choose(I18n.L("转为独立模组"),
				Ui.Muted(I18n.L("「{0}」将拥有自己的 mods / config 目录。\n\n", version) + I18n.L("要把游戏目录里现有的共享模组复制一份过去吗?\n") + I18n.L("选「复制一份」保持现在能玩的样子;选「留空」从零开始装。")).Wrap().MinW(380.0),
				new string[3] { I18n.L("取消"), I18n.L("留空"), I18n.L("复制一份") }, 540.0);
			if (choice <= 0)
			{
				await LoadAsync();
				return;
			}
			seed = choice == 2;
		}
		await PageBase.Api.CallAsync("toggle_version_isolation", new
		{
			version,
			isolated,
			seed,
		});
		PageBase.Toast(isolated ? I18n.L("已转为独立模组") : I18n.L("已改回共用"),
			isolated
				? (I18n.L("「{0}」以后用自己的 mods / config", version) + (seed ? I18n.L(",已复制一份现有模组") : ""))
				: I18n.L("「{0}」与实例里其它版本共用 mods", version),
			ToastKind.Success);
		await LoadAsync();
	}

	/// <summary>【Windows 源】<c>:663-679</c>:副行 = 类型 + MC 版本 (+ 加载器 + 加载器版本)。</summary>
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
		string text = (type.Length > 0) ? (type + " " + v.Mc) : v.Mc;
		if (LoaderNames.Contains(v.Loader))
		{
			string loaderVer = LoaderVerOf(v.Id, v.Loader);
			text = text + "," + v.Loader + ((loaderVer.Length > 0) ? (" " + loaderVer) : "");
		}
		return text;
	}

	/// <summary>【Windows 源】<c>:681-694</c>:从版本 id 里抠出加载器版本号。</summary>
	private static string LoaderVerOf(string id, string loader)
	{
		int at = id.IndexOf(loader, StringComparison.OrdinalIgnoreCase);
		if (at < 0)
		{
			return "";
		}
		string text = id.Substring(at + loader.Length).Trim(' ', '-', '_', ':');
		if (text.Length == 0 || text.Length > 24)
		{
			return "";
		}
		return text;
	}

	/// <summary>【Windows 源】<c>:696-712</c>:按 id 推版本类型(与 LibraryPage.InferType 同一套判据)。</summary>
	internal static string InferType(string id)
	{
		string text = id.ToLowerInvariant();
		if (text.StartsWith("rd-", StringComparison.Ordinal) || text.StartsWith("a1.", StringComparison.Ordinal) ||
			text.StartsWith("c0.", StringComparison.Ordinal) || text.StartsWith("inf-", StringComparison.Ordinal) ||
			text.StartsWith("alpha", StringComparison.Ordinal))
		{
			return "old_alpha";
		}
		if (text.StartsWith("b1.", StringComparison.Ordinal))
		{
			return "old_beta";
		}
		if (SnapRx.IsMatch(text) || text.Contains("-pre", StringComparison.Ordinal) || text.Contains("-rc", StringComparison.Ordinal))
		{
			return "snapshot";
		}
		return "release";
	}

	/// <summary>【Windows 源】<c>:714-745</c>:分组键(隐藏 → 收藏 → 加载器 → 原版/不常用)。</summary>
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
		return v.Loader switch
		{
			"Fabric" => "fabric",
			"Forge" => "forge",
			"Quilt" => "quilt",
			"NeoForge" => "neoforge",
			"OptiFine" => "optifine",
			"LiteLoader" => "liteloader",
			_ => (v.Type == "release") ? "vanilla" : "uncommon",
		};
	}

	/// <summary>【Windows 源】<c>:747-760</c>:当前文件夹的收藏集合(存在偏好里,没有就建)。</summary>
	private List<string> Favs()
	{
		Dictionary<string, List<string>>? all = PageBase.Win?.Prefs.VmFavorites;
		if (all == null)
		{
			return NoFavs;
		}
		if (!all.TryGetValue(_folder, out List<string>? list))
		{
			list = new List<string>();
			all[_folder] = list;
		}
		return list;
	}

	/// <summary>这个版本是不是收藏了(测试缝;同 Windows 的私有 <c>IsFav</c>)。</summary>
	internal bool IsFav(string id)
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

	/// <summary>【Windows 源】<c>:778-787</c>:双击 = 设默认 + 让启动页优先用这个实例/版本 + 跳启动页。</summary>
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

	/// <summary>【Windows 源】<c>:789-800</c>:卸载一个版本(先确认;spec = <c>实例 / 版本</c>)。</summary>
	internal async Task UninstallAsync(string inst, string ver)
	{
		if (await Dlg.Confirm(I18n.L("卸载版本"), I18n.L("确定卸载「{0}」?该版本目录下的独立模组与存档会一并删除。", ver), I18n.L("卸载"), I18n.L("取消"), danger: true))
		{
			await PageBase.Api.CallAsync("uninstall_version", new
			{
				spec = inst + " / " + ver,
			});
			await LoadAsync();
			PageBase.Toast(I18n.L("已卸载"), ver, ToastKind.Success);
		}
	}

	/// <summary>版本行的右键菜单(真实路径只是 <c>BuildVerMenu(...).Open(anchor)</c>,见 D-124)。</summary>
	private void VerMenu(Control anchor, Ver v)
	{
		BuildVerMenu(anchor, v).Open(anchor);
	}

	/// <summary>
	/// 【Windows 源】<c>:802-917</c>:版本行的右键菜单(11 项,含两处分隔线)。
	/// 【C7a 测试缝】菜单本体拆出来(同 D-115 / D-127 的口径):Avalonia 的 <c>ContextMenu</c> 一旦
	/// <c>Open</c> 就落进独立 popup 宿主,测试拿不到也点不到。
	/// </summary>
	internal ContextMenu BuildVerMenu(Control anchor, Ver v)
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
				which = "root",
			});
		});
		Item(I18n.L("打开游戏文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_version_folder", new
			{
				instance = inst,
				version = id,
				which = "game",
			});
		});
		Item(I18n.L("存档管理..."), async delegate
		{
			await SavesDialog.ShowAsync(inst, id);
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名"), async delegate
		{
			string? next = await Dlg.Prompt(I18n.L("重命名版本"), I18n.L("新版本 ID"), id);
			if (!string.IsNullOrWhiteSpace(next) && !(next == id))
			{
				await PageBase.Api.CallAsync("rename_version", new
				{
					instance = inst,
					version = id,
					new_id = next,
				});
				await LoadAsync();
			}
		});
		Item(I18n.L("复制一份"), async delegate
		{
			string? copy = await Dlg.Prompt(I18n.L("复制版本"), I18n.L("新版本 ID"), id + "-copy");
			if (!string.IsNullOrWhiteSpace(copy))
			{
				await PageBase.Api.CallAsync("copy_version", new
				{
					instance = inst,
					version = id,
					new_id = copy,
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
				hidden = !v.Hidden,
			});
			await LoadAsync();
			PageBase.Toast(v.Hidden ? I18n.L("已取消隐藏") : I18n.L("已隐藏"), id, ToastKind.Success);
		});
		Item(I18n.L("创建桌面快捷方式"), async delegate
		{
			string text = await PageBase.Api.CallAsync<string>("create_desktop_shortcut", new
			{
				instance = inst,
				version = id,
			});
			await Dlg.Alert(I18n.L("已创建"), I18n.L("桌面快捷方式:\n{0}\n\n双击即可直接启动该版本。", text));
		});
		Item(I18n.L("导出启动脚本"), async delegate
		{
			string? dest = Dlg.SaveFile(I18n.L("批处理 (*.bat)|*.bat"), "launch-" + inst + "-" + id + ".bat", I18n.L("导出启动脚本"));
			if (dest != null)
			{
				await PageBase.Api.StartTaskAsync("export_launch_script", new
				{
					instance = inst,
					version = id,
					dest,
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		Item(I18n.L("修复(补全缺失文件)"), async delegate
		{
			await PageBase.Api.StartTaskAsync("repair_version", new
			{
				instance = inst,
				version = id,
			});
			PageBase.Win?.FlyToTasks(anchor, I18n.L("修复中"));
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("卸载这个版本"), () => UninstallAsync(inst, id));
		return menu;

		void Item(string head, Func<Task> act)
		{
			MenuItem item = new MenuItem
			{
				Header = head,
			};
			item.Click += delegate
			{
				PageBase.Run(act);
			};
			menu.Items.Add(item);
		}
	}

	/// <summary>左栏实例行的右键菜单(同上;真实路径 <c>FolderMenu</c> 只是 <c>.Open(anchor)</c>)。</summary>
	private void FolderMenu(Control anchor, string instance)
	{
		BuildFolderMenu(anchor, instance).Open(anchor);
	}

	/// <summary>【Windows 源】<c>:919-998</c>:左栏实例行的右键菜单(11 项,第 7 项前一条分隔线)。</summary>
	internal ContextMenu BuildFolderMenu(Control anchor, string instance)
	{
		ContextMenu menu = new ContextMenu();
		Item(I18n.L("打开文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_folder", new
			{
				name = instance,
			});
		});
		Item(I18n.L("设为默认实例"), async delegate
		{
			_folder = instance;
			await EnsureCurrentAsync();
			await LoadAsync();
			PageBase.Toast(I18n.L("默认实例"), I18n.L("之后的启动将默认使用「{0}」", instance), ToastKind.Success);
		});
		Item(I18n.L("选择 Java..."), () => PickJavaAsync(instance));
		Item(I18n.L("管理模组"), async delegate
		{
			await ModsDialog.ShowAsync(instance);
		});
		Item(I18n.L("存档管理..."), async delegate
		{
			await SavesDialog.ShowAsync(instance, "");
		});
		Item(I18n.L("导出为整合包"), async delegate
		{
			string? dest = Dlg.SaveFile(I18n.L("Modrinth 整合包 (*.mrpack)|*.mrpack"), instance + ".mrpack", I18n.L("导出整合包"));
			if (dest != null)
			{
				await PageBase.Api.StartTaskAsync("export_modpack", new
				{
					instance,
					dest,
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名..."), async delegate
		{
			string? next = await Dlg.Prompt(I18n.L("重命名实例"), I18n.L("新名称"), instance);
			if (!string.IsNullOrWhiteSpace(next) && !(next == instance))
			{
				await PageBase.Api.CallAsync("rename_instance", new
				{
					name = instance,
					new_name = next,
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
					name = instance,
				});
				await LoadAsync();
				PageBase.Toast(I18n.L("已删除"), instance, ToastKind.Success);
			}
		});
		return menu;

		void Item(string head, Func<Task> act)
		{
			MenuItem item = new MenuItem
			{
				Header = head,
			};
			item.Click += delegate
			{
				PageBase.Run(act);
			};
			menu.Items.Add(item);
		}
	}

	/// <summary>【Windows 源】<c>:1000-1033</c>:给实例挑 Java(<c>java_combo_options</c> → 弹卡 → <c>set_instance_java</c>)。</summary>
	private async Task PickJavaAsync(string instance)
	{
		List<JavaOption> opts = (await PageBase.Api.TryCallAsync("java_combo_options", new
		{
			instance,
			scan_system = true,
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (opts.Count == 0)
		{
			PageBase.Toast(I18n.L("没找到 Java"), I18n.L("去 Java 页下载一个"), ToastKind.Warning);
			return;
		}
		string selected = await PageBase.Api.TryCallAsync("java_combo_label_for", new
		{
			instance,
			options = opts,
		}, I18n.L("自动选择"));
		ComboBox combo = Ui.Combo(opts.Select((JavaOption o) => o.Label), selected);
		string current = (await PageBase.Api.TryCallAsync("instance_java_label", new
		{
			name = instance,
		}, "")) ?? "";
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("留「自动选择」由启动器按版本匹配")), string.IsNullOrEmpty(current) ? null : Ui.Small(I18n.L("当前记录:") + current), combo);
		if (await Dlg.Ask(I18n.L("为「{0}」选择 Java", instance), body, I18n.L("保存")))
		{
			string java = opts.FirstOrDefault((JavaOption o) => o.Label == combo.Str())?.Value ?? I18n.L("自动选择");
			await PageBase.Api.CallAsync("set_instance_java", new
			{
				name = instance,
				java,
			});
			await LoadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:1035-1052</c>:「添加已有文件夹」(选目录 → 问显示名 → <c>import_existing_folder</c>)。</summary>
	private async Task AddFolderAsync()
	{
		string? path = Dlg.PickFolder(I18n.L("选择一个已有的游戏文件夹(含 versions 的 .minecraft)"));
		if (!string.IsNullOrWhiteSpace(path))
		{
			string fallback = Path.GetFileName(path.TrimEnd('\\', '/'));
			string? name = await Dlg.Prompt(I18n.L("导入为文件夹"), I18n.L("显示名称"), fallback);
			if (!string.IsNullOrWhiteSpace(name))
			{
				await PageBase.Api.StartTaskAsync("import_existing_folder", new
				{
					path,
					name,
				});
				PageBase.Toast(I18n.L("已开始导入"), I18n.L("导入进度看「下载任务」"), ToastKind.Success);
			}
		}
	}

	/// <summary>【Windows 源】<c>:1054-1061</c>:「导入整合包」整个交给 <see cref="FileDrop.ImportModpackAsync"/>。</summary>
	private async Task ImportPackAsync()
	{
		string? file = Dlg.PickFile(I18n.L("整合包 (*.mrpack;*.zip)|*.mrpack;*.zip"), I18n.L("导入整合包"));
		if (file != null && PageBase.Win != null)
		{
			await FileDrop.ImportModpackAsync(PageBase.Win, file);
		}
	}

	/// <summary>【Windows 源】<c>:1063-1075</c>:「新建实例」(问名字 → <c>create_instance</c>)。</summary>
	private async Task CreateAsync()
	{
		string? name = await Dlg.Prompt(I18n.L("新建实例"), I18n.L("实例名称(会创建独立的 .minecraft)"), "", I18n.L("例如:1.20.1 生存"));
		if (!string.IsNullOrWhiteSpace(name))
		{
			await PageBase.Api.CallAsync("create_instance", new
			{
				name,
			});
			await LoadAsync();
			PageBase.Toast(I18n.L("已创建"), name, ToastKind.Success);
		}
	}

	/// <summary>【Windows 源】<c>:1077-1096</c>:「导入官方启动器」(探测 → 列出可导入版本 → <c>migrate_official_launcher</c>)。</summary>
	private async Task MigrateAsync()
	{
		if (!(await PageBase.Api.TryCallAsync("detect_official_launcher", null, fallback: false)))
		{
			PageBase.Toast(I18n.L("没找到官方启动器"), I18n.L("未检测到 .minecraft 目录"), ToastKind.Warning);
			return;
		}
		string dir = await PageBase.Api.TryCallAsync("official_launcher_dir", null, "");
		List<string> versions = (await PageBase.Api.TryCallAsync("scan_official_versions", null, new List<string>())) ?? new List<string>();
		TextBox target = Ui.Input("default", "default");
		SPanel body = Ui.V(8.0,
			Ui.Muted(I18n.L("检测到:{0}", dir)),
			Ui.Muted(I18n.L("可导入 {0} 个版本:{1}{2}", versions.Count, string.Join(I18n.L("、"), versions.Take(8)), (versions.Count > 8) ? " ..." : "")),
			Ui.Txt(I18n.L("导入到实例"), 12.0, bold: false, "B.InkMuted"),
			target);
		if (await Dlg.Ask(I18n.L("导入官方启动器"), body, I18n.L("开始导入")))
		{
			await PageBase.Api.StartTaskAsync("migrate_official_launcher", new
			{
				instance = target.Text?.Trim() ?? "default",
			});
			PageBase.Toast(I18n.L("已开始导入"), I18n.L("进度看下载任务"));
		}
	}

	/// <summary>【Windows 源】<c>:1098-1109</c>:这层可视链上有没有按钮(WPF 看 <c>Button</c> 祖先)。</summary>
	private static bool HitButton(Visual? d)
	{
		Visual? cur = d;
		while (cur != null)
		{
			if (cur is Button)
			{
				return true;
			}
			cur = cur.GetVisualParent();
		}
		return false;
	}
}
