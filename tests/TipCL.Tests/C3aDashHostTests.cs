using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3a:<c>DashHost</c>(Windows <c>Pages\DashHost.cs</c>)在**真 Avalonia 平台**上的行为。
///
/// 纪律同 C3aBlockHostTests:每条碰 Avalonia 的用例体都经 <see cref="AvaloniaSession.Run(System.Action)"/>;
/// 期望值来源逐条写在用例上方(<c>Windows 源</c> / <c>实测</c> / <c>本包决定</c>)。
///
/// 拖拽 / 缩放的注入缝:<c>CardView.GrabCore / DragTo / DropNow</c> —— 真实处理器
/// (<c>OnGrabMove</c> / <c>MakeHandle</c> 的 PointerPressed / <c>OnDragMove</c> / <c>OnDrop</c>)
/// 只多做两件事:从事件里取坐标、判左键。测试驱动的就是它们调用的同一层状态机(见 DECISIONS.md D-115)。
/// </summary>
public class C3aDashHostTests
{
	private static void OnAvalonia(Action body)
	{
		AvaloniaSession.Run(body);
	}

	/// <summary>造一份布局。<paramref name="grid"/> = 0 表示关掉吸附(几何用例要精确算术)。</summary>
	private static DashLayout Layout(int grid, params DashCard[] cards)
	{
		return new DashLayout { Grid = grid, Items = new List<DashCard>(cards) };
	}

	private static DashCard Card(string type, double x, double y, double w, double h, int z = 0, bool hidden = false)
	{
		return new DashCard
		{
			Id = type + "-" + z,
			Type = type,
			X = x,
			Y = y,
			W = w,
			H = h,
			Z = z,
			Hidden = hidden
		};
	}

	private static DashHost HostWith(DashLayout layout, Func<string, Control?>? factory = null)
	{
		DashHost host = new DashHost
		{
			ContentFactory = factory ?? ((string type) => C3aBuild.Content())
		};
		host.Load(layout);
		return host;
	}

	// ————— 构造 / Rebuild / 内容工厂 —————

	/// <summary>
	/// 来源:Windows 源 <c>DashHost()</c> —— 构造时只把网格底纹层放进 Children 且收起来,
	/// 卡片要等 <c>Load/Rebuild</c> 才出现;<c>ClipToBounds = false</c>。
	/// </summary>
	[Fact]
	public void Ctor_only_holds_the_hidden_grid_layer_and_does_not_clip()
	{
		OnAvalonia(() =>
		{
			DashHost host = new DashHost();
			Assert.Single(host.Children);
			Assert.Same(host.GridLayerNow, host.Children[0]);
			Assert.False(host.GridLayerNow.IsVisible);
			Assert.False(host.GridLayerNow.IsHitTestVisible);
			Assert.False(host.ClipToBounds);
			Assert.Empty(host.ViewsNow);
			Assert.IsType<DashLayout>(host.Layout);
			Assert.Equal(8, host.Layout.Grid);
			Assert.Equal(3, host.Layout.Items.Count);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>Rebuild</c> —— <c>foreach (_layout.VisibleItems())</c>(= 未隐藏的按 Z 升序)
	/// 每张卡一个 <c>CardView</c>;隐藏卡不建视图。
	/// </summary>
	[Fact]
	public void Rebuild_creates_one_view_per_visible_card_in_z_order()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(0,
				Card("c", 0.0, 0.0, 0.3, 0.3, z: 2),
				Card("a", 0.0, 0.0, 0.3, 0.3, z: 0),
				Card("gone", 0.0, 0.0, 0.3, 0.3, z: 1, hidden: true),
				Card("b", 0.0, 0.0, 0.3, 0.3, z: 1)));

			Assert.Equal(new[] { "a", "b", "c" }, TypesOf(host));
			// Children = 网格层 + 三张卡
			Assert.Equal(4, host.Children.Count);

			// 再 Rebuild 一次不会留下上轮的视图
			host.Rebuild();
			Assert.Equal(new[] { "a", "b", "c" }, TypesOf(host));
			Assert.Equal(4, host.Children.Count);
		});
	}

	/// <summary>来源:Windows 源 <c>Cached</c> —— 工厂返回 null(不是 FrameworkElement)时这张卡直接跳过。</summary>
	[Fact]
	public void Rebuild_skips_cards_whose_factory_returns_null()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(
				Layout(0, Card("a", 0.0, 0.0, 0.3, 0.3, z: 0), Card("b", 0.0, 0.0, 0.3, 0.3, z: 1)),
				(string type) => (type == "b") ? null : C3aBuild.Content());

			Assert.Equal(new[] { "a" }, TypesOf(host));
			Assert.Single(host.ContentsNow);
			Assert.True(host.ContentsNow.ContainsKey("a"));
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>Cached</c> 的 <c>_contents[type]</c> 缓存 —— 同一 type 只问工厂一次。
	/// 同时钉住"同一份内容只能待在一个位置"(后建的卡把内容从先建的卡那里摘过来,
	/// 与 Windows 的 <c>SetContent</c> 行为一致)。
	/// </summary>
	[Fact]
	public void Content_factory_is_asked_once_per_type_and_the_last_card_wins_the_shared_content()
	{
		OnAvalonia(() =>
		{
			int calls = 0;
			DashHost host = HostWith(
				Layout(0, Card("notes", 0.0, 0.0, 0.3, 0.3, z: 0), Card("notes", 0.5, 0.5, 0.3, 0.3, z: 1)),
				delegate(string type)
				{
					calls++;
					return C3aBuild.Content();
				});
			using var shown = UiHost.Show(host);

			Assert.Equal(1, calls);
			Assert.Single(host.ContentsNow);
			Control content = host.ContentsNow["notes"];
			Assert.Same(content, host.ViewsNow[1].BodyNow.Content);
			Assert.Null(host.ViewsNow[0].BodyNow.Content);
		});
	}

	/// <summary>来源:Windows 源 <c>cardView.SetContent(TitleFactory?.Invoke(item.Type) ?? item.Type, ...)</c>。</summary>
	[Fact]
	public void Title_comes_from_the_title_factory_otherwise_from_the_card_type()
	{
		OnAvalonia(() =>
		{
			DashHost plain = HostWith(Layout(0, Card("news", 0.0, 0.0, 0.3, 0.3)));
			Assert.Equal("news", plain.ViewsNow[0].TitleNow.Text);

			DashHost titled = new DashHost
			{
				ContentFactory = (string type) => C3aBuild.Content(),
				TitleFactory = (string type) => I18n.L("新闻")
			};
			titled.Load(Layout(0, Card("news", 0.0, 0.0, 0.3, 0.3)));
			Assert.Equal(I18n.L("新闻"), titled.ViewsNow[0].TitleNow.Text);
		});
	}

	// ————— 编辑态与网格底纹 —————

	/// <summary>
	/// 来源:Windows 源 <c>EditMode</c> setter(<c>SetEdit</c> 逐个 + <c>RefreshGridLayer</c> +
	/// <c>InvalidateArrange</c>)与 <c>RefreshGridLayer</c> 的 <c>_edit &amp;&amp; _layout.Snap</c>;
	/// <c>Snap =&gt; Grid &gt; 0</c>。
	/// </summary>
	[Fact]
	public void EditMode_shows_the_grid_layer_only_when_snapping_is_on()
	{
		OnAvalonia(() =>
		{
			DashHost snapping = HostWith(Layout(8, Card("a", 0.0, 0.0, 0.3, 0.3)));
			snapping.EditMode = true;
			Assert.True(snapping.GridLayerNow.IsVisible);
			Assert.IsType<DrawingBrush>(snapping.GridLayerNow.Background);
			Assert.True(snapping.ViewsNow[0].EditNow);
			Assert.True(snapping.ViewsNow[0].EditMaskNow.IsVisible);
			Assert.True(snapping.ViewsNow[0].HandleLayerNow.IsVisible);
			Assert.True(snapping.ViewsNow[0].CloseNow.IsVisible);

			DashHost noSnap = HostWith(Layout(0, Card("a", 0.0, 0.0, 0.3, 0.3)));
			noSnap.EditMode = true;
			Assert.False(noSnap.GridLayerNow.IsVisible);
			Assert.Null(noSnap.GridLayerNow.Background);
			Assert.True(noSnap.ViewsNow[0].EditNow);

			// 退出编辑态后底色与手柄都收回
			snapping.EditMode = false;
			Assert.False(snapping.GridLayerNow.IsVisible);
			Assert.False(snapping.ViewsNow[0].EditMaskNow.IsVisible);
			Assert.False(snapping.ViewsNow[0].HandleLayerNow.IsVisible);
			Assert.False(snapping.ViewsNow[0].CloseNow.IsVisible);
			Assert.True(snapping.ViewsNow[0].BodyNow.IsHitTestVisible);
		});
	}

	/// <summary>来源:Windows 源 <c>EditMode</c> setter 的 <c>if (_edit == value) return;</c>。</summary>
	[Fact]
	public void Setting_EditMode_to_the_same_value_is_a_noop()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(8, Card("a", 0.0, 0.0, 0.3, 0.3)));
			host.EditMode = false;
			Assert.False(host.GridLayerNow.IsVisible);

			host.EditMode = true;
			IBrush? brush = host.GridLayerNow.Background;
			host.EditMode = true;
			Assert.Same(brush, host.GridLayerNow.Background);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>RefreshGridLayer</c> 的 <c>DrawingBrush</c>:
	/// <c>Viewport</c>/<c>ViewportUnits.Absolute</c> 取 (0,0,grid,grid)、<c>TileMode.Tile</c>、
	/// 主题色 alpha 38、笔宽 0.6、整体 <c>Opacity 0.9</c>。
	/// mac 差异:Avalonia 没有 Viewport,用 SourceRect / DestinationRect(Absolute)等价(差异表第 4 条)。
	/// </summary>
	[Fact]
	public void Grid_brush_tiles_the_configured_size_with_the_accent_colour()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(16, Card("a", 0.0, 0.0, 0.3, 0.3)));
			host.EditMode = true;

			DrawingBrush brush = Assert.IsType<DrawingBrush>(host.GridLayerNow.Background);
			Assert.Equal(TileMode.Tile, brush.TileMode);
			Assert.Equal(0.9, brush.Opacity, 6);
			Assert.Equal(RelativeUnit.Absolute, brush.DestinationRect.Unit);
			Assert.Equal(16.0, brush.DestinationRect.Rect.Width, 6);
			Assert.Equal(16.0, brush.DestinationRect.Rect.Height, 6);
			Assert.Equal(16.0, brush.SourceRect.Rect.Width, 6);

			GeometryDrawing drawing = Assert.IsType<GeometryDrawing>(brush.Drawing);
			Assert.IsType<RectangleGeometry>(drawing.Geometry);
			Assert.NotNull(drawing.Pen);
			Assert.Equal(0.6, drawing.Pen!.Thickness, 6);
			Color penColour = Assert.IsAssignableFrom<ISolidColorBrush>(drawing.Pen.Brush).Color;
			Color accent = BlockHost.AccentColor();
			Assert.Equal(38, (int)penColour.A);
			Assert.Equal(accent.R, penColour.R);
			Assert.Equal(accent.G, penColour.G);
			Assert.Equal(accent.B, penColour.B);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>RefreshThemeVisuals</c>(重刷网格底纹 + 每张卡的蒙版)。
	/// mac 侧的触发点是 <c>ActualThemeVariantChanged</c>(差异表第 5 条),这里直接调成员验证效果。
	/// </summary>
	[Fact]
	public void RefreshThemeVisuals_rebuilds_the_grid_brush_and_every_card_mask()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(8, Card("a", 0.0, 0.0, 0.3, 0.3), Card("b", 0.5, 0.5, 0.3, 0.3, z: 1)));
			host.EditMode = true;
			host.GridLayerNow.Background = null;

			host.RefreshThemeVisuals();

			Assert.IsType<DrawingBrush>(host.GridLayerNow.Background);
			Color accent = BlockHost.AccentColor();
			foreach (DashHost.CardView view in host.ViewsNow)
			{
				Color mask = Assert.IsAssignableFrom<ISolidColorBrush>(view.EditMaskNow.Background).Color;
				Assert.Equal(10, (int)mask.A);
				Assert.Equal(accent.R, mask.R);
				Assert.Equal(accent.G, mask.G);
				Assert.Equal(accent.B, mask.B);
			}
		});
	}

	// ————— 吸附 / 最小值 —————

	/// <summary>
	/// 来源:Windows 源 <c>SnapV</c> —— 先钳到 [0,1];不吸附或轴长 ≤ 1 时原样返回;
	/// 否则 <c>round(v*axis/grid)*grid/axis</c> 再钳一次。
	/// </summary>
	[Fact]
	public void SnapV_rounds_to_the_grid_and_clamps_to_the_unit_interval()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(8));
			// 0.013 * 640 = 8.32 → /8 = 1.04 → round 1 → 8/640
			Assert.Equal(0.0125, host.SnapV(0.013, 640.0), 9);
			Assert.Equal(1.0, host.SnapV(1.5, 640.0), 9);
			Assert.Equal(0.0, host.SnapV(-0.5, 640.0), 9);
			Assert.Equal(0.5, host.SnapV(0.5, 640.0), 9);
			// 轴长 ≤ 1:不吸附
			Assert.Equal(0.013, host.SnapV(0.013, 1.0), 9);

			host.Layout.Grid = 0;
			Assert.Equal(0.013, host.SnapV(0.013, 640.0), 9);
		});
	}

	/// <summary>来源:Windows 源 <c>MinPx</c> 与 <c>FallbackMin = (W: 200, H: 120)</c>。</summary>
	[Fact]
	public void MinPx_prefers_the_configured_minimum_and_falls_back_to_200x120()
	{
		OnAvalonia(() =>
		{
			DashHost host = new DashHost();
			Assert.Equal((W: 200.0, H: 120.0), host.MinPx("whatever"));

			host.MinSizes["news"] = (260, 180);
			Assert.Equal((W: 260.0, H: 180.0), host.MinPx("news"));
			Assert.Equal((W: 200.0, H: 120.0), host.MinPx("other"));
		});
	}

	// ————— 增删卡 / 空位 —————

	/// <summary>
	/// 来源:Windows 源 <c>AddCard</c> + <c>FindFreeSpot</c> —— 依次尝试左上 / 右上 / 左下 / 右下
	/// 四个候选位置,取第一个与现有可见卡不相交的;尺寸是
	/// <c>clamp(max(0.3, minPxW/宽), 0.1, 1)</c>(高同理)。
	/// </summary>
	[Fact]
	public void AddCard_fills_the_four_candidate_spots_in_order_then_falls_back_to_005()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(0));
			using var shown = UiHost.Show(host);
			double w = host.Bounds.Width;
			double h = host.Bounds.Height;
			double cw = Clamp(Math.Max(0.3, 200.0 / w), 0.1, 1.0);
			double ch = Clamp(Math.Max(0.3, 120.0 / h), 0.1, 1.0);

			host.AddCard("a");
			host.AddCard("b");
			host.AddCard("c");
			host.AddCard("d");

			Assert.Equal(new[] { "a", "b", "c", "d" }, TypesOf(host));
			AssertRect(host, "a", 0.0, 0.0, cw, ch);
			AssertRect(host, "b", 1.0 - cw, 0.0, cw, ch);
			AssertRect(host, "c", 0.0, 1.0 - ch, cw, ch);
			AssertRect(host, "d", 1.0 - cw, 1.0 - ch, cw, ch);

			// 第五张:四个角都被占了 → 兜底 (0.05, 0.05)
			host.AddCard("e");
			AssertRect(host, "e", 0.05, 0.05, cw, ch);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>AddCard</c> —— 同 type 已有隐藏卡时**取消隐藏**并抬 Z,而不是再插入一张。
	/// </summary>
	[Fact]
	public void AddCard_unhides_an_existing_hidden_card_instead_of_adding_a_new_one()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("notes", 0.2, 0.2, 0.3, 0.3, z: 0, hidden: true));
			DashHost host = HostWith(layout);
			Assert.False(host.Has("notes"));
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.AddCard("notes");

			Assert.True(host.Has("notes"));
			Assert.Single(layout.Items);
			Assert.False(layout.Items[0].Hidden);
			Assert.Equal(new[] { "notes" }, TypesOf(host));
			Assert.Equal(1, changed);
		});
	}

	/// <summary>来源:Windows 源 <c>if (!_layout.Items.Any(c =&gt; c.Type == type &amp;&amp; !c.Hidden))</c> 的前提 —— 已可见则什么都不做。</summary>
	[Fact]
	public void AddCard_is_ignored_when_the_type_is_already_visible()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("notes", 0.2, 0.2, 0.3, 0.3));
			DashHost host = HostWith(layout);
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.AddCard("notes");

			Assert.Single(layout.Items);
			Assert.Equal(0, changed);
		});
	}

	/// <summary>来源:Windows 源 <c>RemoveCard</c>(丢出 Items → Rebuild → Changed)。</summary>
	[Fact]
	public void RemoveCard_drops_the_item_and_fires_Changed()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.0, 0.0, 0.3, 0.3, z: 0), Card("b", 0.5, 0.5, 0.3, 0.3, z: 1));
			DashHost host = HostWith(layout);
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.RemoveCard(layout.Items[0]);

			Assert.Single(layout.Items);
			Assert.Equal(new[] { "b" }, TypesOf(host));
			Assert.Equal(1, changed);
		});
	}

	/// <summary>来源:Windows 源 <c>Has(type)</c> —— 只认未隐藏的卡。</summary>
	[Fact]
	public void Has_reports_only_visible_cards()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(0, Card("a", 0.0, 0.0, 0.3, 0.3), Card("b", 0.0, 0.0, 0.3, 0.3, z: 1, hidden: true)));
			Assert.True(host.Has("a"));
			Assert.False(host.Has("b"));
			Assert.False(host.Has("nope"));
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>BringToTop</c> —— 从 Children 摘掉再追加(可视顺序最后 = 最上层),
	/// 并把卡的 <c>Z</c> 抬到 <c>NextZ()</c>。
	/// </summary>
	[Fact]
	public void BringToTop_moves_the_view_to_the_top_and_bumps_z()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.0, 0.0, 0.3, 0.3, z: 0), Card("b", 0.5, 0.5, 0.3, 0.3, z: 1));
			DashHost host = HostWith(layout);
			DashHost.CardView first = host.ViewsNow[0];
			DashHost.CardView second = host.ViewsNow[1];
			Assert.Same(second, host.Children[2]);

			host.BringToTop(first);

			Assert.Same(second, host.Children[1]);
			Assert.Same(first, host.Children[2]);
			Assert.Equal(2, first.Card.Z);
		});
	}

	// ————— 拖拽 / 缩放(注入缝) —————

	/// <summary>
	/// 来源:Windows 源 <c>Grab</c>(<c>_grabPoint = e.GetPosition(_host)</c>、
	/// <c>_grabRect = new Rect(Card.X, Card.Y, Card.W, Card.H)</c>、<c>BringToTop</c>)与
	/// <c>OnDragMove</c> 的 move 分支(<c>SnapV(Clamp.Of(grab.X + dx, 0, 1 - grab.W))</c>)。
	/// 这里 Grid = 0(不吸附)以便精确算术。
	/// </summary>
	[Fact]
	public void Grab_then_DragTo_moves_the_card_by_the_normalized_pointer_delta_and_clamps_it()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.2, 0.2, 0.2, 0.2));
			DashHost host = HostWith(layout);
			host.MinSizes["a"] = (10, 10);
			using var shown = UiHost.Show(host);
			double w = host.Bounds.Width;
			double h = host.Bounds.Height;

			DashHost.CardView view = host.ViewsNow[0];
			Point grab = new Point(100.0, 100.0);
			view.GrabCore("move", grab, null, null);
			Assert.Equal("move", view.ModeNow);

			view.DragTo(new Point(grab.X + 0.1 * w, grab.Y + 0.1 * h));
			Assert.Equal(0.3, view.Card.X, 6);
			Assert.Equal(0.3, view.Card.Y, 6);
			Assert.Equal(0.2, view.Card.W, 6);
			Assert.Equal(0.2, view.Card.H, 6);

			// 拖出画布右侧/下侧 → 钳在 1 - W / 1 - H
			view.DragTo(new Point(grab.X + 10.0 * w, grab.Y + 10.0 * h));
			Assert.Equal(0.8, view.Card.X, 6);
			Assert.Equal(0.8, view.Card.Y, 6);

			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};
			view.DropNow();
			Assert.Null(view.ModeNow);
			Assert.Equal(1, changed);
		});
	}

	/// <summary>来源:Windows 源 <c>OnDragMove</c> 的 <c>if (_mode == null ...) return;</c> —— 没抓起就不动。</summary>
	[Fact]
	public void DragTo_without_a_grab_changes_nothing()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.2, 0.2, 0.2, 0.2));
			DashHost host = HostWith(layout);
			using var shown = UiHost.Show(host);

			host.ViewsNow[0].DragTo(new Point(999.0, 999.0));

			Assert.Equal(0.2, layout.Items[0].X, 6);
			Assert.Equal(0.2, layout.Items[0].Y, 6);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>OnDragMove</c> 的 se 分支 + <c>num5/num6</c>
	/// (<c>min(1, (minPx + 10) / 轴长)</c>)—— 最小像素尺寸是硬下限。
	/// </summary>
	[Fact]
	public void Resizing_from_the_south_east_corner_respects_the_minimum_pixel_size()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.2, 0.2, 0.2, 0.2));
			DashHost host = HostWith(layout);
			host.MinSizes["a"] = (200, 120);
			using var shown = UiHost.Show(host);
			double w = host.Bounds.Width;
			double h = host.Bounds.Height;
			double minW = Math.Min(1.0, 210.0 / w);
			double minH = Math.Min(1.0, 130.0 / h);

			DashHost.CardView view = host.ViewsNow[0];
			Point grab = new Point(300.0, 300.0);
			view.GrabCore("se", grab, null, null);
			view.DragTo(new Point(grab.X - 10.0 * w, grab.Y - 10.0 * h));

			Assert.Equal(minW, view.Card.W, 6);
			Assert.Equal(minH, view.Card.H, 6);
			Assert.Equal(0.2, view.Card.X, 6);
			Assert.Equal(0.2, view.Card.Y, 6);
			view.DropNow();
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>OnDragMove</c> 的 nw 分支 —— 西/北边动的时候,
	/// 对角(右/下边)保持不动,<c>W = grab.Right - 新 X</c>。
	/// </summary>
	[Fact]
	public void Resizing_from_the_north_west_corner_keeps_the_opposite_corner_fixed()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.4, 0.4, 0.4, 0.4));
			DashHost host = HostWith(layout);
			host.MinSizes["a"] = (10, 10);
			using var shown = UiHost.Show(host);
			double w = host.Bounds.Width;
			double h = host.Bounds.Height;

			DashHost.CardView view = host.ViewsNow[0];
			Point grab = new Point(50.0, 50.0);
			view.GrabCore("nw", grab, null, null);
			view.DragTo(new Point(grab.X + 0.1 * w, grab.Y + 0.1 * h));

			Assert.Equal(0.5, view.Card.X, 6);
			Assert.Equal(0.5, view.Card.Y, 6);
			Assert.Equal(0.3, view.Card.W, 6);
			Assert.Equal(0.3, view.Card.H, 6);
			// 右下角没动
			Assert.Equal(0.8, view.Card.X + view.Card.W, 6);
			Assert.Equal(0.8, view.Card.Y + view.Card.H, 6);
			view.DropNow();
		});
	}

	/// <summary>来源:Windows 源 <c>OnDragMove</c> 的 e 分支 —— 右边界钳在画布 <c>1.0</c>。</summary>
	[Fact]
	public void Resizing_eastwards_stops_at_the_canvas_edge()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.4, 0.4, 0.3, 0.3));
			DashHost host = HostWith(layout);
			host.MinSizes["a"] = (10, 10);
			using var shown = UiHost.Show(host);
			double w = host.Bounds.Width;

			DashHost.CardView view = host.ViewsNow[0];
			Point grab = new Point(10.0, 10.0);
			view.GrabCore("e", grab, null, null);
			view.DragTo(new Point(grab.X + 10.0 * w, grab.Y));

			Assert.Equal(0.6, view.Card.W, 6);
			Assert.Equal(1.0, view.Card.X + view.Card.W, 6);
			view.DropNow();
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>OnDrop</c> —— 只有抓起过才清模式 + 放开捕获 + <c>NotifyChanged</c>。
	/// 重复松手不该重复通知。
	/// </summary>
	[Fact]
	public void DropNow_notifies_exactly_once_per_grab()
	{
		OnAvalonia(() =>
		{
			DashLayout layout = Layout(0, Card("a", 0.2, 0.2, 0.2, 0.2));
			DashHost host = HostWith(layout);
			using var shown = UiHost.Show(host);
			DashHost.CardView view = host.ViewsNow[0];
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			// 没抓起就松手:不通知
			view.DropNow();
			Assert.Equal(0, changed);

			view.GrabCore("move", new Point(10.0, 10.0), null, null);
			view.DragTo(new Point(20.0, 20.0));
			view.DropNow();
			Assert.Equal(1, changed);
			Assert.Null(view.ModeNow);

			view.DropNow();
			view.DropNow();
			Assert.Equal(1, changed);
		});
	}

	/// <summary>
	/// 动画关掉时(离屏 / 测试进程没有推进动画时钟,D-69 / D-110):
	/// <c>Motion.StaggerItems</c> 直接返回,Rebuild 之后各卡的终态就是"完全不透明、没有变换",
	/// 再推一次时钟也不会变 —— 没有迟到的开场动画。
	/// </summary>
	[Fact]
	public void With_motion_disabled_rebuild_leaves_no_stagger_running()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(0, Card("a", 0.0, 0.0, 0.3, 0.3), Card("b", 0.5, 0.5, 0.3, 0.3, z: 1)));
			using var shown = UiHost.Show(host);
			Assert.False(Motion.Enabled, "离屏/测试进程里 Motion.Enabled 必须为假(Motion.HasClock)");

			UiHost.Flush();
			foreach (DashHost.CardView view in host.ViewsNow)
			{
				Assert.Equal(1.0, view.Opacity, 6);
				Assert.Null(view.RenderTransform);
			}

			// 连拖带放一轮,状态仍当场落定
			DashHost.CardView first = host.ViewsNow[0];
			first.GrabCore("move", new Point(1.0, 1.0), null, null);
			first.DragTo(new Point(2.0, 2.0));
			first.DropNow();
			UiHost.Flush();
			Assert.Null(first.ModeNow);
			Assert.Equal(1.0, first.Opacity, 6);
		});
	}

	// ————— 测量 / 排布 —————

	/// <summary>来源:Windows 源 <c>MeasureOverride</c> —— 无穷可用尺寸时按 900×600。</summary>
	[Fact]
	public void Measure_falls_back_to_900x600_for_an_infinite_available_size()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(0, Card("a", 0.0, 0.0, 0.5, 0.5)));
			host.Measure(Size.Infinity);
			Assert.Equal(900.0, host.DesiredSize.Width, 6);
			Assert.Equal(600.0, host.DesiredSize.Height, 6);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>ArrangeOverride</c> —— 每张卡按归一化矩形落位,
	/// 宽高各自 <c>max(40, W*宽 - 10)</c> / <c>max(30, H*高 - 10)</c>(即卡片之间留 10px 的"缝",
	/// 对齐同一文件里的 <c>Gap</c> 常量);网格底纹层铺满整个宿主。
	/// </summary>
	[Fact]
	public void Arrange_places_each_card_by_its_normalized_rect_and_stretches_the_grid_layer()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(8, Card("a", 0.5, 0.5, 0.5, 0.5)));
			using var shown = UiHost.Show(host);
			UiHost.Flush();
			double w = host.Bounds.Width;
			double h = host.Bounds.Height;
			Assert.True(w > 40.0 && h > 30.0, "宿主必须先被排布出真实尺寸: " + w + "x" + h);

			DashHost.CardView view = host.ViewsNow[0];
			Assert.Equal(0.5 * w, view.Bounds.X, 6);
			Assert.Equal(0.5 * h, view.Bounds.Y, 6);
			Assert.Equal(Math.Max(40.0, 0.5 * w - 10.0), view.Bounds.Width, 6);
			Assert.Equal(Math.Max(30.0, 0.5 * h - 10.0), view.Bounds.Height, 6);

			// 网格底纹层默认不可见,而 Avalonia 的布局引擎不给不可见控件排布(Bounds 恒为 0),
			// 所以要在"它真的该铺满"的状态(编辑态 + 吸附)下断言。
			host.EditMode = true;
			UiHost.Flush();
			Assert.True(host.GridLayerNow.IsVisible);
			Assert.Equal(w, host.GridLayerNow.Bounds.Width, 6);
			Assert.Equal(h, host.GridLayerNow.Bounds.Height, 6);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>CardView.MakeHandle</c> —— 八个手柄,名字与中心比例是
	/// nw(0,0) n(0.5,0) ne(1,0) w(0,0.5) e(1,0.5) sw(0,1) s(0.5,1) se(1,1);
	/// 11×11、圆角 3、描边 1.5;<c>ArrangeOverride</c> 把它们摆到 <c>中心*边长 - 5.5</c>。
	/// mac 差异:光标用 <c>new Cursor(StandardCursorType.X)</c>(差异表第 2 条)。
	/// </summary>
	[Fact]
	public void Edit_mode_lays_out_eight_resize_handles_at_the_wpf_anchor_points()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(0, Card("a", 0.0, 0.0, 1.0, 1.0)));
			using var shown = UiHost.Show(host);
			host.EditMode = true;
			UiHost.Flush();

			DashHost.CardView view = host.ViewsNow[0];
			Canvas layer = view.HandleLayerNow;
			Assert.Equal(8, layer.Children.Count);
			Assert.Equal(
				new[] { "nw", "n", "ne", "w", "e", "sw", "s", "se" },
				TagsOf(layer));

			double w = layer.Bounds.Width;
			double h = layer.Bounds.Height;
			foreach (Control child in layer.Children)
			{
				Border handle = Assert.IsAssignableFrom<Border>(child);
				Assert.Equal(11.0, handle.Width, 6);
				Assert.Equal(11.0, handle.Height, 6);
				Assert.Equal(3.0, handle.CornerRadius.TopLeft, 6);
				Assert.Equal(1.5, handle.BorderThickness.Left, 6);
				Assert.NotNull(handle.Cursor);
			}

			Border nw = Assert.IsAssignableFrom<Border>(layer.Children[0]);
			Assert.Equal(-5.5, Canvas.GetLeft(nw), 6);
			Assert.Equal(-5.5, Canvas.GetTop(nw), 6);

			Border e = Assert.IsAssignableFrom<Border>(layer.Children[4]);
			Assert.Equal(w - 5.5, Canvas.GetLeft(e), 6);
			Assert.Equal(h / 2.0 - 5.5, Canvas.GetTop(e), 6);

			Border se = Assert.IsAssignableFrom<Border>(layer.Children[7]);
			Assert.Equal(w - 5.5, Canvas.GetLeft(se), 6);
			Assert.Equal(h - 5.5, Canvas.GetTop(se), 6);
		});
	}

	/// <summary>来源:Windows 源 <c>SetEdit</c> —— 非编辑态手柄层、蒙版、关闭按钮都不显示。</summary>
	[Fact]
	public void Handles_and_close_button_are_hidden_outside_edit_mode()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(8, Card("a", 0.0, 0.0, 0.3, 0.3)));
			DashHost.CardView view = host.ViewsNow[0];
			Assert.False(view.HandleLayerNow.IsVisible);
			Assert.False(view.EditMaskNow.IsVisible);
			Assert.False(view.CloseNow.IsVisible);
			Assert.True(view.BodyNow.IsHitTestVisible);
			// 蒙版底色在构造时就刷好了(Windows 构造里也调了一次 RefreshEditMask)
			Assert.Equal(10, (int)Assert.IsAssignableFrom<ISolidColorBrush>(view.EditMaskNow.Background).Color.A);
		});
	}

	/// <summary>来源:Windows 源 <c>SetContent</c> —— 换内容时把旧宿主里的同一个控件先摘下来。</summary>
	[Fact]
	public void SetContent_detaches_the_content_from_a_foreign_host()
	{
		OnAvalonia(() =>
		{
			DashHost host = HostWith(Layout(0, Card("a", 0.0, 0.0, 0.3, 0.3)));
			using var shown = UiHost.Show(host);
			DashHost.CardView view = host.ViewsNow[0];

			Border content = new Border { Height = 12.0 };
			ContentControl foreign = new ContentControl { Content = content };
			UiHost.Show(foreign);
			Assert.NotNull(content.Parent);

			view.SetContent("新标题", content);

			Assert.Null(foreign.Content);
			Assert.Equal("新标题", view.TitleNow.Text);
			Assert.Same(content, view.BodyNow.Content);
		});
	}

	private static string[] TypesOf(DashHost host)
	{
		IReadOnlyList<DashHost.CardView> views = host.ViewsNow;
		string[] types = new string[views.Count];
		for (int i = 0; i < views.Count; i++)
		{
			types[i] = views[i].Card.Type;
		}
		return types;
	}

	private static void AssertRect(DashHost host, string type, double x, double y, double w, double h)
	{
		DashCard? found = null;
		foreach (DashCard card in host.Layout.Items)
		{
			if (card.Type == type)
			{
				found = card;
			}
		}
		Assert.NotNull(found);
		Assert.Equal(x, found!.X, 6);
		Assert.Equal(y, found.Y, 6);
		Assert.Equal(w, found.W, 6);
		Assert.Equal(h, found.H, 6);
	}

	private static List<string> TagsOf(Canvas layer)
	{
		List<string> tags = new List<string>();
		foreach (Control child in layer.Children)
		{
			tags.Add((string)child.Tag!);
		}
		return tags;
	}

	private static double Clamp(double value, double min, double max)
	{
		return (value < min) ? min : ((value > max) ? max : value);
	}
}