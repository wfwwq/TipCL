using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 测试隔离的回归守卫(R1):测试自己的数据根必须落在隔离目录里,绝不能是真实用户目录
/// %LOCALAPPDATA%\TipCL —— 那是桌面版 TipCL 真在用的数据目录。背景见 TestIsolation.cs。
///
/// 这几条用例刻意在构造 TestHome **之前**先读一次 Utils.ROOT:那不是多余的动作,而是
/// 复现真实序列 —— Utils.ROOT 是惰性缓存的静态量,进程里任何一处(别的测试类、产品代码的
/// 静态初始化)先碰到它,根就钉死在那一刻的取值上,之后 TestHome 只设环境变量再也改不动。
/// </summary>
public class TestIsolationTests
{
	private static string Norm(string path)
	{
		return Path.GetFullPath(path).TrimEnd('\\', '/');
	}

	/// <summary>真实用户数据目录(桌面版 TipCL 真在用的那个)。</summary>
	private static string RealUserRoot()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TipCL");
	}

	/// <summary>path 等于 parent,或落在 parent 之下。</summary>
	private static bool IsAtOrUnder(string path, string parent)
	{
		string p = Norm(path);
		string q = Norm(parent);
		return p.Equals(q, StringComparison.OrdinalIgnoreCase) ||
			p.StartsWith(q + "\\", StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ProcessRootIsNotTheRealUserDataDir()
	{
		// 这一条不建 TestHome:它验的是「进程级兜底」—— 就算某条用例完全没走 TestHome,
		// 根也必须落在隔离目录(修复前是 %LOCALAPPDATA%\TipCL)。
		string cached = Utils.ROOT;
		string resolved = Utils.ResolveRoot();

		Assert.False(IsAtOrUnder(cached, RealUserRoot()),
			"Utils.ROOT 指向真实用户目录,测试会读写用户数据:" + cached);
		Assert.False(IsAtOrUnder(resolved, RealUserRoot()),
			"Utils.ResolveRoot() 指向真实用户目录,测试会读写用户数据:" + resolved);
	}

	[Fact]
	public void RootFollowsTestHomeEvenIfRootWasReadEarlier()
	{
		// 先读一次:这一下就把惰性缓存的静态根钉在当前取值上(修复前 = 真实用户目录)。
		string pinned = Utils.ROOT;

		using TestHome home = new TestHome();

		Assert.True(IsAtOrUnder(Utils.ROOT, home.Dir),
			"TestHome 之后 Utils.ROOT 没跟着走:先读到的是 " + pinned + ",现在是 " + Utils.ROOT + ",期望落在 " + home.Dir);
	}

	[Fact]
	public void ResolveRootAndCachedRootAgreeInsideTestHome()
	{
		using TestHome home = new TestHome();

		// 产品代码里两条取根的路都在用:Utils.ROOT(缓存)与 Utils.ResolveRoot()(每次读
		// TIPCL_HOME,例如 PhoneLink.StatePath)。两条路不一致时,同一次同步的「本地世界路径」
		// 与「基准文件路径」会分家,表现为莫名其妙的 Conflict。
		Assert.Equal(Norm(home.Dir), Norm(Utils.ROOT));
		Assert.Equal(Norm(home.Dir), Norm(Utils.ResolveRoot()));
	}

	[Fact]
	public void JsonWritesLandInIsolatedRootNotInRealUserDir()
	{
		using TestHome home = new TestHome();

		// 先确认根在隔离目录里**再动手写**:这条用例红了必须在写之前就红,
		// 绝不允许测试自己往真实用户目录里写任何文件。
		Assert.True(IsAtOrUnder(Utils.ROOT, home.Dir), "数据根不在隔离目录里,拒绝写盘:" + Utils.ROOT);

		// 隔离根里放一个有哨兵的 config.json:写盘如果还往别处跑,哨兵就不会被动到。
		string sentinel = Path.Combine(home.Dir, "config.json");
		File.WriteAllText(sentinel, "{\"wp2-sentinel\": true}");

		Utils.WriteJson(Path.Combine(Utils.ROOT, "wp2-probe.json"), new Dictionary<string, object>
		{
			{ "sentinel", "wp2" },
		});

		Assert.True(File.Exists(Path.Combine(home.Dir, "wp2-probe.json")), "写盘没落到隔离根:" + home.Dir);
		Assert.Equal("{\"wp2-sentinel\": true}", File.ReadAllText(sentinel));

		// 真实用户目录里不允许出现探针文件(只读检查,不写)。
		Assert.False(File.Exists(Path.Combine(RealUserRoot(), "wp2-probe.json")),
			"探针文件出现在真实用户目录里:" + RealUserRoot());
	}
}
