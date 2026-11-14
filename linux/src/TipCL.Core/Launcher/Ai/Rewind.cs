// M4-H（AI 服务层）：回退上一轮（照 Windows 版 Launcher/Ai/Rewind.cs 搬运）。
//
// 「撤回」= 两件事一起做:把对话退回到最后一句用户消息之前 + 把这轮改过的文件按检查点还原。
// 轮次从会话事件流（Store 的 ai_sessions/*.jsonl）里数:TurnStarted 记一轮、TurnRewound 表示这轮已经退过，
// 所以连点两次「撤回」不会退到同一轮上（照参考版口径）。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>回退上一轮:对话截断 + 磁盘还原 + 标记已回退。</summary>
public static class Rewind
{
    /// <summary>还没被回退过的轮次（按发生顺序）。</summary>
    public static List<string> TurnCandidates(string chatId)
    {
        var turns = new List<string>();
        var rewound = new HashSet<string>(StringComparer.Ordinal);
        foreach (PyDict ev in Store.ReadEvents(Py.Truthy(chatId) ? chatId : "active"))
        {
            string turnId = Py.Str(Py.Or(ev.Get("turn_id"), ""));
            if (turnId.Length == 0)
            {
                continue;
            }
            string kind = Py.Str(ev.Get("event"));
            if (kind == "TurnStarted")
            {
                turns.Add(turnId);
            }
            else if (kind == "TurnRewound")
            {
                rewound.Add(turnId);
            }
        }
        return turns.Where(t => !rewound.Contains(t)).ToList();
    }

    private static void MarkRewound(string chatId, string turnId)
    {
        try
        {
            Store.LogEvent(chatId, "TurnRewound", PyDict.Of("turn_id", turnId ?? ""));
        }
        catch (Exception)
        {
        }
    }

    private static void ClearPlanOf(string chatId, string turnId)
    {
        try
        {
            PyDict data = Store.Load();
            if (Store.GetChat(data, chatId)?.Get("plan") is PyDict plan && Py.Str(Py.Or(plan.Get("turn_id"), "")) == turnId)
            {
                Store.SetPlan(data, chatId, null);
            }
        }
        catch (Exception)
        {
        }
    }

    private static PyDict Result(bool ok, bool truncated, bool rollbackable)
        => PyDict.Of(
            "ok", ok,
            "truncated", truncated,
            "restored_files", 0L,
            "restored_bytes", 0L,
            "rollbackable", rollbackable,
            "disk_changed", false);

    /// <summary>
    /// 回退存档里当前对话的最后一轮。返回 {ok,truncated,restored_files,restored_bytes,rollbackable,disk_changed[,reason]}。
    /// </summary>
    public static PyDict RewindLastRound(object chatIdObj)
    {
        string chatId = Py.Str(Py.Truthy(chatIdObj) ? chatIdObj : "active");
        PyDict data = Store.Load();
        PyDict? chat = Store.GetChat(data, chatId);
        if (chat == null || chat.Count == 0)
        {
            return Result(false, false, false);
        }
        List<object> messages = Py.ListOr(Py.Or(chat.Get("messages"), new List<object>()));
        int cut = LastUserIndex(messages);
        if (cut < 0)
        {
            // 没有可退的用户消息（只有插队消息或空对话）
            return Result(true, false, false);
        }
        chat["messages"] = messages.GetRange(0, cut);
        chat["updated"] = (long)Math.Floor(Py.Time());
        Store.Save(data);

        List<string> candidates = TurnCandidates(chatId);
        string lastTurnWithOps = Checkpoint.LastTurnId(chatId);
        if (candidates.Count > 0)
        {
            string turnId = candidates[candidates.Count - 1];
            PyDict result = Result(true, true, lastTurnWithOps.Length > 0);
            bool hasOps = Checkpoint.TurnHasOps(chatId, turnId);
            if (!hasOps)
            {
                MarkRewound(chatId, turnId);
                ClearPlanOf(chatId, turnId);
                return result;
            }
            PyDict rolled = Checkpoint.Rollback(chatId, turnId);
            result["restored_files"] = Py.Or(rolled.Get("restored_files"), 0L);
            result["restored_bytes"] = Py.Or(rolled.Get("restored_bytes"), 0L);
            result["ok"] = Py.Truthy(rolled.Get("ok"));
            result["disk_changed"] = true;
            ClearPlanOf(chatId, turnId);
            MarkRewound(chatId, turnId);
            return result;
        }
        PyDict only = Result(true, true, false);
        only["reason"] = "会话日志缺失,无法定位该轮的磁盘改动;已只回退对话";
        return only;
    }

    /// <summary>
    /// 不需要存档时的等价操作：就地截断消息列表（回到最后一条非插队用户消息之前），
    /// 并把检查点里最后一轮的文件改动还原。返回删掉的消息条数。
    /// </summary>
    public static int TrimMessages(List<object> messages)
    {
        int cut = LastUserIndex(messages);
        if (cut < 0)
        {
            return 0;
        }
        int removed = messages.Count - cut;
        messages.RemoveRange(cut, removed);
        return removed;
    }

    /// <summary>最后一条「非插队」用户消息的下标（没有返回 -1）。</summary>
    public static int LastUserIndex(List<object> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i] is PyDict m && Py.Eq(m.Get("role"), "user") && !Store.IsSteerMessage(m))
            {
                return i;
            }
        }
        return -1;
    }
}
