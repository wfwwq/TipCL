// M4-H P6:插件界面贡献的本地快照(对齐 Windows Pages/Plugins/PluginUi.cs 的壳侧职责)。
//
// 与参考版的差别(见 docs/PARITY.md §7.1):
// - 参考版快照来自后端 RPC 的 plugins_ui / plugins_list;Linux 没有 RPC 桥,数据源是
//   进程内的 Core/Plugins/PluginHost(启停落盘 + manifest 的 contributes 汇总)。
// - 参考版的 NativePluginUi(反射取 .NET 插件的 WPF 原生界面)在 Linux 不移植:
//   参考契约基于 net48 + WPF,本进程加载不了(§7.2)。
// 外壳(Shell/MainWindow)与插件页订阅 Changed 重建顶部页签 / 刷新列表。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TipCL.App.Common.Plugins;
using TipCL.Core;

namespace TipCL.App.Pages.Plugins;

/// <summary>插件 UI 贡献与已装插件列表的进程内快照。</summary>
public static class PluginUi
{
    private static List<UiContribution> _all = new List<UiContribution>();
    private static List<PluginInfo> _plugins = new List<PluginInfo>();

    /// <summary>界面贡献或插件列表变了(在调用线程上触发,订阅方自行调度到 UI 线程)。</summary>
    public static event Action? Changed;

    public static IReadOnlyList<UiContribution> All => _all;

    public static IReadOnlyList<PluginInfo> Plugins => _plugins;

    public static bool Loaded { get; private set; }

    public static IEnumerable<UiContribution> OfKind(string kind)
        => _all.Where(c => c.Kind == kind).OrderBy(c => c.Order).ThenBy(c => c.Label, StringComparer.Ordinal);

    public static UiContribution? Find(string kind, string plugin, string id)
        => _all.FirstOrDefault(c => c.Kind == kind && c.Plugin == plugin && c.Id == id);

    public static UiContribution? FindPage(string pageId)
        => _all.FirstOrDefault(c => c.Kind == "page" && c.PageId == pageId);

    public static PluginInfo? Plugin(string id)
        => _plugins.FirstOrDefault(p => p.Id == id);

    public static string PluginName(string id)
        => Plugin(id)?.DisplayName ?? id;

    /// <summary>重新读一遍磁盘状态(廉价:插件目录 + manifest + plugins.json)。</summary>
    public static void Refresh()
    {
        List<UiContribution> next = new List<UiContribution>();
        try
        {
            foreach (PyDict row in PluginRuntime.UiContributions())
            {
                try
                {
                    using var doc = JsonDocument.Parse(PyJson.Dumps(row, ensureAscii: false));
                    UiContribution c = UiContribution.From(doc.RootElement.Clone());
                    if (c.Plugin.Length > 0 && c.Kind.Length > 0) next.Add(c);
                }
                catch (Exception)
                {
                    // 单个贡献读不出来不该拖垮整张表
                }
            }
        }
        catch (Exception)
        {
        }

        List<PluginInfo> plugins = new List<PluginInfo>();
        try
        {
            foreach (var info in PluginRuntime.Discover())
            {
                plugins.Add(new PluginInfo
                {
                    Id = info.Id,
                    Name = info.Manifest?.Name ?? info.Id,
                    Version = info.Manifest?.Version ?? "",
                    Author = info.Manifest?.Author ?? "",
                    Description = info.Manifest?.Description ?? "",
                    Icon = info.Manifest?.Icon ?? "",
                    Runtime = info.Manifest?.Runtime ?? "node",
                    Enabled = info.Enabled,
                    State = info.State,
                    Error = info.Error,
                    Source = PluginRuntime.Host.StateStore.Get(info.Id)?.Source ?? "local",
                    DevPath = PluginRuntime.Host.StateStore.Get(info.Id)?.DevPath ?? "",
                    Permissions = info.Manifest?.Permissions ?? new List<string>(),
                    Granted = PluginRuntime.Host.StateStore.Get(info.Id)?.GrantedPermissions ?? new List<string>(),
                    Homepage = info.Manifest?.Homepage ?? "",
                });
            }
        }
        catch (Exception)
        {
        }

        bool changed = !Same(next, _all) || !SamePlugins(plugins, _plugins);
        _all = next;
        _plugins = plugins;
        Loaded = true;
        if (changed) Changed?.Invoke();
    }

    private static bool Same(List<UiContribution> a, List<UiContribution> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Key != b[i].Key || a[i].Label != b[i].Label || a[i].Order != b[i].Order || a[i].Url != b[i].Url)
                return false;
        }
        return true;
    }

    private static bool SamePlugins(List<PluginInfo> a, List<PluginInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Id != b[i].Id || a[i].Version != b[i].Version || a[i].Enabled != b[i].Enabled || a[i].State != b[i].State)
                return false;
        }
        return true;
    }
}
