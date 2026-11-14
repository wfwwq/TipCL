// M4-H R21c:行内码底色(PARITY §9.5 里「仍未做」的两条 UI 欠账之一)。
//
// 背景:参考版 Windows 是 WPF 的 `Run` + `Background`(`Pages/Ai/MdView.cs:340`,
// `SetResourceReference(TextElement.BackgroundProperty, "B.Hover")`)—— 行内码自带一小块底色。
// P5 当年以为「Avalonia 的 Run 没有 Background」而退成「等宽 + 主题色」;这个前提在 11.3.2 上不成立:
// `TextElement.BackgroundProperty` 存在,而且真会被画出来。所以这里照参考版原样做:同一个属性、同一个键。
//
// 这里钉三件事:1行内码的 Run 挂着主题键 B.Hover 的底色、字色仍是 B.AccentText、字体仍是 Mono/12;
// 2底色是**真画出来的**(无头 Skia 抓帧数像素,不是只看属性挂上没挂上);
// 3底色只画不参与布局:去掉底色后段落尺寸一字不差,行距仍是 22,长行内码不把段落撑宽。
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using TipCL.App.Pages.Ai;
using TipCL.App.Shell;
using Xunit;

namespace TipCL.Tests.App;

public class MdViewInlineCodeTests
{
    /// <summary>MdView 的宿主 StackPanel(内容 → ScrollViewer → StackPanel)。</summary>
    private static StackPanel Host(MdView view) => (StackPanel)((ScrollViewer)view.Content!).Content!;

    /// <summary>首块的 TextBlock(MdView 的段落就是 TextBlock.Inlines)。</summary>
    private static TextBlock Paragraph(string markdown)
    {
        var view = new MdView();
        view.SetMarkdown(markdown);
        return Host(view).Children.OfType<TextBlock>().First();
    }

    /// <summary>段落里那个带底色的行内码 Run。</summary>
    private static Run CodeRun(TextBlock paragraph)
    {
        Run? run = paragraph.Inlines!.OfType<Run>().FirstOrDefault(r => r.Background != null);
        Assert.NotNull(run);                       // 旧实现(裸 Run,无底色)在这里就是红的
        return run!;
    }

    /// <summary>当前主题下某键的颜色(资源没挂上会直接失败,别让断言拿 null 蒙混过去)。</summary>
    private static Color PaletteColor(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var value), $"Application 级缺少主题键 {key}");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    /// <summary>WCAG 相对亮度对比度(1..21)。</summary>
    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
        => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte v)
    {
        double s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>跑一次真布局并抓当前帧(与 InstanceDetailCrashTests 的 PassAsync 同一条路)。</summary>
    private static WriteableBitmap Frame(TopLevel window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        return frame!;
    }

    /// <summary>帧里与给定 RGB 相差不超过 tol 的像素个数(BGRA8888)。</summary>
    private static int CountColor(WriteableBitmap frame, Color color, int tol)
    {
        int n = 0;
        using var copy = new WriteableBitmap(frame.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = copy.Lock())
        {
            frame.CopyPixels(fb, AlphaFormat.Unpremul);
            var row = new byte[fb.RowBytes];
            for (int y = 0; y < frame.PixelSize.Height; y++)
            {
                Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, row.Length);
                for (int x = 0; x < frame.PixelSize.Width; x++)
                {
                    if (Math.Abs(row[x * 4 + 2] - color.R) <= tol
                        && Math.Abs(row[x * 4 + 1] - color.G) <= tol
                        && Math.Abs(row[x * 4 + 0] - color.B) <= tol)
                    {
                        n++;
                    }
                }
            }
        }
        return n;
    }

    /// <summary>把一段 markdown 渲染进无头窗口并数出「底色像素」个数。</summary>
    private static int BackgroundPixels(string markdown)
    {
        Color hover = PaletteColor("B.Hover");
        var view = new MdView { Width = 400 };
        view.SetMarkdown(markdown);
        var window = new Window { Width = 400, Height = 140, Content = view };
        window.Show();
        using WriteableBitmap frame = Frame(window);
        // 精确匹配(容差 0):浅色档 B.Hover #F2F2F2 与表格线 B.LineSoft #F0F0F0 只差 2,
        // 给容差就会把表格分隔线数成「行内码底色」。
        return CountColor(frame, hover, tol: 0);
    }

    [Fact]
    public void InlineCode_CarriesThemedBackground() => TestApp.Run(() =>
    {
        _ = new MainWindow();                     // 挂 Application 级 Palette,否则取色全是 null

        var view = new MdView();
        view.SetMarkdown("含 `码  abc` 的行。");
        TextBlock paragraph = Host(view).Children.OfType<TextBlock>().First();
        Run code = CodeRun(paragraph);

        // 底色:与参考版 Run.Background 同键(B.Hover:浅 #F2F2F2 / 深 #2A2A2A)
        Assert.Equal(PaletteColor("B.Hover"), Assert.IsType<SolidColorBrush>(code.Background).Color);
        Assert.Equal(PaletteColor("B.AccentText"), Assert.IsType<SolidColorBrush>(code.Foreground).Color);
        Assert.Same(MdView.Mono, code.FontFamily);       // 等宽字体沿用现在的 Mono
        Assert.Equal(12, code.FontSize);                 // 字号沿用现在的 12
        Assert.Equal(" 码  abc ", code.Text);            // 原来的 " " + x.Text + " " 一字未动(连续空白保留)

        // 底色上还得看得清:AccentText vs B.Hover 要有可读对比
        double contrast = Contrast(PaletteColor("B.AccentText"), PaletteColor("B.Hover"));
        Assert.True(contrast >= 4.5, $"行内码字色对底色对比只有 {contrast:0.00}:1");
    });

    [Fact]
    public void InlineCode_BackgroundIsActuallyPainted() => TestApp.Run(() =>
    {
        var window = new MainWindow();
        bool before = window.IsDark;
        try
        {
            window.SetDark(false);                                    // 固定浅色:B.Hover = #F2F2F2
            Assert.Equal(Color.Parse("#FFF2F2F2"), PaletteColor("B.Hover"));

            // 对照:没写行内码时,同色像素只能是「可忽略的噪声」,不能是一块底色。
            // **不能断言恰好 0**:字形栅格化与抗锯齿逐平台不同 —— Windows 上是 0 个,Ubuntu 上
            // 会在文字边缘凑出 1 个恰好等于 #F2F2F2 的像素(2026-11-13 第一次在 Linux 上真跑
            // 测试时才红的:Expected 0 / Actual 1)。判据改成「比底色块小两个数量级」:区分度
            // 一点没丢(带行内码时 >100),也不会因为一个边缘像素把整条用例判死。
            int control = BackgroundPixels("前缀 abc 后缀");
            Assert.True(control <= 5, $"对照段落里同色像素有 {control} 个,底色计数已经不可信");

            int withCode = BackgroundPixels("前缀 `abc` 后缀");
            Assert.True(withCode > 100, $"行内码底色没画出来:只数到 {withCode} 个 #F2F2F2 像素");
            Assert.True(withCode > control * 10, $"底色块({withCode})与对照噪声({control})区分不开");

            // 底色在嵌套上下文里也要画得出来(AI 回答里行内码常常嵌在粗体 / 列表 / 引用 / 表格 / 标题里)
            foreach (string markdown in new[]
                     {
                         "**粗体里的 `abc` 代码**",
                         "- 列表里的 `abc`",
                         "> 引用里的 `abc`",
                         "| 名称 | 值 |\n| :- | :- |\n| `abc` | 1 |",
                         "## 标题里的 `abc`",
                     })
            {
                int nested = BackgroundPixels(markdown);
                Assert.True(nested > 100, $"嵌套上下文里的行内码底色没画出来({markdown.Replace('\n', ' ')}):只数到 {nested} 个像素");
            }
        }
        finally
        {
            window.SetDark(before);                                   // 别把浅色留给别的用例
        }
    });

    [Fact]
    public void InlineCode_BackgroundDoesNotAffectLayout() => TestApp.Run(() =>
    {
        _ = new MainWindow();

        var plain = Paragraph("前缀 abcdef 后缀");
        plain.Measure(new Size(320, double.PositiveInfinity));
        var withCode = Paragraph("前缀 `abcdef` 后缀");
        withCode.Measure(new Size(320, double.PositiveInfinity));

        Assert.Equal(22, plain.DesiredSize.Height);                            // 段落行距基线
        Assert.Equal(plain.DesiredSize.Height, withCode.DesiredSize.Height);   // 行内码不许把行距改坏

        // 底色只画、不参与布局:把底色的绑定摘掉,段落尺寸必须一字不差
        Size withBackground = withCode.DesiredSize;
        Run code = CodeRun(withCode);
        code.Background = null;
        withCode.InvalidateMeasure();
        withCode.Measure(new Size(320, double.PositiveInfinity));
        Assert.Equal(withBackground, withCode.DesiredSize);

        // 长行内码照常参与折行:段落宽度夹在可用宽内(不是靠 InlineUIContainer 那种不可折方块)
        var longCode = Paragraph("前缀 `" + string.Join(" ", Enumerable.Repeat("mmmm", 20)) + "` 后缀");
        longCode.Measure(new Size(320, double.PositiveInfinity));
        Assert.True(longCode.DesiredSize.Width <= 320, $"长行内码把段落撑到 {longCode.DesiredSize.Width}");
    });
}
