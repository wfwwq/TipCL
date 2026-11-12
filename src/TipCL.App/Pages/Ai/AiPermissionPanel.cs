// 来源: Windows 版参考仓库 src/TipCL/Pages/AiPermissionPanel.cs(M4-H P5)。
// 权限管理面板:五档权限(每次确认 / 写直接执行 / 只看不动 / 全自动 / 自定义)+ 自定义规则(允许/禁止/每次问)。
// 档位与规则都落在 config.json(ai_permission_mode / ai_permission_rules),由 Core 的 ToolRunner 判定时读取 ——
// 与 Windows 版共用同一批配置键(值也一致),这样两边互换配置文件行为不变。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>权限管理面板(照 Windows 版 AiPermissionPanel)。</summary>
internal sealed class AiPermissionPanel : Border
{
    /// <summary>档位键(与 Windows 版一致,存进 config.json)。</summary>
    internal static readonly string[] Modes = { "default", "acceptEdits", "plan", "yolo", "custom" };

    /// <summary>档位文案(顺序与 Modes 对齐)。</summary>
    internal static readonly string[] ModeLabels = { "每次确认", "写直接执行", "只看不动", "全自动", "自定义" };

    private static readonly string[] Behaviors = { "allow", "deny", "ask" };

    private static readonly string[] BehaviorLabels = { "允许", "禁止", "每次问" };

    private readonly IApi _api;
    private readonly string _instance;
    private readonly ComboBox _mode = UiClass.Combo(ModeLabels.ToList(), "每次确认", 170);
    private readonly ComboBox _tool = UiClass.Combo(Tools.Names(), null, 190);
    private readonly ComboBox _behavior = UiClass.Combo(BehaviorLabels.ToList(), "允许", 100);
    private readonly ComboBox _scope = UiClass.Combo(new[] { "所有实例", "仅当前实例" }, "所有实例", 125);
    private readonly TextBox _content = UiClass.Input("限定参数(留空 = 整个工具)");
    private readonly StackPanel _rules = UiClass.V(6);
    private readonly TextBlock _status = UiClass.Small("");
    private readonly TextBlock _usage = UiClass.Small("");

    public AiPermissionPanel(IApi api, string instance)
    {
        _api = api;
        _instance = instance;

        var save = U.Btn("保存档位", BtnKind.Primary, (_, _) => _ = SaveModeAsync());
        var control = UiClass.H(8, _mode, save);
        var add = U.Btn("添加规则", BtnKind.Soft, (_, _) => _ = AddRuleAsync());
        var row = UiClass.G(null, "*,Auto,Auto,Auto,Auto");
        row.Add(_tool, 0, 0);
        row.Add(UiExt.Mk(_behavior, 8, 0, 0, 0), 0, 1);
        row.Add(UiExt.Mk(_content, 8, 0, 8, 0), 0, 2);
        row.Add(_scope, 0, 3);
        row.Add(UiExt.Mk(add, 8, 0, 0, 0), 0, 4);

        var body = UiClass.V(8,
            UiClass.Section("权限档位", "与 Windows 版共用五档权限;自定义规则会覆盖默认判定"),
            UiClass.Field("当前档位", control, "点保存后立即对下一轮生效"),
            UiClass.Section("规则", "规则可记到全局或当前实例;删除类工具不提供「始终允许」"),
            row,
            _status,
            _usage,
            new ScrollViewer { Content = _rules, MaxHeight = 290, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Child = body;
        Padding = new Thickness(2);
    }

    /// <summary>刷新当前档位与规则列表(宿主在弹出前后各调一次即可)。</summary>
    public async Task RefreshAsync()
    {
        string mode = Py.S(TipCL.Core.Launcher.Config.CONFIG.Get("ai_permission_mode", "default"));
        int index = Array.IndexOf(Modes, mode);
        _mode.SelectedIndex = index >= 0 ? index : 0;
        await ReloadRulesAsync();
    }

    private async Task SaveModeAsync()
    {
        int index = Math.Clamp(_mode.SelectedIndex, 0, Modes.Length - 1);
        await _api.CallAsync<object>("save_settings", new
        {
            data = new
            {
                ai_permission_mode = Modes[index],
                ai_confirm_writes = Modes[index] != "yolo",
            },
        });
        _status.Text = "已保存";
    }

    private async Task AddRuleAsync()
    {
        string behavior = Behaviors[Math.Clamp(_behavior.SelectedIndex, 0, 2)];
        string tool = _tool.SelectedItem as string ?? "";
        if (tool.Length == 0)
        {
            _status.Text = "先选一个工具";
            return;
        }
        var rules = Py.ListOr(TipCL.Core.Launcher.Config.CONFIG.Get("ai_permission_rules")).Select(Py.D).ToList();
        rules.RemoveAll(r => Py.S(r.Get("tool")) == tool
            && Py.S(r.Get("instance")) == (_scope.SelectedIndex == 1 ? _instance : ""));
        rules.Add(PyDict.Of(
            "tool", tool,
            "behavior", behavior,
            "content", (_content.Text ?? "").Trim(),
            "instance", _scope.SelectedIndex == 1 ? _instance : ""));
        await _api.CallAsync<object>("save_settings", new
        {
            data = new Dictionary<string, object> { ["ai_permission_rules"] = rules.Cast<object>().ToList() },
        });
        _content.Text = "";
        _status.Text = "已添加规则";
        await ReloadRulesAsync();
    }

    private async Task ReloadRulesAsync()
    {
        int denied = 0;
        int total = 0;
        _rules.Children.Clear();
        foreach (object? raw in Py.ListOr(TipCL.Core.Launcher.Config.CONFIG.Get("ai_permission_rules")))
        {
            PyDict rule = Py.D(raw);
            string tool = Py.S(rule.Get("tool"));
            string scope = Py.S(rule.Get("instance"));
            string behavior = Py.S(rule.Get("behavior"));
            string content = Py.S(rule.Get("content"));
            total++;
            if (behavior == "deny")
            {
                denied++;
            }
            int behaviorIndex = Array.IndexOf(Behaviors, behavior);
            string text = (scope.Length == 0 ? "全局" : scope) + " · " + tool
                + (content.Length > 0 ? " · " + content : "")
                + " · " + BehaviorLabels[behaviorIndex >= 0 ? behaviorIndex : 0];
            var del = UiClass.IconBtn("Close", "删除规则", (_, _) => _ = RemoveRuleAsync(tool, scope));
            _rules.Children.Add(UiClass.G(null, "*,Auto").Add(UiClass.Muted(text).Wrap(), 0, 0).Add(del, 0, 1));
        }
        if (total == 0)
        {
            _rules.Children.Add(UiClass.Muted("还没有自定义规则"));
        }
        int modeIndex = Math.Clamp(_mode.SelectedIndex, 0, ModeLabels.Length - 1);
        _usage.Text = "当前档位:" + ModeLabels[modeIndex]
            + (total > 0 ? $" · 自定义规则 {total} 条(其中禁止 {denied} 条)" : "");
    }

    private async Task RemoveRuleAsync(string tool, string instance)
    {
        var rules = Py.ListOr(TipCL.Core.Launcher.Config.CONFIG.Get("ai_permission_rules")).Select(Py.D)
            .Where(r => !(Py.S(r.Get("tool")) == tool && Py.S(r.Get("instance")) == instance))
            .ToList();
        await _api.CallAsync<object>("save_settings", new
        {
            data = new Dictionary<string, object> { ["ai_permission_rules"] = rules.Cast<object>().ToList() },
        });
        await ReloadRulesAsync();
    }
}
