// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/Ai/Preview.cs(M4-H R5 搬运)。
//
// 权限确认卡的「改前改后」预览:在写操作真正执行之前,把它会改到的东西算成给用户看的行
// (逐行 diff / 删除清单 / 安装目标),这样「点允许」之前就能看清将要发生什么。
//
// 与参考版的差别(见 DECISIONS D-31):
//   1. 没有 BackendApi:实例目录按 Linux 既有的一条解析(Instances.GetInstancePath,
//      与 Tools.AffectedPaths 同源),参数里的 instance 优先,其次执行器给的当前实例;
//   2. 返回结构键名(kind/head/lines/too_large)与参考版逐字一致,两边共用同一套判断;
//   3. 返回 null 表示「这个工具没有可预览的改动」,界面据此不显示预览框。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>写操作的改前改后预览(照 Windows 版 Preview;纯计算,不动机器状态)。</summary>
public static class Preview
{
    /// <summary>逐行 diff 最多给这么多行,再多就落盘 + 只显示前 N 行(照参考版 200)。</summary>
    public const int MAX_PREVIEW_LINES = 200;

    private static string FmtBytes(long n)
    {
        if (n >= 1048576)
        {
            return Py.RoundHalfEven((double)n / 1024.0 / 1024.0, 1) + " MB";
        }
        if (n >= 1024)
        {
            return Py.RoundHalfEven((double)n / 1024.0, 1) + " KB";
        }
        return n + " B";
    }

    /// <summary>这次调用落在哪个实例目录(解析失败返回 null = 不预览)。</summary>
    private static string? InstanceDir(PyDict args, string instance)
    {
        try
        {
            string name = Py.S(Py.Or(args.Get("instance"), instance));
            if (name.Length == 0)
            {
                name = Py.S(Config.CONFIG.Get("default_instance", "default"));
            }
            return Instances.GetInstancePath(name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>write_mod_config 的目标文件:只允许落在该实例的 config/ 下(与 ToolRunner 的白名单同规矩)。</summary>
    private static string? ConfigTarget(string? instDir, PyDict args)
    {
        if (instDir == null)
        {
            return null;
        }
        string rel = Py.LStrip(Py.S(args.Get("path")).Replace("\\", "/"), "/");
        if (rel.Length == 0 || new PPath(rel).Parts.Contains(".."))
        {
            return null;
        }
        return PPath.Join(instDir, "config", rel);
    }

    /// <summary>目录统计(文件数 + 总字节);超过上限就标记截断,避免对着巨大的实例目录数到天黑。</summary>
    private static (long N, long Total, bool Truncated) DirStats(string path, int cap = 5000)
    {
        long n = 0;
        long total = 0;
        bool truncated = false;
        try
        {
            foreach (string file in LocalFs.RglobFiles(path))
            {
                n++;
                try
                {
                    total += new FileInfo(PPath.Abs(file)).Length;
                }
                catch (Exception)
                {
                }
                if (n >= cap)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (Exception)
        {
        }
        return (n, total, truncated);
    }

    private static PyDict Row(string kind, string head, List<string> lines, bool tooLarge)
        => PyDict.Of("kind", kind, "head", head, "lines", lines.Cast<object>().ToList(), "too_large", tooLarge);

    /// <summary>
    /// 算出这次写操作「将要发生什么」的预览。返回 null = 认不出的工具 / 参数不足以定位目标。
    /// <paramref name="instance"/> 是执行器的当前实例(参数里没写 instance 时用它)。
    /// </summary>
    public static PyDict? ChangePreview(string name, PyDict? args, string instance = "")
    {
        args ??= new PyDict();
        var lines = new List<string>();
        switch (name)
        {
            case "write_mod_config":
            {
                string head = Tr("将要写入的变更:");
                string? target = ConfigTarget(InstanceDir(args, instance), args);
                if (target == null)
                {
                    return null;
                }
                string old = "";
                bool existed = LocalFs.IsFile(target);
                if (existed)
                {
                    try
                    {
                        old = AiFs.ReadText(target, replace: true);
                    }
                    catch (Exception)
                    {
                        old = "";
                    }
                }
                string content = Py.S(args.Get("content"));
                string label = Py.Str(Py.Or(args.Get("path"), "config"));
                List<string> diff = UnifiedDiff(Py.SplitLines(old), Py.SplitLines(content), "a/" + label, "b/" + label);
                if (!existed)
                {
                    lines.Add(Tr("(新建文件)"));
                }
                bool tooLarge = false;
                if (diff.Count > MAX_PREVIEW_LINES)
                {
                    tooLarge = true;
                    // Linux 侧 Artifacts.Store 返回的是「回给模型的那段话」,拿路径要用 Save(参考版等同 Store 的路径返回值)
                    string stored = Artifacts.Save(string.Join("\n", diff), name);
                    lines.AddRange(diff.Take(MAX_PREVIEW_LINES));
                    if (stored.Length > 0)
                    {
                        lines.Add(Tr("变更过大,完整内容见 {path}").Replace("{path}", stored));
                    }
                    else
                    {
                        lines.Add(Tr("变更过大,仅显示前 {n} 行").Replace("{n}", MAX_PREVIEW_LINES.ToString()));
                    }
                }
                else
                {
                    lines.AddRange(diff);
                }
                return Row("diff", head, lines, tooLarge);
            }

            case "delete_mod":
            case "disable_mod":
            case "enable_mod":
            {
                string? instDir = InstanceDir(args, instance);
                if (instDir == null)
                {
                    return null;
                }
                string file = Py.S(args.Get("filename"));
                string mods = PPath.Join(instDir, "mods");
                string head;
                List<string> targets;
                if (name == "delete_mod")
                {
                    head = Tr("将被删除:");
                    targets = new List<string> { PPath.Join(mods, file), PPath.Join(mods, file + ".disabled") };
                }
                else if (name == "disable_mod")
                {
                    head = Tr("将被改名:");
                    targets = new List<string> { PPath.Join(mods, file) };
                }
                else
                {
                    head = Tr("将被改名:");
                    targets = new List<string> { PPath.Join(mods, file + ".disabled") };
                }
                long total = 0;
                long count = 0;
                foreach (string target in targets)
                {
                    if (!LocalFs.IsFile(target))
                    {
                        continue;
                    }
                    long size;
                    try
                    {
                        size = LocalFs.Size(target);
                    }
                    catch (Exception)
                    {
                        size = 0;
                    }
                    total += size;
                    count++;
                    string baseName = new PPath(target).Name;
                    if (name == "delete_mod")
                    {
                        lines.Add("- mods/" + baseName + "(" + FmtBytes(size) + ")");
                    }
                    else
                    {
                        string renamed = name == "disable_mod"
                            ? baseName + ".disabled"
                            : (baseName.EndsWith(".disabled", StringComparison.Ordinal)
                                ? baseName.Substring(0, baseName.Length - 9)
                                : baseName);
                        lines.Add("- mods/" + baseName + " → mods/" + renamed);
                    }
                }
                if (count == 0)
                {
                    lines.Add(Tr("(没找到这个文件,执行时可能报「不存在」)"));
                }
                else
                {
                    lines.Add(Tr("共 {n} 个文件,{size}").Replace("{n}", count.ToString()).Replace("{size}", FmtBytes(total)));
                }
                return Row("summary", head, lines, tooLarge: false);
            }

            case "delete_instance":
            {
                string? instDir = InstanceDir(PyDict.Of("instance", args.Get("name")), instance);
                if (instDir == null)
                {
                    return null;
                }
                var (n, total, truncated) = DirStats(instDir);
                lines.Add("- " + new PPath(instDir).Name + "/");
                lines.Add(Tr("共 {n} 个文件,{size}(不可恢复)")
                    .Replace("{n}", (truncated ? ">" : "") + n)
                    .Replace("{size}", FmtBytes(total)));
                return Row("summary", Tr("将被删除:"), lines, tooLarge: false);
            }

            default:
            {
                if (InstallTargets.Contains(name))
                {
                    string head = Tr("安装目标:");
                    string? instDir = InstanceDir(args, instance);
                    string? sub = name switch
                    {
                        "install_world" => "saves",
                        "install_datapack" => "datapacks",
                        "install_resourcepack" => "resourcepacks",
                        "install_shader" => "shaderpacks",
                        "install_mod" => "mods",
                        _ => null,
                    };
                    if (name == "download_java")
                    {
                        lines.Add(Tr("Java 运行时目录(由启动器管理)"));
                        lines.Add(Tr("预计新增文件数:多个(安装时确定)"));
                    }
                    else
                    {
                        if (instDir == null)
                        {
                            return null;
                        }
                        lines.Add(sub != null ? PPath.Join(instDir, sub) : instDir);
                        switch (name)
                        {
                            case "install_mod":
                            case "install_shader":
                            case "install_resourcepack":
                            case "install_datapack":
                                lines.Add(Tr("预计新增文件数:{n}").Replace("{n}", "1"));
                                break;
                            case "install_world":
                                lines.Add(Tr("预计新增文件数:存档目录(含 level.dat 等多个文件)"));
                                break;
                            default:
                                lines.Add(Tr("预计新增文件数:安装时确定(多于 1 个)"));
                                break;
                        }
                    }
                    return Row("summary", head, lines, tooLarge: false);
                }

                if (name == "create_instance")
                {
                    string head = Tr("安装目标:");
                    string? instDir = InstanceDir(new PyDict(), instance);
                    if (instDir != null)
                    {
                        lines.Add(PPath.Join(new PPath(instDir).Parent.ToString(), Py.S(args.Get("name"))));
                    }
                    lines.Add(Tr("预计新增文件数:安装时确定(多于 1 个)"));
                    return Row("summary", head, lines, tooLarge: false);
                }

                if (name == "launch_game")
                {
                    string head = Tr("启动目标:");
                    string inst = Py.S(args.Get("instance"));
                    string version = Py.S(args.Get("version"));
                    if (inst.Length > 0)
                    {
                        lines.Add(Tr("实例:{name}").Replace("{name}", inst));
                    }
                    if (version.Length > 0)
                    {
                        lines.Add(Tr("版本:{ver}").Replace("{ver}", version));
                    }
                    return Row("info", head, lines, tooLarge: false);
                }
                return null;
            }
        }
    }

    /// <summary>会写盘、因而值得给预览的工具名(照参考版的分支)。</summary>
    private static readonly HashSet<string> InstallTargets = new(StringComparer.Ordinal)
    {
        "install_mod",
        "install_modpack",
        "install_shader",
        "install_resourcepack",
        "install_datapack",
        "install_world",
        "install_game",
        "download_java",
    };

    /// <summary>
    /// 把预览字典摊平成确认卡要显示的行(照 Windows 版 ShowConfirm:head 以「@@ 」开头 + 丢弃空行)。
    /// 两个页面的确认回调都只经这一处,避免再出现「界面支持预览、调用点却传空列表」的断链。
    /// </summary>
    public static List<string> Flatten(PyDict? preview)
    {
        var lines = new List<string>();
        if (preview == null)
        {
            return lines;
        }
        string head = Py.S(preview.Get("head"));
        if (head.Length > 0)
        {
            lines.Add(head.StartsWith("@@", StringComparison.Ordinal) ? head : "@@ " + head);
        }
        foreach (object? raw in Py.ListOr(preview.Get("lines")))
        {
            string line = Py.S(raw);
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    /// <summary>取整块匹配(Python difflib.SequenceMatcher.get_matching_blocks 的等价实现)。</summary>
    private static List<int[]> MatchingBlocks(List<string> a, List<string> b)
    {
        int n = a.Count;
        int m = b.Count;
        var b2j = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int j = 0; j < m; j++)
        {
            if (!b2j.TryGetValue(b[j], out List<int>? idx))
            {
                idx = b2j[b[j]] = new List<int>();
            }
            idx.Add(j);
        }
        if (m >= 200)
        {
            // 热门行(占比超过 1%)不进索引:与 difflib 的 autojunk 同一策略,避免 O(n·m) 退化
            int ntest = m / 100 + 1;
            foreach (string key in b2j.Where(kv => kv.Value.Count > ntest).Select(kv => kv.Key).ToList())
            {
                b2j.Remove(key);
            }
        }
        var empty = new List<int>();
        var queue = new List<int[]> { new int[4] { 0, n, 0, m } };
        var blocks = new List<int[]>();
        while (queue.Count > 0)
        {
            int[] range = queue[queue.Count - 1];
            queue.RemoveAt(queue.Count - 1);
            int alo = range[0];
            int ahi = range[1];
            int blo = range[2];
            int bhi = range[3];
            int besti = alo;
            int bestj = blo;
            int bestsize = 0;
            var j2len = new Dictionary<int, int>();
            for (int i = alo; i < ahi; i++)
            {
                var newj2len = new Dictionary<int, int>();
                List<int> js = b2j.TryGetValue(a[i], out List<int>? found) ? found : empty;
                foreach (int j in js)
                {
                    if (j < blo)
                    {
                        continue;
                    }
                    if (j >= bhi)
                    {
                        break;
                    }
                    j2len.TryGetValue(j - 1, out int prev);
                    int k = newj2len[j] = prev + 1;
                    if (k > bestsize)
                    {
                        besti = i - k + 1;
                        bestj = j - k + 1;
                        bestsize = k;
                    }
                }
                j2len = newj2len;
            }
            while (besti > alo && bestj > blo && a[besti - 1] == b[bestj - 1])
            {
                besti--;
                bestj--;
                bestsize++;
            }
            while (besti + bestsize < ahi && bestj + bestsize < bhi && a[besti + bestsize] == b[bestj + bestsize])
            {
                bestsize++;
            }
            if (bestsize > 0)
            {
                blocks.Add(new int[3] { besti, bestj, bestsize });
                if (alo < besti && blo < bestj)
                {
                    queue.Add(new int[4] { alo, besti, blo, bestj });
                }
                if (besti + bestsize < ahi && bestj + bestsize < bhi)
                {
                    queue.Add(new int[4] { besti + bestsize, ahi, bestj + bestsize, bhi });
                }
            }
        }
        blocks.Sort((x, y) =>
        {
            if (x[0] != y[0])
            {
                return x[0].CompareTo(y[0]);
            }
            return x[1] != y[1] ? x[1].CompareTo(y[1]) : x[2].CompareTo(y[2]);
        });
        // 相邻块合并(difflib 的同一段处理)
        int i1 = 0;
        int j1 = 0;
        int k1 = 0;
        var merged = new List<int[]>();
        foreach (int[] block in blocks)
        {
            if (i1 + k1 == block[0] && j1 + k1 == block[1])
            {
                k1 += block[2];
                continue;
            }
            if (k1 > 0)
            {
                merged.Add(new int[3] { i1, j1, k1 });
            }
            i1 = block[0];
            j1 = block[1];
            k1 = block[2];
        }
        if (k1 > 0)
        {
            merged.Add(new int[3] { i1, j1, k1 });
        }
        merged.Add(new int[3] { n, m, 0 });
        return merged;
    }

    /// <summary>把匹配块展开成 opcode(equal / replace / delete / insert)。</summary>
    private static List<(string Tag, int i1, int i2, int j1, int j2)> Opcodes(List<string> a, List<string> b)
    {
        int i = 0;
        int j = 0;
        var ops = new List<(string, int, int, int, int)>();
        foreach (int[] block in MatchingBlocks(a, b))
        {
            int ai = block[0];
            int bj = block[1];
            int size = block[2];
            string tag = i < ai && j < bj ? "replace" : (i < ai ? "delete" : (j < bj ? "insert" : ""));
            if (tag.Length > 0)
            {
                ops.Add((tag, i, ai, j, bj));
            }
            i = ai + size;
            j = bj + size;
            if (size > 0)
            {
                ops.Add(("equal", ai, i, bj, j));
            }
        }
        return ops;
    }

    /// <summary>unified diff 的区间写法(与 difflib._format_range_unified 一致)。</summary>
    private static string Range(int start, int stop)
    {
        int beginning = start + 1;
        int length = stop - start;
        if (length == 0)
        {
            beginning--;
        }
        return length == 1 ? beginning.ToString() : beginning + "," + length;
    }

    /// <summary>逐行 unified diff(照参考版 Preview.UnifiedDiff;格式与 Python difflib 一致,供 LooksLikeDiff 识别)。</summary>
    public static List<string> UnifiedDiff(List<string> a, List<string> b, string fromfile, string tofile, int n = 3)
    {
        List<(string, int, int, int, int)> groups = Opcodes(a, b);
        if (groups.Count == 0)
        {
            groups.Add(("equal", 0, 1, 0, 1));
        }
        if (groups[0].Item1 == "equal")
        {
            var first = groups[0];
            groups[0] = (first.Item1, Math.Max(first.Item2, first.Item3 - n), first.Item3, Math.Max(first.Item4, first.Item5 - n), first.Item5);
        }
        if (groups[groups.Count - 1].Item1 == "equal")
        {
            var last = groups[groups.Count - 1];
            groups[groups.Count - 1] = (last.Item1, last.Item2, Math.Min(last.Item3, last.Item2 + n), last.Item4, Math.Min(last.Item5, last.Item4 + n));
        }
        int nn = n + n;
        var hunks = new List<List<(string, int, int, int, int)>>();
        var hunk = new List<(string, int, int, int, int)>();
        foreach (var group in groups)
        {
            var (tag, i1, i2, j1, j2) = group;
            if (tag == "equal" && i2 - i1 > nn)
            {
                hunk.Add((tag, i1, Math.Min(i2, i1 + n), j1, Math.Min(j2, j1 + n)));
                hunks.Add(hunk);
                hunk = new List<(string, int, int, int, int)>();
                i1 = Math.Max(i1, i2 - n);
                j1 = Math.Max(j1, j2 - n);
            }
            hunk.Add((tag, i1, i2, j1, j2));
        }
        if (hunk.Count > 0 && (hunk.Count != 1 || hunk[0].Item1 != "equal"))
        {
            hunks.Add(hunk);
        }
        var lines = new List<string>();
        bool headerDone = false;
        foreach (List<(string, int, int, int, int)> item in hunks)
        {
            if (!headerDone)
            {
                headerDone = true;
                lines.Add("--- " + fromfile);
                lines.Add("+++ " + tofile);
            }
            lines.Add("@@ -" + Range(item[0].Item2, item[item.Count - 1].Item3) + " +" + Range(item[0].Item4, item[item.Count - 1].Item5) + " @@");
            foreach (var (tag, i1, i2, j1, j2) in item)
            {
                if (tag == "equal")
                {
                    for (int i = i1; i < i2; i++)
                    {
                        lines.Add(" " + a[i]);
                    }
                    continue;
                }
                if (tag is "replace" or "delete")
                {
                    for (int i = i1; i < i2; i++)
                    {
                        lines.Add("-" + a[i]);
                    }
                }
                if (tag is "replace" or "insert")
                {
                    for (int j = j1; j < j2; j++)
                    {
                        lines.Add("+" + b[j]);
                    }
                }
            }
        }
        return lines;
    }

    /// <summary>界面文案(与参考版一样走翻译表;没有该语言的表时原样返回)。</summary>
    private static string Tr(string key) => I18nCore.Tr(key);
}
