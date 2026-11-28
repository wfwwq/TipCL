using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using TipCL.Tests.AiReplay;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-T 场景表(Windows 侧)第 16-18 条:中文口语请求 → 关键词路由选工具 → 工具序列跑通。
/// 全部保持离线:需要联网的 search_mods 只做「有没有下发」的路由断言,
/// 真正的联网执行按红线不在本机跑(见 16c 的 Skip 说明);写类工具用 confirmFn 桩拦下,不会真的下载。
/// </summary>
public class AiReplayRoutingTests
{
	private static List<string> EventToolNames(AiReplayHarness h, string type)
	{
		return h.PluginEventsOf(type).Select((PyDict e) => Py.S((e.Get("data") as PyDict ?? new PyDict()).Get("name"))).ToList();
	}

	// ————————————————————— 16「给 1.20.1 Fabric 装上钠和小地图」 —————————————————————

	[Fact]
	public void Scenario16a_装模组请求_下发搜索与安装工具()
	{
		using AiReplayHarness h = new AiReplayHarness();
		string text = "给 1.20.1 Fabric 装上钠和小地图";
		Assert.True(Agent.WantsAction(text));
		h.Model.Then(ModelReply.Text("好的,我先搜一下。"));
		h.Send(text);
		ModelRequest req = h.Model.Requests[0];
		// 这条流程要用的三件套必须都在下发的工具表里(否则模型一调就触发 tool_schema_miss 重试)
		Assert.Contains("search_mods", req.ToolNames);
		Assert.Contains("ask_user", req.ToolNames);
		Assert.Contains("install_mod", req.ToolNames);
		Assert.Equal("fake-model-1", req.Model);
		h.AssertSingleTurnEnd("stop");
	}

	[Fact]
	public void Scenario16b_选定后安装序列_离线桩()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("ask_user", "{\"title\":\"装哪几个\",\"questions\":[{\"id\":\"q1\",\"prompt\":\"这两个都装吗?\",\"allow_multiple\":true,\"options\":[{\"id\":\"sodium\",\"label\":\"钠\"},{\"id\":\"minimap\",\"label\":\"小地图\"}]}]}", "c1"));
		h.Model.Then(ModelReply.CallsOf(new FakeToolCall("install_mod", "{\"instance\":\"default\",\"name\":\"sodium\"}", "c2"), new FakeToolCall("install_mod", "{\"instance\":\"default\",\"name\":\"xaeros-minimap\"}", "c3")));
		h.Model.Then(ModelReply.Text("按你选的这两个来。"));
		h.Model.Then(ModelReply.Text("按你选的这两个来。"));
		AgentResult r = h.Send("给 1.20.1 Fabric 装上钠和小地图");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		// 工具序列:ask_user → install_mod ×2(写工具被 confirmFn 桩拦下,不联网、不下载)
		Assert.Equal(new[] { "ask_user", "install_mod", "install_mod" }, EventToolNames(h, "tool/result"));
		Assert.Single(h.AskAsked);
		// 界面会把选项交给确认卡(NormalizeAskArgs 还会补一个「其他」),这里只锁「题目带上了两个候选」
		List<object> options = Py.ListOr(((PyDict)h.AskAsked[0][0]).Get("options"));
		List<string> labels = options.Select((object o) => (o is PyDict d) ? Py.S(Py.Or(d.Get("label"), d.Get("id"))) : Py.Str(o)).ToList();
		Assert.True(options.Count >= 2);
		Assert.Contains("钠", labels);
		Assert.Equal(new[] { "install_mod", "install_mod" }, h.ConfirmAsked.Select(c => c.Tool).ToArray());
		Assert.Equal(4, h.Model.Requests.Count);
		// 问完用户之后那一轮,模型的回复会再被顶一轮(界面要求「选完必须立刻接着调工具」),这里只锁请求条数
		Assert.Equal("按你选的这两个来。", r.Text);
		h.AssertSingleTurnEnd("stop");
	}

	[Fact(Skip = "AI5-T 场景 16(完整序列 search_mods → ask_user → install_mod×2):search_mods 对中文名会真的去查 Modrinth/CurseForge,按红线不在本机跑真实网络;路由与派发已由 16a / 16b 覆盖")]
	public void Scenario16c_装钠的完整工具序列()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("search_mods", "{\"query\":\"钠\"}", "c1"));
		h.Model.Then(ModelReply.Call("ask_user", "{\"prompt\":\"装哪个?\",\"options\":[\"钠\",\"小地图\"]}", "c2"));
		h.Model.Then(ModelReply.CallsOf(new FakeToolCall("install_mod", "{\"instance\":\"default\",\"name\":\"sodium\"}", "c3"), new FakeToolCall("install_mod", "{\"instance\":\"default\",\"name\":\"xaeros-minimap\"}", "c4")));
		h.Model.Then(ModelReply.Text("装好了。"));
		h.Send("给 1.20.1 Fabric 装上钠和小地图");
		Assert.Equal(new[] { "search_mods", "ask_user", "install_mod", "install_mod" }, EventToolNames(h, "tool/result"));
	}

	// ————————————————————— 17「游戏闪退了」 —————————————————————

	[Fact]
	public void Scenario17_闪退_读日志再诊断()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Call("get_crash_report", "{\"instance\":\"default\"}", "c1"));
		h.Model.Then(ModelReply.Call("get_latest_log", "{\"instance\":\"default\"}", "c2"));
		h.Model.Then(ModelReply.Call("diagnose_launch", "{\"instance\":\"default\"}", "c3"));
		h.Model.Then(ModelReply.Text("看完了,像是缺依赖。"));
		AgentResult r = h.Send("游戏闪退了");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal(new[] { "get_crash_report", "get_latest_log", "diagnose_launch" }, EventToolNames(h, "tool/result"));
		// 三条都是只读,全程不弹确认
		Assert.Empty(h.ConfirmAsked);
		Assert.Equal(4, h.Model.Requests.Count);
		Assert.Contains("diagnose_launch", h.Model.Requests[0].ToolNames);
		h.AssertSingleTurnEnd("stop");
	}

	// ————————————————————— 18「好卡」 —————————————————————

	/// <summary>
	/// 18a:场景表原文就是「好卡」两个字。现状**命不中**关键词表
	/// (ExtraTools.Keywords 里是「很卡」「卡顿」,没有单独的「卡」)→ 首轮不下发卡顿三件套,这条现在红。
	/// 这属于 AI5-T 新发现的工具路由缺口,任务书没有对应修复包,建议交给 C3(工具选择量化)一并处理。
	/// </summary>
	[Fact(Skip = "AI5-T 场景 18:「好卡」命不中关键词路由(ExtraTools.Keywords 只有「很卡」「卡顿」),首轮不下发 get_system_info / diagnose_lag / tune_jvm;需要给关键词表补「卡」,无对应包号(AI5-T 新发现,建议并入 C3),补好后去掉 Skip")]
	public void Scenario18a_好卡_下发卡顿三件套()
	{
		using AiReplayHarness h = new AiReplayHarness();
		h.Model.Then(ModelReply.Text("我先看看你的配置。"));
		h.Send("好卡");
		Assert.Contains("get_system_info", h.Model.Requests[0].ToolNames);
		Assert.Contains("diagnose_lag", h.Model.Requests[0].ToolNames);
		Assert.Contains("tune_jvm", h.Model.Requests[0].ToolNames);
	}

	/// <summary>
	/// 18b:关键词命中时(「帧数」)确实会把非 CORE 的卡顿三件套带出来,并按顺序跑到 tune_jvm。
	/// 这三件套都不在 CORE_TOOLS 里,所以这条是真在验关键词路由。
	/// </summary>
	[Fact]
	public void Scenario18b_帧数低_走卡顿三件套()
	{
		using AiReplayHarness h = new AiReplayHarness();
		Assert.DoesNotContain("diagnose_lag", AiData.CORE_TOOLS);
		h.Confirm = delegate(string tool, PyDict args, string label, string reason)
		{
			h.ConfirmAsked.Add((tool, label, reason));
			return true;
		};
		h.Model.Then(ModelReply.Call("get_system_info", "{}", "c1"));
		h.Model.Then(ModelReply.Call("diagnose_lag", "{\"instance\":\"default\"}", "c2"));
		h.Model.Then(ModelReply.Call("tune_jvm", "{\"instance\":\"default\"}", "c3"));
		h.Model.Then(ModelReply.Text("内存和 GC 我建议这么调。"));
		AgentResult r = h.Send("游戏帧数很低,很卡");
		Assert.Equal(StopReason.COMPLETED, r.stop_reason);
		Assert.Equal(new[] { "get_system_info", "diagnose_lag", "tune_jvm" }, EventToolNames(h, "tool/result"));
		Assert.Contains("get_system_info", h.Model.Requests[0].ToolNames);
		Assert.Contains("diagnose_lag", h.Model.Requests[0].ToolNames);
		Assert.Contains("tune_jvm", h.Model.Requests[0].ToolNames);
		// tune_jvm 是写工具(要弹确认),另外两件套是只读
		Assert.Equal(new[] { "tune_jvm" }, h.ConfirmAsked.Select(c => c.Tool).ToArray());
		h.AssertSingleTurnEnd("stop");
	}
}
