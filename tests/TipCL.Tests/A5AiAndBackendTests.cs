using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A5:本轮才进编译的 Ai 纯逻辑(Tokens / Compact / Conflict)+ McpServer 的落盘隔离守卫。
///
/// 期望值全部由源码推出:Ai\Tokens.cs、Ai\Compact.cs、Ai\Conflict.cs(InspectJar 257-295)、
/// Ai\McpServer.cs(58-98)。
///
/// 红线相关:本文件不构造 BackendApi(它的构造函数 BackendApi.cs:1738-1749 会往后台线程
/// `AuthHttp.Warmup("https://login.live.com/")` —— 联网,禁止),也不启动任何监听。
/// McpServer 只用 `SetEnabled(false)`:它只写 mcp_server.json 再调 Stop()(McpServer.cs:86-98),
/// 不开 TcpListener。所有落盘都在 TestHome.Root 下。
/// </summary>
public class A5AiAndBackendTests
{
    // ─────────────── Tokens ───────────────

    [Fact]
    public void EstimateText_counts_non_cjk_as_a_quarter_token_and_cjk_as_one()
    {
        Assert.Equal(0L, Tokens.EstimateText(null));
        Assert.Equal(0L, Tokens.EstimateText(""));
        Assert.Equal(2L, Tokens.EstimateText("abcd"));   // 4*0.25 = 1 → (long)1 + 1
        Assert.Equal(3L, Tokens.EstimateText("\u4E2D\u6587"));  // 2*1.0 = 2 → 2 + 1
        Assert.Equal(2L, Tokens.EstimateText("a\u4E2D"));       // 0.25+1 = 1.25 → 1 + 1
    }

    [Fact]
    public void EstimateText_counts_full_width_punctuation_as_cjk()
    {
        // U+3002 落在 12288..12351 区间(Tokens.cs:19)
        Assert.Equal(2L, Tokens.EstimateText("\u3002"));
    }

    [Fact]
    public void EstimateMessages_adds_four_tokens_of_overhead_per_message()
    {
        List<object> messages = new List<object> { PyDict.Of("role", "user", "content", "abcd") };

        Assert.Equal(6L, Tokens.EstimateMessages(messages));   // 4 + EstimateText("abcd")
        Assert.Equal(0L, Tokens.EstimateMessages(null));
        Assert.Equal(0L, Tokens.EstimateMessages(new List<object> { "not-a-dict" }));
    }

    [Fact]
    public void EstimateMessages_counts_tool_call_name_and_arguments()
    {
        var call = PyDict.Of("function", PyDict.Of("name", "t", "arguments", "{}"));
        List<object> messages = new List<object>
        {
            PyDict.Of("role", "assistant", "content", "x", "tool_calls", new List<object> { call })
        };

        // 4(overhead) + 1("x") + 1("{}") + 1("t")
        Assert.Equal(7L, Tokens.EstimateMessages(messages));
    }

    [Fact]
    public void UsageInt_reads_the_first_present_alias()
    {
        Assert.Equal(7L, Tokens.UsageInt(PyDict.Of("prompt_tokens", 7L), "prompt_tokens", "input_tokens"));
        Assert.Equal(9L, Tokens.UsageInt(PyDict.Of("input_tokens", 9L), "prompt_tokens", "input_tokens"));
        Assert.Equal(0L, Tokens.UsageInt(new PyDict(), "prompt_tokens", "input_tokens"));
    }

    [Fact]
    public void UpdateFromUsage_records_a_provider_reported_baseline()
    {
        var st = new TokenState();
        PyDict usage = PyDict.Of(
            "prompt_tokens", 100L,
            "completion_tokens", 20L,
            "prompt_tokens_details", PyDict.Of("cached_tokens", 30L));

        Tokens.UpdateFromUsage(st, usage, 3L);

        Assert.Equal(100L, st.base_tokens);
        Assert.Equal(3L, st.base_msg_index);
        Assert.Equal(30L, st.cache_read);
        Assert.Equal(20L, st.last_output_tokens);
        Assert.Equal("provider_usage", st.source);
    }

    [Fact]
    public void UpdateFromUsage_accepts_the_input_output_aliases_and_keeps_the_baseline_without_prompt_tokens()
    {
        var st = new TokenState();

        Tokens.UpdateFromUsage(st, PyDict.Of("output_tokens", 5L), 1L);
        Assert.Equal(0L, st.base_tokens);
        Assert.Equal("estimate", st.source);       // 没有 prompt_tokens → baseline 不变
        Assert.Equal(5L, st.last_output_tokens);

        Tokens.UpdateFromUsage(st, PyDict.Of("input_tokens", 42L), 2L);
        Assert.Equal(42L, st.base_tokens);
        Assert.Equal(2L, st.base_msg_index);
        Assert.Equal(0L, st.cache_read);
        Assert.Equal("provider_usage", st.source);
    }

    [Fact]
    public void UpdateFromUsage_tolerates_null_usage()
    {
        var st = new TokenState();

        Tokens.UpdateFromUsage(st, null, 0L);

        Assert.Equal(0L, st.last_output_tokens);
        Assert.Equal("estimate", st.source);
    }

    [Fact]
    public void CurrentInputTokens_adds_the_estimate_of_the_tail_to_the_provider_baseline()
    {
        List<object> messages = new List<object>
        {
            PyDict.Of("role", "user", "content", "abcd"),
            PyDict.Of("role", "user", "content", "abcd")
        };
        Assert.Equal(12L, Tokens.EstimateMessages(messages));

        var st = new TokenState { source = "provider_usage", base_tokens = 100L, base_msg_index = 1L };
        Assert.Equal(106L, Tokens.CurrentInputTokens(messages, st));

        // base_msg_index 超出当前消息数 → 退回纯估算
        st.base_msg_index = 5L;
        Assert.Equal(12L, Tokens.CurrentInputTokens(messages, st));

        st.source = "estimate";
        Assert.Equal(12L, Tokens.CurrentInputTokens(messages, st));
        Assert.Equal(0L, Tokens.CurrentInputTokens(null, st));
    }

    // ─────────────── Compact ───────────────

    [Fact]
    public void AutoConfigFromSettings_defaults_and_clamps_the_context_window()
    {
        Assert.Equal(131072L, Compact.AutoConfigFromSettings(null).context_window);
        Assert.Equal(131072L, Compact.AutoConfigFromSettings(new PyDict()).context_window);
        Assert.Equal(131072L, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", "abc")).context_window);
        Assert.Equal(131072L, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", 0L)).context_window);
        Assert.Equal(8192L, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", 5000L)).context_window);
        Assert.Equal(2000000L, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", 3000000L)).context_window);
        Assert.Equal(65536L, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", 65536L)).context_window);
    }

    [Fact]
    public void AutoThreshold_is_the_window_minus_the_buffer_never_below_zero()
    {
        Assert.Equal(118072L, Compact.AutoThreshold(new AutoConfig()));                 // 131072 - 13000
        Assert.Equal(0L, Compact.AutoThreshold(new AutoConfig { context_window = 10000L }));
    }

    [Fact]
    public void ShouldAutocompact_reports_disabled_and_circuit_breaker()
    {
        List<object> messages = Enumerable.Range(0, 20)
            .Select(i => (object)PyDict.Of("role", "user", "content", i.ToString()))
            .ToList();

        Assert.Equal((false, "disabled"), Compact.ShouldAutocompact(messages, new AutoConfig { enabled = false }, new CompactState(), new TokenState()));
        Assert.Equal((false, "circuit_breaker"), Compact.ShouldAutocompact(
            messages,
            new AutoConfig { max_consecutive_failures = 3L },
            new CompactState { consecutive_failures = 3L },
            new TokenState()));
    }

    private static List<object> ToolMessages(int count, int contentLength)
    {
        return Enumerable.Range(0, count)
            .Select(i => (object)PyDict.Of("role", "tool", "content", new string((char)('a' + i), contentLength)))
            .ToList();
    }

    [Fact]
    public void Microcompact_returns_the_input_unchanged_when_disabled()
    {
        List<object> messages = ToolMessages(3, 300);

        MicroResult r = Compact.Microcompact(messages, new MicroConfig { enabled = false }, 0.0);

        Assert.Equal("disabled", r.decision_reason);
        Assert.Equal(3, r.messages.Count);
        Assert.False(r.changed);
    }

    [Fact]
    public void Microcompact_reports_no_candidates_without_tool_messages()
    {
        var messages = new List<object> { PyDict.Of("role", "user", "content", "hi") };

        MicroResult r = Compact.Microcompact(messages, new MicroConfig(), 0.0);

        Assert.Equal("no_candidates", r.decision_reason);
        Assert.False(r.changed);
    }

    [Fact]
    public void Microcompact_reports_nothing_to_clear_when_every_tool_message_is_recent()
    {
        List<object> messages = ToolMessages(2, 300);

        MicroResult r = Compact.Microcompact(messages, new MicroConfig { keep_recent_tool_results = 5L }, 0.0);

        Assert.Equal("nothing_to_clear", r.decision_reason);
        Assert.False(r.changed);
    }

    [Fact]
    public void Microcompact_is_not_triggered_without_a_token_threshold_or_idle_time()
    {
        List<object> messages = ToolMessages(1, 300);
        var cfg = new MicroConfig { keep_recent_tool_results = 0L };

        MicroResult r = Compact.Microcompact(messages, cfg, 0.0);

        Assert.Equal("not_triggered", r.decision_reason);
        Assert.Equal(300, ((string)((PyDict)r.messages[0]).Get("content")).Length);
    }

    [Fact]
    public void Microcompact_clears_old_tool_results_when_the_token_threshold_is_hit()
    {
        List<object> messages = ToolMessages(2, 300);
        var cfg = new MicroConfig
        {
            keep_recent_tool_results = 1L,
            threshold_tokens = 1L,
            min_token_savings = 0L
        };

        MicroResult r = Compact.Microcompact(messages, cfg, 0.0);

        Assert.Equal("applied", r.decision_reason);
        Assert.True(r.changed);
        Assert.Equal(1L, r.cleared_count);
        Assert.True(r.tokens_saved > 0L);
        Assert.Equal(Compact._PLACEHOLDER, ((PyDict)r.messages[0]).Get("content"));
        // 最近一条(tool 消息被保留的那个)不动
        Assert.Equal(300, ((string)((PyDict)r.messages[1]).Get("content")).Length);
        // 原来的列表不被就地改写
        Assert.Equal(300, ((string)((PyDict)messages[0]).Get("content")).Length);
    }

    [Fact]
    public void Microcompact_is_triggered_by_idle_time_alone()
    {
        List<object> messages = ToolMessages(1, 300);
        var cfg = new MicroConfig
        {
            keep_recent_tool_results = 0L,
            idle_threshold_minutes = 30L,
            min_token_savings = 0L
        };

        Assert.Equal("applied", Compact.Microcompact(messages, cfg, 30.0 * 60.0).decision_reason);
        Assert.Equal("not_triggered", Compact.Microcompact(messages, cfg, 30.0 * 60.0 - 1.0).decision_reason);
    }

    [Fact]
    public void Microcompact_gives_up_when_the_saving_is_below_the_minimum()
    {
        List<object> messages = ToolMessages(1, 300);
        var cfg = new MicroConfig
        {
            keep_recent_tool_results = 0L,
            threshold_tokens = 1L,
            min_token_savings = 100000L
        };

        MicroResult r = Compact.Microcompact(messages, cfg, 0.0);

        Assert.Equal("below_min_savings", r.decision_reason);
        Assert.False(r.changed);
        Assert.Equal(0L, r.cleared_count);
        Assert.Equal(300, ((string)((PyDict)r.messages[0]).Get("content")).Length);
    }

    [Fact]
    public void Microcompact_constants_match_the_source()
    {
        Assert.Equal(
            "[\u5DE5\u5177\u7ED3\u679C\u5DF2\u6E05\u7406\uFF1A\u5185\u5BB9\u8F83\u65E7\u5DF2\u4ECE\u4E0A\u4E0B\u6587\u79FB\u9664\uFF0C\u9700\u8981\u65F6\u8BF7\u91CD\u65B0\u8C03\u7528\u8BE5\u5DE5\u5177\u83B7\u53D6]",
            Compact._PLACEHOLDER);
        Assert.Equal(131072L, Compact.DEFAULT_CONTEXT_WINDOW);
        Assert.Equal(8192L, Compact.MIN_CONTEXT_WINDOW);
        Assert.Equal(2000000L, Compact.MAX_CONTEXT_WINDOW);
    }

    // ─────────────── Conflict.InspectJar ───────────────

    private static string NewTempDir(string name)
    {
        string dir = Path.Combine(TestHome.Root, "a5-ai", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteJar(string dirName, string fileName, params (string entry, string text)[] entries)
    {
        string dir = NewTempDir(dirName);
        string path = Path.Combine(dir, fileName);
        using FileStream fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach ((string entry, string text) in entries)
        {
            ZipArchiveEntry e = zip.CreateEntry(entry);
            using Stream s = e.Open();
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
        }
        return path;
    }

    [Fact]
    public void InspectJar_reads_a_fabric_mod_json()
    {
        string path = WriteJar("jar-fabric", "cool-mod.jar",
            ("fabric.mod.json", """{"id":"cool-mod","name":"Cool Mod","version":"1.4.2","depends":{"minecraft":">=1.20"},"breaks":{"bad-mod":"<1.0"}}"""),
            ("README.txt", "hi"));

        PyDict r = Conflict.InspectJar(path);

        Assert.Equal("cool-mod.jar", Py.S(r.Get("file")));
        Assert.Equal("cool-mod", Py.S(r.Get("id")));
        Assert.Equal("Cool Mod", Py.S(r.Get("name")));
        Assert.Equal("1.4.2", Py.S(r.Get("version")));
        Assert.Equal("fabric", Py.S(r.Get("loader")));
        Assert.True((bool)r.Get("enabled"));
        Assert.Null(r.Get("error"));

        var depends = (List<object>)r.Get("depends");
        Assert.Single(depends);
        Assert.Equal("minecraft", Py.S(((PyDict)depends[0]).Get("id")));
        Assert.Equal(">=1.20", Py.S(((PyDict)depends[0]).Get("version")));
        var breaks = (List<object>)r.Get("breaks");
        Assert.Single(breaks);
        Assert.Equal("bad-mod", Py.S(((PyDict)breaks[0]).Get("id")));
    }

    [Fact]
    public void InspectJar_prefers_fabric_over_the_other_metadata_files()
    {
        string path = WriteJar("jar-priority", "p.jar",
            ("META-INF/mods.toml", "modLoader=\"javafml\""),
            ("fabric.mod.json", """{"id":"fabric-wins","version":"1.0.0"}"""));

        PyDict r = Conflict.InspectJar(path);

        Assert.Equal("fabric-wins", Py.S(r.Get("id")));
        Assert.Equal("fabric", Py.S(r.Get("loader")));
    }

    [Fact]
    public void InspectJar_uses_the_forge_loader_for_mods_toml()
    {
        string path = WriteJar("jar-forge", "f.jar",
            ("META-INF/mods.toml", """
            [[mods]]
            modId="forge-mod"
            version="2.0.0"
            displayName="Forge Mod"
            """));

        PyDict r = Conflict.InspectJar(path);

        Assert.Equal("forge-mod", Py.S(r.Get("id")));
        Assert.Equal("Forge Mod", Py.S(r.Get("name")));
        Assert.Equal("2.0.0", Py.S(r.Get("version")));
        Assert.Equal("forge", Py.S(r.Get("loader")));
    }

    [Fact]
    public void InspectJar_uses_the_neoforge_loader_for_neoforge_mods_toml()
    {
        string path = WriteJar("jar-neoforge", "n.jar",
            ("META-INF/neoforge.mods.toml", """
            [[mods]]
            modId="neo-mod"
            version="3.0.0"
            displayName="Neo Mod"
            """));

        PyDict r = Conflict.InspectJar(path);

        Assert.Equal("neo-mod", Py.S(r.Get("id")));
        Assert.Equal("neoforge", Py.S(r.Get("loader")));
    }

    [Fact]
    public void InspectJar_reads_mcmod_info_as_forge()
    {
        string path = WriteJar("jar-mcmod", "old.jar",
            ("mcmod.info", """[{"modid":"old-mod","name":"Old Mod","version":"0.1","requiredMods":["other"]}]"""));

        PyDict r = Conflict.InspectJar(path);

        Assert.Equal("old-mod", Py.S(r.Get("id")));
        Assert.Equal("Old Mod", Py.S(r.Get("name")));
        Assert.Equal("0.1", Py.S(r.Get("version")));
        Assert.Equal("forge", Py.S(r.Get("loader")));
        var depends = (List<object>)r.Get("depends");
        Assert.Single(depends);
        Assert.Equal("other", Py.S(((PyDict)depends[0]).Get("id")));
        Assert.Equal("*", Py.S(((PyDict)depends[0]).Get("version")));
    }

    [Fact]
    public void InspectJar_falls_back_to_the_file_name_when_the_zip_has_no_mod_metadata()
    {
        string path = WriteJar("jar-plain", "plain-mod.jar", ("assets/x.txt", "nothing useful"));

        PyDict r = Conflict.InspectJar(path);

        Assert.Equal("plain-mod", Py.S(r.Get("id")));
        Assert.Equal("plain-mod", Py.S(r.Get("name")));
        Assert.Equal("", Py.S(r.Get("version")));
        Assert.Equal("unknown", Py.S(r.Get("loader")));
        Assert.True((bool)r.Get("enabled"));
        Assert.Null(r.Get("error"));
    }

    [Fact]
    public void InspectJar_marks_a_disabled_jar_and_keeps_the_double_extension_in_the_id()
    {
        string path = WriteJar("jar-disabled", "off.jar.disabled", ("fabric.mod.json", """{"id":"off","version":"1.0.0"}"""));

        PyDict r = Conflict.InspectJar(path);

        Assert.Equal("off.jar.disabled", Py.S(r.Get("file")));
        Assert.False((bool)r.Get("enabled"));
        // id / name / version 都来自清单,不是文件名(文件名版本见下面那条 "没有元数据" 的用例)
        Assert.Equal("off", Py.S(r.Get("id")));
        Assert.Equal("off", Py.S(r.Get("name")));
        Assert.Equal("1.0.0", Py.S(r.Get("version")));
        Assert.Equal("fabric", Py.S(r.Get("loader")));
    }

    [Fact]
    public void InspectJar_reports_an_error_for_a_file_that_is_not_a_zip()
    {
        string dir = NewTempDir("jar-broken");
        string path = Path.Combine(dir, "broken.jar");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not a zip at all"));

        PyDict r = Conflict.InspectJar(path);

        Assert.NotNull(r.Get("error"));
        Assert.Equal("broken", Py.S(r.Get("id")));
        Assert.Equal("unknown", Py.S(r.Get("loader")));
        Assert.True((bool)r.Get("enabled"));
    }

    [Fact]
    public void InspectJar_reports_an_error_for_broken_json_inside_a_valid_zip()
    {
        string path = WriteJar("jar-badjson", "bad-json.jar", ("fabric.mod.json", "{ not json"));

        PyDict r = Conflict.InspectJar(path);

        Assert.NotNull(r.Get("error"));
        Assert.Equal("bad-json", Py.S(r.Get("id")));
    }

    // ─────────────── McpServer:落盘隔离守卫(A4 欠的那条) ───────────────

    [Fact]
    public void McpServer_constants_are_the_source_values()
    {
        Assert.Equal(25585, McpServer.DefaultPort);
        Assert.Equal("2025-06-18", McpServer.ProtocolVersion);
        Assert.Equal("http://127.0.0.1:25585/mcp", McpServer.Url);
        Assert.False(McpServer.Running);
        Assert.Equal("", McpServer.LastError);
    }

    [Fact]
    public void McpServer_SetEnabled_writes_mcp_server_json_under_the_isolated_test_home()
    {
        string path = Path.Combine(TestHome.Root, "mcp_server.json");
        try
        {
            if (File.Exists(path)) File.Delete(path);

            bool running = McpServer.SetEnabled(false);

            Assert.False(running);
            Assert.False(McpServer.Running);
            Assert.True(File.Exists(path), "mcp_server.json 必须出现在隔离目录里:" + path);
            Assert.StartsWith(TestHome.Root, path, StringComparison.Ordinal);

            string json = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("\"enabled\": false", json, StringComparison.Ordinal);
            Assert.Contains("\"port\": 25585", json, StringComparison.Ordinal);

            // 公开读取端反向证明:Enabled 从同一个文件解析,且 ResolveRoot(McpServer.PrefPath 用的那个)
            // 就是隔离目录 —— 不会落到用户真实的 %LOCALAPPDATA%\TipCL
            Assert.False(McpServer.Enabled);
            Assert.Equal(Path.GetFullPath(TestHome.Root), Path.GetFullPath(Utils.ResolveRoot()));
            Assert.Equal(Path.GetDirectoryName(path), Path.GetDirectoryName(Path.Combine(Utils.ResolveRoot(), "mcp_server.json")));
            Assert.StartsWith(AppContext.BaseDirectory, Utils.ResolveRoot(), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void McpServer_Enabled_is_false_without_a_pref_file_and_reads_the_port_back()
    {
        string path = Path.Combine(TestHome.Root, "mcp_server.json");
        try
        {
            if (File.Exists(path)) File.Delete(path);
            Assert.False(McpServer.Enabled);

            File.WriteAllText(path, """{"enabled": true, "port": 45678}""", new UTF8Encoding(false));
            Assert.True(McpServer.Enabled);
            Assert.Equal(45678, McpServer.Port);
            Assert.Equal("http://127.0.0.1:45678/mcp", McpServer.Url);
        }
        finally
        {
            // 把静态 Port 复位成默认值(先写回默认端口、读一次,再删文件)
            File.WriteAllText(path, """{"enabled": false, "port": 25585}""", new UTF8Encoding(false));
            _ = McpServer.Enabled;
            _ = McpServer.SetEnabled(false);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
