using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

public class UpdaterApplyTests
{
	[Fact]
	public void ScriptWaitsCopiesAndRestarts()
	{
		string s = Updater.BuildApplyScript(@"C:\Users\张三\AppData\Local\TipCL\cache\TipCL-1.0.7.bin", @"D:\游戏\TipCL.exe", 1234, restart: true);
		Assert.Contains("Wait-Process -Id 1234", s);
		Assert.Contains(@"-LiteralPath 'C:\Users\张三\AppData\Local\TipCL\cache\TipCL-1.0.7.bin' -Destination 'D:\游戏\TipCL.exe'", s);
		Assert.Contains(@"Start-Process -FilePath 'D:\游戏\TipCL.exe'", s);
	}

	[Fact]
	public void ScriptEscapesQuotesAndCanSkipRestart()
	{
		string s = Updater.BuildApplyScript(@"C:\a'b\x.bin", @"C:\it's\TipCL.exe", 1, restart: false);
		Assert.Contains(@"'C:\a''b\x.bin'", s);
		Assert.Contains(@"'C:\it''s\TipCL.exe'", s);
		Assert.DoesNotContain("Start-Process", s);
	}
}
