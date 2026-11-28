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
/// AI5-D4:确认卡的「改前改后」预览覆盖 + AI5-D3 的「这一步无法回退」提示。
///
/// 复现的缺陷:Preview.ChangePreview 里没有 fs_write / tune_jvm / restore_save_backup /
/// layout_apply 的分支 —— 这四个写工具点「允许」之前什么也看不到。这里钉三件事:
/// <list type="number">
/// <item>四个工具都有预览,而且给的是真东西(逐行 diff / 目标存档 / 全局设置改前改后);</item>
/// <item>没有专属预览的写工具都在显式豁免表里,并且卡上still有一句「这一步无法回退」;</item>
/// <item>ToolMeta 里每个写工具都必须「有预览」或「在豁免表里」,豁免表本身也被锁住。</item>
/// </list>
/// 预览只算不写:这里所有调用都不得改盘(用 TestHome 隔离,顺便验证它没写)。
/// </summary>
public class PreviewCoverageTests : IDisposable
{
	private const string InstName = "d4test";

	private readonly TestHome _home = new TestHome();

	private readonly BackendApi _api;

	private readonly string _inst;

	private readonly string? _layoutDir;

	public PreviewCoverageTests()
	{
		_api = new BackendApi(new EventBus());
		_inst = Instances.GetInstancePath(InstName);
		Directory.CreateDirectory(PPath.Join(_inst, "mods"));
		Directory.CreateDirectory(PPath.Join(_inst, "saves"));
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

	private static List<string> Lines(PyDict preview)
	{
		Assert.NotNull(preview);
		return Py.ListOr(preview.Get("lines")).Select(Py.S).ToList();
	}

	// ───────────────────── AI5-D4:四个新分支 ─────────────────────

	[Fact]
	public void FsWritePreviewShowsLineDiff()
	{
		PyDict preview = Preview.ChangePreview(_api, "fs_write", PyDict.Of("instance", InstName, "path", "config/opts.txt", "content", "a=2\nb=3\n"));
		Assert.Equal("diff", Py.S(preview.Get("kind")));
		List<string> lines = Lines(preview);
		// 新建文件:一句说明 + 目标绝对路径 + 逐行 diff
		Assert.Contains(new PPath(PPath.Join(_inst, "config", "opts.txt")).Abs(), lines);
		Assert.Contains("+a=2", lines);
		Assert.Contains("+b=3", lines);
	}

	[Fact]
	public void FsWritePreviewStaysSilentForRejectedPaths()
	{
		// 越界路径:执行时会被守卫拒掉,预览也不该编一份出来
		Assert.Null(Preview.ChangePreview(_api, "fs_write", PyDict.Of("instance", InstName, "path", "C:/Windows/system32/x.txt", "content", "x")));
		Assert.Null(Preview.ChangePreview(_api, "fs_write", PyDict.Of("instance", InstName, "path", "../escape.txt", "content", "x")));
	}

	[Fact]
	public void TuneJvmPreviewShowsGlobalSettingsChange()
	{
		PyDict preview = Preview.ChangePreview(_api, "tune_jvm", PyDict.Of("instance", InstName, "apply", true));
		Assert.Equal("diff", Py.S(preview.Get("kind")));
		List<string> lines = Lines(preview);
		// 写入目标必须和检查点/AffectedPaths 是同一个文件
		Assert.Contains(lines, l => l.Contains(Config.CONFIG_FILE));
		// 改前(=全局默认)与改后各一行:内存 + GC,一共 2 减 2 加
		Assert.Equal(2, lines.Count(l => l.StartsWith("- ")));
		Assert.Equal(2, lines.Count(l => l.StartsWith("+ ")));
		long cur = Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L));
		Assert.Contains(lines, l => l.StartsWith("- ") && l.Contains(cur.ToString() + " MB"));
		long totalMb = 0L;
		try
		{
			totalMb = Py.Int(Py.Or(Sysinfo.MemoryInfo().Get("total_mb"), 0L));
		}
		catch (Exception)
		{
			totalMb = 0L;
		}
		// 实例里没有模组 → 推荐值只跟电脑内存有关(与预览同一份实现)
		Assert.Contains(lines, l => l.StartsWith("+ ") && l.Contains(ExtraTools.RecommendMemory(totalMb, 0).ToString() + " MB"));
	}

	[Fact]
	public void TuneJvmPreviewSaysItWillNotChangeAnything()
	{
		// apply=false:只是建议,卡上要说清不会动设置
		PyDict preview = Preview.ChangePreview(_api, "tune_jvm", PyDict.Of("instance", InstName));
		Assert.Equal("info", Py.S(preview.Get("kind")));
		Assert.DoesNotContain(Lines(preview), l => l.Contains(Config.CONFIG_FILE));
	}

	[Fact]
	public void RestoreSaveBackupPreviewShowsTargetWorld()
	{
		Directory.CreateDirectory(PPath.Join(_inst, "saves", "world"));
		PyDict preview = Preview.ChangePreview(_api, "restore_save_backup", PyDict.Of("instance", InstName, "backup_name", "world-20261128-101010.zip", "overwrite", true));
		Assert.Equal("summary", Py.S(preview.Get("kind")));
		List<string> lines = Lines(preview);
		// 目标路径与 Tools.AffectedPaths 同源(检查点拍的就是这一个目录)
		Assert.Equal(new List<string> { PPath.Join(_inst, "saves", "world") }, Tools.AffectedPaths("restore_save_backup", PyDict.Of("instance", InstName, "backup_name", "world-20261128-101010.zip", "overwrite", true), InstName));
		Assert.Contains(lines, l => l.Contains("world"));
		// 备份文件不在(用例没造 zip):照实说、别假装能回滚;覆盖模式还要说清先备份当前存档
		Assert.Contains(lines, l => l.Contains("没找到这个备份"));
		Assert.Contains(lines, l => l.Contains("回滚前先把当前存档"));
		Assert.Equal(5, lines.Count);
	}

	[Fact]
	public void LayoutApplyPreviewDiffsTheLayoutDocument()
	{
		PyDict preview = Preview.ChangePreview(_api, "layout_apply", PyDict.Of("shell_tabs", Py.L("launch", "settings")));
		Assert.Equal("diff", Py.S(preview.Get("kind")));
		List<string> lines = Lines(preview);
		Assert.Contains("- 顶栏页签 → launch / settings", lines);
		Assert.Contains("--- a/ui_layout_v2.json", lines);
		Assert.Contains("+++ b/ui_layout_v2.json", lines);
	}

	[Fact]
	public void LayoutApplyPreviewReportsUnusableInput()
	{
		// 参数认不出来:说清「这次改动用不了」,而不是显示一份空的 diff
		PyDict preview = Preview.ChangePreview(_api, "layout_apply", new PyDict());
		Assert.Equal("info", Py.S(preview.Get("kind")));
		Assert.NotEmpty(Lines(preview));
	}

	// ───────────────────── AI5-D3:不可回退时的提示 ─────────────────────

	[Fact]
	public void ExemptWritesTellTheUserItCannotBeRolledBack()
	{
		foreach (string name in new string[6] { "backup_save", "multiplayer", "add_offline_account", "switch_account", "set_skin", "launch_game" })
		{
			PyDict preview = Preview.ChangePreview(_api, name, PyDict.Of("instance", InstName));
			Assert.NotNull(preview);
			string note = Tools.NoRollbackMessage(name);
			Assert.NotEmpty(note);
			Assert.Contains(note, Lines(preview));
		}
	}

	[Fact]
	public void SnapshotToolsDoNotShowRollbackWarning()
	{
		PyDict preview = Preview.ChangePreview(_api, "delete_mod", PyDict.Of("instance", InstName, "filename", "a.jar"));
		Assert.DoesNotContain(Lines(preview), l => l.Contains(Tools.NoRollbackMessage("backup_save")));
	}

	[Fact]
	public void RollbackNoteIsWiredToTheGlossary()
	{
		// 新文案必须接线:英文词表里有这条 key,Tr(key,"en") 才不会原样返回 key
		Assert.NotEqual("这一步无法回退:{reason}", I18nCore.Tr("这一步无法回退:{reason}", "en"));
		Assert.NotEqual(Tools.NoSnapshotReason("backup_save"), I18nCore.Tr(Tools.NoSnapshotReason("backup_save"), "en"));
	}

	// ───────────────────── D4 验收:覆盖率 + 豁免表 ─────────────────────

	/// <summary>没有专属预览分支、走显式豁免的写工具(表里多一个少一个,用例就红)。</summary>
	private static readonly string[] ExemptFromPreview = new string[7]
	{
		"add_offline_account", "backup_save", "forget", "multiplayer", "remember", "set_skin", "switch_account"
	};

	[Fact]
	public void TheFourNewToolsHaveBuiltinPreviews()
	{
		Assert.True(Preview.HasBuiltinPreview("fs_write"));
		Assert.True(Preview.HasBuiltinPreview("tune_jvm"));
		Assert.True(Preview.HasBuiltinPreview("restore_save_backup"));
		Assert.True(Preview.HasBuiltinPreview(LayoutTools.ApplyName));
	}

	[Fact]
	public void EveryWriteToolHasPreviewOrExplicitExemption()
	{
		List<string> writes = ToolMeta.Map.Values.Where(m => !m.@readonly).Select(m => m.name).ToList();
		Assert.NotEmpty(writes);
		List<string> missing = writes.Where(n => !Preview.HasBuiltinPreview(n) && Preview.NoPreviewReason(n).Length == 0).ToList();
		Assert.Empty(missing);
	}

	[Fact]
	public void ExemptionTableIsLocked()
	{
		// 豁免表里的每一条都要有理由
		Assert.All(ExemptFromPreview, n => Assert.NotEmpty(Preview.NoPreviewReason(n)));
		// 反过来:今天的写工具里「没有专属预览」的那些 == 豁免表 ∩ 写工具(多写少写都红)
		List<string> writes = ToolMeta.Map.Values.Where(m => !m.@readonly).Select(m => m.name).ToList();
		List<string> exemptWrites = writes.Where(n => Preview.NoPreviewReason(n).Length > 0).OrderBy(n => n, StringComparer.Ordinal).ToList();
		List<string> expected = ExemptFromPreview.Where(n => writes.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
		Assert.Equal(expected, exemptWrites);
		// 豁免的工具不能在「有预览」那张表里(两张表不许重叠)
		Assert.All(ExemptFromPreview, n => Assert.False(Preview.HasBuiltinPreview(n)));
	}

	[Fact]
	public void PreviewNeverWritesToDisk()
	{
		// 预览是纯计算:跑完一圈,磁盘上不该多出任何文件(目录是 Tools._inst 的既有行为,不算)
		Preview.ChangePreview(_api, "fs_write", PyDict.Of("instance", InstName, "path", "config/new.txt", "content", "x"));
		Preview.ChangePreview(_api, "tune_jvm", PyDict.Of("instance", InstName, "apply", true));
		Preview.ChangePreview(_api, "restore_save_backup", PyDict.Of("instance", InstName, "backup_name", "world-20261128-101010.zip", "overwrite", true));
		Preview.ChangePreview(_api, "layout_apply", PyDict.Of("shell_tabs", Py.L("launch")));
		Assert.False(File.Exists(PPath.Join(_inst, "config", "new.txt")));
		Assert.Empty(Directory.GetFiles(PPath.Join(_inst, "saves")));
		Assert.Empty(Directory.GetFiles(PPath.Join(_inst, "config")));
	}
}
