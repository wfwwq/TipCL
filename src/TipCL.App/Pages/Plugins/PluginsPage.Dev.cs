// ————— 开发者分段(本地安装 / 运行环境 / 供应商 / 日志) —————
// M4-H P6:从 PluginsPage.cs 拆出,与 Windows 版 Pages/Plugins/PluginsPage.Dev.cs 同名同分段。
//
// 与参考版的差别(见 docs/PARITY.md §7.1/§7.2):
// - 参考版的「运行环境」卡显示 Node 宿主是否已下载、页面 token、WebView2 状态;Linux 版不内置 Node 宿主、
//   不引 WebView,卡片如实写「没有插件运行时」,并把原因(两份契约各自的阻塞点)写清楚。
// - 参考版的「模型供应商」是给插件直接调模型的(PluginLlm);没有插件进程就没有消费者,
//   所以不提供「添加供应商」按钮(不做点了没用的入口),只列已存在的记录。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.App.Common;
using TipCL.App.Common.Plugins;
using TipCL.Core;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

public sealed partial class PluginsPage
{
    // ————— 开发者 —————

    private void BuildDev()
    {
        var zip = U.Btn("从 zip 安装", BtnKind.Soft, async delegate
        {
            string? path = await Dlg.PickFile("选择插件包", "*.zip");
            if (path != null) await InstallLocalAsync(path, dev: false);
        });
        var folder = U.Btn("从文件夹安装", BtnKind.Soft, async delegate
        {
            string? path = await Dlg.PickFolder("选择插件文件夹(里面有 tipcl-plugin.json)");
            if (path != null) await InstallLocalAsync(path, dev: false);
        });
        var link = U.Btn("链接开发文件夹", BtnKind.Primary, async delegate
        {
            string? path = await Dlg.PickFolder("选择插件开发文件夹(不复制,只记录路径)");
            if (path != null) await InstallLocalAsync(path, dev: true);
        });
        var local = U.Card(U.V(8,
            U.Section("本地安装", "zip / 文件夹会复制进 TipCL(先落暂存目录再原子替换);开发文件夹只链接,不复制"),
            U.H(8, zip, folder, link),
            U.Small("也可以直接把插件 zip 或文件夹拖进这个页面。")));

        var runtime = U.Card(U.V(6,
            U.Section("运行环境", "Linux 版没有内置插件运行时,插件代码不会被执行"),
            U.V(2,
                U.Field("Node 插件", U.Muted("没有 Node 宿主:不内置 Node / npm,也不引入 CEF / WebKitGTK"), null, 140),
                U.Field(".NET 插件", U.Muted("参考契约是 net48 的 TipCL 程序集 + WPF 界面类型,本进程加载不了"), null, 140),
                U.Field("网页界面", U.Muted("没有内置网页视图;插件给出的地址可以在浏览器里打开"), null, 140),
                U.Field("文件管理", U.Txt("可用:安装 / 更新 / 卸载 / 启停落盘 / 日志 / 数据目录", 13), null, 140)),
            U.Small("也就是说:插件文件能装能卸,但不会有插件代码在本进程里跑。参考版这里显示的是 Node 宿主、页面令牌与 WebView2 状态。")));

        var providers = U.Card(U.V(8,
            U.Section("模型供应商", "给插件直接调用模型用的 OpenAI 兼容端点(参考版由插件宿主消费)"),
            _providerHost,
            U.Small("没有插件运行时就没有消费者,所以这里不提供「添加供应商」入口;已存在的记录只做展示。")));

        _logBox.Height = 300;
        _logPick.SelectionChanged += delegate { _ = LoadLogAsync(); };
        var logRefresh = U.Btn("刷新", BtnKind.Chip, delegate { _ = LoadLogAsync(); }, RefreshGlyph);
        var logFolder = U.Btn("打开日志文件夹", BtnKind.Chip, delegate { OpenFolder(_dirs.LogsRoot); });
        var logs = U.Card(U.V(8,
            U.Section("插件日志", "安装、启停、失败原因都写在这里(plugin-logs/<id>.log)"),
            U.H(8, _logPick, logRefresh, logFolder),
            _logBox));
        _devView = new ScrollViewer
        {
            Content = U.V(10, local, runtime, providers, logs).Mk(15, 4, 15, 16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private async Task LoadDevAsync()
    {
        if (_installed.Count == 0) await LoadInstalledAsync();
        string keep = _logPick.SelectedIndex >= 0 && _logPick.SelectedIndex < _logIds.Count ? _logIds[_logPick.SelectedIndex] : "";
        _logIds.Clear();
        _logIds.AddRange(_installed.Select(p => p.Id));
        _logIds.Add("host");
        var names = _installed.Select(p => p.DisplayName).ToList();
        names.Add("宿主");
        await Dispatcher.UIThread.InvokeAsync(delegate
        {
            _logPick.Items.Clear();
            foreach (string n in names) _logPick.Items.Add(n);
            int idx = _logIds.IndexOf(keep);
            _logPick.SelectedIndex = idx >= 0 ? idx : (_logIds.Count > 0 ? 0 : -1);
        });
        _providerHost.Children.Clear();
        var providers = _store.Providers();
        if (providers.Count == 0)
        {
            _providerHost.Children.Add(U.Muted("没有额外供应商(默认的 tipcl 跟随 AI 设置)。"));
        }
        foreach (var p in providers)
        {
            _providerHost.Children.Add(U.RowCard(U.V(2,
                U.H(6, U.Txt(p.Name, 13, bold: true), U.Tag(p.Id)),
                U.Small(p.BaseUrl).Trim()), null, 10));
        }
        await LoadLogAsync();
    }

    private async Task LoadLogAsync()
    {
        int i = _logPick.SelectedIndex;
        if (i < 0 || i >= _logIds.Count)
        {
            _logBox.Text = "";
            return;
        }
        string id = _logIds[i];
        _logBox.Text = id == "host" ? "没有宿主进程,宿主日志不适用。每个插件的日志在各自那一项里。" : await ReadLogAsync(id);
    }

    private static async Task<string> ReadLogAsync(string id, int lines = 400)
    {
        var dirs = new PluginDirs();
        string file = dirs.LogFile(id);
        try
        {
            if (!File.Exists(file)) return "(日志是空的)";
            var all = await Task.Run(() => File.ReadLines(file).ToList());
            return string.Join("\n", all.Skip(Math.Max(0, all.Count - lines)));
        }
        catch (Exception ex)
        {
            return "读不到日志:" + ex.Message;
        }
    }

    private async Task ShowLogAsync(string id, string title)
    {
        var box = U.LogBox();
        box.Height = 380;
        box.MinWidth = 640;
        box.Text = await ReadLogAsync(id);
        var dlg = new Window
        {
            Title = $"{title} · 日志",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = true,
            Width = 860,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new Border { Padding = new Thickness(16), Child = box },
        };
        dlg.Show();
    }
}
