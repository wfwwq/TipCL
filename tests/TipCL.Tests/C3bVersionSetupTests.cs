using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3b:<see cref="VersionSetupDialog"/>(「版本设置」对话框)的移植验收。
///
/// 纪律同 <c>C3bCrashUiTests</c>:用例体走 <c>AvaloniaSession.Run</c>(D-79)、
/// "先推卡片再点按钮"(D-113)、主动泵而不是阻塞等续体(D-114)、全局静态用 <see cref="C3bScope"/> 还原(D-69)、
/// 后端走 C2a 的回环假后端(不起真后端、不出网、不弹真文件选择器)。
///
/// 控件按 <b>类型 + 顺序</b> 取:14 行字段在 <c>Ui.Field</c> 里是按源码顺序建进去的,
/// 所以 TextBox 的次序就是"独占内存 / JVM 参数 / 直连服务器 / 端口 / 启动前命令 / 退出后命令 / 窗口标题 /
/// 统一通行证",ComboBox 是"版本隔离 / 垃圾回收器 / 窗口模式 / 绑定账号",CheckBox 是"等待启动前命令结束 /
/// 在版本列表中隐藏"(见 <see cref="C3bExpected.Fields"/> 与源码行号)。
/// </summary>
public class C3bVersionSetupTests
{
    private const string Instance = "实例A";

    private const string Version = "1.20.1";

    /// <summary>三个响应的假后端(顺序 = <c>get_version_settings</c> / <c>get_accounts</c> / <c>save_version_settings</c>)。</summary>
    private static C3bBackend Backend(string settings = "{}", string accounts = "[]", string save = "{}")
    {
        return new C3bBackend(
            C3bBackend.Rpc(settings),
            C3bBackend.Rpc(accounts),
            C3bBackend.Rpc(save));
    }

    private static (Task Task, DialogCard Card) Open(C3bBackend backend)
    {
        Task task = VersionSetupDialog.ShowAsync(Instance, Version);
        // ShowAsync 要先跑两次 RPC 才推卡片,所以这里必须泵到卡片出现(D-113 的"先推出来再点")。
        return (task, C3bFiles.WaitDialog());
    }

    private static void Save(DialogCard card, Task task)
    {
        C3bFiles.Click(C3bFiles.ByText(card, I18n.L(C3bExpected.VersionOk)));
        C3bFiles.PumpTask(task);
    }

    private static JsonElement SavedPayload(C3bBackend backend)
	{
		// 【D1 修正 2026-11-14】原来这里断的是"第 3 个请求就是 save_version_settings" —— 那是一条
		// **顺序假设**,并发负载下会假红(VSTest 通道偶发、进程内通道不复现;本项目运营规则第 7 条)。
		// 改成"等效果":等 save_version_settings 真的到达(有截止时间)再取它的载荷,
		// 并把**实际收到的调用清单**带进失败信息,下次一眼能定位。
		// 载荷断言(下面调用方那一堆)一个都没改。
		C3bFiles.Pump(() => SaveRequestIndex(backend) >= 0, timeoutMs: 9000);
		int at = SaveRequestIndex(backend);
		Assert.True(at >= 0, "没等到 save_version_settings;实际收到的调用:" + MethodList(backend));
		return C3bFiles.Params(backend.RequestAt(at)).GetProperty("data");
	}

	/// <summary>最后一个 <c>save_version_settings</c> 请求的下标(没有则 -1)。</summary>
	private static int SaveRequestIndex(C3bBackend backend)
	{
		int found = -1;
		for (int i = 0; i < backend.RequestsCount; i++)
		{
			if (C3bFiles.Method(backend.RequestAt(i)) == "save_version_settings")
			{
				found = i;
			}
		}
		return found;
	}

	/// <summary>把假后端收到的方法名拼成一行,只用于失败信息。</summary>
	private static string MethodList(C3bBackend backend)
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder("[");
		for (int i = 0; i < backend.RequestsCount; i++)
		{
			if (i > 0)
			{
				sb.Append(", ");
			}
			sb.Append(C3bFiles.Method(backend.RequestAt(i)));
		}
		return sb.Append(']').ToString();
	}

    // ---------------------------------------------------------------- 卡片与控件形状

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:61</c>:标题是「版本设置 · 」+ 版本,确定按钮「保存」。</summary>
    [Fact]
    public void Title_is_the_prefix_plus_version_and_ok_is_save()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            Assert.Equal(I18n.L(C3bExpected.VersionTitlePrefix) + Version, card.Title);
            Assert.True(C3bFiles.HasButton(card, I18n.L(C3bExpected.VersionOk)));
            // Dlg.Ask 的取消按钮是「取消」(C2a 的默认文案)。
            Assert.True(C3bFiles.HasButton(card, I18n.L("取消")));
            Assert.Equal(C3bExpected.VersionScrollMinWidth, Assert.IsAssignableFrom<SmoothScroll>(card.BodyControl).MinWidth);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:58</c>:14 行的标签与顺序,以及每类控件的个数。</summary>
    [Fact]
    public void Fourteen_rows_are_built_in_the_windows_order()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            List<string> expected = C3bExpected.Fields.Select(I18n.L).Where(s => s.Length > 0).ToList();
            List<string> actual = C3bFiles.Texts(card).Select(t => t.Text ?? "")
                .Where(s => expected.Contains(s, StringComparer.Ordinal))
                .ToList();
            Assert.Equal(expected, actual);

            Assert.Equal(8, C3bFiles.Inputs(card).Count);
            Assert.Equal(4, C3bFiles.Combos(card).Count);
            Assert.Equal(2, C3bFiles.Checks(card).Count);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:40-56</c>:每个输入框的占位文案
    /// (Avalonia 侧放在 <c>Tag</c> 里,由 C1b 的样式接住成 watermark;WPF 是 <c>TextBox</c> 的水印)。</summary>
    [Fact]
    public void Placeholders_match_the_windows_source()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            List<TextBox> inputs = C3bFiles.Inputs(card);
            Assert.Equal(I18n.L("留空则用全局设置"), inputs[0].Tag);
            Assert.Equal("-XX:+UseG1GC ...", inputs[1].Tag);
            Assert.Equal(I18n.L("进游戏直连的服务器"), inputs[2].Tag);
            Assert.Equal(C3bExpected.PortPlaceholder, inputs[3].Tag);
            Assert.Equal(I18n.L("启动前执行的命令"), inputs[4].Tag);
            Assert.Equal(I18n.L("退出后执行的命令"), inputs[5].Tag);
            Assert.Equal(I18n.L("自定义游戏窗口标题"), inputs[6].Tag);
            Assert.Equal(I18n.L("统一通行证 ServerId(可空)"), inputs[7].Tag);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源 + 本机实测】<c>VersionSetupDialog.cs:39-57</c>:后端给空对象时用的是
    /// <c>VersionSettingsDto</c> **自己的属性初值** —— 注意 <c>Isolation</c> 的初值是 <c>"none"</c>
    /// (<c>Models\VersionSettingsDto.cs:7</c>),它正好是 <c>IsoValues</c> 的第 2 项,所以版本隔离落在
    /// 「关闭(共用实例目录)」而**不是**第 0 项(第 0 项只留给"值不在表里"的情况,见下一条用例);
    /// 垃圾回收器的初值是空串 → 走 <c>"auto"</c> → 第 0 项;窗口模式初值 <c>"window"</c> → 第 0 项;
    /// 绑定账号初值空 → 第 0 项「(不绑定)」;两个开关取 DTO 的默认值
    /// (<c>PreLaunchWait</c> 默认 <c>true</c>、<c>Hidden</c> 默认 <c>false</c>)。</summary>
    [Fact]
    public void Empty_dto_uses_the_dto_defaults()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            List<ComboBox> combos = C3bFiles.Combos(card);
            Assert.Equal(1, combos[0].SelectedIndex);
            Assert.Equal(I18n.L(C3bExpected.IsoLabels[1]), combos[0].Str());
            Assert.Equal(0, combos[1].SelectedIndex);
            Assert.Equal(I18n.L(C3bExpected.GcLabels[0]), combos[1].Str());
            Assert.Equal(0, combos[2].SelectedIndex);
            Assert.Equal(I18n.L(C3bExpected.WinModes[0]), combos[2].Str());
            Assert.Equal(new[] { I18n.L(C3bExpected.NoAccount) }, combos[3].Items.Cast<string>().ToArray());

            Assert.All(C3bFiles.Inputs(card), box => Assert.Equal("", box.Text));
            List<CheckBox> checks = C3bFiles.Checks(card);
            Assert.True(checks[0].IsChecked);
            Assert.False(checks[1].IsChecked);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:37-38</c>:账号列表前面**插入**「(不绑定)」,
    /// 且顺序原样保留。</summary>
    [Fact]
    public void Account_list_gets_the_no_binding_entry_first()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend(accounts: "[\"Steve\",\"Alex\"]");
            (Task task, DialogCard card) = Open(backend);

            ComboBox acc = C3bFiles.Combos(card)[3];
            Assert.Equal(
                new[] { I18n.L(C3bExpected.NoAccount), "Steve", "Alex" },
                acc.Items.Cast<string>().ToArray());

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:39-55</c>:DTO 里有值就选中对应文案
    /// (版本隔离 / 垃圾回收器 / 窗口模式 / 绑定账号)。</summary>
    [Fact]
    public void Dto_values_select_the_matching_items()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend(
                settings: "{\"isolation\":\"saves\",\"gc\":\"zgc\",\"window_mode\":\"fullscreen\",\"login_account\":\"Steve\",\"memory_mb\":8192,\"port\":\"25570\"}",
                accounts: "[\"Steve\",\"Alex\"]");
            (Task task, DialogCard card) = Open(backend);

            List<ComboBox> combos = C3bFiles.Combos(card);
            Assert.Equal(2, combos[0].SelectedIndex);
            Assert.Equal(I18n.L(C3bExpected.IsoLabels[2]), combos[0].Str());
            Assert.Equal(2, combos[1].SelectedIndex);
            Assert.Equal(I18n.L(C3bExpected.GcLabels[2]), combos[1].Str());
            Assert.Equal(1, combos[2].SelectedIndex);
            Assert.Equal(I18n.L(C3bExpected.WinModes[1]), combos[2].Str());
            Assert.Equal(1, combos[3].SelectedIndex);
            Assert.Equal("Steve", combos[3].Str());

            List<TextBox> inputs = C3bFiles.Inputs(card);
            Assert.Equal("8192", inputs[0].Text);
            Assert.Equal("25570", inputs[3].Text);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源 + 本机实测】<c>Math.Max(0, Array.IndexOf(...))</c>:候选表里没有的值
    /// **退化成第 0 项**,不是 -1、也不抛(空白字符串同样退化)。</summary>
    [Theory]
    [InlineData("{\"isolation\":\"bogus\",\"gc\":\"bogus\",\"window_mode\":\"bogus\",\"login_account\":\"nobody\"}")]
    [InlineData("{\"isolation\":\"\",\"gc\":\"\",\"window_mode\":\"\",\"login_account\":\"\"}")]
    public void Unknown_values_fall_back_to_the_first_option(string settings)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend(settings: settings, accounts: "[\"Steve\"]");
            (Task task, DialogCard card) = Open(backend);

            List<ComboBox> combos = C3bFiles.Combos(card);
            Assert.All(combos, combo => Assert.Equal(0, combo.SelectedIndex));

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【本机实测 / D-118】<c>BindWindowMaxHeight(0.55, 260)</c> 在离屏(没有
    /// <c>ApplicationLifetime</c>)时不绑定:不抛异常、<c>MaxHeight</c> 保持 NaN
    /// —— 这是 C1c/C2b 已经定下的口径,这里只钉住它,不新增行为。</summary>
    [Fact]
    public void Max_height_is_left_unbound_without_a_desktop_lifetime()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            SmoothScroll scroll = Assert.IsAssignableFrom<SmoothScroll>(card.BodyControl);
            // Avalonia 的 Layoutable.MaxHeight 默认是 PositiveInfinity(不是 NaN);没宿主就不绑,
            // 所以这里保持默认值 —— 这正是"没有 ApplicationLifetime 时 BindWindowMaxHeight 直接返回"的表现。
            Assert.True(double.IsPositiveInfinity(scroll.MaxHeight));

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    // ---------------------------------------------------------------- 取参数

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:36-37</c>:两次读取的方法名与参数形状。</summary>
    [Fact]
    public void Reads_settings_and_accounts_with_the_windows_arguments()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);
            backend.Need(2);

            Assert.Equal("get_version_settings", C3bFiles.Method(backend.RequestAt(0)));
            JsonElement settingsArgs = C3bFiles.Params(backend.RequestAt(0));
            Assert.Equal(Instance, settingsArgs.GetProperty("instance").GetString());
            Assert.Equal(Version, settingsArgs.GetProperty("version").GetString());

            Assert.Equal("get_accounts", C3bFiles.Method(backend.RequestAt(1)));
            // 【Windows 源】get_accounts 的参数是 null(BridgeClient 会把它写成空对象)。
            Assert.Equal(JsonValueKind.Object, C3bFiles.Params(backend.RequestAt(1)).ValueKind);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【Windows 源 + 本机实测】<c>TryCallAsync</c> 的兜底:两次读取返回 error 时
    /// 不抛、按 <c>VersionSettingsDto</c> 的初值建卡片(版本隔离 = "none" → 第 2 项;
    /// gc 空 → "auto" → 第 0 项;账号表只有「(不绑定)」)。</summary>
    [Fact]
    public void Read_failures_degrade_to_defaults_instead_of_throwing()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = new C3bBackend(
                C3bBackend.RpcError("后端没起来"),
                C3bBackend.RpcError("后端没起来"),
                C3bBackend.Rpc("{}"));
            (Task task, DialogCard card) = Open(backend);
            backend.Need(2);

            Assert.Equal(I18n.L(C3bExpected.IsoLabels[1]), C3bFiles.Combos(card)[0].Str());
            Assert.Equal(I18n.L(C3bExpected.GcLabels[0]), C3bFiles.Combos(card)[1].Str());
            Assert.Equal(
                new[] { I18n.L(C3bExpected.NoAccount) },
                C3bFiles.Combos(card)[3].Items.Cast<string>().ToArray());
            Assert.True(C3bFiles.Checks(card)[0].IsChecked);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);
        });
    }

    /// <summary>【任务书】取消:一个 save 请求都不发、一个 Toast 都不弹(但两次读取照发)。</summary>
    [Fact]
    public void Cancel_sends_no_save_request_and_no_toast()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);
            backend.Need(2);

            C3bFiles.Click(C3bFiles.ByText(card, I18n.L("取消")));
            C3bFiles.PumpTask(task);

            Assert.Empty(toasts.Items);
            Assert.Equal(2, backend.RequestsCount);
            // 多打一次 save 请求就会在这 250ms 里被记到;没记到才算过。
            Assert.Throws<TimeoutException>(() => backend.MustNotReach(3));
            Assert.Empty(env.Host.Children);
        });
    }

    // ---------------------------------------------------------------- 保存

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:65-101</c>:保存写回的那份 payload ——
    /// 14 个键一个不多一个不少,取值逐项对照(含"改了控件才写、没改就写原值")。</summary>
    [Fact]
    public void Save_posts_the_fourteen_key_payload()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = Backend(accounts: "[\"Steve\",\"Alex\"]");
            (Task task, DialogCard card) = Open(backend);

            List<TextBox> inputs = C3bFiles.Inputs(card);
            List<ComboBox> combos = C3bFiles.Combos(card);
            List<CheckBox> checks = C3bFiles.Checks(card);
            combos[0].SelectedIndex = 2;
            inputs[0].Text = "4096";
            inputs[1].Text = "-XX:+UseZGC";
            combos[1].SelectedIndex = 2;
            inputs[2].Text = "mc.example.com";
            inputs[3].Text = "25566";
            inputs[4].Text = "echo hi";
            checks[0].IsChecked = false;
            inputs[5].Text = "echo bye";
            inputs[6].Text = "我的世界";
            combos[2].SelectedIndex = 2;
            combos[3].SelectedIndex = 2;
            inputs[7].Text = "nid8";
            checks[1].IsChecked = true;

            Save(card, task);
            JsonElement parameters = C3bFiles.Params(backend.RequestAt(2));
            JsonElement data = SavedPayload(backend);

            Assert.Equal(Instance, parameters.GetProperty("instance").GetString());
            Assert.Equal(Version, parameters.GetProperty("version").GetString());
            Assert.Equal(
                C3bExpected.PayloadKeys.OrderBy(k => k, StringComparer.Ordinal),
                data.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal));

            Assert.Equal("saves", data.GetProperty("isolation").GetString());
            Assert.Equal("-XX:+UseZGC", data.GetProperty("jvm_args").GetString());
            Assert.Equal("zgc", data.GetProperty("gc").GetString());
            Assert.Equal("mc.example.com", data.GetProperty("server").GetString());
            Assert.Equal("25566", data.GetProperty("port").GetString());
            Assert.Equal("echo hi", data.GetProperty("pre_launch").GetString());
            Assert.False(data.GetProperty("pre_launch_wait").GetBoolean());
            Assert.Equal("echo bye", data.GetProperty("post_launch").GetString());
            Assert.Equal("我的世界", data.GetProperty("window_title").GetString());
            Assert.Equal("maximized", data.GetProperty("window_mode").GetString());
            Assert.Equal("Alex", data.GetProperty("login_account").GetString());
            Assert.Equal("nid8", data.GetProperty("nide8_id").GetString());
            Assert.True(data.GetProperty("hidden").GetBoolean());
            Assert.Equal(4096, data.GetProperty("memory_mb").GetInt32());

            (string Title, string Body, ToastKind Kind) toast = Assert.Single(toasts.Items);
            Assert.Equal(I18n.L("已保存"), toast.Title);
            Assert.Equal(I18n.L("版本设置已写入"), toast.Body);
            Assert.Equal(ToastKind.Success, toast.Kind);
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:86-93</c>:内存只在"能解析成正整数"时写数字,
    /// 其余一律 <c>null</c>(= 用全局内存)。</summary>
    [Theory]
    [InlineData("", "null")]
    [InlineData("abc", "null")]
    [InlineData("0", "null")]
    [InlineData("-3", "null")]
    [InlineData("  ", "null")]
    [InlineData("4096", "4096")]
    [InlineData(" 12 ", "12")]
    public void Memory_is_written_only_for_positive_numbers(string typed, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            C3bFiles.Inputs(card)[0].Text = typed;
            Save(card, task);
            JsonElement memory = SavedPayload(backend).GetProperty("memory_mb");

            if (expected == "null")
            {
                Assert.Equal(JsonValueKind.Null, memory.ValueKind);
            }
            else
            {
                Assert.Equal(int.Parse(expected), memory.GetInt32());
            }
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:76-81</c>:窗口模式下标 → 取值映射。</summary>
    [Theory]
    [InlineData(0, "window")]
    [InlineData(1, "fullscreen")]
    [InlineData(2, "maximized")]
    public void Window_mode_maps_the_three_values(int index, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            C3bFiles.Combos(card)[2].SelectedIndex = index;
            Save(card, task);

            Assert.Equal(expected, SavedPayload(backend).GetProperty("window_mode").GetString());
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:66</c>:版本隔离的 4 个取值往返
    /// (选中第 N 项 → 写 <c>IsoValues[N]</c>)。</summary>
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "none")]
    [InlineData(2, "saves")]
    [InlineData(3, "all")]
    public void Isolation_round_trips_every_value(int index, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            C3bFiles.Combos(card)[0].SelectedIndex = index;
            Save(card, task);

            Assert.Equal(expected, SavedPayload(backend).GetProperty("isolation").GetString());
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:68</c>:垃圾回收器的 5 个取值往返。</summary>
    [Theory]
    [InlineData(0, "auto")]
    [InlineData(1, "g1")]
    [InlineData(2, "zgc")]
    [InlineData(3, "serial")]
    [InlineData(4, "none")]
    public void Gc_round_trips_every_value(int index, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            C3bFiles.Combos(card)[1].SelectedIndex = index;
            Save(card, task);

            Assert.Equal(expected, SavedPayload(backend).GetProperty("gc").GetString());
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:82</c>:绑定账号下标 0 = 空串(不绑定),
    /// 其余取下拉框里那一项的文字。</summary>
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Steve")]
    [InlineData(2, "Alex")]
    public void Login_account_maps_index_zero_to_empty(int index, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend(accounts: "[\"Steve\",\"Alex\"]");
            (Task task, DialogCard card) = Open(backend);

            C3bFiles.Combos(card)[3].SelectedIndex = index;
            Save(card, task);

            Assert.Equal(expected, SavedPayload(backend).GetProperty("login_account").GetString());
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:72 / 84</c>:两个复选框 → 两个布尔。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Boolean_flags_round_trip(bool wait, bool hidden)
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            List<CheckBox> checks = C3bFiles.Checks(card);
            checks[0].IsChecked = wait;
            checks[1].IsChecked = hidden;
            Save(card, task);

            JsonElement data = SavedPayload(backend);
            Assert.Equal(wait, data.GetProperty("pre_launch_wait").GetBoolean());
            Assert.Equal(hidden, data.GetProperty("hidden").GetBoolean());
        });
    }

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:96-107</c>:后端报错时弹 Error 的「保存失败」
    /// (消息原样带出来),不弹「已保存」。</summary>
    [Fact]
    public void Save_failure_toasts_the_error_message()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3aToasts toasts = new C3aToasts();
            using C3bBackend backend = new C3bBackend(
                C3bBackend.Rpc("{}"),
                C3bBackend.Rpc("[]"),
                C3bBackend.RpcError("磁盘满了"));
            (Task task, DialogCard card) = Open(backend);

            Save(card, task);
            backend.Need(3);

            (string Title, string Body, ToastKind Kind) toast = Assert.Single(toasts.Items);
            Assert.Equal(I18n.L("保存失败"), toast.Title);
            Assert.Equal("磁盘满了", toast.Body);
            Assert.Equal(ToastKind.Error, toast.Kind);
        });
    }

    /// <summary>【任务书 + 本机实测】保存之后对话框干净收摊:宿主空、没有残留件;
    /// 再泵 20 轮也不冒新东西(没有计时器 / 订阅漏下)。</summary>
    [Fact]
    public void Dialog_closes_cleanly_after_saving()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            Motion.Enabled = false;
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            Save(card, task);
            Assert.True(task.IsCompletedSuccessfully);
            Assert.False(Dlg.AnyOpen);
            Assert.Empty(env.Host.Children);
            Assert.False(env.Host.IsVisible);
            C3bFiles.Flush(20);
            Assert.Empty(env.Host.Children);
            Assert.Equal(0, Smoke.SmokeState.PendingOperations);
        });
    }

    /// <summary>【任务书】连点两次「保存」是安全的:卡片已经摘掉,第二次点击只让
    /// <c>TrySetResult</c> / <c>Close</c> 空转,不多打一次 save 请求。</summary>
    [Fact]
    public void Clicking_save_twice_is_safe_and_sends_one_request()
    {
        AvaloniaSession.Run(() =>
        {
            using C3bScope scope = C3bScope.Capture();
            using DlgHarness env = DlgHarness.Show();
            using C3bBackend backend = Backend();
            (Task task, DialogCard card) = Open(backend);

            Button ok = C3bFiles.ByText(card, I18n.L(C3bExpected.VersionOk));
            C3bFiles.Click(ok);
            C3bFiles.PumpTask(task);
            C3bFiles.Click(ok);
            C3bFiles.Click(ok);
            C3bFiles.Flush(5);

            backend.Need(3);
            Assert.Equal(3, backend.RequestsCount);
            Assert.Throws<TimeoutException>(() => backend.MustNotReach(4));
            Assert.Empty(env.Host.Children);
        });
    }
}
