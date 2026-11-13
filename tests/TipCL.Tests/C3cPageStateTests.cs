using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3c(二):启动页的状态机与交互。
///
/// 纪律(与 C3b 同一套):
/// <list type="bullet">
/// <item>碰 Avalonia 的用例体全走 <c>AvaloniaSession.Run</c>(D-79);</item>
/// <item>后端走**按方法名路由**的回环假后端 <see cref="C3cFakeApi"/>(<c>TcpListener</c> 绑 127.0.0.1:0;
///   不起真 <c>BackendApi</c> / <c>BridgeServer</c>、不出网、不启外部进程);</item>
/// <item>等续体一律"一边泵 Dispatcher 一边看条件"(<see cref="C3cFiles.Pump"/>),**不阻塞等**(D-114/D-120);</item>
/// <item>对话框走 <see cref="DlgHarness"/> + <c>C2c2Files.Dialog()</c>("先把卡片推出来、再点按钮",D-113);</item>
/// <item>驱动页面走它**自己的入口**:按钮走 <c>RaiseEvent</c>,非按钮控件那一层走页面为此留的 internal 处理器体
///   (不构造 Pointer 事件、不用 Headless 的输入模拟 —— 同 D-115)。</item>
/// </list>
/// 每条断言注明来源:【Windows 源】/【任务书】/【本机实测】。
/// </summary>
public class C3cPageStateTests
{
    private const string InstancesJson = """
        [{"name":"main","path":"C:\\mc\\main","versions":2,"mc":"1.20.1","pack":"Fabric Pack","pack_version":"1.0","mc_version":"1.20.1","java":"C:\\jdk17","java_label":"Java 17 (C:\\jdk17)"},
         {"name":"empty","path":"C:\\mc\\empty","versions":0,"mc":"","pack":"","pack_version":"","mc_version":"","java":"","java_label":""}]
        """;

    private const string AccountsJson = """[{"name":"Steve","active":true}]""";

    private const string SettingsJson = """{"default_memory_mb":2048,"default_resolution":[1280,720]}""";

    private const string VersionsJson = """["1.20.1","fabric-loader-0.20.1-1.20.1"]""";

    private const string JavaOptionsJson = """
        [{"label":"Java 17 (C:\\jdk17)","value":"C:\\jdk17"},{"label":"自动选择","value":"自动选择"}]
        """;

    private const string ModsJson = """
        [{"filename":"a.jar","name":"A","enabled":true,"version":"1","size":"1 KB"},
         {"filename":"b.jar","name":"B","enabled":false,"version":"1","size":"2 KB"}]
        """;

    private const string NewsJson = """[{"title":"公告一","body":"正文一","version":"1"}]""";

    /// <summary>【本机实测】启动页在 <c>FillCardData</c> 里会打到的全部方法(happy path 路由)。</summary>
    private static C3cFakeApi Api()
    {
        return new C3cFakeApi()
            .Route("get_instances", InstancesJson)
            .Route("get_account_rows", AccountsJson)
            .Route("get_settings", SettingsJson)
            .Route("get_installed_versions", VersionsJson)
            .Route("java_combo_options", JavaOptionsJson)
            .Route("java_combo_label_for", "\"Java 17 (C:\\\\jdk17)\"")
            .Route("get_installed_mod_entries", ModsJson)
            .Route("get_setting", parameters => parameters.TryGetProperty("key", out JsonElement k) && k.GetString() == "homepage_mode" ? "\"news\"" : "\"\"")
            .Route("cached_news", NewsJson)
            .Route("fetch_news", "[]")
            .Route("get_total_playtime", "7384")
            .Route("preflight_launch", """{"ok":true,"items":[]}""")
            .Route("launch_game", "\"task-1\"")
            .Route("cancel_task", "null")
            .Route("update_settings", "null")
            .Route("set_instance_java", "null");
    }

    /// <summary>跑一次 <c>LoadAsync</c>(经页面自己的 <c>EnsureLoadedAsync</c>)并把 fire-and-forget 的尾巴也泵完。</summary>
    private static void Load(LaunchPage page)
    {
        Task task = page.EnsureLoadedAsync();
        C3cFiles.PumpTask(task);
        C3cFiles.Settle(12);
    }

    private static LaunchPage NewPage(C3cFakeApi api, bool load = true)
    {
        _ = api;
        var page = new LaunchPage();
        if (load)
        {
            Load(page);
        }

        return page;
    }

    // ---------------------------------------------------------------- 加载(状态机:无实例 → 有实例)

    /// <summary>【Windows 源】<c>FillCardData</c>:实例列表 / 账号 / 设置 / 版本 / Java / 模组数全部落地。</summary>
    [Fact]
    public void Load_fills_instances_versions_java_and_mods()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);

            Assert.Equal("main", page.HeroTitleNow);
            TextBlock kicker = C3cFiles.Texts(page).Single(t => t.MaxWidth == C3cExpected.KickerMaxWidth);
            Assert.Equal(I18n.L(C3cExpected.ReadyText) + " · C:\\mc\\main", kicker.Text);
            Assert.Equal("main", page.CombosNow[0].Str());
            Assert.Equal(new[] { "1.20.1", "fabric-loader-0.20.1-1.20.1" }, page.CombosNow[1].Items.Cast<string>().ToArray());
            Assert.Equal("Steve", page.CombosNow[2].Str());
            Assert.Equal("Java 17 (C:\\jdk17)", page.CombosNow[3].Str());            Assert.Equal(2048.0, page.MemorySliderNow.Value, 3);
            Assert.Equal("2048 MB", page.MemoryLabelNow.Text);
            Assert.Equal("1280", page.WidthBoxNow.Text);
            Assert.Equal("720", page.HeightBoxNow.Text);
            Assert.True(page.LaunchButtonNow.IsEnabled, "有版本时启动按钮应当可用");
            Assert.Equal(2, page.JavaOptionCountNow);

            // 小标签行(顺序照 SyncHero):版本 chip(加载器名+版本,这里是纯版本号)/ Java / 内存 / 模组数
            Assert.Equal(new[] { "1.20.1", "Java 17", "2 GB", I18n.L("{0} 个模组", 2) }, page.ChipsNow.ToArray());
        });
    }

    /// <summary>【Windows 源】<c>LoaderChip</c>:6 组加载器前缀 → 标签,不匹配回落版本全名(Theory)。</summary>
    [Theory]
    [InlineData("fabric-loader-0.20.1-26.3", "Fabric 0.20.1")]
    [InlineData("neoforge-21.1.72", "NeoForge 21.1.72")]
    [InlineData("optifine-HD_U_I7", "OptiFine HD_U_I7")]
    [InlineData("liteloader-1.12.2", "LiteLoader 1.12.2")]
    [InlineData("quilt-loader-0.23.1-1.20.1", "Quilt 0.23.1")]
    [InlineData("forge-1.20.1-47.2.0", "Forge 1.20.1")]
    [InlineData("1.20.1", "1.20.1")]
    public void Loader_chip_follows_the_windows_prefix_table(string version, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("get_installed_versions", "[\"" + version + "\"]");
            var page = NewPage(api);
            Assert.Equal(new[] { expected }, page.ChipsNow.Take(1).ToArray());
            Assert.Equal(version, page.CombosNow[1].Str());
        });
    }

    /// <summary>【Windows 源】<c>SyncHero</c> 的 Java 标签:从「Java 17 (...)」/「jdk1.8.0_301」里抠出版本号。</summary>
    [Theory]
    [InlineData("Java 17 (C:\\jdk-17)", "Java 17")]
    [InlineData("jdk1.8.0_301", "Java 8")]
    [InlineData("自动选择", "Java 自动")]
    public void Java_chip_follows_the_windows_regex(string label, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            // Java 下拉的可选值就是这个 label(Windows 也是"从 java_combo_options 来的标签")
            api.Route("java_combo_options", "[{\"label\":" + JsonSerializer.Serialize(label) + ",\"value\":\"" + "v" + "\"}]");
            api.Route("java_combo_label_for", JsonSerializer.Serialize(label));
            var page = NewPage(api);
            Assert.True(page.ChipsNow.Contains(expected), "chips=" + string.Join("|", page.ChipsNow));
        });
    }

    /// <summary>【Windows 源】<c>SyncBanner</c>:有 pack 时标题取 pack,副标题是 <c>pack_version · Minecraft mc_version · 实例 name</c>。</summary>
    [Fact]
    public void Banner_uses_the_pack_when_the_instance_has_one()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            Assert.Equal("Fabric Pack", page.BannerTitleNow);
            Assert.Equal("1.0 · Minecraft 1.20.1 · " + I18n.L("实例 ") + "main", page.BannerSubNow);
        });
    }

    /// <summary>【Windows 源】<c>SyncBanner</c>:没有 pack 时标题取所选版本,副标题是「实例 X · 点启动游戏进入世界」。</summary>
    [Fact]
    public void Banner_falls_back_to_the_selected_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("get_instances", """[{"name":"empty","path":"C:\\mc\\empty","versions":0,"mc":"","pack":""}]""");
            var page = NewPage(api);
            Assert.Equal("1.20.1", page.BannerTitleNow);
            Assert.Equal(I18n.L(C3cExpected.BannerWithVersionSub, "empty"), page.BannerSubNow);
        });
    }

    /// <summary>【Windows 源】<c>ReloadVersionsAsync</c>:版本列表为空时给出「先去下载装一个」并禁用启动。</summary>
    [Fact]
    public void Empty_version_list_sets_the_status_and_disables_launch()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("get_installed_versions", "[]");
            var page = NewPage(api);
            Assert.Equal(I18n.L(C3cExpected.NoVersionsStatus), page.StatusTextNow);
            Assert.False(page.LaunchButtonNow.IsEnabled);
        });
    }

    /// <summary>【Windows 源】<c>ReloadVersionsAsync</c> 的 <c>catch (BridgeCallException)</c>:
    /// 后端出错 ≠ 没有版本 —— 不许把状态写成「还没有版本」。</summary>
    [Fact]
    public void Version_load_error_does_not_claim_there_are_no_versions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.RouteError("get_installed_versions", "boom");
            var page = NewPage(api);
            Assert.NotEqual(I18n.L(C3cExpected.NoVersionsStatus), page.StatusTextNow);
            Assert.False(page.LaunchButtonNow.IsEnabled); // 没有版本项 → 仍然禁用
        });
    }

    /// <summary>【Windows 源】<c>Clamp.Of(value2, 1024, 32768)</c>:内存值越界要夹住(Theory)。</summary>
    [Theory]
    [InlineData(999999, 32768.0)]
    [InlineData(1, 1024.0)]
    [InlineData(4096, 4096.0)]
    public void Memory_from_settings_is_clamped(int setting, double expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("get_settings", "{\"default_memory_mb\":" + setting + "}");
            var page = NewPage(api);
            Assert.Equal(expected, page.MemorySliderNow.Value, 3);
            Assert.Equal($"{(int)expected} MB", page.MemoryLabelNow.Text);
        });
    }

    /// <summary>【Windows 源】<c>LoadNewsAsync</c>:主页模式为 blank 时新闻区与跑马灯都写「主页已设为空白」。</summary>
    [Fact]
    public void Blank_homepage_mode_is_honoured()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("get_setting", "\"blank\"");
            var page = NewPage(api);
            Assert.Equal(I18n.L(C3cExpected.BlankHomeText), page.NewsTickerNow);
            Assert.Single(page.NewsHostNow.Children);
            Assert.Equal(I18n.L(C3cExpected.BlankHomeText), Assert.IsType<TextBlock>(page.NewsHostNow.Children[0]).Text);
        });
    }

    /// <summary>【Windows 源】<c>FillNews</c>:跑马灯取第一条标题,列表最多 6 条。</summary>
    [Fact]
    public void News_rows_fill_the_ticker_and_the_list()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("cached_news", "[{\"title\":\"公告一\",\"body\":\"正文一\"},{\"title\":\"公告二\",\"body\":\"\"}]");
            var page = NewPage(api);
            Assert.Equal("公告一", page.NewsTickerNow);
            // 两条新闻:标题 + 有正文的那条带一行正文 = 3 个控件
            Assert.Equal(3, page.NewsHostNow.Children.Count);
        });
    }

    /// <summary>【Windows 源】<c>FillNews</c>:一条都没有时写「暂无新闻」。</summary>
    [Fact]
    public void No_news_shows_the_placeholder()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("cached_news", "[]");
            var page = NewPage(api);
            Assert.Equal(I18n.L(C3cExpected.NoNewsText), page.NewsTickerNow);
        });
    }

    /// <summary>【Windows 源】<c>LoadPlaytimeAsync</c>:总时长按 <c>Fmt.Duration</c> 排版。</summary>
    [Fact]
    public void Playtime_uses_the_shared_formatter()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            Assert.Equal(Fmt.Duration(7384L), page.PlaytimeNow.Text);
        });
    }

    /// <summary>【Windows 源】<c>SyncTaskCard</c>:没有任务时写「暂无下载任务」。</summary>
    [Fact]
    public void Task_card_is_empty_without_tasks()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var tasks = new C3cTaskStore(emptyNow: true);
            using var api = Api();
            var page = NewPage(api);
            Assert.Equal(I18n.L(C3cExpected.NoTasksText), Assert.IsType<TextBlock>(page.TaskHostNow.Children[0]).Text);
        });
    }

    /// <summary>【Windows 源】<c>SyncTaskCard</c>:有任务时最多 5 行,行内是「标题 + 进度/完成/失败」。</summary>
    [Fact]
    public void Task_card_lists_the_running_tasks()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var tasks = new C3cTaskStore(emptyNow: true);
            using var api = Api();
            string id = "c3c-task-" + Guid.NewGuid().ToString("N");
            TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = id, Title = "下载 X" });
            var page = NewPage(api);
            Assert.Single(page.TaskHostNow.Children);
            Grid row = Assert.IsType<Grid>(page.TaskHostNow.Children[0]);
            Assert.Contains(row.Children.OfType<TextBlock>(), t => t.Text == "下载 X");
        });
    }

    /// <summary>【Windows 源】<c>SyncTaskCard</c>:最多列 5 行(倒序取最新的 5 条)。</summary>
    [Fact]
    public void Task_card_shows_at_most_five_rows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var tasks = new C3cTaskStore(emptyNow: true);
            using var api = Api();
            for (int i = 0; i < 7; i++)
            {
                TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = "c3c-many-" + i, Title = "任务 " + i });
            }

            var page = NewPage(api);
            Assert.Equal(5, page.TaskHostNow.Children.Count);
        });
    }

    /// <summary>【Windows 源】<c>EnsureLaunchAccountAsync</c>:选了账号就写进偏好并落盘(不额外弹框)。</summary>
    [Fact]
    public void Account_is_reused_from_the_backend_list()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            // 账号下拉已经有后端给的 Steve(ListAsync 直接返回,没弹「创建离线用户」)
            Assert.Equal("Steve", page.CombosNow[2].Str());
            Assert.Equal(1, api.Count("get_account_rows"));
        });
    }

    /// <summary>【Windows 源】没有后端(<c>AppServices.Client == null</c>)时 <c>EnsureLoadedAsync</c>
    /// 如实把失败说出来(Toast「加载失败」)而不是把异常抛给外壳(PageBase 的 <c>LoadOnceAsync</c> 吞掉并记账)。</summary>
    [Fact]
    public void Load_without_a_backend_surfaces_a_toast_and_does_not_throw()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            AppServices.Client = null;
            using var toasts = new C3cToasts();
            var page = new LaunchPage();
            Task task = page.EnsureLoadedAsync();
            C3cFiles.PumpTask(task);
            Assert.True(task.IsCompletedSuccessfully, "加载失败不许把异常抛给外壳");
            Assert.Contains(toasts.Items, t => t.Title == I18n.L("加载失败"));
            Assert.Equal(I18n.L(C3cExpected.HeroNoInstance), page.HeroTitleNow);
        });
    }

    // ---------------------------------------------------------------- 交互(点按钮 → 后端调用)

    /// <summary>【Windows 源】<c>LaunchAsync</c> 的第一道闸:没有版本 → Toast「没有版本」+ 跳「下载」页。</summary>
    [Fact]
    public void Launch_without_a_version_toasts_and_navigates_to_download()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var registry = new RegistryScope();
            registry.Register("download", () => new ProbePage());
            using var shell = ShellFixture.Create();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();

            // 直接驱动外壳的那一页(它就是注册表造出来的 LaunchPage)
            var page = Assert.IsType<LaunchPage>(shell.Window.GetPage(C3cExpected.PageId));
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.Pump(() => toasts.Has(I18n.L(C3cExpected.NoVersionToastTitle)));
            Assert.Contains(toasts.Items, t => t.Title == I18n.L(C3cExpected.NoVersionToastTitle) && t.Body == I18n.L(C3cExpected.NoVersionToastBody));
            Assert.Equal("download", shell.Window.CurrentPageId);
            Assert.Equal(0, api.Count("launch_game"));
        });
    }

    /// <summary>【Windows 源】<c>LaunchAsync</c>:点「启动游戏」→ <c>preflight_launch</c> →
    /// <c>startTaskAsync("launch_game", ...)</c>,并把界面切到"运行中"(进度条可见 / 主按钮换停止 / 状态改「准备启动...」)。</summary>
    [Fact]
    public void Launch_starts_the_game_and_switches_to_the_running_state()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);

            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            C3cFiles.Settle();

            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("launch_game")).RootElement;
            Assert.Equal("main", parameters.GetProperty("instance").GetString());
            Assert.Equal("1.20.1", parameters.GetProperty("version").GetString());
            Assert.Equal("Steve", parameters.GetProperty("account").GetString());
            Assert.Equal("Steve", parameters.GetProperty("username").GetString());
            Assert.Equal(2048, parameters.GetProperty("memory_mb").GetInt32());
            Assert.Equal(1280, parameters.GetProperty("width").GetInt32());
            Assert.Equal(720, parameters.GetProperty("height").GetInt32());
            // 【Windows 源】SelectedJava 取的是 JavaOption.Value(不是 Label):送到后端的是那串路径
            Assert.Equal("C:\\jdk17", parameters.GetProperty("java").GetString());
            // 直连没填 → extra_game_args = null;BridgeClient 的序列化配置会把 null 键**省掉**,
            // 所以"没有这个键"与"值是 null"是同一件事(申报里写明)。
            Assert.False(parameters.TryGetProperty("extra_game_args", out _));
            Assert.False(parameters.GetProperty("force").GetBoolean());

            Assert.Equal("task-1", page.TaskIdNow);
            Assert.Equal(I18n.L(C3cExpected.PreparingText), page.StatusTextNow);
            Assert.True(page.ProgressShownNow);
            Assert.True(page.StopShownNow);
            Assert.False(page.LaunchShownNow);
            Assert.False(page.LaunchButtonNow.IsEnabled);
            Assert.True(page.StopButtonNow.IsEnabled);
        });
    }

    /// <summary>【Windows 源】<c>LaunchAsync</c>:预检也调了,参数与启动一致(instance / version / memory_mb / java)。</summary>
    [Fact]
    public void Launch_calls_preflight_first()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            Assert.Equal(1, api.Count("preflight_launch"));
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("preflight_launch")).RootElement;
            Assert.Equal("main", parameters.GetProperty("instance").GetString());
            Assert.Equal(2048, parameters.GetProperty("memory_mb").GetInt32());
            Assert.True(page.ProgressShownNow);
        });
    }

    /// <summary>【Windows 源】<c>ExtraArgs()</c>:直连填了 ip:port 就切成 <c>--server/--port</c>,没填就是 null(Theory)。</summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("mc.example.com:25565", "mc.example.com|25565")]
    [InlineData("mc.example.com", "mc.example.com|25565")]
    public void Extra_args_follow_the_windows_rule(string server, string? expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            page.ServerBoxNow.Text = server;
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("launch_game")).RootElement;
            bool present = parameters.TryGetProperty("extra_game_args", out JsonElement args);
            if (expected is null)
            {
                // 没填直连:键被序列化配置省掉(null 键不写)
                Assert.False(present);
                return;
            }

            Assert.True(present);
            string[] parts = expected.Split('|');
            Assert.Equal(new[] { "--server", parts[0], "--port", parts[1] }, args.EnumerateArray().Select(x => x.GetString()).ToArray());
        });
    }

    /// <summary>【Windows 源】<c>Int(text, fallback)</c>:分辨率写坏了退回 854×480(不写 0)。</summary>
    [Fact]
    public void Broken_resolution_falls_back_to_854x480()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            page.WidthBoxNow.Text = "abc";
            page.HeightBoxNow.Text = "0";
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("launch_game")).RootElement;
            Assert.Equal(854, parameters.GetProperty("width").GetInt32());
            Assert.Equal(480, parameters.GetProperty("height").GetInt32());
        });
    }

    /// <summary>【Windows 源】<c>StopAsync</c>:点「停止」→ <c>cancel_task</c> 带当前 task_id。</summary>
    [Fact]
    public void Stop_cancels_the_running_task()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            C3cFiles.Settle();

            C3cFiles.Click(page.StopButtonNow);
            C3cFiles.PumpCall(api, "cancel_task");
            Assert.Equal("task-1", JsonDocument.Parse(api.ParamsOf("cancel_task")).RootElement.GetProperty("task_id").GetString());
        });
    }

    /// <summary>【Windows 源】<c>LaunchAsync</c> 的预检支:预检回 error 时必须弹「启动预检未通过」让人确认,不点就不启动。</summary>
    [Fact]
    public void Preflight_errors_ask_before_launching()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("preflight_launch", """{"ok":false,"items":[{"level":"error","code":"java","title":"Java 不对","detail":"换一个"}]}""");
            var page = NewPage(api);
            using var dlg = DlgHarness.Show();
            using var env = UiHost.Show(page);

            C3cFiles.Click(page.LaunchButtonNow);
            DialogCard card = C3bFiles.WaitDialog();
            Assert.Equal(I18n.L("启动预检未通过"), card.Title);
            Assert.True(api.Count("launch_game") == 0, "没点确认之前不许启动");

            // 点「仍要启动」→ force = true 并真的启动
            C2c2Files.Click(card, I18n.L("仍要启动"));
            C3cFiles.PumpCall(api, "launch_game");
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("launch_game")).RootElement;
            Assert.True(parameters.GetProperty("force").GetBoolean());
        });
    }

    /// <summary>【Windows 源】预检只有一个 warn 时弹「启动预检有警告」,点「继续启动」后 force 仍是 false。</summary>
    [Fact]
    public void Preflight_warnings_ask_but_do_not_force()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("preflight_launch", """{"ok":true,"items":[{"level":"warn","code":"mem","title":"内存偏低","detail":"建议调大"}]}""");
            var page = NewPage(api);
            using var dlg = DlgHarness.Show();
            using var env = UiHost.Show(page);

            C3cFiles.Click(page.LaunchButtonNow);
            DialogCard card = C3bFiles.WaitDialog();
            Assert.Equal(I18n.L("启动预检有警告"), card.Title);
            C2c2Files.Click(card, I18n.L("继续启动"));
            C3cFiles.PumpCall(api, "launch_game");
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("launch_game")).RootElement;
            Assert.False(parameters.GetProperty("force").GetBoolean());
        });
    }

    /// <summary>【Windows 源】<c>LaunchAsync</c> 里 <c>preflight_launch</c> 失败(<c>null</c>)时也要问一句
    /// 「启动预检不可用」,不点就什么都不做。</summary>
    [Fact]
    public void Unavailable_preflight_asks_before_launching()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.RouteError("preflight_launch", "no such method");
            var page = NewPage(api);
            using var dlg = DlgHarness.Show();
            using var env = UiHost.Show(page);

            C3cFiles.Click(page.LaunchButtonNow);
            DialogCard card = C3bFiles.WaitDialog();
            Assert.Equal(I18n.L("启动预检不可用"), card.Title);
            Assert.Equal(0, api.Count("launch_game"));
        });
    }

    /// <summary>【Windows 源】<c>StartTaskAsync</c> 抛异常时:按钮状态还原 + Toast「启动失败」。</summary>
    [Fact]
    public void Launch_failure_restores_the_buttons_and_toasts()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.RouteError("launch_game", "端口被占");
            using var toasts = new C3cToasts();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.Pump(() => toasts.Has(I18n.L(C3cExpected.LaunchFailedTitle)));
            C3cFiles.Settle();
            Assert.Contains(toasts.Items, t => t.Title == I18n.L(C3cExpected.LaunchFailedTitle) && t.Body.Contains("端口被占", StringComparison.Ordinal));
            Assert.True(page.LaunchShownNow);
            Assert.False(page.StopShownNow);
            Assert.True(page.LaunchButtonNow.IsEnabled);
            Assert.Null(page.TaskIdNow);
        });
    }

    /// <summary>【Windows 源】<c>LaunchAsync</c> 里游戏已经在跑时的选择:取消 → 什么都不做。</summary>
    [Fact]
    public void Multi_instance_prompt_can_be_declined()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("is_game_running", "true");
            api.Route("allow_multi_instance", "false");
            var page = NewPage(api);
            using var dlg = DlgHarness.Show();
            using var env = UiHost.Show(page);

            C3cFiles.Click(page.LaunchButtonNow);
            DialogCard card = C3bFiles.WaitDialog();
            Assert.Equal(I18n.L(C3cExpected.MultiRunningTitle), card.Title);
            C2c2Files.Click(card, I18n.L(C3cExpected.MultiRunningOptions[0]));
            C3cFiles.Settle();
            Assert.Equal(0, api.Count("launch_game"));
            Assert.Equal(0, api.Count("set_multi_instance"));
        });
    }

    /// <summary>【Windows 源】同上但点「打开多开并启动」:<c>set_multi_instance</c> + Toast + 真的启动。</summary>
    [Fact]
    public void Multi_instance_prompt_can_enable_multi_and_launch()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("is_game_running", "true");
            api.Route("allow_multi_instance", "false");
            api.Route("set_multi_instance", "null");
            using var toasts = new C3cToasts();
            var page = NewPage(api);
            using var dlg = DlgHarness.Show();
            using var env = UiHost.Show(page);

            C3cFiles.Click(page.LaunchButtonNow);
            DialogCard card = C3bFiles.WaitDialog();
            C2c2Files.Click(card, I18n.L(C3cExpected.MultiRunningOptions[1]));
            C3cFiles.PumpCall(api, "launch_game");
            Assert.Equal(1, api.Count("set_multi_instance"));
            Assert.True(JsonDocument.Parse(api.ParamsOf("set_multi_instance")).RootElement.GetProperty("allow").GetBoolean());
            Assert.True(toasts.Has(I18n.L("已允许多开")));
        });
    }

    /// <summary>【Windows 源】<c>OnEvent</c> 的 <c>game_started</c> / <c>game_exited</c> 分支:状态文本按事件改。</summary>
    [Fact]
    public void Game_events_update_the_status_text()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            C3cFiles.Settle();

            page.OnEvent(new BridgeEvent { Event = "game_started", TaskId = "task-1" });
            Assert.Equal(I18n.L(C3cExpected.GameStartedText), page.StatusTextNow);
            Assert.True(page.ProgressBarNow.Value >= 92.0);

            using JsonDocument payload = JsonDocument.Parse("{\"code\":0}");
            page.OnEvent(new BridgeEvent { Event = "game_exited", TaskId = "task-1", Payload = payload.RootElement.Clone() });
            Assert.Equal(I18n.L(C3cExpected.GameExitedFormat, 0), page.StatusTextNow);
        });
    }

    /// <summary>【Windows 源】<c>OnEvent</c> 的 <c>progress</c> / <c>log</c> / <c>finished</c> 三条主线。</summary>
    [Fact]
    public void Progress_log_and_finish_events_drive_the_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            C3cFiles.Settle();

            page.OnEvent(new BridgeEvent { Event = "progress", TaskId = "task-1", Current = 25, Total = 100, Message = "下载中 1.2 MB/s" });
            Assert.Contains("25", page.ProgressBarNow.Value.ToString("0"));
            Assert.StartsWith(I18n.L("下载中"), page.StatusTextNow, StringComparison.Ordinal);

            page.OnEvent(new BridgeEvent { Event = "log", TaskId = "task-1", Text = "第一行" });
            page.OnEvent(new BridgeEvent { Event = "log", TaskId = "task-1", Text = "第二行" });
            Assert.Equal(2, page.LogLineCountNow);
            Assert.Equal("第一行\n第二行", page.LogTextNow);

            page.OnEvent(new BridgeEvent { Event = "finished", TaskId = "task-1", Success = true, Message = "" });
            Assert.Equal(I18n.L(C3cExpected.FinishedText), page.StatusTextNow);
            Assert.False(page.StopShownNow);
            Assert.True(page.LaunchShownNow);
            Assert.Null(page.TaskIdNow);
        });
    }

    /// <summary>【Windows 源】<c>AppendLog</c>:超过 2500 行时从头裁掉(缓冲上限)。</summary>
    [Fact]
    public void Log_buffer_is_capped_at_2500_lines()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            C3cFiles.Settle();

            for (int i = 0; i < 2600; i++)
            {
                page.OnEvent(new BridgeEvent { Event = "log", TaskId = "task-1", Text = "line" + i });
            }

            Assert.Equal(2500, page.LogLineCountNow);
            Assert.EndsWith("line2599", page.LogTextNow, StringComparison.Ordinal);
            Assert.DoesNotContain("line99\n", page.LogTextNow, StringComparison.Ordinal);
        });
    }

    /// <summary>【Windows 源】<c>OnEvent</c> 的 <c>finished</c> 失败支:没弹过崩溃卡就弹一次(AI 修复那条路)。</summary>
    [Fact]
    public void Failed_task_opens_the_crash_dialog()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            C3cFiles.Settle();
            using var dlg = DlgHarness.Show();
            using var env = UiHost.Show(page);

            page.OnEvent(new BridgeEvent { Event = "finished", TaskId = "task-1", Success = false, Message = "Java 起不来" });
            DialogCard card = C3bFiles.WaitDialog();
            Assert.Equal(I18n.L(C3cExpected.CrashTitle), card.Title);
            Assert.True(C3bFiles.HasButton(card, I18n.L(C3bExpected.CrashAiFix)), "进游戏失败时崩溃卡要带「交给 AI 修复」");
        });
    }

    /// <summary>【Windows 源】<c>OnEvent("crash")</c>:崩溃事件也走同一张卡。</summary>
    [Fact]
    public void Crash_event_opens_the_crash_dialog()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            using var dlg = DlgHarness.Show();
            using var env = UiHost.Show(page);

            page.OnEvent(new BridgeEvent
            {
                Event = "crash",
                TaskId = "",
                Title = "崩了",
                Message = "退出码 1",
                Crash = new CrashReport { Title = "崩了", Detail = "堆栈", TaskId = "" },
            });
            DialogCard card = C3bFiles.WaitDialog();
            Assert.Equal(I18n.L(C3cExpected.CrashTitle), card.Title);
        });
    }

    /// <summary>【Windows 源】<c>malformed</c> 的 <c>ret</c>:<c>Processor</c>(<c>player</c>)... 见 <c>OnEvent</c> 里
    /// 「<c>ev.TaskId != _taskId</c> 就直接 return」那条守卫 —— 别人的任务事件不影响本页状态。</summary>
    [Fact]
    public void Events_for_other_tasks_are_ignored()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            C3cFiles.Settle();

            page.OnEvent(new BridgeEvent { Event = "progress", TaskId = "other", Current = 90, Total = 100, Message = "别人的任务" });
            Assert.Equal(I18n.L(C3cExpected.PreparingText), page.StatusTextNow);
            page.OnEvent(new BridgeEvent { Event = "log", TaskId = "other", Text = "不该出现" });
            Assert.Equal(0, page.LogLineCountNow);
        });
    }

    /// <summary>【Windows 源】<c>OnShown</c>:显示时补拉版本列表并重刷英雄区(实例切换后回来不会显示陈旧数据)。</summary>
    [Fact]
    public void OnShown_refreshes_versions_when_an_instance_is_selected()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            int before = api.Count("get_installed_versions");
            page.OnShown();
            C3cFiles.Pump(() => api.Count("get_installed_versions") > before);
            Assert.True(api.Count("get_installed_versions") > before);
        });
    }

    /// <summary>【Windows 源】<c>PreferInstance</c>:偏好实例/版本要等首次填充完再落下(公共入口)。</summary>
    [Fact]
    public void PreferInstance_selects_instance_and_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            page.PreferInstance("main", "fabric-loader-0.20.1-1.20.1");
            C3cFiles.Pump(() => page.CombosNow[1].Str() == "fabric-loader-0.20.1-1.20.1");
            Assert.Equal("main", page.CombosNow[0].Str());
            Assert.Equal("fabric-loader-0.20.1-1.20.1", page.CombosNow[1].Str());
            Assert.Equal("Fabric 0.20.1", page.ChipsNow[0]);
        });
    }

    /// <summary>【Windows 源】<c>LaunchNowAsync</c>(AI 页任务卡用的入口):选中后直接启动。</summary>
    [Fact]
    public void LaunchNow_launches_the_requested_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Task task = page.LaunchNowAsync("main", "1.20.1");
            C3cFiles.PumpTask(task);
            C3cFiles.PumpCall(api, "launch_game");
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("launch_game")).RootElement;
            Assert.Equal("main", parameters.GetProperty("instance").GetString());
            Assert.Equal("1.20.1", parameters.GetProperty("version").GetString());
        });
    }

    /// <summary>【Windows 源】<c>EnsureLaunchAccountAsync</c> 的账号切换支:选了别的账号要重新填下拉并写偏好。</summary>
    [Fact]
    public void Ensure_account_fills_the_combo_when_the_backend_picks_another_one()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("get_account_rows", """[{"name":"Alex","active":true},{"name":"Steve","active":false}]""");
            var page = NewPage(api);
            Assert.Equal("Alex", page.CombosNow[2].Str());
            Assert.Equal(new[] { "Alex", "Steve" }, page.CombosNow[2].Items.Cast<string>().ToArray());
        });
    }

    /// <summary>【Windows 源】<c>SaveJavaAsync</c>:Java 下拉一改就写 <c>set_instance_java</c>(值取 value 而不是 label)。</summary>
    [Fact]
    public void Changing_java_writes_it_back()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);

            page.CombosNow[1].SelectedIndex = 1; // 先选个版本(否则 java 那一步没有实例也无所谓)
            page.CombosNow[3].SelectedIndex = 1; // 自动选择
            C3cFiles.PumpCall(api, "set_instance_java");
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("set_instance_java")).RootElement;
            Assert.Equal("main", parameters.GetProperty("name").GetString());
            Assert.Equal("自动选择", parameters.GetProperty("java").GetString());
        });
    }

    /// <summary>【Windows 源】<c>SelectedJava</c>:标签以「自动」开头时用 value「自动选择」,小标签写「Java 自动」。</summary>
    [Fact]
    public void Auto_java_is_selected_and_launched_as_auto()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("java_combo_label_for", "\"自动选择\"");
            var page = NewPage(api);
            Assert.Equal("自动选择", page.CombosNow[3].Str());
            Assert.Contains(I18n.L("Java 自动"), page.ChipsNow);

            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            JsonElement parameters = JsonDocument.Parse(api.ParamsOf("launch_game")).RootElement;
            Assert.Equal("自动选择", parameters.GetProperty("java").GetString());
        });
    }

    /// <summary>【Windows 源】<c>SaveDisplayPrefsAsync</c>:分辨率失焦提交 → <c>update_settings</c> 的
    /// <c>default_memory_mb</c> / <c>default_resolution</c>(数组两个 int)。</summary>
    [Fact]
    public void Committing_display_prefs_writes_memory_and_resolution()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            page.MemorySliderNow.Value = 8192.0;
            page.WidthBoxNow.Text = "1920";
            page.HeightBoxNow.Text = "1080";
            page.CommitDisplayPrefs();
            C3cFiles.PumpCall(api, "update_settings");
            JsonElement settings = JsonDocument.Parse(api.ParamsOf("update_settings")).RootElement.GetProperty("settings");
            Assert.Equal(8192, settings.GetProperty("default_memory_mb").GetInt32());
            int[] resolution = settings.GetProperty("default_resolution").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            Assert.Equal(new[] { 1920, 1080 }, resolution);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:279-296</c>:内存滑杆改动会把 650ms 的防抖计时器起起来。</summary>
    [Fact]
    public void Memory_slider_starts_the_debounce_timer()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = NewPage(api);
            Assert.False(page.MemorySaveTimerRunningNow);
            page.MemorySliderNow.Value = 6144.0;
            Assert.True(page.MemorySaveTimerRunningNow);
            Assert.Equal("6144 MB", page.MemoryLabelNow.Text);
            Assert.Contains("6 GB", page.ChipsNow);
        });
    }

    /// <summary>【Windows 源】<c>_notes.LostFocus</c>:便签提交写进偏好(有外壳时)。</summary>
    [Fact]
    public void Notes_are_committed_to_the_preferences()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var registry = new RegistryScope();
            using var shell = ShellFixture.Create();
            var page = Assert.IsType<LaunchPage>(shell.Window.GetPage(C3cExpected.PageId));
            page.NotesNow.Text = "记一笔";
            page.CommitNotes();
            Assert.Equal("记一笔", shell.Window.Prefs.Notes);
        });
    }

    /// <summary>【Windows 源】<c>OnShown</c> 把便签从偏好灌回来(<c>LoadAsync</c> 的 <c>_layoutLoaded</c> 只灌一次)。</summary>
    [Fact]
    public void Notes_are_loaded_from_the_preferences_once()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            using var registry = new RegistryScope();
            using var shell = ShellFixture.Create();
            shell.Window.Prefs.Notes = "上次写的";
            var page = new LaunchPage();
            Load(page);
            Assert.Equal("上次写的", page.NotesNow.Text);
        });
    }
}
