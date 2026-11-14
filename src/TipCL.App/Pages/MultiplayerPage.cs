using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C6b:Windows 版 <c>Pages\MultiplayerPage.cs</c>(18815 字节 / 625 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「联机」页(导航 id = <c>multiplayer</c>,侧栏有入口,标题「联机」,图标 <c>\ue701</c>):
/// 「陶瓦联机 | 陶瓦联机 © burningtnt · 基于 EasyTier」的那一页。
///
/// 形状(逐条照抄 Windows):说明(Section + 局域网地址)/ 状态卡(圆点 + 状态 + 明细 + 防火墙三个动作)/
/// 房间卡(邀请码 + 复制)/ 操作区(按 <c>terracotta_snapshot.state</c> 现算的动作卡)/
/// 玩家列表(头像 + 名字与 vendor + 房主/成员标签)/ 节点说明(会合节点 + 项目主页页脚),共六个区块。
///
/// 数据源与调用名一字未改:<c>lan_hint</c> / <c>local_ips</c> / <c>terracotta_snapshot</c> /
/// <c>terracotta_prepare</c>(走 <c>StartTaskAsync</c>)/ <c>terracotta_host</c> / <c>terracotta_join</c> /
/// <c>terracotta_direct_connect</c> / <c>terracotta_enter_world</c> / <c>terracotta_idle</c> /
/// <c>terracotta_allow_firewall</c> / <c>terracotta_open_firewall_settings</c> / <c>terracotta_player</c> /
/// <c>terracotta_shutdown</c> / <c>task_title</c>。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>DispatcherTimer</c>(WPF <c>System.Windows.Threading</c>)→ Avalonia 的
///   <c>Avalonia.Threading.DispatcherTimer</c>;1.2 秒轮询、<c>OnShown</c> 起 / <c>OnHidden</c> 停一字未改;</item>
/// <item><c>SetResourceReference(BackgroundProperty / ForegroundProperty, key)</c> → <c>Dyn(...)</c>;</item>
/// <item><c>Visibility</c> / <c>UIElement</c> / <c>FrameworkElement</c> → <c>IsVisible</c> / <c>Control</c>
///   (显示隐藏都用既有门面 <c>Show(bool)</c>);</item>
/// <item><b>剪贴板</b>:Windows 的 <c>Clipboard.SetText</c> + 空 <c>catch</c> → mac 走**既有**注入缝
///   <c>CrashUi.CopyAsync(this, text)</c>(C2a/C3b 的同一处口子,离屏不碰真剪贴板);写进去了才 Toast,
///   失败当没发生 —— 与 Windows 的 try/catch 等价;</item>
/// <item><c>FillActions</c> 的**判据一个没改**,但写法换成对 <c>state</c> 的普通 <c>switch</c>:
///   Windows 那份是反编译器把 <c>switch (state.Length)</c> + <c>goto</c> 摊开的产物(reverse-engineering 痕迹),
///   逐字保留没有意义;这里把同一组 <c>state → 动作</c> 映射写清楚,并把"未知状态"的那一支保持成兜底;</item>
/// <item><c>OpenFirewallAsync</c> 的兜底 <c>Ui.OpenUrl("ms-settings:windowsdefender")</c> **逐字保留**:
///   它是 Windows 的设置页 URI,mac 上打不开 —— 但这一支只在后端 <c>terracotta_open_firewall_settings</c>
///   抛异常时才走,而 mac 侧那个后端自己会处理平台差异。保留原样 = 不替后端假设;
///   (若真机实测发现 mac 上点了没反应,属"如实降级",记 PROGRESS 的 C6b 小节)。</item>
/// </list>
/// </summary>
public sealed class MultiplayerPage : PageBase
{
	private const string ProjectHome = "https://github.com/burningtnt/Terracotta";

	private readonly TextBlock _state = new TextBlock
	{
		FontSize = 18.0,
		FontWeight = FontWeight.SemiBold,
	};

	private readonly TextBlock _detail = Ui.Muted("");

	private readonly TextBlock _lan = Ui.Small("");

	private readonly TextBlock _nodes = Ui.Small("");

	private readonly TextBlock _room = new TextBlock
	{
		FontSize = 21.0,
		FontWeight = FontWeight.Bold,
	};

	private readonly TextBlock _roomHint = Ui.Small("");

	private readonly Border _roomCard;

	private readonly SPanel _actions = Ui.V(10.0);

	private readonly SPanel _players = Ui.V(8.0);

	private readonly TextBlock _playersTitle = Ui.Txt(I18n.L("房间成员"), 13.0, bold: true);

	private readonly Border _dot = new Border
	{
		Width = 10.0,
		Height = 10.0,
		CornerRadius = new CornerRadius(999.0),
	};

	private readonly DispatcherTimer _timer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(1200.0),
	};

	private bool _busy;

	private bool _polling;

	private bool _autoStarted;

	private string _uiState = "";

	private string _playerSig = "";

	private string _lastState = "";

	private string _prepareTask = "";

	/// <summary>【Windows 源】<c>:71</c>:标题「联机」。</summary>
	public override string Title => I18n.L("联机");

	/// <summary>【Windows 源】<c>:73-120</c>:六个区块的 <c>BlockHost("multiplayer")</c> + 轮询计时器。</summary>
	public MultiplayerPage()
	{
		Button allowFirewall = Ui.Btn(I18n.L("允许访问"), BtnKind.Primary, delegate
		{
			PageBase.Run(AllowFirewallAsync);
		}, "\uea18");
		Button openSettings = Ui.Btn(I18n.L("打开设置"), BtnKind.Chip, delegate
		{
			PageBase.Run(OpenFirewallAsync);
		});
		Button shutdown = Ui.Btn(I18n.L("关闭内核"), BtnKind.Ghost, delegate
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.CallAsync("terracotta_shutdown");
				await ReloadAsync();
			});
		});
		Border stateCard = Ui.Card(Ui.V(10.0, Ui.H(10.0, _dot.VCenter(), _state), _detail.Wrap(), Ui.H(8.0, allowFirewall, openSettings, shutdown)), 18.0);
		Button copyRoom = Ui.Btn(I18n.L("复制邀请码"), BtnKind.Chip, delegate
		{
			Copy(_room.Text);
		}, "\ue8c8");
		_room.Dyn(TextBlock.ForegroundProperty, "B.AccentDeep");
		_roomCard = Ui.Card(Ui.V(6.0, Ui.Small(I18n.L("邀请码")), Ui.H(10.0, _room.VCenter(), copyRoom.VCenter()), _roomHint.Wrap()));
		_roomCard.IsVisible = false;
		_playersTitle.IsVisible = false;
		SPanel footer = Ui.H(10.0, Ui.Btn(I18n.L("Terracotta 项目主页"), BtnKind.Ghost, delegate
		{
			Ui.OpenUrl(ProjectHome);
		}, "\ue71b"), Ui.Small(I18n.L("Terracotta | 陶瓦联机  © burningtnt  ·  基于 EasyTier")).VCenter());
		Grid head = Ui.Section(I18n.L("陶瓦联机"), I18n.L("输入邀请码即可加入。陶瓦是 EasyTier P2P 打洞，不是 FRP 隧道；") + I18n.L("会与 HMCL 一样带上自定义会合节点。PCL 房间号不互通，局域网请用下面的地址。"));
		// 页面布局自定义(WP-U3):块内组合沿用旧 ScrollBody 的 10 间距包一层;默认全可见、顺序同旧 ScrollBody 实参
		// (「节点说明」块顺带承载项目主页页脚,收起它意味着同时隐藏页脚)
		Content = new BlockHost("multiplayer", new List<BlockSpec>
		{
			new BlockSpec { Id = "header", Title = I18n.L("说明"), Content = Ui.V(10.0, head, _lan.Wrap()) },
			new BlockSpec { Id = "state", Title = I18n.L("状态卡"), Content = stateCard },
			new BlockSpec { Id = "room", Title = I18n.L("房间卡"), Content = _roomCard },
			new BlockSpec { Id = "actions", Title = I18n.L("操作区"), Content = _actions },
			new BlockSpec { Id = "players", Title = I18n.L("玩家列表"), Content = Ui.V(10.0, _playersTitle, _players) },
			new BlockSpec { Id = "nodes", Title = I18n.L("节点说明"), Content = Ui.V(10.0, _nodes.Wrap(), footer) },
		}).Root;
		_timer.Tick += delegate
		{
			PageBase.Run(ReloadAsync);
		};
	}

	/// <summary>【Windows 源】<c>:122-126</c>:局域网地址 + 一次快照。</summary>
	protected override async Task LoadAsync()
	{
		await LoadLanAsync();
		await ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:128-132</c>:显示时开轮询,并试一次"自动准备"。</summary>
	public override void OnShown()
	{
		_timer.Start();
		PageBase.Run(MaybePrepareAsync);
	}

	/// <summary>【Windows 源】<c>:134-137</c>:隐藏时停表(不留后台轮询)。</summary>
	public override void OnHidden()
	{
		_timer.Stop();
	}

	/// <summary>【Windows 源】<c>:139-148</c>:有本机 IP 就自己拼「地址:25565」,否则用后端那句提示。</summary>
	private async Task LoadLanAsync()
	{
		string hint = (await PageBase.Api.TryCallAsync("lan_hint", new
		{
			port = 25565
		}, "")) ?? "";
		List<string> ips = (await PageBase.Api.TryCallAsync("local_ips", null, new List<string>())) ?? new List<string>();
		_lan.Text = ((ips.Count > 0) ? I18n.L("本机局域网地址：{0}", string.Join(I18n.L("、"), ips.Select((string ip) => ip + ":25565"))) : hint);
		// 【Avalonia 差异】WPF 的 ToolTip 是属性;Avalonia 是附加属性,本仓统一用 UiExt.Tip(...) 这个门面
		if (!string.IsNullOrWhiteSpace(hint))
		{
			_lan.Tip(hint);
		}
	}

	/// <summary>【Windows 源】<c>:150-169</c>:一次只跑一个快照(轮询与手动刷新可能撞上)。</summary>
	private async Task ReloadAsync()
	{
		if (_polling)
		{
			return;
		}

		_polling = true;
		try
		{
			TerracottaSnap? snap = await PageBase.Api.TryCallAsync<TerracottaSnap>("terracotta_snapshot");
			if (snap != null)
			{
				Render(snap);
			}
		}
		finally
		{
			_polling = false;
		}
	}

	/// <summary>【Windows 源】<c>:171-182</c>:支持、已装、没在跑 ⇒ 自动准备一次(每次显示只自动一次)。</summary>
	private async Task MaybePrepareAsync()
	{
		if (!_autoStarted && !_busy)
		{
			TerracottaSnap? snap = await PageBase.Api.TryCallAsync<TerracottaSnap>("terracotta_snapshot");
			if (snap != null && snap.Supported && snap.Installed && !snap.Running)
			{
				_autoStarted = true;
				await PrepareAsync(null);
			}
		}
	}

	/// <summary>【Windows 源】<c>:184-260</c>:状态机 → 文案 / 圆点颜色 / 明细 / 房间卡 / 节点 / 动作 / 玩家。</summary>
	private void Render(TerracottaSnap snap)
	{
		string state = (string.IsNullOrEmpty(snap.State) ? "missing" : snap.State);
		// 【Windows 源】:正在"忙"而状态还是 missing/idle ⇒ 显示成 installing;
		// 状态已经落到 waiting / host-ok / guest-ok 时把 busy 收回来
		bool busy = _busy && (state == "missing" || state == "idle");
		if (busy)
		{
			state = "installing";
		}
		else if (state == "waiting" || state == "host-ok" || state == "guest-ok")
		{
			_busy = false;
		}

		_state.Text = ((state == "exception" || state == "fatal") ? I18n.L("加入失败") : (string.IsNullOrWhiteSpace(snap.Label) ? state : snap.Label));
		_dot.Dyn(Border.BackgroundProperty, DotBrush(state));
		string detail = (!string.IsNullOrWhiteSpace(snap.Error) ? snap.Error : snap.Label);
		if (!string.IsNullOrWhiteSpace(snap.ErrorHint))
		{
			detail = (snap.Error + "\n" + snap.ErrorHint).Trim();
		}
		else if (!string.IsNullOrWhiteSpace(snap.DifficultyHint))
		{
			detail = (snap.Label + "\n" + snap.DifficultyHint).Trim();
		}

		List<string> facts = new List<string>();
		if (!snap.Supported)
		{
			facts.Add(I18n.L("当前系统架构不支持"));
		}

		facts.Add(snap.Installed ? I18n.L("内核已安装") : I18n.L("内核未安装"));
		facts.Add(snap.Running ? I18n.L("内核运行中") : I18n.L("内核未运行"));
		if (!string.IsNullOrWhiteSpace(snap.Player))
		{
			facts.Add(I18n.L("玩家 ") + snap.Player);
		}

		if (snap.GameRunning)
		{
			facts.Add(I18n.L("游戏运行中"));
		}

		_detail.Text = string.Join(" · ", facts) + (string.IsNullOrWhiteSpace(detail) ? "" : ("\n" + detail));
		bool hasRoom = !string.IsNullOrWhiteSpace(snap.Room) || !string.IsNullOrWhiteSpace(snap.Url);
		_roomCard.Show(hasRoom);
		if (hasRoom)
		{
			_room.Text = (string.IsNullOrWhiteSpace(snap.Room) ? I18n.L("陶瓦联机大厅") : snap.Room);
			_roomHint.Text = (string.IsNullOrWhiteSpace(snap.Url) ? I18n.L("让好友在联机页选「我想当房客」，填这串邀请码。") : I18n.L("启动游戏 → 多人游戏 → 双击「陶瓦联机大厅」。"));
		}

		_nodes.Text = ((snap.Nodes.Count == 0) ? "" : I18n.L("会合节点 {0} 个：{1}", snap.Nodes.Count, string.Join("  ", snap.Nodes.Take(3))));
		if (state != _uiState)
		{
			_uiState = state;
			FillActions(state, snap);
		}

		RenderPlayers(snap.Profiles);
		// 【Windows 源】:刚变 host-ok 且拿到房间号 ⇒ 自动把邀请码复制一份
		if (!string.IsNullOrWhiteSpace(snap.Room) && state == "host-ok" && _lastState != "host-ok")
		{
			Copy(snap.Room, I18n.L("已把邀请码复制到剪贴板"));
		}

		_lastState = state;
	}

	/// <summary>【Windows 源】<c>:262-281</c>:状态 → 圆点颜色键。</summary>
	private static string DotBrush(string state)
	{
		switch (state)
		{
		case "host-ok":
		case "waiting":
		case "guest-ok":
			return "B.Ok";
		case "idle":
			return "B.Accent";
		case "exception":
		case "fatal":
		case "unsupported":
			return "B.Danger";
		case "missing":
			return "B.Warn";
		default:
			return "B.InkFaint";
		}
	}

	/// <summary>【Windows 源】<c>:283-303</c>:玩家签名没变就不重画(轮询每 1.2 秒一次,别抖)。</summary>
	private void RenderPlayers(List<TerracottaProfile> profiles)
	{
		string signature = string.Join("|", profiles.Select((TerracottaProfile p) => p.Name + "/" + p.Kind + "/" + p.Vendor));
		if (signature == _playerSig)
		{
			return;
		}

		_playerSig = signature;
		_players.Children.Clear();
		_playersTitle.Show(profiles.Count > 0);
		foreach (TerracottaProfile profile in profiles)
		{
			bool host = string.Equals(profile.Kind, "HOST", StringComparison.OrdinalIgnoreCase);
			Grid grid = Ui.G(null, "Auto,*,Auto");
			grid.Add(new ThumbTile(profile.Name, 36.0, 18.0).M(0.0, 0.0, 12.0), 0);
			grid.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(profile.Name) ? I18n.L("玩家") : profile.Name, 13.0, bold: true).Trim(), Ui.Small(string.IsNullOrWhiteSpace(profile.Vendor) ? (host ? I18n.L("房主") : I18n.L("成员")) : profile.Vendor)).VCenter(), 0, 1);
			grid.Add(Ui.Tag(host ? I18n.L("房主") : I18n.L("成员"), "B.OnAccentStrong", "B.AccentStrong").VCenter(), 0, 2);
			_players.Children.Add(Ui.Card(grid, 12.0));
		}

		Motion.Stagger(_players, 18.0, 190.0, 8.0);
	}

	/// <summary>
	/// 【Windows 源】<c>:305-451</c>:<c>state</c> → 操作区那几张卡。
	/// 判据与顺序逐条照抄;**写法**从反编译器的 <c>switch (state.Length)</c> + <c>goto</c> 换成对状态名的普通 <c>switch</c>
	/// (同一组映射,读得出来;未列出的状态落到最后那一组兜底动作 —— Windows 也是这个落点)。
	/// </summary>
	private void FillActions(string state, TerracottaSnap snap)
	{
		_actions.Children.Clear();
		if (state == null)
		{
			return;
		}

		switch (state)
		{
		case "missing":
			Action(I18n.L("瓦"), I18n.L("下载陶瓦联机内核"), I18n.L("首次使用需要下载约 8 MB 的官方内核，之后可直接开房。"), I18n.L("下载"), () => PrepareAsync(_actions), primary: true);
			return;
		case "waiting":
			Action(I18n.L("房"), I18n.L("我想当房主"), I18n.L("创建房间并生成邀请码，与好友一起畅玩。"), I18n.L("创建"), HostAsync, primary: true);
			Action(I18n.L("客"), I18n.L("我想当房客"), I18n.L("输入房主提供的邀请码加入游戏世界。"), I18n.L("加入"), JoinAsync);
			return;
		case "host-ok":
			Action(I18n.L("复"), I18n.L("复制邀请码"), I18n.L("好友在联机页选「我想当房客」并填这串邀请码即可加入。"), I18n.L("复制"), delegate
			{
				Copy(_room.Text, I18n.L("已把邀请码复制到剪贴板"));
				return Task.CompletedTask;
			}, primary: true);
			Action(I18n.L("返"), I18n.L("退出"), I18n.L("这会彻底关闭房间，其他房客将一并退出。"), I18n.L("退出"), BackAsync);
			return;
		case "host-scanning":
		case "host-starting":
			Action(I18n.L("扫"), I18n.L("正在扫描局域网世界"), I18n.L("请启动游戏，进入单人世界，按 ESC，选择对局域网开放。"), I18n.L("退出"), BackAsync);
			return;
		case "unsupported":
			Action("!", I18n.L("当前系统不支持"), I18n.L("陶瓦联机暂未提供此架构的官方内核。"), I18n.L("了解"), delegate
			{
				Ui.OpenUrl(ProjectHome);
				return Task.CompletedTask;
			});
			return;
		case "idle":
			Action("▶", I18n.L("启动联机内核"), I18n.L("内核已安装，点一下即可开始联机。"), I18n.L("启动"), () => PrepareAsync(_actions), primary: true);
			return;
		case "installing":
		case "unknown":
		case "launching":
			Action("...", I18n.L("请稍候"), string.IsNullOrWhiteSpace(snap.Label) ? I18n.L("正在准备联机内核。") : snap.Label, I18n.L("刷新"), ReloadAsync);
			return;
		case "guest-connecting":
		case "guest-starting":
			Action(I18n.L("连"), I18n.L("正在加入房间"), string.IsNullOrWhiteSpace(snap.DifficultyHint) ? I18n.L("正在与房主建立连接。") : snap.DifficultyHint, I18n.L("退出"), BackAsync);
			return;
		case "guest-ok":
			Action(I18n.L("进"), I18n.L("进入世界"), I18n.L("启动游戏后到多人游戏双击「陶瓦联机大厅」，或点这里直接进入。"), I18n.L("进入"), EnterWorldAsync, primary: true);
			Action(I18n.L("返"), I18n.L("退出"), I18n.L("这不影响其他房客加入当前房间。"), I18n.L("退出"), BackAsync);
			return;
		default:
			// 【Windows 源】:exception / fatal 以及任何没列出来的状态都落到这三条兜底
			Action("!", I18n.L("联机失败"), (!string.IsNullOrWhiteSpace(snap.ErrorHint)) ? snap.ErrorHint : (string.IsNullOrWhiteSpace(snap.Error) ? I18n.L("请返回后重试，或检查网络。") : snap.Error), I18n.L("返回"), BackAsync);
			Action(I18n.L("直"), I18n.L("朋友是公网就直连"), I18n.L("让他把单人世界对局域网开放，并在路由映射该端口，然后填他的公网 IP:端口。"), I18n.L("直连"), DirectConnectAsync);
			Action(I18n.L("启"), I18n.L("重新启动内核"), I18n.L("若内核已退出，点此重新拉起。"), I18n.L("重启"), () => PrepareAsync(_actions));
			return;
		}
	}

	/// <summary>【Windows 源】<c>:453-467</c>:一张动作卡(字母方块 + 标题与说明 + 按钮)。</summary>
	private void Action(string letter, string title, string desc, string buttonText, Func<Task> act, bool primary = false)
	{
		Button button = Ui.Btn(buttonText, (!primary) ? BtnKind.Normal : BtnKind.Primary, delegate
		{
			PageBase.Run(act);
		});
		button.MinWidth = 96.0;
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(new ThumbTile(letter, 46.0, 12.0).M(0.0, 0.0, 14.0), 0);
		grid.Add(Ui.V(2.0, Ui.Txt(title, 13.5, bold: true), Ui.Small(desc).Wrap()).VCenter(), 0, 1);
		grid.Add(button.VCenter().M(14.0, 0.0, 0.0, 0.0), 0, 2);
		Border card = Ui.Card(grid, 14.0);
		Motion.HoverLift(card, 1.004, 1.0, 16.0);
		_actions.Children.Add(card);
	}

	/// <summary>【Windows 源】<c>:469-482</c>:准备内核(已经在准备就不重入);有锚点且开了飞行才飞一下。</summary>
	private async Task PrepareAsync(Control? anchor)
	{
		if (!_busy)
		{
			_busy = true;
			_prepareTask = await PageBase.Api.StartTaskAsync("terracotta_prepare");
			if (anchor != null && SettingsPage.FlyEnabled)
			{
				PageBase.Win?.FlyToTasks(anchor, I18n.L("联"));
			}

			PageBase.Toast(I18n.L("正在准备联机内核"), I18n.L("进度看下载任务"));
			await ReloadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:484-501</c>:没在游戏里先问一句「您似乎忘记启动游戏了」。</summary>
	private async Task HostAsync()
	{
		string player = await PageBase.Api.TryCallAsync("terracotta_player", null, "Player");
		TerracottaSnap? snap = await PageBase.Api.TryCallAsync<TerracottaSnap>("terracotta_snapshot");
		if (snap != null && !snap.GameRunning)
		{
			if (!await Dlg.Confirm(I18n.L("您似乎忘记启动游戏了"), I18n.L("将以「{0}」的身份开房。\n请先启动游戏,进入单人世界,按 ESC,选择「对局域网开放」。", player), I18n.L("游戏已启动"), I18n.L("取消")))
			{
				return;
			}
		}
		else
		{
			PageBase.Toast(I18n.L("正在开房"), I18n.L("以「{0}」的身份创建房间", player));
		}

		await PageBase.Api.CallAsync("terracotta_host");
		await ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:503-514</c>:问邀请码(<c>Dlg.Prompt</c>),空着就什么都不做。</summary>
	private async Task JoinAsync()
	{
		string? room = await Dlg.Prompt(I18n.L("我想当房客"), I18n.L("请输入房主提供的邀请码"), "", "U/XXXX-XXXX-XXXX-XXXX");
		if (!string.IsNullOrWhiteSpace(room))
		{
			await PageBase.Api.CallAsync("terracotta_join", new
			{
				room = room.Trim()
			});
			await ReloadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:516-528</c>:公网直连(地址交给后端,Toast 用后端回的那句)。</summary>
	private async Task DirectConnectAsync()
	{
		string? address = await Dlg.Prompt(I18n.L("公网直连"), I18n.L("朋友需先对局域网开放世界，并在路由器映射该端口，然后填他的公网地址。"), "", I18n.L("例如 1.2.3.4:25565"));
		if (!string.IsNullOrWhiteSpace(address))
		{
			string message = await PageBase.Api.CallAsync<string>("terracotta_direct_connect", new
			{
				address = address.Trim()
			});
			PageBase.Toast(I18n.L("正在直连"), string.IsNullOrWhiteSpace(message) ? I18n.L("启动后会进入该服务器。") : message, ToastKind.Success);
			await ReloadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:530-546</c>:<c>task-</c> 开头 = 起了一个启动任务(飞一下 + 另两句 Toast)。</summary>
	private async Task EnterWorldAsync()
	{
		string result = (await PageBase.Api.CallAsync<string>("terracotta_enter_world")) ?? "";
		if (result.StartsWith("task-", StringComparison.Ordinal))
		{
			if (SettingsPage.FlyEnabled)
			{
				PageBase.Win?.FlyToTasks(_actions, I18n.L("进"));
			}

			PageBase.Toast(I18n.L("正在启动游戏"), I18n.L("启动后会直接进入陶瓦联机大厅"), ToastKind.Success);
		}
		else
		{
			PageBase.Toast(I18n.L("已加入房间"), string.IsNullOrWhiteSpace(result) ? I18n.L("请到多人游戏双击「陶瓦联机大厅」。") : result, ToastKind.Success);
		}

		await ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:548-552</c>:退出 = 内核回 idle 再刷一次。</summary>
	private async Task BackAsync()
	{
		await PageBase.Api.CallAsync("terracotta_idle");
		await ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:554-558</c>:提交防火墙规则,Toast 用后端回的那句。</summary>
	private async Task AllowFirewallAsync()
	{
		string message = await PageBase.Api.CallAsync<string>("terracotta_allow_firewall");
		PageBase.Toast(I18n.L("防火墙"), string.IsNullOrWhiteSpace(message) ? I18n.L("已提交规则") : message, ToastKind.Success);
	}

	/// <summary>【Windows 源】<c>:560-570</c>:打开系统防火墙设置;后端这一手抛 <c>BridgeCallException</c> 才退回 URI 那条路。</summary>
	private async Task OpenFirewallAsync()
	{
		try
		{
			await PageBase.Api.CallAsync("terracotta_open_firewall_settings");
		}
		catch (BridgeCallException)
		{
			Ui.OpenUrl("ms-settings:windowsdefender");
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:572-591</c>:把文本放进剪贴板 + Toast;空串直接返回、失败静默(Windows 是空 catch)。
	/// mac 侧走**既有**的剪贴板注入缝 <c>CrashUi.CopyAsync</c>(离屏不碰真剪贴板),写进去了才 Toast。
	/// </summary>
	private void Copy(string? text, string? title = null)
	{
		string trimmed = (text ?? "").Trim();
		if (trimmed.Length == 0)
		{
			return;
		}

		string toast = title ?? I18n.L("已复制");
		PageBase.Run(async delegate
		{
			if (await CrashUi.CopyAsync(this, trimmed))
			{
				PageBase.Toast(toast, trimmed, ToastKind.Success);
			}
		});
	}

	/// <summary>【Windows 源】<c>:593-624</c>:任务结束且(是准备任务,或标题里有「陶瓦」)⇒ 收 busy 并刷新。</summary>
	public override void OnEvent(BridgeEvent ev)
	{
		if (ev.Event != "finished")
		{
			return;
		}

		PageBase.Run(async delegate
		{
			string? title = TaskStore.Get(ev.TaskId)?.Title;
			if (title == null)
			{
				title = (await PageBase.Api.TryCallAsync("task_title", new
				{
					task_id = ev.TaskId
				}, "")) ?? "";
			}

			string label = title;
			if (ev.TaskId == _prepareTask || label.Contains("陶瓦", StringComparison.Ordinal))
			{
				_busy = false;
				if (ev.TaskId == _prepareTask)
				{
					_prepareTask = "";
				}

				if (!ev.Success)
				{
					PageBase.Toast(string.IsNullOrWhiteSpace(label) ? I18n.L("联机内核") : label, ev.Message, ToastKind.Error);
				}

				await ReloadAsync();
			}
		});
	}
}
