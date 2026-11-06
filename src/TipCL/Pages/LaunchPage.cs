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

// action-first 启动屏：大实例标题 + 单一「启动游戏」主按钮 + 右上安静区药丸 + 底部公告条。
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

	// —— 启动屏 UI ——
	private readonly TextBlock _kickerText = Ui.Txt("", 12.0, bold: false, "B.InkMuted");

	private readonly TextBlock _heroTitle = new TextBlock
	{
		FontSize = 30.0,
		FontWeight = FontWeights.SemiBold,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	private readonly TextBlock _meta = new TextBlock
	{
		FontSize = 12.5,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	private TextBlock _accountPillText = Ui.Txt("", 12.0, bold: false, "B.InkSoft");

	// 主操作行外壳：运行中隐藏、显示同尺寸红「停止」，保证该行始终只有一个 52 高按钮
	private Border? _launchShell;

	private readonly TextBlock _newsTicker = new TextBlock
	{
		FontSize = 12.5,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	private readonly Border _stopShell = new Border
	{
		CornerRadius = new CornerRadius(12.0),
		Height = 52.0,
		Padding = new Thickness(26.0, 0.0, 26.0, 0.0),
		Visibility = Visibility.Collapsed
	};

	private FrameworkElement? _configBody;

	private Border? _configPill;

	private SPanel? _titleRow;

	private Border? _morePill;

	private Popup? _morePopup;

	private Popup? _configPopup;

	private Popup? _switchPopup;

	private Popup? _newsPopup;

	private StackPanel? _switchList;

	private TextBox? _switchSearch;

	private Ellipse? _glow1;

	private Ellipse? _glow2;

	private string _loaderName = "";

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
		_meta.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		_heroTitle.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
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

	// ————— 启动屏布局 —————

	private void BuildHero()
	{
		// 氛围光 + 方块网格纹理（颜色走色板令牌，深浅色自动切换）
		_glow1 = new Ellipse
		{
			Width = 560.0,
			Height = 320.0,
			IsHitTestVisible = false
		};
		_glow1.SetResourceReference(Shape.FillProperty, "B.HeroGlow1");
		_glow2 = new Ellipse
		{
			Width = 700.0,
			Height = 420.0,
			IsHitTestVisible = false
		};
		_glow2.SetResourceReference(Shape.FillProperty, "B.HeroGlow2");
		Rectangle tex = new Rectangle
		{
			IsHitTestVisible = false
		};
		tex.SetResourceReference(Shape.FillProperty, "B.HeroGrid");
		Canvas glow = new Canvas
		{
			IsHitTestVisible = false
		};
		glow.Children.Add(tex);
		glow.Children.Add(_glow2);
		glow.Children.Add(_glow1);
		// 主内容块：kicker / 大标题 / 元信息 / 主按钮 / 进度行
		_prog.Height = 4.0;
		_prog.Width = 320.0;
		_prog.SetResourceReference(Control.ForegroundProperty, "B.Accent");
		_prog.Opacity = 0.92;
		_prog.Visibility = Visibility.Collapsed;
		_status.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		SPanel kicker = Ui.H(6.0, Ui.Glyph("\ue823", 12.0, "B.InkMuted"), _kickerText);
		_titleRow = Ui.H(9.0, _heroTitle.VCenter(), Ui.Glyph("\ue70d", 18.0, "B.InkFaint"));
		_titleRow.Cursor = Cursors.Hand;
		_titleRow.Background = Brushes.Transparent;
		_titleRow.MouseLeftButtonUp += delegate
		{
			OpenSwitchPopup();
		};
		_configBody = (FrameworkElement)BuildConfig();
		_configBody.MaxHeight = 372.0;
		// 主操作行：52 高大按钮（模板圆角固定 6，用外壳 Border 提供 12 圆角）；
		// 规格要求 16 SemiBold 白字，故 Foreground 钉为 B.OnAccentStrong
		_launchBtn.Style = Ui.S("Btn.Ghost");
		_launchBtn.Padding = new Thickness(0.0);
		_launchBtn.FontSize = 16.0;
		_launchBtn.SetResourceReference(Control.ForegroundProperty, "B.OnAccentStrong");
		_launchBtn.Content = Ui.H(9.0, Ui.Glyph("\ue768", 17.0), Ui.Txt(I18n.L("启动游戏"), 16.0, bold: true));
		_launchShell = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Height = 52.0,
			Padding = new Thickness(34.0, 0.0, 34.0, 0.0),
			Child = _launchBtn
		};
		_launchShell.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
		Motion.HoverLift(_launchShell, 1.02, 1.0, 8.0);
		_stopBtn.Style = Ui.S("Btn.Ghost");
		_stopBtn.Padding = new Thickness(0.0);
		_stopBtn.Content = Ui.H(8.0, Ui.Glyph("\ue71a", 15.0), Ui.Txt(I18n.L("停止"), 15.0, bold: true));
		_stopShell.Child = _stopBtn;
		_stopShell.SetResourceReference(Border.BackgroundProperty, "B.DangerSoft");
		_stopBtn.SetResourceReference(Control.ForegroundProperty, "B.Danger");
		SPanel launchRow = Ui.H(10.0, _launchShell, _stopShell);
		SPanel statusRow = Ui.H(10.0, _prog.VCenter(), _status.VCenter());
		statusRow.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
		SPanel main = Ui.V(12.0, kicker, _titleRow, _meta, launchRow, statusRow);
		main.MaxWidth = 620.0;
		main.HorizontalAlignment = HorizontalAlignment.Left;
		main.VerticalAlignment = VerticalAlignment.Center;
		main.Margin = new Thickness(56.0, 0.0, 0.0, 0.0);
		// 右上安静区：账号 / 启动配置 / AI / 联机 / 插件
			_configPill = Pill("\ue713", I18n.L("启动配置"), OpenConfigPopup, out _);
			Border accountPill = Pill("\ue77b", I18n.L("登录"), delegate
			{
				PageBase.Win?.Navigate("account");
			}, out _accountPillText);
			// 右上安静区收纳（用户实测反馈太挤）：AI / 联机 / 插件收进「更多」浮层，
			// 首页默认可见按钮（不含窗口钮）收敛为 3 个，全部功能仍 ≤2 击可达
			_morePill = Pill("\ue712", I18n.L("更多"), OpenMorePopup, out _);
			SPanel quiet = Ui.H(8.0,
				accountPill,
				_configPill,
				_morePill);
		quiet.HorizontalAlignment = HorizontalAlignment.Right;
		quiet.VerticalAlignment = VerticalAlignment.Top;
		quiet.Margin = new Thickness(0.0, 18.0, 20.0, 0.0);
		// 底部公告条
		_newsTicker.SetResourceReference(TextBlock.ForegroundProperty, "B.InkSoft");
		_newsTicker.Cursor = Cursors.Hand;
		Border chip = new Border
		{
			CornerRadius = new CornerRadius(5.0),
			Padding = new Thickness(7.0, 1.0, 8.0, 2.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		chip.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		chip.Child = Ui.Txt(I18n.L("公告"), 10.0, bold: false, "B.AccentText");
		TextBlock more = Ui.Txt(I18n.L("更多"), 12.0, bold: false, "B.AccentText").VCenter();
		SPanel moreRow = Ui.H(4.0, more, Ui.Glyph("\ue70d", 11.0, "B.AccentText"));
		moreRow.Cursor = Cursors.Hand;
		moreRow.Background = Brushes.Transparent;
		moreRow.MouseLeftButtonUp += delegate
		{
			OpenNewsPopup();
		};
		_newsTicker.MouseLeftButtonUp += delegate
		{
			OpenNewsPopup();
		};
		Grid barGrid = Ui.G(null, "Auto,*,Auto");
		barGrid.Margin = new Thickness(14.0, 0.0, 14.0, 0.0);
		barGrid.Add(chip, 0);
		barGrid.Add(_newsTicker.VCenter().M(10.0, 0.0, 10.0, 0.0), 0, 1);
		barGrid.Add(moreRow, 0, 2);
		Border bar = new Border
		{
			Height = 38.0,
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = barGrid
		};
		bar.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		bar.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		// 根布局：hero 区 * / 公告条 Auto
		Grid hero = new Grid
		{
			ClipToBounds = true
		};
		hero.Children.Add(glow);
		hero.Children.Add(main);
		hero.Children.Add(quiet);
		hero.SizeChanged += delegate
		{
			double w = hero.ActualWidth;
			double h = hero.ActualHeight;
			if (_glow1 != null)
			{
				Canvas.SetLeft(_glow1, w * 0.82 - 280.0);
				Canvas.SetTop(_glow1, -h * 0.10 - 160.0);
			}
			if (_glow2 != null)
			{
				Canvas.SetLeft(_glow2, w * 1.08 - 350.0);
				Canvas.SetTop(_glow2, h * 0.82 - 210.0);
			}
			tex.Width = w;
			tex.Height = h;
		};
		Grid root = Ui.G("*,Auto");
		root.Add(hero, 0);
		root.Add(bar, 1);
		base.Content = root;
		SyncHero();
	}

	private static Border Pill(string glyph, string text, Action click, out TextBlock label)
	{
		TextBlock t = Ui.Txt(text, 12.0, bold: false, "B.InkSoft").VCenter();
		Border b = new Border
		{
			Height = 30.0,
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(12.0, 0.0, 14.0, 1.0),
			Cursor = Cursors.Hand,
			BorderThickness = new Thickness(1.0),
			Child = Ui.H(6.0, Ui.Glyph(glyph, 13.0, "B.InkMuted"), t)
		};
		b.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		b.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		b.MouseEnter += delegate
		{
			b.SetResourceReference(Border.BackgroundProperty, "B.Hover");
		};
		b.MouseLeave += delegate
		{
			b.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		};
		b.MouseLeftButtonUp += delegate
		{
			click();
		};
		label = t;
		return b;
	}

	private static Border PopShell(UIElement content, double width, double maxHeight, Thickness margin)
	{
		Border b = new Border
		{
			CornerRadius = new CornerRadius(10.0),
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

	// 「更多」药丸溢出菜单：AI 助手 / 联机 / 插件（导航到既有页面，不造数据）
	private void OpenMorePopup()
	{
		if (_morePopup == null)
		{
			SPanel list = Ui.V(3.0,
				MoreRow("\ue99a", I18n.L("AI 助手"), "ai"),
				MoreRow("\ue701", I18n.L("联机"), "multiplayer"),
				MoreRow("\uea86", I18n.L("插件"), "plugins"));
			_morePopup = MakePopup(_morePill ?? (FrameworkElement?)_configPill ?? (FrameworkElement?)_titleRow ?? this, PlacementMode.Bottom,
				PopShell(list, 176.0, 240.0, new Thickness(0.0, 6.0, 14.0, 14.0)));
		}
		_morePopup.IsOpen = true;
	}

	private UIElement MoreRow(string glyph, string label, string pageId)
	{
		Border row = new Border
		{
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(10.0, 7.0, 10.0, 8.0),
			Cursor = Cursors.Hand,
			Child = Ui.H(9.0, Ui.Glyph(glyph, 14.0, "B.InkSoft"), Ui.Txt(label, 12.5, bold: false, "B.Ink").VCenter())
		};
		row.MouseEnter += delegate
		{
			row.SetResourceReference(Border.BackgroundProperty, "B.Hover");
		};
		row.MouseLeave += delegate
		{
			row.Background = Brushes.Transparent;
		};
		row.MouseLeftButtonUp += delegate
		{
			if (_morePopup != null)
			{
				_morePopup.IsOpen = false;
			}
			PageBase.Win?.Navigate(pageId);
		};
		return row;
	}

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
			_newsPopup = MakePopup(this, PlacementMode.Top,
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
		if (!string.IsNullOrEmpty(version))
		{
			PageBase.Run(() => TagLoaderAsync(version));
		}
		SyncHero();
	}

	// 启动屏大标题 / 元信息 / 药丸文案；数据全来自现有字段，缺什么省什么
	private void SyncHero()
	{
		string instance = _inst.Str();
		InstanceInfo info = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance);
		_heroTitle.Text = (string.IsNullOrEmpty(instance) ? I18n.L("选择实例") : instance);
		string dir = info?.Path ?? "";
		_kickerText.Text = ((string.IsNullOrEmpty(instance) ? I18n.L("就绪") : I18n.L("就绪 · {0}", instance)) + ((string.IsNullOrEmpty(dir) || dir == instance) ? "" : (" · " + dir)));
		List<string> meta = new List<string>();
		if (!string.IsNullOrEmpty(_loaderName))
		{
			meta.Add(_loaderName);
		}
		string java = _java.Str();
		if (!string.IsNullOrEmpty(java))
		{
			meta.Add(I18n.L("Java {0}", java));
		}
		meta.Add(I18n.L("内存 {0} MB", (int)_mem.Value));
		string user = _acc.Str();
		if (string.IsNullOrEmpty(user))
		{
			user = PageBase.Win?.Prefs.LastUser ?? "";
		}
		if (!string.IsNullOrEmpty(user))
		{
			meta.Add(I18n.L("玩家 {0}", user));
		}
		_meta.Text = string.Join(" · ", meta);
		_accountPillText.Text = (string.IsNullOrEmpty(user) ? I18n.L("登录") : user);
	}

	private async Task TagLoaderAsync(string version)
	{
		List<string> list = await PageBase.Api.TryCallAsync("loader_of", new
		{
			version_id = version
		}, new List<string>());
		if (list != null && list.Count >= 1 && !(_ver.Str() != version))
		{
			string text = list[0];
			_loaderName = (string.IsNullOrWhiteSpace(text) ? "" : text);
			SyncHero();
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
			PageBase.Win?.Navigate("version");
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
	}
}
