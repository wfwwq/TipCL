using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Controls;
using TipCL.Core.Launcher.Ai;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K2 + K3:Windows 版 <c>Pages\AiPage.cs</c>(82,886 字节 / 2752 行 / WPF / net48)的**骨架与顶栏**分区 ——
/// 全部字段/属性(【Windows 源】<c>:25-196</c>)、构造函数(<c>:198-533</c>,整页的视觉树都在这儿长出来)、
/// 以及顶栏那几件(实例胶囊 / MCP 徽标 / 「更多」菜单,<c>:535-605</c> 与 <c>:799-855</c>)。
///
/// <b>分块落地</b>:本文件是 K2/K3,后面的块在 <c>AiPage.Messages.cs</c>(K4)、<c>AiPage.Input.cs</c>(K5)、
/// <c>AiPage.Send.cs</c>(K6)、<c>AiPage.Model.cs</c>(K7)、<c>AiPage.Chats.cs</c>(K8)、
/// <c>AiPage.Cards.cs</c>(K9)、<c>AiPage.Events.cs</c>(K10)、<c>AiPage.SelfTest.cs</c>(K11)。
/// 它们与既有的 <c>Pages\AiPage.Tasks.cs</c>(K1 的静态分区,已在库里)合成**同一个** <c>internal sealed partial class AiPage</c>
/// —— 注意 Tasks.cs 那份声明没写基类,所以基类只在**本文件**里写一次(<c>: PageBase</c>)。
///
/// Windows → mac 的差异(逐条;全部是"框架类型/框架机制"层面的,行为按同义对齐):
/// <list type="number">
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>SetResourceReference(p, key)</c> → <c>Dyn(p, key)</c>
///   (本仓统一口径,见 <see cref="UiExt"/>);<c>Style = Ui.S(key)</c> → <c>StyleKeys.Apply(el, key)</c>
///   (Avalonia 的样式靠类名命中);<c>ToolTip</c> 属性 → <c>ToolTip.SetTip</c> / <c>UiExt.Tip</c>;</item>
/// <item><c>MouseLeftButtonUp</c> → <c>PointerReleased</c>(判 <c>InitialPressMouseButton == Left</c>,
///   与 <see cref="Ui.RowCard"/> 同一条口径);<c>GotKeyboardFocus/LostKeyboardFocus</c> → <c>GotFocus/LostFocus</c>;</item>
/// <item><c>ContextMenu</c>:WPF 的 <c>PlacementTarget = x; IsOpen = true;</c> 在 Avalonia 是
///   <c>menu.Open(x)</c>(D-124);<c>MenuItem.IsCheckable</c> → <c>ToggleType = MenuItemToggleType.CheckBox</c>
///   (Avalonia 11 改了名字,语义相同);「更多」菜单原来靠 <c>ContextMenu.Opened</c> 现算两项的可用性,
///   mac 改成**每次点开前重建**(等价:两项的 <c>IsEnabled</c> 本来就只依赖当时的 <c>_busy</c> / 当前对话);</item>
/// <item><c>Dispatcher.BeginInvoke((Action)SyncMcp)</c> → <c>Dispatcher.UIThread.Post(SyncMcp)</c>
///   (与 C2c 的既有口径一致);</item>
/// <item><b><c>ChatInput</c></b>:Windows 的复合输入框已在 mac 落地为同名的 <see cref="ChatInput"/>
///   (见那个文件的差异表:IME 组合键与自适应高度按 Avalonia 能做到的口径对齐);</item>
/// <item><b>输入框的 WPF 装饰</b>:<c>_input.Style = null</c> 在 Avalonia 没有对等物(样式是类名,不是对象)
///   ⇒ 不写;<c>TextBoxBase.CaretBrush</c> → <c>TextBox.CaretBrush</c>(同名属性);</item>
/// <item><b><c>TipCL.Pages.Plugins.AiViewStrip</c>(插件视图标签条,Windows <c>Pages\Plugins\AiViewStrip.cs</c>)</b>:
///   mac 侧尚未移植(它属于插件那一族,依赖插件视图宿主 / <c>ai_presets</c> 之外的插件 RPC),所以
///   Windows <c>:499 / :503 / :504-508 / :509 / :1211 / :2740 / :2746</c> 那几处接线**本次不搬**(逐条列在交付报告里)。
///   连带空出来的只是一排"插件视图标签 + 它的宿主"两行;消息区 / 输入区 / 分栏都照原样;</item>
/// <item><b>预设清单</b>用的是本包的 <see cref="AiPresets"/>(= Windows 的 <c>PresetCatalog</c>,
///   同一个 <c>ai_presets</c> RPC 与同一套名称回退规则),所以「用预设新建对话」这个入口照搬。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage : PageBase
{
	// =========================================================================
	// K2 · 字段与属性(【Windows 源】Pages\AiPage.cs:25-196)
	// =========================================================================

	private readonly SPanel _chatList = Ui.V(4.0);

	private readonly SPanel _messages = Ui.V(12.0);

	private readonly SmoothScroll _scroll;

	private readonly ChatInput _input = new ChatInput();

	private readonly List<string> _pendingImages = new List<string>();

	private readonly TextBlock _imgChip = new TextBlock();

	private readonly ComboBox _perm = Ui.Combo(new string[5]
	{
		I18n.L("每次确认"),
		I18n.L("写直接执行"),
		I18n.L("只看不动"),
		I18n.L("全自动"),
		I18n.L("自定义")
	}, null, 112.0);

	private readonly ComboBox _model = Ui.Combo(null, null, 220.0, I18n.L("模型名"), editable: true);

	private readonly Button _modelFetch;

	private bool _customModel;

	private readonly Button _send;

	private readonly Button _stop;

	private readonly Button _retry;

	private readonly Button _rewind;

	private readonly TextBlock _status = Ui.Small("");

	private readonly TextBlock _usageLabel = Ui.Small("");

	private bool _syncPerm;

	private bool _syncModel;

	private bool _modelFetched;

	private string _modelSaved = "";

	private bool _modelFetching;

	private readonly StringBuilder _stream = new StringBuilder();

	private readonly StringBuilder _round = new StringBuilder();

	private readonly Dictionary<string, ToolLine> _toolLines = new Dictionary<string, ToolLine>();

	private readonly Dictionary<string, TaskCard> _taskCards = new Dictionary<string, TaskCard>();

	/// <summary>斜杠候选浮层(Windows 的 WPF <c>Popup</c>)。Avalonia 的 <c>Popup</c> 没有
	/// <c>AllowsTransparency</c> / <c>Focusable</c> 两个属性(**如实差异**:Avalonia 的弹层本来就是非焦点、
	/// 自绘背景),所以只留 <c>Placement</c> / <c>IsLightDismissEnabled</c> / <c>Child</c> / <c>PlacementTarget</c>
	/// (WPF 的 <c>StaysOpen = true</c> 对应 Avalonia 的 <c>IsLightDismissEnabled = false</c>)。</summary>
	private readonly Popup _slashPop = new Popup
	{
		Placement = PlacementMode.Top,
		IsLightDismissEnabled = false
	};

	private readonly SPanel _slashRows = Ui.V(2.0);

	private List<SlashCommand> _slashItems = new List<SlashCommand>();

	private int _slashSel = -1;

	private readonly Border _mcpBadge;

	private readonly TextBlock _mcpText = Ui.Txt("", 11.5, bold: false, "B.InkMuted");

	private Bubble? _roundBubble;

	private AiStoreDto _store = new AiStoreDto();

	private bool _busy;

	private string _runChatId = "";

	private bool _abandoned;

	private TaskCompletionSource<object?>? _runEnded;

	private readonly Dictionary<string, (string Name, string ChatId)> _aiPendingTasks = new Dictionary<string, (string, string)>();

	private static readonly string[] PermissionModes = new string[5] { "default", "acceptEdits", "plan", "yolo", "custom" };

	private PlanCard? _planCard;

	private readonly Border _hist;

	/// <summary>L4 历史栏分栏外壳(收起历史栏时左栏与分隔条一起收起;比例存 v2 <c>splits["ai"]["history"]</c>)。</summary>
	private readonly PageLayoutHost _histSplit;

	private readonly TextBox _histSearch;

	private readonly TextBlock _titleText = Ui.Txt("", 14.0, bold: true, "B.Ink");

	private readonly Border _instPill;

	private readonly TextBlock _instText = Ui.Txt("", 11.5, bold: false, "B.AccentText");

	private readonly ContentControl _ringHost = new ContentControl
	{
		Width = 18.0,
		Height = 18.0
	};

	private readonly Border _composer;

	private readonly ContentControl _sendStop = new ContentControl();

	private readonly SPanel _dock = Ui.V(8.0);

	private readonly TextBlock _permTip = Ui.Txt("", 11.5, bold: false, "B.InkMuted");

	private readonly TextBlock _placeholder = Ui.Txt("", 13.5, bold: false, "B.InkFaint");

	/// <summary>空态那一块(Windows 是 <c>FrameworkElement</c>;mac 统一用 <c>Control</c>)。</summary>
	private Control? _emptyView;

	private StepGroup? _group;

	private bool _needWho = true;

	private Bubble? _lastUser;

	private ConfirmCard? _pendingConfirm;

	private long _contextWindow = 131072L;

	private bool _histOpen = true;

	public override string Title => I18n.L("AI 助手");

	private AiChatDto? ActiveChat => _store.Chats.FirstOrDefault((AiChatDto c) => c.Id == _store.ActiveId) ?? _store.Chats.FirstOrDefault();

	private static (string Title, string Sub, string Icon, string Prompt)[] QuickStarts => new(string, string, string, string)[4]
	{
		(I18n.L("下个最新版"), I18n.L("装好最新正式版并建一个实例"), "M12 3v12 M6 11l6 6 6-6 M4 21h16", I18n.L("帮我下载最新正式版 Minecraft")),
		(I18n.L("装 Fabric + 钠"), I18n.L("一步配好 1.20.1 性能优化"), "M12 2l10 6-10 6L2 8z M2 14l10 6 10-6", I18n.L("给我装 1.20.1 Fabric，再装钠和 Iris 光影")),
		(I18n.L("崩溃分析"), I18n.L("读最近一次崩溃报告找原因"), "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8l6 6v12a2 2 0 0 1-2 2z M14 2v6h6 M8 13h8 M8 17h5", I18n.L("刚才启动失败了，读一下日志告诉我原因")),
		(I18n.L("扫模组冲突"), I18n.L("检查版本不兼容和重复模组"), "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10", I18n.L("扫一下当前实例的模组冲突"))
	};

	internal SPanel SelfTestMessages => _messages;

	internal SPanel SelfTestDock => _dock;

	internal ContentControl SelfTestSendStop => _sendStop;

	internal Button SelfTestSend => _send;

	internal Button SelfTestStop => _stop;

	internal TextBlock SelfTestPlaceholder => _placeholder;

	internal Border SelfTestHist => _hist;

	internal SPanel SelfTestChatList => _chatList;

	internal TextBlock SelfTestUsage => _usageLabel;

	internal ComboBox SelfTestPerm => _perm;

	internal TextBlock SelfTestPermTip => _permTip;

	public AiPage()
	{
		_send = new Button
		{
			Template = AiIcons.FlatButtonTemplate(),
			Width = 32.0,
			Height = 32.0,
			Padding = new Thickness(0.0),
			Cursor = new Cursor(StandardCursorType.Hand),
			Content = AiIcons.Icon("M12 19V5 M5 12l7-7 7 7", 16.0, "B.OnAccentStrong")
		};
		ToolTip.SetTip(_send, I18n.L("发送（Enter）"));
		_send.Dyn(TemplatedControl.BackgroundProperty, "B.AccentStrong");
		_send.Click += delegate
		{
			PageBase.Run(SendAsync);
		};
		_stop = new Button
		{
			Template = AiIcons.FlatButtonTemplate(),
			Width = 32.0,
			Height = 32.0,
			Padding = new Thickness(0.0),
			Cursor = new Cursor(StandardCursorType.Hand),
			Content = new Border
			{
				Width = 10.0,
				Height = 10.0,
				CornerRadius = new CornerRadius(2.0),
				Background = Brushes.White
			}
		};
		ToolTip.SetTip(_stop, I18n.L("停止生成"));
		_stop.Dyn(TemplatedControl.BackgroundProperty, "B.Ink");
		_stop.Click += delegate
		{
			PageBase.Run(StopAsync);
		};
		_retry = Ui.Btn(I18n.L("重试"), BtnKind.Chip, delegate
		{
			PageBase.Run(RetryAsync);
		}, "\ue72c");
		_rewind = Ui.Btn(I18n.L("撤回"), BtnKind.Chip, delegate
		{
			PageBase.Run(RewindAsync);
		}, "\ue72b");
		_stop.IsEnabled = false;
		_retry.IsEnabled = false;
		ToolTip.SetTip(_perm, I18n.L("AI 权限等级"));
		_perm.Width = 104.0;
		_perm.SelectionChanged += delegate
		{
			SyncPermTip();
			if (!_syncPerm)
			{
				PageBase.Run(SavePermissionModeAsync, I18n.L("权限设置保存失败"));
			}
		};
		_modelFetch = Ui.Btn("", BtnKind.Icon, delegate
		{
			PageBase.Run(() => FetchModelsAsync(manual: true), I18n.L("拉取模型列表失败"));
		}, "\ue72c");
		ToolTip.SetTip(_modelFetch, I18n.L("刷新模型列表"));
		_model.Width = 190.0;
		ToolTip.SetTip(_model, I18n.L("当前对话使用的模型；换完对下一轮生效"));
		_model.SelectionChanged += delegate
		{
			if (!_syncModel)
			{
				object? selectedItem = _model.SelectedItem;
				string? picked = selectedItem as string;
				if (picked != null)
				{
					PageBase.Run(() => SaveModelAsync(picked), I18n.L("模型保存失败"));
				}
			}
		};
		_model.LostFocus += delegate
		{
			if (!_model.IsDropDownOpen)
			{
				PageBase.Run(() => SaveModelAsync(ModelValue()), I18n.L("模型保存失败"));
			}
		};
		_model.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.Key == Key.Enter && !_model.IsDropDownOpen)
			{
				e.Handled = true;
				PageBase.Run(() => SaveModelAsync(ModelValue()), I18n.L("模型保存失败"));
			}
		};
		_messages.MaxWidth = 760.0;
		_messages.HorizontalAlignment = HorizontalAlignment.Stretch;
		_messages.Margin = new Thickness(28.0, 18.0, 28.0, 12.0);
		Grid scrollHost = new Grid();
		scrollHost.Children.Add(_messages);
		_scroll = Ui.Scroll(scrollHost);
		_input.Submitted += delegate(string text)
		{
			SlashCommand? cmd = AiChatOps.ExactSlash(text);
			if (cmd != null)
			{
				_input.Clear();
				RunSlash(cmd);
			}
			else
			{
				PageBase.Run(() => SendTextAsync(text));
			}
		};
		_input.KeyIntercept = SlashKey;
		Button button = Ui.Btn(I18n.L("新对话"), BtnKind.Primary, delegate
		{
			PageBase.Run(NewChatAsync);
		}, "\ue710");
		button.HorizontalAlignment = HorizontalAlignment.Stretch;
		button.HorizontalContentAlignment = HorizontalAlignment.Center;
		_histSearch = Ui.Input(I18n.L("搜索对话"));
		_histSearch.TextChanged += delegate
		{
			RenderChats();
		};
		Grid grid = Ui.G("Auto,Auto,Auto,*");
		// 「新对话」右边的小箭头:用插件提供的 AI 预设新建对话
		Button presetPick = Ui.IconBtn("\ue70d", I18n.L("用预设新建对话"));
		presetPick.Click += delegate
		{
			AiPresets.OpenMenu(presetPick, (string preset, string view) => NewChatWithPresetAsync(preset, view));
		};
		Grid newRow = Ui.G(null, "*,Auto");
		newRow.Add(button, 0);
		newRow.Add(presetPick.M(4.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid.Add(newRow, 0);
		grid.Add(_histSearch.M(0.0, 8.0), 1);
		grid.Add(Ui.Txt(I18n.L("最近"), 11.0, bold: false, "B.InkFaint").M(8.0, 12.0, 0.0, 4.0), 2);
		grid.Add(Ui.Scroll(_chatList), 3);
		_hist = new Border
		{
			Width = 220.0,
			Padding = new Thickness(10.0, 12.0, 10.0, 12.0),
			BorderThickness = new Thickness(0.0, 0.0, 1.0, 0.0),
			Child = grid
		};
		_hist.Dyn(Border.BorderBrushProperty, "B.LineSoft");
		_hist.Dyn(Border.BackgroundProperty, "B.Paper2");
		Button el = AiIcons.Flat("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18", null, I18n.L("显示 / 隐藏对话列表"), delegate
		{
			_histOpen = !_histOpen;
			// L4:收起历史栏 = 左栏与分隔条一起占 0 宽(该形态不写比例)。
			// 【mac 差异】Windows 走 PageLayoutHost.SetLeftVisible(内部收起分隔条与列);mac 的
			// PageLayoutHost 判据是"左栏孩子自己的 IsVisible"(见那里的 LeftCollapsed),所以这里直接改孩子。
			_hist.IsVisible = _histOpen;
		}, "B.InkMuted", 16.0);
		_titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		_instPill = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(8.0, 2.0, 9.0, 2.0),
			Child = Ui.H(5.0, AiIcons.Icon("M21 8l-9-5-9 5v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8", 12.0, "B.AccentText").VCenter(), _instText.VCenter(), Ui.Txt("▾", 10.0, bold: false, "B.AccentText").VCenter()),
			Cursor = new Cursor(StandardCursorType.Hand),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		ToolTip.SetTip(_instPill, I18n.L("助手默认操作的实例，点击切换"));
		_instPill.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		_instPill.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
		{
			if (e.InitialPressMouseButton == MouseButton.Left)
			{
				PageBase.Run(OpenInstanceMenuAsync, I18n.L("读取实例失败"));
			}
		};
		Border mcpDot = new Border
		{
			Width = 7.0,
			Height = 7.0,
			CornerRadius = new CornerRadius(999.0)
		};
		mcpDot.Dyn(Border.BackgroundProperty, "B.Ok");
		_mcpText.TextTrimming = TextTrimming.CharacterEllipsis;
		_mcpBadge = new Border
		{
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			Child = Ui.H(5.0, mcpDot.VCenter(), _mcpText.VCenter()),
			IsVisible = false,
			Background = Brushes.Transparent
		};
		McpServer.StateChanged += delegate
		{
			Dispatcher.UIThread.Post(SyncMcp);
		};
		McpServer.ToolCalled += delegate
		{
			Dispatcher.UIThread.Post(SyncMcp);
		};
		_usageLabel.VerticalAlignment = VerticalAlignment.Center;
		_usageLabel.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		Button more = AiIcons.Flat("M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M19 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M5 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z", null, I18n.L("更多"), delegate
		{
		}, "B.InkMuted", 16.0);
		more.Click += delegate
		{
			// 【mac 差异】Windows 是 ctor 里建一次菜单、靠 ContextMenu.Opened 现算两项的可用性;
			// mac 每次点开前**重建**一份(两项的可用性本来就只依赖当时的 _busy / 当前对话,等价)。
			more.ContextMenu = MoreMenu();
			more.ContextMenu.Open(more);
		};
		Grid grid2 = Ui.G(null, "Auto,Auto,Auto,*,Auto,Auto,Auto");
		grid2.Add(el.VCenter(), 0);
		grid2.Add(_titleText.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid2.Add(_mcpBadge.VCenter(), 0, 2);
		grid2.Add(_ringHost.VCenter(), 0, 4);
		grid2.Add(_usageLabel.M(6.0, 0.0, 6.0), 0, 5);
		grid2.Add(more.VCenter(), 0, 6);
		Border border = new Border
		{
			Height = 48.0,
			Padding = new Thickness(12.0, 0.0, 12.0, 0.0),
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			Child = grid2
		};
		border.Dyn(Border.BorderBrushProperty, "B.LineSoft");
		// 【mac 差异】Windows 这里还有 _input.Style = null(Avalonia 的样式是类名,没有对象可置空)
		_input.BorderThickness = new Thickness(0.0);
		_input.Background = Brushes.Transparent;
		_input.Padding = new Thickness(2.0, 4.0, 2.0, 4.0);
		_input.FontSize = 13.5;
		_input.Dyn(TextBox.ForegroundProperty, "B.Ink");
		_input.Dyn(TextBox.CaretBrushProperty, "B.Ink");
		_input.TextChanged += delegate
		{
			SyncPlaceholder();
			SyncSlash();
		};
		_placeholder.IsHitTestVisible = false;
		_placeholder.Margin = new Thickness(4.0, 5.0, 0.0, 0.0);
		_placeholder.VerticalAlignment = VerticalAlignment.Top;
		Grid grid3 = new Grid();
		grid3.Children.Add(_input);
		grid3.Children.Add(_placeholder);
		Button el2 = AiIcons.Flat("M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-4 8", null, I18n.L("引用当前实例 / 最近崩溃日志"), delegate
		{
			InsertMention();
		}, "B.InkMuted", 15.0);
		Button el3 = AiIcons.Flat("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", null, I18n.L("按工具 / 目录细调 AI 能做什么"), delegate
		{
			PageBase.Run(OpenPermissionPanelAsync);
		}, "B.InkMuted", 15.0);
		Button elImg = AiIcons.Flat("M3 5h18v14H3z M8.5 8.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3z M21 15l-5-5L5 19", null, I18n.L("附图片（也可以直接粘贴或拖进来）"), PickImage, "B.InkMuted", 15.0);
		SetupImageInput();
		_sendStop.Content = _send;
		Grid grid4 = Ui.G(null, "Auto,Auto,Auto,Auto,Auto,*,Auto");
		grid4.Add(Ui.H(6.0, elImg.VCenter(), _imgChip.VCenter()).M(4.0, 0.0, 0.0, 0.0), 0, 5);
		grid4.Add(el2.VCenter(), 0);
		grid4.Add(_model.M(4.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid4.Add(_modelFetch.M(2.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		grid4.Add(_perm.M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 3);
		grid4.Add(el3.M(2.0, 0.0, 0.0, 0.0).VCenter(), 0, 4);
		grid4.Add(_sendStop.VCenter(), 0, 6);
		_composer = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(12.0, 8.0, 10.0, 8.0),
			Child = Ui.V(6.0, grid3, grid4)
		};
		_composer.Dyn(Border.BackgroundProperty, "B.Paper");
		HookImageDrop(_composer);
		_composer.Dyn(Border.BorderBrushProperty, "B.Line");
		_input.GotFocus += delegate
		{
			_composer.Dyn(Border.BorderBrushProperty, "B.Accent");
		};
		_input.LostFocus += delegate
		{
			_composer.Dyn(Border.BorderBrushProperty, "B.Line");
		};
		_status.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		_status.TextTrimming = TextTrimming.CharacterEllipsis;
		Grid grid5 = Ui.G(null, "Auto,*,Auto");
		grid5.Add(AiIcons.Icon("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", 12.0, "B.InkFaint").VCenter(), 0);
		grid5.Add(_permTip.M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid5.Add(_status.VCenter(), 0, 2);
		Border slashCard = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(4.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			Child = _slashRows
		};
		slashCard.Dyn(Border.BackgroundProperty, "B.Paper");
		slashCard.Dyn(Border.BorderBrushProperty, "B.Line");
		_slashPop.Child = slashCard;
		_slashPop.PlacementTarget = _composer;
		TipCL.Pages.Plugins.PluginActionBar aiActions = new TipCL.Pages.Plugins.PluginActionBar("ai.input", () => new Dictionary<string, object?> { ["chatId"] = _store.ActiveId });
		SPanel sPanel = Ui.V(8.0, _dock, _instPill, aiActions, _composer, grid5.M(4.0, 0.0, 4.0));
		sPanel.MaxWidth = 760.0;
		sPanel.Margin = new Thickness(28.0, 4.0, 28.0, 14.0);
		// 【mac 未移植】Windows 这里是 4 行:"Auto,Auto,*,Auto",第 1 行是插件视图标签条
		// (TipCL.Pages.Plugins.AiViewStrip.Bar)、另有一块跨两行的视图宿主(strip.Host)。见文件头差异表。
		Grid grid6 = Ui.G("Auto,*,Auto");
		grid6.Add(border, 0);
		grid6.Add(_scroll, 1);
		grid6.Add(sPanel, 2);
		// L4 分栏(设计 §4.E 第 7 处):历史栏(现状固定 220)+ 10px 分隔条 + 对话区。
		// 分隔条占住对话区顶部操作条本来就有的 10px 左内边距(grid6 左移 10px 回到原位),首屏逐像素不变;
		// 比例存 v2 splits["ai"]["history"];历史栏收起时左栏与分隔条一起收起(见上面的开关)
		PageLayoutHost grid7 = new PageLayoutHost("ai", "history", GridLength.Auto, new GridLength(1.0, GridUnitType.Star));
		grid6.Margin = new Thickness(-10.0, 0.0, 0.0, 0.0);
		grid7.SetLeft(_hist);
		grid7.SetRight(grid6);
		_histSplit = grid7;
		Border border2 = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			BorderThickness = new Thickness(1.0),
			ClipToBounds = true,
			Child = grid7,
			Margin = new Thickness(16.0, 8.0, 16.0, 16.0)
		};
		border2.Dyn(Border.BackgroundProperty, "B.Paper");
		border2.Dyn(Border.BorderBrushProperty, "B.Line");
		base.Content = border2;
		SyncPlaceholder();
		SyncPermTip();
		SyncInstChip();
		SyncMcp();
	}

	// =========================================================================
	// K3 · 顶栏:实例胶囊 / MCP 徽标 / 「更多」菜单(【Windows 源】:535-605、:799-855)
	// =========================================================================

	private void SyncInstChip()
	{
		string text = PageBase.Win?.Prefs.CatalogInstance ?? "";
		_instText.Text = I18n.L("实例：{0}", (text.Length > 0) ? text : I18n.L("默认"));
	}

	private async Task OpenInstanceMenuAsync()
	{
		List<InstanceInfo> list = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		string current = PageBase.Win?.Prefs.CatalogInstance ?? "";
		ContextMenu menu = new ContextMenu
		{
			Placement = PlacementMode.Top
		};
		if (list.Count == 0)
		{
			menu.Items.Add(new MenuItem
			{
				Header = I18n.L("还没有实例"),
				IsEnabled = false
			});
		}
		foreach (InstanceInfo item in list)
		{
			string name = item.Name;
			MenuItem mi = new MenuItem
			{
				Header = (string.IsNullOrEmpty(item.Mc) ? name : (name + "  ·  " + item.Mc)),
				// 【Avalonia 差异】WPF 的 IsCheckable → MenuItemToggleType.CheckBox(见 LibraryPage 的先例)
				ToggleType = MenuItemToggleType.CheckBox,
				IsChecked = (name == current)
			};
			mi.Click += delegate
			{
				SetAssistantInstance(name);
			};
			menu.Items.Add(mi);
		}
		menu.Open(_instPill);
	}

	private void SetAssistantInstance(string name)
	{
		MainWindow? win = PageBase.Win;
		if (win == null)
		{
			return;
		}

		win.Prefs.CatalogInstance = name ?? "";
		win.SaveUiPrefs();
		SyncInstChip();
		PageBase.Toast(I18n.L("已切换实例"), I18n.L("助手接下来默认操作「{0}」", name));
	}

	private void SyncMcp()
	{
		bool running = McpServer.Running;
		_mcpBadge.IsVisible = running;
		if (!running)
		{
			return;
		}

		string last = McpServer.LastTool;
		_mcpText.Text = (last.Length > 0) ? I18n.L("MCP 已开启 · {0} {1}", last, McpServer.LastToolAt.ToString("HH:mm")) : I18n.L("MCP 已开启");
		string tip = I18n.L("MCP Server 地址：{0}", McpServer.Url);
		if (last.Length > 0)
		{
			tip = tip + "\n" + I18n.L("最近一次外部调用:{0}({1})", last, McpServer.LastToolAt.ToString("yyyy-MM-dd HH:mm:ss"));
		}

		ToolTip.SetTip(_mcpBadge, tip);
	}

	private ContextMenu MoreMenu()
	{
		ContextMenu contextMenu = new ContextMenu();
		MenuItem menuItem = new MenuItem
		{
			Header = I18n.L("新对话")
		};
		menuItem.Click += delegate
		{
			PageBase.Run(NewChatAsync);
		};
		MenuItem menuItem2 = new MenuItem
		{
			Header = I18n.L("管理权限")
		};
		menuItem2.Click += delegate
		{
			PageBase.Run(OpenPermissionPanelAsync);
		};
		MenuItem rewind = new MenuItem
		{
			Header = I18n.L("撤回上一轮")
		};
		rewind.Click += delegate
		{
			if (!_busy)
			{
				PageBase.Run(RewindAsync);
			}
		};
		MenuItem del = new MenuItem
		{
			Header = I18n.L("删除这个对话")
		};
		del.Click += delegate
		{
			string id = _store.ActiveId;
			if (!string.IsNullOrEmpty(id))
			{
				PageBase.Run(() => SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_delete_chat", new
				{
					chat_id = id
				}, _store)));
			}
		};
		contextMenu.Items.Add(menuItem);
		contextMenu.Items.Add(menuItem2);
		contextMenu.Items.Add(rewind);
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(del);
		// 【mac 差异】Windows 把这两行的可用性放在 ContextMenu.Opened 里现算;这里在**建菜单时**算
		// (调用点每次点开前重建,语义相同)。
		rewind.IsEnabled = !_busy && LastUserText().Length > 0;
		del.IsEnabled = ActiveChat != null;
		return contextMenu;
	}
}
