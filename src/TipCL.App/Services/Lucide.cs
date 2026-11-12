using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using TipCL.Themes;
// WPF 版的 Path 也要写全名才不和 IO 那侧的 Path 冲突;
// Avalonia 这边用别名,公开签名里的类型还是 Avalonia.Controls.Shapes.Path。
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace TipCL.Services;

/// <summary>
/// C1a:Lucide 图标系统,从 Windows 版 Services\Lucide.cs(WPF)移植到 Avalonia 11。
///
/// 移植原则:
/// - 所有 <c>public const string</c> 的 SVG path-data 常量**逐字节照抄**(页面会直接引用它们);
/// - 公开方法名与签名保持 <c>Icon</c> / <c>Spinner</c> / <c>Asset</c> / <c>Rotate</c> / <c>Shimmer</c> / <c>ToolIcon</c>,
///   后续页面包照抄调用点即可;
/// - WPF 的 <c>LayoutTransform</c>(整体缩放,含描边)在 Avalonia 没有对应属性,改成**把缩放烘进几何**
///   并把 StrokeThickness 同步乘上系数 —— 渲染结果与 WPF 等价,同时把 RenderTransform 留给旋转
///   (Spinner / Rotate 要用);
/// - 刷子按字符串键查色板(C1a 的 Palette),查不到给中性灰兜底;
/// - 动画:WPF 的 Motion.Spin / Motion.Loop 属于 C2 的 Services,Motion.cs 还没有 mac 版,
///   这里用 Avalonia 自带的 Animation 实现同样效果的**最小版本**(见 DECISIONS.md D-68)。
/// </summary>
public static class Lucide
{
    public const string Brain = "M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77";

    public const string ChevronRight = "M9 18l6-6-6-6";

    public const string LoaderCircle = "M21 12a9 9 0 1 1-6.219-8.56";

    private const string Circle12 = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0";

    public const string CircleCheck = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M9 12l2 2 4-4";

    public const string CircleSlash2 = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M22 2L2 22";

    public const string CircleX = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M15 9l-6 6 M9 9l6 6";

    public const string Search = "M21 21l-4.34-4.34 M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0";

    public const string FileText = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z M14 2v5a1 1 0 0 0 1 1h5 M10 9H8 M16 13H8 M16 17H8";

    public const string FilePenLine = "M14.364 13.634a2 2 0 0 0-.506.854l-.837 2.87a.5.5 0 0 0 .62.62l2.87-.837a2 2 0 0 0 .854-.506l4.013-4.009a1 1 0 0 0-3.004-3.004z M14.487 7.858A1 1 0 0 1 14 7V2 M20 19.645V20a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l2.516 2.516 M8 18h1";

    public const string ShieldAlert = "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z M12 8v4 M12 16h.01";

    public const string Eye = "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0";

    public const string Sparkles = "M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z M20 2v4 M22 4h-4 M2 20a2 2 0 1 0 4 0a2 2 0 1 0-4 0";

    public const string SlidersHorizontal = "M10 5H3 M12 19H3 M14 3v4 M16 17v4 M21 12h-9 M21 19h-5 M21 5h-7 M8 10v4 M8 12H3";

    public const string Ban = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M4.929 4.929L19.07 19.071";

    public const string Trash2 = "M10 11v6 M14 11v6 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2";

    /// <summary>图标设计稿的画布边长(WPF 版同样按 24 缩放)。</summary>
    public const double DesignSize = 24.0;

    /// <summary>描边宽度(设计稿尺度);实际描边 = 2 × size/24。</summary>
    public const double DesignStroke = 2.0;

    /// <summary>WPF 版 Motion.Enabled 的替身:没有 Motion 门面之前默认开启动画。</summary>
    public static bool MotionEnabled { get; set; } = true;

    private static readonly ConditionalWeakTable<TextBlock, TranslateTransform> _shimmerSlide = new ConditionalWeakTable<TextBlock, TranslateTransform>();

    private static readonly object AssetGate = new object();

    private static readonly Dictionary<string, Bitmap> AssetCache = new Dictionary<string, Bitmap>(StringComparer.Ordinal);

    /// <summary>
    /// 工具名 → 图标 path data。与 Windows 版逐字相同,只把 <c>AiUiText.IsMcp</c> 的三行判定内联进来
    /// —— AI 页面组(TipCL.Pages.Ai)是 C3 的事,这一层不许依赖页面命名空间。
    /// </summary>
    public static string? ToolIcon(string? tool)
    {
        switch (tool)
        {
        case "search_modpacks":
        case "search_versions":
        case "search_content":
        case "search_worlds":
        case "search_mods":
            return "M21 21l-4.34-4.34 M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0";
        case "read_mod_config":
        case "get_latest_log":
        case "read_artifact":
        case "get_crash_report":
        case "list_mod_configs":
            return "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z M14 2v5a1 1 0 0 0 1 1h5 M10 9H8 M16 13H8 M16 17H8";
        case "write_mod_config":
            return "M14.364 13.634a2 2 0 0 0-.506.854l-.837 2.87a.5.5 0 0 0 .62.62l2.87-.837a2 2 0 0 0 .854-.506l4.013-4.009a1 1 0 0 0-3.004-3.004z M14.487 7.858A1 1 0 0 1 14 7V2 M20 19.645V20a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l2.516 2.516 M8 18h1";
        default:
            if (IsMcp(tool))
            {
                return "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z";
            }

            return null;
        }
    }

    /// <summary>MCP 工具名判定,与 Windows 版 <c>Pages/Ai/AiUiText.IsMcp</c> 逐字相同(C3 落地后可改回调用它)。</summary>
    public static bool IsMcp(string? tool)
    {
        string text = tool ?? "";
        if (!text.StartsWith("mcp__", StringComparison.Ordinal) && !text.StartsWith("mcp.", StringComparison.Ordinal))
        {
            return text.StartsWith("mcp_", StringComparison.Ordinal);
        }

        return true;
    }

    /// <summary>
    /// 一个描边图标。<paramref name="data"/> 是 24×24 设计稿尺度的 SVG path data,
    /// <paramref name="size"/> 是最终边长,<paramref name="brushKey"/> 是色板键(如 <c>"B.InkMuted"</c>)。
    /// </summary>
    public static ShapePath Icon(string data, double size = 16.0, string brushKey = "B.InkMuted")
    {
        double scale = size / DesignSize;
        return new ShapePath
        {
            Data = Scaled(Geometry.Parse(data), scale),
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            StrokeThickness = DesignStroke * scale,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stroke = Palette.Brush(brushKey),
            IsHitTestVisible = false,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        };
    }

    /// <summary>转圈图标(1 秒一圈,无限循环;动画细节见文件头的说明)。</summary>
    public static ShapePath Spinner(double size = 16.0, string brushKey = "B.AccentDeep")
    {
        ShapePath path = Icon(LoaderCircle, size, brushKey);
        var rotate = new RotateTransform(0.0);
        path.RenderTransform = rotate;
        path.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        Spin(path, rotate);
        return path;
    }

    /// <summary>
    /// 工具图标:优先用内嵌 PNG 当"形状",再用 <paramref name="brushKey"/> 的颜色填出来
    /// (WPF 是 Rectangle + OpacityMask=ImageBrush,这里等价);没有该工具的 PNG 时退回转圈图标。
    /// </summary>
    public static Control Asset(string tool, double size = 16.0, string brushKey = "B.AccentDeep")
    {
        Bitmap? bitmap = Decode(tool);
        if (bitmap is null)
        {
            return Spinner(size, brushKey);
        }

        return new Rectangle
        {
            Width = size,
            Height = size,
            Fill = Palette.Brush(brushKey),
            OpacityMask = new ImageBrush { Source = bitmap, Stretch = Stretch.Uniform },
            IsHitTestVisible = false,
        };
    }

    /// <summary>折叠箭头:展开转 90°,收起回 0°(160 ms,三次缓出;关掉动画就直接落位)。</summary>
    public static void Rotate(ShapePath chevron, bool open)
    {
        if (chevron is null)
        {
            return;
        }

        if (chevron.RenderTransform is not RotateTransform rotate)
        {
            rotate = new RotateTransform(0.0);
            chevron.RenderTransform = rotate;
        }

        double target = open ? 90.0 : 0.0;
        if (!MotionEnabled)
        {
            rotate.Angle = target;
            return;
        }

        Animate(rotate, RotateTransform.AngleProperty, rotate.Angle, target, TimeSpan.FromMilliseconds(160.0), new CubicEaseOut());
    }

    /// <summary>
    /// 文字微光:把前景换成带高光的渐变刷,并让高光水平往返滑动(WPF 版用 Motion.Loop,
    /// mac 版 Motion 未移植,这里用 Avalonia 的无限动画实现)。
    /// </summary>
    public static void Shimmer(TextBlock tb, bool on, string restoreKey)
    {
        if (tb is null)
        {
            return;
        }

        if (_shimmerSlide.TryGetValue(tb, out TranslateTransform? existing))
        {
            _shimmerSlide.Remove(tb);
        }

        if (!on || !MotionEnabled)
        {
            tb.Foreground = Palette.Brush(restoreKey);
            return;
        }

        Color strong = (tb.Foreground as ISolidColorBrush)?.Color ?? Palette.Color(restoreKey);
        Color weak = Color.FromArgb(56, strong.R, strong.G, strong.B);
        var slide = new TranslateTransform(-2.0, 0.0);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1.0, 0.0, RelativeUnit.Relative),
            Transform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(3.0, 1.0),
                    slide,
                },
            },
            GradientStops =
            {
                new GradientStop(strong, 0.0),
                new GradientStop(strong, 0.34),
                new GradientStop(weak, 0.5),
                new GradientStop(strong, 0.66),
                new GradientStop(strong, 1.0),
            },
        };

        tb.Foreground = brush;
        _shimmerSlide.Add(tb, slide);

        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(4.0),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(TranslateTransform.XProperty, -2.0) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(TranslateTransform.XProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(TranslateTransform.XProperty, 0.0) } },
            },
        };

        Run(animation, slide);
    }

    /// <summary>把几何按缩放系数烘进去(等价 WPF 的 LayoutTransform = ScaleTransform)。</summary>
    private static Geometry Scaled(Geometry geometry, double scale)
    {
        if (Math.Abs(scale - 1.0) > 1e-9)
        {
            geometry.Transform = new ScaleTransform(scale, scale);
        }

        return geometry;
    }

    /// <summary>转圈:0°→360°,1 秒一轮,无限循环。</summary>
    private static void Spin(Animatable owner, RotateTransform rotate)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.0),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(RotateTransform.AngleProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(RotateTransform.AngleProperty, 360.0) } },
            },
        };

        Run(animation, rotate);
    }

    /// <summary>两帧动画(起点 → 终点),带缓动。</summary>
    private static void Animate(Animatable target, AvaloniaProperty property, double from, double to, TimeSpan duration, Easing easing)
    {
        var animation = new Animation
        {
            Duration = duration,
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(property, to) } },
            },
        };

        Run(animation, target);
    }

    /// <summary>
    /// 起动画。两条纪律:
    /// 1. 动画是装饰性的:**任何失败都不许把界面带崩**(拿不到 tick 的离屏环境里 RunAsync 也可能失败),
    ///    所以吞掉异常;
    /// 2. 只在**真正跑起来的应用生命周期**下起动画。Headless / 离屏(测试进程、--smoke)里没有任何东西
    ///    推进动画时钟,无限动画会一直挂着 —— 本包实测:测试进程里起了这些无限动画之后,
    ///    进程跑完所有用例却卡在收尾(见 DECISIONS.md D-69)。所以这里显式判掉。
    /// </summary>
    private static void Run(Animation animation, Animatable target)
    {
        if (Application.Current?.ApplicationLifetime is null)
        {
            return;
        }

        try
        {
            _ = animation.RunAsync(target);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>把内嵌 PNG 解成 Avalonia 位图;没有或解不开返回 null。结果带缓存(位图不可变,可共享)。</summary>
    private static Bitmap? Decode(string tool)
    {
        string name = tool ?? "";
        lock (AssetGate)
        {
            if (AssetCache.TryGetValue(name, out Bitmap? cached))
            {
                return cached;
            }

            if (!LucideAssets.Icons.TryGetValue(name, out byte[]? bytes) || bytes is null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new Bitmap(stream);
                AssetCache[name] = bitmap;
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
