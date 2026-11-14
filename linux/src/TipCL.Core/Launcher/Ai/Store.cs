// M4-H（AI 服务层）：AI 对话与会话事件存储（照 Windows 版 Launcher/Ai/Store.cs 搬运）。
//
// 两件事:
//   1. 对话存档 $TIPCL_HOME/ai_chats.json —— 与 Windows 版同一路径同一形状（active_id + chats[]），
//      两边的对话可以互相搬;单对话最多 200 条消息、最多 40 个对话（照参考版上限）。
//   2. 会话事件流 $TIPCL_HOME/cache/ai_sessions/<chat_id>.jsonl —— TurnStarted / ToolStarted /
//      CompactBoundary / TurnRewound 这些事件;回退（Rewind）靠它认出「最后一轮的 turn_id」。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>AI 对话存档与事件流。</summary>
public static class Store
{
    public const int MaxChats = 40;

    public const int MaxMessages = 200;

    /// <summary>消息里除 role/content 之外要带进模型上下文的字段（照参考版）。</summary>
    private static readonly string[] KeepFields = { "tool_calls", "tool_call_id", "name", "id", "note", "images", "keep" };

    /// <summary>插队消息（用户在助手说话时说的一句）的 id 前缀。</summary>
    public const string SteerIdPrefix = "steer_";

    /// <summary>测试可指向临时目录；空 = 用默认位置。</summary>
    public static string? SessionsDir;

    private static readonly object EventLock = new();

    public const int EventLogKeepDays = 30;

    public const long EventLogMaxBytes = 4194304;

    private static bool _eventPruned;

    /// <summary>测试可指向临时文件；空 = 用默认位置（与 Windows 版同一路径）。</summary>
    public static string? StoreFileOverride;

    public static string StoreFile => StoreFileOverride ?? PPath.Join(Utils.ROOT, "ai_chats.json");

    private static PyDict Empty()
    {
        string id = NewId();
        return PyDict.Of("active_id", id, "chats", Py.L(BlankChat(id)));
    }

    /// <summary>新对话 id（12 位十六进制，照参考版）。</summary>
    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

    private static PyDict BlankChat(string? cid = null)
        => PyDict.Of("id", cid ?? NewId(), "title", "新对话", "updated", (long)Py.Time(), "messages", new List<object>());

    /// <summary>是不是「插队」消息（回退时不算一轮）。</summary>
    public static bool IsSteerMessage(object? m)
        => m is PyDict d
           && Py.Eq(d.Get("role"), "user")
           && Py.S(d.Get("id")).StartsWith(SteerIdPrefix, StringComparison.Ordinal);

    private static PyDict LoadMessage(PyDict m)
    {
        var outRow = PyDict.Of("role", m.Get("role"), "content", Py.Or(m.Get("content"), ""));
        foreach (string key in KeepFields)
        {
            if (m.Get(key) != null)
            {
                outRow[key] = m[key];
            }
        }
        return outRow;
    }

    private static List<object> Tail(List<object> l, int n)
        => l.Count <= n ? l : l.GetRange(l.Count - n, n);

    /// <summary>读存档（缺失/坏掉就重建一份并落盘，照参考版）。</summary>
    public static PyDict Load()
    {
        if (Utils.ReadJson(StoreFile) is not PyDict root || root.Get("chats") is not List<object> chats || chats.Count == 0)
        {
            PyDict fresh = Empty();
            Save(fresh);
            return fresh;
        }
        var kept = new List<object>();
        foreach (object item in chats)
        {
            if (item is not PyDict chat || !Py.Truthy(chat.Get("id")))
            {
                continue;
            }
            var msgs = new List<object>();
            foreach (object raw in Py.ListOr(Py.Or(chat.Get("messages"), new List<object>())))
            {
                if (raw is PyDict m && Py.In(m.Get("role"), Py.L("user", "assistant", "error", "tool")))
                {
                    msgs.Add(LoadMessage(m));
                }
            }
            var row = PyDict.Of(
                "id", Py.Str(chat["id"]),
                "title", Py.Slice(Py.Str(Py.Or(chat.Get("title"), "对话")), null, 40),
                "updated", Py.Int(Py.Or(chat.Get("updated"), 0L)),
                "messages", Tail(msgs, MaxMessages));
            if (chat.Get("plan") is PyDict)
            {
                row["plan"] = chat["plan"];
            }
            if (Py.S(chat.Get("preset")).Length > 0)
            {
                row["preset"] = Py.S(chat["preset"]);
            }
            if (Py.S(chat.Get("view")).Length > 0)
            {
                row["view"] = Py.S(chat["view"]);
            }
            kept.Add(row);
        }
        if (kept.Count == 0)
        {
            PyDict fresh = Empty();
            Save(fresh);
            return fresh;
        }
        string active = Py.S(root.Get("active_id"));
        if (!kept.Any(c => Py.Eq(((PyDict)c)["id"], active)))
        {
            active = (string)((PyDict)kept[0])["id"];
        }
        return PyDict.Of("active_id", active, "chats", kept);
    }

    /// <summary>落盘（超过 40 个对话时丢最旧的；active_id 指向不存在时改正）。</summary>
    public static void Save(PyDict data)
    {
        List<object> chats = Py.ListOr(Py.Or(data.Get("chats"), new List<object>()));
        List<object> kept = chats.Count > MaxChats ? chats.GetRange(0, MaxChats) : chats;
        string active = Py.S(data.Get("active_id"));
        if (kept.Count > 0 && !kept.Any(c => Py.Eq(((PyDict)c).Get("id"), active)))
        {
            active = Py.Str(((PyDict)kept[0])["id"]);
            data["active_id"] = active;
        }
        Utils.WriteJson(StoreFile, PyDict.Of("active_id", kept.Count > 0 ? active : "", "chats", kept));
    }

    /// <summary>新建对话并设为当前。</summary>
    public static PyDict NewChat(PyDict data)
    {
        PyDict chat = BlankChat();
        var all = new List<object> { chat };
        all.AddRange(Py.ListOr(Py.Or(data.Get("chats"), new List<object>())));
        data["chats"] = all.Count > MaxChats ? all.GetRange(0, MaxChats) : all;
        data["active_id"] = chat["id"];
        Save(data);
        return chat;
    }

    /// <summary>
    /// 按 id 取对话，不存在就建一个（放最前）并落盘，不改变 active_id。
    /// 调用方自带 chat_id 的场景（崩溃修复的 fix-xxxx）必须用它：否则回合会落到别的对话上，
    /// 调用方按 chat_id 过滤事件就永远等不到自己的回复（参考版踩过这个坑，注释照抄）。
    /// </summary>
    public static PyDict EnsureChat(PyDict data, object cid, string? title = null)
    {
        PyDict? chat = GetChat(data, cid);
        if (chat != null)
        {
            return chat;
        }
        chat = BlankChat(Py.Str(cid));
        if (!string.IsNullOrEmpty(title))
        {
            chat["title"] = Py.Slice(title, null, 40);
        }
        var all = new List<object> { chat };
        all.AddRange(Py.ListOr(Py.Or(data.Get("chats"), new List<object>())));
        data["chats"] = all.Count > MaxChats ? all.GetRange(0, MaxChats) : all;
        Save(data);
        return chat;
    }

    /// <summary>按 id 取对话（不存在返回 null）。</summary>
    public static PyDict? GetChat(PyDict data, object? cid)
    {
        foreach (object item in Py.ListOr(Py.Or(data.Get("chats"), new List<object>())))
        {
            if (Py.Eq(((PyDict)item).Get("id"), cid))
            {
                return (PyDict)item;
            }
        }
        return null;
    }

    /// <summary>切换当前对话（不存在返回 null，不落盘）。</summary>
    public static PyDict? SetActive(PyDict data, object cid)
    {
        PyDict? chat = GetChat(data, cid);
        if (chat == null)
        {
            return null;
        }
        data["active_id"] = cid;
        Save(data);
        return chat;
    }

    /// <summary>
    /// 存档里的对话 → 发给模型的 messages。
    /// 失败回合（401、断网、手动停止）只给用户看，不进上下文：否则改好设置后模型会照着旧报错复读;
    /// 紧挨着报错的那条用户消息也一起去掉（那一轮没有答复，用户会重发）。
    /// </summary>
    public static List<object> ApiMessages(List<object>? messages)
    {
        var outList = new List<object>();
        List<object> src = messages ?? new List<object>();
        var skip = new HashSet<int>();
        for (int i = 0; i < src.Count; i++)
        {
            if (src[i] is PyDict e && Py.Eq(e.Get("role"), "error"))
            {
                skip.Add(i);
                if (i > 0 && src[i - 1] is PyDict u && Py.Eq(u.Get("role"), "user"))
                {
                    skip.Add(i - 1);
                }
            }
        }
        for (int idx = 0; idx < src.Count; idx++)
        {
            object item = src[idx];
            if (skip.Contains(idx) || item is not PyDict m)
            {
                continue;
            }
            if (Py.Eq(m.Get("role"), "tool"))
            {
                var row = PyDict.Of("role", "tool", "content", Py.Or(m.Get("content"), ""));
                if (Py.Truthy(m.Get("tool_call_id")))
                {
                    row["tool_call_id"] = m["tool_call_id"];
                }
                if (Py.Truthy(m.Get("name")))
                {
                    row["name"] = m["name"];
                }
                outList.Add(row);
            }
            else if (Py.Eq(m.Get("role"), "assistant"))
            {
                var row = PyDict.Of("role", "assistant", "content", Py.Or(m.Get("content"), ""));
                if (Py.Truthy(m.Get("tool_calls")))
                {
                    row["tool_calls"] = m["tool_calls"];
                }
                outList.Add(row);
            }
            else if (Py.Eq(m.Get("role"), "user"))
            {
                var row = PyDict.Of("role", "user", "content", Py.Or(m.Get("content"), ""));
                // 摘要标记要带着 id / keep 进上下文，否则下一轮认不出来，会从头再摘要一遍
                if (Py.S(m.Get("id")).StartsWith("compact_", StringComparison.Ordinal))
                {
                    row["id"] = m["id"];
                    if (m.Get("keep") != null)
                    {
                        row["keep"] = m["keep"];
                    }
                }
                if (Py.Truthy(m.Get("images")))
                {
                    row["images"] = Py.ListCtor(m["images"]);
                }
                outList.Add(row);
            }
        }
        while (outList.Count > 0 && Py.Eq(((PyDict)outList[0]).Get("role"), "tool"))
        {
            outList.RemoveAt(0);
        }
        return outList;
    }

    /// <summary>整份覆盖某个对话的消息（标题为空时按第一条用户消息自动起名）。</summary>
    public static void UpsertMessages(PyDict data, object cid, List<object>? messages, string? title = null)
    {
        PyDict? chat = GetChat(data, cid);
        if (chat == null)
        {
            return;
        }
        List<object> loaded = (messages ?? new List<object>())
            .Select(m => m is PyDict d ? (object)LoadMessage(d) : m)
            .ToList();
        chat["messages"] = Tail(loaded, MaxMessages);
        chat["updated"] = (long)Py.Time();
        if (!string.IsNullOrEmpty(title))
        {
            chat["title"] = Py.Slice(title, null, 40);
        }
        else if (Py.In(chat.Get("title"), Py.L("", "新对话")))
        {
            foreach (PyDict item in (List<object>)chat["messages"])
            {
                if (Py.Eq(item.Get("role"), "user") && Py.Strip(Py.S(item.Get("content"))).Length > 0)
                {
                    chat["title"] = Py.Slice(Py.Strip(Py.S(item["content"])).Replace("\n", " "), null, 24);
                    break;
                }
            }
        }
        data["chats"] = Py.Sorted((List<object>)data["chats"], c => Py.Or(((PyDict)c).Get("updated"), 0L), reverse: true);
        Save(data);
    }

    /// <summary>清掉多余的空白对话（只留当前那一个）。</summary>
    public static void PruneEmpty(PyDict data)
    {
        List<object> source = Py.ListOr(Py.Or(data.Get("chats"), new List<object>()));
        List<PyDict> blanks = source.Where(c => !Py.Truthy(((PyDict)c).Get("messages"))).Cast<PyDict>().ToList();
        if (blanks.Count <= 1)
        {
            return;
        }
        object? active = data.Get("active_id");
        PyDict keep = blanks.FirstOrDefault(c => Py.Eq(c.Get("id"), active)) ?? blanks[0];
        var drop = new HashSet<string>(blanks.Where(c => !Py.Eq(c["id"], keep["id"])).Select(c => Py.Str(c["id"])));
        data["chats"] = source.Where(c => !drop.Contains(Py.Str(((PyDict)c).Get("id")))).ToList();
        if (active is string a && drop.Contains(a))
        {
            var left = (List<object>)data["chats"];
            data["active_id"] = left.Count > 0 ? ((PyDict)left[0])["id"] : "";
        }
        Save(data);
    }

    /// <summary>删对话（删完一个不剩就自动建一个空的）。</summary>
    public static PyDict? DeleteChat(PyDict data, object cid)
    {
        data["chats"] = Py.ListOr(Py.Or(data.Get("chats"), new List<object>()))
            .Where(c => !Py.Eq(((PyDict)c).Get("id"), cid))
            .ToList();
        var chats = (List<object>)data["chats"];
        if (chats.Count == 0)
        {
            PyDict chat = BlankChat();
            data["chats"] = Py.L(chat);
            data["active_id"] = chat["id"];
        }
        else if (Py.Eq(data.Get("active_id"), cid))
        {
            data["active_id"] = ((PyDict)chats[0])["id"];
        }
        Save(data);
        return GetChat(data, data["active_id"]);
    }

    /// <summary>存/清当前计划的卡片状态（plan 为 null 表示清掉）。</summary>
    public static void SetPlan(PyDict data, object cid, PyDict? plan)
    {
        PyDict? chat = GetChat(data, cid);
        if (chat == null)
        {
            return;
        }
        if (plan == null)
        {
            chat.Pop("plan");
        }
        else
        {
            chat["plan"] = plan;
        }
        Save(data);
    }

    private static void PruneEventLogs(string dir)
    {
        if (_eventPruned)
        {
            return;
        }
        _eventPruned = true;
        try
        {
            double cutoff = Py.Time() - (double)EventLogKeepDays * 86400.0;
            foreach (string file in Directory.GetFiles(dir, "*.jsonl"))
            {
                try
                {
                    var info = new FileInfo(file);
                    double mtime = (info.LastWriteTimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    if (mtime < cutoff || info.Length > EventLogMaxBytes)
                    {
                        info.Delete();
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>会话事件目录（Rewind 找 turn 也读这里）。</summary>
    public static string SessionDirPath()
        => SessionsDir ?? PPath.Join(Utils.ROOT, "cache", "ai_sessions");

    /// <summary>记一条会话事件（TurnStarted / TurnRewound / ToolStarted ...）。</summary>
    public static void LogEvent(string sessionId, string ev, PyDict? fields = null)
    {
        try
        {
            string dir = SessionDirPath();
            Utils.EnsureDir(dir);
            PruneEventLogs(Utils.AbsPath(dir));
            var row = PyDict.Of("ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"), "event", ev ?? "");
            if (fields != null)
            {
                row.Update(fields);
            }
            string path = Utils.AbsPath(PPath.Join(dir, (string.IsNullOrEmpty(sessionId) ? "active" : sessionId) + ".jsonl"));
            lock (EventLock)
            {
                File.AppendAllText(path, PyJson.Dumps(row, null, ensureAscii: false) + "\n", new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>读回某个会话的事件（坏行跳过；Rewind 与测试用）。</summary>
    public static List<PyDict> ReadEvents(string sessionId)
    {
        var events = new List<PyDict>();
        string path = Utils.AbsPath(PPath.Join(SessionDirPath(), (string.IsNullOrEmpty(sessionId) ? "active" : sessionId) + ".jsonl"));
        if (!File.Exists(path))
        {
            return events;
        }
        try
        {
            string[] lines = new UTF8Encoding(false, false).GetString(File.ReadAllBytes(path)).Split('\n');
            foreach (string line in lines)
            {
                if (line.Length == 0)
                {
                    continue;
                }
                try
                {
                    if (PyJson.Loads(line) is PyDict row)
                    {
                        events.Add(row);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
        return events;
    }
}
