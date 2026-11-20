// AI-logs-fs:日志工具(get_latest_log / get_crash_report)、文件工具(fs_list / fs_read / fs_write)、
// open_folder,以及「提示里点名的工具都注册了」的动态自检。
//
// 全部在 TestIsolation 指好的临时 TIPCL_HOME 里跑;要放到「允许范围之外」的东西放在系统临时目录下另一个
// 随机目录里,用完删掉。不联网、不碰真实用户数据;打开文件夹用 Crash.ShellOpenHook 截住,不会真的弹窗。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiLogsFsTests
{
    /// <summary>什么都不做的门面:这些工具都不走 IApi。</summary>
    private sealed class NullApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static Instance NewInstance(string tag) => Instances.CreateUniqueInstance("lfs-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));

    private static string Dir(Instance inst, params string[] parts)
    {
        string dir = Path.Combine(new[] { PPath.Abs(inst.path) }.Concat(parts).ToArray());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static string ReadNorm(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static ToolRunner Runner(Instance inst, string mode = "default")
        => new(new NullApi()) { PermissionMode = mode, Instance = inst.name };

    private static async Task<(ToolCall Call, string Text)> Run(ToolRunner runner, string tool, PyDict args)
    {
        var call = new ToolCall("c-" + Guid.NewGuid().ToString("N").Substring(0, 6), tool, args);
        string text = await runner.RunAsync(call);
        return (call, text);
    }

    private static string Outside()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tipcl-lfs-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>建目录链接:先试符号链接,Windows 上没权限就退到目录联接(mklink /J 不需要管理员)。</summary>
    private static bool TryLinkDir(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception)
        {
        }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using Process? p = Process.Start(psi);
                p?.WaitForExit(15000);
                return Directory.Exists(link);
            }
            catch (Exception)
            {
            }
        }
        return false;
    }

    // ───────────────────────────── 提示 ↔ 注册表 ─────────────────────────────

    [Fact]
    public async Task MentionedTools_InPromptsAndLocales_AreRegisteredInSchemaMetaAndDispatch()
    {
        List<AiToolRefs.Mention> mentions = AiToolRefs.AllMentions();
        // 扫描本身要真的扫到东西(否则「零问题」没意义):三处来源都点了 get_latest_log / get_crash_report
        Assert.Contains(mentions, m => m.Name == "get_latest_log" && m.Source.StartsWith("系统提示", StringComparison.Ordinal));
        Assert.Contains(mentions, m => m.Name == "get_crash_report" && m.Source.StartsWith("崩溃修复上下文", StringComparison.Ordinal));
        Assert.Contains(mentions, m => m.Name == "get_latest_log" && m.Source.StartsWith("locales/", StringComparison.Ordinal));
        // 参数名、文件名不算工具
        Assert.DoesNotContain(mentions, m => m.Name == "allow_multiple");
        Assert.DoesNotContain(mentions, m => m.Name == "ai_memory");

        List<string> problems = await AiToolRefs.ProblemsAsync(mentions);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task ToolRefs_FlagsUnregisteredNames_ButNotParamsFilesMcpOrMatchingWildcards()
    {
        List<AiToolRefs.Mention> mentions = AiToolRefs.Scan("测试文本",
            "先调 get_fake_log,再看 list_mods;选择题可以 allow_multiple=true;布局写进 ui_layout_v2.json;" +
            "先 install_* 再 nosuch_*;外部工具 mcp_demo_ping 不管。");
        Assert.Contains(mentions, m => m.Name == "get_fake_log" && !m.Wildcard);
        Assert.Contains(mentions, m => m.Name == "list_mods" && !m.Wildcard);
        Assert.Contains(mentions, m => m.Name == "install" && m.Wildcard);
        Assert.Contains(mentions, m => m.Name == "nosuch" && m.Wildcard);
        Assert.DoesNotContain(mentions, m => m.Name is "allow_multiple" or "ui_layout_v2" or "mcp_demo_ping");

        List<string> problems = await AiToolRefs.ProblemsAsync(mentions);
        Assert.Equal(2, problems.Count);
        string fake = Assert.Single(problems, p => p.Contains("get_fake_log", StringComparison.Ordinal));
        // 三处都缺都要报出来(原来那个 bug 就是三处全缺)
        Assert.Contains("schema 里没有", fake);
        Assert.Contains("ToolMeta.ALL 里没有", fake);
        Assert.Contains("ToolRunner 分发不认", fake);
        Assert.Contains(problems, p => p.Contains("nosuch_*", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dispatch_EverySchemaTool_IsHandledByRunner_AndUnknownIsNot()
    {
        foreach (string name in Tools.Names())
        {
            Assert.True(await ToolRunner.DispatchesAsync(name), "ToolRunner 分发不认 " + name);
        }
        Assert.False(await ToolRunner.DispatchesAsync("no_such_tool"));
        Assert.False(await ToolRunner.DispatchesAsync(""));
    }

    [Fact]
    public void NewTools_SchemaMetaAndLabels_MatchWindowsShape()
    {
        List<string> names = Tools.Names();
        foreach (string tool in new[] { "get_latest_log", "get_crash_report", "fs_list", "fs_read", "fs_write", "open_folder" })
        {
            Assert.Contains(tool, names);
            Assert.NotNull(ToolMeta.Get(tool));
            Assert.NotEqual(tool, Tools.Label(tool));
        }
        foreach (string ro in new[] { "get_latest_log", "get_crash_report", "fs_list", "fs_read" })
        {
            ToolMetaInfo meta = ToolMeta.Get(ro)!;
            Assert.True(meta.@readonly);
            Assert.Equal("read", meta.side_effect);
            Assert.Equal("low", meta.risk);
        }
        ToolMetaInfo write = ToolMeta.Get("fs_write")!;
        Assert.False(write.@readonly);
        Assert.Equal("write_local", write.side_effect);
        Assert.Equal("high", write.risk);
        Assert.True(ToolMeta.Get("open_folder")!.@readonly);
        // 高风险写不给「本对话都允许」(D-24)
        Assert.False(ConfirmCard.AllowAlways("fs_write"));
        Assert.True(ConfirmCard.AllowAlways("fs_read"));

        PyDict Props(string tool)
        {
            object schema = Tools.Schemas.First(s => Tools.SchemaName(s) == tool);
            return Py.D(Py.D(Py.D(Py.D(schema)!.Get("function"))!.Get("parameters"))!.Get("properties"))!;
        }
        foreach (string key in new[] { "instance", "version", "offset_line", "max_lines" })
        {
            Assert.True(Props("get_latest_log").ContainsKey(key), key);
            Assert.True(Props("get_crash_report").ContainsKey(key), key);
        }
        foreach (string key in new[] { "path", "offset_line", "max_lines", "instance" })
        {
            Assert.True(Props("fs_read").ContainsKey(key), key);
        }
        foreach (string key in new[] { "path", "content", "append", "instance" })
        {
            Assert.True(Props("fs_write").ContainsKey(key), key);
        }
        Assert.True(Props("fs_list").ContainsKey("glob"));
        Assert.True(Props("open_folder").ContainsKey("kind"));
    }

    [Fact]
    public void SelectToolSchemas_LogToolsAreCore_FsAndFolderComeWithKeywords()
    {
        List<string> Picked(string text) => Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", text) })
            .Select(Tools.SchemaName).ToList();

        List<string> hello = Picked("你好");
        Assert.Contains("get_latest_log", hello);
        Assert.Contains("get_crash_report", hello);

        List<string> file = Picked("帮我看看 latest.log 里写了啥");
        Assert.Contains("fs_read", file);
        Assert.Contains("fs_list", file);

        Assert.Contains("open_folder", Picked("打开模组文件夹"));
    }

    [Fact]
    public void SystemPrompt_MentionsTheNewTools()
    {
        string prompt = AiData.SystemPrompt();
        foreach (string tool in new[] { "get_latest_log", "get_crash_report", "fs_list", "fs_read", "fs_write", "open_folder" })
        {
            Assert.Contains(tool, prompt);
        }
    }

    // ───────────────────────────── get_latest_log / get_crash_report ─────────────────────────────

    [Fact]
    public async Task GetLatestLog_ReadsInstanceLatestLog_TailByDefault()
    {
        Instance inst = NewInstance("log");
        string log = WriteFile(Path.Combine(Dir(inst, "logs"), "latest.log"),
            "[main/INFO]: Loading Minecraft\n[main/ERROR]: Mixin apply failed\n[main/FATAL]: Game crashed\n");

        (ToolCall call, string text) = await Run(Runner(inst), "get_latest_log", new PyDict());

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.StartsWith("文件: ", text);
        Assert.Contains("latest.log", text);
        Assert.Contains("Game crashed", text);
        Assert.Contains("Mixin apply failed", text);
        Assert.True(File.Exists(log));
    }

    [Fact]
    public async Task GetLatestLog_Paging_OffsetAndMaxLines()
    {
        Instance inst = NewInstance("page");
        WriteFile(Path.Combine(Dir(inst, "logs"), "latest.log"),
            string.Join("\n", Enumerable.Range(0, 50).Select(i => "line " + i)) + "\n");

        (_, string text) = await Run(Runner(inst), "get_latest_log",
            PyDict.Of("instance", inst.name, "offset_line", 10L, "max_lines", 5L));

        Assert.Contains("共 50 行", text);
        Assert.Contains("第 10–15 行", text);
        Assert.Contains("line 10", text);
        Assert.Contains("line 14", text);
        Assert.DoesNotContain("line 15", text);
        Assert.DoesNotContain("line 9\n", text);

        // 只给 max_lines = 读末尾
        (_, string tail) = await Run(Runner(inst), "get_latest_log", PyDict.Of("max_lines", 3L));
        Assert.Contains("最后 3 行", tail);
        Assert.Contains("line 49", tail);
        Assert.DoesNotContain("line 46", tail);
    }

    [Fact]
    public async Task GetLatestLog_And_CrashReport_Missing_SaySo()
    {
        Instance inst = NewInstance("none");
        (ToolCall call, string text) = await Run(Runner(inst), "get_latest_log", new PyDict());
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Equal("没有 latest 日志", text);
        (_, string crash) = await Run(Runner(inst), "get_crash_report", new PyDict());
        Assert.Equal("没有 crash 日志", crash);
    }

    [Fact]
    public async Task GetCrashReport_PicksNewestReportByMTime()
    {
        Instance inst = NewInstance("crash");
        string dir = Dir(inst, "crash-reports");
        // 名字排序与修改时间相反:证明是按「最新」挑,而不是按文件名排序后的第一份
        string older = WriteFile(Path.Combine(dir, "crash-2026-10-24_12.04.31-client.txt"), "---- Minecraft Crash Report ----\nOLD REPORT\n");
        string newer = WriteFile(Path.Combine(dir, "crash-2026-01-01_00.00.00-client.txt"), "---- Minecraft Crash Report ----\nNEW REPORT\n");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        (ToolCall call, string text) = await Run(Runner(inst), "get_crash_report", new PyDict());

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Contains("NEW REPORT", text);
        Assert.DoesNotContain("OLD REPORT", text);
        Assert.Contains(Path.GetFileName(newer), text);
    }

    [Fact]
    public async Task GetLatestLog_HugeResult_GoesToArtifact_AndReadArtifactReadsItBack()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("huge");
        WriteFile(Path.Combine(Dir(inst, "logs"), "latest.log"),
            string.Join("\n", Enumerable.Range(0, 1500).Select(i => "[12:00:00] [Render thread/INFO]: chunk " + i + " ........")) + "\n");

        ToolRunner runner = Runner(inst);
        (_, string text) = await Run(runner, "get_latest_log", PyDict.Of("max_lines", 2000L));

        Assert.StartsWith("[结果过长,已存文件] cache/ai_results/", text);
        string artifact = text.Split('\n')[0].Split(' ').Last();
        (ToolCall back, string page) = await Run(runner, "read_artifact", PyDict.Of("artifact_id", artifact, "offset", 1400L, "limit", 10L));
        Assert.Equal(ToolCallStatus.COMPLETED, back.status);
        Assert.Contains("chunk 13", page);
    }

    // ───────────────────────────── fs_list / fs_read ─────────────────────────────

    [Fact]
    public async Task FsList_And_FsRead_ResolveRelativeToInstance_AndSupportGlob()
    {
        Instance inst = NewInstance("fs");
        string logs = Dir(inst, "logs");
        WriteFile(Path.Combine(logs, "latest.log"), "a\nb\nc\n");
        WriteFile(Path.Combine(logs, "debug.log"), "d\n");
        WriteFile(Path.Combine(logs, "notes.txt"), "n\n");
        string cfg = WriteFile(Path.Combine(Dir(inst, "config"), "sodium.toml"), "fps = 60\nvsync = true\n");
        ToolRunner runner = Runner(inst);

        (ToolCall listCall, string list) = await Run(runner, "fs_list", PyDict.Of("path", "logs", "glob", "*.log"));
        Assert.Equal(ToolCallStatus.COMPLETED, listCall.status);
        Assert.Contains("latest.log", list);
        Assert.Contains("debug.log", list);
        Assert.DoesNotContain("notes.txt", list);

        (_, string read) = await Run(runner, "fs_read", PyDict.Of("path", "config/sodium.toml"));
        Assert.Contains("共 2 行", read);
        Assert.Contains("vsync = true", read);

        // 绝对路径(落在实例里)也行
        (_, string abs) = await Run(runner, "fs_read", PyDict.Of("path", cfg, "offset_line", 1L, "max_lines", 1L));
        Assert.Contains("vsync = true", abs);
        Assert.DoesNotContain("fps = 60", abs);
    }

    [Fact]
    public void FsResolve_BlocksTraversalAndAbsoluteEscape()
    {
        Instance inst = NewInstance("trav");
        Dir(inst, "logs");
        List<string> roots = AiFsTools.FsRoots(inst);
        string baseDir = PPath.Abs(inst.path);
        string outside = Outside();
        try
        {
            string outsideFile = WriteFile(Path.Combine(outside, "secret.txt"), "top secret");
            var bad = new List<string>
            {
                "", "   ", "../x", "..\\x", "logs/../../x", "logs\\..\\..\\x", "./../x",
                outsideFile, outside, Path.GetPathRoot(outside)!,
            };
            if (OperatingSystem.IsWindows())
            {
                bad.AddRange(new[]
                {
                    "C:x", "C:", @"\\server\share\x", @"\\?\C:\x", @"\Windows\x",
                    "logs\\latest.log:stream", "logs\\LATEST~1.LOG", "logs\\latest.log.", "logs \\latest.log",
                    @"C:\Windows\System32\drivers\etc\hosts",
                    // 设备名(GetFullPath 会把它们变成 \\.\nul 这类设备路径)
                    "nul", "logs\\con.txt", "aux",
                });
            }
            else
            {
                bad.AddRange(new[] { "/etc/passwd", "/proc/self/environ" });
            }
            foreach (string raw in bad)
            {
                Assert.ThrowsAny<PyException>(() => AiFsTools.FsResolve(raw, roots, baseDir));
            }
            // 合法的照样放行,且解析到实例里
            string ok = AiFsTools.FsResolve("logs/latest.log", roots, baseDir);
            Assert.True(AiFsTools.IsWithin(ok, baseDir));
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task FsTools_BlockSymlinkEscape_ButAllowLinksThatStayInside()
    {
        Instance inst = NewInstance("link");
        string root = PPath.Abs(inst.path);
        string logs = Dir(inst, "logs");
        WriteFile(Path.Combine(logs, "latest.log"), "inside\n");
        string outside = Outside();
        try
        {
            WriteFile(Path.Combine(outside, "secret.txt"), "top secret");
            string escape = Path.Combine(root, "escape");
            string inner = Path.Combine(root, "logs-link");
            // Linux / macOS 上符号链接总能建;Windows 上至少目录联接(mklink /J)不需要管理员 —— 建不出来就让用例红,不悄悄跳过
            Assert.True(TryLinkDir(escape, outside) && TryLinkDir(inner, logs), "建不出目录链接,场景构造失败");
            ToolRunner runner = Runner(inst);
            runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));

            (ToolCall read, string readText) = await Run(runner, "fs_read", PyDict.Of("path", "escape/secret.txt"));
            Assert.Equal(ToolCallStatus.FAILED, read.status);
            Assert.Contains("permission_denied", readText);
            Assert.DoesNotContain("top secret", readText);

            (ToolCall list, _) = await Run(runner, "fs_list", PyDict.Of("path", "escape"));
            Assert.Equal(ToolCallStatus.FAILED, list.status);

            (ToolCall write, _) = await Run(runner, "fs_write", PyDict.Of("path", "escape/planted.txt", "content", "x"));
            Assert.Equal(ToolCallStatus.FAILED, write.status);
            Assert.False(File.Exists(Path.Combine(outside, "planted.txt")));
            Assert.Empty(Tools.AffectedPaths("fs_write", PyDict.Of("path", "escape/planted.txt", "content", "x"), inst.name));
            Assert.Null(Preview.ChangePreview("fs_write", PyDict.Of("path", "escape/planted.txt", "content", "x"), inst.name));

            // 指向允许范围之内的链接照常可用
            (ToolCall ok, string okText) = await Run(runner, "fs_read", PyDict.Of("path", "logs-link/latest.log"));
            Assert.Equal(ToolCallStatus.COMPLETED, ok.status);
            Assert.Contains("inside", okText);
        }
        finally
        {
            foreach (string link in new[] { Path.Combine(root, "escape"), Path.Combine(root, "logs-link") })
            {
                try
                {
                    if (Directory.Exists(link))
                    {
                        Directory.Delete(link, false);
                    }
                }
                catch (Exception)
                {
                }
            }
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task FsTools_ProtectLauncherSecrets_StateFiles_ScriptsAndBinaries()
    {
        Instance inst = NewInstance("prot");
        string dataRoot = PPath.Abs(Utils.ROOT);
        string accounts = Path.Combine(dataRoot, "accounts.json");
        bool hadAccounts = File.Exists(accounts);
        if (!hadAccounts)
        {
            WriteFile(accounts, "{\"token\":\"secret\"}");
        }
        string config = Path.Combine(dataRoot, "config.json");
        string? configBefore = File.Exists(config) ? File.ReadAllText(config) : null;
        try
        {
            ToolRunner runner = Runner(inst);
            runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));

            (ToolCall readAcc, string accText) = await Run(runner, "fs_read", PyDict.Of("path", accounts));
            Assert.Equal(ToolCallStatus.FAILED, readAcc.status);
            Assert.DoesNotContain("secret", accText);

            (ToolCall writeCfg, _) = await Run(runner, "fs_write",
                PyDict.Of("path", config, "content", "{\"ai_permission_mode\":\"yolo\"}"));
            Assert.Equal(ToolCallStatus.FAILED, writeCfg.status);
            Assert.Equal(configBefore, File.Exists(config) ? File.ReadAllText(config) : null);

            foreach (string raw in new[]
                     {
                         Path.Combine(dataRoot, "cache", "ai_checkpoints", "x.txt"),
                         Path.Combine(dataRoot, "ai_chats.json"),
                         "mods/evil.sh", "mods/evil.bat", "mods/evil.jar", "run.ps1",
                     })
            {
                (ToolCall w, _) = await Run(runner, "fs_write", PyDict.Of("path", raw, "content", "x"));
                Assert.True(w.status == ToolCallStatus.FAILED, raw);
            }
            Assert.False(File.Exists(Path.Combine(PPath.Abs(inst.path), "mods", "evil.sh")));
        }
        finally
        {
            if (!hadAccounts)
            {
                File.Delete(accounts);
            }
        }
    }

    [Fact]
    public async Task FsRead_RefusesBinaryHugeAndNulFiles()
    {
        Instance inst = NewInstance("bin");
        string dir = Dir(inst, "stuff");
        File.WriteAllBytes(Path.Combine(dir, "icon.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        WriteFile(Path.Combine(dir, "big.txt"), new string('x', (int)AiFsTools.FS_MAX_READ + 10));
        File.WriteAllBytes(Path.Combine(dir, "zero.txt"), new byte[] { 0x61, 0x00, 0x62 });
        ToolRunner runner = Runner(inst);

        Assert.Contains("二进制", (await Run(runner, "fs_read", PyDict.Of("path", "stuff/icon.png"))).Text);
        Assert.Contains("文件太大", (await Run(runner, "fs_read", PyDict.Of("path", "stuff/big.txt"))).Text);
        Assert.Contains("空字节", (await Run(runner, "fs_read", PyDict.Of("path", "stuff/zero.txt"))).Text);
        (ToolCall missing, string missingText) = await Run(runner, "fs_read", PyDict.Of("path", "stuff/none.txt"));
        Assert.Equal(ToolCallStatus.FAILED, missing.status);
        Assert.Contains("not_found", missingText);
    }

    // ───────────────────────────── fs_write:确认链 + 预览 + 检查点 ─────────────────────────────

    [Fact]
    public async Task FsWrite_NeedsConfirm_ShowsDiffPreview_ThenWritesAndAppends()
    {
        Instance inst = NewInstance("write");
        string target = Path.Combine(PPath.Abs(inst.path), "config", "notes.txt");

        // 没接确认回调:拒,且不落盘
        (ToolCall denied, string deniedText) = await Run(Runner(inst), "fs_write", PyDict.Of("path", "config/notes.txt", "content", "hello\n"));
        Assert.Equal(ToolCallStatus.DENIED, denied.status);
        Assert.Contains("permission_denied", deniedText);
        Assert.False(File.Exists(target));

        // 用户拒绝:同样不落盘
        ToolRunner runner = Runner(inst);
        runner.Confirm = (_, _, _, _) => Task.FromResult((false, false));
        (ToolCall refused, _) = await Run(runner, "fs_write", PyDict.Of("path", "config/notes.txt", "content", "hello\n"));
        Assert.Equal(ToolCallStatus.DENIED, refused.status);
        Assert.False(File.Exists(target));

        // 用户同意:确认卡拿到的是「改前改后」的逐行 diff,标签带路径,理由非空
        var previews = new List<List<string>>();
        var labels = new List<string>();
        var reasons = new List<string>();
        runner.Confirm = (call, label, reason, preview) =>
        {
            labels.Add(label);
            reasons.Add(reason);
            previews.Add(Preview.Flatten(preview));
            return Task.FromResult((true, false));
        };
        (ToolCall wrote, string wroteText) = await Run(runner, "fs_write", PyDict.Of("path", "config/notes.txt", "content", "hello\n"));
        Assert.Equal(ToolCallStatus.COMPLETED, wrote.status);
        Assert.Contains("已写入", wroteText);
        Assert.Equal("hello\n", ReadNorm(target));
        Assert.Equal("写文件 config/notes.txt", labels[0]);
        Assert.Equal("高风险操作需要确认", reasons[0]);
        Assert.Contains("（新建文件）", previews[0]);
        Assert.Contains("+hello", previews[0]);
        Assert.True(AiUiText.LooksLikeDiff(previews[0]));

        (ToolCall appended, string appendedText) = await Run(runner, "fs_write",
            PyDict.Of("path", "config/notes.txt", "content", "world\n", "append", true));
        Assert.Equal(ToolCallStatus.COMPLETED, appended.status);
        Assert.Contains("已追加", appendedText);
        Assert.Equal("hello\nworld\n", ReadNorm(target));
        Assert.Contains("（追加到文件末尾）", previews[1]);
        Assert.Contains("+world", previews[1]);
        Assert.Contains(" hello", previews[1]);
        Assert.DoesNotContain("-hello", previews[1]);

        // 「只看不动」档:直接拒,确认卡都不弹
        ToolRunner plan = Runner(inst, "plan");
        int asked = 0;
        plan.Confirm = (_, _, _, _) =>
        {
            asked++;
            return Task.FromResult((true, false));
        };
        (ToolCall planCall, _) = await Run(plan, "fs_write", PyDict.Of("path", "config/notes.txt", "content", "x"));
        Assert.Equal(ToolCallStatus.DENIED, planCall.status);
        Assert.Equal(0, asked);
        Assert.Equal("hello\nworld\n", ReadNorm(target));

        // 超限内容拒写
        (_, string tooBig) = await Run(runner, "fs_write",
            PyDict.Of("path", "config/big.txt", "content", new string('y', (int)AiFsTools.FS_MAX_WRITE + 1)));
        Assert.Contains("写入内容太大", tooBig);
        Assert.False(File.Exists(Path.Combine(PPath.Abs(inst.path), "config", "big.txt")));
    }

    [Fact]
    public async Task FsWrite_AffectedPaths_FeedCheckpoint_SoRollbackRestoresAndRemoves()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("ckpt");
        string existing = WriteFile(Path.Combine(Dir(inst, "config"), "options.txt"), "v1\n");
        string created = Path.Combine(PPath.Abs(inst.path), "config", "new", "fresh.txt");
        ToolRunner runner = Runner(inst);
        runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));
        string chat = "chat-" + Guid.NewGuid().ToString("N");

        PyDict editArgs = PyDict.Of("path", "config/options.txt", "content", "v2\n");
        PyDict newArgs = PyDict.Of("path", "config/new/fresh.txt", "content", "brand new\n");
        List<string> editPaths = Tools.AffectedPaths("fs_write", editArgs, inst.name);
        List<string> newPaths = Tools.AffectedPaths("fs_write", newArgs, inst.name);
        Assert.Equal(AiFsTools.RealPath(existing), Assert.Single(editPaths));
        Assert.Equal(AiFsTools.RealPath(created), Assert.Single(newPaths));

        Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, "t1", editPaths.Concat(newPaths)).Get("ok")));
        Assert.Equal(ToolCallStatus.COMPLETED, (await Run(runner, "fs_write", editArgs)).Call.status);
        Assert.Equal(ToolCallStatus.COMPLETED, (await Run(runner, "fs_write", newArgs)).Call.status);
        Assert.Equal("v2\n", ReadNorm(existing));
        Assert.True(File.Exists(created));

        PyDict rolled = Checkpoint.Rollback(chat, "t1");
        Assert.True(Py.Truthy(rolled.Get("ok")), PyJson.Dumps(rolled));
        Assert.Equal("v1\n", ReadNorm(existing));
        Assert.False(File.Exists(created));
    }

    // ───────────────────────────── open_folder ─────────────────────────────

    [Fact]
    public async Task OpenFolder_OpensInstanceSubfolders_ThroughTheSharedShellOpen()
    {
        Instance inst = NewInstance("open");
        string root = PPath.Abs(inst.path);
        var opened = new List<string>();
        Action<string>? saved = Crash.ShellOpenHook;
        Crash.ShellOpenHook = p => opened.Add(PPath.Abs(p));
        try
        {
            ToolRunner runner = Runner(inst);
            foreach ((string kind, string expect) in new[]
                     {
                         ("mods", Path.Combine(root, "mods")),
                         ("logs", Path.Combine(root, "logs")),
                         ("saves", Path.Combine(root, "saves")),
                         ("config", Path.Combine(root, "config")),
                         ("模组", Path.Combine(root, "mods")),
                     })
            {
                opened.Clear();
                (ToolCall call, string text) = await Run(runner, "open_folder", PyDict.Of("kind", kind));
                Assert.Equal(ToolCallStatus.COMPLETED, call.status);
                Assert.StartsWith("已打开 ", text);
                Assert.True(Directory.Exists(expect), expect);
                Assert.True(string.Equals(Path.GetFullPath(expect), Path.GetFullPath(Assert.Single(opened)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), kind);
            }

            opened.Clear();
            (ToolCall game, _) = await Run(runner, "open_folder", new PyDict());
            Assert.Equal(ToolCallStatus.COMPLETED, game.status);
            Assert.True(AiFsTools.IsWithin(Assert.Single(opened), root));

            opened.Clear();
            (ToolCall backups, _) = await Run(runner, "open_folder", PyDict.Of("kind", "backups"));
            Assert.Equal(ToolCallStatus.COMPLETED, backups.status);
            Assert.True(AiFsTools.IsWithin(Assert.Single(opened), root));

            opened.Clear();
            (ToolCall bogus, string bogusText) = await Run(runner, "open_folder", PyDict.Of("kind", "../../etc"));
            Assert.Equal(ToolCallStatus.FAILED, bogus.status);
            Assert.Contains("bad_arguments", bogusText);
            Assert.Empty(opened);
        }
        finally
        {
            Crash.ShellOpenHook = saved;
        }
    }
}
