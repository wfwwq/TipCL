using System;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-D6 的守卫(2026-11-28):确认卡原来每 200ms 轮询一次、没有期限 —— 用户走开后
/// 这一轮永远等下去(_ai_busy 一直为真,发不了新消息)。现在按 ConfirmCardTimeout(默认 120 秒,
/// 与 MCP 外部确认 McpServer.ConfirmTimeout 同一口径)超时,超时按拒绝处理,并发一条
/// ai.status(kind=confirm_timeout)让界面的状态行说明「是超时,不是有人点了拒绝」。
/// 询问卡(ask)不设期限,仍按老口径一直等。
/// </summary>
public class AiConfirmCardTimeoutTests
{
	[Fact]
	public void ConfirmCardTimeoutIs120SecondsLikeMcp()
	{
		Assert.Equal(TimeSpan.FromSeconds(120), BackendApi.ConfirmCardTimeout);
		Assert.Equal(McpServer.ConfirmTimeout, BackendApi.ConfirmCardTimeout);
	}

	[Fact]
	public void ConfirmCardTimesOut()
	{
		using ManualResetEventSlim ev = new ManualResetEventSlim(initialState: false);
		Assert.False(BackendApi.WaitCardAnswered(ev, () => false, TimeSpan.FromMilliseconds(250)));
	}

	[Fact]
	public async Task ConfirmCardAnsweredInTime()
	{
		using ManualResetEventSlim ev = new ManualResetEventSlim(initialState: false);
		Task setter = Task.Run(async delegate
		{
			await Task.Delay(100).ConfigureAwait(false);
			ev.Set();
		});
		Assert.True(await Task.Run(() => BackendApi.WaitCardAnswered(ev, () => false, TimeSpan.FromSeconds(5))));
		await setter;
	}

	[Fact]
	public void ConfirmCardCancelStopsWaiting()
	{
		using ManualResetEventSlim ev = new ManualResetEventSlim(initialState: false);
		Assert.False(BackendApi.WaitCardAnswered(ev, () => true, TimeSpan.FromSeconds(120)));
	}

	[Fact]
	public void ConfirmCardTimeoutUsesClock()
	{
		// 注入时钟:第一次取起点(0 秒),之后一次就跳到 100000 秒,120 秒的口径必然已经超了
		using ManualResetEventSlim ev = new ManualResetEventSlim(initialState: false);
		int calls = 0;
		Assert.False(BackendApi.WaitCardAnswered(ev, () => false, TimeSpan.FromSeconds(120), delegate
		{
			calls++;
			return (calls == 1) ? 0.0 : 100000.0;
		}));
		Assert.True(calls >= 2);
	}

	[Fact]
	public async Task ZeroTimeoutWaitsWithoutDeadline()
	{
		// 询问卡(ask)不设超时,仍按老口径一直等:零超时表示没有期限
		using ManualResetEventSlim ev = new ManualResetEventSlim(initialState: false);
		Task setter = Task.Run(async delegate
		{
			await Task.Delay(500).ConfigureAwait(false);
			ev.Set();
		});
		Assert.True(await Task.Run(() => BackendApi.WaitCardAnswered(ev, () => false, TimeSpan.Zero)));
		await setter;
	}

	[Fact]
	public void TimeoutEventCarriesKindAndExplanation()
	{
		// 「超时 → 拒绝 + 事件」里的事件:页面收到 kind=confirm_timeout 会落到状态行,label 能读懂
		PyDict payload = BackendApi.ConfirmTimeoutStatus(TimeSpan.FromSeconds(120), "chat-1");
		Assert.Equal("confirm_timeout", Py.S(payload.Get("kind")));
		Assert.Equal("chat-1", Py.S(payload.Get("chat_id")));
		string label = Py.S(payload.Get("label"));
		Assert.Contains("120", label);
		Assert.Contains("拒绝", label);
		// 秒数跟着配置走
		Assert.Contains("5", Py.S(BackendApi.ConfirmTimeoutStatus(TimeSpan.FromSeconds(5), "").Get("label")));
	}

	[Fact]
	public void ConfirmPathWiresTimeoutAndExplainsInUi()
	{
		string src = RepoFiles.Src("src/TipCL/Core/BackendApi.cs");
		// 确认卡按 ConfirmCardTimeout 等,超时后发一条 ai.status(页面的状态行会把 label 显示出来)
		Assert.Contains("WaitCard(_ai_confirm_ev, timeout)", src);
		Assert.Contains("ConfirmCardTimeout", src);
		Assert.Contains("ConfirmTimeoutStatus(timeout, runCid)", src);
		// 询问卡仍是无限期等待
		Assert.Contains("WaitCard(_ai_ask_ev)", src);
	}
}