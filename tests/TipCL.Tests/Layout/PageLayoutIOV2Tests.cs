// R32-LIN-IO 新增:布局导出/导入 v2 的回归闸门(对应 progress/R32-ROUND2-SPEC.md §2.3.3 的验收清单)。
//
// 这一批用例的存在意义(见 R32-PLAN R32-D25):「改回 v1 必须变红」。
//   * 导出侧:解析产物必须是 kind=tipcl-layout / version=2,且 mode / cols / x-y-w-h / style /
//     pad / span / min_h / z / splits / shell.density / shell.tabs / profiles / widgets **逐字段相等**;
//     旧 v1 实现只写 {id,hidden},`LayoutV2.FromJsonText` 会直接返回 null ⇒ 第一条断言就红。
//   * 导入侧:v2 文件必须真的落进 v2 真源(盘上 + 内存),旧实现走 PageLayoutStore.ParseImport,
//     对 v2 文件必抛「布局文件无效」⇒ ImportFromFileDialog 返回 null ⇒ 红。
//   * v1 文件在 v2 已存在时:只能并顺序+显隐,其它 v2 字段按 id 保留,且 v1 读取路径立刻可见
//     (这一条同时钉住 ImportFromFileDialog 里的 PageLayoutStore.Reload())。
//   * D-02:LayoutEditor.Persist() 里的 PageLayoutStore.Reload() 被移除后,
//     PersistRefreshesV1Cache... 会拿到预热时的旧快照 ⇒ 红。
//
// 所有落盘用例一律 DirOverride 到独立临时目录(绝不碰真实用户目录),收尾必还原并删目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using TipCL.Tests.App;
using Xunit;
using ToastKind = TipCL.Core.Layout.ToastKind;

/// <summary>导出 = 整份 v2;导入 = v2 整条覆盖 / v1 只并顺序与显隐。</summary>
public class PageLayoutIOV2Tests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_linio_" + Guid.NewGuid().ToString("N"));

	private void UseDir()
	{
		Directory.CreateDirectory(_tmp);
		LayoutStoreV2.DirOverride = _tmp;
		// 与 App 启动同装配(生产实现:SaveV1 只写文件,不刷 PageLayoutStore 的进程内快照)
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
	}

	private void Release()
	{
		PageLayoutIO.SaveFileDialog = null;
		PageLayoutIO.PickFileDialog = null;
		PageLayoutIO.Toast = null;
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
		try
		{
			Directory.Delete(_tmp, true);
		}
		catch (Exception)
		{
		}
	}

	private string V2Path
	{
		get { return Path.Combine(_tmp, LayoutStoreV2.FileName); }
	}

	private string V1Path
	{
		get { return Path.Combine(_tmp, LayoutStoreV2.V1FileName); }
	}

	private sealed class ToastRecord
	{
		public string Title = "";
		public string Detail = "";
		public ToastKind Kind;
	}

	private List<ToastRecord> CaptureToasts()
	{
		List<ToastRecord> list = new List<ToastRecord>();
		PageLayoutIO.Toast = (title, detail, kind) => list.Add(new ToastRecord
		{
			Title = title,
			Detail = detail,
			Kind = kind
		});
		return list;
	}

	// ————— 测试文档:故意把 v1 根本装不下的字段全部填上非默认值 —————

	private static LayoutV2 RichDocument()
	{
		LayoutV2 doc = new LayoutV2
		{
			// 方案名必须存在于 Profiles 里,否则 Sanitize 会把它当"悬空方案"清空(与 Windows 同语义)
			ActiveProfile = "备用"
		};
		doc.Layout.Shell.Density = LayoutSchema.DensityCompact;
		doc.Layout.Shell.Tabs = new List<string> { "launch", "library" };
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 6,
			RowH = 64.0,
			Gap = 12.0,
			Pad = new double[4] { 10.0, 11.0, 12.0, 13.0 }
		};
		page.Blocks.Add(new BlockLayout
		{
			Id = "folders",
			X = 0.0,
			Y = 0.0,
			W = 2.0,
			H = 2.0,
			Span = 2,
			Style = LayoutSchema.StyleCard,
			Title = true,
			Pad = new double[4] { 4.0, 5.0, 6.0, 7.0 },
			MinH = 120.0,
			Z = 0
		});
		page.Blocks.Add(new BlockLayout
		{
			Id = "versions",
			X = 2.0,
			Y = 0.0,
			W = 4.0,
			H = 3.0,
			Hidden = true,
			Style = LayoutSchema.StyleDivider,
			Z = 1
		});
		page.Blocks.Add(new BlockLayout
		{
			Id = "custom:w-clock",
			X = 2.0,
			Y = 3.0,
			W = 2.0,
			H = 1.0,
			Z = 2
		});
		doc.Layout.Pages["library:main"] = page;
		SplitState splits = new SplitState();
		splits.Ratios["folders"] = 0.35;
		doc.Layout.Splits["library:main"] = splits;
		WidgetDef widget = new WidgetDef
		{
			Id = "w-clock",
			Type = "clock",
			Title = "时钟"
		};
		widget.Settings["format"] = "HH:mm";
		doc.Layout.Widgets.Add(widget);
		PageLayoutV2 profilePage = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Gap = 6.0
		};
		profilePage.Blocks.Add(new BlockLayout
		{
			Id = "header",
			Hidden = true,
			Style = LayoutSchema.StyleCard
		});
		LayoutBody profile = new LayoutBody();
		profile.Pages["tasks"] = profilePage;
		doc.Profiles["备用"] = profile;
		return doc;
	}

	// ————— 逐字段断言(不是「能解析就行」) —————

	private static void AssertPageFields(PageLayoutV2 expected, PageLayoutV2 actual, string where)
	{
		Assert.True(expected.Version == actual.Version, where + " version: " + expected.Version + " vs " + actual.Version);
		Assert.True(expected.Mode == actual.Mode, where + " mode: " + expected.Mode + " vs " + actual.Mode);
		Assert.True(expected.Cols == actual.Cols, where + " cols: " + expected.Cols + " vs " + actual.Cols);
		Assert.True(Near(expected.RowH, actual.RowH), where + " row_h: " + expected.RowH + " vs " + actual.RowH);
		Assert.True(Near(expected.Gap, actual.Gap), where + " gap: " + expected.Gap + " vs " + actual.Gap);
		Assert.True(SamePad(expected.Pad, actual.Pad), where + " pad 不一致");
		Assert.True(expected.Blocks.Count == actual.Blocks.Count, where + " 块数: " + expected.Blocks.Count + " vs " + actual.Blocks.Count);
		for (int i = 0; i < expected.Blocks.Count && i < actual.Blocks.Count; i++)
		{
			BlockLayout e = expected.Blocks[i];
			BlockLayout a = actual.Blocks[i];
			string at = where + " 块#" + i + "(" + e.Id + ")";
			Assert.True(e.Id == a.Id, at + " id: " + e.Id + " vs " + a.Id);
			Assert.True(e.Hidden == a.Hidden, at + " hidden: " + e.Hidden + " vs " + a.Hidden);
			Assert.True(Near(e.X, a.X), at + " x: " + e.X + " vs " + a.X);
			Assert.True(Near(e.Y, a.Y), at + " y: " + e.Y + " vs " + a.Y);
			Assert.True(Near(e.W, a.W), at + " w: " + e.W + " vs " + a.W);
			Assert.True(Near(e.H, a.H), at + " h: " + e.H + " vs " + a.H);
			Assert.True(e.Span == a.Span, at + " span: " + e.Span + " vs " + a.Span);
			Assert.True(e.Style == a.Style, at + " style: " + e.Style + " vs " + a.Style);
			Assert.True(e.Title == a.Title, at + " title: " + e.Title + " vs " + a.Title);
			Assert.True(SamePad(e.Pad, a.Pad), at + " pad 不一致");
			Assert.True(Near(e.MinH, a.MinH), at + " min_h: " + e.MinH + " vs " + a.MinH);
			Assert.True(e.Z == a.Z, at + " z: " + e.Z + " vs " + a.Z);
		}
	}

	private static bool Near(double a, double b)
	{
		return Math.Abs(a - b) < 1e-6;
	}

	private static bool SamePad(double[]? a, double[]? b)
	{
		if (a == null || b == null)
		{
			return a == null && b == null;
		}
		return a.SequenceEqual(b);
	}

	// ————— 1. 导出:v2 整份文档,字段逐项无损 —————

	[Fact]
	public void ExportWritesWholeV2DocumentWithEveryField()
	{
		UseDir();
		try
		{
			LayoutV2 doc = RichDocument();
			LayoutStoreV2.Save(doc);
			string? suggested = null;
			string exportPath = Path.Combine(_tmp, "out.json");
			PageLayoutIO.SaveFileDialog = (filter, name) =>
			{
				suggested = name;
				return exportPath;
			};
			List<ToastRecord> toasts = CaptureToasts();

			PageLayoutIO.ExportToFileDialog("library:main");

			Assert.Equal("library-main.json", suggested);
			Assert.Single(toasts);
			Assert.Equal(ToastKind.Success, toasts[0].Kind);
			Assert.True(File.Exists(exportPath));
			byte[] bytes = File.ReadAllBytes(exportPath);
			Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "导出的文件不应带 UTF-8 BOM");

			string text = File.ReadAllText(exportPath);
			// 旧 v1 实现的指纹:kind=tipcl-page-layout / version=1 —— 这里必须一条都不出现
			Assert.DoesNotContain("tipcl-page-layout", text);
			Assert.Contains("\"kind\": \"tipcl-layout\"", text);
			Assert.Contains("\"version\": 2", text);

			LayoutV2? back = LayoutV2.FromJsonText(text);
			Assert.NotNull(back);
			LayoutV2 parsed = back!;
			Assert.Equal(2, parsed.Version);
			Assert.Equal(LayoutSchema.Kind, parsed.Kind);
			// 方案(profiles)与当前方案名
			Assert.Equal("备用", parsed.ActiveProfile);
			Assert.True(parsed.Profiles.ContainsKey("备用"));
			AssertPageFields(doc.Profiles["备用"].Pages["tasks"], parsed.Profiles["备用"].Pages["tasks"], "profiles[备用].tasks");
			// 外壳
			Assert.Equal(LayoutSchema.DensityCompact, parsed.Layout.Shell.Density);
			Assert.Equal(new[] { "launch", "library" }, parsed.Layout.Shell.Tabs.ToArray());
			// 页面 + 每块几何/样式
			Assert.True(parsed.Layout.Pages.ContainsKey("library:main"));
			AssertPageFields(doc.Layout.Pages["library:main"], parsed.Layout.Pages["library:main"], "library:main");
			// 分栏
			Assert.Equal(0.35, parsed.Layout.Splits["library:main"].Ratios["folders"], 6);
			// 自建区块定义
			WidgetDef def = Assert.Single(parsed.Layout.Widgets);
			Assert.Equal("w-clock", def.Id);
			Assert.Equal("clock", def.Type);
			Assert.Equal("时钟", def.Title);
			Assert.Equal("HH:mm", def.Settings["format"]?.ToString());
		}
		finally
		{
			Release();
		}
	}

	// 导出读的是 v2 真源,两次导出必须都反映最新状态(「先导出→再改→再导出」不会拿到旧内容)。
	[Fact]
	public void ExportReflectsLatestV2StateOnEveryCall()
	{
		UseDir();
		try
		{
			LayoutV2 doc = RichDocument();
			LayoutStoreV2.Save(doc);
			string first = Path.Combine(_tmp, "first.json");
			string second = Path.Combine(_tmp, "second.json");
			string target = first;
			PageLayoutIO.SaveFileDialog = (filter, name) => target;
			PageLayoutIO.Toast = (title, detail, kind) => { };

			PageLayoutIO.ExportToFileDialog("library:main");
			Assert.False(LayoutV2.FromJsonText(File.ReadAllText(first))!.Layout.Pages["library:main"].Blocks[0].Title == false);

			// 改一次真源(等价于编辑器里点「隐藏」后落盘)
			LayoutV2 changed = LayoutStoreV2.Load();
			changed.Layout.Pages["library:main"].Blocks[0].Title = false;
			changed.Layout.Pages["library:main"].Blocks[0].Hidden = true;
			LayoutStoreV2.Save(changed);

			target = second;
			PageLayoutIO.ExportToFileDialog("library:main");
			LayoutV2 reread = LayoutV2.FromJsonText(File.ReadAllText(second))!;
			Assert.False(reread.Layout.Pages["library:main"].Blocks[0].Title);
			Assert.True(reread.Layout.Pages["library:main"].Blocks[0].Hidden);
		}
		finally
		{
			Release();
		}
	}

	// ————— 2. 导入 v2:盘上 + 内存都变成文件里的值 —————

	[Fact]
	public void ImportV2DocumentOverwritesPagesOnDiskAndInMemory()
	{
		UseDir();
		try
		{
			// 现状:v2 已存在,tasks 是 stack 单块、另有一页 other
			LayoutV2 seed = new LayoutV2();
			PageLayoutV2 oldTasks = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeStack
			};
			oldTasks.Blocks.Add(new BlockLayout { Id = "header", Z = 0 });
			seed.Layout.Pages["tasks"] = oldTasks;
			PageLayoutV2 other = new PageLayoutV2();
			other.Blocks.Add(new BlockLayout { Id = "only", Z = 0 });
			seed.Layout.Pages["other"] = other;
			LayoutStoreV2.Save(seed);

			// 导入文件:同一页整条换成 grid 6 列 + 几何 + 变体 + 自建块定义
			LayoutV2 incoming = new LayoutV2();
			PageLayoutV2 newTasks = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeGrid,
				Cols = 6,
				RowH = 48.0,
				Gap = 14.0,
				Pad = new double[4] { 20.0, 21.0, 22.0, 23.0 }
			};
			newTasks.Blocks.Add(new BlockLayout
			{
				Id = "list",
				X = 2.0,
				Y = 1.0,
				W = 3.0,
				H = 2.0,
				Style = LayoutSchema.StyleCard,
				Title = true,
				Z = 0
			});
			newTasks.Blocks.Add(new BlockLayout
			{
				Id = "header",
				X = 0.0,
				Y = 0.0,
				W = 6.0,
				H = 1.0,
				Hidden = true,
				Z = 1
			});
			newTasks.Blocks.Add(new BlockLayout
			{
				Id = "custom:w-note",
				X = 5.0,
				Y = 3.0,
				W = 1.0,
				H = 1.0,
				Z = 2
			});
			incoming.Layout.Pages["tasks"] = newTasks;
			PageLayoutV2 narrowPage = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeStack
			};
			narrowPage.Blocks.Add(new BlockLayout { Id = "list", Z = 0 });
			LayoutBody narrow = new LayoutBody();
			narrow.Pages["tasks"] = narrowPage;
			incoming.Layout.Variants["narrow"] = narrow;
			incoming.Layout.Widgets.Add(new WidgetDef
			{
				Id = "w-note",
				Type = "note",
				Title = "便签"
			});
			string importPath = Path.Combine(_tmp, "import-v2.json");
			File.WriteAllText(importPath, LayoutStoreV2.ExportJson(incoming));

			// 预热 v1 读取缓存(旧实现下 v2 永远读不到;新实现导入后必须刷新)
			PageLayoutDoc? before = PageLayoutStore.Load("tasks");
			Assert.NotNull(before);
			Assert.Equal(new[] { "header" }, before!.Blocks.Select((BlockState b) => b.Id).ToArray());

			List<ToastRecord> toasts = CaptureToasts();
			PageLayoutIO.PickFileDialog = filter => importPath;

			int? count = PageLayoutIO.ImportFromFileDialog();

			Assert.Equal(1, count);
			Assert.Single(toasts);
			Assert.Equal(ToastKind.Success, toasts[0].Kind);

			// 盘上:v2 文件里的 tasks 就是导入文件里的那一页
			string raw = File.ReadAllText(V2Path);
			LayoutV2? disk = LayoutV2.FromJsonText(raw);
			Assert.NotNull(disk);
			AssertPageFields(newTasks, disk!.Layout.Pages["tasks"], "disk.tasks");
			Assert.Equal("narrow", Assert.Single(disk.Layout.Variants.Keys.ToArray()));
			AssertPageFields(narrowPage, disk.Layout.Variants["narrow"].Pages["tasks"], "disk.variants.narrow.tasks");
			Assert.Contains(disk.Layout.Widgets, (WidgetDef w) => w.Id == "w-note" && w.Type == "note");

			// 内存(LayoutStoreV2.Load 的副本)
			LayoutV2 mem = LayoutStoreV2.Load();
			AssertPageFields(newTasks, mem.Layout.Pages["tasks"], "mem.tasks");
			Assert.Contains(mem.Layout.Widgets, (WidgetDef w) => w.Id == "w-note");
			// 文件里没有的页不许被动
			Assert.True(mem.Layout.Pages.ContainsKey("other"));
			Assert.Equal("only", Assert.Single(mem.Layout.Pages["other"].Blocks).Id);

			// v1 读取路径立刻看到写通后的新顺序(Reload 生效)
			PageLayoutDoc? v1 = PageLayoutStore.Load("tasks");
			Assert.NotNull(v1);
			Assert.Equal(new[] { "list", "header", "custom:w-note" }, v1!.Blocks.Select((BlockState b) => b.Id).ToArray());
		}
		finally
		{
			Release();
		}
	}

	// ————— 3. 导入 v1:只并顺序/显隐,v2 其它字段按 id 保留(既定行为) —————

	[Fact]
	public void ImportV1FileMergesOrderAndHiddenIntoExistingV2()
	{
		UseDir();
		try
		{
			LayoutV2 seed = new LayoutV2();
			PageLayoutV2 page = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeGrid,
				Cols = 6,
				Gap = 16.0
			};
			page.Blocks.Add(new BlockLayout
			{
				Id = "a",
				X = 2.0,
				W = 3.0,
				Style = LayoutSchema.StyleCard,
				Title = true,
				Z = 0
			});
			page.Blocks.Add(new BlockLayout
			{
				Id = "b",
				X = 0.0,
				Style = LayoutSchema.StyleDivider,
				Z = 1
			});
			seed.Layout.Pages["tasks"] = page;
			LayoutStoreV2.Save(seed);
			PageLayoutStore.Reload();
			Assert.False(PageLayoutStore.Load("tasks")!.Blocks.Single((BlockState x) => x.Id == "a").Hidden);

			// v1 文件:b 排到最前;a 被隐藏
			string v1Json = "{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{\"tasks\":{\"version\":1,"
				+ "\"blocks\":[{\"id\":\"b\",\"hidden\":false},{\"id\":\"a\",\"hidden\":true}]}}}";
			string importPath = Path.Combine(_tmp, "import-v1.json");
			File.WriteAllText(importPath, v1Json);

			List<ToastRecord> toasts = CaptureToasts();
			PageLayoutIO.PickFileDialog = filter => importPath;

			int? count = PageLayoutIO.ImportFromFileDialog();

			Assert.Equal(1, count);
			Assert.Equal(ToastKind.Success, Assert.Single(toasts).Kind);

			// v2 真源:顺序/显隐按 v1,其它字段按 id 保留,页级参数不动
			LayoutV2 mem = LayoutStoreV2.Load();
			PageLayoutV2 merged = mem.Layout.Pages["tasks"];
			Assert.Equal(new[] { "b", "a" }, merged.Blocks.Select((BlockLayout x) => x.Id).ToArray());
			Assert.Equal(LayoutSchema.ModeGrid, merged.Mode);
			Assert.Equal(6, merged.Cols);
			Assert.Equal(16.0, merged.Gap, 6);
			BlockLayout blockA = merged.Blocks[1];
			Assert.True(blockA.Hidden);
			Assert.Equal(2.0, blockA.X, 6);
			Assert.Equal(3.0, blockA.W, 6);
			Assert.Equal(LayoutSchema.StyleCard, blockA.Style);
			Assert.True(blockA.Title);
			Assert.Equal(LayoutSchema.StyleDivider, merged.Blocks[0].Style);
			Assert.Equal(0, merged.Blocks[0].Z);
			Assert.Equal(1, merged.Blocks[1].Z);

			// 盘上的 v2 与内存一致(导入真的落盘了,不是只在内存里)
			LayoutV2 disk = LayoutV2.FromJsonText(File.ReadAllText(V2Path))!;
			Assert.Equal(new[] { "b", "a" }, disk.Layout.Pages["tasks"].Blocks.Select((BlockLayout x) => x.Id).ToArray());

			// v1 读取路径也立刻看到合并后的顺序(旧实现:写 v1 文件但不碰 v2,v2 仍是最初的 [a,b])
			PageLayoutDoc? v1 = PageLayoutStore.Load("tasks");
			Assert.NotNull(v1);
			Assert.Equal(new[] { "b", "a" }, v1!.Blocks.Select((BlockState x) => x.Id).ToArray());
			Assert.True(v1.Blocks[1].Hidden);
		}
		finally
		{
			Release();
		}
	}

	// ————— 4. 坏文件 / 空文件:一句 Toast,不动真源 —————

	[Fact]
	public void InvalidFileReportsErrorAndLeavesStoreUntouched()
	{
		UseDir();
		try
		{
			LayoutV2 seed = new LayoutV2();
			PageLayoutV2 page = new PageLayoutV2();
			page.Blocks.Add(new BlockLayout { Id = "a", Z = 0 });
			seed.Layout.Pages["tasks"] = page;
			LayoutStoreV2.Save(seed);
			string before = File.ReadAllText(V2Path);

			string importPath = Path.Combine(_tmp, "broken.json");
			File.WriteAllText(importPath, "{ this is not json");
			List<ToastRecord> toasts = CaptureToasts();
			PageLayoutIO.PickFileDialog = filter => importPath;

			int? count = PageLayoutIO.ImportFromFileDialog();

			Assert.Null(count);
			ToastRecord toast = Assert.Single(toasts);
			Assert.Equal(ToastKind.Error, toast.Kind);
			Assert.Equal(I18nCore.Tr("导入失败"), toast.Title);
			Assert.Equal(before, File.ReadAllText(V2Path));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void V2FileWithoutPagesReportsWarningAndDoesNotSave()
	{
		UseDir();
		try
		{
			LayoutV2 seed = new LayoutV2();
			PageLayoutV2 page = new PageLayoutV2();
			page.Blocks.Add(new BlockLayout { Id = "a", Z = 0 });
			seed.Layout.Pages["tasks"] = page;
			LayoutStoreV2.Save(seed);
			string before = File.ReadAllText(V2Path);

			string importPath = Path.Combine(_tmp, "empty-v2.json");
			File.WriteAllText(importPath, "{\"version\":2,\"kind\":\"tipcl-layout\",\"layout\":{\"pages\":{}}}");
			List<ToastRecord> toasts = CaptureToasts();
			PageLayoutIO.PickFileDialog = filter => importPath;

			int? count = PageLayoutIO.ImportFromFileDialog();

			Assert.Null(count);
			ToastRecord toast = Assert.Single(toasts);
			Assert.Equal(ToastKind.Warning, toast.Kind);
			Assert.Equal(I18nCore.Tr("文件里没有可导入的页面"), toast.Title);
			Assert.Equal(before, File.ReadAllText(V2Path));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void CancellingEitherDialogDoesNothingAndSaysNothing()
	{
		UseDir();
		try
		{
			LayoutV2 seed = new LayoutV2();
			LayoutStoreV2.Save(seed);
			string before = File.ReadAllText(V2Path);
			List<ToastRecord> toasts = CaptureToasts();
			PageLayoutIO.PickFileDialog = filter => null;
			PageLayoutIO.SaveFileDialog = (filter, name) => null;

			Assert.Null(PageLayoutIO.ImportFromFileDialog());
			PageLayoutIO.ExportToFileDialog("tasks");

			Assert.Empty(toasts);
			Assert.Equal(before, File.ReadAllText(V2Path));
		}
		finally
		{
			Release();
		}
	}

	// ————— 5. D-02:Persist() 恢复 PageLayoutStore.Reload() 之后,v1 读取路径不再拿到过期缓存 —————

	[Fact]
	public void PersistRefreshesV1CacheSoTheNextV1ReadIsFresh()
	{
		UseDir();
		try
		{
			LayoutV2 seed = new LayoutV2();
			PageLayoutV2 page = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeStack
			};
			page.Blocks.Add(new BlockLayout { Id = "header", Z = 0 });
			page.Blocks.Add(new BlockLayout { Id = "list", Z = 1 });
			seed.Layout.Pages["tasks"] = page;
			LayoutStoreV2.Save(seed);
			PageLayoutStore.Reload();

			// 预热 v1 缓存:此刻 list 未隐藏(这一份就是"没有 Reload 时会一直返回的旧快照")
			PageLayoutDoc? warm = PageLayoutStore.Load("tasks");
			Assert.NotNull(warm);
			Assert.False(warm!.Blocks.Single((BlockState b) => b.Id == "list").Hidden);

			TestApp.Run(() =>
			{
				LayoutEditor editor = new LayoutEditor("tasks", new List<BlockSpec>
				{
					new BlockSpec
					{
						Id = "header",
						Title = "标题",
						Content = new TextBlock { Text = "h" }
					},
					new BlockSpec
					{
						Id = "list",
						Title = "列表",
						Content = new TextBlock { Text = "l" }
					}
				});
				editor.SetBlockHidden("list", true);
			});

			// v2 真源:已隐藏
			Assert.True(LayoutStoreV2.Load().Layout.Pages["tasks"].Blocks.Single((BlockLayout b) => b.Id == "list").Hidden);
			// v1 读取路径:必须立刻反映(移除 Persist() 里的 Reload 后,这里拿到的是预热时的 false)
			PageLayoutDoc? v1 = PageLayoutStore.Load("tasks");
			Assert.NotNull(v1);
			Assert.True(v1!.Blocks.Single((BlockState b) => b.Id == "list").Hidden);
		}
		finally
		{
			Release();
		}
	}
}