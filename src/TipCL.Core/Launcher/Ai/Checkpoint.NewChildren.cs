// AI2-stability:检查点里的「本轮新建的子目录」操作(apply_instance_upgrade 原地升级用)。
// AI3-stability:改为「升级前后目录集合的差集」—— 认得出 OptiFine / Forge 安装器等各种目录形态。
//
// 原地升级会往实例的 versions/ 里装目标 MC 版本与加载器(几百 MB 的 jar + natives),而 versions/ 里已有的版本更大 ——
// 把整个 versions 目录塞进内容快照会撞 512MB 上限、整轮报「无法回退」。可撤回真正需要的只是:
// 「撤回时删掉这一轮**新建**的版本目录,原来就有的一个字节都不动」。所以这里不存内容,只记名单:
//   { parent: <versions 目录>, existing: [工具执行前已有的子目录名], match_mc: <目标 MC 版本>,
//     created: [工具执行后多出来的子目录名](执行完才补上), pending_install: <安装是否转成了后台任务> }
//
// 判定口径(AI3-stability):
//   1. 主口径 = 差集。执行前记 existing,执行完(AiAgent 在工具返回后调 CaptureNewChildren)记 created = 之后 − 之前。
//      写工具永远单独成组执行,这段时间里新出现的目录就是这次升级装出来的 —— 不管目录叫什么、json 继不继承原版
//      (OptiFine 独立 json、改了 id 的整合目录、Forge 老安装器的各种命名都一样认得)。撤回只删 created 里还在的。
//      执行完之后用户自己再装的版本不在 created 里,撤回不碰。
//   2. 安装转成后台任务(install_game 回了 task_id,目录要等任务跑完才出现)时 pending_install=true:撤回额外删
//      「之后才出现、且属于目标版本」的目录(见 BelongsToMc)。
//   3. 没来得及补 created(执行中进程被杀 / 旧版流水):退回「新出现 + 属于目标版本」的口径。
// 「属于目标版本」(BelongsToMc):目录名就是 match_mc;或 <名>.json 的 id / inheritsFrom / jar / clientVersion /
// patches[game].version 是 match_mc;或 inheritsFrom 顺着兄弟目录一路追到 match_mc(OptiFine 装在 Forge 上);
// 或目录名 / id 里以完整版本号的形式含 match_mc(「1.21.1-OptiFine_HD_U_J1」算,「1.21.10」不算)。
// 注意:撤回时安装若还在跑,删的是整个目录,半成品也一并清掉。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher.Ai;

public static partial class Checkpoint
{
    /// <summary>
    /// 记下 <paramref name="parentDir"/> 此刻已有的子目录;回滚这一轮时删掉之后新建的子目录(口径见文件头)。
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

    /// <summary>
    /// AI3-stability:工具执行完后补记「这次新建了哪些子目录」(之后 − 之前的差集)。
    /// 找本轮、同一父目录、还没补记过的最后一条名单;<paramref name="pendingInstall"/> = 安装转成了后台任务。
    /// 返回 <c>{ok, seq, created:[...], reason}</c>;找不到名单时 ok=false(不影响别的检查点)。
    /// </summary>
    public static PyDict CaptureNewChildren(string chatId, string turnId, string parentDir, bool pendingInstall = false)
    {
        string chat = string.IsNullOrEmpty(chatId) ? "active" : chatId;
        try
        {
            string parent = new PPath(parentDir).Resolve().ToString();
            lock (Gate)
            {
                List<object> journal = LoadJournal(chat);
                for (int i = journal.Count - 1; i >= 0; i--)
                {
                    if (journal[i] is not PyDict op || op.Get("new_children") is not PyDict nc)
                    {
                        continue;
                    }
                    if (!Py.Eq(Py.S(op.Get("turn_id")), turnId ?? "") || nc.Get("created") != null
                        || !string.Equals(Py.S(nc.Get("parent")), parent, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var existing = new HashSet<string>(Py.ListOr(nc.Get("existing")).Select(x => Py.S(x)), StringComparer.OrdinalIgnoreCase);
                    List<string> created = ChildDirNames(parent).Where(n => !existing.Contains(n)).ToList();
                    nc["created"] = created.Cast<object>().ToList();
                    nc["pending_install"] = pendingInstall;
                    nc["captured_ts"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                    SaveJournal(chat, journal);
                    return PyDict.Of("ok", true, "seq", op.Get("seq"), "created", created.Cast<object>().ToList(), "reason", "");
                }
                return PyDict.Of("ok", false, "seq", -1L, "created", new List<object>(), "reason", "no pending new_children op");
            }
        }
        catch (Exception ex)
        {
            return PyDict.Of("ok", false, "seq", -1L, "created", new List<object>(), "reason", Py.ExcStr(ex));
        }
    }

    /// <summary>重放一条「新建子目录」操作:按文件头的三种口径删掉本次新建的子目录;返回删除失败的条数。</summary>
    private static long ApplyNewChildren(PyDict op)
    {
        string parent = Py.S(op.Get("parent"));
        if (parent.Length == 0 || !Directory.Exists(Abs(parent)))
        {
            return 0;
        }
        var existing = new HashSet<string>(Py.ListOr(op.Get("existing")).Select(x => Py.S(x)), StringComparer.OrdinalIgnoreCase);
        string mc = Py.S(op.Get("match_mc"));
        bool captured = op.Get("created") is List<object>;
        var created = new HashSet<string>(Py.ListOr(op.Get("created")).Select(x => Py.S(x)), StringComparer.OrdinalIgnoreCase);
        bool pending = Py.Truthy(op.Get("pending_install"));
        long failures = 0;
        foreach (string name in ChildDirNames(parent))
        {
            if (existing.Contains(name))
            {
                continue;
            }
            string dir = PPath.Join(parent, name);
            bool remove = captured
                ? created.Contains(name) || (pending && BelongsToMc(dir, name, mc))
                : BelongsToMc(dir, name, mc);
            if (remove && !RemoveFile(dir))
            {
                failures++;
            }
        }
        return failures;
    }

    /// <summary>
    /// 这个版本目录是不是目标 MC 版本(或基于它的加载器 / OptiFine 版本)。口径见文件头;读不到 json 时只按名字判断。
    /// </summary>
    internal static bool BelongsToMc(string dir, string name, string mc)
    {
        if (mc.Length == 0 || string.Equals(name, mc, StringComparison.OrdinalIgnoreCase) || HasVersionToken(name, mc))
        {
            return true;
        }
        string parent = Path.GetDirectoryName(Path.GetFullPath(Abs(dir))) ?? "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string cur = name;
        string curDir = dir;
        // 顺着 inheritsFrom 追兄弟目录(OptiFine → Forge → 原版),最多 6 层,防环
        for (int depth = 0; depth < 6 && seen.Add(cur); depth++)
        {
            PyDict? vj = ReadVersionJson(curDir, cur);
            if (vj == null)
            {
                return false;
            }
            if (JsonNamesMc(vj, mc))
            {
                return true;
            }
            string next = Py.S(vj.Get("inheritsFrom"));
            if (next.Length == 0 || parent.Length == 0)
            {
                return false;
            }
            if (string.Equals(next, mc, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            cur = next;
            curDir = PPath.Join(parent, next);
        }
        return false;
    }

    private static PyDict? ReadVersionJson(string dir, string name)
    {
        try
        {
            string json = PPath.Join(dir, name + ".json");
            if (Utils.ReadJson(json) is PyDict vj)
            {
                return vj;
            }
            // 有的启动器改了目录名但没改 json 名:目录里只有一个 json 时也认
            string abs = Abs(dir);
            if (Directory.Exists(abs))
            {
                string[] jsons = Directory.GetFiles(abs, "*.json", SearchOption.TopDirectoryOnly);
                if (jsons.Length == 1 && Utils.ReadJson(jsons[0]) is PyDict only)
                {
                    return only;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    /// <summary>版本 json 自己有没有写明基于哪个 MC 版本。</summary>
    private static bool JsonNamesMc(PyDict vj, string mc)
    {
        bool Is(object? v) => string.Equals(Py.S(v), mc, StringComparison.OrdinalIgnoreCase);
        if (Is(vj.Get("id")) || Is(vj.Get("inheritsFrom")) || Is(vj.Get("jar")) || Is(vj.Get("clientVersion"))
            || HasVersionToken(Py.S(vj.Get("id")), mc))
        {
            return true;
        }
        // HMCL 一类的合并 json:patches[] 里 id=game 的那一项写着原版版本号
        foreach (object raw in Py.ListOr(vj.Get("patches")))
        {
            if (raw is PyDict p && Py.Eq(Py.S(p.Get("id")), "game") && Is(p.Get("version")))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>名字里是否以完整版本号的形式含 <paramref name="mc"/>(前后不能再接数字或「.数字」)。</summary>
    internal static bool HasVersionToken(string text, string mc)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(mc))
        {
            return false;
        }
        return Regex.IsMatch(text, @"(?<![0-9.])" + Regex.Escape(mc) + @"(?![0-9]|\.[0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
