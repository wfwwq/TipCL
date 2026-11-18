using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// 本轮把 AI 页 / 版本安装页 / 版本管理页 / 设置「布局」视图拆成区块(BlockHost)。
/// 页面本身是 WPF(net8 测试宿主实例化不了),所以分两层断言:
///   1) 纯逻辑:「占满剩余高度」的行高分配(LayoutGrid.FillRows)、槽位带上 height_px(LayoutGrid.Pack);
///   2) 接线事实(按源码文本断言,与 StaticInitOrderTests / LayoutNativeBlockTests 同一套做法):
///      各页用的 pageId 与「设置 → 布局 → 页面区块」入口表一致、区块 id 齐全、
///      AI 页对话区/历史是 Fill 块、安装页底栏不进区块、版本管理嵌入态换 pageId。
/// </summary>
public class LayoutPageSplitTests
{
	// ————— 1) 纯逻辑:Fill 行高分配 —————

	private static LayoutCell Cell(string id, int x, int y, int w, int h)
	{
		return new LayoutCell
		{
			Id = id,
			X = x,
			Y = y,
			W = w,
			H = h
		};
	}

	// AI 页默认网格:历史 3 列 × 2 行(Fill)、对话区 9 列(Fill)、输入栏 9 列(普通)
	private static List<LayoutCell> AiCells()
	{
		return new List<LayoutCell>
		{
			Cell("history", 0, 0, 3, 2),
			Cell("chat", 3, 0, 9, 1),
			Cell("input", 3, 1, 9, 1)
		};
	}

	[Fact]
	public void FillRowsGivesExtraOnlyToRowsWithNothingButFillBlocks()
	{
		double[] rows = new double[2] { 160.0, 120.0 };
		HashSet<string> fill = new HashSet<string> { "history", "chat" };
		double[] result = LayoutGrid.FillRows(AiCells(), rows, fill, 10.0, 800.0);
		// 合计 160 + 10 + 120 = 290,余量 510 全给第 0 行(只有 Fill 块);输入栏那一行保持内容高
		Assert.Equal(670.0, result[0], 3);
		Assert.Equal(120.0, result[1], 3);
		Assert.Equal(800.0, result.Sum() + 10.0, 3);
		// 原数组不被就地改
		Assert.Equal(160.0, rows[0]);
	}

	[Fact]
	public void FillRowsFallsBackToAnyRowTouchingAFillBlock()
	{
		// 每一行都混着普通块:退到"含 Fill 块的行",平均分
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("a", 0, 0, 6, 2),
			Cell("b", 6, 0, 6, 1),
			Cell("c", 6, 1, 6, 1)
		};
		double[] result = LayoutGrid.FillRows(cells, new double[2] { 100.0, 100.0 }, new HashSet<string> { "a" }, 0.0, 400.0);
		Assert.Equal(200.0, result[0], 3);
		Assert.Equal(200.0, result[1], 3);
	}

	[Fact]
	public void FillRowsIsIdentityWhenNothingToFillOrAlreadyTallEnough()
	{
		double[] rows = new double[2] { 300.0, 300.0 };
		HashSet<string> fill = new HashSet<string> { "chat" };
		Assert.Equal(rows, LayoutGrid.FillRows(AiCells(), rows, fill, 10.0, 500.0));        // 已经够高
		Assert.Equal(rows, LayoutGrid.FillRows(AiCells(), rows, new HashSet<string>(), 10.0, 2000.0)); // 没有 Fill 块
		Assert.Equal(rows, LayoutGrid.FillRows(AiCells(), rows, fill, 10.0, 0.0));          // 可视高未知
		Assert.Equal(rows, LayoutGrid.FillRows(AiCells(), rows, fill, 10.0, double.PositiveInfinity));
		Assert.Empty(LayoutGrid.FillRows(null, null, fill, 10.0, 900.0));
	}

	[Fact]
	public void PackCarriesHeightPxSoACommitDoesNotEraseIt()
	{
		// 回归:Pack 以前漏带 height_px,任何一次提交(ApplyTo)都会把别的块的固定高度写回 0
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 12,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "a", X = 0.0, Y = 0.0, W = 6.0, H = 1.0, HeightPx = 240.0 },
				new BlockLayout { Id = "b", X = 6.0, Y = 0.0, W = 6.0, H = 1.0 }
			}
		};
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		Assert.Equal(240.0, cells.Single((LayoutCell c) => c.Id == "a").HeightPx);
		LayoutGrid.Apply(page, cells);
		Assert.Equal(240.0, page.Blocks[0].HeightPx);
		Assert.Equal(0.0, page.Blocks[1].HeightPx);
	}

	// ————— 2) 接线事实(源码文本) —————

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

	private static string ConstOf(string source, string name)
	{
		Match m = Regex.Match(source, "const string " + Regex.Escape(name) + " = \"([^\"]+)\"");
		Assert.True(m.Success, "找不到常量 " + name);
		return m.Groups[1].Value;
	}

	private static List<string> BlockIds(string source)
	{
		return Regex.Matches(source, "Id = \"([A-Za-z]+)\"").Cast<Match>().Select((Match m) => m.Groups[1].Value).ToList();
	}

	[Theory]
	[InlineData("src/TipCL/Pages/AiPage.cs", "LayoutPageId", "ai", "history,chat,input")]
	[InlineData("src/TipCL/Pages/InstallPage.cs", "LayoutPageId", "install", "name,loaders,options")]
	[InlineData("src/TipCL/Pages/VersionManagePage.cs", "LayoutPageId", "version-manage", "folders,versions")]
	[InlineData("src/TipCL/Pages/VersionManagePage.cs", "EmbeddedLayoutPageId", "version-manage:embedded", "folders,versions")]
	[InlineData("src/TipCL/Pages/SettingsLayoutPage.cs", "LayoutViewPageId", "settings:layout", "appearance,shell,blocks,splits,widgets,profiles,json")]
	public void SplitPagesUseRegisteredPageIdsAndDeclareTheirBlocks(string file, string constName, string pageId, string idsCsv)
	{
		string src = Src(file);
		Assert.Equal(pageId, ConstOf(src, constName));
		// 入口表登记了这个 pageId
		Assert.NotNull(LayoutBlockEntryCatalog.Find(pageId));
		// 每个区块 id 都在 BlockSpec 里出现
		List<string> ids = BlockIds(src);
		foreach (string id in idsCsv.Split(','))
		{
			Assert.Contains(id, ids);
		}
		// 宿主确实是用这个常量建的
		// (版本管理页按嵌入与否在两个常量间挑一个,传的是局部变量 pageId)
		Assert.Contains("new BlockHost(" + (file.EndsWith("VersionManagePage.cs", StringComparison.Ordinal) ? "pageId" : constName), src);
	}

	[Fact]
	public void AiPageKeepsChatScrollInsideAFillBlockWithNativeDefault()
	{
		string src = Src("src/TipCL/Pages/AiPage.cs");
		// 对话区与历史是 Fill 块(网格里占满剩余高度,不被外层滚动吃掉高度);输入栏跟随内容
		Assert.Matches(new Regex("Id = \"history\",[^}]*Fill = true", RegexOptions.Singleline), src);
		Assert.Matches(new Regex("Id = \"chat\",[^}]*Fill = true", RegexOptions.Singleline), src);
		Assert.DoesNotMatch(new Regex("Id = \"input\",[^}]*Fill = true", RegexOptions.Singleline), src);
		// 默认外观 = 原排版(原生默认排版),消息列表仍在对话区里、吸底仍滚的是它
		Assert.Contains("DefaultAiPage(), native)", src);
		Assert.Contains("chat.Add(_scroll, 2);", src);
		Assert.Contains("_scroll.ScrollToEnd();", src);
	}

	[Fact]
	public void InstallPageFooterStaysOutsideTheBlockHost()
	{
		string src = Src("src/TipCL/Pages/InstallPage.cs");
		Assert.Contains("root.Add(body, 1);", src);
		Assert.Contains("root.Add(footBar, 2);", src);
		// 底栏不在任何 BlockSpec 里
		Assert.DoesNotMatch(new Regex("Content = footBar"), src);
	}

	[Fact]
	public void EmbeddedVersionManageUsesItsOwnPageId()
	{
		string src = Src("src/TipCL/Pages/VersionManagePage.cs");
		Assert.Contains("BuildHost(embedded: true);", src);
		Assert.Contains("string pageId = (embedded ? EmbeddedLayoutPageId : LayoutPageId);", src);
		// 实例详情「设置」分段就是通过 Folder 赋值进入嵌入态的
		Assert.Contains("new VersionManagePage { Folder = _inst }", Src("src/TipCL/Pages/InstanceDetailPage.cs"));
	}
}
