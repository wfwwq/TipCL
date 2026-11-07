using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 启动屏 v2：左对齐大实例标题 + 小标签 +「启动游戏」/配置/联机 + 底部新闻卡；有截图时以最新截图作背景。
// 旧卡片画布（DashHost/DashLayout）不再挂可视树，字段与数据逻辑全部保留。
public sealed class LaunchPage : PageBase
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
		FontWeight = FontWeights.SemiBold
	};

	private readonly TextBlock _bSub = new TextBlock
	{
		FontSize = 12.0,
		Opacity = 0.82
	};

	private readonly SPanel _newsHost = Ui.V(6.0);

	private readonly SPanel _taskHost = Ui.V(6.0);

	private readonly TextBlock _playTotal = new TextBlock
	{
		FontSize = 20.0,
		FontWeight = FontWeights.SemiBold
	};

	private readonly TextBox _notes = Ui.Multi(I18n.L("随手记点什么…"));

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
		Interval = TimeSpan.FromMilliseconds(650.0)
	};

	private bool _layoutLoaded;

	// —— 启动屏 UI（v2：左对齐 + 截图背景；配色随有无截图在 ApplyHeroTone 切换） ——
	private readonly TextBlock _kickerText = new TextBlock
	{
		FontSize = 12.5,
		MaxWidth = 560.0,
		TextTrimming = TextTrimming.CharacterEllipsis,
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly TextBlock _heroTitle = new TextBlock
	{
		FontSize = 34.0,
		FontWeight = FontWeights.ExtraBold,
		MaxWidth = 620.0,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	// 主操作行外壳：运行中隐藏、显示同尺寸「停止」，保证该行始终只有一个 48 高主按钮
	private Border? _launchShell;

	private readonly TextBlock _newsTicker = new TextBlock
	{
		FontSize = 12.5,
		MaxWidth = 520.0,
		TextTrimming = TextTrimming.CharacterEllipsis,
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly Border _stopShell = new Border
	{
		CornerRadius = new CornerRadius(12.0),
		Height = 48.0,
		Padding = new Thickness(26.0, 0.0, 26.0, 0.0),
		Visibility = Visibility.Collapsed
	};

	private FrameworkElement? _configBody;

	// 48×48 启动配置图标钮（配置浮层锚点）
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
		Visibility = Visibility.Collapsed
	};

	private readonly Grid _bgShade = new Grid
	{
		IsHitTestVisible = false,
		Visibility = Visibility.Collapsed
	};

	private readonly SPanel _chipRow = Ui.H(6.0);

	// 随背景切换配色的半透明块（配置钮 / 联机钮 / 新闻卡）
	private readonly List<Border> _glass = new List<Border>();

	private Border? _newsCard;

	private readonly System.Windows.Media.Effects.DropShadowEffect _launchGlow = new System.Windows.Media.Effects.DropShadowEffect
	{
		BlurRadius = 20.0,
		ShadowDepth = 6.0,
		Direction = 270.0
	};

	private readonly System.Windows.Media.Effects.DropShadowEffect _titleShadow = new System.Windows.Media.Effects.DropShadowEffect
	{
		BlurRadius = 12.0,
		ShadowDepth = 2.0,
		Direction = 270.0,
		Opacity = 0.3
	};

	private bool _onImage;

	private string _bgKey = "";

	private int _bgGen;

#if PREMIUM
	// premium（WP-G）：嵌入 hero.jpg 只解码一次；_immersive = 当前处于标题栏沉浸态（背景已搬进 WallHost）
	private static System.Windows.Media.Imaging.BitmapSource? _heroCache;

	private bool _immersive;
#endif

	private int _modCount = -1;

	private string _modKey = "";
	public override string Title => I18n.L("启动");

	private async void HideProgressSoon(int delayMs)
	{
		int gen = _progGen;
		if (delayMs > 0)
		{
			await Task.Delay(delayMs);
		}
		if (gen == _progGen)
		{
			_prog.Visibility = Visibility.Collapsed;
		}
	}

	public LaunchPage()
	{
		_bTitle.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
		_bSub.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
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
		_mem.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
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
			if (!_syncing)
			{
				PageBase.Run(SaveDisplayPrefsAsync);
			}
		};
		_hBox.LostFocus += delegate
		{
			if (!_syncing)
			{
				PageBase.Run(SaveDisplayPrefsAsync);
			}
		};
		_notes.LostFocus += delegate
		{
			if (PageBase.Win != null)
			{
				PageBase.Win.Prefs.Notes = _notes.Text;
				PageBase.Win.SaveUiPrefs();
			}
		};
		BuildHero();
	}

	// ————— 启动屏布局（v2） —————

	private static SolidColorBrush White(byte alpha)
	{
		SolidColorBrush b = new SolidColorBrush(Color.FromArgb(alpha, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		b.Freeze();
		return b;
	}

	private static readonly SolidColorBrush W100 = White(byte.MaxValue);

	private static readonly SolidColorBrush W80 = White(204);

	private static readonly SolidColorBrush W14 = White(36);

	private static readonly SolidColorBrush W22 = White(56);

	// 截图遮罩：rgba(10,14,22,a) 线性渐变；stops 依次为 alpha, offset
	private static Rectangle Shade(Point from, Point to, params double[] stops)
	{
		LinearGradientBrush g = new LinearGradientBrush
		{
			StartPoint = from,
			EndPoint = to
		};
		for (int i = 0; i + 1 < stops.Length; i += 2)
		{
			g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)stops[i], 10, 14, 22), stops[i + 1]));
		}
		g.Freeze();
		return new Rectangle
		{
			Fill = g,
			IsHitTestVisible = false
		};
	}

	private void BuildHero()
	{
		// —— 背景层：截图（异步）+ 左→右深色渐变 + 底部渐变（数值同 launch2.html .sh）——
		_bgShade.Children.Add(Shade(new Point(0.0, 0.5), new Point(1.0, 0.5), 199.0, 0.0, 115.0, 0.42, 0.0, 0.75));
		_bgShade.Children.Add(Shade(new Point(0.5, 1.0), new Point(0.5, 0.0), 140.0, 0.0, 0.0, 0.35));
		// —— 状态行：绿点 就绪 · 游戏目录 ——
		_prog.Height = 4.0;
		_prog.Width = 320.0;
		_prog.SetResourceReference(Control.ForegroundProperty, "B.Accent");
		_prog.Opacity = 0.92;
		_prog.Visibility = Visibility.Collapsed;
		Ellipse dot = new Ellipse
		{
			Width = 7.0,
			Height = 7.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		dot.SetResourceReference(Shape.FillProperty, "B.Ok");
		SPanel kicker = Ui.H(7.0, dot, _kickerText);
		// —— 大标题：实例名 + 下拉箭头，点击打开原有实例切换浮层 ——
		TextBlock chevron = Ui.Glyph("\ue70d", 18.0);
		chevron.Opacity = 0.7;
		_titleRow = Ui.H(10.0, _heroTitle.VCenter(), chevron);
		_titleRow.Cursor = Cursors.Hand;
		_titleRow.Background = Brushes.Transparent;
		_titleRow.HorizontalAlignment = HorizontalAlignment.Left;
		_titleRow.Margin = new Thickness(0.0, 6.0, 0.0, 2.0);
		_titleRow.MouseLeftButtonUp += delegate
		{
			OpenSwitchPopup();
		};
		// —— 小标签行（Java / 内存 / 模组数，SyncHero 填充）——
		_chipRow.Margin = new Thickness(0.0, 10.0, 0.0, 24.0);
		_configBody = (FrameworkElement)BuildConfig();
		_configBody.MaxHeight = 372.0;
		// —— 按钮行：主色「启动游戏」48 高 + 主色柔光；模板圆角固定，故用外壳 Border 提供圆角 ——
		_launchBtn.Style = Ui.S("Btn.Ghost");
		_launchBtn.Padding = new Thickness(0.0);
		_launchBtn.FontSize = 16.0;
		_launchBtn.SetResourceReference(Control.ForegroundProperty, "B.OnAccentStrong");
		TextBlock goText = Ui.Txt(I18n.L("启动游戏"), 16.0).VCenter();
		goText.FontWeight = FontWeights.ExtraBold;
		_launchBtn.Content = Ui.H(10.0, Ui.Glyph("\ue768", 17.0), goText);
		_launchShell = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Height = 48.0,
			Padding = new Thickness(30.0, 0.0, 30.0, 0.0),
			Child = _launchBtn,
			Effect = _launchGlow
		};
		_launchShell.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
		Motion.HoverLift(_launchShell, 1.02, 1.0, 8.0);
		_stopBtn.Style = Ui.S("Btn.Ghost");
		_stopBtn.Padding = new Thickness(0.0);
		_stopBtn.Content = Ui.H(8.0, Ui.Glyph("\ue71a", 15.0), Ui.Txt(I18n.L("停止"), 15.0, bold: true));
		_stopShell.Child = _stopBtn;
		_stopShell.SetResourceReference(Border.BackgroundProperty, "B.DangerSoft");
		_stopBtn.SetResourceReference(Control.ForegroundProperty, "B.Danger");
		_configPill = GlassButton("\ue713", null, OpenConfigPopup);
		_configPill.ToolTip = I18n.L("启动配置");
		// 联机入口（原「更多」抽屉里的联机）：进入现有联机页
		Border net = GlassButton("\ue701", I18n.L("联机"), delegate
		{
			PageBase.Win?.Navigate("multiplayer");
		});
		SPanel launchRow = Ui.H(10.0, _launchShell, _stopShell, _configPill, net);
		// 进度行：空闲（无进度且状态为「就绪」）时与顶部状态重复，收起
		SPanel statusRow = Ui.H(10.0, _prog.VCenter(), _status.VCenter());
		statusRow.Margin = new Thickness(0.0, 14.0, 0.0, 0.0);
		EventHandler syncStatus = delegate
		{
			bool idle = _prog.Visibility != Visibility.Visible && (string.IsNullOrEmpty(_status.Text) || _status.Text == I18n.L("就绪"));
			statusRow.Visibility = (idle ? Visibility.Collapsed : Visibility.Visible);
		};
		System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(_status, syncStatus);
		System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement)).AddValueChanged(_prog, syncStatus);
		syncStatus(null, EventArgs.Empty);
		_hero = Ui.V(0.0, kicker, _titleRow, _chipRow, launchRow, statusRow);
		_hero.MaxWidth = 680.0;
		_hero.HorizontalAlignment = HorizontalAlignment.Left;
		_hero.VerticalAlignment = VerticalAlignment.Center;
		_hero.Margin = new Thickness(56.0, 0.0, 24.0, 0.0);
		// —— 底部新闻卡：圆角 12，主色「新闻」小标签 + 标题 + 箭头；点击打开原公告浮层 ——
		Border tag = new Border
		{
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(8.0, 2.0, 8.0, 3.0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = Ui.Txt(I18n.L("新闻"), 11.0, bold: true, "B.OnAccent")
		};
		tag.SetResourceReference(Border.BackgroundProperty, "B.Accent");
		TextBlock arrow = Ui.Glyph("\ue76c", 10.0);
		arrow.Opacity = 0.8;
		_newsCard = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(8.0, 8.0, 14.0, 8.0),
			Margin = new Thickness(56.0, 0.0, 0.0, 18.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Bottom,
			Cursor = Cursors.Hand,
			Child = Ui.H(10.0, tag, _newsTicker, arrow)
		};
		Glass(_newsCard);
		_newsCard.MouseLeftButtonUp += delegate
		{
			OpenNewsPopup();
		};
		// —— 根布局：背景层铺满两行；内容 * / 新闻 Auto ——
		Grid root = Ui.G("*,Auto");
		root.ClipToBounds = true;
		root.Add(_bgImage, 0, 0, 2);
		root.Add(_bgShade, 0, 0, 2);
		root.Add(_hero, 0);
		root.Add(_newsCard, 1);
		base.Content = root;
		ApplyHeroTone(onImage: false);
		SyncHero();
	}

	// 半透明按钮（48 高，圆角 12）；label 为 null 即 48×48 图标钮
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
			Cursor = Cursors.Hand,
			Child = ((label == null) ? ((UIElement)g) : ((UIElement)Ui.H(8.0, g, Ui.Txt(label, 14.0, bold: true).VCenter())))
		};
		Glass(b);
		b.MouseLeftButtonUp += delegate
		{
			click();
		};
		return b;
	}

	private void Glass(Border b)
	{
		_glass.Add(b);
		b.MouseEnter += delegate
		{
			Tone(b, hover: true);
		};
		b.MouseLeave += delegate
		{
			Tone(b, hover: false);
		};
	}

	// 有截图：白字 + 半透明白块；无截图：主题纸色块 + 主题字色（随主题资源自动切换）
	private void Tone(Border b, bool hover)
	{
		if (_onImage)
		{
			b.Background = (hover ? W22 : W14);
			b.BorderBrush = W22;
			b.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, W100);
		}
		else
		{
			b.SetResourceReference(Border.BackgroundProperty, hover ? "B.Hover" : "B.Paper");
			b.SetResourceReference(Border.BorderBrushProperty, "B.Line");
			b.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Ink");
		}
	}

	private void ApplyHeroTone(bool onImage)
	{
		_onImage = onImage;
		Visibility v = (onImage ? Visibility.Visible : Visibility.Collapsed);
		_bgImage.Visibility = v;
		_bgShade.Visibility = v;
		if (onImage)
		{
			_hero?.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, W100);
			_kickerText.Foreground = W80;
			_status.Foreground = W80;
			_heroTitle.Effect = _titleShadow;
		}
		else
		{
			_hero?.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Ink");
			_kickerText.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
			_status.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
			_heroTitle.Effect = null;
		}
		foreach (Border b in _glass)
		{
			Tone(b, hover: false);
		}
		foreach (UIElement c in _chipRow.Children)
		{
			if (c is Border chip)
			{
				Tone(chip, hover: false);
			}
		}
		// 主按钮柔光取当前主题主色（OnShown 时重取，跟随主题切换）
		if (TryFindResource("B.AccentStrong") is SolidColorBrush accent)
		{
			_launchGlow.Color = accent.Color;
		}
		_launchGlow.Opacity = (onImage ? 0.45 : 0.3);
#if PREMIUM
		SyncImmersive(onImage);
#endif
	}

#if PREMIUM
	// —— premium 标题栏沉浸（交接必读 §2.5：「启动页背景铺到标题栏下，标题栏半透明」；compact 无此代码） ——
	// 有全幅背景（截图或 hero，且用户没设壁纸）时：把 _bgImage/_bgShade 从页面根搬到 MainWindow.WallHost
	//（RowSpan=2，位于 TopBar 之后的非命中层；排在 Wallpaper 自带元素之后，天然压在其上），PageWash 置透明，
	// TopBar 换玻璃底 + 白字（元素级资源覆盖，模拟效果图 .tb.glass）。
	// 还原路径：背景消失（截图没了 / 设了壁纸）或离开启动页（OnHidden）→ 搬回页面根、PageWash 恢复、
	// TopBar 资源字典清空即回落主题画刷。回到启动页时 OnShown→ApplyHeroTone 会自动重新进入沉浸态。
	private void SyncImmersive(bool onImage)
	{
		try
		{
			MainWindow? win = PageBase.Win;
			bool want = onImage && win != null && !Wallpaper.Active && _bgImage.Source != null;
			if (want == _immersive)
			{
				return;
			}
			_immersive = want;
			Grid root = (Grid)base.Content;
			if (want)
			{
				// 先从两个可能的父容器都摘一遍（Remove 对非子元素是无害空操作），避免“已有逻辑父级”异常
				root.Children.Remove(_bgImage);
				root.Children.Remove(_bgShade);
				win.WallHost.Children.Remove(_bgImage);
				win.WallHost.Children.Remove(_bgShade);
				win.WallHost.Children.Add(_bgImage);
				win.WallHost.Children.Add(_bgShade);
				win.PageWash.Opacity = 0.0;
				GlassTopBar(win.TopBar, on: true);
			}
			else
			{
				win.WallHost.Children.Remove(_bgImage);
				win.WallHost.Children.Remove(_bgShade);
				root.Children.Remove(_bgImage);
				root.Children.Remove(_bgShade);
				root.Children.Insert(0, _bgImage);
				root.Children.Insert(1, _bgShade);
				win.PageWash.Opacity = 1.0;
				GlassTopBar(win.TopBar, on: false);
			}
		}
		catch
		{
			// 沉浸只是观感增强：任何异常都放弃沉浸（元素加回页面根由下次调用兜底，Remove 对非子元素是无害空操作）
			_immersive = false;
		}
	}

	// 效果图 .tb.glass：低透明度深色底 + 白系文字（scrim 与 _bgShade 同色系 rgba(10,14,22,·)）。
	// 用元素级 ResourceDictionary 覆盖，不碰样式与 Palette；还原 = 清空字典，DynamicResource 全部回落主题值。
	// 页签下划线 B.Accent 保持主题色（刻意偏离效果图的白色，跟随用户主题）。
	// 另对 TopBar 设 TextElement.Foreground 附加属性（Inherits）：资源字典覆盖管不住「属性继承值」——
	// 「TipCL」标题继承的是 Window 解析好的 B.Ink 画刷（浅色主题下是深色），只有让最近祖先（TopBar）
	// 自己带 Foreground 值才能盖过它；ClearValue 后回落 Window 的继承链。
	private static void GlassTopBar(Border bar, bool on)
	{
		if (!on)
		{
			bar.Resources = new ResourceDictionary();
			bar.ClearValue(System.Windows.Documents.TextElement.ForegroundProperty);
			return;
		}
		ResourceDictionary rd = new ResourceDictionary();
		rd["B.TitleBar"] = Sc(46, 10, 14, 22);
		rd["B.TitleBarLine"] = Sc(46, 255, 255, 255);
		rd["B.Ink"] = Brushes.White;
		rd["B.InkMuted"] = Sc(191, 255, 255, 255);
		rd["B.InkSoft"] = Sc(217, 255, 255, 255);
		rd["B.CaptionInk"] = Sc(230, 255, 255, 255);
		rd["B.TitleSep"] = Sc(64, 255, 255, 255);
		bar.Resources = rd;
		bar.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.White);
	}

	private static SolidColorBrush Sc(byte a, byte r, byte g, byte b)
	{
		SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
		brush.Freeze();
		return brush;
	}
#endif

	private Border Chip(string text)
	{
		Border chip = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(10.0, 3.0, 10.0, 4.0),
			Child = Ui.Txt(text, 12.0)
		};
		Tone(chip, hover: false);
		return chip;
	}

	// 背景：当前实例目录（含版本隔离目录）screenshots 最新一张；同一张不重复解码；任何失败回退渐变底
	private async void RefreshBackground()
	{
		int gen = ++_bgGen;
		try
		{
			string instance = _inst.Str();
			string version = _ver.Str();
			string root = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance)?.Path ?? "";
			string skip = _bgKey;
			// 用户在设置里自定义了壁纸时尊重其选择，不叠截图
			Tuple<string, System.Windows.Media.Imaging.BitmapSource>? shot = null;
			if (!Wallpaper.Active && root.Length > 0)
			{
				string[] dirs = new string[2]
				{
					System.IO.Path.Combine(root, "screenshots"),
					string.IsNullOrEmpty(version) ? "" : System.IO.Path.Combine(root, "versions", version, "screenshots")
				};
				shot = await Task.Run(() => LoadShot(dirs, skip));
			}
			if (gen != _bgGen || (shot != null && shot.Item2 == null))
			{
				return;
			}
#if PREMIUM
			// premium：没有截图（且用户没设自定义壁纸）时用嵌入 hero.jpg（launch2 效果图同款全景）做背景，
			// 不再回退纯渐变底；截图存在时仍截图优先（两版一致）。换壁纸后下次刷新会走 ApplyHeroTone(false) 还原。
			if (shot == null && !Wallpaper.Active)
			{
				System.Windows.Media.Imaging.BitmapSource? hero = await Task.Run(HeroSource);
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
#endif
			_bgKey = shot?.Item1 ?? "";
			_bgImage.Source = shot?.Item2;
			ApplyHeroTone(shot != null);
		}
		catch
		{
			// 背景只是装饰，任何异常都不影响启动页
		}
	}

	// 线程池执行：找最新 png/jpg 截图并解码为冻结位图（OnLoad 不锁文件，不嵌任何图片进 exe）。
	// 与 skipKey 相同则只返回 key（Item2 = null，表示保持现状）；没有截图或出错返回 null。
	private static Tuple<string, System.Windows.Media.Imaging.BitmapSource>? LoadShot(string[] dirs, string skipKey)
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
				return Tuple.Create(key, (System.Windows.Media.Imaging.BitmapSource)null);
			}
			System.Windows.Media.Imaging.BitmapImage bmp = new System.Windows.Media.Imaging.BitmapImage();
			using (FileStream fs = new FileStream(best.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				bmp.BeginInit();
				bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
				bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
				bmp.DecodePixelWidth = 1600;
				bmp.StreamSource = fs;
				bmp.EndInit();
			}
			bmp.Freeze();
			return Tuple.Create(key, (System.Windows.Media.Imaging.BitmapSource)bmp);
		}
		catch
		{
			return null;
		}
	}

#if PREMIUM
	// premium 专属：解码嵌入 hero.jpg（pack URI，后台线程可解码，Freeze 后跨线程用），只解码一次。
	// 视觉基准 launch2.png 的背景是清晰全景，不加 BlurEffect（毛玻璃只给标题栏与半透明块）。
	private static System.Windows.Media.Imaging.BitmapSource? HeroSource()
	{
		if (_heroCache == null)
		{
			try
			{
				System.Windows.Media.Imaging.BitmapImage bmp = new System.Windows.Media.Imaging.BitmapImage();
				bmp.BeginInit();
				bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
				bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
				bmp.DecodePixelWidth = 1600;
				bmp.UriSource = new Uri("pack://application:,,,/TipCL;component/PremiumAssets/hero.jpg");
				bmp.EndInit();
				bmp.Freeze();
				_heroCache = bmp;
			}
			catch
			{
				return null;
			}
		}
		return _heroCache;
	}
#endif

	// 模组数小标签：沿用实例库同源 API，同一实例/版本只查一次
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
			instance = instance,
			version = version
		}, new List<ModEntry>());
		if (key == _modKey)
		{
			_modCount = list?.Count ?? 0;
			SyncHero();
		}
	}
	private static Border PopShell(UIElement content, double width, double maxHeight, Thickness margin)
	{
		Border b = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(12.0),
			Width = width,
			MaxHeight = maxHeight,
			Margin = margin,
			Child = content
		};
		b.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		b.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		Motion.Shadow(b, 22.0, 0.16, 4.0);
		return b;
	}

	private static Popup MakePopup(FrameworkElement target, PlacementMode mode, Border shell)
	{
		return new Popup
		{
			PlacementTarget = target,
			Placement = mode,
			StaysOpen = false,
			AllowsTransparency = true,
			Child = shell
		};
	}

	// ————— 配置浮层 —————

	private void OpenConfigPopup()
	{
		if (_configPopup == null)
		{
			TextBlock head = Ui.Txt(I18n.L("启动配置"), 15.0, bold: true);
			Button close = Ui.IconBtn("\ue711", I18n.L("关闭"), delegate
			{
				if (_configPopup != null)
				{
					_configPopup.IsOpen = false;
				}
			});
			Grid g = Ui.G(null, "*,Auto");
			g.Add(head.VCenter(), 0);
			g.Add(close, 0, 1);
			_configPopup = MakePopup(_configPill ?? (FrameworkElement?)_titleRow ?? this, PlacementMode.Bottom,
				PopShell(Ui.V(10.0, g, _configBody ?? Ui.Empty("\ue713", I18n.L("配置加载中"))), 360.0, 440.0, new Thickness(0.0, 6.0, 14.0, 14.0)));
		}
		_configPopup.IsOpen = true;
	}

	// ————— 实例切换浮层 —————

	private void OpenSwitchPopup()
	{
		if (_switchPopup == null)
		{
			_switchSearch = Ui.Input(I18n.L("搜索实例…"));
			_switchSearch.Height = 30.0;
			_switchSearch.Padding = new Thickness(6.0, 5.0, 6.0, 5.0);
			_switchSearch.TextChanged += delegate
			{
				RenderSwitchRows();
			};
			Grid search = Ui.G(null, "Auto,*");
			search.Add(Ui.Glyph("\ue721", 12.0, "B.InkFaint").M(2.0, 0.0, 6.0, 0.0).VCenter(), 0);
			search.Add(_switchSearch, 0, 1);
			_switchList = new StackPanel();
			SmoothScroll listScroll = Ui.Scroll(_switchList);
			listScroll.MaxHeight = 300.0;
			Button openLib = Ui.Btn(I18n.L("打开实例库"), BtnKind.Ghost, delegate
			{
				_switchPopup.IsOpen = false;
				PageBase.Win?.Navigate("library");
			}, "\ue80a");
			openLib.FontSize = 12.0;
			openLib.HorizontalAlignment = HorizontalAlignment.Left;
			_switchPopup = MakePopup((FrameworkElement?)_titleRow ?? this, PlacementMode.Bottom,
				PopShell(Ui.V(8.0, search, Ui.Sep(), listScroll, Ui.Sep(), openLib), 400.0, 430.0, new Thickness(0.0, 6.0, 14.0, 14.0)));
		}
		_switchSearch.Text = "";
		RenderSwitchRows();
		_switchPopup.IsOpen = true;
	}

	private void RenderSwitchRows()
	{
		if (_switchList == null)
		{
			return;
		}
		_switchList.Children.Clear();
		string q = (_switchSearch?.Text?.Trim() ?? "");
		string current = _inst.Str();
		int shown = 0;
		foreach (InstanceInfo i in _instances)
		{
			if (q.Length > 0 && i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			shown++;
			bool isCurrent = (i.Name == current);
			SPanel subParts = Ui.V(1.0, Ui.Txt(i.Name, 13.5, bold: isCurrent, "B.Ink").Trim(), InstanceSub(i, 11.0, "B.InkFaint"));
			FrameworkElement right = (isCurrent ? CurrentMark() : ManageLink(i.Name));
			right.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			Grid row = Ui.G(null, "*,Auto");
			row.Add(subParts, 0);
			row.Add(right.M(8.0, 0.0, 0.0, 0.0), 0, 1);
			Border wrap = new Border
			{
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(8.0, 6.0, 8.0, 7.0),
				Cursor = (isCurrent ? Cursors.Arrow : Cursors.Hand),
				Child = row
			};
			string instName = i.Name;
			wrap.MouseEnter += delegate
			{
				wrap.SetResourceReference(Border.BackgroundProperty, "B.Hover");
			};
			wrap.MouseLeave += delegate
			{
				wrap.Background = Brushes.Transparent;
			};
			if (!isCurrent)
			{
				wrap.MouseLeftButtonUp += delegate
				{
					if (_switchPopup != null)
					{
						_switchPopup.IsOpen = false;
					}
					if (_inst.Str() != instName)
					{
						_inst.SelectedItem = instName;
					}
				};
			}
			_switchList.Children.Add(wrap);
		}
		if (shown == 0)
		{
			_switchList.Children.Add(Ui.Muted(I18n.L("没有匹配的实例")).M(4.0, 6.0, 4.0, 6.0));
		}
	}

	private static SPanel CurrentMark()
	{
		Ellipse dot = new Ellipse
		{
			Width = 7.0,
			Height = 7.0
		};
		dot.SetResourceReference(Shape.FillProperty, "B.Accent");
		SPanel p = Ui.H(5.0, dot, Ui.Txt(I18n.L("当前"), 11.5, bold: false, "B.InkMuted"));
		return p;
	}

	private Button ManageLink(string instName)
	{
		Button b = Ui.Btn(I18n.L("管理"), BtnKind.Ghost, delegate
		{
			if (_switchPopup != null)
			{
				_switchPopup.IsOpen = false;
			}
			PageBase.Win?.Navigate("instance");
		});
		b.FontSize = 11.5;
		b.Padding = new Thickness(8.0, 2.0, 8.0, 3.0);
		return b;
	}

	private static TextBlock InstanceSub(InstanceInfo i, double size, string fg)
	{
		List<string> parts = new List<string>();
		if (!string.IsNullOrEmpty(i.Pack))
		{
			parts.Add(i.Pack);
		}
		if (!string.IsNullOrEmpty(i.Mc))
		{
			parts.Add("Minecraft " + i.Mc);
		}
		TextBlock t = Ui.Txt(string.Join(" · ", parts), size, bold: false, fg).Trim();
		return t;
	}

	// ————— 公告浮层 —————

	private void OpenNewsPopup()
	{
		if (_newsPopup == null)
		{
			_newsPopup = MakePopup((FrameworkElement?)_newsCard ?? this, PlacementMode.Top,
				PopShell(Ui.Scroll(_newsHost), 420.0, 360.0, new Thickness(0.0, 14.0, 14.0, 6.0)));
		}
		_newsPopup.IsOpen = true;
	}

	// dev-only：按名字打开对应浮层（--pop 截图用）
	public void OpenPopupForDevShot(string what)
	{
		switch (what)
		{
		case "config":
			OpenConfigPopup();
			break;
		case "instances":
			OpenSwitchPopup();
			break;
		case "news":
			OpenNewsPopup();
			break;
		}
	}

	// dev-only：把浮层内容摘出来交给 DevShot 挂进窗口渲染（Popup 是独立 HWND，RenderTargetBitmap 拍不到）
	public FrameworkElement? DetachPopupForShot(string what)
	{
		Popup? p = (what switch
		{
			"config" => _configPopup,
			"instances" => _switchPopup,
			"news" => _newsPopup,
			_ => null,
		});
		if (p?.Child is Border b)
		{
			p.Child = null;
			p.IsOpen = false;
			return b;
		}
		return null;
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
			ComboBox acc = _acc;
			ComboBox c = acc;
			c.Fill(await AccountGate.ListAsync(), (keepAcc.Length > 0) ? keepAcc : PageBase.Win?.Prefs.LastUser);
			Dictionary<string, JsonElement> dictionary = await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
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
		ReloadJavaAsync(scan: true);
		SyncBanner();
		await ApplyPreferenceAsync();
		LoadNewsAsync();
		LoadPlaytimeAsync();
		SyncTaskCard();
	}

	private async Task OnInstanceChanged()
	{
		await ReloadVersionsAsync();
		await ReloadJavaAsync(scan: false);
		SyncBanner();
	}

	private async Task ReloadVersionsAsync()
	{
		string text = _inst.Str();
		if (!string.IsNullOrEmpty(text))
		{
			List<string> list = (await PageBase.Api.TryCallAsync("get_installed_versions", new
			{
				instance = text
			}, new List<string>())) ?? new List<string>();
			_syncing = true;
			_ver.Fill(list);
			_syncing = false;
			_launchBtn.IsEnabled = list.Count > 0 && _taskId == null;
			if (list.Count == 0)
			{
				_status.Text = I18n.L("这个实例还没有版本，先去「原版游戏」装一个");
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
			scan_system = scan
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (!(_inst.Str() != inst) && opts.Count != 0)
		{
			string keep = await PageBase.Api.TryCallAsync("java_combo_label_for", new
			{
				instance = inst,
				options = opts
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
				java = SelectedJava()
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
					Int(_hBox.Text, 480)
				}
			}
		});
	}

	public void PreferInstance(string name, string? version = null)
	{
		_preferredInstance = name;
		_preferredVersion = version;
		if (_instances.Count > 0)
		{
			PageBase.Run(ApplyPreferenceAsync);
		}
	}

	/// <summary>选中指定实例/版本后直接启动（AI 页任务卡「启动游戏」用）。</summary>
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
		string name = _preferredInstance;
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
		string preferredVersion = _preferredVersion;
		_preferredVersion = null;
		if (!string.IsNullOrEmpty(preferredVersion) && _ver.Items.Contains(preferredVersion))
		{
			_syncing = true;
			_ver.Fill(_ver.Items.Cast<string>(), preferredVersion);
			_syncing = false;
		}
		SyncBanner();
	}

	private string SelectedJava()
	{
		string label = _java.Str();
		object obj = _javaOpts.FirstOrDefault((JavaOption o) => o.Label == label)?.Value;
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
		InstanceInfo instanceInfo = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance);
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
			_bSub.Text = (string.IsNullOrEmpty(version) ? I18n.L("先到「原版游戏」安装一个版本") : I18n.L("实例 {0} · 点「启动游戏」进入世界", instance));
		}
		if (!string.IsNullOrEmpty(instance))
		{
			PageBase.Run(() => CountModsAsync(instance, version ?? ""));
		}
		SyncHero();
		RefreshBackground();
	}

	// 启动屏大标题 / 状态行 / 小标签；数据全来自现有字段，缺什么省什么
	private void SyncHero()
	{
		string instance = _inst.Str();
		InstanceInfo info = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance);
		_heroTitle.Text = (string.IsNullOrEmpty(instance) ? I18n.L("选择实例") : instance);
		string dir = info?.Path ?? "";
		_kickerText.Text = I18n.L("就绪") + (string.IsNullOrEmpty(dir) ? "" : (" · " + dir));
		// 小标签：Java 版本 / 内存 / 模组数（「Java 17 (…)」「jdk1.8.0_301」→ Java 17 / Java 8）
		_chipRow.Children.Clear();
		string java = _java.Str();
		if (!string.IsNullOrEmpty(java))
		{
			System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(java, "(?:java|jdk|jre)\\D{0,3}(?:1\\.)?(\\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
			@default = "news"
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
				@default = ""
			}, "");
			_newsHost.Children.Clear();
			try
			{
				string text2 = ((string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ? I18n.L("未设置自定义主页。到设置 → 启动页主页 填本地 HTML 路径。") : (await Task.Run(() => File.ReadAllText(path))));
				string text3 = text2;
				_newsHost.Children.Add(Ui.Muted((text3.Length > 2000) ? (text3.Substring(0, 2000) + "…") : text3));
				_newsTicker.Text = (text3.Length > 60) ? (text3.Substring(0, 60) + "…") : text3;
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
		List<NewsRow> list = await PageBase.Api.TryCallAsync<List<NewsRow>>("fetch_news");
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
		string account = await AccountGate.EnsureAsync(_acc.Str());
		if (account != null && _acc.Str() != account)
		{
			_syncing = true;
			try
			{
				ComboBox acc = _acc;
				ComboBox c = acc;
				c.Fill(await AccountGate.ListAsync(), account);
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
			PageBase.Toast(I18n.L("没有版本"), I18n.L("请先到「原版游戏」安装"), ToastKind.Warning);
			PageBase.Win?.Navigate("download");
			return;
		}
		string account = await EnsureLaunchAccountAsync();
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
		PreflightResult preflightResult = await PageBase.Api.TryCallAsync<PreflightResult>("preflight_launch", new
		{
			instance = instance,
			version = version,
			memory_mb = memory,
			java = java
		});
		bool force = false;
		if (preflightResult == null)
		{
			if (!(await Dlg.Confirm(I18n.L("启动预检不可用"), I18n.L("preflight_launch 调用失败，无法检查 Java / 内存 / 文件完整性。\n跳过预检直接启动？"), I18n.L("继续启动"), I18n.L("取消"))))
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
				string body = string.Join("\n\n", list2.Select((PreflightItem i) => "· " + i.Title + "\n" + i.Detail)) + I18n.L("\n\n仍要继续启动？");
				if (!(await Dlg.Confirm(I18n.L("启动预检有警告"), body, I18n.L("继续启动"))))
				{
					return;
				}
			}
		}
		_logLines.Clear();
		_log.Clear();
		_prog.BeginAnimation(RangeBase.ValueProperty, null);
		_prog.Value = 0.0;
		_prog.Visibility = Visibility.Visible;
		_progGen++;
		_status.Text = I18n.L("准备启动…");
		_launchBtn.IsEnabled = false;
		_stopBtn.IsEnabled = true;
		_launchShell.Visibility = Visibility.Collapsed;
		_stopShell.Visibility = Visibility.Visible;
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
				instance = instance,
				version = version,
				account = account,
				username = account,
				memory_mb = memory,
				width = Int(_wBox.Text, 854),
				height = Int(_hBox.Text, 480),
				java = java,
				extra_game_args = ExtraArgs(),
				force = force
			});
		}
		catch (Exception ex)
		{
			_launchBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			_launchShell.Visibility = Visibility.Visible;
			_stopShell.Visibility = Visibility.Collapsed;
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
			I18n.L("打开多开并启动")
		}, 520.0) != 1)
		{
			return false;
		}
		await PageBase.Api.CallAsync<object>("set_multi_instance", new
		{
			allow = true
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
		string text = _server.Text?.Trim();
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
				text.Substring(num + 1)
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
				task_id = _taskId
			});
		}
	}

	private UIElement BuildConfig()
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
		Button button5 = Ui.IconBtn("\ue712", I18n.L("更多操作"), delegate(object s, RoutedEventArgs _)
		{
			ShowMoreMenu((FrameworkElement)s);
		});
		// 常用的只有实例/版本/用户；Java、内存、分辨率、直连收进「更多设置」，默认折叠
		SPanel advanced = Ui.V(9.0, Field("Java", _java), Field(I18n.L("内存"), grid), Field(I18n.L("分辨率"), control), Field(I18n.L("直连"), _server));
		advanced.Visibility = Visibility.Collapsed;
		Button toggle = Ui.Btn(I18n.L("更多设置"), BtnKind.Ghost, null, "\ue70d");
		toggle.HorizontalAlignment = HorizontalAlignment.Left;
		toggle.Click += delegate
		{
			bool open = advanced.Visibility != Visibility.Visible;
			advanced.Visibility = (open ? Visibility.Visible : Visibility.Collapsed);
			toggle.Content = Ui.H(6.0, Ui.Glyph(open ? "\ue70e" : "\ue70d", 12.0), Ui.Txt(open ? I18n.L("收起") : I18n.L("更多设置")));
		};
		return Ui.Scroll(Ui.V(9.0, Field(I18n.L("实例"), _inst), Field(I18n.L("版本"), _ver), Field(I18n.L("用户"), _acc), toggle, advanced, Ui.Sep().M(0.0, 4.0, 0.0, 2.0), Ui.H(7.0, button, button2, button5)));
	}

	private static UIElement Field(string label, UIElement control)
	{
		Grid grid = Ui.G(null, "58,*");
		grid.Add(Ui.Txt(label, 12.0, bold: false, "B.InkMuted").VCenter(), 0);
		control.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		grid.Add(control, 0, 1);
		return grid;
	}

	private void ShowMoreMenu(FrameworkElement anchor)
	{
		ContextMenu menu = new ContextMenu();
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
				name = _inst.Str()
			});
		});
		menu.PlacementTarget = anchor;
		menu.IsOpen = true;
		void Item(string head, Func<Task> act)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = head
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
		_loginHint = Ui.Muted(I18n.L("正在获取登录代码…"));
		_loginCode = new TextBlock
		{
			Text = "------",
			FontSize = 26.0,
			FontWeight = FontWeights.Bold
		};
		_loginCode.SetResourceReference(TextBlock.ForegroundProperty, "B.AccentText");
		Button button = Ui.Btn(I18n.L("复制代码"), BtnKind.Chip, delegate
		{
			try
			{
				Clipboard.SetText(_loginCode.Text);
				PageBase.Toast(I18n.L("已复制"), I18n.L("代码已放进剪贴板"), ToastKind.Success);
			}
			catch
			{
			}
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
		PasswordBox pw = Ui.Pw();
		ComboBox pick = Ui.Combo(presets.Select((AuthlibPreset p) => p.Name));
		pick.SelectionChanged += delegate
		{
			AuthlibPreset authlibPreset = presets.FirstOrDefault((AuthlibPreset p) => p.Name == pick.Str());
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
				password = (pw.Password ?? "")
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
		string account = await EnsureLaunchAccountAsync();
		if (account == null)
		{
			return;
		}
		using (Dlg.Busy(I18n.L("正在生成启动命令…")))
		{
			string cmd;
			try
			{
				BridgeClient api = PageBase.Api;
				string instance = _inst.Str();
				List<string> list = await api.CallAsync<List<string>>("build_launch_command", new
				{
					instance = instance,
					version = ver,
					account = account,
					username = account,
					memory_mb = (int)_mem.Value,
					width = Int(_wBox.Text, 854),
					height = Int(_hBox.Text, 480),
					java = SelectedJava()
				});
				cmd = ((list != null && list.Count > 0) ? string.Join(" ", list) : null);
			}
			catch (BridgeCallException)
			{
				cmd = await PageBase.Api.CallAsync<string>("get_launch_command", new
				{
					instance = _inst.Str(),
					version = ver,
					account = account,
					username = account,
					memory_mb = (int)_mem.Value
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
			string text2 = Dlg.SaveFile(I18n.L("批处理 (*.bat)|*.bat"), "launch-" + _inst.Str() + "-" + text + ".bat", I18n.L("导出启动脚本"));
			if (text2 != null)
			{
				await PageBase.Api.StartTaskAsync("export_launch_script", new
				{
					instance = _inst.Str(),
					version = text,
					dest = text2
				});
			}
		}
	}

	private async Task ShortcutAsync()
	{
		string text = _ver.Str();
		string text2 = (string.IsNullOrEmpty(text) ? null : (await EnsureLaunchAccountAsync()));
		string text3 = text2;
		if (text3 != null)
		{
			string text4 = await PageBase.Api.CallAsync<string>("create_desktop_shortcut", new
			{
				instance = _inst.Str(),
				version = text,
				username = text3,
				account = text3
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
				version = ver
			});
			PageBase.Win?.FlyToTasks(_launchBtn, I18n.L("修复中"));
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
					_loginHint.Text = I18n.L("在浏览器打开下面的地址并输入代码：\n") + ev.Uri;
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
				TaskId = ev.TaskId
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
					JsonElement value;
					int? num = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("code", out value) && value.ValueKind == JsonValueKind.Number) ? new int?(value.GetInt32()) : ((int?)null));
					_status.Text = ((!num.HasValue) ? I18n.L("游戏已退出") : I18n.L("游戏已退出（退出码 {0}）", num));
					HideProgressSoon(0);
				}
			}
			else
			{
				_status.Text = I18n.L("游戏进程已启动，正在加载世界…");
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
			_status.Text = (string.IsNullOrEmpty(status) ? I18n.L("处理中…") : status) + (string.IsNullOrEmpty(speed) ? "" : ("    " + speed));
			break;
		}
		case "log":
			AppendLog(ev.Text);
			break;
		case "finished":
			_launchBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			_launchShell.Visibility = Visibility.Visible;
			_stopShell.Visibility = Visibility.Collapsed;
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
					Version = _ver.Str()
				}, gameError: true));
			}
			_taskId = null;
			LoadPlaytimeAsync();
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
			bool num = _log.VerticalOffset >= _log.ExtentHeight - _log.ViewportHeight - 24.0;
			_log.Text = string.Join("\n", _logLines);
			if (num)
			{
				_log.ScrollToEnd();
			}
		}
	}

	public override void OnShown()
	{
		SyncTaskCard();
		SyncHero();
		_dash.RefreshThemeVisuals();
		// 主题可能已切换（主色柔光重取）；刚玩完可能有新截图
		ApplyHeroTone(_onImage);
		RefreshBackground();
	}

#if PREMIUM
	// 离开启动页必须退出沉浸：背景搬回页面根（随页面一起离屏）、TopBar 回主题底色、PageWash 恢复。
	// 再次回到启动页时 OnShown → ApplyHeroTone → SyncImmersive 会自动重新进入。
	public override void OnHidden()
	{
		SyncImmersive(false);
	}
#endif
}
