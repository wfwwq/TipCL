// AI2-content:数据包 / 资源包 / KubeJS 工具在界面上的文案 —— 步骤行 / 确认卡的标签,以及确认卡里的「改前改后」预览。
// 这里的中文都是用户能看到的,一律走 I18nCore.Tr(词条在 Resources/locales/{zh_CN,en}.json)。
// 预览直接用 ContentTools 的同一份计划(Plan)算:给用户看的 diff 就是执行时要写的内容。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>内容制作工具的标签与改前改后预览。</summary>
public static class ContentPreview
{
    private static string Tr(string key) => I18nCore.Tr(key);

    /// <summary>工具标签;不是本包的工具返回 null(交回 Tools.Label 原逻辑)。</summary>
    public static string? Label(string name, PyDict? args)
    {
        string? label = name switch
        {
            "create_datapack" => Tr("生成数据包"),
            "create_resourcepack" => Tr("生成资源包"),
            "write_kubejs_script" => Tr("写 KubeJS 脚本"),
            "validate_content" => Tr("检查内容加载错误"),
            _ => null,
        };
        if (label == null || args == null)
        {
            return label;
        }
        string arg = name switch
        {
            "create_datapack" or "create_resourcepack" => Py.S(args.Get("name")),
            "write_kubejs_script" => Py.S(args.Get("name")),
            _ => "",
        };
        return arg.Length > 0 ? label + " " + arg : label;
    }

    private static PyDict Row(string kind, string head, List<string> lines, bool tooLarge = false)
        => PyDict.Of("kind", kind, "head", head, "lines", lines.Cast<object>().ToList(), "too_large", tooLarge);

    /// <summary>算预览;不是本包的写工具返回 null。纯读,不改盘(计划本身只读文件)。</summary>
    public static PyDict? ChangePreview(string name, PyDict? args, string instance)
    {
        if (name is not ("create_datapack" or "create_resourcepack" or "write_kubejs_script"))
        {
            return null;
        }
        ContentTools.Plan? plan;
        try
        {
            plan = ContentTools.PlanFor(name, args, instance);
        }
        catch (Exception ex)
        {
            // 校验不过:执行时同样会被拒,卡片上直接说原因,免得用户点了「允许」才看到失败
            return Row("info", Tr("这一步会被拒绝："), new List<string> { Py.ExcStr(ex) });
        }
        if (plan == null)
        {
            return null;
        }
        var lines = new List<string> { Tr("位置：{path}").Replace("{path}", plan.Root) };
        if (plan.Mc.Length > 0)
        {
            lines.Add(Tr("MC {mc}，格式号 {format}").Replace("{mc}", plan.Mc).Replace("{format}", plan.Format));
        }
        if (name == "write_kubejs_script")
        {
            lines.Add(Tr("静态检查通过：没有发现文件、网络、进程或反射类的用法。脚本会在游戏里运行，请确认内容可信。"));
        }
        if (plan.SuspiciousNamespaces.Count > 0)
        {
            // AI3-content:引用的 ID 命名空间没有来头 —— 只提醒,照样可以写
            lines.Add(Tr("提醒：这些命名空间既不是 minecraft 也不是已装模组，可能拼错了或模组没装（不影响写入）：{list}")
                .Replace("{list}", string.Join(", ", plan.SuspiciousNamespaces)));
        }
        var diff = new List<string>();
        foreach (ContentTools.Write w in plan.Writes)
        {
            if (!w.Changed)
            {
                lines.Add(Tr("未变化：{file}").Replace("{file}", w.Rel));
                continue;
            }
            if (w.Bytes != null)
            {
                diff.Add(Tr("（贴图）{file}：{w}×{h} PNG，{kb} KB")
                    .Replace("{file}", w.Rel).Replace("{w}", w.Width.ToString()).Replace("{h}", w.Height.ToString())
                    .Replace("{kb}", Math.Max(1, (w.Bytes.Length + 1023) / 1024).ToString()));
                continue;
            }
            if (!w.Existed)
            {
                diff.Add(Tr("（新建文件）"));
            }
            diff.AddRange(Preview.UnifiedDiff(Py.SplitLines(w.OldText ?? ""), Py.SplitLines(w.Text ?? ""), "a/" + w.Rel, "b/" + w.Rel));
        }
        bool tooLarge = false;
        if (diff.Count > Preview.MAX_PREVIEW_LINES)
        {
            tooLarge = true;
            string stored = Artifacts.Save(string.Join("\n", diff), name);
            lines.AddRange(diff.Take(Preview.MAX_PREVIEW_LINES));
            lines.Add(stored.Length > 0
                ? Tr("变更过大，完整内容见 {path}").Replace("{path}", stored)
                : Tr("变更过大，仅显示前 {n} 行").Replace("{n}", Preview.MAX_PREVIEW_LINES.ToString()));
        }
        else
        {
            lines.AddRange(diff);
        }
        return Row("diff", Tr("将要写入的变更："), lines, tooLarge);
    }
}
