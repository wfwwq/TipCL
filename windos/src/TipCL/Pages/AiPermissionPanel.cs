using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Core.Launcher.Ai;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// 权限规则下拉里的工具清单:直接来自工具注册表 <see cref="ToolMeta.Map" />,不再手抄一份。
/// 手抄版曾经只有 34 条,而注册表已经有 56 条 —— 少掉的那些(全部 extra 工具、dispatch_subagent、remember 等)
/// 在面板里根本选不到,想给它们加规则只能手改 ai_permissions.json。
/// 单独放一个 public 类是刻意的:仓库没有 InternalsVisibleTo,而 net8 测试宿主实例化不了 WPF 控件,
/// 这样「面板条目集合 == 注册表工具集合」这条验收才能用真实调用钉住(见 AiPermissionPanelToolsTests)。
/// </summary>
public static class AiPermissionTools
{
	/// <summary>面板规则下拉里的全部工具名:注册表里的工具,按名称排序(与原来的手抄表同一个排序口径)。</summary>
	public static string[] Names()
	{
		return ToolMeta.Map.Keys.OrderBy((string n) => n, StringComparer.Ordinal).ToArray();
	}
}

internal sealed class AiPermissionPanel : Border
{
	private static readonly string[] Modes = new string[5] { "default", "acceptEdits", "plan", "yolo", "custom" };

	private static readonly string[] ModeLabels = new string[5]
	{
		I18n.L("每次确认"),
		I18n.L("写直接执行"),
		I18n.L("只看不动"),
		I18n.L("全自动"),
		I18n.L("自定义")
	};

	private static readonly string[] Tools = AiPermissionTools.Names();

	private readonly string _instance;

	private readonly ComboBox _mode = Ui.Combo(ModeLabels, null, 170.0);

	private readonly ComboBox _tool = Ui.Combo(Tools, null, 190.0);

	private readonly ComboBox _behavior = Ui.Combo(new string[3]
	{
		I18n.L("允许"),
		I18n.L("禁止"),
		I18n.L("每次问")
	}, null, 100.0);

	private readonly ComboBox _scope = Ui.Combo(new string[2]
	{
		I18n.L("所有实例"),
		I18n.L("仅当前实例")
	}, null, 125.0);

	private readonly TextBox _content = Ui.Input(I18n.L("限定参数（留空 = 整个工具）"));

	private readonly SPanel _rules = Ui.V(6.0);

	private readonly TextBlock _status = Ui.Small("");

	private readonly TextBlock _usageLabel = Ui.Small("");

	public Action? CloseRequested { get; set; }

	public AiPermissionPanel(string instance)
	{
		_instance = instance;
		SPanel sPanel = Ui.V(8.0, Ui.Section(I18n.L("权限档位"), I18n.L("与 Qt 版共用五档权限；自定义规则会覆盖默认判定")), Ui.Field(I18n.L("当前档位"), _mode, I18n.L("点保存后立即对下一轮生效")));
		Button button = Ui.Btn(I18n.L("保存档位"), BtnKind.Primary, delegate
		{
			PageBase.Run(SaveModeAsync);
		}, "\ue74e");
		SPanel control = Ui.H(8.0, _mode, button);
		sPanel.Children.Clear();
		sPanel.Children.Add(Ui.Section(I18n.L("权限档位"), I18n.L("与 Qt 版共用五档权限；自定义规则会覆盖默认判定")));
		sPanel.Children.Add(Ui.Field(I18n.L("当前档位"), control, I18n.L("点保存后立即对下一轮生效")));
		Button el = Ui.Btn(I18n.L("添加规则"), BtnKind.Soft, delegate
		{
			PageBase.Run(AddRuleAsync);
		}, "\ue710");
		Grid element = Ui.G(null, "*,Auto,Auto,Auto,Auto").Add(_tool, 0).Add(_behavior, 0, 1)
			.Add(_content.M(8.0, 0.0, 8.0), 0, 2)
			.Add(_scope, 0, 3)
			.Add(el, 0, 4);
		sPanel.Children.Add(Ui.Section(I18n.L("规则"), I18n.L("规则可记到全局或当前实例；删除工具不提供始终允许")));
		sPanel.Children.Add(element);
		sPanel.Children.Add(_status);
		sPanel.Children.Add(Ui.Scroll(_rules).Hh(290.0));
		Child = sPanel;
		base.Padding = new Thickness(2.0);
	}

	public async Task RefreshAsync()
	{
		JsonElement value2;
		string value = ((((await AppServices.Client.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>()).TryGetValue("ai_permission_mode", out value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? "default") : "default");
		int num = Array.IndexOf<string>(Modes, value);
		_mode.SelectedIndex = ((num >= 0) ? num : 0);
		await ReloadRulesAsync();
	}

	private async Task SaveModeAsync()
	{
		int num = Clamp.Of(_mode.SelectedIndex, 0, Modes.Length - 1);
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["ai_permission_mode"] = Modes[num],
				["ai_confirm_writes"] = Modes[num] != "yolo"
			}
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
			behavior = behavior,
			content = _content.Text.Trim(),
			instance = instance
		});
		_content.Clear();
		await ReloadRulesAsync();
	}

	private async Task ReloadRulesAsync()
	{
		List<AiPermissionRuleDto> list = (await AppServices.Client.TryCallAsync("ai_permission_rules", null, new List<AiPermissionRuleDto>())) ?? new List<AiPermissionRuleDto>();
		_rules.Children.Clear();
		foreach (AiPermissionRuleDto row in list)
		{
			string key = row.Key;
			string text = (string.IsNullOrWhiteSpace(row.Scope) ? I18n.L("全局") : row.Scope);
			string text2 = text + " · " + row.ToolName + (string.IsNullOrWhiteSpace(row.RuleContent) ? "" : (" · " + row.RuleContent)) + " · " + row.BehaviorLabel;
			Button el = Ui.IconBtn("\ue74d", I18n.L("删除规则"), delegate
			{
				PageBase.Run(async delegate
				{
					await AppServices.Client.CallAsync<object>("ai_permission_rule_remove", new
					{
						key = key,
						instance = (row.Instance ?? "")
					});
					await ReloadRulesAsync();
				});
			});
			_rules.Children.Add(Ui.G(null, "*,Auto").Add(Ui.Muted(text2).Wrap(), 0).Add(el, 0, 1));
		}
		if (list.Count == 0)
		{
			_rules.Children.Add(Ui.Muted(I18n.L("还没有自定义规则")));
		}
	}
}
