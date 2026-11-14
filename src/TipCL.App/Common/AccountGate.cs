using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Common;

/// <summary>
/// 启动前的账号闸门(逐条对齐 Windows `Services/AccountGate.cs`)。
///
/// 参考版启动页的「用户」是一个**真实账号下拉**(`_acc = Ui.Combo()` + `AccountGate.ListAsync()`),
/// 点「启动」时先过 `EnsureLaunchAccountAsync` → `EnsureAsync`:选中的账号还在就用它;
/// 一个账号都没有就弹「创建离线用户」(名字 + 默认皮肤,「创建并继续」)就地建一个再继续;
/// 只有用户取消才真的不启动。
///
/// Linux 侧此前是一个自由文本框、也没有闸门:一个账号都没有时点「启动」只会拿到
/// `LaunchCore.RequireLaunchAccount` 抛出的「启动游戏前请先创建一个离线用户」,
/// 而那个提示指向「账号」页 —— 界面上没有一条能直接往下走的路。这一版把闸门补齐
/// (2026-11-13 收尾轮;这条缺口是「在 Linux 上真跑测试」时才被发现的,见 PROGRESS R21i)。
/// </summary>
internal static class AccountGate
{
    /// <summary>哨兵名:空 / 离线模式 = 还没有具体账号(与参考版 IsSentinel、LaunchCore.IsOfflineAccount 同口径)。</summary>
    public static bool IsSentinel(string? name)
    {
        string s = (name ?? "").Trim();
        return s.Length == 0 || s == "离线模式";
    }

    /// <summary>名字合法:1–16 个字符、不含空白、不是哨兵(照参考版 ValidName)。</summary>
    public static bool ValidName(string? name)
    {
        string s = (name ?? "").Trim();
        return s.Length is >= 1 and <= 16 && !s.Any(char.IsWhiteSpace) && !IsSentinel(s);
    }

    /// <summary>
    /// 真实账号名列表(排除哨兵;当前账号排最前 —— 对齐参考版 `orderby Active descending`)。
    /// 读的是同一个 `get_account_rows`,所以与「账号」页看到的是同一份数据。
    /// </summary>
    public static async Task<List<string>> ListAsync()
    {
        var rows = await InProcessApi.Instance.TryCallAsync<List<object>>("get_account_rows", new List<object>())
                   ?? new List<object>();
        var list = new List<(string Name, bool Active)>();
        foreach (var r in rows)
        {
            var d = Py.D(r);
            string n = Py.S(d.Get("name"));
            if (!IsSentinel(n) && !list.Any(x => x.Name == n))
                list.Add((n, Py.Truthy(d.Get("active"))));
        }
        return list.OrderByDescending(x => x.Active).Select(x => x.Name).ToList();
    }

    /// <summary>
    /// 确保有一个可用账号:选中的还在就用它 → 否则用列表里第一个 → 都没有就弹对话框建一个。
    /// 返回 null 表示用户取消了创建(调用方这次不要启动)。
    /// </summary>
    public static async Task<string?> EnsureAsync(string? preferred = null)
    {
        var list = await ListAsync();
        string want = (preferred ?? "").Trim();
        if (!IsSentinel(want) && list.Contains(want))
            return want;
        if (list.Count > 0)
            return list[0];
        return await CreateOfflineAsync();
    }

    /// <summary>
    /// 「创建离线用户」对话框(forge 成可注入的一步:测试不该真弹窗)。
    /// 入参 = 预填名字(取自 `last_user`,哨兵或 "Player" 视作空,照参考版);
    /// 返回 null = 用户取消,否则给出 (名字, 皮肤键)。
    /// </summary>
    internal static Func<string, Task<(string Name, string Skin)?>> Prompt { get; set; } = DefaultPrompt;

    private static async Task<(string, string)?> DefaultPrompt(string typed)
    {
        while (true)
        {
            var name = UiClass.Input("例如 Steve", typed);
            var skin = UiClass.Combo(new[] { "默认", "Steve", "Alex" });
            var body = UiClass.V(8,
                UiClass.Muted("启动游戏前需要先创建一个离线用户(无正版验证)。"),
                name,
                UiClass.Field("默认皮肤", skin, null, 78));
            if (!await Dlg.Ask("创建离线用户", body, "创建并继续"))
                return null;
            string got = name.Text?.Trim() ?? "";
            if (ValidName(got))
            {
                string picked = skin.SelectedItem as string ?? "默认";
                return (got, picked == "Steve" ? "steve" : picked == "Alex" ? "alex" : "default");
            }
            // 名字不合法就再问一次(照参考版:提示后重开同一个对话框,不吞掉用户输入)
            ToastHost.Show("用户名不可用", "1–16 个字符,不能含空格", ToastKind.Warning);
            typed = got;
        }
    }

    /// <summary>建一个离线账号(名字已校验);返回落库后的名字,用户取消返回 null。</summary>
    public static async Task<string?> CreateOfflineAsync()
    {
        string typed = Py.S(Config.CONFIG.Get("last_user", "")).Trim();
        if (IsSentinel(typed) || typed == "Player")
            typed = "";

        var picked = await Prompt(typed);
        if (picked == null)
            return null;

        string name = picked.Value.Name;
        await InProcessApi.Instance.CallAsync<object>("add_offline_account",
            new PyDict { ["username"] = name, ["skin"] = picked.Value.Skin });
        ToastHost.Show("已创建离线用户", name, ToastKind.Success);
        return name;
    }
}
