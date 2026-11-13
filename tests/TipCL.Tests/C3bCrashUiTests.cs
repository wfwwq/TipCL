using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3b:<see cref="CrashUi"/>(崩溃 / 异常退出对话框)的移植验收。
///
/// 纪律:
/// <list type="bullet">
/// <item>每条碰 Avalonia 的用例体都走 <c>AvaloniaSession.Run(...)</c>(D-79 / C1b-fix);</item>
/// <item>驱动对话框一律"**先把对话框推出来、再点按钮**"(D-113),点按钮走 <c>RaiseEvent</c>,
///   **不模拟真实输入设备**;</item>
/// <item>需要等续体 / 后端往返时用 <see cref="C3bFiles.Pump"/>(主动泵 Dispatcher),**不阻塞等**续体(D-114);</item>
/// <item>全局静态全部由 <see cref="C3bScope"/> 快照还原(D-69);</item>
/// <item>后端走 C2a 的回环假后端(<c>TcpListener</c> 绑 127.0.0.1:0),不起真后端、不出网、不碰真文件选择器;</item>
/// <item>剪贴板走 <see cref="CrashUi.ClipboardWriter"/> 注入缝,用例断言"平台剪贴板一次都没被碰"
///   (<c>ClipboardPlatformAttempts</c> 增量恒为 0)。</item>
/// </list>
/// 每条断言都注明来源:【Windows 源】/【任务书】/【本机实测】。
/// </summary>
public class C3bCrashUiTests
{
    private static CrashReport Full()
    {
        return new CrashReport
        {
            Title = "标题(不该出现)",
            Headline = "游戏崩了",
            Summary = "Java 进程以非零码退出",
            Detail = "完整日志第一行\n第二行",
            Help = "试试换个 Java 版本",
            ExitCode = 1,
            ExitHint = "内存不足",
            TaskId = "task-1",
            DirectFile = "/tmp/crash.log",
        };
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:13-148</c>:把卡片推出来并把它找回来。</summary>
    private static (Task<bool> Task, DialogCard Card) Open(CrashReport report, bool gameError = false)
    {
        Task<bool> task = CrashUi.ShowAsync(report, gameError);
        // D-113:先把对话框推出来(UiHost.Flush),再谈点按钮。
        return (task, C2c2Files.Dialog());
    }

    /// <summary>拿到"动作 / 工具按钮"那一行(靠某一个 chip 往回找容器,不去猜控件树的结构)。</summary>
    private static WrapPanel ActionRow(DialogCard card)
    {
        Button chip = C3bFiles.ByText(card, I18n.L("复制全文"));
        return Assert.IsAssignableFrom<WrapPanel>(chip.FindLogicalAncestorOfType<WrapPanel>());
    }

    // ---------------------------------------------------------------- 卡片结构

    /// <summary>【Windows 源】<c>CrashUi.cs:144-148</c>:标题「启动出问题了」,宽度 760,两个选项顺序 = 关闭 / 重新启动。</summary>
    [Fact]
    public void Dialog_uses_the_windows_title_options_and_width()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            Assert.Equal(I18n.L(C3bExpected.CrashTitle), card.Title);
            Assert.Equal(C3bExpected.CrashDialogWidth, card.MaxWidth);

            SPanel footer = Assert.IsAssignableFrom<SPanel>(card.FooterRow);
            List<string> labels = footer.Children.OfType<Button>().Select(C3bFiles.TextOf).ToList();
            Assert.Equal(new[] { I18n.L("关闭"), I18n.L("重新启动") }, labels);
            // 【Windows 源】Dlg.Choose:最后一个选项是主按钮。
            Assert.Contains(StyleKeys.Map("Btn.Primary"), footer.Children.OfType<Button>().Last().Classes);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:148</c>:<c>== 1</c> —— 选第 1 项(重新启动)返回 true。</summary>
    [Fact]
    public void Choosing_restart_returns_true()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("重新启动")));
            C3bFiles.PumpTask(task);
            Assert.True(task.Result);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:144-148</c>:选第 0 项(关闭)返回 false。</summary>
    [Fact]
    public void Choosing_close_returns_false()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
            Assert.False(task.Result);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:16</c>:headline 优先于 title。</summary>
    [Fact]
    public void Headline_wins_over_title()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            Assert.NotNull(C3bFiles.FindText(card, "游戏崩了"));
            Assert.Null(C3bFiles.FindText(card, "标题(不该出现)"));
            // 【Windows 源】Ui.Txt(head, 15.0, bold: true).Wrap()
            TextBlock head = C3bFiles.Texts(card).First(t => t.Text == "游戏崩了");
            Assert.Equal(15.0, head.FontSize);
            // 【Windows 源 + 本机实测】Ui.Txt(bold: true) 用的是 SemiBold(=600,枚举别名 DemiBold),
            // 不是 Bold(700)—— 与 C1c 的 Weight("SemiBold") 同一档。
            Assert.Equal(FontWeight.SemiBold, head.FontWeight);
            Assert.Equal(TextWrapping.Wrap, head.TextWrapping);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:16</c>:headline 空(含纯空白)时退回 title。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Head_falls_back_to_title(string headline)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            CrashReport report = Full();
            report.Headline = headline;
            report.Title = "只有标题";
            (Task<bool> task, DialogCard card) = Open(report);

            Assert.NotNull(C3bFiles.FindText(card, "只有标题"));
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:17-20</c>:两个都空时用「游戏异常退出」。</summary>
    [Fact]
    public void Head_falls_back_to_the_localised_default()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            CrashReport report = new CrashReport();
            Assert.Equal(I18n.L(C3bExpected.CrashFallbackTitle), CrashUi.HeadOf(report));

            (Task<bool> task, DialogCard card) = Open(report);
            Assert.NotNull(C3bFiles.FindText(card, I18n.L(C3bExpected.CrashFallbackTitle)));
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:24-38</c>:Summary / Help 空(含纯空白)就不建那两行
    /// ——直接钉面板的子项个数(常态 6 行:标题 / 摘要 / 退出码 / 帮助 / 日志框 / 按钮行)。</summary>
    [Fact]
    public void Summary_and_help_lines_only_appear_when_present()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            SPanel body = Assert.IsAssignableFrom<SPanel>(card.BodyControl);
            Assert.Equal(6, body.Children.Count);
            Assert.NotNull(C3bFiles.FindText(card, "Java 进程以非零码退出"));
            Assert.NotNull(C3bFiles.FindText(card, "试试换个 Java 版本"));
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);

            CrashReport bare = new CrashReport { Headline = "只有标题" };
            (Task<bool> task2, DialogCard card2) = Open(bare);
            SPanel body2 = Assert.IsAssignableFrom<SPanel>(card2.BodyControl);
            Assert.Equal(2, body2.Children.Count);
            Assert.NotNull(C3bFiles.FindText(card2, "只有标题"));
            C3bFiles.Click(C3bFiles.ByText(card2, I18n.L("关闭")));
            C3bFiles.PumpTask(task2);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:28-34</c>:退出码行 <c>I18n.L("退出码 {0}{1}", code, hint)</c>,
    /// hint 为空时只留码;【Windows 源】<c>ExitCode</c> 为 null 时整行不建。</summary>
    [Fact]
    public void Exit_code_line_follows_the_windows_shape()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();

            (Task<bool> t1, DialogCard c1) = Open(Full());
            Assert.NotNull(C3bFiles.FindText(c1, I18n.L(C3bExpected.CrashExitFormat, 1, " · 内存不足")));
            C3bFiles.Click(C3bFiles.ByText(c1, I18n.L("关闭")));
            C3bFiles.PumpTask(t1);

            CrashReport noHint = Full();
            noHint.ExitHint = "  ";
            (Task<bool> t2, DialogCard c2) = Open(noHint);
            Assert.NotNull(C3bFiles.FindText(c2, I18n.L(C3bExpected.CrashExitFormat, 1, "")));
            C3bFiles.Click(C3bFiles.ByText(c2, I18n.L("关闭")));
            C3bFiles.PumpTask(t2);

            CrashReport noCode = Full();
            noCode.ExitCode = null;
            (Task<bool> t3, DialogCard c3) = Open(noCode);
            Assert.DoesNotContain(C3bFiles.Texts(c3), t => (t.Text ?? "").StartsWith("退出码", StringComparison.Ordinal));
            C3bFiles.Click(C3bFiles.ByText(c3, I18n.L("关闭")));
            C3bFiles.PumpTask(t3);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:39-49</c>:Detail 非空才建日志框,高 190、换行、带 <c>Input.Log</c> 样式;
    /// 空 Detail 时一个日志框都不该有。</summary>
    [Fact]
    public void Detail_becomes_a_wrapped_log_box()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();

            (Task<bool> t1, DialogCard c1) = Open(Full());
            TextBox log = Assert.Single(C3bFiles.Inputs(c1));
            Assert.Equal("完整日志第一行\n第二行", log.Text);
            Assert.Equal(C3bExpected.CrashLogHeight, log.Height);
            Assert.Equal(TextWrapping.Wrap, log.TextWrapping);
            // 【Windows 源】Style = Ui.S("Input.Log");Avalonia 侧是类名(C1b 的 StyleKeys)。
            Assert.Contains(StyleKeys.Map(C3bExpected.CrashLogStyle), log.Classes);
            C3bFiles.Click(C3bFiles.ByText(c1, I18n.L("关闭")));
            C3bFiles.PumpTask(t1);

            CrashReport noDetail = Full();
            noDetail.Detail = "";
            (Task<bool> t2, DialogCard c2) = Open(noDetail);
            Assert.Empty(C3bFiles.Inputs(c2));
            C3bFiles.Click(C3bFiles.ByText(c2, I18n.L("关闭")));
            C3bFiles.PumpTask(t2);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:22</c>:面板 <c>MinWidth = 520</c>。</summary>
    [Fact]
    public void Panel_min_width_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            SPanel body = Assert.IsAssignableFrom<SPanel>(card.BodyControl);
            Assert.Equal(C3bExpected.CrashPanelMinWidth, body.MinWidth);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    // ---------------------------------------------------------------- 动作按钮

    /// <summary>【Windows 源】<c>CrashUi.cs:52-77</c>:每个 action 一个 <c>BtnKind.Soft</c> 按钮,文案 = Label,顺序照列表。</summary>
    [Fact]
    public void Actions_become_soft_buttons_in_order()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            CrashReport report = Full();
            report.Actions = new List<CrashAction>
            {
                new CrashAction { Id = "a1", Label = "关掉光影" },
                new CrashAction { Id = "a2", Label = "降到 8 段渲染" },
            };
            (Task<bool> task, DialogCard card) = Open(report);

            List<Button> actions = ActionRow(card).Children.OfType<Button>().Take(2).ToList();
            Assert.Equal(new[] { "关掉光影", "降到 8 段渲染" }, actions.Select(C3bFiles.TextOf).ToArray());
            Assert.All(actions, b => Assert.Contains(StyleKeys.Map("Btn.Soft"), b.Classes));

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:52</c>:<c>report.Actions ?? new List&lt;CrashAction&gt;()</c> ——
    /// 为 null 时不抛,那一行里只剩 5 个工具按钮。</summary>
    [Fact]
    public void Null_actions_do_not_throw_and_leave_only_the_chips()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            CrashReport report = Full();
            report.Actions = null;
            (Task<bool> task, DialogCard card) = Open(report);

            WrapPanel row = ActionRow(card);
            Assert.Equal(C3bExpected.CrashChips.Length, row.Children.Count);
            Assert.Equal(
                C3bExpected.CrashChips.Select(I18n.L).ToArray(),
                row.Children.OfType<Button>().Select(C3bFiles.TextOf).ToArray());

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:89-143</c>:5 个工具按钮的文案与顺序,且都是 <c>BtnKind.Chip</c>。</summary>
    [Fact]
    public void Chips_are_the_five_windows_tools_in_order()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            WrapPanel row = ActionRow(card);
            List<Button> chips = row.Children.OfType<Button>().Skip(0).ToList();
            Assert.Equal(
                C3bExpected.CrashChips.Select(I18n.L).ToArray(),
                chips.Select(C3bFiles.TextOf).ToArray());
            Assert.All(chips, b => Assert.Contains(StyleKeys.Map("Btn.Chip"), b.Classes));

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:78-88</c>:「交给 AI 修复」只在 <c>gameError</c> 为 true 时存在,
    /// 且是主按钮 + 带提示。</summary>
    [Fact]
    public void Ai_fix_button_only_appears_for_game_errors()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();

            (Task<bool> t1, DialogCard c1) = Open(Full(), gameError: false);
            Assert.False(C3bFiles.HasButton(c1, I18n.L(C3bExpected.CrashAiFix)));
            C3bFiles.Click(C3bFiles.ByText(c1, I18n.L("关闭")));
            C3bFiles.PumpTask(t1);

            (Task<bool> t2, DialogCard c2) = Open(Full(), gameError: true);
            Button aiFix = C3bFiles.ByText(c2, I18n.L(C3bExpected.CrashAiFix));
            Assert.Contains(StyleKeys.Map("Btn.Primary"), aiFix.Classes);
            // 【Windows 源】button.ToolTip = ...;Avalonia 是挂载属性。
            Assert.Equal(I18n.L("把这份报错直接交给 AI 助手诊断和修复"), ToolTip.GetTip(aiFix));
            C3bFiles.Click(C3bFiles.ByText(c2, I18n.L("关闭")));
            C3bFiles.PumpTask(t2);
        });
    }

    /// <summary>【任务书 + 本机实测】<c>AiFixWindow</c>(22.6 KB 的整窗)不在本包范围:
    /// 挂点接上时点击**必须只走挂点**,拿到同一份 report。</summary>
    [Fact]
    public void Ai_fix_click_goes_through_the_injectable_launcher()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            CrashReport report = Full();
            List<CrashReport> launched = new List<CrashReport>();
            CrashUi.AiFixLauncher = r => launched.Add(r);

            (Task<bool> task, DialogCard card) = Open(report, gameError: true);
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L(C3bExpected.CrashAiFix)));

            Assert.Same(report, Assert.Single(launched));
            Assert.Empty(toasts.Items);
            // 对话框没被关掉(挂点只负责"开窗",不负责收卡片)。
            Assert.True(Dlg.AnyOpen);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【本机实测 / D-118】挂点没接时**如实提示**,不静默空操作(与 Windows 的直接开窗不同,已记档)。</summary>
    [Fact]
    public void Ai_fix_click_without_a_launcher_reports_instead_of_failing_silently()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            CrashUi.AiFixLauncher = null;

            (Task<bool> task, DialogCard card) = Open(Full(), gameError: true);
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L(C3bExpected.CrashAiFix)));

            Assert.Single(toasts.Items);
            Assert.Equal(I18n.L("暂未实现"), toasts.Items[0].Title);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    // ---------------------------------------------------------------- 复制全文

    /// <summary>【Windows 源】<c>CrashUi.cs:136</c>:三段之间是"一个换行 + 两个换行"。</summary>
    [Fact]
    public void Copy_text_has_the_windows_three_section_shape()
    {
        CrashReport report = new CrashReport { Summary = "摘要", Detail = "明细" };
        Assert.Equal("头\n摘要\n\n明细", CrashUi.CopyText("头", report));
        Assert.Equal("头\n\n\n", CrashUi.CopyText("头", new CrashReport()));
    }

    /// <summary>【任务书 + 本机实测】「复制全文」走注入缝:拿到与 Windows 逐字相同的文本、
    /// 弹「已复制」,而且**平台剪贴板一次都没被碰**(增量 0)。</summary>
    [Fact]
    public void Copy_uses_the_injectable_seam_and_toasts_copied()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            List<string> copied = new List<string>();
            CrashUi.ClipboardWriter = (owner, text) =>
            {
                Assert.NotNull(owner);
                copied.Add(text);
                return Task.FromResult(true);
            };
            int attempts = CrashUi.ClipboardPlatformAttempts;

            CrashReport report = Full();
            (Task<bool> task, DialogCard card) = Open(report);
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("复制全文")));
            // 对话框还开着,所以这里只能"推几轮",不能等对话框任务(见 D-120)。
            C3bFiles.Settle();

            Assert.Equal(new[] { CrashUi.CopyText("游戏崩了", report) }, copied);
            Assert.Equal(I18n.L("已复制"), Assert.Single(toasts.Items).Title);
            // 红线:注入缝接上时绝不碰平台剪贴板。
            Assert.Equal(attempts, CrashUi.ClipboardPlatformAttempts);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:132-142</c>:复制失败(返回 false)时不弹「已复制」——
    /// 与 Windows 的"先 SetText 成功才 Toast"等价。</summary>
    [Fact]
    public void Copy_does_not_toast_when_the_seam_reports_failure()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            CrashUi.ClipboardWriter = (owner, text) => Task.FromResult(false);

            (Task<bool> task, DialogCard card) = Open(Full());
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("复制全文")));
            C3bFiles.Settle();

            Assert.Empty(toasts.Items);
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:139-141</c> 的空 <c>catch</c>:剪贴板抛异常也不许崩、不许弹错。</summary>
    [Fact]
    public void Copy_swallows_a_throwing_seam()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            CrashUi.ClipboardWriter = (owner, text) => throw new InvalidOperationException("剪贴板被占用");

            (Task<bool> task, DialogCard card) = Open(Full());
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("复制全文")));
            C3bFiles.Settle();

            Assert.Empty(toasts.Items);
            Assert.True(Dlg.AnyOpen);
            Assert.False(task.IsFaulted);
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【本机实测 / D-118】离屏(<c>Dlg.SmokeActive</c>)不碰真剪贴板:没接缝时返回 false、不弹「已复制」,
    /// 且平台剪贴板路径一次都没被走到。</summary>
    [Fact]
    public void Copy_when_smoke_is_active_skips_the_platform_clipboard()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            CrashUi.ClipboardWriter = null;
            Dlg.SmokeActive = true;
            int attempts = CrashUi.ClipboardPlatformAttempts;

            (Task<bool> task, DialogCard card) = Open(Full());
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("复制全文")));
            C3bFiles.Settle();

            Assert.Empty(toasts.Items);
            Assert.Equal(attempts, CrashUi.ClipboardPlatformAttempts);
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    // ---------------------------------------------------------------- 走后端的那几个按钮

    /// <summary>【Windows 源】<c>CrashUi.cs:56-75</c>:点动作按钮 → <c>apply_crash_action</c>,成功后弹「已处理」,
    /// 且按钮在处理期间被禁用、结束后恢复。</summary>
    [Fact]
    public void Action_button_calls_apply_crash_action_and_toasts_done()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = new C3bBackend(C3bBackend.Rpc("{\"ok\":true,\"message\":\"已把光影关掉\"}"));

            CrashReport report = Full();
            report.Actions = new List<CrashAction> { new CrashAction { Id = "kill-shaders", Label = "关掉光影" } };
            (Task<bool> task, DialogCard card) = Open(report);

            Button action = C3bFiles.ByText(card, "关掉光影");
            C3bFiles.Click(action);
            backend.Need(1);
            C3bFiles.Pump(() => toasts.Items.Count > 0);

            Assert.Equal("apply_crash_action", C3bFiles.Method(backend.RequestAt(0)));
            Assert.Equal("kill-shaders", C3bFiles.Params(backend.RequestAt(0)).GetProperty("action").GetProperty("id").GetString());
            Assert.True(action.IsEnabled);
            (string Title, string Body, ToastKind Kind) toast = Assert.Single(toasts.Items);
            Assert.Equal(I18n.L("已处理"), toast.Title);
            Assert.Equal("已把光影关掉", toast.Body);
            Assert.Equal(ToastKind.Success, toast.Kind);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:68</c>:<c>ok</c> 不是 true 时弹 Warning 的「没能处理」。</summary>
    [Fact]
    public void Action_button_toasts_failure_when_the_backend_says_not_ok()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = new C3bBackend(C3bBackend.Rpc("{\"ok\":false,\"message\":\"版本不匹配\"}"));

            CrashReport report = Full();
            report.Actions = new List<CrashAction> { new CrashAction { Id = "a1", Label = "关掉光影" } };
            (Task<bool> task, DialogCard card) = Open(report);

            C3bFiles.Click(C3bFiles.ByText(card, "关掉光影"));
            backend.Need(1);
            C3bFiles.Pump(() => toasts.Items.Count > 0);

            (string Title, string Body, ToastKind Kind) toast = Assert.Single(toasts.Items);
            Assert.Equal(I18n.L("没能处理"), toast.Title);
            Assert.Equal("版本不匹配", toast.Body);
            Assert.Equal(ToastKind.Warning, toast.Kind);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:89-97</c>:「打开日志文件」→ <c>open_crash_file</c>
    /// (参数带 task_id 与 direct_file),弹「已打开」。</summary>
    [Fact]
    public void Open_log_button_calls_open_crash_file()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = new C3bBackend(C3bBackend.Rpc("\"/tmp/crash.log\""));

            (Task<bool> task, DialogCard card) = Open(Full());
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("打开日志文件")));
            backend.Need(1);
            C3bFiles.Pump(() => toasts.Items.Count > 0);

            Assert.Equal("open_crash_file", C3bFiles.Method(backend.RequestAt(0)));
            System.Text.Json.JsonElement parameters = C3bFiles.Params(backend.RequestAt(0));
            Assert.Equal("task-1", parameters.GetProperty("task_id").GetString());
            Assert.Equal("/tmp/crash.log", parameters.GetProperty("path").GetString());
            Assert.Equal(I18n.L("已打开"), Assert.Single(toasts.Items).Title);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:98-110</c>:「导出报告」先走 <c>Dlg.SaveFile</c>;
    /// 离屏 / 取消时返回 null → **一个请求都不发、一个 Toast 都不弹**。
    /// 顺带钉住"测试里绝不会弹真文件选择器":离屏那条路上系统对话框被记了一笔。</summary>
    [Fact]
    public void Export_button_cancels_through_the_smoke_seam_and_sends_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = new C3bBackend();
            List<string> cancelled = new List<string>();
            Dlg.SmokeActive = true;
            Dlg.SmokeAutoCancelSystemDialog = title => cancelled.Add(title);

            (Task<bool> task, DialogCard card) = Open(Full());
            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("导出报告")));
            C3bFiles.Settle();

            Assert.Equal(new[] { I18n.L("导出崩溃报告") }, cancelled);
            Assert.Empty(toasts.Items);
            Assert.Equal(0, backend.RequestsCount);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>CrashUi.cs:119-131</c>:「附说明反馈」弹**第二个**对话框(标题/按钮照源码);
    /// 取消它 → 不发 <c>submit_crash_feedback</c>,外层卡片还在。</summary>
    [Fact]
    public void Feedback_chip_opens_a_nested_dialog_and_cancel_sends_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = new C3bBackend();

            (Task<bool> task, DialogCard outer) = Open(Full());
            C3bFiles.Click(C3bFiles.ByText(outer, I18n.L("附说明反馈")));
            // 嵌套卡片是**第二张**:Dlg 把两张都挂在同一个宿主里。
            C3bFiles.Pump(() => C3bFiles.Descendants(Dlg.Host!).OfType<DialogCard>().Count() == 2);
            DialogCard inner = C3bFiles.Descendants(Dlg.Host!).OfType<DialogCard>().Last();

            Assert.Equal(I18n.L(C3bExpected.FeedbackTitle), inner.Title);
            Assert.True(C3bFiles.HasButton(inner, I18n.L(C3bExpected.FeedbackOk)));
            // 【Windows 源】Ui.Multi(..., 110.0) —— 那一段说明就是一个多行输入框。
            Assert.Equal(110.0, Assert.Single(C3bFiles.Inputs(inner)).Height);

            C3bFiles.Click(C3bFiles.ByText(inner, I18n.L("取消")));
            C3bFiles.Pump(() => C3bFiles.Descendants(Dlg.Host!).OfType<DialogCard>().Count() == 1);

            Assert.Empty(toasts.Items);
            Assert.Equal(0, backend.RequestsCount);
            Assert.True(Dlg.AnyOpen);

            C3bFiles.Click(C3bFiles.ByText(outer, I18n.L("关闭")));
            C3bFiles.PumpTask(task);
        });
    }

    // ---------------------------------------------------------------- 收摊

    /// <summary>【任务书 + 本机实测】对话框关掉之后:宿主空、遮罩落到终态、没有残留的可视件;
    /// <c>Motion.Enabled = false</c> 时 <c>Fade</c> / <c>PopOut</c> 是同步落终态的(所以这一刻就该干净)。</summary>
    [Fact]
    public void Closing_frees_the_layer_and_reaches_the_final_state_at_once()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            Motion.Enabled = false;

            (Task<bool> task, DialogCard card) = Open(Full());
            Grid wrap = Assert.IsAssignableFrom<Grid>(Assert.Single(env.Host.Children));
            Border mask = Assert.IsAssignableFrom<Border>(wrap.Children[0]);
            // 推出来时就是终态(Motion 关着):遮罩已经是不透明的 1.0。
            Assert.Equal(1.0, mask.Opacity);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("关闭")));
            C3bFiles.PumpTask(task);

            Assert.False(task.Result);
            Assert.False(Dlg.AnyOpen);
            Assert.Empty(env.Host.Children);
            Assert.False(env.Host.IsVisible);
            // 同步落终态:遮罩已经淡到 0,不需要再推任何时钟。
            Assert.Equal(0.0, mask.Opacity);
            // 再泵 20 轮也不该冒出新东西(没有残留计时器 / 订阅在往宿主里塞控件)。
            C3bFiles.Flush(20);
            Assert.Empty(env.Host.Children);
            Assert.Equal(0, Smoke.SmokeState.PendingOperations);
        });
    }

    /// <summary>【任务书】连点两次「关闭」是安全的:卡片已经摘掉了,第二次点击只让
    /// <c>TrySetResult</c> / <c>Close</c> 空转,不抛、不重复入树。</summary>
    [Fact]
    public void Clicking_close_twice_is_safe()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            (Task<bool> task, DialogCard card) = Open(Full());

            Button close = C3bFiles.ByText(card, I18n.L("关闭"));
            C3bFiles.Click(close);
            C3bFiles.PumpTask(task);
            C3bFiles.Click(close);
            C3bFiles.Click(close);
            C3bFiles.Flush(5);

            Assert.False(task.Result);
            Assert.Empty(env.Host.Children);
            Assert.False(Dlg.AnyOpen);
        });
    }
}
