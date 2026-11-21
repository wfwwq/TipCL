// AI-perf-saves:卡顿优化 + 存档备份的 AI 工具(get_system_info / diagnose_lag / tune_jvm / list_saves /
// backup_save / list_save_backups / restore_save_backup),照 Windows 版 Core/Launcher/Ai/ExtraTools.cs 移植。
// i18n:ignore-file —— 本文件的中文都是给模型看的:工具 schema 的 description 与工具结果里的 message
// (Windows 版同样不走词表);界面上能看到的标签与确认卡预览在 PerfSavesPreview.cs,那边一律走 I18nCore.Tr。
//
// 分工:
//   · 本类 = 纯静态的「算 / 做」,不依赖 ToolRunner,测试能直接调;
//   · ToolRunner.PerfSaves.cs = 分发 + 「游戏运行中拒绝写存档」(要问宿主 is_game_running);
//   · PerfSavesPreview.cs = 标签与改前改后预览;
//   · Core 底层:SystemInfo(硬件)、JvmTuning(实例内存/GC + 诊断)、Saves / SaveBackups(打包与回滚)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

/// <summary>卡顿优化 + 存档备份工具的实现(静态,可单测)。</summary>
public static class PerfSavesTools
{
    /// <summary>本包注册的工具名(声明顺序)。</summary>
    public static readonly string[] Names =
    {
        "get_system_info", "diagnose_lag", "tune_jvm", "list_saves", "backup_save", "list_save_backups", "restore_save_backup",
    };

    /// <summary>
    /// 工具 schema(OpenAI function 形状;名字、参数名与 Windows 版 ExtraTools.SchemasJson 一致)。
    /// 差别:tune_jvm 写到**实例**设置(Windows 写全局);restore_save_backup 回滚前一律先备份当前同名存档;
    /// 两个写存档的工具在游戏运行中会被拒绝 —— 描述里照实写,免得模型以为能做。
    /// </summary>
    private const string SchemasJson = """
        [
        {"type":"function","function":{"name":"get_system_info","description":"查看电脑配置：系统、CPU（核数）、显卡、内存总量和可用量、已装 Java。判断卡顿、分配内存前先看。","parameters":{"type":"object","properties":{}}}},
        {"type":"function","function":{"name":"diagnose_lag","description":"诊断游戏卡顿/掉帧：扫 latest.log 里的 Can't keep up、内存溢出、GC 告警，对比实例分配的内存和电脑内存，统计模组数，检查是否缺少常见优化模组（Sodium / Lithium / FerriteCore / ModernFix 等），给出建议。","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离时填版本号，一般留空"}}}}},
        {"type":"function","function":{"name":"tune_jvm","description":"按电脑内存和模组数量推荐游戏内存与 GC 方案。apply=false 只给建议；apply=true 写入该实例的启动设置（之后启动生效；单独设置过内存/GC 的版本仍以版本设置为准）。也可以直接传 memory_mb / gc 指定。","parameters":{"type":"object","properties":{"instance":{"type":"string"},"apply":{"type":"boolean","description":"是否写入设置，默认 false"},"memory_mb":{"type":"integer","description":"可选，手动指定内存（MB）"},"gc":{"type":"string","description":"可选：auto / g1 / g1_tuned / zgc / none"}}}}},
        {"type":"function","function":{"name":"list_saves","description":"列出实例里的存档（世界）","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离时填版本号，一般留空"}}}}},
        {"type":"function","function":{"name":"backup_save","description":"把一个存档打包备份（zip，放在实例的 backups 目录）。改模组、升级版本、装整合包前建议先备份。游戏运行中不能备份。","parameters":{"type":"object","properties":{"name":{"type":"string","description":"存档文件夹名"},"instance":{"type":"string"},"version":{"type":"string"}},"required":["name"]}}},
        {"type":"function","function":{"name":"list_save_backups","description":"列出存档备份，可按存档名过滤","parameters":{"type":"object","properties":{"name":{"type":"string"},"instance":{"type":"string"},"version":{"type":"string"}}}}},
        {"type":"function","function":{"name":"restore_save_backup","description":"从备份回滚存档。回滚前一律先自动把当前同名存档再备份一次。overwrite=true 覆盖同名存档；false 则恢复成新的存档文件夹。游戏运行中不能回滚。","parameters":{"type":"object","properties":{"backup_name":{"type":"string","description":"list_save_backups 返回的备份文件名"},"overwrite":{"type":"boolean"},"instance":{"type":"string"},"version":{"type":"string"}},"required":["backup_name"]}}}
        ]
        """;

    private static List<object>? _schemas;

    /// <summary>解析好的 schema(每次返回同一份,Tools.Schemas 合并时只取一次)。</summary>
    public static List<object> Schemas() => _schemas ??= Py.ListOr(PyJson.Loads(SchemasJson));

    /// <summary>
    /// 这些工具即使用户点了「本对话都允许」也不记成免确认(按 D-24:高风险/会删改存档的操作每次都要点)。
    /// 确认卡(ConfirmCard.AllowAlways)不给那个按钮,执行器这边再兜一层,防别的界面传来 always=true。
    /// </summary>
    public static bool NeverAlwaysAllow(string tool) => tool == "restore_save_backup";

    // ───────────────────────────── 公共小工具 ─────────────────────────────

    public static string? Ver(PyDict args)
    {
        string v = Py.Strip(Py.S(args.Get("version")));
        return v.Length > 0 ? v : null;
    }

    /// <summary>
    /// 参数里的实例(没写就用执行器的当前实例,再没有就用默认实例)。名字写了但不存在时**报错**,
    /// 不像 <c>new Instance(name)</c> 那样在单根模式下悄悄落到根目录(那样会改错实例的设置/存档)。
    /// </summary>
    public static Instance ResolveInstance(PyDict args, string? defaultInstance)
    {
        string name = Py.Strip(Py.S(Py.Or(args.Get("instance"), defaultInstance)));
        if (name.Length == 0)
        {
            return new Instance(null);
        }
        List<string> names = Instances.ListInstances().Select(Py.Str).ToList();
        if (names.Contains(name))
        {
            return new Instance(name);
        }
        var inst = new Instance(name);
        if (inst.name == name && LocalFs.IsDir(inst.path))
        {
            return inst;
        }
        throw new ValueError("没有这个实例：" + name);
    }

    private static double SizeMb(object? bytes) => Math.Round(Py.Int(Py.Or(bytes, 0L)) / 1048576.0, 1);

    private static string Stamp(object? mtime)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(Py.Int(mtime)).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static PyDict Err(string tool, string code, string message)
        => PyDict.Of("ok", false, "error", code, "message", message, "detail", "", "tool", tool);

    // ───────────────────────────── get_system_info ─────────────────────────────

    /// <summary>get_system_info 的结果:SystemInfo 快照 + 给模型读的 text(格式照 Windows 版 Sysinfo.FormatText)。</summary>
    public static PyDict SystemInfoResult(PyDict snap)
    {
        var result = new PyDict(snap)
        {
            ["text"] = SystemInfo.FormatText(snap),
        };
        return result;
    }

    // ───────────────────────────── tune_jvm ─────────────────────────────

    /// <summary>
    /// 算出 tune_jvm 要做什么(纯读):<c>{ok, apply, instance, meta_path, total_mb, mod_count,
    /// current:{memory_mb, memory_source, gc_preset, gc_label}, target:{memory_mb, gc_preset, gc_label},
    /// version_overrides, error?, message?}</c>。确认卡预览与执行都走这一份,保证「看到的就是要写的」。
    /// </summary>
    public static PyDict PlanTune(PyDict args, string? defaultInstance, long? totalMbOverride = null)
    {
        Instance inst = ResolveInstance(args, defaultInstance);
        long totalMb = totalMbOverride ?? SystemInfo.TotalMemoryMb();
        int mods = JvmTuning.ModFiles(inst).Count;
        PyDict cur = JvmTuning.Effective(inst);
        long mem = Py.Truthy(args.Get("memory_mb")) ? Py.Int(args.Get("memory_mb")) : JvmTuning.RecommendMemory(totalMb, mods);
        string gc = Py.Lower(Py.Strip(Py.S(Py.Or(args.Get("gc"), args.Get("gc_preset")))));
        if (gc.Length == 0)
        {
            gc = JvmTuning.RecommendGc(mem, 17);
        }
        PyDict plan = PyDict.Of(
            "ok", true,
            "apply", Py.Truthy(args.Get("apply")),
            "instance", inst.name,
            "meta_path", JvmTuning.InstanceMetaPath(inst),
            "total_mb", totalMb,
            "mod_count", (long)mods,
            "current", cur,
            "target", PyDict.Of("memory_mb", mem, "gc_preset", gc, "gc_label", JvmTuning.GcLabel(gc)),
            "version_overrides", JvmTuning.VersionOverrides(inst));
        if (mem < JvmTuning.MinMemoryMb || mem > JvmTuning.MaxMemoryMb)
        {
            plan["ok"] = false;
            plan["error"] = "bad_arguments";
            plan["message"] = $"内存要在 {JvmTuning.MinMemoryMb}~{JvmTuning.MaxMemoryMb} MB 之间";
        }
        else if (totalMb > 0 && mem > totalMb - 1024)
        {
            plan["ok"] = false;
            plan["error"] = "memory_too_high";
            plan["message"] = $"电脑只有 {totalMb} MB 内存，分配 {mem} MB 会让系统卡死，最多建议 {Math.Max(1024L, totalMb * 7 / 10)} MB。";
        }
        else if (!JvmTuning.IsGcPreset(gc))
        {
            plan["ok"] = false;
            plan["error"] = "bad_arguments";
            plan["message"] = "gc 只能是 auto / g1 / g1_tuned / zgc / none";
        }
        return plan;
    }

    /// <summary>执行 tune_jvm:apply=false 只回建议;apply=true 写进实例 .instance.json(下次启动生效)。</summary>
    public static PyDict RunTune(PyDict args, string? defaultInstance, long? totalMbOverride = null)
    {
        PyDict plan = PlanTune(args, defaultInstance, totalMbOverride);
        PyDict cur = Py.D(plan.Get("current"));
        PyDict target = Py.D(plan.Get("target"));
        string head = $"电脑内存 {Py.Int(plan.Get("total_mb"))} MB，模组 {Py.Int(plan.Get("mod_count"))} 个。"
                      + $"当前：{Py.Int(cur.Get("memory_mb"))} MB，{Py.S(cur.Get("gc_label"))}。"
                      + $"建议：{Py.Int(target.Get("memory_mb"))} MB，{Py.S(target.Get("gc_label"))}。";
        if (!Py.Truthy(plan.Get("ok")))
        {
            plan["message"] = Py.S(plan.Get("message"));
            plan["applied"] = false;
            return plan;
        }
        if (!Py.Truthy(plan.Get("apply")))
        {
            plan["applied"] = false;
            plan["message"] = head + "（还没改，确认后用 apply=true 写入）";
            return plan;
        }
        Instance inst = ResolveInstance(PyDict.Of("instance", plan.Get("instance")), defaultInstance);
        PyDict after = JvmTuning.ApplyToInstance(inst, Py.Int(target.Get("memory_mb")), Py.S(target.Get("gc_preset")));
        string tail = "已写入实例「" + inst.name + "」的设置，下次启动生效。";
        List<object> overrides = Py.ListOr(plan.Get("version_overrides"));
        if (overrides.Count > 0)
        {
            tail += "这些版本单独设置过内存/GC，仍以版本设置为准："
                    + string.Join("、", overrides.Select(o => Py.S(Py.D(o).Get("version")))) + "。";
        }
        plan["applied"] = true;
        plan["after"] = after;
        plan["message"] = head + tail;
        return plan;
    }

    // ───────────────────────────── 存档 ─────────────────────────────

    /// <summary>list_saves:<c>[{name, size_mb, modified, bytes}]</c>(字段照 Windows 版,另带原始字节数)。</summary>
    public static List<object> ListSaves(PyDict args, string? defaultInstance)
    {
        Instance inst = ResolveInstance(args, defaultInstance);
        return Saves.ListSaves(inst, Ver(args)).OfType<PyDict>()
            .Select(s => (object)PyDict.Of("name", s.Get("name"), "size_mb", SizeMb(s.Get("bytes")), "modified", Stamp(s.Get("mtime")),
                "bytes", s.Get("bytes")))
            .ToList();
    }

    /// <summary>list_save_backups:Saves.ListBackups 的原始行 + 可读的 size_mb / modified;name 参数按存档名过滤。</summary>
    public static List<object> ListBackups(PyDict args, string? defaultInstance)
    {
        Instance inst = ResolveInstance(args, defaultInstance);
        object? filter = Py.Truthy(args.Get("name")) ? args.Get("name") : null;
        return Saves.ListBackups(inst, filter, Ver(args)).OfType<PyDict>()
            .Select(r =>
            {
                var row = new PyDict(r)
                {
                    ["size_mb"] = SizeMb(r.Get("bytes")),
                    ["modified"] = Stamp(r.Get("mtime")),
                };
                return (object)row;
            })
            .ToList();
    }

    /// <summary>backup_save:打 zip 到 &lt;游戏目录&gt;/backups,返回 <c>{ok, backup_name, save, bytes, size_mb, path, message}</c>。</summary>
    public static PyDict Backup(PyDict args, string? defaultInstance, Action<string, long, long>? onProgress = null)
    {
        Instance inst = ResolveInstance(args, defaultInstance);
        string name = Py.Strip(Py.S(args.Get("name")));
        if (name.Length == 0)
        {
            throw new ValueError("缺少存档名(name 字段)");
        }
        PyDict r = Saves.BackupSave(inst, name, Ver(args), onProgress);
        return PyDict.Of(
            "ok", true,
            "backup_name", r.Get("name"),
            "save", r.Get("save"),
            "bytes", r.Get("bytes"),
            "size_mb", SizeMb(r.Get("bytes")),
            "path", r.Get("path"),
            "message", "已备份：" + Py.S(r.Get("name")));
    }

    /// <summary>
    /// restore_save_backup:先备份当前同名存档,再回滚(见 SaveBackups.RestoreWithSafetyBackup)。
    /// 返回 <c>{ok, restored_to, path, from, overwrite, safety_backup, message}</c>。
    /// </summary>
    public static PyDict Restore(PyDict args, string? defaultInstance, Action<string, long, long>? onProgress = null)
    {
        Instance inst = ResolveInstance(args, defaultInstance);
        string backup = Py.Strip(Py.S(args.Get("backup_name")));
        if (backup.Length == 0)
        {
            throw new ValueError("缺少备份文件名(backup_name 字段,用 list_save_backups 查)");
        }
        bool overwrite = Py.Truthy(args.Get("overwrite"));
        PyDict r = SaveBackups.RestoreWithSafetyBackup(inst, backup, Ver(args), overwrite, onProgress);
        string safety = Py.S(r.Get("safety_backup"));
        string message = "已从 " + backup + " 恢复到存档「" + Py.S(r.Get("name")) + "」。"
                         + (safety.Length > 0 ? "回滚前已把当前存档备份为 " + safety + "。" : "当时没有同名存档，不需要先备份。");
        return PyDict.Of(
            "ok", true,
            "restored_to", r.Get("name"),
            "path", r.Get("path"),
            "from", r.Get("from"),
            "overwrite", overwrite,
            "safety_backup", safety,
            "message", message);
    }

    // ───────────────────────────── 检查点 ─────────────────────────────

    /// <summary>
    /// 这些工具会改到哪些路径(给 Tools.AffectedPaths / Checkpoint.Snapshot);不是本包的工具返回 null。
    ///   · tune_jvm(apply=true)→ 实例的 .instance.json;只给建议时不改盘 → 空表;
    ///   · restore_save_backup → 回滚的目标存档目录(覆盖时 = 现有存档,回退能整体还原;不覆盖时 = 新目录,回退即删掉);
    ///     回滚前打的那份安全备份 zip **不进**检查点:它就是给人留的后路,对话回退不该把它删掉;
    ///   · backup_save → 空表:只新增一个 zip,不改任何已有文件;把整个 backups 目录(常有几个 GB 的 zip)
    ///     塞进检查点只会撞 512MB 上限,让「这一步无法回退」的提示误报。
    /// </summary>
    public static List<string>? AffectedPaths(string name, PyDict? args, string? instance)
    {
        args ??= new PyDict();
        try
        {
            switch (name)
            {
                case "tune_jvm":
                    if (!Py.Truthy(args.Get("apply")))
                    {
                        return new List<string>();
                    }
                    return new List<string> { JvmTuning.InstanceMetaPath(ResolveInstance(args, instance)) };
                case "restore_save_backup":
                {
                    PyDict plan = SaveBackups.PlanRestore(ResolveInstance(args, instance), Py.S(args.Get("backup_name")),
                        Ver(args), Py.Truthy(args.Get("overwrite")));
                    return new List<string> { Py.S(plan.Get("target_path")) };
                }
                case "backup_save":
                    return new List<string>();
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            return Names.Contains(name) ? new List<string>() : null;
        }
    }

    // ───────────────────────────── 自检 ─────────────────────────────

    /// <summary>
    /// --ai-selftest 里本包的几项(不碰用户数据:不建实例、不写存档;
    /// 「游戏运行中拒绝」用一个只会回答 is_game_running=true 的假门面验,拒绝发生在任何读写盘之前)。
    /// </summary>
    public static async Task SelfCheckAsync(Action<string, bool> check)
    {
        try
        {
            List<string> names = Tools.Names();
            check("卡顿/存档工具已进表", Names.All(names.Contains));
            check("卡顿/存档工具有元信息", Names.All(n => ToolMeta.Get(n) != null));
            check("只读工具判只读", ToolMeta.Get("get_system_info") is { @readonly: true }
                                   && ToolMeta.Get("diagnose_lag") is { @readonly: true }
                                   && ToolMeta.Get("list_saves") is { @readonly: true }
                                   && ToolMeta.Get("list_save_backups") is { @readonly: true });
            check("回滚存档是高风险删除类", ToolMeta.Get("restore_save_backup") is { @readonly: false, side_effect: "delete", risk: "high" });
            check("回滚存档不给「本对话都允许」", NeverAlwaysAllow("restore_save_backup") && !NeverAlwaysAllow("backup_save"));
            check("内存推荐:16G/150 模组 → 8192", JvmTuning.RecommendMemory(16384, 150) == 8192);
            check("内存推荐:4G/200 模组封顶 70%", JvmTuning.RecommendMemory(4096, 200) == 2816);
            check("GC 推荐:6G → 调优 G1", JvmTuning.RecommendGc(6144, 17) == "g1_tuned" && JvmTuning.RecommendGc(2048, 17) == "auto");
            PyDict perf = JvmTuning.PerfModStatus(new[] { "sodium-fabric-0.5.8.jar", "lithium-0.11.jar" });
            check("优化模组识别", Py.ListOr(perf.Get("installed")).Count == 2 && Py.ListOr(perf.Get("missing")).Count == 3);
            PyDict mem = SystemInfo.ParseMeminfo("MemTotal:       16384000 kB\nMemFree:  1000 kB\nMemAvailable:    8192000 kB\n");
            check("内存解析(/proc/meminfo)", Py.Int(mem.Get("total_mb")) == 16000 && Py.Int(mem.Get("avail_mb")) == 8000);
            check("内存探测有值", SystemInfo.TotalMemoryMb() > 0);

            var runner = new ToolRunner(new RunningBackend()) { PermissionMode = "yolo" };
            var call = new ToolCall("perf-1", "backup_save", PyDict.Of("name", "selftest-world"));
            string result = await runner.RunAsync(call).ConfigureAwait(false);
            check("游戏运行中拒绝备份存档", result.Contains("game_running"));
            var call2 = new ToolCall("perf-2", "restore_save_backup", PyDict.Of("backup_name", "selftest.zip"));
            string result2 = await runner.RunAsync(call2).ConfigureAwait(false);
            check("游戏运行中拒绝回滚存档", result2.Contains("game_running"));
        }
        catch (Exception ex)
        {
            check("卡顿/存档自检异常:" + Py.ExcStr(ex), false);
        }
    }

    /// <summary>自检用假门面:只回答「游戏正在运行」,其余一律空。</summary>
    private sealed class RunningBackend : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            if (method == "is_game_running" && true is T yes)
            {
                return Task.FromResult<T?>(yes);
            }
            return Task.FromResult<T?>(default);
        }
    }
}
