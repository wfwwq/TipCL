using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K8:对话列表 / 历史栏 / 切换与加载。【Windows 源】<c>Pages\AiPage.cs:1036-1047</c>(权限面板)、
/// <c>:1049-1073</c>(<c>LoadAsync</c>)、<c>:1075-1118</c>(<c>RestorePendingCard</c>)、
/// <c>:1120-1251</c>(<c>RenderChats</c> / <c>DelMenu</c> / <c>NewChatAsync</c> / <c>SwitchChatAsync</c>)、
/// <c>:2714-2742</c>(<c>NewChatWithPresetAsync</c> / <c>OpenChatAsync</c>)。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><c>MouseEnter/MouseLeave</c> → <c>PointerEntered/PointerExited</c>;<c>SetResourceReference</c> → <c>Dyn</c>;</item>
/// <item><b>预设标记</b>:<c>TipCL.Pages.Plugins.PresetCatalog.Name(chat.Preset)</c> → <see cref="AiPresets.Name"/>
///   (同一个 <c>ai_presets</c> RPC、同一套名称回退,理由见那个类);</item>
/// <item><b>插件视图</b>:Windows <c>:1211</c> 的 <c>_pluginViews.SetChat(chatId, preset, view)</c> 与
///   <c>:2740</c> 的 <c>_pluginViews.Select(view)</c> 依赖 mac 尚未移植的 <c>AiViewStrip</c> ⇒ **本次不搬**
///   (逐条列在交付报告;这两处只影响"插件视图标签条",不影响对话本身);</item>
/// <item><c>Dlg.Panel</c> / <c>Dlg.Layer.Close</c> / <c>Dlg.Confirm</c> 都是 mac 已落地件的同名成员,
///   <c>AiPermissionPanel</c> 的构造与 <c>RefreshAsync</c> 也与 Windows 同形;</item>
/// <item><b>行为逐条照搬</b>:搜索框按标题过滤、当前对话高亮、正在跑的那个对话一个圆点、删除菜单、
///   切对话时若那一轮在跑就 <c>AbandonRunAsync</c>、"挂起卡"恢复(confirm / ask 两种)。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>打开权限管理面板(【Windows 源】:1036-1047)。</summary>
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

	/// <summary>首次显示时把对话列表、权限档位、模型都拉一遍(【Windows 源】:1049-1073)。</summary>
	protected override async Task LoadAsync()
	{
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, new AiStoreDto())) ?? new AiStoreDto();
		await SyncPermissionAsync();
		await SyncModelAsync();
		if (!_modelFetched && _customModel)
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

	/// <summary>页面重新打开时把"还挂着的确认/提问卡"复原(【Windows 源】:1075-1118)。</summary>
	private void RestorePendingCard()
	{
		JsonElement? pc = _store.PendingCard;
		if (!pc.HasValue || pc.GetValueOrDefault().ValueKind != JsonValueKind.Object || !_store.Busy)
		{
			return;
		}

		string kind = GetStr("kind");
		if (kind.Length == 0)
		{
			return;
		}

		string chatId = GetStr("chat_id");
		if (chatId.Length <= 0 || !(chatId != _store.ActiveId))
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
				_runChatId = ((chatId.Length > 0) ? chatId : _store.ActiveId);
			}

			if (kind == "confirm")
			{
				ShowConfirm(ev);
			}
			else if (kind == "ask")
			{
				ShowAsk(ev);
			}
		}

		string GetStr(string prop)
		{
			if (!pc.Value.TryGetProperty(prop, out JsonElement value) || value.ValueKind != JsonValueKind.String)
			{
				return "";
			}

			return value.GetString() ?? "";
		}
	}

	/// <summary>重画历史栏那一列(【Windows 源】:1120-1212)。</summary>
	private void RenderChats()
	{
		_chatList.Children.Clear();
		string query = _histSearch.Text?.Trim() ?? "";
		int shown = 0;
		foreach (AiChatDto chat in _store.Chats)
		{
			string id = chat.Id;
			string label = (string.IsNullOrWhiteSpace(chat.Title) ? I18n.L("新对话") : chat.Title);
			if (query.Length > 0 && label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}

			shown++;
			bool active = id == _store.ActiveId;
			bool running = _busy && id == _runChatId;
			TextBlock textBlock = Ui.Txt(label, 12.5, active, active ? "B.AccentText" : "B.InkSoft");
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			Grid grid = Ui.G(null, "Auto,*,Auto");
			if (!string.IsNullOrEmpty(chat.Preset))
			{
				string presetName = AiPresets.Name(chat.Preset);
				Border presetTag = Ui.Tag(presetName, "B.AccentText", "B.AccentSoft");
				presetTag.MaxWidth = 76.0;
				ToolTip.SetTip(presetTag, I18n.L("预设：{0}", presetName));
				grid.Add(presetTag.M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
			}

			if (running)
			{
				Border dot = new Border
				{
					Width = 6.0,
					Height = 6.0,
					CornerRadius = new CornerRadius(999.0),
					Margin = new Thickness(0.0, 0.0, 7.0, 0.0)
				};
				ToolTip.SetTip(dot, I18n.L("正在运行"));
				dot.Dyn(Border.BackgroundProperty, "B.Accent");
				grid.Add(dot.VCenter(), 0);
			}

			grid.Add(textBlock.VCenter(), 0, 1);
			Button button = new Button
			{
				Template = AiIcons.FlatButtonTemplate(),
				Content = grid,
				Padding = new Thickness(9.0, 7.0, 9.0, 7.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = new Cursor(StandardCursorType.Hand),
				Focusable = false
			};
			ToolTip.SetTip(button, label);
			if (button.Content is Grid inner)
			{
				inner.HorizontalAlignment = HorizontalAlignment.Stretch;
			}

			if (active)
			{
				button.Dyn(TemplatedControl.BackgroundProperty, "B.AccentSoft");
			}
			else
			{
				button.Background = Brushes.Transparent;
				button.PointerEntered += delegate
				{
					button.Dyn(TemplatedControl.BackgroundProperty, "B.Hover");
				};
				button.PointerExited += delegate
				{
					button.Background = Brushes.Transparent;
				};
			}

			button.Click += delegate
			{
				if (id != _store.ActiveId)
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

		if (shown == 0)
		{
			_chatList.Children.Add(Ui.Txt((query.Length > 0) ? I18n.L("没有匹配的对话") : I18n.L("还没有对话"), 12.0, bold: false, "B.InkFaint").M(8.0, 4.0));
		}

		AiChatDto? activeChat = ActiveChat;
		_titleText.Text = ((activeChat == null || string.IsNullOrWhiteSpace(activeChat.Title)) ? I18n.L("新对话") : activeChat.Title);
		SyncInstChip();
		// 【mac 未移植】Windows :1211 还有 _pluginViews.SetChat(activeChat?.Id, Preset, View) —— 见文件头差异表第 3 条
	}

	/// <summary>对话行的右键菜单:删掉这个对话(【Windows 源】:1214-1230)。</summary>
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

	/// <summary>换一个对话(【Windows 源】:1237-1251):换了就把正在跑的那一轮放掉。</summary>
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

	/// <summary>按预设新建对话(【Windows 源】:2714-2721)。</summary>
	private Task NewChatWithPresetAsync(string preset, string view)
	{
		if (string.IsNullOrEmpty(preset))
		{
			return NewChatAsync();
		}

		return SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_new_chat", new { preset, view }, _store));
	}

	/// <summary>插件请求打开某个对话(plugins.ui_request kind "openChat"),可指定标签(aiView id);
	/// 【Windows 源】:2723-2742。标签那一半依赖未移植的 <c>AiViewStrip</c> ⇒ 只保留切对话这一半。</summary>
	public async Task OpenChatAsync(string chatId, string view, string plugin = "")
	{
		if (!string.IsNullOrEmpty(chatId) && chatId != _store.ActiveId)
		{
			// 插件刚建的对话列表里可能还没有:先刷新再切
			if (!_store.Chats.Any((AiChatDto c) => c.Id == chatId))
			{
				_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
			}

			await SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_set_active", new
			{
				chat_id = chatId
			}, _store));
		}
	}
}
