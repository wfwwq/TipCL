using System;
using System.IO;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// R35 win-dl:下载页编辑态(用户截图 2026-11-21 14:47)——
/// 1) 编辑条下面第一个块的块名标签 / 上边框 / 右上按钮被切掉;2) 选中框只剩左上、右上两小截;3) 编辑态看不到「筛选」块。
/// 界面是 WPF,net8 测试宿主实例化不了控件:几何规则直接断言,接线按源码文本断言(同 R35WinFix2Tests)。
/// 数值对照 layout3.html:.ebar 46 + margin-bottom 12;.cv padding-top 18、左右 10;.lab / .acts top:-14;outline-offset 4。
/// </summary>
public class R35WinDlTests
{
	private static string Root()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return dir!.FullName;
	}

	private static string Src(string rel)
	{
		return File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static readonly double[] DownloadEditMargin = new double[4] { 22.0, LayoutV3Compat.EditCanvasTop, 22.0, 16.0 };

	[Fact]
	public void EditModeFirstBlockTopIsNotUnderTheEditBar()
	{
		// 「下载」:宿主边距 0 → 编辑条上 14(LayoutEditor 构造里 _list.Margin.Top < 8 的分支),内容区用页面给的编辑态边距
		double barTop = 14.0;
		double[] pad = LayoutV3Compat.EditContentPad(0.0, 0.0, 0.0, 0.0, DownloadEditMargin);
		double barBottom = barTop + LayoutV3Compat.EditBarHeight;
		double firstBlockTop = LayoutV3Compat.EditFirstBlockTop(barTop, pad[1]);
		// 第一个块的上沿在编辑条下沿 + 间距之下,而且浮动块名 / 右上按钮(伸出 14)也整块在编辑条下面、在滚动区里
		Assert.True(firstBlockTop >= barBottom + LayoutV3Compat.EditBarGap, "第一个块的上沿压在编辑条下面");
		Assert.True(pad[1] >= LayoutV3Compat.EditLabelRise + 4.0, "内容区上内边距不够块名标签伸出的高度,标签会被滚动区切掉");
		Assert.True(firstBlockTop - LayoutV3Compat.EditLabelRise >= barBottom + LayoutV3Compat.EditBarGap);
		// 效果图:块上沿 = .bodyA 上 14 + .ebar 46 + 12 + .cv 18 = 90
		Assert.Equal(90.0, firstBlockTop);
		// 左右与编辑条对齐(22),虚线 / 选中框(块外 4 + 线宽 2)四边都在滚动区里
		Assert.Equal(LayoutV3Compat.EditBarSideMargin(0.0), pad[0]);
		Assert.Equal(LayoutV3Compat.EditBarSideMargin(0.0), pad[2]);
		Assert.True(pad[3] >= LayoutV3Compat.EditOutlineReach);
	}

	[Theory]
	[InlineData(0.0, 0.0, 0.0, 0.0)]
	[InlineData(18.0, 14.0, 18.0, 16.0)]
	[InlineData(56.0, 2.0, 56.0, 0.0)]
	[InlineData(double.NaN, -5.0, 3.0, double.PositiveInfinity)]
	public void EditPadAlwaysLeavesRoomForLabelAndOutline(double l, double t, double r, double b)
	{
		double[] pad = LayoutV3Compat.EditContentPad(l, t, r, b);
		Assert.True(pad[1] >= LayoutV3Compat.EditLabelRise + 4.0);
		Assert.True(pad[0] >= LayoutV3Compat.EditOutlineReach);
		Assert.True(pad[2] >= LayoutV3Compat.EditOutlineReach);
		Assert.True(pad[3] >= LayoutV3Compat.EditOutlineReach);
		// 常态边距更大时原样保留(不把别的页挤窄)
		if (l >= 10.0)
		{
			Assert.Equal(l, pad[0]);
		}
		// 普通页(编辑条上 10):第一个块同样在编辑条下面
		Assert.True(LayoutV3Compat.EditFirstBlockTop(10.0, pad[1]) - LayoutV3Compat.EditLabelRise >= 10.0 + LayoutV3Compat.EditBarHeight + LayoutV3Compat.EditBarGap);
	}

	[Fact]
	public void EditGeometryMatchesLayout3AndTheCardChrome()
	{
		Assert.Equal(46.0, LayoutV3Compat.EditBarHeight);
		Assert.Equal(12.0, LayoutV3Compat.EditBarGap);
		Assert.Equal(14.0, LayoutV3Compat.EditLabelRise);
		Assert.Equal(18.0, LayoutV3Compat.EditCanvasTop);
		string v3 = Src("src/TipCL/Pages/Layout/LayoutEditorV3.cs");
		// 编辑条:MinHeight 34 + 上下 5 + 描边 1 = 46
		Assert.Contains("bar.MinHeight = 34.0;", v3);
		Assert.Contains("_barHost.Padding = new Thickness(14.0, 5.0, 8.0, 5.0);", v3);
		// 块名 / 右上按钮伸出 14、虚线框外让 4 —— 与上面的常量同口径
		Assert.Contains("_v3Label.Margin = new Thickness(8.0, -14.0, 0.0, 0.0);", v3);
		Assert.Contains("_v3Acts.Margin = new Thickness(0.0, -14.0, 8.0, 0.0);", v3);
		Assert.Contains("_v3Outline.Margin = new Thickness(-4.0);", v3);
	}

	[Fact]
	public void EditorUsesEditPadForBothListAndGridHosts()
	{
		string ed = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		Assert.Contains("_list.Margin = ContentPad(plan);", ed);
		Assert.Contains("_box.Margin = ContentPad(_state.Plan());", ed);
		Assert.DoesNotContain("_list.Margin = (_marginOverride ?? PadOf(plan));", ed);
		Assert.DoesNotContain("_box.Margin = (_marginOverride ?? PadOf(_state.Plan()));", ed);
		Assert.Contains("LayoutV3Compat.EditContentPad(pad.Left, pad.Top, pad.Right, pad.Bottom, EditMargin)", ed);
		// 编辑条下面一律空 12
		Assert.Contains("LayoutV3Compat.EditBarSideMargin(_list.Margin.Right), LayoutV3Compat.EditBarGap);", ed);
	}

	[Fact]
	public void DownloadEditKeepsFiltersBlockVisibleAndAlignsBlocksWithTheBar()
	{
		string dl = Src("src/TipCL/Pages/DownloadPage.cs");
		// 两块都在区块宿主里;编辑期间只收起宿主外的分类标签行,宿主本身不收
		Assert.Contains("new BlockSpec { Id = \"filters\", Title = I18n.L(\"筛选\"), Content = bar }", dl);
		Assert.Contains("new BlockSpec { Id = \"list\", Title = I18n.L(\"版本列表\"), Content = card }", dl);
		Assert.DoesNotContain("_vanillaHost.Visibility", dl);
		Assert.DoesNotContain("bar.Visibility", dl);
		Assert.Contains("top.Add(segRail, 0);", dl);
		// 编辑态边距交给编辑器,块自己的 22px 左右边距收掉、完成后恢复
		Assert.Contains("_vanillaHost.Editor.EditMargin = new double[4] { 22.0, TipCL.Pages.Layout.LayoutV3Compat.EditCanvasTop, 22.0, 16.0 };", dl);
		Assert.Contains("bar.Margin = (editing ? new Thickness(0.0) : barMargin);", dl);
		Assert.Contains("card.Margin = (editing ? new Thickness(0.0) : cardMargin);", dl);
	}

	[Fact]
	public void EditPreviewHeightIsCappedSoLaterBlocksStayReachable()
	{
		// 几百行的版本列表在编辑态封顶,排在它后面的「筛选」块就在一屏之内(1000×600 窗口内容区约 600 - 标题栏 48)
		Assert.InRange(LayoutV3Compat.EditPreviewMaxHeight, 160.0, 400.0);
		double firstTop = LayoutV3Compat.EditFirstBlockTop(14.0, LayoutV3Compat.EditCanvasTop);
		Assert.True(firstTop + LayoutV3Compat.EditPreviewMaxHeight + 10.0 + 40.0 <= 552.0);
		string ed = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		Assert.Contains("if (_edit && !_fill)", ed);
		Assert.Contains("max = Math.Min(max, LayoutV3Compat.EditPreviewMaxHeight);", ed);
		int setEdit = ed.IndexOf("public void SetEdit(bool edit)", StringComparison.Ordinal);
		Assert.True(setEdit > 0);
		Assert.True(ed.IndexOf("ApplyBodyHeight();", setEdit, StringComparison.Ordinal) > setEdit);
	}
}
