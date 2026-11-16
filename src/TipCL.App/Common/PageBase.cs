using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using UiFacade = TipCL.App.Ui.Ui;
using Avalonia.Layout;
using Avalonia.Media;

namespace TipCL.App.Common;

/// <summary>
/// M2 页面基类（Avalonia UserControl）：生命周期 + 页面令牌 + 布局自定义钩子位。
/// 生命周期：构建（构造函数）→ OnCreate → 导航参数 OnNavigate → OnActivate / OnDeactivate。
/// 由外壳在挂页/切页时调用 Attach/Detach（接线归外壳，B0 只提供入口，见依赖申请）。
/// </summary>
public abstract class PageBase : UserControl
{
    /// <summary>页面 Body 标准边距（对齐 Windows 版 26→20 收紧后的令牌）。</summary>
    public const double BodyMargin = 20;

    /// <summary>页面 Body 组间距令牌。</summary>
    public const double BodyGap = 14;

    protected PageBase(string pageId, string pageTitle)
    {
        PageId = pageId;
        PageTitle = pageTitle;
    }

    public string PageId { get; }

    public string PageTitle { get; }

    /// <summary>页面级 API 门面。</summary>
    protected static IApi Api => InProcessApi.Instance;

    // ───────────────────────────── 生命周期 ─────────────────────────────

    private bool _created;

    /// <summary>首次挂到外壳时调用一次（懒初始化数据加载放这里）。</summary>
    protected virtual void OnCreate()
    {
    }

    /// <summary>导航到本页并带参数时调用（每次都触发）。</summary>
    protected virtual void OnNavigate(object? parameter)
    {
    }

    /// <summary>本页成为当前页。</summary>
    protected virtual void OnActivate()
    {
    }

    /// <summary>离开本页（页面对象常驻缓存，停用≠销毁）。</summary>
    protected virtual void OnDeactivate()
    {
    }

    /// <summary>外壳事件流透传（progress/finished/login_* 等），默认忽略。</summary>
    protected virtual void OnEvent(ApiEvent ev)
    {
    }

    /// <summary>外壳接线入口：首次挂载触发 OnCreate，之后每次激活触发 OnActivate。</summary>
    internal void ShellAttach(object? parameter)
    {
        if (!_created)
        {
            _created = true;
            OnCreate();
        }
        OnNavigate(parameter);
        OnActivate();
    }

    internal void ShellDetach() => OnDeactivate();

    /// <summary>外壳事件流派发入口（页面内 OnEvent 为 protected，外壳经此转发）。</summary>
    internal void ShellEvent(ApiEvent ev) => OnEvent(ev);

    // ───────────────────────────── 页面内容出口（布局自定义钩子已删除） ─────────────────────────────

    /// <summary>页面内容出口:子类构建 Body 后经此赋给 Content。</summary>
    // 契约变更(R32-LW):原 `public static Func<string, Control, Control>? LayoutWrapper` 钩子已删除 ——
    // 它是 M2-B4 遗留的占位("B4 的 BlockHost 会设置它"),实际从未被任何页面/BlockHost 赋值或调用
    // (grep 全树仅本文件 2 处引用),且 Windows 冻结件 windos/src/TipCL/Pages/PageBase.cs 无此成员。
    // 页面托管区块改走显式 `BlockHost(pageId, specs, ...)` 并入页面自己的根布局,不再经此隐式包装。
    protected Control Body
    {
        set => Content = value;
    }

    // ───────────────────────────── 构建小工具 ─────────────────────────────

    /// <summary>纵向布局，间距用 BodyGap 令牌。</summary>
    protected static StackPanel V(double gap = BodyGap, double margin = 0)
        => new() { Spacing = gap, Margin = new Thickness(margin) };

    /// <summary>标准 ScrollBody：内容吃满 + Body 边距令牌，供内容可滚动的页面用。</summary>
    protected static ScrollViewer ScrollBody(Control inner, double marginX = BodyMargin, double marginTop = 16, double marginBottom = 16)
        => new()
        {
            Content = inner,
            Padding = new Thickness(0),
            Margin = new Thickness(marginX, marginTop, marginX, marginBottom),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

    /// <summary>页标题（21px Bold，对齐 A2 阶梯 T.PageTitle）。</summary>
    protected TextBlock PageTitle_(string text)
        => new()
        {
            Text = text,
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            [!TextBlock.ForegroundProperty] = Res.Bind("B.Ink"),
        };

    /// <summary>轻提示（Toast 服务）。</summary>
    protected static void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
        => ToastHost.Show(title, body, kind);
}
