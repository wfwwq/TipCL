// AI2-i18n-ux:ToolRunner 的一处扩展 —— 长工具的「阶段进度」回调(目前只有 apply_instance_upgrade 用)。
//
// 背景(progress/AI-new-features.md 遗留第 8 条):Linux IApi 的 install_game 是同步调用(装完才返回),
// apply_instance_upgrade 期间 AI 页的步骤行一直只显示「准备 / 执行中」,看不出走到哪一步。
// 这里给执行器加一个 ToolProgress(工具名, 阶段文字)回调:升级按 核对模组 → 复制实例 → 安装版本 →
// 替换模组 → 预检 五段推进,每进一段回调一次(文字已走词表),AI 页把它接到对应步骤行上。
// install_game 本身不改(仍是同步的)。最后的「预检」是真跑:升级完对目标实例跑一遍 AiPreflight.Check(只读),
// 结论放进工具结果的 preflight 字段;预检自身出错不影响升级结果。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>长工具的阶段进度(工具名, 已本地化的阶段文字)。可能在后台线程回调,宿主自己回投 UI 线程。</summary>
    public Action<string, string>? ToolProgress { get; set; }

    /// <summary>测试注入点:替换升级结束后的预检(不碰真实 Java / 版本文件)。</summary>
    internal static Func<Instance, PyDict>? UpgradePreflightHook;

    /// <summary>升级的阶段总数(核对模组 / 复制实例 / 安装版本 / 替换模组 / 预检)。</summary>
    public const int UpgradeStageCount = 5;

    private int _lastUpgradeStage = -1;

    /// <summary>第 <paramref name="stage"/> 段(0 起)的阶段名。</summary>
    public static string UpgradeStageName(int stage, bool inPlace) => stage switch
    {
        0 => I18nCore.Tr("核对模组"),
        1 => inPlace ? I18nCore.Tr("准备原地升级") : I18nCore.Tr("复制实例"),
        2 => I18nCore.Tr("安装版本"),
        3 => I18nCore.Tr("替换模组"),
        _ => I18nCore.Tr("预检"),
    };

    /// <summary>步骤行上显示的阶段文字,例如「第 2/5 步:复制实例」。</summary>
    public static string UpgradeStageText(int stage, bool inPlace)
    {
        int n = Math.Clamp(stage, 0, UpgradeStageCount - 1);
        return I18nCore.Tr("第 {n}/{total} 步:{stage}")
            .Replace("{n}", (n + 1).ToString())
            .Replace("{total}", UpgradeStageCount.ToString())
            .Replace("{stage}", UpgradeStageName(n, inPlace));
    }

    /// <summary>计划卡推进到第 done 步时顺带报阶段(同一段只报一次)。</summary>
    private void UpgradeStage(int done, bool inPlace)
    {
        int stage = Math.Clamp(done, 0, UpgradeStageCount - 1);
        if (done == 0)
        {
            _lastUpgradeStage = -1; // 新的一次升级(上一次中途抛异常也不影响这次)
        }
        if (stage == _lastUpgradeStage)
        {
            return;
        }
        _lastUpgradeStage = stage;
        try
        {
            ToolProgress?.Invoke("apply_instance_upgrade", UpgradeStageText(stage, inPlace));
        }
        catch (Exception)
        {
            // 界面回调出错不影响升级本身
        }
    }

    /// <summary>升级收尾:对目标实例跑一遍预检(只读),把结论并进结果。</summary>
    private async Task<object?> AfterUpgradePreflightAsync(PyDict result, CancellationToken ct)
    {
        _lastUpgradeStage = -1;
        string target = Py.S(result.Get("target_instance"));
        if (target.Length == 0)
        {
            return result;
        }
        try
        {
            Func<Instance, PyDict>? hook = UpgradePreflightHook;
            PyDict report = await Task.Run(() => hook != null ? hook(new Instance(target)) : AiPreflight.Check(new Instance(target)), ct)
                .ConfigureAwait(false);
            result["preflight"] = PyDict.Of(
                "ok", Py.Truthy(report.Get("ok")),
                "error_count", Py.Or(report.Get("error_count"), 0L),
                "warn_count", Py.Or(report.Get("warn_count"), 0L),
                "summary", Py.S(report.Get("summary")),
                "items", Py.ListOr(report.Get("items")).Take(8).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result["preflight"] = PyDict.Of("ok", false, "error", Py.ExcStr(ex));
        }
        return result;
    }
}
