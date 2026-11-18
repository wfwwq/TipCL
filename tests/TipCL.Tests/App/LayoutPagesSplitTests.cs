// 布局修复(Linux 第三批·任务 A):AI 页 / 版本安装页 / 版本管理页 / 设置 → 布局 拆成可拖动的区块。
// 钉住的事实:
//   1 每页的区块宿主、pageId、区块 id 与出厂布局(没有存档时与拆块前同一套几何);
//   2 AI 对话区「吃满视口」:有界高度、能自己滚(吸底靠它),grid 布局下同样有界;
//   3 版本安装页的安装栏在宿主之外(固定在底部,不进可拖区域);
//   4 版本管理页嵌入实例详情时用另一个 pageId;
//   5 新 pageId 都登记进了设置页「页面区块」入口表;
//   6 纯函数 LayoutGrid.FillHeight 与 FillPanel 的有界填充。
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Pages.Ai;
using TipCL.App.Pages.Install;
using TipCL.App.Pages.Layout;
using TipCL.App.Pages.Version;
using TipCL.App.Shell;
using TipCL.App.Pages.Settings;
using TipCL.Core.Launcher.Layout;
using Xunit;

namespace TipCL.Tests.App;

public class LayoutFillPureTests
{
	[Fact]
	public void FillHeightTakesWhatTheOtherRowsLeave()
	{
		// 三行:标题 60 / 填充块(这一行不计它时只有兜底 40)/ 输入 90,间距 10,视口 600
		double[] rows = { 60.0, 40.0, 90.0 };
		double h = LayoutGrid.FillHeight(rows, 1, 1, 10.0, 600.0, 120.0);
		Assert.Equal(600.0 - 60.0 - 90.0 - 20.0, h, 6);
	}

	[Fact]
	public void FillHeightHonoursMinimumAndIgnoresInfiniteViewport()
	{
		double[] rows = { 300.0, 40.0, 300.0 };
		Assert.Equal(120.0, LayoutGrid.FillHeight(rows, 1, 1, 10.0, 500.0, 120.0), 6);
		Assert.Equal(0.0, LayoutGrid.FillHeight(rows, 1, 1, 10.0, double.PositiveInfinity, 120.0), 6);
		// 跨两行的填充块:两行都不算"其它行"
		Assert.Equal(600.0 - 300.0 - 20.0, LayoutGrid.FillHeight(rows, 1, 2, 10.0, 600.0, 120.0), 6);
		// 填充块在行表之外(新行):间距按 4 行算
		Assert.Equal(120.0, LayoutGrid.FillHeight(rows, 3, 1, 10.0, 600.0, 120.0), 6);
		Assert.Equal(900.0 - 640.0 - 30.0, LayoutGrid.FillHeight(rows, 3, 1, 10.0, 900.0, 120.0), 6);
	}

	[Fact]
	public void FillPanelBoundFillBehavesLikeAutoStarAuto() => TestApp.Run(() =>
	{
		Border top = new Border { Height = 50.0 };
		ScrollViewer middle = new ScrollViewer { Content = new Border { Height = 5000.0 } };
		Border bottom = new Border { Height = 70.0 };
		FillPanel panel = new FillPanel { Spacing = 0.0, FillChild = middle, BoundFill = true };
		panel.Children.Add(top);
		panel.Children.Add(middle);
		panel.Children.Add(bottom);
		Window window = new Window { Width = 400.0, Height = 500.0, Content = panel };
		window.Show();
		window.UpdateLayout();
		try
		{
			Assert.Equal(380.0, middle.Bounds.Height, 3);
			Assert.Equal(50.0, middle.Bounds.Y, 3);
			Assert.Equal(430.0, bottom.Bounds.Y, 3);
			// 有界 ⇒ 它自己能滚(这正是 AI 对话区吸底的前提)
			Assert.True(middle.ScrollBarMaximum.Y > 4000.0, "填充块必须拿到有限高度并且能自己滚");
		}
		finally
		{
			window.Close();
		}
	});
}

[Collection("TipCL 真机交互回归")]
public class LayoutPagesSplitTests
{
	private static void Pump(Window win)
	{
		for (int i = 0; i < 4; i++)
		{
			AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
			Dispatcher.UIThread.RunJobs();
			using Avalonia.Media.Imaging.Bitmap? frame = win.CaptureRenderedFrame();
		}
	}

	private static Window Show(Control page)
	{
		Window win = new Window { Width = 900.0, Height = 600.0, Content = page };
		win.Show();
		Pump(win);
		return win;
	}

	private static BlockHost Host(Visual root, string pageId)
	{
		return root.GetVisualDescendants().OfType<BlockHost>().FirstOrDefault(h => h.PageId == pageId)
			?? throw new InvalidOperationException("找不到区块宿主 " + pageId);
	}

	private static string[] BlockIds(BlockHost host)
	{
		return host.Editor.State.Order.ToArray();
	}

	private static Rect BoundsIn(Visual v, Visual root)
	{
		Point p = v.TranslatePoint(new Point(0, 0), root) ?? throw new InvalidOperationException("不在树里");
		return new Rect(p, v.Bounds.Size);
	}

	[Fact]
	public void AiPageIsBlockHostWithBoundedSelfScrollingChat()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("aisplit");
		TestApp.Run(() =>
		{
			AiPage page = new AiPage();
			Window win = Show(page);
			try
			{
				BlockHost host = Host(page, AiPage.LayoutPageId);
				Assert.Equal(new[] { "header", "chat", "input" }, BlockIds(host));
				Assert.Equal(LayoutSchema.ModeStack, host.Editor.State.Page.Mode);

				ScrollViewer chat = page.GetVisualDescendants().OfType<ScrollViewer>().First(LayoutFill.GetFillsViewport);
				LayoutCard header = page.GetVisualDescendants().OfType<LayoutCard>().First(c => c.Spec.Id == "header");
				LayoutCard input = page.GetVisualDescendants().OfType<LayoutCard>().First(c => c.Spec.Id == "input");
				Rect h = BoundsIn(header, win);
				Rect c = BoundsIn(chat, win);
				Rect i = BoundsIn(input, win);
				// 与拆块前的 Grid(Auto,*,Auto) + 边距 22,18,22,16 同一套几何
				Assert.Equal(18.0, h.Y, 1);
				Assert.Equal(22.0, h.X, 1);
				Assert.Equal(h.Bottom, c.Y, 1);
				Assert.Equal(c.Bottom, i.Y, 1);
				Assert.Equal(600.0 - 16.0, i.Bottom, 1);

				// 对话区自己能滚:塞进超高内容后它的可滚范围 > 0(吸底 ScrollToEnd 改的就是它的 Offset)
				((StackPanel)chat.Content!).Children.Add(new Border { Height = 3000.0 });
				Pump(win);
				Assert.True(chat.ScrollBarMaximum.Y > 2000.0, "对话区必须有界并能自己滚动");
				chat.Offset = new Vector(0, double.MaxValue);
				Pump(win);
				Assert.Equal(chat.ScrollBarMaximum.Y, chat.Offset.Y, 1);
			}
			finally
			{
				win.Close();
			}
		});
	}

	[Fact]
	public void AiChatStaysBoundedAfterUserSavedAGridLayout()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("aigrid");
		// 用户拖过之后存成 12 列网格(stack 进编辑态再保存就是这个形状)
		PageLayoutV2 grid = AiPage.DefaultLayout();
		Assert.True(LayoutEngine.StackToGrid(grid, AiPage.LayoutPageId));
		LayoutV2 doc = LayoutStoreV2.Load();
		LayoutEngine.ApplyPage(doc, AiPage.LayoutPageId, grid);
		LayoutStoreV2.Save(doc);
		LayoutStoreV2.ResetCache();
		TestApp.Run(() =>
		{
			AiPage page = new AiPage();
			Window win = Show(page);
			try
			{
				BlockHost host = Host(page, AiPage.LayoutPageId);
				Assert.Equal(LayoutSchema.ModeGrid, host.Editor.State.Page.Mode);
				ScrollViewer chat = page.GetVisualDescendants().OfType<ScrollViewer>().First(LayoutFill.GetFillsViewport);
				((StackPanel)chat.Content!).Children.Add(new Border { Height = 3000.0 });
				Pump(win);
				Assert.True(chat.Bounds.Height < 600.0, "grid 布局下对话区也必须有界,实际高 = " + chat.Bounds.Height);
				Assert.True(chat.ScrollBarMaximum.Y > 2000.0, "grid 布局下对话区必须仍能自己滚动");
			}
			finally
			{
				win.Close();
			}
		});
	}

	[Fact]
	public void InstallPageKeepsInstallBarOutsideTheDraggableArea()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("install");
		TestApp.Run(() =>
		{
			InstallPage page = new InstallPage("1.20.1", "正式版", () => { });
			Window win = Show(page);
			try
			{
				BlockHost host = Host(page, InstallPage.LayoutPageId);
				Assert.Equal(new[] { "header", "name", "loaders", "options" }, BlockIds(host));
				// 「开始安装」按钮不在宿主之内(底部固定栏)
				Button go = page.GetVisualDescendants().OfType<Button>()
					.First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "开始安装"));
				Assert.DoesNotContain(go, host.GetVisualDescendants());
				Assert.True(BoundsIn(go, win).Bottom <= 600.0 && BoundsIn(go, win).Bottom > 520.0, "安装栏应贴在页面底部");
				// 标题行仍在左上 22,14
				LayoutCard header = page.GetVisualDescendants().OfType<LayoutCard>().First(c => c.Spec.Id == "header");
				Assert.Equal(22.0, BoundsIn(header, win).X, 1);
			}
			finally
			{
				win.Close();
			}
		});
	}

	[Fact]
	public void VersionManageUsesSeparatePageIdWhenEmbedded()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("vm");
		TestApp.Run(() =>
		{
			VersionManagePage standalone = new VersionManagePage();
			VersionManagePage embedded = new VersionManagePage(embedded: true);
			Assert.Equal(VersionManagePage.LayoutPageId, standalone.LayoutId);
			Assert.Equal(VersionManagePage.EmbeddedLayoutPageId, embedded.LayoutId);
			Assert.NotEqual(standalone.LayoutId, embedded.LayoutId);
			Window win = Show(standalone);
			try
			{
				BlockHost host = Host(standalone, "version-manage");
				Assert.Equal(new[] { "header", "versions" }, BlockIds(host));
				Assert.Equal(12.0, host.Editor.State.Page.Gap, 3);
			}
			finally
			{
				win.Close();
			}
		});
	}

	[Fact]
	public void SettingsLayoutViewGroupsAreBlocks()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("setlayout");
		TestApp.Run(() =>
		{
			MainWindow win = new MainWindow();
			win.Show();
			Pump(win);
			win.Navigate("settings");
			Pump(win);
			SettingsPage page = Assert.IsType<SettingsPage>(win.PageHostControl!.Content);
			page.OpenLayoutView();
			Pump(win);
			try
			{
				BlockHost host = Host(page, "settings:layout");
				Assert.Equal(new[] { "header", "appearance", "shell", "blocks", "splits", "widgets", "profiles", "json" }, BlockIds(host));
				Assert.True(host.Editor.CanEdit);
			}
			finally
			{
				win.Close();
			}
		});
	}

	[Fact]
	public void NewPageIdsAreRegisteredInTheSettingsEntryList()
	{
		foreach (string id in new[] { "ai", "install", "version-manage", "settings:layout" })
		{
			Assert.NotNull(LayoutBlockEntryCatalog.Find(id));
		}
		Assert.Equal(7, LayoutBlockEntryCatalog.Find("settings:layout")!.SettingsView);
		Assert.Equal("instance", LayoutBlockEntryCatalog.Find("version-manage")!.NavId);
		Assert.NotEqual("", LayoutBlockEntryCatalog.Find("install")!.NeedsContext);
	}
}
