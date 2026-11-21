// AI-logs-fs:日志 / 文件 / 打开文件夹这几样 AI 工具的实现。
// i18n:ignore-file —— AI-integration:本文件的中文只出现在回给模型的工具结果 / 报错里(工具结果是数据,不是界面文案,
// 同 SystemInfo.cs 与 ToolRunner 既有口径),以及 open_folder 的中文别名匹配表(用户输入的词,必须保持中文)。
//
// 来源(只读参照,不改 windos/):
//   · get_latest_log / get_crash_report —— Windows 版 Tools.cs:993-996 + Diagnose.LogExcerpt / CollectLogs;
//   · fs_list / fs_read / fs_write      —— Windows 版 ExtraTools.cs:497-793(FsRoots / FsResolve / FsList / FsRead / FsWrite);
//   · open_folder                        —— Windows 版 ExtraTools.cs:797-821。
//
// 复用 Linux 已有的代码,不另写一份:
//   · 日志文件怎么找 = Crash.CollectFiles + Crash.Classify(diagnose_launch / 崩溃窗口用的同一条链),
//     读文本 = Crash.ReadText(UTF-8 读不了时回落 GB18030/GBK,中文 Windows 的 latest.log 就是这样);
//   · 版本隔离时的游戏目录 = VersionSettings.GameDir(与 Crash._run 同一个口径);
//   · 打开文件夹 = VersionOps.OpenFolder / Crash.OpenPath(Linux 走 xdg-open,macOS 走 open,
//     Windows 上跑 Avalonia 时走 ShellExecute → 资源管理器;测试用 Crash.ShellOpenHook 截住)。
//
// 与 Windows 版的差别(都是 Linux 侧更严,不是放宽):
//   1. 路径守卫除了「..」与允许根之外,还**逐段跟随符号链接 / 目录联接**算真实路径再判归属
//      (Windows 版 PPath.Resolve 只是 GetFullPath,实例里放一个指向外面的链接就能出去);
//   2. 启动器自己的账号 / 配置文件(accounts.json、config.json、device_id)读写都拒,
//      AI 自己的状态文件(会话、记忆、cache/ 下的检查点与产物)不许写 —— 否则「写一下 config.json
//      把权限档改成全自动」就能绕过确认链;
//   3. fs_write 拒绝脚本 / 二进制后缀(.sh / .bat / .jar / .exe …),免得写出能被双击执行的东西;
//   4. get_crash_report 取**最新**一份(按修改时间);Windows 版取的是排序后的第一份,也就是最旧的那份。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>日志 / 文件 / 打开文件夹工具(ToolRunner 的 get_latest_log、get_crash_report、fs_*、open_folder 都走这里)。</summary>
public static class AiFsTools
{
    /// <summary>fs_list 最多返回这么多条(照 Windows 版 _FS_MAX_LIST)。</summary>
    public const int FS_MAX_LIST = 200;

    /// <summary>fs_read 能读的最大文件(512 KB,照 Windows 版 _FS_MAX_READ)。</summary>
    public const long FS_MAX_READ = 524288L;

    /// <summary>fs_write 一次最多写这么多(256 KB,照 Windows 版 _FS_MAX_WRITE)。</summary>
    public const long FS_MAX_WRITE = 262144L;

    /// <summary>按行读时一次最多给这么多行(照 Windows 版 _FS_MAX_LINES)。</summary>
    public const int FS_MAX_LINES = 2000;

    /// <summary>不分页时日志只给末尾这么多字(照 Windows 版 LogExcerpt 的 maxChars=6000)。</summary>
    public const int LOG_TAIL_CHARS = 6000;

    /// <summary>不分页时读日志末尾多少字节(照 Windows 版 CollectLogs 的 120000)。</summary>
    private const long LOG_READ_BYTES = 120000L;

    /// <summary>分页读日志时最多读末尾这么多字节(再大的日志只看最后 4 MB,结果里会说明)。</summary>
    private const long LOG_PAGE_BYTES = 4L * 1024 * 1024;

    /// <summary>不按文本读的后缀(照 Windows 版 _FS_BINARY_SUFFIX)。</summary>
    public static readonly HashSet<string> BinarySuffix = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jar", ".zip", ".7z", ".rar", ".gz", ".tar", ".exe", ".dll", ".bin", ".so", ".dylib", ".class",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".bmp", ".psd",
        ".ogg", ".wav", ".mp3", ".flac", ".mp4", ".mkv", ".webm",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".pdf", ".pak", ".nbt", ".dat", ".dat_old", ".mca", ".mcr", ".litematic",
    };

    /// <summary>fs_write 不写的可执行脚本后缀(Linux 侧加的一道:写出来就能被双击执行)。</summary>
    private static readonly HashSet<string> ScriptSuffix = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sh", ".bash", ".bat", ".cmd", ".ps1", ".vbs", ".desktop", ".command", ".appimage", ".msi", ".lnk",
    };

    /// <summary>Windows:锚点后前一级命中这些就拒(照 Windows 版 _FS_DENY_TOP)。</summary>
    private static readonly HashSet<string> DenyTopWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "program files", "program files (x86)", "programdata", "$recycle.bin", "system volume information",
    };

    /// <summary>Linux / macOS:根下这些系统目录一律拒(就算用户把实例根设到了 /)。</summary>
    private static readonly HashSet<string> DenyTopUnix = new(StringComparer.Ordinal)
    {
        "etc", "usr", "bin", "sbin", "lib", "lib32", "lib64", "libx32", "boot", "proc", "sys", "dev", "run",
        "var", "snap", "srv", "root", "System", "Library", "private",
    };

    /// <summary>路径中任何一级是这些就拒(密钥目录)。</summary>
    private static readonly HashSet<string> DenyAnywhere = new(StringComparer.OrdinalIgnoreCase) { ".ssh", ".gnupg" };

    /// <summary>数据目录根下的账号 / 配置文件:读写都拒(里面有令牌、API Key、权限档位)。</summary>
    private static readonly string[] SecretFiles = { "accounts.json", "config.json", "device_id" };

    /// <summary>数据目录根下 AI 自己的状态文件:可以读,不许写。</summary>
    private static readonly string[] NoWriteFiles = { "ai_chats.json", "ai-memory.json", "ai_memory.md" };

    /// <summary>数据目录根下不许写的目录(检查点、工具产物、缓存都在 cache/ 里)。</summary>
    private static readonly string[] NoWriteDirs = { "cache" };

    private static StringComparison PathCmp => Utils.IS_WINDOWS ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer => Utils.IS_WINDOWS ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // ───────────────────────────── 实例 / 允许根 ─────────────────────────────

    /// <summary>
    /// 这次调用落在哪个实例:参数里的 instance 优先,其次执行器的当前实例,都没有就用默认实例
    /// (与 Tools.AffectedPaths / Preview.InstanceDir 同一个口径,检查点与预览才对得上同一个文件)。
    /// </summary>
    public static Instance ResolveInstance(PyDict? args, string instance)
    {
        string name = Py.S(Py.Or(args?.Get("instance"), instance));
        return new Instance(name.Length > 0 ? name : null);
    }

    /// <summary>
    /// fs_* 允许的根目录:启动器数据目录、实例根,以及版本隔离时有独立 saves 的 versions/&lt;版本&gt;/
    /// (照 Windows 版 ExtraTools.FsRoots;实例根本身包含 versions,单列出来是为了把隔离版本目录说清楚)。
    /// </summary>
    public static List<string> FsRoots(Instance? instance)
    {
        var roots = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrEmpty(p))
            {
                return;
            }
            string a = PPath.Abs(p);
            if (!roots.Any(r => string.Equals(r, a, PathCmp)))
            {
                roots.Add(a);
            }
        }
        Add(Utils.ROOT);
        if (instance != null)
        {
            Add(instance.path);
            try
            {
                string vdir = PPath.Abs(instance.VersionsDir());
                if (Directory.Exists(vdir))
                {
                    foreach (string v in Directory.GetDirectories(vdir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        if (Directory.Exists(Path.Combine(v, "saves")))
                        {
                            Add(v);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 列不了版本目录就只给实例根:少一个根只会让模型多报一次「不在允许范围」,不会越权
            }
        }
        return roots;
    }

    private static (List<string> roots, string baseDir) FsContext(PyDict args, string instance)
    {
        Instance? inst = null;
        try
        {
            inst = ResolveInstance(args, instance);
        }
        catch (Exception)
        {
            inst = null;
        }
        string baseDir = inst?.path ?? "";
        if (baseDir.Length == 0)
        {
            baseDir = Utils.ROOT;
        }
        return (FsRoots(inst), PPath.Abs(baseDir));
    }

    // ───────────────────────────── 路径守卫 ─────────────────────────────

    /// <summary>
    /// 跟随符号链接 / 目录联接后的真实路径(逐段走:任何一级是链接就换成它的最终目标,再接着往下走)。
    /// 不存在的尾段原样接上(fs_write 新建文件时目标还不存在)。链接层数超过 40 视为环,直接拒。
    /// </summary>
    public static string RealPath(string path) => RealPath(path, 0);

    private static string RealPath(string path, int depth)
    {
        if (depth > 40)
        {
            throw new PermissionError("符号链接层数过多,可能是环");
        }
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        string current = root;
        string[] parts = full.Substring(root.Length)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string next = Path.Combine(current, parts[i]);
            FileSystemInfo? info = null;
            if (Directory.Exists(next))
            {
                info = new DirectoryInfo(next);
            }
            else
            {
                var fi = new FileInfo(next);
                // 悬空链接 Exists=false,但 LinkTarget 仍有值:也要当链接处理,不能当成「还不存在的新文件」放过
                if (fi.Exists || fi.LinkTarget != null)
                {
                    info = fi;
                }
            }
            string? linkTarget = info?.LinkTarget;
            if (info != null && linkTarget != null)
            {
                string target = Path.IsPathRooted(linkTarget)
                    ? linkTarget
                    : Path.Combine(Path.GetDirectoryName(next) ?? current, linkTarget);
                next = RealPath(target, depth + 1);
            }
            current = next;
        }
        return current;
    }

    /// <summary><paramref name="path"/> 是否就是 <paramref name="root"/> 或在它下面(按平台大小写规则,按分隔符边界比)。</summary>
    public static bool IsWithin(string path, string root)
    {
        string p = Path.GetFullPath(path);
        string r = Path.GetFullPath(root);
        string sep = Path.DirectorySeparatorChar.ToString();
        string pTrim = p.Length > (Path.GetPathRoot(p) ?? "").Length ? p.TrimEnd(Path.DirectorySeparatorChar) : p;
        string rTrim = r.Length > (Path.GetPathRoot(r) ?? "").Length ? r.TrimEnd(Path.DirectorySeparatorChar) : r;
        if (string.Equals(pTrim, rTrim, PathCmp))
        {
            return true;
        }
        string prefix = rTrim.EndsWith(sep, StringComparison.Ordinal) ? rTrim : rTrim + sep;
        return pTrim.StartsWith(prefix, PathCmp);
    }

    /// <summary>拒绝列表:锚点后第一级命中系统目录、或任意一级是密钥目录(照 Windows 版 FsDenied,补了 Linux 的系统目录)。</summary>
    public static bool FsDenied(string realPath)
    {
        string full = Path.GetFullPath(realPath);
        string root = Path.GetPathRoot(full) ?? "";
        string[] parts = full.Substring(root.Length)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            if (Utils.IS_WINDOWS ? DenyTopWindows.Contains(parts[0]) : DenyTopUnix.Contains(parts[0]))
            {
                return true;
            }
        }
        return parts.Any(p => DenyAnywhere.Contains(p));
    }

    /// <summary>
    /// fs_* 共用的路径守卫:绝对路径必须落在允许根内,相对路径基于实例根解析;
    /// 「..」、驱动器相对路径(C:foo)、备用数据流(a.txt:x)、网络 / 设备路径、8.3 短名、末尾带点或空格的段一律拒;
    /// 逐段跟随链接后的真实路径逃出允许根、命中系统目录或启动器的账号/配置文件也拒。
    /// 返回真实路径(之后的读写、检查点、预览都用这一份,不会出现「检查的是 A、写的是 B」)。
    /// </summary>
    public static string FsResolve(string? raw, List<string> roots, string baseDir, bool forWrite = false)
    {
        string text = (raw ?? "").Trim();
        if (text.Length == 0)
        {
            throw new ValueError("路径不能为空");
        }
        if (text.IndexOf('\0') >= 0)
        {
            throw new ValueError("路径里有非法字符");
        }
        char sep = Path.DirectorySeparatorChar;
        // 两种斜杠都当分隔符:Linux 上把「..\..\」当成一个文件名放过去,换到 Windows 上就是穿越
        string norm = Utils.IS_WINDOWS ? text.Replace('/', '\\') : text.Replace('\\', '/');
        string[] segs = norm.Split(sep, StringSplitOptions.RemoveEmptyEntries);
        if (segs.Any(s => s == ".."))
        {
            throw new PermissionError("路径里不能用 ..");
        }
        if (Utils.IS_WINDOWS)
        {
            if (norm.StartsWith("\\\\", StringComparison.Ordinal))
            {
                throw new PermissionError("不支持网络路径或设备路径");
            }
            bool hasDrive = norm.Length >= 2 && norm[1] == ':' && char.IsLetter(norm[0]);
            if (hasDrive && !(norm.Length >= 3 && norm[2] == '\\'))
            {
                throw new ValueError("不能只写盘符,请写完整绝对路径或相对实例根的路径");
            }
            if (!hasDrive && norm.StartsWith("\\", StringComparison.Ordinal))
            {
                throw new ValueError("请写完整绝对路径(带盘符)或相对实例根的路径");
            }
            if (norm.IndexOf(':', hasDrive ? 2 : 0) >= 0)
            {
                throw new PermissionError("路径里不能有冒号(备用数据流)");
            }
            foreach (string s in segs.Skip(hasDrive ? 1 : 0))
            {
                if (s != "." && (s.EndsWith(".", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal)))
                {
                    throw new ValueError("文件名不能以点或空格结尾");
                }
                if (Regex.IsMatch(s, "~[0-9]"))
                {
                    throw new ValueError("不支持 8.3 短文件名,请写完整文件名");
                }
            }
        }
        string full = Path.IsPathRooted(norm)
            ? Path.GetFullPath(norm)
            : Path.GetFullPath(Path.Combine(PPath.Abs(string.IsNullOrEmpty(baseDir) ? Utils.ROOT : baseDir), norm));
        string real = RealPath(full);
        List<string> allowed = (roots ?? FsRoots(null)).Select(r => RealPath(PPath.Abs(r))).ToList();
        if (!allowed.Any(r => IsWithin(real, r)))
        {
            throw new PermissionError("路径不在允许范围内:只能访问启动器数据目录或实例目录");
        }
        if (FsDenied(real))
        {
            throw new PermissionError("不允许访问系统目录");
        }
        string dataRoot = RealPath(PPath.Abs(Utils.ROOT));
        string name = Path.GetFileName(real);
        string parent = Path.GetDirectoryName(real) ?? "";
        bool atDataRoot = string.Equals(parent.TrimEnd(sep), dataRoot.TrimEnd(sep), PathCmp);
        if (atDataRoot && SecretFiles.Any(f => string.Equals(f, name, PathCmp)))
        {
            throw new PermissionError("这是启动器的账号/配置文件,不允许 AI 直接读写;要改设置请让用户在设置页里改");
        }
        if (forWrite)
        {
            if (atDataRoot && NoWriteFiles.Any(f => string.Equals(f, name, PathCmp)))
            {
                throw new PermissionError("这是 AI 助手自己的状态文件,不能用 fs_write 改");
            }
            if (NoWriteDirs.Any(d => IsWithin(real, Path.Combine(dataRoot, d))))
            {
                throw new PermissionError("缓存目录(检查点、工具产物)不能用 fs_write 改");
            }
            string suffix = Path.GetExtension(real);
            if (ScriptSuffix.Contains(suffix) || BinarySuffix.Contains(suffix))
            {
                throw new PermissionError("不能写可执行脚本或二进制文件(" + suffix + ")");
            }
        }
        return real;
    }

    // ───────────────────────────── fs_list / fs_read / fs_write ─────────────────────────────

    private static Regex? FsGlob(string glob)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            return null;
        }
        return new Regex("^" + Regex.Escape(glob.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Stamp(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static PyDict FsEntry(string path)
    {
        bool dir = Directory.Exists(path);
        long bytes = 0L;
        string mtime = "";
        try
        {
            if (dir)
            {
                mtime = Stamp(Directory.GetLastWriteTimeUtc(path));
            }
            else
            {
                var fi = new FileInfo(path);
                bytes = fi.Length;
                mtime = Stamp(fi.LastWriteTimeUtc);
            }
        }
        catch (Exception)
        {
            // 读不到大小/时间就给 0 和空串,条目照列(照参考版)
        }
        return PyDict.Of("name", Path.GetFileName(path), "type", dir ? "dir" : "file", "bytes", bytes, "mtime", mtime);
    }

    /// <summary>fs_list:列目录(名字 / 类型 / 大小 / 修改时间,最多 200 条,可按 glob 过滤)。</summary>
    public static object FsList(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string raw = Py.Str(Py.Or(args.Get("path"), ""));
        string glob = Py.Str(Py.Or(args.Get("glob"), ""));
        (List<string> roots, string baseDir) = FsContext(args, instance);
        string path = FsResolve(raw, roots, baseDir);
        if (File.Exists(path))
        {
            return new List<object> { FsEntry(path) };
        }
        if (!Directory.Exists(path))
        {
            throw new FileNotFoundError("[Errno 2] No such file or directory: " + path);
        }
        Regex? filter = FsGlob(glob);
        var list = new List<string>();
        try
        {
            list.AddRange(Directory.EnumerateFileSystemEntries(path));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new PermissionError("读不了目录 " + path + ": " + Py.ExcStr(ex));
        }
        list.Sort(AiFs.ComparePaths);
        var rows = new List<object>();
        foreach (string item in list)
        {
            if (filter != null && !filter.IsMatch(Path.GetFileName(item)))
            {
                continue;
            }
            rows.Add(FsEntry(item));
            if (rows.Count >= FS_MAX_LIST)
            {
                break;
            }
        }
        return rows;
    }

    /// <summary>按行取一段(offset_line 为负 / 不填 = 读末尾 max_lines 行;照 Windows 版 FsRead 的分页口径)。</summary>
    public static string LineWindow(string text, object? offsetLine, object? maxLines)
    {
        List<string> lines = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        int count = lines.Count;
        long offset = Py.Int(Py.Or(offsetLine, -1L));
        long take = Py.Int(Py.Or(maxLines, 200L));
        if (take <= 0)
        {
            take = 200L;
        }
        if (take > FS_MAX_LINES)
        {
            take = FS_MAX_LINES;
        }
        IEnumerable<string> source;
        string range;
        if (offset >= 0)
        {
            int start = (int)Math.Min(offset, count);
            source = lines.Skip(start).Take((int)take);
            range = "第 " + start + "–" + Math.Min(count, start + (int)take) + " 行";
        }
        else
        {
            int start = Math.Max(0, count - (int)take);
            source = lines.Skip(start);
            range = "最后 " + (count - start) + " 行";
        }
        string head = count > 0 ? "共 " + count + " 行,显示" + range + "。" : "文件是空的。";
        return head + "\n" + string.Join("\n", source);
    }

    /// <summary>fs_read:读文本文件(默认最后 200 行;offset_line / max_lines 分页;上限 512 KB,二进制拒读)。</summary>
    public static object FsRead(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string raw = Py.Str(Py.Or(args.Get("path"), ""));
        object? offsetLine = args.Get("offset_line");
        object? maxLines = args.Get("max_lines");
        (List<string> roots, string baseDir) = FsContext(args, instance);
        string path = FsResolve(raw, roots, baseDir);
        if (!File.Exists(path))
        {
            throw new FileNotFoundError("[Errno 2] No such file or directory: " + path);
        }
        string suffix = Path.GetExtension(path);
        if (BinarySuffix.Contains(suffix))
        {
            return "这是二进制文件(" + suffix + "),读不了文本。用 fs_list 看看目录里有什么能读的。";
        }
        long size = new FileInfo(path).Length;
        if (size > FS_MAX_READ)
        {
            return "文件太大(" + (size / 1024) + " KB,上限 512 KB),读不了全文。可以先用 fs_list 确认位置,再让用户手动处理。";
        }
        byte[] head = new byte[Math.Min(4096, (int)size)];
        using (FileStream fs = File.OpenRead(path))
        {
            int read = 0;
            while (read < head.Length)
            {
                int n = fs.Read(head, read, head.Length - read);
                if (n <= 0)
                {
                    break;
                }
                read += n;
            }
        }
        if (head.Any(b => b == 0))
        {
            return "文件里有空字节,像是二进制文件,不按文本读。";
        }
        // 解码复用 Crash.ReadText:UTF-8 读不了时回落 GB18030 / GBK(中文 Windows 上的日志常见)
        return LineWindow(Crash.ReadText(path), offsetLine, maxLines);
    }

    /// <summary>fs_write:写文本文件(默认覆盖,append=true 追加;上限 256 KB;自动建父目录)。</summary>
    public static object FsWrite(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string raw = Py.Str(Py.Or(args.Get("path"), ""));
        string text = Py.Str(Py.Or(args.Get("content"), ""));
        bool append = Py.Truthy(args.Get("append"));
        (List<string> roots, string baseDir) = FsContext(args, instance);
        string path = FsResolve(raw, roots, baseDir, forWrite: true);
        if (Directory.Exists(path))
        {
            throw new ValueError("目标是个目录,不能当文件写");
        }
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        if (bytes.Length > FS_MAX_WRITE)
        {
            return "写入内容太大(" + (bytes.Length / 1024) + " KB,上限 256 KB),拒绝了。";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (append && File.Exists(path))
        {
            AiFs.WriteText(path, AiFs.ReadText(path, replace: true) + text);
        }
        else
        {
            AiFs.WriteText(path, text);
        }
        return "已" + (append ? "追加" : "写入") + " " + path + "(" + bytes.Length + " 字节)";
    }

    /// <summary>
    /// fs_write 会改到的路径(检查点在写之前按这份做快照,回退时才知道要还原什么)。
    /// 解析失败(越界 / 受保护)返回空清单:那种调用执行时也会被拒,不需要快照。
    /// </summary>
    public static List<string> AffectedPaths(PyDict? args, string instance)
    {
        args ??= new PyDict();
        try
        {
            (List<string> roots, string baseDir) = FsContext(args, instance);
            return new List<string> { FsResolve(Py.Str(Py.Or(args.Get("path"), "")), roots, baseDir, forWrite: true) };
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// fs_write 的「改前改后」预览(权限确认卡用):逐行 diff,行数超过 Preview.MAX_PREVIEW_LINES 就截断并把完整 diff 落盘。
    /// 返回结构与 Preview.ChangePreview 的 write_mod_config 分支同形(kind / head / lines / too_large)。
    /// 目标解析不出来(越界 / 受保护)返回 null —— 执行时同样会被守卫拒掉。
    /// </summary>
    public static PyDict? WritePreview(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string target;
        try
        {
            (List<string> roots, string baseDir) = FsContext(args, instance);
            target = FsResolve(Py.Str(Py.Or(args.Get("path"), "")), roots, baseDir, forWrite: true);
        }
        catch (Exception)
        {
            return null;
        }
        bool existed = File.Exists(target);
        string old = "";
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
        bool append = Py.Truthy(args.Get("append"));
        string after = append ? old + content : content;
        string label = Py.Str(Py.Or(args.Get("path"), "file")).Replace('\\', '/');
        List<string> diff = Preview.UnifiedDiff(Py.SplitLines(old), Py.SplitLines(after), "a/" + label, "b/" + label);
        var lines = new List<string>();
        if (!existed)
        {
            lines.Add(I18nCore.Tr("（新建文件）"));
        }
        else if (append)
        {
            lines.Add(I18nCore.Tr("（追加到文件末尾）"));
        }
        lines.Add(target);
        bool tooLarge = false;
        if (diff.Count > Preview.MAX_PREVIEW_LINES)
        {
            tooLarge = true;
            string stored = Artifacts.Save(string.Join("\n", diff), "fs_write");
            lines.AddRange(diff.Take(Preview.MAX_PREVIEW_LINES));
            lines.Add(stored.Length > 0
                ? I18nCore.Tr("变更过大，完整内容见 {path}").Replace("{path}", stored)
                : I18nCore.Tr("变更过大，仅显示前 {n} 行").Replace("{n}", Preview.MAX_PREVIEW_LINES.ToString()));
        }
        else
        {
            lines.AddRange(diff);
        }
        return PyDict.Of("kind", "diff", "head", I18nCore.Tr("将要写入的变更："), "lines", lines.Cast<object>().ToList(), "too_large", tooLarge);
    }

    // ───────────────────────────── get_latest_log / get_crash_report ─────────────────────────────

    /// <summary>
    /// 找日志文件:latest = logs/latest.log(Crash.Classify 判成 mc 且名字以 latest 开头的那份);
    /// crash = crash-reports 下修改时间最新的一份。版本隔离时在 VersionSettings.GameDir 下找(与 diagnose_launch 同口径)。
    /// 找不到返回空串。
    /// </summary>
    public static string FindLog(Instance instance, string kind, string version = "")
    {
        string root = version.Length > 0 ? VersionSettings.GameDir(instance, version) : instance.path;
        List<string> files = Crash.CollectFiles(root, null, loose: true);
        if (kind == "crash")
        {
            return files
                .Where(f => Crash.Classify(f) == "crash")
                .OrderByDescending(f => SafeMTime(f))
                .ThenByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .FirstOrDefault() ?? "";
        }
        string? latest = files.FirstOrDefault(f => Crash.Classify(f) == "mc"
            && Py.Lower(Path.GetFileName(f)).StartsWith("latest", StringComparison.Ordinal));
        if (latest != null)
        {
            return latest;
        }
        string fallback = PPath.Join(root, "logs", "latest.log");
        return LocalFs.IsFile(fallback) ? fallback : "";
    }

    private static DateTime SafeMTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(PPath.Abs(path));
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// get_latest_log / get_crash_report 的结果。不传 offset_line / max_lines 时与 Windows 版 Diagnose.LogExcerpt
    /// 同形:「文件: 路径」+ 末尾 6000 字;传了就按行分页(与 fs_read 同一套口径)。
    /// 超长结果由 ToolRunner 统一落盘(Artifacts),模型用 read_artifact 回读。
    /// </summary>
    public static string LogExcerpt(PyDict? args, string instance, string kind)
    {
        args ??= new PyDict();
        string version = Py.S(args.Get("version"));
        object? offsetLine = args.Get("offset_line");
        object? maxLines = args.Get("max_lines");
        Instance inst = ResolveInstance(args, instance);
        string path = FindLog(inst, kind, version);
        if (path.Length == 0)
        {
            return "没有 " + kind + " 日志";
        }
        bool paged = offsetLine != null || maxLines != null;
        long bytes = paged ? LOG_PAGE_BYTES : LOG_READ_BYTES;
        string text = Crash.ReadText(path, bytes);
        if (text.Length == 0)
        {
            return "没有 " + kind + " 日志";
        }
        if (!paged)
        {
            return "文件: " + path + "\n\n" + Py.Slice(text, -LOG_TAIL_CHARS);
        }
        string note = "";
        try
        {
            if (new FileInfo(PPath.Abs(path)).Length > LOG_PAGE_BYTES)
            {
                note = "(文件超过 4 MB,只读了最后 4 MB,行号从这一段开头算)\n";
            }
        }
        catch (Exception)
        {
            note = "";
        }
        return "文件: " + path + "\n" + note + LineWindow(text, offsetLine, maxLines);
    }

    // ───────────────────────────── open_folder ─────────────────────────────

    /// <summary>open_folder 认的 kind(Windows 版那 8 个 + Linux FolderMap 已有的几个)。</summary>
    public static readonly string[] FolderKinds =
    {
        "game", "root", "mods", "saves", "resourcepacks", "shaderpacks", "datapacks", "screenshots",
        "config", "logs", "crash-reports", "backups", "version",
    };

    private static string NormalizeKind(string kind) => kind switch
    {
        "" or "instance" or "游戏" or "实例" => "game",
        "mod" or "模组" => "mods",
        "save" or "world" or "worlds" or "存档" => "saves",
        "log" or "日志" => "logs",
        "crash" or "crash_reports" or "crashreports" or "崩溃报告" => "crash-reports",
        "backup" or "备份" => "backups",
        "resourcepack" or "资源包" => "resourcepacks",
        "shaderpack" or "shader" or "shaders" or "光影" => "shaderpacks",
        "datapack" or "数据包" => "datapacks",
        "screenshot" or "截图" => "screenshots",
        "配置" => "config",
        _ => kind,
    };

    /// <summary>
    /// open_folder:用系统文件管理器打开实例的某个文件夹(不存在就先建)。
    /// FolderMap 里有的(game / mods / saves / logs …)交给 VersionOps.OpenFolder(它自带「必须在实例目录内」的校验);
    /// config / backups 在这里算出路径,同样校验在实例目录内再交给 Crash.OpenPath。
    /// </summary>
    public static string OpenFolder(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string kind = NormalizeKind(Py.Lower(Py.Strip(Py.Str(Py.Or(args.Get("kind"), "game")))));
        string version = Py.S(args.Get("version"));
        Instance inst = ResolveInstance(args, instance);
        if (!FolderKinds.Contains(kind))
        {
            throw new ValueError("不认识的文件夹类型: " + kind + "(可选 " + string.Join(" / ", FolderKinds) + ")");
        }
        object? ver = version.Length > 0 ? version : null;
        if (kind is not ("config" or "backups"))
        {
            return "已打开 " + VersionOps.OpenFolder(inst, ver, kind);
        }
        PyDict map = VersionOps.FolderMap(inst, ver);
        string dir = kind == "backups"
            ? Saves.BackupsDir(inst, ver)
            : PPath.Join(Py.S(map.Get("game")), "config");
        if (!IsWithin(RealPath(PPath.Abs(dir)), RealPath(PPath.Abs(inst.path))))
        {
            throw new PermissionError("非法路径: " + dir);
        }
        Utils.EnsureDir(dir);
        if (!Crash.OpenPath(dir))
        {
            throw new OSError("无法打开: " + dir);
        }
        return "已打开 " + dir;
    }

    // ───────────────────────────── schema ─────────────────────────────

    /// <summary>
    /// 这几个工具的 schema(OpenAI function 形状)。名字、参数名与 Windows 版同名同形;
    /// get_latest_log / get_crash_report 比 Windows 版多两个可选的分页参数(与 fs_read 同名)。
    /// Tools.Schemas 把它们接在内置清单后面。
    /// </summary>
    public const string SchemaJson = """
        [
        {"type":"function","function":{"name":"get_latest_log","description":"读取 latest.log 末尾。版本隔离实例必须传 version。日志太长要分段看时传 offset_line / max_lines","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离实例传版本名,空则用实例根"},"offset_line":{"type":"integer","description":"可选,起始行(0 起);不填读末尾"},"max_lines":{"type":"integer","description":"可选,读取行数,默认 200,最多 2000"}}}}},
        {"type":"function","function":{"name":"get_crash_report","description":"读取最新崩溃报告(crash-reports 里最新的一份)。版本隔离实例必须传 version。要分段看时传 offset_line / max_lines","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离实例传版本名,空则用实例根"},"offset_line":{"type":"integer","description":"可选,起始行(0 起);不填读末尾"},"max_lines":{"type":"integer","description":"可选,读取行数,默认 200,最多 2000"}}}}},
        {"type":"function","function":{"name":"fs_list","description":"列出目录内容:文件名、类型、大小、修改时间,最多 200 条,可用 glob 过滤(如 *.log)。path 填绝对路径,或相对实例根的路径。只能访问启动器数据目录和实例目录(含版本隔离的 versions/版本/),系统目录会被拒绝。","parameters":{"type":"object","properties":{"path":{"type":"string"},"glob":{"type":"string","description":"可选,按文件名过滤,如 *.log"},"instance":{"type":"string"}},"required":["path"]}}},
        {"type":"function","function":{"name":"fs_read","description":"读文本文件,典型用途是读 latest.log 排查启动失败。默认返回最后 200 行;offset_line 从第几行开始读(0 起),max_lines 控制行数。上限 512 KB;二进制文件(jar / png / exe / zip 等)会拒绝,先用 fs_list 看看。路径限制同 fs_list,启动器的账号和配置文件读不了。","parameters":{"type":"object","properties":{"path":{"type":"string"},"offset_line":{"type":"integer","description":"起始行(0 起),不填读末尾"},"max_lines":{"type":"integer","description":"读取行数,默认 200"},"instance":{"type":"string"}},"required":["path"]}}},
        {"type":"function","function":{"name":"fs_write","description":"写文本文件:默认覆盖,append=true 追加到末尾。只能写在启动器数据目录或实例目录(含 versions/版本/)里,内容上限 256 KB,会自动建父目录。适合写配置、说明文件;不要碰启动器自己的核心文件和存档,脚本和二进制文件会被拒绝。","parameters":{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string","description":"完整文件内容"},"append":{"type":"boolean","description":"true 追加,默认 false 覆盖"},"instance":{"type":"string"}},"required":["path","content"]}}},
        {"type":"function","function":{"name":"open_folder","description":"用系统的文件管理器打开实例的文件夹,方便用户自己放文件","parameters":{"type":"object","properties":{"kind":{"type":"string","description":"game / mods / saves / resourcepacks / shaderpacks / datapacks / screenshots / config / logs / crash-reports / backups"},"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离时填版本名,一般留空"}}}}}
        ]
        """;

    /// <summary>解析好的 schema 列表(每次返回新列表,调用方可以随意 AddRange)。</summary>
    public static List<object> Schemas() => Py.ListOr(PyJson.Loads(SchemaJson));
}
