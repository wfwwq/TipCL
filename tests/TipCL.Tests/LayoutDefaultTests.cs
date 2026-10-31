using System.Linq;
using TipCL.Core.Launcher;
using Xunit;

public class LayoutDefaultTests
{
	[Fact]
	public void BackendDefaultIsTheCompactFourCardHome()
	{
		var types = UiLayout.DefaultDoc().items.Select(i => i.type).ToArray();
		Assert.Equal(new[] { "banner", "config", "quick", "news" }, types);
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
