// M4-H（AI 服务层）：文件检查点与回滚（照 Windows 版 Launcher/Ai/Checkpoint.cs 搬运）。
//
// 目的:AI 要改玩家的游戏目录（改配置、装模组、删文件…）时，先按「受影响路径」做一份内容寻址的快照，
// 万一改坏了能整体退回。落盘结构（与参考版一致）:
//
//   $TIPCL_HOME/cache/ai_checkpoints/<chat_id>/journal.json   操作流水（每轮一条:seq/turn_id/files/dirs/bytes）
//   $TIPCL_HOME/cache/ai_checkpoints/<chat_id>/files/<sha256>.bin   内容块（同一内容只存一份）
//
// 回滚语义与参考版一致:按 seq 逆序重放目标轮次的操作，先删掉「这轮新增但当时不存在」的文件（sweep），
// 再把每个文件恢复到当时的内容/mtime；恢复失败的条数记在 failures 里（不静默当成功）。
// 上限也照抄:单轮快照 512MB、单会话流水 1GB、单目录 20000 个文件（超了就明确拒绝这一轮，而不是半个快照）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>文件检查点：快照、回滚、流水回收。</summary>
public static class Checkpoint
{
    /// <summary>测试可指向临时目录；空 = 用默认位置。</summary>
    public static string? CheckpointsDir;

    private static readonly object Gate = new();

    /// <summary>最多保留多少个对话的检查点目录（更旧的整目录删掉）。</summary>
    public const long KeepChats = 20;

    /// <summary>单轮快照的体积上限。</summary>
    public const long MaxSnapshotBytes = 536870912;

    /// <summary>单会话流水的上限。</summary>
    public const long MaxChatBytes = 1073741824;

    /// <summary>单个目录最多扫多少文件（超了就拒绝这一轮）。</summary>
    public const int MaxDirFiles = 20000;

    private static string Root()
    {
        string dir = CheckpointsDir ?? PPath.Join(Utils.ROOT, "cache", "ai_checkpoints");
        Utils.EnsureDir(dir);
        return dir;
    }

    /// <summary>chat_id 做文件名安全化（照参考版:只留字母数字 - _，非 ASCII 字母也算）。</summary>
    private static string ChatDir(string chatId)
    {
        string raw = Py.Truthy(chatId) ? chatId : "active";
        var safe = new System.Text.StringBuilder();
        foreach (int cp in PyDifflib.CodePoints(raw))
        {
            bool alnum = cp < 65536 && char.IsLetterOrDigit((char)cp);
            if (alnum || cp == '-' || cp == '_')
            {
                safe.Append(char.ConvertFromUtf32(cp));
            }
        }
        string name = safe.Length == 0 ? "active" : safe.ToString();
        string dir = PPath.Join(Root(), name);
        Utils.EnsureDir(dir);
        return dir;
    }

    private static string JournalPath(string chatId) => PPath.Join(ChatDir(chatId), "journal.json");

    private static string FilesDir(string chatId)
    {
        string dir = PPath.Join(ChatDir(chatId), "files");
        Utils.EnsureDir(dir);
        return dir;
    }

    private static List<object> LoadJournal(string chatId)
        => (Utils.ReadJson(JournalPath(chatId)) as List<object>) ?? new List<object>();

    private static void SaveJournal(string chatId, List<object> ops)
        => Utils.WriteJson(JournalPath(chatId), ops);

    private static string Abs(string p) => Utils.AbsPath(p);

    private static bool IsSymlink(string abs)
    {
        try
        {
            return (File.GetAttributes(abs) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>把某个内容块写回文件（含 mtime）。</summary>
    private static void RestoreFile(string chatId, PyDict entry)
    {
        string path = Py.Str(entry.Req("path"));
        byte[] bytes = File.ReadAllBytes(Abs(PPath.Join(FilesDir(chatId), Py.Str(entry.Req("sha256")) + ".bin")));
        string full = Path.GetFullPath(Abs(path));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(Abs(path), bytes);
        try
        {
            double mtime = Py.Float(Py.Or(entry.Get("mtime"), 0L));
            if (mtime > 0)
            {
                File.SetLastWriteTimeUtc(Abs(path), new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks((long)(mtime * 10000000.0)));
            }
        }
        catch (Exception)
        {
        }
    }

    private static bool RemoveFile(string path)
    {
        try
        {
            string abs = Abs(path);
            if (Directory.Exists(abs) && !IsSymlink(abs))
            {
                Directory.Delete(abs, true);
                return true;
            }
            if (File.Exists(abs) || IsSymlink(abs))
            {
                File.Delete(abs);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>删掉目录里「快照时不存在」的条目（这轮新增的文件/目录）。</summary>
    private static void SweepNewEntries(string bas, HashSet<string> knownFiles, HashSet<string> knownDirs)
    {
        string path = Abs(bas);
        if (!Directory.Exists(path))
        {
            return;
        }
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            List<string> all = Directory
                .EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)
                .Select(e => PPath.Join(bas, e.Substring(full.Length + 1)))
                .ToList();
            // 逆序（深的先删）:目录里还有东西时先清空
            all.Sort(string.CompareOrdinal);
            all.Reverse();
            foreach (string item in all)
            {
                string abs = Abs(item);
                if (File.Exists(abs))
                {
                    if (!knownFiles.Contains(item))
                    {
                        RemoveFile(item);
                    }
                }
                else if (Directory.Exists(abs) && !knownDirs.Contains(item))
                {
                    try
                    {
                        Directory.Delete(abs, true);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }

    /// <summary>重放一条操作；返回恢复失败的条数。</summary>
    private static long ApplyOp(string chatId, PyDict op)
    {
        List<PyDict> files = Py.ListOr(Py.Or(op.Get("files"), new List<object>())).Cast<PyDict>().ToList();
        var knownFiles = new HashSet<string>(files.Select(f => Py.Str(f.Req("path"))));
        var knownDirs = op.Get("known_dirs") is List<object> { Count: > 0 } dirs
            ? new HashSet<string>(dirs.Select(Py.Str))
            : new HashSet<string>(files.Where(f => Py.Truthy(f.Get("sha256"))).Select(f => new PPath(Py.Str(f.Get("path"))).Parent.ToString()));
        foreach (PyDict f in files.Where(f => Py.Truthy(f.Get("sha256"))))
        {
            knownDirs.Add(new PPath(Py.Str(f.Get("path"))).Parent.ToString());
        }
        long failures = 0;
        foreach (object d in Py.ListOr(Py.Or(op.Get("dirs"), new List<object>())))
        {
            SweepNewEntries(Py.Str(d), knownFiles, knownDirs);
        }
        foreach (PyDict entry in files)
        {
            if (!Py.Truthy(entry.Get("sha256")))
            {
                if (!RemoveFile(Py.Str(entry.Req("path"))))
                {
                    failures++;
                }
                continue;
            }
            try
            {
                RestoreFile(chatId, entry);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is KeyError || ex is ValueError)
            {
                failures++;
            }
        }
        return failures;
    }

    private static object? Seq(object op) => (op as PyDict)?.Get("seq");

    /// <summary>
    /// 回滚。<paramref name="allOps"/> 为真时退回全部轮次；否则退回指定轮（<paramref name="turnId"/> 为空 = 最后一轮）。
    /// 成功的操作从流水里摘掉（不会重复回滚），失败的原样留着。
    /// </summary>
    public static PyDict Rollback(string chatId, string turnId = "", bool allOps = false)
    {
        string chat = Py.Truthy(chatId) ? chatId : "active";
        lock (Gate)
        {
            List<object> journal = LoadJournal(chat);
            if (journal.Count == 0)
            {
                return PyDict.Of("ok", true, "restored_files", 0L, "restored_bytes", 0L);
            }
            List<object?> targetSeqs;
            if (allOps)
            {
                targetSeqs = journal.Select(Seq).ToList();
            }
            else if (Py.Truthy(turnId))
            {
                targetSeqs = journal.Where(o => Py.Eq((o as PyDict)?.Get("turn_id"), turnId)).Select(Seq).ToList();
            }
            else
            {
                object? lastTurn = Py.Or(((PyDict)journal[journal.Count - 1]).Get("turn_id"), "");
                targetSeqs = journal.Where(o => Py.Eq((o as PyDict)?.Get("turn_id"), lastTurn)).Select(Seq).ToList();
            }
            if (targetSeqs.Count == 0)
            {
                return PyDict.Of("ok", true, "restored_files", 0L, "restored_bytes", 0L);
            }
            bool InTarget(object o) => targetSeqs.Any(s => Py.Eq(s, Seq(o)));
            List<object> items = journal.Where(InTarget).ToList();
            List<object> rest = journal.Where(o => !InTarget(o)).ToList();
            long restoredFiles = 0;
            long restoredBytes = 0;
            long failures = 0;
            var failedOps = new List<object>();
            foreach (PyDict op in Py.Sorted(items, o => Py.Or(Seq(o), 0L), reverse: true))
            {
                try
                {
                    long failed = ApplyOp(chat, op);
                    failures += failed;
                    if (failed != 0)
                    {
                        failedOps.Add(op);
                        continue;
                    }
                    restoredFiles += Py.ListOr(Py.Or(op.Get("files"), new List<object>())).Count;
                    restoredBytes += Py.Int(Py.Or(op.Get("bytes"), 0L));
                }
                catch (Exception)
                {
                    failures++;
                    failedOps.Add(op);
                }
            }
            rest = Py.Sorted(rest.Concat(failedOps), o => Py.Or(Seq(o), 0L));
            SaveJournal(chat, rest);
            GcBlobs(chat, rest);
            return PyDict.Of("ok", failures == 0, "restored_files", restoredFiles, "restored_bytes", restoredBytes, "failures", failures);
        }
    }

    /// <summary>回收不再被任何流水引用的内容块。</summary>
    private static void GcBlobs(string chatId, List<object> ops)
    {
        var used = new HashSet<string>(ops
            .SelectMany(o => Py.ListOr(Py.Or(((PyDict)o).Get("files"), new List<object>())))
            .Select(f => Py.Str(((PyDict)f).Get("sha256"))));
        try
        {
            foreach (string file in Directory.EnumerateFiles(Abs(FilesDir(chatId)), "*.bin"))
            {
                if (!used.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }

    private static (List<string> files, bool truncated) IterFiles(string path)
    {
        if (LocalFs.IsFile(path))
        {
            return (new List<string> { path }, false);
        }
        if (!LocalFs.IsDir(path))
        {
            return (new List<string>(), false);
        }
        var list = new List<string>();
        foreach (string item in AiFs.RglobSorted(path))
        {
            if (LocalFs.IsFile(item))
            {
                list.Add(item);
                if (list.Count >= MaxDirFiles)
                {
                    return (list, true);
                }
            }
        }
        return (list, false);
    }

    private static List<string> IterDirs(string path)
        => LocalFs.IsDir(path) ? AiFs.RglobSorted(path).Where(LocalFs.IsDir).ToList() : new List<string>();

    /// <summary>写一个内容块（同内容只写一次；先写临时文件再原子替换）。</summary>
    private static bool StoreBlob(string chatId, byte[] data, string sha)
    {
        try
        {
            string dir = FilesDir(chatId);
            string target = PPath.Join(dir, sha + ".bin");
            if (!LocalFs.Exists(target))
            {
                string tmp = PPath.Join(dir, ".tmp-" + sha.Substring(0, 12) + "-" + Py.FloatRepr(Py.Time()));
                File.WriteAllBytes(Abs(tmp), data);
                Utils.OsReplace(Abs(tmp), Abs(target));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static PyDict Fail(long seq, string reason)
        => PyDict.Of("ok", false, "seq", seq, "files", 0L, "bytes", 0L, "reason", reason);

    /// <summary>给一组路径做快照（目录会展开成其中每个文件）。失败时把它自己刚写的块回收掉。</summary>
    public static PyDict Snapshot(string chatId, string turnId, IEnumerable<string>? paths)
    {
        string chat = string.IsNullOrEmpty(chatId) ? "active" : chatId;
        try
        {
            lock (Gate)
            {
                List<object> journal = LoadJournal(chat);
                var files = new List<object>();
                var dirs = new List<object>();
                var knownDirs = new List<string>();
                long totalBytes = 0;
                foreach (string raw in paths ?? Enumerable.Empty<string>())
                {
                    string path;
                    try
                    {
                        path = new PPath(raw).Resolve().ToString();
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    bool isDir = LocalFs.IsDir(path);
                    if (isDir)
                    {
                        dirs.Add(path);
                        knownDirs.Add(path);
                        knownDirs.AddRange(IterDirs(path));
                    }
                    if (!isDir && !LocalFs.Exists(path))
                    {
                        // 现在不存在 = 这轮新创的，回滚时就该删掉
                        files.Add(PyDict.Of("path", path, "sha256", "", "size", 0L, "mtime", 0L));
                        continue;
                    }
                    (List<string> found, bool truncated) = IterFiles(path);
                    if (truncated)
                    {
                        GcBlobs(chat, journal);
                        return Fail(journal.Count, "目录文件数超过 " + MaxDirFiles + ",本轮不可回滚");
                    }
                    foreach (string file in found)
                    {
                        byte[] data;
                        try
                        {
                            if (!LocalFs.IsFile(file))
                            {
                                continue;
                            }
                            data = File.ReadAllBytes(Abs(file));
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            GcBlobs(chat, journal);
                            return Fail(journal.Count, "无法读取 " + file);
                        }
                        string sha;
                        using (SHA256 sha256 = SHA256.Create())
                        {
                            sha = string.Concat(sha256.ComputeHash(data).Select(b => b.ToString("x2")));
                        }
                        if (!StoreBlob(chat, data, sha))
                        {
                            GcBlobs(chat, journal);
                            return Fail(journal.Count, "检查点写入失败(磁盘满或权限不足)");
                        }
                        totalBytes += data.Length;
                        files.Add(PyDict.Of("path", file, "sha256", sha, "size", (long)data.Length, "mtime", AiFs.MTime(file)));
                        if (totalBytes > MaxSnapshotBytes)
                        {
                            GcBlobs(chat, journal);
                            return Fail(journal.Count, "变更体积超过检查点上限");
                        }
                    }
                }
                long seq = journal.Count;
                if (files.Count > 0 || dirs.Count > 0)
                {
                    List<string> known = knownDirs.Distinct().ToList();
                    known.Sort(string.CompareOrdinal);
                    journal.Add(PyDict.Of(
                        "seq", seq,
                        "turn_id", turnId ?? "",
                        "ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                        "files", files,
                        "dirs", dirs,
                        "known_dirs", known.Cast<object>().ToList(),
                        "bytes", totalBytes));
                    SaveJournal(chat, journal);
                }
                return PyDict.Of("ok", true, "seq", seq, "files", (long)files.Count, "bytes", totalBytes, "reason", "");
            }
        }
        catch (Exception)
        {
            return Fail(-1, "检查点写入失败");
        }
    }

    /// <summary>开一个新对话时调：裁掉超过 1GB 的旧流水、只保留最近 20 个对话的检查点。</summary>
    public static void BeginChat(string chatId)
    {
        try
        {
            string chat = string.IsNullOrEmpty(chatId) ? "active" : chatId;
            lock (Gate)
            {
                List<object> journal = LoadJournal(chat);
                long total = journal.Sum(o => Py.Int(Py.Or(((PyDict)o).Get("bytes"), 0L)));
                var kept = new List<object>();
                for (int i = journal.Count - 1; i >= 0; i--)
                {
                    if (total > MaxChatBytes)
                    {
                        total -= Py.Int(Py.Or(((PyDict)journal[i]).Get("bytes"), 0L));
                    }
                    else
                    {
                        kept.Insert(0, journal[i]);
                    }
                }
                if (kept.Count != journal.Count)
                {
                    SaveJournal(chat, kept);
                    GcBlobs(chat, kept);
                }
                foreach (string dir in LocalFs.Children(Root())
                             .Where(LocalFs.IsDir)
                             .OrderByDescending(AiFs.MTime)
                             .ToList()
                             .Skip((int)KeepChats))
                {
                    try
                    {
                        Directory.Delete(Abs(dir), true);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>最后一轮的 turn_id（没有流水就空）。</summary>
    public static string LastTurnId(string chatId)
    {
        List<object> journal = LoadJournal(Py.Truthy(chatId) ? chatId : "active");
        if (journal.Count == 0)
        {
            return "";
        }
        return Py.Str(Py.Or(((PyDict)journal[journal.Count - 1]).Get("turn_id"), ""));
    }

    /// <summary>某一轮是否留下过磁盘改动（决定回退要不要动文件）。</summary>
    public static bool TurnHasOps(string chatId, string turnId)
    {
        string tid = turnId ?? "";
        if (tid.Length == 0)
        {
            return false;
        }
        return LoadJournal(Py.Truthy(chatId) ? chatId : "active").Any(o => Py.Eq(((PyDict)o).Get("turn_id"), tid));
    }
}
