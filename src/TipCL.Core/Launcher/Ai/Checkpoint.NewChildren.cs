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
        long failures = 0;
        foreach (string name in ChildDirNames(parent))
        {
            if (existing.Contains(name))
            {
                continue;
            }
            string dir = PPath.Join(parent, name);
            if (!BelongsToMc(dir, name, mc))
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
