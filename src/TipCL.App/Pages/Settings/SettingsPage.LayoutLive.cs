// AI2-i18n-ux(acct-layout-mp 遗留):设置 →「布局」视图开着时,布局文档被别处改了(AI 的 layout_apply、
// 别的页面编辑器提交、导入方案……)要自动刷新 —— 方案列表 / 当前方案 / 分栏比例 / JSON 卡片都跟着变。
//
// 做法:订阅已有的静态事件 LayoutStoreV2.LayoutChanged(进可视树订阅、离开退订,与 BlockHost 同一纪律);
// 事件可能来自任意线程(AI 工具在线程池上跑),一律投回构造时捕获的 UI 调度器、合并成一次处理。
// 处理时按「布局 JSON 文本」做指纹:与视图上次构建时一样(本视图自己改完已经 RefreshLayoutView 过)就什么都不做;
// 不一样且「布局」视图正显示 → 重建视图。JSON 卡片里用户没动过的文本换成最新;用户正编辑着没应用的草稿则保留并提示。
// 本视图自己的区块正处于编辑态时不重建(会打断拖动),等下一次变更或重新进入再刷新。
// 共享的 SettingsPage.Layout.cs 只在 BuildLayoutView 末尾加了一行 LayoutLiveOnBuilt(host)。
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using ToastKind = TipCL.App.Common.ToastKind;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage
{
    private Action? _layoutLiveHandler;
    private Dispatcher? _layoutLiveUi;
    private string _layoutLiveStamp = "";
    private bool _layoutLivePosted;
    private BlockHost? _layoutLiveHost;

    /// <summary>「布局」视图因外部变更被自动重建的次数(测试用)。</summary>
    internal int LayoutLiveRefreshCount { get; private set; }

    private static string LayoutLiveStamp()
    {
        try
        {
            return LayoutJsonEditor.Text(LayoutStoreV2.Load());
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>BuildLayoutView 每次构建完调用:记下这一版的指纹;第一次调用时把订阅接上。</summary>
    private void LayoutLiveOnBuilt(Control host)
    {
        _layoutLiveStamp = LayoutLiveStamp();
        _layoutLiveHost = host as BlockHost;
        if (_layoutLiveHandler != null)
        {
            return;
        }
        _layoutLiveUi = Dispatcher.UIThread;
        _layoutLiveHandler = OnLayoutChangedLive;
        AttachedToVisualTree += (_, _) =>
        {
            LayoutStoreV2.LayoutChanged -= _layoutLiveHandler;
            LayoutStoreV2.LayoutChanged += _layoutLiveHandler;
        };
        DetachedFromVisualTree += (_, _) => LayoutStoreV2.LayoutChanged -= _layoutLiveHandler;
    }

    // 任意线程:只负责投递(合并连续多次保存)
    private void OnLayoutChangedLive()
    {
        if (_layoutLivePosted)
        {
            return;
        }
        _layoutLivePosted = true;
        Dispatcher ui = _layoutLiveUi ?? Dispatcher.UIThread;
        ui.Post(() => ApplyLayoutLive(), DispatcherPriority.Background);
    }

    /// <summary>UI 线程:布局文档真的变了且「布局」视图正显示 → 重建视图。返回是否重建(测试用)。</summary>
    internal bool ApplyLayoutLive()
    {
        _layoutLivePosted = false;
        if (_layoutRebuilding || this.GetVisualRoot() == null)
        {
            return false;
        }
        string now = LayoutLiveStamp();
        if (now == _layoutLiveStamp)
        {
            return false;
        }
        Control? layoutView = _views[LayoutView];
        if (layoutView == null || !ReferenceEquals(_view.Content, layoutView))
        {
            // 没在看「布局」视图:切过去时 Select 会重建,这里不用动
            return false;
        }
        if (_layoutLiveHost?.Editor.IsEditing == true)
        {
            return false;
        }
        string before = _layoutLiveStamp;
        bool draftKept = _layoutJsonText.Length > 0 && _layoutJsonText != before;
        if (!draftKept)
        {
            _layoutJsonText = "";
        }
        RefreshLayoutView();
        LayoutLiveRefreshCount++;
        if (draftKept)
        {
            PageBase.Toast(I18nCore.Tr("布局已在别处被修改"), I18nCore.Tr("JSON 编辑框里保留的是你还没应用的草稿;点「刷新」可载入最新布局"), ToastKind.Info);
        }
        return true;
    }
}
