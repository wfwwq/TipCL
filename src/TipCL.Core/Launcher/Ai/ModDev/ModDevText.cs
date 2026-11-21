// AI2-moddev-mcp:本包工具上界面的文案 —— 步骤行标签、确认卡「改前改后」预览、非测试实例的确认理由。
// 全部走 I18nCore.Tr(词表在 locales/zh_CN.json / en.json,紧跟 MCP 那几条之后)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>本包工具的标签 / 预览 / 确认理由(界面文案)。</summary>
public static class ModDevText
{
    /// <summary>目标不是测试实例时,确认卡上「为什么要你确认」那一行。</summary>
    public static string NonDevReason()
        => I18nCore.Tr("目标实例不是测试实例(dev):往日常实例装本地 jar 风险更高,请确认这是你要的");

    /// <summary>步骤行 / 确认卡的标签;不是本包的工具返回 null。</summary>
    public static string? Label(string name, PyDict? args)
    {
        if (!ModDevSchemas.Handles(name))
        {
            return null;
        }
        args ??= new PyDict();
        string arg = name switch
        {
            "create_test_instance" => (Py.S(args.Get("mc_version")) + " " + Py.S(args.Get("loader"))).Trim(),
            "install_local_mod" => Path.GetFileName(Py.S(args.Get("jar_path"))),
            "revert_local_mod" => Py.S(args.Get("change_id")),
            "run_test_launch" => Py.S(args.Get("instance")),
            _ => Py.S(args.Get("task_id")),
        };
        string label = name switch
        {
            "create_test_instance" => I18nCore.Tr("建测试实例"),
            "install_local_mod" => I18nCore.Tr("装本地模组"),
            "revert_local_mod" => I18nCore.Tr("撤回本地模组"),
            "run_test_launch" => I18nCore.Tr("测试启动"),
            _ => I18nCore.Tr("查任务进度"),
        };
        return arg.Length > 0 ? label + " " + arg : label;
    }

    private static PyDict Row(string head, List<string> lines)
        => PyDict.Of("kind", "summary", "head", head, "lines", lines.Cast<object>().ToList(), "too_large", false);

    /// <summary>确认卡的「将要发生什么」;不是本包的写工具返回 null。</summary>
    public static PyDict? ChangePreview(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        var lines = new List<string>();
        switch (name)
        {
            case "create_test_instance":
            {
                string path = ModDevOps.PlannedInstancePath(args);
                if (path.Length > 0)
                {
                    lines.Add("+ " + path);
                }
                lines.Add(I18nCore.Tr("安装 Minecraft {mc} {loader}")
                    .Replace("{mc}", Py.S(args.Get("mc_version")))
                    .Replace("{loader}", Py.S(args.Get("loader"))).Trim());
                List<string> deps = ModDevOps.Dependencies(args);
                if (deps.Count > 0)
                {
                    lines.Add(I18nCore.Tr("依赖模组: {deps}").Replace("{deps}", string.Join(", ", deps)));
                }
                return Row(I18nCore.Tr("将要新建测试实例:"), lines);
            }
            case "install_local_mod":
            {
                ModDevOps.InstallPlan plan = ModDevOps.Plan(args, instance);
                if (!plan.Ok)
                {
                    lines.Add("✗ " + plan.ErrorMessage);
                    return Row(I18nCore.Tr("这一步会被拒绝:"), lines);
                }
                ModJarInfo info = plan.Info!;
                lines.Add("+ mods/" + Path.GetFileName(plan.Target) + "  (" + info.Id + " " + info.Version + " · " + string.Join("/", info.Loaders) + ")");
                foreach (string old in plan.Replaced)
                {
                    lines.Add("- mods/" + Path.GetFileName(old) + " → " + ModDevOps.ReplacedDir + "/");
                }
                if (!plan.Dev)
                {
                    lines.Add("⚠ " + NonDevReason());
                }
                foreach (string w in plan.Warnings)
                {
                    lines.Add("! " + w);
                }
                lines.Add(I18nCore.Tr("会先做检查点,可用 revert_local_mod 撤回"));
                return Row(I18nCore.Tr("将要安装本地模组到「{instance}」:").Replace("{instance}", plan.Inst!.name), lines);
            }
            case "revert_local_mod":
                lines.Add(I18nCore.Tr("按检查点 {id} 还原:删掉那次装进去的 jar,放回被替换的旧 jar").Replace("{id}", Py.S(args.Get("change_id"))));
                return Row(I18nCore.Tr("将要撤回:"), lines);
            case "run_test_launch":
            {
                string inst = Py.S(Py.Or(args.Get("instance"), instance));
                long timeout = Py.Int(Py.Or(args.Get("timeout_seconds"), (long)TestLaunch.DefaultTimeoutSeconds));
                lines.Add(I18nCore.Tr("实例 {instance} · 版本 {version}")
                    .Replace("{instance}", inst)
                    .Replace("{version}", Py.S(args.Get("version")) is { Length: > 0 } v ? v : I18nCore.Tr("自动")));
                lines.Add(I18nCore.Tr("等到进入主菜单、崩溃或超时({n} 秒)后自动关闭游戏").Replace("{n}", timeout.ToString()));
                return Row(I18nCore.Tr("将要测试启动:"), lines);
            }
            default:
                return null;
        }
    }
}
