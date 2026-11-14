// 来源: Windows 版参考仓库 src/TipCL/Pages/Ai/AiIcons.cs(M4-H P5)。
// Windows 用 Lucide 的 SVG path + WPF Path/ControlTemplate;Linux 侧同一套 path data,
// 用 Avalonia 的 Geometry.Parse + Path 画出来(与 Common/IconLib 同一思路:矢量 + 主题色描边)。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using TipCL.App.Common;

namespace TipCL.App.Pages.Ai;

/// <summary>AI 子系统的矢量图标(24 单位视窗;与外壳的 16 单位 IconLib 并存,互不改动)。</summary>
internal static class AiIcons
{
    public const string Spark = "M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z";
    public const string Layers = "M12 2l10 6-10 6L2 8z M2 14l10 6 10-6";
    public const string Check = "M20 6L9 17l-5-5";
    public const string X = "M18 6L6 18 M6 6l12 12";
    public const string Skip = "M9 6l6 6-6 6";
    public const string ChevDown = "M6 9l6 6 6-6";
    public const string ChevRight = "M9 6l6 6-6 6";
    public const string ArrowUp = "M12 19V5 M5 12l7-7 7 7";
    public const string Undo = "M3 7v6h6 M21 17a9 9 0 0 0-15-6.7L3 13";
    public const string Retry = "M21 12a9 9 0 1 1-3-6.7L21 8 M21 3v5h-5";
    public const string Copy = "M11 9h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2z M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1";
    public const string ThumbUp = "M7 10v12 M15 5.9L14 10h5.8a2 2 0 0 1 2 2.3l-1.4 8a2 2 0 0 1-2 1.7H7V10l4-8a3 3 0 0 1 4 3.9z";
    public const string Shield = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";
    public const string Cpu = "M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M9 9h6v6H9z M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3";
    public const string Plus = "M12 5v14 M5 12h14";
    public const string Panel = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18";
    public const string Branch = "M6 3a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 15a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M18 5a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 9v6 M18 11a6 6 0 0 1-6 6H9";
    public const string Alert = "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z M12 9v4 M12 17h.01";
    public const string Trash = "M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6";
    public const string Pen = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";
    public const string Globe = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M3 12h18 M12 3a14 14 0 0 1 0 18 M12 3a14 14 0 0 0 0 18";
    public const string Plug = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z";
    public const string Box = "M21 8l-9-5-9 5v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8";
    public const string List = "M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01";
    public const string Archive = "M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4";
    public const string Clock = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M12 7v5l3 2";
    public const string Search = "M21 21l-4.34-4.34 M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16z";
    public const string File = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8l6 6v12a2 2 0 0 1-2 2z M14 2v6h6 M8 13h8 M8 17h5";
    public const string Download = "M12 3v12 M6 11l6 6 6-6 M4 21h16";
    public const string Scan = "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10";
    public const string Settings = "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1";
    public const string Spinner = "M19 12a7 7 0 1 1-2.3-5.2 M19 3v5h-5";

    /// <summary>描边图标(24 单位视窗缩放到 size)。</summary>
    public static Path Icon(string data, double size = 16, string brushKey = "B.InkMuted")
    {
        var geometry = Geometry.Parse(data);
        if (Math.Abs(size - 24) > 0.01)
        {
            geometry.Transform = new ScaleTransform(size / 24.0, size / 24.0);
        }
        var path = new Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            StrokeThickness = 2 * (size / 24.0),
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path[!Path.StrokeProperty] = Res.Bind(brushKey);
        return path;
    }

    /// <summary>工具图标:按工具名挑一个语义图标(照 Windows 版 Lucide.ToolIcon)。</summary>
    public static string ToolIcon(string? tool)
    {
        string name = tool ?? "";
        if (name.Length == 0)
        {
            return Spinner;
        }
        if (name.Contains("install") || name.Contains("download"))
        {
            return Download;
        }
        if (name.Contains("search"))
        {
            return Search;
        }
        if (name.Contains("delete") || name.Contains("remove"))
        {
            return Trash;
        }
        if (name.Contains("config") || name.Contains("write"))
        {
            return Pen;
        }
        if (name.Contains("java"))
        {
            return Cpu;
        }
        if (name.Contains("launch") || name.Contains("game"))
        {
            return Spark;
        }
        if (name.Contains("list") || name.Contains("inspect"))
        {
            return List;
        }
        if (name.Contains("diagnose") || name.Contains("crash") || name.Contains("log") || name.Contains("conflict"))
        {
            return Shield;
        }
        if (name.Contains("plan"))
        {
            return Layers;
        }
        if (name.StartsWith("mcp", StringComparison.Ordinal))
        {
            return Plug;
        }
        return Settings;
    }

    /// <summary>26×26 的图标块(底色随状态;完成/失败/跳过时右下角带一个小角标)。</summary>
    public static Grid Tile(Control icon, ToolState state)
    {
        var box = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Child = Center(icon),
        };
        box[!Border.BackgroundProperty] = Res.Bind(state switch
        {
            ToolState.Failed => "B.DangerSoft",
            ToolState.Skipped => "B.Hover",
            _ => "B.AccentSoft",
        });

        var grid = new Grid
        {
            Width = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Top,
        };
        grid.Children.Add(box);

        if (state is ToolState.Done or ToolState.Failed or ToolState.Skipped)
        {
            string data = state switch
            {
                ToolState.Done => Check,
                ToolState.Failed => X,
                _ => Skip,
            };
            var badgeIcon = Icon(data, 7, "B.OnAccentStrong");
            badgeIcon.StrokeThickness = 4;
            var badge = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = Center(badgeIcon),
            };
            badge[!Border.BorderBrushProperty] = Res.Bind("B.Paper");
            badge[!Border.BackgroundProperty] = Res.Bind(state switch
            {
                ToolState.Done => "B.Accent",
                ToolState.Failed => "B.Danger",
                _ => "B.InkFaint",
            });
            grid.Children.Add(badge);
        }
        return grid;
    }

    /// <summary>上下文占用环(0..1)。</summary>
    public static Grid Ring(double ratio, double size = 18)
    {
        var grid = new Grid { Width = size, Height = size };
        var circle = new Ellipse { StrokeThickness = 2.4 };
        circle[!Ellipse.StrokeProperty] = Res.Bind("B.Line");
        grid.Children.Add(circle);

        if (ratio > 0.001)
        {
            double r = size / 2.0 - 1.2;
            double c = size / 2.0;
            double sweep = Math.Min(ratio, 0.9999) * 2 * Math.PI;
            var figure = new PathFigure
            {
                StartPoint = new Point(c, c - r),
                IsClosed = false,
            };
            figure.Segments!.Add(new ArcSegment
            {
                Point = new Point(c + r * Math.Sin(sweep), c - r * Math.Cos(sweep)),
                Size = new Size(r, r),
                RotationAngle = 0,
                IsLargeArc = sweep > Math.PI,
                SweepDirection = SweepDirection.Clockwise,
                IsStroked = true,
            });
            var geometry = new PathGeometry();
            geometry.Figures!.Add(figure);
            var arc = new Path
            {
                Data = geometry,
                StrokeThickness = 2.4,
                StrokeLineCap = PenLineCap.Round,
            };
            arc[!Path.StrokeProperty] = Res.Bind(ratio > 0.85 ? "B.Danger" : (ratio > 0.65 ? "B.Warn" : "B.Accent"));
            grid.Children.Add(arc);
        }
        return grid;
    }

    private static T Center<T>(T el) where T : Control
    {
        el.HorizontalAlignment = HorizontalAlignment.Center;
        el.VerticalAlignment = VerticalAlignment.Center;
        return el;
    }
}

/// <summary>工具行的状态(照 Windows 版 Pages/ToolState.cs)。</summary>
internal enum ToolState
{
    Prepare,
    Running,
    Done,
    Skipped,
    Failed,
}
