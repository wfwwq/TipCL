using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K7:权限档位 / 模型选择 / 用量圆环与状态行。【Windows 源】<c>Pages\AiPage.cs:773-787</c>(<c>SyncPermTip</c>)、
/// <c>:857-1034</c>(<c>SyncPermissionAsync</c> … <c>FetchModelsAsync</c>)、<c>:2524-2561</c>(<c>ShowUsage</c>)。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>SetResourceReference(TextBlock/Control.ForegroundProperty, …)</c>
///   → <c>Dyn(TextBlock/TemplatedControl.ForegroundProperty, …)</c>;<c>ToolTip</c> → <c>ToolTip.SetTip</c>;</item>
/// <item><c>SettingsPage.AiModelList</c> 是 mac 设置页里已落地的**同一个嵌套类型**(<c>SettingsPage.cs:64</c>,
///   <c>Ok</c> / <c>Models</c> / <c>Error</c> 三字段一字不改),所以这里直接复用,不另起一个同形类;</item>
/// <item><c>_model.Items.Contains/Add</c>、<c>_model.Text</c>、<c>_model.IsKeyboardFocusWithin</c> 在 Avalonia 同名同义
///   (可编辑 <c>ComboBox</c> 的 <c>Text</c> 就是那一格);</item>
/// <item>用量圆环走既有的 <c>AiIcons.Ring(ratio)</c>,占比 / token 文案走既有的 <c>AiUiText.ContextRatio</c> /
///   <c>AiUiText.Tokens</c>(都是 C8a 已落地并与 Windows 逐值对齐的件);</item>
/// <item><b>行为逐条照搬</b>:权限档位写 <c>ai_permission_mode</c> + <c>ai_confirm_writes</c>(yolo 之外都为 true)、
///   内置网关模式 <c>ai_mode != "custom"</c> 时模型选择器整件隐藏、拉取模型列表成功后按可用模型重填且保留当前值、
///   <c>real_requests</c> 有值而估算为 0 时不显示「(估算)」。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>权限档位下方那句说明(【Windows 源】:773-787)。</summary>
	private void SyncPermTip()
	{
		int selectedIndex = _perm.SelectedIndex;
		_permTip.Text = selectedIndex switch
		{
			1 => I18n.L("当前「写直接执行」：写入不再询问，删除仍会先问你"),
			2 => I18n.L("当前「只看不动」：只读取和分析，不会改任何文件"),
			3 => I18n.L("当前「全自动」：所有操作都不再询问，请谨慎使用"),
			4 => I18n.L("当前「自定义」：按权限管理里的规则执行"),
			_ => I18n.L("当前「每次确认」：读取直接做，写入和删除都会先问你"),
		};
		_permTip.Dyn(TextBlock.ForegroundProperty, (selectedIndex == 3) ? "B.Danger" : "B.InkMuted");
		_perm.Dyn(TemplatedControl.ForegroundProperty, (selectedIndex == 3) ? "B.Danger" : "B.Ink");
	}

	/// <summary>从设置里读权限档位并选中(【Windows 源】:857-879)。</summary>
	private async Task SyncPermissionAsync()
	{
		Dictionary<string, JsonElement> settings = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		string mode = "default";
		if (settings.TryGetValue("ai_permission_mode", out JsonElement value) && value.ValueKind == JsonValueKind.String)
		{
			mode = value.GetString() ?? mode;
		}

		int index = Array.IndexOf(PermissionModes, mode);
		if (index < 0)
		{
			index = 0;
		}

		_syncPerm = true;
		try
		{
			_perm.SelectedIndex = index;
		}
		finally
		{
			_syncPerm = false;
		}
	}

	/// <summary>存权限档位(【Windows 源】:881-892)。</summary>
	private async Task SavePermissionModeAsync()
	{
		string mode = ((_perm.SelectedIndex >= 0 && _perm.SelectedIndex < PermissionModes.Length) ? PermissionModes[_perm.SelectedIndex] : "default");
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["ai_permission_mode"] = mode,
				["ai_confirm_writes"] = mode != "yolo"
			}
		});
	}

	/// <summary>当前模型名(输入框优先,其次下拉选中;【Windows 源】:894-902)。</summary>
	private string ModelValue()
	{
		string? text = _model.Text?.Trim();
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

	/// <summary>读设置:是不是自定义接口(决定模型选择器显不显示)与当前模型(【Windows 源】:927-941)。</summary>
	private async Task SyncModelAsync()
	{
		Dictionary<string, JsonElement> settings = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		// 内置网关模式模型固定,不给选
		_customModel = settings.TryGetValue("ai_mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "custom";
		_model.IsVisible = _customModel;
		_modelFetch.IsVisible = _model.IsVisible;
		if (!_model.IsKeyboardFocusWithin)
		{
			string text = ((settings.TryGetValue("ai_model", out JsonElement value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "");
			SetModel(text);
			_modelSaved = text.Trim();
		}
	}

	/// <summary>存模型(【Windows 源】:943-968)。</summary>
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

	/// <summary>从网关拉一遍模型清单(【Windows 源】:970-1034)。</summary>
	private async Task FetchModelsAsync(bool manual = false)
	{
		if (_modelFetching || !_customModel)
		{
			return;
		}

		_modelFetching = true;
		_modelFetch.IsEnabled = false;
		try
		{
			Dictionary<string, JsonElement> settings = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
			Dictionary<string, object> payload = new Dictionary<string, object>();
			foreach (KeyValuePair<string, JsonElement> item in settings)
			{
				payload[item.Key] = item.Value;
			}

			payload["ai_model"] = ModelValue();
			SettingsPage.AiModelList? list = await PageBase.Api.TryCallAsync<SettingsPage.AiModelList>("ai_list_models", new
			{
				settings = payload
			});
			if (list == null)
			{
				PageBase.Toast(I18n.L("拉取失败"), I18n.L("没能从网关取到模型列表，可直接手填模型名"), ToastKind.Warning);
				return;
			}

			string keep = ModelValue();
			_syncModel = true;
			try
			{
				_model.Items.Clear();
				foreach (string model in list.Models)
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

			if (keep.Length > 0)
			{
				SetModel(keep);
			}

			if (list.Ok && list.Models.Count > 0)
			{
				if (manual)
				{
					PageBase.Toast(I18n.L("模型列表已刷新"), I18n.L("共 {0} 个可用模型", list.Models.Count), ToastKind.Success);
				}
			}
			else
			{
				PageBase.Toast(I18n.L("没取到模型"), string.IsNullOrEmpty(list.Error) ? I18n.L("网关没返回模型列表，可直接手填模型名") : list.Error, ToastKind.Warning);
			}
		}
		finally
		{
			_modelFetching = false;
			_modelFetch.IsEnabled = true;
		}
	}

	/// <summary>顶栏右端的用量圆环 + token 文案(【Windows 源】:2524-2561)。</summary>
	private void ShowUsage(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("usage", out JsonElement u) && u.ValueKind == JsonValueKind.Object)
		{
			long estimated = GetNum("input_estimated");
			long input = GetNum("input") + estimated;
			long output = GetNum("output");
			if (input != 0L || output != 0L)
			{
				string estimatedMark = ((u.TryGetProperty("real_requests", out JsonElement real) && real.TryGetInt64(out long realCount) && realCount > 0 && estimated == 0L) ? "" : I18n.L("（估算）"));
				long requests = GetNum("requests");
				double ratio = AiUiText.ContextRatio(input, requests, _contextWindow);
				_ringHost.Content = AiIcons.Ring(ratio);
				_usageLabel.Text = $"{ratio * 100.0:0}% · {AiUiText.Tokens(input)}";
				string tip = string.Format(I18n.L("本会话：输入 {0} / 输出 {1} tokens{2}"), Fmt(input), Fmt(output), estimatedMark) + "\n" + string.Format(I18n.L("上下文窗口 {0},圆环为每轮平均占用的估算"), AiUiText.Tokens(_contextWindow));
				ToolTip.SetTip(_usageLabel, tip);
				ToolTip.SetTip(_ringHost, tip);
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
			if (!u.TryGetProperty(prop, out JsonElement value) || !value.TryGetInt64(out long number))
			{
				return 0L;
			}

			return number;
		}
	}
}
