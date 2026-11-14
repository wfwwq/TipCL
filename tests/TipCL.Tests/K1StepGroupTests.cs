using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL.Pages;
using TipCL.Pages.Ai;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// K1:<see cref="StepGroup"/> 的用例(工具步骤组;`AiPage` 的硬前置件)。
/// 口径:标题摘要走 <c>AiUiText.StepSummary</c>(已有单测),这里钉的是**组自己的行为** ——
/// 步数统计、行的归属转移、完成时"未手动切过且 &gt; 3 步才自动收起"、失败步数的计数文案、子代理容器。
/// 点击那条路走 <c>ToggleForTest</c>(D-127:与真实处理器同一段代码,不模拟鼠标)。
/// </summary>
public class K1StepGroupTests
{
	/// <summary>新建即展开,标题是"执行中"那套摘要(0 步、还没跑满 0.1s ⇒ 不带用时)。</summary>
	[Fact]
	public void New_group_is_open_and_titles_itself_as_running()
	{
		AvaloniaSession.Run(() =>
		{
			StepGroup group = new StepGroup();
			Assert.True(group.IsOpen);
			Assert.Equal(0, group.Count);
			Assert.StartsWith("执行了 0 步", group.TitleText);
		});
	}

	/// <summary>加行会计数;同一行再挂一次不重复(已经在组里就只刷新);从别的容器挂过来会被摘走。</summary>
	[Fact]
	public void Lines_are_counted_and_moved_between_containers()
	{
		AvaloniaSession.Run(() =>
		{
			StepGroup group = new StepGroup();
			ToolLine a = new ToolLine("第一步", "read_file");
			ToolLine b = new ToolLine("第二步", "install_mod");
			group.AddLine(a);
			group.AddLine(b);
			Assert.Equal(2, group.Count);
			group.AddLine(a);
			Assert.Equal(2, group.Count);
			// 从别的容器挂过来:原容器里要消失(Windows:panel.Children.Remove)
			StackPanel other = new StackPanel();
			ToolLine c = new ToolLine("第三步", "search_mods");
			other.Children.Add(c);
			group.AddLine(c);
			Assert.Empty(other.Children);
			Assert.Equal(3, group.Count);
		});
	}

	/// <summary>完成时:步数 ≤ 3 ⇒ 保持展开;步数 &gt; 3 ⇒ 自动收起(Windows <c>Complete</c>)。</summary>
	[Theory]
	[InlineData(3, true)]
	[InlineData(4, false)]
	public void Complete_auto_collapses_only_above_three_steps(int lines, bool stillOpen)
	{
		AvaloniaSession.Run(() =>
		{
			StepGroup group = new StepGroup();
			for (int i = 0; i < lines; i++)
			{
				group.AddLine(new ToolLine("第 " + i + " 步", "read_file"));
			}
			group.Complete();
			Assert.Equal(stillOpen, group.IsOpen);
		});
	}

	/// <summary>**用户手动切过 ⇒ 完成时不自动收起**(这条只有测试缝能钉住)。</summary>
	[Fact]
	public void Complete_keeps_the_users_choice_open()
	{
		AvaloniaSession.Run(() =>
		{
			StepGroup group = new StepGroup();
			for (int i = 0; i < 5; i++)
			{
				group.AddLine(new ToolLine("第 " + i + " 步", "read_file"));
			}
			group.ToggleForTest();
			Assert.False(group.IsOpen);
			group.ToggleForTest();
			Assert.True(group.IsOpen);
			group.Complete();
			Assert.True(group.IsOpen);
		});
	}

	/// <summary>失败步数进计数文案(有失败且已结束 ⇒ "· N 步失败";标题仍是 <c>StepSummary</c> 那套)。</summary>
	[Fact]
	public void Failed_steps_are_reported_in_the_counter()
	{
		AvaloniaSession.Run(() =>
		{
			StepGroup group = new StepGroup();
			ToolLine f = new ToolLine("写配置", "write_mod_config");
			group.AddLine(f);
			f.SetState(ToolState.Failed);
			group.Complete();
			Assert.Equal(1, group.Count);
			Assert.Contains("步失败", TextOf(group));
		});
	}

	/// <summary>子代理容器:返回的容器挂在组里(内容加进去就能被组看见)。</summary>
	[Fact]
	public void Subagent_container_lives_inside_the_group()
	{
		AvaloniaSession.Run(() =>
		{
			StepGroup group = new StepGroup();
			SPanel host = group.AddSubagent("子代理:扫模组");
			Assert.NotNull(host);
			host.Children.Add(new ToolLine("子任务一", "scan_mod_conflicts"));
			// 子代理里的行**不算**本组步数(Windows:Count 只数 _body 的直接 ToolLine 孩子)
			Assert.Equal(0, group.Count);
		});
	}

	/// <summary>把整组文本拼起来(断言计数文案用,不依赖控件树细节)。</summary>
	private static string TextOf(Control root)
	{
		System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
		foreach (TextBlock tb in root.GetLogicalDescendants().OfType<TextBlock>())
		{
			if (!string.IsNullOrEmpty(tb.Text))
			{
				parts.Add(tb.Text);
			}
		}
		return string.Join(" ", parts);
	}
}
