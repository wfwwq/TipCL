// AI-new-features:导出诊断报告(export_diagnostic_report 工具 + AI 页「导出诊断报告」入口共用)。
//
// 内容:对话里崩溃排查过程的摘要 + 日志关键片段 + 实例 / 模组清单 + 硬件摘要。
// 日志读取直接走 Core(Crash.CollectFiles / Crash.ReadText / Crash.AnalyzeInstance),**不注册**
// get_latest_log / get_crash_report(那两个工具归 AI-logs-fs 包)。
// 脱敏(Redact)是硬要求:账号 token / uuid / 含用户名的路径 / 邮箱 / 本机用户名一律打码,由测试断言锁住。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher.Ai;

/// <summary>诊断报告:组装 + 脱敏 + 落盘(zip 或 markdown)。</summary>
public static class DiagnosticReport
{
    /// <summary>默认导出目录(数据目录下的 diagnostics/;用户在 AI 页选目录时不用它)。</summary>
    public static string DefaultDir() => PPath.Join(Utils.ROOT, "diagnostics");

    // ───────────────────────────── 脱敏 ─────────────────────────────

    private static readonly Regex KeyValueSecret = new(
        "(?i)(\"?(?:access_?token|accessToken|client_?token|refresh_?token|session_?id|sessionid|api_?key|apikey|password|passwd|secret|authorization|xuid|msa_?token|id_token)\"?\\s*[:=]\\s*\"?)(?:Bearer\\s+)?[^\"\\s,;}]+",
        RegexOptions.CultureInvariant);

    private static readonly Regex CliSecret = new(
        "(?i)(--(?:accessToken|uuid|clientId|xuid|session)\\s+)\\S+",
        RegexOptions.CultureInvariant);

    private static readonly Regex CliUser = new("(?i)(--username\\s+)\\S+", RegexOptions.CultureInvariant);

    private static readonly Regex Bearer = new("(?i)(Bearer\\s+)[A-Za-z0-9._\\-]{8,}", RegexOptions.CultureInvariant);

    private static readonly Regex Jwt = new("eyJ[A-Za-z0-9_\\-]{8,}\\.[A-Za-z0-9_\\-]{8,}\\.[A-Za-z0-9_\\-]{4,}", RegexOptions.CultureInvariant);

    private static readonly Regex WinUserPath = new("(?i)([A-Z]:[\\\\/]+(?:Users|Documents and Settings)[\\\\/]+)[^\\\\/\\s\"'<>|:*?]+", RegexOptions.CultureInvariant);

    private static readonly Regex UnixUserPath = new("(/(?:home|Users)/)[^/\\s\"'<>]+", RegexOptions.CultureInvariant);

    private static readonly Regex Email = new("[A-Za-z0-9._%+\\-]+@[A-Za-z0-9\\-]+(?:\\.[A-Za-z0-9\\-]+)*\\.[A-Za-z]{2,}", RegexOptions.CultureInvariant);

    private static readonly Regex Uuid = new("(?i)\\b[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}\\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// 把一段文本里的敏感信息打码。<paramref name="extraWords"/> 是额外要抹掉的词(账号名、本机用户名等,长度 ≥ 3 才生效)。
    /// 规则:token / 密钥 / Bearer / JWT / 启动参数里的 --accessToken --uuid --username / 含用户名的家目录路径 / 邮箱 / UUID。
    /// </summary>
    public static string Redact(string? text, IEnumerable<string>? extraWords = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        string s = Crash.FilterSecrets(text);
        s = KeyValueSecret.Replace(s, "$1***");
        s = CliSecret.Replace(s, "$1***");
        s = CliUser.Replace(s, "$1<player>");
        s = Bearer.Replace(s, "$1***");
        s = Jwt.Replace(s, "***");
        // 家目录:先按本机真实家目录整段替换成 ~,再兜底泛化规则(别人机器上的路径、大小写不一致的写法)
        foreach (string home in HomeDirs())
        {
            s = ReplaceIgnoreCase(s, home, "~");
            s = ReplaceIgnoreCase(s, home.Replace('\\', '/'), "~");
        }
        s = WinUserPath.Replace(s, "$1<user>");
        s = UnixUserPath.Replace(s, "$1<user>");
        s = Email.Replace(s, "<email>");
        s = Uuid.Replace(s, "<uuid>");
        foreach (string word in SensitiveWords(extraWords))
        {
            s = Regex.Replace(s, "(?<![A-Za-z0-9_])" + Regex.Escape(word) + "(?![A-Za-z0-9_])", "<user>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return s;
    }

    private static IEnumerable<string> HomeDirs()
    {
        var list = new List<string>();
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && home.Length > 3)
            {
                list.Add(home.TrimEnd('\\', '/'));
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    private static IEnumerable<string> SensitiveWords(IEnumerable<string>? extra)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            words.Add(Environment.UserName ?? "");
        }
        catch (Exception)
        {
        }
        foreach (string w in extra ?? Array.Empty<string>())
        {
            words.Add(w ?? "");
        }
        // 太短或太常见的词不抹(否则 "Administrator" 之外的 "a"、"pc" 会把正文打烂)
        return words.Where(w => w.Trim().Length >= 3 && !CommonWords.Contains(w.Trim())).Select(w => w.Trim()).OrderByDescending(w => w.Length).ToList();
    }

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "users", "admin", "root", "home", "default", "player", "minecraft", "java", "test",
    };

    private static string ReplaceIgnoreCase(string s, string find, string with)
        => find.Length == 0 ? s : Regex.Replace(s, Regex.Escape(find), with.Replace("$", "$$"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>当前登录过的账号名(脱敏时一并抹掉;读不到就算了)。</summary>
    private static List<string> AccountNames()
    {
        var names = new List<string>();
        try
        {
            names.Add(Py.S(Config.CONFIG.Get("last_user")));
            foreach (object? raw in Py.ListOr(Utils.ReadJson(PPath.Join(Utils.ROOT, "accounts.json"), new List<object>())))
            {
                PyDict a = Py.D(raw);
                names.Add(Py.S(Py.Or(a.Get("name"), a.Get("username"))));
            }
        }
        catch (Exception)
        {
        }
        return names.Where(n => n.Length > 0).ToList();
    }

    // ───────────────────────────── 组装 ─────────────────────────────

    /// <summary>
    /// 组装报告正文(markdown)与要附带的日志片段。<paramref name="conversation"/> 是当前对话的消息列表
    /// (OpenAI 形状,可为空);<paramref name="hardware"/> 为空时走本地最小探测。
    /// </summary>
    public static (string Markdown, List<(string Name, string Text)> Logs) Build(Instance inst, List<object>? conversation, PyDict? hardware = null, IEnumerable<string>? extraWords = null)
    {
        var words = AccountNames().Concat(extraWords ?? Array.Empty<string>()).ToList();
        string R(string t) => Redact(t, words);
        var md = new StringBuilder();
        md.AppendLine("# TipCL 诊断报告");
        md.AppendLine();
        md.AppendLine("- 生成时间:" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        md.AppendLine("- 启动器版本:" + AppInfo.APP_VERSION);
        md.AppendLine("- 实例:" + R(inst.name));
        md.AppendLine("- 说明:账号令牌、UUID、含用户名的路径、邮箱已自动打码,可以放心发给别人看。");
        md.AppendLine();

        // 1. 排查过程摘要
        md.AppendLine("## 一、排查过程摘要");
        md.AppendLine();
        List<string> convo = ConversationSummary(conversation);
        if (convo.Count == 0)
        {
            md.AppendLine("(当前对话里还没有排查记录)");
        }
        foreach (string line in convo)
        {
            md.AppendLine(R(line));
        }
        md.AppendLine();

        // 2. 自动诊断结论
        md.AppendLine("## 二、启动诊断结论");
        md.AppendLine();
        try
        {
            PyDict report = Crash.AnalyzeInstance(inst, "", true, "");
            md.AppendLine("- 是否崩溃:" + (Py.Truthy(report.Get("is_crash")) ? "是" : "否 / 未发现"));
            string headline = Py.S(report.Get("headline"));
            if (headline.Length > 0)
            {
                md.AppendLine("- 结论:" + R(headline));
            }
            string summary = Py.S(report.Get("summary"));
            if (summary.Length > 0)
            {
                md.AppendLine("- 摘要:" + R(AiUiText.Trim(summary, 600)));
            }
            foreach (object? raw in Py.ListOr(report.Get("reasons")).Take(6))
            {
                PyDict r = Py.D(raw);
                md.AppendLine("  - " + R(Py.S(r.Get("title"))) + (Py.S(r.Get("extra")).Length > 0 ? ":" + R(AiUiText.Trim(Py.S(r.Get("extra")), 200)) : ""));
            }
        }
        catch (Exception ex)
        {
            md.AppendLine("- 诊断没跑完:" + R(Py.ExcStr(ex)));
        }
        md.AppendLine();

        // 3. 实例与模组清单
        md.AppendLine("## 三、实例与模组");
        md.AppendLine();
        try
        {
            string loader = Mods.DetectLoader(inst) ?? "原版";
            string mc = Mods.DetectMcVersion(inst) ?? "?";
            md.AppendLine("- MC 版本:" + mc + ";加载器:" + loader);
            md.AppendLine("- 已装版本:" + string.Join(", ", inst.InstalledIds().Select(x => Py.S(x))));
            List<object> mods = Mods.ListInstanceModEntries(inst);
            md.AppendLine("- 模组 " + mods.Count + " 个(✗ = 已禁用):");
            foreach (object raw in mods.Take(300))
            {
                PyDict m = Py.D(raw);
                md.AppendLine("  - " + (Py.Truthy(m.Get("enabled")) ? "" : "✗ ") + R(Py.S(m.Get("filename"))));
            }
        }
        catch (Exception ex)
        {
            md.AppendLine("- 读取模组清单失败:" + R(Py.ExcStr(ex)));
        }
        md.AppendLine();

        // 4. 硬件摘要
        md.AppendLine("## 四、硬件摘要");
        md.AppendLine();
        PyDict hw = hardware ?? NfHardwareProbe.Probe();
        md.AppendLine("- 内存:" + Py.S(hw.Get("ram_mb")) + " MB(可用 " + Py.S(hw.Get("ram_avail_mb")) + " MB)");
        md.AppendLine("- 显卡:" + R(Py.S(Py.Or(hw.Get("gpu"), "未识别"))) + (Py.Int(Py.Or(hw.Get("vram_mb"), 0L)) > 0 ? "(" + Py.S(hw.Get("vram_mb")) + " MB 显存)" : ""));
        if (Py.S(hw.Get("summary")).Length > 0)
        {
            md.AppendLine("- 概要:" + R(Py.S(hw.Get("summary"))));
        }
        md.AppendLine("- 分配给游戏:" + Py.S(Py.Or(Config.CONFIG.Get("memory_mb"), "默认")) + " MB");
        md.AppendLine();

        // 5. 日志关键片段
        var logs = new List<(string Name, string Text)>();
        md.AppendLine("## 五、日志关键片段");
        md.AppendLine();
        List<string> files;
        try
        {
            files = Crash.CollectFiles(inst.path, null, loose: true).Take(6).ToList();
        }
        catch (Exception)
        {
            files = new List<string>();
        }
        if (files.Count == 0)
        {
            md.AppendLine("(没找到 latest.log / 崩溃报告)");
        }
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            string text = Crash.ReadText(file, 400_000);
            string snippet = R(KeyLines(text));
            string name = new PPath(file).Name;
            string unique = name;
            for (int i = 2; !usedNames.Add(unique); i++)
            {
                unique = new PPath(name).Stem + "_" + i + new PPath(name).Suffix;
            }
            logs.Add((unique, snippet));
            md.AppendLine("### " + R(unique));
            md.AppendLine();
            md.AppendLine("```");
            md.AppendLine(AiUiText.Trim(snippet, 6000));
            md.AppendLine("```");
            md.AppendLine();
        }
        return (md.ToString(), logs);
    }

    /// <summary>对话 → 排查过程要点(用户问了什么、调了哪些工具、助手给的结论)。系统消息不进报告。</summary>
    public static List<string> ConversationSummary(List<object>? conversation, int maxLines = 40)
    {
        var lines = new List<string>();
        if (conversation == null)
        {
            return lines;
        }
        foreach (object raw in conversation)
        {
            if (raw is not PyDict m)
            {
                continue;
            }
            string role = Py.S(m.Get("role"));
            string content = Py.S(Images.StripImages(m.Get("content"))).Trim();
            switch (role)
            {
                case "user":
                    if (content.Length > 0)
                    {
                        lines.Add("- **用户**:" + OneLine(content, 300));
                    }
                    break;
                case "assistant":
                {
                    var tools = Py.ListOr(m.Get("tool_calls"))
                        .Select(t => Tools.Label(Py.S(Py.D(Py.D(t).Get("function")).Get("name"))))
                        .Where(n => n.Length > 0).ToList();
                    if (tools.Count > 0)
                    {
                        lines.Add("- 调用工具:" + string.Join("、", tools));
                    }
                    if (content.Length > 0)
                    {
                        lines.Add("- **助手**:" + OneLine(content, 500));
                    }
                    break;
                }
                case "tool":
                    if (content.Length > 0)
                    {
                        lines.Add("  - 结果:" + OneLine(content, 200));
                    }
                    break;
            }
        }
        return lines.Count > maxLines ? lines.Skip(lines.Count - maxLines).Prepend("- …(更早的对话省略)").ToList() : lines;
    }

    private static string OneLine(string text, int max)
        => AiUiText.Trim(Regex.Replace(text, "\\s+", " "), max);

    /// <summary>日志关键片段:报错 / 异常 / Caused by 及其上下 2 行(最多 160 行)+ 末尾 60 行。</summary>
    public static string KeyLines(string text)
    {
        List<string> all = Py.SplitLines(text ?? "");
        var keep = new SortedSet<int>();
        var key = new Regex("(?i)(ERROR|FATAL|Exception|Caused by|Crash|Mixin.*(fail|error)|Missing|mismatch|requires|Could not|Failed)", RegexOptions.CultureInvariant);
        for (int i = 0; i < all.Count && keep.Count < 160; i++)
        {
            if (key.IsMatch(all[i]))
            {
                for (int j = Math.Max(0, i - 2); j <= Math.Min(all.Count - 1, i + 2); j++)
                {
                    keep.Add(j);
                }
            }
        }
        for (int i = Math.Max(0, all.Count - 60); i < all.Count; i++)
        {
            keep.Add(i);
        }
        var sb = new StringBuilder();
        int last = -2;
        foreach (int i in keep)
        {
            if (i != last + 1 && last >= 0)
            {
                sb.AppendLine("…");
            }
            sb.AppendLine(all[i]);
            last = i;
        }
        return sb.ToString();
    }

    // ───────────────────────────── 落盘 ─────────────────────────────

    /// <summary>
    /// 算出这次导出写到哪(写进 args["_out_path"],之后检查点 / 预览 / 执行都用同一个,保证一致)。
    /// dest_dir 为空 → 数据目录下 diagnostics/;format = zip(默认)/ md;重名自动加序号,绝不覆盖已有文件。
    /// </summary>
    public static string ResolveOutPath(PyDict args, string instance)
    {
        string cached = Py.S(args.Get("_out_path"));
        if (cached.Length > 0)
        {
            return cached;
        }
        string dir = Py.Strip(Py.S(args.Get("dest_dir")));
        if (dir.Length == 0)
        {
            dir = DefaultDir();
        }
        dir = PPath.Norm(dir);
        string format = Py.Lower(Py.S(Py.Or(args.Get("format"), "zip"))) == "md" ? "md" : "zip";
        string inst = Instances.SanitizeInstanceName(Py.S(Py.Or(args.Get("instance"), instance)), "game");
        string stem = "TipCL-诊断报告-" + inst + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string path = PPath.Join(dir, stem + "." + format);
        for (int i = 2; File.Exists(PPath.Abs(path)); i++)
        {
            path = PPath.Join(dir, stem + "-" + i + "." + format);
        }
        args["_out_path"] = path;
        return path;
    }

    /// <summary>写出报告,返回 {ok, path, format, bytes, files}。</summary>
    public static PyDict Export(Instance inst, List<object>? conversation, string outPath, PyDict? hardware = null, IEnumerable<string>? extraWords = null)
    {
        (string markdown, List<(string Name, string Text)> logs) = Build(inst, conversation, hardware, extraWords);
        string abs = PPath.Abs(outPath);
        string? parent = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        if (File.Exists(abs))
        {
            throw new FileExistsError("目标文件已存在,不覆盖: " + outPath);
        }
        var utf8 = new UTF8Encoding(false);
        var entries = new List<object>();
        if (abs.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(abs, markdown, utf8);
            entries.Add("report.md");
        }
        else
        {
            using var fs = new FileStream(abs, FileMode.CreateNew);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            void Put(string name, string text)
            {
                using Stream s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                byte[] bytes = utf8.GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
                entries.Add(name);
            }
            Put("report.md", markdown);
            foreach ((string name, string text) in logs)
            {
                Put("logs/" + name, text);
            }
        }
        return PyDict.Of(
            "ok", true,
            "path", outPath,
            "format", abs.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "md" : "zip",
            "bytes", new FileInfo(abs).Length,
            "files", entries,
            "redacted", true,
            "message", "诊断报告已导出(敏感信息已打码)");
    }
}
