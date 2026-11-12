using System;
using System.IO;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 静态字段按书写顺序初始化，引用排在后面的字段会拿到 null，导致类型初始化失败、启动崩溃。
/// WPF 页面在 Linux 上没法实例化，这里直接检查源码顺序。
/// </summary>
public class StaticInitOrderTests
{
	private static string Src(string rel)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
			dir = dir.Parent;
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir!.FullName, rel));
	}

	[Fact]
	public void SettingsPage_ExtrasDeclaredBeforeUse()
	{
		string s = Src("src/TipCL/Pages/SettingsPage.cs");
		int extras = s.IndexOf("internal static ThemeExtras Extras { get; } =", StringComparison.Ordinal);
		int color = s.IndexOf("private static string _visualColor =", StringComparison.Ordinal);
		int bg = s.IndexOf("private static string _visualBackground =", StringComparison.Ordinal);
		Assert.True(extras >= 0 && color >= 0 && bg >= 0);
		Assert.True(extras < color && extras < bg, "Extras 必须声明在 _visualColor / _visualBackground 之前");
	}
}
