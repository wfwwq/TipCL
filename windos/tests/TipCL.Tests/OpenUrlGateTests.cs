using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 外部链接节流闸门(现场 bug 回归):联机页 <c>unsupported</c> 说明卡上的「了解」按钮会打开
/// github.com/burningtnt/Terracotta,而启动器界面毫无变化 → 用户以为没反应连点 → 同一个页面被反复打开。
/// 这里把"同一 URL 3 秒内只开一次"钉死,防止以后有人把 <see cref="Ui.OpenUrl"/> 的节流删掉。
/// </summary>
public class OpenUrlGateTests
{
	public OpenUrlGateTests()
	{
		OpenUrlGate.Reset();
	}

	[Fact]
	public void SameUrlIsOnlyOpenedOnceInsideTheWindow()
	{
		const string url = "https://github.com/burningtnt/Terracotta";
		Assert.True(OpenUrlGate.TryAcquire(url, 1000));
		Assert.False(OpenUrlGate.TryAcquire(url, 1000));   // 连点第二下
		Assert.False(OpenUrlGate.TryAcquire(url, 2500));   // 1.5s 后还是拦
		Assert.False(OpenUrlGate.TryAcquire(url, 3999));   // 差 1ms 到期
	}

	[Fact]
	public void WindowIsExclusiveAtTheBoundary()
	{
		const string url = "https://github.com/burningtnt/Terracotta";
		Assert.True(OpenUrlGate.TryAcquire(url, 0));
		// 恰好满 3000ms:允许(否则轮询每秒重试一次时会永远被拦)
		Assert.True(OpenUrlGate.TryAcquire(url, OpenUrlGate.DefaultWindowMs));
	}

	[Fact]
	public void DifferentUrlsAreIndependent()
	{
		Assert.True(OpenUrlGate.TryAcquire("https://a.example", 0));
		Assert.True(OpenUrlGate.TryAcquire("https://b.example", 0));
		Assert.False(OpenUrlGate.TryAcquire("https://a.example", 0));
	}

	[Fact]
	public void EmptyOrNullUrlIsNeverOpened()
	{
		Assert.False(OpenUrlGate.TryAcquire(null, 0));
		Assert.False(OpenUrlGate.TryAcquire("", 0));
		Assert.False(OpenUrlGate.TryAcquire("   ", 0));
	}

	[Fact]
	public void UrlIsTrimmedBeforeComparing()
	{
		Assert.True(OpenUrlGate.TryAcquire(" https://a.example ", 0));
		Assert.False(OpenUrlGate.TryAcquire("https://a.example", 10));
	}

	[Fact]
	public void ResetLetsTheSameUrlOpenAgain()
	{
		Assert.True(OpenUrlGate.TryAcquire("https://a.example", 0));
		Assert.False(OpenUrlGate.TryAcquire("https://a.example", 1));
		OpenUrlGate.Reset();
		Assert.True(OpenUrlGate.TryAcquire("https://a.example", 1));
	}

	[Fact]
	public void ZeroWindowMeansAlwaysAllowed()
	{
		Assert.True(OpenUrlGate.TryAcquire("https://a.example", 500, 0));
		Assert.True(OpenUrlGate.TryAcquire("https://a.example", 500, 0));
	}
}