using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C5c **布局探针**:把"窄页面"这件事实测成数字(不是靠截图猜)。
///
/// 为什么要有这一组:mac 的页面区被 196px 侧栏压到约 **668px**,而 Windows 版没有侧栏 ⇒
/// 两个缺陷只在移植后暴露:
/// <list type="number">
/// <item><c>Ui.Section</c> 的标题在窄页面被右列挤成 **0 宽**(标题整块看不见);</item>
/// <item><c>PageBase.ScrollBody</c> 的 <c>SmoothScroll</c> 在内容宽于视口时拿到了**非零水平偏移**,
///   整页内容左偏(库页白卡实测从 x=197 起,正确应当是 x≈214)。</item>
/// </list>
///
/// 这一组读的是几何量(<c>Bounds</c> / <c>ScrollViewer.Offset</c> / <c>Extent</c> / <c>Viewport</c> /
/// <c>TranslatePoint</c>),不读像素坐标 —— 截图只能看到"哪儿不对",探针才能说"差多少"。
/// 命名避开 Avalonia 基类既有成员(那个僵尸踩过 <c>Loaded</c>/<c>Shown</c> ⇒ CS0079)。
/// </summary>
public class C5cLayoutProbeTests
{
    /// <summary>本题实测的窄页面宽度:900 宽窗口 − 196px 侧栏 − 36px 页边距 ≈ 668。</summary>
    private const double NarrowPageWidth = 668.0;

    /// <summary>把页面放进一个**固定宽度**的宿主里(模拟被侧栏压窄之后的页面区)。</summary>
    private static (UiHost Window, Border Frame) MountNarrow(Control page, double width = NarrowPageWidth)
    {
        Grid host = new Grid();
        UiHost window = UiHost.Show(host);
        // 与 --smoke 的窗口同尺寸(900x550):侧栏 196 之后页面区正好 668,探针数字能直接和像素取证对照
        window.Window.Width = 900.0;
        window.Window.Height = 550.0;
        C5bFiles.Flush(2);
        Border frame = new Border
        {
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = page,
        };
        host.Children.Add(frame);
        C5bFiles.Flush(6);
        return (window, frame);
    }

    /// <summary>控件左上角换算到窗口坐标(与"像素取证"同一把尺子)。</summary>
    private static Point OriginInWindow(Visual control, Window window)
    {
        return control.TranslatePoint(new Point(0.0, 0.0), window) ?? new Point(double.NaN, double.NaN);
    }

    /// <summary>从某个控件往上到窗口,把每一层的"类型 X:宽"打出来(定位偏移是从哪一层开始的)。</summary>
    private static string Chain(Visual control, Window window)
    {
        List<string> parts = new List<string>();
        Visual? cursor = control;
        while (cursor != null)
        {
            if (cursor is Control c)
            {
                Point p = OriginInWindow(c, window);
                parts.Add(c.GetType().Name + "(x=" + p.X.ToString("0.#") + ",w=" + c.Bounds.Width.ToString("0.#") + ")");
            }

            if (ReferenceEquals(cursor, window))
            {
                break;
            }

            cursor = cursor.GetVisualParent();
        }

        return string.Join(" < ", parts);
    }

    private static TextBlock TextOf(Control root, string text)
    {
        TextBlock? hit = C5bFiles.Texts(root).FirstOrDefault(t => string.Equals(t.Text, text, StringComparison.Ordinal));
        Assert.True(hit != null, "页里找不到文案「" + text + "」");
        return hit!;
    }

    /// <summary>
    /// 探针 1(<b>缺陷 1</b>):窄页面下 <c>Ui.Section</c> 的**标题必须还有可用宽度**。
    /// 修复前这一条的实测值是 `0`,所以它同时也是"修复前证据"。
    /// </summary>
    [Fact]
    public void Probe_section_title_keeps_a_usable_width_on_a_narrow_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            C5bFiles.LayoutDir();
            InstancePage page = new InstancePage();
            (UiHost window, _) = MountNarrow(page);
            try
            {
                TextBlock title = TextOf(page, I18n.L("实例"));
                TextBlock sub = TextOf(page, I18n.L("每个实例是一份独立的 .minecraft,互不干扰"));
                Grid section = title.GetVisualAncestors().OfType<Grid>()
                    .First(g => g.Children.OfType<Control>().Any(c => C5bFiles.Descendants(c).OfType<Button>().Any()));
                Control? right = section.Children.OfType<Control>().FirstOrDefault(c => C5bFiles.Descendants(c).OfType<Button>().Any());
                Assert.True(right != null, "Section 里应当能认出右簇");

                double pageWidth = page.Bounds.Width;
                double titleWidth = title.Bounds.Width;
                double subWidth = sub.Bounds.Width;
                double rightWidth = right!.Bounds.Width;
                string measured = "页面宽=" + pageWidth.ToString("0.#") + " 标题宽=" + titleWidth.ToString("0.#") +
                                  " 副说明宽=" + subWidth.ToString("0.#") + " 右簇宽=" + rightWidth.ToString("0.#") +
                                  " 右簇行=" + Grid.GetRow(right) + " 右簇x=" + right.Bounds.X.ToString("0.#") +
                                  " 区宽=" + section.Bounds.Width.ToString("0.#");

                // 668 的页面区里:标题与副说明都必须有可用宽度(修复前实测都是 0 —— 这一条就是缺陷 1 的判据)
                Assert.True(titleWidth >= 160.0, "窄页面下 Section 标题被压没了!(" + measured + ")");
                Assert.True(subWidth >= 160.0, "窄页面下 Section 副说明也被压没了!(" + measured + ")");
                // 修法是"摆不开就把右簇换到第二行"(C5c):668 摆不开 653 宽的右簇 ⇒ 必须已经降级
                Assert.Equal(1, Grid.GetRow(right));
            }
            finally
            {
                window.Dispose();
                UiHost.Flush();
            }
        });
    }

    /// <summary>
    /// 探针 2(<b>缺陷 2</b>):窄页面下 <c>ScrollBody</c> 的那根 <c>ScrollViewer</c> **水平偏移必须是 0**,
    /// 白卡左缘应当落在页面左边缘 + 18px 页边距处。
    /// </summary>
    [Fact]
    public void Probe_library_body_has_no_horizontal_offset_on_a_narrow_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            C5bFiles.LayoutDir();
            LibraryPage page = new LibraryPage();
            (UiHost window, Border frame) = MountNarrow(page);
            try
            {
                SmoothScroll scroll = C5bFiles.Descendants(page).OfType<SmoothScroll>().First();
                Vector offset = ((ScrollViewer)scroll).Offset;
                Size extent = scroll.Extent;
                Size viewport = scroll.Viewport;
                Point frameOrigin = OriginInWindow(frame, window.Window);
                Point scrollOrigin = OriginInWindow(scroll, window.Window);
                // 左侧那张卡:双栏 body(3 列)里第 0 列的那个 Border
                Grid body = C5bFiles.Descendants(page).OfType<Grid>().First(g => g.ColumnDefinitions.Count >= 3);
                Border leftCard = body.Children.OfType<Border>().First(c => Grid.GetColumn(c) == 0);
                Point cardOrigin = OriginInWindow(leftCard, window.Window);
                string measured = "Offset=(" + offset.X.ToString("0.#") + "," + offset.Y.ToString("0.#") + ") Extent=" + extent.Width.ToString("0.#") +
                                  " Viewport=" + viewport.Width.ToString("0.#") + " 水平滚动=" + scroll.HorizontalScrollBarVisibility +
                                  " 页面起x=" + frameOrigin.X.ToString("0.#") + " 页面宽=" + page.Bounds.Width.ToString("0.#") +
                                  " 滚动框起x=" + scrollOrigin.X.ToString("0.#") + " 滚动框宽=" + scroll.Bounds.Width.ToString("0.#") +
                                  " 左卡起x=" + cardOrigin.X.ToString("0.#") + " 左卡宽=" + leftCard.Bounds.Width.ToString("0.#") +
                                  " 左卡布局x=" + leftCard.Bounds.X.ToString("0.#") + " 左卡变换=" + (leftCard.RenderTransform?.ToString() ?? "null") +
                                  " 左卡Margin=" + leftCard.Margin +
                                  " 列宽=" + string.Join("/", body.ColumnDefinitions.Select(c => c.ActualWidth.ToString("0.#"))) +
                                  " 列间距=" + body.ColumnSpacing.ToString("0.#") +
                                  "\n链路 = " + Chain(leftCard, window.Window);

                Assert.True(Math.Abs(offset.X) < 0.5, "窄页面下正文被水平滚走了!(" + measured + ")");
                // 页边距 18:左卡左缘 = 页面左缘 + 18
                Assert.True(Math.Abs(cardOrigin.X - (frameOrigin.X + 18.0)) <= 1.0, "库页白卡没有落在 18px 页边距上!(" + measured + ")");
            }
            finally
            {
                window.Dispose();
                UiHost.Flush();
            }
        });
    }

    /// <summary>
    /// 探针 3(不许把缺陷修坏):**宽页面**(不受侧栏挤压时)的 Section 仍然必须是"左标题 + 右一簇"两列,
    /// 且列宽与修复前一致 —— 保证修法是"极窄才降级",不是"永远改排布"。
    /// </summary>
    [Fact]
    public void Probe_wide_page_section_stays_two_columns()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            C5bFiles.LayoutDir();
            InstancePage page = new InstancePage();
            (UiHost window, _) = MountNarrow(page, 1200.0);
            try
            {
                TextBlock title = TextOf(page, I18n.L("实例"));
                // Section 的那个 Grid = 标题的祖先里**带按钮子元素**的第一个(近处的 H2 只有文字)
                Grid section = title.GetVisualAncestors().OfType<Grid>()
                    .First(g => g.Children.OfType<Control>().Any(c => C5bFiles.Descendants(c).OfType<Button>().Any()));
                // 右簇 = Section 里"带着按钮的那一块"(标题那块一个按钮都没有)
                Control? right = section.Children.OfType<Control>().FirstOrDefault(c => C5bFiles.Descendants(c).OfType<Button>().Any());
                Assert.True(right != null, "Section 里应当能认出右列那一簇");
                string measured = "页面宽=" + page.Bounds.Width.ToString("0.#") + " 区宽=" + section.Bounds.Width.ToString("0.#") +
                                  " 标题宽=" + title.Bounds.Width.ToString("0.#") + " 右簇x=" + right!.Bounds.X.ToString("0.#") +
                                  " 右簇宽=" + right.Bounds.Width.ToString("0.#") + " 右簇行=" + Grid.GetRow(right) +
                                  " 右簇列=" + Grid.GetColumn(right) + " 行数=" + section.RowDefinitions.Count +
                                  " 列宽=" + string.Join("/", section.ColumnDefinitions.Select(c => c.ActualWidth.ToString("0.#")));

                // 宽页面:右簇仍在标题**右边**(同一行)
                Assert.Equal(0, Grid.GetRow(right));
                Assert.True(right.Bounds.X >= title.Bounds.Width, "宽页面下右簇应当在标题右侧!(" + measured + ")");
                Assert.True(title.Bounds.Width >= 160.0, "宽页面下标题必须有宽度!(" + measured + ")");
            }
            finally
            {
                window.Dispose();
                UiHost.Flush();
            }
        });
    }
}
