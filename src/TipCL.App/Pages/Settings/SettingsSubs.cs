// 设置页 · 左导航子视图（M2-B5）：账号 / Java / 服务器 / 游戏时长。
// 每个都是本页自持的 PageBase 实例（照 R3 §2.1：不与 MainWindow 共享，避免同一控件两个父级）。
// 数据全部走 PageBase.Api 的 snake_case 契约；M2 尚未搬的后端能力（账号/Java/服务器/时长
// 的读写）TryCallAsync 回默认值，显示空态与「后端 M3 接入」提示，接口已按 Windows 版键名对齐。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Shell;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Settings;

/// <summary>子视图公共骨架：标题 + 说明 + 滚动主体，数据装载走 LoadAsync。</summary>
// [R32-LINMORE] 加 partial:区块宿主接线(UseBlockHost)单独放 SettingsSubView.Layout.cs,
// 免得在这个 640 行文件里做大段插入。
internal abstract partial class SettingsSubView : PageBase
{
    protected SettingsSubView(string id, string title, string sub) : base(id, title)
    {
        Header = UiClass.Section(title, sub);
        _stack = UiClass.V(10);
        _stack.Children.Add(Header);
        var scroll = new ScrollViewer
        {
            Content = _stack,
            Padding = new Avalonia.Thickness(0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.Margin = new Avalonia.Thickness(18, 6, 18, 16);
        Content = scroll;
    }

    protected readonly StackPanel _stack;

    /// <summary>标题行(两列 "*,Auto");子视图可往右列塞自己的操作区,避免再插一个重复标题。</summary>
    protected Grid Header { get; }

    /// <summary>把行包装进统一间距的纵向容器（B5 收尾补齐）。</summary>
    protected static Avalonia.Controls.Control RowsSub(params Avalonia.Controls.Control[] rows)
    {
        var sp = new StackPanel { Spacing = 12 };
        foreach (var c in rows) sp.Children.Add(c);
        return sp;
    }

    /// <summary>Api.TryCallAsync 的异常转 Toast 包装。</summary>
    protected static async void Run(Func<Task> work, string? failTitle = null)
    {
        try { await work(); }
        catch (Exception ex) { ToastHost.Show(failTitle ?? I18nCore.Tr("操作失败"), ex.Message, ToastKind.Error); }
    }

    protected override void OnActivate() => Run(LoadCoreAsync, I18nCore.Tr("加载失败"));

    protected abstract Task LoadCoreAsync();

    /// <summary>get_instances 已在 IApi 实现：返回实例名列表。</summary>
    protected static async Task<List<string>> InstanceNamesAsync()
    {
        var rows = await Api.TryCallAsync<List<object>>("get_instances", new List<object>());
        return rows?.Select(r => Py.S(Py.D(r).Get("name"))).Where(n => n.Length > 0).ToList() ?? new List<string>();
    }
}

// ───────────────────────────── 账号 ─────────────────────────────

internal sealed class AccountView : SettingsSubView
{
    private sealed class AccountRow
    {
        public string Name = "";
        public string Type = "";
        public string Uuid = "";
        public bool Active;
    }

    private readonly StackPanel _list = UiClass.V(10);
    private readonly TextBlock _count = UiClass.Muted("");

    public AccountView() : base("settings:account", I18nCore.Tr("账号"), I18nCore.Tr("启动游戏时使用标记为「当前」的账号"))
    {
        var addOffline = UiClass.Btn(I18nCore.Tr("添加离线账号"), BtnKind.Primary, (_, _) => Run(AddOfflineAsync, I18nCore.Tr("添加失败")));
        // [audit/B] 这两个入口原来是假按钮(只弹「登录后端 M3 接入，暂不可用」),而能力其实已经有了:
        // IApi 的 start_microsoft_login / start_authlib_login / start_nide8_login 齐全,「账号」页也把
        // 设备码弹窗、皮肤站表单、统一通行证表单全实现了。这里不重复实现一遍,直接把用户送到那个真入口。
        var addMs = UiClass.Btn(I18nCore.Tr("微软登录"), BtnKind.Soft, (_, _) => OpenAccountPage());
        ToolTip.SetTip(addMs, I18nCore.Tr("在「账号」页用微软账号登录(设备码)"));
        var addAuthlib = UiClass.Btn(I18nCore.Tr("皮肤站登录"), BtnKind.Chip, (_, _) => OpenAccountPage());
        ToolTip.SetTip(addAuthlib, I18nCore.Tr("在「账号」页用皮肤站(Yggdrasil)登录"));
        _stack.Children.Add(UiClass.Card(RowsSub(UiClass.Field("", UiClass.H(8, _count.VCenter(), addAuthlib, addMs, addOffline)))));
        _stack.Children.Add(_list);
    }

    /// <summary>跳到「账号」页 —— 登录流程(设备码弹窗 / 皮肤站 / 统一通行证)的唯一实现处。</summary>
    private void OpenAccountPage()
    {
        if (TopLevel.GetTopLevel(this) is MainWindow win)
        {
            win.Navigate("account");
            return;
        }
        Toast(I18nCore.Tr("账号"), I18nCore.Tr("在「账号」页用微软 / 皮肤站登录"), ToastKind.Info);
    }

    protected override async Task LoadCoreAsync()
    {
        var rows = await Api.TryCallAsync<List<object>>("get_account_rows", new List<object>());
        var accounts = rows?.Select(r =>
        {
            var d = Py.D(r);
            return new AccountRow
            {
                Name = Py.S(d.Get("name")),
                Type = Py.S(d.Get("type")),
                Uuid = Py.S(d.Get("uuid")),
                Active = Py.Truthy(d.Get("active")),
            };
        }).ToList() ?? new List<AccountRow>();
        _list.Children.Clear();
        _count.Text = $"共 {accounts.Count} 个";
        if (accounts.Count == 0)
        {
            _list.Children.Add(UiClass.Card(UiClass.Empty("?", I18nCore.Tr("还没有账号"), I18nCore.Tr("添加离线账号先玩起来；微软 / 皮肤站登录在「账号」页")), 18));
            return;
        }
        foreach (var acc in accounts)
        {
            string typeLabel = acc.Type switch
            {
                "microsoft" => I18nCore.Tr("微软"),
                "offline" => I18nCore.Tr("离线"),
                "authlib" => I18nCore.Tr("皮肤站"),
                "nide8" => I18nCore.Tr("统一通行证"),
                _ => string.IsNullOrEmpty(acc.Type) ? I18nCore.Tr("其他") : acc.Type,
            };
            var row = UiClass.G(null, "Auto,*,Auto");
            row.Add(UiClass.Pane(UiClass.Txt(string.IsNullOrEmpty(acc.Name) ? "?" : acc.Name[..1].ToUpperInvariant(), 16, bold: true, "B.OnAccentStrong")
                .Center(), "B.Accent", 20, 10), 0);
            var head = UiClass.H(6, UiClass.Txt(acc.Name, 13.5, bold: true).VCenter());
            if (acc.Active) head.Children.Add(UiClass.Tag(I18nCore.Tr("当前"), "B.OnAccentStrong", "B.AccentStrong"));
            head.Children.Add(UiClass.Tag(typeLabel));
            row.Add(UiClass.V(3, head, UiClass.Small(string.IsNullOrEmpty(acc.Uuid) ? typeLabel + I18nCore.Tr("账号") : "UUID " + acc.Uuid)).VCenter(), 0, 1);
            var ops = UiClass.H(6);
            if (!acc.Active)
                ops.Children.Add(UiClass.Btn(I18nCore.Tr("设为当前"), BtnKind.Soft, (_, _) => Run(async () =>
                {
                    await Api.CallAsync<object?>("set_active_account", new { name = acc.Name });
                    Toast(I18nCore.Tr("已切换"), $"当前账号：{acc.Name}", ToastKind.Success);
                    await LoadCoreAsync();
                }, I18nCore.Tr("切换失败"))));
            ops.Children.Add(UiClass.Btn(I18nCore.Tr("删除"), BtnKind.Ghost, (_, _) => Run(async () =>
            {
                if (await Dlg.Confirm(I18nCore.Tr("删除账号"), $"将删除「{acc.Name}」。", I18nCore.Tr("删除"), I18nCore.Tr("取消"), danger: true))
                {
                    await Api.CallAsync<object?>("remove_account", new { name = acc.Name });
                    await LoadCoreAsync();
                }
            }, I18nCore.Tr("删除失败"))));
            row.Add(ops.VCenter(), 0, 2);
            _list.Children.Add(UiClass.RowCard(row, padding: 13));
        }
    }

    private async Task AddOfflineAsync()
    {
        var name = UiClass.Input(I18nCore.Tr("例如 Steve"));
        if (!await Dlg.Ask(I18nCore.Tr("添加离线账号"), UiClass.V(8, UiClass.Muted(I18nCore.Tr("离线模式，无正版验证")), name), I18nCore.Tr("添加"))) return;
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            Toast(I18nCore.Tr("缺少名字"), I18nCore.Tr("请填写离线角色名"), ToastKind.Warning);
            return;
        }
        await Api.CallAsync<object?>("add_offline_account", new { username = name.Text.Trim(), skin = "default" });
        Toast(I18nCore.Tr("已添加"), name.Text.Trim(), ToastKind.Success);
        await LoadCoreAsync();
    }
}

// ───────────────────────────── Java ─────────────────────────────

/// <summary>
/// Java 子页(照 Windows 版 Pages/JavaPage.cs 排版:说明 / 安装方式 / 实例默认 / 已检测列表)。
/// Windows 该页是独立 PageBase,由设置页内层导航第 3 项挂进去;Linux 侧同样挂在设置页内层导航里。
/// </summary>
internal sealed class JavaView : SettingsSubView
{
    private static readonly (string Major, string Note)[] Majors =
    {
        ("8", I18nCore.Tr("1.16 及以下旧版本")),
        ("11", I18nCore.Tr("部分旧模组环境")),
        ("17", I18nCore.Tr("1.18 – 1.20.4 推荐")),
        ("21", "1.20.5 – 1.21.11"),
        ("25", I18nCore.Tr("26.1+ 新版本")),
    };

    private readonly StackPanel _list = UiClass.V(8);
    private readonly TextBlock _count = UiClass.Muted("");
    private readonly ComboBox _inst = UiClass.Combo(null, null, 170);
    private readonly ComboBox _java = UiClass.Combo();
    private readonly ComboBox _vendor = UiClass.Combo(null, null, 170);
    private readonly TextBlock _instJava = UiClass.Small("");
    private readonly TextBox _manual = UiClass.Input("手动填 java 路径(可空)");

    private List<string> _instances = new();
    private List<(string Label, string Value)> _javaOpts = new();
    private List<string> _vendors = new();
    private bool _sync;

    public JavaView() : base("settings:java", "Java", "游戏按版本挑 Java:26.1+ 要 25,1.20.5+ 要 21,1.18+ 要 17,老版本用 8")
    {
        var scan = U.Btn(I18nCore.Tr("扫描系统 Java"), BtnKind.Soft, (_, _) => Run(() => ReloadListAsync(scan: true), I18nCore.Tr("扫描失败")), "Search");
        var refresh = U.IconBtn("Refresh", I18nCore.Tr("刷新"), (_, _) => Run(LoadCoreAsync, I18nCore.Tr("加载失败")));
        // 标题与副标题由 SettingsSubView 基类给出,这里只往右列补计数 / 刷新 / 扫描(Windows JavaPage 的头部操作区)
        var headOps = UiClass.H(8, _count.VCenter(), refresh, scan);
        headOps.VerticalAlignment = VerticalAlignment.Center;
        Header.Add(headOps, 0, 1);

        // ── 安装方式:发行版 + 5 张下载卡 ──
        var cards = new WrapPanel();
        foreach (var (major, note) in Majors)
        {
            string m = major;
            var dl = U.Btn(I18nCore.Tr("下载"), BtnKind.Chip, null, "Download");
            dl.Click += (_, _) => Run(() => DownloadAsync(m), I18nCore.Tr("下载失败"));
            dl.HorizontalAlignment = HorizontalAlignment.Stretch;
            var card = UiClass.Card(UiClass.V(6, UiClass.Txt("Java " + m, 15, bold: true), UiClass.Small(note).Wrap(), dl), 13);
            card.Width = 158;
            card.Margin = new Thickness(0, 0, 10, 10);
            Motion.HoverLift(card, 1.01);
            cards.Children.Add(card);
        }
        var vendorBar = UiClass.G(null, "*,Auto,Auto");
        vendorBar.Add(UiClass.Txt(I18nCore.Tr("下载新运行时"), 13, bold: true).VCenter(), 0, 0);
        var vendorLbl = UiClass.Txt(I18nCore.Tr("发行版"), 12, fg: "B.InkMuted");
        vendorLbl.Mk(0, 0, 8, 0).VCenter();
        vendorBar.Add(vendorLbl, 0, 1);
        vendorBar.Add(_vendor.VCenter(), 0, 2);
        var installCard = UiClass.Card(UiClass.V(10, vendorBar, cards), 13);
        _stack.Children.Add(installCard);

        // ── 实例默认 ──
        _inst.SelectionChanged += (_, _) => { if (!_sync) Run(ReloadJavaOptionsAsync, I18nCore.Tr("读取失败")); };
        _java.SelectionChanged += (_, _) => { if (!_sync) Run(SaveJavaAsync, I18nCore.Tr("保存失败")); };
        var manualRow = UiClass.G(null, "*,Auto");
        manualRow.Add(_manual, 0, 0);
        _manual.Mk(0, 0, 0, 0);
        var useManual = U.Btn(I18nCore.Tr("用这个路径"), BtnKind.Chip, (_, _) => Run(ApplyManualAsync, I18nCore.Tr("保存失败")), "Check");
        useManual.Mk(8, 0, 0, 0).VCenter();
        manualRow.Add(useManual, 0, 1);
        var defaultsCard = UiClass.Card(UiClass.V(4,
            UiClass.Field(I18nCore.Tr("实例"), _inst),
            UiClass.Field(I18nCore.Tr("默认 Java"), _java, I18nCore.Tr("「自动选择」由启动器按游戏版本匹配")),
            UiClass.Field(I18nCore.Tr("自定义路径"), manualRow, "填绝对路径后点右边按钮,会先做一次规范化"),
            UiClass.Field("", _instJava)), 13);
        _stack.Children.Add(defaultsCard);

        // ── 已检测列表 ──
        _stack.Children.Add(_list);
        // [R32-LINMORE] 区块宿主(§2.1-5):intro(说明 = 标题行,Windows JavaPage 的首块就是它自己的
        // Ui.Section 标题,不是另造的说明卡)/ install(安装方式)/ defaults(实例默认)/ list(已检测列表)。
        UseBlockHost("java", "intro", "Java",
            ("install", I18nCore.Tr("安装方式"), installCard),
            ("defaults", I18nCore.Tr("实例默认"), defaultsCard),
            ("list", I18nCore.Tr("已检测列表"), _list));
    }

    protected override async Task LoadCoreAsync()
    {
        var rows = await Api.TryCallAsync<List<object>>("get_instances", new List<object>()) ?? new();
        _instances = rows.Select(r => Py.S(Py.D(r).Get("name"))).Where(n => n.Length > 0).ToList();
        _sync = true;
        _inst.Items.Clear();
        foreach (string n in _instances) _inst.Items.Add(n);
        if (_instances.Count > 0) _inst.SelectedIndex = 0;
        _sync = false;

        await ReloadVendorsAsync();
        await ReloadJavaOptionsAsync();
        await ReloadListAsync(scan: false);
    }

    private async Task ReloadVendorsAsync()
    {
        _vendors = await Api.TryCallAsync<List<string>>("java_vendor_list", null) ?? new();
        if (_vendors.Count == 0) _vendors = new List<string> { "adoptium" };
        var labels = new List<string>();
        foreach (string v in _vendors)
            labels.Add(await Api.TryCallAsync<string>("java_vendor_label", v, new { vendor = v }) ?? v);
        _sync = true;
        _vendor.Items.Clear();
        foreach (string l in labels) _vendor.Items.Add(l);
        int idx = _vendors.IndexOf("adoptium");
        _vendor.SelectedIndex = idx >= 0 ? idx : 0;
        _sync = false;
    }

    private string SelectedVendor()
    {
        int i = _vendor.SelectedIndex;
        return i < 0 || i >= _vendors.Count ? "adoptium" : _vendors[i];
    }

    private async Task ReloadJavaOptionsAsync()
    {
        string inst = _inst.SelectedItem as string ?? "";
        if (inst.Length == 0) return;
        var rawOpts = await Api.TryCallAsync<List<object?>>("java_combo_options", null, new { instance = inst, scan_system = false }) ?? new();
        var opts = rawOpts.OfType<PyDict>().ToList();
        if (opts.Count == 0) return;
        string keep = await Api.TryCallAsync<string>("java_combo_label_for", I18nCore.Tr("自动选择"), new { instance = inst, options = opts }) ?? I18nCore.Tr("自动选择");
        _sync = true;
        _javaOpts = opts.Select(o => (Py.S(o.Get("label")), Py.S(o.Get("value")))).ToList();
        _java.Items.Clear();
        foreach (var (label, _) in _javaOpts) _java.Items.Add(label);
        int i = _javaOpts.FindIndex(o => o.Label == keep);
        _java.SelectedIndex = i >= 0 ? i : 0;
        _sync = false;
        await ShowInstanceJavaAsync(inst);
    }

    private async Task ShowInstanceJavaAsync(string inst)
    {
        string stored = await Api.TryCallAsync<string>("get_instance_java", "", new { name = inst }) ?? "";
        string label = await Api.TryCallAsync<string>("instance_java_label", "", new { name = inst }) ?? "";
        _instJava.Text = stored.Length == 0
            ? ""
            : "当前记录:" + label + (stored == label ? "" : $"({stored})");
    }

    private async Task SaveJavaAsync()
    {
        string inst = _inst.SelectedItem as string ?? "";
        if (inst.Length == 0) return;
        string label = _java.SelectedItem as string ?? "";
        string java = _javaOpts.FirstOrDefault(o => o.Label == label).Value;
        if (string.IsNullOrEmpty(java)) java = label.Length == 0 ? I18nCore.Tr("自动选择") : label;
        await Api.CallAsync<object>("set_instance_java", new { name = inst, java });
        await ShowInstanceJavaAsync(inst);
        Toast(I18nCore.Tr("已保存"), $"「{inst}」默认 Java:{label}", ToastKind.Success);
    }

    private async Task ApplyManualAsync()
    {
        string inst = _inst.SelectedItem as string ?? "";
        string raw = _manual.Text?.Trim() ?? "";
        if (inst.Length == 0 || raw.Length == 0)
        {
            Toast(I18nCore.Tr("先填路径"), "填一个 java 可执行文件的绝对路径", ToastKind.Warning);
            return;
        }
        string normalized = await Api.CallAsync<string>("normalize_java_pref", new { java = raw }) ?? raw;
        if (normalized != raw) Toast(I18nCore.Tr("已规范化"), raw + "\n→ " + normalized);
        await Api.CallAsync<object>("set_instance_java", new { name = inst, java = normalized });
        _manual.Text = "";
        await ReloadJavaOptionsAsync();
        Toast(I18nCore.Tr("已保存"), $"「{inst}」默认 Java:{normalized}", ToastKind.Success);
    }

    private async Task ReloadListAsync(bool scan)
    {
        var rawRows = await Api.TryCallAsync<List<object?>>("get_java_list", null, new { scan_system = scan }) ?? new();
        var rows = rawRows.OfType<PyDict>().ToList();
        _list.Children.Clear();
        _count.Text = $"共 {rows.Count} 个";
        if (rows.Count == 0)
        {
            _list.Children.Add(UiClass.Card(UiClass.Empty("Box", I18nCore.Tr("没有找到 Java"), "点上面「下载」装一个,或扫描系统已有安装"), 18));
            return;
        }
        var cards = new List<Control>();
        foreach (var j in rows)
        {
            string name = Py.S(j.Get("name"));
            string major = Py.S(j.Get("major"));
            string path = Py.S(j.Get("path"));

            var badge = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
            };
            badge.Dyn(Border.BackgroundProperty, "B.AccentSoft");
            badge.Child = UiClass.Txt(major, 14, bold: true, "B.AccentText").Center();

            var setDefault = U.Btn(I18nCore.Tr("设为默认"), BtnKind.Chip, (_, _) => Run(async () =>
            {
                await Api.CallAsync<object>("save_settings", new { data = new { default_java = path } });
                Toast(I18nCore.Tr("已设为默认"), name, ToastKind.Success);
            }, I18nCore.Tr("保存失败")));
            var forInstance = U.Btn(I18nCore.Tr("给本实例"), BtnKind.Chip, (_, _) => Run(async () =>
            {
                string inst = _inst.SelectedItem as string ?? "";
                if (inst.Length == 0) return;
                await Api.CallAsync<object>("set_instance_java", new { name = inst, java = path });
                await ReloadJavaOptionsAsync();
                Toast(I18nCore.Tr("已保存"), $"「{inst}」默认 Java:{name}", ToastKind.Success);
            }, I18nCore.Tr("保存失败")));
            var openDir = U.IconBtn("Folder", I18nCore.Tr("打开所在目录"), (_, _) => Run(async () =>
            {
                string target = File.Exists(path) ? (Path.GetDirectoryName(path) ?? path) : path;
                bool ok = await Api.TryCallAsync<bool>("open_media", false, new { path = target });
                if (!ok) Toast(I18nCore.Tr("打开失败"), target, ToastKind.Warning);
            }, I18nCore.Tr("打开失败")));

            var col = UiClass.V(2, UiClass.Txt(name, 13, bold: true).Trim(), UiClass.Mono(path).Trim());
            col.VCenter();
            var grid = UiClass.G(null, "Auto,*,Auto");
            badge.Mk(0, 0, 12, 0);
            grid.Add(badge, 0, 0);
            grid.Add(col, 0, 1);
            var ops = UiClass.H(6, setDefault, forInstance, openDir);
            ops.Mk(12, 0, 0, 0).VCenter();
            grid.Add(ops, 0, 2);
            cards.Add(UiClass.RowCard(grid, null, 11));
        }
        foreach (var c in cards) _list.Children.Add(c);
    }

    private async Task DownloadAsync(string major)
    {
        string vendor = SelectedVendor();
        string method = vendor == "adoptium" ? "download_java" : "install_java";
        await Api.CallAsync<string>(method, new { major = int.Parse(major), vendor });
        Toast(I18nCore.Tr("已加入队列"), $"正在下载 {vendor} Java {major},进度看下载任务");
        if (TopLevel.GetTopLevel(this) is MainWindow w) w.Navigate("tasks");
    }

    protected override void OnEvent(ApiEvent ev)
    {
        if (ev.Event != "finished" || !ev.Success) return;
        var row = TaskStore.Get(ev.TaskId);
        if (row != null && row.Title.IndexOf("Java", StringComparison.OrdinalIgnoreCase) >= 0)
            Run(async () =>
            {
                await ReloadListAsync(scan: false);
                await ReloadJavaOptionsAsync();
            });
    }
}
// ───────────────────────────── 服务器 ─────────────────────────────

internal sealed class ServersView : SettingsSubView
{
    private sealed class ServerRow
    {
        public string Name = "";
        public string Ip = "";
        public int Port = 25565;
        public string Description = "";
    }

    private readonly ComboBox _inst = UiClass.Combo(null, null, 200);
    private readonly StackPanel _list = UiClass.V(9);
    private readonly TextBlock _count = UiClass.Muted("");
    private bool _sync;

    public ServersView() : base("settings:servers", I18nCore.Tr("服务器"), I18nCore.Tr("直接写进实例的 servers.dat，进游戏就能看到"))
    {
        var add = UiClass.Btn(I18nCore.Tr("添加服务器"), BtnKind.Primary, (_, _) => Run(() => EditAsync(null, -1)));
        // [audit/B] 导入 / 导出原来只弹「后端 M3 接入」的假提示,而 IApi 的 import_servers / export_servers
        // 早就接上了(Core 的 Servers.ImportServersTxt / ExportServersTxt)—— 这里把入口真正接上。
        var import = UiClass.Btn(I18nCore.Tr("导入"), BtnKind.Chip, (_, _) => Run(ImportServersAsync, I18nCore.Tr("导入失败")));
        var export = UiClass.Btn(I18nCore.Tr("导出"), BtnKind.Chip, (_, _) => Run(ExportServersAsync, I18nCore.Tr("导出失败")));
        var instCard = UiClass.Card(RowsSub(UiClass.Field(I18nCore.Tr("实例"), UiClass.H(8, _inst, _count.VCenter(), export, import, add))));
        _stack.Children.Add(instCard);
        _inst.SelectionChanged += (_, _) => { if (!_sync) Run(ReloadAsync); };
        _stack.Children.Add(_list);
        // [R32-LINMORE] 区块宿主(§2.1-4):header(概览 = 标题行)/ instance(实例行)/ list(服务器列表),
        // 顺序与 id 逐字对齐 Windows ServersPage 的 BlockHost("servers", ...)。
        UseBlockHost("servers", "header", I18nCore.Tr("服务器"),
            ("instance", I18nCore.Tr("实例行"), instCard),
            ("list", I18nCore.Tr("服务器列表"), _list));
    }

    protected override async Task LoadCoreAsync()
    {
        var names = await InstanceNamesAsync();
        _sync = true;
        var sel = KeyOf();
        _inst.Items.Clear();
        foreach (var n in names) _inst.Items.Add(n);
        if (!string.IsNullOrEmpty(sel) && _inst.Items.Contains(sel)) _inst.SelectedItem = sel;
        else if (_inst.Items.Count > 0) _inst.SelectedIndex = 0;
        _sync = false;
        await ReloadAsync();
    }

    private string KeyOf() => _inst.SelectedItem as string ?? "";

    private async Task ReloadAsync()
    {
        string inst = KeyOf();
        if (string.IsNullOrEmpty(inst)) return;
        var rows = await Api.TryCallAsync<List<object>>("list_servers", new List<object>(), new { instance = inst });
        var servers = rows?.Select(r =>
        {
            var d = Py.D(r);
            return new ServerRow
            {
                Name = Py.S(d.Get("name")),
                Ip = Py.S(d.Get("ip")),
                Port = (int)Py.IntOr(d.Get("port"), 25565),
                Description = Py.S(d.Get("description")),
            };
        }).ToList() ?? new List<ServerRow>();
        _list.Children.Clear();
        _count.Text = $"{servers.Count} 个";
        if (rows == null)
            _list.Children.Add(UiClass.Pane(UiClass.Small(I18nCore.Tr("服务器列表读不出来(servers.dat 可能损坏或还没生成)，当前为空态展示")), "B.Paper2", 8, 10));
        if (servers.Count == 0 && rows != null)
        {
            _list.Children.Add(UiClass.Empty("S", I18nCore.Tr("还没有服务器"), I18nCore.Tr("点右上角添加，或从 servers.txt 导入")));
            return;
        }
        for (int i = 0; i < servers.Count; i++)
        {
            int idx = i;
            var r = servers[i];
            var row = UiClass.G(null, "Auto,*,Auto");
            row.Add(UiClass.Pane(UiClass.Txt("S", 14, bold: true, "B.OnAccentStrong").Center(), "B.Accent", 18, 10), 0);
            row.Add(UiClass.V(3,
                UiClass.Txt(string.IsNullOrWhiteSpace(r.Name) ? r.Ip : r.Name, 13.5, bold: true),
                UiClass.Small($"{r.Ip}:{r.Port}" + (string.IsNullOrWhiteSpace(r.Description) ? "" : "  ·  " + r.Description))).VCenter(), 0, 1);
            var ops = UiClass.H(6,
                UiClass.Btn(I18nCore.Tr("编辑"), BtnKind.Chip, (_, _) => Run(() => EditAsync(r, idx))),
                UiClass.Btn(I18nCore.Tr("删除"), BtnKind.Ghost, (_, _) => Run(async () =>
                {
                    if (await Dlg.Confirm(I18nCore.Tr("删除服务器"), r.Name, I18nCore.Tr("删除"), I18nCore.Tr("取消"), danger: true))
                    {
                        await Api.CallAsync<object?>("delete_server", new { instance = KeyOf(), index = idx });
                        await ReloadAsync();
                    }
                })));
            row.Add(ops.VCenter(), 0, 2);
            _list.Children.Add(UiClass.Card(row, 13));
        }
    }

    /// <summary>
    /// [audit/B] 导入 servers.txt:IApi 的 import_servers 早就实现了(Core Servers.ImportServersTxt),
    /// 原来这里只弹一句「导入后端 M3 接入」。选文件 → 读文本 → 调 API → 刷新列表。
    /// </summary>
    private async Task ImportServersAsync()
    {
        string inst = KeyOf();
        if (inst.Length == 0)
        {
            Toast(I18nCore.Tr("先选实例"), I18nCore.Tr("服务器列表是按实例存的"), ToastKind.Warning);
            return;
        }
        string? path = await Dlg.PickFile(I18nCore.Tr("导入服务器列表"), "*.txt");
        if (path == null)
        {
            return;
        }
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Toast(I18nCore.Tr("读取失败"), ex.Message, ToastKind.Error);
            return;
        }
        long added = await Api.CallAsync<long>("import_servers", new { instance = inst, text });
        Toast(I18nCore.Tr("已导入"), $"新增 {added} 个服务器", ToastKind.Success);
        await ReloadAsync();
    }

    /// <summary>[audit/B] 导出 servers.txt(export_servers 已实现):先取文本再选保存位置。</summary>
    private async Task ExportServersAsync()
    {
        string inst = KeyOf();
        if (inst.Length == 0)
        {
            Toast(I18nCore.Tr("先选实例"), I18nCore.Tr("服务器列表是按实例存的"), ToastKind.Warning);
            return;
        }
        string text = await Api.CallAsync<string>("export_servers", new { instance = inst }) ?? "";
        if (text.Length == 0)
        {
            Toast(I18nCore.Tr("没有可导出的服务器"), "", ToastKind.Warning);
            return;
        }
        string? path = await Dlg.SaveFile(I18nCore.Tr("导出服务器列表"), "servers.txt");
        if (path == null)
        {
            return;
        }
        try
        {
            File.WriteAllText(path, text);
            Toast(I18nCore.Tr("已导出"), path, ToastKind.Success);
        }
        catch (Exception ex)
        {
            Toast(I18nCore.Tr("写入失败"), ex.Message, ToastKind.Error);
        }
    }

    private async Task EditAsync(ServerRow? row, int index)
    {
        string inst = KeyOf();
        if (string.IsNullOrEmpty(inst)) return;
        var name = UiClass.Input(I18nCore.Tr("显示名"), row?.Name ?? "");
        var ip = UiClass.Input("mc.example.com", row?.Ip ?? "");
        var port = UiClass.Input("25565", (row?.Port ?? 25565).ToString());
        var desc = UiClass.Input(I18nCore.Tr("备注（可空）"), row?.Description ?? "");
        if (!await Dlg.Ask(row == null ? I18nCore.Tr("添加服务器") : I18nCore.Tr("编辑服务器"), UiClass.V(8, name, ip, port, desc), I18nCore.Tr("保存"))) return;
        int p = int.TryParse(port.Text?.Trim(), out var v) ? v : 25565;
        if (row != null)
            await Api.CallAsync<object?>("update_server", new { instance = inst, index, name = name.Text?.Trim() ?? "", ip = ip.Text?.Trim() ?? "", port = p, description = desc.Text?.Trim() ?? "" });
        else
            await Api.CallAsync<object?>("add_server", new { instance = inst, name = name.Text?.Trim() ?? "", ip = ip.Text?.Trim() ?? "", port = p, description = desc.Text?.Trim() ?? "" });
        await ReloadAsync();
    }
}

// ───────────────────────────── 游戏时长 ─────────────────────────────

internal sealed class PlaytimeView : SettingsSubView
{
    private readonly StackPanel _list = UiClass.V(10);
    private readonly TextBlock _total = new() { FontSize = 34, FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock _sub = UiClass.Muted("");
    private readonly ComboBox _inst = UiClass.Combo(null, null, 190);
    private bool _sync;

    private const string AllInstances = "全部实例";

    public PlaytimeView() : base("settings:playtime", I18nCore.Tr("游戏时长"), I18nCore.Tr("按实例与版本统计，每次退出游戏后自动累加"))
    {
        var refresh = U.Btn(I18nCore.Tr("刷新"), BtnKind.Chip, (_, _) => Run(LoadCoreAsync), "Refresh");
        var clearAll = UiClass.Btn(I18nCore.Tr("清空统计"), BtnKind.Ghost, (_, _) => Run(async () =>
        {
            if (await Dlg.Confirm(I18nCore.Tr("清空游戏时长"), I18nCore.Tr("所有实例的时长记录都会归零。"), I18nCore.Tr("清空"), I18nCore.Tr("取消"), danger: true))
            {
                await Api.CallAsync<object?>("clear_playtime", new { instance = "", version = "" });
                await LoadCoreAsync();
            }
        }, "清空失败"));
        _total.Foreground = UiClass.Brush("B.AccentDeep") ?? Avalonia.Media.Brushes.Black;
        var totalCard = UiClass.Card(UiClass.G(null, "*,Auto")
            .Add(UiClass.V(4, UiClass.Muted(I18nCore.Tr("累计游玩")), _total, _sub), 0)
            .Add(UiClass.Txt("⏱", 44, fg: "B.AccentSoft2").VCenter(), 0, 1), 20);
        _stack.Children.Add(totalCard);
        var rangeCard = UiClass.Card(UiClass.H(10, UiClass.Txt(I18nCore.Tr("范围"), 12, fg: "B.InkMuted").VCenter(), _inst.VCenter(), refresh.VCenter(), clearAll.VCenter()), 12);
        _stack.Children.Add(rangeCard);
        _inst.SelectionChanged += (_, _) => { if (!_sync) Run(ReloadAsync); };
        _stack.Children.Add(_list);
        // [R32-LINMORE] 区块宿主(§2.1-6):header(概览 = 标题行)/ total(累计卡)/ range(范围卡)/ list(明细列表),
        // 顺序与 id 逐字对齐 Windows PlaytimePage 的 BlockHost("playtime", ...)。
        UseBlockHost("playtime", "header", I18nCore.Tr("游戏时长"),
            ("total", I18nCore.Tr("累计卡"), totalCard),
            ("range", I18nCore.Tr("范围卡"), rangeCard),
            ("list", I18nCore.Tr("明细列表"), _list));
    }

    protected override async Task LoadCoreAsync()
    {
        var names = await InstanceNamesAsync();
        _sync = true;
        var sel = _inst.SelectedItem as string;
        _inst.Items.Clear();
        _inst.Items.Add(AllInstances);
        foreach (var n in names) _inst.Items.Add(n);
        if (!string.IsNullOrEmpty(sel) && _inst.Items.Contains(sel)) _inst.SelectedItem = sel;
        else _inst.SelectedIndex = 0;
        _sync = false;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        string? inst = _inst.SelectedItem as string;
        long total = await Api.TryCallAsync("get_total_playtime", 0L);
        _total.Text = Format(total);
        if (inst is null or AllInstances)
        {
            var all = await Api.TryCallAsync<Dictionary<string, object>>("get_all_playtime");
            _sub.Text = all == null ? I18nCore.Tr("读不到时长统计(playtime.json 可能损坏)，暂无数据") : $"{all.Count} 个实例有记录";
            _list.Children.Clear();
            if (all == null || all.Count == 0)
            {
                _list.Children.Add(UiClass.Empty("⏱", I18nCore.Tr("还没有游玩记录"), I18nCore.Tr("启动一次游戏后这里就有数据了")));
                return;
            }
            foreach (var kv in all.OrderByDescending(kv => Secs(kv.Value)))
            {
                long secs = Secs(kv.Value);
                var row = UiClass.G(null, "*,Auto");
                row.Add(UiClass.V(6, UiClass.Txt(kv.Key, 14, bold: true), UiClass.Small(Format(secs)).Wrap()), 0);
                row.Add(UiClass.Txt(Format(secs), 13, bold: true, "B.AccentDeep").VCenter(), 0, 1);
                _list.Children.Add(UiClass.Card(row, 14));
            }
        }
        else
        {
            var data = await Api.TryCallAsync<Dictionary<string, object>>("get_playtime", new Dictionary<string, object>(), new { instance = inst });
            _sub.Text = $"实例「{inst}」";
            _list.Children.Clear();
            if (data == null || data.Count == 0)
            {
                _list.Children.Add(UiClass.Empty("⏱", I18nCore.Tr("这个实例还没有记录"), I18nCore.Tr("启动一次游戏后这里就有数据了")));
                return;
            }
            foreach (var kv in data.Where(kv => kv.Key != "total" && kv.Key != "sessions").OrderByDescending(kv => Py.IntOr(kv.Value, 0)))
            {
                long secs = Py.IntOr(kv.Value, 0);
                var row = UiClass.G(null, "*,Auto");
                row.Add(UiClass.V(6, UiClass.Txt(kv.Key, 13.5, bold: true), UiClass.Small(Format(secs))), 0);
                row.Add(UiClass.Txt(Format(secs), 13, bold: true, "B.AccentDeep").VCenter(), 0, 1);
                _list.Children.Add(UiClass.Card(row, 13));
            }
        }
    }

    private static long Secs(object o)
    {
        if (Py.D(o).Get("total") is object t) return Py.IntOr(t, 0);
        return Py.IntOr(o, 0);
    }

    private static string Format(long secs)
    {
        if (secs <= 0) return I18nCore.Tr("0 分钟");
        return secs >= 3600 ? $"{secs / 3600.0:0.0} 小时" : $"{secs / 60.0:0} 分钟";
    }
}
