using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C3c:Windows 版 <c>Pages\LaunchPage.cs</c>(58383 字节 / WPF / net48)+ <c>Pages\LaunchPage.Popups.cs</c>
/// (9479 字节)到 Avalonia 11 / net8 的移植 —— **启动页,也就是本工程的第一个真实页面**。
///
/// 形状(WP-U3 之后的 Windows 现状,逐条照抄):
/// <list type="bullet">
/// <item>背景层(实例最新截图 → 没有就用内嵌 <c>hero.jpg</c> 全景图)+ 左→右深色渐变 + 底部渐变;</item>
/// <item>英雄区(左对齐):绿点 + 就绪行 / 大实例标题(点开实例切换浮层)/ 小标签行(加载器 · Java · 内存 · 模组数)/
///   主操作行(48 高「启动游戏」+ 停止 + 配置钮 + 联机钮)/ 进度行(空闲时收起);</item>
/// <item>内容区块交给 <see cref="BlockHost"/>(<c>news</c> 默认可见,<c>tasks</c> / <c>playtime</c> /
///   <c>notes</c> / <c>log</c> 默认收起),右键「自定义布局...」由宿主自带;</item>
/// <item>三个浮层(配置 / 实例切换 / 公告)在 <c>LaunchPage.Popups.cs</c> 里(Windows 也是 partial 拆开的)。</item>
/// </list>
///
/// Windows → Avalonia 的差异(逐条,理由写在行内注释里):
/// <list type="number">
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;</item>
/// <item><c>SetResourceReference(属性, 键)</c> → <c>UiExt.Dyn(控件, AvaloniaProperty, 键)</c>
///   (Avalonia 的动态资源靠 <c>DynamicResourceExtension</c>,<c>Dyn</c> 就是它的一行包装);</item>
/// <item><c>TextBox.Style = Ui.S("Input.Log")</c> 这类 → <c>StyleKeys.Apply(控件, 键)</c>;</item>
/// <item><c>Cursors.Hand</c> → <c>new Cursor(StandardCursorType.Hand)</c>(Avalonia 没有 <c>Cursors</c> 静态类);</item>
/// <item><c>MouseLeftButtonUp</c> → <c>PointerReleased</c>,<c>MouseEnter</c> / <c>MouseLeave</c> →
///   <c>PointerEntered</c> / <c>PointerExited</c>;按钮点用 <c>Click</c>;</item>
/// <item><c>FontWeights.SemiBold</c> → <c>FontWeight.SemiBold</c>,<c>FontWeights.ExtraBold</c> →
///   <c>FontWeight.ExtraBold</c>(枚举名不同);</item>
/// <item><c>System.Windows.Media.Imaging.BitmapSource</c> → <c>Avalonia.Media.Imaging.Bitmap</c>,
///   解码统一走 <see cref="ImageDecode"/>(mac 侧的 Skia 编解码器;WPF 是 <c>BitmapImage</c> +
///   <c>CacheOption.OnLoad</c> + <c>DecodePixelWidth</c>,<c>ImageDecode.Decode(字节, 目标宽)</c> 是同义);</item>
/// <item><c>SolidColorBrush.Freeze()</c> → 去掉(Avalonia 的画刷没有 Freeze);</item>
/// <item><c>LinearGradientBrush.StartPoint</c> 在 Avalonia 是 <c>RelativePoint</c>,所以
///   <c>new RelativePoint(点, RelativeUnit.Relative)</c> —— 渐变停靠点与坐标一字未改;</item>
/// <item><c>DependencyPropertyDescriptor.FromProperty(...).AddValueChanged(控件, 回调)</c>(WPF 专属)→
///   <c>AvaloniaObject.PropertyChanged</c> 事件 + 判 <c>e.Property</c>,并且改成
///   **进可视树订阅 / 出可视树退订**(见 <see cref="AttachedToVisualTree"/>),否则页面是外壳缓存的
///   长命对象、订阅会一直挂着;</item>
/// <item><c>_prog.BeginAnimation(RangeBase.ValueProperty, null)</c> → 去掉(进度动画由
///   <c>Motion.Progress</c> 统一管,见 C1c 的 Motion 门面);</item>
/// <item><c>Clipboard.SetText(...)</c>(WPF 静态入口)→ 复用 C3b 的剪贴板唯一入口
///   <see cref="CrashUi.CopyAsync"/>(它在离屏 / 测试里不碰真剪贴板,失败一律返回 false);</item>
/// <item><c>PasswordBox.Password</c> → <c>TextBox.Text</c>(Avalonia 11 没有 <c>PasswordBox</c>,见 D-75;
///   <c>Ui.Pw()</c> 给的是一份带 <c>PasswordChar</c> 的 <c>TextBox</c>);</item>
/// <item><c>_log.VerticalOffset / ExtentHeight / ViewportHeight / ScrollToEnd()</c>(WPF 的 TextBox 有)→
///   Avalonia 的 <c>TextBox</c> 没有这些,只有它模板里那根 <c>ScrollViewer</c> 有:取不到就**跳过自动滚动**
///   (日志内容照样刷新,只是不追底);</item>
/// <item><c>PageBase.Win?.FlyToTasks(...)</c> → <see cref="FlyToTasksSeam"/>:mac 的外壳(D-97)没有那个
///   "飞向任务页"的浮层,本包按红线**不动外壳**,所以留一个可注入挂点,详见类注释末尾;
///   <b>C3d 更正:外壳已经补上 <c>MainWindow.FlyToTasks</c>(现在页面直接写
///   <c>Win?.FlyToTasks(anchor, text)</c> 即可),这个注入缝由外壳构造函数接上真成员、关窗时解绑
///   —— 本条差异与下面那段"用户可见的降级"**都已作废**,保留原文只为不改写历史;</b></item>
/// <item><c>ContextMenu</c> / <c>MenuItem.Header</c> / <c>Popup</c> 都是 Avalonia 同名类型,
///   只有 <c>Popup</c> 的 <c>StaysOpen</c> 在 Avalonia 叫 <c>IsLightDismissEnabled</c>、<c>AllowsTransparency</c>
///   没有对应(浮层本来就带透明层),这三处差异在 <c>LaunchPage.Popups.cs</c> 里;</item>
/// <item><c>#if IMMERSIVE</c> 的标题栏沉浸那段(<c>SyncImmersive</c> / <c>GlassTopBar</c> / <c>OnHidden</c>)
///   **没有移植**:Windows 的默认构建(<c>-p:Immersive=true</c> 才定义 <c>IMMERSIVE</c>)里它根本不参与编译,
///   而 mac 的外壳是系统红绿灯 + 侧栏布局,没有 Windows 那个 <c>TopBar</c> / <c>WallHost</c> 结构。</item>
/// </list>
///
/// <b>用户可见的降级(请 Lead 裁决,见 D-121)</b>:Windows 在「修复当前版本」时会调
/// <c>Win.FlyToTasks(_launchBtn, "修复中")</c> 播一个"飞向任务页"的小动画。mac 的外壳没搬 `FlyHost`(D-97),
/// 所以这里走注入缝 <see cref="FlyToTasksSeam"/>:挂点没接时**不播动画**(任务本身照跑、照常 Toast)。
/// 这是本页唯一一处用户可见的降级。
///
/// <b>C3d 更正(见 PROGRESS 的 C3c 小节末尾):这条降级已在 C3d 修好。</b>Lead 的裁决是"一次修好外壳,
/// 不在 13 个页面包里重复同一个注入缝",所以外壳补上了 <c>MainWindow.FlyToTasks</c> + `FlyHost`,
/// 并由外壳构造函数把这个接缝接到真成员上(关窗解绑)—— 现在「修复当前版本」真的会飞向任务页。
/// 后续页面**直接写 <c>Win?.FlyToTasks(anchor, text)</c>** 即可,不必再留缝。
/// </summary>
public sealed partial class LaunchPage : PageBase
{
	private readonly ComboBox _inst = Ui.Combo();

	private readonly ComboBox _ver = Ui.Combo();

	private readonly ComboBox _acc = Ui.Combo();

	private readonly ComboBox _java = Ui.Combo();

	private readonly TextBox _user = Ui.Input("Player");

	private readonly TextBox _server = Ui.Input(I18n.L("直连服务器 ip:port（可空）"));

	private readonly TextBox _wBox = Ui.Input("854", "", 74.0);

	private readonly TextBox _hBox = Ui.Input("480", "", 74.0);

	private readonly Slider _mem = Ui.Sld(1024.0, 32768.0, 4096.0, 256.0);

	private readonly TextBlock _memLbl = Ui.Txt("4096 MB", 12.0, bold: true);

	private readonly Button _launchBtn = Ui.Btn(I18n.L("启动游戏"), BtnKind.Primary, null, "\ue768");

	private readonly Button _stopBtn = Ui.Btn(I18n.L("停止"), BtnKind.Danger, null, "\ue71a");

	private readonly ProgressBar _prog = Ui.Prog();

	private readonly TextBlock _status = Ui.Small(I18n.L("就绪"));

	private int _progGen;

	private readonly TextBox _log = Ui.LogBox();

	private readonly TextBlock _bTitle = new TextBlock
	{
		FontSize = 17.0,
		FontWeight = FontWeight.SemiBold,
	};

	private readonly TextBlock _bSub = new TextBlock
	{
		FontSize = 12.0,
		Opacity = 0.82,
	};

	private readonly SPanel _newsHost = Ui.V(6.0);

	private readonly SPanel _taskHost = Ui.V(6.0);

	private readonly TextBlock _playTotal = new TextBlock
	{
		FontSize = 20.0,
		FontWeight = FontWeight.SemiBold,
	};

	private readonly TextBox _notes = Ui.Multi(I18n.L("随手记点什么..."));

	/// <summary>旧卡片画布:Windows 在 WP-U3 之后不再把它挂进可视树,字段与数据逻辑全部保留(逐字照抄)。</summary>
	private readonly DashHost _dash = new DashHost();

	private readonly List<string> _logLines = new List<string>();

	private List<JavaOption> _javaOpts = new List<JavaOption>();

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private string? _taskId;

	private string? _loginTask;

	private bool _syncing;

	private bool _crashShown;

	private TextBlock? _loginHint;

	private TextBlock? _loginCode;

	private string _loginUri = "";

	private Dlg.Layer? _loginLayer;

	private string? _preferredInstance;

	private string? _preferredVersion;

	private readonly DispatcherTimer _memSaveTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(650.0),
	};

	private bool _layoutLoaded;

	// —— 启动屏 UI(v2:左对齐 + 截图背景;配色随有无截图在 ApplyHeroTone 切换) ——
	private readonly TextBlock _kickerText = new TextBlock
	{
		FontSize = 12.5,
		MaxWidth = 560.0,
		TextTrimming = TextTrimming.CharacterEllipsis,
		VerticalAlignment = VerticalAlignment.Center,
	};

	private readonly TextBlock _heroTitle = new TextBlock
	{
		FontSize = 34.0,
		FontWeight = FontWeight.ExtraBold,
		MaxWidth = 620.0,
		TextTrimming = TextTrimming.CharacterEllipsis,
	};

	// 主操作行外壳:运行中隐藏、显示同尺寸「停止」,保证该行始终只有一个 48 高主按钮
	private Border? _launchShell;

	private readonly TextBlock _newsTicker = new TextBlock
	{
		FontSize = 12.5,
		MaxWidth = 520.0,
		TextTrimming = TextTrimming.CharacterEllipsis,
		VerticalAlignment = VerticalAlignment.Center,
	};

	private readonly Border _stopShell = new Border
	{
		CornerRadius = new CornerRadius(12.0),
		Height = 48.0,
		Padding = new Thickness(26.0, 0.0, 26.0, 0.0),
		IsVisible = false,
	};

	private Control? _configBody;

	// 48×48 启动配置图标钮(配置浮层锚点)
	private Border? _configPill;

	private SPanel? _titleRow;

	private SPanel? _hero;

	private Popup? _configPopup;

	private Popup? _switchPopup;

	private Popup? _newsPopup;

	private StackPanel? _switchList;

	private TextBox? _switchSearch;

	private readonly Image _bgImage = new Image
	{
		Stretch = Stretch.UniformToFill,
		IsHitTestVisible = false,
		IsVisible = false,
	};

	private readonly Grid _bgShade = new Grid
	{
		IsHitTestVisible = false,
		IsVisible = false,
	};

	private readonly SPanel _chipRow = Ui.H(6.0);

	// 主按钮内嵌版本(SyncHero 填充):细竖线分隔 + 所选版本全名
	private readonly Border _goSep = new Border { Width = 1.0, Height = 20.0, Opacity = 0.45, Margin = new Thickness(2.0, 0.0, 2.0, 0.0) };

	private readonly TextBlock _goVer = Ui.Txt("").VCenter();

	// 随背景切换配色的半透明块(配置钮 / 联机钮 / 新闻卡)
	private readonly List<Border> _glass = new List<Border>();

	private Border? _newsCard;

	// 内容区块宿主(WP-U3):BuildHero 里构建并放进根 Grid 第 1 行
	private BlockHost? _blocks;

	// 进度行(空闲时收起):SyncStatusRow 要能碰到它,所以从 BuildHero 的局部变量提成字段
	private SPanel? _statusRow;

	/// <summary>
	/// 主按钮柔光。WPF 是 <c>DropShadowEffect{BlurRadius=20, ShadowDepth=6, Direction=270}</c>
	/// (270° = 竖直向下);Avalonia 没有 Direction,等价写法是 <c>OffsetY = +ShadowDepth</c>
	/// (与 C1c 的 <c>Motion.Shadow</c> 同一口径)。
	/// </summary>
	private readonly DropShadowEffect _launchGlow = new DropShadowEffect
	{
		BlurRadius = 20.0,
		OffsetY = 6.0,
	};

	/// <summary>同上的标题阴影(<c>BlurRadius=12, ShadowDepth=2, Direction=270, Opacity=0.3</c>)。</summary>
	private readonly DropShadowEffect _titleShadow = new DropShadowEffect
	{
		BlurRadius = 12.0,
		OffsetY = 2.0,
		Opacity = 0.3,
	};

	private bool _onImage;

	private string _bgKey = "";

	private int _bgGen;

	/// <summary>嵌入 <c>hero.jpg</c>(启动页默认背景)只解码一次(两版通用)。WPF 是 <c>BitmapSource</c> + Freeze。</summary>
	private static Bitmap? _heroCache;

	private int _modCount = -1;

	private string _modKey = "";

	public override string Title => I18n.L("启动");

	/// <summary>
	/// 「飞向任务页」的注入缝(见类注释末尾的降级说明)。
	/// Windows 是 <c>Win.FlyToTasks(source, text)</c>;mac 的外壳还没有这个成员,所以由外壳补齐时接上,
	/// 没接就只是不播那个动画(修复任务照跑)。
	///
	/// <b>C3d:外壳已经补齐并把这条缝接上了</b>(<c>MainWindow</c> 构造函数里
	/// <c>FlyToTasksSeam = (source, text) =&gt; FlyToTasks(source, text)</c>,关窗置空)。
	/// 缝的签名保持不变(下游可继续注入自己的探针 / 替身);**新页面不要再加缝**,
	/// 直接写 <c>Win?.FlyToTasks(anchor, text)</c>。
	/// </summary>
	internal static Action<Control, string>? FlyToTasksSeam { get; set; }

	private async void HideProgressSoon(int delayMs)
	{
		int gen = _progGen;
		if (delayMs > 0)
		{
			await Task.Delay(delayMs);
		}
		if (gen == _progGen)
		{
			_prog.IsVisible = false;
		}
	}

	public LaunchPage()
	{
		_bTitle.Dyn(TextBlock.ForegroundProperty, "B.Ink");
		_bSub.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		_stopBtn.IsEnabled = false;
		_launchBtn.Click += delegate
		{
			PageBase.Run(LaunchAsync, I18n.L("启动失败"));
		};
		_stopBtn.Click += delegate
		{
			PageBase.Run(StopAsync);
		};
		_inst.SelectionChanged += delegate
		{
			if (!_syncing)
			{
				PageBase.Run(OnInstanceChanged);
			}
		};
		_ver.SelectionChanged += delegate
		{
			if (!_syncing)
			{
				SyncBanner();
			}
		};
		_java.SelectionChanged += delegate
		{
			if (!_syncing)
			{
				PageBase.Run(SaveJavaAsync);
			}
		};
		// WPF 是 RoutedPropertyChangedEventArgs<double>,Avalonia 是 RangeBaseValueChangedEventArgs(同样是 NewValue)。
		_mem.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
		{
			_memLbl.Text = $"{(int)e.NewValue} MB";
			SyncHero();
			if (!_syncing)
			{
				_memSaveTimer.Stop();
				_memSaveTimer.Start();
			}
		};
		_memSaveTimer.Tick += delegate
		{
			_memSaveTimer.Stop();
			if (!_syncing)
			{
				PageBase.Run(SaveDisplayPrefsAsync);
			}
		};
		_wBox.LostFocus += delegate
		{
			CommitDisplayPrefs();
		};
		_hBox.LostFocus += delegate
		{
			CommitDisplayPrefs();
		};
		_notes.LostFocus += delegate
		{
			CommitNotes();
		};
		BuildHero();
	}

	/// <summary>
	/// 【Windows 源】分辨率两个输入框 <c>LostFocus</c> 的处理器体(抽成成员只是为了能被告知式地驱动:
	/// 构造 <c>LostFocusEventArgs</c> 属于"模拟真实输入设备",见 D-115 的同一套做法)。
	/// </summary>
	internal void CommitDisplayPrefs()
	{
		if (!_syncing)
		{
			PageBase.Run(SaveDisplayPrefsAsync);
		}
	}

	/// <summary>【Windows 源】便签 <c>LostFocus</c> 的处理器体:写进偏好并落盘。</summary>
	internal void CommitNotes()
	{
		if (PageBase.Win != null)
		{
			PageBase.Win.Prefs.Notes = _notes.Text;
			PageBase.Win.SaveUiPrefs();
		}
	}

	/// <summary>【Windows 源】<c>LaunchPage.cs:425-428</c>:「联机」玻璃钮的处理器体(进联机页)。</summary>
	internal void OpenMultiplayer()
	{
		PageBase.Win?.Navigate("multiplayer");
	}

	// ————— 启动屏布局(v2) —————

	private static IBrush White(byte alpha)
	{
		// WPF 那两行 Freeze() 去掉:Avalonia 的 Brush 没有 Freeze。
		return new SolidColorBrush(Color.FromArgb(alpha, byte.MaxValue, byte.MaxValue, byte.MaxValue));
	}

	private static readonly IBrush W100 = White(byte.MaxValue);

	private static readonly IBrush W80 = White(204);

	private static readonly IBrush W14 = White(36);

	private static readonly IBrush W22 = White(56);

	// 截图遮罩:rgba(10,14,22,a) 线性渐变;stops 依次为 alpha, offset
	private static Rectangle Shade(Point from, Point to, params double[] stops)
	{
		// Avalonia 的渐变起止点是 RelativePoint(WPF 是 Point)—— 数值与含义一字未改。
		LinearGradientBrush g = new LinearGradientBrush
		{
			StartPoint = new RelativePoint(from, RelativeUnit.Relative),
			EndPoint = new RelativePoint(to, RelativeUnit.Relative),
		};
		for (int i = 0; i + 1 < stops.Length; i += 2)
		{
			g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)stops[i], 10, 14, 22), stops[i + 1]));
		}
		return new Rectangle
		{
			Fill = g,
			IsHitTestVisible = false,
		};
	}

	private void BuildHero()
	{
		// —— 背景层:截图(异步)+ 左→右深色渐变 + 底部渐变(数值同 launch2.html .sh)——
		_bgShade.Children.Add(Shade(new Point(0.0, 0.5), new Point(1.0, 0.5), 199.0, 0.0, 115.0, 0.42, 0.0, 0.75));
		_bgShade.Children.Add(Shade(new Point(0.5, 1.0), new Point(0.5, 0.0), 140.0, 0.0, 0.0, 0.35));
		// —— 状态行:绿点 就绪 · 游戏目录 ——
		_prog.Height = 4.0;
		_prog.Width = 320.0;
		_prog.Dyn(TemplatedControl.ForegroundProperty, "B.Accent");
		_prog.Opacity = 0.92;
		_prog.IsVisible = false;
		Ellipse dot = new Ellipse
		{
			Width = 7.0,
			Height = 7.0,
			VerticalAlignment = VerticalAlignment.Center,
		};
		dot.Dyn(Shape.FillProperty, "B.Ok");
		SPanel kicker = Ui.H(7.0, dot, _kickerText);
		// —— 大标题:实例名 + 下拉箭头,点击打开原有实例切换浮层 ——
		TextBlock chevron = Ui.Glyph("\ue70d", 18.0);
		chevron.Opacity = 0.7;
		_titleRow = Ui.H(10.0, _heroTitle.VCenter(), chevron);
		_titleRow.Cursor = new Cursor(StandardCursorType.Hand);
		_titleRow.Background = Brushes.Transparent;
		_titleRow.HorizontalAlignment = HorizontalAlignment.Left;
		_titleRow.Margin = new Thickness(0.0, 6.0, 0.0, 2.0);
		_titleRow.PointerReleased += delegate
		{
			OpenSwitchPopup();
		};
		// —— 小标签行(Java / 内存 / 模组数,SyncHero 填充)——
		_chipRow.Margin = new Thickness(0.0, 10.0, 0.0, 24.0);
		_configBody = BuildConfig();
		// MaxHeight 由 MakePopup 统一绑窗口百分比,不再写死
		// —— 按钮行:主色「启动游戏」48 高 + 主色柔光;模板圆角固定,故用外壳 Border 提供圆角 ——
		StyleKeys.Apply(_launchBtn, "Btn.Ghost");
		_launchBtn.Padding = new Thickness(0.0);
		_launchBtn.FontSize = 16.0;
		_launchBtn.Dyn(TemplatedControl.ForegroundProperty, "B.OnAccentStrong");
		TextBlock goText = Ui.Txt(I18n.L("启动游戏"), 16.0).VCenter();
		goText.FontWeight = FontWeight.ExtraBold;
		_goVer.FontSize = 13.0;
		_goVer.Opacity = 0.85;
		_goVer.IsVisible = false;
		_goSep.IsVisible = false;
		_goSep.Dyn(Border.BackgroundProperty, "B.OnAccentStrong");
		_launchBtn.Content = Ui.H(10.0, Ui.Glyph("\ue768", 17.0), goText, _goSep, _goVer);
		_launchShell = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Height = 48.0,
			Padding = new Thickness(30.0, 0.0, 30.0, 0.0),
			Child = _launchBtn,
			Effect = _launchGlow,
		};
		_launchShell.Dyn(Border.BackgroundProperty, "B.AccentStrong");
		Motion.HoverLift(_launchShell, 1.02, 1.0, 8.0);
		StyleKeys.Apply(_stopBtn, "Btn.Ghost");
		_stopBtn.Padding = new Thickness(0.0);
		_stopBtn.Content = Ui.H(8.0, Ui.Glyph("\ue71a", 15.0), Ui.Txt(I18n.L("停止"), 15.0, bold: true));
		_stopShell.Child = _stopBtn;
		_stopShell.Dyn(Border.BackgroundProperty, "B.DangerSoft");
		_stopBtn.Dyn(TemplatedControl.ForegroundProperty, "B.Danger");
		_configPill = GlassButton("\ue713", null, OpenConfigPopup);
		_configPill.Tip(I18n.L("启动配置"));
		// 联机入口(原「更多」抽屉里的联机):进入现有联机页
		Border net = GlassButton("\ue701", I18n.L("联机"), OpenMultiplayer);
		SPanel launchRow = Ui.H(10.0, _launchShell, _stopShell, _configPill, net);
		// 进度行:空闲(无进度且状态为「就绪」)时与顶部状态重复,收起
		_statusRow = Ui.H(10.0, _prog.VCenter(), _status.VCenter());
		_statusRow.Margin = new Thickness(0.0, 14.0, 0.0, 0.0);
		SyncStatusRow();
		_hero = Ui.V(0.0, kicker, _titleRow, _chipRow, launchRow, _statusRow);
		_hero.MaxWidth = 680.0;
		_hero.HorizontalAlignment = HorizontalAlignment.Left;
		_hero.VerticalAlignment = VerticalAlignment.Center;
		_hero.Margin = new Thickness(56.0, 0.0, 24.0, 0.0);
		// —— 底部新闻卡:圆角 12,主色「新闻」小标签 + 标题 + 箭头;点击打开原公告浮层 ——
		Border tag = new Border
		{
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(8.0, 2.0, 8.0, 3.0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = Ui.Txt(I18n.L("新闻"), 11.0, bold: true, "B.OnAccent"),
		};
		tag.Dyn(Border.BackgroundProperty, "B.Accent");
		TextBlock arrow = Ui.Glyph("\ue76c", 10.0);
		arrow.Opacity = 0.8;
		_newsCard = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(8.0, 8.0, 14.0, 8.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Bottom,
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = Ui.H(10.0, tag, _newsTicker, arrow),
		};
		Glass(_newsCard);
		_newsCard.PointerReleased += delegate
		{
			OpenNewsPopup();
		};
		// —— 内容区块(WP-U3):新闻卡默认可见,任务/时长/便签/日志默认收起;右键「自定义布局...」由 BlockHost 自带 ——
		// contentMargin 对齐英雄区:左 56(同 _hero)、右 24(同 _hero)、下 18(原新闻卡底边距),默认渲染与旧布局逐像素等价
		Border logCard = Ui.Card(_log, 10.0);
		logCard.Height = 240.0;
		_blocks = new BlockHost("launch", new List<BlockSpec>
		{
			new BlockSpec { Id = "news", Title = I18n.L("新闻"), Content = _newsCard },
			new BlockSpec { Id = "tasks", Title = I18n.L("下载任务"), Content = Ui.Card(_taskHost), DefaultVisible = false },
			new BlockSpec { Id = "playtime", Title = I18n.L("游戏时长"), Content = Ui.Card(_playTotal), DefaultVisible = false },
			new BlockSpec { Id = "notes", Title = I18n.L("便签"), Content = Ui.Card(_notes), DefaultVisible = false },
			new BlockSpec { Id = "log", Title = I18n.L("日志"), Content = logCard, DefaultVisible = false },
		}, new Thickness(56.0, 0.0, 24.0, 18.0));
		// 多块同时展开时限高内滚(约 62% 页高),避免 Auto 行把上方英雄区挤塌;默认仅新闻卡时远达不到上限。
		// 【机械改动点,见 C3a 的 Lead 验收与 C1c 的 SmoothScroll.BindWindowMaxHeight】
		// WPF 绑的是 this.ActualHeight;Avalonia 没有 ActualHeight,只有 Visual.Bounds,
		// 所以路径写成 Bounds.Height(Bounds 是会通知的属性,页面尺寸变了会重算);属性换成 Layoutable.MaxHeightProperty。
		// 这条订阅的源是页面自己、目标是页面的孩子,引用图完全在页面内部,不会把页面挂到任何外部根上。
		_blocksMaxHeightBinding = _blocks.Root.Bind(Layoutable.MaxHeightProperty, new Binding("Bounds.Height")
		{
			Source = this,
			Converter = new Ui.WindowPctConverter(0.62),
		});
		// —— 根布局:背景层铺满两行;内容 * / 区块 Auto ——
		Grid root = Ui.G("*,Auto");
		root.ClipToBounds = true;
		root.Add(_bgImage, 0, 0, 2);
		root.Add(_bgShade, 0, 0, 2);
		root.Add(_hero, 0);
		root.Add(_blocks.Root, 1);
		Content = root;
		ApplyHeroTone(onImage: false);
		SyncHero();
	}

	// 半透明按钮(48 高,圆角 12);label 为 null 即 48×48 图标钮
	private Border GlassButton(string glyph, string? label, Action click)
	{
		TextBlock g = Ui.Glyph(glyph, 17.0);
		g.HorizontalAlignment = HorizontalAlignment.Center;
		Border b = new Border
		{
			Height = 48.0,
			MinWidth = 48.0,
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			Padding = ((label == null) ? new Thickness(0.0) : new Thickness(16.0, 0.0, 16.0, 0.0)),
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = ((label == null) ? ((Control)g) : ((Control)Ui.H(8.0, g, Ui.Txt(label, 14.0, bold: true).VCenter()))),
		};
		Glass(b);
		b.PointerReleased += delegate
		{
			click();
		};
		return b;
	}

	private void Glass(Border b)
	{
		_glass.Add(b);
		b.PointerEntered += delegate
		{
			Tone(b, hover: true);
		};
		b.PointerExited += delegate
		{
			Tone(b, hover: false);
		};
	}

	// 有截图:白字 + 半透明白块;无截图:主题纸色块 + 主题字色(随主题资源自动切换)
	private void Tone(Border b, bool hover)
	{
		if (_onImage)
		{
			b.Background = (hover ? W22 : W14);
			b.BorderBrush = W22;
			b.SetValue(TextElement.ForegroundProperty, W100);
		}
		else
		{
			b.Dyn(Border.BackgroundProperty, hover ? "B.Hover" : "B.Paper");
			b.Dyn(Border.BorderBrushProperty, "B.Line");
			b.Dyn(TextElement.ForegroundProperty, "B.Ink");
		}
	}

	/// <summary>
	/// 背景 tone 切换(有截图 = 白字 + 半透明白块;无截图 = 主题纸色 + 主题字色)。
	/// 【Windows 源】<c>LaunchPage.cs:555-595</c>。可访问性由 private 降为 internal:
	/// 测试要能被告知式地驱动它(不模拟真实输入设备,同 D-115 的口径)。
	/// </summary>
	internal void ApplyHeroTone(bool onImage)
	{
		_onImage = onImage;
		_bgImage.IsVisible = onImage;
		_bgShade.IsVisible = onImage;
		if (onImage)
		{
			_hero?.SetValue(TextElement.ForegroundProperty, W100);
			_kickerText.Foreground = W80;
			_status.Foreground = W80;
			_heroTitle.Effect = _titleShadow;
		}
		else
		{
			_hero?.Dyn(TextElement.ForegroundProperty, "B.Ink");
			_kickerText.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
			_status.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
			_heroTitle.Effect = null;
		}
		foreach (Border b in _glass)
		{
			Tone(b, hover: false);
		}
		foreach (Control c in _chipRow.Children)
		{
			if (c is Border chip)
			{
				Tone(chip, hover: false);
			}
		}
		// 主按钮柔光取当前主题主色(OnShown 时重取,跟随主题切换)。
		// WPF 是 TryFindResource("B.AccentStrong") as SolidColorBrush —— 取不到就**不动**柔光颜色,
		// 这里照抄这个语义(Palette.TryGet 失败时直接跳过,而不是写兜底灰)。
		if (Palette.TryGet("B.AccentStrong", Palette.Current, out object? accent) && accent is ISolidColorBrush brush)
		{
			_launchGlow.Color = brush.Color;
		}
		_launchGlow.Opacity = (onImage ? 0.45 : 0.3);
	}

	/// <summary>
	/// 【Windows 源】<c>BuildHero</c> 里的局部函数 <c>syncStatus</c>(逐行照抄):
	/// 没有进度条、状态又是「就绪」时把整个进度行收起(与顶部状态行重复)。
	/// 提成方法只是因为 mac 侧改用 <c>PropertyChanged</c> 订阅、要能退订(见 <see cref="AttachedToVisualTree"/>)。
	/// </summary>
	private void SyncStatusRow()
	{
		if (_statusRow is null)
		{
			return;
		}
		bool idle = !_prog.IsVisible && (string.IsNullOrEmpty(_status.Text) || _status.Text == I18n.L("就绪"));
		_statusRow.IsVisible = !idle;
	}

	private void OnStatusPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == TextBlock.TextProperty)
		{
			SyncStatusRow();
		}
	}

	private void OnProgPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == Visual.IsVisibleProperty)
		{
			SyncStatusRow();
		}
	}

	private bool _syncHooksAttached;

	/// <summary>区块宿主的 MaxHeight 绑定句柄(Avalonia 的 <c>Bind</c> 返回订阅;只留着备查,不主动释放)。</summary>
	private IDisposable? _blocksMaxHeightBinding;

	/// <summary>那条 MaxHeight 绑定还在不在(测试用来钉"绑定建起来了"这一事实)。</summary>
	internal bool BlocksHeightBindingAlive => _blocksMaxHeightBinding != null;

	/// <summary>
	/// 释放那条 MaxHeight 绑定(测试收尾用;生产路径不释放 —— 页面由 <c>PageHost</c> 缓存到进程结束,
	/// Windows 的 <c>SetBinding</c> 也是同一种"活到进程结束"的形状)。
	/// </summary>
	internal void ReleaseBlocksHeightBinding()
	{
		_blocksMaxHeightBinding?.Dispose();
		_blocksMaxHeightBinding = null;
	}

	/// <summary>此刻有没有挂着那两个变更订阅(测试用来钉"出可视树就不留订阅")。</summary>
	internal bool SyncHooksAttached => _syncHooksAttached;

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		if (!_syncHooksAttached)
		{
			_syncHooksAttached = true;
			_status.PropertyChanged += OnStatusPropertyChanged;
			_prog.PropertyChanged += OnProgPropertyChanged;
			// Windows 在 AddValueChanged 之后也立刻跑一次。
			SyncStatusRow();
		}
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		DetachSyncHooks();
	}

	/// <summary>退订那两个变更订阅(出可视树时自动调;测试也可以直接调)。</summary>
	internal void DetachSyncHooks()
	{
		if (_syncHooksAttached)
		{
			_syncHooksAttached = false;
			_status.PropertyChanged -= OnStatusPropertyChanged;
			_prog.PropertyChanged -= OnProgPropertyChanged;
		}
	}

	private Border Chip(string text)
	{
		Border chip = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(10.0, 3.0, 10.0, 4.0),
			Child = Ui.Txt(text, 12.0),
		};
		Tone(chip, hover: false);
		return chip;
	}

	// 背景:当前实例目录(含版本隔离目录)screenshots 最新一张;同一张不重复解码;任何失败回退渐变底
	private async void RefreshBackground()
	{
		int gen = ++_bgGen;
		try
		{
			string instance = _inst.Str();
			string version = _ver.Str();
			string root = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance)?.Path ?? "";
			string skip = _bgKey;
			// 用户在设置里自定义了壁纸时尊重其选择,不叠截图
			Tuple<string, Bitmap?>? shot = null;
			if (!Wallpaper.Active && root.Length > 0)
			{
				string[] dirs = new string[2]
				{
					System.IO.Path.Combine(root, "screenshots"),
					string.IsNullOrEmpty(version) ? "" : System.IO.Path.Combine(root, "versions", version, "screenshots"),
				};
				shot = await Task.Run(() => LoadShot(dirs, skip));
			}
			if (gen != _bgGen || (shot != null && shot.Item2 == null))
			{
				return;
			}
			// 没有截图(且用户没设自定义壁纸)时用嵌入 hero.jpg(launch2 效果图同款全景)做默认背景,
			// 不再回退纯渐变底;截图存在时仍截图优先。换壁纸后下次刷新会走 ApplyHeroTone(false) 还原。
			if (shot == null && !Wallpaper.Active)
			{
				Bitmap? hero = await Task.Run(HeroSource);
				if (gen != _bgGen)
				{
					return;
				}
				if (hero != null)
				{
					_bgKey = "";
					_bgImage.Source = hero;
					ApplyHeroTone(onImage: true);
					return;
				}
			}
			_bgKey = shot?.Item1 ?? "";
			_bgImage.Source = shot?.Item2;
			ApplyHeroTone(shot != null);
		}
		catch
		{
			// 背景只是装饰,任何异常都不影响启动页
		}
	}

	// 线程池执行:找最新 png/jpg 截图并解码(OnLoad 不锁文件,不嵌任何图片进 exe)。
	// 与 skipKey 相同则只返回 key(Item2 = null,表示保持现状);没有截图或出错返回 null。
	private static Tuple<string, Bitmap?>? LoadShot(string[] dirs, string skipKey)
	{
		try
		{
			FileInfo? best = null;
			foreach (string d in dirs)
			{
				if (d.Length == 0 || !Directory.Exists(d))
				{
					continue;
				}
				foreach (FileInfo f in new DirectoryInfo(d).EnumerateFiles())
				{
					string ext = f.Extension.ToLowerInvariant();
					if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && (best == null || f.LastWriteTimeUtc > best.LastWriteTimeUtc))
					{
						best = f;
					}
				}
			}
			if (best == null)
			{
				return null;
			}
			string key = best.FullName + "|" + best.LastWriteTimeUtc.Ticks;
			if (key == skipKey)
			{
				return Tuple.Create(key, (Bitmap?)null);
			}
			// WPF 是 BitmapImage{OnLoad, IgnoreColorProfile, DecodePixelWidth=1600} + Freeze;
			// mac 侧统一走 ImageDecode(Skia 编解码器),解码失败同样返回 null(→ 回退 hero / 渐变底)。
			Bitmap? bmp = ImageDecode.DecodeFile(best.FullName, 1600);
			if (bmp is null)
			{
				return null;
			}
			return Tuple.Create(key, bmp);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>嵌入 <c>hero.jpg</c> 的 avares URI(= Windows 的 <c>pack://application:,,,/TipCL;component/PremiumAssets/hero.jpg</c>)。</summary>
	internal const string HeroUri = "avares://TipCL/PremiumAssets/hero.jpg";

	// 解码嵌入 hero.jpg(pack URI,后台线程可解码,解码后可跨线程用),只解码一次。
	// 视觉基准 launch2.png 的背景是清晰全景,不加 BlurEffect(毛玻璃只给标题栏与半透明块)。
	private static Bitmap? HeroSource()
	{
		if (_heroCache == null)
		{
			try
			{
				using Stream? src = AssetLoader.Open(new Uri(HeroUri));
				if (src is null)
				{
					return null;
				}
				using MemoryStream ms = new MemoryStream();
				src.CopyTo(ms);
				Bitmap? bmp = ImageDecode.Decode(ms.ToArray(), 1600);
				if (bmp == null)
				{
					return null;
				}
				_heroCache = bmp;
			}
			catch
			{
				return null;
			}
		}
		return _heroCache;
	}

	/// <summary>丢掉 hero 的解码缓存(测试用:换语言 / 重来时不必猜缓存状态)。</summary>
	internal static void ResetHeroCache()
	{
		_heroCache = null;
	}

	// 模组数小标签:沿用实例库同源 API,同一实例/版本只查一次
	private async Task CountModsAsync(string instance, string version)
	{
		string key = instance + "|" + version;
		if (key == _modKey)
		{
			return;
		}
		_modKey = key;
		_modCount = -1;
		List<ModEntry> list = await PageBase.Api.TryCallAsync("get_installed_mod_entries", new
		{
			instance,
			version,
		}, new List<ModEntry>());
		if (key == _modKey)
		{
			_modCount = list?.Count ?? 0;
			SyncHero();
		}
	}

	// ————— 数据填充 —————

	protected override async Task LoadAsync()
	{
		if (!_layoutLoaded)
		{
			_layoutLoaded = true;
			if (PageBase.Win != null)
			{
				_notes.Text = PageBase.Win.Prefs.Notes;
			}
		}
		await FillCardData();
	}

	private async Task FillCardData()
	{
		_syncing = true;
		try
		{
			_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
			_inst.Fill(_instances.Select((InstanceInfo i) => i.Name));
			string keepAcc = _acc.Str();
			_acc.Fill(await AccountGate.ListAsync(), (keepAcc.Length > 0) ? keepAcc : PageBase.Win?.Prefs.LastUser);
			Dictionary<string, JsonElement>? dictionary = await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
			if (dictionary != null)
			{
				if (dictionary.TryGetValue("default_memory_mb", out var value) && value.TryGetInt32(out var value2))
				{
					_mem.Value = Clamp.Of(value2, 1024, 32768);
				}
				if (dictionary.TryGetValue("default_resolution", out var value3) && value3.ValueKind == JsonValueKind.Array && value3.GetArrayLength() >= 2)
				{
					_wBox.Text = value3[0].ToString();
					_hBox.Text = value3[1].ToString();
				}
			}
			_memLbl.Text = $"{(int)_mem.Value} MB";
			if (PageBase.Win != null && !string.IsNullOrEmpty(PageBase.Win.Prefs.Notes))
			{
				_notes.Text = PageBase.Win.Prefs.Notes;
			}
			if (string.IsNullOrWhiteSpace(_user.Text))
			{
				_user.Text = PageBase.Win?.Prefs.LastUser ?? "";
			}
		}
		finally
		{
			_syncing = false;
		}
		await ReloadVersionsAsync();
		await ReloadJavaAsync(scan: false);
		_ = ReloadJavaAsync(scan: true);
		SyncBanner();
		await ApplyPreferenceAsync();
		_ = LoadNewsAsync();
		_ = LoadPlaytimeAsync();
		SyncTaskCard();
	}

	private async Task OnInstanceChanged()
	{
		await ReloadVersionsAsync();
		await ReloadJavaAsync(scan: false);
		SyncBanner();
	}

	private int _verGen;

	private async Task ReloadVersionsAsync()
	{
		string text = _inst.Str();
		if (!string.IsNullOrEmpty(text))
		{
			int gen = ++_verGen;
			List<string> list;
			try
			{
				list = await PageBase.Api.CallAsync<List<string>>("get_installed_versions", new
				{
					instance = text,
				}) ?? new List<string>();
			}
			catch (BridgeCallException)
			{
				// 后端出错 ≠ 实例没有版本:保留现有列表,不误显「还没有版本」
				_launchBtn.IsEnabled = _ver.Items.Count > 0 && _taskId == null;
				return;
			}
			if (gen != _verGen || _inst.Str() != text)
			{
				return; // 过期结果丢弃:期间实例已切换或有更新的一次重载
			}
			_syncing = true;
			_ver.Fill(list);
			_syncing = false;
			_launchBtn.IsEnabled = list.Count > 0 && _taskId == null;
			if (list.Count == 0)
			{
				_status.Text = I18n.L("这个实例还没有版本，先去「下载」装一个");
			}
		}
	}

	private async Task ReloadJavaAsync(bool scan)
	{
		string inst = _inst.Str();
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}
		List<JavaOption> opts = (await PageBase.Api.TryCallAsync("java_combo_options", new
		{
			instance = inst,
			scan_system = scan,
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (!(_inst.Str() != inst) && opts.Count != 0)
		{
			string keep = await PageBase.Api.TryCallAsync("java_combo_label_for", new
			{
				instance = inst,
				options = opts,
			}, "自动选择");
			_syncing = true;
			_javaOpts = opts;
			_java.Fill(opts.Select((JavaOption o) => o.Label), keep);
			_syncing = false;
			SyncHero();
		}
	}

	private async Task SaveJavaAsync()
	{
		string text = _inst.Str();
		if (!string.IsNullOrEmpty(text))
		{
			await PageBase.Api.TryCallAsync<object>("set_instance_java", new
			{
				name = text,
				java = SelectedJava(),
			});
		}
	}

	private async Task SaveDisplayPrefsAsync()
	{
		await PageBase.Api.TryCallAsync<object>("update_settings", new
		{
			settings = new
			{
				default_memory_mb = (int)_mem.Value,
				default_resolution = new int[2]
				{
					Int(_wBox.Text, 854),
					Int(_hBox.Text, 480),
				},
			},
		});
	}

	public void PreferInstance(string name, string? version = null)
	{
		_preferredInstance = name;
		_preferredVersion = version;
		// 必须等首次 FillCardData 完成(否则 _ver.Items 未灌入,版本偏好会被丢弃),且与飞行中的填充串行
		PageBase.Run(async delegate
		{
			await EnsureLoadedAsync();
			await ApplyPreferenceAsync();
		});
	}

	/// <summary>选中指定实例/版本后直接启动(AI 页任务卡「启动游戏」用)。</summary>
	public async Task LaunchNowAsync(string name, string? version = null)
	{
		await EnsureLoadedAsync();
		_preferredInstance = name;
		_preferredVersion = version;
		await FillCardData();
		await LaunchAsync();
	}

	private async Task ApplyPreferenceAsync()
	{
		string? name = _preferredInstance;
		if (string.IsNullOrEmpty(name) || _instances.All((InstanceInfo i) => i.Name != name))
		{
			return;
		}
		_preferredInstance = null;
		if (_inst.Str() != name)
		{
			_syncing = true;
			_inst.Fill(_instances.Select((InstanceInfo i) => i.Name), name);
			_syncing = false;
			await ReloadVersionsAsync();
			await ReloadJavaAsync(scan: false);
		}
		else
		{
			// 实例已选中但版本列表可能是旧的(首次填充尾部还会再刷一次),先确保包含目标版本
			await ReloadVersionsAsync();
		}
		string? preferredVersion = _preferredVersion;
		if (string.IsNullOrEmpty(preferredVersion))
		{
			SyncBanner();
			return;
		}
		if (_ver.Items.Contains(preferredVersion))
		{
			_preferredVersion = null;
			_syncing = true;
			try
			{
				// 直接选中,不用自身 Items 重填(Fill 迭代活视图 + SelectedIndex=0 回落会丢选中)
				_ver.SelectedItem = preferredVersion;
			}
			finally
			{
				_syncing = false;
			}
		}
		else if (_ver.Items.Count > 0)
		{
			// 版本列表已就绪但没有目标版本(可能已被卸载)——丢弃偏好避免反复重试
			_preferredVersion = null;
		}
		// 版本列表未就绪(0 项)时保留偏好,等下一次 ApplyPreferenceAsync 再试
		SyncBanner();
	}

	/// <summary>chip 首位显示「加载器名 版本」:与后端 LOADER_TAGS 同前缀表;不匹配回落版本全名。</summary>
	private static string LoaderChip(string ver)
	{
		string low = ver.ToLowerInvariant();
		// fabric-loader-0.20.1-26.3 → Fabric 0.20.1;forge-1.20.1-47.x → Forge 1.20.1;neoforge-21.x.x → NeoForge 21.x.x
		(string prefix, string label)[] tags = new[]
		{
			("fabric-loader-", "Fabric"),
			("neoforge-", "NeoForge"),
			("optifine-", "OptiFine"),
			("liteloader-", "LiteLoader"),
			("quilt-loader-", "Quilt"),
			("forge-", "Forge"),
		};
		foreach (var (prefix, label) in tags)
		{
			if (!low.StartsWith(prefix, StringComparison.Ordinal))
			{
				continue;
			}
			string rest = ver.Substring(prefix.Length);
			int dash = rest.IndexOf('-');
			return (dash > 0 ? rest.Substring(0, dash) : rest) is string lv && lv.Length > 0 ? label + " " + lv : ver;
		}
		return ver;
	}

	private string SelectedJava()
	{
		string label = _java.Str();
		object? obj = _javaOpts.FirstOrDefault((JavaOption o) => o.Label == label)?.Value;
		if (obj == null)
		{
			if (!string.IsNullOrEmpty(label))
			{
				return label;
			}
			obj = "自动选择";
		}
		return (string)obj;
	}

	private void SyncBanner()
	{
		string version = _ver.Str();
		string instance = _inst.Str();
		InstanceInfo? instanceInfo = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance);
		if (instanceInfo != null && !string.IsNullOrEmpty(instanceInfo.Pack))
		{
			List<string> list = new List<string>();
			if (!string.IsNullOrEmpty(instanceInfo.PackVersion))
			{
				list.Add(instanceInfo.PackVersion);
			}
			if (!string.IsNullOrEmpty(instanceInfo.McVersion))
			{
				list.Add("Minecraft " + instanceInfo.McVersion);
			}
			list.Add(I18n.L("实例 ") + instance);
			_bTitle.Text = instanceInfo.Pack;
			_bSub.Text = string.Join(" · ", list);
		}
		else
		{
			_bTitle.Text = (string.IsNullOrEmpty(version) ? I18n.L("未选择版本") : version);
			_bSub.Text = (string.IsNullOrEmpty(version) ? I18n.L("先到「下载」安装一个版本") : I18n.L("实例 {0} · 点「启动游戏」进入世界", instance));
		}
		if (!string.IsNullOrEmpty(instance))
		{
			PageBase.Run(() => CountModsAsync(instance, version ?? ""));
		}
		SyncHero();
		RefreshBackground();
	}

	// 启动屏大标题 / 状态行 / 小标签;数据全来自现有字段,缺什么省什么
	private void SyncHero()
	{
		string instance = _inst.Str();
		InstanceInfo? info = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance);
		_heroTitle.Text = (string.IsNullOrEmpty(instance) ? I18n.L("选择实例") : instance);
		string dir = info?.Path ?? "";
		_kickerText.Text = I18n.L("就绪") + (string.IsNullOrEmpty(dir) ? "" : (" · " + dir));
		// 小标签:加载器版本 / Java / 内存 / 模组数(「Java 17 (...)」「jdk1.8.0_301」→ Java 17 / Java 8)
		_chipRow.Children.Clear();
		string ver = _ver.Str();
		// 主按钮内嵌版本全名(fabric-loader-0.20.1-26.3 / 1.20.1);小标签首位显示加载器名+版本
		_goVer.Text = ver;
		bool hasVer = !string.IsNullOrEmpty(ver);
		_goVer.IsVisible = hasVer;
		_goSep.IsVisible = hasVer;
		if (hasVer)
		{
			// 加载器名+版本:与后端 LOADER_TAGS 同一套前缀(fabric-loader-x.y.z / forge-x / neoforge-x / optifine-x)
			_chipRow.Children.Add(Chip(LoaderChip(ver)));
		}
		string java = _java.Str();
		if (!string.IsNullOrEmpty(java))
		{
			Match m = Regex.Match(java, "(?:java|jdk|jre)\\D{0,3}(?:1\\.)?(\\d+)", RegexOptions.IgnoreCase);
			_chipRow.Children.Add(Chip(java.StartsWith("自动", StringComparison.Ordinal) ? I18n.L("Java 自动") : (m.Success ? ("Java " + m.Groups[1].Value) : java)));
		}
		_chipRow.Children.Add(Chip($"{_mem.Value / 1024.0:0.#} GB"));
		if (_modCount > 0)
		{
			_chipRow.Children.Add(Chip(I18n.L("{0} 个模组", _modCount)));
		}
	}

	private async Task LoadNewsAsync()
	{
		_newsHost.Children.Clear();
		_newsHost.Children.Add(Ui.Skeleton(14.0));
		_newsHost.Children.Add(Ui.Skeleton(12.0, 180.0));
		string text = await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "homepage_mode",
			@default = "news",
		}, "news");
		if (text == "blank")
		{
			_newsHost.Children.Clear();
			_newsHost.Children.Add(Ui.Muted(I18n.L("主页已设为空白")));
			_newsTicker.Text = I18n.L("主页已设为空白");
			return;
		}
		if (text == "custom")
		{
			string path = await PageBase.Api.TryCallAsync("get_setting", new
			{
				key = "custom_homepage",
				@default = "",
			}, "");
			_newsHost.Children.Clear();
			try
			{
				string text2 = ((string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ? I18n.L("未设置自定义主页。到设置 → 启动页主页 填本地 HTML 路径。") : (await Task.Run(() => File.ReadAllText(path))));
				string text3 = text2;
				_newsHost.Children.Add(Ui.Muted((text3.Length > 2000) ? (text3.Substring(0, 2000) + "...") : text3));
				_newsTicker.Text = (text3.Length > 60) ? (text3.Substring(0, 60) + "...") : text3;
				return;
			}
			catch (Exception ex)
			{
				_newsHost.Children.Add(Ui.Muted(I18n.L("读不了自定义主页：") + ex.Message));
				_newsTicker.Text = I18n.L("读不了自定义主页");
				return;
			}
		}
		List<NewsRow> rows = (await PageBase.Api.TryCallAsync("cached_news", null, new List<NewsRow>())) ?? new List<NewsRow>();
		FillNews(rows);
		List<NewsRow>? list = await PageBase.Api.TryCallAsync<List<NewsRow>>("fetch_news");
		if (list != null && list.Count > 0)
		{
			FillNews(list);
		}
	}

	private void FillNews(List<NewsRow> rows)
	{
		_newsHost.Children.Clear();
		if (rows.Count == 0)
		{
			_newsHost.Children.Add(Ui.Muted(I18n.L("暂无新闻")));
			_newsTicker.Text = I18n.L("暂无新闻");
			return;
		}
		foreach (NewsRow item in rows.Take(6))
		{
			_newsHost.Children.Add(Ui.Txt(item.Title, 12.5, bold: true).Wrap());
			if (!string.IsNullOrEmpty(item.Body))
			{
				_newsHost.Children.Add(Ui.Small(item.Body).Wrap());
			}
		}
		_newsTicker.Text = rows[0].Title;
		Motion.Stagger(_newsHost, 20.0, 200.0, 8.0);
	}

	private async Task LoadPlaytimeAsync()
	{
		long seconds = await PageBase.Api.TryCallAsync("get_total_playtime", null, 0L);
		_playTotal.Text = Fmt.Duration(seconds);
	}

	private void SyncTaskCard()
	{
		_taskHost.Children.Clear();
		List<TaskRow> list = TaskStore.Rows.Reverse().Take(5).ToList();
		if (list.Count == 0)
		{
			_taskHost.Children.Add(Ui.Muted(I18n.L("暂无下载任务")));
			return;
		}
		foreach (TaskRow item in list)
		{
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.Txt(item.Title, 12.0).Trim(), 0);
			grid.Add(Ui.Small(item.Finished ? (item.Success ? I18n.L("完成") : I18n.L("失败")) : $"{item.Progress:0}%").M(8.0, 0.0, 0.0, 0.0), 0, 1);
			_taskHost.Children.Add(grid);
		}
	}

	private async Task<string?> EnsureLaunchAccountAsync()
	{
		string? account = await AccountGate.EnsureAsync(_acc.Str());
		if (account != null && _acc.Str() != account)
		{
			_syncing = true;
			try
			{
				_acc.Fill(await AccountGate.ListAsync(), account);
			}
			finally
			{
				_syncing = false;
			}
		}
		return account;
	}

	private async Task LaunchAsync()
	{
		string version = _ver.Str();
		if (string.IsNullOrEmpty(version))
		{
			// 显示为空可能是过期状态(如刚从实例库带版本回来)——重拉一次再判
			await ReloadVersionsAsync();
			version = _ver.Str();
		}
		if (string.IsNullOrEmpty(version))
		{
			PageBase.Toast(I18n.L("没有版本"), I18n.L("请先到「下载」安装"), ToastKind.Warning);
			PageBase.Win?.Navigate("download");
			return;
		}
		string? account = await EnsureLaunchAccountAsync();
		if (account == null)
		{
			return;
		}
		string instance = _inst.Str();
		int memory = (int)_mem.Value;
		string java = SelectedJava();
		if (!(await CheckMultiInstanceAsync()))
		{
			return;
		}
		PreflightResult? preflightResult = await PageBase.Api.TryCallAsync<PreflightResult>("preflight_launch", new
		{
			instance,
			version,
			memory_mb = memory,
			java,
		});
		bool force = false;
		if (preflightResult == null)
		{
			if (!(await Dlg.Confirm(I18n.L("启动预检不可用"), I18n.L("preflight_launch 调用失败,无法检查 Java / 内存 / 文件完整性。\n跳过预检直接启动?"), I18n.L("继续启动"), I18n.L("取消"))))
			{
				return;
			}
		}
		else
		{
			List<PreflightItem> list = preflightResult.Items.Where((PreflightItem i) => i.Level == "error").ToList();
			List<PreflightItem> list2 = preflightResult.Items.Where((PreflightItem i) => i.Level == "warn").ToList();
			if (list.Count > 0)
			{
				string text = string.Join("\n\n", list.Select((PreflightItem i) => "· " + i.Title + "\n" + i.Detail));
				if (list2.Count > 0)
				{
					text = text + "\n\n" + string.Join("\n\n", list2.Select((PreflightItem i) => "· " + i.Title + "\n" + i.Detail));
				}
				text = text + "\n\n" + I18n.L("这些问题可能导致启动失败。仍要强制启动？");
				if (!(await Dlg.Confirm(I18n.L("启动预检未通过"), text, I18n.L("仍要启动"))))
				{
					return;
				}
				force = true;
			}
			else if (list2.Count > 0)
			{
				string body = string.Join("\n\n", list2.Select((PreflightItem i) => "· " + i.Title + "\n" + i.Detail)) + I18n.L("\n\n仍要继续启动?");
				if (!(await Dlg.Confirm(I18n.L("启动预检有警告"), body, I18n.L("继续启动"))))
				{
					return;
				}
			}
		}
		_logLines.Clear();
		_log.Clear();
		// WPF 这里还有一行 _prog.BeginAnimation(RangeBase.ValueProperty, null)(把进度条的旧动画清掉);
		// Avalonia 没有 BeginAnimation,进度动画统一由 Motion.Progress 管(C1c 的门面),所以去掉。
		_prog.Value = 0.0;
		_prog.IsVisible = true;
		_progGen++;
		_status.Text = I18n.L("准备启动...");
		_launchBtn.IsEnabled = false;
		_stopBtn.IsEnabled = true;
		_launchShell!.IsVisible = false;
		_stopShell.IsVisible = true;
		_crashShown = false;
		Motion.Pulse(_launchBtn);
		if (PageBase.Win != null)
		{
			PageBase.Win.Prefs.LastUser = account;
			PageBase.Win.SaveUiPrefs();
		}
		try
		{
			_taskId = await PageBase.Api.StartTaskAsync("launch_game", new
			{
				instance,
				version,
				account,
				username = account,
				memory_mb = memory,
				width = Int(_wBox.Text, 854),
				height = Int(_hBox.Text, 480),
				java,
				extra_game_args = ExtraArgs(),
				force,
			});
		}
		catch (Exception ex)
		{
			_launchBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			_launchShell!.IsVisible = true;
			_stopShell.IsVisible = false;
			PageBase.Toast(I18n.L("启动失败"), ex.Message, ToastKind.Error);
		}
	}

	private async Task<bool> CheckMultiInstanceAsync()
	{
		if (!(await PageBase.Api.TryCallAsync("is_game_running", null, fallback: false)))
		{
			return true;
		}
		if (await PageBase.Api.TryCallAsync("allow_multi_instance", null, fallback: false))
		{
			return true;
		}
		if (await Dlg.Choose(I18n.L("游戏已经在运行"), Ui.Muted(I18n.L("当前设置不允许多开。可以先关掉正在跑的那个，或者打开多开后再启动一个。")).Wrap().MinW(360.0), new string[2]
		{
			I18n.L("取消"),
			I18n.L("打开多开并启动"),
		}, 520.0) != 1)
		{
			return false;
		}
		await PageBase.Api.CallAsync<object>("set_multi_instance", new
		{
			allow = true,
		});
		PageBase.Toast(I18n.L("已允许多开"), I18n.L("可以在设置页再关掉"), ToastKind.Success);
		return true;
	}

	private static int Int(string? s, int fallback)
	{
		if (!int.TryParse(s?.Trim(), out var result) || result <= 0)
		{
			return fallback;
		}
		return result;
	}

	private string[]? ExtraArgs()
	{
		string? text = _server.Text?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		int num = text.LastIndexOf(':');
		if (num > 0)
		{
			return new string[4]
			{
				"--server",
				text.Substring(0, num),
				"--port",
				text.Substring(num + 1),
			};
		}
		return new string[4] { "--server", text, "--port", "25565" };
	}

	private async Task StopAsync()
	{
		if (_taskId != null)
		{
			await PageBase.Api.TryCallAsync<object>("cancel_task", new
			{
				task_id = _taskId,
			});
		}
	}

	private Control BuildConfig()
	{
		SPanel control = Ui.H(6.0, _wBox, Ui.Txt("×").VCenter(), _hBox);
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(_mem.VCenter(), 0);
		grid.Add(_memLbl.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Button button = Ui.Btn(I18n.L("微软登录"), BtnKind.Soft, delegate
		{
			PageBase.Run(MicrosoftLoginAsync);
		}, "\ue77b");
		Button button2 = Ui.Btn(I18n.L("皮肤站"), BtnKind.Chip, delegate
		{
			PageBase.Run(AuthlibLoginAsync);
		});
		Button button5 = Ui.IconBtn("\ue712", I18n.L("更多操作"), delegate(object? s, RoutedEventArgs _)
		{
			ShowMoreMenu((Control)s!);
		});
		// 常用的只有实例/版本/用户;Java、内存、分辨率、直连收进「更多设置」,默认折叠
		SPanel advanced = Ui.V(9.0, Field("Java", _java), Field(I18n.L("内存"), grid), Field(I18n.L("分辨率"), control), Field(I18n.L("直连"), _server));
		advanced.IsVisible = false;
		Button toggle = Ui.Btn(I18n.L("更多设置"), BtnKind.Ghost, null, "\ue70d");
		toggle.HorizontalAlignment = HorizontalAlignment.Left;
		toggle.Click += delegate
		{
			bool open = !advanced.IsVisible;
			advanced.IsVisible = open;
			toggle.Content = Ui.H(6.0, Ui.Glyph(open ? "\ue70e" : "\ue70d", 12.0), Ui.Txt(open ? I18n.L("收起") : I18n.L("更多设置")));
		};
		return Ui.Scroll(Ui.V(9.0, Field(I18n.L("实例"), _inst), Field(I18n.L("版本"), _ver), Field(I18n.L("用户"), _acc), toggle, advanced, Ui.Sep().M(0.0, 4.0, 0.0, 2.0), Ui.H(7.0, button, button2, button5)));
	}

	private static Control Field(string label, Control control)
	{
		Grid grid = Ui.G(null, "58,*");
		grid.Add(Ui.Txt(label, 12.0, bold: false, "B.InkMuted").VCenter(), 0);
		// WPF 是 control.SetValue(FrameworkElement.VerticalAlignmentProperty, Center);Avalonia 直接写 CLR 属性。
		control.VerticalAlignment = VerticalAlignment.Center;
		grid.Add(control, 0, 1);
		return grid;
	}

	/// <summary>
	/// 【Windows 源】<c>LaunchPage.cs:1526-1556</c>:「更多操作」的上下文菜单(7 项,顺序照源码)。
	/// 成员可访问性由 private 降为 internal:测试要不模拟输入设备地点开它(同 D-115 的口径)。
	/// </summary>
	internal void ShowMoreMenu(Control anchor)
	{
		ContextMenu menu = new ContextMenu();
		_moreMenu = menu;
		Item(I18n.L("版本设置"), VersionSettingsAsync);
		Item(I18n.L("刷新新闻"), LoadNewsAsync);
		Item(I18n.L("查看启动命令"), ShowLaunchCommandAsync);
		Item(I18n.L("导出启动脚本 (.bat)"), ExportScriptAsync);
		Item(I18n.L("创建桌面快捷方式"), ShortcutAsync);
		Item(I18n.L("修复当前版本"), RepairAsync);
		Item(I18n.L("打开实例文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_folder", new
			{
				name = _inst.Str(),
			});
		});
		menu.PlacementTarget = anchor;
		// WPF 是 menu.IsOpen = true;Avalonia 的 MenuBase.IsOpen 的 setter 不可见,等价入口是 ContextMenu.Open。
		// **必须把锚点传给 Open(anchor)**:无参的 Open() 会去解析 PlacementTarget 的宿主,离屏/测试里解析不到
		// 就抛 ArgumentNullException("control")(本机实测,页面用例抓到的真 bug,见 PROGRESS 的 C3c 小节)。
		menu.Open(anchor);
		void Item(string head, Func<Task> act)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = head,
			};
			menuItem.Click += delegate
			{
				PageBase.Run(act);
			};
			menu.Items.Add(menuItem);
		}
	}

	private async Task MicrosoftLoginAsync()
	{
		if (_loginLayer != null)
		{
			return;
		}
		_loginHint = Ui.Muted(I18n.L("正在获取登录代码..."));
		_loginCode = new TextBlock
		{
			Text = "------",
			FontSize = 26.0,
			FontWeight = FontWeight.Bold,
		};
		_loginCode.Dyn(TextBlock.ForegroundProperty, "B.AccentText");
		Button button = Ui.Btn(I18n.L("复制代码"), BtnKind.Chip, delegate
		{
			// WPF 是 Clipboard.SetText + Toast,外面一个空 catch;mac 走 C3b 的剪贴板唯一入口
			// (失败返回 false = 不提示,与"抛了就当没发生"等价)。
			PageBase.Run(async delegate
			{
				if (await CrashUi.CopyAsync(_loginCode, _loginCode?.Text ?? ""))
				{
					PageBase.Toast(I18n.L("已复制"), I18n.L("代码已放进剪贴板"), ToastKind.Success);
				}
			});
		}, "\ue8c8");
		Button button2 = Ui.Btn(I18n.L("打开浏览器"), BtnKind.Primary, delegate
		{
			if (!string.IsNullOrEmpty(_loginUri))
			{
				Ui.OpenUrl(_loginUri);
			}
		}, "\ue71b");
		SPanel body = Ui.V(10.0, _loginHint, _loginCode, Ui.H(8.0, button, button2));
		_loginLayer = Dlg.Panel(I18n.L("微软账号登录"), body, 460.0, delegate
		{
			_loginLayer = null;
		}, dismissable: false);
		_loginTask = await PageBase.Api.StartTaskAsync("start_microsoft_login");
	}

	private async Task AuthlibLoginAsync()
	{
		List<AuthlibPreset> presets = (await PageBase.Api.TryCallAsync("authlib_presets", null, new List<AuthlibPreset>())) ?? new List<AuthlibPreset>();
		TextBox api = Ui.Input("https://littleskin.cn/api/yggdrasil", presets.FirstOrDefault()?.Api ?? "");
		TextBox user = Ui.Input(I18n.L("邮箱 / 用户名"));
		// Avalonia 11 没有 PasswordBox:Ui.Pw() 给的是带 PasswordChar 的 TextBox(见 D-75),
		// 取值由 Windows 的 pw.Password 变成 pw.Text。
		TextBox pw = Ui.Pw();
		ComboBox pick = Ui.Combo(presets.Select((AuthlibPreset p) => p.Name));
		pick.SelectionChanged += delegate
		{
			AuthlibPreset? authlibPreset = presets.FirstOrDefault((AuthlibPreset p) => p.Name == pick.Str());
			if (authlibPreset != null)
			{
				api.Text = authlibPreset.Api;
			}
		};
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("选择皮肤站，或直接填 Yggdrasil API")), pick, api, user, pw);
		if (await Dlg.Ask(I18n.L("皮肤站登录"), body, I18n.L("登录")))
		{
			await PageBase.Api.StartTaskAsync("start_authlib_login", new
			{
				api = (api.Text?.Trim() ?? ""),
				username = (user.Text?.Trim() ?? ""),
				password = (pw.Text ?? ""),
			});
		}
	}

	private async Task VersionSettingsAsync()
	{
		string instance = _inst.Str();
		string text = _ver.Str();
		if (string.IsNullOrEmpty(text))
		{
			PageBase.Toast(I18n.L("未选择版本"), I18n.L("先安装并选中一个版本"), ToastKind.Warning);
		}
		else
		{
			await VersionSetupDialog.ShowAsync(instance, text);
		}
	}

	private async Task ShowLaunchCommandAsync()
	{
		string ver = _ver.Str();
		if (string.IsNullOrEmpty(ver))
		{
			return;
		}
		string? account = await EnsureLaunchAccountAsync();
		if (account == null)
		{
			return;
		}
		using (Dlg.Busy(I18n.L("正在生成启动命令...")))
		{
			string? cmd;
			try
			{
				BridgeClient api = PageBase.Api;
				string instance = _inst.Str();
				List<string>? list = await api.CallAsync<List<string>>("build_launch_command", new
				{
					instance,
					version = ver,
					account,
					username = account,
					memory_mb = (int)_mem.Value,
					width = Int(_wBox.Text, 854),
					height = Int(_hBox.Text, 480),
					java = SelectedJava(),
				});
				cmd = ((list != null && list.Count > 0) ? string.Join(" ", list) : null);
			}
			catch (BridgeCallException)
			{
				cmd = await PageBase.Api.CallAsync<string>("get_launch_command", new
				{
					instance = _inst.Str(),
					version = ver,
					account,
					username = account,
					memory_mb = (int)_mem.Value,
				});
			}
			await Dlg.Alert(I18n.L("启动命令"), cmd ?? "");
		}
	}

	private async Task ExportScriptAsync()
	{
		string text = _ver.Str();
		if (!string.IsNullOrEmpty(text))
		{
			string? text2 = Dlg.SaveFile(I18n.L("批处理 (*.bat)|*.bat"), "launch-" + _inst.Str() + "-" + text + ".bat", I18n.L("导出启动脚本"));
			if (text2 != null)
			{
				await PageBase.Api.StartTaskAsync("export_launch_script", new
				{
					instance = _inst.Str(),
					version = text,
					dest = text2,
				});
			}
		}
	}

	private async Task ShortcutAsync()
	{
		string text = _ver.Str();
		string? account = (string.IsNullOrEmpty(text) ? null : (await EnsureLaunchAccountAsync()));
		if (account != null)
		{
			string text4 = await PageBase.Api.CallAsync<string>("create_desktop_shortcut", new
			{
				instance = _inst.Str(),
				version = text,
				username = account,
				account,
			});
			PageBase.Toast(I18n.L("已创建快捷方式"), text4 ?? "", ToastKind.Success);
		}
	}

	private async Task RepairAsync()
	{
		string ver = _ver.Str();
		bool flag = !string.IsNullOrEmpty(ver);
		if (flag)
		{
			flag = await Dlg.Confirm(I18n.L("修复版本"), I18n.L("重新校验并补齐「{0}」的依赖库与资源？", ver));
		}
		if (flag)
		{
			await PageBase.Api.StartTaskAsync("repair_version", new
			{
				instance = _inst.Str(),
				version = ver,
			});
			// 【Windows 源】Win?.FlyToTasks(_launchBtn, I18n.L("修复中")) —— C3c 时 mac 的外壳没有这个成员,
			// 走注入缝、没接挂点就不播(那条用户可见的降级,见类注释与 D-121);
			// **C3d 起外壳的构造函数把这个缝接到了真成员上**,所以这一句等于 Windows 的那一句。
			FlyToTasksSeam?.Invoke(_launchBtn, I18n.L("修复中"));
		}
	}

	public override void OnEvent(BridgeEvent ev)
	{
		switch (ev.Event)
		{
		case "login_code":
			if (_loginLayer != null)
			{
				_loginUri = ev.Uri;
				if (_loginCode != null)
				{
					_loginCode.Text = ev.Code;
				}
				if (_loginHint != null)
				{
					_loginHint.Text = I18n.L("在浏览器打开下面的地址并输入代码:\n") + ev.Uri;
				}
			}
			break;
		case "login_status":
			if (_loginHint != null)
			{
				_loginHint.Text = ev.Text;
			}
			break;
		case "finished":
			if (!(ev.TaskId != _taskId))
			{
				break;
			}
			goto case "task_added";
		case "progress":
			if (!(ev.TaskId != _taskId))
			{
				break;
			}
			goto case "task_added";
		case "task_added":
			SyncTaskCard();
			break;
		}
		if (ev.Event == "finished" && ev.TaskId == _loginTask)
		{
			if (ev.Success)
			{
				_loginLayer?.Close();
				_loginLayer = null;
				PageBase.Run(FillCardData);
			}
			else if (_loginHint != null)
			{
				_loginHint.Text = ev.Message;
			}
		}
		if (ev.Event == "crash" && (string.IsNullOrEmpty(ev.TaskId) || ev.TaskId == _taskId))
		{
			_crashShown = true;
			CrashReport report = ev.Crash ?? new CrashReport
			{
				Title = ev.Title,
				Detail = ev.Message,
				TaskId = ev.TaskId,
			};
			PageBase.Run(() => HandleCrashAsync(report, gameError: true));
		}
		if (_taskId != null)
		{
			string text = ev.Event;
			if (!(text == "game_started"))
			{
				if (text == "game_exited")
				{
					int? num = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("code", out JsonElement value) && value.ValueKind == JsonValueKind.Number) ? new int?(value.GetInt32()) : ((int?)null));
					_status.Text = ((!num.HasValue) ? I18n.L("游戏已退出") : I18n.L("游戏已退出（退出码 {0}）", num));
					HideProgressSoon(0);
				}
			}
			else
			{
				_status.Text = I18n.L("游戏进程已启动,正在加载世界...");
				Motion.Progress(_prog, Math.Max(_prog.Value, 92.0));
			}
		}
		if (ev.TaskId != _taskId)
		{
			return;
		}
		switch (ev.Event)
		{
		case "progress":
		{
			Motion.Progress(_prog, (ev.Total > 0) ? ((double)ev.Current * 100.0 / (double)ev.Total) : 0.0);
			Fmt.SplitMsg(ev.Message, out string status, out string speed);
			_status.Text = (string.IsNullOrEmpty(status) ? I18n.L("处理中...") : status) + (string.IsNullOrEmpty(speed) ? "" : ("    " + speed));
			break;
		}
		case "log":
			AppendLog(ev.Text);
			break;
		case "finished":
			_launchBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			_launchShell!.IsVisible = true;
			_stopShell.IsVisible = false;
			_status.Text = ((!string.IsNullOrEmpty(ev.Message)) ? ev.Message : (ev.Success ? I18n.L("已完成") : I18n.L("✘ 失败")));
			if (ev.Success)
			{
				Motion.Progress(_prog, 100.0);
			}
			HideProgressSoon(ev.Success ? 1200 : 0);
			if (!ev.Success && !_crashShown && ev.Message != "已取消")
			{
				_crashShown = true;
				PageBase.Run(() => HandleCrashAsync(new CrashReport
				{
					Title = I18n.L("启动失败"),
					Headline = ev.Message,
					Detail = ev.Message,
					Instance = _inst.Str(),
					Version = _ver.Str(),
				}, gameError: true));
			}
			_taskId = null;
			_ = LoadPlaytimeAsync();
			break;
		}
	}

	private async Task HandleCrashAsync(CrashReport report, bool gameError = false)
	{
		if (await CrashUi.ShowAsync(report, gameError))
		{
			await LaunchAsync();
		}
	}

	private void AppendLog(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			_logLines.Add(text);
			if (_logLines.Count > 2500)
			{
				_logLines.RemoveRange(0, _logLines.Count - 2500);
			}
			// 【Windows 源】bool atBottom = _log.VerticalOffset >= _log.ExtentHeight - _log.ViewportHeight - 24.0;
			// Avalonia 的 TextBox 没有这三个属性,等价物是它模板里那根 ScrollViewer;取不到(还没套模板)就不滚。
			ScrollViewer? scroller = LogScroller();
			bool atBottom = scroller != null && scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 24.0;
			_log.Text = string.Join("\n", _logLines);
			if (atBottom)
			{
				scroller!.ScrollToEnd();
			}
		}
	}

	/// <summary>日志框内部的滚动条(Avalonia 的 TextBox 把滚动交给模板里的 ScrollViewer)。</summary>
	private ScrollViewer? LogScroller()
	{
		return _log.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
	}

	public override void OnShown()
	{
		SyncTaskCard();
		SyncHero();
		_dash.RefreshThemeVisuals();
		// 主题可能已切换(主色柔光重取);刚玩完可能有新截图
		ApplyHeroTone(_onImage);
		RefreshBackground();
		// 页面隐藏期间收不到 ui_changed 刷新(如实例库改了默认实例/版本)——显示时补拉
		if (!string.IsNullOrEmpty(_inst.Str()))
		{
			PageBase.Run(async delegate
			{
				await ReloadVersionsAsync();
				SyncBanner();
			});
		}
	}

	// ————— 测试用的只读快照(不改任何行为;口径同 C3a 的 OrderNow / ViewsNow) —————

	/// <summary>背景图与遮罩此刻可不可见 = <c>_onImage</c>(ApplyHeroTone 的效果)。</summary>
	internal bool OnImageNow => _onImage;

	/// <summary>背景图片源(没有解码出来时为 null)。</summary>
	internal IImage? BackgroundImageNow => _bgImage.Source;

	/// <summary>内嵌 hero.jpg 的解码入口(测试用来钉"资源确实嵌进来了")。</summary>
	internal static Bitmap? HeroSourceForTests()
	{
		return HeroSource();
	}

	/// <summary>旧卡片画布(DashHost):Windows 也不挂可视树,所以只能经这个成员拿到它。</summary>
	internal DashHost DashNow => _dash;

	/// <summary>页面自建的内容区块宿主。</summary>
	internal BlockHost? BlocksNow => _blocks;

	/// <summary>英雄区竖排容器 / 主按钮外壳 / 配置钮 / 新闻卡 / 配置面板(都要么不在可视树的可达路径上,
	/// 要么在动态资源解析前读不到颜色,所以留只读入口)。</summary>
	internal SPanel? HeroNow => _hero;

	internal Border? LaunchShellNow => _launchShell;

	internal Border? ConfigPillNow => _configPill;

	internal Border? NewsCardNow => _newsCard;

	internal Control? ConfigBodyNow => _configBody;

	/// <summary>英雄区旁边那几个玻璃块(配置钮 / 联机钮 / 新闻卡)—— 用来钉 tone 的批量刷新。</summary>
	internal IReadOnlyList<Border> GlassNow => _glass.ToArray();

	/// <summary>英雄区大标题 / 小标签行 / 状态行文案(测试按 <see cref="I18n.L"/> 的键断言)。</summary>
	internal string HeroTitleNow => _heroTitle.Text ?? "";

	internal string StatusTextNow => _status.Text ?? "";

	internal string NewsTickerNow => _newsTicker.Text ?? "";

	internal string BannerTitleNow => _bTitle.Text ?? "";

	internal string BannerSubNow => _bSub.Text ?? "";

	/// <summary>小标签行里的文案(按可视顺序)。</summary>
	internal List<string> ChipsNow => _chipRow.Children.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text ?? "").ToList();

	/// <summary>主操作行此刻显示的是「启动游戏」外壳还是「停止」外壳(运行态就是后者)。</summary>
	internal bool StopShownNow => _stopShell.IsVisible;

	internal bool LaunchShownNow => _launchShell?.IsVisible ?? false;

	/// <summary>进度条此刻可不可见 / 它的值(闲置状态两条都在状态机里用得上)。</summary>
	internal bool ProgressShownNow => _prog.IsVisible;

	internal double ProgressValueNow => _prog.Value;

	/// <summary>内存保存用的防抖计时器有没有在跑(泄漏检查用)。</summary>
	internal bool MemorySaveTimerRunningNow => _memSaveTimer.IsEnabled;

	/// <summary>后端给的任务号(mac 侧没有别的办法从外面看"任务有没有在跑")。</summary>
	internal string? TaskIdNow => _taskId;

	/// <summary>日志框里此刻的文本 / 行缓冲(测试驱动 AppendLog 的路径)。</summary>
	internal string LogTextNow => _log.Text ?? "";

	internal int LogLineCountNow => _logLines.Count;

	/// <summary>此刻后端调用有没有在飞(仅用于断言"没有多余请求"的用例)。</summary>
	internal int JavaOptionCountNow => _javaOpts.Count;

	// ————— 下面这一批是"控件在隐藏块 / 懒建浮层里"时的只读入口(不改任何行为;C3a 的 OrderNow 口径) —————

	/// <summary>启动游戏 / 停止两个主按钮。</summary>
	internal Button LaunchButtonNow => _launchBtn;

	internal Button StopButtonNow => _stopBtn;

	internal ProgressBar ProgressBarNow => _prog;

	/// <summary>四个下拉框,按下标:实例 / 版本 / 用户(账号)/ Java。</summary>
	internal ComboBox[] CombosNow => new[] { _inst, _ver, _acc, _java };

	/// <summary>内存滑杆与它右边那个「4096 MB」标签。</summary>
	internal Slider MemorySliderNow => _mem;

	internal TextBlock MemoryLabelNow => _memLbl;

	/// <summary>分辨率两个输入框(占位分别是 854 / 480)。</summary>
	internal TextBox WidthBoxNow => _wBox;

	internal TextBox HeightBoxNow => _hBox;

	internal TextBox ServerBoxNow => _server;

	internal TextBox UserBoxNow => _user;

	/// <summary>四个默认收起区块里的控件(不在可视树的可达路径上,所以给只读入口)。</summary>
	internal SPanel TaskHostNow => _taskHost;

	internal TextBlock PlaytimeNow => _playTotal;

	internal TextBox NotesNow => _notes;

	internal SPanel NewsHostNow => _newsHost;

	/// <summary>声明式驱动的入口(不模拟输入设备):模拟「点了某个非按钮控件」的那一段处理器体。</summary>
	internal void PickInstanceForTest(string name)
	{
		PickInstanceFromSwitch(name);
	}

	internal void PickVersionForTest(string version)
	{
		PickVersionFromSwitch(version);
	}

	/// <summary>
	/// 最近一次弹出的「更多操作」上下文菜单(Windows 里它是个局部变量,弹完就没引用了)。
	/// mac 侧留一个字段只为了能被告知式地验它(菜单项顺序 / 点某一项走哪条路)。
	/// </summary>
	private ContextMenu? _moreMenu;

	/// <summary>最近一次弹出的「更多操作」菜单(没弹过时为 null)。</summary>
	internal ContextMenu? MoreMenuNow => _moreMenu;
}
