using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-S(Windows 侧):
///   1) 子代理会话 id 每次派发唯一(<父id>-sub-<8 位随机>);
///   2) 轮数设置项 ai_subagent_max_rounds(默认 4)+ dispatch_subagent 的 max_rounds 参数(硬上限内覆盖);
///   3) 会话内状态工具不并行、dispatch_subagent 保留并行;
///   4) 工具名 → 「声称类别」的映射。
/// </summary>
public class AiSubagentTests
{
	[Fact]
	public void SessionIdIsUniquePerDispatch()
	{
		string a = Agent.SubagentSessionId("chat-1");
		string b = Agent.SubagentSessionId("chat-1");
		Assert.NotEqual(a, b);
		Assert.StartsWith("chat-1-sub-", a);
		Assert.StartsWith("chat-1-sub-", b);
		Assert.Equal("chat-1-sub-".Length + 8, a.Length);
	}

	[Fact]
	public void SessionIdFallsBackToActive()
	{
		Assert.StartsWith("active-sub-", Agent.SubagentSessionId(null));
		Assert.StartsWith("active-sub-", Agent.SubagentSessionId(""));
	}

	[Fact]
	public void MaxRoundsDefaultsToFour()
	{
		Assert.Equal(4L, Agent.SUBAGENT_MAX_ROUNDS_DEFAULT);
		Assert.Equal(4L, Agent.SubagentMaxRounds(new PyDict(), new PyDict()));
		// 0 / 负数 / 认不出来的值一律落回默认值,不会变成「不限轮数」
		Assert.Equal(4L, Agent.SubagentMaxRounds(PyDict.Of("ai_subagent_max_rounds", 0L), new PyDict()));
		Assert.Equal(4L, Agent.SubagentMaxRounds(PyDict.Of("ai_subagent_max_rounds", "abc"), new PyDict()));
	}

	[Fact]
	public void MaxRoundsReadsSettingAndArgument()
	{
		PyDict setting = PyDict.Of("ai_subagent_max_rounds", 8L);
		Assert.Equal(8L, Agent.SubagentMaxRounds(setting, new PyDict()));
		// config.json 里写成字符串也认
		Assert.Equal(6L, Agent.SubagentMaxRounds(PyDict.Of("ai_subagent_max_rounds", "6"), new PyDict()));
		// 参数可以往下压,也可以往上抬(硬上限内)
		Assert.Equal(2L, Agent.SubagentMaxRounds(setting, PyDict.Of("max_rounds", 2L)));
		Assert.Equal(12L, Agent.SubagentMaxRounds(setting, PyDict.Of("max_rounds", 12L)));
		// 没有设置项时,参数照样生效
		Assert.Equal(3L, Agent.SubagentMaxRounds(new PyDict(), PyDict.Of("max_rounds", 3L)));
	}

	[Fact]
	public void MaxRoundsIsCapped()
	{
		Assert.Equal(20L, Agent.SUBAGENT_MAX_ROUNDS_HARD_CAP);
		Assert.Equal(20L, Agent.SubagentMaxRounds(PyDict.Of("ai_subagent_max_rounds", 50L), new PyDict()));
		Assert.Equal(20L, Agent.SubagentMaxRounds(new PyDict(), PyDict.Of("max_rounds", 999L)));
		// 参数里的 0 当「没给」处理,不能把子代理压成 0 轮
		Assert.Equal(4L, Agent.SubagentMaxRounds(new PyDict(), PyDict.Of("max_rounds", 0L)));
	}

	[Fact]
	public void SessionStateToolsAreNotParallelizable()
	{
		foreach (string name in new string[3] { "update_plan", "remember", "forget" })
		{
			Assert.False(Scheduler.IsParallelizable(new ToolCall("c-" + name, name, new PyDict())), name + " 不该进并行组");
		}
		// dispatch_subagent 在会话 id 唯一之后保留并行
		Assert.True(Scheduler.IsParallelizable(new ToolCall("c-sub", "dispatch_subagent", new PyDict())));
		// 普通只读查询照旧并行
		Assert.True(Scheduler.IsParallelizable(new ToolCall("c-log", "get_latest_log", new PyDict())));
		// 写工具照旧不并行
		Assert.False(Scheduler.IsParallelizable(new ToolCall("c-write", "write_mod_config", new PyDict())));
	}

	[Fact]
	public void SessionStateToolsSitInTheirOwnGroup()
	{
		List<ToolCall> calls = new List<ToolCall>
		{
			new ToolCall("a", "get_latest_log", new PyDict()),
			new ToolCall("b", "remember", new PyDict()),
			new ToolCall("c", "get_crash_report", new PyDict())
		};
		List<List<ToolCall>> groups = Scheduler.Group(calls);
		Assert.Equal(3, groups.Count);
		Assert.Equal("a", groups[0][0].id);
		Assert.Equal("b", groups[1][0].id);
		Assert.Equal("c", groups[2][0].id);
	}

	[Fact]
	public void ToolCategoryMapping()
	{
		Assert.Equal(Agent.CLAIM_INSTALL, Agent.ToolClaimCategory("install_mod"));
		Assert.Equal(Agent.CLAIM_INSTALL, Agent.ToolClaimCategory("download_java"));
		Assert.Equal(Agent.CLAIM_REMOVE, Agent.ToolClaimCategory("delete_instance"));
		Assert.Equal(Agent.CLAIM_CREATE, Agent.ToolClaimCategory("create_instance"));
		Assert.Equal(Agent.CLAIM_LAUNCH, Agent.ToolClaimCategory("launch_game"));
		Assert.Equal(Agent.CLAIM_MODIFY, Agent.ToolClaimCategory("write_mod_config"));
		Assert.Equal(Agent.CLAIM_MODIFY, Agent.ToolClaimCategory("tune_jvm"));
		Assert.Equal(Agent.CLAIM_MEMORY, Agent.ToolClaimCategory("remember"));
		Assert.Equal(Agent.CLAIM_MEMORY, Agent.ToolClaimCategory("forget"));
		// 只看不做类的工具不兑现任何完成声明
		Assert.Null(Agent.ToolClaimCategory("search_mods"));
		Assert.Null(Agent.ToolClaimCategory("list_mods"));
		Assert.Null(Agent.ToolClaimCategory("get_latest_log"));
		Assert.Null(Agent.ToolClaimCategory("mcp_some_tool"));
		Assert.Null(Agent.ToolClaimCategory(null));
	}
}
