// R32-MED:来源 Windows 版参考仓库 windos/src/TipCL/Pages/Layout/LayoutFill.cs(L1 35 行;
//   整文件只在 L1 有 —— L2@33b5fff 里这个文件是"新增未在 L2",WT-R31-MERGE 里与 L1 逐字节相同)。
//   Linux 同层落地点:wt-linux-layout/src/TipCL.App/Pages/Layout/LayoutFill.cs。
// WPF → Avalonia 11 映射(本文件,与 Linux 版逐条相同):
//   DependencyProperty.RegisterAttached("FillsBody", typeof(bool), typeof(LayoutFill), new PropertyMetadata(false))
//     → AvaloniaProperty.RegisterAttached<Control, bool>("FillsBody", typeof(LayoutFill), false)
//       (Avalonia 的签名是 (name, ownerType, defaultValue, ...):泛型第 1 参 = **宿主类型** Control、
//        ownerType = 注册者 LayoutFill(静态类可以作 Type 传);返回 AttachedProperty<bool>,
//        语义 = "挂在任意 Control 上的 bool,缺省 false")
//   SetFillsBody(DependencyObject, bool) / GetFillsBody(DependencyObject)
//     → SetValue/GetValue;参数类型 DependencyObject → Control(与 R32-WIN-LAYOUT-SPEC §0.2.6 的并集签名一致)
//   空值口径(与 lin-editor 约定):GetFillsBody(null) = false,不抛(WPF 版传 null 会 NRE;
//   编辑器调用点是 `spec.Content != null && GetFillsBody(spec.Content)`,空值安全只是省掉调用方的显式判空)。
// 与 Linux 版唯一的差别:namespace TipCL.App.Pages.Layout → TipCL.Pages(mac D7 扁平),以及 XML <see cref> 指向同树类型。
using Avalonia;
using Avalonia.Controls;

namespace TipCL.Pages;

/// <summary>
/// [layout-fix] V-1:标记"页面主体填充块"的附加属性(挂在那个区块的**内容元素**上,即
/// <see cref="BlockSpec.Content"/>)。带这个标记、并且**正好是最后一块**的区块,
/// 在非编辑态吃掉内容区剩下的高度(实现见 <see cref="FillPanel.FillChild"/>);
/// 顺序被用户在编辑态改掉时退回原来的"按内容高度排布",不做任何猜测。
///
/// 为什么需要它:改版前这类页面的根是 <c>Grid("Auto,*")</c> —— 顶部一条在 Auto 行、主体在 * 行,
/// 主体因此天然占满剩下的高度。插件页的空状态占位是 <c>Ui.Empty</c>(自带 VerticalAlignment=Center),
/// 就是靠这份高度才在页面中部垂直居中的;改成 <c>BlockHost</c> 之后所有区块都按各自内容高度排布,
/// 那 422px 的剩余高度没有了 ⇒ 空状态整块上移贴到页签下面(R31 复验单 V-1)。
/// 页面在自己这个"主体块"上挂一个标记即可恢复旧观感。
///
/// 不挂标记的页面(启动页 / 下载页 / 任务页 / 设置页各视图......)渲染一个像素都不变 ——
/// 这一条是拿到"改动前后逐字节相同"那三页的截图对照的前提。
/// </summary>
public static class LayoutFill
{
	/// <summary>该区块是否"吃掉内容区剩余高度"(默认 false = 与改动前逐像素一致的按内容高度排布)。</summary>
	public static readonly AttachedProperty<bool> FillsBodyProperty = AvaloniaProperty.RegisterAttached<Control, bool>(
		"FillsBody", typeof(LayoutFill), false);

	public static void SetFillsBody(Control element, bool value)
	{
		element.SetValue(FillsBodyProperty, value);
	}

	/// <summary>取值;元素为 null 时返回 false(不抛)。</summary>
	public static bool GetFillsBody(Control? element)
	{
		return element != null && element.GetValue(FillsBodyProperty);
	}
}
