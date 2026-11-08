// M3-C1 插件宿主 App 侧门面：包一层 Core.Plugins.PluginHost（进程内最小宿主），
// 给页面提供启停 / 事件 / UI 贡献 kind 分发的唯一入口。B4 的 PluginsPage 目前直读
// PluginStateStore（M2 数据面），切到本门面属页面文件改动（B4 地盘），已在 progress/M3-C1.md
// 记依赖申请；本门面先行落地，语义与页面现状兼容。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using CorePlugins = TipCL.Core.Plugins;
using PyDict = TipCL.Core.PyDict;

namespace TipCL.App.Common.Plugins;

/// <summary>
/// 插件运行时门面（进程内宿主，最小版）。Changed 事件在宿主线程触发，订阅方自行调度到 UI 线程。
/// </summary>
public static class PluginRuntime
{
    private static readonly Lazy<CorePlugins.PluginHost> _host =
        new Lazy<CorePlugins.PluginHost>(() => new CorePlugins.PluginHost());

    public static CorePlugins.PluginHost Host => _host.Value;

    /// <summary>插件状态变化（id；"" 表示全量刷新）。</summary>
    public static event Action<string> Changed
    {
        add { Host.Changed += value; }
        remove { Host.Changed -= value; }
    }

    public static List<CorePlugins.PluginInstanceInfo> Discover() => Host.Discover();

    public static CorePlugins.PluginInstanceInfo Info(string id) => Host.Info(id);

    public static PyDict Start(string id) => Host.Start(id);

    public static PyDict Stop(string id) => Host.Stop(id);

    public static bool IsRunning(string id) => Host.IsRunning(id);

    public static string ReadLog(string id) => Host.ReadLog(id);

    /// <summary>已启用插件的 UI 贡献汇总（plugin/kind/id/spec）。</summary>
    public static List<PyDict> UiContributions() => Host.UiContributions();

    /// <summary>
    /// UI 贡献 kind 分发表（对齐 B4 PluginModels.UiDispatch 的五类口径）：
    /// page / aiView / overlay 走浏览器降级（返回 null 表示无原生承载）；dashCard / action
    /// 宿主最小版无 RPC 桥可执行，返回 null。返回非空 PyDict 时带 target 信息。
    /// </summary>
    public static PyDict? Dispatch(PyDict contribution)
    {
        string kind = Py.S(contribution.Get("kind"));
        PyDict spec = contribution.Get("spec") as PyDict ?? new PyDict();
        string url = Py.S(spec.Get("url"));
        switch (kind)
        {
            case "page":
            case "aiView":
            case "overlay":
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return PyDict.Of("kind", kind, "openInBrowser", url);
                return null;
            default:
                return null;
        }
    }
}
