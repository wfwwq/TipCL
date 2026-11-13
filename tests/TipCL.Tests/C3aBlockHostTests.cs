using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3a:<c>BlockHost</c>(Windows <c>Pages\BlockHost.cs</c>)在**真 Avalonia 平台**上的行为。
///
/// 纪律:
/// - 每条碰 Avalonia 的用例体都经 <see cref="AvaloniaSession.Run(System.Action)"/>(D-79 / C1b-fix);
/// - 期望值来源逐条写在用例上方:标注 <c>Windows 源</c>(BlockHost.cs 的行/语义)或
///   <c>实测</c>(本机跑出来的 Avalonia 值)或 <c>本包决定</c>(差异表里那几条);
/// - 落盘一律走 <see cref="C3aHome"/> 注入的目录(红线:不碰用户真实数据目录);
/// - 拖拽不模拟真实输入设备:驱动的是 <c>BlockHost.BeginDragCore / DragTo / EndDrag</c>,
///   即真实事件处理器(<c>OnDragMove</c> / <c>OnDragEnd</c>)调用的同一层状态机(见 DECISIONS.md D-115)。
/// </summary>
public class C3aBlockHostTests
{
	private static void OnAvalonia(Action body)
	{
		AvaloniaSession.Run(body);
	}

	// ————— 构造与默认布局 —————

	/// <summary>
	/// 来源:Windows 源 BlockHost.cs 构造尾部 <c>ApplyStored(); Render();</c> +
	/// <c>ApplyDoc</c>(未知块丢弃、缺失块按 DefaultVisible 补尾)+ <c>Render</c>(隐藏块不进列表)。
	/// 无布局文档时 = 全默认:a/c 默认可见,b 默认收起;顺序 = 传进来的 specs 顺序。
	/// </summary>
	[Fact]
	public void Ctor_renders_visible_blocks_in_spec_order_and_keeps_hidden_ones_out_of_the_list()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b", visible: false), C3aBuild.Spec("c"));

			Assert.Equal(new[] { "a", "c" }, C3aTree.RenderedIds(host));
			Assert.Equal(new[] { "a", "b", "c" }, C3aTree.OrderIds(host));
			Assert.Equal(new[] { "b" }, host.HiddenNow);
			Assert.False(host.EditNow);
			Assert.False(host.TrayShownNow);
			Assert.False(host.BarShownNow);
			Assert.False(host.DraggingNow);
			Assert.Null(host.DragSourceNow);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>BlockHost</c> 构造里 <c>if (item != null &amp;&amp; !string.IsNullOrEmpty(item.Id))</c>
	/// 与 <c>specs ?? Array.Empty&lt;BlockSpec&gt;()</c> —— 空 id 被丢弃,null 集合不炸。
	/// </summary>
	[Fact]
	public void Ctor_drops_specs_with_empty_ids_and_tolerates_a_null_spec_list()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = new BlockHost("p", new List<BlockSpec>
			{
				C3aBuild.Spec("a"),
				C3aBuild.Spec(""),
				null!
			});
			Assert.Equal(new[] { "a" }, C3aTree.OrderIds(host));

			BlockHost empty = new BlockHost("p2", null!);
			Assert.Empty(C3aTree.OrderIds(empty));
			Assert.Empty(host.HiddenNow);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>contentMargin ?? new Thickness(18,14,18,16)</c>,以及
	/// <c>bottom.Margin = new Thickness(_list.Margin.Left, 8, _list.Margin.Right, _list.Margin.Bottom)</c>。
	/// 默认值必须与 PageBase.ScrollBody 的 (18,14,18,16) 对齐。
	/// </summary>
	[Fact]
	public void Ctor_default_content_margin_matches_PageBase_ScrollBody_and_the_bottom_row_follows_it()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			Thickness list = C3aTree.ListOf(host).Margin;
			Assert.Equal(new Thickness(18.0, 14.0, 18.0, 16.0), list);

			Thickness bottom = C3aTree.BottomOf(host).Margin;
			Assert.Equal(new Thickness(18.0, 8.0, 18.0, 16.0), bottom);

			// 块间距:Windows 的 private const double Gap = 10.0
			Assert.Equal(10.0, C3aTree.ListOf(host).Spacing, 6);
		});
	}

	/// <summary>
	/// 来源:Windows 源构造签名 <c>(string pageId, IReadOnlyList&lt;BlockSpec&gt; specs, Thickness? contentMargin = null)</c>
	/// —— 第三个参数只是覆盖列表与底部那一行的左右下边距。
	/// </summary>
	[Fact]
	public void Ctor_custom_content_margin_applies_to_the_list_and_to_the_bottom_row()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = new BlockHost("p", new List<BlockSpec> { C3aBuild.Spec("a") }, new Thickness(56.0, 0.0, 24.0, 18.0));

			Assert.Equal(new Thickness(56.0, 0.0, 24.0, 18.0), C3aTree.ListOf(host).Margin);
			Assert.Equal(new Thickness(56.0, 8.0, 24.0, 18.0), C3aTree.BottomOf(host).Margin);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>if (_specs.Count &gt;= 2)</c> 才挂「自定义布局...」右键菜单项。
	/// </summary>
	[Fact]
	public void Ctor_only_builds_the_customize_menu_when_there_are_at_least_two_blocks()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost one = C3aBuild.Host("p", C3aBuild.Spec("a"));
			Assert.Null(one.ContextMenu);

			BlockHost two = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			Assert.NotNull(two.ContextMenu);
			Assert.Single(two.ContextMenu!.Items);
			MenuItem item = Assert.IsAssignableFrom<MenuItem>(two.ContextMenu.Items[0]);
			Assert.Equal(I18n.L("自定义布局..."), item.Header);
		});
	}

	/// <summary>
	/// 来源:Windows 源菜单项的 <c>menuItem.Click += delegate { EnterEdit(); }</c> ——
	/// 这里走真实的 <c>MenuItem.ClickEvent</c> 路由(即真实入口,不是直接调 EnterEdit)。
	/// </summary>
	[Fact]
	public void Clicking_the_customize_menu_item_enters_edit_mode()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			MenuItem item = Assert.IsAssignableFrom<MenuItem>(host.ContextMenu!.Items[0]);

			item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

			Assert.True(host.EditNow);
			Assert.True(host.BarShownNow);
			Assert.False(host.TrayShownNow);
		});
	}

	// ————— 编辑模式 —————

	/// <summary>
	/// 来源:Windows 源 <c>EnterEdit</c>(<c>_edit = true; Render(); _barHost.Visibility = Visible</c>)与
	/// <c>RefreshTray</c> 末句 <c>_edit &amp;&amp; _tray.Children.Count &gt; 0</c> ——
	/// 没有隐藏块时托盘不占位。每张卡的操作条也要跟着出现。
	/// </summary>
	[Fact]
	public void EnterEdit_shows_the_toolbar_and_only_shows_the_tray_when_something_is_hidden()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b", visible: false), C3aBuild.Spec("c"));

			// b 默认收起,但托盘只在编辑态出现(常态隐藏)
			Assert.False(C3aTree.TrayHostOf(host).IsVisible);
			host.EnterEdit();
			Assert.True(host.BarShownNow);
			Assert.True(host.TrayShownNow);
			Assert.Equal(new[] { "b" }, host.TrayIdsNow);
			foreach (BlockHost.BlockCard card in C3aTree.RenderedCards(host))
			{
				Assert.True(card.BarNow.IsVisible);
				Assert.Equal(1.4, card.BorderThickness.Left, 6);
			}
		});
	}

	/// <summary>来源:Windows 源 <c>if (_edit || _specs.Count == 0) return;</c>(幂等 + 空宿主不进编辑态）。</summary>
	[Fact]
	public void EnterEdit_is_idempotent_and_ignored_on_an_empty_host()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"));
			host.EnterEdit();
			host.EnterEdit();
			Assert.True(host.EditNow);

			BlockHost empty = new BlockHost("p2", new List<BlockSpec>());
			empty.EnterEdit();
			Assert.False(empty.EditNow);
			Assert.False(empty.BarShownNow);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>ExitEdit</c> —— <c>_edit=false; Persist(); Render(); 两个宿主都收起;
	/// Toast(「布局已保存」, Success)</c>。Persist 同时把 Changed 事件打出去。
	/// </summary>
	[Fact]
	public void ExitEdit_persists_fires_Changed_and_toasts()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b", visible: false));
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.EnterEdit();
			host.ExitEdit();

			Assert.False(host.EditNow);
			Assert.False(host.BarShownNow);
			Assert.False(host.TrayShownNow);
			Assert.Equal(1, changed);
			Assert.Single(toasts.Items);
			Assert.Equal(I18n.L("布局已保存"), toasts.Items[0].Title);
			Assert.Equal(ToastKind.Success, toasts.Items[0].Kind);
			Assert.NotNull(PageLayoutStore.Load("p"));
		});
	}

	/// <summary>来源:Windows 源 <c>if (!_edit) return;</c> —— 非编辑态退出编辑什么也不做。</summary>
	[Fact]
	public void ExitEdit_does_nothing_when_not_editing()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.ExitEdit();

			Assert.Equal(0, changed);
			Assert.Empty(toasts.Items);
			Assert.Null(PageLayoutStore.Load("p"));
		});
	}

	// ————— 显隐 / 顺序 / 托盘 —————

	/// <summary>
	/// 来源:Windows 源 <c>HideBlock</c> —— 进 <c>_hidden</c>、从可视列表摘掉、刷托盘、Persist。
	/// </summary>
	[Fact]
	public void HideBlock_removes_the_block_from_the_list_adds_a_tray_chip_and_persists()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			host.EnterEdit();

			host.HideBlock(host.CardOf("b")!.Spec);

			Assert.Equal(new[] { "a", "c" }, C3aTree.RenderedIds(host));
			Assert.Equal(new[] { "b" }, host.TrayIdsNow);
			Assert.True(host.TrayShownNow);
			// 隐藏项仍然占着 _order 里的原槽位(恢复时回原位)
			Assert.Equal(new[] { "a", "b", "c" }, C3aTree.OrderIds(host));
			PageLayoutDoc saved = Assert.IsType<PageLayoutDoc>(PageLayoutStore.Load("p"));
			Assert.Equal(new[] { "a", "b", "c" }, IdsIn(saved));
			Assert.True(saved.Blocks[1].Hidden);
		});
	}

	/// <summary>来源:Windows 源 <c>if (!_edit || _hidden.Contains(spec.Id)) return;</c>。</summary>
	[Fact]
	public void HideBlock_is_ignored_outside_edit_mode_and_for_already_hidden_blocks()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			host.HideBlock(host.CardOf("a")!.Spec);
			Assert.Equal(new[] { "a", "b" }, C3aTree.OrderIds(host));
			Assert.Empty(host.HiddenNow);

			host.EnterEdit();
			host.HideBlock(host.CardOf("a")!.Spec);
			Assert.Equal(new[] { "a" }, host.HiddenNow);
			host.HideBlock(host.CardOf("a")!.Spec);
			Assert.Equal(new[] { "a" }, host.HiddenNow);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>Restore</c> + <c>InsertVisual</c> —— 隐藏项留在 <c>_order</c> 原槽位,
	/// 恢复时插到"排在它前面的可见块数"那个索引上。
	/// </summary>
	[Fact]
	public void Restore_puts_the_block_back_at_its_original_slot()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"), C3aBuild.Spec("d"));
			host.EnterEdit();

			// 隐藏中间两块:列表 [a,d],order 仍是 [a,b,c,d]
			host.HideBlock(host.CardOf("b")!.Spec);
			host.HideBlock(host.CardOf("c")!.Spec);
			Assert.Equal(new[] { "a", "d" }, C3aTree.RenderedIds(host));
			Assert.Equal(new[] { "a", "b", "c", "d" }, C3aTree.OrderIds(host));

			// 先恢复 c:排在 c 前面的可见块只有 a → 插到索引 1
			host.Restore(host.CardOf("c")!.Spec);
			Assert.Equal(new[] { "a", "c", "d" }, C3aTree.RenderedIds(host));

			// 再恢复 b:排在 b 前面的可见块还是只有 a → 插到索引 1
			host.Restore(host.CardOf("b")!.Spec);
			Assert.Equal(new[] { "a", "b", "c", "d" }, C3aTree.RenderedIds(host));
			Assert.Empty(host.HiddenNow);
			Assert.False(host.TrayShownNow);
		});
	}

	/// <summary>来源:Windows 源 <c>if (!_edit || !_hidden.Contains(spec.Id)) return;</c>。</summary>
	[Fact]
	public void Restore_is_ignored_outside_edit_mode_and_for_visible_blocks()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			host.EnterEdit();
			host.HideBlock(host.CardOf("b")!.Spec);

			host.Restore(host.CardOf("a")!.Spec);
			Assert.Equal(new[] { "b" }, host.HiddenNow);

			host.ExitEdit();
			host.Restore(host.CardOf("b")!.Spec);
			Assert.Equal(new[] { "b" }, host.HiddenNow);
			Assert.Equal(new[] { "a" }, C3aTree.RenderedIds(host));
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>ResetPage</c> —— 保存 Default 文档、重渲染、Changed、
	/// Toast(「布局已重置」, Success)。
	/// </summary>
	[Fact]
	public void ResetPage_restores_the_default_document_and_fires_Changed()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b", visible: false), C3aBuild.Spec("c"));
			host.EnterEdit();
			// 先把 c 藏起来、顺序改成 [c,a,b],再重置
			host.HideBlock(host.CardOf("c")!.Spec);
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.ResetPage();

			Assert.Equal(new[] { "a", "b", "c" }, C3aTree.OrderIds(host));
			Assert.Equal(new[] { "b" }, host.HiddenNow);
			Assert.Equal(new[] { "a", "c" }, C3aTree.RenderedIds(host));
			Assert.Equal(1, changed);
			Assert.Single(toasts.Items);
			Assert.Equal(I18n.L("布局已重置"), toasts.Items[0].Title);
		});
	}

	// ————— 持久化 —————

	/// <summary>
	/// 来源:Windows 源 <c>Persist</c>(把 <c>_order</c> 与 <c>_hidden</c> 组装成
	/// <c>PageLayoutDoc</c> 交给 Store)+ 构造头的 <c>ApplyStored</c>。
	/// 存 → 新建宿主重读 → 顺序与显隐完全一致("重新打开这一页"的等价物)。
	/// </summary>
	[Fact]
	public void Save_then_reload_round_trip_keeps_the_exact_order_and_visibility()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();

			BlockHost first = C3aBuild.Host("launch", C3aBuild.Spec("news"), C3aBuild.Spec("tasks"), C3aBuild.Spec("notes"));
			first.EnterEdit();
			// 把 notes 拖到最上面(离屏宿主没有布局高度,全部走 H() 的 120 兜底,
			// 所以 MidY(0)=60 / MidY(1)=190 / MidY(2)=320 —— 见下面 MidY 的专项用例)
			BlockHost.BlockCard notes = first.CardOf("notes")!;
			first.BeginDragCore(notes, notes.HandleNow, null);
			first.DragTo(50.0);
			first.EndDrag(notes.HandleNow);
			first.HideBlock(first.CardOf("tasks")!.Spec);
			Assert.Equal(new[] { "notes", "news", "tasks" }, C3aTree.OrderIds(first));

			home.ForgetCache();

			BlockHost second = C3aBuild.Host("launch", C3aBuild.Spec("news"), C3aBuild.Spec("tasks"), C3aBuild.Spec("notes"));
			Assert.Equal(new[] { "notes", "news", "tasks" }, C3aTree.OrderIds(second));
			Assert.Equal(new[] { "tasks" }, second.HiddenNow);
			Assert.Equal(new[] { "notes", "news" }, C3aTree.RenderedIds(second));
		});
	}

	/// <summary>
	/// 落盘落在注入目录里(红线),文件是 UTF-8 **无 BOM**、带 kind 标记
	/// (来源:Windows 源 <c>PageLayoutStore.WriteFile</c> 的
	/// <c>new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)</c> + <c>ExportJson</c> 的 kind)。
	/// </summary>
	[Fact]
	public void Persist_writes_the_document_into_the_injected_directory_as_utf8_without_bom()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			host.EnterEdit();
			host.HideBlock(host.CardOf("b")!.Spec);

			Assert.Equal(home.Dir, PageLayoutStore.DirPath);
			string file = Path.Combine(home.Dir, "ui_page_layouts.json");
			Assert.True(File.Exists(file), "落盘文件必须出现在注入目录里: " + file);

			byte[] bytes = File.ReadAllBytes(file);
			Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "不允许 BOM");
			string text = Encoding.UTF8.GetString(bytes);
			Assert.Contains("\"kind\": \"tipcl-page-layout\"", text, StringComparison.Ordinal);
			Assert.Contains("\"hidden\": true", text, StringComparison.Ordinal);
			Assert.False(File.Exists(file + ".tmp"), "原子落盘不该留下 .tmp");
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>ApplyDoc</c> 的 <c>doc.Normalize(KnownIds(), VisibleIds())</c> ——
	/// 文档里的未知块丢掉,缺失的块按 DefaultVisible 补到末尾。
	/// </summary>
	[Fact]
	public void Stored_document_with_unknown_ids_is_normalised_against_the_current_specs()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			PageLayoutStore.Save("p", new PageLayoutDoc
			{
				Blocks = new List<BlockState>
				{
					new BlockState { Id = "c", Hidden = false },
					new BlockState { Id = "gone", Hidden = false }
				}
			});

			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b", visible: false), C3aBuild.Spec("c"));

			Assert.Equal(new[] { "c", "a", "b" }, C3aTree.OrderIds(host));
			Assert.Equal(new[] { "b" }, host.HiddenNow);
			Assert.Equal(new[] { "c", "a" }, C3aTree.RenderedIds(host));
		});
	}

	// ————— 几何:MidY / H / 可视序号 —————

	/// <summary>
	/// 来源:Windows 源 <c>H(spec)</c>(缓存高度 &gt; 1 才用,否则 120)+ <c>MidY(k)</c>
	/// (每个块累加 <c>H + Gap</c>,返回 <c>累计 + H/2</c>;越界返回累计值)。
	/// 离屏宿主没有布局高度 → 一律走 120 兜底,于是可以得到精确期望值:Gap = 10。
	/// </summary>
	[Fact]
	public void MidY_uses_the_120_fallback_height_and_the_10_pixel_gap_on_an_unlaid_out_host()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));

			Assert.Equal(120.0, host.H(C3aBuild.Spec("x")), 6);
			Assert.Equal(60.0, host.MidY(0), 6);
			Assert.Equal(190.0, host.MidY(1), 6);
			Assert.Equal(320.0, host.MidY(2), 6);
			// 越界:返回"所有可见块 + 间距"的累计值
			Assert.Equal(390.0, host.MidY(3), 6);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>BeginDrag</c> 里
	/// <c>if (blockCard.ActualHeight &gt; 1.0) _dragHeights[...] = ActualHeight</c> ——
	/// 布局过之后缓存的是"这张卡真实排出来的高度"(不是 120 兜底),<c>MidY</c> 再按它算中点。
	/// 这里不写死像素值(编辑态的内容体外壳带 6/8 内边距,高度不该被测试猜出来),
	/// 而是把"缓存值 = 卡片的实际高度"这一条直接钉住,再用它验算 MidY 的公式。
	/// </summary>
	[Fact]
	public void MidY_uses_the_cached_real_heights_once_the_host_has_been_laid_out()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a", contentHeight: 40.0), C3aBuild.Spec("b", contentHeight: 60.0));
			using var shown = UiHost.Show(host);
			host.EnterEdit();

			BlockHost.BlockCard first = host.CardOf("a")!;
			BlockHost.BlockCard second = host.CardOf("b")!;
			Assert.True(first.Bounds.Height > 1.0, "排过布局之后卡片高度必须 > 1");
			host.BeginDragCore(first, first.HandleNow, null);

			double h0 = host.H(first.Spec);
			double h1 = host.H(second.Spec);
			Assert.Equal(first.Bounds.Height, h0, 6);
			Assert.Equal(second.Bounds.Height, h1, 6);
			Assert.Equal(h0 / 2.0, host.MidY(0), 6);
			Assert.Equal(h0 + 10.0 + h1 / 2.0, host.MidY(1), 6);
		});
	}

	/// <summary>来源:Windows 源 <c>VisibleIndexOf</c>(隐藏块返回 -1)与 <c>VisibleCount</c>。</summary>
	[Fact]
	public void VisibleIndexOf_and_VisibleCount_ignore_hidden_blocks()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			host.EnterEdit();
			Assert.Equal(3, host.VisibleCount());
			Assert.Equal(1, host.VisibleIndexOf(host.CardOf("b")!.Spec));

			host.HideBlock(host.CardOf("b")!.Spec);
			Assert.Equal(2, host.VisibleCount());
			Assert.Equal(-1, host.VisibleIndexOf(host.CardOf("b")!.Spec));
			Assert.Equal(new[] { "a", "c" }, AsIds(host.VisibleSpecs()));
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>MoveVisible</c> —— <c>_order</c> 里插到目标可见块的旁边,
	/// 可视树同步搬家(<c>InsertVisual</c>)。
	/// </summary>
	[Fact]
	public void MoveVisible_swaps_with_the_neighbour_in_both_the_model_and_the_visual_list()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));

			host.MoveVisible(0, 1);
			Assert.Equal(new[] { "b", "a", "c" }, C3aTree.OrderIds(host));
			Assert.Equal(new[] { "b", "a", "c" }, C3aTree.RenderedIds(host));

			host.MoveVisible(2, -1);
			Assert.Equal(new[] { "b", "c", "a" }, C3aTree.OrderIds(host));
			Assert.Equal(new[] { "b", "c", "a" }, C3aTree.RenderedIds(host));
		});
	}

	// ————— 拖拽 —————

	/// <summary>
	/// 来源:Windows 源 <c>BeginDrag</c> + <c>OnDragMove</c> + <c>EndDrag</c>。
	/// 状态机入口是 <c>BeginDragCore</c> / <c>DragTo</c> / <c>EndDrag</c>(真实处理器也只调这三个)。
	/// 离屏高度兜底 120 → 拖动最后一块到 y=100(在 MidY(0)=60 与 MidY(1)=190 之间)只换一格。
	/// </summary>
	[Fact]
	public void Drag_moves_a_block_up_one_slot_when_the_pointer_passes_the_neighbour_midpoint()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			host.EnterEdit();
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			BlockHost.BlockCard card = host.CardOf("c")!;
			host.BeginDragCore(card, card.HandleNow, null);
			Assert.True(host.DraggingNow);
			Assert.Same(card.HandleNow, host.DragSourceNow);
			Assert.Equal(0.75, card.Opacity, 6);

			host.DragTo(100.0);
			Assert.Equal(new[] { "a", "c", "b" }, C3aTree.OrderIds(host));
			Assert.Equal(new[] { "a", "c", "b" }, C3aTree.RenderedIds(host));

			host.EndDrag(card.HandleNow);
			Assert.False(host.DraggingNow);
			Assert.Null(host.DragSourceNow);
			Assert.Equal(1.0, card.Opacity, 6);
			Assert.Equal(1, changed);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>OnDragMove</c> 的两个 while 循环 —— 一次移动可以跨多格,
	/// "逐格与相邻块中点比较,直到不再交换"。
	/// </summary>
	[Fact]
	public void Drag_can_cross_several_slots_in_a_single_move()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"), C3aBuild.Spec("d"));
			host.EnterEdit();

			BlockHost.BlockCard card = host.CardOf("d")!;
			host.BeginDragCore(card, card.HandleNow, null);
			host.DragTo(50.0);
			Assert.Equal(new[] { "d", "a", "b", "c" }, C3aTree.OrderIds(host));

			// 再往下丢回最后
			host.DragTo(10000.0);
			Assert.Equal(new[] { "a", "b", "c", "d" }, C3aTree.OrderIds(host));

			host.EndDrag(card.HandleNow);
		});
	}

	/// <summary>来源:Windows 源 <c>OnDragMove</c> 的 <c>if (num &lt; 0) return;</c> —— 拖的块被隐藏时不动。</summary>
	[Fact]
	public void Drag_does_nothing_when_the_dragged_block_became_invisible()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			host.EnterEdit();
			BlockHost.BlockCard card = host.CardOf("c")!;
			host.BeginDragCore(card, card.HandleNow, null);

			// 拖拽期间把这块藏了(托盘点击那种场景)
			host.HideBlock(card.Spec);
			host.DragTo(0.0);

			Assert.Equal(new[] { "a", "b", "c" }, C3aTree.OrderIds(host));
			host.EndDrag(card.HandleNow);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>BeginDrag</c> 头一句 <c>if (!_edit || _dragCard != null) return;</c> ——
	/// 非编辑态不能拖,拖拽中再按下也不重入。
	/// </summary>
	[Fact]
	public void BeginDrag_is_ignored_outside_edit_mode_and_while_already_dragging()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			BlockHost.BlockCard a = host.CardOf("a")!;
			BlockHost.BlockCard b = host.CardOf("b")!;

			host.BeginDragCore(a, a.HandleNow, null);
			Assert.False(host.DraggingNow);

			host.EnterEdit();
			host.BeginDragCore(a, a.HandleNow, null);
			Assert.True(host.DraggingNow);

			// 第二次按下不该换拖拽对象
			host.BeginDragCore(b, b.HandleNow, null);
			host.DragTo(10000.0);
			Assert.Equal(new[] { "b", "a" }, C3aTree.OrderIds(host));
			Assert.Same(a.HandleNow, host.DragSourceNow);
		});
	}

	/// <summary>
	/// 来源:Windows 源两处循环的边界(<c>num &gt; 0</c> / <c>num &lt; num2 - 1</c>)——
	/// 指针跑到宿主外面(负 Y / 超大 Y)只会停在第 0 / 最后一位,不会把 <c>_order</c> 搞乱。
	/// </summary>
	[Fact]
	public void A_drag_that_leaves_the_host_does_not_corrupt_the_layout()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			host.EnterEdit();
			BlockHost.BlockCard card = host.CardOf("b")!;
			host.BeginDragCore(card, card.HandleNow, null);

			host.DragTo(-100000.0);
			Assert.Equal(new[] { "b", "a", "c" }, C3aTree.OrderIds(host));
			host.DragTo(100000.0);
			Assert.Equal(new[] { "a", "c", "b" }, C3aTree.OrderIds(host));
			host.EndDrag(card.HandleNow);

			// 拖完之后状态干净、模型与可视列表一致、并且已经落盘
			Assert.False(host.DraggingNow);
			Assert.Equal(C3aTree.OrderIds(host), C3aTree.RenderedIds(host));
			PageLayoutDoc saved = Assert.IsType<PageLayoutDoc>(PageLayoutStore.Load("p"));
			Assert.Equal(new[] { "a", "c", "b" }, IdsIn(saved));
		});
	}

	/// <summary>
	/// 泄漏/重复订阅检查:一次拖拽只该换一格(说明处理器没有在同一个元素上叠了两次),
	/// 结束后再调 <c>EndDrag</c> 既不抛也不重复通知;再拖一次仍然只换一格。
	/// 说明:Avalonia 没有公开的"处理器个数"API,所以这里按**行为**钉:状态已清干净 + 重复操作恰好一次生效。
	/// </summary>
	[Fact]
	public void Ending_a_drag_clears_the_state_and_repeated_drags_apply_exactly_one_step_each()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			host.EnterEdit();
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			BlockHost.BlockCard card = host.CardOf("c")!;
			host.BeginDragCore(card, card.HandleNow, null);
			// y = -1 一定小于任何 MidY(0) → 直接拖到最上面(不依赖具体布局高度)
			host.DragTo(-1.0);
			host.EndDrag(card.HandleNow);
			Assert.Equal(1, changed);
			Assert.Equal(new[] { "c", "a", "b" }, C3aTree.OrderIds(host));

			// 重复收尾:无状态可清 → 不该再通知
			host.EndDrag(card.HandleNow);
			host.EndDrag(card.HandleNow);
			Assert.Equal(1, changed);

			// 同一个手柄再来一次:过掉下一位的中点 → 恰好换一格、恰好通知一次
			host.BeginDragCore(card, card.HandleNow, null);
			host.DragTo(host.MidY(1) + 1.0);
			host.EndDrag(card.HandleNow);
			Assert.Equal(new[] { "a", "c", "b" }, C3aTree.OrderIds(host));
			Assert.Equal(2, changed);
			Assert.Equal(1.0, card.Opacity, 6);
		});
	}

	// ————— 单卡外观 —————

	/// <summary>
	/// 来源:Windows 源 <c>BlockCard.SetEdit</c> —— 操作条显隐、内容体不可点、内容体外壳
	/// 由 0 变 (6,0,6,8)、描边由 0 变 1.4。
	/// </summary>
	[Fact]
	public void BlockCard_SetEdit_toggles_the_bar_padding_border_and_hit_testing()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			BlockHost.BlockCard card = host.CardOf("a")!;

			card.SetEdit(false);
			Assert.False(card.BarNow.IsVisible);
			Assert.True(card.BodyNow.IsHitTestVisible);
			Assert.Equal(new Thickness(0.0), card.BodyWrapNow.Padding);
			Assert.Equal(0.0, card.BorderThickness.Left, 6);

			card.SetEdit(true);
			Assert.True(card.BarNow.IsVisible);
			Assert.False(card.BodyNow.IsHitTestVisible);
			Assert.Equal(new Thickness(6.0, 0.0, 6.0, 8.0), card.BodyWrapNow.Padding);
			Assert.Equal(1.4, card.BorderThickness.Left, 6);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>BlockCard.RefreshTint</c> + <c>BlockHost.AccentColor()</c> ——
	/// 编辑态铺一层"主题色 + alpha 10",非编辑态 Backgroud 置 null。
	/// </summary>
	[Fact]
	public void BlockCard_tint_is_the_accent_colour_at_alpha_10_only_in_edit_mode()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			BlockHost.BlockCard card = host.CardOf("a")!;
			Color accent = BlockHost.AccentColor();

			card.SetEdit(true);
			Color tintOf = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
			Assert.Equal(10, (int)tintOf.A);
			Assert.Equal(accent.R, tintOf.R);
			Assert.Equal(accent.G, tintOf.G);
			Assert.Equal(accent.B, tintOf.B);

			card.SetEdit(false);
			Assert.Null(card.Background);
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>AccentColor()</c> ——
	/// <c>Application.Current.TryFindResource("B.Accent") as SolidColorBrush</c>,取不到才回落到
	/// <c>Color.FromRgb(46,155,107)</c>。mac 侧的等价物是 <c>Palette.TryGet(键)</c>(差异表第 5 条)。
	/// </summary>
	[Fact]
	public void AccentColor_comes_from_the_palette_key_when_it_exists()
	{
		OnAvalonia(() =>
		{
			using var shown = UiHost.Show(new Border());
			Assert.True(Palette.TryGet("B.Accent", Palette.Current, out object? raw), "色板里必须有 B.Accent");
			Color expected = raw switch
			{
				ISolidColorBrush brush => brush.Color,
				Color color => color,
				_ => throw new InvalidOperationException("B.Accent 既不是刷子也不是颜色")
			};
			Assert.Equal(expected, BlockHost.AccentColor());
		});
	}

	/// <summary>
	/// 来源:Windows 源 <c>BlockCard</c> 构造尾部 —— 内容已挂在别的 <c>ContentControl</c> 上时先摘下来
	/// (<c>spec.Content is FrameworkElement { Parent: ContentControl parent } &amp;&amp; parent != _body</c>)。
	/// mac 差异:Avalonia 的父级是模板里的 ContentPresenter,所以 US 两种父级都认(见差异表)。
	/// </summary>
	[Fact]
	public void Block_content_is_detached_from_a_foreign_content_control_before_being_adopted()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			Border content = new Border { Height = 20.0 };
			ContentControl foreign = new ContentControl { Content = content };
			using var shown = UiHost.Show(foreign);
			Assert.NotNull(content.Parent);

			BlockHost host = C3aBuild.Host("p", new BlockSpec { Id = "a", Title = "a", Content = content });

			Assert.Null(foreign.Content);
			BlockHost.BlockCard card = host.CardOf("a")!;
			Assert.Same(content, card.BodyNow.Content);
		});
	}

	/// <summary>来源:Windows 源 <c>BlockCard</c> 里 <c>_body.Content = spec.Content</c>;每块一张常驻卡。</summary>
	[Fact]
	public void Each_block_keeps_one_cached_card_holding_its_own_content()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockSpec a = C3aBuild.Spec("a", contentHeight: 33.0);
			BlockSpec b = C3aBuild.Spec("b", contentHeight: 44.0);
			BlockHost host = C3aBuild.Host("p", a, b);

			Assert.Same(host.CardOf("a"), host.CardOf("a"));
			Assert.Same(a.Content, host.CardOf("a")!.BodyNow.Content);
			Assert.Same(b.Content, host.CardOf("b")!.BodyNow.Content);

			// 重渲染(编辑态进出 + 隐藏恢复)之后还是同一张卡
			host.EnterEdit();
			host.HideBlock(b);
			host.Restore(b);
			host.ExitEdit();
			Assert.Same(a.Content, host.CardOf("a")!.BodyNow.Content);
			Assert.Same(host.CardOf("a"), C3aTree.RenderedCards(host)[0]);
		});
	}

	/// <summary>
	/// 动画关掉(离屏 / 测试进程里没有东西推进动画时钟,见 D-69 / D-110)时:
	/// 进编辑态、显隐、拖拽都**当场落终态**,再推一次时钟不会有任何迟到变化。
	/// 同时断言 BlockHost 全程没有动画残留:Opacity 恒为 1、RenderTransform 没被动过。
	/// </summary>
	[Fact]
	public void With_motion_disabled_every_change_lands_immediately_and_leaves_nothing_pending()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			using var shown = UiHost.Show(host);
			Assert.False(Motion.Enabled, "离屏/测试进程里 Motion.Enabled 必须为假(Motion.HasClock)");

			host.EnterEdit();
			BlockHost.BlockCard card = host.CardOf("c")!;
			host.BeginDragCore(card, card.HandleNow, null);
			// y = -1 保证越过最上一位的中点(不依赖具体布局高度)
			host.DragTo(-1.0);
			UiHost.Flush();

			// 拖拽是"交换即落位,不做动画插值"(Windows 源注释),所以推时钟前后顺序一致
			string[] afterDrag = C3aTree.OrderIds(host);
			Assert.Equal(new[] { "c", "a", "b" }, afterDrag);
			UiHost.Flush();
			Assert.Equal(afterDrag, C3aTree.OrderIds(host));

			host.EndDrag(card.HandleNow);
			UiHost.Flush();
			Assert.Equal(afterDrag, C3aTree.OrderIds(host));
			Assert.Equal(1.0, card.Opacity, 6);
			Assert.Null(card.RenderTransform);
			foreach (BlockHost.BlockCard each in C3aTree.RenderedCards(host))
			{
				Assert.Equal(1.0, each.Opacity, 6);
			}
			// 再推一次时钟:没有任何迟到的动画/计时器把状态改回去。
			// (Avalonia 11 的 Dispatcher 没有公开的"还有没有排队工作"查询,所以按行为钉。)
			UiHost.Flush();
			Assert.Equal(afterDrag, C3aTree.OrderIds(host));
			Assert.Equal(1.0, card.Opacity, 6);
			Assert.Null(card.RenderTransform);
		});
	}

	/// <summary>来源:Windows 源 <c>public FrameworkElement Root =&gt; this</c>;mac 的对应基类是 Control。</summary>
	[Fact]
	public void Root_is_the_host_itself()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			Assert.Same(host, host.Root);
			Assert.IsAssignableFrom<Control>(host.Root);
		});
	}

	/// <summary>来源:Windows 源 <c>base.Focusable = false</c>(宿主自己不吃焦点,焦点留给内容)。</summary>
	[Fact]
	public void Host_is_not_focusable_and_the_list_children_are_the_cached_cards()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			Assert.False(host.Focusable);
			Assert.Equal(2, C3aTree.ListOf(host).Children.Count);
			Assert.Same(host.CardOf("a"), C3aTree.ListOf(host).Children[0]);
		});
	}

	/// <summary>来源:Windows 源工具栏四个按钮(完成编辑 / 重置本页 / 导出布局 / 导入布局)的文案与顺序。</summary>
	[Fact]
	public void Toolbar_carries_the_four_windows_actions_in_order()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			SPanel bar = Assert.IsAssignableFrom<SPanel>(C3aTree.BarHostOf(host).Child);
			Assert.Equal(4, bar.Children.Count);
			Assert.Equal(I18n.L("完成编辑"), ((Button)bar.Children[0]).Content);
			Assert.Equal(I18n.L("重置本页"), ((Button)bar.Children[1]).Content);
			Assert.Equal(I18n.L("导出布局"), ((Button)bar.Children[2]).Content);
			Assert.Equal(I18n.L("导入布局"), ((Button)bar.Children[3]).Content);
			Assert.Equal(HorizontalAlignment.Right, bar.HorizontalAlignment);
			Assert.Equal(new Thickness(10.0, 8.0, 10.0, 8.0), C3aTree.BarHostOf(host).Padding);
			Assert.Equal(10.0, C3aTree.BarHostOf(host).CornerRadius.TopLeft, 6);
		});
	}

	private static string[] IdsIn(PageLayoutDoc doc)
	{
		string[] ids = new string[doc.Blocks.Count];
		for (int i = 0; i < doc.Blocks.Count; i++)
		{
			ids[i] = doc.Blocks[i].Id;
		}
		return ids;
	}

	private static string[] AsIds(List<BlockSpec> specs)
	{
		string[] ids = new string[specs.Count];
		for (int i = 0; i < specs.Count; i++)
		{
			ids[i] = specs[i].Id;
		}
		return ids;
	}
}
