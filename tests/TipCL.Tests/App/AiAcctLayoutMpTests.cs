// AI-acct-layout-mp:账号 / 皮肤 / 联机 / 排版 七个 AI 工具的回归。
//   1 工具表:7 个名字与 Windows 同名、有风险分级、写工具非只读、关键词能把它们挑出来;
//   2 账号:list_accounts 绝不返回令牌/密码/uuid(断言字段白名单 + 原值不出现);写操作要确认;
//          同名正版账号不会被离线账号顶掉;set_skin 只认 png + 离线账号、兼容 skin_file / skin_model 别名;
//          另有一条走真实 InProcessApi 的全链路(隔离 TIPCL_HOME,用唯一名,收尾删号);
//   3 联机:未装 → 起准备任务(任务卡)不开房;已在跑 → 开房并带回房间号;join 必须带房间号;stop / status;
//   4 排版:layout_apply 先存「AI 备份 <时间>」再落盘、触发 LayoutChanged;D-30 —— file:// / http / 白名单外的按钮动作
//          整次拒绝且不落盘(参数里的 widgets 与整份 json 两条路都测),AI 写入的网页/按钮一律默认关闭、
//          用户已开且目标没变的保持;判定与 App 侧 WidgetCommon 逐条对照一致;备份只留 5 份;确认卡拿得到预览。
// 布局用例与其它碰 LayoutStoreV2 静态状态的用例同属「TipCL 真机交互回归」集合(不并行),存储指到临时目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Launcher.Layout;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>可按方法名塞返回值的假门面(记录每次调用)。</summary>
internal sealed class ScriptedApi : ToolRunner.IBackend
{
    public readonly List<(string Method, PyDict Args)> Calls = new();

    public readonly Dictionary<string, Func<PyDict, object?>> Handlers = new(StringComparer.Ordinal);

    public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
    {
        PyDict dict = ToDict(args);
        Calls.Add((method, dict));
        object? result = Handlers.TryGetValue(method, out var h) ? h(dict) : PyDict.Of("ok", true);
        if (result is null)
        {
            return Task.FromResult<T?>(default);
        }
        if (result is T typed)
        {
            return Task.FromResult<T?>(typed);
        }
        object? converted = PyJson.Loads(PyJson.Dumps(result));
        return Task.FromResult(converted is T ok ? ok : default);
    }

    public bool Called(string method) => Calls.Any(c => c.Method == method);

    private static PyDict ToDict(object? args)
    {
        if (args is null)
        {
            return new PyDict();
        }
        if (args is PyDict d)
        {
            return d;
        }
        var result = new PyDict();
        foreach (var p in args.GetType().GetProperties())
        {
            result[p.Name] = p.GetValue(args)!;
        }
        return result;
    }
}

public class AiAcctLayoutMpToolTableTests
{
    [Fact]
    public void SevenTools_AreInSchemaAndMeta_WithWindowsNames()
    {
        var names = Tools.Names();
        foreach (string n in new[] { "list_accounts", "add_offline_account", "switch_account", "set_skin", "multiplayer", "layout_get", "layout_apply" })
        {
            Assert.Contains(n, names);
            Assert.NotNull(ToolMeta.Get(n));
        }
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.True(ToolMeta.Get("list_accounts")!.@readonly);
        Assert.True(ToolMeta.Get("layout_get")!.@readonly);
        foreach (string w in new[] { "add_offline_account", "switch_account", "set_skin", "multiplayer", "layout_apply" })
        {
            Assert.False(ToolMeta.Get(w)!.@readonly, w + " 必须走确认");
            Assert.True(Tools.IsWriteTool(w));
        }
        // 联机拉起进程 + 连公共节点:按联网记,风险标签不能是「只读」
        Assert.Equal("network", ToolMeta.Get("multiplayer")!.side_effect);
        Assert.Equal(AiRisk.Network, AiUiText.RiskOf("multiplayer", ToolMeta.Get("multiplayer")!.side_effect));
        Assert.Equal(AiRisk.Write, AiUiText.RiskOf("layout_apply", ToolMeta.Get("layout_apply")!.side_effect));

        // 参数名与 Windows 版同形
        PyDict skin = Py.D(Py.D(Tools.Schemas.First(s => Tools.SchemaName(s) == "set_skin")).Get("function"));
        PyDict props = Py.D(Py.D(skin.Get("parameters")).Get("properties"));
        Assert.True(props.ContainsKey("name") && props.ContainsKey("path") && props.ContainsKey("model"));
        PyDict mp = Py.D(Py.D(Tools.Schemas.First(s => Tools.SchemaName(s) == "multiplayer")).Get("function"));
        Assert.True(Py.D(Py.D(mp.Get("parameters")).Get("properties")).ContainsKey("action"));
    }

    [Fact]
    public void Keywords_PickTheRightTools()
    {
        List<string> Pick(string text) => Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", text) }).Select(Tools.SchemaName).ToList();
        Assert.Contains("multiplayer", Pick("开个房和朋友联机"));
        Assert.Contains("set_skin", Pick("帮我把这张图设成皮肤"));
        Assert.Contains("list_accounts", Pick("我有哪些账号"));
        var layout = Pick("界面太乱了,帮我排版一下");
        Assert.Contains("layout_get", layout);
        Assert.Contains("layout_apply", layout);
    }

    [Fact]
    public void Labels_FollowArgs_AndFallBack()
    {
        Assert.Equal("切换账号 Alex", Tools.Label("switch_account", PyDict.Of("name", "Alex")));
        Assert.Equal("切换账号", Tools.Label("switch_account", new PyDict()));
        Assert.Equal("联机:host", Tools.Label("multiplayer", PyDict.Of("action", "host")));
        Assert.Equal("调整界面布局", Tools.Label("layout_apply", new PyDict()));
    }

    [Fact]
    public void AffectedPaths_OnlyLayoutApplySnapshotsFiles()
    {
        List<string> paths = Tools.AffectedPaths("layout_apply", new PyDict(), "");
        Assert.Contains(paths, p => p.EndsWith(LayoutStoreV2.FileName, StringComparison.Ordinal));
        Assert.Contains(paths, p => p.EndsWith(LayoutStoreV2.V1FileName, StringComparison.Ordinal));
        // 账号文件里是封存过的令牌:故意不做文件快照(回退旧文件会冲掉之后登录的账号)
        Assert.Empty(Tools.AffectedPaths("add_offline_account", PyDict.Of("username", "Alex"), ""));
        Assert.Empty(Tools.AffectedPaths("multiplayer", PyDict.Of("action", "host"), ""));
    }

    [Fact]
    public void AttachmentNote_OnlyListsPngPaths()
    {
        Assert.Equal("", AcctLayoutMpTools.AttachmentNote(new[] { "/tmp/a.jpg" }));
        Assert.Equal("", AcctLayoutMpTools.AttachmentNote(null));
        string note = AcctLayoutMpTools.AttachmentNote(new[] { "/tmp/a.jpg", "/tmp/skin.PNG" });
        Assert.Contains("/tmp/skin.PNG", note);
        Assert.Contains("set_skin", note);
        Assert.DoesNotContain("a.jpg", note);
    }
}

public class AiAcctLayoutMpAccountTests
{
    private static ToolRunner Runner(ScriptedApi api, bool allow = true)
    {
        var runner = new ToolRunner(api) { PermissionMode = "default", Instance = "default" };
        if (allow)
        {
            runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));
        }
        return runner;
    }

    private static List<object> Rows(params PyDict[] rows) => rows.Cast<object>().ToList();

    [Fact]
    public async Task ListAccounts_NeverReturnsTokensPasswordsOrUuid()
    {
        var api = new ScriptedApi();
        api.Handlers["get_account_rows"] = _ => Rows(
            PyDict.Of("name", "Steve", "type", "microsoft", "uuid", "UUID-123", "active", true,
                "access_token", "SECRET-ACCESS", "refresh_token", "SECRET-REFRESH", "password", "SECRET-PASS",
                "api", "https://skin.example/api", "avatar", "data:image/png;base64,SECRETAVATAR",
                "skin_file", "/home/u/.tipcl/skins/Steve.png", "skin_model", "slim"),
            PyDict.Of("name", "Alex", "type", "offline", "uuid", "UUID-456", "active", false));
        var call = new ToolCall("l1", "list_accounts", new PyDict());
        string text = await Runner(api, allow: false).RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status); // 只读:不需要确认
        foreach (string secret in new[] { "SECRET", "UUID-", "access_token", "refresh_token", "password", "uuid", "avatar", "skin.example", "/home/u" })
        {
            Assert.DoesNotContain(secret, text);
        }
        var rows = Py.ListOr(PyJson.Loads(text));
        Assert.Equal(2, rows.Count);
        foreach (object r in rows)
        {
            Assert.Equal(AcctLayoutMpTools.AccountFields.OrderBy(x => x), Py.D(r).Keys.OrderBy(x => x));
        }
        PyDict steve = Py.D(rows[0]);
        Assert.Equal("Steve.png", Py.S(steve.Get("skin_file")));
        Assert.Equal("slim", Py.S(steve.Get("skin_model")));
        Assert.True(Py.Truthy(steve.Get("active")));
    }

    [Fact]
    public async Task AddOfflineAccount_NeedsConfirm_ValidatesName_AndCallsApi()
    {
        var api = new ScriptedApi();
        api.Handlers["get_account_rows"] = _ => new List<object>();
        var denied = new ToolCall("a0", "add_offline_account", PyDict.Of("username", "Alex_01"));
        await Runner(api, allow: false).RunAsync(denied);
        Assert.Equal(ToolCallStatus.DENIED, denied.status);
        Assert.False(api.Called("add_offline_account"));

        var bad = new ToolCall("a1", "add_offline_account", PyDict.Of("username", "a b"));
        string badText = await Runner(api).RunAsync(bad);
        Assert.Contains("3-16", badText);
        Assert.False(api.Called("add_offline_account"));

        var ok = new ToolCall("a2", "add_offline_account", PyDict.Of("username", "Alex_01"));
        string okText = await Runner(api).RunAsync(ok);
        Assert.Equal(ToolCallStatus.COMPLETED, ok.status);
        Assert.Contains("Alex_01", okText);
        Assert.Equal("Alex_01", Py.S(api.Calls.Single(c => c.Method == "add_offline_account").Args.Get("username")));
    }

    [Fact]
    public async Task AddOfflineAccount_DoesNotClobberSameNamePremiumAccount()
    {
        var api = new ScriptedApi();
        api.Handlers["get_account_rows"] = _ => Rows(PyDict.Of("name", "Steve", "type", "microsoft", "active", true));
        string text = await Runner(api).RunAsync(new ToolCall("a3", "add_offline_account", PyDict.Of("username", "Steve")));
        Assert.Contains("正版", text);
        Assert.False(api.Called("add_offline_account"));
    }

    [Fact]
    public async Task SwitchAccount_UnknownNameListsAccounts_KnownNameSwitches()
    {
        var api = new ScriptedApi();
        api.Handlers["get_account_rows"] = _ => Rows(PyDict.Of("name", "Steve", "type", "offline"), PyDict.Of("name", "Alex", "type", "offline"));
        api.Handlers["set_active_account"] = a => Py.S(a.Get("name"));
        string miss = await Runner(api).RunAsync(new ToolCall("s1", "switch_account", PyDict.Of("name", "Herobrine")));
        Assert.Contains("Steve", miss);
        Assert.Contains("Alex", miss);
        Assert.False(api.Called("set_active_account"));

        string hit = await Runner(api).RunAsync(new ToolCall("s2", "switch_account", PyDict.Of("name", "alex")));
        Assert.Contains("Alex", hit);
        Assert.Equal("Alex", Py.S(api.Calls.Single(c => c.Method == "set_active_account").Args.Get("name")));
    }

    [Fact]
    public async Task SetSkin_OfflineOnly_PngOnly_AcceptsAliases()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tipcl_aiskin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string png = Path.Combine(dir, "skin.png");
            File.WriteAllBytes(png, SkinPngFixture.Make(64, 64));
            var api = new ScriptedApi();
            api.Handlers["get_account_rows"] = _ => Rows(
                PyDict.Of("name", "Alex", "type", "offline", "active", true),
                PyDict.Of("name", "Steve", "type", "microsoft", "active", false));

            // 别名 skin_file / skin_model;不写 name → 用当前账号
            var call = new ToolCall("k1", "set_skin", PyDict.Of("skin_file", png, "skin_model", "slim"));
            string text = await Runner(api).RunAsync(call);
            Assert.Equal(ToolCallStatus.COMPLETED, call.status);
            Assert.Contains("Alex", text);
            var set = api.Calls.Single(c => c.Method == "set_account_skin").Args;
            Assert.Equal("Alex", Py.S(set.Get("name")));
            Assert.Equal(png, Py.S(set.Get("path")));
            Assert.Equal("slim", Py.S(set.Get("model")));

            string premium = await Runner(api).RunAsync(new ToolCall("k2", "set_skin", PyDict.Of("name", "Steve", "path", png)));
            Assert.Contains("离线账号", premium);
            string notPng = await Runner(api).RunAsync(new ToolCall("k3", "set_skin", PyDict.Of("name", "Alex", "path", Path.Combine(dir, "nope.jpg"))));
            Assert.Contains("png", notPng);
            Assert.Equal(1, api.Calls.Count(c => c.Method == "set_account_skin"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    [Fact]
    public void RealApi_AddSwitchSkinList_EndToEnd() => TestApp.RunAsync(async () =>
    {
        // 走真实 InProcessApi(设置页「账号」同一套 Core API),数据根是 TestIsolation 的临时 TIPCL_HOME
        string name = "ai_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string dir = Path.Combine(Path.GetTempPath(), "tipcl_aiskin_real_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var runner = new ToolRunner(new RealBackend()) { PermissionMode = "default", Instance = "default" };
        runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));
        try
        {
            string added = await runner.RunAsync(new ToolCall("r1", "add_offline_account", PyDict.Of("username", name)));
            Assert.Contains(name, added);

            string switched = await runner.RunAsync(new ToolCall("r2", "switch_account", PyDict.Of("name", name)));
            Assert.Contains(name, switched);

            string png = Path.Combine(dir, "my skin.png");
            File.WriteAllBytes(png, SkinPngFixture.Make(64, 64));
            var skinCall = new ToolCall("r3", "set_skin", PyDict.Of("name", name, "path", png, "model", "slim"));
            string skinned = await runner.RunAsync(skinCall);
            Assert.True(skinCall.status == ToolCallStatus.COMPLETED, skinned);

            PyDict skin = (await InProcessApi.Instance.CallAsync<PyDict>("get_account_skin", new PyDict { ["name"] = name }))!;
            Assert.False(string.IsNullOrEmpty(Py.S(skin["skin_file"])));
            Assert.True(Py.Truthy(skin["slim"]));

            string listed = await runner.RunAsync(new ToolCall("r4", "list_accounts", new PyDict()));
            PyDict mine = Py.ListOr(PyJson.Loads(listed)).Select(Py.D).Single(r => Py.S(r.Get("name")) == name);
            Assert.Equal("offline", Py.S(mine.Get("type")));
            Assert.True(Py.Truthy(mine.Get("active")));
            Assert.Equal("slim", Py.S(mine.Get("skin_model")));
            Assert.DoesNotContain("uuid", listed);
        }
        finally
        {
            await InProcessApi.Instance.CallAsync<object>("remove_account", new PyDict { ["name"] = name });
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    });

    private sealed class RealBackend : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => InProcessApi.Instance.CallAsync<T>(method, args, ct);
    }
}

public class AiAcctLayoutMpMultiplayerTests
{
    private static (ToolRunner runner, List<string> tasks) Runner(ScriptedApi api)
    {
        var tasks = new List<string>();
        var runner = new ToolRunner(api)
        {
            PermissionMode = "default",
            Instance = "default",
            Confirm = (_, _, _, _) => Task.FromResult((true, false)),
            TaskStarted = (id, _, tool) => tasks.Add(id + "|" + tool),
        };
        return (runner, tasks);
    }

    private static ScriptedApi Api(bool supported = true, bool installed = true, bool running = true, string room = "")
    {
        var api = new ScriptedApi();
        api.Handlers["terracotta_snapshot"] = _ => PyDict.Of("supported", supported, "installed", installed, "running", running,
            "label", running ? "等待中" : "未启动", "room", room, "error", "", "game_running", false);
        api.Handlers["terracotta_prepare"] = _ => "task-tc-1";
        return api;
    }

    [Fact]
    public async Task Multiplayer_IsConfirmGated()
    {
        var api = Api();
        var runner = new ToolRunner(api) { PermissionMode = "default" };
        var call = new ToolCall("m0", "multiplayer", PyDict.Of("action", "host"));
        await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.DENIED, call.status);
        Assert.False(api.Called("terracotta_host"));
    }

    [Fact]
    public async Task Host_WhenNotInstalled_StartsPrepareTask_AndDoesNotHost()
    {
        var api = Api(installed: false, running: false);
        var (runner, tasks) = Runner(api);
        string text = await runner.RunAsync(new ToolCall("m1", "multiplayer", PyDict.Of("action", "host")));
        Assert.Contains("下载", text);
        Assert.True(api.Called("terracotta_prepare"));
        Assert.False(api.Called("terracotta_host"));
        Assert.Equal(new[] { "task-tc-1|multiplayer" }, tasks);
    }

    [Fact]
    public async Task Host_WhenRunning_ReturnsRoomNumber()
    {
        var api = Api(room: "U/ABCD-EFGH-IJKL-MNOP");
        var (runner, _) = Runner(api);
        string text = await runner.RunAsync(new ToolCall("m2", "multiplayer", PyDict.Of("action", "host")));
        Assert.True(api.Called("terracotta_host"));
        PyDict result = Py.D(PyJson.Loads(text));
        Assert.Equal("U/ABCD-EFGH-IJKL-MNOP", Py.S(result.Get("room")));
        Assert.Contains("U/ABCD-EFGH-IJKL-MNOP", Py.S(result.Get("message")));
        Assert.False(api.Called("terracotta_prepare"));
    }

    [Fact]
    public async Task Join_NeedsRoom_Stop_Status_Unsupported()
    {
        var api = Api();
        var (runner, _) = Runner(api);
        Assert.Contains("房间号", await runner.RunAsync(new ToolCall("m3", "multiplayer", PyDict.Of("action", "join"))));
        Assert.False(api.Called("terracotta_join"));

        await runner.RunAsync(new ToolCall("m4", "multiplayer", PyDict.Of("action", "join", "room", " U/AAAA-BBBB-CCCC-DDDD ")));
        Assert.Equal("U/AAAA-BBBB-CCCC-DDDD", Py.S(api.Calls.Single(c => c.Method == "terracotta_join").Args.Get("room")));

        Assert.Contains("退出", await runner.RunAsync(new ToolCall("m5", "multiplayer", PyDict.Of("action", "stop"))));
        Assert.True(api.Called("terracotta_idle"));

        PyDict status = Py.D(PyJson.Loads(await runner.RunAsync(new ToolCall("m6", "multiplayer", PyDict.Of("action", "status")))));
        Assert.True(Py.Truthy(status.Get("running")));
        Assert.False(api.Called("terracotta_prepare"));

        var none = Api(supported: false);
        var (r2, _) = Runner(none);
        Assert.Contains("不支持", await r2.RunAsync(new ToolCall("m7", "multiplayer", PyDict.Of("action", "host"))));
        Assert.False(none.Called("terracotta_host"));
    }
}

[Collection("TipCL 真机交互回归")]
public class AiAcctLayoutMpLayoutTests
{
    private static ToolRunner Runner(Action<PyDict?>? onPreview = null)
    {
        return new ToolRunner(new ScriptedApi())
        {
            PermissionMode = "default",
            Instance = "default",
            Confirm = (_, _, _, preview) =>
            {
                onPreview?.Invoke(preview);
                return Task.FromResult((true, false));
            },
        };
    }

    private static string FileText() => File.Exists(LayoutStoreV2.FilePath) ? File.ReadAllText(LayoutStoreV2.FilePath) : "";

    private static PyDict Web(string id, string url, bool enabled = false)
        => PyDict.Of("id", id, "type", "web", "title", "网页", "settings", PyDict.Of("url", url, "enabled", enabled));

    private static PyDict Button(string id, string action, bool enabled = false)
        => PyDict.Of("id", id, "type", "button", "title", "按钮", "settings", PyDict.Of("action", action, "enabled", enabled));

    [Fact]
    public void LayoutGet_IsReadOnly_AndSummarizes() => TestApp.RunAsync(async () =>
    {
        using var store = new LayoutStoreDir("aiget");
        var call = new ToolCall("g1", "layout_get", new PyDict());
        string text = await new ToolRunner(new ScriptedApi()).RunAsync(call); // 没接确认也能跑
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        PyDict summary = Py.D(PyJson.Loads(text));
        Assert.Equal(2L, Py.Int(summary.Get("version")));
        Assert.NotEmpty(Py.ListOr(Py.D(summary.Get("shell")).Get("tabs")));
        Assert.False(File.Exists(LayoutStoreV2.FilePath), "只读工具不该落盘");
    });

    [Fact]
    public void LayoutApply_BacksUpAsAiProfile_ThenSaves_AndRaisesLayoutChanged() => TestApp.RunAsync(async () =>
    {
        using var store = new LayoutStoreDir("aiapply");
        LayoutV2 before = LayoutStoreV2.Load();
        int changed = 0;
        Action onChanged = () => Interlocked.Increment(ref changed);
        LayoutStoreV2.LayoutChanged += onChanged;
        try
        {
            PyDict? seen = null;
            var call = new ToolCall("p1", "layout_apply", PyDict.Of(
                "shell_tabs", new List<object> { "settings", "launch" },
                "page", "launch", "blocks", new List<object> { PyDict.Of("id", "news", "hidden", true), PyDict.Of("id", "hero") }));
            string text = await Runner(p => seen = p).RunAsync(call);
            Assert.Equal(ToolCallStatus.COMPLETED, call.status);
            Assert.Contains("AI 备份 ", text);

            // 确认卡拿得到「改前改后」预览,且预览里点明会先备份
            Assert.NotNull(seen);
            List<string> flat = Preview.Flatten(seen);
            Assert.Contains(flat, l => l.Contains("AI 备份"));
            Assert.Contains(flat, l => l.StartsWith("+", StringComparison.Ordinal) && l.Contains("news(hidden)"));

            LayoutStoreV2.ResetCache();
            LayoutV2 after = LayoutStoreV2.Load(); // 从磁盘重读,证明真的写进了 ui_layout_v2.json
            Assert.Equal(new[] { "settings", "launch" }, after.Layout.Shell.Tabs);
            Assert.Equal("news", after.Layout.Pages["launch"].Blocks[0].Id);
            Assert.True(after.Layout.Pages["launch"].Blocks[0].Hidden);
            string backup = Assert.Single(after.Profiles.Keys, k => k.StartsWith(LayoutTools.BackupPrefix, StringComparison.Ordinal));
            Assert.Contains(backup, text);
            Assert.Equal(before.Layout.Shell.Tabs, after.Profiles[backup].Shell.Tabs);
            Assert.True(changed >= 1, "保存后要触发 LayoutChanged,页面区块才会即时重排");
            Assert.True(File.Exists(LayoutStoreV2.V1FilePath), "v1 投影要跟着写通");
        }
        finally
        {
            LayoutStoreV2.LayoutChanged -= onChanged;
        }
    });

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("FILE:///C:/Windows/win.ini")]
    [InlineData("http://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    public void LayoutApply_RejectsNonHttpsWeb_AndWritesNothing(string url) => TestApp.RunAsync(async () =>
    {
        using var store = new LayoutStoreDir("aiweb");
        string text = await Runner().RunAsync(new ToolCall("w1", "layout_apply", PyDict.Of("widgets", new List<object> { Web("evil", url, enabled: true) })));
        Assert.Contains("不能用", text);
        Assert.False(File.Exists(LayoutStoreV2.FilePath), "被拒的改动不能落盘");
        LayoutStoreV2.ResetCache();
        Assert.DoesNotContain(LayoutStoreV2.Load().Profiles.Keys, k => k.StartsWith(LayoutTools.BackupPrefix, StringComparison.Ordinal));
    });

    [Theory]
    [InlineData("cmd:calc.exe")]
    [InlineData("exec:/bin/sh")]
    [InlineData("folder:C:/Windows")]
    [InlineData("rpc:delete_instance")]
    [InlineData("page:not-a-page")]
    [InlineData("calc.exe")]
    public void LayoutApply_RejectsButtonActionsOutsideWhitelist(string action) => TestApp.RunAsync(async () =>
    {
        using var store = new LayoutStoreDir("aibtn");
        string viaWidgets = await Runner().RunAsync(new ToolCall("b1", "layout_apply", PyDict.Of("widgets", new List<object> { Button("evil", action) })));
        Assert.Contains("不能用", viaWidgets);

        // 整份 json 这条路同样要过门禁(不能拿 json 绕过 widgets 的校验)
        LayoutV2 doc = LayoutStoreV2.Load();
        doc.Layout.Widgets.Add(new WidgetDef { Id = "evil2", Type = "button", Title = "x", Settings = new Dictionary<string, object?> { ["action"] = action, ["enabled"] = true } });
        PyDict json = Py.D(PyJson.Loads(LayoutStoreV2.ExportJson(doc)));
        string viaJson = await Runner().RunAsync(new ToolCall("b2", "layout_apply", PyDict.Of("json", json)));
        Assert.Contains("不能用", viaJson);
        Assert.False(File.Exists(LayoutStoreV2.FilePath));
    });

    [Fact]
    public void LayoutApply_AiCannotEnableDangerousWidgets_ButKeepsUsersOwnSwitch() => TestApp.RunAsync(async () =>
    {
        using var store = new LayoutStoreDir("aiarm");
        // 用户自己早已开好的网页块
        LayoutV2 seed = LayoutStoreV2.Load();
        seed.Layout.Widgets.Add(new WidgetDef { Id = "mine", Type = "web", Title = "我的", Settings = new Dictionary<string, object?> { ["url"] = "https://minecraft.net/", ["enabled"] = true } });
        LayoutStoreV2.Save(seed);

        string text = await Runner().RunAsync(new ToolCall("d1", "layout_apply", PyDict.Of("widgets", new List<object>
        {
            Web("news", "https://example.com/news", enabled: true),
            Button("go", "page:launch", enabled: true),
            PyDict.Of("id", "mine", "type", "web", "title", "改个名", "settings", PyDict.Of("url", "https://minecraft.net/", "enabled", true)),
        })));
        Assert.Contains("保持关闭", text);
        LayoutStoreV2.ResetCache();
        Dictionary<string, WidgetDef> widgets = LayoutStoreV2.Load().Layout.Widgets.ToDictionary(w => w.Id);
        Assert.False(Enabled(widgets["news"]));
        Assert.False(Enabled(widgets["go"]));
        Assert.True(Enabled(widgets["mine"]));
        Assert.Equal("改个名", widgets["mine"].Title);

        // 改了已开块的网址 → 视为新目标,也要关掉
        await Runner().RunAsync(new ToolCall("d2", "layout_apply", PyDict.Of("widgets", new List<object>
        {
            PyDict.Of("id", "mine", "type", "web", "title", "我的", "settings", PyDict.Of("url", "https://evil.example/", "enabled", true)),
        })));
        LayoutStoreV2.ResetCache();
        Assert.False(Enabled(LayoutStoreV2.Load().Layout.Widgets.Single(w => w.Id == "mine")));
    });

    private static bool Enabled(WidgetDef w) => w.Settings.TryGetValue("enabled", out object? v) && v is bool b && b;

    [Fact]
    public void LayoutApply_KeepsAtMostFiveAiBackups_AndRejectsBadInput() => TestApp.RunAsync(async () =>
    {
        using var store = new LayoutStoreDir("aiprune");
        for (int i = 0; i < 7; i++)
        {
            var tabs = i % 2 == 0 ? new List<object> { "launch", "settings" } : new List<object> { "settings", "launch" };
            await Runner().RunAsync(new ToolCall("x" + i, "layout_apply", PyDict.Of("shell_tabs", tabs)));
        }
        LayoutStoreV2.ResetCache();
        Assert.Equal(LayoutTools.MaxBackups, LayoutStoreV2.Load().Profiles.Keys.Count(k => k.StartsWith(LayoutTools.BackupPrefix, StringComparison.Ordinal)));

        Assert.Contains("stack / grid / canvas", await Runner().RunAsync(new ToolCall("y1", "layout_apply", PyDict.Of("page", "launch", "mode", "spiral"))));
        Assert.Contains("页签", await Runner().RunAsync(new ToolCall("y2", "layout_apply", PyDict.Of("shell_tabs", new List<object> { "rm -rf" }))));
        Assert.Contains("找不到", await Runner().RunAsync(new ToolCall("y3", "layout_apply", PyDict.Of("profile", "不存在的方案"))));
        Assert.Contains("没有识别到", await Runner().RunAsync(new ToolCall("y4", "layout_apply", new PyDict())));
    });

    [Fact]
    public void LayoutApply_WithoutConfirm_IsDenied_AndWritesNothing() => TestApp.RunAsync(async () =>
    {
        using var store = new LayoutStoreDir("aideny");
        var call = new ToolCall("n1", "layout_apply", PyDict.Of("shell_tabs", new List<object> { "settings" }));
        await new ToolRunner(new ScriptedApi()) { PermissionMode = "default" }.RunAsync(call);
        Assert.Equal(ToolCallStatus.DENIED, call.status);
        Assert.False(File.Exists(LayoutStoreV2.FilePath));
    });

    [Fact]
    public void Guard_AgreesWithWidgetCommon_OnEveryCase()
    {
        string[] urls =
        {
            "", "   ", "example.com", "https://example.com", "https://example.com/a?b=1", "HTTPS://EXAMPLE.COM",
            "http://example.com", "file:///etc/passwd", "File://x", "javascript:alert(1)", "data:text/html,x",
            "ftp://example.com", "https://", "https:///nohost", new string('a', 3000),
        };
        foreach (string url in urls)
        {
            bool a = LayoutTools.TryAcceptWebUrl(url, out string na, out string ra);
            bool b = WidgetCommon.TryAcceptWebUrl(url, out string nb, out string rb);
            Assert.True(a == b && na == nb && ra == rb, "url 判定不一致: " + url);
        }
        string[] actions =
        {
            "", "page", "page:", "page:launch", "page:multiplayer", "page:plugin:", "page:plugin:demo:panel", "page:nope",
            "launch:默认", "launch:默认|1.20.1", "launch:|1.20.1", "folder:data", "folder:DATA", "folder:C:/",
            "rpc:list_instances", "rpc:get_launcher_state", "rpc:plugins_list", "rpc:delete_instance", "cmd:calc", "exec:sh", ":x",
        };
        foreach (string action in actions)
        {
            bool a = LayoutTools.TryParseAction(action, out string ka, out string a1, out string a2, out string ra);
            bool b = WidgetCommon.TryParseAction(action, out string kb, out string b1, out string b2, out string rb);
            Assert.True(a == b && ka == kb && a1 == b1 && a2 == b2 && ra == rb, "action 判定不一致: " + action);
        }
        Assert.Equal(WidgetCommon.RpcAllowList, LayoutTools.RpcAllowList);
        Assert.Equal(WidgetCommon.ActionKinds, LayoutTools.ActionKinds);
    }
}

/// <summary>造一张最小合法 PNG(RGBA,全透明):Skin.ValidateSkin 只看 IHDR 的宽高。</summary>
internal static class SkinPngFixture
{
    public static byte[] Make(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        Chunk(ms, "IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[1 + width * 4];
                for (int y = 0; y < height; y++)
                {
                    z.Write(row);
                }
            }
            Chunk(ms, "IDAT", raw.ToArray());
        }
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteBE(byte[] buf, int at, int value)
    {
        buf[at] = (byte)(value >> 24);
        buf[at + 1] = (byte)(value >> 16);
        buf[at + 2] = (byte)(value >> 8);
        buf[at + 3] = (byte)value;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);
        byte[] head = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(head);
        s.Write(data);
        var crc = new byte[4];
        WriteBE(crc, 0, (int)Crc(head.Concat(data).ToArray()));
        s.Write(crc);
    }

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
        }
        return c ^ 0xFFFFFFFF;
    }
}
