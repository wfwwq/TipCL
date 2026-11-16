// R32-MED:新增小文件(GridCanvasPanel / GuideLayer / LayoutEditor 共用的编辑态配色)。
// 来源:
//   Windows 版参考仓库 windos/src/TipCL/Pages/Layout/LayoutEditor.cs 的 AccentColor()(L1 版;L2/WT-R31-MERGE 同)。
//   Linux 同层落地点 wt-linux-layout/src/TipCL.App/Pages/Layout/LayoutVisualTokens.cs(R32-LV 的等价抽取件)。
// Windows 版里 GridCanvasPanel / GuideLayer / LayoutEditor 三处都直接调 LayoutEditor.AccentColor()
// (读资源 "B.Accent",拿不到就回落到 Color.FromRgb(46,155,107))。
// mac 树里同一个取值口径已经存在:Pages/BlockHost.cs 的 BlockHost.AccentColor()(C3a 落地,internal static,
// 逐字就是 Windows 那个函数)。本包不再抄一份,改为**委托**过去 —— 全树只有一处实现,不会出现两份漂移。
// (Linux 版之所以复制一份,是因为那里的 LayoutEditor.cs 属另一工作包;mac 树没有这个约束。)
using Avalonia.Media;

namespace TipCL.Pages;

/// <summary>布局可视化层(自绘 Panel / 辅助线层 / 编辑器)要用的主题色。取值口径与 Windows 版
/// <c>LayoutEditor.AccentColor()</c> 一致:资源 <c>B.Accent</c> 存在就用它,否则回落 <c>#2E9B6B</c>。
/// mac 实现直接转调 <see cref="BlockHost.AccentColor"/> —— 同一个函数,不复制第二份。</summary>
internal static class LayoutVisualTokens
{
	/// <summary>主题强调色(资源缺失时的兜底色与 Windows 版相同)。</summary>
	public static Color AccentColor()
	{
		return BlockHost.AccentColor();
	}
}
