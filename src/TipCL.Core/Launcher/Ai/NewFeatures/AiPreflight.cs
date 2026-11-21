// AI-new-features:启动前预检(preflight_check 工具 + 启动页「启动前 AI 预检」共用)。
//
// 不重复造轮子:底子是 Linux 已有的 Preflight.CheckLaunch(实例目录 / 版本 JSON / 依赖库 / 资源 / natives /
// 磁盘 / 指定 Java)与 Conflict.ScanConflicts(缺前置 / 加载器不匹配 / 重复 / breaks)。这里只补它们没覆盖的:
//   1. 「自动选择」Java 时实际会挑到哪个、版本够不够、老版本 MC 配新 Java;
//   2. 分配内存与**物理总内存**比(Preflight 只在 Windows 上比可用内存);
//   3. 把两路结果合成一份带 fix(下一步建议)的清单,给 AI 与启动页提示条用。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>启动前预检(只读)。</summary>
public static class AiPreflight
{
    /// <summary>设置开关「启动前 AI 预检」的配置键(缺省 = 开)。</summary>
    public const string ENABLED_KEY = "ai_preflight_enabled";

    /// <summary>启动页点启动时要不要先跑一遍预检(默认开;用户在「设置 → AI 助手」里关)。</summary>
    public static bool IsEnabled()
    {
        object? raw = Config.CONFIG.Get(ENABLED_KEY);
        return raw == null || Py.Truthy(raw);
    }

    /// <summary>写开关并落盘。</summary>
    public static void SetEnabled(bool on)
    {
        Config.CONFIG.Set(ENABLED_KEY, on);
        Config.CONFIG.Save();
    }

    /// <summary>测试注入点:已装 Java 列表(每项 {major, exe});为空时读 Java.CachedAllJavas(不扫盘)。</summary>
    internal static Func<List<object>>? JavaListHook;

    /// <summary>测试注入点:探测某个 java 可执行文件的主版本号。</summary>
    internal static Func<string, long?>? JavaMajorHook;

    /// <summary>
    /// 跑一遍预检。<paramref name="version"/> 为空时挑实例里最新装的那个版本;<paramref name="memoryMb"/> ≤ 0 时读配置。
    /// 返回 {ok, instance, version, items:[{level, code, title, detail, fix}], error_count, warn_count, summary}。
    /// </summary>
    public static PyDict Check(Instance inst, string? version = null, long memoryMb = 0, string? javaExe = null, PyDict? hardware = null)
    {
        version = Py.Strip(version ?? "");
        if (version.Length == 0)
        {
            version = PickVersion(inst);
        }
        if (memoryMb <= 0)
        {
            memoryMb = Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), Config.CONFIG.Get("default_memory_mb"), 0L));
        }
        string java = NormalizeJava(javaExe);
        if (java.Length == 0)
        {
            java = NormalizeJava(SafeJavaPref(inst));
        }

        var items = new List<PyDict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string level, string code, string title, string detail, string fix)
        {
            if (seen.Add(code + "\0" + detail))
            {
                items.Add(PyDict.Of("level", level, "code", code, "title", title, "detail", detail, "fix", fix));
            }
        }

        // 1) 现成的启动预检
        try
        {
            PyDict pre = Preflight.CheckLaunch(inst, version, memoryMb, java);
            foreach (object? raw in Py.ListOr(pre.Get("items")))
            {
                PyDict it = Py.D(raw);
                string level = Py.S(it.Get("level"));
                if (level == "ok")
                {
                    continue;
                }
                string code = Py.S(it.Get("code"));
                Add(level, code, Py.S(it.Get("title")), Py.S(it.Get("detail")), FixFor(code));
            }
        }
        catch (Exception ex)
        {
            Add("warn", "preflight_failed", "基础预检没跑完", Py.ExcStr(ex), "可以先启动看看,失败后用 diagnose_launch 分析");
        }

        // 2) Java 与 MC 版本是否匹配(含「自动选择」)
        PyDict? vjson = ResolvedVersionJson(inst, version);
        if (vjson != null)
        {
            CheckJava(vjson, java, Add);
        }

        // 3) 模组:缺前置 / 加载器不匹配 / 重复 / 互斥
        try
        {
            PyDict scan = Conflict.ScanConflicts(inst);
            foreach (object? raw in Py.ListOr(scan.Get("issues")))
            {
                PyDict issue = Py.D(raw);
                string type = Py.S(issue.Get("type"));
                string msg = Py.S(issue.Get("message"));
                switch (type)
                {
                    case "missing_dep":
                        Add("error", "dep_missing", "缺少前置模组", msg, "用 search_mods 搜「" + Py.S(issue.Get("need")) + "」并安装,或先 disable_mod 禁用 " + Py.S(issue.Get("file")));
                        break;
                    case "loader_mismatch":
                        Add("error", "loader_mismatch", "模组与加载器不匹配", msg, "禁用这个模组,或换成对应加载器的版本");
                        break;
                    case "duplicate_id":
                        Add("warn", "mod_duplicate", "同一个模组装了多份", msg, "只留一份,其余 disable_mod");
                        break;
                    case "breaks":
                        Add("warn", "mod_breaks", "模组声明互不兼容", msg, "让用户选留哪个,另一个 disable_mod");
                        break;
                }
            }
        }
        catch (Exception)
        {
            // 没有 mods 目录 / 读不了 jar:不算问题
        }

        // 4) 分配内存 vs 物理内存
        if (memoryMb > 0)
        {
            PyDict hw = hardware ?? NfHardwareProbe.ProbeMemory();
            long total = Py.Int(Py.Or(hw.Get("ram_mb"), 0L));
            if (total > 0)
            {
                if (memoryMb >= total)
                {
                    Add("error", "memory_exceeds_physical", "分配内存超过物理内存",
                        "游戏分配 " + memoryMb + " MB,本机物理内存只有约 " + total + " MB,JVM 会起不来或疯狂换页",
                        "把内存改到 " + SuggestMemory(total) + " MB 左右");
                }
                else if (memoryMb > total * 0.8)
                {
                    Add("warn", "memory_near_physical", "分配内存接近物理内存",
                        "游戏分配 " + memoryMb + " MB,占本机 " + total + " MB 的 " + (int)Math.Round(memoryMb * 100.0 / total) + "%,系统和显卡驱动会不够用",
                        "建议改到 " + SuggestMemory(total) + " MB 左右");
                }
            }
        }

        int errors = items.Count(i => Py.S(i.Get("level")) == "error");
        int warns = items.Count(i => Py.S(i.Get("level")) == "warn");
        string summary = errors == 0 && warns == 0
            ? "预检通过,没发现会挡住启动的问题"
            : "发现 " + errors + " 个会导致启动失败的问题、" + warns + " 个提醒";
        return PyDict.Of(
            "ok", errors == 0,
            "instance", inst.name,
            "version", version,
            "memory_mb", memoryMb,
            "java", java.Length > 0 ? java : "自动选择",
            "items", items.Cast<object>().ToList(),
            "error_count", (long)errors,
            "warn_count", (long)warns,
            "summary", summary);
    }

    /// <summary>把预检结果写成一段交给 AI 的开场上下文(启动页「交给 AI 一键修复」用)。</summary>
    public static string BuildFixPrompt(PyDict report)
    {
        var lines = new List<string>
        {
            "启动前预检发现了问题,请帮我修好再启动。",
            "实例:" + Py.S(report.Get("instance")) + ";版本:" + Py.S(report.Get("version")) +
            ";内存:" + Py.S(report.Get("memory_mb")) + " MB;Java:" + Py.S(report.Get("java")),
            "预检结果(" + Py.S(report.Get("summary")) + "):",
        };
        foreach (object? raw in Py.ListOr(report.Get("items")))
        {
            PyDict it = Py.D(raw);
            string level = Py.S(it.Get("level")) == "error" ? "错误" : "提醒";
            lines.Add("- [" + level + "] " + Py.S(it.Get("title")) + ":" + AiUiText.Trim(Py.S(it.Get("detail")), 240) +
                      (Py.S(it.Get("fix")).Length > 0 ? "(建议:" + Py.S(it.Get("fix")) + ")" : ""));
        }
        lines.Add("请先用 preflight_check 复核,再按问题逐个修复(写操作照常走确认);修完告诉我能不能启动。");
        return string.Join("\n", lines);
    }

    private static long SuggestMemory(long totalMb)
    {
        long target = totalMb >= 16000 ? 6144 : (totalMb >= 8000 ? 4096 : Math.Max(1024, totalMb / 2));
        return Math.Min(target, Math.Max(1024, (long)(totalMb * 0.6)));
    }

    private static string FixFor(string code) => code switch
    {
        "no_version" or "no_version_json" => "先 install_game 装好这个版本",
        "libs_missing" or "libs_hash" or "assets_index_missing" or "assets_missing" or "natives_missing" => "到版本页点「修复」,或重新 install_game 同一版本补文件",
        "disk_low" or "disk_warn" => "清理磁盘空间(设置 → 存储 → 空间清理)",
        "mod_unzipped" => "删掉 mods 里被解压出来的文件夹,直接放 .jar",
        "vanilla_mods" => "先给这个实例装对应的加载器(Fabric / Forge 等)",
        "java_missing" => "把 Java 改回「自动选择」,或 download_java 装一个",
        "java_too_old" => "download_java 装需要的版本,或把 Java 改回「自动选择」",
        "not_writable" => "检查实例目录权限,或换一个可写的游戏目录",
        "memory_high" => "把分配内存调小一些",
        _ => "",
    };

    private static void CheckJava(PyDict vjson, string java, Action<string, string, string, string, string> add)
    {
        long required;
        try
        {
            required = Java.RequiredJavaMajor(vjson);
        }
        catch (Exception)
        {
            return;
        }
        if (required <= 0)
        {
            return;
        }
        if (java.Length > 0)
        {
            long? major = JavaMajor(java);
            if (major is long m && m > 0)
            {
                if (m < required)
                {
                    add("error", "java_too_old", "Java 版本过低(需要 " + required + "+)", "当前指定的是 Java " + m, FixFor("java_too_old"));
                }
                else if (required <= 8 && m >= 17)
                {
                    add("warn", "java_too_new", "老版本 MC 配了新 Java", "这个版本要 Java " + required + ",指定的是 Java " + m + ",Forge 等老加载器常因此崩溃", "改成 Java 8(download_java 8)或「自动选择」");
                }
            }
            return;
        }
        List<object> javas;
        try
        {
            javas = JavaListHook?.Invoke() ?? Java.CachedAllJavas();
        }
        catch (Exception)
        {
            javas = new List<object>();
        }
        var majors = javas.Select(j => Py.Int(Py.Or(Py.D(j).Get("major"), 0L))).Where(x => x > 0).ToList();
        bool exact = majors.Contains(required);
        bool newer = required >= 17 && majors.Any(x => x > required);
        if (!exact && !newer)
        {
            add("warn", "java_auto_download", "本机没有合适的 Java " + required,
                "已装:" + (majors.Count > 0 ? string.Join(", ", majors.Distinct().OrderBy(x => x).Select(x => "Java " + x)) : "无") + ";启动时会自动下载,首次启动会慢一些",
                "可以先 download_java " + required);
        }
    }

    private static long? JavaMajor(string exe)
    {
        Func<string, long?>? hook = JavaMajorHook;
        if (hook != null)
        {
            return hook(exe);
        }
        try
        {
            return Java.GetJavaMajor(exe);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string NormalizeJava(string? raw)
    {
        string s = Py.Strip(raw ?? "");
        return s is "" or "自动选择" or "auto" or "default" ? "" : s;
    }

    private static string SafeJavaPref(Instance inst)
    {
        try
        {
            return inst.JavaPref();
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>没指定版本时:优先带加载器的、名字最大的那个(与启动页「最近装的」口径接近)。</summary>
    public static string PickVersion(Instance inst)
    {
        try
        {
            List<string> ids = inst.InstalledIds().Select(x => Py.S(x)).Where(s => s.Length > 0).ToList();
            if (ids.Count == 0)
            {
                return "";
            }
            string last = Py.S(Config.CONFIG.Get("last_version"));
            if (last.Length > 0 && ids.Contains(last))
            {
                return last;
            }
            string[] loaders = { "fabric", "forge", "quilt", "neoforge", "optifine" };
            string? withLoader = ids.Where(i => loaders.Any(l => Py.Lower(i).Contains(l))).OrderBy(i => i, StringComparer.Ordinal).LastOrDefault();
            return withLoader ?? ids.OrderBy(i => i, StringComparer.Ordinal).Last();
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static PyDict? ResolvedVersionJson(Instance inst, string version)
    {
        if (version.Length == 0)
        {
            return null;
        }
        try
        {
            if (inst.VersionJson(version) is not PyDict raw || raw.Count == 0)
            {
                return null;
            }
            try
            {
                return Manifest.ResolveInherits(raw, pid => inst.VersionJson(pid) as PyDict);
            }
            catch (Exception)
            {
                return raw;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
