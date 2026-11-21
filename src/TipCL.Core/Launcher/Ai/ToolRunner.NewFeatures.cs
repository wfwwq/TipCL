// AI-new-features:新工具的执行分发(ToolRunner 的 partial;共享的 ExecuteAsync switch 里只加了几行 case 转到这里)。
//
// 工具清单(风险分级见 AiToolTypes.cs 的 ToolMeta.ALL 末尾):
//   plan_instance_upgrade(只读+联网)/ apply_instance_upgrade(写,high)/ preflight_check(只读)/
//   list_servers(只读)/ add_server(写)/ remove_server(删除,high)/ ping_server(只读+联网)/
//   recommend_shaders(只读+联网)/ export_diagnostic_report(写本地文件)。
// 写工具一律走 RunAsync 的同一条确认链:ToolMeta 风险分级 → Decide → Confirm(带 NewFeatureEffects.Preview 预览)
// → AiAgent 在执行前按 NewFeatureEffects.AffectedPaths 做检查点。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>
    /// 当前对话的消息列表(export_diagnostic_report 要把排查过程写进报告)。AI 页注入;没注入时报告里写「还没有排查记录」。
    /// </summary>
    public Func<List<object>>? ConversationProvider { get; set; }

    /// <summary>
    /// 这个工具能不能给「本对话都允许」(D-24:删除 / 高危类不给)。本包新增的两个危险工具在执行器里也守这条:
    /// 就算界面传回 always=true,也只按「仅这一次」处理。既有的 delete_mod / delete_instance 仍只由确认卡
    /// (ConfirmCard.AllowAlways)把关,行为不变(已有用例锁着)。AI-integration:确认卡是否显示那个按钮现在只看
    /// ConfirmCard.AllowAlways(remove_server / apply_instance_upgrade / fs_write 已并入那张名单),这里是执行器侧的双保险。
    /// </summary>
    public static bool AllowsAlways(string tool) => tool switch
    {
        "remove_server" or "apply_instance_upgrade" => false,
        // AI-integration:fs_write 也在执行器里守一道(确认卡 ConfirmCard.AllowAlways 已不给;restore_save_backup 由 NeverAlwaysAllow 守)
        "fs_write" => false,
        _ => true,
    };

    private static Instance ExistingInstance(string name)
    {
        var inst = new Instance(name.Length > 0 ? name : null);
        if (!LocalFs.IsDir(inst.path))
        {
            throw new FileNotFoundError("实例不存在: " + (name.Length > 0 ? name : inst.name));
        }
        return inst;
    }

    private async Task<object?> ExecuteNewFeaturesAsync(ToolCall call, PyDict args, CancellationToken ct)
    {
        switch (call.name)
        {
            case "plan_instance_upgrade":
            {
                Instance inst = ExistingInstance(InstName(args));
                string target = Py.S(args.Get("target_version"));
                string loader = Py.S(args.Get("loader"));
                return await Task.Run(() => InstanceUpgrade.Plan(inst, target, loader), ct).ConfigureAwait(false);
            }

            case "apply_instance_upgrade":
                return await ApplyUpgradeAsync(args, ct).ConfigureAwait(false);

            case "preflight_check":
            {
                Instance inst = ExistingInstance(InstName(args));
                string version = Py.S(args.Get("version"));
                long memory = Py.Int(Py.Or(args.Get("memory_mb"), 0L));
                string java = Py.S(args.Get("java"));
                return await Task.Run(() => AiPreflight.Check(inst, version, memory, java), ct).ConfigureAwait(false);
            }

            case "list_servers":
            {
                Instance inst = ExistingInstance(InstName(args));
                List<object> servers = Servers.ListServers(inst);
                return PyDict.Of("instance", inst.name, "count", (long)servers.Count, "servers", servers);
            }

            case "add_server":
            {
                Instance inst = ExistingInstance(InstName(args));
                (string host, long port) = Servers._split_addr(Py.S(Py.Or(args.Get("address"), args.Get("ip"))), args.Get("port"));
                if (host.Length == 0)
                {
                    throw new ValueError("缺少服务器地址(address)");
                }
                PyDict added = Servers.AddServer(inst, Py.Or(args.Get("name"), host), host, port, args.Get("description"));
                return PyDict.Of("ok", true, "instance", inst.name, "server", added, "message", "已添加到游戏内「多人游戏」列表");
            }

            case "remove_server":
            {
                Instance inst = ExistingInstance(InstName(args));
                int index = NewFeatureEffects.FindServerIndex(inst, args);
                if (index < 0)
                {
                    throw new ServerError("没找到这台服务器(先 list_servers 看 index)");
                }
                PyDict removed = Py.D(Servers.ListServers(inst)[index]);
                Servers.DeleteServer(inst, (long)index);
                return PyDict.Of("ok", true, "instance", inst.name, "removed", removed);
            }

            case "ping_server":
            {
                string address = Py.S(Py.Or(args.Get("address"), args.Get("ip")));
                object? port = args.Get("port");
                if (address.Length == 0 && args.Get("index") != null)
                {
                    Instance inst = ExistingInstance(InstName(args));
                    int index = NewFeatureEffects.FindServerIndex(inst, args);
                    if (index < 0)
                    {
                        throw new ServerError("没找到这台服务器(先 list_servers 看 index)");
                    }
                    PyDict row = Py.D(Servers.ListServers(inst)[index]);
                    address = Py.S(row.Get("ip"));
                    port = row.Get("port");
                }
                (string host, long p) = Servers._split_addr(address, port);
                int timeout = (int)Math.Clamp(Py.Int(Py.Or(args.Get("timeout_ms"), 5000L)), 200L, 30000L);
                return await ServerPing.PingAsync(host, p, timeout, ct).ConfigureAwait(false);
            }

            case "recommend_shaders":
            {
                string kind = Py.Lower(Py.S(Py.Or(args.Get("kind"), "shader")));
                kind = kind is "resourcepack" or "resourcepacks" or "资源包" or "材质包" ? "resourcepack" : "shader";
                string query = Py.S(args.Get("query"));
                if (query.Length == 0)
                {
                    query = kind == "shader" ? "shader" : "resource pack";
                }
                string method = kind == "shader" ? "search_shaders" : "search_resourcepacks";
                List<object> results = await _api.CallAsync<List<object>>(method,
                    new { query, source = Py.Or(args.Get("source"), "全部") }, ct).ConfigureAwait(false) ?? new List<object>();
                PyDict hw = await Task.Run(() => ShaderAdvisor.ProbeHardware(), ct).ConfigureAwait(false);
                PyDict? loader = null;
                if (kind == "shader")
                {
                    try
                    {
                        loader = ShaderAdvisor.ShaderLoader(new Instance(InstName(args).Length > 0 ? InstName(args) : null));
                    }
                    catch (Exception)
                    {
                        loader = null;
                    }
                }
                return ShaderAdvisor.Recommend(results, hw, kind, loader);
            }

            case "export_diagnostic_report":
            {
                Instance inst = ExistingInstance(InstName(args));
                string outPath = DiagnosticReport.ResolveOutPath(args, inst.name);
                List<object>? conversation = ConversationProvider?.Invoke();
                return await Task.Run(() => DiagnosticReport.Export(inst, conversation, outPath), ct).ConfigureAwait(false);
            }

            default:
                return ToolErr(call.name, "unknown_tool", "没有这个工具: " + call.name, "");
        }
    }

    /// <summary>apply_instance_upgrade:配合 update_plan 计划卡逐步推进(核对 → 复制 → 装版本 → 换模组)。</summary>
    private async Task<object?> ApplyUpgradeAsync(PyDict args, CancellationToken ct)
    {
        Instance inst = ExistingInstance(InstName(args));
        string target = Py.S(args.Get("target_version"));
        if (target.Length == 0)
        {
            throw new ValueError("缺少目标 MC 版本(target_version)");
        }
        bool inPlace = Py.Truthy(args.Get("in_place"));
        // AI2-i18n-ux:计划卡标题是用户看的,走词表
        string[] titles =
        {
            I18nCore.Tr("核对每个模组在 {mc} 上有没有对应版本").Replace("{mc}", target),
            inPlace ? I18nCore.Tr("原地升级「{instance}」(已做检查点)").Replace("{instance}", inst.name) : I18nCore.Tr("复制为新实例(config / 存档 / 资源包 / 光影)"),
            I18nCore.Tr("安装 Minecraft {mc} 与加载器").Replace("{mc}", target),
            I18nCore.Tr("下载升级后的模组,跳过目标版本没有的"),
        };
        void Show(int done)
        {
            var items = new List<(string Title, string Status)>();
            for (int i = 0; i < titles.Length; i++)
            {
                items.Add((titles[i], i < done ? "completed" : (i == done ? "in_progress" : "pending")));
            }
            PlanChanged?.Invoke(items);
            UpgradeStage(done, inPlace); // AI2-i18n-ux:步骤行「执行中」后面带上当前阶段
        }
        Show(0);
        object? InstallGame(string mc, string loaderLabel, string instance)
        {
            object? res = _api.CallAsync<object>("install_game", new
            {
                version = mc,
                loader = loaderLabel,
                loader_version = (object?)null,
                instance,
            }, ct).ConfigureAwait(false).GetAwaiter().GetResult();
            PyDict row = Py.D(res);
            string taskId = Py.S(Py.Or(row.Get("task_id"), row.Get("id")));
            if (taskId.Length > 0)
            {
                TaskStarted?.Invoke(taskId, I18nCore.Tr("安装 {mc} → {instance}").Replace("{mc}", mc).Replace("{instance}", instance), "install_game");
            }
            return res;
        }
        PyDict result = await Task.Run(() => InstanceUpgrade.Apply(inst, args, InstallGame, step => Show(step + 1)), ct).ConfigureAwait(false);
        Show(titles.Length);
        return await AfterUpgradePreflightAsync(result, ct).ConfigureAwait(false); // AI2-i18n-ux:最后一段「预检」
    }
}
