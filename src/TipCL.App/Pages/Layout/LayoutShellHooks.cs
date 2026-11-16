// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/LayoutShellHooks.cs(463 行,冻结件 @0a304a7)。
// 主窗口级全局钩子(按 audit-fix E-01 口径):BlockHost 自己**不挂** ContextMenu(见 BlockHost.cs 头注释),
// 右键菜单与"统一滚轮"都在这一处挂:
//   1) 右键:页面内容区任意处右键 → 「自定义布局...」;编辑中 → 「完成编辑」「重置本页布局」;/ 分隔 /「布局设置...」;
//      文本框、自带 ContextMenu 的控件、菜单自身 → 让位(否则会把用户/插件自己的菜单顶掉);
//   2) 滚轮:鼠标下最内层可滚容器自己吃(route==0,不接管,保留原生平滑);最内层吃不下 → 接管并滚外层仍可滚的;
//      鼠标完全不在滚动容器里(页头 / 分段栏 / 编辑工具条)→ 滚当前页里最大的可滚区域;
//      Ctrl / Shift / Slider 一律不拦截。
// 目标宿主选取顺序(纯判定在 LayoutShellLogic):最内层区块宿主 → 包含鼠标位置的最小宿主 → 页面里最大的宿主。
//
// Avalonia 差异映射(逐条实测,见 R32-LW.md §3):
//   MouseRightButtonUpEvent            → InputElement.PointerReleasedEvent + InitialPressMouseButton == Right
//   PreviewMouseWheelEvent             → InputElement.PointerWheelChangedEvent + RoutingStrategies.Tunnel(= WPF Preview)
//   **注册策略必须只给一路**:这两个事件的 RoutingStrategies 实测都是 Tunnel|Bubble ⇒ 一次注册 Tunnel|Bubble 会触发两遍
//     (临时反射探针实测);右键走 Bubble(pageHost 先于窗口),滚轮走 Tunnel(窗口先于内层,Handled 才能压掉内层的二次滚动)
//   RoutedEventArgs 无 OriginalSource      → e.Source
//   DependencyObject 可视树               → Avalonia.VisualTree.VisualExtensions.GetVisualParent/GetVisualChildren
//   GetPosition(x)                        → e.GetPosition(x)(返回 Point)
//   TransformToAncestor().TransformBounds() → VisualExtensions.TranslatePoint(p, target) + Visual.Bounds(尺寸)
//   MouseWheelEventArgs.Delta(±120/格)     → PointerWheelEventArgs.Delta.Y(±1.0/格;Delta 是 Avalonia.Vector)
//   ScrollViewer.VerticalOffset/ScrollableHeight → ScrollViewer.Offset.Y / ScrollBarMaximum.Y
//   WPF 的 SmoothScroll / CanContentScroll → **Linux 无对应类型** ⇒ 固定 48px/格(Windows 的 3.0/48.0 分支被删)
//   PlacementMode.MousePoint               → Avalonia.Controls.PlacementMode.Pointer
//   menu.IsOpen = true                     → menu.Open(placementTarget)(Avalonia 的 MenuBase.IsOpen 无文档,Open 才是公开入口)
//   win.Toast(title, body, kind)           → LayoutFx.ShowToast(title, body, kind)
//   I18n.L(key)                            → I18nCore.Tr(key)
//   BlockHost.Editor(FrameworkElement 侧 internal) → 本程序集内可见,右键链路直接读 Editor.IsEditing / CanEdit
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TipCL.Core.Launcher;
using ToastKind = TipCL.Core.Layout.ToastKind;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 主窗口级布局钩子:全局右键「自定义布局...」+ 统一滚轮路由。
/// 装配点:MainWindow 构造末尾 LayoutShellHooks.Attach(this)(与 FileDrop.Attach 并排);窗口关闭时 Detach。
/// </summary>
public static class LayoutShellHooks
{
    private static Shell.MainWindow? _win;

    // 委托实例必须缓存:静态事件/路由事件要用**同一个**委托才能 RemoveHandler 对称退订
    private static EventHandler<PointerReleasedEventArgs>? _pageHandler;

    private static EventHandler<PointerReleasedEventArgs>? _windowHandler;

    private static EventHandler<PointerWheelEventArgs>? _wheelHandler;

    /// <summary>是否已挂上(幂等保护)。</summary>
    public static bool Attached => _win != null;

    /// <summary>挂到主窗口(重复调用是空操作)。</summary>
    public static void Attach(Shell.MainWindow win)
    {
        if (win == null || _win != null)
        {
            return;
        }
        _win = win;
        ContentControl? pageHost = win.PageHostControl;
        if (pageHost != null)
        {
            // 页面内容区:右键气泡链上先到 pageHost,弹完置 Handled ⇒ 窗口级那一遍自动让位
            _pageHandler = OnPageRightButtonUp;
            pageHost.AddHandler(InputElement.PointerReleasedEvent, _pageHandler, RoutingStrategies.Bubble, handledEventsToo: false);
        }
        // 窗口级兜底:源不在 pageHost 可视子树里(弹层 / 逻辑父)但落点仍在页面矩形内的情况
        _windowHandler = OnWindowRightButtonUp;
        win.AddHandler(InputElement.PointerReleasedEvent, _windowHandler, RoutingStrategies.Bubble, handledEventsToo: false);
        // 统一滚轮:窗口级 Tunnel(= WPF Preview),先于内层拿到事件,才能决定"接管并 Handled"或"放手"
        _wheelHandler = OnPreviewWheel;
        win.AddHandler(InputElement.PointerWheelChangedEvent, _wheelHandler, RoutingStrategies.Tunnel, handledEventsToo: false);
    }

    /// <summary>摘钩(窗口关闭 / 测试收尾)。</summary>
    public static void Detach()
    {
        Shell.MainWindow? win = _win;
        _win = null;
        if (win == null)
        {
            return;
        }
        try
        {
            if (_pageHandler != null && win.PageHostControl is { } pageHost)
            {
                pageHost.RemoveHandler(InputElement.PointerReleasedEvent, _pageHandler);
            }
            if (_windowHandler != null)
            {
                win.RemoveHandler(InputElement.PointerReleasedEvent, _windowHandler);
            }
            if (_wheelHandler != null)
            {
                win.RemoveHandler(InputElement.PointerWheelChangedEvent, _wheelHandler);
            }
        }
        catch
        {
            // 窗口正在销毁:摘钩失败也不能崩
        }
        _pageHandler = null;
        _windowHandler = null;
        _wheelHandler = null;
    }

    // ───────────────────────────── 右键 ─────────────────────────────

    private static void OnWindowRightButtonUp(object? sender, PointerReleasedEventArgs e)
    {
        Shell.MainWindow? win = _win;
        if (win == null || e.Handled || e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }
        ContentControl? pageHost = win.PageHostControl;
        if (pageHost == null || !pageHost.IsVisible)
        {
            return;
        }
        // 源本来就在页面内容区里 ⇒ pageHost 那一遍已经处理过(或已让位),这里不重复
        for (Visual? node = e.Source as Visual; node != null; node = ParentOf(node))
        {
            if (ReferenceEquals(node, pageHost))
            {
                return;
            }
        }
        Point point = e.GetPosition(pageHost);
        if (point.X < 0.0 || point.Y < 0.0 || point.X > pageHost.Bounds.Width || point.Y > pageHost.Bounds.Height)
        {
            return;
        }
        OnPageRightButtonUp(sender, e);
    }

    private static void OnPageRightButtonUp(object? sender, PointerReleasedEventArgs e)
    {
        Shell.MainWindow? win = _win;
        if (win == null || e.Handled || e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }
        ContentControl? pageHost = win.PageHostControl;
        Visual? source = e.Source as Visual;
        // 让位判定:文本框(含密码框)/ 自带 ContextMenu 的控件 / 菜单自身 —— 沿可视祖先链一直到 pageHost
        for (Visual? node = source; node != null && !ReferenceEquals(node, pageHost); node = ParentOf(node))
        {
            if (node is TextBox)
            {
                return;
            }
            if (node is Control element && element.ContextMenu != null)
            {
                return;
            }
            if (node is ContextMenu || node is MenuItem || node is Menu)
            {
                return;
            }
        }
        if (pageHost?.Content is not Visual page)
        {
            return;
        }
        Point point = e.GetPosition(page);
        List<BlockHost> hosts = FindHosts(page);
        BlockHost? editing = hosts.Find(delegate (BlockHost host)
        {
            return host.Editor.IsEditing;
        });
        ContextMenu menu = new();
        if (editing == null)
        {
            MenuItem enter = new() { Header = I18nCore.Tr("自定义布局...") };
            enter.Click += delegate
            {
                EnterEditAt(win, page, source, point);
            };
            menu.Items.Add(enter);
        }
        else
        {
            MenuItem done = new() { Header = I18nCore.Tr("完成编辑") };
            done.Click += delegate
            {
                editing.Editor.ExitEdit();
            };
            menu.Items.Add(done);
            MenuItem reset = new() { Header = I18nCore.Tr("重置本页布局") };
            reset.Click += delegate
            {
                editing.Editor.ResetPage();
            };
            menu.Items.Add(reset);
        }
        menu.Items.Add(new Separator());
        MenuItem settings = new() { Header = I18nCore.Tr("布局设置...") };
        settings.Click += delegate
        {
            OpenLayoutSettings(win);
        };
        menu.Items.Add(settings);
        menu.PlacementTarget = pageHost;
        menu.Placement = Avalonia.Controls.PlacementMode.Pointer;
        menu.Open(pageHost);
        e.Handled = true;
    }

    /// <summary>
    /// 在鼠标处进入编辑态:选目标宿主(最内层 → 含点最小 → 页面最大)。
    /// 页面一个可编辑区块都没有时给提示并返回 false。
    /// </summary>
    public static bool EnterEditAt(Shell.MainWindow win, Visual page, Visual? source, Point pointInPage)
    {
        List<BlockHost> hosts = FindHosts(page);
        List<LayoutRect> rects = new(hosts.Count);
        int innermost = LayoutHostPick.None;
        for (int i = 0; i < hosts.Count; i++)
        {
            BlockHost host = hosts[i];
            rects.Add(BoundsIn(host, page));
            if (innermost == LayoutHostPick.None && IsWithin(source, host))
            {
                innermost = i;
            }
        }
        int pick = LayoutHostPick.Resolve(innermost, rects, pointInPage.X, pointInPage.Y);
        BlockHost? target = ((pick >= 0 && pick < hosts.Count) ? hosts[pick] : null);
        if (target != null && target.EnterEdit())
        {
            return true;
        }
        // 移植降级:Linux 设置页暂无「布局」内层视图(见 R32-LW.md §7),文案改为指向现存入口
        LayoutFx.ShowToast(I18nCore.Tr("这个页面没有可拖动的区块"), I18nCore.Tr("页签、主题可以在「设置」里调整;区块布局的整机设置在后续包补"), ToastKind.Warning);
        return false;
    }

    /// <summary>打开「布局设置」。</summary>
    public static void OpenLayoutSettings(Shell.MainWindow win)
    {
        // Windows: win.Navigate("settings") + SettingsPage.OpenLayoutView()(切到第 8 个内层「布局」视图)。
        // 移植降级:Linux SettingsPage 目前只有 7 个内层视图、没有 OpenLayoutView/BuildLayoutView
        // (grep 全树零命中),所以这里只保证"切到设置页";整页布局设置在后续包补(见 R32-LW.md §7)。
        win.Navigate("settings");
    }

    // ───────────────────────────── 可视树小工具 ─────────────────────────────

    /// <summary>页面(含嵌套)里所有"可编辑"的区块宿主。</summary>
    public static List<BlockHost> FindHosts(Visual root)
    {
        List<BlockHost> list = new();
        Collect(root, list, 0);
        return list;
    }

    private static void Collect(Visual? node, List<BlockHost> list, int depth)
    {
        if (node == null || depth > 60)
        {
            return;
        }
        if (!node.IsVisible)
        {
            return;
        }
        if (node is BlockHost host && host.Editor.CanEdit)
        {
            list.Add(host);
        }
        try
        {
            foreach (Visual child in node.GetVisualChildren())
            {
                Collect(child, list, depth + 1);
            }
        }
        catch
        {
            // 模板正在重建:这一层没走完也不影响已收集到的宿主
        }
    }

    /// <summary>把 element 的矩形换算到 page 的坐标系(等效 WPF TransformToAncestor+TransformBounds)。</summary>
    private static LayoutRect BoundsIn(Visual element, Visual page)
    {
        try
        {
            Point? origin = element.TranslatePoint(new Point(0.0, 0.0), page);
            if (origin is null)
            {
                return default(LayoutRect);
            }
            return new LayoutRect(origin.Value.X, origin.Value.Y, element.Bounds.Width, element.Bounds.Height);
        }
        catch
        {
            return default(LayoutRect);
        }
    }

    private static bool IsWithin(Visual? node, Visual? ancestor)
    {
        for (Visual? walk = node; walk != null; walk = ParentOf(walk))
        {
            if (ReferenceEquals(walk, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>可视父(Avalonia 无 DependencyObject 通用父链;右键/滚轮判定都只看可视树)。</summary>
    private static Visual? ParentOf(Visual? node)
    {
        return node?.GetVisualParent();
    }

    // ───────────────────────────── 统一滚轮 ─────────────────────────────

    private static void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        Shell.MainWindow? win = _win;
        if (win == null || e.Handled || e.Delta.Y == 0.0)
        {
            return;
        }
        // Ctrl(缩放)/ Shift(横向)不拦截
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0)
        {
            return;
        }
        Visual? source = e.Source as Visual;
        ContentControl? pageHost = win.PageHostControl;
        bool inPage = false;
        ScrollViewer? innermost = null;
        ScrollViewer? target = null;
        List<ScrollViewer> chain = new();
        List<bool> canScroll = new();
        for (Visual? node = source; node != null; node = ParentOf(node))
        {
            if (ReferenceEquals(node, pageHost))
            {
                inPage = true;
                break;
            }
            if (node is Slider)
            {
                // 滑块自己的滚轮语义(调值)优先
                return;
            }
            if (node is ScrollViewer viewer)
            {
                if (innermost == null)
                {
                    innermost = viewer;
                }
                bool can = CanScroll(viewer, e.Delta.Y);
                chain.Add(viewer);
                canScroll.Add(can);
                if (target == null && can)
                {
                    target = viewer;
                }
            }
        }
        int route = LayoutWheelRouting.Route(canScroll, chain.Count != 0);
        if (route == 0)
        {
            // 最内层自己还能滚:交给原生(保留系统平滑滚动),不接管
            return;
        }
        if (target != null)
        {
            // 最内层滚不动了:滚外层仍可滚的那个,并吃掉事件(否则内层气泡还会再滚一次)
            ScrollBy(target, e.Delta.Y);
            e.Handled = true;
            return;
        }
        if (!inPage || innermost != null || route != LayoutWheelRouting.OutsideAnyScroll)
        {
            return;
        }
        if (pageHost?.Content is Visual page)
        {
            ScrollViewer? main = FindMainScroller(page, e.Delta.Y);
            if (main != null)
            {
                ScrollBy(main, e.Delta.Y);
                e.Handled = true;
            }
        }
    }

    private static bool CanScroll(ScrollViewer viewer, double delta)
    {
        return LayoutWheelRouting.CanScroll(viewer.Offset.Y, viewer.ScrollBarMaximum.Y, delta)
            && viewer.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    /// <summary>
    /// 滚一次(转交给普通 ScrollViewer 时的一次跳跃)。
    /// Avalonia 差异:Delta.Y ≈ ±1.0/格(不是 WPF 的 ±120),且无 SmoothScroll / 无 CanContentScroll
    /// ⇒ 固定 48px/格(Windows 的 `CanContentScroll ? 3.0 : 48.0` 分支在 Linux 无对应属性,已删)。
    /// </summary>
    private static void ScrollBy(ScrollViewer viewer, double delta)
    {
        double max = viewer.ScrollBarMaximum.Y;
        double target = viewer.Offset.Y - delta * 48.0;
        if (target < 0.0)
        {
            target = 0.0;
        }
        else if (target > max)
        {
            target = max;
        }
        viewer.Offset = new Avalonia.Vector(viewer.Offset.X, target);
    }

    private static ScrollViewer? FindMainScroller(Visual root, double delta)
    {
        List<ScrollViewer> found = new();
        CollectScrollViewers(root, found, 0);
        List<double> areas = new(found.Count);
        List<bool> canScroll = new(found.Count);
        foreach (ScrollViewer viewer in found)
        {
            areas.Add(Math.Max(0.0, viewer.Bounds.Width) * Math.Max(0.0, viewer.Bounds.Height));
            canScroll.Add(CanScroll(viewer, delta));
        }
        int index = LayoutWheelRouting.Widest(areas, canScroll);
        return ((index >= 0 && index < found.Count) ? found[index] : null);
    }

    private static void CollectScrollViewers(Visual? node, List<ScrollViewer> into, int depth)
    {
        if (node == null || depth > 60 || into.Count > 256)
        {
            return;
        }
        if (!node.IsVisible)
        {
            return;
        }
        if (node is ScrollViewer viewer)
        {
            into.Add(viewer);
        }
        try
        {
            foreach (Visual child in node.GetVisualChildren())
            {
                CollectScrollViewers(child, into, depth + 1);
            }
        }
        catch
        {
            // 模板正在重建:跳过这一层
        }
    }
}
