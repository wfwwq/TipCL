using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages;

// 联机页(移植自 Windows 版 Pages/MultiplayerPage.cs):陶瓦联机(Terracotta / EasyTier P2P)。
// 顶部说明 + 局域网地址 → 状态卡(状态点 + 详情 + 防火墙钮)→ 房间卡(邀请码)→ 操作区(按状态渲染)
// → 房间成员 → 会合节点说明。1.2s 轮询 terracotta_snapshot;进页自动准备内核(仅当已装且未运行)。
public sealed class MultiplayerPage : PageBase
{
    private const string ProjectHome = "https://github.com/burningtnt/Terracotta";

    private readonly TextBlock _state = UiClass.Txt("", 18, bold: true);

    private readonly TextBlock _detail = UiClass.Muted("").Wrap();

    private readonly TextBlock _lan = UiClass.Small("").Wrap();

    private readonly TextBlock _nodes = UiClass.Small("").Wrap();

    private readonly TextBlock _room = UiClass.Txt("", 21, bold: true, "B.AccentDeep");

    private readonly TextBlock _roomHint = UiClass.Small("").Wrap();

    private readonly Border _roomCard;

    private readonly StackPanel _actions = UiClass.V(10);

    private readonly StackPanel _players = UiClass.V(8);

    private readonly TextBlock _playersTitle = UiClass.Txt("房间成员", 13, bold: true);

    private readonly Ellipse _dot = new() { Width = 10, Height = 10 };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    private bool _busy;

    private bool _polling;

    private bool _autoStarted;

    private string _uiState = "";

    private string _playerSig = "";

    private string _lastState = "";

    public MultiplayerPage() : base("multiplayer", "联机")
    {
        var allowFw = UiClass.Btn("允许访问", BtnKind.Primary, (_, _) => RunSafe(AllowFirewallAsync, ""));
        var openFw = UiClass.Btn("打开设置", BtnKind.Chip, (_, _) => RunSafe(OpenFirewallAsync, ""));
        var shutdown = UiClass.Btn("关闭内核", BtnKind.Ghost, (_, _) => RunSafe(async () =>
        {
            await Api.CallAsync<object>("terracotta_shutdown");
            await ReloadAsync();
        }, ""));
        _dot.VerticalAlignment = VerticalAlignment.Center;
        _dot.Dyn(Shape.FillProperty, "B.InkFaint");
        var stateCard = UiClass.Card(UiClass.V(10,
            UiClass.H(10, _dot, _state),
            _detail,
            UiClass.H(8, allowFw, openFw, shutdown)), 18);

        var copyRoom = UiClass.Btn("复制邀请码", BtnKind.Chip, async (_, _) =>
            await CopyAsync(_room.Text ?? "", "已把邀请码复制到剪贴板"));
        _roomCard = UiClass.Card(UiClass.V(6,
            UiClass.Small("邀请码"),
            UiClass.H(10, _room.VCenter(), copyRoom.VCenter()),
            _roomHint));
        _roomCard.IsVisible = false;
        _playersTitle.IsVisible = false;

        var homeBtn = UiClass.Btn("Terracotta 项目主页", BtnKind.Ghost, (_, _) => OpenUrl(ProjectHome));
        var footer = UiClass.H(10, homeBtn,
            UiClass.Small("Terracotta | 陶瓦联机  © burningtnt  ·  基于 EasyTier").VCenter());

        var header = UiClass.Section("陶瓦联机",
            "输入邀请码即可加入。陶瓦是 EasyTier P2P 打洞,不是 FRP 隧道;会与 HMCL 一样带上自定义会合节点。PCL 房间号不互通,局域网请用下面的地址。");

        Body = new BlockHost("multiplayer", new List<BlockSpec>
        {
            new() { Id = "header", Title = "说明", Content = UiClass.V(10, header, _lan) },
            new() { Id = "state", Title = "状态卡", Content = stateCard },
            new() { Id = "room", Title = "房间卡", Content = _roomCard },
            new() { Id = "actions", Title = "操作区", Content = _actions },
            new() { Id = "players", Title = "玩家列表", Content = UiClass.V(10, _playersTitle, _players) },
            new() { Id = "nodes", Title = "节点说明", Content = UiClass.V(10, _nodes, footer) },
        }).Root;

        _timer.Tick += (_, _) => RunSafe(ReloadAsync, "");
    }

    protected override void OnCreate()
    {
        _ = LoadLanAsync();
        RunSafe(ReloadAsync, "");
    }

    protected override void OnActivate()
    {
        _timer.Start();
        _ = LoadLanAsync();
        RunSafe(ReloadAsync, "");
        RunSafe(MaybePrepareAsync, "");
    }

    protected override void OnDeactivate()
    {
        _timer.Stop();
        RunSafe(ReloadAsync, "");
    }

    private async Task LoadLanAsync()
    {
        string hint = await Api.TryCallAsync("lan_hint", "", new PyDict { ["port"] = 25565L }) ?? "";
        var ips = await Api.TryCallAsync<List<object>>("local_ips", new List<object>()) ?? new List<object>();
        _lan.Text = ips.Count > 0
            ? "本机局域网地址:" + string.Join("、", ips.Select(ip => Py.S(ip) + ":25565"))
            : hint;
    }

    private async Task ReloadAsync()
    {
        if (_polling)
            return;
        _polling = true;
        try
        {
            var snap = await Api.TryCallAsync<PyDict>("terracotta_snapshot");
            if (snap != null)
                Render(snap);
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task MaybePrepareAsync()
    {
        if (_autoStarted || _busy)
            return;
        var snap = await Api.TryCallAsync<PyDict>("terracotta_snapshot");
        if (snap == null)
            return;
        if (Py.Truthy(snap.Get("supported")) && Py.Truthy(snap.Get("installed")) && !Py.Truthy(snap.Get("running")))
        {
            _autoStarted = true;
            await PrepareAsync();
        }
    }

    private void Render(PyDict s)
    {
        string state = Py.S(s.Get("state"));
        if (state.Length == 0)
            state = "missing";

        if (_busy)
        {
            if (state is "missing" or "idle")
                state = "installing";
        }
        else if (state is "waiting" or "host-ok" or "guest-ok" or "idle")
        {
            _busy = false;
        }

        string label = Py.S(s.Get("label"));
        _state.Text = state is "exception" or "fatal" ? "加入失败" : (label.Length == 0 ? state : label);
        _dot.Dyn(Shape.FillProperty, DotBrush(state));

        string detailText = Py.S(s.Get("error"));
        if (detailText.Length == 0)
            detailText = label;
        string errorHint = Py.S(s.Get("error_hint"));
        string difficultyHint = Py.S(s.Get("difficulty_hint"));
        if (errorHint.Length > 0)
            detailText = (detailText + "\n" + errorHint).Trim();
        else if (difficultyHint.Length > 0)
            detailText = (label + "\n" + difficultyHint).Trim();

        var bits = new List<string>();
        if (!Py.Truthy(s.Get("supported")))
            bits.Add("当前系统架构不支持");
        bits.Add(Py.Truthy(s.Get("installed")) ? "内核已安装" : "内核未安装");
        bits.Add(Py.Truthy(s.Get("running")) ? "内核运行中" : "内核未运行");
        string player = Py.S(s.Get("player"));
        if (player.Length > 0)
            bits.Add("玩家 " + player);
        if (Py.Truthy(s.Get("game_running")))
            bits.Add("游戏运行中");
        _detail.Text = string.Join(" · ", bits) + (detailText.Length == 0 ? "" : "\n" + detailText);

        string room = Py.S(s.Get("room"));
        string url = Py.S(s.Get("url"));
        bool hasRoom = room.Length > 0 || url.Length > 0;
        _roomCard.IsVisible = hasRoom;
        if (hasRoom)
        {
            _room.Text = room.Length == 0 ? "陶瓦联机大厅" : room;
            _roomHint.Text = url.Length == 0
                ? "让好友在联机页选「我想当房客」,填这串邀请码。"
                : "启动游戏 → 多人游戏 → 双击「陶瓦联机大厅」。";
        }

        var nodes = Py.ListOr(s.Get("nodes")).Select(Py.S).ToList();
        _nodes.Text = nodes.Count == 0 ? "" : $"会合节点 {nodes.Count} 个:{string.Join("  ", nodes.Take(3))}";

        if (state != _uiState)
        {
            _uiState = state;
            FillActions(state, s);
        }

        RenderPlayers(Py.ListOr(s.Get("profiles")));

        if (room.Length > 0 && state == "host-ok" && _lastState != "host-ok")
            _ = CopyAsync(room, "已把邀请码复制到剪贴板");
        _lastState = state;
    }

    private static string DotBrush(string state) => state switch
    {
        "host-ok" or "waiting" or "guest-ok" => "B.Ok",
        "idle" => "B.Accent",
        "exception" or "fatal" or "unsupported" => "B.Danger",
        "missing" => "B.Warn",
        _ => "B.InkFaint",
    };

    private void RenderPlayers(List<object> profiles)
    {
        string sig = string.Join("|", profiles.Select(p =>
        {
            var d = Py.D(p);
            return Py.S(d.Get("name")) + "/" + Py.S(d.Get("kind")) + "/" + Py.S(d.Get("vendor"));
        }));
        if (sig == _playerSig)
            return;
        _playerSig = sig;
        _players.Children.Clear();
        _playersTitle.IsVisible = profiles.Count > 0;
        foreach (var p in profiles)
        {
            var d = Py.D(p);
            string name = Py.S(d.Get("name"));
            string kind = Py.S(d.Get("kind"));
            string vendor = Py.S(d.Get("vendor"));
            bool isHost = string.Equals(kind, "HOST", StringComparison.OrdinalIgnoreCase);
            var tile = new AvatarTile(name, 36, 18);
            var grid = UiClass.G(null, "Auto,*,Auto");
            grid.Add(tile.Mk(0, 0, 12, 0), 0, 0);
            grid.Add(UiClass.V(1,
                UiClass.Txt(name.Length == 0 ? "玩家" : name, 13, bold: true).Trim(),
                UiClass.Small(vendor.Length == 0 ? (isHost ? "房主" : "成员") : vendor)).VCenter(), 0, 1);
            grid.Add(UiClass.Tag(isHost ? "房主" : "成员", "B.OnAccentStrong", "B.AccentStrong").VCenter(), 0, 2);
            _players.Children.Add(UiClass.Card(grid, 12));
        }
        Motion.Stagger(_players, 18);
    }

    private void FillActions(string state, PyDict s)
    {
        _actions.Children.Clear();
        switch (state)
        {
            case "missing":
                Action("下载陶瓦联机内核", "首次使用需要下载约 8 MB 的官方内核,之后可直接开房。", "下载", PrepareAsync, primary: true);
                return;
            case "idle":
                Action("启动联机内核", "内核已安装,点一下即可开始联机。", "启动", PrepareAsync, primary: true);
                return;
            case "waiting":
                Action("我想当房主", "创建房间并生成邀请码,与好友一起畅玩。", "创建", HostAsync, primary: true);
                Action("我想当房客", "输入房主提供的邀请码加入游戏世界。", "加入", JoinAsync);
                return;
            case "host-scanning":
            case "host-starting":
                Action("正在扫描局域网世界", "请启动游戏,进入单人世界,按 ESC,选择对局域网开放。", "退出", BackAsync);
                return;
            case "host-ok":
                Action("复制邀请码", "好友在联机页选「我想当房客」并填这串邀请码即可加入。", "复制",
                    () => CopyAsync(_room.Text ?? "", "已把邀请码复制到剪贴板"), primary: true);
                Action("退出", "这会彻底关闭房间,其他房客将一并退出。", "退出", BackAsync);
                return;
            case "guest-connecting":
            case "guest-starting":
                Action("正在加入房间",
                    Py.S(s.Get("difficulty_hint")).Length > 0 ? Py.S(s.Get("difficulty_hint")) : "正在与房主建立连接。",
                    "退出", BackAsync);
                return;
            case "guest-ok":
                Action("进入世界", "启动游戏后到多人游戏双击「陶瓦联机大厅」,或点这里直接进入。", "进入", EnterWorldAsync, primary: true);
                Action("退出", "这不影响其他房客加入当前房间。", "退出", BackAsync);
                return;
            case "unsupported":
                Action("当前系统不支持", "陶瓦联机暂未提供此架构的官方内核。", "了解", () =>
                {
                    OpenUrl(ProjectHome);
                    return Task.CompletedTask;
                });
                return;
            case "installing":
            case "launching":
            case "unknown":
                Action("请稍候",
                    Py.S(s.Get("label")).Length > 0 ? Py.S(s.Get("label")) : "正在准备联机内核。",
                    "刷新", ReloadAsync);
                return;
            default:
                Action("联机失败",
                    Py.S(s.Get("error_hint")).Length > 0 ? Py.S(s.Get("error_hint"))
                        : (Py.S(s.Get("error")).Length > 0 ? Py.S(s.Get("error")) : "请返回后重试,或检查网络。"),
                    "返回", BackAsync);
                Action("朋友是公网就直连", "让他把单人世界对局域网开放,并在路由映射该端口,然后填他的公网 IP:端口。", "直连", DirectConnectAsync);
                Action("重新启动内核", "若内核已退出,点此重新拉起。", "重启", PrepareAsync);
                return;
        }
    }

    private void Action(string title, string desc, string buttonText, Func<Task> act, bool primary = false)
    {
        var btn = UiClass.Btn(buttonText, primary ? BtnKind.Primary : BtnKind.Normal, (_, _) => RunSafe(act, ""));
        btn.MinWidth = 96;
        var tile = new AvatarTile(title, 46, 12);
        var grid = UiClass.G(null, "Auto,*,Auto");
        grid.Add(tile.Mk(0, 0, 14, 0), 0, 0);
        grid.Add(UiClass.V(2, UiClass.Txt(title, 13.5, bold: true), UiClass.Small(desc).Wrap()).VCenter(), 0, 1);
        grid.Add(btn.VCenter().Mk(14, 0, 0, 0), 0, 2);
        var card = UiClass.Card(grid, 14);
        Motion.HoverLift(card, 1.004);
        _actions.Children.Add(card);
    }

    // ————— 动作 —————

    private async Task PrepareAsync()
    {
        if (_busy)
            return;
        _busy = true;
        await Api.CallAsync<string>("terracotta_prepare");
        Toast("正在准备联机内核", "进度看下载任务");
        await ReloadAsync();
    }

    private async Task HostAsync()
    {
        string player = await Api.TryCallAsync("terracotta_player", "Player") ?? "Player";
        var snap = await Api.TryCallAsync<PyDict>("terracotta_snapshot");
        bool gameRunning = snap != null && Py.Truthy(snap.Get("game_running"));
        if (!gameRunning)
        {
            if (!await Dlg.Confirm("您似乎忘记启动游戏了",
                    $"将以「{player}」的身份开房。\n请先启动游戏,进入单人世界,按 ESC,选择「对局域网开放」。",
                    "游戏已启动", "取消"))
                return;
        }
        else
        {
            Toast("正在开房", $"以「{player}」的身份创建房间");
        }
        await Api.CallAsync<object>("terracotta_host");
        await ReloadAsync();
    }

    private async Task JoinAsync()
    {
        string? code = await Dlg.Prompt("我想当房客", "请输入房主提供的邀请码", "", "U/XXXX-XXXX-XXXX-XXXX");
        if (string.IsNullOrWhiteSpace(code))
            return;
        await Api.CallAsync<object>("terracotta_join", new PyDict { ["room"] = code.Trim() });
        await ReloadAsync();
    }

    private async Task DirectConnectAsync()
    {
        string? addr = await Dlg.Prompt("公网直连",
            "朋友需先对局域网开放世界,并在路由器映射该端口,然后填他的公网地址。",
            "", "例如 1.2.3.4:25565");
        if (string.IsNullOrWhiteSpace(addr))
            return;
        string hint = await Api.CallAsync<string>("terracotta_direct_connect", new PyDict { ["address"] = addr.Trim() }) ?? "";
        Toast("正在直连", hint.Length == 0 ? "启动后会进入该服务器。" : hint, ToastKind.Success);
        await ReloadAsync();
    }

    private async Task EnterWorldAsync()
    {
        string text = await Api.CallAsync<string>("terracotta_enter_world") ?? "";
        if (text.StartsWith("task-", StringComparison.Ordinal))
            Toast("正在启动游戏", "启动后会直接进入陶瓦联机大厅", ToastKind.Success);
        else
            Toast("已加入房间", text.Length == 0 ? "请到多人游戏双击「陶瓦联机大厅」。" : text, ToastKind.Success);
        await ReloadAsync();
    }

    private async Task BackAsync()
    {
        await Api.CallAsync<object>("terracotta_idle");
        await ReloadAsync();
    }

    private async Task AllowFirewallAsync()
    {
        string text = await Api.CallAsync<string>("terracotta_allow_firewall") ?? "";
        Toast("防火墙", text.Length == 0 ? "已提交规则" : text, ToastKind.Success);
    }

    private async Task OpenFirewallAsync()
    {
        try
        {
            await Api.CallAsync<object>("terracotta_open_firewall_settings");
        }
        catch (Exception)
        {
            // Linux 无「防火墙设置页」这种单入口:退回提示(Windows 走 ms-settings:)
            Toast("防火墙", "请在系统设置里放行陶瓦内核的网络访问", ToastKind.Info);
        }
    }

    private async Task CopyAsync(string text, string toast)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip != null)
            await clip.SetTextAsync(text);
        Toast("已复制", toast, ToastKind.Success);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", new[] { url });
            else
                Process.Start("xdg-open", new[] { url });
        }
        catch (Exception)
        {
        }
    }

    private void RunSafe(Func<Task> action, string failTitle)
    {
        _ = RunSafeAsync(action, failTitle);
    }

    private async Task RunSafeAsync(Func<Task> action, string failTitle)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (failTitle.Length > 0)
                Toast(failTitle, ex.Message, ToastKind.Error);
        }
    }
}
