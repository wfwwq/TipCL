// AI2-stability:apply_instance_upgrade 原地升级的检查点补充 —— 把「这次新装的版本目录」也纳入撤回。
//
// NewFeatureEffects.AffectedPaths 给原地升级的快照只有 mods/ 与 .instance.json(版本目录太大不进内容快照),
// 撤回后会留下新装的目标版本目录。这里在同一轮里再记一条「versions/ 下新建子目录」操作(Checkpoint.SnapshotNewChildren),
// 撤回时删掉本次新建、且属于目标 MC 版本的版本目录;已存在的版本目录不删、不改。复制模式不需要(整份新实例目录本来就会被删)。
// AI3-stability:工具执行完再补记一次(CaptureAfter)—— 撤回按「执行前后目录集合的差集」删,认得出 OptiFine / Forge 安装器等
// 不继承原版、id 另起名字的目录(口径见 Checkpoint.NewChildren.cs 文件头)。
using System;
using System.Collections.Generic;

namespace TipCL.Core.Launcher.Ai;

/// <summary>写工具在内容快照之外还要记的「新建子目录」名单。</summary>
public static class UpgradeCheckpoint
{
    /// <summary>某次工具调用要监视的 (父目录, 目标 MC 版本);不需要时返回空表。</summary>
    public static List<(string Parent, string MatchMc)> Watches(string name, PyDict? args, string instance)
    {
        var list = new List<(string, string)>();
        if (name != "apply_instance_upgrade" || args == null || !Py.Truthy(args.Get("in_place")))
        {
            return list;
        }
        string mc = Py.Strip(Py.S(args.Get("target_version")));
        if (mc.Length == 0)
        {
            return list;
        }
        try
        {
            string inst = Py.S(Py.Or(args.Get("instance"), instance));
            if (inst.Length == 0)
            {
                inst = Py.S(Config.CONFIG.Get("default_instance", "default"));
            }
            list.Add((new Instance(inst.Length > 0 ? inst : null).VersionsDir(), mc));
        }
        catch (Exception)
        {
            // 实例都解析不了:执行时也会失败,不必监视
        }
        return list;
    }

    /// <summary>AiAgent 在写工具执行前调:按 <see cref="Watches"/> 记下名单。返回记了几条(失败不阻断操作)。</summary>
    public static int SnapshotFor(string chatId, string turnId, string name, PyDict? args, string instance)
    {
        int n = 0;
        foreach ((string parent, string mc) in Watches(name, args, instance))
        {
            if (Py.Truthy(Checkpoint.SnapshotNewChildren(chatId, turnId, parent, mc).Get("ok")))
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// AI3-stability:AiAgent 在写工具执行完(不论成功失败)后调:把「执行后 − 执行前」的差集补进名单。
    /// <paramref name="resultText"/> 是工具结果(JSON):install_game 回了 task_id = 安装转成了后台任务,目录之后才出现。
    /// 返回补记了几条(失败不阻断;没补上的名单撤回时按「属于目标版本」的旧口径兜底)。
    /// </summary>
    public static int CaptureAfter(string chatId, string turnId, string name, PyDict? args, string instance, string? resultText)
    {
        List<(string Parent, string MatchMc)> watches = Watches(name, args, instance);
        if (watches.Count == 0)
        {
            return 0;
        }
        bool pending = InstallWentBackground(resultText);
        int n = 0;
        foreach ((string parent, string _) in watches)
        {
            if (Py.Truthy(Checkpoint.CaptureNewChildren(chatId, turnId, parent, pending).Get("ok")))
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>工具结果里 install_game 是不是转成了后台任务(有 task_id)。解析不了按「不是」。</summary>
    internal static bool InstallWentBackground(string? resultText)
    {
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return false;
        }
        try
        {
            if (PyJson.Loads(resultText) is PyDict r && r.Get("install_game") is PyDict ig)
            {
                return Py.S(Py.Or(ig.Get("task_id"), ig.Get("id"))).Length > 0;
            }
        }
        catch (Exception)
        {
        }
        return false;
    }
}
