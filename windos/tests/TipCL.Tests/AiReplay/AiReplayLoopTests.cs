using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Plugins;
using TipCL.Tests.AiReplay;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-T 场景表(Windows 侧)主循环部分:1、2、3、4、5、7、8、9、10、11、13、14、15、19。
/// 每个用例都断言 stop_reason、最终文本、请求序列(含 tools 集合与 model)、终态事件条数、工具调用序列,
/// 外加插件事件序列 turn/start → ... → turn/end。
/// 依赖修复才能绿的场景(6、9 的工具表一致性、12 的兜底工具表、21、22)标 Skip 并注明转绿包号。
/// </summary>
public class AiReplayLoopTests
{
	private static List<object> History(params string[] userAndAssistant)
	{
		List<object> list = new List<object>();
		for (int i = 0; i < userAndAssistant.Length; i++)
		{
			list.Add(PyDict.Of("role", (i % 2 == 0) ? "user" : "assistant", "content", userAndAssistant[i]));
		}
		return list;
	}

	private static List<ToolCall> L(params ToolCall[] calls)
	{
		return new List<ToolCall>(calls);
	}

	private static string ToolResultText(PyDict toolResultEvent)
	{
		PyDict data = toolResultEvent.Get("data") as PyDict ?? new PyDict();
		List<object> content = Py.ListOr(Py.Or(data.Get("content"), new List<object>()));
		return content.Count == 0 ? "" : Py.S(((PyDict)content[0]).Get("text"));
	}

	// ————————————————————— 1 纯文字回复 —————————————————————

	[Fact]
	public void Scenario01a_纯文字回复_一条终态事件()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Text("你好,", "我是启动器助手。"));
		AgentResult r = h.Send("在吗");
		Assert.Equal("你好,我是启动器助手。", r.Text);
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Single(h.Model.Requests);
		Assert.Empty(h.PluginEventsOf("tool/start"));
		h.AssertSingleTurnEnd("stop");
		Assert.Equal(new[] { "turn/start", "user/message", "assistant/message", "turn/end" }, h.PluginTypes().Where(t => t != "assistant/delta").ToArray());
		Assert.Equal("你好,我是启动器助手。", h.Streamed.ToString());
	}

	/// <summary>
	/// 场景 1 的另一半:回复里声称「已经帮你安装好了」却没调工具 → Windows 保留 no_tool_call
	/// (Agent._result 只在 ClaimsAction 为真时保留这个 stop_reason,否则归一到 completed)。
	/// </summary>
	[Fact]
	public void Scenario01b_声称做过却没调工具_no_tool_call()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Text("已经帮你安装好钠了。"));
		AgentResult r = h.Send("装个钠");
		Assert.Equal(StopReason.NO_TOOL_CALL, r.stop_reason);
		Assert.Equal("已经帮你安装好钠了。", r.Text);
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 2 单个只读工具 → 文字 —————————————————————

	[Fact]
	public void Scenario02_只读工具再回到文字_completed()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("list_instances", "{}", "call_1"));
		h.Model.Then(ModelReply.Text("你有一个实例:default。"));
		AgentResult r = h.Send("我有哪些实例?");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal("你有一个实例:default。", r.Text);
		Assert.Equal(2, h.Model.Requests.Count);
		Assert.Contains("list_instances", h.Model.Requests[0].ToolNames);
		// 工具结果回到模型,并带来源前缀
		List<string> results = h.Model.Requests[1].ToolResults;
		Assert.Single(results);
		Assert.StartsWith("[来源: 本地文件/日志/状态读取]", results[0]);
		// 工具调用序列 + 插件事件序列
		Assert.Equal(new[] { "list_instances" }, h.PluginEventsOf("tool/start").Select((PyDict e) => Py.S((e.Get("data") as PyDict ?? new PyDict()).Get("name"))).ToArray());
		Assert.Equal(1, h.PluginEventCount("tool/result"));
		// 一条带工具调用的 assistant/message + 回合收尾补发的一条最终答复
		Assert.Equal(2, h.PluginEventCount("assistant/message"));
		h.AssertSingleTurnEnd("stop");
		Assert.Equal("list_instances", Py.S(h.StoreEventsOf("ToolCompleted")[0].Get("tool")));
		Assert.False(Py.Truthy((h.PluginEventsOf("tool/result")[0].Get("data") as PyDict).Get("isError")));
		Assert.Equal(new[] { "ToolStarted", "ToolCompleted" }, h.StoreEventNames().Where(n => n == "ToolStarted" || n == "ToolCompleted").ToArray());
	}

	// ————————————————————— 3 一轮多个工具,部分并行 —————————————————————

	[Fact]
	public void Scenario03a_分组符合Scheduler()
	{
		Assert.Single(Scheduler.Group(L(new ToolCall("1", "list_instances", new PyDict()), new ToolCall("2", "get_launcher_state", new PyDict()), new ToolCall("3", "get_latest_log", new PyDict()))));
		Assert.Equal(3, Scheduler.Group(L(new ToolCall("1", "list_instances", new PyDict()), new ToolCall("2", "get_launcher_state", new PyDict()), new ToolCall("3", "get_latest_log", new PyDict())))[0].Count);
		// 写工具不能进并行组,前后各切一刀
		List<List<ToolCall>> mixed = Scheduler.Group(L(new ToolCall("1", "list_instances", new PyDict()), new ToolCall("2", "write_mod_config", new PyDict()), new ToolCall("3", "get_latest_log", new PyDict())));
		Assert.Equal(3, mixed.Count);
		Assert.Equal("write_mod_config", mixed[1][0].name);
		// ask_user 独占
		List<List<ToolCall>> asked = Scheduler.Group(L(new ToolCall("1", "ask_user", new PyDict()), new ToolCall("2", "list_instances", new PyDict())));
		Assert.Equal(2, asked.Count);
		// 删除类独占
		Assert.Equal("delete_instance", Scheduler.Group(L(new ToolCall("1", "delete_instance", new PyDict()), new ToolCall("2", "list_instances", new PyDict())))[0][0].name);
		// 并行组按 maxConcurrency 切片
		List<List<ToolCall>> cut = Scheduler.Group(L(new ToolCall("1", "list_instances", new PyDict()), new ToolCall("2", "get_launcher_state", new PyDict()), new ToolCall("3", "get_latest_log", new PyDict())), 2);
		Assert.Equal(new[] { 2, 1 }, cut.Select((List<ToolCall> g) => g.Count).ToArray());
	}

	[Fact]
	public void Scenario03b_一轮多工具_结果顺序正确()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Confirm = delegate(string tool, PyDict args, string label, string reason)
		{
			return true;
		};
		h.Model.Then(ModelReply.CallsOf(new FakeToolCall("list_instances", "{}", "c1"), new FakeToolCall("get_launcher_state", "{}", "c2"), new FakeToolCall("get_latest_log", "{}", "c3"), new FakeToolCall("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-3b.toml\",\"content\":\"[a]\\nb = 1\\n\"}", "c4")));
		h.Model.Then(ModelReply.Text("看完了。"));
		AgentResult r = h.Send("看看启动器状态,顺手把配置改一下");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		// 工具调用序列:三条只读在一个并行组里(组内并发,start 顺序不定,实测两次跑出来的顺序就不同),
		// 写工具单独一组且必然排在这三条之后;组内顺序按集合比较。
		List<string> starts = h.PluginEventsOf("tool/start").Select((PyDict e) => Py.S((e.Get("data") as PyDict ?? new PyDict()).Get("name"))).ToList();
		Assert.Equal(4, starts.Count);
		Assert.Equal("write_mod_config", starts[3]);
		Assert.Equal(new SortedSet<string> { "list_instances", "get_launcher_state", "get_latest_log" }, new SortedSet<string>(starts.Take(3)));
		// 三条只读进同一并行组,写工具单独一组:写工具的 tool/start 必然晚于三条只读的 tool/result
		long lastReadResult = h.PluginEventsOf("tool/result").Where((PyDict e) => Py.S((e.Get("data") as PyDict ?? new PyDict()).Get("name")) != "write_mod_config").Max((PyDict e) => h.EventSeq(e));
		PyDict writeStart = h.PluginEventsOf("tool/start").Single((PyDict e) => Py.S((e.Get("data") as PyDict ?? new PyDict()).Get("name")) == "write_mod_config");
		Assert.True(h.EventSeq(writeStart) > lastReadResult, "写工具的 tool/start 应该排在三条只读工具的 tool/result 之后");
		// 结果顺序 = tool_calls 顺序
		List<string> toolMessageNames = h.Model.Requests[1].Messages.Where((PyDict m) => Py.Eq(m.Get("role"), "tool")).Select((PyDict m) => Py.S(m.Get("name"))).ToList();
		Assert.Equal(new[] { "list_instances", "get_launcher_state", "get_latest_log", "write_mod_config" }, toolMessageNames.ToArray());
		// 写工具真的落在临时目录里(检查点没报警)
		Assert.True(File.Exists(h.InstanceConfigFile("t-win-3b.toml")), "写工具应该落在临时实例的 config 目录里");
		Assert.Empty(h.StatusesOf("checkpoint_warn"));
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 4 写工具,用户拒绝 —————————————————————

	[Fact]
	public void Scenario04_写工具被拒绝_不落盘_轮次继续()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-4.toml\",\"content\":\"x\"}", "c1"));
		h.Model.Then(ModelReply.Text("好,那我不改了。"));
		AgentResult r = h.Send("把配置改成 x");
		Assert.False(File.Exists(h.InstanceConfigFile("t-win-4.toml")), "用户拒绝后不许落盘");
		Assert.Single(h.ConfirmAsked);
		Assert.Equal("write_mod_config", h.ConfirmAsked[0].Tool);
		// 模型收到拒绝结果,轮次继续
		Assert.Contains("用户拒绝了这次操作", h.Model.Requests[1].ToolResults[0]);
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal("好,那我不改了。", r.Text);
		// 拒绝的工具根本没执行
		Assert.Empty(h.StoreEventsOf("ToolStarted"));
		PyDict denied = h.PluginEventsOf("tool/result").Single();
		Assert.True(Py.Truthy((denied.Get("data") as PyDict).Get("isError")));
		Assert.Contains("用户拒绝了这次操作", ToolResultText(denied));
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 5 确认后返回 AiRule —————————————————————

	[Fact]
	public void Scenario05_确认后记住规则_后续同类调用不再询问()
	{
		using AiReplayHarness h = new AiReplayHarness();
		int asked = 0;
		h.Confirm = delegate(string tool, PyDict args, string label, string reason)
		{
			asked++;
			return new AiRule(tool, null, "allow", "global");
		};
		h.Model.Then(ModelReply.CallsOf(new FakeToolCall("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-5a.toml\",\"content\":\"a\"}", "c1")));
		h.Model.Then(ModelReply.Call("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-5b.toml\",\"content\":\"b\"}", "c2"));
		h.Model.Then(ModelReply.Text("两个配置都改好了。"));
		AgentResult r = h.Send("改两个配置");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal("两个配置都改好了。", r.Text);
		Assert.Equal(1, asked);
		Assert.True(File.Exists(h.InstanceConfigFile("t-win-5a.toml")));
		Assert.True(File.Exists(h.InstanceConfigFile("t-win-5b.toml")));
		// 规则落到 ai_permissions.json 的 global.allow 里
		List<object> globalAllow = Py.ListOr(((PyDict)h.PermissionStore()["global"])["allow"]);
		Assert.Contains(globalAllow, delegate(object o)
		{
			return Py.Eq(((PyDict)o)["toolName"], "write_mod_config");
		});

		// 下一轮:规则从盘上重新读(等价于界面重启后),仍然不再询问
		h.Model.Then(ModelReply.Call("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-5c.toml\",\"content\":\"c\"}", "c3"));
		h.Model.Then(ModelReply.Text("又改好一个。"));
		AgentResult r2 = h.Send("再改一个配置", r.turn_messages);
		Assert.Equal(StopReason.COMPLETED, r2.stop_reason);
		Assert.Equal(1, asked);
		Assert.True(File.Exists(h.InstanceConfigFile("t-win-5c.toml")));
	}

	// ————————————————————— 7 流式报上下文溢出 —————————————————————

	[Fact]
	public void Scenario07_上下文溢出_反应式压缩一次后继续()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.StreamError("maximum context length exceeded"));
		h.Model.Then(ModelReply.Text("压缩完,继续。"));
		AgentResult r = h.Send("讲个很长的问题");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal("压缩完,继续。", r.Text);
		Assert.Equal(2, h.Model.Requests.Count);
		Assert.True(h.Model.Requests[0].Stream);
		// 只做一次反应式压缩
		Assert.Single(h.TraceEventsOf("reactive_compact"));
		Assert.Single(h.StoreEventsOf("CompactBoundary"));
		Assert.Equal("reactive", Py.S(h.StoreEventsOf("CompactBoundary")[0].Get("trigger")));
		Assert.Contains(h.StatusesOf("compact"), (PyDict p) => Py.S(p.Get("reason")) == "reactive");
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 8 finish_reason=length —————————————————————

	[Fact]
	public void Scenario08_长度截断_最多续写两次后truncated()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Truncated("第一段。"));
		h.Model.Then(ModelReply.Truncated("第二段。"));
		h.Model.Then(ModelReply.Truncated("第三段。"));
		AgentResult r = h.Send("写个长东西");
		Assert.Equal(StopReason.TRUNCATED, r.stop_reason);
		Assert.StartsWith("第一段。第二段。第三段。", r.Text);
		// 收尾那句「回复被长度限制截断了」走 i18n,不去钉具体措辞(断言结构性字段即可)
		Assert.True(r.Text.Length > "第一段。第二段。第三段。".Length);
		Assert.Equal(3, h.Model.Requests.Count);
		// 第二次、第三次请求里应该能看到「请从断开处继续」
		Assert.Contains("请从断开处继续", h.Model.Requests[1].LastUserText);
		Assert.Contains("请从断开处继续", h.Model.Requests[2].LastUserText);
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 9 流式失败 → 非流式兜底 —————————————————————

	[Fact]
	public void Scenario09a_流式失败后兜底成功_文本不重复拼接()
	{
		using AiReplayHarness h = new AiReplayHarness();
		ModelReply broken = ModelReply.Text("好的,我来");
		broken.BreakMidStream = true;
		h.Model.Then(broken);
		h.Model.Then(ModelReply.NonStream("好的,我来装钠。"));
		AgentResult r = h.Send("帮我装钠");
		Assert.Equal("好的,我来装钠。", r.Text);
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal(2, h.Model.Requests.Count);
		Assert.True(h.Model.Requests[0].Stream);
		Assert.False(h.Model.Requests[1].Stream);
		h.AssertSingleTurnEnd("stop");
	}

	[Fact(Skip = "AI5-T 场景 9(工具表一致性):兜底请求传的是全量表 —— Agent.cs 的 ChatOnce 用的是 Tools.TOOL_SCHEMAS;由 D8 修好(改成复用本轮流式请求下发的那份 list3 ?? list4)后去掉 Skip")]
	public void Scenario09b_兜底请求的工具表与流式一致()
	{
		using AiReplayHarness h = new AiReplayHarness();
		ModelReply broken = ModelReply.Text("好的,");
		broken.BreakMidStream = true;
		h.Model.Then(broken);
		h.Model.Then(ModelReply.NonStream("好的,我来。"));
		h.Send("在吗");
		Assert.Equal(h.Model.Requests[0].ToolNames.OrderBy(n => n), h.Model.Requests[1].ToolNames.OrderBy(n => n));
	}

	// ————————————————————— 10 流式和兜底都空 —————————————————————

	[Fact]
	public void Scenario10a_流式掐断且兜底也空_stream_failed()
	{
		using AiReplayHarness h = new AiReplayHarness();
		// 流内 error 事件,文案不带任何「重试/兜底」关键词 → 不可重试,于是走非流式兜底;兜底也空
		h.Model.Then(ModelReply.StreamError("接口没有返回内容"));
		h.Model.Then(ModelReply.NonStream(""));
		AgentResult r = h.Send("在吗");
		Assert.Equal(StopReason.STREAM_FAILED, r.stop_reason);
		Assert.Equal("", r.Text);
		Assert.DoesNotContain("stream_failed", r.detail);
		Assert.DoesNotContain("empty_response", r.detail);
		Assert.Equal(2, h.Model.Requests.Count);
		// 只有一条终态事件
		h.AssertSingleTurnEnd("error");
		Assert.Equal(1, h.PluginEventCount("turn/end"));
		Assert.Equal(0, h.PluginEventCount("assistant/message"));
	}

	[Fact]
	public void Scenario10b_流式与兜底都返回空内容_empty_response()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Empty());
		h.Model.Then(ModelReply.NonStream(""));
		AgentResult r = h.Send("在吗");
		Assert.Equal(StopReason.EMPTY_RESPONSE, r.stop_reason);
		Assert.Equal("", r.Text);
		Assert.DoesNotContain("stream_failed", r.detail);
		h.AssertSingleTurnEnd("error");
		Assert.Equal(1, h.PluginEventCount("turn/end"));
	}

	// ————————————————————— 11 调用了本轮没下发的工具 —————————————————————

	[Fact]
	public void Scenario11_未下发工具_全量重试一次()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("fs_read", "{\"path\":\"nope.txt\"}", "c1"));
		h.Model.Then(ModelReply.Call("unknown_zzz", "{}", "c2"));
		h.Model.Then(ModelReply.Text("读不到那个文件。"));
		AgentResult r = h.Send("帮我查一下 nope.txt 里写了什么");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		// 第一次下发里没有 fs_read
		Assert.DoesNotContain("fs_read", h.Model.Requests[0].ToolNames);
		// 漏选后改用全量表重发,并且只重试一次
		Assert.Equal(Tools.TOOL_SCHEMAS.Count, h.Model.Requests[1].ToolNames.Count);
		Assert.Contains("fs_read", h.Model.Requests[1].ToolNames);
		Assert.Single(h.TraceEventsOf("tool_schema_miss"));
		// 读工具只读不弹卡;重试后仍然没下发的 unknown_zzz 走权限层,默认被拒
		Assert.Equal(new[] { "unknown_zzz" }, h.ConfirmAsked.Select(c => c.Tool).ToArray());
		// 重试后那一轮不再升级(不会发第三次全量请求)
		Assert.Equal(3, h.Model.Requests.Count);
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 13 中途取消 —————————————————————

	[Fact]
	public void Scenario13_中途取消_turn_end_reason_cancelled()
	{
		using AiReplayHarness h = new AiReplayHarness();
		bool cancelled = false;
		h.OnStatusHook = delegate(string kind, PyDict payload)
		{
			if (kind == "tool_done")
			{
				cancelled = true;
			}
		};
		h.Model.Then(ModelReply.Call("list_instances", "{}", "c1"));
		AgentResult r = null;
		AgentCancelled thrown = Assert.Throws<AgentCancelled>(delegate
		{
			r = h.Send("看看实例", null, delegate
			{
				return cancelled;
			});
		});
		Assert.NotNull(thrown);
		Assert.Null(r);
		// 工具那一轮跑完了,下一轮刚开头就被叫停(没有第二次模型请求)
		Assert.Single(h.Model.Requests);
		Assert.Equal(1, h.PluginEventCount("tool/result"));
		PyDict end = h.AssertSingleTurnEnd("cancelled");
		Assert.Equal("cancelled", Py.S((end.Get("data") as PyDict).Get("stopReason")));
		Assert.Contains("TurnFailed", h.StoreEventNames());
		Assert.Contains("cancelled", h.StoreEventsOf("TurnFailed")[0].Get("reason").ToString());
	}

	// ————————————————————— 14 达到最大轮数 —————————————————————

	[Fact]
	public void Scenario14_达到最大轮数_恰好一条终态事件()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Settings["ai_max_rounds"] = 2L;
		h.Model.Then(ModelReply.Call("list_instances", "{}", "c1"));
		h.Model.Then(ModelReply.Call("get_launcher_state", "{}", "c2"));
		AgentResult r = h.Send("一直查下去");
		Assert.Equal(StopReason.MAX_ROUNDS, r.stop_reason);
		Assert.Equal(2L, r.rounds_used);
		Assert.Equal(2, h.Model.Requests.Count);
		Assert.Equal("已用 2/2 回合", r.detail);
		h.AssertSingleTurnEnd("max_rounds");
		Assert.Single(h.StoreEventsOf("TurnSummary"));
		Assert.Equal(2L, Py.Int(h.StoreEventsOf("TurnSummary")[0].Get("rounds")));
		Assert.False(ChatEventLog.Running(h.ChatId));
	}

	// ————————————————————— 15 先 429 —————————————————————

	[Fact]
	public void Scenario15_先429_重试并粘性切到兜底模型()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Settings["ai_fallback_model"] = AiReplayHarness.FakeFallbackModel;
		h.Model.Repeat(4, ModelReply.HttpError(429, "rate limit exceeded"));
		h.Model.Then(ModelReply.Text("兜底模型的答复。"));
		AgentResult r = h.Send("在吗");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal("兜底模型的答复。", r.Text);
		Assert.Equal(5, h.Model.Requests.Count);
		Assert.All(h.Model.Requests.Take(4), (ModelRequest q) => Assert.Equal(AiReplayHarness.FakeModelName, q.Model));
		Assert.Equal(AiReplayHarness.FakeFallbackModel, h.Model.Requests[4].Model);
		Assert.Equal(3, h.StatusesOf("retry").Count);
		Assert.Single(h.StatusesOf("model_fallback"));
		Assert.Single(h.StoreEventsOf("ModelFallback"));
		Assert.Single(h.TraceEventsOf("model_fallback"));
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 19 第二轮仍然下发上一轮用过的工具 —————————————————————

	/// <summary>
	/// 19a 回归面:install_mod 是 CORE 常驻工具,第二轮本来就会下发。
	/// 这条只能当「CORE 工具始终在表里」的回归断言,**不能**当按需加载(历史回看)的验收依据(见 19b)。
	/// </summary>
	[Fact]
	public void Scenario19a_CORE工具第二轮始终下发()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("install_mod", "{\"instance\":\"default\",\"name\":\"sodium\"}", "c1"));
		h.Model.Then(ModelReply.Text("钠我记下了,先不动。"));
		AgentResult first = h.Send("帮我装钠");
		Assert.Contains("install_mod", h.Model.Requests[0].ToolNames);
		h.Model.Then(ModelReply.Call("install_mod", "{\"instance\":\"default\",\"name\":\"xaeros-minimap\"}", "c2"));
		h.Model.Then(ModelReply.Text("小地图也记下了。"));
		AgentResult second = h.Send("再装个小地图", first.turn_messages);
		Assert.Equal(StopReason.COMPLETED, second.stop_reason);
		Assert.Contains("install_mod", h.Model.Requests[2].ToolNames);
	}

	/// <summary>
	/// 19b 按需加载的敏感面:第二轮只说「再来一个吧」(本身不含任何关键词),
	/// 仍然要把上一轮话题带出来的**非 CORE** 工具(search_content / install_shader)留在表里。
	/// Windows 本来就是回看最近 12 条 user/tool 消息,所以这条是「现状」就该绿;
	/// 它是 C1 的验收依据(Linux 只看最后一条用户消息时这条会红)。
	/// </summary>
	[Fact]
	public void Scenario19b_非CORE工具靠回看历史带出来()
	{
		using AiReplayHarness h = new AiReplayHarness();
		Assert.DoesNotContain("install_shader", AiData.CORE_TOOLS);
		h.Model.Then(ModelReply.Call("install_shader", "{\"instance\":\"default\",\"name\":\"BSL\"}", "c1"));
		h.Model.Then(ModelReply.Text("光影我记下了,先不动。"));
		AgentResult first = h.Send("帮我装个 BSL 光影");
		Assert.Contains("install_shader", h.Model.Requests[0].ToolNames);
		h.Model.Then(ModelReply.Call("install_shader", "{\"instance\":\"default\",\"name\":\"Complementary\"}", "c2"));
		h.Model.Then(ModelReply.Text("那就再来一个光影。"));
		AgentResult second = h.Send("再来一个吧", first.turn_messages);
		Assert.Equal(StopReason.COMPLETED, second.stop_reason);
		List<string> turn2Tools = h.Model.Requests[2].ToolNames;
		Assert.Contains("install_shader", turn2Tools);
		Assert.Contains("search_content", turn2Tools);
		Assert.Equal(new[] { "install_shader", "install_shader" }, h.ConfirmAsked.Select(c => c.Tool).ToArray());
	}
}
