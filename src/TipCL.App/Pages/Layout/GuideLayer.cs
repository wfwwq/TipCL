// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/GuideLayer.cs(L1 129 行;
//       L2@33b5fff 逐字节相同、WT-R31-MERGE 也逐字节相同)。
// WPF → Avalonia 11 映射(本文件):
//   : FrameworkElement                  → : Control(Avalonia 没有 FrameworkElement;自绘基类用 Control)
//   protected override void OnRender(DrawingContext) → public override void Render(DrawingContext)
//   SnapsToDevicePixels = true          → UseLayoutRounding = true(SnapsToDevicePixels 在 Avalonia 不存在;
//                                          1px 线的 +0.5 偏移源码里本来就做了,视觉口径不变)
//   Brush.Freeze() / Pen.Freeze()       → 删(无对应;Avalonia 的笔刷/画笔不冻结也不变形)
//   base.ActualWidth/ActualHeight       → Bounds.Width/Bounds.Height
//   Pen.DashStyle = new DashStyle(new double[]{4,3}, 0) → 同名(Avalonia.Media.DashStyle)
//   dc.DrawLine(IPen, Point, Point) / dc.DrawRectangle(IBrush?, IPen?, Rect) → 同签名
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

// 对齐辅助线 / 框选矩形的绘制层(§4.G)。
// 只画不判:判定与磁性吸附位移全在纯模型 LayoutGuides.Align(阈值 4px),这样 net8 测试宿主能断言,
// 而这一层只负责把结果画到画布上方(IsHitTestVisible=false,不吃任何指针事件)。
// 视觉:主题色 10% 透明实线 1px(辅助线)+ 主题色虚线(框选),与编辑态蒙版同一套配色。
public sealed class GuideLayer : Control
{
	private readonly List<LayoutGuideLine> _lines = new List<LayoutGuideLine>();

	private bool _hasMarquee;

	private LayoutRect _marquee;

	// [layout-fix 第三批·任务 B] 改大小时跟在卡片右下角的像素尺寸提示("640 × 220")
	private string _sizeText = "";

	private Point _sizeAnchor;

	/// <summary>构造:不吃指针事件(拖动期间指针必须留在被拖块上)。</summary>
	public GuideLayer()
	{
		IsHitTestVisible = false;
		UseLayoutRounding = true;
	}

	/// <summary>当前是否画着东西(宿主可据此决定要不要刷新)。</summary>
	public bool IsActive
	{
		get
		{
			return _lines.Count > 0 || _hasMarquee || _sizeText.Length > 0;
		}
	}

	/// <summary>画辅助线(list 为空 = 清掉线,保留框选)。</summary>
	public void ShowLines(IReadOnlyList<LayoutGuideLine>? lines)
	{
		_lines.Clear();
		if (lines != null)
		{
			foreach (LayoutGuideLine line in lines)
			{
				if (line != null)
				{
					_lines.Add(line);
				}
			}
		}
		InvalidateVisual();
	}

	/// <summary>画框选矩形。</summary>
	public void ShowMarquee(LayoutRect rect)
	{
		_hasMarquee = true;
		_marquee = rect;
		InvalidateVisual();
	}

	/// <summary>[任务 B] 在 <paramref name="bottomRight"/>(本层坐标,卡片右下角)旁画像素尺寸提示;空串 = 不画。</summary>
	public void ShowSize(Point bottomRight, string text)
	{
		_sizeAnchor = bottomRight;
		_sizeText = text ?? "";
		InvalidateVisual();
	}

	/// <summary>当前的尺寸提示文本(测试 / 调试用;没有 = 空串)。</summary>
	public string SizeText
	{
		get
		{
			return _sizeText;
		}
	}

	/// <summary>全清(退出拖拽/框选时调)。</summary>
	public void Clear()
	{
		if (_lines.Count == 0 && !_hasMarquee && _sizeText.Length == 0)
		{
			return;
		}
		_lines.Clear();
		_hasMarquee = false;
		_sizeText = "";
		InvalidateVisual();
	}

	/// <summary>
	/// 画辅助线:竖线/横线都从画布内容区的一头拉到另一头(与设计一致:对齐线要贯通整个画布,
	/// 只画"两块之间"那一小段看不出对齐关系)。
	/// </summary>
	public override void Render(DrawingContext context)
	{
		if (_lines.Count == 0 && !_hasMarquee && _sizeText.Length == 0)
		{
			return;
		}
		Color accent = LayoutVisualTokens.AccentColor();
		if (_sizeText.Length != 0)
		{
			// 主题色小胶囊 + 白字,贴在卡片右下角内侧(放不下就往里收,不出画布)
			FormattedText text = new FormattedText(_sizeText, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11.0, Brushes.White);
			double w = text.Width + 12.0;
			double h = text.Height + 6.0;
			double x = Clamp(_sizeAnchor.X - w - 6.0, 0.0, Math.Max(0.0, Bounds.Width - w));
			double y = Clamp(_sizeAnchor.Y - h - 6.0, 0.0, Math.Max(0.0, Bounds.Height - h));
			context.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, accent.R, accent.G, accent.B)), null, new RoundedRect(new Rect(x, y, w, h), h / 2.0));
			context.DrawText(text, new Point(x + 6.0, y + 3.0));
		}
		if (_lines.Count != 0)
		{
			Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)), 1.0);
			double width = Math.Max(0.0, Bounds.Width);
			double height = Math.Max(0.0, Bounds.Height);
			foreach (LayoutGuideLine line in _lines)
			{
				if (line.Vertical)
				{
					double x = Clamp(line.Position, 0.0, width);
					context.DrawLine(pen, new Point(x + 0.5, 0.0), new Point(x + 0.5, height));
				}
				else
				{
					double y = Clamp(line.Position, 0.0, height);
					context.DrawLine(pen, new Point(0.0, y + 0.5), new Point(width, y + 0.5));
				}
			}
		}
		if (_hasMarquee)
		{
			Pen pen2 = new Pen(new SolidColorBrush(Color.FromArgb(180, accent.R, accent.G, accent.B)), 1.0)
			{
				DashStyle = new DashStyle(new double[2] { 4.0, 3.0 }, 0.0)
			};
			SolidColorBrush fill = new SolidColorBrush(Color.FromArgb(16, accent.R, accent.G, accent.B));
			context.DrawRectangle(fill, pen2, new Rect(_marquee.X, _marquee.Y, Math.Max(0.0, _marquee.W), Math.Max(0.0, _marquee.H)));
		}
	}

	private static double Clamp(double value, double min, double max)
	{
		if (value < min)
		{
			return min;
		}
		if (value > max)
		{
			return max;
		}
		return value;
	}
}

/// <summary>
/// [R35 lin-l12] GOAL §5.1「拖动时显示淡色列底纹」(效果图 #winA.edit.grid.busy .cols:每列一条 8px 圆角、
/// rgba(0,0,0,.035) 的淡色竖条)。画在画布**之下**(LayoutEditor 把它放在 _stage 的第 0 层),只在拖动 / 改宽度期间显示。
/// 颜色读 L0 的 B.EditColShade;查不到用效果图的数值。启动页这类深色表面用白色 6%(pages3 .dark .colsbg i)。
/// 列宽 / 间距 / 列数由宿主在手势开始时从画布读来塞进来(画布 GridCanvasPanel 归 L3,这里不碰它)。
/// </summary>
public sealed class EditColumnShade : Control
{
	private int _cols;

	private double _columnWidth;

	private double _gap;

	private bool _dark;

	/// <summary>构造:不吃指针事件,默认不显示。</summary>
	public EditColumnShade()
	{
		IsHitTestVisible = false;
		IsVisible = false;
		UseLayoutRounding = true;
	}

	/// <summary>当前列数(测试用)。</summary>
	public int Columns => _cols;

	/// <summary>显示:按画布当前的列数 / 列宽 / 间距画淡色列。</summary>
	public void Show(int cols, double columnWidth, double gap, bool dark)
	{
		_cols = Math.Max(0, cols);
		_columnWidth = Math.Max(0.0, columnWidth);
		_gap = Math.Max(0.0, gap);
		_dark = dark;
		IsVisible = _cols > 0 && _columnWidth > 0.5;
		InvalidateVisual();
	}

	/// <summary>收起。</summary>
	public void Hide()
	{
		if (IsVisible)
		{
			IsVisible = false;
		}
	}

	/// <summary>当前用的列底纹画刷(L0 键 → 兜底)。</summary>
	internal IBrush ShadeBrush()
	{
		if (_dark)
		{
			return LayoutEditChrome.BrushOr("B.EditColShadeOnDark", null, Color.FromArgb(15, 255, 255, 255));
		}
		return LayoutEditChrome.BrushOr("B.EditColShade", null, Color.FromArgb(9, 0, 0, 0));
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		if (_cols <= 0 || _columnWidth <= 0.5)
		{
			return;
		}
		double height = Math.Max(0.0, Bounds.Height);
		if (height <= 1.0)
		{
			return;
		}
		IBrush brush = ShadeBrush();
		for (int i = 0; i < _cols; i++)
		{
			double x = i * (_columnWidth + _gap);
			context.DrawRectangle(brush, null, new RoundedRect(new Rect(x, 0.0, _columnWidth, height), 8.0));
		}
	}
}
