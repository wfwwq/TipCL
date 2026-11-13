// M4-H R17:Markdown 链接可点。
//
// 背景:参考版用 WPF 的 `Hyperlink`(`Pages/Ai/MdView.cs:367`)—— 天生可点、能开浏览器。
// Avalonia 的 `Span` 不是 Control(没有 Cursor / ToolTip / 事件),所以 P5 只能画成「主题色 + 下划线」,
// D-22 把它记成一条平台限制。R17 用 `InlineUIContainer`(Avalonia 里 Inline 可以是控件宿主)把这
// 条限制去掉:链接里放一个真 `TextBlock`,挂手型光标 + tooltip + PointerReleased。
//
// 这里钉三件事:①链接渲染成 InlineUIContainer(不是 Span),孩子带着 URL 与可点的手势;
// ②点击走 `MdView.OpenLink` → `MdView.UrlOpener`(测试里换成收集器,避免真弹浏览器);
// ③非链接的行内元素(粗体/斜体/删除线/纯文本)没被这次改动带偏。
//
// 收集器要**递归**:链接可能嵌在 `Bold`/`Span` 里,也可能在列表项那种嵌套面板里
// (第一版只扫了段落顶层,于是「粗体里的链接」「列表里的链接」两条用例假红)。
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using TipCL.App.Pages.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class MdViewLinkTests
{
    /// <summary>控件树里的全部 TextBlock(段落、列表项、引用块都在不同层)。</summary>
    private static IEnumerable<TextBlock> TextBlocks(Control c)
    {
        if (c is TextBlock self)
        {
            yield return self;
        }
        foreach (Control child in ChildControls(c))
        {
            foreach (TextBlock t in TextBlocks(child))
            {
                yield return t;
            }
        }
    }

    private static IEnumerable<Control> ChildControls(Control c) => c switch
    {
        Panel p => p.Children.OfType<Control>(),
        ContentControl cc when cc.Content is Control inner => new[] { inner },
        Decorator d when d.Child is Control inner2 => new[] { inner2 },
        _ => Enumerable.Empty<Control>(),
    };

    /// <summary>行内树里的全部 Inline(含 Bold/Span 里的嵌套项)。</summary>
    private static IEnumerable<Inline> Flatten(Inline inline)
    {
        yield return inline;
        IEnumerable<Inline> children = inline switch
        {
            Span s when s.Inlines != null => s.Inlines,
            _ => Enumerable.Empty<Inline>(),
        };
        foreach (Inline child in children)
        {
            foreach (Inline x in Flatten(child))
            {
                yield return x;
            }
        }
    }

    private static List<Inline> Inlines(MdView view)
    {
        var all = new List<Inline>();
        foreach (TextBlock tb in TextBlocks(view))
        {
            foreach (Inline inline in tb.Inlines ?? Enumerable.Empty<Inline>())
            {
                all.AddRange(Flatten(inline));
            }
        }
        return all;
    }

    /// <summary>InlineUIContainer 里那个真 TextBlock 的可见文字(文字在 Inlines 的 Run 里,Text 属性是 null)。</summary>
    private static string InnerText(InlineUIContainer container)
        => string.Concat(Assert.IsType<TextBlock>(container.Child).Inlines!.OfType<Run>().Select(r => r.Text));

    [Fact]
    public void Link_RendersAsAClickableInlineContainer() => TestApp.Run(() =>
    {
        var view = new MdView();
        view.SetMarkdown("见 [文档](https://example.com/a?b=1) 这一句。");

        InlineUIContainer? container = Inlines(view).OfType<InlineUIContainer>().FirstOrDefault();
        Assert.NotNull(container);                       // 关键:不是 Span,而是能挂手势的宿主
        Assert.Equal("文档", InnerText(container!));      // 显示的是人读的文字,不是 URL
        Assert.Equal("https://example.com/a?b=1", ToolTip.GetTip(container!.Child));
        Assert.NotNull(container.Child!.Cursor);         // 手型光标(悬停能看出可点)
        Assert.NotNull(((TextBlock)container.Child).TextDecorations);   // 仍有下划线,视觉与参考版一致
        // 排版:宿主里的字号与段落一致(13.5),否则链接会明显忽大忽小
        Assert.Equal(13.5, ((TextBlock)container.Child).FontSize);
    });

    [Fact]
    public void Clicking_GoesThroughTheUrlOpener() => TestApp.Run(() =>
    {
        Action<string> before = MdView.UrlOpener;
        var seen = new List<string>();
        try
        {
            MdView.UrlOpener = seen.Add;
            MdView.OpenLink("https://example.com/x");
            MdView.OpenLink("   ");                       // 空 URL 不进打开器
            Assert.Equal(new[] { "https://example.com/x" }, seen);
        }
        finally
        {
            MdView.UrlOpener = before;                    // 别把替换留给别的用例
        }
    });

    [Fact]
    public void NonLinkInlines_AreUnchanged() => TestApp.Run(() =>
    {
        var view = new MdView();
        view.SetMarkdown("**粗体** 与 *斜体* 与 ~~删除~~ 与纯文本。");

        List<Inline> inlines = Inlines(view);
        Assert.Contains(inlines, i => i is Bold);
        Assert.Contains(inlines, i => i is Italic);
        Assert.Contains(inlines, i => i is Span s && s.TextDecorations == Avalonia.Media.TextDecorations.Strikethrough);
        Assert.DoesNotContain(inlines, i => i is InlineUIContainer);   // 没有链接就不该出现宿主控件
    });

    [Fact]
    public void LinkInsideBoldAndList_IsStillClickable() => TestApp.Run(() =>
    {
        var view = new MdView();
        view.SetMarkdown("**加粗里的 [链接](https://example.com/deep)**\n\n- 列表里的 [条目链接](https://example.com/list)");

        var urls = Inlines(view)
            .OfType<InlineUIContainer>()
            .Select(c => ToolTip.GetTip(c.Child) as string)
            .ToList();
        Assert.Contains("https://example.com/deep", urls);   // 粗体里的链接
        Assert.Contains("https://example.com/list", urls);   // 列表里的链接
    });
}
