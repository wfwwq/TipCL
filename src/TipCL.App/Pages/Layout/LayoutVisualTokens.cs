// 新增小文件(R32-LV 自有):GridCanvasPanel / GuideLayer 共用的编辑态配色。
// Windows 版这两处都直接调 LayoutEditor.AccentColor()(读资源 "B.Accent",拿不到就回落到
// Color.FromRgb(46,155,107));Linux 树的 LayoutEditor.cs 属另一工作包,本包不引用它的内部成员,
// 故把这一个纯函数抽到本文件 —— 取值口径与 Windows 版逐字一致(B.Accent 缺失 → 同一个绿色兜底)。
using Avalonia.Media;

namespace TipCL.App.Pages.Layout;

/// <summary>布局可视化层(自绘 Panel / 辅助线层)要用的主题色。取值口径与 Windows 版
/// <c>LayoutEditor.AccentColor()</c> 一致:资源 <c>B.Accent</c> 存在就用它,否则回落 <c>#2E9B6B</c>。</summary>
internal static class LayoutVisualTokens
{
	/// <summary>主题强调色(资源缺失时的兜底色与 Windows 版相同)。</summary>
	public static Color AccentColor()
	{
		if (U.Brush("B.Accent") is SolidColorBrush brush)
		{
			return brush.Color;
		}
		return Color.FromRgb(46, 155, 107);
	}
}
