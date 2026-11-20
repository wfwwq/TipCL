// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/BlockHost.cs(L1 薄壳,167 行)。
// L1 起:本类只是 LayoutEditor(布局引擎 + v2 持久化)的薄壳 —— 不再直接读写 ui_page_layouts.json,
// 真源是 ui_layout_v2.json(LayoutStoreV2),v1 由 v2 单向写通;BlockSpec / 构造签名 / Root / Changed 均未变。
// Avalonia 差异映射(见 R32-BRIEF 映射表 / R32-LED §4):
//   App.ThemeVisualsChanged → TipCL.App.Ui.Ui.ThemeChanged;AppServices.Window?.Toast → LayoutFx.ShowToast;
//   I18n.L → I18nCore.Tr;base.Dispatcher → Dispatcher.UIThread(CheckAccess/Post);
//   FrameworkElement → Control;UIElement → Control。
// E-01(audit-fix 口径):宿主**不再自挂 ContextMenu** —— 右键「自定义布局...」统一由主窗口的
//   LayoutShellHooks 全局钩子弹(钩子沿祖先链遇到自带菜单就让位)。Avalonia 的 ContextMenu 不做属性继承链合并,
//   宿主一挂,整页右键就只剩这一项。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiFacade = TipCL.App.Ui.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using ToastKind = TipCL.Core.Layout.ToastKind;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 页面区块描述:页面把可重排的内容切成若干 BlockSpec 交给 BlockHost。
/// Id 是稳定英文 id(进布局文档);Title 必须是已经过文案门面的现成文案;
/// Content 由页面自己构建,BlockHost 只负责搬运与重排,不关心内容内部结构。
/// </summary>
public sealed class BlockSpec
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public Control? Content { get; set; }

    public bool DefaultVisible { get; set; } = true;

    /// <summary>
    /// 网格模式下的最小列宽(12 列制;0 = 只用全局下限 2 列)。
    /// 内容本身需要一定宽度才看得见的块(插件列表这类)设一下:旧存档里被缩成 1 列的块渲染时自动撑回来,
    /// 不再出现「内容区块被压成一条细线、插件全看不见」(对齐 Windows 2026-11-19 修复 #7)。只影响渲染,不改写存档。
    /// </summary>
    public int MinCols { get; set; }
}

/// <summary>
/// [R35 lin-l5] 各页「页面默认布局」(GOAL-layout-v3 §5.5 / D-81 Q4;语义照 Windows r34/l5 的 PageDefaultLayouts):
/// 布局文档里**还没有**这一页时,宿主用它代替「白名单纵向流」;文档里有这一页(用户存过 / 导入过 / 应用过方案)
/// 就完全照文档,这里不参与(判定在 LayoutEditor.LoadFromStore:page ?? defaultPage)。
/// 只管联机 / 游戏时长 / Java / 启动下方内容区这 4 页;实例库的默认仍在 LibraryPage 里。
/// 约定:
///   • 网格一律 12 列(= LayoutSchema.ColsDefault,与实例库同制;编辑态把 stack 折成网格时也是 12 列),效果图里的 6 列只是示意;
///   • 可见块写显式坐标(x / y / w = span),和 LibraryPage 的默认同一种写法;
///   • 默认隐藏的块只给列跨度、不给坐标(契约默认矩形 0,0,1,1 = 自动流,见 LayoutGrid.Pack),并且排在最后:
///     在「添加」里显示出来时按顺序接在已有块后面、两两并排,不会都挤进第 0 列或压到别的块上;
///   • 间距 / 页边距沿用各页原来的值(设置内层子页 = 18,6,18,16;其余 = 文档默认),不改观感基线。
/// 每次调用都返回新对象(宿主会再 Clone 一次,这里不共享实例)。纯数据,无头测试可直接调用。
/// </summary>
public static class PageDefaultLayouts
{
    /// <summary>这几份默认网格的列数。</summary>
    public const int Cols = LayoutSchema.ColsDefault;

    /// <summary>设置内层子页(Java / 游戏时长)原来的页边距(= 旧 ScrollViewer 的 18,6,18,16)。</summary>
    internal static double[] SettingsSubPad() => new double[] { 18.0, 6.0, 18.0, 16.0 };

    /// <summary>联机:说明(整行)/ 联机状态 + 房间(各半)/ 快捷操作(整行)/ 玩家 + 节点说明(4:2 = 8:4 列)。</summary>
    public static PageLayoutV2 Multiplayer()
    {
        return Grid(null, At("header", 0, 0, 12), At("state", 0, 1, 6), At("room", 6, 1, 6), At("actions", 0, 2, 12), At("players", 0, 3, 8), At("nodes", 8, 3, 4));
    }

    /// <summary>游戏时长:累计时长 + 时间范围(各半)/ 明细(整行);概览默认隐藏。</summary>
    public static PageLayoutV2 Playtime()
    {
        return Grid(SettingsSubPad(), At("total", 0, 0, 6), At("range", 6, 0, 6), At("list", 0, 1, 12), Off("header", 12));
    }

    /// <summary>Java:安装方式 + 实例默认(各半)/ 已检测的 Java(整行);说明默认隐藏。</summary>
    public static PageLayoutV2 Java()
    {
        return Grid(SettingsSubPad(), At("install", 0, 0, 6), At("defaults", 6, 0, 6), At("list", 0, 1, 12), Off("intro", 12));
    }

    /// <summary>启动页下方内容区(英雄区不进布局):新闻整行;下载任务 / 游戏时长 / 便签显示后两两并排,日志占整行,四块都默认隐藏。</summary>
    public static PageLayoutV2 LaunchContent()
    {
        return Grid(null, At("news", 0, 0, 12), Off("tasks", 6), Off("playtime", 6), Off("notes", 6), Off("log", 12));
    }

    /// <summary>按页面 id 取默认布局;不在这 4 页里的返回 null(= 白名单纵向流,或页面自己给的默认)。</summary>
    public static PageLayoutV2? ForPage(string? pageId)
    {
        switch (pageId)
        {
            case "multiplayer":
                return Multiplayer();
            case "playtime":
                return Playtime();
            case "java":
                return Java();
            case "launch":
                return LaunchContent();
            default:
                return null;
        }
    }

    private static PageLayoutV2 Grid(double[]? pad, params BlockLayout[] blocks)
    {
        PageLayoutV2 page = new PageLayoutV2
        {
            Mode = LayoutSchema.ModeGrid,
            Cols = Cols,
            RowH = 0.0,
            Gap = LayoutSchema.GapDefault,
            Blocks = new List<BlockLayout>(blocks)
        };
        if (pad != null)
        {
            page.Pad = pad;
        }
        for (int i = 0; i < page.Blocks.Count; i++)
        {
            page.Blocks[i].Z = i;
        }
        return page;
    }

    // 可见块:显式位置,宽 = 列跨度
    private static BlockLayout At(string id, int x, int y, int span)
    {
        return new BlockLayout
        {
            Id = id,
            X = x,
            Y = y,
            W = span,
            H = 1.0,
            Span = span,
            MinH = 1.0
        };
    }

    // 默认隐藏块:只给列跨度(坐标保持契约默认 0,0,1,1 → 显示时自动流到末尾的空位)
    private static BlockLayout Off(string id, int span)
    {
        return new BlockLayout
        {
            Id = id,
            Hidden = true,
            Span = span
        };
    }
}

/// <summary>[R35 lin-l5] 区块宿主的入口规则(纯函数,无头测试可直接调用)。</summary>
public static class BlockHostRules
{
    /// <summary>至少几个块才给「自定义布局...」右键菜单(D-81 Q5)。</summary>
    public const int MinBlocksForLayoutMenu = 2;

    /// <summary>
    /// 只有一块内容的页(设置里的「下载与网络」、Linux 的「反馈与帮助」)没什么可排的,右键不给「自定义布局...」。
    /// <paramref name="blockCount"/> = 页面交给宿主的内置块数(含默认隐藏的块,不含文档里的自建 / 插件块) ——
    /// 与 Windows BlockHost 的 specs.Count 同一口径:启动页内容区 5 块里只有新闻可见,仍然算可编辑。
    /// </summary>
    public static bool OffersLayoutMenu(int blockCount)
    {
        return blockCount >= MinBlocksForLayoutMenu;
    }

    /// <summary>入口表里标了「单块」的页一律不给(与 LayoutShellHooks.CanEditPage 的第一条判定同源)。</summary>
    public static bool OffersLayoutMenu(string? pageId, int blockCount)
    {
        LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(pageId ?? "");
        if (entry != null && entry.SingleBlock)
        {
            return false;
        }
        return OffersLayoutMenu(blockCount);
    }
}

/// <summary>
/// 页面区块宿主(WP-U1 冻结契约,公共 API 一字不改):
///   常态按布局文档顺序渲染非隐藏区块,视觉零装饰(间距/边距对齐 PageBase.ScrollBody,默认渲染与旧版逐像素等价);
///   右键「自定义布局...」进入编辑模式(菜单由 LayoutShellHooks 的全局钩子弹,本类不挂 ContextMenu);
///   [R35 lin-l5] 进入条件改回「≥2 个块」(D-81 Q5,见 BlockHostRules):宿主自己的 <see cref="OffersLayoutMenu"/>
///   与 LayoutEditor.CanEdit(单块标记 + 块数 < 2 → false)两道都过才进 —— 全局右键钩子(FindHosts)按 CanEdit 过滤,
///   EnterEdit 再按 OffersLayoutMenu 把一次关;
///   编辑态的拖拽重排 / 显隐 / 托盘 / 工具条在 LayoutEditor 里。
/// </summary>
public sealed class BlockHost : UserControl
{
    private readonly LayoutEditor _editor;

    // [R35 lin-l5] 页面交给宿主的内置块数(只数页面自己的块,不含文档里的自建 / 插件块;口径同 Windows _specCount)
    private readonly int _specCount;

    // [R35 lin-l5] 上一次对外报告的编辑态(EditingChanged 只在真变化时触发)
    private bool _lastEditing;

    // 主题刷新的稳定委托引用(静态事件必须能对称 -=,所以缓存一份而不是每次都新建闭包)
    private readonly Action _themeRefresh;

    // 布局文档变更的稳定委托引用(LayoutStoreV2.LayoutChanged 是静态事件,同样必须对称 -=)
    private readonly Action _storeRefresh;

    // 构造时捕获的 UI 调度器(R32-LINMORE)。为什么不在回调里现读 Dispatcher.UIThread:
    // 无头测试宿主每次派发都会 ResetForUnitTests(),存在"全局 UIThread 被另一个线程重建"的窗口;
    // 现读会让 CheckAccess 在错误线程上返回 true、渲染被拉到没有平台的线程上(实测:全量套件
    // 偶发 Unable to locate IPlatformRenderInterface,集中在 LayoutCard 建图标处)。
    // 捕获实例后,判断与回投都锚在"宿主构造时那个调度器"上,与全局静态的后续重建无关。
    private readonly Dispatcher _ui;

    public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null, PageLayoutV2? defaultPage = null)
    {
        _ui = Dispatcher.UIThread;
        _specCount = specs?.Count ?? 0;
        _editor = new LayoutEditor(pageId, specs, contentMargin, defaultPage);
        _themeRefresh = _editor.RefreshThemeVisuals;
        _storeRefresh = OnLayoutStoreChanged;
        _editor.Changed += delegate
        {
            Changed?.Invoke();
        };
        base.Focusable = false;
        base.Content = _editor.Root;
        // 入口:右键菜单统一由主窗口的全局钩子弹(LayoutShellHooks:「自定义布局...」/ 编辑中的「完成编辑」
        // 「重置本页布局」/「布局设置...」)。宿主自己不再挂 ContextMenu。
        // [R35 lin-l5] 编辑态的进出不止走 EnterEdit(编辑条「完成」、Esc、全局右键「完成编辑」都直接调 LayoutEditor),
        // 所以每次布局刷新时对一下 IsEditing:进出编辑都会重排(编辑条显隐 / 重渲染),这一拍一定会来。
        base.LayoutUpdated += delegate
        {
            SyncEditing();
        };
        base.Loaded += delegate
        {
            UiFacade.ThemeChanged -= _themeRefresh;
            UiFacade.ThemeChanged += _themeRefresh;
            _editor.RefreshThemeVisuals();
            // 布局文档变了(设置页应用整机方案 / 别的编辑器提交)→ 自己重排:
            // 这是"一键切换方案后页面区块当场刷新"的接线(D1);Unloaded 时对称退订,避免页面销毁后还挂着静态事件。
            LayoutStoreV2.LayoutChanged -= _storeRefresh;
            LayoutStoreV2.LayoutChanged += _storeRefresh;
            // 进可视树时补读一次:页面是缓存复用的(切走再切回来只会触发 Loaded),
            // 期间别人改过布局文档 / 窗口跨过窄窗阈值时,这一读保证拿到的是当前变体那一份。
            _editor.ReloadFromStore();
        };
        base.Unloaded += delegate
        {
            UiFacade.ThemeChanged -= _themeRefresh;
            LayoutStoreV2.LayoutChanged -= _storeRefresh;
            // 规格2-问题9:页面被切走(缓存复用页面的宿主进了 ContentControl 的更替)时,
            // 还停在编辑态就自动收尾 —— 保存布局、收起工具栏、提示「布局已保存」,
            // 避免"切页面回来还残留编辑态"。进入编辑后什么都没动的话,布局会照常还原(见 LayoutEditor.ExitEdit)。
            if (_editor.IsEditing)
            {
                try
                {
                    _editor.ExitEdit();
                }
                catch
                {
                    // 窗口正在销毁 / 调度器已关:收尾失败也不能崩
                }
            }
            SyncEditing();
        };
    }

    // LayoutChanged 是静态事件,可能从任意线程来 → 统一回到 UI 线程重排;
    // 自己刚提交过的那一次跳过(编辑器已经重排过了,避免同一次改动渲染两遍)。
    private void OnLayoutStoreChanged()
    {
        // 没挂在任何可视根上的宿主不必重排:它下次进可视树时 Loaded 会补读一次
        // (见构造函数里 Loaded 的 ReloadFromStore)。这条同时挡掉"页面已构造但从未上屏"的
        // 测试场景下、由别的用例触发的无意义渲染。
        if (this.GetVisualRoot() == null)
        {
            return;
        }
        LayoutEditor editor = _editor;
        if (editor.IsSaving)
        {
            return;
        }
        try
        {
            if (_ui.CheckAccess())
            {
                editor.ReloadFromStore();
            }
            else
            {
                _ui.Post(editor.ReloadFromStore);
            }
        }
        catch
        {
            // 页面正在销毁 / 调度器已关:不重排也不能崩
        }
    }

    public Control Root => this;

    /// <summary>本宿主的页面 id(与布局文档里的 pageId 一致)。</summary>
    public string PageId => _editor.State.PageId;

    /// <summary>进入编辑态并提示(全局右键菜单 / 页面自己的入口走这里)。
    /// 返回 false = 这一页没有可编辑的区块(调用方据此给"没有可拖动的区块"的提示)。
    /// 提示只用现成的 Toast(title, body, kind) —— 不要自己拼图标 / 富文本。</summary>
    public bool EnterEdit()
    {
        // [R35 lin-l5] D-81 Q5:至少 2 个块才给编辑入口(与全局右键钩子按 Editor.CanEdit 过滤的口径叠加)
        if (!OffersLayoutMenu || !_editor.CanEdit)
        {
            return false;
        }
        if (!_editor.IsEditing)
        {
            _editor.EnterEdit();
            // [v3] §1.A.10:提示文案换成效果图那一句(拖动调顺序 / 网格拖右边缘改宽 / 悬停看块名与隐藏删除)
            LayoutFx.ShowToast(I18nCore.Tr("已进入自定义布局"), LayoutEditor.FullHint(), ToastKind.Success);
        }
        SyncEditing();
        return true;
    }

    /// <summary>
    /// [R35 lin-l5] 右键是否给「自定义布局...」(≥2 块且不是入口表里标了单块的页,D-81 Q5;规则见 <see cref="BlockHostRules"/>)。
    /// 只数页面交给宿主的内置块(含默认隐藏的),口径同 Windows BlockHost.OffersLayoutMenu。
    /// </summary>
    public bool OffersLayoutMenu => BlockHostRules.OffersLayoutMenu(PageId, _specCount);

    /// <summary>页面交给宿主的内置块数(测试 / 诊断用)。</summary>
    public int SpecCount => _specCount;

    /// <summary>当前是否处于编辑态。</summary>
    public bool IsEditing => _editor.IsEditing;

    /// <summary>
    /// [R35 lin-l5] 编辑态进出(参数 = 现在是否在编辑)。启动页用它把英雄区压暗并挂「主区域固定」标签(§5.5)。
    /// 进:EnterEdit 当场同步触发;出:下一次布局刷新时触发(编辑条「完成」/ Esc / 全局右键「完成编辑」/ 切走页面)。
    /// </summary>
    public event Action<bool>? EditingChanged;

    /// <summary>[R35 lin-l5] 把编辑态与上次报告的对一下,变了就发 EditingChanged(幂等,可随时调)。</summary>
    public void SyncEditing()
    {
        bool now = _editor.IsEditing;
        if (now == _lastEditing)
        {
            return;
        }
        _lastEditing = now;
        try
        {
            EditingChanged?.Invoke(now);
        }
        catch
        {
            // 订阅方的外观刷新失败不能连累编辑器
        }
    }

    /// <summary>
    /// [layout-fix 第三批] 非编辑态收起底部托盘 / 工具条外壳(见 LayoutEditor.CollapseIdleBottom)。
    /// 新拆成区块的页用它让滚动视口的下沿与拆块前一致;既有页面不设,外观不变。
    /// </summary>
    public bool CollapseIdleBottom
    {
        get => _editor.CollapseIdleBottom;
        set => _editor.CollapseIdleBottom = value;
    }

    /// <summary>按滚轮增量滚动区块内容区(能滚才滚;返回是否滚了)。鼠标在宿主之外(启动页英雄区 / 背景图)时,
    /// 页面可以把滚轮转交过来 —— 对齐 Windows 2026-11-19 修复 #2。</summary>
    public bool ScrollBy(int delta) => _editor.ScrollBy(delta);

    public event Action? Changed;

    /// <summary>内部编辑器(调试与宿主内测试用)。</summary>
    internal LayoutEditor Editor => _editor;
}
