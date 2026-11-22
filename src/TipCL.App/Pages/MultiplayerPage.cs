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
using TipCL.Core.Launcher;

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

    private readonly TextBlock _playersTitle = UiClass.Txt(I18nCore.Tr("房间成员"), 13, bold: true);

    private readonly Ellipse _dot = new() { Width = 10, Height = 10 };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    private bool _busy;

    private bool _polling;

    private bool _autoStarted;

    private string _uiState = "";

    private string _playerSig = "";

    private string _lastState = "";

    public MultiplayerPage() : base("multiplayer", I18nCore.Tr("联机"))
    {
        var allowFw = UiClass.Btn(I18nCore.Tr("允许访问"), BtnKind.Primary, (_, _) => RunSafe(AllowFirewallAsync, ""));
        var openFw = UiClass.Btn(I18nCore.Tr("打开设置"), BtnKind.Chip, (_, _) => RunSafe(OpenFirewallAsync, ""));
        var shutdown = UiClass.Btn(I18nCore.Tr("关闭内核"), BtnKind.Ghost, (_, _) => RunSafe(async () =>
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

        var copyRoom = UiClass.Btn(I18nCore.Tr("复制邀请码"), BtnKind.Chip, async (_, _) =>
            await CopyAsync(_room.Text ?? "", I18nCore.Tr("已把邀请码复制到剪贴板")));
        _roomCard = UiClass.Card(UiClass.V(6,
            UiClass.Small(I18nCore.Tr("邀请码")),
            UiClass.H(10, _room.VCenter(), copyRoom.VCenter()),
            _roomHint));
        _roomCard.IsVisible = false;
        _playersTitle.IsVisible = false;

        var homeBtn = UiClass.Btn(I18nCore.Tr("Terracotta 项目主页"), BtnKind.Ghost, (_, _) => OpenUrl(ProjectHome));
        var footer = UiClass.H(10, homeBtn,
            UiClass.Small(I18nCore.Tr("Terracotta | 陶瓦联机  © burningtnt  ·  基于 EasyTier")).VCenter());

        var header = UiClass.Section(I18nCore.Tr("陶瓦联机"),
            "输入邀请码即可加入。陶瓦是 EasyTier P2P 打洞,不是 FRP 隧道;会与 HMCL 一样带上自定义会合节点。PCL 房间号不互通,局域网请用下面的地址。");

        // [R35 lin-l5] §5.5:块名改成用户看得懂的说法(id 不变);文档里还没有这一页时默认就是 12 列网格:
        // 说明整行 / 联机状态 + 房间各半 / 快捷操作整行 / 玩家 + 节点说明 8:4(PageDefaultLayouts.Multiplayer)。
        // 已经存过这一页的用户完全照文档,不受影响。
        Body = new BlockHost("multiplayer", new List<BlockSpec>
        {
            new() { Id = "header", Title = I18nCore.Tr("说明"), Content = UiClass.V(10, header, _lan) },
            new() { Id = "state", Title = I18nCore.Tr("联机状态"), Content = stateCard },
            new() { Id = "room", Title = I18nCore.Tr("房间"), Content = _roomCard },
            new() { Id = "actions", Title = I18nCore.Tr("快捷操作"), Content = _actions },
            new() { Id = "players", Title = I18nCore.Tr("玩家"), Content = UiClass.V(10, _playersTitle, _players) },
            new() { Id = "nodes", Title = I18nCore.Tr("节点说明"), Content = UiClass.V(10, _nodes, footer) },
        }, null, PageDefaultLayouts.Multiplayer()).Root;

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
            ? "本机局域网地址:" + string.Join(I18nCore.Tr("、"), ips.Select(ip => Py.S(ip) + ":25565"))
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
        _state.Text = state is "exception" or "fatal" ? I18nCore.Tr("加入失败") : (label.Length == 0 ? state : label);
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
            bits.Add(I18nCore.Tr("当前系统架构不支持"));
        bits.Add(Py.Truthy(s.Get("installed")) ? I18nCore.Tr("内核已安装") : I18nCore.Tr("内核未安装"));
        bits.Add(Py.Truthy(s.Get("running")) ? I18nCore.Tr("内核运行中") : I18nCore.Tr("内核未运行"));
        string player = Py.S(s.Get("player"));
        if (player.Length > 0)
            bits.Add(I18nCore.Tr("玩家 ") + player);
        if (Py.Truthy(s.Get("game_running")))
            bits.Add(I18nCore.Tr("游戏运行中"));
        _detail.Text = string.Join(" · ", bits) + (detailText.Length == 0 ? "" : "\n" + detailText);

        string room = Py.S(s.Get("room"));
        string url = Py.S(s.Get("url"));
        bool hasRoom = room.Length > 0 || url.Length > 0;
        _roomCard.IsVisible = hasRoom;
        if (hasRoom)
        {
            _room.Text = room.Length == 0 ? I18nCore.Tr("陶瓦联机大厅") : room;
            _roomHint.Text = url.Length == 0
                ? "让好友在联机页选「我想当房客」,填这串邀请码。"
                : I18nCore.Tr("启动游戏 → 多人游戏 → 双击「陶瓦联机大厅」。");
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
            _ = CopyAsync(room, I18nCore.Tr("已把邀请码复制到剪贴板"));
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
                UiClass.Txt(name.Length == 0 ? I18nCore.Tr("玩家") : name, 13, bold: true).Trim(),
                UiClass.Small(vendor.Length == 0 ? (isHost ? I18nCore.Tr("房主") : I18nCore.Tr("成员")) : vendor)).VCenter(), 0, 1);
            grid.Add(UiClass.Tag(isHost ? I18nCore.Tr("房主") : I18nCore.Tr("成员"), "B.OnAccentStrong", "B.AccentStrong").VCenter(), 0, 2);
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
                Action(I18nCore.Tr("下载陶瓦联机内核"), "首次使用需要下载约 8 MB 的官方内核,之后可直接开房。", I18nCore.Tr("下载"), PrepareAsync, primary: true);
                return;
            case "idle":
                Action(I18nCore.Tr("启动联机内核"), "内核已安装,点一下即可开始联机。", I18nCore.Tr("启动"), PrepareAsync, primary: true);
                return;
            case "waiting":
                Action(I18nCore.Tr("我想当房主"), "创建房间并生成邀请码,与好友一起畅玩。", I18nCore.Tr("创建"), HostAsync, primary: true);
                Action(I18nCore.Tr("我想当房客"), I18nCore.Tr("输入房主提供的邀请码加入游戏世界。"), I18nCore.Tr("加入"), JoinAsync);
                return;
            case "host-scanning":
            case "host-starting":
                Action(I18nCore.Tr("正在扫描局域网世界"), "请启动游戏,进入单人世界,按 ESC,选择对局域网开放。", I18nCore.Tr("退出"), BackAsync);
                return;
            case "host-ok":
                Action(I18nCore.Tr("复制邀请码"), I18nCore.Tr("好友在联机页选「我想当房客」并填这串邀请码即可加入。"), I18nCore.Tr("复制"),
                    () => CopyAsync(_room.Text ?? "", I18nCore.Tr("已把邀请码复制到剪贴板")), primary: true);
                Action(I18nCore.Tr("退出"), "这会彻底关闭房间,其他房客将一并退出。", I18nCore.Tr("退出"), BackAsync);
                return;
            case "guest-connecting":
            case "guest-starting":
                Action(I18nCore.Tr("正在加入房间"),
                    Py.S(s.Get("difficulty_hint")).Length > 0 ? Py.S(s.Get("difficulty_hint")) : I18nCore.Tr("正在与房主建立连接。"),
                    I18nCore.Tr("退出"), BackAsync);
                return;
            case "guest-ok":
                Action(I18nCore.Tr("进入世界"), "启动游戏后到多人游戏双击「陶瓦联机大厅」,或点这里直接进入。", I18nCore.Tr("进入"), EnterWorldAsync, primary: true);
                Action(I18nCore.Tr("退出"), I18nCore.Tr("这不影响其他房客加入当前房间。"), I18nCore.Tr("退出"), BackAsync);
                return;
            case "unsupported":
                Action(I18nCore.Tr("当前系统不支持"), I18nCore.Tr("陶瓦联机暂未提供此架构的官方内核。"), I18nCore.Tr("了解"), () =>
                {
                    OpenUrl(ProjectHome);
                    return Task.CompletedTask;
                });
                return;
            case "installing":
            case "launching":
            case "unknown":
                Action(I18nCore.Tr("请稍候"),
                    Py.S(s.Get("label")).Length > 0 ? Py.S(s.Get("label")) : I18nCore.Tr("正在准备联机内核。"),
                    I18nCore.Tr("刷新"), ReloadAsync);
                return;
            default:
                Action(I18nCore.Tr("联机失败"),
                    Py.S(s.Get("error_hint")).Length > 0 ? Py.S(s.Get("error_hint"))
                        : (Py.S(s.Get("error")).Length > 0 ? Py.S(s.Get("error")) : "请返回后重试,或检查网络。"),
                    I18nCore.Tr("返回"), BackAsync);
                Action(I18nCore.Tr("朋友是公网就直连"), "让他把单人世界对局域网开放,并在路由映射该端口,然后填他的公网 IP:端口。", I18nCore.Tr("直连"), DirectConnectAsync);
                Action(I18nCore.Tr("重新启动内核"), "若内核已退出,点此重新拉起。", I18nCore.Tr("重启"), PrepareAsync);
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
        Toast(I18nCore.Tr("正在准备联机内核"), I18nCore.Tr("进度看下载任务"));
        await ReloadAsync();
    }

    private async Task HostAsync()
    {
        string player = await Api.TryCallAsync("terracotta_player", "Player") ?? "Player";
        var snap = await Api.TryCallAsync<PyDict>("terracotta_snapshot");
        bool gameRunning = snap != null && Py.Truthy(snap.Get("game_running"));
        if (!gameRunning)
        {
            if (!await Dlg.Confirm(I18nCore.Tr("您似乎忘记启动游戏了"),
                    $"将以「{player}」的身份开房。\n请先启动游戏,进入单人世界,按 ESC,选择「对局域网开放」。",
                    I18nCore.Tr("游戏已启动"), I18nCore.Tr("取消")))
                return;
        }
        else
        {
            Toast(I18nCore.Tr("正在开房"), $"以「{player}」的身份创建房间");
        }
        await Api.CallAsync<object>("terracotta_host");
        await ReloadAsync();
    }

    private async Task JoinAsync()
    {
        string? code = await Dlg.Prompt(I18nCore.Tr("我想当房客"), I18nCore.Tr("请输入房主提供的邀请码"), "", "U/XXXX-XXXX-XXXX-XXXX");
        if (string.IsNullOrWhiteSpace(code))
            return;
        await Api.CallAsync<object>("terracotta_join", new PyDict { ["room"] = code.Trim() });
        await ReloadAsync();
    }

    private async Task DirectConnectAsync()
    {
        string? addr = await Dlg.Prompt(I18nCore.Tr("公网直连"),
            "朋友需先对局域网开放世界,并在路由器映射该端口,然后填他的公网地址。",
            "", I18nCore.Tr("例如 1.2.3.4:25565"));
        if (string.IsNullOrWhiteSpace(addr))
            return;
        string hint = await Api.CallAsync<string>("terracotta_direct_connect", new PyDict { ["address"] = addr.Trim() }) ?? "";
        Toast(I18nCore.Tr("正在直连"), hint.Length == 0 ? I18nCore.Tr("启动后会进入该服务器。") : hint, ToastKind.Success);
        await ReloadAsync();
    }

    private async Task EnterWorldAsync()
    {
        string text = await Api.CallAsync<string>("terracotta_enter_world") ?? "";
        if (text.StartsWith("task-", StringComparison.Ordinal))
            Toast(I18nCore.Tr("正在启动游戏"), I18nCore.Tr("启动后会直接进入陶瓦联机大厅"), ToastKind.Success);
        else
            Toast(I18nCore.Tr("已加入房间"), text.Length == 0 ? I18nCore.Tr("请到多人游戏双击「陶瓦联机大厅」。") : text, ToastKind.Success);
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
        Toast(I18nCore.Tr("防火墙"), text.Length == 0 ? I18nCore.Tr("已提交规则") : text, ToastKind.Success);
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
            Toast(I18nCore.Tr("防火墙"), "请在系统设置里放行陶瓦内核的网络访问", ToastKind.Info);
        }
    }

    private async Task CopyAsync(string text, string toast)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip != null)
            await clip.SetTextAsync(text);
        Toast(I18nCore.Tr("已复制"), toast, ToastKind.Success);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            ShellOpen.Url(url); // AI2-integration:统一入口(测试进程里不真打开)
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
