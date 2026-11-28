// M4-H P5:AI 的长期记忆(照 Windows 版 Launcher/Ai/Memory 的 remember/forget 工具语义)。
//
// AI5-B1:原先记忆分成两份 —— 助手侧(AiAgent.SystemMessage / ToolRunner 的 remember / forget)
// 读写 `$TIPCL_HOME/ai-memory.json`,界面侧(记忆面板 AiMemoryPanel、设置页 SettingsPage.Cards)
// 读写 `$TIPCL_HOME/ai_memory.md`(Core 的 FileMemory),两边零交叉调用 ⇒「助手记的事面板看不见、
// 面板删掉的条目助手还记着」。现在**只留一份**,以 FileMemory 为准(ai_memory.md,一行一条
// `- 日期 内容`,玩家可直接用记事本改);本类退化成 FileMemory 的门面,调用点不用改,
// 也就不会再出现「同一件事两个真源」。
//
// 迁移:第一次访问时把旧的 `ai-memory.json`(形状 `{"items":[...]}`)**幂等**迁进 ai_memory.md,
// 逐条走 FileMemory.Remember(它自带「同样内容已经记过就不重复记」的去重),迁完把原文件改名成
// `ai-memory.json.migrated` 留档。改名这一步不能省:原文件留着的话,面板里删掉的记忆会在下次启动
// 被再迁一遍(复活);万一改名失败(只读盘之类),去重仍然保证不出现重复条目 —— 迁移本身是幂等的。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>AI 记住的长期事实(用户偏好、常用实例、踩过的坑)。落盘见 <see cref="FileMemory"/>。</summary>
public static class AiMemory
{
    /// <summary>上限:与 FileMemory 一致(超过就丢最旧的,避免记忆无限膨胀进上下文)。</summary>
    public const int MaxItems = FileMemory.MaxEntries;

    /// <summary>旧文件迁完之后的留档后缀(AI5-B1)。</summary>
    public const string MigratedSuffix = ".migrated";

    /// <summary>旧记忆文件名(AI5-B1 之前助手侧写的那一份)。</summary>
    public const string LegacyFileName = "ai-memory.json";

    private static readonly object Gate = new();

    /// <summary>记忆落盘的文件 —— 就是界面记忆面板操作的那一份(AI5-B1:一份真源)。</summary>
    public static string FilePath() => FileMemory.FilePath;

    /// <summary>旧的 JSON 记忆文件(还在的话;用于迁移与诊断)。</summary>
    public static List<string> LegacyFiles()
    {
        var found = new List<string>();
        foreach (string dir in new[] { Utils.ROOT, Path.GetDirectoryName(FilePath()) ?? "" })
        {
            if (dir.Length == 0)
            {
                continue;
            }
            string path = Path.Combine(PPath.Abs(new PPath(dir).ToString()), LegacyFileName);
            if (File.Exists(path) && !found.Contains(path, StringComparer.Ordinal))
            {
                found.Add(path);
            }
        }
        return found;
    }

    /// <summary>
    /// 最近一次**真的执行了**迁移时,因为 <see cref="FileMemory.MaxEntries"/> 条上限被裁掉的条数
    /// (裁剪方向与 FileMemory 一致:从最旧的开始丢)。没执行过迁移时保持 0。
    /// 这是给排障/用户看的诊断量:迁移本身不销毁任何东西(旧文件被改名留档),但超出上限的条目
    /// 不会进 ai_memory.md,用户需要知道「这次迁丢了几条」才知道去留档文件里找。
    /// </summary>
    public static int LastMigrationEvicted { get; private set; }

    /// <summary>
    /// 把旧的 <c>ai-memory.json</c> 幂等迁进 <c>ai_memory.md</c>;返回这次真的新增的条数。
    /// 每次访问都查一遍(只是两三次 File.Exists):**不缓存「已经查过」** —— 缓存住的话,
    /// 测试/用户在同一次运行里新放下的旧文件永远不会被迁(用例要能验这条路径)。
    /// 幂等性不靠缓存:条目重复靠 FileMemory.Remember 的去重挡住,文件迁完会改名。
    /// 迁移若触发了上限裁剪,会往 <see cref="Trace"/> 记一条 <c>ai_memory_migrate</c>(带 imported / evicted / cap);
    /// 诊断写失败不影响对话(Trace.Record 自己吞异常,这里再包一层)。
    /// </summary>
    public static int MigrateLegacy()
    {
        lock (Gate)
        {
            List<string> files = LegacyFiles();
            if (files.Count == 0)
            {
                return 0;
            }
            int before = FileMemory.Entries().Count;
            int added = 0;
            foreach (string file in files)
            {
                foreach (string item in ReadLegacy(file))
                {
                    string result = FileMemory.Remember(item);
                    if (result.StartsWith("已记住:", StringComparison.Ordinal))
                    {
                        added++;
                    }
                }
                try
                {
                    // 关键:只改名,不删除。超出上限的条目在 .migrated 里原样留着,用户可人工找回。
                    File.Move(file, file + MigratedSuffix, overwrite: true);
                }
                catch (Exception)
                {
                    // 留档失败不该影响对话:条目已经进 ai_memory.md,下次启动去重后会原样跳过
                }
            }
            LastMigrationEvicted = Math.Max(0, before + added - FileMemory.Entries().Count);
            try
            {
                Trace.Record("ai_memory_migrate", null, "", "", null, null, null,
                    "files", (long)files.Count,
                    "imported", (long)added,
                    "evicted", (long)LastMigrationEvicted,
                    "cap", (long)FileMemory.MaxEntries);
            }
            catch (Exception)
            {
                // 诊断不能拖垮记忆读写
            }
            return added;
        }
    }

    /// <summary>全部记忆(顺序即写入顺序;不含 FileMemory 记的日期前缀)。</summary>
    public static List<string> All()
    {
        MigrateLegacy();
        return FileMemory.Entries()
            .Select(FileMemory.TextOf)
            .Where(x => x.Length > 0)
            .ToList();
    }

    /// <summary>记一条;重复内容不重复记(判据沿用 FileMemory.Remember)。返回是否真的新增。</summary>
    public static bool Add(string text)
    {
        MigrateLegacy();
        return FileMemory.Remember(text).StartsWith("已记住:", StringComparison.Ordinal);
    }

    /// <summary>按关键词忘掉(包含匹配,大小写不敏感);返回忘掉的条数。</summary>
    public static int Forget(string keyword)
    {
        MigrateLegacy();
        int before = FileMemory.Entries().Count;
        FileMemory.Forget(keyword);
        int after = FileMemory.Entries().Count;
        return Math.Max(0, before - after);
    }

    /// <summary>清空(测试与「重置」用)。</summary>
    public static void Clear()
    {
        MigrateLegacy();
        FileMemory.Clear();
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

    /// <summary>读旧 JSON 文件里的条目;文件坏了当空(不拖垮对话)。</summary>
    private static List<string> ReadLegacy(string path)
    {
        try
        {
            object parsed = PyJson.Loads(File.ReadAllText(path));
            return Py.ListOr(Py.D(parsed).Get("items"))
                .Select(Py.Str)
                .Select(x => x.Replace("\r", " ").Replace("\n", " ").Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }
}
