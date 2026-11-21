// AI2-i18n-ux(acct-layout-mp 遗留):分栏比例「改了就即时生效」。
//
// PageLayoutHost 原来只在构造时读一次存储比例(SplitRatioStore.Read);AI 的 layout_apply(json / profile)、
// 设置页「分栏比例」的复位只改了布局文档,已经打开的页面要重新进入才按新比例排。
// 这里让每个分栏宿主在挂上可视树期间订阅 LayoutStoreV2.LayoutChanged(离开可视树即退订 —— 静态事件必须对称 -=,
// 与 BlockHost 同一纪律):事件来了投回构造时捕获的 UI 调度器,重读本处比例,变了才 SyncColumns。
// 自己拖动松手(CommitDrag 落盘)触发的那一次,重读到的就是刚写入的值 ⇒ 什么都不做。
using System;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Controls;

public sealed partial class PageLayoutHost
{
    private Dispatcher? _liveUi;
    private Action? _liveHandler;
    private bool _livePosted;

    /// <summary>因布局文档变化而重排的次数(测试用)。</summary>
    internal int LiveRatioReloads { get; private set; }

    private void WireLiveRatio()
    {
        _liveUi = Dispatcher.UIThread;
        _liveHandler = OnLayoutStoreChangedLive;
        AttachedToVisualTree += (_, _) =>
        {
            LayoutStoreV2.LayoutChanged -= _liveHandler;
            LayoutStoreV2.LayoutChanged += _liveHandler;
            // 不在树上那段时间里别人可能改过:挂回来时补读一次
            ReloadStoredRatio();
        };
        DetachedFromVisualTree += (_, _) => LayoutStoreV2.LayoutChanged -= _liveHandler;
    }

    private void OnLayoutStoreChangedLive()
    {
        if (_livePosted)
        {
            return;
        }
        _livePosted = true;
        try
        {
            (_liveUi ?? Dispatcher.UIThread).Post(() =>
            {
                _livePosted = false;
                if (this.GetVisualRoot() != null)
                {
                    ReloadStoredRatio();
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception)
        {
            _livePosted = false;
        }
    }

    /// <summary>重读本处的存储比例;与当前不同才重排。返回是否重排。</summary>
    internal bool ReloadStoredRatio()
    {
        if (_dragging)
        {
            return false;
        }
        double fresh = SplitRatioStore.Read(_pageId, _splitId) ?? double.NaN;
        bool same = (double.IsNaN(fresh) && double.IsNaN(_storedRatio))
                    || (!double.IsNaN(fresh) && !double.IsNaN(_storedRatio) && Math.Abs(fresh - _storedRatio) < 1e-9);
        if (same)
        {
            return false;
        }
        _storedRatio = fresh;
        SyncColumns();
        LiveRatioReloads++;
        return true;
    }
}
