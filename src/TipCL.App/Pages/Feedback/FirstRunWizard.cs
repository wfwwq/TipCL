// 来源: Windows 版参考仓库 src/TipCL/Pages/FirstRunWizard.cs(M4-H P4 移植)。
// 首启向导:4 步(说明 / 游戏目录与下载源 / 默认内存与隔离 / 容易错过的功能),
// 「跳过向导」把 first_run 置 false 后直接关掉;走完则把目录、下载源、内存、隔离写进配置。
// 降级:Windows 的 Dlg.Layer 是自绘浮层,Linux 侧用 Dlg.Panel 的模态窗口(同一批控件与流程)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Feedback;

/// <summary>首次运行向导(照 Windows 版 FirstRunWizard)。</summary>
public static class FirstRunWizard
{
    private static readonly (string Title, string Body)[] Highlights =
    {
        ("把文件直接拖进窗口", "整合包、模组、资源包、光影、存档、皮肤、壁纸都认;认不准会问你一句,不用先找对页面。"),
        ("版本管理是排版的中心", "「版本管理」页能按来源分组看版本、收藏、复制、重命名、隐藏;双击直接启动。"),
        ("换个背景", "设置页能设静态壁纸、指一个文件夹轮播;模糊和遮罩也在那儿调。"),
        ("AI 助手在标题栏", "崩溃日志看不懂、模组冲突理不清,可以直接问它。"),
        ("这个向导随时能再看", "「设置 → 通用」里点「重新运行向导」,目录、下载源、内存、隔离会再问一遍。"),
    };

    private static readonly (string Key, string Label)[] SrcOpts =
    {
        ("auto", "自动(官方慢则 BMCLAPI)"),
        ("official", "仅官方"),
        ("bmclapi", "仅 BMCLAPI"),
    };

    private static readonly (string Key, string Label)[] IsoOpts =
    {
        ("all", "完全独立(推荐)"),
        ("mods", "隔离 Mod 与配置"),
        ("saves", "隔离存档"),
        ("none", "不隔离"),
    };

    private static readonly string[] Hints =
    {
        "先把游戏目录和下载源定下来。",
        "版本都会装在这个目录里;下载源决定从哪儿拉文件。",
        "这两项只影响以后新建的版本,已经装好的不受影响。",
        "最后几句,都是容易错过的地方。",
    };

    /// <summary>首启时按配置决定是否弹(照 Windows MaybeShowAsync)。</summary>
    public static async Task MaybeShowAsync()
    {
        bool shown = Py.Truthy(Config.CONFIG.Get("first_run", (object)false));
        if (shown) await ShowAsync();
    }

    public static async Task ShowAsync()
    {
        var api = InProcessApi.Instance;
        string curDir = Py.S(Config.CONFIG.Get("game_dir", Config.CONFIG.Get("root", "")));
        var gameDir = UiClass.Input("游戏目录", curDir);
        var browse = U.Btn("浏览...", BtnKind.Chip, null, "Folder");
        browse.Click += (_, _) => _ = BrowseAsync();
        var dirRow = UiClass.G(null, "*,Auto");
        dirRow.Add(gameDir, 0, 0);
        browse.Mk(8, 0, 0, 0).VCenter();
        dirRow.Add(browse, 0, 1);

        var src = UiClass.Combo(SrcOpts.Select(o => o.Label));
        string curSrc = Py.S(Config.CONFIG.Get("download_source", "auto"));
        src.SelectedIndex = Math.Max(0, Array.FindIndex(SrcOpts, o => o.Key == (curSrc.Length > 0 ? curSrc : "auto")));

        var mem = UiClass.Sld(512, 32768, Clamp(Py.Int(Config.CONFIG.Get("default_memory_mb", 4096)), 512, 32768), 256);
        var memLbl = UiClass.Txt($"{(int)mem.Value} MB", 12, bold: true);
        mem.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) memLbl.Text = $"{(int)mem.Value} MB";
        };
        var memRow = UiClass.G(null, "*,Auto");
        memRow.Add(mem.VCenter(), 0, 0);
        memLbl.Mk(10, 0, 0, 0).VCenter();
        memRow.Add(memLbl, 0, 1);

        var iso = UiClass.Combo(IsoOpts.Select(o => o.Label));
        string curIso = Py.S(Config.CONFIG.Get("default_isolation", "all"));
        iso.SelectedIndex = Math.Max(0, Array.FindIndex(IsoOpts, o => o.Key == (curIso.Length > 0 ? curIso : "all")));

        var pages = new Control[]
        {
            UiClass.V(6,
                UiClass.Txt("三步就能开始玩", 14, bold: true),
                UiClass.Muted("1. 选游戏目录和下载源"),
                UiClass.Muted("2. 定默认内存和版本隔离"),
                UiClass.Muted("3. 看一眼几个容易错过的功能"),
                UiClass.Small("这些以后都能在设置里改;不想现在弄就点「跳过向导」。").Wrap()),
            UiClass.V(8,
                UiClass.Txt("游戏目录(版本都装在这里)"), dirRow,
                UiClass.Txt("文件下载源"), src,
                UiClass.Small("国内网络建议保持「自动」:官方慢的时候会自己切到 BMCLAPI 镜像。").Wrap()),
            UiClass.V(8,
                UiClass.Txt("默认内存"), memRow,
                UiClass.Txt("新版本默认隔离"), iso,
                UiClass.Small("「完全独立」= 每个版本有自己的 mods / 配置 / 存档,换版本不会把上一个版本的模组拖进去。").Wrap()),
            BuildHighlights(),
        };

        var hint = UiClass.Muted("");
        hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var step = UiClass.Small("");
        var host = new Grid { MinWidth = 470, MinHeight = 250 };
        foreach (var p in pages)
        {
            p.IsVisible = false;
            host.Children.Add(p);
        }

        var back = UiClass.Btn("上一步");
        var next = UiClass.Btn("下一步", BtnKind.Primary);
        var skip = UiClass.Btn("跳过向导", BtnKind.Ghost);

        int index = 0;
        var done = new TaskCompletionSource<bool>();
        var footer = UiClass.G(null, "Auto,*,Auto,Auto");
        footer.Add(step.VCenter(), 0, 0);
        skip.Mk(0, 0, 8, 0).VCenter();
        footer.Add(skip, 0, 1);
        back.Mk(8, 0, 0, 0).VCenter();
        footer.Add(back, 0, 2);
        next.Mk(8, 0, 0, 0).VCenter();
        footer.Add(next, 0, 3);
        var body = UiClass.V(12, hint, host, UiClass.Sep(), footer);

        back.Click += (_, _) => Show(index - 1);
        next.Click += (_, _) =>
        {
            if (index < pages.Length - 1) Show(index + 1);
            else done.TrySetResult(true);
        };
        skip.Click += (_, _) => done.TrySetResult(false);
        Show(0);

        // 非模态面板:三个按钮自己决定结果,拿到结果就关窗(照 Windows Dlg.Panel + layer.Close)
        var handle = Dlg.OpenPanel("欢迎使用 TipCL", body, 560);
        bool finished;
        while (true)
        {
            var winner = await Task.WhenAny(done.Task, handle.Closed);
            if (winner == done.Task) break;
            // 用户直接关窗 = 跳过
            done.TrySetResult(false);
            break;
        }
        finished = done.Task.IsCompletedSuccessfully && done.Task.Result;
        handle.Close();

        if (!finished)
        {
            await api.TryCallAsync<object>("save_settings", null, new { data = new { first_run = false } });
            return;
        }

        string typed = gameDir.Text?.Trim() ?? "";
        if (typed.Length > 0 && typed != curDir)
        {
            try
            {
                await api.CallAsync<string>("set_game_dir", new { path = typed });
            }
            catch (Exception ex)
            {
                await Dlg.Alert("游戏目录没能设置", $"{typed}\n\n{ex.Message}\n\n已保留原来的目录,可到「设置」里重新选择。");
            }
        }
        await api.CallAsync<object>("save_settings", new
        {
            data = new Dictionary<string, object>
            {
                ["download_source"] = SrcOpts[Math.Max(0, src.SelectedIndex)].Key,
                ["default_memory_mb"] = (int)mem.Value,
                ["default_isolation"] = IsoOpts[Math.Max(0, iso.SelectedIndex)].Key,
                ["first_run"] = false,
            },
        });
        ToastHost.Show("向导已完成", "目录、下载源、内存、隔离已按你的选择写入", ToastKind.Success);

        void Show(int i)
        {
            index = Math.Clamp(i, 0, pages.Length - 1);
            for (int j = 0; j < pages.Length; j++) pages[j].IsVisible = j == index;
            hint.Text = Hints[index];
            step.Text = $"第 {index + 1} / {pages.Length} 步";
            back.IsEnabled = index > 0;
            next.Content = UiClass.Txt(index == pages.Length - 1 ? "开始使用" : "下一步");
            Motion.FadeIn(pages[index], 200, 8);
        }

        async Task BrowseAsync()
        {
            string? picked = await Dlg.PickFolder("选择游戏目录");
            if (picked != null) gameDir.Text = picked;
        }
    }

    private static Control BuildHighlights()
    {
        var col = UiClass.V(9);
        foreach (var (title, body) in Highlights)
        {
            col.Children.Add(UiClass.Txt(title, 13, bold: true));
            col.Children.Add(UiClass.Small(body).Wrap());
        }
        return col;
    }

    private static long Clamp(long v, long min, long max) => v < min ? min : v > max ? max : v;
}
