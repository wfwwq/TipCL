using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

public sealed class AiPage : PageBase
{
	private readonly SPanel _chatList = Ui.V(4.0);

	private readonly SPanel _messages = Ui.V(12.0);

	private readonly SmoothScroll _scroll;

	private readonly ChatInput _input = new ChatInput();

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

	private readonly Dictionary<string, ToolLine> _taskLines = new Dictionary<string, ToolLine>();

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

	private FrameworkElement? _emptyView;

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
			Cursor = Cursors.Hand,
			ToolTip = I18n.L("发送（Enter）"),
			Content = AiIcons.Icon("M12 19V5 M5 12l7-7 7 7", 16.0, "B.OnAccentStrong")
		};
		_send.SetResourceReference(Control.BackgroundProperty, "B.AccentStrong");
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
			Cursor = Cursors.Hand,
			ToolTip = I18n.L("停止生成"),
			Content = new Border
			{
				Width = 10.0,
				Height = 10.0,
				CornerRadius = new CornerRadius(2.0),
				Background = Brushes.White
			}
		};
		_stop.SetResourceReference(Control.BackgroundProperty, "B.Ink");
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
		_perm.ToolTip = I18n.L("AI 权限等级");
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
		_modelFetch.ToolTip = I18n.L("刷新模型列表");
		_model.Width = 190.0;
		_model.ToolTip = I18n.L("当前对话使用的模型；换完对下一轮生效");
		_model.SelectionChanged += delegate
		{
			if (!_syncModel)
			{
				object selectedItem = _model.SelectedItem;
				string picked = selectedItem as string;
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
		_model.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return && !_model.IsDropDownOpen)
			{
				e.Handled = true;
				PageBase.Run(() => SaveModelAsync(ModelValue()), I18n.L("模型保存失败"));
			}
		};
		_messages.MaxWidth = 760.0;
		_messages.HorizontalAlignment = HorizontalAlignment.Stretch;
		_messages.Margin = new Thickness(28.0, 18.0, 28.0, 12.0);
		_scroll = Ui.Scroll(new Grid
		{
			Children = { (UIElement)_messages }
		});
		_input.Submitted += delegate(string text)
		{
			PageBase.Run(() => SendTextAsync(text));
		};
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
		grid.Add(button, 0);
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
		_hist.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		_hist.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		Button el = AiIcons.Flat("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18", null, I18n.L("显示 / 隐藏对话列表"), delegate
		{
			_histOpen = !_histOpen;
			_hist.Visibility = ((!_histOpen) ? Visibility.Collapsed : Visibility.Visible);
		}, "B.InkMuted", 16.0);
		_titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		_instPill = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(8.0, 2.0, 9.0, 2.0),
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			Child = Ui.H(5.0, AiIcons.Icon("M21 8l-9-5-9 5v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8", 12.0, "B.AccentText").VCenter(), _instText.VCenter()),
			ToolTip = I18n.L("助手默认操作的实例"),
			Visibility = Visibility.Collapsed
		};
		_instPill.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		_usageLabel.VerticalAlignment = VerticalAlignment.Center;
		_usageLabel.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		Button more = AiIcons.Flat("M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M19 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M5 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z", null, I18n.L("更多"), delegate
		{
		}, "B.InkMuted", 16.0);
		more.ContextMenu = MoreMenu();
		more.Click += delegate
		{
			more.ContextMenu.PlacementTarget = more;
			more.ContextMenu.IsOpen = true;
		};
		Grid grid2 = Ui.G(null, "Auto,Auto,Auto,*,Auto,Auto,Auto");
		grid2.Add(el.VCenter(), 0);
		grid2.Add(_titleText.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid2.Add(_instPill.VCenter(), 0, 2);
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
		border.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		_input.Style = null;
		_input.BorderThickness = new Thickness(0.0);
		_input.Background = Brushes.Transparent;
		_input.Padding = new Thickness(2.0, 4.0, 2.0, 4.0);
		_input.FontSize = 13.5;
		_input.SetResourceReference(Control.ForegroundProperty, "B.Ink");
		_input.SetResourceReference(TextBoxBase.CaretBrushProperty, "B.Ink");
		_input.TextChanged += delegate
		{
			SyncPlaceholder();
		};
		_placeholder.IsHitTestVisible = false;
		_placeholder.Margin = new Thickness(4.0, 5.0, 0.0, 0.0);
		_placeholder.VerticalAlignment = VerticalAlignment.Top;
		Grid grid3 = new Grid
		{
			Children = 
			{
				(UIElement)_input,
				(UIElement)_placeholder
			}
		};
		Button el2 = AiIcons.Flat("M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-4 8", null, I18n.L("引用当前实例 / 最近崩溃日志"), delegate
		{
			InsertMention();
		}, "B.InkMuted", 15.0);
		Button el3 = AiIcons.Flat("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", null, I18n.L("按工具 / 目录细调 AI 能做什么"), delegate
		{
			PageBase.Run(OpenPermissionPanelAsync);
		}, "B.InkMuted", 15.0);
		_sendStop.Content = _send;
		Grid grid4 = Ui.G(null, "Auto,Auto,Auto,Auto,Auto,*,Auto");
		grid4.Add(el2.VCenter(), 0);
		grid4.Add(_model.M(4.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid4.Add(_modelFetch.M(2.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		grid4.Add(_perm.M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 3);
		grid4.Add(el3.M(2.0, 0.0, 0.0, 0.0).VCenter(), 0, 4);
		grid4.Add(_sendStop.VCenter(), 0, 6);
		_composer = new Border
		{
			CornerRadius = new CornerRadius(14.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(12.0, 8.0, 10.0, 8.0),
			Child = Ui.V(6.0, grid3, grid4)
		};
		_composer.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_composer.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_input.GotKeyboardFocus += delegate
		{
			_composer.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		};
		_input.LostKeyboardFocus += delegate
		{
			_composer.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		};
		_status.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		_status.TextTrimming = TextTrimming.CharacterEllipsis;
		Grid grid5 = Ui.G(null, "Auto,*,Auto");
		grid5.Add(AiIcons.Icon("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", 12.0, "B.InkFaint").VCenter(), 0);
		grid5.Add(_permTip.M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid5.Add(_status.VCenter(), 0, 2);
		SPanel sPanel = Ui.V(8.0, _dock, _composer, grid5.M(4.0, 0.0, 4.0));
		sPanel.MaxWidth = 760.0;
		sPanel.Margin = new Thickness(28.0, 4.0, 28.0, 14.0);
		Grid grid6 = Ui.G("Auto,*,Auto");
		grid6.Add(border, 0);
		grid6.Add(_scroll, 1);
		grid6.Add(sPanel, 2);
		Grid grid7 = Ui.G(null, "Auto,*");
		grid7.Add(_hist, 0);
		grid7.Add(grid6, 0, 1);
		Border border2 = new Border
		{
			CornerRadius = new CornerRadius(14.0),
			BorderThickness = new Thickness(1.0),
			ClipToBounds = true,
			Child = grid7,
			Margin = new Thickness(16.0, 8.0, 16.0, 16.0)
		};
		border2.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		border2.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		base.Content = border2;
		SyncPlaceholder();
		SyncPermTip();
	}

	private void SyncPlaceholder()
	{
		if (_pendingConfirm != null && !_pendingConfirm.Answered)
		{
			_placeholder.Text = I18n.L("先处理上面的确认，或直接输入新要求来拒绝");
		}
		else if (_busy)
		{
			_placeholder.Text = I18n.L("回复中也可以继续输入，会立刻交给助手");
		}
		else
		{
			_placeholder.Text = I18n.L("问点什么，@ 引用实例或崩溃日志…  Enter 发送，Shift+Enter 换行");
		}
		_placeholder.Visibility = ((!string.IsNullOrEmpty(_input.Text)) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void SyncPermTip()
	{
		int selectedIndex = _perm.SelectedIndex;
		TextBlock permTip = _permTip;
		permTip.Text = selectedIndex switch
		{
			1 => I18n.L("当前「写直接执行」：写入不再询问，删除仍会先问你"), 
			2 => I18n.L("当前「只看不动」：只读取和分析，不会改任何文件"), 
			3 => I18n.L("当前「全自动」：所有操作都不再询问，请谨慎使用"), 
			4 => I18n.L("当前「自定义」：按权限管理里的规则执行"), 
			_ => I18n.L("当前「每次确认」：读取直接做，写入和删除都会先问你"), 
		};
		_permTip.SetResourceReference(TextBlock.ForegroundProperty, (selectedIndex == 3) ? "B.Danger" : "B.InkMuted");
		_perm.SetResourceReference(Control.ForegroundProperty, (selectedIndex == 3) ? "B.Danger" : "B.Ink");
	}

	private void InsertMention()
	{
		string text = PageBase.Win?.Prefs.CatalogInstance ?? "";
		string text2 = ((text.Length > 0) ? I18n.L("@实例「{0}」 ", text) : I18n.L("@最近的崩溃日志 "));
		int caretIndex = _input.CaretIndex;
		_input.Text = _input.Text.Insert(Math.Min(caretIndex, _input.Text.Length), text2);
		_input.CaretIndex = Math.Min(caretIndex + text2.Length, _input.Text.Length);
		_input.Focus();
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
		contextMenu.Opened += delegate
		{
			rewind.IsEnabled = !_busy && LastUserText().Length > 0;
			del.IsEnabled = ActiveChat != null;
		};
		return contextMenu;
	}

	private async Task SyncPermissionAsync()
	{
		Dictionary<string, JsonElement> obj = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		string text = "default";
		if (obj.TryGetValue("ai_permission_mode", out var value) && value.ValueKind == JsonValueKind.String)
		{
			text = value.GetString() ?? text;
		}
		int num = Array.IndexOf<string>(PermissionModes, text);
		if (num < 0)
		{
			num = 0;
		}
		_syncPerm = true;
		try
		{
			_perm.SelectedIndex = num;
		}
		finally
		{
			_syncPerm = false;
		}
	}

	private async Task SavePermissionModeAsync()
	{
		string text = ((_perm.SelectedIndex >= 0 && _perm.SelectedIndex < PermissionModes.Length) ? PermissionModes[_perm.SelectedIndex] : "default");
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["ai_permission_mode"] = text,
				["ai_confirm_writes"] = text != "yolo"
			}
		});
	}

	private string ModelValue()
	{
		string text = _model.Text?.Trim();
		if (text != null && text.Length > 0)
		{
			return text;
		}
		return (_model.SelectedItem as string) ?? "";
	}

	private void SetModel(string? name)
	{
		string text = name?.Trim() ?? "";
		if (text.Length == 0)
		{
			return;
		}
		_syncModel = true;
		try
		{
			if (!_model.Items.Contains(text))
			{
				_model.Items.Add(text);
			}
			_model.SelectedItem = text;
			_model.Text = text;
		}
		finally
		{
			_syncModel = false;
		}
	}

	private async Task SyncModelAsync()
	{
		if (!_model.IsKeyboardFocusWithin)
		{
			JsonElement value;
			string text = ((((await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>()).TryGetValue("ai_model", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "");
			SetModel(text);
			_modelSaved = text.Trim();
		}
	}

	private async Task SaveModelAsync(string? raw)
	{
		string v = (raw ?? "").Trim();
		if (v.Length == 0 || v == _modelSaved)
		{
			return;
		}
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object> { ["ai_model"] = v }
		});
		_modelSaved = v;
		if (!_model.Items.Contains(v))
		{
			_syncModel = true;
			try
			{
				_model.Items.Add(v);
			}
			finally
			{
				_syncModel = false;
			}
		}
		PageBase.Toast(I18n.L("模型已切换"), I18n.L("下一轮对话开始用 {0}", v), ToastKind.Success);
	}

	private async Task FetchModelsAsync(bool manual = false)
	{
		if (_modelFetching)
		{
			return;
		}
		_modelFetching = true;
		_modelFetch.IsEnabled = false;
		try
		{
			Dictionary<string, JsonElement> obj = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			foreach (KeyValuePair<string, JsonElement> item in obj)
			{
				dictionary[item.Key] = item.Value;
			}
			dictionary["ai_model"] = ModelValue();
			SettingsPage.AiModelList aiModelList = await PageBase.Api.TryCallAsync<SettingsPage.AiModelList>("ai_list_models", new
			{
				settings = dictionary
			});
			if (aiModelList == null)
			{
				PageBase.Toast(I18n.L("拉取失败"), I18n.L("没能从网关取到模型列表，可直接手填模型名"), ToastKind.Warning);
				return;
			}
			string text = ModelValue();
			_syncModel = true;
			try
			{
				_model.Items.Clear();
				foreach (string model in aiModelList.Models)
				{
					if (!string.IsNullOrWhiteSpace(model))
					{
						_model.Items.Add(model);
					}
				}
			}
			finally
			{
				_syncModel = false;
			}
			if (text.Length > 0)
			{
				SetModel(text);
			}
			if (aiModelList.Ok && aiModelList.Models.Count > 0)
			{
				if (manual)
				{
					PageBase.Toast(I18n.L("模型列表已刷新"), I18n.L("共 {0} 个可用模型", aiModelList.Models.Count), ToastKind.Success);
				}
			}
			else
			{
				PageBase.Toast(I18n.L("没取到模型"), string.IsNullOrEmpty(aiModelList.Error) ? I18n.L("网关没返回模型列表，可直接手填模型名") : aiModelList.Error, ToastKind.Warning);
			}
		}
		finally
		{
			_modelFetching = false;
			_modelFetch.IsEnabled = true;
		}
	}

	private async Task OpenPermissionPanelAsync()
	{
		string instance = (await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = "default"
		}, "default")) ?? "default";
		AiPermissionPanel panel = new AiPermissionPanel(instance);
		await panel.RefreshAsync();
		Dlg.Layer layer = Dlg.Panel(I18n.L("权限管理"), panel, 860.0);
		panel.CloseRequested = layer.Close;
	}

	protected override async Task LoadAsync()
	{
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, new AiStoreDto())) ?? new AiStoreDto();
		await SyncPermissionAsync();
		await SyncModelAsync();
		if (!_modelFetched)
		{
			_modelFetched = true;
			PageBase.Run(() => FetchModelsAsync());
		}
		if (_busy && !_store.Busy)
		{
			ResetRunState();
		}
		else if (!_busy && _store.Busy)
		{
			SetBusy(on: true);
		}
		RenderChats();
		if (!_busy || _runChatId.Length <= 0 || !(_runChatId == _store.ActiveId))
		{
			RenderMessages();
			RestorePendingCard();
		}
	}

	private void RestorePendingCard()
	{
		JsonElement? pc = _store.PendingCard;
		if (!pc.HasValue || pc.GetValueOrDefault().ValueKind != JsonValueKind.Object || !_store.Busy)
		{
			return;
		}
		string text = GetStr("kind");
		if (text.Length == 0)
		{
			return;
		}
		string text2 = GetStr("chat_id");
		if (text2.Length <= 0 || !(text2 != _store.ActiveId))
		{
			BridgeEvent ev = new BridgeEvent
			{
				Name = GetStr("name"),
				Label = GetStr("label"),
				Payload = pc.Value
			};
			SetBusy(on: true);
			if (_runChatId.Length == 0)
			{
				_runChatId = ((text2.Length > 0) ? text2 : _store.ActiveId);
			}
			if (text == "confirm")
			{
				ShowConfirm(ev);
			}
			else if (text == "ask")
			{
				ShowAsk(ev);
			}
		}
		string GetStr(string prop)
		{
			if (!pc.Value.TryGetProperty(prop, out var value) || value.ValueKind != JsonValueKind.String)
			{
				return "";
			}
			return value.GetString() ?? "";
		}
	}

	private void RenderChats()
	{
		_chatList.Children.Clear();
		string text = _histSearch?.Text?.Trim() ?? "";
		int num = 0;
		foreach (AiChatDto chat in _store.Chats)
		{
			string id = chat.Id;
			string text2 = (string.IsNullOrWhiteSpace(chat.Title) ? I18n.L("新对话") : chat.Title);
			if (text.Length > 0 && text2.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			num++;
			bool flag = id == _store.ActiveId;
			bool num2 = _busy && id == _runChatId;
			TextBlock textBlock = Ui.Txt(text2, 12.5, flag, flag ? "B.AccentText" : "B.InkSoft");
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			Grid grid = Ui.G(null, "Auto,*");
			if (num2)
			{
				Border border = new Border
				{
					Width = 6.0,
					Height = 6.0,
					CornerRadius = new CornerRadius(3.0),
					Margin = new Thickness(0.0, 0.0, 7.0, 0.0),
					ToolTip = I18n.L("正在运行")
				};
				border.SetResourceReference(Border.BackgroundProperty, "B.Accent");
				grid.Add(border.VCenter(), 0);
			}
			grid.Add(textBlock.VCenter(), 0, 1);
			Button button = new Button
			{
				Template = AiIcons.FlatButtonTemplate(),
				Content = grid,
				Padding = new Thickness(9.0, 7.0, 9.0, 7.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = Cursors.Hand,
				ToolTip = text2,
				Focusable = false
			};
			if (button.Content is Grid grid2)
			{
				grid2.HorizontalAlignment = HorizontalAlignment.Stretch;
			}
			if (flag)
			{
				button.SetResourceReference(Control.BackgroundProperty, "B.AccentSoft");
			}
			else
			{
				button.Background = Brushes.Transparent;
				button.MouseEnter += delegate
				{
					button.SetResourceReference(Control.BackgroundProperty, "B.Hover");
				};
				button.MouseLeave += delegate
				{
					button.Background = Brushes.Transparent;
				};
			}
			button.Click += delegate
			{
				if (!(id == _store.ActiveId))
				{
					PageBase.Run(() => SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_set_active", new
					{
						chat_id = id
					}, _store)));
				}
			};
			button.ContextMenu = DelMenu(id);
			_chatList.Children.Add(button.Stretch());
		}
		if (num == 0)
		{
			_chatList.Children.Add(Ui.Txt((text.Length > 0) ? I18n.L("没有匹配的对话") : I18n.L("还没有对话"), 12.0, bold: false, "B.InkFaint").M(8.0, 4.0));
		}
		AiChatDto activeChat = ActiveChat;
		_titleText.Text = ((activeChat == null || string.IsNullOrWhiteSpace(activeChat.Title)) ? I18n.L("新对话") : activeChat.Title);
		string text3 = PageBase.Win?.Prefs.CatalogInstance ?? "";
		_instText.Text = text3;
		_instPill.Visibility = ((text3.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private ContextMenu DelMenu(string id)
	{
		ContextMenu contextMenu = new ContextMenu();
		MenuItem menuItem = new MenuItem
		{
			Header = I18n.L("删除这个对话")
		};
		menuItem.Click += delegate
		{
			PageBase.Run(() => SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_delete_chat", new
			{
				chat_id = id
			}, _store)));
		};
		contextMenu.Items.Add(menuItem);
		return contextMenu;
	}

	private Task NewChatAsync()
	{
		return SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_new_chat", null, _store));
	}

	private async Task SwitchChatAsync(Func<Task<AiStoreDto?>> op)
	{
		string before = _store.ActiveId;
		_store = (await op()) ?? _store;
		if (_store.ActiveId != before || (_busy && ActiveChat == null))
		{
			await AbandonRunAsync();
		}
		if (!_busy)
		{
			_status.Text = "";
		}
		RenderChats();
		RenderMessages();
	}

	private async Task AbandonRunAsync()
	{
		if (_busy)
		{
			_abandoned = true;
			TaskCompletionSource<object?> ended = (_runEnded = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));
			if (!BackendBusy(await PageBase.Api.TryCallAsync<JsonElement>("ai_stop")))
			{
				_roundBubble = null;
				_round.Clear();
				_stream.Clear();
				_runChatId = "";
				_status.Text = "";
				SetBusy(on: false);
				_runEnded?.TrySetResult(null);
				_runEnded = null;
			}
			else
			{
				await Task.WhenAny(ended.Task, Task.Delay(3000));
				ResetRunState();
				PageBase.Toast(I18n.L("已停止上一个对话的回合"), I18n.L("切换对话时正在运行的那一轮已中断"));
			}
		}
	}

	private static bool BackendBusy(JsonElement r)
	{
		if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("busy", out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private Bubble OpenRound(string? thinking = null)
	{
		Bubble roundBubble = _roundBubble;
		if (roundBubble != null)
		{
			if (thinking != null)
			{
				roundBubble.SetThinking(thinking);
			}
			return roundBubble;
		}
		_round.Clear();
		_group?.Refresh();
		_group = null;
		Bubble bubble = new Bubble("assistant", "…", RetryFrom);
		bubble.SetThinking(thinking ?? I18n.L("正在想…"));
		Add(bubble);
		return _roundBubble = bubble;
	}

	private void CloseRound()
	{
		if (_roundBubble != null)
		{
			_roundBubble.SetText(_round.ToString());
			_roundBubble = null;
			_round.Clear();
		}
	}

	private void ResetRunState()
	{
		_roundBubble = null;
		_round.Clear();
		_stream.Clear();
		_runChatId = "";
		_abandoned = false;
		_status.Text = "";
		SetBusy(on: false);
		_runEnded?.TrySetResult(null);
		_runEnded = null;
	}

	private bool RunIsDisplayed(BridgeEvent ev)
	{
		string text = ChatIdOf(ev);
		if (text.Length == 0)
		{
			text = _runChatId;
		}
		if (text.Length != 0)
		{
			return text == _store.ActiveId;
		}
		return true;
	}

	private static string ChatIdOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("chat_id", out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private void RenderMessages()
	{
		_messages.Children.Clear();
		_toolLines.Clear();
		_taskLines.Clear();
		_roundBubble = null;
		_round.Clear();
		_stream.Clear();
		_group = null;
		_needWho = true;
		_lastUser = null;
		_emptyView = null;
		_planCard = null;
		AiChatDto activeChat = ActiveChat;
		_titleText.Text = ((activeChat == null || string.IsNullOrWhiteSpace(activeChat.Title)) ? I18n.L("新对话") : activeChat.Title);
		if (activeChat == null || activeChat.Messages.Count == 0)
		{
			_emptyView = EmptyState();
			_messages.Children.Add(_emptyView);
			_retry.IsEnabled = false;
			_rewind.IsEnabled = false;
			return;
		}
		Bubble lastAssistant = null;
		foreach (AiMsgDto message in activeChat.Messages)
		{
			if (message.Role == "user" && message.Id.StartsWith("compact_", StringComparison.Ordinal))
			{
				_messages.Children.Add(Divider(I18n.L("更早的对话已压缩成摘要")));
				continue;
			}
			bool flag;
			switch (message.Role)
			{
			case "user":
			case "assistant":
			case "error":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				string text = message.Content;
				if (!string.IsNullOrWhiteSpace(message.Note) && !text.Contains(message.Note, StringComparison.Ordinal))
				{
					text = (text + "\n\n" + message.Note).Trim();
				}
				Bubble bubble = new Bubble(message.Role, text, RetryFrom);
				if (message.Role == "user")
				{
					_needWho = true;
					_lastUser = bubble;
					lastAssistant = null;
				}
				else
				{
					AddWhoIfNeeded(animate: false);
					lastAssistant = bubble;
				}
				_messages.Children.Add(bubble);
			}
		}
		AiPlanDto plan = activeChat.Plan;
		if (plan != null)
		{
			List<AiPlanItemDto> items = plan.Items;
			if (items != null && items.Count > 0)
			{
				_planCard = new PlanCard((from i in activeChat.Plan.Items
					where !string.IsNullOrWhiteSpace(i.Title)
					select (Title: i.Title, Status: i.Status)).ToList());
				_messages.Children.Add(_planCard);
			}
		}
		Motion.Stagger(_messages, 18.0, 200.0, 8.0);
		_retry.IsEnabled = !_busy && LastUserText().Length > 0;
		_rewind.IsEnabled = _retry.IsEnabled;
		SyncTurnActions(lastAssistant);
		ScrollDown();
	}

	private void SyncTurnActions(Bubble? lastAssistant = null)
	{
		foreach (Bubble item in _messages.Children.OfType<Bubble>())
		{
			if (item.IsMine)
			{
				item.Rewind = null;
			}
			else
			{
				item.Retry = null;
			}
		}
		if (_busy)
		{
			return;
		}
		if (_lastUser != null)
		{
			_lastUser.Rewind = delegate
			{
				PageBase.Run(RewindAsync);
			};
		}
		Bubble bubble = lastAssistant ?? _messages.Children.OfType<Bubble>().LastOrDefault((Bubble b) => !b.IsMine);
		int num = ((bubble == null) ? (-1) : _messages.Children.IndexOf(bubble));
		int num2 = ((_lastUser == null) ? (-1) : _messages.Children.IndexOf(_lastUser));
		if (bubble != null && num > num2 && LastUserText().Length > 0)
		{
			bubble.Retry = delegate
			{
				PageBase.Run(RetryAsync);
			};
		}
	}

	private FrameworkElement EmptyState()
	{
		Border border = new Border
		{
			Width = 52.0,
			Height = 52.0,
			CornerRadius = new CornerRadius(16.0),
			Child = AiIcons.Icon("M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z", 26.0, "B.OnAccentStrong").Mid(),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		border.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
		TextBlock textBlock = Ui.Txt(I18n.L("今天想折腾点什么？"), 20.0, bold: true, "B.Ink");
		textBlock.HorizontalAlignment = HorizontalAlignment.Center;
		textBlock.Margin = new Thickness(0.0, 14.0, 0.0, 0.0);
		TextBlock textBlock2 = Ui.Txt(I18n.L("下游戏、装模组、读崩溃日志，改动前都会先问你"), 13.0, bold: false, "B.InkMuted");
		textBlock2.HorizontalAlignment = HorizontalAlignment.Center;
		textBlock2.Margin = new Thickness(0.0, 6.0, 0.0, 18.0);
		UniformGrid uniformGrid = new UniformGrid
		{
			Columns = 2,
			HorizontalAlignment = HorizontalAlignment.Center,
			Width = 620.0
		};
		(string, string, string, string)[] quickStarts = QuickStarts;
		for (int i = 0; i < quickStarts.Length; i++)
		{
			(string, string, string, string) tuple = quickStarts[i];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			string item3 = tuple.Item3;
			string item4 = tuple.Item4;
			string p = item4;
			Border border2 = new Border
			{
				Width = 30.0,
				Height = 30.0,
				CornerRadius = new CornerRadius(9.0),
				Child = AiIcons.Icon(item3, 16.0, "B.AccentText").Mid()
			};
			border2.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
			Grid grid = Ui.G(null, "Auto,*");
			grid.Add(border2.TopLeft(), 0);
			grid.Add(Ui.V(2.0, Ui.Txt(item, 13.0, bold: true, "B.Ink"), Ui.Txt(item2, 12.0, bold: false, "B.InkMuted")).M(11.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			Button card = new Button
			{
				Template = AiIcons.FlatButtonTemplate(),
				Content = grid,
				Padding = new Thickness(14.0, 12.0, 14.0, 12.0),
				Margin = new Thickness(5.0),
				BorderThickness = new Thickness(1.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = Cursors.Hand,
				Focusable = false
			};
			grid.HorizontalAlignment = HorizontalAlignment.Left;
			card.SetResourceReference(Control.BorderBrushProperty, "B.Line");
			card.SetResourceReference(Control.BackgroundProperty, "B.Paper");
			card.MouseEnter += delegate
			{
				card.SetResourceReference(Control.BackgroundProperty, "B.Hover");
			};
			card.MouseLeave += delegate
			{
				card.SetResourceReference(Control.BackgroundProperty, "B.Paper");
			};
			card.Click += delegate
			{
				PageBase.Run(() => SendTextAsync(p));
			};
			uniformGrid.Children.Add(card);
		}
		SPanel sPanel = Ui.V(0.0, border, textBlock, textBlock2, uniformGrid);
		sPanel.HorizontalAlignment = HorizontalAlignment.Center;
		sPanel.VerticalAlignment = VerticalAlignment.Center;
		sPanel.Margin = new Thickness(0.0, 90.0, 0.0, 40.0);
		return sPanel;
	}

	private static FrameworkElement Divider(string text)
	{
		Grid grid = Ui.G(null, "*,Auto,*");
		Border border = new Border
		{
			Height = 1.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		Border border2 = new Border
		{
			Height = 1.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		border.SetResourceReference(Border.BackgroundProperty, "B.Line");
		border2.SetResourceReference(Border.BackgroundProperty, "B.Line");
		grid.Add(border, 0);
		grid.Add(Ui.H(6.0, AiIcons.Icon("M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4", 12.0, "B.InkFaint").VCenter(), Ui.Txt(text, 11.5, bold: false, "B.InkFaint").VCenter()).M(10.0, 0.0, 10.0), 0, 1);
		grid.Add(border2, 0, 2);
		grid.Margin = new Thickness(0.0, 2.0, 0.0, 2.0);
		return grid;
	}

	private static FrameworkElement Note(string text, string icon = "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z M12 9v4 M12 17h.01")
	{
		Border border = new Border();
		border.CornerRadius = new CornerRadius(8.0);
		border.Padding = new Thickness(10.0, 6.0, 10.0, 6.0);
		border.Child = Ui.H(8.0, AiIcons.Icon(icon, 13.0, "B.Warn").VCenter(), Ui.Txt(text, 12.0, bold: false, "B.Warn").Wrap().VCenter());
		border.SetResourceReference(Border.BackgroundProperty, "B.WarnSoft");
		return border;
	}

	private void AddWhoIfNeeded(bool animate = true)
	{
		if (_needWho)
		{
			_needWho = false;
			Border border = new Border
			{
				Width = 22.0,
				Height = 22.0,
				CornerRadius = new CornerRadius(7.0),
				Child = AiIcons.Icon("M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z", 12.0, "B.OnAccentStrong").Mid()
			};
			border.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
			SPanel sPanel = Ui.H(7.0, border.VCenter(), Ui.Txt(I18n.L("TipCL 助手"), 12.0, bold: false, "B.InkMuted").VCenter());
			sPanel.Margin = new Thickness(0.0, 6.0, 0.0, -4.0);
			sPanel.Tag = "who";
			_messages.Children.Add(sPanel);
			if (animate)
			{
				Motion.FadeIn(sPanel, 200.0, 8.0);
			}
		}
	}

	private void ScrollDown()
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			_scroll.ScrollToEnd();
		});
	}

	private void Add(UIElement el)
	{
		if (_emptyView != null)
		{
			_messages.Children.Remove(_emptyView);
			_emptyView = null;
		}
		if (el is FrameworkElement { Parent: Panel parent })
		{
			if (parent == _messages)
			{
				ScrollDown();
				return;
			}
			parent.Children.Remove(el);
		}
		if (el is Bubble { IsMine: not false } bubble)
		{
			_needWho = true;
			_group = null;
			_lastUser = bubble;
		}
		else if (!(el is ConfirmCard))
		{
			AddWhoIfNeeded();
		}
		_messages.Children.Add(el);
		Motion.FadeIn(el, 200.0, 8.0);
		ScrollDown();
	}

	private string LastUserText()
	{
		return ActiveChat?.Messages.LastOrDefault((AiMsgDto m) => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content) && !m.Id.StartsWith("steer_", StringComparison.Ordinal))?.Content ?? "";
	}

	private Task SendAsync()
	{
		return SendTextAsync(_input.Text);
	}

	private void RetryFrom(string text)
	{
		PageBase.Run(() => SendTextAsync(text));
	}

	private Task RetryAsync()
	{
		string text = LastUserText();
		if (text.Length != 0)
		{
			return SendTextAsync(text);
		}
		return Task.CompletedTask;
	}

	private async Task SendTextAsync(string raw, bool echo = true)
	{
		string text = (raw ?? "").Trim();
		if (text.Length == 0)
		{
			return;
		}
		if (_busy && _pendingConfirm != null && !_pendingConfirm.Answered)
		{
			_pendingConfirm.Reject();
		}
		if (_busy)
		{
			JsonElement jsonElement = await PageBase.Api.TryCallAsync<JsonElement>("ai_steer", new { text });
			if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("ok", out var value) && value.ValueKind == JsonValueKind.True)
			{
				if (echo)
				{
					_input.Clear();
					Add(new Bubble("user", text, RetryFrom));
					PageBase.Toast(I18n.L("已插队"), I18n.L("这句话会立刻交给正在运行的助手"));
				}
				return;
			}
			TaskCompletionSource<object> taskCompletionSource = _runEnded ?? (_runEnded = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));
			await Task.WhenAny(taskCompletionSource.Task, Task.Delay(3000));
			if (_busy)
			{
				PageBase.Toast(I18n.L("上一条还在处理"), I18n.L("等它说完，或点「停止」再发"), ToastKind.Warning);
				return;
			}
		}
		if (echo)
		{
			_input.Clear();
		}
		_runChatId = _store.ActiveId;
		SetBusy(on: true);
		_status.Text = I18n.L("思考中…");
		if (echo)
		{
			Add(new Bubble("user", text, RetryFrom));
		}
		_stream.Clear();
		_roundBubble = OpenRound();
		Dictionary<string, object> launch = new Dictionary<string, object>
		{
			["instance"] = PageBase.Win?.Prefs.CatalogInstance ?? "",
			["username"] = PageBase.Win?.Prefs.LastUser ?? ""
		};
		OpResult opResult = await PageBase.Api.TryCallAsync<OpResult>("ai_send", new
		{
			text = text,
			chat_id = _store.ActiveId,
			launch = launch
		});
		if (opResult == null || !opResult.Ok)
		{
			string text2 = opResult?.Message;
			string text3 = ((text2 != null && text2.Length > 0) ? text2 : I18n.L("后端没有响应，请稍后再试"));
			if (_roundBubble != null)
			{
				_messages.Children.Remove(_roundBubble);
			}
			Add(new Bubble("error", text3, RetryFrom));
			ResetRunState();
			_status.Text = text3;
		}
	}

	private async Task StopAsync()
	{
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_stop");
		_status.Text = I18n.L("已停止");
		if (!BackendBusy(r))
		{
			ResetRunState();
			_status.Text = I18n.L("已停止");
		}
	}

	private async Task RewindAsync()
	{
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_rewind", new
		{
			chat_id = (_store.ActiveId ?? "")
		});
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
		RenderMessages();
		JsonElement value;
		bool flag = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("ok", out value) && value.GetBoolean();
		JsonElement value2;
		bool flag2 = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("disk_changed", out value2) && value2.GetBoolean();
		if (!flag && flag2)
		{
			_status.Text = I18n.L("对话已回退，但磁盘改动没能全部还原，请手动检查相关文件");
			return;
		}
		if (!flag)
		{
			JsonElement value3;
			string text = ((r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
			_status.Text = ((text.Length > 0) ? text : I18n.L("撤回失败"));
			return;
		}
		int num = 0;
		if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("restored_files", out var value4) && value4.TryGetInt32(out var value5))
		{
			num = value5;
		}
		JsonElement value6;
		if (flag2)
		{
			_status.Text = string.Format(I18n.L("已撤回：{0} 个文件的改动已还原"), num);
		}
		else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("rollbackable", out value6) && value6.ValueKind == JsonValueKind.False)
		{
			_status.Text = I18n.L("已撤回：只回退了对话，这一轮的磁盘改动无法自动还原");
		}
		else
		{
			_status.Text = I18n.L("已撤回：只回退了对话，这一轮没有可还原的磁盘改动");
		}
	}

	private void SetBusy(bool on)
	{
		bool flag = _busy != on;
		_busy = on;
		_send.IsEnabled = !on;
		_stop.IsEnabled = on;
		_sendStop.Content = (on ? _stop : _send);
		_rewind.IsEnabled = !on && LastUserText().Length > 0;
		_retry.IsEnabled = !on && LastUserText().Length > 0;
		if (!on)
		{
			foreach (StepGroup item in _messages.Children.OfType<StepGroup>())
			{
				item.Complete();
			}
			_group = null;
			ClearDock();
		}
		SyncPlaceholder();
		SyncTurnActions();
		if (flag)
		{
			RenderChats();
		}
	}

	private void ClearDock()
	{
		if (_pendingConfirm != null)
		{
			_ = _pendingConfirm.Answered;
		}
		_pendingConfirm = null;
		_dock.Children.Clear();
	}

	private ToolLine Line(string name, string text, ToolState state)
	{
		if (_toolLines.TryGetValue(name, out ToolLine value))
		{
			value.SetText(text);
			value.SetState(state);
			FindGroup(value)?.Refresh();
			return value;
		}
		value = new ToolLine(text, name);
		value.SetState(state);
		_toolLines[name] = value;
		if (_group == null || _group.Parent != _messages)
		{
			_group = new StepGroup();
			Add(_group);
		}
		_group.AddLine(value);
		ScrollDown();
		return value;
	}

	private static StepGroup? FindGroup(DependencyObject el)
	{
		for (DependencyObject dependencyObject = el; dependencyObject != null; dependencyObject = LogicalTreeHelper.GetParent(dependencyObject))
		{
			if (dependencyObject is StepGroup result)
			{
				return result;
			}
		}
		return null;
	}

	public override void OnEvent(BridgeEvent ev)
	{
		string text = ev.Event;
		if (text == null)
		{
			return;
		}
		switch (text.Length)
		{
		case 8:
			switch (text[0])
			{
			case 'a':
				if (text == "ai.delta" && RunIsDisplayed(ev))
				{
					_stream.Append(ev.Text);
					Bubble bubble = OpenRound();
					_round.Append(ev.Text);
					bubble.SetText(_round.ToString(), live: true);
					ScrollDown();
				}
				break;
			case 'p':
			{
				if (text == "progress" && _taskLines.TryGetValue(ev.TaskId, out ToolLine value2))
				{
					value2.SetProgress(ev.Current, ev.Total, ev.Message);
				}
				break;
			}
			case 'f':
				if (text == "finished")
				{
					if (_taskLines.TryGetValue(ev.TaskId, out ToolLine value))
					{
						value.Finish(ev.Success, ev.Message);
					}
					ReportPendingTask(ev);
				}
				break;
			}
			break;
		case 7:
			switch (text[3])
			{
			case 'd':
			{
				if (!(text == "ai.done"))
				{
					break;
				}
				string text4 = ChatIdOf(ev);
				if (_busy && _runChatId.Length > 0 && text4.Length > 0 && text4 != _runChatId)
				{
					break;
				}
				bool quiet = _abandoned;
				bool mine = RunIsDisplayed(ev);
				_status.Text = "";
				if (!quiet && mine)
				{
					NotifyStop(ev);
				}
				if (mine)
				{
					ShowUsage(ev);
				}
				string text5 = StopNote(ev);
				if (mine && (_round.Length > 0 || text5.Length > 0))
				{
					string text6 = _round.ToString();
					if (text5.Length > 0 && !text6.Contains(text5))
					{
						text6 = ((text6.Length > 0) ? (text6 + "\n\n" + text5) : text5);
					}
					OpenRound().SetText(text6);
				}
				if (mine)
				{
					foreach (var (key, item) in PendingTasksOf(ev))
					{
						_aiPendingTasks[key] = (item, ChatIdOf(ev));
					}
				}
				ResetRunState();
				string unsent3 = UnsentOf(ev);
				PageBase.Run(async delegate
				{
					AiStoreDto aiStoreDto = await PageBase.Api.TryCallAsync("ai_list_chats", null, _store);
					_store = aiStoreDto ?? _store;
					RenderChats();
					if (!mine)
					{
						RenderMessages();
					}
					if (mine && !quiet && unsent3.Length > 0)
					{
						await SendTextAsync(unsent3, echo: false);
					}
				});
				break;
			}
			case 'f':
			{
				if (!(text == "ai.fail"))
				{
					break;
				}
				string text2 = ChatIdOf(ev);
				if (_busy && _runChatId.Length > 0 && text2.Length > 0 && text2 != _runChatId)
				{
					break;
				}
				bool flag = RunIsDisplayed(ev);
				bool abandoned = _abandoned;
				_status.Text = (ev.Stopped ? "" : ev.Text);
				if (flag && _roundBubble != null && _round.Length > 0)
				{
					_roundBubble.SetText(_round.ToString());
				}
				if (ev.Stopped)
				{
					if (flag && _roundBubble != null && _round.Length == 0)
					{
						_roundBubble.SetText(I18n.L("已停止"));
					}
					if (!abandoned)
					{
						PageBase.Toast(I18n.L("已停止"), I18n.L("可以继续说下一句"));
					}
				}
				else if (flag)
				{
					if (_roundBubble != null && _round.Length == 0)
					{
						_roundBubble.SetText(ev.Text);
					}
					else
					{
						Add(new Bubble("error", ev.Text, RetryFrom));
					}
				}
				else
				{
					PageBase.Toast(I18n.L("助手出错"), ev.Text, ToastKind.Error);
				}
				string text3 = _status.Text;
				ResetRunState();
				_status.Text = text3;
				string unsent2 = UnsentOf(ev);
				if (flag && !abandoned && unsent2.Length > 0)
				{
					PageBase.Run(() => SendTextAsync(unsent2, echo: false));
				}
				break;
			}
			}
			break;
		case 9:
			if (text == "ai.status" && RunIsDisplayed(ev))
			{
				OnStatus(ev);
			}
			break;
		case 10:
			if (text == "ai.confirm" && RunIsDisplayed(ev))
			{
				ShowConfirm(ev);
			}
			break;
		case 6:
			if (text == "ai.ask" && RunIsDisplayed(ev))
			{
				ShowAsk(ev);
			}
			break;
		}
	}

	internal static List<(string TaskId, string Name)> PendingTasksOf(BridgeEvent ev)
	{
		List<(string, string)> list = new List<(string, string)>();
		if (StopReasonOf(ev) != "pending_task" || !ev.Payload.TryGetProperty("pending_tasks", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Object)
			{
				JsonElement value2;
				string text = ((item.TryGetProperty("task_id", out value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? "") : "");
				if (text.Length != 0)
				{
					JsonElement value3;
					string text2 = ((item.TryGetProperty("name", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
					list.Add((text, (text2.Length > 0) ? text2 : I18n.L("任务")));
				}
			}
		}
		return list;
	}

	internal static string PendingTaskReport(string name, BridgeEvent finished)
	{
		return "[后台任务回报] " + name + " " + (finished.Success ? I18n.L("成功") : I18n.L("失败")) + "：" + finished.Message;
	}

	private void ReportPendingTask(BridgeEvent ev)
	{
		if (!_aiPendingTasks.TryGetValue(ev.TaskId, out var task))
		{
			return;
		}
		_aiPendingTasks.Remove(ev.TaskId);
		if (task.ChatId.Length <= 0 || !(task.ChatId != _store.ActiveId))
		{
			if (!_busy)
			{
				PageBase.Toast(I18n.L("后台任务完成"), I18n.L("助手回来汇报结果"));
			}
			PageBase.Run(() => SendTextAsync(PendingTaskReport(task.Name, ev), echo: false));
		}
	}

	internal static string UnsentOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("unsent", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return "";
		}
		List<string> list = new List<string>();
		foreach (JsonElement item in value.EnumerateArray())
		{
			string text = ((item.ValueKind == JsonValueKind.String) ? (item.GetString() ?? "").Trim() : "");
			if (text.Length > 0)
			{
				list.Add(text);
			}
		}
		return string.Join("\n\n", list);
	}

	private static string StopReasonOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("stop_reason", out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static string DetailOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("detail", out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static string StopNote(BridgeEvent ev)
	{
		string text = StopReasonOf(ev) switch
		{
			"no_tool_call" => I18n.L("它没有真的开始执行：模型只回了文字，没有调用任何工具。"), 
			"max_rounds" => I18n.L("步骤太多，先停在这里。你可以让我继续。"), 
			"pending_task" => I18n.L("下载/安装还在后台跑，可以在「下载任务」里看进度。"), 
			"stream_failed" => I18n.L("接口这轮没有返回内容，已停止。"), 
			"empty_response" => I18n.L("接口返回了空回复。"), 
			_ => "", 
		};
		if (text.Length == 0)
		{
			return "";
		}
		string text2 = DetailOf(ev);
		if (text2.Length > 0 && !text.Contains(text2))
		{
			text = text + I18n.L("（") + text2 + I18n.L("）");
		}
		return I18n.L("（提示：") + text + I18n.L("）");
	}

	private void NotifyStop(BridgeEvent ev)
	{
		string text = StopReasonOf(ev);
		if (!(text == "no_tool_call"))
		{
			if (text == "pending_task")
			{
				PageBase.Toast(I18n.L("任务还在后台跑"), I18n.L("可以在「下载任务」里查进度。"));
			}
		}
		else
		{
			PageBase.Toast(I18n.L("它没有真的开始执行"), I18n.L("模型只回了文字，没有调用任何工具。"), ToastKind.Warning);
		}
	}

	private void OnStatus(BridgeEvent ev)
	{
		string name = (string.IsNullOrEmpty(ev.Name) ? ev.Kind : ev.Name);
		string text = (string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label);
		switch (ev.Kind)
		{
		case "plan":
			ShowPlan(ev);
			break;
		case "compact":
			Add(Divider(I18n.L("更早的对话已压缩成摘要")));
			break;
		case "checkpoint_warn":
		{
			JsonElement value2;
			string text3 = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("message", out value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? "") : "");
			PageBase.Toast(I18n.L("无法一键撤回"), (text3.Length > 0) ? text3 : I18n.L("检查点不可用，本轮改动无法自动撤回"), ToastKind.Warning);
			break;
		}
		case "model_fallback":
		{
			JsonElement value3;
			string text4 = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("model", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
			PageBase.Toast(I18n.L("已切换备用模型"), text4, ToastKind.Warning);
			Add(Note((text4.Length > 0) ? I18n.L("主模型暂时不可用，本轮已切换到 {0}", text4) : I18n.L("主模型暂时不可用，本轮已切换到备用模型")));
			break;
		}
		case "thinking":
			_status.Text = I18n.L("思考中…");
			OpenRound();
			break;
		case "think":
		{
			JsonElement value;
			bool flag = ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("after_tools", out value) && value.ValueKind == JsonValueKind.True;
			string thinking = (flag ? I18n.L("搜完了，正在整理…") : I18n.L("正在想…"));
			_status.Text = (flag ? I18n.L("搜完了，正在整理…") : I18n.L("思考中…"));
			OpenRound(thinking);
			break;
		}
		case "tool":
			if (ev.Name == "ask_user")
			{
				if (_roundBubble == null)
				{
					OpenRound(I18n.L("请在下面选一下"));
				}
			}
			else
			{
				CloseRound();
				Line(name, I18n.L("准备：") + text, ToolState.Prepare);
				_status.Text = I18n.L("正在执行操作…");
			}
			break;
		case "tool_run":
		case "tool_start":
			CloseRound();
			Line(name, I18n.L("执行中：") + text, ToolState.Running);
			_status.Text = I18n.L("正在执行操作…");
			break;
		case "tool_done":
		{
			CloseRound();
			ToolLine toolLine = Line(name, I18n.L("完成：") + text, ToolState.Done);
			string text2 = TaskIdOf(ev);
			if (text2.Length > 0)
			{
				toolLine.BindTask();
				_taskLines[text2] = toolLine;
			}
			_status.Text = I18n.L("操作完成");
			break;
		}
		case "tool_skip":
			CloseRound();
			Line(name, I18n.L("已跳过：") + text, ToolState.Skipped);
			break;
		default:
			if (!string.IsNullOrWhiteSpace(ev.Label))
			{
				_status.Text = ev.Label;
			}
			break;
		}
	}

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
		if (ev.Payload.TryGetProperty("task_id", out var value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? "";
		}
		if (!ev.Payload.TryGetProperty("result", out var value2) || value2.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		string text = value2.GetString() ?? "";
		if (!text.StartsWith("{", StringComparison.Ordinal))
		{
			return "";
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			JsonElement value3;
			return (jsonDocument.RootElement.TryGetProperty("task_id", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "";
		}
		catch
		{
			return "";
		}
	}

	private void ShowUsage(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
		{
			long num = GetNum("input_estimated");
			long num2 = GetNum("input") + num;
			long num3 = GetNum("output");
			if (num2 != 0L || num3 != 0L)
			{
				JsonElement value;
				long value2;
				string arg = ((u.TryGetProperty("real_requests", out value) && value.TryGetInt64(out value2) && value2 > 0 && num == 0L) ? "" : I18n.L("（估算）"));
				long requests = GetNum("requests");
				double num4 = AiUiText.ContextRatio(num2, requests, _contextWindow);
				_ringHost.Content = AiIcons.Ring(num4);
				_usageLabel.Text = $"{num4 * 100.0:0}% · {AiUiText.Tokens(num2)}";
				string toolTip = string.Format(I18n.L("本会话：输入 {0} / 输出 {1} tokens{2}"), Fmt(num2), Fmt(num3), arg) + "\n" + string.Format(I18n.L("上下文窗口 {0}，圆环为每轮平均占用的估算"), AiUiText.Tokens(_contextWindow));
				_usageLabel.ToolTip = toolTip;
				_ringHost.ToolTip = toolTip;
			}
		}
		static string Fmt(long n)
		{
			if (n < 1000)
			{
				return n.ToString();
			}
			return $"{(double)n / 1000.0:0.0}k";
		}
		long GetNum(string prop)
		{
			if (!u.TryGetProperty(prop, out var value3) || !value3.TryGetInt64(out var value4))
			{
				return 0L;
			}
			return value4;
		}
	}

	private void ShowPlan(BridgeEvent ev)
	{
		List<(string, string)> list = new List<(string, string)>();
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("items", out var value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item2 in value.EnumerateArray())
			{
				if (item2.ValueKind == JsonValueKind.Object)
				{
					JsonElement value2;
					string text = (item2.TryGetProperty("title", out value2) ? (value2.GetString() ?? "") : "");
					JsonElement value3;
					string item = (item2.TryGetProperty("status", out value3) ? (value3.GetString() ?? "pending") : "pending");
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add((text, item));
					}
				}
			}
		}
		if (list.Count != 0)
		{
			if (_planCard != null)
			{
				_messages.Children.Remove(_planCard);
				_planCard = null;
			}
			_planCard = new PlanCard(list);
			Add(_planCard);
		}
	}

	private void ShowConfirm(BridgeEvent ev)
	{
		CloseRound();
		string label = (string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label);
		JsonElement value;
		string reason = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("reason", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "") ?? "";
		List<string> list = new List<string>();
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("preview", out var value2) && value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("lines", out var value3) && value3.ValueKind == JsonValueKind.Array)
		{
			if (value2.TryGetProperty("head", out var value4) && value4.ValueKind == JsonValueKind.String)
			{
				string text = value4.GetString() ?? "";
				if (text.Length > 0)
				{
					list.Add(text.StartsWith("@@", StringComparison.Ordinal) ? text : ("@@ " + text));
				}
			}
			foreach (JsonElement item in value3.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					string text2 = item.GetString() ?? "";
					if (text2.Length > 0)
					{
						list.Add(text2);
					}
				}
			}
		}
		ClearDock();
		ConfirmCard card = null;
		card = new ConfirmCard(ev.Name ?? "", label, reason, list, ConfirmCard.AllowAlways(ev), delegate(bool ok, bool always, string scope)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_confirm", new { ok, always, scope });
			});
		});
		card.Resolved += delegate(bool ok)
		{
			if (_pendingConfirm == card)
			{
				_pendingConfirm = null;
				_dock.Children.Remove(card);
			}
			TextBlock textBlock = Ui.Txt((ok ? I18n.L("已允许：") : I18n.L("已拒绝：")) + label, 12.0, bold: false, ok ? "B.InkMuted" : "B.Danger");
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			SPanel sPanel = Ui.H(6.0, AiIcons.Icon(ok ? "M20 6L9 17l-5-5" : "M18 6L6 18 M6 6l12 12", 12.0, ok ? "B.Accent" : "B.Danger").VCenter(), textBlock.VCenter());
			sPanel.Margin = new Thickness(6.0, 0.0, 0.0, 0.0);
			if (_group != null && _group.Parent == _messages)
			{
				_messages.Children.Insert(_messages.Children.IndexOf(_group) + 1, sPanel);
			}
			else
			{
				_messages.Children.Add(sPanel);
			}
			_status.Text = (ok ? I18n.L("正在执行操作…") : I18n.L("已拒绝"));
			SyncPlaceholder();
			ScrollDown();
		};
		_pendingConfirm = card;
		_dock.Children.Add(card);
		Motion.FadeIn(card, 200.0, 8.0);
		_status.Text = I18n.L("等你确认");
		SyncPlaceholder();
		ScrollDown();
	}

	private void ShowAsk(BridgeEvent ev)
	{
		CloseRound();
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("questions", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_answer", new
				{
					result = (object)null
				});
			});
			return;
		}
		JsonElement value2;
		AskCard el = new AskCard(ev.Payload.TryGetProperty("title", out value2) ? (value2.GetString() ?? "") : "", value, delegate(object? result)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_answer", new { result });
			});
		});
		Add(el);
		_status.Text = I18n.L("等你回答");
	}

	internal void SelfTestLoad(AiStoreDto store)
	{
		_store = store;
		RenderChats();
		RenderMessages();
	}

	internal void SelfTestUser(string text)
	{
		_runChatId = _store.ActiveId;
		SetBusy(on: true);
		Add(new Bubble("user", text, RetryFrom));
	}

	internal void SelfTestBusy(bool on)
	{
		SetBusy(on);
	}

	internal void SelfTestScrollEnd()
	{
		_scroll.ScrollToEnd();
	}

	public override void OnShown()
	{
		_input.Focus();
	}
}
