// AI2-stability:检查点里的「本轮新建的子目录」操作(apply_instance_upgrade 原地升级用)。
//
// 原地升级会往实例的 versions/ 里装目标 MC 版本与加载器(几百 MB 的 jar + natives),而 versions/ 里已有的版本更大 ——
// 把整个 versions 目录塞进内容快照会撞 512MB 上限、整轮报「无法回退」。可撤回真正需要的只是:
// 「撤回时删掉这一轮**新建**的版本目录,原来就有的一个字节都不动」。所以这里不存内容,只记一份名单:
//   { parent: <versions 目录>, existing: [快照时已有的子目录名], match_mc: <目标 MC 版本> }
// 回滚时删掉 parent 下「不在 existing 里」且「属于 match_mc」的子目录 —— 属于 = 目录名就是 match_mc,
// 或目录里 <名>.json 的 id / inheritsFrom 是 match_mc(Fabric / Forge / NeoForge / Quilt 的版本 json 都继承原版)。
// 这样即使用户在这一轮之后自己又装了别的版本,撤回也不会误删它。
// 注意:安装是后台任务,快照时版本目录还不存在、撤回时它可能才装到一半 —— 删的是整个目录,半成品也一并清掉。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

public static partial class Checkpoint
{
    /// <summary>
    /// 记下 <paramref name="parentDir"/> 此刻已有的子目录;回滚这一轮时删掉之后新建、且属于 <paramref name="matchMc"/> 的子目录。
    /// <paramref name="matchMc"/> 为空 = 新建的子目录一律删。返回 <c>{ok, seq, existing, reason}</c>。
    /// </summary>
    public static PyDict SnapshotNewChildren(string chatId, string turnId, string parentDir, string matchMc)
    {
        string chat = string.IsNullOrEmpty(chatId) ? "active" : chatId;
        try
        {
            string parent = new PPath(parentDir).Resolve().ToString();
            List<string> existing = ChildDirNames(parent);
            lock (Gate)
            {
                List<object> journal = LoadJournal(chat);
                long seq = journal.Count;
                journal.Add(PyDict.Of(
                    "seq", seq,
                    "turn_id", turnId ?? "",
                    "ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    "files", new List<object>(),
                    "dirs", new List<object>(),
                    "known_dirs", new List<object>(),
                    "bytes", 0L,
                    "new_children", PyDict.Of(
                        "parent", parent,
                        "existing", existing.Cast<object>().ToList(),
                        "match_mc", Py.Strip(matchMc ?? ""))));
                SaveJournal(chat, journal);
                return PyDict.Of("ok", true, "seq", seq, "existing", (long)existing.Count, "reason", "");
            }
        }
        catch (Exception ex)
        {
            return PyDict.Of("ok", false, "seq", -1L, "existing", 0L, "reason", Py.ExcStr(ex));
        }
    }

    /// <summary>重放一条「新建子目录」操作:删掉新出现且属于目标版本的子目录;返回删除失败的条数。</summary>
    private static long ApplyNewChildren(PyDict op)
    {
        string parent = Py.S(op.Get("parent"));
        if (parent.Length == 0 || !Directory.Exists(Abs(parent)))
        {
            return 0;
        }
        var existing = new HashSet<string>(Py.ListOr(op.Get("existing")).Select(x => Py.S(x)), StringComparer.OrdinalIgnoreCase);
        string mc = Py.S(op.Get("match_mc"));
        // AI3-content:only_empty = 只删 names 里点名、本次新建、且此刻已经空了的目录(见 SnapshotNewDirs)
        bool onlyEmpty = Py.Truthy(op.Get("only_empty"));
        var names = new HashSet<string>(Py.ListOr(op.Get("names")).Select(x => Py.S(x)), StringComparer.OrdinalIgnoreCase);
        long failures = 0;
        foreach (string name in ChildDirNames(parent))
        {
            if (existing.Contains(name))
            {
                continue;
            }
            string dir = PPath.Join(parent, name);
            if (onlyEmpty)
            {
                if (!names.Contains(name))
                {
                    continue; // 没点名的不动(比如游戏自己后来建的)
                }
                if (!IsEmptyTree(Abs(dir)))
                {
                    // 里面还有文件(用户后来放的 / 没撤掉的):目录本身留着,只把它里面已经空了的子目录清掉;不算失败
                    PruneEmptySubdirs(Abs(dir));
                    continue;
                }
            }
            else if (!BelongsToMc(dir, name, mc))
            {
                continue;
            }
            if (!RemoveFile(dir))
            {
                failures++;
            }
        }
        return failures;
    }

    /// <summary>删掉目录里「整棵都是空目录」的子目录(深的先处理);链接不进也不删。</summary>
    private static void PruneEmptySubdirs(string abs)
    {
        try
        {
            foreach (string sub in Directory.EnumerateDirectories(abs).ToList())
            {
                if (IsSymlink(sub))
                {
                    continue;
                }
                if (IsEmptyTree(sub))
                {
                    Directory.Delete(sub, true);
                }
                else
                {
                    PruneEmptySubdirs(sub);
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }

    /// <summary>目录树里一个文件都没有(只有空子目录);本身是链接、或里面有链接时一律算「不空」(不去动它)。</summary>
    internal static bool IsEmptyTree(string abs)
    {
        try
        {
            if (!Directory.Exists(abs) || IsSymlink(abs))
            {
                return false;
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(abs, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(entry) || IsSymlink(entry))
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// AI3-content:记下 <paramref name="parentDir"/> 下「这一步可能会新建」的子目录名(<paramref name="names"/>,此刻已存在的自动去掉)。
    /// 撤回这一轮时,内容快照先把新建的文件删掉(它的 seq 更大,先重放),再轮到这一条:点名的目录若已经空了就整个删掉;
    /// 原来就有的目录、没点名的目录、里面还有文件的目录都不动。所以调用方要在 <see cref="Snapshot"/> 之前调。
    /// 返回 <c>{ok, seq, names, reason}</c>;没有要记的名字时 ok=true、seq=-1、不写流水。
    /// </summary>
    public static PyDict SnapshotNewDirs(string chatId, string turnId, string parentDir, IEnumerable<string> names)
    {
        string chat = string.IsNullOrEmpty(chatId) ? "active" : chatId;
        try
        {
            string parent = new PPath(parentDir).Resolve().ToString();
            var existingSet = new HashSet<string>(ChildDirNames(parent), StringComparer.OrdinalIgnoreCase);
            List<string> fresh = (names ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrEmpty(n) && n != "." && n != ".." && n.IndexOfAny(new[] { '/', '\\' }) < 0 && !existingSet.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fresh.Count == 0)
            {
                return PyDict.Of("ok", true, "seq", -1L, "names", 0L, "reason", "");
            }
            List<string> existing = existingSet.ToList();
            existing.Sort(StringComparer.Ordinal);
            lock (Gate)
            {
                List<object> journal = LoadJournal(chat);
                long seq = journal.Count;
                journal.Add(PyDict.Of(
                    "seq", seq,
                    "turn_id", turnId ?? "",
                    "ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    "files", new List<object>(),
                    "dirs", new List<object>(),
                    "known_dirs", new List<object>(),
                    "bytes", 0L,
                    "new_children", PyDict.Of(
                        "parent", parent,
                        "existing", existing.Cast<object>().ToList(),
                        "match_mc", "",
                        "only_empty", true,
                        "names", fresh.Cast<object>().ToList())));
                SaveJournal(chat, journal);
                return PyDict.Of("ok", true, "seq", seq, "names", (long)fresh.Count, "reason", "");
            }
        }
        catch (Exception ex)
        {
            return PyDict.Of("ok", false, "seq", -1L, "names", 0L, "reason", Py.ExcStr(ex));
        }
    }

    /// <summary>这个版本目录是不是目标 MC 版本(或继承它的加载器版本)。读不到 json 时只按目录名判断。</summary>
    internal static bool BelongsToMc(string dir, string name, string mc)
    {
        if (mc.Length == 0 || string.Equals(name, mc, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        try
        {
            string json = PPath.Join(dir, name + ".json");
            if (Utils.ReadJson(json) is PyDict vj)
            {
                return string.Equals(Py.S(vj.Get("id")), mc, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Py.S(vj.Get("inheritsFrom")), mc, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
        }
        return false;
    }

    private static List<string> ChildDirNames(string parent)
    {
        try
        {
            string abs = Abs(parent);
            if (!Directory.Exists(abs))
            {
                return new List<string>();
            }
            List<string> names = Directory.EnumerateDirectories(abs).Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }
}
