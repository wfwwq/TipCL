using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using TipCL.Tests.AiReplay;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-T 场景表(Windows 侧):6 计划审批、12 子代理写拦截、21 多子代理会话隔离、22「不询问」档。
/// 6 / 12b / 21 / 22a 依赖修复才能绿,按期望行为写好并标 Skip(注明转绿包号);
/// 12a / 22b 是现状就该绿的守卫。
/// </summary>
public class AiReplayPlanSubagentPermissionTests
{
	private static string ToolResultText(PyDict toolResultEvent)
	{
		PyDict data = toolResultEvent.Get("data") as PyDict ?? new PyDict();
		List<object> content = Py.ListOr(Py.Or(data.Get("content"), new List<object>()));
		return content.Count == 0 ? "" : Py.S(((PyDict)content[0]).Get("text"));
	}

	// ————————————————————— 6 update_plan 后计划被驳回 —————————————————————

	[Fact(Skip = "AI5-T 场景 6:Windows 现在是「先执行工具、再弹计划审批」(Agent.cs 的 Scheduler.RunGroups 在 plan_approval 之前),驳回时写操作已经落盘;由 P2(审批挪到执行之前)修好后去掉 Skip")]
	public void Scenario06_计划被驳回_不执行任何写操作()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Settings["ai_permission_mode"] = "plan";
		// 显式 allow 规则优先于 plan 档 → 现在这个写操作会真的执行(这正是 P2 要修的点)
		h.Settings["ai_permission_rules"] = Py.L(PyDict.Of("toolName", "write_mod_config", "behavior", "allow"));
		h.Confirm = delegate(string tool, PyDict args, string label, string reason)
		{
			h.ConfirmAsked.Add((tool, label, reason));
			return false;
		};
		h.Model.Then(ModelReply.CallsOf(new FakeToolCall("update_plan", "{\"items\":[{\"title\":\"改配置\"},{\"title\":\"重启\"}]}", "c1"), new FakeToolCall("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-6.toml\",\"content\":\"x\"}", "c2")));
		h.Model.Then(ModelReply.Text("好,那我先不动。"));
		AgentResult r = h.Send("先给我个计划");
		// 「计划未获批准」那句文案走 i18n,这里只钉结构性事实:没执行任何工具、没落盘
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.False(File.Exists(h.InstanceConfigFile("t-win-6.toml")), "驳回后不许落盘");
		Assert.Empty(h.StoreEventsOf("ToolStarted"));
		Assert.All(h.ConfirmAsked, c => Assert.Equal("plan_approval", c.Tool));
	}

	// ————————————————————— 12 子代理尝试写工具 —————————————————————

	[Fact]
	public void Scenario12a_子代理写工具被拦截()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("dispatch_subagent", "{\"task\":\"把结果写到 out.txt\"}", "c1"));
		// 子代理第 1 次:调了一个它那份只读工具表里没有的写工具 → 触发全量重试
		h.Model.Then(ModelReply.Call("fs_write", "{\"path\":\"out.txt\",\"content\":\"hi\"}", "s1"));
		// 子代理第 2 次(全量重试后仍然只读)→ 执行前被 SubagentAllows 拦下
		h.Model.Then(ModelReply.Call("fs_write", "{\"path\":\"out.txt\",\"content\":\"hi\"}", "s2"));
		h.Model.Then(ModelReply.Text("我不能写文件。"));
		h.Model.Then(ModelReply.Text("那就算了。"));
		AgentResult r = h.Send("派个子代理去写文件");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		// 子代理请求 1:写工具没下发 → tool_schema_miss;请求 2 仍是只读表
		Assert.DoesNotContain("fs_write", h.Model.Requests[1].ToolNames);
		Assert.DoesNotContain("fs_write", h.Model.Requests[2].ToolNames);
		Assert.Contains("子代理只读", string.Join("\n", h.Model.Requests[3].ToolResults));
		Assert.NotEmpty(h.TraceEventsOf("subagent_write_blocked"));
		// 没有真的写盘
		Assert.False(File.Exists(Path.Combine(h.Root, "out.txt")));
		// 子代理自己的会话记录写在 <父id>-sub 里
		Assert.NotEmpty(h.StoreEvents(h.ChatId + "-sub"));
		Assert.Contains("子代理只读", h.TraceEventsOf("subagent_write_blocked")[0].Get("exc_msg")?.ToString() ?? "子代理只读");
		h.AssertSingleTurnEnd("stop");
	}

	[Fact(Skip = "AI5-T 场景 12(兜底工具表):子代理走非流式兜底时,Agent.cs 的 ChatOnce 传的是全量 Tools.TOOL_SCHEMAS,子代理在兜底里也能看到写工具;由 D8 修好后去掉 Skip")]
	public void Scenario12b_子代理兜底请求里没有写工具()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("dispatch_subagent", "{\"task\":\"随便说点什么\"}", "c1"));
		ModelReply broken = ModelReply.Text("我先看看");
		broken.BreakMidStream = true;
		h.Model.Then(broken);
		h.Model.Then(ModelReply.NonStream("看完了。"));
		h.Model.Then(ModelReply.Text("好。"));
		h.Send("派个子代理");
		// 子代理的最后一次请求是非流式兜底:它下发的工具表里不该有写工具
		ModelRequest fallback = h.Model.Requests.Last(q => !q.Stream);
		Assert.All(fallback.ToolNames, delegate(string n)
		{
			Assert.True(ToolMeta.Get(n) == null || ToolMeta.Get(n).@readonly, "子代理兜底请求里不该有写工具:" + n);
		});
	}

	// ————————————————————— 21 派两个子代理 —————————————————————

	[Fact(Skip = "AI5-T 场景 21:Windows 的 RunSubagent 把会话 id 固定成 <父id>-sub,同一回合派两个子代理会串在一起;由 S(改成 <父id>-sub-<短随机id>)修好后去掉 Skip")]
	public void Scenario21_两个子代理的会话id不同()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.CallsOf(new FakeToolCall("dispatch_subagent", "{\"task\":\"任务甲\"}", "c1"), new FakeToolCall("dispatch_subagent", "{\"task\":\"任务乙\"}", "c2")));
		h.Model.Then(ModelReply.Text("甲说完了。"));
		h.Model.Then(ModelReply.Text("乙说完了。"));
		h.Model.Then(ModelReply.Text("两个子任务都回来了。"));
		h.Send("同时派两个子代理");
		string dir = Path.Combine(h.Root, "cache", "ai_sessions");
		List<string> subs = Directory.GetFiles(dir, "*.jsonl").Select(Path.GetFileNameWithoutExtension).Where(n => n.Contains("-sub")).ToList();
		Assert.Equal(2, subs.Count);
		Assert.Equal(2, subs.Distinct().Count());
	}

	// ————————————————————— 22「不询问」档 —————————————————————

	[Fact(Skip = "AI5-T 场景 22:「不询问」(dontAsk)现在是 ask → deny,写工具被拒绝而不是直接执行;由 P1(改成 ask → allow,删除类仍拒绝)修好后去掉 Skip")]
	public void Scenario22a_不询问档_写工具直接执行()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Settings["ai_permission_mode"] = "dontAsk";
		h.Model.Then(ModelReply.Call("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-22.toml\",\"content\":\"a\"}", "c1"));
		h.Model.Then(ModelReply.Text("改好了。"));
		AgentResult r = h.Send("把配置改了");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Empty(h.ConfirmAsked);
		Assert.True(File.Exists(h.InstanceConfigFile("t-win-22.toml")), "「不询问」= 自动放行,不该被拒");
	}

	/// <summary>
	/// 22b 现状就该绿的守卫(改完 P1 之后也必须仍然绿):
	/// 「不询问」档下删除类仍然拒绝,显式 deny 规则永远优先。
	/// </summary>
	[Fact]
	public void Scenario22b_不询问档_删除类与显式deny仍然拒绝()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Settings["ai_permission_mode"] = "dontAsk";
		h.Model.Then(ModelReply.Call("delete_instance", "{\"name\":\"default\"}", "c1"));
		h.Model.Then(ModelReply.Text("那我不删了。"));
		AgentResult r = h.Send("删掉 default 实例");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Empty(h.ConfirmAsked);
		Assert.True(Directory.Exists(h.InstancePath("default")), "删除类在「不询问」档下必须仍然拒绝");
		Assert.Contains(h.PluginEventsOf("tool/result"), (PyDict e) => Py.Truthy((e.Get("data") as PyDict).Get("isError")));

		// 显式 deny 规则:即使档位放行也优先拒绝
		using AiReplayHarness h2 = new AiReplayHarness();
		h2.Settings["ai_permission_mode"] = "yolo";
		h2.Settings["ai_permission_rules"] = Py.L(PyDict.Of("toolName", "write_mod_config", "behavior", "deny"));
		h2.Model.Then(ModelReply.Call("write_mod_config", "{\"instance\":\"default\",\"path\":\"t-win-22b.toml\",\"content\":\"a\"}", "c1"));
		h2.Model.Then(ModelReply.Text("这条被规则挡住了。"));
		AgentResult r2 = h2.Send("把配置改了");
		Assert.Equal(StopReason.COMPLETED, r2.stop_reason);
		Assert.False(File.Exists(h2.InstanceConfigFile("t-win-22b.toml")));
		Assert.Empty(h2.StoreEventsOf("ToolStarted"));
	}
}
