// AI-perf-saves:实例级内存 / GC 设置 + 卡顿诊断(照 Windows 版 Ai/ExtraTools.cs 的 DiagnoseLag / RecommendMemory /
// RecommendGc / TuneJvm 移植)。
// i18n:ignore-file —— 本文件的中文是给模型看的诊断结论(工具结果,与 Windows 版同口径),界面不直接显示。
//
// 落盘位置(Linux 实例配置的真实字段):实例目录下的 .instance.json(Instance.Meta / SetMeta,
// 与实例 Java 偏好 "java" 同一个文件),键名
//   memory_mb  — 正整数,MB;
//   gc_preset  — Gc.LABELS 的键之一(auto / g1 / g1_tuned / zgc / none)。
// 启动链读取顺序(LaunchFlow.Prepare,LaunchCore.RunAsync 走的就是它):
//   内存:版本设置 tipcl.json 的 memory_mb → 实例 .instance.json 的 memory_mb → 调用方传的 memory_mb
//         (启动页滑块 = 全局 default_memory_mb)→ 全局 default_memory_mb → 4096(McLauncher 兜底);
//   GC  :版本设置的 gc → 实例的 gc_preset → 全局 gc_preset → auto。
// 与 Windows 版的差别:Windows 的 tune_jvm 写**全局**设置(default_memory_mb / gc_preset);
// Linux 按任务书写到**实例**上(多实例时每个整合包各调各的),全局设置不动。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher;

/// <summary>实例级 JVM 调优(内存 + GC 预设)与卡顿诊断。</summary>
public static class JvmTuning
{
    /// <summary>.instance.json 里的内存键(MB)。</summary>
    public const string MetaMemory = "memory_mb";

    /// <summary>.instance.json 里的 GC 预设键。</summary>
    public const string MetaGc = "gc_preset";

    public const long MinMemoryMb = 512;

    public const long MaxMemoryMb = 32768;

    /// <summary>McLauncher.BuildLaunchCommand 在什么都没给时用的内存。</summary>
    public const long LauncherDefaultMemoryMb = 4096;

    /// <summary>常见优化模组(照 Windows 版 ExtraTools.PerfMods,文件名小写包含任一关键字即算装了)。</summary>
    public static readonly (string label, string[] keys)[] PerfMods =
    {
        ("渲染优化（Sodium / Embeddium / OptiFine 之一）", new[] { "sodium", "embeddium", "rubidium", "optifine", "nvidium" }),
        ("逻辑优化（Lithium / Canary / Radium）", new[] { "lithium", "canary", "radium" }),
        ("内存优化（FerriteCore）", new[] { "ferritecore" }),
        ("启动和杂项优化（ModernFix）", new[] { "modernfix" }),
        ("实体剔除（Entity Culling）", new[] { "entityculling" }),
    };

    // ───────────────────────────── 读 / 写实例设置 ─────────────────────────────

    /// <summary>实例自己设置的内存(MB);没设或值非法返回 null。</summary>
    public static object? InstanceMemory(Instance instance)
    {
        try
        {
            object? raw = instance.Meta().Get(MetaMemory);
            if (!Py.Truthy(raw))
            {
                return null;
            }
            long mb = Py.Int(raw);
            return mb > 0 ? mb : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>实例自己设置的 GC 预设;没设或不是合法预设时返回 null(交给下一层)。</summary>
    public static object? InstanceGc(Instance instance)
    {
        try
        {
            string gc = Py.Lower(Py.Strip(Py.S(instance.Meta().Get(MetaGc))));
            return IsGcPreset(gc) ? gc : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsGcPreset(string? key) => !string.IsNullOrEmpty(key) && Gc.LABELS.ContainsKey(key);

    public static string GcLabel(string key) => Py.S(Gc.LABELS.Get(key, key));

    /// <summary>
    /// 某实例(可选某版本)启动时实际会用的内存与 GC,以及各自来自哪一层:
    /// <c>{memory_mb, memory_source, gc_preset, gc_label, gc_source}</c>;source ∈ version / instance / global / default。
    /// 顺序与 <see cref="LaunchFlow.Prepare"/> 完全一致(调用方不额外传 memory_mb 时)。
    /// </summary>
    public static PyDict Effective(Instance instance, string? version = null)
    {
        PyDict vs = new PyDict();
        if (!string.IsNullOrEmpty(version))
        {
            try
            {
                vs = VersionSettings.Load(instance, version);
            }
            catch (Exception)
            {
                vs = new PyDict();
            }
        }
        long memory;
        string memSource;
        object? vMem = vs.Get("memory_mb");
        object? iMem = InstanceMemory(instance);
        object? gMem = Config.CONFIG.Get("default_memory_mb");
        if (PositiveLong(vMem) is long v)
        {
            memory = v;
            memSource = "version";
        }
        else if (PositiveLong(iMem) is long i)
        {
            memory = i;
            memSource = "instance";
        }
        else if (PositiveLong(gMem) is long g)
        {
            memory = g;
            memSource = "global";
        }
        else
        {
            memory = LauncherDefaultMemoryMb;
            memSource = "default";
        }
        string gc;
        string gcSource;
        string vGc = Py.Lower(Py.Strip(Py.S(vs.Get("gc"))));
        object? iGc = InstanceGc(instance);
        string gGc = Py.Lower(Py.Strip(Py.S(Config.CONFIG.Get("gc_preset"))));
        if (vGc.Length > 0)
        {
            gc = vGc;
            gcSource = "version";
        }
        else if (iGc is string ig)
        {
            gc = ig;
            gcSource = "instance";
        }
        else if (gGc.Length > 0)
        {
            gc = gGc;
            gcSource = "global";
        }
        else
        {
            gc = "auto";
            gcSource = "default";
        }
        return PyDict.Of("memory_mb", memory, "memory_source", memSource, "gc_preset", gc, "gc_label", GcLabel(gc), "gc_source", gcSource);
    }

    /// <summary>该实例里单独设置过内存或 GC 的版本(这些版本仍以版本设置为准):<c>[{version, memory_mb, gc}]</c>。</summary>
    public static List<object> VersionOverrides(Instance instance)
    {
        var list = new List<object>();
        try
        {
            foreach (object id in instance.InstalledIds())
            {
                PyDict vs = VersionSettings.Load(instance, id);
                object? mem = PositiveLong(vs.Get("memory_mb"));
                string gc = Py.Strip(Py.S(vs.Get("gc")));
                if (mem != null || gc.Length > 0)
                {
                    list.Add(PyDict.Of("version", Py.Str(id), "memory_mb", mem, "gc", gc));
                }
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    /// <summary>
    /// 把内存与 GC 写进实例设置(.instance.json)。参数非法抛 <see cref="ValueError"/>;
    /// 实例目录不存在抛 <see cref="InstanceError"/>(不凭空造实例)。返回写入后的 <see cref="Effective"/>。
    /// </summary>
    public static PyDict ApplyToInstance(Instance instance, long memoryMb, string gcPreset)
    {
        if (!LocalFs.IsDir(instance.path))
        {
            throw new InstanceError(Py.Fmt("实例 {0} 不存在。", instance.name));
        }
        if (memoryMb < MinMemoryMb || memoryMb > MaxMemoryMb)
        {
            throw new ValueError(Py.Fmt("内存要在 {0}~{1} MB 之间", MinMemoryMb, MaxMemoryMb));
        }
        string gc = Py.Lower(Py.Strip(gcPreset ?? ""));
        if (!IsGcPreset(gc))
        {
            throw new ValueError("gc 只能是 auto / g1 / g1_tuned / zgc / none");
        }
        instance.SetMeta(MetaMemory, memoryMb);
        instance.SetMeta(MetaGc, gc);
        return Effective(instance);
    }

    /// <summary>实例设置文件(.instance.json)的路径:检查点与确认卡预览要用它。</summary>
    public static string InstanceMetaPath(Instance instance) => PPath.Join(instance.path, Instances.INSTANCE_META);

    // ───────────────────────────── 推荐值(照 Windows 版)─────────────────────────────

    /// <summary>按电脑内存和模组数推荐内存(MB),不超过电脑内存的 70%,也不超过 16 GB(照 Windows 版 RecommendMemory)。</summary>
    public static long RecommendMemory(long totalMb, int modCount)
    {
        long want = modCount >= 200 ? 10240 : (modCount >= 120 ? 8192 : (modCount >= 60 ? 6144 : (modCount >= 1 ? 4096 : 3072)));
        if (totalMb > 0)
        {
            long cap = Math.Max(1024L, totalMb * 7 / 10);
            want = Math.Min(want, cap);
        }
        return Math.Min(16384L, want / 256 * 256);
    }

    /// <summary>按内存与 Java 大版本推荐 GC 预设(照 Windows 版 RecommendGc;Windows 调用处固定传 17)。</summary>
    public static string RecommendGc(long memoryMb, int javaMajor)
    {
        if (javaMajor >= 21 && memoryMb >= 12288)
        {
            return "zgc";
        }
        return memoryMb >= 4096 ? "g1_tuned" : "auto";
    }

    // ───────────────────────────── 诊断 ─────────────────────────────

    /// <summary>实例(或版本隔离目录)mods 里启用中的 jar,文件名小写。</summary>
    public static List<string> ModFiles(Instance instance, string? version = null)
    {
        try
        {
            string dir = Mods.ContentDir(instance, "mods", string.IsNullOrEmpty(version) ? null : version);
            string abs = PPath.Abs(dir);
            if (!Directory.Exists(abs))
            {
                return new List<string>();
            }
            return Directory.GetFiles(abs)
                .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f).ToLowerInvariant())
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    /// <summary>优化模组装没装:<c>{installed:[label], missing:[label], optifine_sodium_conflict}</c>。</summary>
    public static PyDict PerfModStatus(IEnumerable<string> modFiles)
    {
        List<string> mods = modFiles.Select(m => (m ?? "").ToLowerInvariant()).ToList();
        var installed = new List<object>();
        var missing = new List<object>();
        foreach ((string label, string[] keys) in PerfMods)
        {
            (mods.Any(f => keys.Any(k => f.Contains(k))) ? installed : missing).Add(label);
        }
        bool conflict = mods.Any(f => f.Contains("optifine"))
                        && mods.Any(f => f.Contains("sodium") || f.Contains("embeddium") || f.Contains("rubidium"));
        return PyDict.Of("installed", installed, "missing", missing, "optifine_sodium_conflict", conflict);
    }

    /// <summary>
    /// 找最近的 latest.log 并读尾部(最多 <paramref name="maxBytes"/> 字节)。给了版本按该版本的游戏目录找,
    /// 否则实例根目录,再退到各版本隔离目录里最新的那份。找不到返回 ("", "")。
    /// </summary>
    public static (string path, string text) ReadLatestLog(Instance instance, string? version = null, int maxBytes = 4 * 1024 * 1024)
    {
        var candidates = new List<string>();
        try
        {
            if (!string.IsNullOrEmpty(version))
            {
                candidates.Add(PPath.Join(VersionSettings.GameDir(instance, version), "logs", "latest.log"));
            }
            candidates.Add(PPath.Join(instance.path, "logs", "latest.log"));
            string versions = PPath.Abs(instance.VersionsDir());
            if (Directory.Exists(versions))
            {
                candidates.AddRange(Directory.GetDirectories(versions).Select(d => Path.Combine(d, "logs", "latest.log")));
            }
        }
        catch (Exception)
        {
        }
        string? best = null;
        if (!string.IsNullOrEmpty(version) && candidates.Count > 0 && File.Exists(PPath.Abs(candidates[0])))
        {
            best = candidates[0];
        }
        else
        {
            best = candidates.Where(c => File.Exists(PPath.Abs(c)))
                .OrderByDescending(c => File.GetLastWriteTimeUtc(PPath.Abs(c)))
                .FirstOrDefault();
        }
        if (best == null)
        {
            return ("", "");
        }
        try
        {
            using var fs = new FileStream(PPath.Abs(best), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - maxBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[fs.Length - start];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = fs.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    break;
                }
                read += n;
            }
            return (best, Encoding.UTF8.GetString(buffer, 0, read));
        }
        catch (Exception)
        {
            return (best, "");
        }
    }

    private static readonly Regex CantKeepUp = new("Can't keep up!.*?Running (\\d+)ms or (\\d+) ticks behind", RegexOptions.CultureInvariant);

    /// <summary>扫日志里的卡顿信号(照 Windows 版):Can't keep up 次数与最大落后、OOM、Full GC、警告条数。</summary>
    public static PyDict ScanLog(string log)
    {
        log ??= "";
        MatchCollection behind = CantKeepUp.Matches(log);
        long worst = 0;
        foreach (Match m in behind)
        {
            if (long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms) && ms > worst)
            {
                worst = ms;
            }
        }
        return PyDict.Of(
            "cant_keep_up", (long)behind.Count,
            "worst_behind_ms", worst,
            "out_of_memory", log.Contains("OutOfMemoryError"),
            "full_gc", log.Contains("GC overhead limit") || Regex.IsMatch(log, "Pause Full|Full GC"),
            "warn_count", (long)Regex.Matches(log, "/WARN\\]").Count);
    }

    /// <summary>
    /// 卡顿诊断(只读)。字段照 Windows 版的口径并结构化:
    /// <c>{instance, version, total_mb, avail_mb, memory_mb, memory_source, gc_preset, gc_label, mod_count,
    /// recommended_memory_mb, recommended_gc, recommended_gc_label, perf_mods:{installed,missing,…},
    /// gpus, log:{found, path, …}, version_overrides, issues[], suggestions[], text}</c>。
    /// <paramref name="sys"/> 为空时现场探测(<see cref="SystemInfo.Snapshot"/>);
    /// <paramref name="logText"/> 非空时不去读盘(测试用)。
    /// </summary>
    public static PyDict DiagnoseLag(Instance instance, string? version = null, PyDict? sys = null, string? logText = null)
    {
        sys ??= SystemInfo.Snapshot();
        long totalMb = Py.Int(Py.Or(sys.Get("total_mb"), 0L));
        long availMb = Py.Int(Py.Or(sys.Get("avail_mb"), 0L));
        PyDict eff = Effective(instance, version);
        long alloc = Py.Int(eff.Get("memory_mb"));
        string gc = Py.S(eff.Get("gc_preset"));
        List<string> mods = ModFiles(instance, version);
        long need = RecommendMemory(totalMb, mods.Count);
        string gcRec = RecommendGc(need, 17);
        PyDict perf = PerfModStatus(mods);

        var issues = new List<object>();
        var suggestions = new List<object>();
        if (totalMb > 0 && alloc > totalMb * 3 / 4)
        {
            issues.Add("分配的内存超过电脑内存的 3/4，系统会频繁换页导致卡顿，建议调小。");
        }
        if (alloc + 512 < need)
        {
            issues.Add($"模组较多，分配偏少，建议约 {need} MB。");
        }
        else if (alloc > need + 4096 && mods.Count < 60)
        {
            issues.Add($"分配过多也会让 GC 停顿变长，建议约 {need} MB。");
        }
        if (availMb > 0 && availMb < alloc)
        {
            issues.Add($"电脑当前只剩 {availMb} MB 可用内存，比分配给游戏的 {alloc} MB 还少，先关掉浏览器等占内存的程序。");
        }

        string logPath;
        string log;
        if (logText != null)
        {
            logPath = "";
            log = logText;
        }
        else
        {
            (logPath, log) = ReadLatestLog(instance, version);
        }
        PyDict logInfo = ScanLog(log);
        logInfo["found"] = log.Length > 0;
        logInfo["path"] = logPath;
        if (log.Length > 0)
        {
            long behind = Py.Int(logInfo.Get("cant_keep_up"));
            if (behind > 0)
            {
                issues.Add($"服务端/世界逻辑跟不上 {behind} 次（最严重落后 {Py.Int(logInfo.Get("worst_behind_ms"))} ms），多半是实体、红石机器或区块加载太多，或者内存不足。");
            }
            if (Py.Truthy(logInfo.Get("out_of_memory")))
            {
                issues.Add("日志里有内存溢出（OutOfMemoryError），需要加内存或减少模组/光影。");
            }
            if (Py.Truthy(logInfo.Get("full_gc")))
            {
                issues.Add("出现过 Full GC，内存紧张，建议调大内存或换成调优 G1。");
            }
            long warn = Py.Int(logInfo.Get("warn_count"));
            if (warn > 500)
            {
                issues.Add($"日志里有 {warn} 条警告，刷屏的模组可能拖慢游戏，可以看看是哪个模组在报。");
            }
        }
        if (mods.Count > 0)
        {
            List<object> missing = Py.ListOr(perf.Get("missing"));
            if (missing.Count > 0)
            {
                issues.Add("没装这些常见优化模组（要和加载器、版本匹配）：" + string.Join("、", missing.Select(Py.Str)) + "。");
            }
            if (Py.Truthy(perf.Get("optifine_sodium_conflict")))
            {
                issues.Add("OptiFine 和 Sodium/Embeddium 同时装了，会冲突，只留一个。");
            }
        }

        if (Math.Abs(alloc - need) > 512 || gc != gcRec)
        {
            suggestions.Add($"用 tune_jvm（apply=true）把该实例调成 {need} MB + {GcLabel(gcRec)}，下次启动生效。");
        }
        if (mods.Count > 0 && Py.ListOr(perf.Get("missing")).Count > 0)
        {
            suggestions.Add("按加载器和版本补装缺的优化模组（先 search_mods 再 ask_user 让用户选）。");
        }
        List<object> gpus = Py.ListOr(sys.Get("gpus"));
        var vendors = gpus.Select(g => Py.S(Py.D(g).Get("vendor"))).ToList();
        if (vendors.Contains("Intel") && (vendors.Contains("NVIDIA") || vendors.Contains("AMD")))
        {
            suggestions.Add("这台电脑有核显和独显：确认 Java 跑在独显上（Linux 可用 prime-run / DRI_PRIME=1，Windows 在显卡控制面板给 javaw 选高性能）。");
        }
        List<object> overrides = VersionOverrides(instance);
        if (overrides.Count > 0)
        {
            suggestions.Add("这些版本单独设置过内存/GC，启动它们时以版本设置为准：" + string.Join("、", overrides.Select(o => Py.S(Py.D(o).Get("version")))) + "。");
        }
        if (issues.Count == 0)
        {
            suggestions.Add("没发现明显问题。若仍卡顿，多半是显卡驱动、光影太重、渲染距离太高，或笔记本没用独显。");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"电脑内存 {totalMb} MB（当前可用 {availMb} MB），游戏分配 {alloc} MB，GC 方案 {gc}，模组 {mods.Count} 个。");
        if (log.Length == 0)
        {
            sb.AppendLine("没有找到 latest.log（还没启动过，或日志在别的版本目录）。");
        }
        if (issues.Count == 0)
        {
            sb.AppendLine("没发现明显问题。若仍卡顿，多半是显卡驱动、光影太重、渲染距离太高，或笔记本没用独显。");
        }
        else
        {
            sb.AppendLine("发现：");
            foreach (object issue in issues)
            {
                sb.AppendLine("- " + Py.Str(issue));
            }
        }

        return PyDict.Of(
            "instance", instance.name,
            "version", version ?? "",
            "total_mb", totalMb,
            "avail_mb", availMb,
            "memory_mb", alloc,
            "memory_source", eff.Get("memory_source"),
            "gc_preset", gc,
            "gc_label", eff.Get("gc_label"),
            "mod_count", (long)mods.Count,
            "recommended_memory_mb", need,
            "recommended_gc", gcRec,
            "recommended_gc_label", GcLabel(gcRec),
            "perf_mods", perf,
            "gpus", gpus,
            "log", logInfo,
            "version_overrides", overrides,
            "issues", issues,
            "suggestions", suggestions,
            "text", sb.ToString().TrimEnd());
    }

    private static long? PositiveLong(object? raw)
    {
        if (!Py.Truthy(raw))
        {
            return null;
        }
        try
        {
            long v = Py.Int(raw);
            return v > 0 ? v : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
