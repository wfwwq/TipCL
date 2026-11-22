using System.Linq;
using TipCL.Core.Launcher;
using Xunit;

public class LayoutDefaultTests
{
	[Fact]
	public void BackendDefaultIsTheCompactThreeCardHome()
	{
		// GOAL WP4.2：横幅移出画布（固定启动头），默认 3 卡：配置 / 快捷入口 / 新闻
		var types = UiLayout.DefaultDoc().items.Select(i => i.type).ToArray();
		Assert.Equal(new[] { "config", "quick", "news" }, types);
	}

	// 防止前后端默认布局再次分叉（否则「恢复默认布局」会丢卡片或错位）
	[Fact]
	public void UiAndBackendDefaultsMatch()
	{
		var ui = TipCL.Pages.DashLayout.Default().Items.Select(c => (c.Type, c.Id, c.X, c.Y, c.W, c.H)).ToArray();
		var be = UiLayout.DefaultDoc().items.Select(i => (i.type, i.id, i.x, i.y, i.w, i.h)).ToArray();
		Assert.Equal(be, ui);
	}

	// 默认卡片互不重叠、不出画布
	[Fact]
	public void DefaultCardsDoNotOverlap()
	{
		var it = UiLayout.DefaultDoc().items;
		foreach (var a in it)
		{
			Assert.True(a.x >= 0 && a.y >= 0 && a.x + a.w <= 1.0001 && a.y + a.h <= 1.0001, a.type);
			foreach (var b in it.Where(b => b != a))
				Assert.False(a.x < b.x + b.w - 1e-6 && b.x < a.x + a.w - 1e-6 && a.y < b.y + b.h - 1e-6 && b.y < a.y + a.h - 1e-6, a.type + "/" + b.type);
		}
	}
}

public class VersionCompareTests
{
	[Fact]
	public void TwoDigitPatchIsNewer()
	{
		Assert.True(Updater.Newer("1.0.10", "1.0.9"));
		Assert.False(Updater.Newer("1.0.9", "1.0.10"));
	}

	[Fact]
	public void UpdateUrlAndClientIdAreBuiltIn()
	{
		Assert.Equal(Config.DEFAULT_UPDATE_URL, Config.UpdateUrl());
		Assert.Equal(Config.DEFAULT_MS_CLIENT_ID, Config.MsClientId());
	}
}

// R34 §5.5:区块改用用户看得懂的名字 —— 只改 Title,不改 id(id 进布局文档,改了就丢用户布局)。
// 页面类是 WPF 控件,这里按源码断言宿主里那一行 BlockSpec。
public class PageBlockTitleTests
{
	private static string Src(string rel)
	{
		System.IO.DirectoryInfo? dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
		while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, rel));
	}

	[Theory]
	[InlineData("MultiplayerPage.cs", "state", "联机状态", "状态卡")]
	[InlineData("MultiplayerPage.cs", "room", "房间", "房间卡")]
	[InlineData("MultiplayerPage.cs", "actions", "快捷操作", "操作区")]
	[InlineData("MultiplayerPage.cs", "players", "玩家", "玩家列表")]
	[InlineData("PlaytimePage.cs", "total", "累计时长", "累计卡")]
	[InlineData("PlaytimePage.cs", "range", "时间范围", "范围卡")]
	[InlineData("PlaytimePage.cs", "list", "明细", "明细列表")]
	[InlineData("JavaPage.cs", "list", "已检测的 Java", "已检测列表")]
	[InlineData("ServersPage.cs", "instance", "实例", "实例行")]
	[InlineData("AccountPage.cs", "list", "账号列表", "账号卡")]
	public void BlockTitleIsRenamedButIdIsKept(string file, string id, string title, string oldTitle)
	{
		string src = Src("src/TipCL/Pages/" + file);
		// 同一个 id 还在,标题换成了新名字
		Assert.Contains("new BlockSpec { Id = \"" + id + "\", Title = I18n.L(\"" + title + "\")", src);
		// 旧名字不再出现在任何界面文案里
		Assert.DoesNotContain("I18n.L(\"" + oldTitle + "\")", src);
	}
}
