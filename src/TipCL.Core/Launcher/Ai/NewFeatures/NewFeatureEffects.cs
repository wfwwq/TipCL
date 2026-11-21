// AI-new-features:新写工具的「会改到哪」(检查点)与「改前改后」(确认卡预览)。
//
// 共享文件里只加了几行 case 转到这里(Tools.AffectedPaths / Preview.ChangePreview),逻辑都在本文件,
// 合并时冲突面只有那几行 case 标签。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>新工具的检查点路径与确认预览。</summary>
public static class NewFeatureEffects
{
    /// <summary>本包新增的全部工具名(只读 + 写)。</summary>
    public static readonly string[] TOOL_NAMES =
    {
        "plan_instance_upgrade", "apply_instance_upgrade", "preflight_check",
        "list_servers", "add_server", "remove_server", "ping_server",
        "recommend_shaders", "export_diagnostic_report",
    };

    private static string InstanceOf(PyDict args, string instance)
    {
        string name = Py.S(Py.Or(args.Get("instance"), instance));
        return name.Length > 0 ? name : Py.S(Config.CONFIG.Get("default_instance", "default"));
    }

    /// <summary>检查点要快照的路径(Tools.AffectedPaths 转过来)。认不出返回空清单 = 不做检查点。</summary>
    public static List<string> AffectedPaths(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        try
        {
            string instName = InstanceOf(args, instance);
            string root = Instances.GetInstancePath(instName);
            switch (name)
            {
                case "add_server":
                case "remove_server":
                    return new List<string> { PPath.Join(root, Servers.GAME_FILE), PPath.Join(root, Servers.SERVER_FILE) };
                case "apply_instance_upgrade":
                    if (Py.Truthy(args.Get("in_place")))
                    {
                        // 原地升级:mods 目录与实例元数据(新装的版本目录太大,不进内容快照;
                        // AI2-stability:它由 UpgradeCheckpoint 另记「新建子目录」名单,撤回时删掉,已有的版本不动)
                        return new List<string> { PPath.Join(root, "mods"), PPath.Join(root, ".instance.json") };
                    }
                    // 复制模式:新实例目录此刻不存在 ⇒ 回滚 = 删掉它,原实例一个字节不动
                    return new List<string> { Instances.GetInstancePath(InstanceUpgrade.NewInstanceName(args, instName)) };
                case "export_diagnostic_report":
                    return new List<string> { DiagnosticReport.ResolveOutPath(args, instName) };
            }
        }
        catch (Exception)
        {
        }
        return new List<string>();
    }

    private static PyDict Row(string kind, string head, List<string> lines)
        => PyDict.Of("kind", kind, "head", head, "lines", lines.Cast<object>().ToList(), "too_large", false);

    // AI2-i18n-ux:确认卡预览是用户看的,一律走词表;占位 {x} 在 en 译文里原样保留
    private static string Tr(string key) => I18nCore.Tr(key);

    private static string Tr(string key, params (string Name, object? Value)[] fill)
    {
        string s = I18nCore.Tr(key);
        foreach ((string name, object? value) in fill)
        {
            s = s.Replace("{" + name + "}", Py.S(value));
        }
        return s;
    }

    /// <summary>按 index 或 address(ip[:port])在实例的服务器列表里找一台;找不到返回 -1。</summary>
    public static int FindServerIndex(Instance inst, PyDict args)
    {
        List<object> list = Servers.ListServers(inst);
        object? idx = args.Get("index");
        if (idx != null && !(idx is string s0 && s0.Length == 0))
        {
            long i;
            try
            {
                i = Py.Int(idx);
            }
            catch (Exception)
            {
                return -1;
            }
            return i >= 0 && i < list.Count ? (int)i : -1;
        }
        string address = Py.Strip(Py.S(Py.Or(args.Get("address"), args.Get("ip"))));
        if (address.Length == 0)
        {
            string byName = Py.Strip(Py.S(args.Get("name")));
            for (int i = 0; i < list.Count; i++)
            {
                if (byName.Length > 0 && string.Equals(Py.S(Py.D(list[i]).Get("name")), byName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
        (string host, long port) = Servers._split_addr(address, args.Get("port"));
        for (int i = 0; i < list.Count; i++)
        {
            PyDict row = Py.D(list[i]);
            if (string.Equals(Py.S(row.Get("ip")), host, StringComparison.OrdinalIgnoreCase) && Py.Int(Py.Or(row.Get("port"), 25565L)) == port)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>确认卡预览(Preview.ChangePreview 转过来);返回 null = 没有可预览的。</summary>
    public static PyDict? Preview(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        string instName = InstanceOf(args, instance);
        var lines = new List<string>();
        switch (name)
        {
            case "add_server":
            {
                (string host, long port) = Servers._split_addr(Py.S(Py.Or(args.Get("address"), args.Get("ip"))), args.Get("port"));
                if (host.Length == 0)
                {
                    return null;
                }
                string label = Py.S(Py.Or(args.Get("name"), host));
                lines.Add("+ " + label + "  " + host + ":" + port);
                lines.Add(Tr("写入实例「{instance}」的 servers.dat(游戏内「多人游戏」列表)", ("instance", instName)));
                return Row("summary", Tr("将添加服务器："), lines);
            }
            case "remove_server":
            {
                var inst = new Instance(instName);
                int index;
                try
                {
                    index = FindServerIndex(inst, args);
                }
                catch (Exception)
                {
                    return null;
                }
                if (index < 0)
                {
                    lines.Add(Tr("（没找到这台服务器，执行时会报「不存在」）"));
                    return Row("summary", Tr("将移除服务器："), lines);
                }
                PyDict row = Py.D(Servers.ListServers(inst)[index]);
                lines.Add("- #" + index + " " + Py.S(row.Get("name")) + "  " + Py.S(row.Get("ip")) + ":" + Py.S(row.Get("port")));
                lines.Add(Tr("从实例「{instance}」的 servers.dat 里删掉（已做检查点，可撤回）", ("instance", instName)));
                return Row("summary", Tr("将移除服务器："), lines);
            }
            case "export_diagnostic_report":
            {
                string path = DiagnosticReport.ResolveOutPath(args, instName);
                lines.Add(path);
                lines.Add(Tr("内容：排查过程摘要 / 诊断结论 / 实例与模组清单 / 硬件摘要 / 日志关键片段"));
                lines.Add(Tr("账号令牌、UUID、含用户名的路径、邮箱会自动打码"));
                return Row("summary", Tr("将写入："), lines);
            }
            case "apply_instance_upgrade":
            {
                string mc = Py.S(args.Get("target_version"));
                if (mc.Length == 0)
                {
                    return null;
                }
                bool inPlace = Py.Truthy(args.Get("in_place"));
                string loaderArg = Py.S(args.Get("loader"));
                string curLoader = "";
                try
                {
                    curLoader = Mods.DetectLoader(new Instance(instName)) ?? "";
                }
                catch (Exception)
                {
                }
                string toLoader = InstanceUpgrade.NormalizeLoader(loaderArg.Length > 0 ? loaderArg : curLoader);
                if (inPlace)
                {
                    lines.Add("⚠ " + Tr("原地升级实例「{instance}」：mods 目录会被改写（已做检查点，可撤回）", ("instance", instName)));
                }
                else
                {
                    lines.Add(Tr("新建实例「{new}」，原实例「{instance}」与存档不动",
                        ("new", InstanceUpgrade.NewInstanceName(args, instName)), ("instance", instName)));
                    lines.Add(Tr("复制：") + string.Join(" ", InstanceUpgrade.CARRY_OVER));
                }
                lines.Add(Tr("安装 Minecraft {mc} + {loader}", ("mc", mc), ("loader", InstanceUpgrade.LoaderLabel(toLoader))));
                PyDict? plan = InstanceUpgrade.CachedPlan(instName, mc, toLoader);
                if (plan == null)
                {
                    lines.Add(Tr("模组清单执行时联网核对（建议先 plan_instance_upgrade 看清单）"));
                    return Row("summary", Tr("升级方案："), lines);
                }
                PyDict sum = Py.D(plan.Get("summary"));
                lines.Add(Tr("模组：升级 {upgrade} / 原样保留 {compatible} / 跳过 {missing}（目标版本没有）/ 认不出 {unknown}",
                    ("upgrade", sum.Get("upgrade")), ("compatible", sum.Get("compatible")),
                    ("missing", sum.Get("missing")), ("unknown", sum.Get("unknown"))));
                var skip = Py.ListOr(args.Get("skip")).Select(x => Py.S(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string dropped = inPlace ? Tr("将禁用") : Tr("不带过去");
                foreach (object raw in Py.ListOr(plan.Get("mods")).Take(60))
                {
                    PyDict row = Py.D(raw);
                    string mod = Py.S(row.Get("name"));
                    if (skip.Contains(Py.S(row.Get("filename"))))
                    {
                        lines.Add("  ⊘ " + mod + Tr("（用户要求跳过）"));
                        continue;
                    }
                    lines.Add(Py.S(row.Get("status")) switch
                    {
                        "upgrade" => "  ↑ " + mod + ": " + Py.S(row.Get("current")) + " → " + Py.S(row.Get("target_version")),
                        "compatible" => "  = " + mod + Tr("（当前文件已支持 {mc}）", ("mc", mc)),
                        "missing" => "  ✗ " + mod + Tr("（目标版本没有，{action}）", ("action", dropped)),
                        _ => "  ? " + mod + Tr("（认不出来源，{action}）", ("action", dropped)),
                    });
                }
                foreach (object raw in Py.ListOr(plan.Get("dependency_changes")).Take(10))
                {
                    PyDict d = Py.D(raw);
                    var need = Py.ListOr(d.Get("need_install")).Select(x => Py.S(x)).ToList();
                    if (need.Count > 0)
                    {
                        lines.Add("  " + Tr("前置变化：{mod} 新增需要 {need}（装完后需补装）",
                            ("mod", d.Get("mod")), ("need", string.Join(", ", need))));
                    }
                }
                return Row("summary", Tr("升级方案："), lines);
            }
        }
        return null;
    }
}
