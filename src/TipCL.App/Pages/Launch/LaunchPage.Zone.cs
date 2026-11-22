// AI4-launch:启动页 v3 右侧组件区 —— 常态展示 + 编辑态(拖动重排 / 尺寸 / 移除 / 撤销重做)+ 组件库抽屉 + 提示条。
// 准绳 _visual\mockups\launch_v3.html 与 _visual\DECISIONS-UI.md「启动页」:
//   * 组件区是独立的 4 列系统(列宽 74 / 行高 96 / 间距 12;2×1 / 2×2 / 4×2),不进通用布局引擎,不用 FitBox 缩放;
//   * 窗口 < 900 宽减到 2 列(4×2 换 2×2 版式),放不下的收起并在「编辑组件」左边提示;
//   * 编辑条只有 添加组件 / 重置 / 完成,导入导出收进「⋯」;Ctrl+Z / Ctrl+Y;Esc 退出(抽屉开着时先关抽屉);
//   * 数据存布局文件 v2 的 pages.launch.zone(LaunchZone.Read / Write),旧文件没有 zone 时回落默认组件。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Shell;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using UiClass = TipCL.App.Ui.Ui;
using W = TipCL.App.Pages.Launch.LaunchWidgets;

namespace TipCL.App.Pages.Launch;

public sealed partial class LaunchPage : IZoneActions
{
    // —————————————— 状态 ——————————————

    private ZoneEditor _zone = new(LaunchZone.Defaults());

    private readonly ZoneData _zdata = new();

    private int _zdataGen;

    /// <summary>组件区画布(格子坐标 → 像素,子元素绝对定位)。</summary>
    private readonly Canvas _zoneCanvas = new() { Name = "LaunchZone", Width = LaunchZone.Width(4), Height = LaunchZone.Height };

    /// <summary>编辑态的空格子虚线占位。</summary>
    private readonly Canvas _cellLayer = new() { IsHitTestVisible = false, IsVisible = false };

    /// <summary>拖动时的落点预告(主题色 .24 底 + 1.5 虚线)。</summary>
    private readonly Rectangle _ph = new() { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 }, IsVisible = false, IsHitTestVisible = false };

    private readonly Dictionary<string, ZoneCard> _cards = new(StringComparer.Ordinal);

    private Border? _editBtn;

    private Border? _overChip;

    private TextBlock? _overText;

    private StackPanel? _zfoot;

    private Control? _ebar;

    private Border? _ebox;

    private Popup? _moreMenu;

    private Border? _drawer;

    private StackPanel? _libList;

    private Border? _dim;

    private Border? _lockChip;

    private Border? _toastBox;

    private TextBlock? _toastText;

    private DispatcherTimer? _toastTimer;

    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(20) };

    private bool _editing;

    private bool _libOpen;

    private int _cols = LaunchZone.ColsWide;

    private int _rows = LaunchZone.Rows;

    /// <summary>页面顶部被标题栏盖住的高度(标题栏叠在背景图上时 = 46,否则 0)。</summary>
    private double _topInset;

    private DragState? _drag;

    private sealed class ZoneCard
    {
        public ZoneItem Item = new();

        public Panel Root = new();

        public Border Shell = new();

        public Rectangle EditLine = new();

        public StackPanel Tools = new();

        public Border? SizeBtn;

        public TextBlock? SizeText;

        public string Key = "";

        public bool Placed;
    }

    private sealed class DragState
    {
        public string Id = "";

        public ZoneCard Card = null!;

        public Point Start;

        public Point Offset;

        public bool Started;

        public List<ZoneItem>? Order;

        public IPointer? Pointer;
    }

    /// <summary>主题色(启动按钮 / 今天的柱子 / 编辑条主按钮共用)。</summary>
    private static IBrush Accent() => UiClass.Brush("B.AccentStrong") ?? Brushes.SeaGreen;

    private static Color AccentColor() => (Accent() as ISolidColorBrush)?.Color ?? Colors.SeaGreen;

    private static IBrush AccentA(byte a)
    {
        var c = AccentColor();
        return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
    }

    // —————————————— 外部入口(设置 → 布局 → 页面「编辑」、右键「自定义布局...」、AI 工具共用) ——————————————

    /// <summary>当前的启动页实例(只有一个);界面线程用。</summary>
    internal static LaunchPage? Current { get; private set; }

    /// <summary>
    /// 请启动页进入组件编辑态(外壳 / 设置页调;启动页还没建出来时记下,激活后再进)。
    /// 与 AI4-layout 的约定:通用页面编辑态不处理 launch 页,launch 的「编辑」统一走这里。
    /// </summary>
    public static void RequestZoneEdit()
    {
        if (Current != null && Current.IsAttachedToVisualTree())
            Current.EnterZoneEdit();
        else
            _pendingEdit = true;
    }

    private static bool _pendingEdit;

    // —————————————— 建控件 ——————————————

    private Control BuildZoneLayer()
    {
        _ph.Fill = AccentA(61);
        _ph.Stroke = Accent();
        _zoneCanvas.Children.Add(_cellLayer);
        _zoneCanvas.Children.Add(_ph);
        _zoneCanvas.HorizontalAlignment = HorizontalAlignment.Right;
        _zoneCanvas.VerticalAlignment = VerticalAlignment.Top;

        // 「✎ 编辑组件」:h28 / r8 / 白 .12 底 / 白 .2 边 / 12 号,常态 .7 不透明,悬停 1
        var pen = W.Ico(W.IcoPencil, 13, W.W(255));
        var editTxt = W.T(I18nCore.Tr("编辑组件"), 12);
        editTxt.VerticalAlignment = VerticalAlignment.Center;
        _editBtn = new Border
        {
            Name = "ZoneEditBtn",
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(8),
            Background = W.W(31),
            BorderBrush = W.W(51),
            BorderThickness = new Thickness(1),
            Opacity = 0.7,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { pen, editTxt } },
        };
        _editBtn.PointerEntered += (_, _) => { _editBtn.Opacity = 1; _editBtn.Background = W.W(56); };
        _editBtn.PointerExited += (_, _) => { _editBtn.Opacity = 0.7; _editBtn.Background = W.W(31); };
        _editBtn.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                EnterZoneEdit();
                e.Handled = true;
            }
        };
        ToolTip.SetTip(_editBtn, I18nCore.Tr("编辑组件"));

        // 放不下的提示:12 号 / 底 rgba(10,14,22,.55) / 白 .14 边
        _overText = W.T("", 12, 204);
        _overChip = new Border
        {
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(140, 10, 14, 22)),
            BorderBrush = W.W(36),
            BorderThickness = new Thickness(1),
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _overText,
        };
        _zfoot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _overChip, _editBtn },
        };

        var layer = new Panel { Children = { _zoneCanvas, _zfoot } };
        _clockTimer.Tick += (_, _) => W.TickClocks(_zoneCanvas);
        return layer;
    }

    /// <summary>编辑态遮罩 + 主区域锁提示 + 编辑条 + 组件库抽屉 + 提示条。</summary>
    private void BuildEditChrome(Panel root)
    {
        _dim = new Border { Background = new SolidColorBrush(Color.FromArgb(128, 8, 10, 16)), IsHitTestVisible = false, IsVisible = false };
        root.Children.Insert(2, _dim); // 背景图、遮罩渐变之上,主区域之下

        // 编辑条
        var title = new TextBlock { Text = I18nCore.Tr("编辑启动页"), FontSize = 13, FontWeight = FontWeight.ExtraBold, Foreground = Ink(), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var add = BarBtn(I18nCore.Tr("添加组件"), BarKind.Soft, OpenLib, W.IcoPlus);
        add.Name = "ZoneAdd";
        var reset = BarBtn(I18nCore.Tr("重置"), BarKind.Plain, () =>
        {
            if (_zone.Reset())
            {
                LayoutCards();
                Toast(I18nCore.Tr("已恢复默认布局 · Ctrl+Z 撤销"));
            }
        });
        reset.Name = "ZoneReset";
        var done = BarBtn(I18nCore.Tr("完成"), BarKind.Primary, ExitZoneEdit);
        done.Name = "ZoneDone";
        var more = BarBtn("", BarKind.Icon, () => { if (_moreMenu != null) _moreMenu.IsOpen = !_moreMenu.IsOpen; }, W.IcoMore);
        more.Name = "ZoneMore";
        ToolTip.SetTip(more, I18nCore.Tr("更多"));
        _ebox = new Border
        {
            Padding = new Thickness(14, 8, 8, 8),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(247, 255, 255, 255)),
            BoxShadow = BoxShadows.Parse("0 10 30 0 #59000000"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { title, add, reset, done, more } },
        };
        var hint = new TextBlock { FontSize = 12, Foreground = W.W(184), Margin = new Thickness(4, 8, 0, 0) };
        hint.Inlines = new Avalonia.Controls.Documents.InlineCollection();
        hint.Inlines.Add(new Avalonia.Controls.Documents.Run(I18nCore.Tr("拖动组件换位置 · 点尺寸标签切换大小 · ")));
        hint.Inlines.Add(Kbd("Ctrl Z"));
        hint.Inlines.Add(new Avalonia.Controls.Documents.Run(" " + I18nCore.Tr("撤销") + " "));
        hint.Inlines.Add(Kbd("Ctrl Y"));
        hint.Inlines.Add(new Avalonia.Controls.Documents.Run(" " + I18nCore.Tr("重做")));
        _moreMenu = BuildMoreMenu(more);
        var ebar = new StackPanel
        {
            Name = "ZoneEditBar",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            Children = { _ebox, hint, _moreMenu },
        };
        _ebar = ebar;
        root.Children.Add(ebar);

        // 组件库抽屉:左侧 344 宽白底,顶在标题栏下
        var dh = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 16, 16, 12) };
        var dhText = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = I18nCore.Tr("组件库"), FontSize = 15, FontWeight = FontWeight.ExtraBold, Foreground = Ink() },
                new TextBlock { Text = I18nCore.Tr("点「添加」放进右侧组件区，放满了会提示"), FontSize = 12, Foreground = Ink3(), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap },
            },
        };
        dh.Children.Add(dhText);
        var close = BarBtn("", BarKind.Icon, CloseLib, W.IcoX);
        close.Name = "ZoneLibClose";
        ToolTip.SetTip(close, I18nCore.Tr("关闭"));
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(close, 1);
        dh.Children.Add(close);
        _libList = new StackPanel { Spacing = 8, Margin = new Thickness(16, 0, 16, 16) };
        var scroll = new ScrollViewer { Content = _libList, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var dgrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        dgrid.Children.Add(dh);
        Grid.SetRow(scroll, 1);
        dgrid.Children.Add(scroll);
        _drawer = new Border
        {
            Name = "ZoneLibrary",
            Width = 344,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.White,
            BoxShadow = BoxShadows.Parse("12 0 40 0 #59000000"),
            IsVisible = false,
            Child = dgrid,
        };
        root.Children.Add(_drawer);

        // 提示条(页面内,底部居中):rgba(20,22,28,.92) / 圆角 12 / 13 号
        _toastText = new TextBlock { FontSize = 13, Foreground = Brushes.White };
        _toastBox = new Border
        {
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(235, 20, 22, 28)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 72),
            IsHitTestVisible = false,
            IsVisible = false,
            Child = _toastText,
        };
        root.Children.Add(_toastBox);
    }

    private static Avalonia.Controls.Documents.Inline Kbd(string text)
    {
        var b = new Border
        {
            Padding = new Thickness(5, 0, 5, 0),
            CornerRadius = new CornerRadius(8),
            Background = W.W(36),
            Child = new TextBlock { Text = text, FontSize = 12, FontFamily = W.Mono, Foreground = W.W(230) },
        };
        return new Avalonia.Controls.Documents.InlineUIContainer(b) { BaselineAlignment = BaselineAlignment.Center };
    }

    // 编辑条 / 抽屉在白底上:效果图墨色
    private static IBrush Ink() => new SolidColorBrush(Color.Parse("#1F1F1F"));

    private static IBrush Ink2() => new SolidColorBrush(Color.Parse("#5C5C5C"));

    private static IBrush Ink3() => new SolidColorBrush(Color.Parse("#8A8A8A"));

    private static IBrush Paper2() => new SolidColorBrush(Color.Parse("#F3F3F3"));

    private static IBrush Line() => new SolidColorBrush(Color.FromArgb(23, 0, 0, 0));

    private enum BarKind { Plain, Soft, Primary, Icon }

    /// <summary>效果图 .b:h30 / r8 / 13 号;soft = 主题色 .12 底 + .32 边 + 主题色粗字;pri = 主题色底白粗字;ic = 30×30。</summary>
    private Border BarBtn(string text, BarKind kind, Action click, string? icon = null)
    {
        IBrush fg = kind switch { BarKind.Soft => Accent(), BarKind.Primary => Brushes.White, _ => Ink() };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null)
            row.Children.Add(W.Ico(icon, kind == BarKind.Icon ? 14 : 13, fg, icon == W.IcoMore ? 0 : 1.6, icon == W.IcoMore ? fg : null));
        if (text.Length > 0)
            row.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = fg, FontWeight = kind is BarKind.Soft or BarKind.Primary ? FontWeight.Bold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border
        {
            Height = 30,
            MinWidth = 30,
            Padding = kind == BarKind.Icon ? new Thickness(0) : new Thickness(12, 0, 12, 0),
            Width = kind == BarKind.Icon ? 30 : double.NaN,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row,
        };
        void Paint(bool hover)
        {
            switch (kind)
            {
                case BarKind.Soft:
                    b.Background = hover ? Accent() : AccentA(31);
                    b.BorderBrush = hover ? Accent() : AccentA(82);
                    foreach (var c in row.Children)
                    {
                        if (c is TextBlock t) t.Foreground = hover ? Brushes.White : Accent();
                        if (c is Avalonia.Controls.Shapes.Path p) p.Stroke = hover ? Brushes.White : Accent();
                    }
                    break;
                case BarKind.Primary:
                    b.Background = Accent();
                    b.BorderBrush = Accent();
                    b.Opacity = hover ? 0.94 : 1;
                    break;
                default:
                    b.Background = hover ? new SolidColorBrush(Color.Parse("#E9E9E9")) : Paper2();
                    b.BorderBrush = Line();
                    break;
            }
        }
        Paint(false);
        b.PointerEntered += (_, _) => Paint(true);
        b.PointerExited += (_, _) => Paint(false);
        b.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            click();
            e.Handled = true;
        };
        b.Tag = (Action<bool>)Paint;
        return b;
    }

    private Popup BuildMoreMenu(Control anchor)
    {
        var list = new StackPanel { Spacing = 0 };
        void Item(string text, string name, Action act)
        {
            var t = new TextBlock { Text = text, FontSize = 13, Foreground = Ink() };
            var row = new Border { Name = name, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
            row.PointerEntered += (_, _) => row.Background = Paper2();
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left)
                    return;
                if (_moreMenu != null)
                    _moreMenu.IsOpen = false;
                act();
                e.Handled = true;
            };
            list.Children.Add(row);
        }
        Item(I18nCore.Tr("导入布局…"), "ZoneImport", OpenImport);
        Item(I18nCore.Tr("导出布局…"), "ZoneExport", () => _ = ExportZoneAsync());
        Item(I18nCore.Tr("复制布局码"), "ZoneCopyCode", () => _ = CopyZoneCodeAsync());
        list.Children.Add(new Border { Height = 1, Background = Line(), Margin = new Thickness(6, 4, 6, 4) });
        Item(I18nCore.Tr("放弃这次改动"), "ZoneRevert", () =>
        {
            _zone.Revert();
            LayoutCards();
            RenderLib();
            Toast(I18nCore.Tr("已恢复到上次保存的布局"));
        });
        var shell = new Border
        {
            Width = 200,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.White,
            BoxShadow = BoxShadows.Parse("0 12 32 0 #59000000"),
            Margin = new Thickness(0, 8, 12, 12),
            Child = list,
        };
        var p = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            IsLightDismissEnabled = true,
            Child = shell,
        };
        return p;
    }

    // —————————————— 读写 ——————————————

    /// <summary>从布局文件读组件区(没有 → 默认;旧版启动页的便签区块迁成便签组件)。</summary>
    private void LoadZone()
    {
        List<ZoneItem> items;
        try
        {
            var doc = LayoutStoreV2.Load();
            doc.Layout.Pages.TryGetValue(LaunchZone.PageId, out var page);
            items = LaunchZone.Read(page, Py.S(Config.CONFIG.Get("ui_notes", "")));
        }
        catch (Exception ex)
        {
            AppLog.Warn("启动页组件区读取失败,用默认组件: " + ex.Message);
            items = LaunchZone.Defaults();
        }
        _zone = new ZoneEditor(items) { Cols = _cols, Rows = _rows };
    }

    private bool _saving;

    /// <summary>写回布局文件(只改 pages.launch.zone)。</summary>
    private void SaveZone()
    {
        try
        {
            _saving = true;
            var doc = LayoutStoreV2.Load();
            if (!doc.Layout.Pages.TryGetValue(LaunchZone.PageId, out var page) || page == null)
            {
                page = new PageLayoutV2();
                doc.Layout.Pages[LaunchZone.PageId] = page;
            }
            LaunchZone.Write(page, _zone.Items);
            LayoutStoreV2.Save(doc);
        }
        catch (Exception ex)
        {
            AppLog.Warn("启动页组件区保存失败: " + ex.Message);
            Toast(I18nCore.Tr("布局没能保存") + ":" + ex.Message);
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>布局文件被别处改了(设置页应用方案 / 重置 / AI 工具):常态下跟着重读。</summary>
    private void OnLayoutChanged()
    {
        if (_saving || _editing)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            LoadZone();
            LayoutCards();
        });
    }

    // —————————————— 排版 ——————————————

    /// <summary>按页面宽高定列数 / 行数,并摆主区域、组件区、底部条的边距。</summary>
    private void ApplyZoneMetrics()
    {
        double w = Bounds.Width, h = Bounds.Height;
        int cols = LaunchZone.ColsFor(w);
        double avail = h - _topInset - 18 - 10 - 28 - 16;
        int rows = LaunchZone.RowsFor(avail);
        bool changed = cols != _cols || rows != _rows;
        _cols = cols;
        _rows = rows;
        _zone.Cols = cols;
        _zone.Rows = rows;
        double zw = LaunchZone.Width(cols);
        double zh = LaunchZone.Span(rows, LaunchZone.RowH);
        _zoneCanvas.Width = zw;
        _zoneCanvas.Height = zh;
        _zoneCanvas.Margin = new Thickness(0, _topInset + 18, 24, 0);
        if (_zfoot != null)
            _zfoot.Margin = new Thickness(0, _topInset + 18 + zh + 10, 24, 0);
        if (_heroHost != null)
            _heroHost.Margin = new Thickness(56, _topInset, 24 + zw + 40, 70);
        if (_ebar != null)
            _ebar.Margin = new Thickness(24, _topInset + 14, 0, 0);
        if (_drawer != null)
            _drawer.Margin = new Thickness(0, _topInset, 0, 0);
        if (changed)
        {
            BuildCells();
            LayoutCards();
        }
    }

    private void BuildCells()
    {
        _cellLayer.Children.Clear();
        for (int y = 0; y < _rows; y++)
        {
            for (int x = 0; x < _cols; x++)
            {
                var c = new Rectangle
                {
                    Width = LaunchZone.ColW,
                    Height = LaunchZone.RowH,
                    RadiusX = 8,
                    RadiusY = 8,
                    Stroke = W.W(46),
                    StrokeThickness = 1,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                };
                Canvas.SetLeft(c, LaunchZone.OffsetX(x));
                Canvas.SetTop(c, LaunchZone.OffsetY(y));
                _cellLayer.Children.Add(c);
            }
        }
        _cellLayer.Width = LaunchZone.Width(_cols);
        _cellLayer.Height = LaunchZone.Span(_rows, LaunchZone.RowH);
    }

    /// <summary>按当前(或拖动中的临时)顺序摆组件;skip = 正被拖着的那张(位置跟手,不归位)。</summary>
    private void LayoutCards(IReadOnlyList<ZoneItem>? list = null, string? skip = null)
    {
        list ??= _zone.Items;
        var pack = LaunchZone.Pack(list, _cols, _rows);
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in list)
        {
            alive.Add(it.Id);
            if (!_cards.TryGetValue(it.Id, out var card))
            {
                card = MakeCard(it);
                _cards[it.Id] = card;
                _zoneCanvas.Children.Add(card.Root);
            }
            card.Item = it;
            if (!pack.Placed.TryGetValue(it.Id, out var r))
            {
                card.Root.IsVisible = false;
                continue;
            }
            card.Root.IsVisible = true;
            string key = it.Signature() + "|" + r.EffSize + "|" + _zdataGen + "|" + _editing;
            if (card.Key != key)
                FillCard(card, r.EffSize);
            card.Key = key;
            if (it.Id == skip)
                continue;
            double left = LaunchZone.OffsetX(r.X), top = LaunchZone.OffsetY(r.Y);
            double w = LaunchZone.Span(r.W, LaunchZone.ColW), h = LaunchZone.Span(r.H, LaunchZone.RowH);
            // 第一次摆放直接到位(不从 0,0 飞过来);之后的变化按效果图 .22s ease-out 过渡(巡检里 Motion 关 → 直接到位)
            if (!card.Placed || !Motion.Enabled)
                card.Root.Transitions = null;
            else if (card.Root.Transitions == null)
                card.Root.Transitions = CardTransitions();
            Canvas.SetLeft(card.Root, left);
            Canvas.SetTop(card.Root, top);
            card.Root.Width = w;
            card.Root.Height = h;
            card.Placed = true;
        }
        foreach (var id in _cards.Keys.ToList())
        {
            if (alive.Contains(id))
                continue;
            _zoneCanvas.Children.Remove(_cards[id].Root);
            _cards.Remove(id);
        }
        // 放不下的收起并提示(窗口变宽 / 变高后自动回来)
        int n = pack.Overflow.Count;
        if (_overChip != null && _overText != null)
        {
            _overChip.IsVisible = n > 0;
            _overText.Text = string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("另有 {0} 个组件放不下，窗口变宽后自动回来"), n);
        }
    }

    private static Transitions CardTransitions()
    {
        var d = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEaseOut();
        return new Transitions
        {
            new DoubleTransition { Property = Canvas.LeftProperty, Duration = d, Easing = ease },
            new DoubleTransition { Property = Canvas.TopProperty, Duration = d, Easing = ease },
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = d, Easing = ease },
            new DoubleTransition { Property = Layoutable.HeightProperty, Duration = d, Easing = ease },
        };
    }

    private ZoneCard MakeCard(ZoneItem it)
    {
        var card = new ZoneCard { Item = it };
        card.Shell = W.Glass(new Panel());
        card.EditLine = new Rectangle
        {
            RadiusX = 12,
            RadiusY = 12,
            Stroke = W.W(128),
            StrokeThickness = 1.5,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
            IsHitTestVisible = false,
            IsVisible = false,
        };
        card.Tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            IsVisible = false,
        };
        card.Root = new Panel { Name = "Zone_" + it.Id, Background = Brushes.Transparent, Children = { card.Shell, card.EditLine, card.Tools } };
        card.Root.PointerPressed += (s, e) => OnCardPressed(card, e);
        card.Root.PointerMoved += (s, e) => OnCardMoved(card, e);
        card.Root.PointerReleased += (s, e) => OnCardReleased(card, e);
        card.Root.PointerCaptureLost += (s, e) => { if (_drag != null && _drag.Card == card) EndDrag(commit: true); };
        card.Root.PointerEntered += (_, _) => { if (_editing && _drag == null) card.EditLine.Stroke = W.W(255); };
        card.Root.PointerExited += (_, _) => { if (_editing && _drag == null) card.EditLine.Stroke = W.W(128); };
        return card;
    }

    private void FillCard(ZoneCard card, string es)
    {
        var it = card.Item;
        var content = W.Content(it, es, _zdata, this, Accent);
        content.IsHitTestVisible = !_editing;
        card.Shell.Child = content;
        card.EditLine.IsVisible = _editing;
        card.Shell.BorderThickness = new Thickness(_editing ? 0 : 1);
        card.Tools.Children.Clear();
        card.Tools.IsVisible = _editing;
        if (LaunchZone.SizesOf(it.Type).Count > 1)
        {
            var t = new TextBlock { Text = LaunchZone.DimLabel(es), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Ink(), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var sb = ToolBtn(t, () => CycleSize(it.Id));
            ToolTip.SetTip(sb, I18nCore.Tr("切换尺寸"));
            sb.Name = "ZoneSize_" + it.Id;
            card.SizeBtn = sb;
            card.SizeText = t;
            card.Tools.Children.Add(sb);
        }
        var x = ToolBtn(W.Ico(W.IcoX, 11, Ink(), 2), () => RemoveCard(it.Id), del: true);
        x.Name = "ZoneDel_" + it.Id;
        ToolTip.SetTip(x, I18nCore.Tr("移除"));
        card.Tools.Children.Add(x);
    }

    /// <summary>组件右上的小按钮:h22 / 最小宽 22 / 白 .92 底 / 圆角 8;移除钮悬停变主题色。</summary>
    private Border ToolBtn(Control child, Action click, bool del = false)
    {
        var b = new Border
        {
            Height = 22,
            MinWidth = 22,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = child,
        };
        b.PointerEntered += (_, _) =>
        {
            b.Background = del ? Accent() : Brushes.White;
            if (del && child is Avalonia.Controls.Shapes.Path p) p.Stroke = Brushes.White;
        };
        b.PointerExited += (_, _) =>
        {
            b.Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
            if (del && child is Avalonia.Controls.Shapes.Path p) p.Stroke = Ink();
        };
        b.PointerPressed += (_, e) => e.Handled = true; // 不让按下冒泡成「开始拖动」
        b.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            click();
            e.Handled = true;
        };
        return b;
    }

    // —————————————— 编辑态 ——————————————

    internal bool ZoneEditing => _editing;

    internal void EnterZoneEdit()
    {
        if (_editing)
            return;
        _editing = true;
        _zone.Commit(); // 进入时的状态 = 「放弃这次改动」的回退点
        SetEditChrome();
        LayoutCards();
        AttachEditKeys(true);
        Focus();
    }

    internal void ExitZoneEdit()
    {
        if (!_editing)
            return;
        bool dirty = _zone.Dirty;
        CloseLibCore();
        _editing = false;
        if (dirty)
            SaveZone();
        _zone.Commit();
        SetEditChrome();
        LayoutCards();
        AttachEditKeys(false);
        if (dirty)
            Toast(I18nCore.Tr("布局已保存"));
    }

    private void SetEditChrome()
    {
        if (_dim != null) _dim.IsVisible = _editing;
        if (_ebar != null) _ebar.IsVisible = _editing && !_libOpen;
        if (_editBtn != null) _editBtn.IsVisible = !_editing;
        if (_lockChip != null) _lockChip.IsVisible = _editing;
        _cellLayer.IsVisible = _editing;
        if (_heroHost != null)
        {
            _heroHost.Opacity = _editing ? 0.45 : 1;
            _heroHost.IsHitTestVisible = !_editing;
        }
        if (_newsCard != null) _newsCard.IsVisible = !_editing;
        if (_aiPill != null) _aiPill.IsVisible = !_editing;
        if (_drawer != null) _drawer.IsVisible = _editing && _libOpen;
    }

    private TopLevel? _keyHost;

    private void AttachEditKeys(bool on)
    {
        if (on)
        {
            _keyHost = TopLevel.GetTopLevel(this);
            _keyHost?.AddHandler(KeyDownEvent, OnEditKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        else if (_keyHost != null)
        {
            _keyHost.RemoveHandler(KeyDownEvent, OnEditKey);
            _keyHost = null;
        }
    }

    private void OnEditKey(object? sender, KeyEventArgs e)
    {
        if (!_editing)
            return;
        if (e.Source is TextBox)
            return; // 输入框里(导入布局码)不抢快捷键
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (ctrl && e.Key == Key.Z && !shift)
        {
            ZoneUndo();
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift)))
        {
            ZoneRedo();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_moreMenu?.IsOpen == true)
                _moreMenu.IsOpen = false;
            else if (_libOpen)
                CloseLib();
            else
                ExitZoneEdit();
            e.Handled = true;
        }
    }

    internal void ZoneUndo()
    {
        if (!_zone.Undo())
        {
            Toast(I18nCore.Tr("没有可撤销的操作"));
            return;
        }
        LayoutCards();
        RenderLib();
        Toast(I18nCore.Tr("已撤销"));
    }

    internal void ZoneRedo()
    {
        if (!_zone.Redo())
        {
            Toast(I18nCore.Tr("没有可重做的操作"));
            return;
        }
        LayoutCards();
        RenderLib();
        Toast(I18nCore.Tr("已重做"));
    }

    private void CycleSize(string id)
    {
        switch (_zone.CycleSize(id, out string next))
        {
            case ZoneEditor.SizeResult.Changed:
                LayoutCards();
                break;
            case ZoneEditor.SizeResult.OnlyOne:
                Toast(I18nCore.Tr("窄窗口下这个组件只有一种尺寸"));
                break;
            case ZoneEditor.SizeResult.NoRoom:
                Toast(string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("这里放不下 {0}，先挪开或移除别的组件"), LaunchZone.DimLabel(LaunchZone.EffSize(next, _cols))));
                break;
        }
    }

    private void RemoveCard(string id)
    {
        var it = _zone.Items.FirstOrDefault(i => i.Id == id);
        if (it == null || !_zone.Remove(id))
            return;
        LayoutCards();
        RenderLib();
        Toast(string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("已移除「{0}」 · Ctrl+Z 撤销"), W.Name(it.Type)));
    }

    private void AddType(string type)
    {
        var r = _zone.Add(type, out var added);
        switch (r)
        {
            case ZoneEditor.AddResult.Added:
                LayoutCards();
                RenderLib();
                if (added != null && _cards.TryGetValue(added.Id, out var card))
                    Flash(card);
                Toast(string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("已添加「{0}」"), W.Name(type)));
                break;
            case ZoneEditor.AddResult.Full:
                Toast(I18nCore.Tr("组件区放满了：先移除一个，或把大组件换成小尺寸"));
                break;
        }
    }

    private void Flash(ZoneCard card)
    {
        var c = AccentColor();
        card.Shell.BoxShadow = new BoxShadows(new BoxShadow { Spread = 2, Color = c }, new[] { new BoxShadow { Blur = 30, OffsetY = 10, Color = Color.FromArgb(107, c.R, c.G, c.B) } });
        DispatcherTimer.RunOnce(() => card.Shell.BoxShadow = default, TimeSpan.FromMilliseconds(900));
    }

    // —————————————— 拖动 ——————————————

    private void OnCardPressed(ZoneCard card, PointerPressedEventArgs e)
    {
        if (!_editing || !e.GetCurrentPoint(card.Root).Properties.IsLeftButtonPressed)
            return;
        var p = e.GetPosition(_zoneCanvas);
        _drag = new DragState
        {
            Id = card.Item.Id,
            Card = card,
            Start = p,
            Offset = new Point(p.X - Canvas.GetLeft(card.Root), p.Y - Canvas.GetTop(card.Root)),
            Pointer = e.Pointer,
        };
        e.Pointer.Capture(card.Root);
        e.Handled = true;
    }

    private void OnCardMoved(ZoneCard card, PointerEventArgs e)
    {
        var d = _drag;
        if (d == null || d.Card != card)
            return;
        var p = e.GetPosition(_zoneCanvas);
        if (!d.Started)
        {
            if (Math.Abs(p.X - d.Start.X) + Math.Abs(p.Y - d.Start.Y) < 4)
                return;
            d.Started = true;
            card.Root.Transitions = null;
            card.Root.ZIndex = 20;
            card.Root.RenderTransformOrigin = RelativePoint.Center;
            card.Root.RenderTransform = new ScaleTransform(1.03, 1.03);
            card.Shell.BoxShadow = BoxShadows.Parse("0 18 40 0 #73000000");
            card.EditLine.Stroke = Accent();
            card.EditLine.StrokeDashArray = null;
            _ph.Fill = AccentA(61);
            _ph.Stroke = Accent();
            _ph.IsVisible = true;
            foreach (var other in _cards.Values)
                if (other != card && Motion.Enabled)
                    other.Root.Transitions ??= CardTransitions();
        }
        double left = p.X - d.Offset.X, top = p.Y - d.Offset.Y;
        Canvas.SetLeft(card.Root, left);
        Canvas.SetTop(card.Root, top);
        double cx = left + card.Root.Width / 2, cy = top + card.Root.Height / 2;
        var best = LaunchZone.DragOrder(_zone.Items, d.Id, cx, cy, _cols, _rows);
        if (best == null)
            return;
        d.Order = best.Value.Order;
        LayoutCards(d.Order, skip: d.Id);
        var r = best.Value.Spot;
        Canvas.SetLeft(_ph, LaunchZone.OffsetX(r.X));
        Canvas.SetTop(_ph, LaunchZone.OffsetY(r.Y));
        _ph.Width = LaunchZone.Span(r.W, LaunchZone.ColW);
        _ph.Height = LaunchZone.Span(r.H, LaunchZone.RowH);
        e.Handled = true;
    }

    private void OnCardReleased(ZoneCard card, PointerReleasedEventArgs e)
    {
        if (_drag == null || _drag.Card != card)
            return;
        e.Pointer.Capture(null);
        EndDrag(commit: true);
        e.Handled = true;
    }

    private void EndDrag(bool commit)
    {
        var d = _drag;
        _drag = null;
        if (d == null)
            return;
        var card = d.Card;
        card.Root.ZIndex = 0;
        card.Root.RenderTransform = null;
        card.Shell.BoxShadow = default;
        card.EditLine.Stroke = W.W(128);
        card.EditLine.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 };
        _ph.IsVisible = false;
        if (Motion.Enabled)
            card.Root.Transitions = CardTransitions();
        if (commit && d.Started && d.Order != null)
            _zone.Replace(d.Order); // 顺序没变则不记撤销
        LayoutCards();
    }

    // —————————————— 组件库 ——————————————

    private void OpenLib()
    {
        if (!_editing)
            EnterZoneEdit();
        _libOpen = true;
        RenderLib();
        SetEditChrome();
    }

    private void CloseLib()
    {
        CloseLibCore();
        SetEditChrome();
    }

    private void CloseLibCore() => _libOpen = false;

    private void RenderLib()
    {
        if (_libList == null || !_libOpen)
            return;
        _libList.Children.Clear();
        foreach (string type in LaunchZone.Types)
        {
            bool used = !LaunchZone.IsMulti(type) && _zone.Items.Any(i => i.Type == type);
            var sizes = LaunchZone.SizesOf(type);
            string s0 = sizes.Contains(LaunchZone.Small) ? LaunchZone.Small : sizes[0];
            var (cw, chh) = LaunchZone.Dim(s0);
            double w = LaunchZone.Span(cw, LaunchZone.ColW), h = LaunchZone.Span(chh, LaunchZone.RowH);
            var sample = new ZoneItem { Id = "lib_" + type, Type = type, Size = s0 };
            if (type == LaunchZone.TNote)
                sample.Settings["text"] = I18nCore.Tr("周六晚上和朋友联机，记得先装好光影包");
            var preview = W.Glass(W.Content(sample, s0, _zdata, null, Accent));
            preview.Width = w;
            preview.Height = h;
            preview.IsHitTestVisible = false;
            var thumb = new Border
            {
                Width = 112,
                Height = 70,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(255, 27, 34, 48)),
                Child = new Panel
                {
                    Children =
                    {
                        new Image { Source = _bgImage.Source, Stretch = Stretch.UniformToFill },
                        new Border { Background = new SolidColorBrush(Color.FromArgb(115, 10, 14, 22)) },
                        new Viewbox { Width = 104, Height = 62, Stretch = Stretch.Uniform, Child = preview },
                    },
                },
            };
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = W.Name(type), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Ink() });
            info.Children.Add(new TextBlock { Text = W.Desc(type), FontSize = 12, Foreground = Ink2(), TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(new TextBlock { Text = string.Join(" · ", sizes.Select(LaunchZone.DimLabel)), FontSize = 12, Foreground = Ink3(), FontFamily = W.Mono });
            var addBtn = new Border
            {
                Name = "ZoneLibAdd_" + type,
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = used ? I18nCore.Tr("已添加") : I18nCore.Tr("添加"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
            };
            var addTxt = (TextBlock)addBtn.Child;
            if (used)
            {
                addBtn.Background = Brushes.Transparent;
                addBtn.BorderBrush = Brushes.Transparent;
                addTxt.Foreground = Ink3();
            }
            else
            {
                void Paint(bool hover)
                {
                    addBtn.Background = hover ? Accent() : AccentA(31);
                    addBtn.BorderBrush = hover ? Accent() : AccentA(82);
                    addTxt.Foreground = hover ? Brushes.White : Accent();
                }
                addTxt.FontWeight = FontWeight.Bold;
                Paint(false);
                addBtn.Cursor = new Cursor(StandardCursorType.Hand);
                addBtn.PointerEntered += (_, _) => Paint(true);
                addBtn.PointerExited += (_, _) => Paint(false);
                string t = type;
                addBtn.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton != MouseButton.Left)
                        return;
                    AddType(t);
                    e.Handled = true;
                };
            }
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*,Auto") };
            g.Children.Add(thumb);
            info.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            Grid.SetColumn(addBtn, 2);
            g.Children.Add(addBtn);
            _libList.Children.Add(new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Background = Paper2(), Child = g });
        }
    }

    // —————————————— 导入 / 导出 / 布局码 ——————————————

    private const string CodePrefix = "TIPCL-ZONE:";

    /// <summary>当前组件区的布局码(朋友粘贴到「导入布局」即可)。</summary>
    internal string ZoneCode()
    {
        var page = new PageLayoutV2();
        LaunchZone.Write(page, _zone.Items);
        string json = JsonSerializer.Serialize(page.Extra[LaunchZone.ExtraKey]);
        return CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// 解析导入内容:布局码 / 只含 zone 的 JSON / 完整 .tipcl-layout.json(取 pages.launch.zone)。
    /// 认不出时返回 null 并给出原因。
    /// </summary>
    internal static List<ZoneItem>? ParseZoneImport(string text, out string error)
    {
        error = "";
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            error = I18nCore.Tr("内容是空的");
            return null;
        }
        try
        {
            string json = text;
            if (text.StartsWith(CodePrefix, StringComparison.Ordinal))
                json = Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(CodePrefix.Length).Trim()));
            using var jd = JsonDocument.Parse(json);
            var root = jd.RootElement;
            JsonElement zone;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out _))
                zone = root;
            else
            {
                var doc = LayoutV2.FromJson(root);
                if (!doc.Layout.Pages.TryGetValue(LaunchZone.PageId, out var p) || !LaunchZone.HasZone(p))
                {
                    error = I18nCore.Tr("这份布局里没有启动页组件");
                    return null;
                }
                return LaunchZone.Read(p);
            }
            var page = new PageLayoutV2();
            page.Extra[LaunchZone.ExtraKey] = LaunchZone.PlainFromJson(zone);
            return LaunchZone.Read(page);
        }
        catch (Exception ex)
        {
            error = I18nCore.Tr("认不出这段内容") + ":" + ex.Message;
            return null;
        }
    }

    private Popup? _importPopup;

    private TextBox? _importBox;

    private void OpenImport()
    {
        if (_importPopup == null)
        {
            _importBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 96,
                Watermark = I18nCore.Tr("粘贴布局码，或从文件导入"),
                FontSize = 12,
            };
            var fromFile = BarBtn(I18nCore.Tr("从文件…"), BarKind.Plain, () => _ = ImportFromFileAsync());
            var ok = BarBtn(I18nCore.Tr("导入"), BarKind.Primary, () => ApplyImport(_importBox.Text ?? ""));
            var cancel = BarBtn(I18nCore.Tr("取消"), BarKind.Plain, () => _importPopup!.IsOpen = false);
            var shell = new Border
            {
                Width = 320,
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(12),
                Background = Brushes.White,
                BoxShadow = BoxShadows.Parse("0 12 32 0 #59000000"),
                Margin = new Thickness(0, 8, 12, 12),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = I18nCore.Tr("导入布局"), FontSize = 15, FontWeight = FontWeight.ExtraBold, Foreground = Ink() },
                        _importBox,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { fromFile, cancel, ok } },
                    },
                },
            };
            _importPopup = new Popup { PlacementTarget = _ebox, Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true, Child = shell };
            if (Content is Panel host)
                host.Children.Add(_importPopup);
        }
        _importBox!.Text = "";
        _importPopup.IsOpen = true;
    }

    private void ApplyImport(string text)
    {
        var items = ParseZoneImport(text, out string err);
        if (items == null)
        {
            Toast(err);
            return;
        }
        if (_importPopup != null)
            _importPopup.IsOpen = false;
        _zone.Replace(items);
        LayoutCards();
        RenderLib();
        Toast(I18nCore.Tr("已导入 · 点「完成」保存，Ctrl+Z 撤销"));
    }

    private async Task ImportFromFileAsync()
    {
        try
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp == null)
                return;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = I18nCore.Tr("导入布局"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(I18nCore.Tr("布局文件")) { Patterns = new[] { "*.json" } } },
            });
            if (files.Count == 0)
                return;
            await using var s = await files[0].OpenReadAsync();
            using var rd = new StreamReader(s);
            ApplyImport(await rd.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            Toast(I18nCore.Tr("导入失败") + ":" + ex.Message);
        }
    }

    private async Task ExportZoneAsync()
    {
        try
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp == null)
                return;
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = I18nCore.Tr("导出布局"),
                SuggestedFileName = "launch.tipcl-layout.json",
                DefaultExtension = "json",
            });
            if (file == null)
                return;
            // 导出完整布局文件(含当前、尚未保存的组件区),别人用「设置 → 布局 → 导入」或这里的「导入布局」都认
            var doc = LayoutStoreV2.Load().Clone();
            if (!doc.Layout.Pages.TryGetValue(LaunchZone.PageId, out var page) || page == null)
            {
                page = new PageLayoutV2();
                doc.Layout.Pages[LaunchZone.PageId] = page;
            }
            LaunchZone.Write(page, _zone.Items);
            await using var s = await file.OpenWriteAsync();
            await using var w = new StreamWriter(s, new UTF8Encoding(false));
            await w.WriteAsync(LayoutStoreV2.ExportJson(doc));
            Toast(I18nCore.Tr("已导出布局"));
        }
        catch (Exception ex)
        {
            Toast(I18nCore.Tr("导出失败") + ":" + ex.Message);
        }
    }

    private async Task CopyZoneCodeAsync()
    {
        try
        {
            var cb = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cb == null)
                return;
            await cb.SetTextAsync(ZoneCode());
            Toast(I18nCore.Tr("已复制布局码，朋友粘贴到「导入布局」即可"));
        }
        catch (Exception ex)
        {
            Toast(I18nCore.Tr("复制失败") + ":" + ex.Message);
        }
    }

    // —————————————— 提示条 ——————————————

    private void Toast(string msg)
    {
        if (_toastBox == null || _toastText == null)
            return;
        _toastText.Text = msg;
        _toastBox.IsVisible = true;
        _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _toastTimer.Stop();
        _toastTimer.Tick -= HideToast;
        _toastTimer.Tick += HideToast;
        _toastTimer.Start();
    }

    private void HideToast(object? s, EventArgs e)
    {
        _toastTimer?.Stop();
        if (_toastBox != null)
            _toastBox.IsVisible = false;
    }

    /// <summary>巡检 / 测试读最近一条提示。</summary>
    internal string LastToast => _toastText?.Text ?? "";

    // —————————————— 数据 ——————————————

    /// <summary>装组件区数据(游戏时长 / 服务器 / 截图),装好后重画。不阻塞界面。</summary>
    private async Task LoadZoneDataAsync()
    {
        string inst = CurInst(), ver = CurVer();
        var weekTask = Task.Run(() =>
        {
            try { return LaunchWidgetData.Week(Playtime.GetAllPlaytime(), DateTime.Now); }
            catch (Exception) { return null; }
        });
        var shotsTask = Task.Run(() => LoadShots(inst, ver));
        _zdata.Week = await weekTask;
        _zdata.WeekLoaded = true;
        var (shots, count) = await shotsTask;
        _zdata.Shots = shots;
        _zdata.ShotCount = count;
        _zdata.ShotsLoaded = true;
        _zdataGen++;
        LayoutCards();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            _zdata.Server = await ServerStatusHub.Source.FetchAsync(inst, cts.Token);
        }
        catch (Exception ex)
        {
            _zdata.Server = new ServerStatus { NoServer = true, Error = ex.Message };
        }
        _zdata.ServerLoaded = true;
        _zdataGen++;
        LayoutCards();
    }

    private static (List<Bitmap>, int) LoadShots(string inst, string ver)
    {
        var list = new List<Bitmap>();
        int count = 0;
        try
        {
            if (inst.Length == 0)
                return (list, 0);
            string dir = IoPathJoin(GameDirOf(inst, ver), "screenshots");
            if (!Directory.Exists(dir))
                return (list, 0);
            var files = new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            count = files.Count;
            foreach (var f in files.Take(6))
            {
                try
                {
                    using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    list.Add(Bitmap.DecodeToWidth(fs, 240));
                }
                catch (Exception)
                {
                    // 坏图跳过
                }
            }
        }
        catch (Exception)
        {
        }
        return (list, count);
    }

    private static string IoPathJoin(string a, string b) => System.IO.Path.Combine(a, b);

    /// <summary>实例 + 版本的游戏目录(按版本隔离设置;与实例详情 / 实例库同一来源)。</summary>
    internal static string GameDirOf(string inst, string ver)
    {
        try
        {
            var i = new Instance(inst);
            return ver.Length > 0 ? VersionSettings.GameDir(i, ver) : i.path;
        }
        catch (Exception)
        {
            return "";
        }
    }

    // —————————————— IZoneActions ——————————————

    public void OpenFolder(string kind)
    {
        string inst = CurInst(), ver = CurVer();
        if (inst.Length == 0)
        {
            Toast(I18nCore.Tr("还没有选择实例"));
            return;
        }
        string dir = kind == "mods" && ver.Length > 0
            ? SafeModsDir(inst, ver)
            : IoPathJoin(GameDirOf(inst, ver), kind);
        try
        {
            Directory.CreateDirectory(dir);
            ShellOpen.Folder(dir);
        }
        catch (Exception ex)
        {
            Toast(I18nCore.Tr("打不开文件夹") + ":" + ex.Message);
        }
    }

    private static string SafeModsDir(string inst, string ver)
    {
        try { return VersionSettings.ModsDir(new Instance(inst), ver); }
        catch (Exception) { return IoPathJoin(GameDirOf(inst, ver), "mods"); }
    }

    public void JoinServer(ServerStatus server)
    {
        _server.Text = server.Address;
        Config.CONFIG.Set("direct_server", server.Address);
        _ = LaunchAsync();
    }

    public void OpenServers()
    {
        if (TopLevel.GetTopLevel(this) is MainWindow win)
            win.Navigate("multiplayer");
    }

    public void OpenShots() => OpenFolder("screenshots");

    public void BeginEditNote(ZoneItem item, Panel host)
    {
        if (_editing)
            return;
        var box = new TextBox
        {
            Text = item.Text("text"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            CaretBrush = Brushes.White,
            Watermark = I18nCore.Tr("记一句话"),
        };
        host.Children.Clear();
        host.Children.Add(box);
        box.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { box.Focus(); box.CaretIndex = box.Text?.Length ?? 0; });
        bool done = false;
        void Commit()
        {
            if (done)
                return;
            done = true;
            string text = (box.Text ?? "").TrimEnd();
            if (_zone.SetSetting(item.Id, "text", text))
            {
                _zone.SetSetting(item.Id, "updated", DateTimeOffset.Now.ToUnixTimeSeconds());
                SaveZone();
            }
            if (_cards.TryGetValue(item.Id, out var card))
                card.Key = "";
            LayoutCards();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0))
            {
                Commit();
                e.Handled = true;
            }
        };
    }
}
