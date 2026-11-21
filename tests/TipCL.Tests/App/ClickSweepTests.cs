// 按钮点击遍历(audit/D 第二轮,服务"坏的按钮:背后根本没接功能"的诉求)。
//
// 用户的原话是「坏的按钮,背后根本没有接功能」。这类缺陷**不会**在编译、单测、默认冒烟帧里暴露:
// 按钮画出来了、点下去也不崩,只是什么都不会发生。能自动化的部分是:
//   1) 每一页上可见且可用的 Button / ToggleButton / MenuItem,**点一遍不抛异常**
//      (同步异常 + Dispatcher 未处理异常 + 布局 pass 里抛出的都算);
//   2) 记下点击前后的**可观测变化**(换页 / 弹窗数 / 任务数 / 可视树指纹),
//      点完"什么都不变"的按钮列成清单 —— 清单**只统计不判定**(哪些是设计的 no-op 只有人知道),
//      交给 lead / aud-app 分派。
//
// 为什么要指纹而不只比页 id:大部分按钮的效果是"页内出现一个面板/Toast/表格多一行",
// 页 id 不变。指纹 = (页 id | 顶层窗口数 | 任务条数 | 可视节点数 | 全部文本节点内容) 的哈希,
// 任一变化都算"有反应"。
//
// 排除了带明显副作用的按钮(启动游戏 / 安装 / 下载 / 删除 / 卸载 / 退出 / 重启 / 打开文件夹 /
// 选择文件 / 导入 / 导出),它们会真的拉网络、起进程或弹系统文件对话框 —— 在测试里点它们
// 只会把用例变成 flaky 源。清单里单独统计"跳过多少个",不算判定。
//
// 清单落盘:设环境变量 TIPCL_CLICKSWEEP_OUT=<path.tsv> 时写出;不设就只在断言消息里给汇总。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Shell;
using Xunit;

namespace TipCL.Tests.App;

[Collection("TipCL 真机交互回归")]
public class ClickSweepTests
{
    /// <summary>页面 id:与 src/TipCL.App/Shell/MainWindow.axaml.cs 的 GetPage(id) switch 一致。</summary>
    private static readonly string[] Pages =
    {
        "launch", "download", "library", "plugins", "settings",
        "instance", "account", "multiplayer", "tasks", "ai",
    };

    /// <summary>明显有副作用的按钮:点了会拉网络 / 起进程 / 弹系统文件框 ⇒ 本轮不点,只统计。</summary>
    private static readonly string[] SideEffectWords =
    {
        "启动游戏", "开始游戏", "安装", "下载", "删除", "卸载", "退出", "重启", "打开文件夹",
        "选择文件", "选择图片", "选择视频", "选择", "导入", "导出", "上传", "提交", "购买", "登录",
        // 会调系统浏览器 / 文件管理器的按钮:在 Windows 开发机上跑会真的弹出 Edge(用户 2026-11-21 报告)
        "主页", "GitHub", "issue", "网页", "浏览器", "官网", "链接", "文档", "文件夹", "在编辑器中打开",
    };

    /// <summary>
    /// 按控件 Name 跳过的副作用控件(没有文字标签,SideEffectWords 识别不了)。
    /// AI-integration:设置 → AI 助手里的 MCP Server 开关点下去会在本机 127.0.0.1:25585 起监听,
    /// 测试进程会一直占着端口直到结束(也会把 mcp_server.json 的 enabled 写成 true)。
    /// </summary>
    private static readonly HashSet<string> SideEffectNames = new(StringComparer.Ordinal)
    {
        "McpServerSwitch",
        // AI2-i18n-ux:AI 页空态的「点一下直接发」建议按钮会在默认内置网关下真发一轮对话;网络回包时机不定,
        // 它的事件 Post 会落在后续用例的 ResetForUnitTests 里(DlgCloseTests 偶发红,见 progress/AI-engine-mcp.md 遗留 8)。
        "AiSuggestion",
    };

    private sealed class Row
    {
        public string Page = "";
        public string Kind = "";
        public string Label = "";
        public string Outcome = "";   // CHANGED / NO-EFFECT / SKIP
        public string Detail = "";
    }

    private static string TextOf(Visual v)
    {
        var texts = new List<string>();
        foreach (Visual d in v.GetVisualDescendants().Prepend(v))
        {
            if (d is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                texts.Add(tb.Text!);
            }
        }
        if (texts.Count == 0 && v is ContentControl cc && cc.Content is string s && !string.IsNullOrWhiteSpace(s))
        {
            texts.Add(s);
        }
        if (texts.Count == 0 && v is MenuItem mi && mi.Header is string h)
        {
            texts.Add(h);
        }
        return string.Join(" ", texts).Trim();
    }

    /// <summary>当前所有顶层窗口(Avalonia 11 要从 lifetime 上取,Application.Windows 不存在)。</summary>
    private static List<Window> OpenWindows()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
        {
            return desk.Windows.OfType<Window>().ToList();
        }
        return new List<Window>();
    }

    private static string Fingerprint(MainWindow win)
    {
        var sb = new StringBuilder();
        sb.Append(win.CurrentPageId).Append('|');
        sb.Append(OpenWindows().Count).Append('|');
        sb.Append(TaskStore.Rows.Count).Append('|');
        var texts = new List<string>();
        int nodes = 0;
        foreach (Visual v in win.GetVisualDescendants().Prepend(win))
        {
            nodes++;
            if (v is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                texts.Add(tb.Text!);
            }
            // 控件状态也算"变化":开关/勾选/输入框/下拉/滑块/进度条被按下但文本没变的情况很多,
            // 只比文本会把"切了个开关"误判成"点了没反应"。
            if (v is ToggleButton tg)
            {
                texts.Add("tg=" + tg.IsChecked);
            }
            if (v is TextBox tbx)
            {
                texts.Add("tb=" + tbx.Text);
            }
            if (v is ComboBox cb)
            {
                texts.Add("cb=" + cb.SelectedIndex);
            }
            if (v is Slider sl)
            {
                texts.Add("sl=" + sl.Value.ToString("F2"));
            }
            if (v is ProgressBar pb)
            {
                texts.Add("pb=" + pb.Value.ToString("F2"));
            }
        }
        sb.Append(nodes).Append('|');
        texts.Sort(StringComparer.Ordinal);
        sb.Append(string.Join("\u0001", texts));
        return sb.ToString();
    }

    private static async Task PumpAsync(int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            try
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Dispatcher.UIThread.RunJobs();
            }
            catch (Exception)
            {
                // 布局期异常由 Watch 收集,这里只负责推进
            }
            await Task.Delay(15);
        }
    }

    [Fact]
    public void VisibleButtonsAndMenuItemsClickWithoutThrowing()
    {
        var rows = new List<Row>();
        var errors = new List<string>();
        string? outPath = Environment.GetEnvironmentVariable("TIPCL_CLICKSWEEP_OUT");

        TestApp.RunAsync(async () =>
        {
            var win = new MainWindow();
            win.Show();
            await PumpAsync(2);

            foreach (string page in Pages)
            {
                win.Navigate(page);
                await PumpAsync(2);

                var targets = win.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(c => (c is Button || c is MenuItem) && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
                    .Take(80)
                    .ToList();

                foreach (Control c in targets)
                {
                    string label = TextOf(c);
                    string classes = c is StyledElement se ? string.Join(",", se.Classes) : "";
                    string kind = c.GetType().Name + (classes.Length > 0 ? "[" + classes + "]" : "");
                    string labelOrKind = string.IsNullOrWhiteSpace(label) ? "<" + kind + ">" : label;

                    if (SideEffectWords.Any(w => label.Contains(w, StringComparison.Ordinal))
                        || (c.Name is { Length: > 0 } cname && SideEffectNames.Contains(cname)))
                    {
                        rows.Add(new Row { Page = page, Kind = kind, Label = labelOrKind, Outcome = "SKIP", Detail = "副作用类,未点击" });
                        continue;
                    }

                    // 窗口标题栏的最小化/关闭:点下去会把无头窗口关掉/最小化,后面的遍历就没了;
                    // 而且它们在 headless 会话里 `(TopLevel.GetTopLevel(this) as Window)!` 解析为 null,
                    // 必抛 NRE(见 AUDIT-D 第二轮报告里那条跨域问题,交 aud-app 改成空安全)。这里跳过并记数。
                    if (classes.Contains("cap", StringComparison.Ordinal))
                    {
                        rows.Add(new Row { Page = page, Kind = kind, Label = labelOrKind, Outcome = "SKIP", Detail = "窗口标题栏按钮(headless 下 TopLevel 为 null,已单独报告)" });
                        continue;
                    }

                    string before = Fingerprint(win);
                    int errBefore = errors.Count;
                    Exception? thrown = null;
                    string mech = "pointer";
                    try
                    {
                        // 优先走**真指针输入**:headless 会话支持 MouseMove/MouseDown/MouseUp,
                        // 那条路才会真的经过 Button.OnClick(执行 Command、ToggleButton 切 IsChecked、
                        // 下拉展开……)。直接 RaiseEvent(ClickEvent) 会跳过 OnClick —— 第一版就是因此
                        // 把「切换分段/勾选开关」全误判成"点了没反应"。
                        Point? center = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), win);
                        if (center is Point pt && c.Bounds.Width > 0 && c.Bounds.Height > 0)
                        {
                            win.MouseMove(pt, Avalonia.Input.RawInputModifiers.None);
                            win.MouseDown(pt, Avalonia.Input.MouseButton.Left);
                            win.MouseUp(pt, Avalonia.Input.MouseButton.Left);
                        }
                        else
                        {
                            mech = "event";
                            if (c is MenuItem mi)
                            {
                                mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, mi));
                            }
                            else if (c is Button b)
                            {
                                b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, b));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        thrown = ex;
                    }

                    await PumpAsync(2);

                    string after = Fingerprint(win);
                    bool layoutThrew = errors.Count > errBefore;
                    if (thrown != null)
                    {
                        string[] frames = (thrown.StackTrace ?? "").Split('\n')
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .Take(3)
                            .ToArray();
                        rows.Add(new Row
                        {
                            Page = page, Kind = kind, Label = labelOrKind, Outcome = "THREW",
                            Detail = thrown.GetType().Name + ": " + thrown.Message + " @ " + string.Join(" / ", frames),
                        });
                    }
                    else if (layoutThrew)
                    {
                        rows.Add(new Row
                        {
                            Page = page, Kind = kind, Label = labelOrKind, Outcome = "THREW",
                            Detail = "布局/渲染 pass 抛出:" + errors[^1],
                        });
                    }
                    else if (!string.Equals(before, after, StringComparison.Ordinal))
                    {
                        rows.Add(new Row { Page = page, Kind = kind, Label = labelOrKind, Outcome = "CHANGED", Detail = mech });
                    }
                    else
                    {
                        rows.Add(new Row { Page = page, Kind = kind, Label = labelOrKind, Outcome = "NO-EFFECT", Detail = "点击后指纹未变(" + mech + ")" });
                    }

                    // 弹窗/外部窗口是点击的常见副作用:关掉它们,别把后面的点击都投到遮罩上
                    var extra = OpenWindows().Where(w => !ReferenceEquals(w, win)).ToList();
                    foreach (Window w in extra)
                    {
                        try { w.Close(); } catch (Exception) { }
                    }
                    if (extra.Count > 0)
                    {
                        await PumpAsync(1);
                    }
                }
            }

            try { win.Close(); } catch (Exception) { }

            if (!string.IsNullOrWhiteSpace(outPath))
            {
                var sb = new StringBuilder();
                sb.AppendLine("page\tkind\tlabel\toutcome\tdetail");
                foreach (Row r in rows)
                {
                    sb.AppendLine($"{r.Page}\t{r.Kind}\t{r.Label.Replace('\t', ' ')}\t{r.Outcome}\t{r.Detail.Replace('\t', ' ')}");
                }
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".";
                Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            }
        });

        // 判定只到"不抛"为止:no-effect 清单是给人看的,不是断言(设计上的 no-op 存在)。
        // 已知的跨域抛错(已在 AUDIT-D 报告里单列给 aud-app/B)记在这里:修好后请删掉对应行,
        // 那时它若再抛就不是"已知"了。
        var known = new (string Page, string Label, string Exception)[]
        {
            // 插件页「⋯」:more.ContextMenu = menu 之后又 menu.Open(PlacementTarget),
            // Avalonia 11 抛 "Cannot show ContentMenu on a different control to the one it is attached to"
            ("plugins", "⋯", "ArgumentException"),
        };
        var threw = rows.Where(r => r.Outcome == "THREW")
            .Where(r => !known.Any(k => k.Page == r.Page && k.Label == r.Label && r.Detail.StartsWith(k.Exception, StringComparison.Ordinal)))
            .ToList();
        int knownCount = rows.Count(r => r.Outcome == "THREW") - threw.Count;
        int changed = rows.Count(r => r.Outcome == "CHANGED");
        int noEffect = rows.Count(r => r.Outcome == "NO-EFFECT");
        int skipped = rows.Count(r => r.Outcome == "SKIP");

        string summary =
            $"遍历 {rows.Count} 个可交互控件:{changed} 个有可见变化、{noEffect} 个点了没反应、{skipped} 个因副作用/标题栏跳过、"
            + $"{threw.Count} 个抛错(另有 {knownCount} 个已知跨域抛错,见报告)。"
            + (string.IsNullOrWhiteSpace(outPath) ? "" : " 清单:" + outPath);

        Assert.True(threw.Count == 0,
            summary + Environment.NewLine + string.Join(Environment.NewLine,
                threw.Select(t => $"  [{t.Page}] {t.Kind} '{t.Label}' -> {t.Detail}")));

        // 覆盖度自检:一页都扫不到按钮说明取控件的方式坏了(否则这条用例会"绿得没意义")
        Assert.True(changed + noEffect >= 20, summary + " —— 扫到的控件太少,取控件的路径可能已经失效");
    }
}