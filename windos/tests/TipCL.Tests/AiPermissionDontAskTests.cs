using System;
using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// P1(2026-11-28 §3 裁决 6):「不询问」(<c>dontAsk</c>)档由「ask → deny」改成「ask → allow」。
/// 仍然保持不变的:1 显式 deny 规则优先;2 删除类(<c>side_effect = delete</c>)仍然拒绝,不给无限定放行;
/// 3 plan 档不受影响;4 其它档位逐字不变。
/// </summary>
public class AiPermissionDontAskTests
{
	private const string DontAsk = Permission.PermissionMode.DONT_ASK;

	private static PermissionResult Decide(string tool, string mode, PyDict args = null, params AiRule[] rules)
	{
		return Permission.Decide(ToolMeta.Get(tool), args ?? new PyDict(), mode, rules);
	}

	/// <summary>复现:改动前这几条在「不询问」档下全是 deny。</summary>
	[Theory]
	[InlineData("install_mod")]      // write_local / medium
	[InlineData("install_shader")]   // write_local / low
	[InlineData("install_game")]     // write_local / high
	[InlineData("create_instance")]  // write_local / medium
	[InlineData("write_mod_config")] // write_local / high
	[InlineData("launch_game")]      // launch
	public void DontAskAllowsWrites(string tool)
	{
		Assert.Equal("allow", Decide(tool, DontAsk).decision);
	}

	/// <summary>只读工具本来就是 allow,钉住不放宽也不收紧。</summary>
	[Fact]
	public void DontAskKeepsReadOnlyAllowed()
	{
		Assert.Equal("allow", Decide("list_mods", DontAsk).decision);
	}

	/// <summary>验收:「不询问」档下删除类仍然拒绝,不给无限定放行(红线 6)。</summary>
	[Theory]
	[InlineData("delete_mod")]
	[InlineData("delete_instance")]
	public void DontAskStillDeniesDelete(string tool)
	{
		Assert.Equal("deny", Decide(tool, DontAsk).decision);
	}

	/// <summary>
	/// 红线 6:没有 <c>rule_content</c> 的 allow 规则从来不能放行删除类 —— 规则被跳过,判定落回档位默认。
	/// (yolo 那种「档位本身全放行」的既有语义不在本包范围内,这里只钉住规则这一层。)
	/// </summary>
	[Fact]
	public void UnqualifiedAllowRuleNeverAllowsDelete()
	{
		AiRule rule = new AiRule("delete_mod", null, "allow");
		PermissionResult byDefault = Decide("delete_mod", Permission.PermissionMode.DEFAULT, null, rule);
		Assert.Equal("ask", byDefault.decision);
		Assert.Equal("", byDefault.rule_id);
		Assert.Equal("deny", Decide("delete_mod", DontAsk, null, rule).decision);
		Assert.Equal("ask", Decide("delete_mod", Permission.PermissionMode.ACCEPT_EDITS, null, rule).decision);
	}

	/// <summary>验收:命中 deny 规则时,「不询问」档也要拒绝。</summary>
	[Fact]
	public void DenyRuleWinsInDontAsk()
	{
		PermissionResult r = Decide("install_mod", DontAsk, null, new AiRule("install_mod", null, "deny"));
		Assert.Equal("deny", r.decision);
	}

	/// <summary>限定参数的 deny 规则只挡命中的那一次调用。</summary>
	[Fact]
	public void DenyRuleOnlyMatchesItsContent()
	{
		AiRule rule = new AiRule("write_mod_config", "sodium.json", "deny");
		PyDict hit = PyDict.Of("path", "sodium.json");
		PyDict miss = PyDict.Of("path", "create.json");
		Assert.Equal("deny", Decide("write_mod_config", DontAsk, hit, rule).decision);
		Assert.Equal("allow", Decide("write_mod_config", DontAsk, miss, rule).decision);
	}

	/// <summary>规则比档位先算:显式 deny 规则不会因为档位是「不询问」而被放行。</summary>
	[Fact]
	public void ExplicitDenyBeatsEveryPermissiveMode()
	{
		AiRule rule = new AiRule("install_mod", null, "deny");
		Assert.Equal("deny", Decide("install_mod", Permission.PermissionMode.YOLO, null, rule).decision);
		Assert.Equal("deny", Decide("install_mod", Permission.PermissionMode.DONT_ASK, null, rule).decision);
	}

	/// <summary>plan 档不受 P1 影响:非只读一律拒绝。</summary>
	[Fact]
	public void PlanModeUntouched()
	{
		Assert.Equal("deny", Decide("install_mod", Permission.PermissionMode.PLAN).decision);
		Assert.Equal("allow", Decide("list_mods", Permission.PermissionMode.PLAN).decision);
	}

	/// <summary>其它档位的判定逐字不动(回归)。</summary>
	[Theory]
	[InlineData("install_mod", Permission.PermissionMode.DEFAULT, "ask")]
	[InlineData("install_mod", Permission.PermissionMode.EDIT, "allow")]
	[InlineData("install_mod", Permission.PermissionMode.ACCEPT_EDITS, "allow")]
	[InlineData("install_mod", Permission.PermissionMode.BUILD, "allow")]
	[InlineData("install_mod", Permission.PermissionMode.YOLO, "allow")]
	[InlineData("delete_mod", Permission.PermissionMode.ACCEPT_EDITS, "ask")]
	[InlineData("delete_mod", Permission.PermissionMode.BUILD, "ask")]
	[InlineData("delete_mod", Permission.PermissionMode.EDIT, "ask")]
	public void OtherModesUnchanged(string tool, string mode, string expected)
	{
		Assert.Equal(expected, Decide(tool, mode).decision);
	}

	/// <summary>系统提示里的「不询问」口径要写明「自动放行,删除类除外」。</summary>
	[Fact]
	public void PermissionNoteTellsTheTruth()
	{
		string note = Permission.PermissionNote(PyDict.Of("ai_permission_mode", "dontAsk"));
		Assert.Contains("不询问", note);
		Assert.Contains("删除", note);
		Assert.DoesNotContain("会被直接拒绝", note);
	}

	/// <summary>档位名的取值集合不变(向后兼容:旧配置里的 dontAsk 仍然合法)。</summary>
	[Fact]
	public void DontAskIsStillAValidMode()
	{
		Assert.True(Permission.PermissionMode.IsValid("dontAsk"));
		Assert.Equal("dontAsk", Permission.NormalizePermissionMode("dontAsk"));
	}

	// ── P1 补充(2026-11-28):「不询问」开关 ai_permission_dont_ask 与 dontAsk 档同口径 ──
	//
	// 改动前那个开关是叠在 Permission.Decide 之外的**第二条判断**:只认「ask」并一律改成 deny,
	// 于是同一个「不询问」语义有两种相反行为(勾了开关写操作被拒、切到档位却放行)。
	// 现在只在入口处合成一个「生效档位」,回合里不再有第二处判断。

	private static PyDict Settings(string mode, bool dontAsk)
	{
		return PyDict.Of("ai_permission_mode", mode, "ai_confirm_writes", true, "ai_permission_dont_ask", dontAsk);
	}

	[Theory]
	[InlineData("default")]
	[InlineData("edit")]
	[InlineData("acceptEdits")]
	[InlineData("auto")]
	[InlineData("autoEdit")]
	[InlineData("build")]
	[InlineData("custom")]
	[InlineData("dontAsk")]
	public void SwitchFoldsIntoTheDontAskMode(string mode)
	{
		Assert.Equal("dontAsk", Permission.EffectiveMode(Settings(mode, true)));
	}

	/// <summary>plan / yolo / bypassPermissions 是档位给的更明确态度,开关不该压过它们。</summary>
	[Theory]
	[InlineData("plan")]
	[InlineData("yolo")]
	[InlineData("bypassPermissions")]
	public void ClearModesOutrankTheSwitch(string mode)
	{
		Assert.Equal(mode, Permission.EffectiveMode(Settings(mode, true)));
	}

	[Fact]
	public void SwitchOffLeavesTheModeAlone()
	{
		Assert.Equal("acceptEdits", Permission.EffectiveMode(Settings("acceptEdits", false)));
		Assert.Equal("default", Permission.EffectiveMode(Settings("default", false)));
	}

	/// <summary>验收:开关开启 + 写工具 → allow(与 dontAsk 档逐字一致)。</summary>
	[Theory]
	[InlineData("install_mod")]
	[InlineData("install_game")]
	[InlineData("write_mod_config")]
	[InlineData("launch_game")]
	public void SwitchOnAllowsWritesLikeTheMode(string tool)
	{
		string effective = Permission.EffectiveMode(Settings("default", true));
		PermissionResult bySwitch = Permission.Decide(ToolMeta.Get(tool), new PyDict(), effective, null);
		PermissionResult byMode = Permission.Decide(ToolMeta.Get(tool), new PyDict(), DontAsk, null);
		Assert.Equal("allow", bySwitch.decision);
		Assert.Equal(byMode.decision, bySwitch.decision);   // 两条路径必须给出同一个答案
	}

	/// <summary>验收:开关开启 + 删除类 → deny。</summary>
	[Theory]
	[InlineData("delete_mod")]
	[InlineData("delete_instance")]
	public void SwitchOnStillDeniesDelete(string tool)
	{
		string effective = Permission.EffectiveMode(Settings("default", true));
		Assert.Equal("deny", Permission.Decide(ToolMeta.Get(tool), new PyDict(), effective, null).decision);
	}

	/// <summary>开关开启也不放宽既有规则:显式 deny 优先,不带内容的 allow 规则仍不放行删除。</summary>
	[Fact]
	public void SwitchOnStillHonoursRules()
	{
		string effective = Permission.EffectiveMode(Settings("default", true));
		Assert.Equal("deny", Permission.Decide(ToolMeta.Get("install_mod"), new PyDict(), effective, new[] { new AiRule("install_mod", null, "deny") }).decision);

		PermissionResult byRule = Permission.Decide(ToolMeta.Get("delete_mod"), new PyDict(), effective, new[] { new AiRule("delete_mod", null, "allow") });
		Assert.Equal("deny", byRule.decision);   // delete 仍是档位级的 deny
		Assert.Equal("", byRule.rule_id);        // 而且不是那条 allow 规则给的
	}

	/// <summary>开关开启不削弱 plan 档(只看不动)。</summary>
	[Fact]
	public void SwitchOnDoesNotWeakenPlanMode()
	{
		string effective = Permission.EffectiveMode(Settings("plan", true));
		Assert.Equal("plan", effective);
		Assert.Equal("deny", Permission.Decide(ToolMeta.Get("install_mod"), new PyDict(), effective, null).decision);
		Assert.Equal("allow", Permission.Decide(ToolMeta.Get("list_mods"), new PyDict(), effective, null).decision);
	}

	/// <summary>开关开启也不收紧 yolo / bypassPermissions(它们本来连 delete 都放行)。</summary>
	[Fact]
	public void SwitchOnDoesNotTightenYolo()
	{
		string effective = Permission.EffectiveMode(Settings("yolo", true));
		Assert.Equal("yolo", effective);
		Assert.Equal("allow", Permission.Decide(ToolMeta.Get("delete_mod"), new PyDict(), effective, null).decision);
	}

	/// <summary>
	/// 口径不变:能力未知的外部 MCP 工具仍然要先确认。
	/// 生效档位不是 yolo / bypassPermissions ⇒ Agent 里那条「等 MCP 能力未知 → ask」的分支条件成立
	/// (它比对的就是这个生效档位);分支本身由下面那条源码守卫钉住。
	/// </summary>
	[Fact]
	public void SwitchOnKeepsUnknownMcpToolsAsking()
	{
		string effective = Permission.EffectiveMode(Settings("default", true));
		Assert.NotEqual("yolo", effective);
		Assert.NotEqual("bypassPermissions", effective);
		Assert.Equal("allow", Permission.Decide(null, new PyDict(), effective, null).decision);   // 元数据为空时 Decide 按 write_local 放行
	}

	/// <summary>系统提示要跟着生效档位说,不能再说「会被直接拒绝」。</summary>
	[Fact]
	public void PermissionNoteFoldsTheSwitch()
	{
		string note = Permission.PermissionNote(Settings("default", true));
		Assert.Contains("不询问", note);
		Assert.Contains("删除", note);
		Assert.DoesNotContain("会被直接拒绝", note);
	}

	/// <summary>
	/// 接线守卫:开关在 Agent 里只剩入口处一次合成,不再有第二处「ask → deny」。
	/// 端到端(真跑一轮)归 AI5-T 的回放框架。
	/// </summary>
	[Fact]
	public void AgentHasNoSecondSwitchPath()
	{
		string agent = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TipCL", "Core", "Launcher", "Ai", "Agent.cs"));
		Assert.Contains("Permission.EffectiveMode(settings)", agent, StringComparison.Ordinal);
		// 键名只允许出现在注释里(说明这段改动),不允许再被读一次 —— 读了就又是第二处判断。
		Assert.DoesNotContain("Get(\"ai_permission_dont_ask\"", agent, StringComparison.Ordinal);
		Assert.DoesNotContain("&& flag)", agent, StringComparison.Ordinal);
		Assert.Contains("外部 MCP 工具能力未知", agent, StringComparison.Ordinal);
	}

	private static string RepoRoot()
	{
		DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !File.Exists(Path.Combine(d.FullName, "TipCL.sln")))
		{
			d = d.Parent;
		}
		Assert.NotNull(d);
		return d!.FullName;
	}
}
