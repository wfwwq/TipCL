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
}
