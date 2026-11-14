using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// `PlanCard` 的用例(它解开另一条线的 `ShowPlan`)。断的是**结构与文案**(不是像素):
/// 计数来自 <c>AiUiText.PlanProgress</c>(completed/done 才算完成)、标题行有「执行计划」与 `· 已完成 / 总数`、
/// 完成行是**删除线 + 弱色**、进行行是加粗。
/// </summary>
public class PlanCardTests
{
	private static List<(string Title, string Status)> Items()
	{
		return new List<(string, string)>
		{
			("第一步", "completed"),
			("第二步", "in_progress"),
			("第三步", "pending"),
		};
	}

	/// <summary>计数与文案:1/3、标题存在;进度条的宽度/高度是 Windows 那两个值(160×5)。</summary>
	[Fact]
	public void Card_counts_progress_and_shows_the_header()
	{
		AvaloniaSession.Run(() =>
		{
			PlanCard card = new PlanCard(Items());
			Assert.Equal(1, card.Done);
			Assert.Equal(3, card.Total);
			Assert.True(C4Files.HasText(card, "执行计划"));
			Assert.True(C4Files.HasText(card, "· 1 / 3"));
			ProgressBar bar = card.GetLogicalDescendants().OfType<ProgressBar>().Single();
			Assert.Equal(160.0, bar.Width);
			Assert.Equal(5.0, bar.Height);
			// 三条计划行都在
			List<TextBlock> labels = card.GetLogicalDescendants().OfType<TextBlock>().Where(t => (t.Text ?? "").StartsWith("第")).ToList();
			Assert.Equal(3, labels.Count);
		});
	}

	/// <summary>
	/// 完成行 = **删除线 + 弱色**,进行行 = **加粗**,未开始行 = 普通(与 Windows 的三种画法一一对应)。
	/// </summary>
	[Fact]
	public void Row_style_follows_the_status()
	{
		AvaloniaSession.Run(() =>
		{
			PlanCard card = new PlanCard(Items());
			TextBlock done = card.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "第一步");
			TextBlock running = card.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "第二步");
			TextBlock pending = card.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "第三步");
			Assert.Equal(TextDecorations.Strikethrough, done.TextDecorations);
			Assert.Equal(FontWeight.SemiBold, running.FontWeight);
			Assert.Equal(FontWeight.Normal, pending.FontWeight);
			Assert.Null(pending.TextDecorations);
		});
	}

	/// <summary>空计划(0 条)也不崩,计数是 0/0、进度条按 0 算(Windows 的 `(Total &gt; 0) ? … : 0`)。</summary>
	[Fact]
	public void Empty_plan_is_safe()
	{
		AvaloniaSession.Run(() =>
		{
			PlanCard card = new PlanCard(new List<(string, string)>());
			Assert.Equal(0, card.Done);
			Assert.Equal(0, card.Total);
			Assert.True(C4Files.HasText(card, "· 0 / 0"));
			Assert.Equal(0.0, card.GetLogicalDescendants().OfType<ProgressBar>().Single().Value);
		});
	}
}
