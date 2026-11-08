// 设置页 · 左导航子视图（M2-B5）：账号 / Java / 服务器 / 游戏时长。
// 每个都是本页自持的 PageBase 实例（照 R3 §2.1：不与 MainWindow 共享，避免同一控件两个父级）。
// 数据全部走 PageBase.Api 的 snake_case 契约；M2 尚未搬的后端能力（账号/Java/服务器/时长
// 的读写）TryCallAsync 回默认值，显示空态与「后端 M3 接入」提示，接口已按 Windows 版键名对齐。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TipCL.App.Common;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Settings;

/// <summary>子视图公共骨架：标题 + 说明 + 滚动主体，数据装载走 LoadAsync。</summary>
internal abstract class SettingsSubView : PageBase
{
    protected SettingsSubView(string id, string title, string sub) : base(id, title)
    {
        var head = UiClass.Section(title, sub);
        _stack = UiClass.V(10);
        _stack.Children.Add(head);
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
        catch (Exception ex) { ToastHost.Show(failTitle ?? "操作失败", ex.Message, ToastKind.Error); }
    }

    protected override void OnActivate() => Run(LoadCoreAsync, "加载失败");

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

    public AccountView() : base("settings:account", "账号", "启动游戏时使用标记为「当前」的账号")
    {
        var addOffline = UiClass.Btn("添加离线账号", BtnKind.Primary, (_, _) => Run(AddOfflineAsync, "添加失败"));
        var addMs = UiClass.Btn("微软登录", BtnKind.Soft, (_, _) => Toast("微软登录", "登录后端 M3 接入，暂不可用", ToastKind.Warning));
        var addAuthlib = UiClass.Btn("皮肤站登录", BtnKind.Chip, (_, _) => Toast("皮肤站登录", "登录后端 M3 接入，暂不可用", ToastKind.Warning));
        _stack.Children.Add(UiClass.Card(RowsSub(UiClass.Field("", UiClass.H(8, _count.VCenter(), addAuthlib, addMs, addOffline)))));
        _stack.Children.Add(_list);
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
            _list.Children.Add(UiClass.Card(UiClass.Empty("?", "还没有账号", "添加离线账号先玩起来；微软 / 皮肤站登录 M3 接入"), 18));
            return;
        }
        foreach (var acc in accounts)
        {
            string typeLabel = acc.Type switch
            {
                "microsoft" => "微软",
                "offline" => "离线",
                "authlib" => "皮肤站",
                "nide8" => "统一通行证",
                _ => string.IsNullOrEmpty(acc.Type) ? "其他" : acc.Type,
            };
            var row = UiClass.G(null, "Auto,*,Auto");
            row.Add(UiClass.Pane(UiClass.Txt(string.IsNullOrEmpty(acc.Name) ? "?" : acc.Name[..1].ToUpperInvariant(), 16, bold: true, "B.OnAccentStrong")
                .Center(), "B.Accent", 20, 10), 0);
            var head = UiClass.H(6, UiClass.Txt(acc.Name, 13.5, bold: true).VCenter());
            if (acc.Active) head.Children.Add(UiClass.Tag("当前", "B.OnAccentStrong", "B.AccentStrong"));
            head.Children.Add(UiClass.Tag(typeLabel));
            row.Add(UiClass.V(3, head, UiClass.Small(string.IsNullOrEmpty(acc.Uuid) ? typeLabel + "账号" : "UUID " + acc.Uuid)).VCenter(), 0, 1);
            var ops = UiClass.H(6);
            if (!acc.Active)
                ops.Children.Add(UiClass.Btn("设为当前", BtnKind.Soft, (_, _) => Run(async () =>
                {
                    await Api.CallAsync<object?>("set_active_account", new { name = acc.Name });
                    Toast("已切换", $"当前账号：{acc.Name}", ToastKind.Success);
                    await LoadCoreAsync();
                }, "切换失败")));
            ops.Children.Add(UiClass.Btn("删除", BtnKind.Ghost, (_, _) => Run(async () =>
            {
                if (await Dlg.Confirm("删除账号", $"将删除「{acc.Name}」。", "删除", "取消", danger: true))
                {
                    await Api.CallAsync<object?>("remove_account", new { name = acc.Name });
                    await LoadCoreAsync();
                }
            }, "删除失败")));
            row.Add(ops.VCenter(), 0, 2);
            _list.Children.Add(UiClass.RowCard(row, padding: 13));
        }
    }

    private async Task AddOfflineAsync()
    {
        var name = UiClass.Input("例如 Steve");
        if (!await Dlg.Ask("添加离线账号", UiClass.V(8, UiClass.Muted("离线模式，无正版验证"), name), "添加")) return;
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            Toast("缺少名字", "请填写离线角色名", ToastKind.Warning);
            return;
        }
        await Api.CallAsync<object?>("add_offline_account", new { username = name.Text.Trim(), skin = "default" });
        Toast("已添加", name.Text.Trim(), ToastKind.Success);
        await LoadCoreAsync();
    }
}

// ───────────────────────────── Java ─────────────────────────────

internal sealed class JavaView : SettingsSubView
{
    private readonly StackPanel _list = UiClass.V(10);
    private readonly TextBlock _info = UiClass.Small("");

    public JavaView() : base("settings:java", "Java", "管理启动器自带的与系统里找到的 Java 运行时")
    {
        var scan = UiClass.Btn("扫描 Java", BtnKind.Primary, (_, _) => Run(async () =>
        {
            _info.Text = "扫描中…";
            object? r = await Api.TryCallAsync<object?>("scan_java", null);
            if (r == null)
            {
                _info.Text = "扫描后端 M3 接入；可在下方手动添加 Java 路径";
                return;
            }
            await LoadCoreAsync();
        }, "扫描失败"));
        var add = UiClass.Btn("手动添加", BtnKind.Chip, (_, _) => Run(AddJavaAsync, "添加失败"));
        _stack.Children.Add(UiClass.Card(RowsSub(UiClass.Field("扫描", UiClass.H(8, scan, add, _info.VCenter()), "自动找启动器目录与系统 PATH 里的 Java"))));
        _stack.Children.Add(_list);
    }

    protected override async Task LoadCoreAsync()
    {
        var rows = await Api.TryCallAsync<List<object>>("list_java", new List<object>());
        _list.Children.Clear();
        if (rows == null || rows.Count == 0)
        {
            _info.Text = "";
            _list.Children.Add(UiClass.Card(UiClass.Empty("J", "还没有可用的 Java", "下载页安装游戏时可以一键装自带 JRE（M3 接入）；或点「手动添加」"), 18));
            return;
        }
        foreach (var r in rows)
        {
            var d = Py.D(r);
            string name = Py.S(d.Get("name"));
            string ver = Py.S(d.Get("version"));
            string path = Py.S(d.Get("path"));
            var row = UiClass.G(null, "*,Auto");
            row.Add(UiClass.V(3, UiClass.Txt(string.IsNullOrEmpty(name) ? "Java " + ver : name, 13.5, bold: true), UiClass.Small(path).Wrap()), 0);
            var ops = UiClass.H(6);
            if (Py.Truthy(d.Get("is_default")))
                ops.Children.Add(UiClass.Tag("默认", "B.OnAccentStrong", "B.AccentStrong"));
            else
                ops.Children.Add(UiClass.Btn("设为默认", BtnKind.Chip, (_, _) => Run(async () =>
                {
                    await Api.CallAsync<object?>("set_default_java", new { path });
                    await LoadCoreAsync();
                }, "设置失败")));
            row.Add(ops.VCenter(), 0, 1);
            _list.Children.Add(UiClass.RowCard(row, padding: 13));
        }
    }

    private async Task AddJavaAsync()
    {
        string? path = await Dlg.PickFolder("选择 Java 主目录（含 bin/java 的目录）");
        if (string.IsNullOrEmpty(path)) return;
        await Api.CallAsync<object?>("add_java", new { path });
        Toast("已添加", path, ToastKind.Success);
        await LoadCoreAsync();
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

    public ServersView() : base("settings:servers", "服务器", "直接写进实例的 servers.dat，进游戏就能看到")
    {
        var add = UiClass.Btn("添加服务器", BtnKind.Primary, (_, _) => Run(() => EditAsync(null, -1)));
        var import = UiClass.Btn("导入", BtnKind.Chip, (_, _) => Toast("导入", "servers.txt 导入后端 M3 接入", ToastKind.Warning));
        var export = UiClass.Btn("导出", BtnKind.Chip, (_, _) => Toast("导出", "servers.txt 导出后端 M3 接入", ToastKind.Warning));
        _stack.Children.Add(UiClass.Card(RowsSub(UiClass.Field("实例", UiClass.H(8, _inst, _count.VCenter(), export, import, add)))));
        _inst.SelectionChanged += (_, _) => { if (!_sync) Run(ReloadAsync); };
        _stack.Children.Add(_list);
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
            _list.Children.Add(UiClass.Pane(UiClass.Small("服务器列表读写后端 M3 接入（list_servers / add_server），当前为空态展示"), "B.Paper2", 8, 10));
        if (servers.Count == 0 && rows != null)
        {
            _list.Children.Add(UiClass.Empty("S", "还没有服务器", "点右上角添加，或从 servers.txt 导入"));
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
                UiClass.Btn("编辑", BtnKind.Chip, (_, _) => Run(() => EditAsync(r, idx))),
                UiClass.Btn("删除", BtnKind.Ghost, (_, _) => Run(async () =>
                {
                    if (await Dlg.Confirm("删除服务器", r.Name, "删除", "取消", danger: true))
                    {
                        await Api.CallAsync<object?>("delete_server", new { instance = KeyOf(), index = idx });
                        await ReloadAsync();
                    }
                })));
            row.Add(ops.VCenter(), 0, 2);
            _list.Children.Add(UiClass.Card(row, 13));
        }
    }

    private async Task EditAsync(ServerRow? row, int index)
    {
        string inst = KeyOf();
        if (string.IsNullOrEmpty(inst)) return;
        var name = UiClass.Input("显示名", row?.Name ?? "");
        var ip = UiClass.Input("mc.example.com", row?.Ip ?? "");
        var port = UiClass.Input("25565", (row?.Port ?? 25565).ToString());
        var desc = UiClass.Input("备注（可空）", row?.Description ?? "");
        if (!await Dlg.Ask(row == null ? "添加服务器" : "编辑服务器", UiClass.V(8, name, ip, port, desc), "保存")) return;
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

    public PlaytimeView() : base("settings:playtime", "游戏时长", "按实例与版本统计，每次退出游戏后自动累加")
    {
        var refresh = UiClass.Btn("刷新", BtnKind.Chip, (_, _) => Run(LoadCoreAsync));
        var clearAll = UiClass.Btn("清空统计", BtnKind.Ghost, (_, _) => Run(async () =>
        {
            if (await Dlg.Confirm("清空游戏时长", "所有实例的时长记录都会归零。", "清空", "取消", danger: true))
            {
                await Api.CallAsync<object?>("clear_playtime", new { instance = "", version = "" });
                await LoadCoreAsync();
            }
        }, "清空失败"));
        _total.Foreground = UiClass.Brush("B.AccentDeep") ?? Avalonia.Media.Brushes.Black;
        _stack.Children.Add(UiClass.Card(UiClass.G(null, "*,Auto")
            .Add(UiClass.V(4, UiClass.Muted("累计游玩"), _total, _sub), 0)
            .Add(UiClass.Txt("⏱", 44, fg: "B.AccentSoft2").VCenter(), 0, 1), 20));
        _stack.Children.Add(UiClass.Card(UiClass.H(10, UiClass.Txt("范围", 12, fg: "B.InkMuted").VCenter(), _inst.VCenter(), refresh.VCenter(), clearAll.VCenter()), 12));
        _inst.SelectionChanged += (_, _) => { if (!_sync) Run(ReloadAsync); };
        _stack.Children.Add(_list);
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
            _sub.Text = all == null ? "时长统计后端 M3 接入，暂无数据" : $"{all.Count} 个实例有记录";
            _list.Children.Clear();
            if (all == null || all.Count == 0)
            {
                _list.Children.Add(UiClass.Empty("⏱", "还没有游玩记录", "启动一次游戏后这里就有数据了"));
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
                _list.Children.Add(UiClass.Empty("⏱", "这个实例还没有记录", "启动一次游戏后这里就有数据了"));
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
        if (secs <= 0) return "0 分钟";
        return secs >= 3600 ? $"{secs / 3600.0:0.0} 小时" : $"{secs / 60.0:0} 分钟";
    }
}
