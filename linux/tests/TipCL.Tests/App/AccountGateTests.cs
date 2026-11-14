// M4-H R21i:启动前账号闸门(对齐 Windows Services/AccountGate.cs)的回归。
//
// 为什么会有这个文件:在 Linux 上真跑测试时才暴露出「参考版有闸门、Linux 侧没有」——
// 一个账号都没有时点「启动」只能拿到「启动游戏前请先创建一个离线用户」,界面上没有往下走的路。
// 这里钉四件事:
//   1 名单只列真实账号(哨兵「离线模式」/空不算),当前账号排最前;
//   2 已有账号时 EnsureAsync 直接用,不弹对话框;
//   3 用户取消对话框 = 不建账号、调用方拿到 null(这次不启动);
//   4 名字校验口径与参考版 ValidName 一致(1–16 字符、不含空白)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests.App;

public class AccountGateTests
{
    private readonly IApi _api = InProcessApi.Instance;

    private static string NewName() => "ag-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>注入一次对话框结果,并在测试结束时还原(静态状态不能漏给别的用例)。</summary>
    private static async Task<T> WithPrompt<T>(Func<string, Task<(string Name, string Skin)?>> prompt, Func<Task<T>> body)
    {
        var old = AccountGate.Prompt;
        AccountGate.Prompt = prompt;
        try
        {
            return await body();
        }
        finally
        {
            AccountGate.Prompt = old;
        }
    }

    private async Task AddAccountAsync(string name)
        => await _api.CallAsync<object>("add_offline_account", new PyDict { ["username"] = name, ["skin"] = "default" });

    private async Task RemoveAccountAsync(string name)
        => await _api.CallAsync<object>("remove_account", new PyDict { ["name"] = name });

    // ───────────────── 名字口径 ─────────────────

    [Fact]
    public void ValidName_MatchesTheReferenceRules() => TestApp.Run(() =>
    {
        Assert.True(AccountGate.ValidName("Steve"));
        Assert.True(AccountGate.ValidName("玩家1234"));
        Assert.True(AccountGate.ValidName(new string('a', 16)));      // 16 = 上限

        Assert.False(AccountGate.ValidName(""));
        Assert.False(AccountGate.ValidName("   "));
        Assert.False(AccountGate.ValidName(new string('a', 17)));     // 17 = 超一个字符
        Assert.False(AccountGate.ValidName("a b"));                   // 不能含空白
        Assert.False(AccountGate.ValidName("离线模式"));                // 哨兵不算名字

        Assert.True(AccountGate.IsSentinel(""));
        Assert.True(AccountGate.IsSentinel("离线模式"));
        Assert.False(AccountGate.IsSentinel("Steve"));
    });

    // ───────────────── 名单 ─────────────────

    /// <summary>名单里只有真实账号,且当前账号排最前(参考版是 orderby Active descending)。</summary>
    [Fact]
    public void ListAsync_ExcludesSentinel_AndPutsActiveFirst() => TestApp.RunAsync(async () =>
    {
        string a = NewName(), b = NewName();
        await AddAccountAsync(a);
        await AddAccountAsync(b);
        try
        {
            await _api.CallAsync<object>("set_active_account", new PyDict { ["name"] = b });

            var list = await AccountGate.ListAsync();

            Assert.Contains(a, list);
            Assert.Contains(b, list);
            Assert.DoesNotContain("", list);
            Assert.DoesNotContain("离线模式", list);
            Assert.Equal(b, list[0]);                                  // 当前账号排最前
            Assert.Equal(list.Distinct().Count(), list.Count);         // 不重复
        }
        finally
        {
            await RemoveAccountAsync(a);
            await RemoveAccountAsync(b);
        }
    });

    // ───────────────── 闸门 ─────────────────

    /// <summary>已经有账号(且选中的就是它)时:直接用,不弹对话框。</summary>
    [Fact]
    public void EnsureAsync_UsesTheSelectedAccount_WithoutPrompting() => TestApp.RunAsync(async () =>
    {
        string a = NewName();
        await AddAccountAsync(a);
        try
        {
            bool prompted = false;
            string? got = await WithPrompt(_ => { prompted = true; return Task.FromResult<(string, string)?>(null); },
                () => AccountGate.EnsureAsync(a));

            Assert.Equal(a, got);
            Assert.False(prompted);                                    // 不该打扰用户
        }
        finally
        {
            await RemoveAccountAsync(a);
        }
    });

    /// <summary>没有账号(或选中的已经不在)时走创建:注入的对话框返回什么,就建成什么。</summary>
    [Fact]
    public void CreateOffline_TakesNameAndSkinFromTheDialog() => TestApp.RunAsync(async () =>
    {
        string name = NewName();
        string seenTyped = "(not called)";
        bool created = false;

        string? got = await WithPrompt(typed =>
        {
            created = true;
            seenTyped = typed;
            return Task.FromResult<(string, string)?>((name, "steve"));
        }, () => AccountGate.CreateOfflineAsync());

        try
        {
            Assert.True(created, "闸门没弹对话框");
            Assert.Equal(name, got);
            Assert.Contains(name, await AccountGate.ListAsync());       // 真落库了

            var rows = await _api.CallAsync<List<object>>("get_account_rows") ?? new List<object>();
            var mine = rows.Find(r => Py.S(Py.D(r).Get("name")) == name);
            Assert.NotNull(mine);
            Assert.Equal("offline", Py.S(Py.D(mine!).Get("type")));
        }
        finally
        {
            if (got != null) await RemoveAccountAsync(got);
        }
    });

    /// <summary>用户在对话框里点了取消:不建账号,调用方拿到 null(= 这一次不启动)。</summary>
    [Fact]
    public void CreateOffline_UserCancels_ReturnsNullAndCreatesNothing() => TestApp.RunAsync(async () =>
    {
        int before = (await _api.CallAsync<List<object>>("get_account_rows") ?? new List<object>()).Count;

        string? got = await WithPrompt(_ => Task.FromResult<(string, string)?>(null), () => AccountGate.CreateOfflineAsync());

        Assert.Null(got);
        Assert.Equal(before, (await _api.CallAsync<List<object>>("get_account_rows") ?? new List<object>()).Count);
    });

    /// <summary>
    /// 选中的账号已经不在(比如在「账号」页删掉了)时,EnsureAsync 不应把那个名字原样返回 ——
    /// 否则启动链会拿它去查账号并报「账号不存在: X」。
    /// </summary>
    [Fact]
    public void EnsureAsync_StaleSelection_FallsBackToARealAccount() => TestApp.RunAsync(async () =>
    {
        string live = NewName();
        await AddAccountAsync(live);
        try
        {
            string? got = await AccountGate.EnsureAsync("这个账号早就删了");
            Assert.NotNull(got);
            Assert.NotEqual("这个账号早就删了", got);                  // 不能把失效名字原样返回
            Assert.Contains(got!, await AccountGate.ListAsync());      // 必须是真实账号
            // 不写 Assert.Equal(live, got):列表按「当前账号优先」排,别的用例可能也建了账号
            // (xUnit 默认按类并行),所以「第一个」不保证是 live —— 只断言它是个真账号。
        }
        finally
        {
            await RemoveAccountAsync(live);
        }
    });
}
