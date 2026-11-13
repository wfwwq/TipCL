using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

// C3a:Windows 版 Pages\DashHost.cs(WPF)到 Avalonia 11 的移植 —— 自由画布式仪表盘宿主。
// 语义照抄:卡片坐标是 [0,1] 归一化矩形(X/Y/W/H),Z 决定前后,编辑态可拖可缩放可删,
// Snap 打开时按未缩放像素吸附到 Grid 网格,网格底纹只在「编辑态 && Snap」时出现。
//
// mac 侧移植差异(逐条也记在 PROGRESS.md 的 C3a 差异表里):
// 1. 框架类型:UIElement / FrameworkElement → Control;Visibility.Collapsed → IsVisible = false;
//    MouseButtonEventArgs → PointerPressedEventArgs、MouseEventArgs → PointerEventArgs、
//    MouseLeftButtonUp → PointerReleased;UIElement.CaptureMouse/ReleaseMouseCapture →
//    IPointer.Capture(元素)/IPointer.Capture(null)。
// 2. 光标:WPF 的 Cursors.SizeNS / SizeWE / SizeNWSE / SizeNESW / SizeAll →
//    new Cursor(StandardCursorType.SizeNorthSouth / SizeWestEast / TopLeftCorner /
//    TopRightCorner / SizeAll)(Avalonia 11 没有 Cursors 静态类;斜向那两支用的是
//    Avalonia 的角标命名,语义同为"沿该对角线缩放")。
// 3. Rect.IntersectsWith →  Avalonia 的 Rect.Intersects(同名不同词);其余几何算法一字未改。
// 4. 网格底纹:WPF 的 DrawingBrush 用 Viewport + ViewportUnits.Absolute 定瓷砖尺寸;
//    Avalonia 的 DrawingBrush 没有 Viewport,等价物是 SourceRect / DestinationRect 都取
//    (0,0,grid,grid) 的 Absolute 相对矩形 + Stretch.Fill。渲染意图一致(每 grid 像素一道
//    主题色细线),像素级细节在 mac 上不可与 WPF 对比。
// 5. 主题变更:同 BlockHost —— mac 没有 App.ThemeVisualsChanged,订控件自己的
//    ActualThemeVariantChanged(Loaded 订阅 / Unloaded 取消)。
// 6. 取主题色、Toast 同 BlockHost 的两条(Windows 兜底绿保留)。
// 7. 拖拽/缩放入口:事件处理器只负责"取坐标 + 调状态机",状态机是 internal 的
//    GrabCore / DragTo / DropNow,测试驱动的就是这一层(见 DECISIONS.md D-115)。
public sealed class DashHost : Panel
{
	public sealed class CardView : Border
	{
		private static readonly (string Name, double Cx, double Cy)[] Handles = new(string, double, double)[8]
		{
			("nw", 0.0, 0.0),
			("n", 0.5, 0.0),
			("ne", 1.0, 0.0),
			("w", 0.0, 0.5),
			("e", 1.0, 0.5),
			("sw", 0.0, 1.0),
			("s", 0.5, 1.0),
			("se", 1.0, 1.0)
		};

		private readonly DashHost _host;

		private readonly Grid _root = new Grid();

		private readonly Border _shell;

		private readonly Canvas _handleLayer = new Canvas();

		private readonly Border _editMask;

		private readonly TextBlock _title = Ui.Txt("", 12.5, bold: true);

		private readonly Button _close;

		private readonly ContentControl _body = new ContentControl
		{
			Focusable = false
		};

		private bool _edit;

		private Point _grabPoint;

		private Rect _grabRect;

		private string? _mode;

		private IPointer? _grabPointer;

		public DashCard Card { get; }

		public CardView(DashHost host, DashCard card)
		{
			_host = host;
			Card = card;
			_shell = new Border
			{
				CornerRadius = new CornerRadius(10.0),
				BorderThickness = new Thickness(1.0),
				ClipToBounds = true
			};
			_shell.Dyn(Border.BackgroundProperty, "B.Paper");
			_shell.Dyn(Border.BorderBrushProperty, "B.Line");
			Motion.Shadow(_shell, 16.0, 0.07, 3.0);
			_close = Ui.IconBtn("\ue711", I18n.L("移除卡片"), delegate
			{
				_host.RemoveCard(Card);
			}, 11.0);
			_close.Padding = new Thickness(4.0);
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(_title.VCenter(), 0);
			grid.Add(_close, 0, 1);
			grid.Margin = new Thickness(14.0, 10.0, 8.0, 6.0);
			Grid grid2 = Ui.G("Auto,*");
			grid2.Add(grid, 0);
			grid2.Add(new Border
			{
				Padding = new Thickness(14.0, 0.0, 14.0, 12.0),
				Child = _body
			}, 1);
			_shell.Child = grid2;
			_editMask = new Border
			{
				CornerRadius = new CornerRadius(10.0),
				BorderThickness = new Thickness(1.4),
				IsVisible = false,
				Cursor = new Cursor(StandardCursorType.SizeAll)
			};
			RefreshEditMask();
			_editMask.Dyn(Border.BorderBrushProperty, "B.Accent");
			_editMask.PointerPressed += OnGrabMove;
			_editMask.PointerMoved += OnDragMove;
			_editMask.PointerReleased += OnDrop;
			_handleLayer.IsVisible = false;
			(string, double, double)[] handles = Handles;
			for (int num = 0; num < handles.Length; num++)
			{
				(string, double, double) tuple = handles[num];
				_handleLayer.Children.Add(MakeHandle(tuple.Item1));
			}
			_root.Children.Add(_shell);
			_root.Children.Add(_editMask);
			_root.Children.Add(_handleLayer);
			Child = _root;
		}

		public void SetContent(string title, Control? content)
		{
			_title.Text = title;
			// 与 BlockHost.BlockCard 共用同一份"先摘下来"的实现(Windows 两边各写了一遍同样的判断)
			BlockHost.DetachFromForeignHost(_body, content);
			_body.Content = content;
		}

		public void SetEdit(bool edit)
		{
			_edit = edit;
			if (edit)
			{
				RefreshEditMask();
			}
			_editMask.IsVisible = edit;
			_handleLayer.IsVisible = edit;
			_close.IsVisible = edit;
			_body.IsHitTestVisible = !edit;
		}

		private void RefreshEditMask()
		{
			Color color = AccentColor();
			_editMask.Background = new SolidColorBrush(Color.FromArgb(10, color.R, color.G, color.B));
		}

		public void RefreshTheme()
		{
			RefreshEditMask();
		}

		private Border MakeHandle(string name)
		{
			// 语义同 WPF 那份的 Cursors.*(斜向两支用 Avalonia 的角标命名)
			Cursor cursor;
			switch (name)
			{
			case "n":
			case "s":
				cursor = new Cursor(StandardCursorType.SizeNorthSouth);
				break;
			case "e":
			case "w":
				cursor = new Cursor(StandardCursorType.SizeWestEast);
				break;
			case "nw":
			case "se":
				cursor = new Cursor(StandardCursorType.TopLeftCorner);
				break;
			default:
				cursor = new Cursor(StandardCursorType.TopRightCorner);
				break;
			}
			Border border = new Border
			{
				Width = 11.0,
				Height = 11.0,
				CornerRadius = new CornerRadius(3.0),
				BorderThickness = new Thickness(1.5),
				Cursor = cursor,
				Tag = name
			};
			border.Dyn(Border.BackgroundProperty, "B.Paper");
			border.Dyn(Border.BorderBrushProperty, "B.Accent");
			border.PointerPressed += delegate(object? s, PointerPressedEventArgs e)
			{
				Grab(((string)((Border)s!).Tag!), e);
			};
			border.PointerMoved += OnDragMove;
			border.PointerReleased += OnDrop;
			return border;
		}

		private void OnGrabMove(object? sender, PointerPressedEventArgs e)
		{
			Grab("move", e);
		}

		// WPF: ((UIElement)e.Source).CaptureMouse()。Avalonia 的捕获挂在指针上。
		private void Grab(string mode, PointerPressedEventArgs e)
		{
			GrabCore(mode, e.GetPosition(_host), e.Pointer, e.Source as IInputElement);
			e.Handled = true;
		}

		/// <summary>
		/// 抓起的真正实现(与真实事件路径同一个状态机,只是不吃事件对象):
		/// 置模式、记抓点与抓取时的矩形、把指针捕获到 <paramref name="captureTarget"/>。
		/// 测试驱动的入口就是它(见 DECISIONS.md D-115)。
		/// </summary>
		internal void GrabCore(string? mode, Point grabPoint, IPointer? pointer, IInputElement? captureTarget)
		{
			_mode = mode;
			_host.BringToTop(this);
			_grabPoint = grabPoint;
			_grabRect = new Rect(Card.X, Card.Y, Card.W, Card.H);
			_grabPointer = pointer;
			pointer?.Capture(captureTarget);
		}

		private void OnDragMove(object? sender, PointerEventArgs e)
		{
			if (_mode == null || !e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
			{
				return;
			}
			DragTo(e.GetPosition(_host));
			e.Handled = true;
		}

		/// <summary>
		/// 拖到宿主坐标系的 <paramref name="position"/>(Windows 的 OnDragMove 正文,几何一字未改;
		/// 只是把"从事件里取坐标 + 判左键"留在了 <see cref="OnDragMove"/> 里)。
		/// </summary>
		internal void DragTo(Point position)
		{
			if (_mode == null)
			{
				return;
			}
			double num = Math.Max(1.0, _host.Bounds.Width);
			double num2 = Math.Max(1.0, _host.Bounds.Height);
			double num3 = (position.X - _grabPoint.X) / num;
			double num4 = (position.Y - _grabPoint.Y) / num2;
			(double W, double H) tuple = _host.MinPx(Card.Type);
			double item = tuple.W;
			double item2 = tuple.H;
			double num5 = Math.Min(1.0, (item + 10.0) / num);
			double num6 = Math.Min(1.0, (item2 + 10.0) / num2);
			Rect grabRect = _grabRect;
			if (_mode == "move")
			{
				Card.X = _host.SnapV(Clamp.Of(grabRect.X + num3, 0.0, 1.0 - grabRect.Width), num);
				Card.Y = _host.SnapV(Clamp.Of(grabRect.Y + num4, 0.0, 1.0 - grabRect.Height), num2);
			}
			else
			{
				double x = grabRect.X;
				double y = grabRect.Y;
				double val = grabRect.Width;
				double val2 = grabRect.Height;
				if (_mode.Contains('w'))
				{
					double num7 = _host.SnapV(Clamp.Of(grabRect.X + num3, 0.0, grabRect.Right - num5), num);
					val = grabRect.Right - num7;
					x = num7;
				}
				if (_mode.Contains('e'))
				{
					val = Clamp.Of(_host.SnapV(grabRect.X + grabRect.Width + num3, num) - grabRect.X, num5, 1.0 - grabRect.X);
				}
				if (_mode.Contains('n'))
				{
					double num8 = _host.SnapV(Clamp.Of(grabRect.Y + num4, 0.0, grabRect.Bottom - num6), num2);
					val2 = grabRect.Bottom - num8;
					y = num8;
				}
				if (_mode.Contains('s'))
				{
					val2 = Clamp.Of(_host.SnapV(grabRect.Y + grabRect.Height + num4, num2) - grabRect.Y, num6, 1.0 - grabRect.Y);
				}
				Card.X = x;
				Card.Y = y;
				Card.W = Math.Max(num5, val);
				Card.H = Math.Max(num6, val2);
			}
			_host.InvalidateArrange();
		}

		private void OnDrop(object? sender, PointerReleasedEventArgs e)
		{
			DropNow();
			e.Handled = true;
		}

		/// <summary>松手(Windows 的 OnDrop 正文:清模式、放开捕获、通知 Changed)。</summary>
		internal void DropNow()
		{
			if (_mode == null)
			{
				return;
			}
			_mode = null;
			_grabPointer?.Capture(null);
			_grabPointer = null;
			_host.NotifyChanged();
		}

		/// <summary>此刻的拖拽模式("move" / "nw".."se" / null;测试用)。</summary>
		internal string? ModeNow => _mode;

		/// <summary>编辑蒙版、手柄层、关闭按钮、内容体(测试用)。</summary>
		internal Border EditMaskNow => _editMask;

		internal Canvas HandleLayerNow => _handleLayer;

		internal Button CloseNow => _close;

		internal ContentControl BodyNow => _body;

		internal TextBlock TitleNow => _title;

		internal bool EditNow => _edit;

		protected override Size ArrangeOverride(Size final)
		{
			Size result = base.ArrangeOverride(final);
			foreach (Control h in _handleLayer.Children)
			{
				(string, double, double) tuple = Handles.First(((string Name, double Cx, double Cy) x) => x.Name == (string)h.Tag!);
				Canvas.SetLeft(h, tuple.Item2 * final.Width - 5.5);
				Canvas.SetTop(h, tuple.Item3 * final.Height - 5.5);
			}
			return result;
		}
	}

	private const double Gap = 10.0;

	private static readonly (int W, int H) FallbackMin = (W: 200, H: 120);

	private readonly Dictionary<string, Control> _contents = new Dictionary<string, Control>();

	private readonly List<CardView> _views = new List<CardView>();

	private readonly Border _gridLayer = new Border();

	private DashLayout _layout = DashLayout.Default();

	private bool _edit;

	public Func<string, Control?>? ContentFactory;

	public Func<string, string>? TitleFactory;

	public Dictionary<string, (int W, int H)> MinSizes { get; set; } = new Dictionary<string, (int, int)>();

	public DashLayout Layout => _layout;

	public bool EditMode
	{
		get
		{
			return _edit;
		}
		set
		{
			if (_edit == value)
			{
				return;
			}
			_edit = value;
			foreach (CardView view in _views)
			{
				view.SetEdit(value);
			}
			RefreshGridLayer();
			InvalidateArrange();
		}
	}

	public event Action? Changed;

	public DashHost()
	{
		base.ClipToBounds = false;
		_gridLayer.IsVisible = false;
		_gridLayer.IsHitTestVisible = false;
		base.Children.Add(_gridLayer);
		base.Loaded += delegate
		{
			ActualThemeVariantChanged -= OnThemeVariantChanged;
			ActualThemeVariantChanged += OnThemeVariantChanged;
		};
		base.Unloaded += delegate
		{
			ActualThemeVariantChanged -= OnThemeVariantChanged;
		};
	}

	public void Load(DashLayout layout)
	{
		_layout = layout;
		Rebuild();
	}

	public void Rebuild()
	{
		foreach (CardView view in _views)
		{
			base.Children.Remove(view);
		}
		_views.Clear();
		foreach (DashCard item in _layout.VisibleItems())
		{
			Control? control = Cached(item.Type);
			if (control != null)
			{
				CardView cardView = new CardView(this, item);
				cardView.SetContent(TitleFactory?.Invoke(item.Type) ?? item.Type, control);
				cardView.SetEdit(_edit);
				_views.Add(cardView);
				base.Children.Add(cardView);
			}
		}
		RefreshGridLayer();
		InvalidateArrange();
		if (Motion.Enabled)
		{
			Motion.StaggerItems(_views, 30.0, 220.0, 12.0);
		}
	}

	private Control? Cached(string type)
	{
		if (_contents.TryGetValue(type, out Control? value))
		{
			return value;
		}
		if (ContentFactory?.Invoke(type) is not Control control)
		{
			return null;
		}
		_contents[type] = control;
		return control;
	}

	public void AddCard(string type)
	{
		if (!_layout.Items.Any((DashCard c) => c.Type == type && !c.Hidden))
		{
			DashCard? dashCard = _layout.Items.FirstOrDefault((DashCard c) => c.Type == type && c.Hidden);
			if (dashCard != null)
			{
				dashCard.Hidden = false;
				dashCard.Z = _layout.NextZ();
			}
			else
			{
				var (x, y, w, h) = FindFreeSpot(type);
				_layout.Items.Add(new DashCard
				{
					Type = type,
					X = x,
					Y = y,
					W = w,
					H = h,
					Z = _layout.NextZ()
				});
			}
			_layout.Normalize();
			Rebuild();
			Changed?.Invoke();
		}
	}

	private (double X, double Y, double W, double H) FindFreeSpot(string type)
	{
		double num = Math.Max(1.0, base.Bounds.Width);
		double num2 = Math.Max(1.0, base.Bounds.Height);
		(int W, int H) valueOrDefault = MinSizes.TryGetValue(type, out (int, int) found) ? found : FallbackMin;
		int item = valueOrDefault.W;
		int item2 = valueOrDefault.H;
		double num3 = Clamp.Of(Math.Max(0.3, (double)item / num), 0.1, 1.0);
		double num4 = Clamp.Of(Math.Max(0.3, (double)item2 / num2), 0.1, 1.0);
		(double, double)[] array = new(double, double)[4]
		{
			(0.0, 0.0),
			(1.0 - num3, 0.0),
			(0.0, 1.0 - num4),
			(1.0 - num3, 1.0 - num4)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(double, double) tuple = array[i];
			double item3 = tuple.Item1;
			double item4 = tuple.Item2;
			Rect rect = new Rect(item3, item4, num3, num4);
			if (!_layout.VisibleItems().Any((DashCard c) => rect.Intersects(new Rect(c.X, c.Y, c.W, c.H))))
			{
				return (X: item3, Y: item4, W: num3, H: num4);
			}
		}
		return (X: 0.05, Y: 0.05, W: num3, H: num4);
	}

	public void RemoveCard(DashCard card)
	{
		_layout.Items.Remove(card);
		Rebuild();
		Changed?.Invoke();
	}

	public bool Has(string type)
	{
		return _layout.Items.Any((DashCard c) => c.Type == type && !c.Hidden);
	}

	public void NotifyChanged()
	{
		Changed?.Invoke();
	}

	public (double W, double H) MinPx(string type)
	{
		var (num, num2) = MinSizes.TryGetValue(type, out (int, int) found) ? found : FallbackMin;
		return (W: num, H: num2);
	}

	private void RefreshGridLayer()
	{
		bool flag = _edit && _layout.Snap;
		_gridLayer.IsVisible = flag;
		if (!flag)
		{
			_gridLayer.Background = null;
			return;
		}
		int grid = _layout.Grid;
		_gridLayer.Background = new DrawingBrush
		{
			TileMode = TileMode.Tile,
			// WPF 的 Viewport + ViewportUnits.Absolute → Avalonia 的 Source/DestinationRect(Absolute)
			SourceRect = new RelativeRect(0.0, 0.0, grid, grid, RelativeUnit.Absolute),
			DestinationRect = new RelativeRect(0.0, 0.0, grid, grid, RelativeUnit.Absolute),
			Stretch = Stretch.Fill,
			Drawing = new GeometryDrawing
			{
				Geometry = new RectangleGeometry(new Rect(0.0, 0.0, grid, grid)),
				Pen = new Pen(new SolidColorBrush(WithAlpha(AccentColor(), 38)), 0.6)
			},
			Opacity = 0.9
		};
	}

	private static Color AccentColor()
	{
		return BlockHost.AccentColor();
	}

	private static Color WithAlpha(Color c, byte a)
	{
		return Color.FromArgb(a, c.R, c.G, c.B);
	}

	private void OnThemeVariantChanged(object? sender, EventArgs e)
	{
		RefreshThemeVisuals();
	}

	public void RefreshThemeVisuals()
	{
		RefreshGridLayer();
		foreach (CardView view in _views)
		{
			view.RefreshTheme();
		}
	}

	public double SnapV(double v, double axisPx)
	{
		v = Clamp.Of(v, 0.0, 1.0);
		if (!_layout.Snap || axisPx <= 1.0)
		{
			return v;
		}
		return Clamp.Of(Math.Round(v * axisPx / (double)_layout.Grid) * (double)_layout.Grid / axisPx, 0.0, 1.0);
	}

	protected override Size MeasureOverride(Size available)
	{
		double num = (double.IsInfinity(available.Width) ? 900.0 : available.Width);
		double num2 = (double.IsInfinity(available.Height) ? 600.0 : available.Height);
		foreach (Control child in base.Children)
		{
			if (child == _gridLayer)
			{
				child.Measure(available);
			}
			else if (child is CardView cardView)
			{
				child.Measure(new Size(Math.Max(1.0, cardView.Card.W * num), Math.Max(1.0, cardView.Card.H * num2)));
			}
		}
		return new Size(num, num2);
	}

	protected override Size ArrangeOverride(Size final)
	{
		_gridLayer.Arrange(new Rect(0.0, 0.0, final.Width, final.Height));
		foreach (CardView view in _views)
		{
			Rect finalRect = new Rect(view.Card.X * final.Width, view.Card.Y * final.Height, Math.Max(40.0, view.Card.W * final.Width - 10.0), Math.Max(30.0, view.Card.H * final.Height - 10.0));
			view.Arrange(finalRect);
		}
		return final;
	}

	public void BringToTop(CardView v)
	{
		base.Children.Remove(v);
		base.Children.Add(v);
		v.Card.Z = _layout.NextZ();
	}

	// ————— 测试可见的模型视图(C3a 新增,只有 getter) —————

	/// <summary>此刻渲染出来的卡片视图(顺序 = 可视树顺序;测试用)。</summary>
	internal IReadOnlyList<CardView> ViewsNow => _views.ToArray();

	/// <summary>网格底纹层(测试用:看 Background 的瓷砖与开关)。</summary>
	internal Border GridLayerNow => _gridLayer;

	/// <summary>内容工厂缓存下来的内容控件(测试用:验证同一 type 只造一次)。</summary>
	internal IReadOnlyDictionary<string, Control> ContentsNow => new Dictionary<string, Control>(_contents);
}