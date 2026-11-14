// 来源: Windows 版参考仓库 src/TipCL/Pages/VersionSetupDialog.cs(M4-H P2 移植)。
// 版本设置对话框:隔离 / 独占内存 / JVM 参数 / GC / 直连服务器 / 启动前后命令 /
// 窗口标题与模式 / 绑定账号 / 统一通行证 / 列表隐藏。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Version;

/// <summary>版本设置对话框(照 Windows 版 VersionSetupDialog)。</summary>
public static class VersionSetupDialog
{
    private static readonly string[] IsoLabels = { "跟随全局", "关闭(共用实例目录)", "隔离存档", "隔离全部" };
    private static readonly string[] IsoValues = { "", "none", "saves", "all" };
    private static readonly string[] GcLabels = { "自动", "G1(通用)", "ZGC(大内存)", "Serial(老电脑)", "不指定" };
    private static readonly string[] GcValues = { "auto", "g1", "zgc", "serial", "none" };

    public static async Task ShowAsync(string instance, string version)
    {
        var api = InProcessApi.Instance;
        PyDict data = await api.TryCallAsync<PyDict>("get_version_settings", null, new { instance, version }) ?? new PyDict();
        var accounts = await api.TryCallAsync<List<string>>("get_accounts", null) ?? new List<string>();
        accounts.Insert(0, "(不绑定)");

        var iso = UiClass.Combo(IsoLabels, IsoLabels[Math.Max(0, Array.IndexOf(IsoValues, Py.S(data.Get("isolation"))))]);
        var mem = UiClass.Input("留空则用全局设置", Py.Truthy(data.Get("memory_mb")) ? Py.Str(data.Get("memory_mb")) : "");
        var jvm = UiClass.Input("-XX:+UseG1GC ...", Py.S(data.Get("jvm_args")));
        string gcVal = Py.S(data.Get("gc")) is { Length: > 0 } g ? g : "auto";
        var gc = UiClass.Combo(GcLabels, GcLabels[Math.Max(0, Array.IndexOf(GcValues, gcVal))]);
        var server = UiClass.Input("进游戏直连的服务器", Py.S(data.Get("server")));
        var port = UiClass.Input("25565", Py.S(data.Get("port")));
        var pre = UiClass.Input("启动前执行的命令", Py.S(data.Get("pre_launch")));
        var preWait = UiClass.Check("等待启动前命令结束", Py.Truthy(data.Get("pre_launch_wait")));
        var post = UiClass.Input("退出后执行的命令", Py.S(data.Get("post_launch")));
        var title = UiClass.Input("自定义游戏窗口标题", Py.S(data.Get("window_title")));
        string mode = Py.S(data.Get("window_mode"));
        var winMode = UiClass.Combo(new[] { "窗口", "全屏", "最大化" },
            mode == "fullscreen" ? "全屏" : mode == "maximized" ? "最大化" : "窗口");
        var acc = UiClass.Combo(accounts, Py.S(data.Get("login_account")) is { Length: > 0 } la ? la : "(不绑定)");
        var nide8 = UiClass.Input("统一通行证 ServerId(可空)", Py.S(data.Get("nide8_id")));
        var hidden = UiClass.Check("在版本列表中隐藏", Py.Truthy(data.Get("hidden")));

        var body = new ScrollViewer
        {
            Content = UiClass.V(4,
                UiClass.Field("版本隔离", iso, "存档 / 配置是否与实例其它版本分开"),
                UiClass.Field("独占内存", mem, "单位 MB"),
                UiClass.Field("JVM 参数", jvm),
                UiClass.Field("垃圾回收器", gc),
                UiClass.Field("直连服务器", server),
                UiClass.Field("端口", port),
                UiClass.Field("启动前命令", pre),
                UiClass.Field("", preWait),
                UiClass.Field("退出后命令", post),
                UiClass.Field("窗口标题", title),
                UiClass.Field("窗口模式", winMode),
                UiClass.Field("绑定账号", acc, "启动这个版本时强制用该账号"),
                UiClass.Field("统一通行证", nide8),
                UiClass.Field("", hidden)),
            MaxHeight = 380,
            MinWidth = 520,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        if (!await Dlg.Ask("版本设置 · " + version, body, "保存")) return;

        var payload = new Dictionary<string, object?>
        {
            ["isolation"] = IsoValues[Math.Max(0, iso.SelectedIndex)],
            ["jvm_args"] = jvm.Text ?? "",
            ["gc"] = GcValues[Math.Max(0, gc.SelectedIndex)],
            ["server"] = server.Text ?? "",
            ["port"] = port.Text ?? "",
            ["pre_launch"] = pre.Text ?? "",
            ["pre_launch_wait"] = preWait.IsChecked == true,
            ["post_launch"] = post.Text ?? "",
            ["window_title"] = title.Text ?? "",
            ["window_mode"] = winMode.SelectedIndex switch
            {
                1 => "fullscreen",
                2 => "maximized",
                _ => "window",
            },
            ["login_account"] = acc.SelectedIndex <= 0 ? "" : acc.SelectedItem as string ?? "",
            ["nide8_id"] = nide8.Text ?? "",
            ["hidden"] = hidden.IsChecked == true,
            ["memory_mb"] = int.TryParse(mem.Text?.Trim(), out int mb) && mb > 0 ? mb : null,
        };
        try
        {
            await api.CallAsync<object>("save_version_settings", new { instance, version, data = payload });
            ToastHost.Show("已保存", "版本设置已写入", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastHost.Show("保存失败", ex.Message, ToastKind.Error);
        }
    }

    /// <summary>隔离档位下拉的取值(供版本管理页直接改隔离时用)。</summary>
    public static bool IsolatedLabel(string label) => IsoLabels.Contains(label);
}
