using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// G 包(2026-11-28 §3 裁决 3,方案 A):插件工具按 manifest 声明的 <c>sideEffect</c> 接入权限判定、
/// 检查点快照和「内容不可信」来源标签;没声明 <c>sideEffect</c> 的老插件(含 samples/hello-plugin)
/// 行为不变、仍然直接放行,只在来源标签上加「内容不可信」。
/// </summary>
[Collection("plugin-tools")]
public class PluginToolSideEffectTests : IDisposable
{
	private const string Plugin = "test-plugin";

	public PluginToolSideEffectTests()
	{
		PluginTools.UnregisterPlugin(Plugin);
	}

	public void Dispose()
	{
		PluginTools.UnregisterPlugin(Plugin);
	}

	/// <summary>按 manifest 里 tools.register 的声明注册一个插件工具(走真实的解析路径)。</summary>
	private static PluginToolEntry Register(string name, string sideEffect = null, List<object> affectedPaths = null)
	{
		PyDict tool = PyDict.Of("name", name, "description", "测试用插件工具",
			"parameters", PyDict.Of("type", "object", "properties", new PyDict()));
		if (sideEffect != null)
		{
			tool["sideEffect"] = sideEffect;
		}
		if (affectedPaths != null)
		{
			tool["affectedPaths"] = affectedPaths;
		}
		PluginToolEntry e = PluginHostApi.ToolEntry(Plugin, tool);
		PluginTools.Register(e);
		return e;
	}

	// ── 方案 A:没声明 sideEffect 的老插件 ──

	/// <summary>
	/// samples/hello-plugin 的三个工具(hello_wait / hello_badge / hello_ask)都没声明 sideEffect:
	/// 行为必须与改动前一致 —— 一律放行,不弹确认、不做检查点、不进并行组。
	/// </summary>
	[Theory]
	[InlineData("hello_wait")]
	[InlineData("hello_badge")]
	[InlineData("hello_ask")]
	public void UndeclaredPluginToolKeepsLegacyAllowBehavior(string name)
	{
		Register(name);

		Assert.True(PluginAgentBridge.IsPluginTool(name));
		Assert.Null(ToolMeta.Get(name));               // 不参与权限判定 → Agent 里那条老分支直接 allow
		Assert.False(Tools.IsWriteTool(name));
		Assert.Empty(PluginAgentBridge.AffectedPathsOf(null, name, new PyDict()));  // 不做快照
	}

	/// <summary>没声明 sideEffect 的插件工具:结果标「内容不可信」;声明了 read / none 的不标。</summary>
	[Fact]
	public void UndeclaredPluginToolIsUntrustedButDeclaredReadIsNot()
	{
		Register("demo_legacy");
		Register("demo_read", "read");
		Register("demo_net", "network");
		Register("demo_write", "write_local");

		Assert.True(PluginAgentBridge.IsUntrustedSource("demo_legacy"));
		Assert.True(PluginAgentBridge.IsUntrustedSource("demo_net"));
		Assert.False(PluginAgentBridge.IsUntrustedSource("demo_read"));
		Assert.False(PluginAgentBridge.IsUntrustedSource("demo_write"));
		Assert.False(PluginAgentBridge.IsUntrustedSource("list_mods"));   // 内置工具不走这支
	}

	// ── 声明之后:权限判定 ──

	/// <summary>声明 write_local 的插件工具会走 Permission.Decide:default 档触发确认。</summary>
	[Fact]
	public void DeclaredWriteToolAsksInDefaultMode()
	{
		Register("demo_write", "write_local");
		ToolMetaInfo meta = ToolMeta.Get("demo_write");

		Assert.NotNull(meta);
		Assert.False(meta!.@readonly);
		Assert.Equal("write_local", meta.side_effect);
		Assert.True(Tools.IsWriteTool("demo_write"));

		PermissionResult r = Permission.Decide(meta, new PyDict(), Permission.PermissionMode.DEFAULT, null);
		Assert.Equal("ask", r.decision);   // 「ask」在 Agent 里就是弹确认卡
	}

	/// <summary>P1 × G:声明 write_local 的插件工具在「不询问」档下放行。</summary>
	[Fact]
	public void DeclaredWriteToolIsAllowedInDontAskMode()
	{
		Register("demo_write", "write_local");
		Assert.Equal("allow", Permission.Decide(ToolMeta.Get("demo_write"), new PyDict(), Permission.PermissionMode.DONT_ASK, null).decision);
	}

	/// <summary>声明 delete 的插件工具在「不询问」档下仍然拒绝,且不给无限定 allow。</summary>
	[Fact]
	public void DeclaredDeleteToolStaysDeniedInDontAskAndPlan()
	{
		Register("demo_delete", "delete");
		ToolMetaInfo meta = ToolMeta.Get("demo_delete");
		Assert.NotNull(meta);
		Assert.Equal("delete", meta!.side_effect);
		Assert.True(Tools.IsWriteTool("demo_delete"));

		Assert.Equal("deny", Permission.Decide(meta, new PyDict(), Permission.PermissionMode.DONT_ASK, null).decision);
		Assert.Equal("deny", Permission.Decide(meta, new PyDict(), Permission.PermissionMode.PLAN, null).decision);

		// 红线 6:删除类不给无限定 allow —— 不带 rule_content 的 allow 规则对它无效。
		AiRule rule = new AiRule("demo_delete", null, "allow");
		PermissionResult byRule = Permission.Decide(meta, new PyDict(), Permission.PermissionMode.DEFAULT, new[] { rule });
		Assert.Equal("ask", byRule.decision);
		Assert.Equal("", byRule.rule_id);

		// 显式 deny 规则仍然优先。
		Assert.Equal("deny", Permission.Decide(meta, new PyDict(), Permission.PermissionMode.DONT_ASK, new[] { new AiRule("demo_delete", null, "deny") }).decision);
	}

	/// <summary>plan 档对声明写入的插件工具同样是拒绝(子代理只读限制走 SubagentAllows,不受本包影响)。</summary>
	[Fact]
	public void DeclaredWriteToolIsDeniedInPlanMode()
	{
		Register("demo_write", "write_local");
		Assert.Equal("deny", Permission.Decide(ToolMeta.Get("demo_write"), new PyDict(), Permission.PermissionMode.PLAN, null).decision);
	}

	/// <summary>声明 read / network / none 的插件工具是只读,不会进确认卡。</summary>
	[Theory]
	[InlineData("read")]
	[InlineData("network")]
	[InlineData("none")]
	public void DeclaredReadOnlyToolIsNeverAWrite(string side)
	{
		Register("demo_ro", side);
		ToolMetaInfo meta = ToolMeta.Get("demo_ro");
		Assert.NotNull(meta);
		Assert.True(meta!.@readonly);
		Assert.False(Tools.IsWriteTool("demo_ro"));
	}

	// ── 声明之后:检查点快照 ──

	/// <summary>声明写入的插件工具跟内置 write_local 工具走同一张档位阶梯。</summary>
	[Theory]
	[InlineData(Permission.PermissionMode.DEFAULT, "ask")]
	[InlineData(Permission.PermissionMode.PLAN, "deny")]
	[InlineData(Permission.PermissionMode.EDIT, "allow")]
	[InlineData(Permission.PermissionMode.ACCEPT_EDITS, "allow")]
	[InlineData(Permission.PermissionMode.DONT_ASK, "allow")]
	public void DeclaredWriteToolFollowsTheModeLadder(string mode, string expected)
	{
		Register("demo_write", "write_local");
		// 先钉住「元数据真的来自声明」:Permission.Decide 拿到 null 元数据时会按 write_local 兜底,
		// 少了这一行,这条用例在「接线被删掉」时也会假装通过。
		Assert.NotNull(ToolMeta.Get("demo_write"));
		Assert.Equal(expected, Permission.Decide(ToolMeta.Get("demo_write"), new PyDict(), mode, null).decision);
	}

	/// <summary>
	/// 检查点路径:插件工具用 manifest 声明的 affectedPaths,内置工具仍走 Tools.AffectedPaths。
	/// 再真跑一次 Checkpoint.Snapshot,证明「执行前生成检查点」这一步拿得到文件。
	/// </summary>
	[Fact]
	public void CheckpointPathsComeFromPluginDeclaration()
	{
		string dir = Path.Combine(Path.GetTempPath(), "tipcl-g-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "written.txt"), "g-package");
		try
		{
			Register("demo_write", "write_local", new List<object> { dir });

			List<string> paths = PluginAgentBridge.AffectedPathsOf(null, "demo_write", new PyDict());
			Assert.Equal(new[] { dir }, paths);

			// 内置工具仍然走原来的实现(这里没有后端实例 → 空表,和改动前一致)。
			Assert.Empty(PluginAgentBridge.AffectedPathsOf(null, "delete_mod", PyDict.Of("filename", "x.jar")));

			PyDict snapshot = Checkpoint.Snapshot("g-package-chat", "turn-1", paths);
			Assert.True((bool)snapshot["ok"], "检查点没生成:" + Py.S(snapshot.Get("reason")));
			Assert.Equal(1L, (long)snapshot["files"]);
		}
		finally
		{
			try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
		}
	}

	/// <summary>没声明 affectedPaths 就不快照(不假装能一键撤回)。</summary>
	[Fact]
	public void DeclaredWriteWithoutPathsSnapshotsNothing()
	{
		Register("demo_write", "write_local");
		Assert.Empty(PluginAgentBridge.AffectedPathsOf(null, "demo_write", new PyDict()));
	}

	// ── 声明解析与校验 ──

	[Theory]
	[InlineData("none", "none")]
	[InlineData("read", "read")]
	[InlineData("network", "network")]
	[InlineData("write_local", "write_local")]
	[InlineData("delete", "delete")]
	[InlineData("launch", "launch")]
	public void SideEffectValuesAreAccepted(string raw, string expected)
	{
		Assert.Equal(expected, PluginTools.NormalizeSideEffect(raw));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void BlankSideEffectMeansUndeclared(string raw)
	{
		Assert.Null(PluginTools.NormalizeSideEffect(raw));
	}

	/// <summary>字段缺失 = 没声明(老插件的常态)。</summary>
	[Fact]
	public void MissingSideEffectMeansUndeclared()
	{
		Assert.Null(PluginTools.NormalizeSideEffect(null));
		Assert.Null(PluginHostApi.ToolEntry(Plugin, PyDict.Of("name", "demo_undeclared")).SideEffect);
	}

	/// <summary>拼错的 sideEffect 必须报错,不能默默降级成「没声明」(那等于绕过了权限判定)。</summary>
	[Theory]
	[InlineData("write")]
	[InlineData("writeLocal")]
	[InlineData("WRITE_LOCAL")]
	[InlineData("fs")]
	public void BadSideEffectIsRejected(string raw)
	{
		Assert.Throws<PluginRpcException>(() => PluginTools.NormalizeSideEffect(raw));
		Assert.Throws<PluginRpcException>(() => PluginHostApi.ToolEntry(Plugin, PyDict.Of("name", "demo_bad", "sideEffect", raw)));
	}

	[Fact]
	public void AffectedPathsAreCleanedUp()
	{
		List<string> paths = PluginTools.NormalizeAffectedPaths(Py.L("a", "", " ", "a", "b"));
		Assert.Equal(new[] { "a", "b" }, paths);
		Assert.Empty(PluginTools.NormalizeAffectedPaths(null));
	}

	/// <summary>声明值会在注册那一刻落到注册表上(Node 与 .NET 插件共用 NormalizeSideEffect)。</summary>
	[Fact]
	public void DeclarationLandsOnTheRegistryEntry()
	{
		PluginToolEntry e = Register("demo_write", "write_local", new List<object> { "C:/x" });
		Assert.Equal("write_local", e.SideEffect);
		Assert.Equal(new[] { "C:/x" }, e.AffectedPaths);
		Assert.Equal("write_local", PluginTools.DeclaredMeta("demo_write")!.side_effect);
		Assert.Null(PluginTools.DeclaredMeta("demo_unknown"));
	}

	// ── 接线守卫(Agent.cs 是别人属主,G 包只动三处;这里钉住这三处还在) ──

	/// <summary>
	/// Agent.cs 里 G 包只落三处:插件工具的权限判定、插件工具结果的来源标签、检查点取路径。
	/// 端到端(真跑一轮 RunAgent 看确认卡与检查点)归 AI5-T 的回放框架;在那之前用这条守卫挡住
	/// 「有人删掉了接线」这类回归 —— 本仓库已有同类源码守卫(LayoutV3SourceTests / PageDefaultsV3Tests)。
	/// </summary>
	[Fact]
	public void AgentWiringHasTheThreeIntegrationPoints()
	{
		string agent = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TipCL", "Core", "Launcher", "Ai", "Agent.cs"));

		Assert.Contains("ToolMetaInfo pluginMeta = ToolMeta.Get(item11.name);", agent, StringComparison.Ordinal);
		Assert.Contains("PermissionResult pluginVerdict = Permission.Decide(pluginMeta, item11.args, text3, rules);", agent, StringComparison.Ordinal);
		Assert.Contains("PluginAgentBridge.IsUntrustedSource(item13.name)", agent, StringComparison.Ordinal);
		Assert.Contains("PluginAgentBridge.AffectedPathsOf(backend, toolCall2.name, toolCall2.args)", agent, StringComparison.Ordinal);
	}

	/// <summary>协议文档必须写上新增的可选字段(红线 5:只加字段)。</summary>
	[Fact]
	public void DesignDocDeclaresTheNewFields()
	{
		string design = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "plugins", "DESIGN.md"));
		Assert.Contains("sideEffect", design, StringComparison.Ordinal);
		Assert.Contains("affectedPaths", design, StringComparison.Ordinal);
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
