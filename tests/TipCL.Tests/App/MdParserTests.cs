using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.App.Pages.Ai;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H P5 Markdown 解析链回归(从 Windows 版 Pages/Ai/MdParser.cs 原样搬运,这里锁住行为):
/// 块级(标题 / 列表 / 引用 / 代码 / 表格 / 分隔线)+ 行内(粗斜删码链接)+ 转义 + 纯文本化。
/// </summary>
public class MdParserTests
{
    [Fact]
    public void Headings_ParseLevelAndInlines()
    {
        var blocks = MdParser.Parse("# 一级\n## 二级 **粗**\n##### 五级");
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.Heading, b.Kind));
        Assert.Equal(new[] { 1, 2, 5 }, blocks.Select(b => b.Level));
        Assert.Equal("一级", MdParser.PlainText(blocks[0].Inlines));
        Assert.Equal("二级 粗", MdParser.PlainText(blocks[1].Inlines));
        Assert.Contains(blocks[1].Inlines, i => i.Kind == MdInlineKind.Bold && MdParser.PlainText(i.Children) == "粗");
    }

    [Fact]
    public void Paragraph_MergesLinesWithBreak()
    {
        var blocks = MdParser.Parse("第一行\n第二行\n\n另起一段");
        Assert.Equal(2, blocks.Count);
        Assert.Contains(blocks[0].Inlines, i => i.Kind == MdInlineKind.Break);
        Assert.Equal("第一行\n第二行", MdParser.PlainText(blocks[0].Inlines));
        Assert.Equal("另起一段", MdParser.PlainText(blocks[1].Inlines));
    }

    [Fact]
    public void BulletAndOrdered_TrackDepthAndStart()
    {
        var blocks = MdParser.Parse("- 甲\n  - 乙\n    - 丙\n\n3. 三\n4. 四");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(new[] { 0, 1, 2 }, blocks[0].ItemDepth);
        Assert.Equal(new[] { "甲", "乙", "丙" }, blocks[0].Items.Select(MdParser.PlainText));

        Assert.Equal(MdBlockKind.Ordered, blocks[1].Kind);
        Assert.Equal(3, blocks[1].Start);
        Assert.Equal(new[] { "三", "四" }, blocks[1].Items.Select(MdParser.PlainText));
    }

    [Fact]
    public void Quote_ParsesChildrenRecursively()
    {
        var blocks = MdParser.Parse("> 引用第一行\n> **加粗**\n\n之后");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Quote, blocks[0].Kind);
        Assert.Contains(blocks[0].Children, b => b.Kind == MdBlockKind.Paragraph);
        string text = string.Concat(blocks[0].Children.Select(b => MdParser.PlainText(b.Inlines)));
        Assert.Contains("引用第一行", text);
        Assert.Contains("加粗", text);
    }

    [Fact]
    public void CodeBlock_KeepsRawAndLang()
    {
        var blocks = MdParser.Parse("说明\n\n```csharp\nvar a = 1;\nvar b = 2;\n```\n\n结尾");
        Assert.Equal(3, blocks.Count);
        var code = blocks[1];
        Assert.Equal(MdBlockKind.Code, code.Kind);
        Assert.Equal("csharp", code.Lang);
        Assert.Equal("var a = 1;\nvar b = 2;", code.Raw);
        // ~~~ 同样识别
        Assert.Equal(MdBlockKind.Code, MdParser.Parse("~~~\nx\n~~~")[0].Kind);
    }

    [Fact]
    public void Table_ParsesHeaderAlignAndRows()
    {
        var blocks = MdParser.Parse("| 名称 | 数量 |\n| :--- | ---: |\n| 苹果 | 3 |\n| 梨 | 5 |");
        Assert.Single(blocks);
        var t = blocks[0];
        Assert.Equal(MdBlockKind.Table, t.Kind);
        Assert.Equal(2, t.Header.Count);
        Assert.Equal(new[] { "left", "right" }, t.Align);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("苹果", MdParser.PlainText(t.Rows[0][0]));
        Assert.Equal("5", MdParser.PlainText(t.Rows[1][1]));
        // 单元格不足时补齐、多余时截断
        var ragged = MdParser.Parse("| a | b | c |\n| - | - | - |\n| 1 |\n| 1 | 2 | 3 | 4 |")[0];
        Assert.All(ragged.Rows, r => Assert.Equal(3, r.Count));
    }

    [Fact]
    public void Rule_IsDetected()
    {
        Assert.Equal(MdBlockKind.Rule, MdParser.Parse("---")[0].Kind);
        Assert.Equal(MdBlockKind.Rule, MdParser.Parse("***")[0].Kind);
        Assert.Equal(MdBlockKind.Rule, MdParser.Parse("___")[0].Kind);
    }

    [Fact]
    public void Inline_ParsesBoldItalicStrikeCodeLink()
    {
        var inlines = MdParser.ParseInline("**粗** *斜* ~~删~~ `码` [文字](https://example.com)");
        var kinds = inlines.Select(i => i.Kind).ToList();
        Assert.Contains(MdInlineKind.Bold, kinds);
        Assert.Contains(MdInlineKind.Italic, kinds);
        Assert.Contains(MdInlineKind.Strike, kinds);
        Assert.Contains(MdInlineKind.Code, kinds);
        var link = inlines.First(i => i.Kind == MdInlineKind.Link);
        Assert.Equal("https://example.com", link.Url);
        Assert.Equal("文字", MdParser.PlainText(link.Children));

        // 代码里的强调符号不该被解析
        var code = MdParser.ParseInline("`**not bold**`");
        Assert.Single(code);
        Assert.Equal(MdInlineKind.Code, code[0].Kind);
        Assert.Equal("**not bold**", code[0].Text);

        // 转义
        Assert.Equal("**粗**", MdParser.PlainText(MdParser.ParseInline("\\*\\*粗\\*\\*")));
        // 相邻文本合并
        Assert.Single(MdParser.ParseInline("abc"));
    }

    [Fact]
    public void PlainText_FlattensEverything()
    {
        var blocks = MdParser.Parse("> 引用 **粗**\n\n- 项 `码`");
        string all = string.Concat(blocks.Select(b => b.Kind == MdBlockKind.Quote
            ? string.Concat(b.Children.Select(c => MdParser.PlainText(c.Inlines)))
            : string.Concat(b.Items.Select(MdParser.PlainText))));
        Assert.Contains("引用 粗", all);
        Assert.Contains("项 码", all);
    }

    [Fact]
    public void EmptyAndNull_ReturnNoBlocks()
    {
        Assert.Empty(MdParser.Parse(null));
        Assert.Empty(MdParser.Parse(""));
        Assert.Empty(MdParser.Parse("   \n\n  "));
    }

    /// <summary>渲染链必须能吃下真实 markdown 而不抛异常(Avalonia 控件构造在无头 UI 线程上)。</summary>
    [Fact]
    public void MdView_RendersSampleMarkdown() => TestApp.Run(() =>
    {
        var view = new MdView();
        const string sample = "# 标题\n\n正文 **粗体** 与 `代码` 与 [链接](https://example.com)。\n\n" +
                              "- 列表一\n  - 子项\n1. 有序\n\n> 引用\n\n```json\n{\"a\":1}\n```\n\n" +
                              "| A | B |\n| :- | -: |\n| 1 | 2 |\n\n---\n\n结尾";
        view.SetMarkdown(sample);
        Assert.Equal(sample, view.Markdown);
        Assert.NotNull(view.Content);
        // 再设一次(节流路径)不能炸
        view.SetMarkdown(sample + "\n下一段", live: true);
        Assert.EndsWith("下一段", view.Markdown);
    });
}
