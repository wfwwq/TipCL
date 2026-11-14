// M4-H P5:AI 的长期记忆(照 Windows 版 Launcher/Ai/Memory 的 remember/forget 工具语义)。
//
// 落盘位置 `$TIPCL_HOME/ai-memory.json`,形状 `{"items":["...","..."]}`(与 Windows 版
// FileMemory 的存储约定一致,便于两边共用同一份记忆)。纯文件读写,无平台依赖。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>AI 记住的长期事实(用户偏好、常用实例、踩过的坑)。</summary>
public static class AiMemory
{
    /// <summary>上限:超过就丢最旧的(避免记忆无限膨胀进上下文)。</summary>
    public const int MaxItems = 200;

    private static readonly object Gate = new();

    public static string FilePath() => PPath.Join(Utils.ROOT, "ai-memory.json");

    /// <summary>全部记忆(顺序即写入顺序)。</summary>
    public static List<string> All()
    {
        lock (Gate)
        {
            return Load().ToList();
        }
    }

    /// <summary>记一条;重复内容不重复记(按去空白后的全等判断)。</summary>
    public static bool Add(string text)
    {
        string item = Py.Strip(text ?? "");
        if (item.Length == 0)
        {
            return false;
        }
        lock (Gate)
        {
            var items = Load().ToList();
            if (items.Any(x => string.Equals(x.Trim(), item, StringComparison.Ordinal)))
            {
                return false;
            }
            items.Add(item);
            if (items.Count > MaxItems)
            {
                items = items.Skip(items.Count - MaxItems).ToList();
            }
            Save(items);
            return true;
        }
    }

    /// <summary>按关键词忘掉(包含匹配,大小写不敏感);返回忘掉的条数。</summary>
    public static int Forget(string keyword)
    {
        string key = Py.Strip(keyword ?? "");
        if (key.Length == 0)
        {
            return 0;
        }
        lock (Gate)
        {
            var items = Load().ToList();
            int before = items.Count;
            items = items.Where(x => x.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0).ToList();
            int removed = before - items.Count;
            if (removed > 0)
            {
                Save(items);
            }
            return removed;
        }
    }

    /// <summary>清空(测试与「重置」用)。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Save(new List<string>());
        }
    }

    /// <summary>拼成给模型看的上下文片段(没有记忆返回空串)。</summary>
    public static string Context()
    {
        List<string> items = All();
        if (items.Count == 0)
        {
            return "";
        }
        return "# 你之前记住的事\n" + string.Join("\n", items.Select(x => "- " + x));
    }

    private static IEnumerable<string> Load()
    {
        string path = PPath.Abs(FilePath());
        if (!System.IO.File.Exists(path))
        {
            return Array.Empty<string>();
        }
        try
        {
            object parsed = PyJson.Loads(System.IO.File.ReadAllText(path));
            object raw = Py.D(parsed).Get("items");
            return Py.ListOr(raw).Select(Py.Str).Where(x => x.Trim().Length > 0).ToList();
        }
        catch (Exception)
        {
            // 记忆文件坏了不该影响对话:当空处理,下一次写入会覆盖成合法 JSON
            return Array.Empty<string>();
        }
    }

    private static void Save(List<string> items)
    {
        string path = PPath.Abs(FilePath());
        System.IO.Directory.CreateDirectory(PPath.Abs(new PPath(path).Parent.ToString()));
        System.IO.File.WriteAllText(path, PyJson.Dumps(PyDict.Of("items", items.Cast<object>().ToList())));
    }
}
