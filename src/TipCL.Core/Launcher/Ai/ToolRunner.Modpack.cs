// AI2-modpack:整合包制作工具的执行分发(ToolRunner 的 partial;共享的 ExecuteAsync switch 里只加了几行 case 转到这里)。
//
// 写工具(export_modpack / install_mods_batch)一律走 RunAsync 的同一条确认链:ToolMeta 风险分级 → Decide →
// Confirm(带 ModpackPreview 的「将写入的文件清单 + 大小估计」/「逐项列出的模组」)→ AiAgent 在执行前按
// ModpackTools.AffectedPaths 做检查点;两者都不给「本对话都允许」(ToolRunner.AllowsAlways + ConfirmCard.AllowAlways)。
// 本文件不含界面文案:报错文字在 ModpackTools / ModpackPlanner(给模型看),界面文字在 ModpackPreview(走 Tr)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    private async Task<object?> ExecuteModpackAsync(ToolCall call, PyDict args, CancellationToken ct)
    {
        switch (call.name)
        {
            case "check_modpack_distribution":
            {
                // 先把参数当字符串读一遍(自检探针在这里就会停下,不会联网)
                string format = ModpackDistribution.NormalizeFormat(Py.S(args.Get("format")));
                Instance inst = ExistingInstance(Py.S(args.Get("instance")).Length > 0 ? Py.S(args.Get("instance")) : InstName(args));
                PyDict result = await Task.Run(() => ModpackDistribution.Check(inst, format, ModpackSources.Get(), null, useCache: false), ct).ConfigureAwait(false);
                return ModpackTools.CompactCheck(result);
            }

            case "export_modpack":
            {
                string format = ModpackDistribution.NormalizeFormat(Py.S(args.Get("format")));
                Instance inst = ExistingInstance(Py.S(args.Get("instance")).Length > 0 ? Py.S(args.Get("instance")) : InstName(args));
                ModpackExportOptions opt = ModpackTools.OptionsFrom(args);
                opt.Format = format;
                string outPath = ModpackTools.ResolveOutPath(args, inst.name);
                return await Task.Run(() => ModpackExport.Export(inst, outPath, opt), ct).ConfigureAwait(false);
            }

            case "plan_modpack":
            {
                string mc = Py.S(args.Get("mc_version"));
                string loader = Py.S(args.Get("loader"));
                string theme = Py.S(args.Get("theme"));
                string size = Py.S(args.Get("size"));
                List<string> keywords = args.Get("keywords") is string kw
                    ? kw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList()
                    : Py.ListOr(args.Get("keywords")).Select(x => Py.S(x)).ToList();
                PyDict plan = await Task.Run(() => ModpackPlanner.Plan(mc, loader, theme, size, keywords, ModpackSources.Get()), ct).ConfigureAwait(false);
                if (Py.Truthy(plan.Get("ok")))
                {
                    List<string> steps = ModpackPreview.PlanSteps(Py.S(plan.Get("mc_version")), Py.S(plan.Get("loader")));
                    plan["plan_steps"] = steps.Cast<object>().ToList();
                    PlanChanged?.Invoke(steps.Select((t, i) => (t, i == 0 ? "in_progress" : "pending")).ToList());
                }
                return plan;
            }

            case "install_mods_batch":
            {
                string name = Py.S(args.Get("instance"));
                Instance inst = ExistingInstance(name.Length > 0 ? name : InstName(args));
                List<PyDict> items = ModpackTools.CheckBatch(args); // 空 / 超上限:现在就报错,什么都不起
                // AI3-content:交给宿主起后台任务(任务卡逐项显示进度),工具立刻返回 task_id,AI 页不再长时间「执行中」。
                // 确认已在 RunAsync 里整批问过一次;宿主不支持这个方法(如测试里的假后端)时退回原来的同步执行。
                var payload = new PyDict(args) { ["instance"] = inst.name };
                string taskId = await StartBatchTaskAsync(payload, ct).ConfigureAwait(false);
                if (taskId.Length == 0)
                {
                    return await Task.Run(() => ModpackTools.InstallBatch(inst, args, Log, null, ct), ct).ConfigureAwait(false);
                }
                TaskStarted?.Invoke(taskId, ModpackPreview.BatchTaskTitle(items.Count, inst.name), "install_mods_batch");
                return ModpackTools.BatchStarted(taskId, inst, items);
            }

            default:
                return ToolErr(call.name, "unknown_tool", "unknown tool: " + call.name, "");
        }
    }

    /// <summary>AI3-content:让宿主起 install_mods_batch 后台任务,返回任务 id;宿主没有这个方法时返回空串。</summary>
    private async Task<string> StartBatchTaskAsync(PyDict payload, CancellationToken ct)
    {
        object? result;
        try
        {
            result = await _api.CallAsync<object>("install_mods_batch", payload, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is NotImplementedException || e is NotSupportedException)
        {
            return "";
        }
        if (result is string direct)
        {
            return direct;
        }
        return result is PyDict row ? Py.S(Py.Or(row.Get("task_id"), row.Get("id"))) : "";
    }
}
