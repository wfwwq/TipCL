// AI2-modpack:整合包制作工具的界面文案 —— 步骤行 / 确认卡的标签、确认卡里的「将要写入什么」预览、计划卡步骤标题。
// 这里的中文都是用户能看到的,一律走 I18nCore.Tr(词条在 Resources/locales/{zh_CN,en}.json)。
// 预览结构(kind / head / lines / too_large)与 Preview.ChangePreview 一致,由它在 default 分支转给这里。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>整合包工具的标签、确认预览与计划卡步骤。</summary>
public static class ModpackPreview
{
    private static string Tr(string key) => I18nCore.Tr(key);

    /// <summary>确认卡里每一类最多列这么多行(其余折成「另有 N 个」)。</summary>
    public const int MAX_LIST = 40;

    /// <summary>工具标签;不是本包的工具返回 null(交回 Tools.Label 原逻辑)。</summary>
    public static string? Label(string name, PyDict? args)
    {
        switch (name)
        {
            case "check_modpack_distribution":
                return Tr("查整合包分发许可");
            case "export_modpack":
            {
                string inst = Py.S(args?.Get("instance"));
                return inst.Length > 0
                    ? Tr("导出整合包 {instance}").Replace("{instance}", inst)
                    : Tr("导出整合包");
            }
            case "plan_modpack":
            {
                string theme = Py.S(args?.Get("theme"));
                return theme.Length > 0
                    ? Tr("出整合包候选清单「{theme}」").Replace("{theme}", theme)
                    : Tr("出整合包候选清单");
            }
            case "install_mods_batch":
            {
                int n = args == null ? 0 : ModpackTools.BatchItems(args).Count;
                string inst = Py.S(args?.Get("instance"));
                return n > 0 && inst.Length > 0
                    ? Tr("批量安装 {n} 个模组 → {instance}").Replace("{n}", n.ToString()).Replace("{instance}", inst)
                    : Tr("批量安装模组");
            }
            default:
                return null;
        }
    }

    /// <summary>一句话造包的计划卡步骤(plan_modpack 推到计划卡,也原样回给模型,后续 update_plan 用同样的标题)。</summary>
    public static List<string> PlanSteps(string mc, string loader) => new()
    {
        Tr("确认模组清单"),
        Tr("新建实例并安装 Minecraft {mc} + {loader}").Replace("{mc}", mc).Replace("{loader}", InstanceUpgrade.LoaderLabel(loader)),
        Tr("批量安装确认的模组"),
        Tr("检查冲突并做启动前预检"),
        Tr("(可选)试启动并读日志"),
        Tr("导出整合包"),
    };

    /// <summary>批量安装时的步骤行进度。</summary>
    public static string BatchProgress(int i, int n, string name)
        => Tr("({i}/{n})安装 {name}").Replace("{i}", i.ToString()).Replace("{n}", n.ToString()).Replace("{name}", name);

    /// <summary>字节数 → 人读的大小。</summary>
    public static string FmtBytes(long n)
    {
        if (n < 1024)
        {
            return n + " B";
        }
        if (n < 1024 * 1024)
        {
            return (n / 1024.0).ToString("0.#") + " KB";
        }
        if (n < 1024L * 1024 * 1024)
        {
            return (n / 1024.0 / 1024.0).ToString("0.#") + " MB";
        }
        return (n / 1024.0 / 1024.0 / 1024.0).ToString("0.##") + " GB";
    }

    private static PyDict Row(string head, List<string> lines, bool tooLarge)
        => PyDict.Of("kind", "summary", "head", head, "lines", lines.Cast<object>().ToList(), "too_large", tooLarge);

    private static string InstanceOf(PyDict args, string instance)
    {
        string name = Py.S(Py.Or(args.Get("instance"), instance));
        return name.Length > 0 ? name : Py.S(Config.CONFIG.Get("default_instance", "default"));
    }

    /// <summary>把一类清单列进 lines(最多 MAX_LIST 行),返回是否被截断。</summary>
    private static bool ListInto(List<string> lines, IEnumerable<string> items)
    {
        var all = items.ToList();
        foreach (string s in all.Take(MAX_LIST))
        {
            lines.Add(s);
        }
        if (all.Count > MAX_LIST)
        {
            lines.Add("  " + Tr("……另有 {n} 项").Replace("{n}", (all.Count - MAX_LIST).ToString()));
            return true;
        }
        return false;
    }

    /// <summary>本包写工具的确认预览;不是本包的工具返回 null。</summary>
    public static PyDict? ChangePreview(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        switch (name)
        {
            case "export_modpack":
                return ExportPreview(args, instance);
            case "install_mods_batch":
                return BatchPreview(args, instance);
            default:
                return null;
        }
    }

    private static PyDict ExportPreview(PyDict args, string instance)
    {
        string instName = InstanceOf(args, instance);
        var inst = new Instance(instName);
        var lines = new List<string>();
        string head = Tr("将导出整合包：");
        if (!LocalFs.IsDir(inst.path))
        {
            lines.Add(Tr("实例不存在：{name}").Replace("{name}", instName));
            return Row(head, lines, false);
        }
        ModpackExportOptions opt = ModpackTools.OptionsFrom(args);
        string outPath = ModpackTools.ResolveOutPath(args, instName);
        PyDict plan;
        try
        {
            plan = ModpackExport.BuildPlan(inst, opt);
        }
        catch (Exception e)
        {
            lines.Add(Tr("文件：{path}").Replace("{path}", outPath));
            lines.Add(Tr("清单算不出来：{error}").Replace("{error}", Py.ExcStr(e)));
            return Row(head, lines, false);
        }
        bool truncated = false;
        string format = Py.S(plan.Get("format"));
        lines.Add(Tr("文件：{path}").Replace("{path}", outPath));
        lines.Add(Tr("格式：{format} · Minecraft {mc} · {loader}")
            .Replace("{format}", format == "curseforge" ? "CurseForge (manifest.json)" : "Modrinth (.mrpack)")
            .Replace("{mc}", Py.S(plan.Get("mc_version")).Length > 0 ? Py.S(plan.Get("mc_version")) : "?")
            .Replace("{loader}", (Py.S(plan.Get("loader")) + " " + Py.S(plan.Get("loader_version"))).Trim() is { Length: > 0 } l ? l : Tr("原版")));

        List<object> forbidden = Py.ListOr(plan.Get("forbidden"));
        if (forbidden.Count > 0)
        {
            lines.Add(Tr("⚠ 禁止分发（导出会被拒绝，请先替换或排除）：{n} 个").Replace("{n}", forbidden.Count.ToString()));
            truncated |= ListInto(lines, forbidden.Select(x => "  ✗ " + Py.S(Py.D(x).Get("filename"))));
        }
        List<object> linked = Py.ListOr(plan.Get("linked"));
        lines.Add(Tr("链接下载的模组（包里不带 jar，装包时从官方源下载）：{n} 个").Replace("{n}", linked.Count.ToString()));
        truncated |= ListInto(lines, linked.Select(x => "  ↗ " + Py.S(Py.D(x).Get("path")) + " (" + FmtBytes(Py.Int(Py.D(x).Get("size"))) + ")"));
        List<object> skipped = Py.ListOr(plan.Get("skipped_jars"));
        if (skipped.Count > 0)
        {
            lines.Add(Tr("不打包（来源不明或许可未知，可能侵权；用户同意后才能打包）：{n} 个").Replace("{n}", skipped.Count.ToString()));
            truncated |= ListInto(lines, skipped.Select(x => "  ⊘ " + Py.S(Py.D(x).Get("filename"))));
        }
        List<object> excluded = Py.ListOr(plan.Get("excluded"));
        if (excluded.Count > 0)
        {
            lines.Add(Tr("按要求排除：{n} 个").Replace("{n}", excluded.Count.ToString()));
            truncated |= ListInto(lines, excluded.Select(x => "  − " + Py.S(x)));
        }
        List<object> files = Py.ListOr(plan.Get("files"));
        var dirs = Py.ListOr(plan.Get("overrides")).Select(x => Py.S(x)).ToList();
        lines.Add(Tr("写进包里的文件（overrides：{dirs}）：{n} 个")
            .Replace("{dirs}", dirs.Count > 0 ? string.Join(" / ", dirs) : "-")
            .Replace("{n}", files.Count.ToString()));
        truncated |= ListInto(lines, files.Select(x => "  + " + Py.S(Py.D(x).Get("zip_path")) + " (" + FmtBytes(Py.Int(Py.D(x).Get("size"))) + ")"));
        if (Py.ListOr(plan.Get("bundled_jars")).Count > 0 && opt.AllowBundledJars)
        {
            lines.Add(Tr("用户已同意把来源不明的 jar 打进包，分发前请确认作者许可"));
        }
        lines.Add(Tr("包内文件合计约 {size}（压缩前）；装包时另需下载约 {linked}")
            .Replace("{size}", FmtBytes(Py.Int(plan.Get("total_bytes"))))
            .Replace("{linked}", FmtBytes(Py.Int(plan.Get("linked_bytes")))));
        PyDict dist = Py.D(plan.Get("distribution"));
        int errors = Py.ListOr(dist.Get("lookup_errors")).Count;
        if (errors > 0)
        {
            lines.Add(Tr("有 {n} 项联网核对失败，相关模组按来源不明处理").Replace("{n}", errors.ToString()));
        }
        foreach (object w in Py.ListOr(plan.Get("warnings")))
        {
            lines.Add("⚠ " + Py.S(w));
        }
        return Row(head, lines, truncated);
    }

    private static PyDict BatchPreview(PyDict args, string instance)
    {
        string instName = InstanceOf(args, instance);
        List<PyDict> items = ModpackTools.BatchItems(args);
        var lines = new List<string>();
        bool truncated = ListInto(lines, items.Select(m =>
        {
            string slug = Py.S(Py.Or(m.Get("slug"), m.Get("id")));
            string label = ModpackTools.ItemName(m);
            return "  + " + label + (slug.Length > 0 && slug != label ? " (" + slug + ")" : "") + " · " + Py.S(m.Get("source"));
        }));
        lines.Add(Tr("装到实例「{instance}」的 mods 目录，必需的前置会一起装上").Replace("{instance}", instName));
        if (items.Count > ModpackTools.MAX_BATCH)
        {
            lines.Add(Tr("超过一次最多 {n} 个的上限，执行时会被拒绝").Replace("{n}", ModpackTools.MAX_BATCH.ToString()));
        }
        string head = Tr("将批量安装 {n} 个模组（整批只确认这一次）：").Replace("{n}", items.Count.ToString());
        return Row(head, lines, truncated);
    }
}
