// M3-C1：AI 运行时（Core.Launcher.Ai.Client）最小契约测试。纯逻辑层：端点归一化 /
// 错误分类 / SSE 行解码 / 流式事件装配，不发真实网络请求。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiClientTests
{
	// ————— 端点归一化 —————

	[Theory]
	[InlineData("https://api.example.com", "https://api.example.com/v1")]
	[InlineData("https://api.example.com/", "https://api.example.com/v1")]
	[InlineData("https://api.example.com/v1", "https://api.example.com/v1")]
	[InlineData("", "")]
	public void NormalizeBaseAppendsV1(string input, string expected)
	{
		Assert.Equal(expected, Client.NormalizeBase(input));
	}

	// ————— 接入模式 —————

	[Fact]
	public void ModeCustomWithoutUrlFallsBackToBuiltin()
	{
		Assert.Equal("builtin", Client.Mode(PyDict.Of("ai_mode", "custom", "ai_base_url", "")));
		Assert.Equal("custom", Client.Mode(PyDict.Of("ai_mode", "自定义", "ai_base_url", "http://127.0.0.1:11434")));
		Assert.Equal("builtin", Client.Mode(PyDict.Of()));
	}

	[Fact]
	public void CustomModeWithoutUrlResolvesAsBuiltin()
	{
		// Mode() 已把「custom 但没填地址」并入 builtin（见 Client.Mode 语义）
		var ep = Client.ResolveEndpoint(PyDict.Of("ai_mode", "custom", "ai_base_url", ""));
		Assert.Equal("builtin", Py.Str(ep.Get("mode")));
		Assert.Contains("/chat/completions", Py.Str(ep.Get("url")));
	}

	// ————— 错误分类（AIClientError.Category） —————

	[Theory]
	[InlineData("HTTP 401 令牌无效", 401, "auth")]
	[InlineData("rate limit exceeded", 429, "rate_limited")]
	[InlineData("请求超时", 0, "provider_timeout")]
	[InlineData("connection refused", 0, "provider_network_error")]
	[InlineData("stream stalled", 0, "provider_stream_error")]
	[InlineData("神秘错误", 0, "unknown")]
	public void CategorizeMatchesSemantics(string message, int status, string category)
	{
		Assert.Equal(category, new AIClientError(message, status).Category);
	}

	[Fact]
	public void RetryableFollowsStatusAndCategory()
	{
		Assert.True(new AIClientError("服务器错误", 500).Retryable());
		Assert.False(new AIClientError("HTTP 400 坏请求", 400).Retryable());
		Assert.True(new AIClientError("HTTP 401", 401).Fatal());
		Assert.False(new AIClientError("神秘错误", 400).Fatal());
	}

	[Fact]
	public void IsContextOverflowMatchesChineseAndEnglish()
	{
		Assert.True(Client.IsContextOverflow("This model's maximum context length is 8192 tokens"));
		Assert.True(Client.IsContextOverflow("输入过长，请精简后再试"));
		Assert.False(Client.IsContextOverflow("普通错误"));
	}

	// ————— SSE 行解码 —————

	[Fact]
	public void DecodeSseLineKeepsAscii()
	{
		Assert.Equal("data: hello", Client.DecodeSseLine("data: hello"));
		Assert.Equal("", Client.DecodeSseLine((byte[])null!));
	}

	// ————— 流式事件装配（AssembleStream） —————

	private static IEnumerable<byte[]> Lines(params string[] lines)
	{
		return lines.Select((string l) => System.Text.Encoding.UTF8.GetBytes(l));
	}

	[Fact]
	public void AssembleStreamEmitsDeltasThenDone()
	{
		var events = Client.AssembleStream(Lines(
			"data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}",
			"data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}",
			"data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}",
			"data: [DONE]"
		)).ToList();

		var deltas = events.Where((PyDict e) => Py.S(e.Get("type")) == "delta").Select((PyDict e) => Py.S(e.Get("text"))).ToList();
		Assert.Equal(new[] { "你", "好" }, deltas);
		var done = events.Last();
		Assert.Equal("done", Py.S(done.Get("type")));
		Assert.Equal("stop", Py.S(done.Get("finish_reason")));
	}

	[Fact]
	public void AssembleStreamWrapsReasoningInThinkTags()
	{
		var events = Client.AssembleStream(Lines(
			"data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"想\"}}]}",
			"data: {\"choices\":[{\"delta\":{\"content\":\"答\"}}]}",
			"data: [DONE]"
		)).ToList();

		var text = string.Join("", events.Where((PyDict e) => Py.S(e.Get("type")) == "delta").Select((PyDict e) => Py.S(e.Get("text"))));
		Assert.Equal("<think>想</think>答", text);
	}

	[Fact]
	public void AssembleStreamSurfacesUpstreamError()
	{
		var events = Client.AssembleStream(Lines(
			"data: {\"error\":{\"message\":\"额度不足\"}}"
		)).ToList();

		Assert.Single(events);
		Assert.Equal("error", Py.S(events[0].Get("type")));
		Assert.Equal("额度不足", Py.S(events[0].Get("message")));
	}

	[Fact]
	public void AssembleStreamAccumulatesToolCalls()
	{
		var events = Client.AssembleStream(Lines(
			"data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\"}}]}}]}",
			"data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"北京\\\"}\"}}]}}]}",
			"data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}"
		)).ToList();

		var last = events.Last();
		Assert.Equal("tool_calls", Py.S(last.Get("type")));
		var calls = last.Get("tool_calls") as List<object>;
		Assert.NotNull(calls);
		var call = (PyDict)calls![0];
		var fn = (PyDict)call.Get("function");
		Assert.Equal("get_weather", Py.S(fn.Get("name")));
		Assert.Equal("{\"city\":\"北京\"}", Py.S(fn.Get("arguments")));
	}

	[Fact]
	public void AssembleStreamPassesUsageThrough()
	{
		var events = Client.AssembleStream(Lines(
			"data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}",
			"data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"total_tokens\":42}}",
			"data: [DONE]"
		), expectUsage: true).ToList();

		Assert.Contains(events, (PyDict e) => Py.S(e.Get("type")) == "usage" && Py.Int(((PyDict)e.Get("usage")).Req("total_tokens")) == 42L);
	}

	// ————— 杂项 —————

	[Fact]
	public void MaxOutputTokensClampsAndDefaults()
	{
		Assert.Equal(8192L, Client.MaxOutputTokens(new PyDict()));
		Assert.Equal(512L, Client.MaxOutputTokens(PyDict.Of("ai_max_tokens", 1L)));
		Assert.Equal(4096L, Client.MaxOutputTokens(PyDict.Of("ai_max_tokens", 4096L)));
	}
}
