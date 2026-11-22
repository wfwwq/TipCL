// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/Widgets/WidgetUI.cs(分支 r32-r31-merge @ 0a304a7)。
// 移植说明:
//  1 机械改写:namespace TipCL.Pages.Layout → TipCL.App.Pages.Layout;删掉 Linux 不存在的 `using TipCL.Services;`
//    (I18n 住 TipCL.Core.Launcher.I18nCore,Py 住 TipCL.Core);`I18n.L(x)` → `I18nCore.Tr(x)`,
//    `I18n.L(x, a)` → `Py.Fmt(I18nCore.Tr(x), a)`;`UIElement` → `Control`;`Ui.*` → `U.*`(同命名空间门面,
//    裸 `Ui.` 会被 TipCL.App.Ui 命名空间遮蔽);`HorizontalAlignment` 走 Avalonia.Layout。
//  2 降级(WebView2 专属,按本仓既有先例 PluginUi.cs:6 / PluginHostPage.cs:5-7 处理):EditAwareShell 的
//    「编辑态感知并藏起 WebView(HwndHost)」与「进程退出前主动 Dispose WebView2,免得 HwndHost 终结器
//    在 COM 已拆的线程上抛异常(实测退出码 -532462766)」两套机制**只服务于 WebView2**;Linux 没有 WebView
//    宿主,故整体不移植,只保留通用的「离开可视树后延迟确认再释放内容」通路。
using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

// 自建区块的界面层:按类型分派到具体渲染器,并给"查不到 / 已禁用 / 动作不合法"三种情况统一占位。
// 铁律:占位只影响显示,**绝不动布局文档**(用户数据永远保留),删除入口在设置页(L5)。
internal static class WidgetUI
{
    /// <summary>按类型分派渲染。</summary>
    public static Control Build(WidgetDef widget, string pageId)
    {
        if (widget == null)
        {
            return Placeholder(I18nCore.Tr("自建区块不可用"), I18nCore.Tr("布局文档里没有这个区块的定义。"));
        }
        switch (widget.Type)
        {
        case LayoutSchema.WidgetNote:
            return BasicWidgets.Note(widget);
        case LayoutSchema.WidgetClock:
            return BasicWidgets.Clock(widget);
        case LayoutSchema.WidgetDivider:
            return BasicWidgets.Divider(widget);
        case LayoutSchema.WidgetShortcut:
            return ShortcutWidget.Build(widget);
        case LayoutSchema.WidgetPlaytime:
            return PlaytimeWidget.Build(widget);
        case LayoutSchema.WidgetWeb:
        case LayoutSchema.WidgetButton:
            return DangerousWidgets.Build(widget);
        default:
            return Placeholder(I18nCore.Tr("自建区块不可用"), Py.Fmt(I18nCore.Tr("未知的区块类型:{0}"), widget.Type ?? ""));
        }
    }

    /// <summary>布局里引用了 custom:&lt;id&gt; 但文档里没有这个定义(被外部工具删了):占位,不删引用。</summary>
    /// <remarks>R35 lin-int(GOAL §5.1「永远不显示 id」):说明文字里不再带块 id;参数保留(调用方 / 签名不变)。</remarks>
    public static Control MissingWidget(string blockId)
    {
        _ = blockId;
        return Placeholder(I18nCore.Tr("自建区块不可用"), I18nCore.Tr("找不到这个组件的内容，可能已被删除；可以在编辑态点「添加」重新放一个。"));
    }

    /// <summary>危险类型被 Q5 默认关闭时的占位。</summary>
    public static Control DisabledDangerous(WidgetDef widget)
    {
        return Placeholder(I18nCore.Tr("已禁用的自建区块"), I18nCore.Tr("web / button 属于危险类型,默认关闭;要在设置里显式开启(填 settings.enabled = true)才会渲染。"));
    }

    /// <summary>统一的占位样式(和其它"不可用"提示一致)。</summary>
    public static Control Placeholder(string title, string hint)
    {
        Border card = U.Card(U.V(4.0, U.Txt(title, 13.0, bold: true), U.Small(hint).Wrap()), 10.0);
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        return card;
    }

    /// <summary>区块标题(有用户标题就用它,否则用类型的中文名)。</summary>
    public static string TitleOf(WidgetDef widget)
    {
        if (widget != null && !string.IsNullOrEmpty(widget.Title))
        {
            return widget.Title;
        }
        return TypeLabel((widget != null) ? widget.Type : "");
    }

    /// <summary>类型的中文名。</summary>
    public static string TypeLabel(string type)
    {
        switch (type)
        {
        case LayoutSchema.WidgetNote:
            return I18nCore.Tr("便签");
        case LayoutSchema.WidgetShortcut:
            return I18nCore.Tr("快捷启动");
        case LayoutSchema.WidgetClock:
            return I18nCore.Tr("时钟");
        case LayoutSchema.WidgetDivider:
            return I18nCore.Tr("分隔线");
        case LayoutSchema.WidgetPlaytime:
            return I18nCore.Tr("游戏时长");
        case LayoutSchema.WidgetWeb:
            return I18nCore.Tr("网页");
        case LayoutSchema.WidgetButton:
            return I18nCore.Tr("按钮");
        default:
            return I18nCore.Tr("自建区块");
        }
    }
}

/// <summary>
/// 可显式释放内容的外壳:被摘下可视树(切页 / 编辑态重排都会摘)后用一次低优先级回调确认它真的离开了
/// 可视树,再释放内容;下次挂回来时由子类重建。
/// Windows 版这里还有两套 WebView2 专属机制 —— 向上找宿主 ContentControl、按 IsHitTestVisible 感知编辑态
/// 以临时藏起 WebView2(HwndHost,否则会盖住拖拽手柄与操作条),以及进程退出前主动 Dispose WebView2
/// (否则 HwndHost 终结器会在 COM 已拆的线程上抛异常,实测退出码 -532462766);Linux 无 WebView 宿主,
/// 按本仓既有先例(PluginUi.cs:6)整体不移植。
/// </summary>
internal abstract class EditAwareShell : ContentControl
{
    // 内容是不是"必须显式 Dispose"的东西(Linux 上目前没有这样的内容,保留开关以便插件区块沿用同一外壳)
    private readonly bool _disposeOnDetach;

    protected EditAwareShell(bool disposeOnDetach = false)
    {
        _disposeOnDetach = disposeOnDetach;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        DetachedFromVisualTree += delegate
        {
            if (_disposeOnDetach)
            {
                // 不能立刻销毁:编辑态每次 Render 都会把卡片摘下来再挂回去;
                // 用一次低优先级回调确认它真的离开了可视树再销毁
                Dispatcher.UIThread.Post(new Action(delegate
                {
                    if (this.GetVisualRoot() == null)
                    {
                        DisposeContent();
                    }
                }), DispatcherPriority.Background);
            }
        };
    }

    /// <summary>销毁内容(默认把 IDisposable 内容 Dispose 掉);子类可重写以便重建。</summary>
    protected virtual void DisposeContent()
    {
        (Content as IDisposable)?.Dispose();
        Content = null;
    }
}
