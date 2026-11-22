// AI4-launch:启动页 v3 右侧组件区里每种组件的内容渲染(launch_v3.html 的 content() 一一对应)。
// 组件永远画在背景图上:白字 + 深色半透明玻璃卡(rgba(14,18,27,.48) / 边 rgba(255,255,255,.14) / 圆角 12 / 内距 12,14)。
// 字号只用 12 / 13 / 15 / 21 / 34;圆角只用 8 / 12(launch_v3 第 7 条)。
// 数据由 LaunchPage 准备好(ZoneData),这里只负责把数据画成控件;没有数据时画「读取中 / 还没有记录」占位,不抛。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher.Layout;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace TipCL.App.Pages.Launch;

/// <summary>组件区要用到的数据快照(页面异步装好后整体换掉,渲染只读)。</summary>
internal sealed class ZoneData
{
    public WeekPlaytime? Week;

    public bool WeekLoaded;

    public ServerStatus? Server;

    public bool ServerLoaded;

    /// <summary>最近截图(新 → 旧),最多 6 张已解码缩略图。</summary>
    public List<Bitmap> Shots = new();

    public int ShotCount;

    public bool ShotsLoaded;
}

/// <summary>组件里可点的动作(由 LaunchPage 实现;编辑态下组件内容不接收点击)。</summary>
internal interface IZoneActions
{
    void OpenFolder(string kind);

    void JoinServer(ServerStatus server);

    void OpenServers();

    void BeginEditNote(ZoneItem item, Panel host);

    void OpenShots();
}

internal static class LaunchWidgets
{
    // —— 颜色 ——
    public static IBrush W(byte a) => new SolidColorBrush(Color.FromArgb(a, 255, 255, 255));

    public static readonly IBrush GlassBg = new SolidColorBrush(Color.FromArgb(122, 14, 18, 27));

    public static readonly IBrush GlassLine = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));

    public static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#4ADE80"));

    public static readonly FontFamily Mono = new("Consolas, Cascadia Mono, DejaVu Sans Mono, monospace");

    // —— 矢量图标(launch_v3 内联 SVG 原样转成 Path Data,16 视窗,线宽 1.6 圆头) ——
    public const string IcoMods = "M8,1.8 L13.5,4.8 V11.2 L8,14.2 L2.5,11.2 V4.8 Z M2.5,4.8 L8,7.8 L13.5,4.8 M8,7.8 V14.2";
    public const string IcoCam = "M2,5.5 H4.5 L6,3.5 H10 L11.5,5.5 H14 V12.5 H2 Z M10.3,9 A2.3,2.3 0 1 1 5.7,9 A2.3,2.3 0 1 1 10.3,9 Z";
    public const string IcoLog = "M4,1.8 H9.5 L12.5,5 V14.2 H4 Z M9.5,1.8 V5 H12.5 M6,8 H10.5 M6,10.5 H10.5";
    public const string IcoSave = "M1.8,4.2 H6.2 L7.7,5.8 H14.2 V13.2 H1.8 Z";
    public const string IcoLock = "M5,7 H11 A1.5,1.5 0 0 1 12.5,8.5 V12 A1.5,1.5 0 0 1 11,13.5 H5 A1.5,1.5 0 0 1 3.5,12 V8.5 A1.5,1.5 0 0 1 5,7 Z M5.5,7 V5 A2.5,2.5 0 0 1 10.5,5 V7";
    public const string IcoAi = "M8,2 V14 M2.8,5 L13.2,11 M2.8,11 L13.2,5";
    public const string IcoPencil = "M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z";
    public const string IcoChevron = "M4.5,6.5 L8,10 L11.5,6.5";
    public const string IcoPlay = "M5,3.5 V12.5 L12,8 Z";
    public const string IcoSwap = "M2.5,5.5 H12.5 M10,3 L12.5,5.5 L10,8 M13.5,10.5 H3.5 M6,8 L3.5,10.5 L6,13";
    public const string IcoWifi = "M2,6.5 A9,9 0 0 1 14,6.5 M4.2,9 A5.6,5.6 0 0 1 11.8,9 M6.4,11.4 A2.3,2.3 0 0 1 9.6,11.4 M8,13.4 L8.01,13.4";
    public const string IcoArrow = "M6,4 L10,8 L6,12";
    public const string IcoX = "M4,4 L12,12 M12,4 L4,12";
    public const string IcoPlus = "M8,3 V13 M3,8 H13";
    public const string IcoMore = "M2.8,8 A0.7,0.7 0 1 1 4.2,8 A0.7,0.7 0 1 1 2.8,8 Z M7.3,8 A0.7,0.7 0 1 1 8.7,8 A0.7,0.7 0 1 1 7.3,8 Z M11.8,8 A0.7,0.7 0 1 1 13.2,8 A0.7,0.7 0 1 1 11.8,8 Z";

    public static ShapePath Ico(string data, double size, IBrush stroke, double thickness = 1.6, IBrush? fill = null)
    {
        // 16×16 视窗整体按 size/16 缩放(Stretch.None):不能用 Stretch.Uniform,那会按几何外框拉伸、图标变大变形
        var geo = StreamGeometry.Parse(data);
        double k = size / 16.0;
        geo.Transform = new ScaleTransform(k, k);
        return new ShapePath
        {
            Data = geo,
            Stroke = stroke,
            StrokeThickness = thickness * k,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = fill,
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    // —— 文字 ——
    public static TextBlock T(string text, double size, byte alpha = 255, FontWeight? weight = null, bool wrap = false)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = W(alpha),
            FontWeight = weight ?? FontWeight.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            LineHeight = double.NaN,
        };
    }

    /// <summary>.lb:12 号 / 白 .68,可带绿点。</summary>
    public static Control Label(string text, IBrush? dot = null)
    {
        var t = T(text, 12, 174);
        if (dot == null)
            return t;
        var d = new Ellipse { Width = 7, Height = 7, Fill = dot, VerticalAlignment = VerticalAlignment.Center };
        if (ReferenceEquals(dot, Ok))
            d.Effect = new DropShadowEffect { BlurRadius = 8, OffsetX = 0, OffsetY = 0, Color = Color.Parse("#4ADE80"), Opacity = 1 };
        t.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { d, t } };
    }

    /// <summary>.big:21(或 34)号 ExtraBold,后接 13 号白 .7 的小字。</summary>
    public static TextBlock Big(string big, string small, bool xl = false)
    {
        var tb = new TextBlock { Foreground = W(255), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        tb.Inlines = new InlineCollection
        {
            new Run(big) { FontSize = xl ? 34 : 21, FontWeight = FontWeight.ExtraBold },
        };
        if (small.Length > 0)
            tb.Inlines.Add(new Run(small) { FontSize = 13, FontWeight = FontWeight.Normal, Foreground = W(179) });
        return tb;
    }

    public static TextBlock Sub(string text, bool wrap = false) => T(text, 12, 158, wrap: wrap);

    /// <summary>玻璃卡外壳。</summary>
    public static Border Glass(Control child)
    {
        return new Border
        {
            Background = GlassBg,
            BorderBrush = GlassLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            ClipToBounds = true,
            Child = child,
        };
    }

    // —————————————— 各组件 ——————————————

    public static Control Content(ZoneItem it, string es, ZoneData data, IZoneActions? act, Func<IBrush> accent)
    {
        try
        {
            return it.Type switch
            {
                LaunchZone.TPlaytime => Playtime(es, data, accent),
                LaunchZone.TServer => Server(es, data, act, accent),
                LaunchZone.TShortcuts => Shortcuts(es, act),
                LaunchZone.TNote => Note(it, es, act),
                LaunchZone.TClock => Clock(es),
                LaunchZone.TScreenshots => Shots(es, data, act),
                _ => Sub(it.Type),
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn("启动页组件渲染失败 " + it.Type + ": " + ex.Message);
            return Sub(I18nCore.Tr("这个组件暂时显示不了"), wrap: true);
        }
    }

    // —— 游戏时长 ——
    private static Control Playtime(string es, ZoneData data, Func<IBrush> accent)
    {
        var w = data.Week;
        string lb = I18nCore.Tr("本周游戏时长");
        string hours = w == null ? "–" : LaunchWidgetData.Hours1(w.TotalHours);
        string unit = " " + I18nCore.Tr("小时");
        string diff = DiffText(w, data.WeekLoaded);
        if (es == LaunchZone.Small)
        {
            return new StackPanel
            {
                Spacing = 2,
                Children = { Label(lb), Big(hours, unit + (diff.Length > 0 ? " · " + diff : "")) },
            };
        }
        if (es == LaunchZone.Medium)
        {
            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
            g.Children.Add(Label(lb));
            var big = Big(hours, unit);
            Grid.SetRow(big, 1);
            g.Children.Add(big);
            var sub = Sub(diff);
            Grid.SetRow(sub, 2);
            g.Children.Add(sub);
            var bars = Bars(w, 62, accent);
            Grid.SetRow(bars, 3);
            bars.VerticalAlignment = VerticalAlignment.Bottom;
            g.Children.Add(bars);
            return g;
        }
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Label(lb));
        left.Children.Add(Big(hours, unit, xl: true));
        if (diff.Length > 0)
            left.Children.Add(Sub(diff));
        if (w != null && w.TopInstance.Length > 0)
            left.Children.Add(Sub(I18nCore.Tr("玩得最多：") + w.TopInstance));
        else if (data.WeekLoaded && (w == null || !w.AnyRecord))
            left.Children.Add(Sub(I18nCore.Tr("还没有游戏时长记录")));
        left.MaxWidth = 150;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(left);
        var b = Bars(w, 118, accent);
        b.Margin = new Thickness(16, 0, 0, 0);
        b.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(b, 1);
        row.Children.Add(b);
        return row;
    }

    private static string DiffText(WeekPlaytime? w, bool loaded)
    {
        if (w == null)
            return loaded ? "" : I18nCore.Tr("读取中…");
        double d = Math.Round(w.TotalHours - w.LastWeekHours, 1, MidpointRounding.AwayFromZero);
        if (d > 0)
            return string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("比上周多 {0} 小时"), LaunchWidgetData.Hours1(d));
        if (d < 0)
            return string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("比上周少 {0} 小时"), LaunchWidgetData.Hours1(-d));
        return I18nCore.Tr("和上周持平");
    }

    private static readonly string[] DayKeys = { "一", "二", "三", "四", "五", "六", "日" };

    private static Control Bars(WeekPlaytime? w, double h, Func<IBrush> accent)
    {
        double max = 0.5;
        if (w != null)
            max = Math.Max(max, w.Hours.Max());
        var ug = new UniformGrid { Columns = 7, Rows = 1, Margin = new Thickness(-3, 0, -3, 0) };
        for (int i = 0; i < 7; i++)
        {
            bool future = w != null && i > w.Today;
            bool today = w != null && i == w.Today;
            double v = w?.Hours[i] ?? 0;
            Control bar;
            if (future)
            {
                bar = new Rectangle
                {
                    Height = Math.Round(h * 0.28),
                    RadiusX = 4,
                    RadiusY = 4,
                    Stroke = W(77),
                    StrokeThickness = 1,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 },
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
            }
            else
            {
                var bd = new Border
                {
                    Height = Math.Max(h * 0.04, h * Math.Min(1, v / max)),
                    CornerRadius = new CornerRadius(4),
                    Background = today ? accent() : W(128),
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                bar = bd;
            }
            var track = new Panel { Height = h, Children = { bar } };
            var lbl = T(I18nCore.Tr(DayKeys[i]), 12, 140);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.LineHeight = 12;
            var col = new StackPanel { Spacing = 4, Margin = new Thickness(3, 0, 3, 0), Children = { track, lbl } };
            ToolTip.SetTip(col, LaunchWidgetData.Hours1(v) + " " + I18nCore.Tr("小时"));
            ug.Children.Add(col);
        }
        return ug;
    }

    // —— 服务器状态 ——
    private static Control Server(string es, ZoneData data, IZoneActions? act, Func<IBrush> accent)
    {
        var s = data.Server;
        if (s == null)
        {
            return new StackPanel
            {
                Spacing = 2,
                Children = { Label(I18nCore.Tr("服务器状态")), T(data.ServerLoaded ? "" : I18nCore.Tr("读取中…"), 15, 255, FontWeight.Bold, wrap: true) },
            };
        }
        if (s.NoServer)
        {
            var p = new StackPanel { Spacing = 2 };
            p.Children.Add(Label(I18nCore.Tr("服务器状态")));
            p.Children.Add(T(I18nCore.Tr("还没有添加服务器"), 15, 255, FontWeight.Bold, wrap: true));
            p.Children.Add(Sub(I18nCore.Tr("在「联机」里添加后显示在这里"), wrap: true));
            return Clickable(p, () => act?.OpenServers());
        }
        IBrush dot = s.Ok ? Ok : W(110);
        string name = s.Name.Length > 0 ? s.Name : s.Address;
        string line = s.Ok
            ? string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("{0} / {1} 在线 · {2} ms"), s.Online, s.Max, s.LatencyMs)
            : I18nCore.Tr("连不上");
        if (es == LaunchZone.Small)
        {
            var p = new StackPanel { Spacing = 2 };
            p.Children.Add(Label(name, dot));
            var mid = T(line, 15, 255, FontWeight.Bold, wrap: true);
            mid.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(mid);
            ToolTip.SetTip(p, s.Address + (s.Ok ? "" : (s.Error.Length > 0 ? "\n" + s.Error : "")));
            return Clickable(p, () => act?.OpenServers());
        }
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*") };
        g.Children.Add(Label(name, dot));
        TextBlock big = s.Ok
            ? Big(s.Online.ToString(CultureInfo.InvariantCulture), string.Format(CultureInfo.InvariantCulture, I18nCore.Tr(" / {0} 在线"), s.Max))
            : Big(I18nCore.Tr("离线"), "");
        Grid.SetRow(big, 1);
        g.Children.Add(big);
        var mono = T(s.Address + (s.Ok ? " · " + s.LatencyMs.ToString(CultureInfo.InvariantCulture) + " ms" : ""), 12, 158);
        mono.FontFamily = Mono;
        Grid.SetRow(mono, 2);
        g.Children.Add(mono);
        if (s.Players.Count > 0)
        {
            var heads = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var pl in s.Players.Take(5))
            {
                heads.Children.Add(new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = W(51),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(230, 20, 24, 34)),
                    BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(0, 0, -6, 0),
                    Child = new TextBlock { Text = pl.Substring(0, 1).ToUpperInvariant(), FontSize = 12, Foreground = W(255), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
            }
            long more = Math.Max(0, s.Online - Math.Min(5, s.Players.Count));
            if (more > 0)
            {
                var em = T("+" + more.ToString(CultureInfo.InvariantCulture), 12, 153);
                em.Margin = new Thickness(12, 0, 0, 0);
                em.VerticalAlignment = VerticalAlignment.Center;
                heads.Children.Add(em);
            }
            Grid.SetRow(heads, 3);
            g.Children.Add(heads);
        }
        var join = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = accent(),
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = I18nCore.Tr("加入服务器"), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = W(255), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        join.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            act?.JoinServer(s);
            e.Handled = true;
        };
        Grid.SetRow(join, 4);
        g.Children.Add(join);
        return g;
    }

    // —— 快捷入口 ——
    public static readonly (string Kind, string Icon, string Label)[] Tiles =
    {
        ("mods", IcoMods, "模组"),
        ("screenshots", IcoCam, "截图"),
        ("logs", IcoLog, "日志"),
        ("saves", IcoSave, "存档"),
    };

    private static Control Shortcuts(string es, IZoneActions? act)
    {
        int n = es == LaunchZone.Small ? 3 : 4;
        var ug = new UniformGrid
        {
            Columns = es == LaunchZone.Small ? 3 : es == LaunchZone.Medium ? 2 : 4,
            Margin = new Thickness(-3),
        };
        foreach (var (kind, icon, label) in Tiles.Take(n))
        {
            var tile = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = W(20),
                Margin = new Thickness(3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { Ico(icon, 18, W(255)), CenterText(I18nCore.Tr(label), 12) },
                },
            };
            ToolTip.SetTip(tile, string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("打开{0}文件夹"), I18nCore.Tr(label)));
            tile.PointerEntered += (_, _) => tile.Background = W(41);
            tile.PointerExited += (_, _) => tile.Background = W(20);
            string k = kind;
            tile.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left)
                    return;
                act?.OpenFolder(k);
                e.Handled = true;
            };
            ug.Children.Add(tile);
        }
        return ug;
    }

    private static TextBlock CenterText(string s, double size)
    {
        var t = T(s, size);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    // —— 便签 ——
    private static Control Note(ZoneItem it, string es, IZoneActions? act)
    {
        string text = it.Text("text");
        string head = I18nCore.Tr("便签");
        if (es != LaunchZone.Small && it.Settings.TryGetValue("updated", out var u) && u is long ts && ts > 0)
            head += " · " + When(ts);
        var host = new Panel();
        var p = new StackPanel { Spacing = 2 };
        p.Children.Add(Label(head));
        var nt = text.Length == 0
            ? T(I18nCore.Tr("点这里记一句话"), 13, 120, wrap: es != LaunchZone.Small)
            : T(text, 13, 255, wrap: es != LaunchZone.Small);
        nt.Margin = new Thickness(0, 2, 0, 0);
        nt.LineHeight = 19.5;
        if (es != LaunchZone.Small)
            nt.MaxHeight = 19.5 * 7;
        p.Children.Add(nt);
        host.Children.Add(p);
        host.Background = Brushes.Transparent;
        host.Cursor = new Cursor(StandardCursorType.Ibeam);
        host.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            act?.BeginEditNote(it, host);
            e.Handled = true;
        };
        return host;
    }

    private static string When(long epoch)
    {
        var dt = DateTime.UnixEpoch.AddSeconds(epoch).ToLocalTime();
        var now = DateTime.Now;
        string hm = dt.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (dt.Date == now.Date)
            return I18nCore.Tr("今天") + " " + hm;
        if (dt.Date == now.Date.AddDays(-1))
            return I18nCore.Tr("昨天") + " " + hm;
        return string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("{0}月{1}日"), dt.Month, dt.Day);
    }

    // —— 时钟 ——
    private static readonly string[] WeekKeys = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    public static (string Time, string Date) ClockText(DateTime now)
    {
        string t = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        string d = string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("{0}月{1}日"), now.Month, now.Day) + " " + I18nCore.Tr(WeekKeys[(int)now.DayOfWeek]);
        return (t, d);
    }

    private static Control Clock(string es)
    {
        var (t, d) = ClockText(DateTime.Now);
        var big = new TextBlock { Text = t, FontSize = es == LaunchZone.Small ? 21 : 34, FontWeight = FontWeight.ExtraBold, Foreground = W(255), Tag = "clock-time" };
        var sub = Sub(d);
        sub.Tag = "clock-date";
        if (es == LaunchZone.Small)
            return new StackPanel { Spacing = 2, Children = { big, sub } };
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        g.Children.Add(Label(I18nCore.Tr("本地时间")));
        var mid = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { big, sub } };
        Grid.SetRow(mid, 1);
        g.Children.Add(mid);
        return g;
    }

    /// <summary>每 20 秒刷新可视树里所有时钟组件的文字(不重建控件)。</summary>
    public static void TickClocks(Control root)
    {
        var (t, d) = ClockText(DateTime.Now);
        foreach (var tb in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<TextBlock>())
        {
            if (Equals(tb.Tag, "clock-time"))
                tb.Text = t;
            else if (Equals(tb.Tag, "clock-date"))
                tb.Text = d;
        }
    }

    // —— 最近截图 ——
    private static Control Shots(string es, ZoneData data, IZoneActions? act)
    {
        int n = es == LaunchZone.Large ? 6 : 4;
        int cols = es == LaunchZone.Large ? 3 : 2;
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        string head = data.ShotsLoaded && data.ShotCount > 0
            ? string.Format(CultureInfo.InvariantCulture, I18nCore.Tr("最近截图 · 共 {0} 张"), data.ShotCount)
            : I18nCore.Tr("最近截图");
        g.Children.Add(Label(head));
        Control body;
        if (data.ShotsLoaded && data.Shots.Count == 0)
        {
            body = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 2,
                Children = { T(I18nCore.Tr("还没有截图"), 15, 255, FontWeight.Bold), Sub(I18nCore.Tr("游戏里按 F2 截图"), wrap: true) },
            };
        }
        else
        {
            var ug = new UniformGrid { Columns = cols, Rows = 2, Margin = new Thickness(-3, 3, -3, -3) };
            for (int i = 0; i < n; i++)
            {
                var cell = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(3), Background = W(20), ClipToBounds = true };
                if (i < data.Shots.Count)
                    cell.Child = new Image { Source = data.Shots[i], Stretch = Stretch.UniformToFill };
                ug.Children.Add(cell);
            }
            body = ug;
        }
        Grid.SetRow(body, 1);
        g.Children.Add(body);
        var host = Clickable(g, () => act?.OpenShots());
        ToolTip.SetTip(host, I18nCore.Tr("打开截图文件夹"));
        return host;
    }

    private static Control Clickable(Control c, Action click)
    {
        var host = new Panel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { c } };
        host.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            click();
            e.Handled = true;
        };
        return host;
    }

    // —— 组件库里的名称 / 说明 ——
    public static string Name(string type) => type switch
    {
        LaunchZone.TPlaytime => I18nCore.Tr("游戏时长"),
        LaunchZone.TServer => I18nCore.Tr("服务器状态"),
        LaunchZone.TShortcuts => I18nCore.Tr("快捷入口"),
        LaunchZone.TNote => I18nCore.Tr("便签"),
        LaunchZone.TClock => I18nCore.Tr("时钟"),
        LaunchZone.TScreenshots => I18nCore.Tr("最近截图"),
        _ => type,
    };

    public static string Desc(string type) => type switch
    {
        LaunchZone.TPlaytime => I18nCore.Tr("本周每天玩了多久"),
        LaunchZone.TServer => I18nCore.Tr("在线人数、延迟、一键加入"),
        LaunchZone.TShortcuts => I18nCore.Tr("模组、截图、日志、存档文件夹"),
        LaunchZone.TNote => I18nCore.Tr("记一句话，可以加多张"),
        LaunchZone.TClock => I18nCore.Tr("当前时间和日期"),
        LaunchZone.TScreenshots => I18nCore.Tr("当前实例最新的几张截图"),
        _ => "",
    };
}
