using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Core.Launcher.Layout;
using TipCL.Controls;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C9a:<see cref="PageLayoutHost"/> 的用例(两栏 + 可拖分隔条 + 比例记忆)。
///
/// 口径说明:
/// <list type="bullet">
/// <item>**取值**一律对着 Windows 常量断言(<c>RatioMin 0.15 / RatioMax 0.85 / RatioDefault 0.5 / DefaultGap 18</c>);</item>
/// <item>**拖动与双击**走控件上那两条**测试缝**(<c>CommitDragForTest</c> / <c>ResetToDefaultForTest</c>,
///   D-127:驱动内部处理器体,不模拟鼠标 —— 真实路径分别是 <c>GridSplitter.DragCompleted</c> 与
///   <c>PointerPressed(ClickCount==2)</c>,与缝共用同一段代码);</item>
/// <item>**默认渲染回归钉子**:没有存储比例时列宽必须等于**声明值**(这正是"既有帧逐字节不变"的前提);</item>
/// <item>**落点(R32-MWG 已统一,与 Windows 同一处)**:比例落布局 v2 文档
///   <c>Layout.Splits[pageId].Ratios[splitId]</c>,列数落 <c>Layout.Pages[pageId].Cols</c>(需 <c>Mode == grid</c>),
///   都经 <see cref="LayoutStoreV2"/> 落 <c>ui_layout_v2.json</c>。C9a 时期写在 <c>UiPrefs.Splits</c> /
///   <c>UiPrefs.Columns</c> 的旧值**仍读得到**:读时按遗留值代入并就地迁进 v2,迁成功才删旧键
///   (对应 Windows 里"比例只存布局文档"那一条;旧落点作为兜底保留,不算丢用户设置)。</item>
/// <item>**每个用例一份独立布局目录**(<see cref="C3aHome"/>):落点变成进程内带缓存的 v2 文档之后,
///   不隔离就会"上一个用例写过的比例/列数漏进下一个用例",所以整个类随 <see cref="IDisposable"/> 走。</item>
/// </list>
/// </summary>
public class C9aPageLayoutHostTests : IDisposable
{
	/// <summary>本用例专属布局目录(隔离 v2 文档与 v1 投影;红线:绝不碰用户真实数据目录)。</summary>
	private readonly C3aHome _home = new C3aHome();

	public void Dispose()
	{
		_home.Dispose();
	}

	/// <summary>v2 文档里该处比例(直接读真源,用来证"写进去的是文档,不是旧偏好")。</summary>
	private static double? DocRatio(string pageId, string splitId)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		if (!doc.Layout.Splits.TryGetValue(pageId, out SplitState? state) || state == null)
		{
			return null;
		}
		return state.Ratios.TryGetValue(splitId, out double ratio) ? ratio : null;
	}

	/// <summary>v2 文档里该页(没有 = null)。</summary>
	private static PageLayoutV2? DocPage(string pageId)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		return doc.Layout.Pages.TryGetValue(pageId, out PageLayoutV2? page) ? page : null;
	}

	/// <summary>常量与 Windows 逐值相同(改这些值就等于改契约)。</summary>
	[Fact]
	public void Ratio_constants_match_windows()
	{
		Assert.Equal(0.15, PageLayoutHost.MinRatio);
		Assert.Equal(0.85, PageLayoutHost.MaxRatio);
		Assert.Equal(0.5, PageLayoutHost.DefaultRatio);
		Assert.Equal(18.0, PageLayoutHost.DefaultGap);
	}

	/// <summary>夹取:越界夹回区间;NaN / Infinity 落默认 0.5(与 Windows <c>LayoutSchema.Clamp</c> 同口径)。</summary>
	[Theory]
	[InlineData(0.5, 0.5)]
	[InlineData(0.05, 0.15)]
	[InlineData(0.95, 0.85)]
	[InlineData(0.15, 0.15)]
	[InlineData(0.85, 0.85)]
	public void Clamp_ratio_keeps_the_windows_range(double input, double expected)
	{
		Assert.Equal(expected, PageLayoutHost.ClampRatio(input));
	}

	/// <summary>NaN / Infinity 按默认值处理(单独一条,Windows 注释里就是这么写的)。</summary>
	[Fact]
	public void Clamp_ratio_falls_back_to_default_for_nan_and_infinity()
	{
		Assert.Equal(PageLayoutHost.DefaultRatio, PageLayoutHost.ClampRatio(double.NaN));
		Assert.Equal(PageLayoutHost.DefaultRatio, PageLayoutHost.ClampRatio(double.PositiveInfinity));
		Assert.Equal(PageLayoutHost.DefaultRatio, PageLayoutHost.ClampRatio(double.NegativeInfinity));
	}

	/// <summary>像素 ↔ 比例(拿不到可用宽时:比例落默认、像素是 NaN)。</summary>
	[Fact]
	public void Pixel_and_ratio_conversions_match_windows()
	{
		Assert.Equal(0.4, PageLayoutHost.RatioFromPixels(400.0, 1000.0), 3);
		Assert.Equal(400.0, PageLayoutHost.PixelsFromRatio(0.4, 1000.0), 3);
		// 越界 / 非法宽度
		Assert.Equal(PageLayoutHost.MinRatio, PageLayoutHost.RatioFromPixels(10.0, 1000.0), 3);
		Assert.Equal(PageLayoutHost.DefaultRatio, PageLayoutHost.RatioFromPixels(400.0, 0.0), 3);
		Assert.True(double.IsNaN(PageLayoutHost.PixelsFromRatio(0.4, 0.0)));
	}

	/// <summary>`Resolve` 的三级回退各一条:存储 → 声明的现状比例 → 0.5。</summary>
	[Fact]
	public void Resolve_falls_back_from_stored_to_declared_to_default()
	{
		UiPrefs prefs = new UiPrefs();
		// 1 没有存储、也没有声明 ⇒ 0.5
		Assert.Equal(0.5, PageLayoutHost.Resolve(prefs, "page", "split"));
		// 2 没有存储、有声明 ⇒ 声明值
		Assert.Equal(0.3, PageLayoutHost.Resolve(prefs, "page", "split", declaredRatio: 0.3), 3);
		// 3 有存储 ⇒ 存储值(且夹取)
		Assert.True(PageLayoutHost.Write(prefs, "page", "split", 0.7));
		Assert.Equal(0.7, PageLayoutHost.Resolve(prefs, "page", "split", declaredRatio: 0.3), 3);
		// 存储值越界也要被夹:旧落点(C9a 的 UiPrefs.Splits)里塞一个非法值,
		// 读时迁移进 v2 时按同一口径夹到 0.85(文档里没有这一处,所以走的正是遗留回退这条路)
		prefs.Splits[PageLayoutHost.Key("page-legacy", "split")] = 5.0;
		Assert.Equal(0.85, PageLayoutHost.Resolve(prefs, "page-legacy", "split"));
		Assert.Equal(0.85, DocRatio("page-legacy", "split")!.Value, 3);
		Assert.False(prefs.Splits.ContainsKey("page-legacy/split"));
	}

	/// <summary>读 / 写 / 清:落点是 v2 文档,旧偏好键 <c>"pageId/splitId"</c> 只是遗留回退。</summary>
	[Fact]
	public void Read_write_clear_use_the_v2_document_not_the_legacy_prefs_key()
	{
		UiPrefs prefs = new UiPrefs();
		Assert.Null(PageLayoutHost.Read(prefs, "settings", "nav"));
		Assert.False(PageLayoutHost.IsStored(prefs, "settings", "nav"));
		Assert.True(PageLayoutHost.Write(prefs, "settings", "nav", 0.42));
		// 写进 v2 文档;C9a 的旧键不再出现(落点统一)
		Assert.Equal(0.42, DocRatio("settings", "nav")!.Value, 3);
		Assert.False(prefs.Splits.ContainsKey("settings/nav"));
		Assert.Equal(0.42, PageLayoutHost.Read(prefs, "settings", "nav")!.Value, 3);
		Assert.True(PageLayoutHost.IsStored(prefs, "settings", "nav"));
		Assert.True(PageLayoutHost.Clear(prefs, "settings", "nav"));
		Assert.Null(PageLayoutHost.Read(prefs, "settings", "nav"));
		Assert.Null(DocRatio("settings", "nav"));
		// 删不存在的返回 false;空 id 不写
		Assert.False(PageLayoutHost.Clear(prefs, "settings", "nav"));
		Assert.False(PageLayoutHost.Write(prefs, "", "nav", 0.5));
	}

	/// <summary>
	/// 落点是 v2 文档,而且**真的落了盘**:写成功后 <c>ui_layout_v2.json</c> 存在,丢掉内存缓存再读还在;
	/// 旧落点里的值读一次即迁进文档(迁成功删旧键),重启后仍从文档读到。
	/// </summary>
	[Fact]
	public void Writes_land_in_the_v2_document_on_disk()
	{
		UiPrefs prefs = new UiPrefs();
		Assert.True(PageLayoutHost.Write(prefs, "settings", "nav", 0.42));
		Assert.True(File.Exists(LayoutStoreV2.FilePath));
		LayoutStoreV2.ResetCache();
		Assert.Equal(0.42, DocRatio("settings", "nav")!.Value, 3);
		Assert.Equal(0.42, PageLayoutHost.Read(prefs, "settings", "nav")!.Value, 3);

		// 旧落点(C9a 的 UiPrefs.Splits)里的值:读一次就迁进文档
		prefs.Splits[PageLayoutHost.Key("settings", "rail")] = 0.31;
		Assert.Equal(0.31, PageLayoutHost.Read(prefs, "settings", "rail")!.Value, 3);
		Assert.False(prefs.Splits.ContainsKey("settings/rail"));
		LayoutStoreV2.ResetCache();
		Assert.Equal(0.31, DocRatio("settings", "rail")!.Value, 3);
	}

	/// <summary>
	/// **回归钉子**:没有存储比例时,默认渲染用的就是**声明列宽**(页面切到本控件后,既有 16 帧必须逐字节不变)。
	/// </summary>
	[Fact]
	public void Default_columns_are_the_declared_ones()
	{
		AvaloniaSession.Run(() =>
		{
			UiPrefs prefs = new UiPrefs();
			Assert.Null(PageLayoutHost.Read(prefs, "version-manage", "nav"));
			PageLayoutHost host = new PageLayoutHost("version-manage", "nav", new GridLength(272.0), new GridLength(1.0, GridUnitType.Star));
			Assert.False(host.HasStoredRatio);
			Assert.Equal(272.0, host.ColumnDefinitions[0].Width.Value, 3);
			Assert.Equal(GridUnitType.Pixel, host.ColumnDefinitions[0].Width.GridUnitType);
			Assert.Equal(18.0, host.ColumnDefinitions[1].Width.Value, 3);
			Assert.Equal(1.0, host.ColumnDefinitions[2].Width.Value, 3);
			Assert.Equal(GridUnitType.Star, host.ColumnDefinitions[2].Width.GridUnitType);
			// Auto 左栏(account)在无存储时**保持 Auto**(与 Windows 同:只有存过比例才切像素列)
			PageLayoutHost account = new PageLayoutHost("account", "hero", GridLength.Auto, new GridLength(1.0, GridUnitType.Star));
			Assert.Equal(GridUnitType.Auto, account.ColumnDefinitions[0].Width.GridUnitType);
		});
	}

	/// <summary>有存储比例 ⇒ 左栏按 <c>ratio × 可用宽</c> 的像素列落下去(拖动之后重建即此处)。</summary>
	[Fact]
	public void Stored_ratio_applies_pixel_columns()
	{
		AvaloniaSession.Run(() =>
		{
			using var shell = ShellFixture.Create();
			// 存一个 0.3,再构造:布局就绪后左栏应为 0.3 × 可用宽
			PageLayoutHost.Write(shell.Window.Prefs, "c9a-probe", "nav", 0.3);
			PageLayoutHost host = new PageLayoutHost("c9a-probe", "nav", new GridLength(272.0), new GridLength(1.0, GridUnitType.Star));
			Assert.True(host.HasStoredRatio);
			host.Width = 1000.0;
			host.Measure(new Size(1000.0, 600.0));
			host.Arrange(new Rect(0.0, 0.0, 1000.0, 600.0));
			Assert.Equal(300.0, host.ColumnDefinitions[0].Width.Value, 3);
			// 清掉,免得影响别的用例(本类每个用例本就各有目录,这里照旧显式收尾)
			PageLayoutHost.Clear(shell.Window.Prefs, "c9a-probe", "nav");
			shell.Window.SaveUiPrefs();
			Assert.Null(DocRatio("c9a-probe", "nav"));
		});
	}

	/// <summary>
	/// 拖动松手(**测试缝**走的是真实路径那段代码):可写时按当前左栏像素宽算比例并落盘(落 v2 文档);
	/// **`Writable=false`(实例详情内嵌的版本管理)一律不写**。
	/// </summary>
	[Fact]
	public void Drag_commit_writes_a_clamped_ratio_only_when_writable()
	{
		AvaloniaSession.Run(() =>
		{
			using var shell = ShellFixture.Create();
			PageLayoutHost host = new PageLayoutHost("c9a-drag", "nav", new GridLength(272.0), new GridLength(1.0, GridUnitType.Star));
			host.Width = 1000.0;
			host.Measure(new Size(1000.0, 600.0));
			host.Arrange(new Rect(0.0, 0.0, 1000.0, 600.0));
			host.CommitDragForTest();
			// 272 / 1000 = 0.272
			Assert.Equal(0.272, PageLayoutHost.Read(shell.Window.Prefs, "c9a-drag", "nav")!.Value, 3);
			Assert.Equal(0.272, DocRatio("c9a-drag", "nav")!.Value, 3);
			Assert.False((shell.Window.Prefs).Splits.ContainsKey("c9a-drag/nav"));

			// 不可写:不落盘
			PageLayoutHost.Clear(shell.Window.Prefs, "c9a-drag", "nav");
			PageLayoutHost readOnly = new PageLayoutHost("c9a-drag", "nav", new GridLength(272.0), new GridLength(1.0, GridUnitType.Star))
			{
				Writable = false,
			};
			readOnly.Width = 1000.0;
			readOnly.Measure(new Size(1000.0, 600.0));
			readOnly.Arrange(new Rect(0.0, 0.0, 1000.0, 600.0));
			readOnly.CommitDragForTest();
			Assert.Null(PageLayoutHost.Read(shell.Window.Prefs, "c9a-drag", "nav"));
			Assert.Null(DocRatio("c9a-drag", "nav"));
			// 静态入口也一样:显式 writable:false 不写
			Assert.False(PageLayoutHost.Write(shell.Window.Prefs, "c9a-drag", "nav", 0.5, writable: false));
			Assert.Null(DocRatio("c9a-drag", "nav"));
			shell.Window.SaveUiPrefs();
		});
	}

	/// <summary>双击分隔条 = 恢复默认(**测试缝**):清掉存储比例并回到声明列宽。</summary>
	[Fact]
	public void Double_click_reset_clears_the_stored_ratio()
	{
		AvaloniaSession.Run(() =>
		{
			using var shell = ShellFixture.Create();
			PageLayoutHost.Write(shell.Window.Prefs, "c9a-reset", "nav", 0.3);
			PageLayoutHost host = new PageLayoutHost("c9a-reset", "nav", new GridLength(272.0), new GridLength(1.0, GridUnitType.Star));
			host.Width = 1000.0;
			host.Measure(new Size(1000.0, 600.0));
			host.Arrange(new Rect(0.0, 0.0, 1000.0, 600.0));
			Assert.True(host.HasStoredRatio);
			host.ResetToDefaultForTest();
			Assert.False(host.HasStoredRatio);
			Assert.Null(PageLayoutHost.Read(shell.Window.Prefs, "c9a-reset", "nav"));
			Assert.Null(DocRatio("c9a-reset", "nav"));
			Assert.Equal(272.0, host.ColumnDefinitions[0].Width.Value, 3);
			shell.Window.SaveUiPrefs();
		});
	}

	/// <summary>
	/// 命中宽度(设置页传 8):默认 **0** ⇒ 分隔条只占间隙列(别的页完全不受影响);传 8 时它自带命中区,
	/// 且该页 gap 为 0 —— 这就是"gap 0 的页面也能拖"的实现方式(裁决 A)。
	/// </summary>
	[Fact]
	public void Splitter_hit_width_is_opt_in_and_zero_by_default()
	{
		AvaloniaSession.Run(() =>
		{
			PageLayoutHost plain = new PageLayoutHost("c9a-hit", "nav", new GridLength(272.0), new GridLength(1.0, GridUnitType.Star));
			Assert.Equal(0.0, plain.SplitterHitWidth);
			Assert.Equal(18.0, plain.GapWidth);
			PageLayoutHost zeroGap = new PageLayoutHost("settings", "nav", new GridLength(168.0), new GridLength(1.0, GridUnitType.Star), gap: 0.0, splitterHitWidth: 8.0);
			Assert.Equal(8.0, zeroGap.SplitterHitWidth);
			Assert.Equal(0.0, zeroGap.GapWidth);
		});
	}

	/// <summary>
	/// **"零像素"的证据**:静止时那条悬停细线必须 `IsVisible=false`(只有悬停/拖动才显)、分隔条本身可见可命中。
	/// 设置页那两帧逐字节不变(`45594 C19BF1C4...` / `43745 CE33BEFE...`)与这条同源。
	/// </summary>
	[Fact]
	public void Hover_line_is_invisible_at_rest()
	{
		AvaloniaSession.Run(() =>
		{
			PageLayoutHost host = new PageLayoutHost("settings", "nav", new GridLength(168.0), new GridLength(1.0, GridUnitType.Star), gap: 0.0, splitterHitWidth: 8.0);
			host.Width = 1000.0;
			host.Measure(new Size(1000.0, 600.0));
			host.Arrange(new Rect(0.0, 0.0, 1000.0, 600.0));
			Assert.False(host.HoverLineVisible);
			Assert.True(host.SplitterVisible);
			// 收起(内嵌)时分隔条也要消失 ⇒ 拖动入口不存在
			host.SetEmbedded(true);
			Assert.False(host.SplitterVisible);
			host.SetEmbedded(false);
			Assert.True(host.SplitterVisible);
		});
	}

	/// <summary>
	/// 格子列数(Windows <c>SplitRatioStore.PageColumns</c>):**默认 = fallback**(无存储 ⇒ 插件页仍是 3 列,
	/// 用户可见行为不变)。
	/// </summary>
	[Fact]
	public void Page_columns_default_to_the_fallback_when_nothing_is_stored()
	{
		UiPrefs prefs = new UiPrefs();
		Assert.Equal(3, PageLayoutHost.PageColumns(prefs, "plugins", 3, 1, 6));
		// 没有 prefs(离屏/无外壳)也一样回落 fallback
		Assert.Equal(3, PageLayoutHost.PageColumns(null, "plugins", 3, 1, 6));
	}

	/// <summary>列数落 v2 文档:存进去、读回来一致;超出 [min,max] 被夹;再把该页声明成格子模式。</summary>
	[Fact]
	public void Page_columns_are_stored_in_the_v2_document_and_clamped_to_the_range()
	{
		UiPrefs prefs = new UiPrefs();
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "plugins", 4, 2, 6));
		Assert.Equal(4, PageLayoutHost.PageColumns(prefs, "plugins", 3, 2, 6));
		// 落点就是文档:Pages["plugins"].Cols(读侧只认 Mode == grid)
		Assert.Equal(4, DocPage("plugins")!.Cols);
		Assert.Equal(LayoutSchema.ModeGrid, DocPage("plugins")!.Mode);
		Assert.False(prefs.Columns.ContainsKey("plugins"));
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "plugins", 99, 2, 6));
		Assert.Equal(6, PageLayoutHost.PageColumns(prefs, "plugins", 3, 2, 6));
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "plugins", 0, 2, 6));
		Assert.Equal(2, PageLayoutHost.PageColumns(prefs, "plugins", 3, 2, 6));
	}

	/// <summary>
	/// **文档的列档位是 `{2,3,4,6,12}`**(<see cref="LayoutSchema.ColChoices"/>,v2 的网格词汇表):
	/// 请求值不在档位里时落盘会**吸附**(5 → 4、1 → 2),读回来的就是吸附后的值 —— 与 Windows 同口径
	/// (Windows 的读侧也只看文档里的 <c>Cols</c>,而文档经 <c>SanitizePage</c> 吸附,5 / 1 本来就存不住)。
	/// </summary>
	[Fact]
	public void Page_columns_are_snapped_to_the_v2_grid_choices()
	{
		Assert.Equal(new[] { 2, 3, 4, 6, 12 }, LayoutSchema.ColChoices);
		UiPrefs prefs = new UiPrefs();
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "snap", 5, 1, 12));
		Assert.Equal(4, PageLayoutHost.PageColumns(prefs, "snap", 3, 1, 12));
		Assert.Equal(4, DocPage("snap")!.Cols);
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "snap", 1, 1, 12));
		Assert.Equal(2, PageLayoutHost.PageColumns(prefs, "snap", 3, 1, 12));
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "snap", 12, 1, 12));
		Assert.Equal(12, PageLayoutHost.PageColumns(prefs, "snap", 3, 1, 12));
	}

	/// <summary>
	/// 旧落点(<c>UiPrefs.Columns</c>)仍然读得到,非法值(旧版本写过的 0 / 负数)回落 fallback,
	/// 而不是把网格算成 0 列 —— 但**一旦文档里有这一页,就由文档说了算**(不再看旧值)。
	/// </summary>
	[Fact]
	public void Legacy_columns_are_read_but_the_document_wins()
	{
		UiPrefs prefs = new UiPrefs();
		// 文档里没有 "legacy-page" ⇒ 走旧落点
		prefs.Columns["legacy-page"] = 5;
		Assert.Equal(5, PageLayoutHost.PageColumns(prefs, "legacy-page", 3, 1, 6));
		prefs.Columns["legacy-page"] = 0;
		Assert.Equal(3, PageLayoutHost.PageColumns(prefs, "legacy-page", 3, 1, 6));
		prefs.Columns["legacy-page"] = -2;
		Assert.Equal(3, PageLayoutHost.PageColumns(prefs, "legacy-page", 3, 1, 6));
		// 文档里有这一页 ⇒ 旧值被忽略(4 胜 5),即便旧值"看起来合法"
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "legacy-page", 4, 1, 6));
		prefs.Columns["legacy-page"] = 5;
		Assert.Equal(4, PageLayoutHost.PageColumns(prefs, "legacy-page", 3, 1, 6));
	}

	/// <summary>
	/// `gridMode: false` 时**忽略存储值**(Windows 原文:只有声明 `Mode == grid` 的页面才用存储值)——
	/// 这条语义保留着,是为了将来"只想拿 fallback、不想读存储"的调用点。
	/// </summary>
	[Fact]
	public void Page_columns_ignore_storage_when_not_in_grid_mode()
	{
		UiPrefs prefs = new UiPrefs();
		Assert.True(PageLayoutHost.WritePageColumns(prefs, "plugins", 4, 1, 6));
		Assert.Equal(4, PageLayoutHost.PageColumns(prefs, "plugins", 3, 1, 6));
		Assert.Equal(3, PageLayoutHost.PageColumns(prefs, "plugins", 3, 1, 6, gridMode: false));
	}
}
