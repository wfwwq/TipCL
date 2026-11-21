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
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Version;

/// <summary>版本设置对话框(照 Windows 版 VersionSetupDialog)。</summary>
public static class VersionSetupDialog
{
    // AI3-i18n:下拉文案改成每次现查词表的属性(以前是静态只读数组,类型初始化时就定死了语言,切语言后要重启才变)。
    // 对话框每次打开都重建下拉 ⇒ 切语言后下一次打开就是新语言;保存按 SelectedIndex 取值,与文案语言无关。
    private static string[] IsoLabels => new[] { I18nCore.Tr("跟随全局"), I18nCore.Tr("关闭(共用实例目录)"), I18nCore.Tr("隔离存档"), I18nCore.Tr("隔离全部") };
    private static readonly string[] IsoValues = { "", "none", "saves", "all" };
    // AI2-i18n-ux:第 0 项「跟随实例 / 全局」= 不在版本层设 GC(写空串)。以前未设置时下拉默认选「自动」,
    // 一保存就把 gc="auto" 写进版本设置,版本层优先于实例层 ⇒ tune_jvm / 实例设置里的 GC 从此失效。
    private static string[] GcLabels => new[] { I18nCore.Tr("跟随实例 / 全局"), I18nCore.Tr("自动"), I18nCore.Tr("G1(通用)"), I18nCore.Tr("ZGC(大内存)"), I18nCore.Tr("Serial(老电脑)"), I18nCore.Tr("不指定") };
    private static readonly string[] GcValues = { "", "auto", "g1", "zgc", "serial", "none" };

    /// <summary>「绑定账号」下拉第 0 项(不绑定);保存时按 SelectedIndex 判,不比文字。</summary>
    internal static string NoAccountLabel => I18nCore.Tr("(不绑定)");

    /// <summary>隔离下拉的取值与文案(测试用)。</summary>
    internal static (string[] Labels, string[] Values) IsoChoices => (IsoLabels, IsoValues);

    /// <summary>「独占内存」输入框的占位字(AI2-i18n-ux:按新的读取顺序改)。</summary>
    internal static string MemoryPlaceholder => I18nCore.Tr("留空则跟随实例 / 全局设置");

    /// <summary>「独占内存」的说明:留空时实际按 版本 → 实例 → 调用方(启动页) → 全局 → 4096 取(LaunchFlow.Prepare)。</summary>
    internal static string MemoryHint => I18nCore.Tr("单位 MB。留空时依次取:实例设置 → 启动页内存 → 全局默认 → 4096 MB");

    /// <summary>GC 下拉的取值与文案(测试用)。</summary>
    internal static (string[] Labels, string[] Values) GcChoices => (GcLabels, GcValues);

    public static async Task ShowAsync(string instance, string version)
    {
        var api = InProcessApi.Instance;
        PyDict data = await api.TryCallAsync<PyDict>("get_version_settings", null, new { instance, version }) ?? new PyDict();
        var accounts = await api.TryCallAsync<List<string>>("get_accounts", null) ?? new List<string>();
        accounts.Insert(0, NoAccountLabel);

        string[] isoLabels = IsoLabels;
        string[] gcLabels = GcLabels;
        var iso = UiClass.Combo(isoLabels, isoLabels[Math.Max(0, Array.IndexOf(IsoValues, Py.S(data.Get("isolation"))))]);
        var mem = UiClass.Input(MemoryPlaceholder, Py.Truthy(data.Get("memory_mb")) ? Py.Str(data.Get("memory_mb")) : "");
        var jvm = UiClass.Input("-XX:+UseG1GC ...", Py.S(data.Get("jvm_args")));
        string gcVal = Py.S(data.Get("gc"));
        var gc = UiClass.Combo(gcLabels, gcLabels[Math.Max(0, Array.IndexOf(GcValues, gcVal))]);
        var server = UiClass.Input(I18nCore.Tr("进游戏直连的服务器"), Py.S(data.Get("server")));
        var port = UiClass.Input("25565", Py.S(data.Get("port")));
        var pre = UiClass.Input(I18nCore.Tr("启动前执行的命令"), Py.S(data.Get("pre_launch")));
        var preWait = UiClass.Check(I18nCore.Tr("等待启动前命令结束"), Py.Truthy(data.Get("pre_launch_wait")));
        var post = UiClass.Input(I18nCore.Tr("退出后执行的命令"), Py.S(data.Get("post_launch")));
        var title = UiClass.Input(I18nCore.Tr("自定义游戏窗口标题"), Py.S(data.Get("window_title")));
        string mode = Py.S(data.Get("window_mode"));
        var winMode = UiClass.Combo(new[] { I18nCore.Tr("窗口"), I18nCore.Tr("全屏"), I18nCore.Tr("最大化") },
            mode == "fullscreen" ? I18nCore.Tr("全屏") : mode == "maximized" ? I18nCore.Tr("最大化") : I18nCore.Tr("窗口"));
        var acc = UiClass.Combo(accounts, Py.S(data.Get("login_account")) is { Length: > 0 } la ? la : NoAccountLabel);
        var nide8 = UiClass.Input(I18nCore.Tr("统一通行证 ServerId(可空)"), Py.S(data.Get("nide8_id")));
        var hidden = UiClass.Check(I18nCore.Tr("在版本列表中隐藏"), Py.Truthy(data.Get("hidden")));

        var body = new ScrollViewer
        {
            Content = UiClass.V(4,
                UiClass.Field(I18nCore.Tr("版本隔离"), iso, I18nCore.Tr("存档 / 配置是否与实例其它版本分开")),
                UiClass.Field(I18nCore.Tr("独占内存"), mem, MemoryHint),
                UiClass.Field(I18nCore.Tr("JVM 参数"), jvm),
                UiClass.Field(I18nCore.Tr("垃圾回收器"), gc),
                UiClass.Field(I18nCore.Tr("直连服务器"), server),
                UiClass.Field(I18nCore.Tr("端口"), port),
                UiClass.Field(I18nCore.Tr("启动前命令"), pre),
                UiClass.Field("", preWait),
                UiClass.Field(I18nCore.Tr("退出后命令"), post),
                UiClass.Field(I18nCore.Tr("窗口标题"), title),
                UiClass.Field(I18nCore.Tr("窗口模式"), winMode),
                UiClass.Field(I18nCore.Tr("绑定账号"), acc, I18nCore.Tr("启动这个版本时强制用该账号")),
                UiClass.Field(I18nCore.Tr("统一通行证"), nide8),
                UiClass.Field("", hidden)),
            MaxHeight = 380,
            MinWidth = 520,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        if (!await Dlg.Ask(I18nCore.Tr("版本设置 · ") + version, body, I18nCore.Tr("保存"))) return;

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
            ToastHost.Show(I18nCore.Tr("已保存"), I18nCore.Tr("版本设置已写入"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastHost.Show(I18nCore.Tr("保存失败"), ex.Message, ToastKind.Error);
        }
    }

    /// <summary>隔离档位下拉的取值(供版本管理页直接改隔离时用)。</summary>
    public static bool IsolatedLabel(string label) => IsoLabels.Contains(label);
}
