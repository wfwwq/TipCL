// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/Widgets/PluginBlocks.cs(分支 r32-r31-merge @ 0a304a7)。
// 移植说明:
//  1 机械改写:namespace / 删 `using TipCL.Services;`(I18n→I18nCore,插件贡献类型→TipCL.App.Pages.Plugins);
//    `UIElement` → `Control`;`Ui.*` → `U.*`(同命名空间门面);WPF 的 `Loaded` → Avalonia 的 `AttachedToVisualTree`
//    (本仓既有写法见 SettingsPage.cs:376-377、BlockHost.cs:138)。
//  2 降级(WebView2 专属,按本仓既有先例 PluginUi.cs:6 / PluginHostPage.cs:5-7/99-101 处理):
//    Windows 版对插件贡献的界面是「原生工厂优先(NativePluginUi.Content),否则内嵌 WebView(PluginWebView)」,
//    并按 `WebViewHost.AvailabilityChanged` 重建。Linux 两端都没有:没有网页宿主,原生契约基于 WPF 也不可用 ——
//    按本仓既有先例改用 `UiDispatch.Placeholder`(PluginModels.cs:283+,插件界面统一表示「Linux 版还没接入」,
//    若贡献带 http(s) 地址仍提供「在浏览器打开」)。因此:
//      - 删掉 `PluginWebView` / `NativePluginUi` 两条分支,以及编辑态藏 WebView 的 `OnEditChanged` 覆写;
//      - 删掉 `WebViewHost.AvailabilityChanged` 订阅与 `Reset()`(它只为「WebView 宿主从无到有」而存在);
//      - 保留冻结版**本来就有**的语义:块 id 不合法返回 null;插件被禁用 / 卸载时**块保留**、只换成
//        「插件区块不可用」占位(照抄 PluginDashCards.cs:160-172,**绝不删用户数据**);贡献变化时重新同步。
//    **不发明**任何新的 WebView2 依赖,也没有删掉插件区块的其余功能(块保留 / 占位 / 变更同步都在)。
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TipCL.App.Pages.Plugins;
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Layout;

// 插件区块(§4.F 的 kind = "block",块 id = plugin:<plugin>:<blockId>)。
// 语义照抄 PluginDashCards.cs:160-172:插件被禁用 / 卸载后**块保留**、只显示「不可用」,不删用户数据。
internal static class PluginBlocks
{
    // 每个块 id 一个常驻外壳:插件重载 / 禁用只换外壳里的内容,拖动重排期间不重建
    // [audit/B] 键必须带 pageId:同一个 plugin:<plugin>:<blockId> 出现在两页时(布局 JSON 手编、
    // 或插件把块写进多页),只按 blockId 缓存会把**同一个 Shell 控件**交给两张卡 ——
    // LayoutCard 的 `_body.Content = shell` 第二次赋值时,该 Shell 还挂在另一张卡的 ContentPresenter 上,
    // Avalonia 会抛 "already has a visual parent"(极端情况是第二页空白)。分页缓存后各页各一份。
    private static readonly Dictionary<string, Shell> _shells = new Dictionary<string, Shell>(StringComparer.Ordinal);

    private static bool _hooked;

    /// <summary>造插件区块内容;id 不合法返回 null(交给上层显示通用占位)。</summary>
    public static Control? Build(string blockId, string pageId)
    {
        if (!LayoutEngine.TryParsePluginId(blockId, out string plugin, out string id))
        {
            return null;
        }
        Hook();
        // [audit/B] 键 = pageId + blockId(见 _shells 的注释:同一个块在两页时不能共用一个控件实例)
        string key = (pageId ?? "") + "\u0001" + blockId;
        if (!_shells.TryGetValue(key, out Shell? shell))
        {
            shell = new Shell(plugin, id);
            _shells[key] = shell;
        }
        shell.Sync();
        return shell;
    }

    /// <summary>插件界面贡献变化时(插件启用 / 禁用 / 重载),让所有外壳重新同步。</summary>
    private static void Hook()
    {
        if (_hooked)
        {
            return;
        }
        _hooked = true;
        PluginUi.Changed += delegate
        {
            foreach (Shell shell in _shells.Values)
            {
                shell.Sync();
            }
        };
    }

    private sealed class Shell : EditAwareShell
    {
        private readonly string _plugin;

        private readonly string _id;

        private UiContribution? _current;

        public Shell(string plugin, string id)
            : base(disposeOnDetach: true)
        {
            _plugin = plugin;
            _id = id;
            AttachedToVisualTree += delegate
            {
                // 被摘下来销毁过(切页 / 重排):挂回可视树时重建
                if (Content == null)
                {
                    Sync();
                }
            };
        }

        public void Sync()
        {
            UiContribution? contribution = PluginUi.Find(WidgetRegistry.PluginKindBlock, _plugin, _id);
            if (contribution == null)
            {
                // 插件被禁用 / 卸载:块保留,只换内容
                if (_current != null || Content == null)
                {
                    (Content as IDisposable)?.Dispose();
                    _current = null;
                    Content = Unavailable();
                }
                return;
            }
            if (ReferenceEquals(_current, contribution) && Content != null)
            {
                return;
            }
            (Content as IDisposable)?.Dispose();
            _current = contribution;
            // Linux 没有网页宿主 / 原生插件契约:统一如实说明(见文件头 2)
            Content = UiDispatch.Placeholder(contribution);
        }

        // 被摘下来(切页 / 编辑态重排):销毁内容,挂回来时重建
        protected override void DisposeContent()
        {
            _current = null;
            object? content = Content;
            Content = null;
            try
            {
                (content as IDisposable)?.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private static Control Unavailable()
        {
            return U.V(4.0, U.Muted(I18nCore.Tr("插件区块不可用")), U.Small(I18nCore.Tr("提供它的插件被禁用或卸载了;可以在编辑布局里移除这个区块。")).Wrap());
        }
    }
}
