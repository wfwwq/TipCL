using Avalonia.Controls;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// R32-MWG:Windows 版 <c>Pages\Layout\Widgets\WidgetUI.cs</c>(WPF / net48)到 mac(Avalonia 11 / net8.0)的移植。
///
/// 自建区块的界面层:按类型分派到具体渲染器,并给"查不到 / 已禁用 / 动作不合法"三种情况统一占位。
/// 铁律:占位只影响显示,**绝不动布局文档**(用户数据永远保留),删除入口在设置页(L5)。
///
/// Windows → mac 差异(逐条):
/// <list type="number">
/// <item><b>namespace</b> <c>TipCL.Pages.Layout</c> → <c>TipCL.Pages</c>(R32-D7 扁平);</item>
/// <item><b>UIElement → Control</b>:Avalonia 没有 UIElement/FrameworkElement 的二分,
///   所有可视元素都派生自 <c>Control</c>;返回类型与 <c>LayoutEditor.ResolveContent</c>
///   的 <c>built is Control</c> 判定对齐;</item>
/// <item><b>EditAwareShell 整套不搬(D1/D2/D3)</b>:见文件末尾说明。</item>
/// </list>
/// </summary>
internal static class WidgetUI
{
	/// <summary>按类型分派渲染。</summary>
	public static Control Build(WidgetDef? widget, string pageId)
	{
		if (widget == null)
		{
			return Placeholder(I18n.L("自建区块不可用"), I18n.L("布局文档里没有这个区块的定义。"));
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
			return Placeholder(I18n.L("自建区块不可用"), I18n.L("未知的区块类型:{0}", widget.Type ?? ""));
		}
	}

	/// <summary>布局里引用了 custom:&lt;id&gt; 但文档里没有这个定义(被外部工具删了):占位,不删引用。</summary>
	public static Control MissingWidget(string blockId)
	{
		return Placeholder(I18n.L("自建区块不可用"), I18n.L("布局文档里找不到 {0} 的定义;可以重新添加同名区块。", blockId ?? ""));
	}

	/// <summary>危险类型被 Q5 默认关闭时的占位。</summary>
	public static Control DisabledDangerous(WidgetDef widget)
	{
		return Placeholder(I18n.L("已禁用的自建区块"), I18n.L("web / button 属于危险类型,默认关闭;要在设置里显式开启(填 settings.enabled = true)才会渲染。"));
	}

	/// <summary>统一的占位样式(和其它"不可用"提示一致)。</summary>
	public static Control Placeholder(string title, string hint)
	{
		// 【mac 差异】Windows 写作 card.HorizontalAlignment = HorizontalAlignment.Stretch;
		// mac 用既有扩展 UiExt.Stretch<T>(this T) where T : Control,等价语义(Avalonia 没有 WPF 的 Layout 属性包装)。
		Border card = Ui.Card(Ui.V(4.0, Ui.Txt(title, 13.0, bold: true), Ui.Small(hint).Wrap()), 10.0);
		return card.Stretch();
	}

	/// <summary>区块标题(有用户标题就用它,否则用类型的中文名)。</summary>
	public static string TitleOf(WidgetDef? widget)
	{
		if (widget != null && !string.IsNullOrEmpty(widget.Title))
		{
			return widget.Title;
		}
		return TypeLabel((widget != null) ? widget.Type : "");
	}

	/// <summary>类型的中文名。</summary>
	public static string TypeLabel(string? type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return I18n.L("便签");
		case LayoutSchema.WidgetShortcut:
			return I18n.L("快捷启动");
		case LayoutSchema.WidgetClock:
			return I18n.L("时钟");
		case LayoutSchema.WidgetDivider:
			return I18n.L("分隔线");
		case LayoutSchema.WidgetPlaytime:
			return I18n.L("游戏时长");
		case LayoutSchema.WidgetWeb:
			return I18n.L("网页");
		case LayoutSchema.WidgetButton:
			return I18n.L("按钮");
		default:
			return I18n.L("自建区块");
		}
	}
}

// ————— EditAwareShell:整套如实不搬(mac 差异 D1/D2/D3)—————
//
// Windows 的 EditAwareShell(ContentControl 子类)存在的唯一理由:
//   向上找到最近的 ContentControl 宿主,监听它的 IsHitTestVisibleChanged,
//   编辑态把内嵌 WebView2(HwndHost)临时藏起来 —— 因为 HwndHost 会盖住拖拽手柄与操作条。
//
// mac 上这三条前提全部不成立,所以这个名字在本树里**不存在**:
//   1)【D1/D2】mac 没有 WebView2:GOAL.md 第 34 行的降级结论 (D-154) 是"不内嵌浏览器";
//      DangerousWidgets.Web 永远走"在浏览器中打开"分支,不产出任何需要藏起来的原生子窗口;
//   2)【D3】插件区块用真实原生内容/占位(PluginBlocks),同样没有原生子窗口;
//   3)【Avalonia API】即便假设有可藏的东西,Avalonia 也没有 IsHitTestVisibleChanged 事件
//      (全树 grep 只有属性赋值、没有事件订阅),宿主编辑态通知链无法照抄 ——
//      既有先例是 Pages/Plugins/PluginDashCards.cs:它的 CardShell 就是普通 ContentControl,
//      class 文档里明写 HookOwner/ApplyEdit "整套如实不搬"。
//
// 因此本文件只保留纯渲染分派与占位;编辑态相关的机制落在渲染器本身不需要的地方(不落)。
