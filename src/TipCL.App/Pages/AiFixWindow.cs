using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C8b(第 2 步):Windows 版 <c>Pages\AiFixWindow.cs</c>(22,795 字节 / 810 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「AI 修复」整窗:把一份 <see cref="CrashReport"/> 交给 AI 助手,在窗内流式对话、跑计划、确认与追问。
///
/// **本文件分块落地**(Lead 批准:跨轮、留盘、允许中间态红)。块边界写在文件末尾的标记处。
/// 移入 <c>src\TipCL.App\Pages\</c> 后**整树编译 0 错 0 警**(块 1–3;块 4 的接线见 <c>AiFixLauncher.cs</c>)——
/// 从这一步起它不再是 <c>artifacts\wip\</c> 里那份"只在本机、从未编译过"的草稿。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><b>窗口外壳</b>:WPF <c>WindowStyle.None</c> + <c>System.Windows.Shell.WindowChrome</c>
///   (CaptionHeight / ResizeBorderThickness / GlassFrameThickness / UseAeroCaptionButtons)
///   → Avalonia 用 <c>SystemDecorations = None</c> + <c>ExtendClientAreaToDecorationsHint</c> +
///   <c>ExtendClientAreaChromeHints = NoChrome</c>;标题栏那 40px 自己画(**两平台都是自己画**),
///   所以 `WindowChrome.SetIsHitTestVisibleInChrome(button, true)` 这一句**在 mac 不需要**
///   (没有 chrome 层,按钮本来就能点) —— 这不是降级,是换了实现方式;</item>
/// <item><c>WindowStyle.None</c> → <c>SystemDecorations.None</c>;<c>ResizeMode.CanResize</c> → <c>CanResize = true</c>;</item>
/// <item><c>SetResourceReference(Control.XxxProperty, "B.Yyy")</c> → <c>Dyn(...)</c>(本仓统一口径);</item>
/// <item><c>Style = Ui.S("Btn.CaptionClose")</c> → <c>StyleKeys.Apply(button, "Btn.CaptionClose")</c>
///   (**已当场核**:该键在 <c>Themes\StyleKeys.cs</c> 的表里、样式本体在 <c>Themes\Controls.axaml</c> 的
///   <c>BtnCaptionClose</c> 里都存在,所以不需要退化到近似样式);</item>
/// <item><b><c>ChatInput</c></b>(Windows 的复合输入框:多行 + Enter 发送 / Shift+Enter 换行 + <c>Submitted</c> 事件)
///   → mac 用既有 <c>Ui.Input</c> 的多行 <c>TextBox</c> + <c>KeyDown</c> 里判 Enter/Shift(**如实登记为行为差异**:
///   输入框是普通多行框,没有原控件的其它装饰);</item>
/// <item><b><c>SmoothScroll</c></b>(Windows 的平滑滚动容器)→ mac 用既有 <c>Ui.Scroll</c>
///   (**如实登记**:没有平滑滚动动画,只有滚动);</item>
/// <item><c>FrameworkElement</c> → <c>Control</c>;图标走既有 <c>Ui.Glyph</c>/<c>AiIcons</c>;
///   后续块里的 <c>RichTextBox</c> → **复用 C8a 的 <c>MdView</c>**(不新写第二套)、<c>DropShadowEffect</c> → <c>BoxShadow</c>。</item>
/// </list>
/// </summary>
internal sealed class AiFixWindow : Window
{
	private readonly CrashReport _report;

	private readonly SPanel _messages = Ui.V(12.0);

	// 【如实差异】Windows 的 ChatInput 是复合控件;mac 用既有 Ui.Input(多行:AcceptsReturn 在构造函数里开)
	private readonly TextBox _input = Ui.Input("", "", double.NaN);

	private readonly Button _send;

	private readonly TextBlock _status = Ui.Small("");

	private readonly StringBuilder _stream = new StringBuilder();

	private readonly StringBuilder _round = new StringBuilder();

	private readonly Dictionary<string, string> _aiPendingTasks = new Dictionary<string, string>();

	/// <summary>工具行 `ToolLine`:**按工具名复用**(Windows <c>:33</c>)。</summary>
	private readonly Dictionary<string, ToolLine> _toolLines = new Dictionary<string, ToolLine>();

	/// <summary>后台任务 id → 工具行(Windows <c>:35</c>):`tool_done` 之后进度/完成事件落到这一行上。</summary>
	private readonly Dictionary<string, ToolLine> _taskLines = new Dictionary<string, ToolLine>();

	/// <summary>当前这一轮的助手气泡(【Windows 源】<c>:39</c>);失败时把它从消息列表里摘掉。</summary>
	private Bubble? _roundBubble;

	/// <summary>当前那张计划卡(【Windows 源】<c>:41</c>);同一轮只留一张,旧的先摘掉。
	/// `PlanCard` 已由 c4-pages 落地(`04fcd76`),在 `TipCL.Pages` ⇒ 与本体同命名空间,无需 using。</summary>
	private PlanCard? _planCard;

	private bool _busy;

	private bool _kicked;

	private readonly string _chatId;

	private readonly BridgeClient? _client;

	public AiFixWindow(CrashReport report)
	{
		_report = report;
		this.Dyn(ForegroundProperty, "B.Ink");
		_chatId = $"fix-{DateTime.Now:yyyyMMddHHmmss}";
		Title = I18n.L("AI 修复");
		Width = 520.0;
		Height = 660.0;
		MinWidth = 430.0;
		MinHeight = 480.0;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		// 【mac 差异】WPF 的 WindowStyle.None + WindowChrome → Avalonia 的三件套
		SystemDecorations = SystemDecorations.None;
		ExtendClientAreaToDecorationsHint = true;
		// 名字冲突:属性名与枚举类型同名 ⇒ 枚举写全名(CS0176);命名空间按既有用例 MainWindow.cs:163(Avalonia.Platform)
		ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
		_input.AcceptsReturn = true;
		CanResize = true;
		UseLayoutRounding = true;
		this.Dyn(BackgroundProperty, "B.Canvas");
		this.Dyn(BorderBrushProperty, "B.Line");
		BorderThickness = new Avalonia.Thickness(1.0);
		Button close = new Button
		{
			Content = "\ue8bb",
		};
		StyleKeys.Apply(close, "Btn.CaptionClose");
		close.Click += delegate
		{
			Close();
		};
		Grid titleRow = Ui.G(null, "*,Auto")
			.Add(Ui.Txt(Title ?? "", 13.5, bold: true, "B.Ink").M(14.0, 0.0, 0.0, 0.0).VCenter(), 0)
			.Add(close, 0, 1);
		Border titleBar = new Border
		{
			Height = 40.0,
			Child = titleRow,
		};
		titleBar.Dyn(BackgroundProperty, "B.Chrome");
		titleBar.Dyn(BorderBrushProperty, "B.LineSoft");
		titleBar.BorderThickness = new Avalonia.Thickness(0.0, 0.0, 0.0, 1.0);
		_send = Ui.Btn(I18n.L("发送"), BtnKind.Primary, OnMainButtonClick, "\ue724");
		_input.Tag = I18n.L("补充崩溃前做了什么、装了什么...  Enter 发送,Shift+Enter 换行");
		// 【mac 差异】ChatInput.Submitted → 多行 TextBox 的 KeyDown(Enter 发送 / Shift+Enter 换行)
		_input.KeyDown += OnInputKeyDown;
		_input.PropertyChanged += delegate
		{
			SyncMainButton();
		};
		ScrollViewer scroll = Ui.Scroll(_messages.M(4.0, 4.0, 10.0, 4.0));
		Border inputCard = Ui.Card(Ui.V(6.0, _status, Ui.H(8.0, _input, _send)), 12.0);
		Grid content = Ui.G("Auto,*,Auto")
			.Add(titleBar, 0)
			.Add(Ui.Card(scroll, 10.0).M(12.0, 10.0, 12.0, 4.0), 1)
			.Add(inputCard.M(12.0, 0.0, 12.0, 12.0), 2);
		Content = content;
		SyncMainButton();
		Loaded += delegate
		{
			PageBase.Run(KickOffAsync);
		};
		_client = AppServices.Client;
		if (_client != null)
		{
			_client.EventReceived += OnBridgeEvent;
		}
	}

	/// <summary>【mac 差异】替代 Windows <c>ChatInput</c> 的 Enter 发送 / Shift+Enter 换行。</summary>
	private void OnInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
	{
		if (e.Key != Avalonia.Input.Key.Enter)
		{
			return;
		}

		if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0)
		{
			return;
		}

		e.Handled = true;
		string text = _input.Text ?? "";
		if (text.Trim().Length > 0)
		{
			PageBase.Run(() => SendTextAsync(text));
		}
	}

	private Task KickOffAsync()
	{
		if (_kicked)
		{
			return Task.CompletedTask;
		}

		_kicked = true;
		return SendTextAsync(I18n.L("帮我看看游戏为什么闪退"));
	}

	private async Task<string> InterfaceLabelAsync()
	{
		Dictionary<string, JsonElement> s = (await AppServices.Client.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		string mode = (s.TryGetValue("ai_mode", out JsonElement m) && m.ValueKind == JsonValueKind.String) ? (m.GetString() ?? "builtin") : "builtin";
		if (mode != "custom")
		{
			return I18n.L("内置网关");
		}

		string model = (s.TryGetValue("ai_model", out JsonElement mm) && mm.ValueKind == JsonValueKind.String) ? (mm.GetString() ?? "") : "";
		return string.Format(I18n.L("自定义接口({0})"), (model.Length > 0) ? model : I18n.L("未命名模型"));
	}

	// ==== 块 2:崩溃上下文的后半 + 主按钮/发送/停止/忙态(照 Windows 150-309 行)====

	/// <summary>截断到 <paramref name="n"/> 个字符,超长补一句「...(已截断)」(Windows 同名局部函数)。
	/// 【mac 差异 · 仅此一处】Windows 里它是 <see cref="BuildCrashContext"/> 的**局部函数**;
	/// 提到类成员后与 Avalonia 的 <c>Visual.Clip</c>(裁剪几何)同名 ⇒ 加 <c>new</c> 明确"我是另一个东西",
	/// **不改名**(成员名与 Windows 一字不差这条比消警告更重要)。</summary>
	private new static string Clip(string? s, int n)
	{
		string text = (s ?? "").Trim();
		if (text.Length > n)
		{
			return text.Substring(0, n) + I18n.L("...(已截断)");
		}

		return text;
	}

	/// <summary>【Windows 源】<c>:144-202</c> 的同名方法(静态、可测):把崩溃现场拼成给 AI 的上下文。</summary>
	internal static string BuildCrashContext(CrashReport r, string iface)
	{
		List<string> parts = new List<string> { I18n.L("【启动器注入 · 对用户不可见】本次对话的崩溃上下文:") };
		List<string> facts = new List<string>();
		if (!string.IsNullOrWhiteSpace(r.Instance))
		{
			facts.Add(string.Format(I18n.L("实例:{0}"), r.Instance));
		}

		if (!string.IsNullOrWhiteSpace(r.Version))
		{
			facts.Add(string.Format(I18n.L("版本:{0}"), r.Version));
		}

		if (r.ExitCode.HasValue)
		{
			int code = r.ExitCode.GetValueOrDefault();
			string hint = string.IsNullOrWhiteSpace(r.ExitHint) ? "" : string.Format(I18n.L("({0})"), r.ExitHint);
			facts.Add(string.Format(I18n.L("退出码:{0}{1}"), code, hint));
		}

		if (facts.Count > 0)
		{
			parts.Add(string.Join("\n", facts));
		}

		(string Text, string Label, int Limit)[] sections = new(string, string, int)[3]
		{
			(r.Headline, I18n.L("现象"), 400),
			(r.Summary, I18n.L("摘要"), 600),
			(r.Detail, I18n.L("详细信息"), 4000),
		};
		foreach ((string text, string label, int limit) in sections)
		{
			string clipped = Clip(text, limit);
			if (clipped.Length > 0)
			{
				parts.Add(label + I18n.L(":") + "\n" + clipped);
			}
		}

		if (!string.IsNullOrWhiteSpace(r.DirectFile))
		{
			parts.Add(string.Format(I18n.L("完整日志文件:{0}"), r.DirectFile));
		}

		parts.Add(I18n.L("日志自查提醒:上面的日志只是截断摘录。你带有读取日志和检查实例的工具(get_latest_log、get_crash_report、read_artifact、inspect_mod、list_mods 等),请主动调用工具读取完整日志核实原因,不要以「没有日志」「无法访问日志」为由拒绝分析。"));
		parts.Add(string.Format(I18n.L("接口状态:当前对话通过启动器内置的{0}接入,接口正常可用。不要声称「没有配置接口 / 接口不可用」。"), iface));
		parts.Add(I18n.L("如果问题能通过改配置、禁用模组等方式修复,请直接动手(写操作会先征求我同意);需要更多上下文就自己读日志文件。边查边说:有阶段性发现或换步骤时,先用一两句话告诉我,再继续调用工具。最后用一句话给我结论和下一步建议。"));
		return string.Join("\n\n", parts);
	}

	private void OnMainButtonClick(object? sender, RoutedEventArgs e)
	{
		string text = (_input.Text ?? "").Trim();
		if (text.Length > 0)
		{
			PageBase.Run(() => SendTextAsync(text));
		}
		else if (_busy)
		{
			PageBase.Run(StopAsync);
		}
	}

	/// <summary>主按钮在「发送」与「停止」之间切换:有输入 ⇒ 发送;没输入且忙 ⇒ 停止。</summary>
	private void SyncMainButton()
	{
		bool hasText = !string.IsNullOrWhiteSpace(_input.Text);
		bool stopMode = _busy && !hasText;
		_send.Content = Ui.H(6.0, Ui.Glyph(stopMode ? "\ue71a" : "\ue724", 13.0), Ui.Txt(stopMode ? I18n.L("停止") : I18n.L("发送")));
		_send.IsEnabled = hasText || _busy;
	}

	/// <summary>气泡上的「重发」回调(C8a 的 <c>Bubble</c> 第三参是 <c>Action&lt;string&gt;</c> ⇒ 原样可用)。</summary>
	private void RetryFrom(string text)
	{
		PageBase.Run(() => SendTextAsync(text));
	}

	private async Task SendTextAsync(string raw, bool echo = true)
	{
		string text = (raw ?? "").Trim();
		if (text.Length == 0)
		{
			return;
		}

		if (_busy)
		{
			// 忙的时候:先试"插队"(ai_steer)
			JsonElement steer = await AppServices.Client.TryCallAsync<JsonElement>("ai_steer", new { text });
			if (steer.ValueKind == JsonValueKind.Object && steer.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.True)
			{
				if (echo)
				{
					_input.Text = "";
					Add(new Bubble("user", text, RetryFrom));
					_status.Text = I18n.L("已插队") + " · " + I18n.L("这句话会立刻交给正在运行的助手");
					SyncMainButton();
				}

				return;
			}
		}

		if (echo)
		{
			_input.Text = "";
		}

		SetBusy(on: true);
		_status.Text = I18n.L("思考中...");
		if (echo)
		{
			Add(new Bubble("user", text, RetryFrom));
		}

		_stream.Clear();
		_roundBubble = OpenRound();
		Dictionary<string, object?> launch = new Dictionary<string, object>
		{
			["instance"] = _report.Instance ?? "",
			["username"] = (AppServices.Window as MainWindow)?.Prefs.LastUser ?? "",
		};
		string context = BuildCrashContext(_report, await InterfaceLabelAsync());
		OpResult? sent = await AppServices.Client.TryCallAsync<OpResult>("ai_send", new
		{
			text,
			chat_id = _chatId,
			launch,
			context,
		});
		if (sent == null || !sent.Ok)
		{
			string message = sent?.Message ?? "";
			string shown = (message.Length > 0) ? message : I18n.L("后端没有响应,请稍后再试");
			if (_roundBubble != null)
			{
				_messages.Children.Remove(_roundBubble);
				_roundBubble = null;
			}

			Add(new Bubble("error", shown, RetryFrom));
			ResetRunState();
			_status.Text = shown;
		}
	}

	private async Task StopAsync()
	{
		JsonElement r = await AppServices.Client.TryCallAsync<JsonElement>("ai_stop");
		if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("busy", out JsonElement busy) || busy.ValueKind != JsonValueKind.True)
		{
			ResetRunState();
		}

		_status.Text = I18n.L("已停止");
	}

	private void SetBusy(bool on)
	{
		_busy = on;
		SyncMainButton();
	}

	// ==== 块 3 第 1 批:Windows 309-403 的机械成员(ResetRunState / OpenRound / CloseRound / Add / ScrollDown)====

	private void ResetRunState()
	{
		_roundBubble = null;
		_planCard = null;
		_round.Clear();
		_stream.Clear();
		_status.Text = "";
		SetBusy(on: false);
	}

	/// <summary>开一轮助手气泡(Windows <c>:319-326</c>)。</summary>
	private Bubble OpenRound(string? thinking = null)
	{
		_round.Clear();
		Bubble bubble = new Bubble("assistant", "...", RetryFrom);
		bubble.SetThinking(thinking ?? I18n.L("正在想..."));
		Add(bubble);
		return bubble;
	}

	/// <summary>收一轮(Windows <c>:328-336</c>):把流式累积的文本落到气泡上。</summary>
	private void CloseRound()
	{
		if (_roundBubble != null)
		{
			_roundBubble.SetText(_round.ToString());
			_roundBubble = null;
			_round.Clear();
		}
	}

	// ==== 块 3 第 2 批:OnBridgeEvent + Mine + ChatIdOf(Windows 338-507,逐字搬)====
	//
	// 【合并在这一次搬入时做的】块 3 第 1 批曾按用法**猜**过一版 `Add` / `ScrollDown`(整窗从未编译过,
	// 猜的那两版与这里的逐字版**成员签名重复 ⇒ CS0111**)。既然这里已经逐字读到了 Windows 的原文,
	// 就把猜的那两版删掉,**只留逐字版**(不留猜的),语义按 Windows 原文走。

	/// <summary>
	/// 桥事件分发(【Windows 源】<c>:338-488</c>)。原文按"事件名长度 + 首字符"分层 switch
	/// (长度 8:`ai.delta`/`progress`/`finished`;长度 7:`ai.done`/`ai.fail`;长度 9:`ai.status`;
	/// 长度 10:`ai.confirm`;长度 6:`ai.ask`) —— **这个形状照搬**,别改成字符串比较表:
	/// 它是有意的"先按长度分流"写法,diff 要能对上。
	/// 【mac 差异】<c>base.Dispatcher.CheckAccess()/BeginInvoke((Action)…)</c> → Avalonia 的
	/// <c>Dispatcher.UIThread.CheckAccess()/Post(…)</c>。
	/// </summary>
	private void OnBridgeEvent(object? sender, BridgeEvent ev)
	{
		if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
		{
			Avalonia.Threading.Dispatcher.UIThread.Post(delegate
			{
				OnBridgeEvent(sender, ev);
			});
			return;
		}

		string name = ev.Event;
		if (name == null)
		{
			return;
		}

		switch (name.Length)
		{
		case 8:
			switch (name[0])
			{
			case 'a':
				if (name == "ai.delta" && Mine(ev))
				{
					_stream.Append(ev.Text);
					_round.Append(ev.Text);
					if (_roundBubble == null)
					{
						_roundBubble = OpenRound();
					}

					_roundBubble.SetText(_round.ToString(), live: true);
					ScrollDown();
				}

				break;
			case 'p':
				if (name == "progress" && _taskLines.TryGetValue(ev.TaskId, out ToolLine? line))
				{
					line.SetProgress(ev.Current, ev.Total, ev.Message);
				}

				break;
			case 'f':
			{
				if (name != "finished")
				{
					break;
				}

				if (_taskLines.TryGetValue(ev.TaskId, out ToolLine? doneLine))
				{
					doneLine.Finish(ev.Success, ev.Message);
				}

				if (_aiPendingTasks.TryGetValue(ev.TaskId, out string? taskName))
				{
					_aiPendingTasks.Remove(ev.TaskId);
					if (!_busy)
					{
						_status.Text = I18n.L("后台任务完成") + " · " + I18n.L("助手回来汇报结果");
					}

					PageBase.Run(() => SendTextAsync(AiPage.PendingTaskReport(taskName, ev), echo: false));
				}

				break;
			}
			}

			break;
		case 7:
			switch (name[3])
			{
			case 'd':
			{
				if (name != "ai.done" || !Mine(ev))
				{
					break;
				}

				string note = StopNote(ev);
				if (_round.Length == 0 && note.Length > 0)
				{
					OpenRound();
				}

				if (_round.Length > 0 || _roundBubble != null)
				{
					string text = _round.ToString();
					if (note.Length > 0 && !text.Contains(note))
					{
						text = (text.Length > 0) ? (text + "\n\n" + note) : note;
					}

					_roundBubble?.SetText(text);
				}

				// 【Windows 源】:515 是 `foreach (var (key, value) in AiPage.PendingTasksOf(ev))` ——
				// 那个静态方法返回的是 `List<(string TaskId, string Name)>`(值元组),**不是** KeyValuePair,
				// 所以这里按元组解构搬(原来那版写成了 KeyValuePair ⇒ 类型对不上)。
				foreach ((string taskId2, string taskName2) in AiPage.PendingTasksOf(ev))
				{
					_aiPendingTasks[taskId2] = taskName2;
				}

				ResetRunState();
				string unsent = AiPage.UnsentOf(ev);
				if (unsent.Length > 0)
				{
					PageBase.Run(() => SendTextAsync(unsent, echo: false));
				}

				break;
			}
			case 'f':
			{
				if (name != "ai.fail" || !Mine(ev))
				{
					break;
				}

				if (_round.Length > 0)
				{
					_roundBubble?.SetText(_round.ToString());
				}
				else if (ev.Stopped)
				{
					_roundBubble?.SetText(I18n.L("已停止"));
				}
				else if (_roundBubble != null)
				{
					_roundBubble.SetText(ev.Text);
				}
				else
				{
					Add(new Bubble("error", ev.Text, RetryFrom));
				}

				ResetRunState();
				string unsent2 = AiPage.UnsentOf(ev);
				if (unsent2.Length > 0)
				{
					PageBase.Run(() => SendTextAsync(unsent2, echo: false));
				}

				break;
			}
			}

			break;
		case 9:
			if (name == "ai.status" && Mine(ev))
			{
				OnStatus(ev);
			}

			break;
		case 10:
			if (name == "ai.confirm" && Mine(ev))
			{
				ShowConfirm(ev);
			}

			break;
		case 6:
			if (name == "ai.ask" && Mine(ev))
			{
				ShowAsk(ev);
			}

			break;
		}
	}

	/// <summary>这条事件是不是本窗口那一轮对话(Windows <c>:490-498</c>):没有 chat_id ⇒ 看忙不忙。</summary>
	private bool Mine(BridgeEvent ev)
	{
		string chatId = ChatIdOf(ev);
		if (chatId.Length <= 0)
		{
			return _busy;
		}

		return chatId == _chatId;
	}

	/// <summary>从事件里取 chat_id(Windows <c>:500-507</c>)。</summary>
	private static string ChatIdOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("chat_id", out JsonElement v) || v.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return v.GetString() ?? "";
	}

	/// <summary>【Windows 源】<c>:509-514</c>(**已逐字读到**):加进消息列表 + 淡入 + 滚到底。
	/// 之前那条"未逐字读"的标记在此**闭环**:Windows 是 <c>Motion.FadeIn(el, 200.0, 8.0)</c>,mac 有同名 `Motion.FadeIn`,
	/// 所以照搬,不是我先前那个等价猜测。</summary>
	private void Add(Control el)
	{
		_messages.Children.Add(el);
		Motion.FadeIn(el, 200.0, 8.0);
		ScrollDown();
	}

	/// <summary>【Windows 源】<c>:516-522</c>(**已逐字读到**):Windows 是
	/// <c>Dispatcher.BeginInvoke(() =&gt; _scroll.ScrollToEnd())</c> —— 它把 ScrollViewer 存在字段 <c>_scroll</c> 里;
	/// mac 侧没有存字段,改为从消息列表往上找那根模板 <c>ScrollViewer</c>(D-128 口径)再 `ScrollToEnd()`,
	/// **行为等价**:两者都是"下一帧滚到最新",差别只在"怎么拿到那根 scroller"。</summary>
	private void ScrollDown()
	{
		Avalonia.Threading.Dispatcher.UIThread.Post(delegate
		{
			_messages.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
		});
	}

	/// <summary>工具行(【Windows 源】<c>:524-537</c>):同一工具复用同一行,改状态不新开一行。</summary>
	private ToolLine Line(string name, string text, ToolState state)
	{
		if (_toolLines.TryGetValue(name, out ToolLine? line))
		{
			line.SetText(text);
			line.SetState(state);
			return line;
		}

		line = new ToolLine(text, name);
		line.SetState(state);
		_toolLines[name] = line;
		Add(line);
		return line;
	}

	/// <summary>状态事件(【Windows 源】<c>:539-625</c>):按 <c>kind</c> 分派 —— plan 交给 <see cref="ShowPlan"/>,
	/// 其余交给那个"工具行 + 状态行"的分支(工具行按"准备/执行中/完成/已跳过"四种给词与状态)。</summary>
	private void OnStatus(BridgeEvent ev)
	{
		string name = string.IsNullOrEmpty(ev.Name) ? ev.Kind : ev.Name;
		string label = string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label;
		switch (ev.Kind)
		{
		case "plan":
			ShowPlan(ev);
			break;
		case "checkpoint_warn":
		{
			string message = (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String) ? (m.GetString() ?? "") : "";
			_status.Text = (message.Length > 0) ? message : I18n.L("检查点不可用,本轮改动无法自动撤回");
			break;
		}
		case "model_fallback":
		{
			string model = (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("model", out JsonElement mm) && mm.ValueKind == JsonValueKind.String) ? (mm.GetString() ?? "") : "";
			_status.Text = I18n.L("已切换备用模型") + ((model.Length > 0) ? (" · " + model) : "");
			break;
		}
		case "thinking":
			_status.Text = I18n.L("思考中...");
			if (_round.Length == 0)
			{
				OpenRound();
			}

			break;
		case "think":
		{
			bool afterTools = ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("after_tools", out JsonElement at) && at.ValueKind == JsonValueKind.True;
			_status.Text = afterTools ? I18n.L("搜完了,正在整理...") : I18n.L("思考中...");
			if (_round.Length == 0)
			{
				OpenRound(afterTools ? I18n.L("搜完了,正在整理...") : I18n.L("正在想..."));
			}

			break;
		}
		case "tool":
			if (ev.Name == "ask_user")
			{
				if (_round.Length == 0)
				{
					OpenRound(I18n.L("请在下面选一下"));
				}
			}
			else
			{
				CloseRound();
				Line(name, I18n.L("准备:") + label, ToolState.Prepare);
				_status.Text = I18n.L("正在执行操作...");
			}

			break;
		case "tool_run":
		case "tool_start":
			CloseRound();
			Line(name, I18n.L("执行中:") + label, ToolState.Running);
			_status.Text = I18n.L("正在执行操作...");
			break;
		case "tool_done":
		{
			CloseRound();
			ToolLine line = Line(name, I18n.L("完成:") + label, ToolState.Done);
			string taskId = TaskIdOf(ev);
			if (taskId.Length > 0)
			{
				line.BindTask();
				_taskLines[taskId] = line;
			}

			_status.Text = I18n.L("操作完成");
			break;
		}
		case "tool_skip":
			CloseRound();
			Line(name, I18n.L("已跳过:") + label, ToolState.Skipped);
			break;
		default:
			if (!string.IsNullOrWhiteSpace(ev.Label))
			{
				_status.Text = ev.Label;
			}

			break;
		}
	}

	/// <summary>【Windows 源】<c>:627-660</c>:从事件里取后台任务 id(`task_id` / `result` 里嵌的 JSON 都认)。</summary>
	private static string TaskIdOf(BridgeEvent ev)
	{
		if (!string.IsNullOrEmpty(ev.TaskId))
		{
			return ev.TaskId;
		}

		if (ev.Payload.ValueKind != JsonValueKind.Object)
		{
			return "";
		}

		if (ev.Payload.TryGetProperty("task_id", out JsonElement direct) && direct.ValueKind == JsonValueKind.String)
		{
			return direct.GetString() ?? "";
		}

		if (!ev.Payload.TryGetProperty("result", out JsonElement result) || result.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		string inner = result.GetString() ?? "";
		if (!inner.StartsWith("{", StringComparison.Ordinal))
		{
			return "";
		}

		try
		{
			using JsonDocument doc = JsonDocument.Parse(inner);
			return (doc.RootElement.TryGetProperty("task_id", out JsonElement id) && id.ValueKind == JsonValueKind.String) ? (id.GetString() ?? "") : "";
		}
		catch
		{
			return "";
		}
	}

	/// <summary>【Windows 源】<c>:662-669</c>。</summary>
	private static string StopReasonOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("stop_reason", out JsonElement v) || v.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return v.GetString() ?? "";
	}

	/// <summary>【Windows 源】<c>:671-678</c>。</summary>
	private static string DetailOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("detail", out JsonElement v) || v.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return v.GetString() ?? "";
	}

	/// <summary>【Windows 源】<c>:680-701</c>(**已逐字读全**):停因 → 一句给用户看的说明。
	/// 语义两处要点(我先前推断错了,已按原文改):①**未识别的停因 ⇒ 直接返回空串**(走不到 `DetailOf`);
	/// ②命中了某条文案才继续:detail 非空且未被包含时追加成 <c>文案(detail)</c>,最后整体包成 <c>(提示:…)</c>。</summary>
	private static string StopNote(BridgeEvent ev)
	{
		string note = StopReasonOf(ev) switch
		{
			"no_tool_call" => I18n.L("它没有真的开始执行:模型只回了文字,没有调用任何工具。"),
			"max_rounds" => I18n.L("步骤太多,先停在这里。你可以让我继续。"),
			"pending_task" => I18n.L("下载/安装还在后台跑,完成后助手会回来汇报。"),
			"stream_failed" => I18n.L("接口这轮没有返回内容,已停止。"),
			"empty_response" => I18n.L("接口返回了空回复。"),
			_ => "",
		};
		if (note.Length == 0)
		{
			return "";
		}

		string detail = DetailOf(ev);
		if (detail.Length > 0 && !note.Contains(detail))
		{
			note = note + I18n.L("(") + detail + I18n.L(")");
		}

		return I18n.L("(提示:") + note + I18n.L(")");
	}

	/// <summary>【Windows 源】<c>:735-770</c>:确认卡。照 Lead 的口径**复用 C8a 的 <c>ConfirmCard</c>**
	/// (它的 <c>AllowAlways(ev)</c> 与 <c>(ok, always, scope)</c> 回调正是 Windows 那个形状),**不自造第二套**。
	/// preview.lines 的处理逐字搬:head + lines 拼成一段,拼在 reason 前面。</summary>
	private void ShowConfirm(BridgeEvent ev)
	{
		CloseRound();
		string label = string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label;
		string text = (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String) ? (reason.GetString() ?? "") : "";
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("preview", out JsonElement preview) && preview.ValueKind == JsonValueKind.Object && preview.TryGetProperty("lines", out JsonElement lines) && lines.ValueKind == JsonValueKind.Array)
		{
			List<string> parts = new List<string>();
			if (preview.TryGetProperty("head", out JsonElement head) && head.ValueKind == JsonValueKind.String)
			{
				parts.Add(head.GetString() ?? "");
			}

			foreach (JsonElement item in lines.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					parts.Add(item.GetString() ?? "");
				}
			}

			string joined = string.Join("\n", parts.Where((string l) => l.Length > 0));
			if (joined.Length > 0)
			{
				text = string.IsNullOrWhiteSpace(text) ? joined : (joined + "\n" + text);
			}
		}

		ConfirmCard card = new ConfirmCard(label, text, ConfirmCard.AllowAlways(ev), delegate(bool ok, bool always, string scope)
		{
			PageBase.Run(async delegate
			{
				await AppServices.Client.TryCallAsync<object>("ai_confirm", new { ok, always, scope });
			});
		});
		Add(card);
		_status.Text = I18n.L("等你确认");
	}

	/// <summary>【Windows 源】<c>:798-809</c>:关窗时退订 + 还在忙就停掉这一轮。</summary>
	protected override void OnClosed(EventArgs e)
	{
		if (_client != null)
		{
			_client.EventReceived -= OnBridgeEvent;
		}

		if (_busy)
		{
			_ = (_client ?? AppServices.Client).TryCallAsync<JsonElement>("ai_stop");
		}

		base.OnClosed(e);
	}

	/// <summary>【Windows 源】<c>:703-733</c>:计划卡 —— 从 <c>items[]</c> 收 (title, status),非空标题才收;
	/// 有卡先摘旧的再放新的(同一轮只留一张计划卡)。<c>PlanCard</c> 已由 c4-pages 落地(`04fcd76`),签名同 Windows。</summary>
	private void ShowPlan(BridgeEvent ev)
	{
		List<(string Title, string Status)> items = new List<(string, string)>();
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("items", out JsonElement raw) && raw.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in raw.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				string title = item.TryGetProperty("title", out JsonElement t) ? (t.GetString() ?? "") : "";
				string status = item.TryGetProperty("status", out JsonElement s) ? (s.GetString() ?? "pending") : "pending";
				if (!string.IsNullOrWhiteSpace(title))
				{
					items.Add((title, status));
				}
			}
		}

		if (items.Count != 0)
		{
			if (_planCard != null)
			{
				_messages.Children.Remove(_planCard);
				_planCard = null;
			}

			_planCard = new PlanCard(items);
			Add(_planCard);
		}
	}

	/// <summary>【Windows 源】<c>:772-796</c>:追问卡 —— 没有 <c>questions</c> 就直接回一个 <c>null</c> 结果(等于"问不了"),
	/// 否则复用 <c>AskCard</c>(c4-pages 已落地,同签名:`AskCard(title, questions, submit)`),提交后回 <c>ai_answer</c>。</summary>
	private void ShowAsk(BridgeEvent ev)
	{
		CloseRound();
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("questions", out JsonElement questions) || questions.ValueKind != JsonValueKind.Array)
		{
			PageBase.Run(async delegate
			{
				await AppServices.Client.TryCallAsync<object>("ai_answer", new
				{
					result = (object?)null,
				});
			});
			return;
		}

		string title = ev.Payload.TryGetProperty("title", out JsonElement t) ? (t.GetString() ?? "") : "";
		AskCard card = new AskCard(title, questions, delegate(object? result)
		{
			PageBase.Run(async delegate
			{
				await AppServices.Client.TryCallAsync<object>("ai_answer", new { result });
			});
		});
		Add(card);
		_status.Text = I18n.L("等你回答");
	}

	// ==== 仅剩块 4:[ModuleInitializer] 接线 CrashUi.AiFixLauncher + 关闭降级(`:157` 改防御性兜底、保留 `:158`)。
}
