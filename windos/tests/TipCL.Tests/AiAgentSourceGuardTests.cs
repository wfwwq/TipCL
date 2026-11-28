using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-D8 / P2 的接线守卫。主循环是上千行的状态机,net8 测试宿主没法直接跑完整轮次
/// (假模型回放框架由 AI5-T 提供),所以这里与 LayoutV3SourceTests / LayoutPageSplitTests 同一套做法:
/// 按**源码文本**把两条接线事实钉住,行为场景留给 T 的 9 / 12 / 6 号场景。
///   D8:非流式兜底请求必须用本轮流式实际下发的那份工具表(list4),不能再发全量 Tools.TOOL_SCHEMAS;
///   P2:plan 档的 plan_approval 必须在 Scheduler.RunGroups 之前弹,驳回后本轮不执行任何工具。
/// </summary>
public class AiAgentSourceGuardTests
{
	private static string Root()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return dir!.FullName;
	}

	private static string AgentSource()
	{
		return File.ReadAllText(Path.Combine(Root(), "src", "TipCL", "Core", "Launcher", "Ai", "Agent.cs"));
	}

	[Fact]
	public void FallbackUsesTheSameToolListAsTheStream()
	{
		string src = AgentSource();
		Assert.Contains("Client.ChatOnce(_request_settings(), messages, list4, 0.3, httpCancel)", src, StringComparison.Ordinal);
		Assert.DoesNotContain("ChatOnce(_request_settings(), messages, PluginAgentBridge.Tools(pturn, Tools.TOOL_SCHEMAS)", src, StringComparison.Ordinal);
		// 流式那一路仍然是 list4
		Assert.Contains("Client.ChatStream(_request_settings(), messages, list4, 0.3, httpCancel)", src, StringComparison.Ordinal);
	}

	[Fact]
	public void SubagentFallbackAlsoGetsFilteredTools()
	{
		// 子代理的只读过滤发生在 Tools.SelectToolSchemas(ai_subagent)里;兜底复用同一份 list4,
		// 于是子代理的兜底请求里也不会出现写工具
		List<object> tools = Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", "帮我装个小地图模组,顺便删掉旧的") }, PyDict.Of("ai_subagent", true));
		Assert.NotEmpty(tools);
		foreach (object schema in tools)
		{
			string name = Tools.SchemaName(schema);
			Assert.True(Agent.SubagentAllows(name), "子代理工具表里不该有写工具:" + name);
		}
	}

	[Fact]
	public void PlanApprovalHappensBeforeExecution()
	{
		string src = AgentSource();
		int confirmAt = src.IndexOf("_call_confirm(confirmFn, \"plan_approval\"", StringComparison.Ordinal);
		int runAt = src.IndexOf("Scheduler.RunGroups(list13, _execute);", StringComparison.Ordinal);
		Assert.True(confirmAt >= 0, "找不到 plan_approval 的确认调用");
		Assert.True(runAt >= 0, "找不到 Scheduler.RunGroups 调用");
		Assert.True(confirmAt < runAt, "plan_approval 必须在执行工具之前");
		// 执行之后那一段旧审批已经删掉
		Assert.DoesNotContain("planItems.Count > 0 && planTurnId == turn.id && !planApproved", src, StringComparison.Ordinal);
		// 驳回分支:不执行、不生成检查点,只回填
		Assert.Contains("计划未获批准,本轮到此为止", src, StringComparison.Ordinal);
		int rejectAt = src.IndexOf("if (!Py.Truthy(obj4))", confirmAt, StringComparison.Ordinal);
		Assert.True(rejectAt > confirmAt, "找不到驳回分支");
	}

	[Fact]
	public void CheckpointOnlyHappensInsideExecute()
	{
		string src = AgentSource();
		// 检查点只在 _execute 里做:驳回分支根本不进 _execute,所以不会留下检查点
		int snapshot = src.IndexOf("Checkpoint.Snapshot(sessionId, turn.id", StringComparison.Ordinal);
		int executeAt = src.IndexOf("string _execute(ToolCall toolCall2)", StringComparison.Ordinal);
		int rejectReturn = src.IndexOf("计划未获批准,本轮到此为止", StringComparison.Ordinal);
		Assert.True(snapshot > executeAt, "检查点必须只在工具执行体里生成");
		Assert.True(rejectReturn < executeAt, "驳回必须发生在工具执行体之前");
	}
}
