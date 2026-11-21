using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TipCL.Core.Launcher.Layout;
using TipCL.Models;
using TipCL.Pages.Layout;
using TipCL.Services;

namespace TipCL.Pages;

// 启动页 v3(效果图 _r35/ref/launch-v3.html,一比一复刻):
//   左侧固定主区域(实例名 + 标签 + 启动游戏 / 设置 / 联机,左下新闻条)—— 不进布局系统,不能隐藏 / 拖动 / 缩放;
//   右侧组件区:4 列网格(列宽 74 / 行高 96 / 间距 12),尺寸 2×1 / 2×2 / 4×2;窄窗口(&lt; 900)2 列、4×2 按 2×2 摆,放不下的收起并提示;
//   右下「编辑组件」进编辑态:编辑条只有「添加组件 / 重置 / 完成 / ⋯」,Ctrl Z / Ctrl Y 撤销重做;拖动松手按顺序重排,不叠放不留洞。
// 所有数值照抄效果图 CSS(.zone / .wg / .tools / .ebox / .b / .menu / .drawer / .lc / .toast / .news / .aipill / .editbtn)。
public sealed partial class LaunchPage
{
	private sealed class ZoneCard
	{
		public Grid Root = new Grid();

		public Border Glass = new Border();

		public Rectangle Dash = new Rectangle();

		public StackPanel Tools = new StackPanel();

		public Border? SizeBtn;

		public string Key = "";

		public ScaleTransform Scale = new ScaleTransform(1.0, 1.0);
	}

	// ————— 效果图里的固定色(编辑条 / 菜单 / 组件库是白底浮层,不随主题) —————
	private static readonly SolidColorBrush ZInk = ZSc(255, 0x1F, 0x1F, 0x1F);

	private static readonly SolidColorBrush ZInk2 = ZSc(255, 0x5C, 0x5C, 0x5C);

	private static readonly SolidColorBrush ZInk3 = ZSc(255, 0x8A, 0x8A, 0x8A);

	private static readonly SolidColorBrush ZPaper2 = ZSc(255, 0xF3, 0xF3, 0xF3);

	private static readonly SolidColorBrush ZPaperHover = ZSc(255, 0xE9, 0xE9, 0xE9);

	private static readonly SolidColorBrush ZLine = ZSc(23, 0, 0, 0);

	private static readonly SolidColorBrush ZWg = ZSc(122, 14, 18, 27);

	private static readonly SolidColorBrush ZDim = ZSc(128, 8, 10, 16);

	private static readonly SolidColorBrush ZToastBg = ZSc(235, 20, 22, 28);

	private static readonly SolidColorBrush ZOverBg = ZSc(140, 10, 14, 22);

	private static readonly SolidColorBrush ZOk = ZSc(255, 0x4A, 0xDE, 0x80);

	private static readonly SolidColorBrush ZHeadLine = ZSc(230, 20, 24, 34);

	private static readonly FontFamily ZMono = new FontFamily("Consolas, Cascadia Mono, Courier New");

	// ————— 图标(效果图内联 SVG 的路径,16×16 视框,描边 1.6) —————
	private const string IcoPlay = "M5 3.5 L5 12.5 L12 8 Z";

	private const string IcoSliders = "M2.5 4.5 H9.5 M12.5 4.5 H13.5 M2.5 11.5 H4.5 M7.5 11.5 H13.5 M9.5 4.5 A1.5 1.5 0 1 0 12.5 4.5 A1.5 1.5 0 1 0 9.5 4.5 M4.5 11.5 A1.5 1.5 0 1 0 7.5 11.5 A1.5 1.5 0 1 0 4.5 11.5";

	private const string IcoWifi = "M2 6.5 A9 9 0 0 1 14 6.5 M4.2 9 A5.6 5.6 0 0 1 11.8 9 M6.4 11.4 A2.3 2.3 0 0 1 9.6 11.4";

	private const string IcoWifiDot = "M7.4 13.4 A0.6 0.6 0 1 0 8.6 13.4 A0.6 0.6 0 1 0 7.4 13.4";

	private const string IcoChevron = "M4.5 6.5 L8 10 L11.5 6.5";

	private const string IcoArrow = "M6 4 L10 8 L6 12";

	private const string IcoSpark = "M8 2 V14 M2.8 5 L13.2 11 M2.8 11 L13.2 5";

	private const string IcoPencil = "M10.5 2.5 L13.5 5.5 L5.5 13.5 H2.5 V10.5 Z";

	private const string IcoPlus = "M8 3 V13 M3 8 H13";

	private const string IcoClose = "M4 4 L12 12 M12 4 L4 12";

	private const string IcoMore = "M2.2 8 A1.3 1.3 0 1 0 4.8 8 A1.3 1.3 0 1 0 2.2 8 M6.7 8 A1.3 1.3 0 1 0 9.3 8 A1.3 1.3 0 1 0 6.7 8 M11.2 8 A1.3 1.3 0 1 0 13.8 8 A1.3 1.3 0 1 0 11.2 8";

	private const string IcoLock = "M5 7 H11 A1.5 1.5 0 0 1 12.5 8.5 V12 A1.5 1.5 0 0 1 11 13.5 H5 A1.5 1.5 0 0 1 3.5 12 V8.5 A1.5 1.5 0 0 1 5 7 Z M5.5 7 V5 A2.5 2.5 0 0 1 10.5 5 V7";

	private const string IcoMods = "M8 1.8 L13.5 4.8 V11.2 L8 14.2 L2.5 11.2 V4.8 Z M2.5 4.8 L8 7.8 L13.5 4.8 M8 7.8 V14.2";

	private const string IcoCam = "M2 5.5 H4.5 L6 3.5 H10 L11.5 5.5 H14 V12.5 H2 Z M5.7 9 A2.3 2.3 0 1 0 10.3 9 A2.3 2.3 0 1 0 5.7 9";

	private const string IcoLog = "M4 1.8 H9.5 L12.5 5 V14.2 H4 Z M9.5 1.8 V5 H12.5 M6 8 H10.5 M6 10.5 H10.5";

	private const string IcoSave = "M1.8 4.2 H6.2 L7.7 5.8 H14.2 V13.2 H1.8 Z";

	// ————— 状态 —————
	private List<ZoneItem> _zItems = new List<ZoneItem>();

	private string _zSavedJson = "";

	private bool _zLoaded;

	// 0 常态 / 1 编辑 / 2 组件库(编辑态 + 左侧抽屉)
	private int _zMode;

	private readonly List<string> _zHist = new List<string>();

	private readonly List<string> _zFut = new List<string>();

	private int _zCols = LaunchZone.WideCols;

	private int _zRows = LaunchZone.MaxRows;

	private readonly Grid _zHost = new Grid
	{
		HorizontalAlignment = HorizontalAlignment.Right,
		VerticalAlignment = VerticalAlignment.Top,
		Margin = new Thickness(0.0, 18.0, 24.0, 0.0)
	};

	private readonly Canvas _zCells = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

	private readonly Canvas _zCanvas = new Canvas();

	private readonly Rectangle _zPh = new Rectangle
	{
		RadiusX = 12.0,
		RadiusY = 12.0,
		StrokeThickness = 1.5,
		StrokeDashArray = new DoubleCollection { 3.0, 2.0 },
		Visibility = Visibility.Collapsed,
		IsHitTestVisible = false
	};

	private readonly Dictionary<string, ZoneCard> _zEls = new Dictionary<string, ZoneCard>(StringComparer.Ordinal);

	private readonly StackPanel _zFoot = new StackPanel
	{
		Orientation = Orientation.Horizontal,
		HorizontalAlignment = HorizontalAlignment.Right,
		VerticalAlignment = VerticalAlignment.Top
	};

	private readonly TextBlock _zOverText = new TextBlock { FontSize = 12.0 };

	private Border? _zOver;

	private Border? _zEditBtn;

	private FrameworkElement? _zBar;

	private Border? _zMenu;

	private Border? _zMoreBtn;

	private Border? _zDrawer;

	private readonly TranslateTransform _zDrawerT = new TranslateTransform(-351.0, 0.0);

	private readonly StackPanel _zLibList = new StackPanel();

	private Border? _zToast;

	private readonly TextBlock _zToastText = new TextBlock { FontSize = 13.0 };

	private readonly TranslateTransform _zToastT = new TranslateTransform(0.0, 8.0);

	private DispatcherTimer? _zToastTimer;

	private Border? _zDim;

	private Border? _aiPill;

	private Border? _heroLockChip;

	private readonly List<Tuple<Border, byte, byte, byte>> _zGlass = new List<Tuple<Border, byte, byte, byte>>();

	private readonly DispatcherTimer _zClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20.0) };

	private Window? _zKeyWin;

	// 拖动
	private ZoneCard? _zDragCard;

	private string? _zDragId;

	private Point _zDragStart;

	private Vector _zDragOff;

	private bool _zDragStarted;

	private List<ZoneItem>? _zDragOrder;

	// 数据
	private WeekPlay _zWeek = new WeekPlay();

	private ServerRow? _zServer;

	private ServerPingResult? _zPing;

	private int _zPingState; // 0 未开始 / 1 连接中 / 2 完成

	private List<string> _zShotFiles = new List<string>();

	private readonly Dictionary<string, BitmapSource> _zThumbs = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

	private int _zDataGen;

	// ————— 构建 —————

	private void BuildZone(Grid root)
	{
		_zPh.Fill = AccentA(61);
		_zPh.Stroke = AccentA(255);
		_zHost.Children.Add(_zCells);
		_zHost.Children.Add(_zCanvas);
		_zCanvas.Children.Add(_zPh);
		Panel.SetZIndex(_zPh, 0);
		_zOver = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Background = ZOverBg,
			BorderBrush = White(36),
			BorderThickness = new Thickness(1.0),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Visibility = Visibility.Collapsed,
			Child = _zOverText
		};
		_zOverText.Foreground = W80;
		_zEditBtn = new Border
		{
			Height = 28.0,
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			BorderThickness = new Thickness(1.0),
			Opacity = 0.7,
			Cursor = Cursors.Hand,
			Child = ZRow(6.0, ZIcon(IcoPencil, 13.0), ZText(I18n.L("编辑组件"), 12.0))
		};
		ZGlass(_zEditBtn, 31, 51, 56);
		_zEditBtn.MouseEnter += delegate
		{
			_zEditBtn.Opacity = 1.0;
		};
		_zEditBtn.MouseLeave += delegate
		{
			_zEditBtn.Opacity = 0.7;
		};
		_zEditBtn.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
			ZoneBeginEdit();
		};
		_zFoot.Children.Add(_zOver);
		_zFoot.Children.Add(_zEditBtn);
		_aiPill = new Border
		{
			Height = 36.0,
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(12.0, 0.0, 8.0, 0.0),
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(0.0, 0.0, 24.0, 20.0),
			Cursor = Cursors.Hand,
			ToolTip = I18n.L("AI 助手") + " Ctrl+K",
			Child = ZRow(8.0, ZIcon(IcoSpark, 14.0), ZText("AI", 13.0, bold: true), ZKbd("Ctrl K", 1.0))
		};
		ZGlass(_aiPill, 36, 56, 56);
		_aiPill.MouseLeftButtonUp += delegate
		{
			PageBase.Win?.Navigate("ai");
		};
		_zDim = new Border { Background = ZDim, IsHitTestVisible = false, Opacity = 0.0, Visibility = Visibility.Collapsed };
		_zBar = BuildZoneBar();
		_zBar.HorizontalAlignment = HorizontalAlignment.Left;
		_zBar.VerticalAlignment = VerticalAlignment.Top;
		_zBar.Margin = new Thickness(24.0, 14.0, 0.0, 0.0);
		_zBar.Visibility = Visibility.Collapsed;
		_zDrawer = BuildZoneDrawer();
		_zToast = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(14.0, 8.0, 14.0, 8.0),
			Background = ZToastBg,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(0.0, 0.0, 0.0, 72.0),
			IsHitTestVisible = false,
			Opacity = 0.0,
			RenderTransform = _zToastT,
			Child = _zToastText
		};
		_zToastText.Foreground = W100;
		root.Children.Add(_zDim);
		Panel.SetZIndex(_zDim, 1);
		Panel.SetZIndex(_hero, 2);
		root.Children.Add(_aiPill);
		Panel.SetZIndex(_aiPill, 3);
		root.Children.Add(_zHost);
		Panel.SetZIndex(_zHost, 4);
		root.Children.Add(_zFoot);
		Panel.SetZIndex(_zFoot, 4);
		root.Children.Add(_zBar);
		Panel.SetZIndex(_zBar, 25);
		root.Children.Add(_zDrawer);
		Panel.SetZIndex(_zDrawer, 30);
		root.Children.Add(_zToast);
		Panel.SetZIndex(_zToast, 40);
		root.SizeChanged += delegate
		{
			ZoneMeasure(root.ActualWidth, root.ActualHeight, force: false);
		};
		root.PreviewMouseDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (_zMenu != null && _zMenu.Visibility == Visibility.Visible && e.OriginalSource is DependencyObject d && !IsInside(d, _zMenu) && (_zMoreBtn == null || !IsInside(d, _zMoreBtn)))
			{
				_zMenu.Visibility = Visibility.Collapsed;
			}
		};
		// 右键:常态「编辑组件」,编辑态「完成」(右下角按钮之外的第二个入口)
		ContextMenu cm = new ContextMenu();
		root.ContextMenu = cm;
		root.ContextMenuOpening += delegate(object _, ContextMenuEventArgs e)
		{
			if (e.OriginalSource is DependencyObject d && (IsInside(d, _titleRow!) || (_zDrawer != null && IsInside(d, _zDrawer))))
			{
				e.Handled = true;
				return;
			}
			cm.Items.Clear();
			MenuItem mi = new MenuItem { Header = (_zMode == 0) ? I18n.L("编辑组件") : I18n.L("完成") };
			mi.Click += delegate
			{
				if (_zMode == 0)
				{
					ZoneBeginEdit();
				}
				else
				{
					ZoneSetMode(0);
				}
			};
			cm.Items.Add(mi);
		};
		_zClock.Tick += delegate
		{
			ZoneRefreshContent("clock");
		};
		base.Loaded += delegate
		{
			ZoneEnsureLoaded();
			_zClock.Start();
		};
		base.Unloaded += delegate
		{
			_zClock.Stop();
			if (_zMode != 0)
			{
				try
				{
					ZoneSetMode(0); // 切走时在编辑态:等同「完成」(落盘 + 收起编辑条)
				}
				catch
				{
				}
			}
		};
	}

	private void ZoneEnsureLoaded()
	{
		if (_zLoaded)
		{
			return;
		}
		_zLoaded = true;
		_zItems = LoadZoneItems();
		_zSavedJson = LaunchZone.ToJson(_zItems);
		if (base.Content is FrameworkElement root)
		{
			ZoneMeasure(root.ActualWidth, root.ActualHeight, force: true);
		}
		ZoneLayout(_zItems, null);
	}

	/// <summary>
	/// 存档里有新格式(UiPrefs.LaunchZone)就用;没有 → 从旧版启动页区块读时推导(旧存档一个字节不动,
	/// 编辑器「完成」时有改动才写新格式;和 R34 l3 的 canvas 读时推导同一原则)。
	/// </summary>
	private static List<ZoneItem> LoadZoneItems()
	{
		List<ZoneItem>? saved = LaunchZone.FromJson(PageBase.Win?.Prefs.LaunchZone);
		if (saved != null)
		{
			return saved;
		}
		List<KeyValuePair<string, bool>>? legacy = null;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (doc.Layout?.Pages != null && doc.Layout.Pages.TryGetValue("launch", out var page) && page?.Blocks != null)
			{
				legacy = page.Blocks.Where((BlockLayout b) => b != null).Select((BlockLayout b) => new KeyValuePair<string, bool>(b.Id ?? "", b.Hidden)).ToList();
			}
		}
		catch
		{
			legacy = null;
		}
		return LaunchZone.FromLegacy(legacy, RefTypeOf);
	}

	private static string? RefTypeOf(string refId)
	{
		try
		{
			return WidgetRegistry.FindWidget(refId)?.Type;
		}
		catch
		{
			return null;
		}
	}

	private static string RefName(string? refId)
	{
		try
		{
			WidgetDef? w = (refId != null) ? WidgetRegistry.FindWidget(refId) : null;
			if (w != null && !string.IsNullOrWhiteSpace(w.Title))
			{
				return w.Title;
			}
		}
		catch
		{
		}
		return I18n.L("自建组件");
	}

	private string ZName(ZoneItem it)
	{
		if (it.Type == LaunchZone.TypeRef)
		{
			return RefName(it.Ref);
		}
		return I18n.L(LaunchZone.TypeOf(it.Type)?.Name ?? it.Type);
	}

	// 窗口变窄时减列数,不缩字:4 列 ↔ 2 列;主区域右边界 = 24 + 组件区宽 + 40(效果图 setWidth)
	private void ZoneMeasure(double width, double height, bool force)
	{
		if (width <= 0.0)
		{
			return;
		}
		int cols = LaunchZone.ColsFor(width);
		int rows = LaunchZone.RowsFor(height);
		bool changed = cols != _zCols || rows != _zRows;
		_zCols = cols;
		_zRows = rows;
		double zw = LaunchZone.SpanW(cols);
		double zh = LaunchZone.SpanH(rows);
		_zHost.Width = zw;
		_zHost.Height = zh;
		_zFoot.Margin = new Thickness(0.0, 18.0 + zh + 10.0, 24.0, 0.0);
		if (_hero != null)
		{
			_hero.Margin = new Thickness(56.0, 0.0, 24.0 + zw + 40.0, 70.0);
		}
		if (_newsCard != null)
		{
			_newsCard.MaxWidth = Math.Min(440.0, Math.Max(160.0, width - 56.0 - 24.0 - 160.0));
		}
		if (changed || force || _zCells.Children.Count == 0)
		{
			_zCells.Children.Clear();
			for (int y = 0; y < rows; y++)
			{
				for (int x = 0; x < cols; x++)
				{
					Rectangle cell = new Rectangle
					{
						Width = LaunchZone.Col,
						Height = LaunchZone.Row,
						RadiusX = 8.0,
						RadiusY = 8.0,
						Stroke = White(46),
						StrokeThickness = 1.0,
						StrokeDashArray = new DoubleCollection { 3.0, 3.0 }
					};
					Canvas.SetLeft(cell, LaunchZone.Px(x));
					Canvas.SetTop(cell, LaunchZone.Py(y));
					_zCells.Children.Add(cell);
				}
			}
			if (_zLoaded && changed)
			{
				ZoneLayout(_zItems, null);
			}
		}
	}

	// ————— 排版(效果图 layout()) —————

	private ZonePack ZoneLayout(List<ZoneItem> list, string? skip)
	{
		ZonePack p = LaunchZone.Pack(list, _zCols, _zRows);
		foreach (ZoneItem it in list)
		{
			if (!_zEls.TryGetValue(it.Id, out var card))
			{
				card = ZoneMakeCard(it.Id);
				_zEls[it.Id] = card;
			}
			if (!p.Out.TryGetValue(it.Id, out var r))
			{
				card.Root.Visibility = Visibility.Collapsed;
				continue;
			}
			card.Root.Visibility = Visibility.Visible;
			string key = it.Type + "|" + it.Size + "|" + r.Eff + "|" + it.Ref;
			if (card.Key != key)
			{
				ZoneFill(card, it, r.Eff);
			}
			if (it.Id == skip)
			{
				continue;
			}
			card.Root.Width = LaunchZone.SpanW(r.W);
			card.Root.Height = LaunchZone.SpanH(r.H);
			ZMove(card.Root, LaunchZone.Px(r.X), LaunchZone.Py(r.Y), 220);
		}
		foreach (string id in _zEls.Keys.ToList())
		{
			if (!list.Any((ZoneItem i) => i.Id == id))
			{
				_zCanvas.Children.Remove(_zEls[id].Root);
				_zEls.Remove(id);
			}
		}
		int n = p.Over.Count;
		if (_zOver != null)
		{
			_zOver.Visibility = (n > 0) ? Visibility.Visible : Visibility.Collapsed;
			_zOverText.Text = (_zCols < LaunchZone.WideCols) ? I18n.L("另有 {0} 个组件放不下，窗口变宽后自动回来", n) : I18n.L("另有 {0} 个组件放不下，移除一个或换成小尺寸", n);
		}
		return p;
	}

	private void ZMove(UIElement el, double left, double top, int ms)
	{
		if (!Motion.Enabled || ms <= 0 || double.IsNaN(Canvas.GetLeft(el)))
		{
			el.BeginAnimation(Canvas.LeftProperty, null);
			el.BeginAnimation(Canvas.TopProperty, null);
			Canvas.SetLeft(el, left);
			Canvas.SetTop(el, top);
			return;
		}
		Duration d = new Duration(TimeSpan.FromMilliseconds(ms));
		CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
		el.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(left, d) { EasingFunction = ease });
		el.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, d) { EasingFunction = ease });
	}

	private ZoneCard ZoneMakeCard(string id)
	{
		ZoneCard c = new ZoneCard();
		c.Glass.CornerRadius = new CornerRadius(12.0);
		c.Glass.BorderThickness = new Thickness(1.0);
		c.Glass.Padding = new Thickness(14.0, 12.0, 14.0, 12.0);
		c.Glass.ClipToBounds = true;
		c.Dash.RadiusX = 12.0;
		c.Dash.RadiusY = 12.0;
		c.Dash.StrokeThickness = 1.5;
		c.Dash.StrokeDashArray = new DoubleCollection { 3.0, 2.0 };
		c.Dash.Stroke = White(128);
		c.Dash.IsHitTestVisible = false;
		c.Dash.Visibility = Visibility.Collapsed;
		c.Tools.Orientation = Orientation.Horizontal;
		c.Tools.HorizontalAlignment = HorizontalAlignment.Right;
		c.Tools.VerticalAlignment = VerticalAlignment.Top;
		c.Tools.Margin = new Thickness(0.0, 6.0, 6.0, 0.0);
		c.Tools.Visibility = Visibility.Collapsed;
		c.Root.Children.Add(c.Glass);
		c.Root.Children.Add(c.Dash);
		c.Root.Children.Add(c.Tools);
		c.Root.RenderTransformOrigin = new Point(0.5, 0.5);
		c.Root.RenderTransform = c.Scale;
		c.Root.Tag = id;
		c.Root.MouseLeftButtonDown += ZOnDown;
		c.Root.MouseMove += ZOnMove;
		c.Root.MouseLeftButtonUp += ZOnUp;
		c.Root.LostMouseCapture += delegate
		{
			if (_zDragCard == c)
			{
				ZEndDrag(commit: _zDragStarted);
			}
		};
		c.Root.MouseEnter += delegate
		{
			if (_zMode != 0 && _zDragCard == null)
			{
				c.Dash.Stroke = W100;
			}
		};
		c.Root.MouseLeave += delegate
		{
			if (_zDragCard == null)
			{
				c.Dash.Stroke = White(128);
			}
		};
		_zCanvas.Children.Add(c.Root);
		Panel.SetZIndex(c.Root, 1);
		ZoneTone(c);
		ZoneEditVisual(c);
		return c;
	}

	private void ZoneFill(ZoneCard c, ZoneItem it, string eff)
	{
		c.Key = it.Type + "|" + it.Size + "|" + eff + "|" + it.Ref;
		c.Glass.Child = ZoneContent(it, eff, preview: false);
		c.Tools.Children.Clear();
		string[] sizes = LaunchZone.SizesOf(it, RefTypeOf);
		c.SizeBtn = null;
		if (sizes.Length > 1)
		{
			string id = it.Id;
			c.SizeBtn = ZToolBtn(ZText(LaunchZone.DimLabel(eff), 12.0, bold: true), I18n.L("切换尺寸"), del: false, delegate
			{
				ZoneCycleSize(id);
			});
			c.Tools.Children.Add(c.SizeBtn);
		}
		string rid = it.Id;
		Border del = ZToolBtn(ZIcon(IcoClose, 11.0, null, 2.0), I18n.L("移除"), del: true, delegate
		{
			ZoneRemove(rid);
		});
		del.Margin = new Thickness(4.0, 0.0, 0.0, 0.0);
		c.Tools.Children.Add(del);
	}

	// .tools button:22 高、最小 22 宽、左右 6、圆角 8、92% 白底、墨色 12 号粗体;移除钮悬停变主题色
	private Border ZToolBtn(FrameworkElement child, string tip, bool del, Action click)
	{
		Border b = new Border
		{
			Height = 22.0,
			MinWidth = 22.0,
			Padding = new Thickness(6.0, 0.0, 6.0, 0.0),
			CornerRadius = new CornerRadius(8.0),
			Background = White(235),
			Cursor = Cursors.Hand,
			ToolTip = tip,
			Child = child
		};
		child.HorizontalAlignment = HorizontalAlignment.Center;
		child.VerticalAlignment = VerticalAlignment.Center;
		b.SetValue(TextElement.ForegroundProperty, ZInk);
		b.MouseEnter += delegate
		{
			if (del)
			{
				b.Background = AccentA(255);
				b.SetValue(TextElement.ForegroundProperty, W100);
			}
			else
			{
				b.Background = W100;
			}
		};
		b.MouseLeave += delegate
		{
			b.Background = White(235);
			b.SetValue(TextElement.ForegroundProperty, ZInk);
		};
		b.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true; // 不触发拖动
		};
		b.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (_zMode != 0)
			{
				click();
			}
		};
		return b;
	}

	private void ZoneTone(ZoneCard c)
	{
		if (_onImage)
		{
			c.Glass.Background = ZWg;
			c.Glass.BorderBrush = W14;
			c.Glass.SetValue(TextElement.ForegroundProperty, W100);
		}
		else
		{
			c.Glass.SetResourceReference(Border.BackgroundProperty, "B.Paper");
			c.Glass.SetResourceReference(Border.BorderBrushProperty, "B.Line");
			c.Glass.SetResourceReference(TextElement.ForegroundProperty, "B.Ink");
		}
	}

	// 编辑态:1.5 白色 50% 虚线框(悬停纯白)、右上尺寸标签 + 移除;内容不接收鼠标(.win.edit .wg .ct{pointer-events:none})
	private void ZoneEditVisual(ZoneCard c)
	{
		bool edit = _zMode != 0;
		bool drag = _zDragCard == c && _zDragStarted;
		c.Dash.Visibility = (edit && !drag) ? Visibility.Visible : Visibility.Collapsed;
		c.Tools.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
		c.Root.Cursor = edit ? (drag ? Cursors.SizeAll : Cursors.Hand) : null;
		if (c.Glass.Child is UIElement content)
		{
			content.IsHitTestVisible = !edit;
		}
		if (drag)
		{
			c.Glass.BorderThickness = new Thickness(1.5);
			c.Glass.BorderBrush = AccentA(255);
		}
		else
		{
			c.Glass.BorderThickness = edit ? new Thickness(0.0) : new Thickness(1.0);
			if (!edit)
			{
				ZoneTone(c);
			}
		}
		c.Glass.Padding = edit ? new Thickness(15.0, 13.0, 15.0, 13.0) : new Thickness(14.0, 12.0, 14.0, 12.0);
		if (drag)
		{
			c.Glass.Padding = new Thickness(13.5, 11.5, 13.5, 11.5);
		}
	}

	// ————— 拖动(效果图 onDown / onMove / onUp):跟手、其它组件实时让位、红色虚线框预告落点 —————

	private void ZOnDown(object sender, MouseButtonEventArgs e)
	{
		if (_zMode == 0 || !(sender is Grid root) || !(root.Tag is string id) || !_zEls.TryGetValue(id, out var card))
		{
			return;
		}
		_zDragCard = card;
		_zDragId = id;
		_zDragStarted = false;
		_zDragOrder = null;
		_zDragStart = e.GetPosition(_zCanvas);
		_zDragOff = _zDragStart - new Point(Canvas.GetLeft(root), Canvas.GetTop(root));
		root.CaptureMouse();
		e.Handled = true;
	}

	private void ZOnMove(object sender, MouseEventArgs e)
	{
		if (_zDragCard == null || _zDragId == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Point pos = e.GetPosition(_zCanvas);
		if (!_zDragStarted)
		{
			if ((pos - _zDragStart).Length < 4.0)
			{
				return;
			}
			_zDragStarted = true;
			Panel.SetZIndex(_zDragCard.Root, 20);
			_zDragCard.Scale.ScaleX = (_zDragCard.Scale.ScaleY = 1.03);
			_zDragCard.Root.Effect = new DropShadowEffect { BlurRadius = 40.0, ShadowDepth = 18.0, Direction = 270.0, Opacity = 0.45, Color = Colors.Black };
			_zPh.Visibility = Visibility.Visible;
			ZoneEditVisual(_zDragCard);
		}
		double left = pos.X - _zDragOff.X;
		double top = pos.Y - _zDragOff.Y;
		ZMove(_zDragCard.Root, left, top, 0);
		double cx = left + _zDragCard.Root.ActualWidth / 2.0;
		double cy = top + _zDragCard.Root.ActualHeight / 2.0;
		List<ZoneItem>? best = LaunchZone.BestOrder(_zItems, _zDragId, cx, cy, _zCols, _zRows, out var r);
		if (best != null)
		{
			_zDragOrder = best;
			ZoneLayout(best, _zDragId);
			_zPh.Width = LaunchZone.SpanW(r.W);
			_zPh.Height = LaunchZone.SpanH(r.H);
			ZMove(_zPh, LaunchZone.Px(r.X), LaunchZone.Py(r.Y), double.IsNaN(Canvas.GetLeft(_zPh)) ? 0 : 150);
		}
	}

	private void ZOnUp(object sender, MouseButtonEventArgs e)
	{
		if (_zDragCard == null)
		{
			return;
		}
		e.Handled = true;
		ZEndDrag(commit: _zDragStarted);
	}

	private void ZEndDrag(bool commit)
	{
		ZoneCard? card = _zDragCard;
		List<ZoneItem>? order = _zDragOrder;
		_zDragCard = null;
		_zDragId = null;
		_zDragOrder = null;
		bool started = _zDragStarted;
		_zDragStarted = false;
		if (card == null)
		{
			return;
		}
		if (card.Root.IsMouseCaptured)
		{
			card.Root.ReleaseMouseCapture();
		}
		card.Scale.ScaleX = (card.Scale.ScaleY = 1.0);
		card.Root.Effect = null;
		Panel.SetZIndex(card.Root, 1);
		_zPh.Visibility = Visibility.Collapsed;
		ZoneEditVisual(card);
		if (commit && started && order != null && !LaunchZone.SameOrder(order, _zItems))
		{
			ZPushHist();
			_zItems = order;
		}
		ZoneLayout(_zItems, null);
	}

	// ————— 撤销 / 重做 / 尺寸 / 移除 / 添加 / 重置 —————

	private void ZPushHist()
	{
		_zHist.Add(LaunchZone.ToJson(_zItems));
		if (_zHist.Count > 50)
		{
			_zHist.RemoveAt(0);
		}
		_zFut.Clear();
	}

	private void ZoneUndo()
	{
		if (_zHist.Count == 0)
		{
			ZToast(I18n.L("没有可撤销的操作"));
			return;
		}
		_zFut.Add(LaunchZone.ToJson(_zItems));
		_zItems = LaunchZone.FromJson(_zHist[_zHist.Count - 1]) ?? _zItems;
		_zHist.RemoveAt(_zHist.Count - 1);
		ZoneLayout(_zItems, null);
		ZoneRenderLib();
		ZToast(I18n.L("已撤销"));
	}

	private void ZoneRedo()
	{
		if (_zFut.Count == 0)
		{
			ZToast(I18n.L("没有可重做的操作"));
			return;
		}
		_zHist.Add(LaunchZone.ToJson(_zItems));
		_zItems = LaunchZone.FromJson(_zFut[_zFut.Count - 1]) ?? _zItems;
		_zFut.RemoveAt(_zFut.Count - 1);
		ZoneLayout(_zItems, null);
		ZoneRenderLib();
		ZToast(I18n.L("已重做"));
	}

	private void ZoneCycleSize(string id)
	{
		string? next = LaunchZone.NextSize(_zItems, id, _zCols, _zRows, RefTypeOf, out var msg);
		if (next == null)
		{
			if (msg == "one")
			{
				ZToast(I18n.L("窄窗口下这个组件只有一种尺寸"));
			}
			else if (msg != null && msg.StartsWith("nofit:", StringComparison.Ordinal))
			{
				ZToast(I18n.L("这里放不下 {0}，先挪开或移除别的组件", msg.Substring(6)));
			}
			return;
		}
		ZPushHist();
		_zItems = _zItems.Select((ZoneItem i) =>
		{
			if (i.Id != id)
			{
				return i;
			}
			ZoneItem c = i.Clone();
			c.Size = next;
			return c;
		}).ToList();
		ZoneLayout(_zItems, null);
	}

	private void ZoneRemove(string id)
	{
		ZoneItem? it = _zItems.FirstOrDefault((ZoneItem i) => i.Id == id);
		if (it == null)
		{
			return;
		}
		ZPushHist();
		_zItems = _zItems.Where((ZoneItem i) => i.Id != id).ToList();
		ZoneLayout(_zItems, null);
		ZoneRenderLib();
		ZToast(I18n.L("已移除「{0}」 · Ctrl+Z 撤销", ZName(it)));
	}

	private void ZoneAdd(string type, string? refId)
	{
		ZoneItem it;
		if (type == LaunchZone.TypeRef)
		{
			if (refId == null || _zItems.Any((ZoneItem i) => i.Ref == refId))
			{
				return;
			}
			it = new ZoneItem { Type = type, Ref = refId, Size = LaunchZone.SmallestSize(LaunchZone.RefSizes(RefTypeOf(refId))) };
		}
		else
		{
			ZoneType? t = LaunchZone.TypeOf(type);
			if (t == null || (!t.Multi && _zItems.Any((ZoneItem i) => i.Type == type)))
			{
				return;
			}
			it = new ZoneItem { Type = type, Size = LaunchZone.SmallestSize(t.Sizes) };
			if (type == "note" && _zItems.Any((ZoneItem i) => i.Type == "note" && i.Text == null))
			{
				it.Text = ""; // 第二张起自带文字;第一张(旧版启动页那张)继续用 UiPrefs.Notes
			}
		}
		it.Id = LaunchZone.NewId(_zItems);
		List<ZoneItem> trial = new List<ZoneItem>(_zItems) { it };
		if (LaunchZone.Pack(trial, _zCols, _zRows).Over.Contains(it.Id))
		{
			ZToast(I18n.L("组件区放满了：先移除一个，或把大组件换成小尺寸"));
			return;
		}
		ZPushHist();
		_zItems = trial;
		ZoneLayout(_zItems, null);
		ZoneRenderLib();
		if (_zEls.TryGetValue(it.Id, out var card))
		{
			ZFlash(card);
		}
		ZToast(I18n.L("已添加「{0}」", ZName(it)));
	}

	private async void ZFlash(ZoneCard card)
	{
		Color a = AccentA(255).Color;
		card.Root.Effect = new DropShadowEffect { BlurRadius = 30.0, ShadowDepth = 0.0, Color = a, Opacity = 0.9 };
		card.Glass.BorderThickness = new Thickness(2.0);
		card.Glass.BorderBrush = AccentA(255);
		await Task.Delay(900);
		if (_zDragCard != card)
		{
			card.Root.Effect = null;
			ZoneEditVisual(card);
		}
	}

	private void ZoneReset()
	{
		ZPushHist();
		_zItems = LaunchZone.Defaults();
		ZoneLayout(_zItems, null);
		ZoneRenderLib();
		ZToast(I18n.L("已恢复默认布局 · Ctrl+Z 撤销"));
	}

	// ————— 状态切换(效果图 setMode) —————

	/// <summary>右下「编辑组件」/ 右键 / 设置 → 布局「编辑」/ DevShot --edit 共用的入口。</summary>
	public void ZoneBeginEdit()
	{
		ZoneEnsureLoaded();
		if (_zMode == 0)
		{
			_zSavedJson = LaunchZone.ToJson(_zItems);
			_zHist.Clear();
			_zFut.Clear();
		}
		ZoneSetMode(1);
	}

	/// <summary>打开组件库(DevShot --pop layout-drawer 也走这里)。</summary>
	public void ZoneOpenLibrary()
	{
		ZoneBeginEdit();
		ZoneSetMode(2);
	}

	public bool ZoneEditing => _zMode != 0;

	private void ZoneSetMode(int m)
	{
		if (m == 0 && _zMode != 0)
		{
			if (_zDragCard != null)
			{
				ZEndDrag(commit: false);
			}
			string json = LaunchZone.ToJson(_zItems);
			if (json != _zSavedJson)
			{
				// 编辑器提交:这时才写新格式(旧版 launch 页区块原样留在布局文档里,不改不删)
				if (PageBase.Win != null)
				{
					PageBase.Win.Prefs.LaunchZone = json;
					PageBase.Win.SaveUiPrefs();
				}
				_zSavedJson = json;
				if (_zHist.Count > 0)
				{
					ZToast(I18n.L("布局已保存"));
				}
			}
		}
		_zMode = m;
		bool edit = m != 0;
		if (_zMenu != null)
		{
			_zMenu.Visibility = Visibility.Collapsed;
		}
		if (_zDim != null)
		{
			_zDim.Visibility = Visibility.Visible;
			ZFade(_zDim, edit ? 1.0 : 0.0, 250, collapseAtZero: true);
		}
		if (_hero != null)
		{
			ZFade(_hero, edit ? 0.45 : 1.0, 250, collapseAtZero: false);
			_hero.IsHitTestVisible = !edit;
		}
		if (_heroLockChip != null)
		{
			_heroLockChip.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
		}
		foreach (FrameworkElement? f in new FrameworkElement?[2] { _newsCard, _aiPill })
		{
			if (f != null)
			{
				ZFade(f, edit ? 0.0 : 1.0, 200, collapseAtZero: false);
				f.IsHitTestVisible = !edit;
			}
		}
		_zCells.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
		if (_zEditBtn != null)
		{
			_zEditBtn.Visibility = edit ? Visibility.Collapsed : Visibility.Visible;
		}
		if (_zBar != null)
		{
			_zBar.Visibility = (m == 1) ? Visibility.Visible : Visibility.Collapsed;
		}
		if (_zDrawer != null)
		{
			if (m == 2)
			{
				ZoneRenderLib();
				_zDrawer.Visibility = Visibility.Visible;
			}
			ZSlideDrawer(m == 2);
		}
		foreach (ZoneCard c in _zEls.Values)
		{
			ZoneEditVisual(c);
		}
		Window? w = Window.GetWindow(this);
		if (edit && _zKeyWin == null && w != null)
		{
			_zKeyWin = w;
			w.PreviewKeyDown += ZOnKey;
		}
		else if (!edit && _zKeyWin != null)
		{
			_zKeyWin.PreviewKeyDown -= ZOnKey;
			_zKeyWin = null;
		}
		OnLayoutEditingChanged(edit);
	}

	private void ZOnKey(object sender, KeyEventArgs e)
	{
		if (_zMode == 0 || !IsVisible)
		{
			return;
		}
		Key k = (e.Key == Key.System) ? e.SystemKey : e.Key;
		bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
		bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		if (Keyboard.FocusedElement is TextBoxBase)
		{
			return;
		}
		if (ctrl && k == Key.Z && !shift)
		{
			e.Handled = true;
			ZoneUndo();
		}
		else if (ctrl && (k == Key.Y || (k == Key.Z && shift)))
		{
			e.Handled = true;
			ZoneRedo();
		}
		else if (k == Key.Escape)
		{
			e.Handled = true;
			ZoneSetMode((_zMode == 2) ? 1 : 0);
		}
	}

	private static void ZFade(UIElement el, double to, int ms, bool collapseAtZero)
	{
		if (!Motion.Enabled)
		{
			el.BeginAnimation(UIElement.OpacityProperty, null);
			el.Opacity = to;
			if (collapseAtZero && to <= 0.0)
			{
				el.Visibility = Visibility.Collapsed;
			}
			return;
		}
		DoubleAnimation a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
		if (collapseAtZero && to <= 0.0)
		{
			a.Completed += delegate
			{
				if (el.Opacity <= 0.01)
				{
					el.Visibility = Visibility.Collapsed;
				}
			};
		}
		el.BeginAnimation(UIElement.OpacityProperty, a);
	}

	private void ZSlideDrawer(bool open)
	{
		double to = open ? 0.0 : -351.0;
		if (!Motion.Enabled)
		{
			_zDrawerT.BeginAnimation(TranslateTransform.XProperty, null);
			_zDrawerT.X = to;
			if (!open && _zDrawer != null)
			{
				_zDrawer.Visibility = Visibility.Collapsed;
			}
			return;
		}
		DoubleAnimation a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(280))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
		if (!open)
		{
			a.Completed += delegate
			{
				if (_zMode != 2 && _zDrawer != null)
				{
					_zDrawer.Visibility = Visibility.Collapsed;
				}
			};
		}
		_zDrawerT.BeginAnimation(TranslateTransform.XProperty, a);
	}

	private void ZToast(string msg)
	{
		if (_zToast == null)
		{
			return;
		}
		_zToastText.Text = msg;
		_zToast.Opacity = 1.0;
		_zToastT.Y = 0.0;
		if (_zToastTimer == null)
		{
			_zToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800.0) };
			_zToastTimer.Tick += delegate
			{
				_zToastTimer.Stop();
				ZFade(_zToast, 0.0, 200, collapseAtZero: false);
				_zToastT.Y = 8.0;
			};
		}
		_zToastTimer.Stop();
		_zToastTimer.Start();
	}

	// ————— 编辑条(.ebar:.ebox + .hint + ⋯ 菜单) —————

	private FrameworkElement BuildZoneBar()
	{
		TextBlock h3 = ZText(I18n.L("编辑启动页"), 13.0, bold: true);
		h3.FontWeight = FontWeights.ExtraBold;
		h3.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
		Border add = ZBarBtn(I18n.L("添加组件"), IcoPlus, 1, delegate
		{
			ZoneSetMode(2);
		});
		Border reset = ZBarBtn(I18n.L("重置"), null, 0, ZoneReset);
		Border done = ZBarBtn(I18n.L("完成"), null, 2, delegate
		{
			ZoneSetMode(0);
		});
		_zMoreBtn = ZBarBtn(null, IcoMore, 3, delegate
		{
			if (_zMenu != null)
			{
				_zMenu.Visibility = (_zMenu.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
			}
		});
		_zMoreBtn.ToolTip = I18n.L("更多");
		StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
		row.Children.Add(h3);
		foreach (Border b in new Border[4] { add, reset, done, _zMoreBtn })
		{
			b.Margin = new Thickness((row.Children.Count > 1) ? 8.0 : 0.0, 0.0, 0.0, 0.0);
			row.Children.Add(b);
		}
		Border ebox = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(14.0, 8.0, 8.0, 8.0),
			Background = White(247),
			HorizontalAlignment = HorizontalAlignment.Left,
			Effect = new DropShadowEffect { BlurRadius = 30.0, ShadowDepth = 10.0, Direction = 270.0, Opacity = 0.35, Color = Colors.Black },
			Child = row
		};
		ebox.SetValue(TextElement.ForegroundProperty, ZInk);
		h3.VerticalAlignment = VerticalAlignment.Center;
		WrapPanel hint = new WrapPanel { Margin = new Thickness(4.0, 8.0, 0.0, 0.0) };
		hint.SetValue(TextElement.ForegroundProperty, White(184));
		hint.Children.Add(ZText(I18n.L("拖动组件换位置 · 点尺寸标签切换大小 · "), 12.0));
		hint.Children.Add(ZKbd("Ctrl Z", 0.0));
		hint.Children.Add(ZText(" " + I18n.L("撤销") + " ", 12.0));
		hint.Children.Add(ZKbd("Ctrl Y", 0.0));
		hint.Children.Add(ZText(" " + I18n.L("重做"), 12.0));
		StackPanel menuList = new StackPanel();
		menuList.Children.Add(ZMenuItem(I18n.L("导入布局…"), ZoneImport));
		menuList.Children.Add(ZMenuItem(I18n.L("导出布局…"), ZoneExport));
		menuList.Children.Add(ZMenuItem(I18n.L("复制布局码"), delegate
		{
			try
			{
				Clipboard.SetText(LaunchZone.ToCode(_zItems));
				ZToast(I18n.L("已复制布局码，朋友粘贴到「导入布局」即可"));
			}
			catch
			{
				ZToast(I18n.L("复制失败，剪贴板被占用"));
			}
		}));
		menuList.Children.Add(new Border { Height = 1.0, Background = ZLine, Margin = new Thickness(6.0, 4.0, 6.0, 4.0) });
		menuList.Children.Add(ZMenuItem(I18n.L("放弃这次改动"), delegate
		{
			ZPushHist();
			_zItems = LaunchZone.FromJson(_zSavedJson) ?? _zItems;
			ZoneLayout(_zItems, null);
			ZToast(I18n.L("已恢复到上次保存的布局"));
		}));
		_zMenu = new Border
		{
			Width = 200.0,
			Padding = new Thickness(6.0),
			CornerRadius = new CornerRadius(12.0),
			Background = Brushes.White,
			Effect = new DropShadowEffect { BlurRadius = 32.0, ShadowDepth = 12.0, Direction = 270.0, Opacity = 0.35, Color = Colors.Black },
			Visibility = Visibility.Collapsed,
			Child = menuList
		};
		_zMenu.SetValue(TextElement.ForegroundProperty, ZInk);
		Canvas menuHost = new Canvas { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
		Canvas.SetRight(_zMenu, 0.0);
		Canvas.SetTop(_zMenu, 46.0);
		menuHost.Children.Add(_zMenu);
		StackPanel v = new StackPanel();
		v.Children.Add(ebox);
		v.Children.Add(hint);
		Grid g = new Grid();
		g.Children.Add(v);
		g.Children.Add(menuHost);
		return g;
	}

	// .b:30 高、圆角 8、左右 12、13 号;kind 0 普通 / 1 soft(添加)/ 2 pri(完成)/ 3 ic(⋯ 30×30)
	private Border ZBarBtn(string? text, string? icon, int kind, Action click)
	{
		StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
		if (icon != null)
		{
			bool fill = icon == IcoMore;
			content.Children.Add(fill ? ZIcon(null, 14.0, icon) : ZIcon(icon, 13.0));
		}
		if (text != null)
		{
			TextBlock t = ZText(text, 13.0, kind == 1 || kind == 2);
			if (icon != null)
			{
				t.Margin = new Thickness(6.0, 0.0, 0.0, 0.0);
			}
			content.Children.Add(t);
		}
		Border b = new Border
		{
			Height = 30.0,
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = (kind == 3) ? new Thickness(0.0) : new Thickness(12.0, 0.0, 12.0, 0.0),
			Cursor = Cursors.Hand,
			Child = content
		};
		if (kind == 3)
		{
			b.Width = 30.0;
		}
		Action tone = delegate
		{
			bool hover = b.IsMouseOver;
			switch (kind)
			{
			case 1:
				b.Background = hover ? AccentA(255) : AccentA(31);
				b.BorderBrush = AccentA(82);
				b.SetValue(TextElement.ForegroundProperty, hover ? W100 : AccentA(255));
				break;
			case 2:
				b.Background = AccentA(255);
				b.BorderBrush = AccentA(255);
				b.SetValue(TextElement.ForegroundProperty, W100);
				b.Opacity = hover ? 0.94 : 1.0;
				break;
			default:
				b.Background = hover ? ZPaperHover : ZPaper2;
				b.BorderBrush = ZLine;
				b.SetValue(TextElement.ForegroundProperty, ZInk);
				break;
			}
		};
		tone();
		b.MouseEnter += delegate
		{
			tone();
		};
		b.MouseLeave += delegate
		{
			tone();
		};
		b.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
			click();
		};
		return b;
	}

	private Border ZMenuItem(string text, Action click)
	{
		Border b = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 7.0, 10.0, 7.0),
			Background = Brushes.Transparent,
			Cursor = Cursors.Hand,
			Child = ZText(text, 13.0)
		};
		b.MouseEnter += delegate
		{
			b.Background = ZPaper2;
		};
		b.MouseLeave += delegate
		{
			b.Background = Brushes.Transparent;
		};
		b.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (_zMenu != null)
			{
				_zMenu.Visibility = Visibility.Collapsed;
			}
			click();
		};
		return b;
	}

	private void ZoneExport()
	{
		try
		{
			Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog
			{
				FileName = "launch.tipcl-layout.json",
				Filter = I18n.L("TipCL 布局") + " (*.tipcl-layout.json)|*.tipcl-layout.json|JSON (*.json)|*.json"
			};
			if (dlg.ShowDialog(Window.GetWindow(this)) == true)
			{
				System.IO.File.WriteAllText(dlg.FileName, LaunchZone.ToJson(_zItems), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				ZToast(I18n.L("已导出布局"));
			}
		}
		catch (Exception ex)
		{
			ZToast(I18n.L("导出失败：") + ex.Message);
		}
	}

	private void ZoneImport()
	{
		try
		{
			// 先看剪贴板里是不是布局码(「复制布局码」→ 朋友粘贴到「导入布局」),不是就选文件
			string clip = "";
			try
			{
				clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
			}
			catch
			{
			}
			List<ZoneItem>? items = clip.TrimStart().StartsWith(LaunchZone.CodePrefix, StringComparison.Ordinal) ? LaunchZone.FromCodeOrJson(clip) : null;
			if (items == null)
			{
				Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
				{
					Filter = I18n.L("TipCL 布局") + " (*.tipcl-layout.json)|*.tipcl-layout.json|JSON (*.json)|*.json|" + I18n.L("所有文件") + "|*.*"
				};
				if (dlg.ShowDialog(Window.GetWindow(this)) != true)
				{
					return;
				}
				items = LaunchZone.FromCodeOrJson(System.IO.File.ReadAllText(dlg.FileName));
			}
			if (items == null)
			{
				ZToast(I18n.L("这不是启动页组件布局"));
				return;
			}
			ZPushHist();
			_zItems = items;
			ZoneLayout(_zItems, null);
			ZToast(I18n.L("已导入布局 · Ctrl+Z 撤销"));
		}
		catch (Exception ex)
		{
			ZToast(I18n.L("导入失败：") + ex.Message);
		}
	}

	// ————— 组件库(.drawer / .lc) —————

	private Border BuildZoneDrawer()
	{
		TextBlock h3 = ZText(I18n.L("组件库"), 15.0, bold: true);
		h3.FontWeight = FontWeights.ExtraBold;
		TextBlock p = ZText(I18n.L("点「添加」放进右侧组件区，放满了会提示"), 12.0);
		p.Foreground = ZInk3;
		p.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
		p.TextWrapping = TextWrapping.Wrap;
		StackPanel titles = new StackPanel();
		titles.Children.Add(h3);
		titles.Children.Add(p);
		Border close = ZBarBtn(null, IcoClose, 3, delegate
		{
			ZoneSetMode(1);
		});
		close.ToolTip = I18n.L("关闭");
		close.VerticalAlignment = VerticalAlignment.Top;
		Grid head = new Grid { Margin = new Thickness(16.0, 16.0, 16.0, 12.0) };
		head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		head.Children.Add(titles);
		Grid.SetColumn(close, 1);
		close.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		head.Children.Add(close);
		_zLibList.Margin = new Thickness(16.0, 0.0, 16.0, 16.0);
		ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _zLibList };
		DockPanel dock = new DockPanel();
		DockPanel.SetDock(head, Dock.Top);
		dock.Children.Add(head);
		dock.Children.Add(sv);
		Border drawer = new Border
		{
			Width = 344.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Stretch,
			Background = Brushes.White,
			RenderTransform = _zDrawerT,
			Visibility = Visibility.Collapsed,
			Effect = new DropShadowEffect { BlurRadius = 40.0, ShadowDepth = 12.0, Direction = 0.0, Opacity = 0.35, Color = Colors.Black },
			Child = dock
		};
		drawer.SetValue(TextElement.ForegroundProperty, ZInk);
		return drawer;
	}

	private void ZoneRenderLib()
	{
		if (_zMode != 2)
		{
			return;
		}
		_zLibList.Children.Clear();
		foreach (ZoneType t in LaunchZone.Types)
		{
			bool used = !t.Multi && _zItems.Any((ZoneItem i) => i.Type == t.Key);
			string key = t.Key;
			ZoneItem sample = new ZoneItem { Id = "lib-" + t.Key, Type = t.Key, Size = t.Sizes.Contains(LaunchZone.Small) ? LaunchZone.Small : t.Sizes[0] };
			_zLibList.Children.Add(ZLibRow(sample, I18n.L(t.Name), I18n.L(t.Desc), t.Sizes, used, delegate
			{
				ZoneAdd(key, null);
			}));
		}
		// 用户自建的网页 / 按钮 / 快捷方式 / 分隔线组件也能放进组件区(游戏时长 / 时钟 / 便签与上面的内置组件同一种,不重复列)
		try
		{
			foreach (WidgetDef w in WidgetRegistry.AllWidgets())
			{
				if (w.Type == "playtime" || w.Type == "clock" || w.Type == "note")
				{
					continue;
				}
				string refId = LayoutEngine.CustomPrefix + w.Id;
				string[] sizes = LaunchZone.RefSizes(w.Type);
				bool used = _zItems.Any((ZoneItem i) => i.Ref == refId);
				ZoneItem sample = new ZoneItem { Id = "lib-" + refId, Type = LaunchZone.TypeRef, Ref = refId, Size = LaunchZone.SmallestSize(sizes) };
				_zLibList.Children.Add(ZLibRow(sample, string.IsNullOrWhiteSpace(w.Title) ? I18n.L("自建组件") : w.Title, I18n.L("你建的组件"), sizes, used, delegate
				{
					ZoneAdd(LaunchZone.TypeRef, refId);
				}));
			}
		}
		catch
		{
		}
	}

	private FrameworkElement ZLibRow(ZoneItem sample, string name, string desc, string[] sizes, bool used, Action add)
	{
		// 缩略图:112×70,截图背景压 45% 暗,中间是按 2×1(没有就第一档)画的真组件,等比缩到 104×62 以内
		Border thumb = new Border { Height = 70.0, CornerRadius = new CornerRadius(8.0), ClipToBounds = true };
		Grid tg = new Grid();
		if (_bgImage.Source != null)
		{
			tg.Children.Add(new Rectangle { Fill = new ImageBrush(_bgImage.Source) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Center } });
		}
		else
		{
			tg.Children.Add(new Rectangle { Fill = ZSc(255, 0x1B, 0x22, 0x30) });
		}
		tg.Children.Add(new Rectangle { Fill = ZSc(115, 10, 14, 22) });
		double w = LaunchZone.SpanW(LaunchZone.W(sample.Size));
		double h = LaunchZone.SpanH(LaunchZone.H(sample.Size));
		double k = Math.Min(104.0 / w, 62.0 / h);
		Border mini = new Border
		{
			Width = w,
			Height = h,
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(14.0, 12.0, 14.0, 12.0),
			Background = ZWg,
			BorderBrush = W14,
			ClipToBounds = true,
			IsHitTestVisible = false,
			Child = ZoneContent(sample, sample.Size, preview: true)
		};
		mini.SetValue(TextElement.ForegroundProperty, W100);
		tg.Children.Add(new Viewbox { Width = w * k, Height = h * k, Stretch = Stretch.Uniform, Child = mini, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
		thumb.Child = tg;
		StackPanel li = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12.0, 0.0, 12.0, 0.0) };
		li.Children.Add(ZText(name, 13.0, bold: true));
		TextBlock d = ZText(desc, 12.0);
		d.Foreground = ZInk2;
		li.Children.Add(d);
		TextBlock dims = ZText(string.Join(" · ", sizes.Select(LaunchZone.DimLabel)), 12.0);
		dims.Foreground = ZInk3;
		dims.FontFamily = ZMono;
		li.Children.Add(dims);
		Border btn = new Border
		{
			Height = 28.0,
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(12.0, 0.0, 12.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = ZText(used ? I18n.L("已添加") : I18n.L("添加"), 13.0, !used)
		};
		((FrameworkElement)btn.Child).VerticalAlignment = VerticalAlignment.Center;
		if (used)
		{
			btn.Background = Brushes.Transparent;
			btn.BorderBrush = Brushes.Transparent;
			btn.SetValue(TextElement.ForegroundProperty, ZInk3);
		}
		else
		{
			btn.Cursor = Cursors.Hand;
			Action tone = delegate
			{
				btn.Background = btn.IsMouseOver ? AccentA(255) : AccentA(31);
				btn.BorderBrush = AccentA(82);
				btn.SetValue(TextElement.ForegroundProperty, btn.IsMouseOver ? W100 : AccentA(255));
			};
			tone();
			btn.MouseEnter += delegate
			{
				tone();
			};
			btn.MouseLeave += delegate
			{
				tone();
			};
			btn.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
			{
				e.Handled = true;
				add();
			};
		}
		Grid g = new Grid();
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112.0) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		g.Children.Add(thumb);
		Grid.SetColumn(li, 1);
		g.Children.Add(li);
		Grid.SetColumn(btn, 2);
		g.Children.Add(btn);
		return new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(8.0),
			Background = ZPaper2,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			Child = g
		};
	}

	// ————— 组件内容(效果图 content()) —————

	private FrameworkElement ZoneContent(ZoneItem it, string es, bool preview)
	{
		try
		{
			switch (it.Type)
			{
			case "play":
				return ZPlay(es);
			case "server":
				return ZServer(es, preview);
			case "short":
				return ZShort(es, preview);
			case "note":
				return ZNote(it, es, preview);
			case "clock":
				return ZClockContent(es);
			case "shots":
				return ZShots(es);
			case "tasks":
				return ZTasks(es);
			case "log":
				return ZLogContent(es);
			case LaunchZone.TypeRef:
				return ZRef(it, es, preview);
			}
		}
		catch (Exception ex)
		{
			return ZStack(ZLabel(ZName(it)), ZSub(I18n.L("组件暂时不可用：") + ex.Message));
		}
		return ZStack(ZLabel(it.Type));
	}

	// .lb:12 号、68% 不透明、单行省略
	private static TextBlock ZLabel(string text)
	{
		TextBlock t = ZText(text, 12.0);
		t.Opacity = 0.68;
		t.TextTrimming = TextTrimming.CharacterEllipsis;
		return t;
	}

	// .big:21 号 800、行高 1.3;xl = 34 号、行高 1.2;small:13 号 400、70%
	private static TextBlock ZBig(string num, string? small, bool xl)
	{
		TextBlock t = new TextBlock { FontSize = xl ? 34.0 : 21.0, FontWeight = FontWeights.ExtraBold, LineHeight = xl ? 40.8 : 27.3, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis };
		t.Inlines.Add(new Run(num));
		if (!string.IsNullOrEmpty(small))
		{
			t.Inlines.Add(new Run(small) { FontSize = 13.0, FontWeight = FontWeights.Normal, Foreground = new SolidColorBrush(Color.FromArgb(179, 255, 255, 255)) });
		}
		return t;
	}

	private static TextBlock ZSub(string text)
	{
		TextBlock t = ZText(text, 12.0);
		t.Opacity = 0.62;
		t.TextTrimming = TextTrimming.CharacterEllipsis;
		return t;
	}

	private static TextBlock ZMid(string text)
	{
		TextBlock t = ZText(text, 15.0, bold: true);
		t.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
		t.TextWrapping = TextWrapping.Wrap;
		return t;
	}

	private static StackPanel ZStack(params UIElement[] kids)
	{
		StackPanel s = new StackPanel();
		foreach (UIElement k in kids)
		{
			if (k is FrameworkElement f && s.Children.Count > 0 && f.Margin.Top == 0.0)
			{
				f.Margin = new Thickness(f.Margin.Left, 2.0, f.Margin.Right, f.Margin.Bottom);
			}
			s.Children.Add(k);
		}
		return s;
	}

	private FrameworkElement ZPlay(string es)
	{
		WeekPlay wp = _zWeek;
		KeyValuePair<string, string> big = LaunchZone.HoursText(wp.ThisWeek);
		string unit = " " + I18n.L(big.Value);
		string label = I18n.L("本周游戏时长");
		double d = wp.ThisWeek - wp.LastWeek;
		KeyValuePair<string, string> dt = LaunchZone.HoursText(Math.Abs(d));
		string diff = (Math.Abs(d) < 1.0 / 60.0) ? I18n.L("和上周一样") : ((d > 0.0) ? I18n.L("比上周多 {0} {1}", dt.Key, I18n.L(dt.Value)) : I18n.L("比上周少 {0} {1}", dt.Key, I18n.L(dt.Value)));
		if (es == LaunchZone.Small)
		{
			string shortDiff = (Math.Abs(d) < 1.0 / 60.0) ? I18n.L("和上周一样") : ((d > 0.0) ? I18n.L("比上周多 {0}", dt.Key) : I18n.L("比上周少 {0}", dt.Key));
			return ZStack(ZLabel(label), ZBig(big.Key, unit + " · " + shortDiff, xl: false));
		}
		if (es == LaunchZone.Medium)
		{
			Grid g = new Grid();
			g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
			g.Children.Add(ZStack(ZLabel(label), ZBig(big.Key, unit, xl: false), ZSub(diff)));
			FrameworkElement bars = ZBars(62.0);
			Grid.SetRow(bars, 1);
			g.Children.Add(bars);
			return g;
		}
		StackPanel left = ZStack(ZLabel(label), ZBig(big.Key, unit, xl: true), ZSub(diff));
		if (!string.IsNullOrEmpty(wp.Top))
		{
			TextBlock top = ZSub(I18n.L("玩得最多：{0}", wp.Top!));
			top.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
			left.Children.Add(top);
		}
		left.VerticalAlignment = VerticalAlignment.Center;
		Grid hz = new Grid();
		hz.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		hz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16.0) });
		hz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		hz.Children.Add(left);
		FrameworkElement b = ZBars(118.0);
		Grid.SetColumn(b, 2);
		hz.Children.Add(b);
		return hz;
	}

	// .bars:7 根柱(周一 … 周日),间距 6,柱高 = 当天 / 本周最多(最低 4%);今天主题色,今天之后虚线占位 28%
	private FrameworkElement ZBars(double h)
	{
		WeekPlay wp = _zWeek;
		double max = wp.Hours.Where((double? v) => v.HasValue).Select((double? v) => v!.Value).DefaultIfEmpty(0.0).Max();
		Grid g = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
		string days = I18n.L("一二三四五六日");
		for (int i = 0; i < 7; i++)
		{
			if (i > 0)
			{
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6.0) });
			}
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
			double? v = wp.Hours[i];
			Grid bt = new Grid { Height = h };
			FrameworkElement bar;
			if (!v.HasValue)
			{
				bar = new Rectangle { RadiusX = 4.0, RadiusY = 4.0, Stroke = White(77), StrokeThickness = 1.0, StrokeDashArray = new DoubleCollection { 3.0, 3.0 }, Height = h * 0.28 };
			}
			else
			{
				double pct = (max > 0.0) ? Math.Max(0.04, v.Value / max) : 0.04;
				bar = new Border { CornerRadius = new CornerRadius(4.0), Height = Math.Max(2.0, h * pct), Background = (i == wp.Today) ? AccentA(255) : White(128) };
			}
			bar.VerticalAlignment = VerticalAlignment.Bottom;
			bt.Children.Add(bar);
			TextBlock lab = ZText((days.Length >= 7) ? days.Substring(i, 1) : (i + 1).ToString(), 12.0);
			lab.Opacity = 0.55;
			lab.HorizontalAlignment = HorizontalAlignment.Center;
			lab.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
			lab.LineHeight = 12.0;
			lab.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			StackPanel bc = new StackPanel();
			bc.Children.Add(bt);
			bc.Children.Add(lab);
			Grid.SetColumn(bc, g.ColumnDefinitions.Count - 1);
			g.Children.Add(bc);
		}
		return g;
	}

	private FrameworkElement ZServer(string es, bool preview)
	{
		ServerRow? s = _zServer;
		ServerPingResult? p = _zPing;
		string name = (s == null) ? I18n.L("服务器状态") : (string.IsNullOrWhiteSpace(s.Name) ? s.Ip : s.Name);
		Ellipse dot = new Ellipse { Width = 7.0, Height = 7.0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0.0, 0.0, 6.0, 0.0) };
		dot.Fill = (p != null) ? ZOk : White(102);
		if (p != null)
		{
			dot.Effect = new DropShadowEffect { BlurRadius = 8.0, ShadowDepth = 0.0, Color = ZOk.Color, Opacity = 1.0 };
		}
		StackPanel lb = new StackPanel { Orientation = Orientation.Horizontal };
		lb.Children.Add(dot);
		TextBlock lt = ZLabel(name);
		lt.Opacity = 1.0;
		lb.Opacity = 0.68;
		lb.Children.Add(lt);
		string status = (s == null) ? I18n.L("还没有服务器") : ((p != null) ? I18n.L("{0} / {1} 在线 · {2} ms", p.Online, p.Max, p.Ms) : ((_zPingState == 1) ? I18n.L("正在连接…") : I18n.L("离线")));
		if (es == LaunchZone.Small || preview && s == null)
		{
			return ZStack(lb, ZMid(status));
		}
		Grid g = new Grid();
		for (int i = 0; i < 5; i++)
		{
			g.RowDefinitions.Add(new RowDefinition { Height = (i == 3) ? new GridLength(1.0, GridUnitType.Star) : GridLength.Auto });
		}
		g.Children.Add(lb);
		FrameworkElement big = (p != null) ? ZBig(p.Online.ToString(), " / " + p.Max + " " + I18n.L("在线"), xl: false) : (FrameworkElement)ZMid(status);
		big.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
		Grid.SetRow(big, 1);
		g.Children.Add(big);
		if (s != null)
		{
			TextBlock mono = ZSub(s.Ip + ":" + s.Port + ((p != null) ? (" · " + p.Ms + " ms") : ""));
			mono.FontFamily = ZMono;
			mono.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
			Grid.SetRow(mono, 2);
			g.Children.Add(mono);
		}
		if (p != null && p.Sample.Count > 0)
		{
			StackPanel heads = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, 8.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Top };
			foreach (string n in p.Sample.Take(5))
			{
				heads.Children.Add(new Border
				{
					Width = 24.0,
					Height = 24.0,
					CornerRadius = new CornerRadius(12.0),
					Background = White(51),
					BorderBrush = ZHeadLine,
					BorderThickness = new Thickness(1.5),
					Margin = new Thickness(0.0, 0.0, -6.0, 0.0),
					Child = new TextBlock { Text = n.Substring(0, 1).ToUpperInvariant(), FontSize = 12.0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
				});
			}
			int more = Math.Max(0, p.Online - Math.Min(5, p.Sample.Count));
			if (more > 0)
			{
				TextBlock em = ZText("+" + more, 12.0);
				em.Opacity = 0.6;
				em.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);
				em.VerticalAlignment = VerticalAlignment.Center;
				heads.Children.Add(em);
			}
			Grid.SetRow(heads, 3);
			g.Children.Add(heads);
		}
		Border join = new Border
		{
			Height = 30.0,
			CornerRadius = new CornerRadius(8.0),
			Background = AccentA(255),
			Cursor = Cursors.Hand,
			Child = new TextBlock { Text = (s == null) ? I18n.L("添加服务器") : I18n.L("加入服务器"), FontSize = 13.0, FontWeight = FontWeights.Bold, Foreground = W100, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
		};
		if (!preview)
		{
			join.MouseLeftButtonUp += delegate
			{
				ZJoinServer();
			};
		}
		Grid.SetRow(join, 4);
		g.Children.Add(join);
		return g;
	}

	private void ZJoinServer()
	{
		ServerRow? s = _zServer;
		if (s == null)
		{
			PageBase.Win?.Navigate("servers");
			return;
		}
		_server.Text = s.Ip + ":" + s.Port;
		PageBase.Run(LaunchAsync, I18n.L("启动失败"));
	}

	private FrameworkElement ZShort(string es, bool preview)
	{
		int n = (es == LaunchZone.Small) ? 3 : 4;
		Tuple<string, string, string>[] all = new Tuple<string, string, string>[4]
		{
			Tuple.Create(IcoMods, I18n.L("模组"), "mods"),
			Tuple.Create(IcoCam, I18n.L("截图"), "screenshots"),
			Tuple.Create(IcoLog, I18n.L("日志"), "logs"),
			Tuple.Create(IcoSave, I18n.L("存档"), "saves")
		};
		UniformGrid g = new UniformGrid { Columns = (es == LaunchZone.Small) ? 3 : ((es == LaunchZone.Medium) ? 2 : 4), Margin = new Thickness(-3.0) };
		foreach (Tuple<string, string, string> t in all.Take(n))
		{
			StackPanel sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			FrameworkElement ic = ZIcon(t.Item1, 18.0);
			ic.HorizontalAlignment = HorizontalAlignment.Center;
			sp.Children.Add(ic);
			TextBlock lab = ZText(t.Item2, 12.0);
			lab.HorizontalAlignment = HorizontalAlignment.Center;
			lab.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
			sp.Children.Add(lab);
			Border tile = new Border { CornerRadius = new CornerRadius(8.0), Margin = new Thickness(3.0), Background = _onImage ? White(20) : (Brush)Brushes.Transparent, Cursor = Cursors.Hand, Child = sp };
			if (!_onImage)
			{
				tile.SetResourceReference(Border.BackgroundProperty, "B.Hover");
			}
			string sub = t.Item3;
			if (!preview)
			{
				tile.MouseEnter += delegate
				{
					if (_onImage)
					{
						tile.Background = White(41);
					}
				};
				tile.MouseLeave += delegate
				{
					if (_onImage)
					{
						tile.Background = White(20);
					}
				};
				tile.MouseLeftButtonUp += delegate
				{
					ZOpenSub(sub);
				};
			}
			g.Children.Add(tile);
		}
		return g;
	}

	// 快捷入口:当前实例(版本隔离时优先 versions/<版本>/ 下)的 mods / screenshots / logs / saves,不存在就建
	private void ZOpenSub(string sub)
	{
		try
		{
			string inst = _inst.Str();
			string root = _instances.FirstOrDefault((InstanceInfo x) => x.Name == inst)?.Path ?? "";
			if (root.Length == 0)
			{
				ZToast(I18n.L("先选一个实例"));
				return;
			}
			string ver = _ver.Str();
			string iso = string.IsNullOrEmpty(ver) ? "" : System.IO.Path.Combine(root, "versions", ver, sub);
			string dir = (iso.Length > 0 && System.IO.Directory.Exists(iso)) ? iso : System.IO.Path.Combine(root, sub);
			System.IO.Directory.CreateDirectory(dir);
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			ZToast(I18n.L("打不开文件夹：") + ex.Message);
		}
	}

	private FrameworkElement ZNote(ZoneItem it, string es, bool preview)
	{
		string text = it.Text ?? PageBase.Win?.Prefs.Notes ?? "";
		bool one = es == LaunchZone.Small;
		TextBlock lb = ZLabel(I18n.L("便签"));
		if (preview)
		{
			TextBlock nt = ZText((text.Length > 0) ? text : I18n.L("周六晚上和小林联机，记得先装好光影包"), 13.0);
			nt.TextWrapping = one ? TextWrapping.NoWrap : TextWrapping.Wrap;
			nt.TextTrimming = TextTrimming.CharacterEllipsis;
			nt.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
			return ZStack(lb, nt);
		}
		TextBox box = new TextBox
		{
			Style = null,
			Text = text,
			FontSize = 13.0,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			Margin = new Thickness(-2.0, 2.0, 0.0, 0.0),
			AcceptsReturn = !one,
			TextWrapping = one ? TextWrapping.NoWrap : TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			FocusVisualStyle = null
		};
		if (_onImage)
		{
			box.Foreground = W100;
			box.CaretBrush = W100;
		}
		else
		{
			box.SetResourceReference(Control.ForegroundProperty, "B.Ink");
			box.SetResourceReference(TextBoxBase.CaretBrushProperty, "B.Ink");
		}
		TextBlock ph = ZText(I18n.L("随手记点什么…"), 13.0);
		ph.Opacity = 0.45;
		ph.IsHitTestVisible = false;
		ph.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
		ph.Visibility = (text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
		box.TextChanged += delegate
		{
			ph.Visibility = (box.Text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
		};
		string id = it.Id;
		box.LostFocus += delegate
		{
			ZSaveNote(id, box.Text);
		};
		Grid g = new Grid();
		g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		g.Children.Add(lb);
		Grid.SetRow(box, 1);
		Grid.SetRow(ph, 1);
		g.Children.Add(box);
		g.Children.Add(ph);
		return g;
	}

	private void ZSaveNote(string id, string text)
	{
		ZoneItem? it = _zItems.FirstOrDefault((ZoneItem i) => i.Id == id);
		if (it == null || PageBase.Win == null)
		{
			return;
		}
		if (it.Text == null)
		{
			// 旧版启动页那张便签:继续写 UiPrefs.Notes(和以前一样),不碰组件区存档
			if (PageBase.Win.Prefs.Notes != text)
			{
				PageBase.Win.Prefs.Notes = text;
				_notes.Text = text;
				PageBase.Win.SaveUiPrefs();
			}
			return;
		}
		if (it.Text == text)
		{
			return;
		}
		it.Text = text;
		// 第二张起的便签是编辑器里加的,组件区已经是新格式:文字随存档一起写
		if (_zMode == 0 && LaunchZone.FromJson(PageBase.Win.Prefs.LaunchZone) != null)
		{
			string json = LaunchZone.ToJson(_zItems);
			PageBase.Win.Prefs.LaunchZone = json;
			_zSavedJson = json;
			PageBase.Win.SaveUiPrefs();
		}
	}

	private FrameworkElement ZClockContent(string es)
	{
		DateTime now = DateTime.Now;
		string t = now.ToString("HH:mm");
		string d = I18n.L("{0}月{1}日 周{2}", now.Month, now.Day, I18n.L("日一二三四五六").Substring((int)now.DayOfWeek, 1));
		if (es == LaunchZone.Small)
		{
			return ZStack(ZBig(t, null, xl: false), ZSub(d));
		}
		Grid g = new Grid();
		g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		g.Children.Add(ZLabel(I18n.L("本地时间")));
		StackPanel mid = ZStack(ZBig(t, null, xl: true), ZSub(d));
		mid.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetRow(mid, 1);
		g.Children.Add(mid);
		return g;
	}

	private FrameworkElement ZShots(string es)
	{
		int n = (es == LaunchZone.Large) ? 6 : 4;
		Grid g = new Grid();
		g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		g.Children.Add(ZLabel(I18n.L("最近截图 · 共 {0} 张", _zShotFiles.Count)));
		FrameworkElement body;
		if (_zShotFiles.Count == 0)
		{
			body = ZSub(I18n.L("还没有截图，游戏里按 F2"));
			body.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
		}
		else
		{
			UniformGrid ug = new UniformGrid { Columns = (es == LaunchZone.Large) ? 3 : 2, Margin = new Thickness(-3.0, 3.0, -3.0, -3.0) };
			foreach (string f in _zShotFiles.Take(n))
			{
				Border cell = new Border { CornerRadius = new CornerRadius(8.0), Margin = new Thickness(3.0), Background = White(20) };
				if (_zThumbs.TryGetValue(f, out var bmp))
				{
					cell.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
				}
				string path = f;
				cell.Cursor = Cursors.Hand;
				cell.MouseLeftButtonUp += delegate
				{
					try
					{
						Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
					}
					catch
					{
					}
				};
				ug.Children.Add(cell);
			}
			body = ug;
		}
		Grid.SetRow(body, 1);
		g.Children.Add(body);
		return g;
	}

	private FrameworkElement ZTasks(string es)
	{
		List<TaskRow> list = TaskStore.Rows.Reverse().Take((es == LaunchZone.Small) ? 1 : 4).ToList();
		StackPanel s = ZStack(ZLabel(I18n.L("下载任务")));
		if (list.Count == 0)
		{
			s.Children.Add(ZMid(I18n.L("暂无下载任务")));
			return s;
		}
		foreach (TaskRow t in list)
		{
			Grid row = new Grid { Margin = new Thickness(0.0, (es == LaunchZone.Small) ? 4.0 : 6.0, 0.0, 0.0) };
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			TextBlock title = ZText(t.Title, 13.0, es == LaunchZone.Small);
			title.TextTrimming = TextTrimming.CharacterEllipsis;
			row.Children.Add(title);
			TextBlock st = ZSub(t.Finished ? (t.Success ? I18n.L("完成") : I18n.L("失败")) : $"{t.Progress:0}%");
			st.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
			st.VerticalAlignment = VerticalAlignment.Center;
			Grid.SetColumn(st, 1);
			row.Children.Add(st);
			s.Children.Add(row);
		}
		return s;
	}

	private FrameworkElement ZLogContent(string es)
	{
		int n = (es == LaunchZone.Small) ? 1 : 9;
		List<string> lines = _logLines.Skip(Math.Max(0, _logLines.Count - n)).ToList();
		StackPanel s = ZStack(ZLabel(I18n.L("游戏日志")));
		if (lines.Count == 0)
		{
			TextBlock none = ZSub(I18n.L("还没有日志，启动游戏后这里显示输出"));
			none.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
			s.Children.Add(none);
			return s;
		}
		foreach (string l in lines)
		{
			TextBlock t = ZSub(l);
			t.FontFamily = ZMono;
			t.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
			s.Children.Add(t);
		}
		return s;
	}

	private FrameworkElement ZRef(ZoneItem it, string es, bool preview)
	{
		string refId = it.Ref ?? "";
		string? wt = RefTypeOf(refId);
		if (wt == "playtime")
		{
			return ZPlay(es);
		}
		if (wt == "clock")
		{
			return ZClockContent(es);
		}
		object? built = null;
		if (refId.StartsWith("plugin:", StringComparison.Ordinal))
		{
			built = PluginBlocks.Build(refId, "launch");
		}
		else
		{
			WidgetDef? w = WidgetRegistry.FindWidget(refId);
			built = (w != null) ? WidgetUI.Build(w, "launch") : WidgetUI.MissingWidget(refId);
		}
		Grid g = new Grid();
		g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		g.Children.Add(ZLabel(RefName(refId)));
		if (built is UIElement el && !preview)
		{
			Border host = new Border { ClipToBounds = true, Margin = new Thickness(0.0, 4.0, 0.0, 0.0), Child = el };
			Grid.SetRow(host, 1);
			g.Children.Add(host);
		}
		return g;
	}

	// 数据变了只重建受影响的组件内容(正在输入的便签不动)
	private void ZoneRefreshContent(params string[] types)
	{
		if (!_zLoaded)
		{
			return;
		}
		ZonePack p = LaunchZone.Pack(_zItems, _zCols, _zRows);
		foreach (ZoneItem it in _zItems)
		{
			if (types.Length > 0 && !types.Contains(it.Type) && !(it.Type == LaunchZone.TypeRef && (types.Contains("play") || types.Contains("clock"))))
			{
				continue;
			}
			if (_zEls.TryGetValue(it.Id, out var c) && p.Out.TryGetValue(it.Id, out var r))
			{
				if (c.Glass.IsKeyboardFocusWithin)
				{
					continue;
				}
				ZoneFill(c, it, r.Eff);
				ZoneEditVisual(c);
			}
		}
	}

	private DispatcherTimer? _zLogTimer;

	// 游戏日志一秒能来几百行:合并成每 0.5 秒刷新一次「游戏日志」组件
	private void ZoneLogDirty()
	{
		if (!_zLoaded || !_zItems.Any((ZoneItem i) => i.Type == "log"))
		{
			return;
		}
		if (_zLogTimer == null)
		{
			_zLogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500.0) };
			_zLogTimer.Tick += delegate
			{
				_zLogTimer.Stop();
				ZoneRefreshContent("log");
			};
		}
		if (!_zLogTimer.IsEnabled)
		{
			_zLogTimer.Start();
		}
	}

	// ————— 数据:本周游戏时长 / 第一个服务器 + 延迟 / 最近截图 —————

	private async Task ZoneLoadDataAsync()
	{
		int gen = ++_zDataGen;
		try
		{
			_zWeek = await Task.Run(() => ZReadWeek(DateTime.Now));
		}
		catch
		{
			_zWeek = new WeekPlay();
		}
		if (gen != _zDataGen)
		{
			return;
		}
		ZoneRefreshContent("play");
		string inst = _inst.Str();
		string root = _instances.FirstOrDefault((InstanceInfo x) => x.Name == inst)?.Path ?? "";
		string ver = _ver.Str();
		try
		{
			List<string> files = await Task.Run(() => ZListShots(root, ver));
			if (gen != _zDataGen)
			{
				return;
			}
			_zShotFiles = files;
			List<string> need = files.Take(6).Where((string f) => !_zThumbs.ContainsKey(f)).ToList();
			Dictionary<string, BitmapSource> got = await Task.Run(() => ZDecodeThumbs(need));
			foreach (KeyValuePair<string, BitmapSource> kv in got)
			{
				_zThumbs[kv.Key] = kv.Value;
			}
			ZoneRefreshContent("shots");
		}
		catch
		{
		}
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}
		List<ServerRow> servers = (await PageBase.Api.TryCallAsync("list_servers", new { instance = inst }, new List<ServerRow>())) ?? new List<ServerRow>();
		if (gen != _zDataGen)
		{
			return;
		}
		_zServer = servers.FirstOrDefault();
		_zPing = null;
		_zPingState = (_zServer != null) ? 1 : 2;
		ZoneRefreshContent("server");
		if (_zServer != null)
		{
			ServerPingResult? r = await ServerPing.QueryAsync(_zServer.Ip, _zServer.Port);
			if (gen != _zDataGen)
			{
				return;
			}
			_zPing = r;
			_zPingState = 2;
			ZoneRefreshContent("server");
		}
	}

	private static WeekPlay ZReadWeek(DateTime now)
	{
		List<KeyValuePair<string, KeyValuePair<long, long>>> sessions = new List<KeyValuePair<string, KeyValuePair<long, long>>>();
		if (TipCL.Core.Launcher.Playtime.GetAllPlaytime() is TipCL.Core.PyDict all)
		{
			foreach (KeyValuePair<string, object> inst in all)
			{
				if (!(inst.Value is TipCL.Core.PyDict d) || !d.ContainsKey("sessions") || !(d["sessions"] is IEnumerable<object> list))
				{
					continue;
				}
				foreach (object o in list)
				{
					if (o is TipCL.Core.PyDict s && s.ContainsKey("start") && s.ContainsKey("duration"))
					{
						sessions.Add(new KeyValuePair<string, KeyValuePair<long, long>>(inst.Key, new KeyValuePair<long, long>(ZLong(s["start"]), ZLong(s["duration"]))));
					}
				}
			}
		}
		return LaunchZone.Week(sessions, now);
	}

	private static long ZLong(object? o)
	{
		try
		{
			return (o == null) ? 0 : Convert.ToInt64(o, System.Globalization.CultureInfo.InvariantCulture);
		}
		catch
		{
			return 0L;
		}
	}

	private static List<string> ZListShots(string root, string ver)
	{
		List<string> files = new List<string>();
		if (root.Length == 0)
		{
			return files;
		}
		foreach (string dir in new string[2]
		{
			System.IO.Path.Combine(root, "screenshots"),
			string.IsNullOrEmpty(ver) ? "" : System.IO.Path.Combine(root, "versions", ver, "screenshots")
		})
		{
			if (dir.Length == 0 || !System.IO.Directory.Exists(dir))
			{
				continue;
			}
			files.AddRange(System.IO.Directory.EnumerateFiles(dir).Where((string f) => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)));
		}
		return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending((string f) => System.IO.File.GetLastWriteTimeUtc(f)).ToList();
	}

	private static Dictionary<string, BitmapSource> ZDecodeThumbs(List<string> files)
	{
		Dictionary<string, BitmapSource> d = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
		foreach (string f in files)
		{
			try
			{
				// Win7 没有 WebP 解码器:统一走 WebpFallback(截图一般是 png,这里只是守规矩)
				byte[] data = System.IO.File.ReadAllBytes(f);
				if (WebpFallback.Decode(data, delegate(byte[] bytes)
				{
					BitmapImage b = new BitmapImage();
					b.BeginInit();
					b.CacheOption = BitmapCacheOption.OnLoad;
					b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
					b.DecodePixelWidth = 200;
					b.StreamSource = new System.IO.MemoryStream(bytes);
					b.EndInit();
					b.Freeze();
					return b;
				}) is BitmapSource bmp)
				{
					d[f] = bmp;
				}
			}
			catch
			{
			}
		}
		return d;
	}

	// ————— 小工具 —————

	private static SolidColorBrush ZSc(byte a, byte r, byte g, byte b)
	{
		SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
		brush.Freeze();
		return brush;
	}

	private SolidColorBrush AccentA(byte a)
	{
		Color c = (TryFindResource("B.Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0xC9, 0x40, 0x3F);
		SolidColorBrush b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
		b.Freeze();
		return b;
	}

	private static TextBlock ZText(string text, double size, bool bold = false)
	{
		return new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
	}

	private static StackPanel ZRow(double gap, params FrameworkElement[] kids)
	{
		StackPanel s = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		foreach (FrameworkElement k in kids)
		{
			if (s.Children.Count > 0)
			{
				k.Margin = new Thickness(gap, k.Margin.Top, k.Margin.Right, k.Margin.Bottom);
			}
			k.VerticalAlignment = VerticalAlignment.Center;
			s.Children.Add(k);
		}
		return s;
	}

	// kbd:等宽 12 号、左右 6(提示行 5)、圆角 8、14% 白底
	private static Border ZKbd(string text, double padY)
	{
		TextBlock t = new TextBlock { Text = text, FontFamily = ZMono, FontSize = 12.0, Foreground = W80 };
		return new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness((padY > 0.0) ? 6.0 : 5.0, padY, (padY > 0.0) ? 6.0 : 5.0, padY),
			Background = W14,
			VerticalAlignment = VerticalAlignment.Center,
			Child = t
		};
	}

	/// <summary>效果图里的线性图标:16×16 视框、描边 1.6、圆头圆角;颜色跟随继承的前景色。</summary>
	private static FrameworkElement ZIcon(string? stroke, double size, string? fill = null, double sw = 1.6)
	{
		Canvas c = new Canvas { Width = 16.0, Height = 16.0 };
		Binding fg = new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self };
		if (stroke != null)
		{
			System.Windows.Shapes.Path p = new System.Windows.Shapes.Path
			{
				Data = Geometry.Parse(stroke),
				StrokeThickness = sw,
				StrokeLineJoin = PenLineJoin.Round,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round
			};
			p.SetBinding(Shape.StrokeProperty, fg);
			c.Children.Add(p);
		}
		if (fill != null)
		{
			System.Windows.Shapes.Path f = new System.Windows.Shapes.Path { Data = Geometry.Parse(fill) };
			f.SetBinding(Shape.FillProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self });
			c.Children.Add(f);
		}
		return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = c, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
	}

	// 启动页上的半透明块(新闻条 / AI / 编辑组件):有背景图 = 白色 bg% 底 + line% 描边 + 白字;没有 = 主题纸色(同 Tone)
	private void ZGlass(Border b, byte bg, byte line, byte hover)
	{
		_zGlass.Add(Tuple.Create(b, bg, line, hover));
		b.MouseEnter += delegate
		{
			ZToneGlass(b, bg, line, hover, on: true);
		};
		b.MouseLeave += delegate
		{
			ZToneGlass(b, bg, line, hover, on: false);
		};
		ZToneGlass(b, bg, line, hover, on: false);
	}

	private void ZToneGlass(Border b, byte bg, byte line, byte hover, bool on)
	{
		if (_onImage)
		{
			b.Background = White(on ? hover : bg);
			b.BorderBrush = White(line);
			b.SetValue(TextElement.ForegroundProperty, W100);
		}
		else
		{
			b.SetResourceReference(Border.BackgroundProperty, on ? "B.Hover" : "B.Paper");
			b.SetResourceReference(Border.BorderBrushProperty, "B.Line");
			b.SetResourceReference(TextElement.ForegroundProperty, "B.Ink");
		}
	}

	private void ZoneToneAll()
	{
		foreach (Tuple<Border, byte, byte, byte> t in _zGlass)
		{
			ZToneGlass(t.Item1, t.Item2, t.Item3, t.Item4, t.Item1.IsMouseOver);
		}
		foreach (ZoneCard c in _zEls.Values)
		{
			ZoneTone(c);
			ZoneEditVisual(c);
		}
		ZoneRefreshContent("short", "server", "note");
	}

	// 编辑态主区域上方的「主区域固定，不能拖动、隐藏或缩放」(.lock:12 号、上下 4 左右 10、圆角 8、14% 白底 22% 描边、下 12)
	private Border BuildHeroLockChip()
	{
		Border b = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Background = W14,
			BorderBrush = W22,
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0),
			Visibility = Visibility.Collapsed,
			IsHitTestVisible = false,
			Child = ZRow(6.0, ZIcon(IcoLock, 13.0), ZText(I18n.L("主区域固定，不能拖动、隐藏或缩放"), 12.0))
		};
		b.SetValue(TextElement.ForegroundProperty, W100);
		return b;
	}
}
