using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// 执行计划卡(= Windows <c>Pages\PlanCard.cs</c>,95 行)—— 标题行(清单图标 + 「执行计划」+ `· 已完成 / 总数`
/// + 一根 160×5 的进度条)+ 逐条计划行(已完成 = 实心点 + 白勾 + **删除线**且文案转弱色;进行中 = 空心点 + 实心小点 +
/// 加粗;未开始 = 空心点)。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item><c>SetResourceReference</c> → <c>Dyn</c>;颜色键(<c>B.AccentSoft2</c>/<c>B.Paper</c>/<c>B.Accent</c>/
///   <c>B.InkFaint</c>/<c>B.InkMuted</c>/<c>B.Ink</c>)、圆角 10、内边距(13,10,13,10)、`MaxWidth 760`、
///   进度条 160×5、行距 2/9/2 与 Windows **逐值相同**;</item>
/// <item>图标走 <see cref="AiIcons"/>(path 数据逐字照抄),勾选路径的 <c>StrokeThickness 3.5</c> 相同;</item>
/// <item>完成度口径复用 <see cref="AiUiText.PlanProgress"/>(C8a 已落地):状态为 <c>completed</c>/<c>done</c> 才算完成。</item>
/// </list>
/// </summary>
internal sealed class PlanCard : Border
{
	/// <summary>已完成条数(Windows 同名属性)。</summary>
	internal int Done { get; }

	/// <summary>总条数(Windows 同名属性)。</summary>
	internal int Total { get; }

	public PlanCard(List<(string Title, string Status)> items)
	{
		(int Done, int Total) progress = AiUiText.PlanProgress(items);
		Done = progress.Done;
		Total = progress.Total;
		ProgressBar bar = Ui.Prog((Total > 0) ? ((double)Done * 100.0 / (double)Total) : 0.0);
		bar.Height = 5.0;
		bar.Width = 160.0;
		Grid head = Ui.G(null, "Auto,Auto,Auto,*,Auto");
		head.Add(AiIcons.Icon(AiIcons.List, 14.0, "B.AccentText").VCenter(), 0);
		head.Add(Ui.Txt(I18n.L("执行计划"), 12.5, bold: true, "B.Ink").M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		head.Add(Ui.Txt($"· {Done} / {Total}", 12.0, bold: false, "B.InkMuted").M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		head.Add(bar.VCenter(), 0, 4);
		SPanel body = Ui.V(2.0, head.M(0.0, 0.0, 0.0, 6.0));
		foreach ((string title, string status) in items)
		{
			body.Children.Add(Row(title, status));
		}
		base.Child = body;
		base.Padding = new Thickness(13.0, 10.0, 13.0, 10.0);
		base.BorderThickness = new Thickness(1.0);
		base.CornerRadius = new CornerRadius(10.0);
		base.MaxWidth = 760.0;
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		this.Dyn(Border.BorderBrushProperty, "B.AccentSoft2");
		this.Dyn(Border.BackgroundProperty, "B.Paper");
	}

	/// <summary>【Windows 源】<c>:45-94</c> 一条计划行(状态点 + 文案;完成转删除线且弱色)。</summary>
	private static Control Row(string title, string status)
	{
		bool done = status == "completed" || status == "done";
		bool running = status == "in_progress";
		Grid dot = new Grid
		{
			Width = 16.0,
			Height = 16.0,
		};
		Ellipse ring = new Ellipse
		{
			StrokeThickness = 1.5,
		};
		if (done)
		{
			ring.Dyn(Shape.FillProperty, "B.Accent");
			ring.Dyn(Shape.StrokeProperty, "B.Accent");
			dot.Children.Add(ring);
			Shape check = AiIcons.Path(AiIcons.Check, 10.0, Brushes.White);
			check.StrokeThickness = 3.5;
			dot.Children.Add(check.Mid());
		}
		else if (running)
		{
			ring.Dyn(Shape.StrokeProperty, "B.Accent");
			dot.Children.Add(ring);
			Ellipse pip = new Ellipse
			{
				Width = 6.0,
				Height = 6.0,
			};
			pip.Dyn(Shape.FillProperty, "B.Accent");
			dot.Children.Add(pip.Mid());
		}
		else
		{
			ring.Dyn(Shape.StrokeProperty, "B.InkFaint");
			dot.Children.Add(ring);
		}
		TextBlock label = Ui.Txt(title, 12.5, running, done ? "B.InkMuted" : "B.Ink").Wrap();
		if (done)
		{
			label.TextDecorations = TextDecorations.Strikethrough;
		}
		Grid row = Ui.G(null, "Auto,*");
		row.Add(dot.M(0.0, 2.0), 0);
		row.Add(label.M(9.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		row.Margin = new Thickness(0.0, 2.0, 0.0, 2.0);
		return row;
	}
}
