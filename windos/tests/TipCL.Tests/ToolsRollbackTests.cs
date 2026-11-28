using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Launcher.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-D3:写工具的回滚覆盖(Tools.AffectedPaths)。
///
/// 复现的缺陷:这张 switch 只认基础工具 —— tune_jvm / restore_save_backup / layout_apply 落到
/// default 分支返回空清单,于是检查点不做任何快照,而「撤回上一轮」仍然报「已撤回」:
/// 界面说还原了,磁盘上一个字都没动。这里钉四件事:
/// <list type="number">
/// <item>补齐的三个工具给出**正确**路径(不是「非空就行」);</item>
/// <item>原有写工具的路径没被改坏(逐个断言,含 delete_instance 走 name 而不是默认实例);</item>
/// <item>写盘但做不了快照的工具进显式豁免表,并且有一条「这一步无法回退」的说明;</item>
/// <item>ToolMeta 里每个写工具都必须落在「有快照」或「显式豁免」之一 —— 新增写工具忘了登记就红。</item>
/// </list>
/// 全程在 TestHome 的临时数据目录里跑,不碰用户真实数据。
/// </summary>
public class ToolsRollbackTests : IDisposable
{
	private const string InstName = "d3test";

	private readonly TestHome _home = new TestHome();

	private readonly string _inst;

	private readonly string? _layoutDir;

	public ToolsRollbackTests()
	{
		_inst = Instances.GetInstancePath(InstName);
		Directory.CreateDirectory(PPath.Join(_inst, "mods"));
		Directory.CreateDirectory(PPath.Join(_inst, "saves"));
		// 布局文档是进程级静态目录(别的布局用例也碰它):自己钉一个临时目录,结束后还原
		_layoutDir = LayoutStoreV2.DirOverride;
		LayoutStoreV2.DirOverride = Path.Combine(Utils.ROOT, "layout");
		LayoutStoreV2.ResetCache();
	}

	public void Dispose()
	{
		LayoutStoreV2.DirOverride = _layoutDir;
		LayoutStoreV2.ResetCache();
		_home.Dispose();
	}

	// ───────────────────── D3 补齐的三个工具 ─────────────────────

	[Fact]
	public void TuneJvmApplySnapshotsGlobalConfig()
	{
		// Windows 的 tune_jvm 写的是**全局** config.json(ExtraTools.TuneJvm → backend.save_settings)
		Assert.Equal(
			new List<string> { Config.CONFIG_FILE },
			Tools.AffectedPaths("tune_jvm", PyDict.Of("instance", InstName, "apply", true), InstName));
	}

	[Fact]
	public void TuneJvmWithoutApplyHasNoSnapshot()
	{
		// 只给建议(apply=false)不落盘:不该凭空占一份检查点
		Assert.Empty(Tools.AffectedPaths("tune_jvm", PyDict.Of("instance", InstName), InstName));
		Assert.Empty(Tools.AffectedPaths("tune_jvm", PyDict.Of("instance", InstName, "apply", false), InstName));
	}

	[Fact]
	public void RestoreSaveBackupSnapshotsTargetWorld()
	{
		PyDict args = PyDict.Of("instance", InstName, "backup_name", "world-20261128-101010.zip", "overwrite", true);
		Assert.Equal(
			new List<string> { PPath.Join(_inst, "saves", "world") },
			Tools.AffectedPaths("restore_save_backup", args, InstName));
	}

	[Fact]
	public void RestoreSaveBackupWithoutOverwriteSnapshotsNewFolder()
	{
		// 不覆盖时执行会新建一个目录:重名就按 Saves.RestoreBackup 的规则加 -还原 / -还原2 …
		PyDict args = PyDict.Of("instance", InstName, "backup_name", "world-20261128-101010.zip");
		Directory.CreateDirectory(PPath.Join(_inst, "saves", "world"));
		Assert.Equal(
			new List<string> { PPath.Join(_inst, "saves", "world-还原") },
			Tools.AffectedPaths("restore_save_backup", args, InstName));

		Directory.CreateDirectory(PPath.Join(_inst, "saves", "world-还原"));
		Assert.Equal(
			new List<string> { PPath.Join(_inst, "saves", "world-还原2") },
			Tools.AffectedPaths("restore_save_backup", args, InstName));
	}

	[Fact]
	public void RestoreSaveBackupWithoutNameHasNoSnapshot()
	{
		Assert.Empty(Tools.AffectedPaths("restore_save_backup", PyDict.Of("instance", InstName), InstName));
	}

	[Fact]
	public void LayoutApplySnapshotsBothLayoutFiles()
	{
		// 布局写的是全局的 ui_layout_v2.json(真源)+ 写通出来的 ui_page_layouts.json
		Assert.Equal(
			new List<string> { LayoutStoreV2.FilePath, LayoutStoreV2.V1FilePath },
			Tools.AffectedPaths(LayoutTools.ApplyName, new PyDict(), InstName));
		Assert.EndsWith("ui_layout_v2.json", LayoutStoreV2.FilePath);
		Assert.EndsWith("ui_page_layouts.json", LayoutStoreV2.V1FilePath);
	}

	// ───────────────────── 原有写工具:逐个断言路径 ─────────────────────

	[Fact]
	public void ExistingWriteToolsStillResolveToTheRightPaths()
	{
		string mods = PPath.Join(_inst, "mods");
		Assert.Equal(new List<string> { mods }, Tools.AffectedPaths("install_mod", PyDict.Of("instance", InstName), InstName));
		Assert.Equal(new List<string> { PPath.Join(_inst, "shaderpacks") }, Tools.AffectedPaths("install_shader", PyDict.Of("instance", InstName), InstName));
		Assert.Equal(new List<string> { PPath.Join(_inst, "resourcepacks") }, Tools.AffectedPaths("install_resourcepack", PyDict.Of("instance", InstName), InstName));
		Assert.Equal(new List<string> { PPath.Join(_inst, "datapacks") }, Tools.AffectedPaths("install_datapack", PyDict.Of("instance", InstName), InstName));
		Assert.Equal(new List<string> { PPath.Join(_inst, "saves") }, Tools.AffectedPaths("install_world", PyDict.Of("instance", InstName), InstName));
		Assert.Equal(new List<string> { _inst }, Tools.AffectedPaths("install_modpack", PyDict.Of("instance", InstName), InstName));
		Assert.Equal(new List<string> { PPath.Join(_inst, "versions") }, Tools.AffectedPaths("install_game", PyDict.Of("instance", InstName), InstName));
		Assert.Equal(new List<string> { Config.CONFIG.java_dir }, Tools.AffectedPaths("download_java", PyDict.Of("instance", InstName), InstName));

		// 改配置:目标锁定在实例的 config/ 下;越界的相对路径直接不给快照
		Assert.Equal(
			new List<string> { PPath.Join(_inst, "config", "x.toml") },
			Tools.AffectedPaths("write_mod_config", PyDict.Of("instance", InstName, "path", "x.toml"), InstName));
		Assert.Empty(Tools.AffectedPaths("write_mod_config", PyDict.Of("instance", InstName, "path", "../escape.toml"), InstName));

		// 启停 / 删除模组:新旧两个文件名都要能还原
		List<string> expectMod = new List<string> { PPath.Join(mods, "a.jar"), PPath.Join(mods, "a.jar.disabled") };
		Assert.Equal(expectMod, Tools.AffectedPaths("delete_mod", PyDict.Of("instance", InstName, "filename", "a.jar"), InstName));
		Assert.Equal(expectMod, Tools.AffectedPaths("disable_mod", PyDict.Of("instance", InstName, "filename", "a.jar"), InstName));
		Assert.Equal(expectMod, Tools.AffectedPaths("enable_mod", PyDict.Of("instance", InstName, "filename", "a.jar"), InstName));

		// fs_write:目标按工具自己的路径守卫解析成绝对路径
		Assert.Equal(
			new List<string> { new PPath(PPath.Join(_inst, "docs", "a.txt")).Abs() },
			Tools.AffectedPaths("fs_write", PyDict.Of("instance", InstName, "path", "docs/a.txt", "content", "hi"), InstName));
	}

	[Fact]
	public void DeleteInstanceSnapshotsTheInstanceInTheNameArgument()
	{
		// 删实例的目标是参数里的 name,不是 instance:以前这里返回的是默认实例目录,
		// 快照拍到别的实例上 —— 真被删的那个实例没进检查点,撤回时却显示已还原。
		string other = Instances.GetInstancePath("d3other");
		Assert.Equal(
			new List<string> { other },
			Tools.AffectedPaths("delete_instance", PyDict.Of("name", "d3other"), InstName));
		Assert.Empty(Tools.AffectedPaths("delete_instance", new PyDict(), InstName));
	}

	[Fact]
	public void BackendOverloadMatchesInstanceOverload()
	{
		// 生产调用点是 Tools.AffectedPaths(backend, name, args)(Agent.cs 里做检查点那处):
		// 两条入口必须给出同一份路径 —— 别只在测试入口上正确。
		BackendApi api = new BackendApi(new EventBus());
		foreach (string name in new string[3] { "install_mod", "restore_save_backup", "tune_jvm" })
		{
			PyDict args = PyDict.Of("instance", InstName, "backup_name", "world-20261128-101010.zip", "overwrite", true, "apply", true);
			Assert.Equal(
				Tools.AffectedPaths(name, args, InstName),
				Tools.AffectedPaths(api, name, args));
		}
	}

	// ───────────────────── 豁免表与覆盖率锁 ─────────────────────

	/// <summary>写盘、但有意不做文件快照的工具(AI5-D3 的显式豁免表)。</summary>
	private static readonly string[] ExemptWrites = new string[6] { "backup_save", "multiplayer", "add_offline_account", "switch_account", "set_skin", "launch_game" };

	[Fact]
	public void ExemptWritesDoNotClaimSnapshots()
	{
		foreach (string name in ExemptWrites)
		{
			Assert.Equal("exempt", Tools.SnapshotCoverage(name));
			Assert.Empty(Tools.AffectedPaths(name, PyDict.Of("instance", InstName), InstName));
			// 「不可回退」时 UI 得说清:理由 + 那句给用户看的话都要有
			Assert.NotEmpty(Tools.NoSnapshotReason(name));
			Assert.Contains("这一步无法回退", Tools.NoRollbackMessage(name));
		}
	}

	[Fact]
	public void ReadOnlyToolsHaveNeither() 
	{
		Assert.Equal("none", Tools.SnapshotCoverage("list_mods"));
		Assert.Equal("", Tools.NoRollbackMessage("list_mods"));
		// 有快照的工具不该报「无法回退」
		Assert.Equal("", Tools.NoRollbackMessage("install_mod"));
		Assert.Equal("snapshot", Tools.SnapshotCoverage("restore_save_backup"));
	}

	[Fact]
	public void EveryWriteToolIsCoveredOrExplicitlyExempt()
	{
		List<string> writes = ToolMeta.Map.Values
			.Where(m => !m.@readonly)
			.Select(m => m.name)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();
		Assert.NotEmpty(writes);

		List<string> uncovered = writes.Where(n => Tools.SnapshotCoverage(n) == "none").ToList();
		Assert.Empty(uncovered);
	}

	[Fact]
	public void EveryRegisteredSnapshotToolReturnsPaths()
	{
		// 反向锁:登记成「有快照」的工具必须真能算出路径 —— 只登记不实现(或拼错名字)在这里就红。
		// 参数用各工具最简的合法入参;path / filename 这类必须给的参数照 schema 给。
		Dictionary<string, PyDict> cases = new Dictionary<string, PyDict>(StringComparer.Ordinal)
		{
			{ "write_mod_config", PyDict.Of("instance", InstName, "path", "x.toml") },
			{ "delete_mod", PyDict.Of("instance", InstName, "filename", "a.jar") },
			{ "enable_mod", PyDict.Of("instance", InstName, "filename", "a.jar") },
			{ "disable_mod", PyDict.Of("instance", InstName, "filename", "a.jar") },
			{ "delete_instance", PyDict.Of("name", "d3other") },
			{ "create_instance", PyDict.Of("instance", InstName, "name", "d3new") },
			{ "install_mod", PyDict.Of("instance", InstName) },
			{ "install_shader", PyDict.Of("instance", InstName) },
			{ "install_resourcepack", PyDict.Of("instance", InstName) },
			{ "install_datapack", PyDict.Of("instance", InstName) },
			{ "install_world", PyDict.Of("instance", InstName) },
			{ "install_modpack", PyDict.Of("instance", InstName) },
			{ "install_game", PyDict.Of("instance", InstName) },
			{ "download_java", PyDict.Of("instance", InstName) },
			{ "fs_write", PyDict.Of("instance", InstName, "path", "docs/a.txt", "content", "hi") },
			{ "tune_jvm", PyDict.Of("instance", InstName, "apply", true) },
			{ "restore_save_backup", PyDict.Of("instance", InstName, "backup_name", "world-20261128-101010.zip") },
			{ LayoutTools.ApplyName, new PyDict() }
		};
		foreach (KeyValuePair<string, PyDict> kv in cases)
		{
			Assert.Equal("snapshot", Tools.SnapshotCoverage(kv.Key));
			Assert.NotEmpty(Tools.AffectedPaths(kv.Key, kv.Value, InstName));
		}
		// 双向:注册表里所有「有快照」的工具都在这份参数表里 —— 表里多一个少一个都红
		List<string> registered = ToolMeta.Map.Values
			.Select(m => m.name)
			.Where(n => Tools.SnapshotCoverage(n) == "snapshot")
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();
		Assert.Equal(cases.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList(), registered);
	}
}
