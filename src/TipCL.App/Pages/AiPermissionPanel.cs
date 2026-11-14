using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8a:AI **权限档位 + 自定义规则**面板(= Windows <c>Pages\AiPermissionPanel.cs</c>,156 行)。
///
/// 形状照抄:五档权限下拉(`default / acceptEdits / plan / yolo / custom`,文案走 <c>I18n.L</c>)+
/// 「保存档位」、「规则」一节(工具 34 项 / 允许·禁止·每次问 / 限定参数 / 范围 / 添加规则)+ 状态行 +
/// 规则列表(每条一行 + 删除钮,空时显示「还没有自定义规则」)。
/// 后端方法一字不改:`get_settings` / `save_settings`(写 <c>ai_permission_mode</c> +
/// <c>ai_confirm_writes = mode != "yolo"</c>)/ `ai_permission_rule_add` / `ai_permission_rules` / `ai_permission_rule_remove`。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item>规则列表的固定高度:WPF 写 <c>Ui.Scroll(_rules).Hh(290.0)</c>,mac 的 <c>UiExt</c> **没有 <c>Hh</c>**
///   ⇒ 这里用等价写法 <c>SmoothScroll.Height = 290.0</c>(同一含义:固定 290 高的可滚动区);</item>
/// <item><c>_content.Text.Trim()</c> → <c>_content.Text?.Trim() ?? ""</c>(mac 开了可空注解,语义不变);</item>
/// <item>Windows 构造函数里先建一节再 <c>Children.Clear()</c> 重加(反编译出来的赋值顺序痕迹)——
///   这里直接建**最终那一串**子元素,顺序与最终结果逐条相同:权限档位节 / 当前档位行 / 规则节 / 规则行 / 状态 / 规则列表;</item>
/// <item><c>SetResourceReference</c> 等一个没用(WPF 那份这文件里也没有)。</item>
/// </list>
/// </summary>
internal sealed class AiPermissionPanel : Border
{
	/// <summary>五档权限(后端取值,一字不改)。</summary>
	private static readonly string[] Modes = new string[5] { "default", "acceptEdits", "plan", "yolo", "custom" };

	private static readonly string[] ModeLabels = new string[5]
	{
		I18n.L("每次确认"),
		I18n.L("写直接执行"),
		I18n.L("只看不动"),
		I18n.L("全自动"),
		I18n.L("自定义"),
	};

	/// <summary>可配置的工具(34 项,顺序照抄 —— 它决定下拉里的顺序)。</summary>
	private static readonly string[] Tools = new string[34]
	{
		"ask_user", "create_instance", "delete_instance", "delete_mod", "diagnose_launch", "disable_mod", "download_java", "enable_mod", "get_crash_report", "get_java_list",
		"get_latest_log", "get_launcher_state", "inspect_mod", "install_datapack", "install_game", "install_mod", "install_modpack", "install_resourcepack", "install_shader", "install_world",
		"launch_game", "list_installed_versions", "list_instances", "list_mod_configs", "list_mods", "read_artifact", "read_mod_config", "scan_mod_conflicts", "search_content", "search_modpacks",
		"search_mods", "search_versions", "search_worlds", "write_mod_config",
	};

	private readonly string _instance;

	private readonly ComboBox _mode = Ui.Combo(ModeLabels, null, 170.0);

	private readonly ComboBox _tool = Ui.Combo(Tools, null, 190.0);

	private readonly ComboBox _behavior = Ui.Combo(new string[3]
	{
		I18n.L("允许"),
		I18n.L("禁止"),
		I18n.L("每次问"),
	}, null, 100.0);

	private readonly ComboBox _scope = Ui.Combo(new string[2]
	{
		I18n.L("所有实例"),
		I18n.L("仅当前实例"),
	}, null, 125.0);

	private readonly TextBox _content = Ui.Input(I18n.L("限定参数(留空 = 整个工具)"));

	private readonly SPanel _rules = Ui.V(6.0);

	private readonly TextBlock _status = Ui.Small("");

	private readonly TextBlock _usageLabel = Ui.Small("");

	/// <summary>宿主(设置卡)要求关闭时回调;Windows 同名成员。</summary>
	public Action? CloseRequested { get; set; }

	public AiPermissionPanel(string instance)
	{
		_instance = instance;
		Button save = Ui.Btn(I18n.L("保存档位"), BtnKind.Primary, delegate
		{
			PageBase.Run(SaveModeAsync);
		}, "\ue74e");
		SPanel modeRow = Ui.H(8.0, _mode, save);
		Button addRule = Ui.Btn(I18n.L("添加规则"), BtnKind.Soft, delegate
		{
			PageBase.Run(AddRuleAsync);
		}, "\ue710");
		Grid ruleRow = Ui.G(null, "*,Auto,Auto,Auto,Auto")
			.Add(_tool, 0)
			.Add(_behavior, 0, 1)
			.Add(((Control)_content).M(8.0, 0.0, 8.0), 0, 2)
			.Add(_scope, 0, 3)
			.Add(addRule, 0, 4);
		SmoothScroll rulesScroll = Ui.Scroll(_rules);
		// 【Windows 差异】WPF 是 Ui.Scroll(_rules).Hh(290.0);mac 的 UiExt 没有 Hh ⇒ 直接给固定高度
		rulesScroll.Height = 290.0;
		SPanel body = Ui.V(8.0,
			Ui.Section(I18n.L("权限档位"), I18n.L("与 Qt 版共用五档权限;自定义规则会覆盖默认判定")),
			Ui.Field(I18n.L("当前档位"), modeRow, I18n.L("点保存后立即对下一轮生效")),
			Ui.Section(I18n.L("规则"), I18n.L("规则可记到全局或当前实例;删除工具不提供始终允许")),
			ruleRow,
			_status,
			rulesScroll);
		base.Child = body;
		base.Padding = new Thickness(2.0);
	}

	/// <summary>拉当前档位 + 规则列表(进面板时调用)。</summary>
	public async Task RefreshAsync()
	{
		string mode = "default";
		Dictionary<string, JsonElement> settings =
			(await AppServices.Client.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		if (settings.TryGetValue("ai_permission_mode", out JsonElement value) && value.ValueKind == JsonValueKind.String)
		{
			mode = value.GetString() ?? "default";
		}
		int index = Array.IndexOf(Modes, mode);
		_mode.SelectedIndex = ((index >= 0) ? index : 0);
		await ReloadRulesAsync();
	}

	/// <summary>写档位(顺带写 <c>ai_confirm_writes</c>:只有 yolo 才关掉写确认)。</summary>
	private async Task SaveModeAsync()
	{
		int index = Clamp.Of(_mode.SelectedIndex, 0, Modes.Length - 1);
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["ai_permission_mode"] = Modes[index],
				["ai_confirm_writes"] = Modes[index] != "yolo",
			},
		});
		_status.Text = I18n.L("已保存");
	}

	private async Task AddRuleAsync()
	{
		string behavior = (new string[3] { "allow", "deny", "ask" })[Clamp.Of(_behavior.SelectedIndex, 0, 2)];
		string instance = ((_scope.SelectedIndex == 1) ? _instance : "");
		await AppServices.Client.CallAsync<List<AiPermissionRuleDto>>("ai_permission_rule_add", new
		{
			tool = _tool.Str(),
			behavior,
			content = _content.Text?.Trim() ?? "",
			instance,
		});
		_content.Clear();
		await ReloadRulesAsync();
	}

	private async Task ReloadRulesAsync()
	{
		List<AiPermissionRuleDto> rules =
			(await AppServices.Client.TryCallAsync("ai_permission_rules", null, new List<AiPermissionRuleDto>())) ?? new List<AiPermissionRuleDto>();
		_rules.Children.Clear();
		foreach (AiPermissionRuleDto row in rules)
		{
			string key = row.Key;
			string scope = (string.IsNullOrWhiteSpace(row.Scope) ? I18n.L("全局") : row.Scope);
			string line = scope + " · " + row.ToolName + (string.IsNullOrWhiteSpace(row.RuleContent) ? "" : (" · " + row.RuleContent)) + " · " + row.BehaviorLabel;
			Button remove = Ui.IconBtn("\ue74d", I18n.L("删除规则"), delegate
			{
				PageBase.Run(async delegate
				{
					await AppServices.Client.CallAsync<object>("ai_permission_rule_remove", new
					{
						key,
						instance = (row.Instance ?? ""),
					});
					await ReloadRulesAsync();
				});
			});
			_rules.Children.Add(Ui.G(null, "*,Auto").Add(Ui.Muted(line).Wrap(), 0).Add(remove, 0, 1));
		}
		if (rules.Count == 0)
		{
			_rules.Children.Add(Ui.Muted(I18n.L("还没有自定义规则")));
		}
	}
}
