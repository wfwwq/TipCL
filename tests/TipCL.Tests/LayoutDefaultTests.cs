using System.Linq;
using TipCL.Core.Launcher;
using Xunit;

public class LayoutDefaultTests
{
	[Fact]
	public void BackendDefaultHasAllSixHomeCards()
	{
		var types = UiLayout.DefaultDoc().items.Select(i => i.type).ToArray();
		Assert.Equal(new[] { "banner", "config", "log", "quick", "playtime", "news" }, types);
	}
}
