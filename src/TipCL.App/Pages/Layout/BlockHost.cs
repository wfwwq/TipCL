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
}

/// <summary>
/// 页面区块宿主(WP-U1 冻结契约,公共 API 一字不改):
///   常态按布局文档顺序渲染非隐藏区块,视觉零装饰(间距/边距对齐 PageBase.ScrollBody,默认渲染与旧版逐像素等价);
///   右键「自定义布局...」进入编辑模式(菜单由 LayoutShellHooks 的全局钩子弹,本类不挂 ContextMenu);
///   这一页仍有「≥1 个区块」的进入条件,现在由 LayoutEditor.CanEdit(内部 specs.Count >= 1)把关;
///   编辑态的拖拽重排 / 显隐 / 托盘 / 工具条在 LayoutEditor 里。
/// </summary>
public sealed class BlockHost : UserControl
{
    private readonly LayoutEditor _editor;

    // 主题刷新的稳定委托引用(静态事件必须能对称 -=,所以缓存一份而不是每次都新建闭包)
    private readonly Action _themeRefresh;

    // 布局文档变更的稳定委托引用(LayoutStoreV2.LayoutChanged 是静态事件,同样必须对称 -=)
    private readonly Action _storeRefresh;

    public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null, PageLayoutV2? defaultPage = null)
    {
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
        };
    }

    // LayoutChanged 是静态事件,可能从任意线程来 → 统一回到 UI 线程重排;
    // 自己刚提交过的那一次跳过(编辑器已经重排过了,避免同一次改动渲染两遍)。
    private void OnLayoutStoreChanged()
    {
        LayoutEditor editor = _editor;
        if (editor.IsSaving)
        {
            return;
        }
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                editor.ReloadFromStore();
            }
            else
            {
                Dispatcher.UIThread.Post(editor.ReloadFromStore);
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
        if (!_editor.CanEdit)
        {
            return false;
        }
        if (!_editor.IsEditing)
        {
            _editor.EnterEdit();
            LayoutFx.ShowToast(I18nCore.Tr("已进入自定义布局"), I18nCore.Tr("按住区块拖动即可移动,完成编辑后自动保存"), ToastKind.Success);
        }
        return true;
    }

    public event Action? Changed;

    /// <summary>内部编辑器(调试与宿主内测试用)。</summary>
    internal LayoutEditor Editor => _editor;
}
