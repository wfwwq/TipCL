using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Shape = Avalonia.Controls.Shapes.Shape;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL;

/// <summary>
/// 应用外壳:窗口装饰、标题栏、侧栏、页面宿主、系统菜单与快捷键。
///
/// 尺寸与 macOS 窗口语义按 GOAL §3 的窗口行:900×550(最小 720×480)、标题栏 46px、
/// 用 <c>ExtendClientAreaToDecorationsHint</c> 借系统的红绿灯(内容铺进标题栏,所以标题栏内容
/// 左侧让出 <see cref="TitleBarLeftInset"/> 的 78px);**不画 Windows 式最小化/最大化/关闭按钮**
/// (Windows 的 <c>MinBtn</c>/<c>MaxBtn</c>/<c>CloseBtn</c> 与 <c>WindowChrome</c> 在本包删掉)。
///
/// 结构对照 Windows 的 <c>MainWindow.xaml</c>(逐块对应):
/// <list type="bullet">
/// <item><c>RootBorder</c>(玻璃边框)→ mac 同名保留;</item>
/// <item><c>ContentLayer</c>(标题栏 + 内容两行)→ 保留,**并当作 <c>Dlg.BlurTarget</c>**;</item>
/// <item><c>TopBar</c> → 保留,但右侧的 AI/主题/账号圆片与三个 caption 按钮去掉(见上);</item>
/// <item><c>PageHost</c>(Windows 的 <c>ContentControl</c>)→ 由 <see cref="Pages.PageHost"/> 接替
/// (C2b 抽出来是为了能单测);</item>
/// <item>侧栏:Windows 现状是顶栏页签(侧栏已从可视树撤掉),但 mac 侧任务书明确要「侧栏宽度取
/// <c>UiPrefs.SideWidth</c>」,所以这里按 <see cref="NavModel"/> 的行表重建侧栏 —— 导航数据
/// 与页面 id 的绑定关系与 Windows 完全一致,C3 的页面照旧靠 <see cref="NavModel.PageForNavKey"/> 挂上去;</item>
/// <item><c>ToastHost</c> / <c>DialogHost</c> → 右上角 Toast 宿主(C2c1 起画真的卡片:
/// <see cref="ToastLayer"/> + <see cref="ToastCard"/>,<c>AppServices.ToastSink</c> 在这里接上)、
/// <c>DialogHost</c> 接给 <c>Dlg.Host</c>;
/// DropVeil、FlyHost、Wallpaper、下载任务底条(Dock)之属 C2c 的其余部分,本包不画。</item>
/// </list>
/// </summary>
internal sealed class MainWindow : Window
{
	// ==== 窗口与标题栏尺寸(任务书 / GOAL §3 的窗口行)====
	internal const double WindowWidth = 900.0;

	internal const double WindowHeight = 550.0;

	internal const double WindowMinWidth = 720.0;

	internal const double WindowMinHeight = 480.0;

	/// <summary>标题栏高度(Windows 的 <c>WindowChrome.CaptionHeight=46</c>)。</summary>
	internal const double TitleBarHeight = 46.0;

	/// <summary>标题栏内容左侧让位:macOS 把系统红绿灯画在左上角,内容要躲开(任务书:约 78px)。</summary>
	internal const double TitleBarLeftInset = 78.0;

	/// <summary>侧栏宽度下限(Windows <c>MainWindow.SideMinWidth</c>)。</summary>
	public const int SideMinWidth = 140;

	/// <summary>侧栏宽度上限(Windows <c>MainWindow.SideMaxWidth</c>)。</summary>
	public const int SideMaxWidth = 320;

	/// <summary>侧栏宽度默认值(Windows <c>MainWindow.SideDefaultWidth</c>)。</summary>
	internal const double SideDefaultWidth = 196.0;

	/// <summary>
	/// 页面表:页面 id / 标题 / 图标码位与 Windows 版 <c>MainWindow.NavDefs</c> **逐字一致**
	/// (顺序也一致 —— ⌘1...⌘9 用的是*可见顺序*,不是这张表的顺序)。
	/// 工厂在 mac 侧留空:页面由 C3 各包通过 <see cref="PageRegistry.Register"/> 注册(见 <see cref="NavDef"/> 注释)。
	/// </summary>
	public static readonly List<NavDef> NavDefs = new List<NavDef>
	{
		new NavDef("launch", I18n.L("启动"), "\ue7c1", null),
		new NavDef("library", I18n.L("实例库"), "\ue80a", null),
		new NavDef("instance", I18n.L("版本管理"), "\ue80a", null),
		new NavDef("account", I18n.L("账号"), "\ue77b", null),
		new NavDef("ai", I18n.L("AI 助手"), "\ue99a", null),
		new NavDef("multiplayer", I18n.L("联机"), "\ue701", null),
		new NavDef("settings", I18n.L("设置"), "\ue713", null),
		new NavDef("tasks", I18n.L("下载任务"), "\ue9d5", null),
		new NavDef("download", I18n.L("下载"), "\ue896", null),
		new NavDef("plugins", I18n.L("插件"), "\uea86", null)
	};

	private UiPrefs _prefs;

	private NavConfig _navCfg = new NavConfig();

	private readonly PageHost _host;

	private readonly Border _rootBorder;

	private readonly Border _topBar;

	private readonly Border _sidebar;

	/// <summary>背景宿主(Windows <c>MainWindow.xaml</c> 的 <c>WallHost</c>;C2c2)。</summary>
	private readonly Grid _wallHost;

	/// <summary>页面底纹(Windows 的 <c>PageWash</c>;有壁纸时它透明,壁纸才透得出来)。</summary>
	private readonly Rectangle _pageWash;

	private readonly Grid _bodyGrid;

	private readonly Grid _contentLayer;

	private readonly Grid _dialogHost;

	private readonly StackPanel _toastHost;

	/// <summary>Toast 宿主的管理器(最多 4 张、进/退场动画、自动关闭与计时器收摊;Cards 属 C2c1)。</summary>
	private readonly ToastLayer _toastLayer;

	private readonly Grid _navGrid;

	private readonly TextBlock _brand;

	/// <summary>拖放浮层(Windows <c>MainWindow.xaml</c> 的 <c>DropVeil</c> + <c>ShowDropVeil</c>;C2c2)。</summary>
	private readonly DropVeil _dropVeil;

	private bool _bridgeReady;

	/// <summary>后端启动器(C2d;null = 还没有人调过 <see cref="ConnectAsync"/>)。</summary>
	private BackendBootstrap? _bootstrap;

	/// <summary>
	/// 构造外壳。<paramref name="prefs"/> 为 null 时按 Windows 的做法从数据目录读
	/// <c>wpf-ui.json</c>(只读);测试与 <c>--smoke</c> 直接注入一份,免得离屏像素随用户偏好变。
	/// </summary>
	internal MainWindow(UiPrefs? prefs = null)
	{
		Title = I18n.L("TipCL 启动器");
		Width = WindowWidth;
		Height = WindowHeight;
		MinWidth = WindowMinWidth;
		MinHeight = WindowMinHeight;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;

		// macOS 的系统红绿灯:把标题栏让给系统画,内容铺满整窗。PreferSystemChrome 显式写出来
		// (Avalonia 默认值也是它,但这里要让"我们用系统红绿灯"这件事在代码里看得见)。
		ExtendClientAreaToDecorationsHint = true;
		ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
		ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;

		_prefs = prefs ?? new UiPrefs();

		// ---- 可视树(照 Windows 的 MainWindow.xaml 搭;Name 也照 Windows 的 x:Name 起名)----
		_brand = Ui.Txt("TipCL", 14.0, bold: true).VCenter();
		_topBar = BuildTopBar();
		_navGrid = new Grid { Name = "NavGrid" };
		_sidebar = BuildSidebar();
		_host = new PageHost(ResolvePage) { Name = "PageHostCtl" };
		_wallHost = new Grid { Name = "WallHost", IsHitTestVisible = false, ClipToBounds = true };
		_pageWash = new Rectangle();
		_pageWash.Dyn(Shape.FillProperty, "B.PageWash");
		_bodyGrid = BuildBody();
		_contentLayer = BuildContentLayer();
		_dialogHost = new Grid { Name = "DialogHost", IsVisible = false };
		_toastHost = new StackPanel
		{
			Name = "ToastHost",
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 12.0, 16.0, 0.0)
		};
		_toastLayer = new ToastLayer(_toastHost);
		_dropVeil = new DropVeil();
		_rootBorder = new Border
		{
			Name = "RootBorder",
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Child = BuildRoot()
		};
		Content = _rootBorder;

		// ---- 主题:跟随窗口变体;变体一变就重刷外壳各处取色 ----
		ActualThemeVariantChanged += delegate
		{
			ApplyThemeVisuals();
		};
		ApplyThemeVisuals();

		// ---- 快捷键(Windows 的 PreviewKeyDown → Avalonia 的隧道阶段 KeyDown)----
		AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);

		// ---- 拖放(Windows 的 PreviewDragEnter/Over/Leave/Drop + 窗口级 Drop;见 DropVeil / FileDrop)----
		// 窗口自己允许拖放(Windows 的 <Window AllowDrop="True">),真正的判定在 FileDrop 里。
		DragDrop.SetAllowDrop(this, true);
		AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
		AddHandler(DragDrop.DragOverEvent, OnDragOver);
		AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
		AddHandler(DragDrop.DropEvent, OnDrop);
		FileDrop.Attach(this, _dropVeil);

		// ---- 背景管线(Windows 的 Wallpaper.Attach(WallHost, RootBorder, TopBar, null, PageWash))----
		// mac 侧第四个参数给的是侧栏(Windows 没有侧栏);ChromeInvalidated 不接 —— Windows 接的是
		// "最大化 / 还原字形"的同步,mac 用系统红绿灯,没有那个字形(见 C2b 的窗口差异表)。
		Wallpaper.Attach(_wallHost, _rootBorder, _topBar, _sidebar, _pageWash);
		Activated += delegate
		{
			Wallpaper.SetWindowActive(true);
		};
		Deactivated += delegate
		{
			Wallpaper.SetWindowActive(false);
		};

		// ---- 命令与系统菜单(GOAL:接 NativeMenu,快捷键 Ctrl→⌘)----
		NavigateCommand = new ShellCommand(delegate(object? parameter)
		{
			if (parameter is string id)
			{
				Navigate(id);
			}
		});
		RefreshCommand = new ShellCommand(RefreshCurrent);
		ToggleThemeCommand = new ShellCommand(ToggleTheme);
		AiCommand = new ShellCommand(delegate
		{
			Navigate("ai");
		});
		SyncMenu();

		// ---- 服务接线(C2a 验收清单)----
		AppServices.Window = this;
		Dlg.Host = _dialogHost;
		Dlg.BlurTarget = _contentLayer;
		// C2c1:Toast 的宿主接上真实的卡片(Windows 是 window.Toast(...),mac 走 ToastSink 委托,
		// 见 D-88/D-95)。接上之后 AppServices.Toast / PageBase.Toast / Dlg.Form 的「填写有误」都有声了。
		AppServices.ToastSink = Toast;

		// 关窗即收摊:Toast 的自动关闭计时器一个都不留(C2c1 的泄漏纪律,见 D-105)。
		Closed += delegate
		{
			_toastLayer.Dispose();
			// 拖放浮层与静态引用一并收摊:关窗后不留可见状态、也不留活窗口(FileDrop 是静态类)。
			_dropVeil.Reset();
			FileDrop.Detach(this);
			// 背景管线收摊(Windows 的 Closed → Wallpaper.Shutdown;mac 多一步摘图层引用,
			// 免得下一个外壳往新宿主里挂时那两个静态图层还挂在旧宿主上)。
			Wallpaper.Shutdown();
			Wallpaper.Detach();
		};

		// ---- 偏好:注入的用注入的,否则按 Windows 从数据目录读 ----
		if (prefs is null)
		{
			LoadUiPrefs();
		}
		else
		{
			ApplyPrefs(prefs);
		}
		ApplySidebarWidth(_prefs.SideWidth);

		// ---- 首屏:与 Windows 一样先切"启动"页(instant,不带动画)----
		SyncSidebar();
		Navigate("launch", instant: true);
	}

	/// <summary>--smoke 解析到的页面 id。A1/A2 只解析;本包起若能建出页面就切过去,建不出来(页面还没移植)就留在首屏。</summary>
	public string? PageId { get; init; }

	/// <summary>用户偏好(Windows 的 <c>MainWindow.Prefs</c>)。</summary>
	public UiPrefs Prefs => _prefs;

	/// <summary>导航配置(Windows 的 <c>MainWindow.NavConfig</c>;属性名与类型同名是 Windows 的写法)。</summary>
	public NavConfig NavConfig => _navCfg;

	/// <summary>当前页 id(Windows 同名成员;还没有页面时是空串)。</summary>
	public string CurrentPageId => _host.CurrentId;

	/// <summary>页面宿主的当前页(测试与后续包用)。</summary>
	internal PageBase? CurrentPage => _host.Current;

	/// <summary>页面宿主控件(C2b 抽出来的那块;Windows 是 <c>ContentControl PageHost</c>)。</summary>
	internal PageHost Host => _host;

	/// <summary>侧栏宽度(px;取自 <see cref="UiPrefs.SideWidth"/>,夹在 <see cref="SideMinWidth"/>~<see cref="SideMaxWidth"/>)。</summary>
	internal double SidebarWidth { get; private set; } = SideDefaultWidth;

	/// <summary>
	/// 后端有没有连上(Windows 的 <c>_bridgeReady</c>)。C2b **没有接后端启动**(见 D-92),
	/// 所以默认 false:页面能切、能显示,但 <c>LoadAsync</c>/<c>OnShown</c> 不跑
	/// —— 与 Windows「还没连上后端时不加载页面」的状态一致。后端接线包把它置 true 即可。
	/// </summary>
	/// <remarks>C2d 起:后端真的起来时由 <see cref="ConnectAsync"/> 把它置 true,失败路径保持 false
	/// (见 DECISIONS.md D-102)。</remarks>
	internal bool BridgeReady
	{
		get
		{
			return _bridgeReady;
		}
		set
		{
			_bridgeReady = value;
		}
	}

	/// <summary>最近一次后端启动失败的原因(空串 = 没失败过 / 已经就绪)。</summary>
	internal string BridgeError { get; private set; } = "";

	/// <summary>
	/// C2d:连后端(Windows 的 <c>MainWindow.ConnectAsync</c> 前半截)。成功才把
	/// <see cref="BridgeReady"/> 打开;失败保持 false,原因留在 <see cref="BridgeError"/>
	/// 与调试输出里(绝不静默变成 ready)。本方法自己吞掉所有异常,可以 fire-and-forget 调用。
	/// 续体走线程池(ConfigureAwait(false)),所以同步等待版 <see cref="ConnectBlocking"/>
	/// 在 AvaloniaSession 线程上也安全。
	/// </summary>
	internal async Task<bool> ConnectAsync(BackendBootstrap? bootstrap = null, CancellationToken ct = default(CancellationToken))
	{
		BackendBootstrap boot = bootstrap ?? new BackendBootstrap();
		_bootstrap = boot;
		BridgeHost? host = await boot.StartAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (host == null)
		{
			BridgeReady = false;
			BridgeError = (boot.Error == null) ? "" : boot.Error.Message;
			Debug.WriteLine("[bridge] 未连接: " + boot.Error);
			return false;
		}
		AppServices.Host = host;
		AppServices.Client = host.Client;
		BridgeError = "";
		BridgeReady = true;

		// Windows 在连上之后接着跑 ReloadCurrentAsync(把当前页补载 + OnShown),本包照做。
		// 这一句碰页面,所以必须回到 UI 线程;失败只记日志,绝不影响"已经就绪"这个事实。
		try
		{
			ReloadCurrentAfterConnect();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[bridge] 补载当前页失败: " + ex);
		}
		return true;
	}

	/// <summary>
	/// 后端就绪之后把当前页补上(Windows 的 <c>ReloadCurrentAsync</c>:<c>EnsureLoadedAsync</c> + <c>OnShown</c>)。
	/// <see cref="ConnectAsync"/> 的续体在线程池上(它用 ConfigureAwait(false),这样同步等待版在
	/// AvaloniaSession 线程上才不会自锁),所以这里往 Dispatcher 投一次,由 UI 线程去碰页面。
	/// </summary>
	private void ReloadCurrentAfterConnect()
	{
		if (Dispatcher.UIThread.CheckAccess())
		{
			ReloadCurrentNow();
			return;
		}
		Dispatcher.UIThread.Post(ReloadCurrentNow);
	}

	/// <summary>补载当前页(必须在 UI 线程上跑)。</summary>
	private void ReloadCurrentNow()
	{
		PageBase? current = _host.Current;
		if (current is null)
		{
			return;
		}
		PageBase.Run(async delegate
		{
			await current.EnsureLoadedAsync();
			current.OnShown();
		});

		// Windows 在后端连上之后也把背景管线跑一遍(MainWindow.xaml.cs:357),否则上次会话设的
		// 壁纸永远不出现。失败只记日志(它与"后端已就绪"无关)。
		try
		{
			PageBase.Run(() => Wallpaper.ReloadAsync(), I18n.L("壁纸没能加载"));
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[wallpaper] 加载失败: " + ex);
		}
	}

	/// <summary>同步等待版:用例体跑在 AvaloniaSession 线程上,不能 await(见 D-79)。</summary>
	internal bool ConnectBlocking(BackendBootstrap bootstrap, CancellationToken ct = default(CancellationToken))
	{
		return ConnectAsync(bootstrap, ct).GetAwaiter().GetResult();
	}

	/// <summary>
	/// C2d:生产启动路径(由 <c>App</c> 的桌面生命周期调一次)。不阻塞启动;失败只记日志,
	/// 窗口的 <see cref="BridgeReady"/> 保持 false。离屏 <c>--smoke</c> 不走这里
	/// (SetupWithoutStarting 不进桌面生命周期),所以冒烟像素与后端无关(见 D-96/D-102)。
	/// </summary>
	internal void StartBackend()
	{
		Closed += delegate
		{
			StopBackend();
		};
		_ = ConnectAsync();
	}

	/// <summary>C2d:收后端(关窗 / 测试收摊)。先把总闸关回去,再停止监听。</summary>
	internal void StopBackend()
	{
		BridgeReady = false;
		_bootstrap?.Stop();
		_bootstrap = null;
	}

	/// <summary>页面命令:参数是页面 id(菜单项与 ⌘N 都走它)。</summary>
	internal ICommand NavigateCommand { get; }

	/// <summary>刷新当前页(⌘R;Windows 是 F5)。</summary>
	internal ICommand RefreshCommand { get; }

	/// <summary>深色 / 浅色切换(Windows 标题栏的 <c>ThemeBtn</c>)。</summary>
	internal ICommand ToggleThemeCommand { get; }

	/// <summary>AI 助手(⌘K)。</summary>
	internal ICommand AiCommand { get; }

	/// <summary>系统菜单(GOAL:接 NativeMenu 系统菜单栏)。</summary>
	internal NativeMenu? Menu { get; private set; }

	/// <summary>标题栏控件(测试要验高度与左让位)。</summary>
	internal Border TopBar => _topBar;

	/// <summary>侧栏控件。</summary>
	internal Border Sidebar => _sidebar;

	/// <summary>对话层宿主(= <c>Dlg.Host</c>)。</summary>
	internal Grid DialogHostPanel => _dialogHost;

	/// <summary>Toast 宿主面板(卡片由 <see cref="ToastCard"/> 画;上限 4 张,见 <see cref="Toasts"/>)。</summary>
	internal StackPanel ToastHostPanel => _toastHost;

	/// <summary>页面正文那块(被 <c>Dlg.BlurTarget</c> 模糊的目标)。</summary>
	internal Grid ContentLayerPanel => _contentLayer;

	/// <summary>偏好文件路径:数据目录下的 <c>wpf-ui.json</c>(Windows 同名同文件,见 D-92)。</summary>
	internal static string PrefsPath
	{
		get
		{
			string root;
			try
			{
				root = BridgeHost.FindRoot();
			}
			catch
			{
				root = AppContext.BaseDirectory;
			}
			return Path.Combine(root, "wpf-ui.json");
		}
	}

	// =====================================================================
	// 可视树
	// =====================================================================

	private Control BuildRoot()
	{
		// 行 46 + *:上标题栏、下内容;DialogHost 跨两行盖在最上面(Windows 同)。
		Grid outer = new Grid();
		outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
		outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

		outer.Children.Add(_contentLayer);
		Grid.SetRow(_contentLayer, 0);
		Grid.SetRowSpan(_contentLayer, 2);

		// 拖放浮层:盖在正文之上、Toast / 对话层之下(Windows 的 XAML 顺序也是
		// ContentLayer → DropVeil → ToastHost → DialogHost)。
		outer.Children.Add(_dropVeil);
		Grid.SetRow(_dropVeil, 1);

		outer.Children.Add(_toastHost);
		Grid.SetRow(_toastHost, 1);

		outer.Children.Add(_dialogHost);
		Grid.SetRow(_dialogHost, 0);
		Grid.SetRowSpan(_dialogHost, 2);
		return outer;
	}

	private Grid BuildContentLayer()
	{
		Grid layer = new Grid { Name = "ContentLayer" };
		layer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
		layer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		// 壁纸层在标题栏与正文之下、铺满整个内容层(Windows 的 WallHost 也是 Row=0 RowSpan=2)。
		layer.Children.Add(_wallHost);
		Grid.SetRow(_wallHost, 0);
		Grid.SetRowSpan(_wallHost, 2);

		layer.Children.Add(_topBar);
		Grid.SetRow(_topBar, 0);
		layer.Children.Add(_bodyGrid);
		Grid.SetRow(_bodyGrid, 1);
		return layer;
	}

	private Grid BuildBody()
	{
		Grid area = new Grid();
		area.Children.Add(_pageWash);
		area.Children.Add(_host);
		Border divider = new Border { BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0) };

		Grid body = new Grid { Name = "BodyGrid" };
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		body.Children.Add(_sidebar);
		body.Children.Add(area);
		Grid.SetColumn(area, 1);
		divider.BorderBrush = Palette.Brush("B.Line");
		return body;
	}

	private Border BuildTopBar()
	{
		// 标题栏内容:左侧让出系统红绿灯的位置(78px),之后是 Windows 那套 logo + "TipCL" 文字。
		// 右侧不画任何 caption 按钮(mac 用系统红绿灯)。
		SPanel left = Ui.H(8.0);
		left.Margin = new Thickness(TitleBarLeftInset, 0.0, 20.0, 0.0);
		left.VerticalAlignment = VerticalAlignment.Center;

		TextBlock mark = Ui.Txt("T", 12.0, bold: true).Center().VCenter();
		mark.Dyn(TextBlock.ForegroundProperty, "B.OnLogo");
		Border logo = new Border
		{
			Width = 22.0,
			Height = 22.0,
			CornerRadius = new CornerRadius(6.0),
			Background = Palette.Brush("B.LogoFill"),
			Child = mark
		};
		left.Children.Add(logo);
		left.Children.Add(_brand);

		Grid row = new Grid { Name = "TopBarRow" };
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		row.Children.Add(left);

		Border bar = new Border
		{
			Name = "TopBar",
			Height = TitleBarHeight,
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			Child = row
		};
		// 标题栏空白处拖窗口(macOS 上等价 WPF 的 CaptionHeight 拖动区)。
		bar.PointerPressed += delegate(object? _, PointerPressedEventArgs e)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				BeginMoveDrag(e);
			}
		};
		return bar;
	}

	private Border BuildSidebar()
	{
		ScrollViewer scroll = new ScrollViewer
		{
			Content = _navGrid,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		return new Border
		{
			Name = "Sidebar",
			BorderThickness = new Thickness(0.0, 0.0, 1.0, 0.0),
			Padding = new Thickness(0.0, 8.0, 0.0, 8.0),
			Child = scroll
		};
	}

	// =====================================================================
	// 侧栏
	// =====================================================================

	/// <summary>按当前 <see cref="NavConfig"/> 重建侧栏行(分组头 / 撑条 / 条目)。</summary>
	internal void SyncSidebar()
	{
		_navGrid.Children.Clear();
		_navGrid.RowDefinitions.Clear();
		int row = 0;
		foreach (NavEntry entry in NavModel.NavItemsFromConfig(_navCfg))
		{
			Control? control = entry.Kind switch
			{
				NavEntryKind.Header => NavHeader(entry.Label),
				NavEntryKind.Stretch => null,
				_ => NavItem(entry)
			};
			_navGrid.RowDefinitions.Add(new RowDefinition
			{
				Height = (entry.Kind == NavEntryKind.Stretch) ? new GridLength(1.0, GridUnitType.Star) : GridLength.Auto
			});
			if (control is not null)
			{
				_navGrid.Children.Add(control);
				Grid.SetRow(control, row);
			}
			row++;
		}
		ApplyThemeVisuals();
		SyncNavSelection();
	}

	private static Control NavHeader(string label)
	{
		TextBlock text = Ui.Small(label);
		text.Margin = new Thickness(12.0, 10.0, 8.0, 4.0);
		return text;
	}

	private Control NavItem(NavEntry entry)
	{
		string? pageId = PageFor(entry.Key);
		ToggleButton row = new ToggleButton
		{
			Height = 34.0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Left,
			Padding = new Thickness(12.0, 0.0, 10.0, 0.0),
			Tag = entry.Key,
			IsTabStop = true
		};
		StyleKeys.Apply(row, "Tab.Top");
		SPanel content = Ui.H(10.0);
		content.VerticalAlignment = VerticalAlignment.Center;
		Border? glyph = GlyphFor(pageId);
		if (glyph is not null)
		{
			content.Children.Add(glyph);
		}
		content.Children.Add(Ui.Txt(entry.Label, 13.5).VCenter().Trim());
		row.Content = content;
		if (pageId is not null)
		{
			row.Cursor = new Cursor(StandardCursorType.Hand);
			row.Click += delegate
			{
				Navigate(pageId);
			};
		}
		return row;
	}

	private static Border? GlyphFor(string? pageId)
	{
		if (pageId is null)
		{
			return null;
		}
		NavDef? def = NavDefs.FirstOrDefault((NavDef d) => d.Id == pageId);
		if (def is null || string.IsNullOrEmpty(def.Glyph))
		{
			return null;
		}
		TextBlock glyph = Ui.Glyph(def.Glyph, 15.0);
		glyph.VerticalAlignment = VerticalAlignment.Center;
		return new Border
		{
			Width = 20.0,
			Child = glyph
		};
	}

	/// <summary>侧栏选中态与当前页联动(Windows 的 <c>SyncTabs</c>)。</summary>
	internal void SyncNavSelection()
	{
		string current = _host.CurrentId;
		foreach (Control child in _navGrid.Children)
		{
			if (child is ToggleButton tab && tab.Tag is string key)
			{
				tab.IsChecked = string.Equals(PageFor(key), current, StringComparison.Ordinal);
			}
		}
	}

	/// <summary>导航键 → 页面 id(分区键 <c>download</c>/<c>more</c> 没有直接页,返回 null;Windows 的 <c>PageOf</c> 同)。</summary>
	private static string? PageFor(string navKey)
	{
		return NavModel.PageForNavKey.TryGetValue(navKey, out string? pageId) ? pageId : null;
	}

	// =====================================================================
	// Toast(C2c1)
	// =====================================================================

	/// <summary>
	/// 弹一张 Toast 卡片。对应 Windows <c>MainWindow.Toast</c>(<c>MainWindow.xaml.cs:1179-1255</c>):
	/// 非 UI 线程来的调用先投回 UI 线程;上限 4 张;图标/颜色按 <paramref name="kind"/> 取;
	/// 进/退场走 <see cref="Motion.SlideIn"/>/<see cref="Motion.SlideOut"/>;
	/// 点一下关掉、悬停暂停自动关闭、错误 7 秒其余 4 秒(定时器归 <see cref="ToastLayer"/> 管)。
	/// 卡片本身在 <see cref="ToastCard"/>。
	/// </summary>
	internal void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(delegate
			{
				Toast(title, body, kind);
			});
			return;
		}

		_toastLayer.Show(title, body, kind);
	}

	/// <summary>收掉所有 Toast 卡片并停掉它们的计时器(窗口关闭时调,保证不留残留计时器)。</summary>
	internal void ClearToasts()
	{
		_toastLayer.CloseAll();
	}

	/// <summary>Toast 宿主管理器(测试要验堆叠 / 自动关闭 / 计时器不泄漏)。</summary>
	internal ToastLayer Toasts => _toastLayer;

	// =====================================================================
	// 导航
	// =====================================================================

	/// <summary>
	/// 切页(Windows 的 <c>MainWindow.Navigate</c>):同一个 id 直接返回,未知 id(没有页面)也返回,
	/// 否则老页 <c>OnHidden</c> → 换页(<c>instant</c> / 关闭动画时直接换,否则 <c>Motion.PageSwap</c>)
	/// → 新页 <c>EnsureLoadedAsync</c> + <c>OnShown</c>(只在后端就绪时)。
	/// </summary>
	public void Navigate(string id, bool instant = false)
	{
		bool forward = NavOrderIndex(id) >= NavOrderIndex(_host.CurrentId);
		if (!_host.Navigate(id, instant, forward, BridgeReady))
		{
			return;
		}
		SyncNavSelection();
		SyncMenu();
	}

	/// <summary>取(或首次建)一个页面;没注册工厂返回 null(Windows 的 <c>GetPage</c> 同)。</summary>
	public PageBase? GetPage(string id)
	{
		return _host.GetPage(id);
	}

	/// <summary>
	/// 页面解析:优先用 <see cref="NavDef.Create"/>(留给 C3 想直接写死工厂的包),否则问
	/// <see cref="PageRegistry"/>(C3 各页面包注册的地方);都取不到返回 null ⇒ 外壳静默不切页。
	/// </summary>
	internal static PageBase? ResolvePage(string id)
	{
		NavDef? def = NavDefs.FirstOrDefault((NavDef d) => d.Id == id);
		if (def?.Create is { } create)
		{
			return create();
		}
		return PageRegistry.Create(id);
	}

	/// <summary>⌘1...⌘9:切到可见顺序里的第 N 个页面(越界什么都不做,Windows 同)。</summary>
	internal bool NavigateIndex(int index)
	{
		List<string> order = VisibleFlatOrder();
		if (index < 0 || index >= order.Count)
		{
			return false;
		}
		Navigate(order[index]);
		return true;
	}

	/// <summary>可见的页面顺序(Windows 的 <c>VisibleFlatOrder</c>:分区展开成页面 id,去重)。</summary>
	internal List<string> VisibleFlatOrder()
	{
		return VisibleFlatEntries().Select(pair => pair.PageId).ToList();
	}

	/// <summary>可见顺序 + 它来自哪个导航键(菜单要拿导航键算标签;Windows 只要页面 id)。</summary>
	internal List<(string Key, string PageId)> VisibleFlatEntries()
	{
		List<(string Key, string PageId)> list = new List<(string, string)>();
		Dictionary<string, List<string>> members = NavModel.SectionMembersFromConfig(_navCfg);
		foreach (NavEntry item in NavModel.NavItemsFromConfig(_navCfg))
		{
			if (item.Kind != NavEntryKind.Item)
			{
				continue;
			}
			if (NavModel.IsSection(item.Key))
			{
				foreach (string key in members[item.Key])
				{
					string? pageId = PageFor(key);
					if (pageId is not null && !list.Any(pair => pair.PageId == pageId))
					{
						list.Add((key, pageId));
					}
				}
			}
			else
			{
				string? pageId = PageFor(item.Key);
				if (pageId is not null && !list.Any(pair => pair.PageId == pageId))
				{
					list.Add((item.Key, pageId));
				}
			}
		}
		return list;
	}

	/// <summary>页面在可见顺序里的位次(找不到算 99;Windows 的 <c>NavOrderIndex</c>)。</summary>
	internal int NavOrderIndex(string pageId)
	{
		int index = VisibleFlatOrder().IndexOf(pageId);
		return (index >= 0) ? index : 99;
	}

	/// <summary>刷新当前页(⌘R;Windows 的 F5:没加载过的页 <c>RefreshAsync</c> 自己会空转)。</summary>
	// =====================================================================
	// 拖放(Windows MainWindow.xaml.cs:238-256 的接线 + 1133-1177 的三个处理函数)
	// =====================================================================

	/// <summary>
	/// 拖进 / 拖过:带文件就出浮层(Windows 的 <c>PreviewDragEnter</c>/<c>PreviewDragOver</c> →
	/// <c>ShowDropVeil(e.Data.GetDataPresent(DataFormats.FileDrop))</c>)。
	/// </summary>
	private void OnDragEnter(object? sender, DragEventArgs e)
	{
		FileDrop.DragEnter(DroppedPaths(e));
	}

	private void OnDragOver(object? sender, DragEventArgs e)
	{
		// Windows 的 <c>OnDragOver</c> 顺带把效果定成 Copy / None(mac 侧只有"出不出浮层"这一层状态)。
		FileDrop.DragOver(DroppedPaths(e));
	}

	/// <summary>拖走了:收浮层(Windows 的 <c>PreviewDragLeave</c>)。</summary>
	private void OnDragLeave(object? sender, RoutedEventArgs e)
	{
		FileDrop.DragLeave();
	}

	/// <summary>落点:收浮层并分发(Windows 的 <c>PreviewDrop</c> + <c>Drop</c>)。</summary>
	private void OnDrop(object? sender, DragEventArgs e)
	{
		FileDrop.Drop(DroppedPaths(e));
	}

	/// <summary>这次拖拽携带的本地文件路径(Avalonia 的 <c>e.Data.GetFiles()</c> 对应
	/// Windows 的 <c>e.Data.GetData(DataFormats.FileDrop)</c>;取不到就是空表)。</summary>
	private static List<string> DroppedPaths(DragEventArgs e)
	{
		List<string> paths = new List<string>();
		try
		{
			// Avalonia 11.3.2 把 DragEventArgs.Data 标成 [Obsolete]("Use DataTransfer instead"),
			// 但 11.3.2 的程序集里**根本没有** DataTransfer / IDataTransfer(已按字节核验:
			// ref 与 lib 两处的 Avalonia.Base.dll / Avalonia.Controls.dll 都没有这个符号),
			// 这一版换不过去。NoWarn 没动,只把这一行局部包住;等 D-01 升级 Avalonia 后换新 API。
#pragma warning disable CS0618
			IEnumerable<IStorageItem>? files = e.Data.GetFiles();
#pragma warning restore CS0618
			foreach (IStorageItem item in files ?? Enumerable.Empty<IStorageItem>())
			{
				string path = item.TryGetLocalPath() ?? "";
				if (path.Length > 0)
				{
					paths.Add(path);
				}
			}
		}
		catch
		{
			// 拿不到文件列表就当"没带文件"(Windows 那边 GetDataPresent 为 false 时也是这个结果)。
		}

		return paths;
	}

	internal void RefreshCurrent()
	{
		PageBase? current = _host.Current;
		if (current is null)
		{
			return;
		}
		PageBase.Run(async delegate
		{
			await current.RefreshAsync();
		}, I18n.L("刷新失败"));
	}

	/// <summary>导航配置变了(Windows 的 <c>ApplyNavSettings</c>):存下来并重建侧栏与菜单。</summary>
	public void ApplyNavSettings(JsonElement settings)
	{
		_navCfg = NavConfig.FromJson(settings);
		SyncSidebar();
		SyncMenu();
	}

	// =====================================================================
	// 系统菜单与快捷键
	// =====================================================================

	/// <summary>
	/// 重建系统菜单(GOAL:接 <c>NativeMenu</c>)。两块子菜单:
	/// 「页面」= 可见顺序前 9 项,依次挂 ⌘1...⌘9(与 Windows 的 Ctrl+数字同一个顺序来源);
	/// 「操作」= Windows 外壳暴露的动作:AI 助手(⌘K)、设置(⌘,)、刷新(⌘R)、深色/浅色。
	/// </summary>
	internal void SyncMenu()
	{
		NativeMenu menu = new NativeMenu();

		NativeMenuItem pages = new NativeMenuItem { Header = I18n.L("页面") };
		NativeMenu pageItems = new NativeMenu();
		List<(string Key, string PageId)> entries = VisibleFlatEntries();
		for (int i = 0; i < entries.Count && i < ShellShortcuts.PageShortcutCount; i++)
		{
			(string key, string pageId) = entries[i];
			pageItems.Items.Add(new NativeMenuItem
			{
				Header = NavLabelFor(key),
				Command = NavigateCommand,
				CommandParameter = pageId,
				Gesture = ShellShortcuts.PageGesture(i),
				ToggleType = NativeMenuItemToggleType.Radio,
				IsChecked = string.Equals(pageId, _host.CurrentId, StringComparison.Ordinal)
			});
		}
		pages.Menu = pageItems;

		NativeMenuItem actions = new NativeMenuItem { Header = I18n.L("操作") };
		NativeMenu actionItems = new NativeMenu();
		actionItems.Items.Add(new NativeMenuItem
		{
			Header = I18n.L("AI 助手"),
			Command = AiCommand,
			Gesture = ShellShortcuts.AiGesture
		});
		actionItems.Items.Add(new NativeMenuItem
		{
			Header = I18n.L("设置"),
			Command = NavigateCommand,
			CommandParameter = "settings",
			Gesture = ShellShortcuts.SettingsGesture
		});
		actionItems.Items.Add(new NativeMenuItem
		{
			Header = I18n.L("刷新"),
			Command = RefreshCommand,
			Gesture = ShellShortcuts.RefreshGesture
		});
		actionItems.Items.Add(new NativeMenuItem
		{
			Header = I18n.L("切换深色 / 浅色"),
			Command = ToggleThemeCommand
		});
		actions.Menu = actionItems;

		menu.Items.Add(pages);
		menu.Items.Add(actions);
		Menu = menu;
		NativeDock.SetMenu(this, menu);
	}

	/// <summary>菜单项的显示名(与侧栏同一套:分组标签优先,再退到导航名)。</summary>
	private static string NavLabelFor(string navKey)
	{
		return NavModel.GroupedLabels.TryGetValue(navKey, out string? label) ? label : NavModel.NavLabel(navKey);
	}

	/// <summary>
	/// 快捷键(Windows 的 <c>OnShortcut</c> + GOAL 的 ⌘ 映射):
	/// ⌘1...9 切页、⌘K AI(对话框开着或焦点在文本框里不抢)、⌘, 设置、⌘R 刷新。
	/// 测试**不模拟按键**:直接 <c>NavigateCommand.Execute(id)</c> / <c>NavigateIndex(n)</c>,
	/// 键码映射由 <see cref="ShellShortcuts"/> 的纯函数覆盖。
	/// </summary>
	private void OnShortcut(object? sender, KeyEventArgs e)
	{
		if (!ShellShortcuts.IsAccelerator(e.KeyModifiers))
		{
			return;
		}
		int index = ShellShortcuts.PageIndex(e.Key);
		if (index >= 0)
		{
			NavigateIndex(index);
			e.Handled = true;
			return;
		}
		switch (e.Key)
		{
		case Key.OemComma:
			Navigate("settings");
			e.Handled = true;
			break;
		case Key.R:
			RefreshCurrent();
			e.Handled = true;
			break;
		case Key.K:
			if (ShellShortcuts.CanStealAiShortcut(FocusManager?.GetFocusedElement() as Control, Dlg.AnyOpen))
			{
				Navigate("ai");
				e.Handled = true;
			}
			break;
		}
	}

	// =====================================================================
	// 主题与偏好
	// =====================================================================

	/// <summary>
	/// 按当前主题变体刷新外壳各处取色(标题栏、侧栏、页面底纹、根边框)。
	/// <c>--smoke</c> 设完主题后显式调一次;平时由 <c>ActualThemeVariantChanged</c> 触发。
	/// 取色一律经 <see cref="Palette"/>:键缺失时给中性灰兜底,不会抛异常。
	/// </summary>
	internal void ApplyThemeVisuals()
	{
		ThemeVariant variant = (RequestedThemeVariant == ThemeVariant.Dark || ActualThemeVariant == ThemeVariant.Dark)
			? ThemeVariant.Dark
			: ThemeVariant.Light;

		// 窗口底色取色板 Canvas(A1 起就是这么干的,C1a 之后是真色板值)。
		Background = Palette.Brush("B.Canvas", variant);
		_rootBorder.BorderBrush = Palette.Brush("B.GlassBorder", variant);
		_topBar.Background = Palette.Brush("B.TitleBar", variant);
		_topBar.BorderBrush = Palette.Brush("B.TitleBarLine", variant);
		_sidebar.Background = Palette.Brush("B.Side", variant);
		_sidebar.BorderBrush = Palette.Brush("B.Line", variant);
		_brand.Foreground = Palette.Brush("B.Ink", variant);
		foreach (Control child in _navGrid.Children)
		{
			if (child is TextBlock header)
			{
				header.Foreground = Palette.Brush("B.InkFaint", variant);
			}
		}

		// Windows 是 App.ThemeVisualsChanged → Wallpaper.OnThemeChanged(mac 没有那个事件,
		// 外壳自己刷完取色再喊它一声):有壁纸时把标题栏玻璃 / 页面底纹透明那套重新落一遍。
		Wallpaper.OnThemeChanged();
	}

	/// <summary>侧栏宽度落点(把偏好里的 <c>SideWidth</c> 夹到上下限后写进布局)。</summary>
	internal void ApplySidebarWidth(double width)
	{
		double wanted = (width > 0.0) ? width : SideDefaultWidth;
		double clamped = Math.Clamp(wanted, SideMinWidth, SideMaxWidth);
		SidebarWidth = clamped;
		_bodyGrid.ColumnDefinitions[0].Width = new GridLength(clamped);
	}

	/// <summary>把一份偏好应用上去(动画开关 / 时长倍率 / 深色),不落盘(Windows 的 <c>LoadUiPrefs</c> 尾部同)。</summary>
	private void ApplyPrefs(UiPrefs prefs)
	{
		// Windows: Motion.Enabled = prefs.Anim; Motion.Scale = Clamp(prefs.AnimScale, 0.4, 2.2)。
		// mac 的 Motion.Enabled getter 还要求"有人在推进动画时钟"(离屏/测试进程里没有,见 D-69),
		// 所以离屏像素不会随偏好变。
		Motion.Enabled = prefs.Anim;
		Motion.Scale = Clamp.Of(prefs.AnimScale, 0.4, 2.2);
		ApplyThemeState(prefs.Dark, persistLocal: false);
	}

	/// <summary>按 Windows 的做法从数据目录读 <c>wpf-ui.json</c>(读不到 / 读坏 → 默认值,不抛)。</summary>
	private void LoadUiPrefs()
	{
		try
		{
			_prefs = File.Exists(PrefsPath)
				? (JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(PrefsPath)) ?? new UiPrefs())
				: new UiPrefs();
		}
		catch
		{
			_prefs = new UiPrefs();
		}
		ApplyPrefs(_prefs);
	}

	/// <summary>把偏好写回数据目录(Windows 的 <c>SaveUiPrefs</c>;本包**不在启动/切主题时自动调用**)。</summary>
	public void SaveUiPrefs()
	{
		try
		{
			File.WriteAllText(PrefsPath, JsonSerializer.Serialize(_prefs, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch
		{
		}
	}

	/// <summary>
	/// 切主题(Windows 的 <c>ApplyThemeState</c> 简化版):窗口与应用都设到目标变体,
	/// <paramref name="persistLocal"/> 为真时同时记进内存偏好(落盘由调用方决定)。
	/// </summary>
	internal void ApplyThemeState(bool dark, bool persistLocal = true)
	{
		ThemeVariant variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
		RequestedThemeVariant = variant;
		if (Application.Current is { } app)
		{
			app.RequestedThemeVariant = variant;
		}
		if (persistLocal)
		{
			_prefs.Dark = dark;
		}
		ApplyThemeVisuals();
	}

	/// <summary>深色 / 浅色切换(Windows 标题栏 ThemeBtn 的动作)。</summary>
	internal void ToggleTheme()
	{
		ApplyThemeState(ActualThemeVariant != ThemeVariant.Dark);
	}
}
