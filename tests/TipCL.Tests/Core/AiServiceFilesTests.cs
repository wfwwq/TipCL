// M4-H（AI 服务层）测试之二:检查点回滚 / 回退（Rewind）/ 对话存档 / 产物回读 / MCP 配置与路由。
//
// 这些都会真读写文件,所以一律在临时目录里跑（AiServiceDirs + TestIsolation 的 TIPCL_HOME）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class CheckpointTests
{
    [Fact]
    public void SnapshotThenRollbackRestoresModifiedFile()
    {
        using var dirs = new AiServiceDirs();
        string file = Path.Combine(dirs.Root, "work", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "before");
        var stamp = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        string chat = "chat-" + Guid.NewGuid().ToString("N");
        PyDict snap = Checkpoint.Snapshot(chat, "t1", new[] { file });
        Assert.True(Py.Truthy(snap.Get("ok")));
        Assert.Equal(1, Py.Int(snap.Get("files")));
        Assert.True(Checkpoint.TurnHasOps(chat, "t1"));
        Assert.Equal("t1", Checkpoint.LastTurnId(chat));

        File.WriteAllText(file, "AI 改过了");
        PyDict rolled = Checkpoint.Rollback(chat, "t1");
        Assert.True(Py.Truthy(rolled.Get("ok")));
        Assert.Equal(1, Py.Int(rolled.Get("restored_files")));
        Assert.Equal("before", File.ReadAllText(file));
        // mtime 也还原
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
        // 回滚过的轮次从流水里摘掉了
        Assert.False(Checkpoint.TurnHasOps(chat, "t1"));
    }

    [Fact]
    public void RollbackDeletesFilesThatDidNotExistWhenSnapshotted()
    {
        using var dirs = new AiServiceDirs();
        string target = Path.Combine(dirs.Root, "mods");
        Directory.CreateDirectory(target);
        string chat = "chat-" + Guid.NewGuid().ToString("N");

        PyDict snap = Checkpoint.Snapshot(chat, "t1", new[] { target });
        Assert.True(Py.Truthy(snap.Get("ok")));

        File.WriteAllText(Path.Combine(target, "sodium.jar"), "新装的模组");
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        File.WriteAllText(Path.Combine(target, "sub", "nested.jar"), "嵌套的");

        PyDict rolled = Checkpoint.Rollback(chat, "t1");
        Assert.True(Py.Truthy(rolled.Get("ok")));
        Assert.False(File.Exists(Path.Combine(target, "sodium.jar")));
        Assert.False(Directory.Exists(Path.Combine(target, "sub")));
        // 目录本身是快照时就在的，不能删
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void RollbackAllCoversEveryTurnAndIsIdempotent()
    {
        using var dirs = new AiServiceDirs();
        string file = Path.Combine(dirs.Root, "a.txt");
        File.WriteAllText(file, "v0");
        string chat = "chat-" + Guid.NewGuid().ToString("N");

        Checkpoint.Snapshot(chat, "t1", new[] { file });
        File.WriteAllText(file, "v1");
        Checkpoint.Snapshot(chat, "t2", new[] { file });
        File.WriteAllText(file, "v2");

        PyDict rolled = Checkpoint.Rollback(chat, "", allOps: true);
        Assert.True(Py.Truthy(rolled.Get("ok")));
        Assert.Equal("v0", File.ReadAllText(file));

        // 流水已清:再回滚一次是空操作，不该报错
        PyDict again = Checkpoint.Rollback(chat, "", allOps: true);
        Assert.True(Py.Truthy(again.Get("ok")));
        Assert.Equal(0, Py.Int(again.Get("restored_files")));
    }

    [Fact]
    public void RollbackWithoutAnyJournalIsNoOp()
    {
        using var dirs = new AiServiceDirs();
        PyDict rolled = Checkpoint.Rollback("empty-" + Guid.NewGuid().ToString("N"));
        Assert.True(Py.Truthy(rolled.Get("ok")));
        Assert.Equal(0, Py.Int(rolled.Get("restored_files")));
    }

    [Fact]
    public void SnapshotOfMissingPathRecordsItAsAbsentSoRollbackRemovesIt()
    {
        using var dirs = new AiServiceDirs();
        string file = Path.Combine(dirs.Root, "new-config.toml");
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        Checkpoint.Snapshot(chat, "t1", new[] { file });
        File.WriteAllText(file, "AI 新建的");
        Checkpoint.Rollback(chat, "t1");
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void ChatIdIsSanitizedForDirectoryName()
    {
        using var dirs = new AiServiceDirs();
        string chat = "../../evil name/" + Guid.NewGuid().ToString("N");
        Checkpoint.Snapshot(chat, "t1", new[] { Path.Combine(dirs.Root, "x.txt") });
        // 目录只能落在 checkpoints 根下（非法字符被替换掉，不会穿出去）
        foreach (string sub in Directory.GetDirectories(Checkpoint.CheckpointsDir!))
        {
            Assert.StartsWith(Path.GetFullPath(Checkpoint.CheckpointsDir!), Path.GetFullPath(sub));
            Assert.DoesNotContain("..", Path.GetFileName(sub));
        }
    }
}

public class RewindTests
{
    private static (string chat, string file) ArrangeChat(AiServiceDirs dirs, string chatId)
    {
        string file = Path.Combine(dirs.Root, "world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "原始存档");

        var data = Store.Load();
        PyDict chat = Store.EnsureChat(data, chatId, "测试对话");
        chat["messages"] = new List<object>
        {
            PyDict.Of("role", "user", "content", "第一句"),
            PyDict.Of("role", "assistant", "content", "回答一"),
            PyDict.Of("role", "user", "content", "帮我改存档"),
            PyDict.Of("role", "assistant", "content", "改好了"),
        };
        Store.Save(data);
        return (chatId, file);
    }

    [Fact]
    public void RewindTruncatesChatAndRestoresFiles()
    {
        using var dirs = new AiServiceDirs();
        string chatId = "rewind-" + Guid.NewGuid().ToString("N");
        (string chat, string file) = ArrangeChat(dirs, chatId);

        Store.LogEvent(chat, "TurnStarted", PyDict.Of("turn_id", "t1"));
        Checkpoint.Snapshot(chat, "t1", new[] { file });
        File.WriteAllText(file, "被 AI 改坏了");
        Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t1", "tool", "write_mod_config"));

        PyDict result = Rewind.RewindLastRound(chat);
        Assert.True(Py.Truthy(result.Get("ok")));
        Assert.True(Py.Truthy(result.Get("truncated")));
        Assert.True(Py.Truthy(result.Get("disk_changed")));
        Assert.Equal(1, Py.Int(result.Get("restored_files")));
        Assert.Equal("原始存档", File.ReadAllText(file));

        // 对话退到最后一句用户消息之前
        List<object> messages = Py.ListOr(Store.GetChat(Store.Load(), chat)!.Get("messages"));
        Assert.Equal(2, messages.Count);
        Assert.Equal("第一句", Py.S(((PyDict)messages[0]).Get("content")));

        // 同一会话再退一次:退的是更早那一轮（对话继续缩短），但磁盘上没有别的东西可还了
        PyDict again = Rewind.RewindLastRound(chat);
        Assert.True(Py.Truthy(again.Get("ok")));
        Assert.False(Py.Truthy(again.Get("disk_changed")));
        Assert.Equal(0, Py.Int(again.Get("restored_files")));
    }

    [Fact]
    public void RewindWithoutSessionLogStillTruncatesChat()
    {
        using var dirs = new AiServiceDirs();
        string chatId = "rewind-nolog-" + Guid.NewGuid().ToString("N");
        (string chat, _) = ArrangeChat(dirs, chatId);
        PyDict result = Rewind.RewindLastRound(chat);
        Assert.True(Py.Truthy(result.Get("ok")));
        Assert.True(Py.Truthy(result.Get("truncated")));
        Assert.Contains("会话日志缺失", Py.S(result.Get("reason")));
    }

    [Fact]
    public void RewindMarksTurnRewoundSoCandidatesShrink()
    {
        using var dirs = new AiServiceDirs();
        string chatId = "rewind-cand-" + Guid.NewGuid().ToString("N");
        ArrangeChat(dirs, chatId);
        Store.LogEvent(chatId, "TurnStarted", PyDict.Of("turn_id", "t1"));
        Store.LogEvent(chatId, "TurnStarted", PyDict.Of("turn_id", "t2"));
        Assert.Equal(new[] { "t1", "t2" }, Rewind.TurnCandidates(chatId).ToArray());
        Rewind.RewindLastRound(chatId);
        Assert.Equal(new[] { "t1" }, Rewind.TurnCandidates(chatId).ToArray());
    }

    [Fact]
    public void TrimMessagesStopsBeforeLastUserMessage()
    {
        var messages = new List<object>
        {
            PyDict.Of("role", "user", "content", "a"),
            PyDict.Of("role", "assistant", "content", "b"),
            PyDict.Of("role", "user", "content", "c"),
            PyDict.Of("role", "assistant", "content", "d"),
        };
        Assert.Equal(2, Rewind.TrimMessages(messages));
        Assert.Equal(2, messages.Count);

        // 只有「插队」消息时不算一轮
        var steerOnly = new List<object>
        {
            PyDict.Of("role", "user", "content", "顺便", "id", "steer_1"),
        };
        Assert.Equal(0, Rewind.TrimMessages(steerOnly));
        Assert.Single(steerOnly);
    }

    [Fact]
    public void RewindOnUnknownChatReportsFailure()
    {
        using var dirs = new AiServiceDirs();
        PyDict result = Rewind.RewindLastRound("nope-" + Guid.NewGuid().ToString("N"));
        Assert.False(Py.Truthy(result.Get("ok")));
        Assert.False(Py.Truthy(result.Get("truncated")));
    }
}

public class StoreTests
{
    [Fact]
    public void LoadCreatesStoreFileAndKeepsActiveChat()
    {
        using var dirs = new AiServiceDirs();
        PyDict data = Store.Load();
        Assert.True(Py.Truthy(data.Get("active_id")));
        Assert.Single(Py.ListOr(data.Get("chats")));
        Assert.True(File.Exists(Store.StoreFile));
    }

    [Fact]
    public void UpsertMessagesTitlesChatFromFirstUserMessageAndSortsByUpdated()
    {
        using var dirs = new AiServiceDirs();
        var data = Store.Load();
        PyDict chat = Store.NewChat(data);
        string id = Py.S(chat["id"]);
        Store.UpsertMessages(data, id, new List<object>
        {
            PyDict.Of("role", "user", "content", "帮我把内存改大一点\n第二行"),
            PyDict.Of("role", "assistant", "content", "好"),
        });
        PyDict reloaded = Store.GetChat(Store.Load(), id)!;
        Assert.Equal("帮我把内存改大一点 第二行", Py.S(reloaded.Get("title")));
    }

    [Fact]
    public void EnsureChatIsStableForUnknownIds()
    {
        using var dirs = new AiServiceDirs();
        var data = Store.Load();
        PyDict first = Store.EnsureChat(data, "fix-abc", "AI 修复");
        PyDict again = Store.EnsureChat(Store.Load(), "fix-abc");
        Assert.Equal(Py.S(first["id"]), Py.S(again["id"]));
        Assert.Equal("AI 修复", Py.S(again.Get("title")));
    }

    [Fact]
    public void ApiMessagesDropsErrorTurnsAndLeadingToolMessages()
    {
        var messages = new List<object>
        {
            PyDict.Of("role", "user", "content", "第一句"),
            PyDict.Of("role", "assistant", "content", "回答"),
            PyDict.Of("role", "user", "content", "这句会失败"),
            PyDict.Of("role", "error", "content", "HTTP 401"),
            PyDict.Of("role", "user", "content", "第二句"),
            PyDict.Of("role", "assistant", "content", "回答二"),
            PyDict.Of("role", "tool", "content", "工具结果", "tool_call_id", "call_1", "name", "list_mods"),
        };
        List<object> api = Store.ApiMessages(messages);
        var contents = api.Select(m => Py.S(((PyDict)m).Get("content"))).ToList();
        Assert.DoesNotContain("HTTP 401", contents);
        Assert.DoesNotContain("这句会失败", contents);
        Assert.Contains("第一句", contents);
        Assert.Equal("tool", Py.S(((PyDict)api[api.Count - 1]).Get("role")));
        Assert.Equal("call_1", Py.S(((PyDict)api[api.Count - 1]).Get("tool_call_id")));

        // 开头的孤儿 tool 消息要去掉
        List<object> trimmed = Store.ApiMessages(new List<object> { PyDict.Of("role", "tool", "content", "x"), PyDict.Of("role", "user", "content", "y") });
        Assert.Single(trimmed);
        Assert.Equal("user", Py.S(((PyDict)trimmed[0]).Get("role")));
    }

    [Fact]
    public void CompactMarkerKeepsIdAndKeepFields()
    {
        var messages = new List<object>
        {
            PyDict.Of("role", "user", "content", "[历史摘要]\n摘要", "id", "compact_u3", "keep", 4L),
        };
        List<object> api = Store.ApiMessages(messages);
        Assert.Equal("compact_u3", Py.S(((PyDict)api[0]).Get("id")));
        Assert.Equal(4, Py.Int(((PyDict)api[0]).Get("keep")));
    }

    [Fact]
    public void DeleteChatKeepsAtLeastOneAndSwitchesActive()
    {
        using var dirs = new AiServiceDirs();
        var data = Store.Load();
        PyDict a = Store.NewChat(data);
        PyDict b = Store.NewChat(data);
        PyDict left = Store.DeleteChat(data, a["id"])!;
        Assert.Equal(Py.S(b["id"]), Py.S(left["id"]));
        Assert.Equal(Py.S(b["id"]), data.Get("active_id"));
        Store.DeleteChat(data, b["id"]);
        Assert.Single(Py.ListOr(data.Get("chats")));
    }

    [Fact]
    public void PruneEmptyDropsBlankChatsButKeepsActiveOne()
    {
        using var dirs = new AiServiceDirs();
        var data = Store.Load();
        PyDict active = Store.NewChat(data);
        Store.UpsertMessages(data, active["id"], new List<object> { PyDict.Of("role", "user", "content", "hi") });
        Store.NewChat(data);
        Store.NewChat(data);
        Store.SetActive(data, active["id"]);
        Store.PruneEmpty(data);
        List<object> chats = Py.ListOr(Store.Load().Get("chats"));
        // 空白对话只留一个；有内容的当前对话保留
        Assert.Equal(2, chats.Count);
        Assert.Contains(chats, c => Py.S(((PyDict)c)["id"]) == Py.S(active["id"]));
        List<object> blankOnes = chats.Where(c => Py.ListOr(((PyDict)c).Get("messages")).Count == 0).ToList();
        Assert.Single(blankOnes);
    }

    [Fact]
    public void LogEventWritesJsonLinesAndReadEventsSkipsGarbage()
    {
        using var dirs = new AiServiceDirs();
        string chat = "events-" + Guid.NewGuid().ToString("N");
        Store.LogEvent(chat, "TurnStarted", PyDict.Of("turn_id", "t1"));
        string path = Path.Combine(Store.SessionsDir!, chat + ".jsonl");
        File.AppendAllText(path, "这不是 JSON\n");
        Store.LogEvent(chat, "TurnCompleted", PyDict.Of("turn_id", "t1"));
        List<PyDict> events = Store.ReadEvents(chat);
        Assert.Equal(2, events.Count);
        Assert.Equal("TurnStarted", Py.S(events[0].Get("event")));
        Assert.Equal("t1", Py.S(events[0].Get("turn_id")));
        Assert.Equal("TurnCompleted", Py.S(events[1].Get("event")));
    }

    [Fact]
    public void SetPlanAndClearPlanRoundTrip()
    {
        using var dirs = new AiServiceDirs();
        var data = Store.Load();
        PyDict chat = Store.NewChat(data);
        Store.SetPlan(data, chat["id"], PyDict.Of("turn_id", "t1", "items", new List<object>()));
        Assert.True(Py.Truthy(Store.GetChat(Store.Load(), chat["id"])!.Get("plan")));
        Store.SetPlan(data, chat["id"], null);
        Assert.Null(Store.GetChat(Store.Load(), chat["id"])!.Get("plan"));
    }
}

public class ArtifactsTests
{
    [Fact]
    public void StoreWritesFileAndReturnsHead()
    {
        using var dirs = new AiServiceDirs();
        string text = string.Join("\n", Enumerable.Range(1, 300).Select(i => "第 " + i + " 行内容"));
        string reply = Artifacts.Store(text, "list_mods");
        Assert.Contains("已存文件", reply);
        Assert.Contains("总行数 300", reply);
        Assert.Contains("第 1 行内容", reply);
        Assert.DoesNotContain("第 200 行内容", reply);

        string rel = reply.Split('\n')[0].Replace("[结果过长,已存文件] ", "").Trim();
        Assert.StartsWith("cache/ai_results/", rel);
        Assert.True(File.Exists(Path.Combine(Artifacts.ArtifactsDir!, new PPath(rel).Name)));
    }

    [Fact]
    public void ReadArtifactSupportsPagingAndRejectsBadNames()
    {
        using var dirs = new AiServiceDirs();
        var lines = Enumerable.Range(1, 50).Select(i => "line " + i).ToList();
        string text = string.Join("\n", lines);
        string rel = Artifacts.Save(text, "x");
        string name = new PPath(rel).Name;
        int total = Py.SplitLines(text).Count;

        string head = Artifacts.ReadArtifact(name);
        Assert.Contains($"第 1-{total}/{total} 行", head);
        Assert.Contains("line 1", head);
        string page = Artifacts.ReadArtifact(name, 10L, 5L);
        Assert.Contains($"第 11-15/{total} 行", page);
        Assert.Contains("line 11", page);
        Assert.DoesNotContain("line 16", page);
        Assert.Contains("起点超出范围", Artifacts.ReadArtifact(name, 999L, 5L));
        Assert.Contains("找不到 artifact", Artifacts.ReadArtifact("nope.txt"));
        // 带目录的输入只取文件名（模型常整段贴路径），永远只在产物目录里找，绝不会穿出去
        Assert.Contains("找不到 artifact", Artifacts.ReadArtifact("../../etc/passwd"));
        Assert.Contains("无效的 artifact 名", Artifacts.ReadArtifact("a:b.txt"));
        Assert.Contains("无效的 artifact 名", Artifacts.ReadArtifact(""));
        Assert.Contains("无效的 artifact 名", Artifacts.ReadArtifact("x..y.txt"));
    }

    [Fact]
    public void ArtifactIdIsStableAndShort()
    {
        Assert.Equal(8, Artifacts.ArtifactId("abc").Length);
        Assert.Equal(Artifacts.ArtifactId("abc"), Artifacts.ArtifactId("abc"));
        Assert.NotEqual(Artifacts.ArtifactId("abc"), Artifacts.ArtifactId("abd"));
    }

    [Fact]
    public void PruneKeepsNewestFilesAndDropsExcess()
    {
        using var dirs = new AiServiceDirs();
        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(Artifacts.ArtifactsDir!, $"f{i}.txt");
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i));
        }
        Assert.Equal(3, Artifacts.Prune(days: 7, keep: 2));
        Assert.Equal(2, Directory.GetFiles(Artifacts.ArtifactsDir!, "*.txt").Length);
    }
}

public class McpTests
{
    [Fact]
    public void ServerConfigsSkipsInvalidEntries()
    {
        var settings = PyDict.Of("ai_mcp_servers", new List<object>
        {
            PyDict.Of("name", "files", "command", "npx", "args", new List<object> { "-y", "server-files" },
                "env", PyDict.Of("TOKEN", 123L)),
            PyDict.Of("name", "bad name!", "command", "x"),
            PyDict.Of("name", "no-command"),
            PyDict.Of("command", "y"),
            "不是字典",
        });
        List<PyDict> rows = Mcp.ServerConfigs(settings);
        Assert.Single(rows);
        Assert.Equal("files", Py.S(rows[0]["name"]));
        Assert.Equal("npx", Py.S(rows[0]["command"]));
        Assert.Equal(2, Py.ListOr(rows[0]["args"]).Count);
        Assert.Equal("123", Py.S(((PyDict)rows[0]["env"]!).Get("TOKEN")));
        Assert.Empty(Mcp.ServerConfigs(null));
        Assert.Empty(Mcp.ServerConfigs(PyDict.Of("ai_mcp_servers", "不是数组")));
    }

    [Fact]
    public void ConnectServersSkipsUnstartableCommand()
    {
        using var dirs = new AiServiceDirs();
        List<McpClient> clients = Mcp.ConnectServers(PyDict.Of("ai_mcp_servers", new List<object>
        {
            PyDict.Of("name", "ghost", "command", "tipcl-no-such-mcp-server-" + Guid.NewGuid().ToString("N")),
        }));
        // 启动不了就不返回连接，也不抛（一轮对话不该因为一个 server 挂了就崩）
        Assert.Empty(clients);
        Assert.True(File.Exists(Path.Combine(Trace.TraceDir!, DateTime.Now.ToString("yyyyMMdd") + ".jsonl")));
    }

    [Fact]
    public void RouteCallRejectsUnknownNamesAndNonMcpNames()
    {
        Assert.True(Mcp.IsMcpTool("mcp_files_read"));
        Assert.False(Mcp.IsMcpTool("list_mods"));
        Assert.Equal("", Mcp.RouteCall(new List<McpClient>(), "list_mods", new PyDict()));
        McpError ex = Assert.Throws<McpError>(() => Mcp.RouteCall(new List<McpClient>(), "mcp_files_read", new PyDict()));
        Assert.Contains("没有叫 mcp_files_read 的 MCP 工具", ex.Message);
    }

    [Fact]
    public void ToolSchemasAreEmptyWithoutConnectedServers()
    {
        Assert.Empty(Mcp.McpToolSchemas(null));
        Assert.Empty(Mcp.McpToolSchemas(new List<McpClient>()));
        Mcp.CloseAll(null);
    }

    [Fact]
    public void ClientNormalizesArgsAndExposesProtocolVersion()
    {
        var client = new McpClient("files", "npx", new List<object> { "-y", 42L }, PyDict.Of("A", 1L));
        Assert.Equal("files", client.name);
        Assert.Equal(new[] { "-y", "42" }, client.args.ToArray());
        Assert.Equal("2024-11-05", McpClient.ProtocolVersion);
        client.Close();
    }
}