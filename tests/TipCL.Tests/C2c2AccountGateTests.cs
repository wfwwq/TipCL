using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c2:账号守卫(<see cref="AccountGate"/>)。用例体全在 <see cref="AvaloniaSession.Run(Action)"/> 里跑
/// (D-79):"要不要先建账号"这条判断本身可以纯算,但 <c>CreateOfflineAsync</c> 会弹对话框、
/// 读外壳偏好,所以要真外壳 + 程序化回答对话框。
///
/// 后端调用走本包留的两个注入缝(<see cref="AccountGate.RowSource"/> /
/// <see cref="AccountGate.AccountCreator"/>)—— 红线不许在测试里造 <c>BackendApi</c> / <c>BridgeServer</c>。
///
/// 对话框怎么"答":**先把对话框推出来,再点按钮**。不能用离屏的 <c>SmokeAutoAnswer</c> 去点确定 ——
/// 那个回调是在 <c>Dlg.Push</c> 里被调的,而 <c>Dlg.Ask</c> 的 <c>button.Click += …</c> 在 <c>Push</c>
/// 返回之后才装上,所以在那一刻点确定,TrySetResult 根本还没接上(本包实测踩到,见 PROGRESS 的 C2c2 小节)。
/// </summary>
[Collection("C2c2Globals")]
public sealed class C2c2AccountGateTests
{
    private const string OkText = "创建并继续";

    private static AccountRow Row(string name, string type = "offline", bool active = false)
    {
        return new AccountRow { Name = name, Type = type, Active = active };
    }

    private static void Rows(params AccountRow[] rows)
    {
        List<AccountRow> list = rows.ToList();
        AccountGate.RowSource = () => Task.FromResult(list);
    }

    /// <summary>压住离屏自动回答,由用例自己点(见类注释)。</summary>
    private static void ManualDialogs()
    {
        Dlg.SmokeAutoAnswer = delegate(Control _, Action _) { };
    }

    // ---------------------------------------------------------------- 哨兵名(is_sentinel)

    /// <summary>【Windows 源】<c>AccountGate.cs:12-20</c>:空 / 空白 / 「离线模式」都算"还没登录"。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Is_sentinel_treats_blank_names_as_signed_out(string? name)
    {
        Assert.True(AccountGate.IsSentinel(name));
    }

    /// <summary>【Windows 源】「离线模式」是哨兵名(中英两种写法都算)。</summary>
    [Fact]
    public void Is_sentinel_recognises_the_offline_marker()
    {
        Assert.True(AccountGate.IsSentinel("离线模式"));
        Assert.True(AccountGate.IsSentinel(" 离线模式 "));
        Assert.True(AccountGate.IsSentinel(I18n.L("离线模式")));
    }

    /// <summary>【Windows 源】真名字不是哨兵(含 <c>Player</c> —— 只有 <c>CreateOfflineAsync</c> 的预填值
    /// 会清掉它,<c>IsSentinel</c> 不认它)。</summary>
    [Fact]
    public void Is_sentinel_accepts_real_names()
    {
        Assert.False(AccountGate.IsSentinel("Steve"));
        Assert.False(AccountGate.IsSentinel("Alex"));
        Assert.False(AccountGate.IsSentinel("Player"));
    }

    // ---------------------------------------------------------------- 名字合法性

    /// <summary>【Windows 源】<c>AccountGate.cs:92-99</c>:1-16 个字符、不能含空格、不能是哨兵。</summary>
    [Fact]
    public void Valid_name_matches_windows_rules()
    {
        Assert.False(AccountGate.ValidName(""));
        Assert.True(AccountGate.ValidName("a"));
        Assert.True(AccountGate.ValidName("Steve"));
        Assert.True(AccountGate.ValidName(new string('a', C2c2Expected.NameMaxLength)));
        Assert.False(AccountGate.ValidName(new string('a', C2c2Expected.NameMaxLength + 1)));
        Assert.False(AccountGate.ValidName("a b"));
        Assert.False(AccountGate.ValidName(" Steve"));
        Assert.False(AccountGate.ValidName("Steve "));
        Assert.False(AccountGate.ValidName("Steve\t"));
        Assert.False(AccountGate.ValidName("离线模式"));
        Assert.True(AccountGate.ValidName("Player"));
    }

    [Fact]
    public void Name_length_bounds_match_windows()
    {
        Assert.Equal(1, C2c2Expected.NameMinLength);
        Assert.Equal(16, C2c2Expected.NameMaxLength);
    }

    // ---------------------------------------------------------------- 清单

    /// <summary>【Windows 源】<c>ListAsync</c>:滤掉哨兵名、激活的在前、去重。</summary>
    [Fact]
    public void List_filters_sentinels_orders_active_first_and_distincts()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            Rows(
                Row("离线模式"),
                Row("Steve", active: true),
                Row("Alex"),
                Row("Alex"),
                Row("  "));

            List<string> names = AccountGate.ListAsync().GetAwaiter().GetResult();

            Assert.Equal(new[] { "Steve", "Alex" }, names);
        });
    }

    /// <summary>【Windows 源】没有任何行时给空表(上游据此进"要建账号"那一支)。</summary>
    [Fact]
    public void List_is_empty_when_nothing_is_signed_in()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            Rows();

            Assert.Empty(AccountGate.ListAsync().GetAwaiter().GetResult());
        });
    }

    // ---------------------------------------------------------------- EnsureAsync

    /// <summary>【Windows 源】给的偏好在列表里 → 用它(去空白)。</summary>
    [Fact]
    public void Ensure_prefers_the_given_name_when_it_exists()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            Rows(Row("Steve", active: true), Row("Alex"));

            Assert.Equal("Alex", AccountGate.EnsureAsync(" Alex ").GetAwaiter().GetResult());
        });
    }

    /// <summary>【Windows 源】偏好在列表里找不到 → 用列表第一个(激活的优先)。</summary>
    [Fact]
    public void Ensure_falls_back_to_the_first_available_name()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            Rows(Row("Steve"), Row("Alex", active: true));

            Assert.Equal("Alex", AccountGate.EnsureAsync("nobody").GetAwaiter().GetResult());
            Assert.Equal("Alex", AccountGate.EnsureAsync().GetAwaiter().GetResult());
            Assert.Equal("Alex", AccountGate.EnsureAsync("离线模式").GetAwaiter().GetResult());
        });
    }

    // ---------------------------------------------------------------- 没登录那一支(对话框)

    /// <summary>【任务书 + Windows 源】一个可用账号都没有 → 弹「创建离线用户」→ 建好 → 返回它的名字。</summary>
    [Fact]
    public void Ensure_creates_an_offline_account_when_signed_out()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Steve" }, smoke: true);
            Rows(Row("离线模式"), Row("  "));
            ManualDialogs();
            List<(string Name, string Skin)> created = new List<(string, string)>();
            AccountGate.AccountCreator = delegate(string name, string skin)
            {
                created.Add((name, skin));
                return Task.FromResult(name);
            };

            Task<string?> task = AccountGate.EnsureAsync();
            DialogCard card = C2c2Files.Dialog();

            Assert.Equal(I18n.L("创建离线用户"), card.Title);
            Assert.Equal("Steve", C2c2Files.Input(card).Text);

            C2c2Files.Click(card, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);

            Assert.True(task.IsCompleted, "点完确定之后整条链应当在泵里跑完");
            Assert.Equal("Steve", task.Result);
            Assert.Equal(("Steve", "default"), Assert.Single(created));
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【Windows 源】用户在对话框上点取消 → <c>null</c>(算了就是算了,不会自动建号)。</summary>
    [Fact]
    public void Ensure_returns_null_when_the_dialog_is_cancelled()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(smoke: true);
            Rows();
            ManualDialogs();
            int created = 0;
            AccountGate.AccountCreator = delegate(string name, string skin)
            {
                created++;
                return Task.FromResult(name);
            };

            Task<string?> task = AccountGate.EnsureAsync();
            DialogCard card = C2c2Files.Dialog();
            C2c2Files.Click(card, I18n.L("取消"));
            C2c2Files.Pump(() => task.IsCompleted);

            Assert.True(task.IsCompleted);
            Assert.Null(task.Result);
            Assert.Equal(0, created);
        });
    }

    /// <summary>【Windows 源】预填值来自外壳偏好的 <c>LastUser</c>;<c>Player</c> 这个默认值会被清掉,
    /// 空名字不合法 → 再问一次(第二次填个合法名字才建)。</summary>
    [Fact]
    public void Create_offline_prefills_from_prefs_and_drops_the_player_default()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Player" }, smoke: true);
            ManualDialogs();
            AccountGate.AccountCreator = (name, skin) => Task.FromResult(name);
            List<string> typed = new List<string>();

            Task<string?> task = AccountGate.CreateOfflineAsync();

            DialogCard first = C2c2Files.Dialog();
            typed.Add(C2c2Files.Input(first).Text ?? "");
            C2c2Files.Click(first, I18n.L(OkText));

            DialogCard second = C2c2Files.Dialog();
            typed.Add(C2c2Files.Input(second).Text ?? "");
            C2c2Files.Input(second).Text = "Notch";
            C2c2Files.Click(second, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);

            Assert.True(task.IsCompleted);
            Assert.Equal("Notch", task.Result);
            Assert.Equal(new[] { "", "" }, typed);
        });
    }

    /// <summary>【Windows 源】预填值不是 <c>Player</c> 时原样进输入框,点一下就成了。</summary>
    [Fact]
    public void Create_offline_keeps_a_usable_prefill()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Alex" }, smoke: true);
            ManualDialogs();
            List<string> typed = new List<string>();
            AccountGate.AccountCreator = (name, skin) => Task.FromResult(name);

            Task<string?> task = AccountGate.CreateOfflineAsync();
            DialogCard card = C2c2Files.Dialog();
            typed.Add(C2c2Files.Input(card).Text ?? "");
            C2c2Files.Click(card, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);

            Assert.Equal("Alex", task.Result);
            Assert.Equal(new[] { "Alex" }, typed);
        });
    }

    /// <summary>【Windows 源】皮肤下拉框的映射:0 → default、1 → steve、2 → alex。</summary>
    [Theory]
    [InlineData(0, "default")]
    [InlineData(1, "steve")]
    [InlineData(2, "alex")]
    public void Create_offline_maps_the_skin_index(int index, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Steve" }, smoke: true);
            ManualDialogs();
            string? skin = null;
            AccountGate.AccountCreator = delegate(string name, string chosen)
            {
                skin = chosen;
                return Task.FromResult(name);
            };

            Task<string?> task = AccountGate.CreateOfflineAsync();
            DialogCard card = C2c2Files.Dialog();
            C2c2Files.Combo(card).SelectedIndex = index;
            C2c2Files.Click(card, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);

            Assert.Equal("Steve", task.Result);
            Assert.Equal(expected, skin);
        });
    }

    /// <summary>【Windows 源】后端给了名字就用后端那个(空才回落成用户输入的)。</summary>
    [Fact]
    public void Create_offline_uses_the_backend_name_when_it_gives_one()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Steve" }, smoke: true);
            ManualDialogs();
            AccountGate.AccountCreator = (name, skin) => Task.FromResult("Steve#1234");

            Task<string?> task = AccountGate.CreateOfflineAsync();
            DialogCard card = C2c2Files.Dialog();
            C2c2Files.Click(card, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);

            Assert.Equal("Steve#1234", task.Result);
        });
    }

    /// <summary>【Windows 源】建完 Toast 一句(走外壳的 Toast 通道)。</summary>
    [Fact]
    public void Create_offline_toasts_the_created_account()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Steve" }, smoke: true);
            ManualDialogs();
            AccountGate.AccountCreator = (name, skin) => Task.FromResult(name);

            Task<string?> task = AccountGate.CreateOfflineAsync();
            DialogCard card = C2c2Files.Dialog();
            C2c2Files.Click(card, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);
            UiHost.Flush();

            ToastCard toast = shell.Window.ToastHostPanel.Children.OfType<ToastCard>().Last();

            Assert.Equal(I18n.L("已创建离线用户"), toast.Title);
            Assert.Equal("Steve", toast.Body);
            Assert.Equal(ToastKind.Success, toast.Kind);
        });
    }

    /// <summary>【Windows 源】用户名不合法时会再问一次,并给一句警告 Toast。</summary>
    [Fact]
    public void Create_offline_warns_and_asks_again_for_a_bad_name()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "bad name" }, smoke: true);
            ManualDialogs();
            AccountGate.AccountCreator = (name, skin) => Task.FromResult(name);

            Task<string?> task = AccountGate.CreateOfflineAsync();

            DialogCard first = C2c2Files.Dialog();
            Assert.Equal("bad name", C2c2Files.Input(first).Text);
            C2c2Files.Click(first, I18n.L(OkText));

            DialogCard second = C2c2Files.Dialog();
            C2c2Files.Input(second).Text = "ok";
            C2c2Files.Click(second, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);
            UiHost.Flush();

            Assert.Equal("ok", task.Result);
            ToastCard warning = shell.Window.ToastHostPanel.Children.OfType<ToastCard>().First();
            Assert.Equal(I18n.L("用户名不可用"), warning.Title);
            Assert.Equal(I18n.L("1–16 个字符,不能含空格"), warning.Body);
            Assert.Equal(ToastKind.Warning, warning.Kind);
        });
    }

    /// <summary>【任务书】"没登录"的完整链:<c>EnsureAsync</c> 在空清单下会一直走到"建一个号"为止,
    /// 而且**哨兵偏好不会被误当成已登录**。</summary>
    [Fact]
    public void Ensure_signed_out_end_to_end_creates_and_returns_the_account()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Alex" }, smoke: true);
            Rows(Row("离线模式"));
            ManualDialogs();
            AccountGate.AccountCreator = (name, skin) => Task.FromResult(name);

            Task<string?> task = AccountGate.EnsureAsync("离线模式");
            DialogCard card = C2c2Files.Dialog();
            C2c2Files.Click(card, I18n.L(OkText));
            C2c2Files.Pump(() => task.IsCompleted);

            Assert.Equal("Alex", task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【Windows 源】清单里已经有账号时<b>不该</b>弹对话框(这条防的是"多弹一个框")。</summary>
    [Fact]
    public void Ensure_with_an_existing_account_never_opens_a_dialog()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create(new UiPrefs { LastUser = "Alex" }, smoke: true);
            Rows(Row("Steve", active: true));
            ManualDialogs();
            AccountGate.AccountCreator = (name, skin) => Task.FromResult(name);

            Assert.Equal("Steve", AccountGate.EnsureAsync().GetAwaiter().GetResult());
            Assert.False(Dlg.AnyOpen);
        });
    }
}
