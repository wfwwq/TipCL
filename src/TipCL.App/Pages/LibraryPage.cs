using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
/// C4:Windows 版 <c>Pages\LibraryPage.cs</c>(31560 字节 / 1075 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **实例库**(导航 id <c>library</c>)。左栏文件夹列表(300~380 宽,可经布局文档整栏收起)+ 右栏版本选择
/// (按加载器分组、可折叠、带搜索)。
///
/// 形状(逐条照抄 Windows,含 2026-11 效果图那一版):
/// <list type="bullet">
/// <item>左栏:「文件夹列表」大标题 + 当前路径(可 ToolTip)+ 实例行(图标 / 名称 / 「N 个版本」/ 默认标签 / 操作钮)
///   + 分隔线 + 「添加或导入」四个动作(添加已有文件夹 / 导入整合包 / 新建实例 / 导入官方启动器);</item>
/// <item>右栏:「版本选择」大标题 + 计数 + 搜索框 + 「实例详情」+ 「刷新」+ 副说明(双击版本直接启动;行尾可收藏/删除/设置)
///   + 七个加载器分组卡(Fabric / Forge / Quilt / NeoForge / OptiFine / LiteLoader / 原版),组头有折叠箭头与「已折叠」;</item>
/// <item>版本行:加载器图标 + 版本 id + 类型/快照/模组数/隔离标签 + 「正式版 1.20.1,fabric · Java ... · 上次游玩 ...」副行
///   + 收藏 / 卸载 / 版本设置三个行尾钮。</item>
/// </list>
///
/// <b>WP-U3:这个页面不套 <c>BlockHost</c></b>,而是自己用 <see cref="PageLayoutStore"/> 直读直写 <c>"library"</c>
/// 布局文档:右键「自定义布局...」可显隐文件夹栏(勾选即时生效并落盘)、导出/导入布局;版本列表恒可见
/// (<c>list</c> 的 <c>Hidden</c> 被强制回 false)。
///
/// 数据/行为与 Windows 同源:<c>get_instances</c> / <c>get_setting(default_instance)</c> / <c>game_root_path</c> /
/// <c>get_version_rows</c> / <c>get_playtime</c>(派生「上次游玩」,无需新后端方法)/ <c>uninstall_version</c> /
/// <c>open_instance_folder</c> / <c>rename_instance</c> / <c>delete_instance</c> / <c>create_instance</c> /
/// <c>import_existing_folder</c> / <c>migrate_official_launcher</c> / <c>detect_official_launcher</c> /
/// <c>official_launcher_dir</c> / <c>scan_official_versions</c> / Java 四件套。收藏走 <c>Prefs.VmFavorites</c>、
/// 折叠状态走 <c>Prefs.Expanded["lib:&lt;key&gt;"]</c>(与 Windows 同键名)。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内注释里):
/// <list type="number">
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;<c>Visibility.Collapsed</c> → <c>IsVisible = false</c>;</item>
/// <item><c>SetResourceReference(属性, 键)</c> → <c>UiExt.Dyn(控件, 属性, 键)</c>;
///   <c>Style = Ui.S("Btn.Icon")</c> → <c>StyleKeys.Apply(控件, "Btn.Icon")</c>;</item>
/// <item><c>Cursors.Hand</c> → <c>new Cursor(StandardCursorType.Hand)</c>;
///   <c>MouseEnter</c> / <c>MouseLeave</c> / <c>MouseLeftButtonUp</c> / <c>MouseLeftButtonDown</c> →
///   <c>PointerEntered</c> / <c>PointerExited</c> / <c>PointerReleased</c> / <c>PointerPressed</c>;</item>
/// <item><c>FontWeights.ExtraBold</c> → <c>FontWeight.ExtraBold</c>;<c>TextWrapping.Wrap</c> 同名(<c>Avalonia.Media</c>);</item>
/// <item><c>控件.ToolTip = 文本</c> → <c>ToolTip.SetTip(控件, 文本)</c>(Avalonia 把 ToolTip 做成挂载属性);
///   <c>ToolTipService.SetShowDuration(控件, 60000)</c> **无法照抄** —— Avalonia 11.3.23 的 <c>ToolTip</c>
///   只有 <c>ShowDelay</c>、没有 <c>ShowDuration</c>(本机核过),这一句舍去并在 <see cref="UpdateFolderPath"/> 里写明;</item>
/// <item><c>MenuItem.IsCheckable = true</c> → <c>ToggleType = MenuItemToggleType.CheckBox</c>
///   (Avalonia 11 把"可勾选"做成三态枚举:None / CheckBox / Radio);</item>
/// <item><c>VisualTreeHelper.GetParent(d)</c> → <c>d.GetVisualParent()</c>
///   (Avalonia 11 没有 <c>VisualTreeHelper</c> 这个类,等价物是 <c>Avalonia.VisualTree</c> 的扩展方法);</item>
/// <item><c>e.OriginalSource as DependencyObject</c> → <c>e.Source as Visual</c>,
///   <c>VisualTreeHelper.GetParent(d)</c> → <c>d.GetVisualParent()</c>(见 <see cref="HitButton"/>);</item>
/// <item><c>menu.PlacementTarget = anchor; menu.IsOpen = true;</c> → <c>menu.Open(anchor)</c>
///   (Avalonia 的 <c>MenuBase.IsOpen</c> setter 不可见,且无参 <c>Open()</c> 会抛 —— 见 D-124);</item>
/// <item><c>FileDrop.ImportModpackAsync(Win, file)</c>:mac 的 <c>Win</c> 静态类型是 <c>MainWindow?</c>,
///   所以这里先抄进一个局部变量再判空(Windows 的 <c>Win</c> 是非空单例)—— 语义不变;</item>
/// <item><c>BclExtensions.Contains(s, q, StringComparison)</c>(net48 的补丁扩展)→ net8 的
///   <c>string.Contains(string, StringComparison)</c>(同名同义,BCL 自带)。</item>
/// </list>
///
/// <b>忠实照抄、未改的一处 Windows 写法(用户可见,请 Lead 知悉)</b>:
/// <see cref="FolderPathFor"/> 在实例没有给出 <c>Path</c> 时用
/// <c>_rootPath.TrimEnd('\\','/') + "\\" + name</c> 拼一条**反斜杠**路径。mac 上传进来的实例
/// <c>Path</c> 一般非空(后端给的是真路径),所以这段只影响"路径缺失时的兜底"这一支;本包按"忠实移植"保留,
/// 但它在 macOS 上会显示成 Windows 风格的路径 —— 记在 PROGRESS 的 C4 小节与 D-133 里,请 Lead 决定是否单开小包改。
///
/// <b>页内切到实例详情</b>:<c>InstanceDetailPage</c> **不是**导航页(它没有自己的 id),Windows 也是这样 ——
/// 「实例详情」按钮把 <c>Content</c> 换成一份 <c>InstanceDetailPage</c>,它的「返回」再把原内容换回来。
/// </summary>
public sealed class LibraryPage : PageBase
{
	/// <summary>版本行(Windows 的私有 <c>Ver</c>):页面内部用的视图模型,不是后端 DTO(<see cref="VersionRowDto"/> 才是)。</summary>
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

		public long LastPlayed;
	}

	/// <summary>一个加载器分组(Windows 的私有 <c>Group</c>)。</summary>
	private sealed class Group
	{
		public string Key = "";

		public bool Open;

		public Border Card = new Border();

		public StackPanel Body = new StackPanel();

		public TextBlock Chevron = new TextBlock();

		/// <summary>「已折叠」小字(Windows 是 <c>FrameworkElement</c>,mac 是 <c>Control</c>)。</summary>
		public Control Folded = new TextBlock();
	}

	/// <summary>左栏一行文件夹(Windows 的私有 <c>FolderRow</c>;<c>Paint</c> 就是那两条动态资源赋值)。</summary>
	private sealed class FolderRow
	{
		public InstanceInfo Info = new InstanceInfo();

		public Border Root = new Border();

		public bool Hover;

		/// <summary>选中 / 悬停换底色与描边色(三条分支与 Windows 逐字对应)。</summary>
		public void Paint(bool selected)
		{
			Root.Dyn(Border.BackgroundProperty, (selected || Hover) ? "B.AccentSoft" : "B.Paper2");
			Root.Dyn(Border.BorderBrushProperty, selected ? "B.Accent" : (Hover ? "B.AccentLite" : "B.Line"));
		}
	}

	private static readonly string[] LoaderKeys = new string[6] { "fabric", "forge", "quilt", "neoforge", "optifine", "liteloader" };

	private static readonly string[] LoaderNames = new string[6] { "Fabric", "Forge", "Quilt", "NeoForge", "OptiFine", "LiteLoader" };

	/// <summary>快照版 id 的形状(<c>1.20.1-pre1</c> 之类由 <see cref="InferType"/> 的另两支兜)。</summary>
	private static readonly Regex _SnapRx = new Regex("^\\d+w\\d+[a-z]", RegexOptions.IgnoreCase);

	/// <summary>七个分组的键/标题/默认展开(Windows 是 <c>static readonly</c>,文案在类型初始化那一刻就定死 —— 逐字保留)。</summary>
	private static readonly (string Key, string Title, bool Open)[] Defs = new(string, string, bool)[7]
	{
		("fabric", I18n.L("Fabric 版本"), true),
		("forge", I18n.L("Forge 版本"), true),
		("quilt", I18n.L("Quilt 版本"), true),
		("neoforge", I18n.L("NeoForge 版本"), true),
		("optifine", I18n.L("OptiFine 版本"), true),
		("liteloader", I18n.L("LiteLoader 版本"), true),
		("vanilla", I18n.L("原版版本"), true),
	};

	private readonly StackPanel _folders = new StackPanel();

	private readonly TextBlock _folderPath = Ui.Small("").Trim();

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBox _search = Ui.Input(I18n.L("过滤版本名"), "", 170.0);

	private readonly StackPanel _groupsHost = new StackPanel();

	private readonly SPanel _empty = Ui.Empty("\ue80a", I18n.L("这个文件夹还没有版本"), I18n.L("点左侧「添加或导入」,或到「下载」页装一个"));

	private readonly List<FolderRow> _folderRows = new List<FolderRow>();

	/// <summary>
	/// C4:按分组键记账的当前分组(Windows 不存这个表 —— 它靠事件闭包直接拿到 <c>Group</c>)。
	/// 存在的唯一理由是给测试与"告知式驱动"一个**按 key** 折叠分组的入口(见 <see cref="ToggleGroupForTest"/>);
	/// 渲染逻辑本身一个字节没改:每次 <see cref="RenderGroups"/> 开头清空、建组时登记。
	/// </summary>
	private readonly Dictionary<string, Group> _groups = new Dictionary<string, Group>(StringComparer.Ordinal);

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private List<Ver> _vers = new List<Ver>();

	private string _folder = "";

	private string _defaultInstance = "";

	private string _rootPath = "";

	/// <summary>没有外壳 / 没有偏好时的空收藏表(Windows 同:一个共享的只读空表,免得每次现 new)。</summary>
	private static readonly List<string> _noFavs = new List<string>();

	/// <summary>页内切换前的原始内容(进入实例详情后返回时恢复;PageBase.Content 是 object)。</summary>
	private object? _shell;

	// WP-U3 页面布局自定义:双栏外壳与左右两栏(文件夹栏可经布局文档收起,字段与数据逻辑不受影响)
	private readonly Grid _body;

	private readonly Border _leftCard;

	private readonly SPanel _rightCol;

	public override string Title => I18n.L("实例库");

	public LibraryPage()
	{
		// 左栏:文件夹列表卡
		Button add = Ui.Btn(I18n.L("添加已有文件夹"), BtnKind.Chip, delegate
		{
			Run(AddFolderAsync, I18n.L("导入失败"));
		}, "\ue8b7");
		add.Stretch();
		Button pack = Ui.Btn(I18n.L("导入整合包"), BtnKind.Chip, delegate
		{
			Run(ImportPackAsync);
		}, "\ue7b8");
		pack.Stretch();
		Button create = Ui.Btn(I18n.L("新建实例"), BtnKind.Soft, delegate
		{
			Run(CreateAsync, I18n.L("创建失败"));
		}, "\ue710");
		create.Stretch();
		Button migrate = Ui.Btn(I18n.L("导入官方启动器"), BtnKind.Chip, delegate
		{
			Run(MigrateAsync);
		}, "\ue8b5");
		migrate.Stretch();
		_folderPath.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		TextBlock h = Ui.Txt(I18n.L("文件夹列表"), 18.0, bold: true);
		h.FontWeight = FontWeight.ExtraBold;
		Border left = Ui.Card(Ui.V(10.0, h, _folderPath, _folders, Ui.Sep(), Ui.Txt(I18n.L("添加或导入"), 13.0, bold: true), add, pack, create, migrate), 14.0);
		// 左栏随窗口比例伸缩,窄窗不挤右栏(2026-11-07 用户反馈:420 固定宽在窄窗占比过大)
		left.MinWidth = 300.0;
		left.MaxWidth = 380.0;
		left.VerticalAlignment = VerticalAlignment.Top;
		// 右栏头部两行:第一行 标题+计数+搜索+刷新;第二行 副说明完整换行显示
		TextBlock h2 = Ui.Txt(I18n.L("版本选择"), 21.0, bold: true);
		h2.FontWeight = FontWeight.ExtraBold;
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			Run(LoadAsync);
		}, "\ue72c");
		_search.TextChanged += delegate
		{
			RenderGroups();
		};
		// 「实例详情」入口(2026-11-07 拍板):版本选择区头部动作簇,刷新钮左侧
		Button detail = Ui.Btn(I18n.L("实例详情"), BtnKind.Chip, delegate
		{
			Run(OpenDetailAsync, I18n.L("打开失败"));
		}, "\ue946");
		Grid head = Ui.G("Auto,Auto", "Auto,Auto,*,Auto,Auto,Auto");
		TextBlock subHead = Ui.Txt(I18n.L("双击版本直接启动;行尾可收藏/删除/设置"), 12.5, bold: false, "B.InkMuted");
		subHead.TextWrapping = TextWrapping.Wrap;
		head.Add(h2.VCenter(), 0, 0);
		head.Add(_count.VCenter().M(10.0, 0.0, 10.0, 0.0), 0, 1);
		head.Add(_search.VCenter(), 0, 3);
		head.Add(detail.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 4);
		head.Add(refresh.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 5);
		head.Add(subHead, 1, 0, 1, 6);
		_empty.Show(shown: false);
		SPanel right = Ui.V(12.0, head, _groupsHost, _empty);
		right.VerticalAlignment = VerticalAlignment.Top;
		_leftCard = left;
		_rightCol = right;
		// 行高显式 Auto:网格自身高度恒等于内容,杜绝隐式 * 行被外层余量撑高(左栏卡片随之拉伸的根因向量)
		_body = Ui.G("Auto");
		base.Content = PageBase.ScrollBody(_body);
		ApplyFolderLayout();
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = _folder,
			PreferKind = "modpack"
		});
	}

	// ————— 页面布局自定义(WP-U3):双栏页不套 BlockHost,直接用 PageLayoutStore 读写 "library" 文档 —————

	// 读取布局文档并补全:list 恒可见(Hidden 强制 false),folders 条目缺失时按可见补齐
	private static PageLayoutDoc LoadLayoutDoc()
	{
		PageLayoutDoc doc = PageLayoutStore.Load("library") ?? PageLayoutDoc.Default(new[] { "folders", "list" }, new[] { "folders", "list" });
		doc.Blocks ??= new List<BlockState>();
		bool hasFolders = false;
		foreach (BlockState b in doc.Blocks)
		{
			if (b.Id == "folders")
			{
				hasFolders = true;
			}
			else if (b.Id == "list")
			{
				b.Hidden = false; // 版本列表是页面本体,任何情况下不允许隐藏
			}
		}
		if (!hasFolders)
		{
			doc.Blocks.Add(new BlockState { Id = "folders", Hidden = false });
		}
		return doc;
	}

	/// <summary>文件夹栏现在可见吗(布局文档里 <c>folders.Hidden</c> 取反;条目缺失按可见)。</summary>
	internal static bool FoldersVisible()
	{
		foreach (BlockState b in LoadLayoutDoc().Blocks)
		{
			if (b.Id == "folders")
			{
				return !b.Hidden;
			}
		}
		return true;
	}

	// 按布局文档应用文件夹栏显隐:可见 → 双栏("*,18,2*",与现状一致);隐藏 → 列定义改 "*"、左栏移出可视树(数据逻辑照常),右栏独占
	private void ApplyFolderLayout()
	{
		bool folders = FoldersVisible();
		_body.ColumnDefinitions.Clear();
		// 右栏(版本选择)是页面本体,任何布局下都必须挂在 _body 里
		if (!_body.Children.Contains(_rightCol))
		{
			_body.Children.Add(_rightCol);
		}
		if (folders)
		{
			_body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
			_body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18.0) });
			_body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.0, GridUnitType.Star) });
			if (!_body.Children.Contains(_leftCard))
			{
				_body.Children.Add(_leftCard);
			}
			Grid.SetColumn(_rightCol, 2);
		}
		else
		{
			_body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
			_body.Children.Remove(_leftCard);
			Grid.SetColumn(_rightCol, 0);
		}
		_body.ContextMenu = LayoutMenu(folders);
	}

	// 右键「自定义布局...」:显隐文件夹栏(勾选即时生效并保存)、导出/导入布局(导入由 PageLayoutIO 落盘,重读文档立即生效)
	private ContextMenu LayoutMenu(bool foldersShown)
	{
		ContextMenu menu = new ContextMenu();
		MenuItem root = new MenuItem
		{
			Header = I18n.L("自定义布局...")
		};
		MenuItem foldersItem = new MenuItem
		{
			Header = I18n.L("显示文件夹栏"),
			// 【Avalonia 差异】WPF 是 IsCheckable = true;Avalonia 11 的 MenuItem 改叫
			// ToggleType = MenuItemToggleType.CheckBox(语义相同:可勾选、点了不收起菜单)
			ToggleType = MenuItemToggleType.CheckBox,
			IsChecked = foldersShown
		};
		foldersItem.Click += delegate
		{
			// 勾选项点击默认不收起菜单,这里主动关掉再应用(Avalonia 的 MenuBase 只有 Close())
			menu.Close();
			PageLayoutDoc doc = LoadLayoutDoc();
			foreach (BlockState b in doc.Blocks)
			{
				if (b.Id == "folders")
				{
					b.Hidden = !(foldersItem.IsChecked == true);
				}
			}
			PageLayoutStore.Save("library", doc);
			ApplyFolderLayout();
		};
		MenuItem exportItem = new MenuItem
		{
			Header = I18n.L("导出布局")
		};
		exportItem.Click += delegate
		{
			PageLayoutIO.ExportToFileDialog("library");
		};
		MenuItem importItem = new MenuItem
		{
			Header = I18n.L("导入布局")
		};
		importItem.Click += delegate
		{
			// 取消(null)不动布局;导入成功后 PageLayoutIO 已落盘,重读文档立即生效
			if (PageLayoutIO.ImportFromFileDialog().HasValue)
			{
				ApplyFolderLayout();
			}
		};
		root.Items.Add(foldersItem);
		root.Items.Add(exportItem);
		root.Items.Add(importItem);
		menu.Items.Add(root);
		return menu;
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
		// 当前文件夹还在(按名字找)就留着,否则退回默认实例、再退回第一个实例、最后空串
		if (!_instances.Any((InstanceInfo i) => i.Name == _folder))
		{
			_folder = (_instances.Any((InstanceInfo i) => i.Name == _defaultInstance) ? _defaultInstance : (_instances.FirstOrDefault()?.Name ?? ""));
		}
		await EnsureCurrentAsync();
		await ReloadVersionsAsync();
		RenderFolders();
		RenderGroups();
	}

	/// <summary>刷新:Windows 这里是 <c>await LoadAsync()</c> —— **不看** <c>_loaded</c>(与基类的默认行为不同,逐字照抄)。</summary>
	public override async Task RefreshAsync()
	{
		await LoadAsync();
	}

	// 与 VersionManagePage 一致:get_version_rows 返回当前(默认)实例的版本,切文件夹先设默认
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
		// 上次游玩:从 get_playtime 的 sessions(start 时间戳 + version)派生每版本最近一次启动,无需新后端
		Dictionary<string, long> lastPlayed = new Dictionary<string, long>();
		JsonElement playtime = await Api.TryCallAsync<JsonElement>("get_playtime", new { instance = _defaultInstance });
		if (playtime.ValueKind == JsonValueKind.Object && playtime.TryGetProperty("sessions", out JsonElement sessions) && sessions.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in sessions.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("version", out JsonElement vEl) || !item.TryGetProperty("start", out JsonElement sEl) || !sEl.TryGetInt64(out long start))
				{
					continue;
				}
				string vid = vEl.ToString();
				if (vid.Length > 0 && (!lastPlayed.TryGetValue(vid, out long prev) || start > prev))
				{
					lastPlayed[vid] = start;
				}
			}
		}
		_vers = source.Select((VersionRowDto r) => new Ver
		{
			Id = r.Id,
			Loader = r.Loader,
			Mc = r.Mc,
			Type = InferType(r.Id),
			Isolated = r.Isolated,
			Mods = r.Mods,
			IsolationLabel = r.IsolationLabel,
			Java = r.Java,
			LastPlayed = lastPlayed.TryGetValue(r.Id, out long lp) ? lp : 0L
		}).ToList();
	}

	/// <summary>Unix 秒 → 「今天 14:20 / 3 天前 / 2026-09-15」;0 = 从未游玩。</summary>
	private static string LastPlayedText(long unixSec)
	{
		if (unixSec <= 0)
		{
			return "";
		}
		DateTime t = DateTimeOffset.FromUnixTimeSeconds(unixSec).LocalDateTime;
		DateTime today = DateTime.Today;
		if (t.Date == today)
		{
			return I18n.L("上次游玩 今天 {0:H:mm}", t);
		}
		if (t.Date == today.AddDays(-1.0))
		{
			return I18n.L("上次游玩 昨天 {0:H:mm}", t);
		}
		if ((today - t.Date).TotalDays < 30.0)
		{
			return I18n.L("上次游玩 {0} 天前", (int)(today - t.Date).TotalDays);
		}
		return I18n.L("上次游玩 {0:yyyy-MM-dd}", t);
	}

	/// <summary>选中左栏某个文件夹(同一个就什么都不做):清标记 → 重刷路径与行底色 → 设默认实例 → 重拉版本。</summary>
	internal void SelectFolder(string name)
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

	/// <summary>这个文件夹的完整路径:实例给的 <c>Path</c> 优先,否则用游戏根目录拼一个(见类注释里那条 Windows 写法)。</summary>
	internal string FolderPathFor(string name)
	{
		string known = _instances.FirstOrDefault((InstanceInfo i) => i.Name == name)?.Path ?? "";
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
		_folderPath.Text = ((text.Length > 0) ? text : I18n.L("当前文件夹"));
		ToolTip.SetTip(_folderPath, (text.Length > 0) ? text : null);
		// 【Avalonia 差异,降级但如实】Windows 还有一句 ToolTipService.SetShowDuration(_folderPath, 60000)
		// (让提示停留 60 秒)。Avalonia 11.3.23 的 ToolTip 只有 ShowDelay,**没有** ShowDuration
		// (本机在 ref 程序集里逐名核过:ShowDelay 有、ShowDuration 没有),所以这一句无法照抄:
		// 提示照常显示,只是按平台默认时长消失。这不影响任何数据/交互,故不另设桩。
	}

	private FolderRow BuildFolderRow(InstanceInfo info)
	{
		string instName = info.Name;
		TextBlock name = Ui.Txt(instName, 13.5, bold: true).Trim();
		TextBlock count = Ui.Small(I18n.L("{0} 个版本", info.Versions)).Trim();
		SPanel mid = Ui.V(1.0, name, count);
		mid.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
		Border icon = new Border
		{
			Width = 32.0,
			Height = 32.0,
			CornerRadius = new CornerRadius(8.0)
		};
		icon.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		icon.Child = Ui.Glyph("\ue8b7", 14.0, "B.AccentText").Center();
		SPanel tail = Ui.H(6.0);
		tail.VerticalAlignment = VerticalAlignment.Center;
		if (instName == _defaultInstance)
		{
			tail.Children.Add(Ui.Tag(I18n.L("默认"), "B.OnAccentStrong", "B.AccentStrong"));
		}
		tail.Children.Add(Ui.IconBtn("\ue712", I18n.L("实例操作"), delegate(object? s, RoutedEventArgs _)
		{
			FolderMenu((Control)s!, instName);
		}, 13.0));
		Grid g = Ui.G(null, "Auto,*,Auto");
		g.Add(icon.VCenter(), 0);
		g.Add(mid.VCenter(), 0, 1);
		g.Add(tail, 0, 2);
		Border root = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(10.0, 9.0, 10.0, 9.0),
			BorderThickness = new Thickness(1.0),
			Child = g,
			Cursor = new Cursor(StandardCursorType.Hand),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		ToolTip.SetTip(root, FolderPathFor(instName));
		FolderRow row = new FolderRow
		{
			Info = info,
			Root = root
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
			// 点在行尾的按钮上不算选中这一行(Windows 用 OriginalSource 往树上找 Button)
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

	private void RenderGroups()
	{
		_groupsHost.Children.Clear();
		_groups.Clear();
		string q = _search.Text?.Trim() ?? "";
		Dictionary<string, List<Ver>> map = new Dictionary<string, List<Ver>>();
		foreach (Ver ver in _vers)
		{
			// 搜索按版本 id 大小写不敏感匹配(net48 的 BclExtensions.Contains → net8 的 string.Contains)
			if (q.Length == 0 || ver.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
			{
				string key = GroupKeyOf(ver);
				if (!map.TryGetValue(key, out var list))
				{
					list = (map[key] = new List<Ver>());
				}
				list.Add(ver);
			}
		}
		int total = 0;
		List<Control> cards = new List<Control>();
		(string Key, string Title, bool Open)[] defs = Defs;
		for (int i = 0; i < defs.Length; i++)
		{
			var (key, title, def) = defs[i];
			if (!map.TryGetValue(key, out var list) || list.Count == 0)
			{
				continue;
			}
			// 搜索中一律展开;否则按偏好里记的折叠状态(没有记录就用默认值)
			bool searching = q.Length > 0;
			Group group = BuildGroup(key, title, list.Count, searching || PrefsOpen(key, def));
			int ri = 0;
			foreach (Ver item in list.OrderBy((Ver v) => v.Id, StringComparer.OrdinalIgnoreCase))
			{
				Control row = BuildVerRow(item);
				// 每组第一行不画上分隔线,其余行画一条 B.RowLine(0,1,0,0)
				if (ri++ > 0 && row is Border rb)
				{
					rb.BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0);
					rb.Dyn(Border.BorderBrushProperty, "B.RowLine");
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
		TextBlock head = Ui.Txt(title + " (" + count + ")", 13.5, bold: true, "B.AccentText").Trim();
		// 组头图标:加载器的官方图标;「原版」等没有图标的键交给 LoaderIcons 自己回落
		string loaderKey = (LoaderKeys.Contains(key) ? key : "");
		Control icon = LoaderIcons.Tile(loaderKey, title, 22.0);
		TextBlock chev = Ui.Glyph("\ue70d", 12.0, "B.InkMuted");
		TextBlock folded = Ui.Small(I18n.L("已折叠"));
		folded.Dyn(TextBlock.ForegroundProperty, "B.InkFaint");
		// Windows 用 Ui.Spring() 把「已折叠」与箭头推到行尾(宽度占满的空白)
		SPanel bar = Ui.H(8.0, icon, head, Ui.Spring(), folded, chev);
		bar.VerticalAlignment = VerticalAlignment.Center;
		Border hd = new Border
		{
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = bar
		};
		StackPanel body = new StackPanel
		{
			Margin = new Thickness(8.0, 2.0, 8.0, 8.0)
		};
		Border sep = Ui.Sep();
		hd.PointerEntered += delegate
		{
			hd.Dyn(Border.BackgroundProperty, "B.Paper2");
		};
		hd.PointerExited += delegate
		{
			hd.Background = null;
		};
		hd.PointerReleased += delegate
		{
			ToggleGroup(group);
		};
		Border card = Ui.Card(Ui.V(0.0, hd, sep, body), 0.0);
		card.Margin = new Thickness(0.0, 0.0, 0.0, 10.0);
		group.Card = card;
		group.Body = body;
		group.Chevron = chev;
		group.Folded = folded;
		_groups[key] = group;
		return group;
	}

	private static void ApplyOpen(Group g, bool open)
	{
		g.Open = open;
		g.Body.IsVisible = open;
		g.Folded.IsVisible = !open;
		g.Chevron.Text = (open ? "\ue70e" : "\ue70d");
	}

	/// <summary>折叠/展开一个分组并把它记进偏好(键名 <c>lib:&lt;key&gt;</c>,与 Windows 同)。</summary>
	private void ToggleGroup(Group g)
	{
		ApplyOpen(g, !g.Open);
		MainWindow? win = Win;
		if (win?.Prefs != null)
		{
			win.Prefs.Expanded["lib:" + g.Key] = g.Open;
			win.SaveUiPrefs();
		}
	}

	/// <summary>
	/// C4 测试缝:按分组键折叠/展开(等价于点组头那一行 —— 组头是 <c>Border</c> 而不是 <c>Button</c>,
	/// 所以按钮那套 <c>RaiseEvent</c> 驱动不了它,按 D-127 的口径把"处理器体"暴露出来)。
	/// 返回 false = 这个键现在没有分组(比如版本全被搜索过滤掉了)。
	/// </summary>
	internal bool ToggleGroupForTest(string key)
	{
		if (!_groups.TryGetValue(key, out Group? g) || g is null)
		{
			return false;
		}
		ToggleGroup(g);
		return true;
	}

	/// <summary>C4 测试缝:当前渲染出来的分组(键 → 是否展开);只读快照,与 D-115 的同类成员一个口径。</summary>
	internal IReadOnlyDictionary<string, bool> GroupsNow()
	{
		return _groups.ToDictionary(kv => kv.Key, kv => kv.Value.Open, StringComparer.Ordinal);
	}

	private bool PrefsOpen(string key, bool def)
	{
		MainWindow? win = Win;
		if (win?.Prefs != null && win.Prefs.Expanded.TryGetValue("lib:" + key, out var value))
		{
			return value;
		}
		return def;
	}

	private Control BuildVerRow(Ver v)
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
		SPanel nameRow = Ui.H(6.0, Ui.Txt(id, 15.0, bold: true).Trim());
		if (tags.Children.Count > 0)
		{
			nameRow.Children.Add(tags);
		}
		string sub = SubLine(v);
		TextBlock subTb = Ui.Txt(sub, 12.5, bold: false, "B.InkMuted").Trim();
		SPanel mid = Ui.V(2.0, nameRow, subTb);
		Button star = new Button
		{
			Content = Ui.Glyph(fav ? "\ue735" : "\ue734", 13.0, fav ? "B.Accent" : null)
		};
		StyleKeys.Apply(star, "Btn.Icon");
		ToolTip.SetTip(star, I18n.L(fav ? "取消收藏" : "收藏"));
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
		g.Add(LoaderIcons.Tile(v.Loader.ToLowerInvariant(), id, 36.0).VCenter(), 0);
		g.Add(mid.VCenter().M(10.0, 0.0, 10.0, 0.0), 0, 1);
		g.Add(tail, 0, 2);
		Border row = new Border
		{
			MinHeight = 72.0,
			Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
			Background = Brushes.Transparent,
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = g
		};
		row.PointerEntered += delegate
		{
			row.Dyn(Border.BackgroundProperty, "B.RowHover");
		};
		row.PointerExited += delegate
		{
			row.Background = Brushes.Transparent;
		};
		row.PointerPressed += delegate(object? _, PointerPressedEventArgs e)
		{
			// 【Windows 源】:双击版本行(且不点在行尾按钮上)= 设为默认实例后跳启动页
			if (!HitButton(e.Source as Visual) && e.ClickCount == 2)
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
			s = s + "," + v.Loader;
		}
		if (!string.IsNullOrWhiteSpace(v.Java))
		{
			s = s + " · Java " + v.Java;
		}
		string lp = LastPlayedText(v.LastPlayed);
		if (lp.Length > 0)
		{
			s = s + " · " + lp;
		}
		return s;
	}

	/// <summary>按版本 id 猜类型(收藏 / 快照标签与副行都用它;后端 <c>VersionRowDto</c> 没有 type 字段)。</summary>
	internal static string InferType(string id)
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
		int i = Array.IndexOf(LoaderNames, v.Loader);
		if (i >= 0)
		{
			return LoaderKeys[i];
		}
		return "vanilla";
	}

	private List<string> Favs()
	{
		Dictionary<string, List<string>>? prefs = Win?.Prefs.VmFavorites;
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

	/// <summary>这个版本在当前文件夹里被收藏了吗(与 Windows 同:按文件夹分别记)。</summary>
	internal bool IsFav(string id)
	{
		return Favs().Contains(id);
	}

	/// <summary>
	/// 收藏 / 取消收藏:表里没有就加,有就删;然后落盘并重画分组(Windows 同)。
	/// 参数是页面私有的视图模型 <c>Ver</c>,所以这里保持 private —— 用例走**行尾那颗星钮**
	/// (<c>star.Click</c> 是真 <c>Button</c>,<c>RaiseEvent</c> 驱动得到)而不是调这个方法。
	/// </summary>
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

	/// <summary>双击版本行:设默认实例 → 让启动页优先用这个实例/版本 → 跳到启动页(Windows 同)。</summary>
	internal async Task LaunchVerAsync(string inst, string ver)
	{
		await InstancePage.SetDefaultAsync(inst);
		_defaultInstance = inst;
		if (Win?.GetPage("launch") is LaunchPage launchPage)
		{
			launchPage.PreferInstance(inst, ver);
		}
		Win?.Navigate("launch");
	}

	// 页内切换到实例详情:get_version_rows 只认当前默认实例,进入前把选中文件夹设为默认(与双击启动同模式),返回后不回滚
	internal async Task OpenDetailAsync()
	{
		if (string.IsNullOrEmpty(_folder))
		{
			return;
		}
		if (_folder != _defaultInstance)
		{
			await InstancePage.SetDefaultAsync(_folder);
			_defaultInstance = _folder;
		}
		InstanceInfo info = _instances.FirstOrDefault((InstanceInfo i) => i.Name == _folder) ?? new InstanceInfo
		{
			Name = _folder,
			Path = FolderPathFor(_folder)
		};
		// 第一次进来时把原内容记下来,「返回」再换回去(Windows 用同一个 _shell 字段)
		_shell ??= base.Content;
		base.Content = new InstanceDetailPage(info, delegate
		{
			if (_shell is Control shell)
			{
				base.Content = shell;
			}
			Run(LoadAsync);
		});
	}

	/// <summary>卸载一个版本(要确认;确认后连该版本目录下的独立模组与存档一起删)。</summary>
	internal async Task UninstallAsync(string inst, string ver)
	{
		if (await Dlg.Confirm(I18n.L("卸载版本"), I18n.L("确定卸载「{0}」?该版本目录下的独立模组与存档会一并删除。", ver), I18n.L("卸载"), I18n.L("取消"), danger: true))
		{
			await Api.CallAsync("uninstall_version", new
			{
				spec = inst + " / " + ver
			});
			await LoadAsync();
			Toast(I18n.L("已卸载"), ver, ToastKind.Success);
		}
	}

	/// <summary>左栏实例行的「实例操作」右键菜单(与 Windows 逐项同序;第 6 项后有一条分隔线)。</summary>
	private void FolderMenu(Control anchor, string instance)
	{
		BuildFolderMenu(anchor, instance).Open(anchor);
	}

	/// <summary>
	/// C4 测试缝:建出那份菜单并返回(**不弹出**),真实路径 <see cref="FolderMenu"/> 只是
	/// <c>BuildFolderMenu(...).Open(anchor)</c>。拆开的理由与 D-124 记的一样:Avalonia 的
	/// <c>ContextMenu</c> 一旦 <c>Open</c> 就落进独立的 popup 宿主,测试既拿不到它也点不到菜单项;
	/// 拆出来之后,9 个菜单项的顺序与各自的动作都能被**告知式地**驱动(同 D-115 / D-127 的口径)。
	/// </summary>
	internal ContextMenu BuildFolderMenu(Control anchor, string instance)
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
		Item(I18n.L("选择 Java..."), () => PickJavaAsync(instance));
		Item(I18n.L("管理模组"), async delegate
		{
			await ModsDialog.ShowAsync(instance);
		});
		Item(I18n.L("存档管理..."), async delegate
		{
			await SavesDialog.ShowAsync(instance, "");
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名..."), async delegate
		{
			string? text = await Dlg.Prompt(I18n.L("重命名实例"), I18n.L("新名称"), instance);
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
			if (await Dlg.Confirm(I18n.L("删除实例"), I18n.L("「{0}」下的版本、模组、存档都会被删除,且不可恢复。", instance), I18n.L("永久删除"), I18n.L("取消"), danger: true))
			{
				await Api.CallAsync("delete_instance", new
				{
					name = instance
				});
				await LoadAsync();
				Toast(I18n.L("已删除"), instance, ToastKind.Success);
			}
		});
		// Avalonia 的 MenuBase 只有 Open(anchor)(无参 Open() 会抛,见 D-124)
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
		return menu;
	}

	/// <summary>「选择 Java...」:拉可选清单 + 当前记录,选了才写回 <c>set_instance_java</c>。</summary>
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
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("留「自动选择」由启动器按版本匹配")), (string.IsNullOrEmpty(cur) ? null : Ui.Small(I18n.L("当前记录:") + cur)), combo);
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

	/// <summary>「添加已有文件夹」:选目录 → 问显示名 → 起 <c>import_existing_folder</c> 任务。</summary>
	private async Task AddFolderAsync()
	{
		string? path = Dlg.PickFolder(I18n.L("选择一个已有的游戏文件夹(含 versions 的 .minecraft)"));
		if (!string.IsNullOrWhiteSpace(path))
		{
			string fileName = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
			string? name = await Dlg.Prompt(I18n.L("导入为文件夹"), I18n.L("显示名称"), fileName);
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

	/// <summary>「导入整合包」:选文件后整个交给 <see cref="FileDrop.ImportModpackAsync"/>。</summary>
	private async Task ImportPackAsync()
	{
		string? file = Dlg.PickFile(I18n.L("整合包 (*.mrpack;*.zip)|*.mrpack;*.zip"), I18n.L("导入整合包"));
		// mac 的 Win 是 MainWindow?(Windows 是非空单例),所以先抄进局部变量再判空 —— 语义不变
		MainWindow? win = Win;
		if (file != null && win != null)
		{
			await FileDrop.ImportModpackAsync(win, file);
		}
	}

	/// <summary>「新建实例」:问名字 → <c>create_instance</c> → 重载。</summary>
	private async Task CreateAsync()
	{
		string? name = await Dlg.Prompt(I18n.L("新建实例"), I18n.L("实例名称(会创建独立的 .minecraft)"), "", I18n.L("例如:1.20.1 生存"));
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

	/// <summary>「导入官方启动器」:先探测有没有,再确认导入到哪个实例名下(默认 <c>default</c>)。</summary>
	private async Task MigrateAsync()
	{
		if (!await Api.TryCallAsync("detect_official_launcher", null, fallback: false))
		{
			Toast(I18n.L("没找到官方启动器"), I18n.L("未检测到 .minecraft 目录"), ToastKind.Warning);
			return;
		}
		string dir = await Api.TryCallAsync("official_launcher_dir", null, "");
		List<string> list = (await Api.TryCallAsync("scan_official_versions", null, new List<string>())) ?? new List<string>();
		TextBox target = Ui.Input("default", "default");
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("检测到:{0}", dir)), Ui.Muted(I18n.L("可导入 {0} 个版本:{1}{2}", list.Count, string.Join(I18n.L("、"), list.Take(8)), (list.Count > 8) ? " ..." : "")), Ui.Txt(I18n.L("导入到实例"), 12.0, bold: false, "B.InkMuted"), target);
		if (await Dlg.Ask(I18n.L("导入官方启动器"), body, I18n.L("开始导入")))
		{
			await Api.StartTaskAsync("migrate_official_launcher", new
			{
				instance = (target.Text?.Trim() ?? "default")
			});
			Toast(I18n.L("已开始导入"), I18n.L("进度看下载任务"));
		}
	}

	/// <summary>
	/// 这个可视链上有没有按钮(点行尾按钮时不要把整行当成"选中它"/"双击版本")。
	/// Windows 走 <c>VisualTreeHelper.GetParent(DependencyObject)</c>;Avalonia 11 **没有**
	/// <c>VisualTreeHelper</c>(本机在 ref 程序集里核过),等价物是 <c>Avalonia.VisualTree</c>
	/// 的扩展方法 <c>Visual.GetVisualParent()</c>。
	/// </summary>
	private static bool HitButton(Visual? d)
	{
		while (d != null)
		{
			if (d is Button)
			{
				return true;
			}
			d = d.GetVisualParent();
		}
		return false;
	}
}
