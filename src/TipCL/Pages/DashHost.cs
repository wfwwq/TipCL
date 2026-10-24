using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Services;

namespace TipCL.Pages;

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

		public DashCard Card { get; }

		public CardView(DashHost host, DashCard card)
		{
			_host = host;
			Card = card;
			_shell = new Border
			{
				CornerRadius = new CornerRadius(12.0),
				BorderThickness = new Thickness(1.0),
				ClipToBounds = true
			};
			_shell.SetResourceReference(Border.BackgroundProperty, "B.Paper");
			_shell.SetResourceReference(Border.BorderBrushProperty, "B.Line");
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
				CornerRadius = new CornerRadius(12.0),
				BorderThickness = new Thickness(1.4),
				Visibility = Visibility.Collapsed,
				Cursor = Cursors.SizeAll
			};
			RefreshEditMask();
			_editMask.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
			_editMask.MouseLeftButtonDown += OnGrabMove;
			_editMask.MouseMove += OnDragMove;
			_editMask.MouseLeftButtonUp += OnDrop;
			_handleLayer.Visibility = Visibility.Collapsed;
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

		public void SetContent(string title, UIElement? content)
		{
			_title.Text = title;
			if (content is FrameworkElement { Parent: ContentControl parent } && parent != _body)
			{
				parent.Content = null;
			}
			_body.Content = content;
		}

		public void SetEdit(bool edit)
		{
			_edit = edit;
			if (edit)
			{
				RefreshEditMask();
			}
			_editMask.Visibility = ((!edit) ? Visibility.Collapsed : Visibility.Visible);
			_handleLayer.Visibility = ((!edit) ? Visibility.Collapsed : Visibility.Visible);
			_close.Visibility = ((!edit) ? Visibility.Collapsed : Visibility.Visible);
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
			Border border = new Border();
			border.Width = 11.0;
			border.Height = 11.0;
			border.CornerRadius = new CornerRadius(3.0);
			border.BorderThickness = new Thickness(1.5);
			Border border2 = border;
			Cursor cursor;
			switch (name)
			{
			case "n":
			case "s":
				cursor = Cursors.SizeNS;
				break;
			case "e":
			case "w":
				cursor = Cursors.SizeWE;
				break;
			case "nw":
			case "se":
				cursor = Cursors.SizeNWSE;
				break;
			default:
				cursor = Cursors.SizeNESW;
				break;
			}
			border2.Cursor = cursor;
			border.Tag = name;
			border.SetResourceReference(Border.BackgroundProperty, "B.Paper");
			border.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
			border.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				_mode = (string)((Border)s).Tag;
				Grab(e);
			};
			border.MouseMove += OnDragMove;
			border.MouseLeftButtonUp += OnDrop;
			return border;
		}

		private void OnGrabMove(object sender, MouseButtonEventArgs e)
		{
			_mode = "move";
			Grab(e);
		}

		private void Grab(MouseButtonEventArgs e)
		{
			_host.BringToTop(this);
			_grabPoint = e.GetPosition(_host);
			_grabRect = new Rect(Card.X, Card.Y, Card.W, Card.H);
			((UIElement)e.Source).CaptureMouse();
			e.Handled = true;
		}

		private void OnDragMove(object sender, MouseEventArgs e)
		{
			if (_mode == null || e.LeftButton != MouseButtonState.Pressed)
			{
				return;
			}
			Point position = e.GetPosition(_host);
			double num = Math.Max(1.0, _host.ActualWidth);
			double num2 = Math.Max(1.0, _host.ActualHeight);
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
			e.Handled = true;
		}

		private void OnDrop(object sender, MouseButtonEventArgs e)
		{
			if (_mode != null)
			{
				_mode = null;
				((UIElement)e.Source).ReleaseMouseCapture();
				_host.NotifyChanged();
				e.Handled = true;
			}
		}

		protected override Size ArrangeOverride(Size final)
		{
			Size result = base.ArrangeOverride(final);
			foreach (Border h in _handleLayer.Children)
			{
				(string, double, double) tuple = Handles.First(((string Name, double Cx, double Cy) x) => x.Name == (string)h.Tag);
				Canvas.SetLeft(h, tuple.Item2 * final.Width - 5.5);
				Canvas.SetTop(h, tuple.Item3 * final.Height - 5.5);
			}
			return result;
		}
	}

	private const double Gap = 10.0;

	private static readonly (int W, int H) FallbackMin = (W: 200, H: 120);

	private readonly Dictionary<string, FrameworkElement> _contents = new Dictionary<string, FrameworkElement>();

	private readonly List<CardView> _views = new List<CardView>();

	private readonly Border _gridLayer = new Border();

	private DashLayout _layout = DashLayout.Default();

	private bool _edit;

	public Func<string, UIElement?>? ContentFactory;

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
		_gridLayer.Visibility = Visibility.Collapsed;
		_gridLayer.IsHitTestVisible = false;
		base.Children.Add(_gridLayer);
		base.Loaded += delegate
		{
			App.ThemeVisualsChanged -= RefreshThemeVisuals;
			App.ThemeVisualsChanged += RefreshThemeVisuals;
		};
		base.Unloaded += delegate
		{
			App.ThemeVisualsChanged -= RefreshThemeVisuals;
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
			UIElement uIElement = Cached(item.Type);
			if (uIElement != null)
			{
				CardView cardView = new CardView(this, item);
				cardView.SetContent(TitleFactory?.Invoke(item.Type) ?? item.Type, uIElement);
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

	private UIElement? Cached(string type)
	{
		if (_contents.TryGetValue(type, out FrameworkElement value))
		{
			return value;
		}
		if (!(ContentFactory?.Invoke(type) is FrameworkElement frameworkElement))
		{
			return null;
		}
		_contents[type] = frameworkElement;
		return frameworkElement;
	}

	public void AddCard(string type)
	{
		if (!_layout.Items.Any((DashCard c) => c.Type == type && !c.Hidden))
		{
			DashCard dashCard = _layout.Items.FirstOrDefault((DashCard c) => c.Type == type && c.Hidden);
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
			this.Changed?.Invoke();
		}
	}

	private (double X, double Y, double W, double H) FindFreeSpot(string type)
	{
		double num = Math.Max(1.0, base.ActualWidth);
		double num2 = Math.Max(1.0, base.ActualHeight);
		(int W, int H) valueOrDefault = MinSizes.GetValueOrDefault(type, FallbackMin);
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
			if (!_layout.VisibleItems().Any((DashCard c) => rect.IntersectsWith(new Rect(c.X, c.Y, c.W, c.H))))
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
		this.Changed?.Invoke();
	}

	public bool Has(string type)
	{
		return _layout.Items.Any((DashCard c) => c.Type == type && !c.Hidden);
	}

	public void NotifyChanged()
	{
		this.Changed?.Invoke();
	}

	public (double W, double H) MinPx(string type)
	{
		var (num, num2) = MinSizes.GetValueOrDefault(type, FallbackMin);
		return (W: num, H: num2);
	}

	private void RefreshGridLayer()
	{
		bool flag = _edit && _layout.Snap;
		_gridLayer.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		if (!flag)
		{
			_gridLayer.Background = null;
			return;
		}
		int grid = _layout.Grid;
		_gridLayer.Background = new DrawingBrush
		{
			TileMode = TileMode.Tile,
			Viewport = new Rect(0.0, 0.0, grid, grid),
			ViewportUnits = BrushMappingMode.Absolute,
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
		if (!(Application.Current?.TryFindResource("B.Accent") is SolidColorBrush solidColorBrush))
		{
			return Color.FromRgb(46, 155, 107);
		}
		return solidColorBrush.Color;
	}

	private static Color WithAlpha(Color c, byte a)
	{
		return Color.FromArgb(a, c.R, c.G, c.B);
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
		foreach (UIElement child in base.Children)
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
}
