using System.Collections.Generic;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using TipCL.Tests.AiReplay;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-T 回放框架自检:假模型走的是不是真解析路径、请求有没有被完整记下来。
/// 这两条是后面所有场景用例的地基,先钉住。
/// </summary>
public class AiReplayHarnessTests
{
	private static List<object> Messages(string text)
	{
		return new List<object> { PyDict.Of("role", "user", "content", text) };
	}

	/// <summary>界面每轮都会带一份工具表;空表会被 Body() 省掉 tools 字段,假模型就当成摘要请求了。</summary>
	private static List<object> Tools1()
	{
		return new List<object> { Tools.TOOL_SCHEMAS[0] };
	}

	private static string Dump(PyDict ev)
	{
		return PyJson.Dumps(ev, null, ensureAscii: false);
	}

	/// <summary>把 SSE 报文按行切成字节(和 AiResponse.IterLines 切出来的东西同形)。</summary>
	private static List<byte[]> Lines(string sse)
	{
		return (from line in sse.Split('\n')
			where line.Trim().Length > 0
			select Encoding.UTF8.GetBytes(line)).ToList();
	}

	/// <summary>
	/// 假流必须经过现有 AssembleStream 的 SSE 解析路径,不能绕开:
	/// 同一份字节直接喂 AssembleStream 与经 ChatStream(TestTransport)解析出来的事件要逐条相同,
	/// 而且工具参数是跨两个 chunk 拼出来的 —— 只有真解析器会这么干。
	/// </summary>
	[Fact]
	public void FakeStreamIsParsedByExistingAssembleStreamPath()
	{
		using AiReplayHarness h = new AiReplayHarness();
		ModelReply reply = ModelReply.CallsOf(new FakeToolCall("list_instances", "{\"instance\":\"default\"}", "call_a"));
		h.Model.Then(reply);
		List<PyDict> viaClient = Client.ChatStream(h.Settings, Messages("看看有哪些实例"), Tools1()).ToList();
		List<PyDict> manual = Client.AssembleStream(Lines(reply.BuildStreamBody()), expectUsage: true).ToList();
		Assert.Equal(manual.Select(Dump).ToList(), viaClient.Select(Dump).ToList());
		Assert.Equal(new[] { "usage", "tool_calls" }, viaClient.Select((PyDict e) => Py.S(e.Get("type"))).ToArray());
		PyDict call = (PyDict)Py.ListOr(viaClient[1].Get("tool_calls"))[0];
		PyDict fn = (PyDict)((PyDict)call)["function"];
		Assert.Equal("list_instances", Py.S(fn["name"]));
		Assert.Equal("{\"instance\":\"default\"}", Py.S(fn["arguments"]));
	}

	/// <summary>纯文字流的解析结果:delta → usage → done(finish_reason 原样带出)。</summary>
	[Theory]
	[InlineData("stop")]
	[InlineData("length")]
	public void FakeTextStreamYieldsDeltaUsageDone(string finish)
	{
		using AiReplayHarness h = new AiReplayHarness();
		ModelReply reply = ModelReply.Text("你好,", "我是启动器助手。");
		reply.Finish = finish;
		h.Model.Then(reply);
		List<PyDict> events = Client.ChatStream(h.Settings, Messages("在吗"), Tools1()).ToList();
		Assert.Equal(new[] { "delta", "delta", "usage", "done" }, events.Select((PyDict e) => Py.S(e.Get("type"))).ToArray());
		Assert.Equal("你好,", Py.S(events[0].Get("text")));
		Assert.Equal("我是启动器助手。", Py.S(events[1].Get("text")));
		Assert.Equal(finish, Py.S(events[3].Get("finish_reason")));
	}

	/// <summary>每次请求都记下了 messages、tools、model(场景断言全靠这三样)。</summary>
	[Fact]
	public void FakeModelRecordsEveryRequest()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Text("好"));
		List<object> tools = new List<object> { Tools.TOOL_SCHEMAS[0], Tools.TOOL_SCHEMAS[1] };
		Client.ChatStream(h.Settings, Messages("在吗"), tools).ToList();
		Assert.Single(h.Model.Requests);
		ModelRequest req = h.Model.Requests[0];
		Assert.Equal(AiReplayHarness.FakeModelName, req.Model);
		Assert.True(req.Stream);
		Assert.Equal(new[] { Tools.SchemaName(tools[0]), Tools.SchemaName(tools[1]) }, req.ToolNames.ToArray());
		Assert.Equal("在吗", req.LastUserText);
		Assert.Equal("user", req.Roles[req.Roles.Count - 1]);
		Assert.True(Py.Truthy(req.Body.Get("stream_options")));
	}
}
