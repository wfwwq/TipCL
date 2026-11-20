// AI-perf-saves:卡顿优化 + 存档备份工具的界面文案 —— 步骤行 / 确认卡的标签,以及确认卡里的「改前改后」预览。
// 这里的中文都是用户能看到的,一律走 I18nCore.Tr(词条在 Resources/locales/{zh_CN,en}.json)。
// 预览结构(kind / head / lines / too_large)与 Preview.ChangePreview 一致,由它在 default 分支转给这里。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>卡顿/存档工具的标签与改前改后预览。</summary>
public static class PerfSavesPreview
{
    private static string Tr(string key) => I18nCore.Tr(key);

    /// <summary>工具标签;不是本包的工具返回 null(交回 Tools.Label 原逻辑)。</summary>
    public static string? Label(string name, PyDict? args)
    {
        string? label = name switch
        {
            "get_system_info" => Tr("查看电脑配置"),
            "diagnose_lag" => Tr("诊断卡顿"),
            "tune_jvm" => Tr("调优内存和 GC"),
            "list_saves" => Tr("查看存档"),
            "backup_save" => Tr("备份存档"),
            "list_save_backups" => Tr("查看存档备份"),
            "restore_save_backup" => Tr("回滚存档"),
            _ => null,
        };
        if (label == null || args == null)
        {
            return label;
        }
        string arg = name switch
        {
            "backup_save" => Py.S(args.Get("name")),
            "restore_save_backup" => Py.S(args.Get("backup_name")),
            "tune_jvm" => Py.Truthy(args.Get("apply")) ? Py.S(args.Get("instance")) : "",
            _ => "",
        };
        return arg.Length > 0 ? label + " " + arg : label;
    }

    private static PyDict Row(string kind, string head, List<string> lines)
        => PyDict.Of("kind", kind, "head", head, "lines", lines.Cast<object>().ToList(), "too_large", false);

    private static string SourceText(object? source) => Py.S(source) switch
    {
        "version" => Tr("版本设置"),
        "instance" => Tr("实例设置"),
        "global" => Tr("全局默认"),
        _ => Tr("启动器默认"),
    };

    private static string MemLine(object? mb, object? source)
        => Tr("内存：{mb} MB（{src}）").Replace("{mb}", Py.Str(mb)).Replace("{src}", SourceText(source));

    private static string GcLine(object? label) => Tr("GC：{label}").Replace("{label}", Tr(Py.S(label)));

    /// <summary>算预览;不是本包的工具返回 null。纯读,不改盘。</summary>
    public static PyDict? ChangePreview(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        var lines = new List<string>();
        switch (name)
        {
            case "tune_jvm":
            {
                PyDict plan = PerfSavesTools.PlanTune(args, instance);
                PyDict cur = Py.D(plan.Get("current"));
                PyDict target = Py.D(plan.Get("target"));
                if (!Py.Truthy(plan.Get("ok")))
                {
                    lines.Add(Py.S(plan.Get("message")));
                    return Row("info", Tr("这次调优不会执行："), lines);
                }
                if (!Py.Truthy(plan.Get("apply")))
                {
                    lines.Add(MemLine(target.Get("memory_mb"), "instance"));
                    lines.Add(GcLine(target.Get("gc_label")));
                    return Row("info", Tr("只给建议，不会改动设置："), lines);
                }
                lines.Add("- " + MemLine(cur.Get("memory_mb"), cur.Get("memory_source")));
                lines.Add("+ " + MemLine(target.Get("memory_mb"), "instance"));
                lines.Add("- " + GcLine(cur.Get("gc_label")));
                lines.Add("+ " + GcLine(target.Get("gc_label")));
                lines.Add(Tr("写入：{path}").Replace("{path}", Py.S(plan.Get("meta_path"))));
                List<object> overrides = Py.ListOr(plan.Get("version_overrides"));
                if (overrides.Count > 0)
                {
                    lines.Add(Tr("这些版本单独设置过内存或 GC，仍以版本设置为准：{list}")
                        .Replace("{list}", string.Join(", ", overrides.Select(o => Py.S(Py.D(o).Get("version"))))));
                }
                return Row("diff", Tr("将要修改实例「{name}」的启动设置：").Replace("{name}", Py.S(plan.Get("instance"))), lines);
            }

            case "backup_save":
            {
                Instance inst = PerfSavesTools.ResolveInstance(args, instance);
                string world = Py.S(args.Get("name"));
                PyDict? row = Saves.ListSaves(inst, PerfSavesTools.Ver(args)).OfType<PyDict>()
                    .FirstOrDefault(s => Py.S(s.Get("name")) == world);
                if (row == null)
                {
                    lines.Add(Tr("（没找到这个存档，执行时会报「存档不存在」）"));
                }
                else
                {
                    lines.Add("saves/" + world);
                }
                lines.Add(Tr("备份到：{dir}").Replace("{dir}", Saves.BackupsDir(inst, PerfSavesTools.Ver(args))));
                return Row("summary", Tr("将要备份存档："), lines);
            }

            case "restore_save_backup":
            {
                Instance inst = PerfSavesTools.ResolveInstance(args, instance);
                PyDict plan = SaveBackups.PlanRestore(inst, Py.S(args.Get("backup_name")), PerfSavesTools.Ver(args), Py.Truthy(args.Get("overwrite")));
                lines.Add(Tr("备份文件：{name}").Replace("{name}", Py.S(plan.Get("backup"))));
                if (!Py.Truthy(plan.Get("backup_exists")))
                {
                    lines.Add(Tr("（没找到这个备份，执行时会报「备份不存在」）"));
                }
                lines.Add(Tr("目标存档：{name}").Replace("{name}", Py.S(plan.Get("target"))));
                lines.Add(Py.Truthy(plan.Get("overwrite")) ? Tr("覆盖现有存档") : Tr("恢复成新的存档文件夹，不动现有存档"));
                if (Py.Truthy(plan.Get("will_backup_current")))
                {
                    lines.Add(Tr("回滚前先把当前存档「{name}」备份成 zip").Replace("{name}", Py.S(plan.Get("world"))));
                }
                return Row("summary", Tr("将要从备份回滚存档："), lines);
            }

            default:
                return null;
        }
    }
}
