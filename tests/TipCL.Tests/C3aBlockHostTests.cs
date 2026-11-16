using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3a(第 2 版,薄壳契约):mac 的 <c>Pages\BlockHost.cs</c> 在 R32-MWG 之后只是
/// <c>LayoutEditor</c>(R32-MED 布局引擎 + v2 持久化)的薄壳 —— 页面区块的真源是
/// <c>ui_layout_v2.json</c>,v1 的 <c>ui_page_layouts.json</c> 由 v2 单向写通。
///
/// 所以这一层只验证"壳"负责的事:
/// - 构造 / 内容搬运 / 可视树形状 / 内容边距 / PageId / Root / Focusable;
/// - 编辑入口 <c>EnterEdit</c> 的门槛、Toast、幂等;
/// - <c>Changed</c> 转发、静态 <c>LayoutChanged</c> 触发的重排、主题变体变更后的重刷;
/// - <c>Unloaded</c> 自动收尾、v2 + v1 双文件落盘、v2 读入时对当前 specs 的归一化;
/// - 测试视窗 <c>OrderNow / HiddenNow</c> 的只读投影语义;
/// - <c>AccentColor</c> 与 <c>DetachFromForeignHost</c> 这两个仍留在宿主里的静态助手。
///
/// 拖拽 / 一维换序 / 中点计算 / 折叠 / 缩放 / 编辑态蒙版这些**引擎行为**的用例在
/// <see cref="R32McLayoutEditorTests"/>(40 条,R32-MED 入库)。v1 时代 C3a 里同名的那些
/// (MidY_*/MoveVisible_*/Drag_*/BlockCard_SetEdit 等)跟随代码所有权一起搬过去了,这里不再重复 ——
/// 差异与逐条去向见 progress\R32-MWG.md「C3a 用例去向」表。
///
/// 纪律:每条碰 Avalonia 的用例体都经 <see cref="AvaloniaSession.Run(System.Action)"/>;
/// 落盘一律走 <see cref="C3aHome"/> 注入的目录(红线:不碰用户真实数据目录)。
/// </summary>
public class C3aBlockHostTests
{
	private static void OnAvalonia(Action body)
	{
		AvaloniaSession.Run(body);
	}

	// ————— 构造 / 可视树 —————

	/// <summary>
	/// 来源:Windows 源 <c>BlockHost</c> 构造尾部 <c>ApplyStored(); Render();</c> ——
	/// 无布局文档时全默认:顺序 = specs 顺序,<c>DefaultVisible = false</c> 的块进隐藏集合、不进列表。
	/// </summary>
	[Fact]
	public void Ctor_renders_visible_blocks_in_spec_order_and_keeps_hidden_ones_out_of_the_list()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b", visible: false), C3aBuild.Spec("c"));

			Assert.Equal(new[] { "a", "c" }, C3aTree.RenderedIds(host).ToArray());
			// OrderNow 与 v1 同口径:隐藏块留在顺序里(原槽位),只是不进渲染列表
			Assert.Equal(new[] { "a", "b", "c" }, host.OrderNow.Select(spec => spec.Id).ToArray());
			Assert.Equal(new[] { "b" }, host.HiddenNow.ToArray());
			Assert.False(host.Editor.IsEditing);
			Assert.False(C3aTree.TrayHostOf(host).IsVisible);
			Assert.False(C3aTree.BarHostOf(host).IsVisible);
		});
	}

	/// <summary>
	/// 来源:Windows 源构造里 <c>if (item != null &amp;&amp; !string.IsNullOrEmpty(item.Id))</c>
	/// 与 <c>specs ?? Array.Empty&lt;BlockSpec&gt;()</c> —— 空 id 被丢弃,null 集合不炸。
	/// </summary>
	[Fact]
	public void Ctor_drops_specs_with_empty_ids_and_tolerates_a_null_spec_list()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();

			BlockHost none = new BlockHost("p", null!);
			Assert.Empty(none.OrderNow);
			Assert.False(none.EnterEdit(), "没有区块的页面不能进编辑态");

			BlockHost host = C3aBuild.Host("p", new BlockSpec { Id = "" }, C3aBuild.Spec("a"));
			Assert.Equal(new[] { "a" }, host.OrderNow.Select(spec => spec.Id).ToArray());
			Assert.Equal(new[] { "a" }, C3aTree.RenderedIds(host).ToArray());
		});
	}

	/// <summary>来源:Windows 源 <c>public FrameworkElement Root =&gt; this</c>;内容就是编辑器根(两行 <c>*,Auto</c>)。</summary>
	[Fact]
	public void Root_is_the_host_itself_and_content_is_the_editor_root_grid()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			Assert.Same(host, host.Root);
			Grid root = C3aTree.Root(host);
			Assert.Equal(2, root.RowDefinitions.Count);
			Assert.Equal(GridLength.Star, root.RowDefinitions[0].Height);
			Assert.Equal(GridLength.Auto, root.RowDefinitions[1].Height);
			Assert.Same(C3aTree.ScrollOf(host), root.Children[0]);
			Assert.Same(C3aTree.BottomOf(host), root.Children[1]);
		});
	}

	/// <summary>来源:Windows 源 <c>base.Focusable = false</c>(宿主不吃焦点);单列时列表孩子就是那几张常驻卡。</summary>
	[Fact]
	public void Host_is_not_focusable_and_the_list_children_are_the_cached_cards()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			Assert.False(host.Focusable);
			FillPanel list = C3aTree.ListOf(host);
			Assert.Equal(2, list.Children.Count);
			Assert.Same(C3aTree.RenderedCards(host)[0], list.Children[0]);
		});
	}

	/// <summary>来源:Windows 源 ctor 的默认 <c>contentMargin</c>(18,14,18,16),底部那一列左右、下边跟上。</summary>
	[Fact]
	public void Ctor_default_content_margin_matches_the_editor_default()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"));

			Assert.Equal(new Thickness(18.0, 14.0, 18.0, 16.0), C3aTree.ListOf(host).Margin);
			Assert.Equal(18.0, C3aTree.BottomOf(host).Margin.Left, 6);
			Assert.Equal(18.0, C3aTree.BottomOf(host).Margin.Right, 6);
			Assert.Equal(16.0, C3aTree.BottomOf(host).Margin.Bottom, 6);
		});
	}

	/// <summary>来源:Windows 源 ctor 的 <c>contentMargin</c> 参数(启动页传 (56,0,24,18))。</summary>
	[Fact]
	public void Ctor_custom_content_margin_applies_to_the_list_and_to_the_bottom_row()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = new BlockHost("p", new List<BlockSpec> { C3aBuild.Spec("a") }, new Thickness(56.0, 0.0, 24.0, 18.0));

			Assert.Equal(new Thickness(56.0, 0.0, 24.0, 18.0), C3aTree.ListOf(host).Margin);
			Assert.Equal(56.0, C3aTree.BottomOf(host).Margin.Left, 6);
			Assert.Equal(24.0, C3aTree.BottomOf(host).Margin.Right, 6);
			Assert.Equal(18.0, C3aTree.BottomOf(host).Margin.Bottom, 6);
		});
	}

	/// <summary>来源:Windows 源 <c>public string PageId =&gt; ...</c>(布局文档里的 pageId)。</summary>
	[Fact]
	public void PageId_is_the_page_id_the_host_was_built_with()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("launch", C3aBuild.Spec("a"));
			Assert.Equal("launch", host.PageId);
		});
	}

	// ————— 编辑入口 —————

	/// <summary>来源:Windows 源 <c>EnterEdit</c>(进编辑 + Toast);工具条随编辑态出现,托盘此刻还是空的。</summary>
	[Fact]
	public void EnterEdit_enters_edit_mode_shows_the_bar_and_toasts()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			Assert.True(host.EnterEdit());
			Assert.True(host.Editor.IsEditing);
			Assert.True(C3aTree.BarHostOf(host).IsVisible);
			Assert.False(C3aTree.TrayHostOf(host).IsVisible);
			Assert.Single(toasts.Items);
			Assert.Equal(I18n.L("已进入自定义布局"), toasts.Items[0].Title);
			Assert.Equal(ToastKind.Success, toasts.Items[0].Kind);
		});
	}

	/// <summary>来源:Windows 源 <c>EnterEdit</c> 的 <c>if (!_edit)</c> 守卫 —— 重复进入不重复提示。</summary>
	[Fact]
	public void EnterEdit_is_idempotent_and_toasts_only_once()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			Assert.True(host.EnterEdit());
			Assert.True(host.EnterEdit());
			Assert.True(host.Editor.IsEditing);
			Assert.Single(toasts.Items);
		});
	}

	/// <summary>来源:Windows 源 <c>specs.Count &gt;= 1</c> 的入口条件;mac 由 <c>LayoutEditor.CanEdit</c> 把关,
	/// <c>EnterEdit()</c> 返回 false 表示"这一页没有可编辑的区块",调用方据此给提示。</summary>
	[Fact]
	public void EnterEdit_returns_false_and_does_not_edit_when_there_are_no_blocks()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = new BlockHost("p", new List<BlockSpec>());

			Assert.False(host.EnterEdit());
			Assert.False(host.Editor.IsEditing);
			Assert.Empty(toasts.Items);
		});
	}

	// ————— 显隐 / 重排 / 落盘 —————

	/// <summary>来源:Windows 源 <c>HideBlock</c>(编辑态才生效、立即重排落盘)+ 托盘 chip。</summary>
	[Fact]
	public void HideBlock_removes_the_block_adds_a_tray_chip_and_persists()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockSpec a = C3aBuild.Spec("a");
			BlockSpec b = C3aBuild.Spec("b");
			BlockHost host = C3aBuild.Host("p", a, b);
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.EnterEdit();
			host.Editor.HideBlock(b);

			Assert.Equal(new[] { "a" }, C3aTree.RenderedIds(host).ToArray());
			Assert.True(C3aTree.TrayHostOf(host).IsVisible);
			WrapPanel tray = C3aTree.TrayOf(host);
			Assert.Single(tray.Children);
			// Windows 的托盘 chip 是 Content=string;mac 的 Ui.Btn(..., glyph) 把文案做成「图标 + 文字」组合,
			// 文字同时写进 AutomationProperties.Name —— 这里认后者(组合控件的可视文案口径)。
			Button chip = Assert.IsType<Button>(tray.Children[0]);
			Assert.Equal(b.Title, AutomationProperties.GetName(chip));
			Assert.Contains("b", host.HiddenNow);
			Assert.True(changed > 0, "隐藏会立刻落盘并触发 Changed");
			Assert.True(File.Exists(Path.Combine(home.Dir, "ui_layout_v2.json")));
		});
	}

	/// <summary>来源:Windows 源 <c>HideBlock</c> 的 <c>if (!_edit) return;</c> 与"已隐藏的块不重复处理"。</summary>
	[Fact]
	public void HideBlock_is_ignored_outside_edit_mode_and_for_already_hidden_blocks()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockSpec a = C3aBuild.Spec("a");
			BlockSpec b = C3aBuild.Spec("b");
			BlockHost host = C3aBuild.Host("p", a, b);

			host.Editor.HideBlock(a);
			Assert.Equal(new[] { "a", "b" }, C3aTree.RenderedIds(host).ToArray());
			Assert.Empty(host.HiddenNow);

			host.EnterEdit();
			host.Editor.HideBlock(b);
			host.Editor.HideBlock(b);
			Assert.Single(C3aTree.TrayOf(host).Children);
			Assert.Equal(new[] { "a" }, C3aTree.RenderedIds(host).ToArray());
		});
	}

	/// <summary>来源:Windows 源 <c>Restore</c> —— 隐藏项留在原槽位,恢复时回到原位置(不是追加到末尾)。</summary>
	[Fact]
	public void Restore_puts_the_block_back_at_its_original_slot()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockSpec a = C3aBuild.Spec("a");
			BlockHost host = C3aBuild.Host("p", a, C3aBuild.Spec("b"), C3aBuild.Spec("c"));

			host.EnterEdit();
			host.Editor.HideBlock(a);
			Assert.Equal(new[] { "b", "c" }, C3aTree.RenderedIds(host).ToArray());

			host.Editor.SetBlockHidden("a", false);
			Assert.Equal(new[] { "a", "b", "c" }, C3aTree.RenderedIds(host).ToArray());
			Assert.Empty(host.HiddenNow);
		});
	}

	/// <summary>来源:Windows 源 <c>ExitEdit</c>(落盘 + Toast + Changed);R32 之后盘上是 v2 + v1 两份。</summary>
	[Fact]
	public void ExitEdit_persists_the_document_fires_Changed_and_toasts()
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

			host.EnterEdit();
			host.Editor.HideBlock(host.OrderNow.First(spec => spec.Id == "b"));
			toasts.Items.Clear();
			int before = changed;
			host.Editor.ExitEdit();

			Assert.False(host.Editor.IsEditing);
			Assert.True(changed > before);
			Assert.Contains(toasts.Items, toast => toast.Title == I18n.L("布局已保存"));
			Assert.True(File.Exists(Path.Combine(home.Dir, "ui_layout_v2.json")));
			Assert.True(File.Exists(Path.Combine(home.Dir, "ui_page_layouts.json")));
		});
	}

	/// <summary>来源:Windows 源「进编辑后不动任何东西直接完成编辑,页面应该和之前完全一样」——
	/// 不写盘、不提示(引擎在 ExitEdit 里把编辑快照还原)。</summary>
	[Fact]
	public void ExitEdit_without_any_change_writes_nothing_and_toasts_nothing()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			host.EnterEdit();
			toasts.Items.Clear();
			host.Editor.ExitEdit();

			Assert.False(host.Editor.IsEditing);
			Assert.Empty(toasts.Items);
			Assert.Empty(Directory.GetFiles(home.Dir, "*.json", SearchOption.AllDirectories));
		});
	}

	/// <summary>来源:Windows 源 <c>ExitEdit</c> 的 <c>if (!_edit) return;</c>。</summary>
	[Fact]
	public void ExitEdit_does_nothing_when_not_editing()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"));

			host.Editor.ExitEdit();
			Assert.False(host.Editor.IsEditing);
			Assert.Empty(toasts.Items);
			Assert.Empty(Directory.GetFiles(home.Dir, "*.json", SearchOption.AllDirectories));
		});
	}

	/// <summary>来源:mac 壳自己的收尾约定(Windows 没有这一段):页面被切走时仍停在编辑态 → 自动
	/// <c>ExitEdit</c>(保存布局),避免"切回来还残留编辑态"。</summary>
	[Fact]
	public void Unloaded_exits_edit_mode_and_saves_the_pending_layout()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			using (UiHost.Show(host))
			{
				host.EnterEdit();
				host.Editor.HideBlock(host.OrderNow.First(spec => spec.Id == "b"));
				UiHost.Flush();
				Assert.True(host.Editor.IsEditing);
			}

			Assert.False(host.Editor.IsEditing);
			home.ForgetCache();
			BlockHost reloaded = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			Assert.Equal(new[] { "a" }, C3aTree.RenderedIds(reloaded).ToArray());
			Assert.Contains("b", reloaded.HiddenNow);
		});
	}

	/// <summary>来源:Windows 源 <c>ResetPage</c> —— 回到页面给的默认文档(白名单顺序 + DefaultVisible)。</summary>
	[Fact]
	public void ResetPage_restores_the_default_document_and_fires_Changed()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			using var toasts = new C3aToasts();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c", visible: false));
			int changed = 0;
			host.Changed += delegate
			{
				changed++;
			};

			host.EnterEdit();
			host.Editor.HideBlock(host.OrderNow.First(spec => spec.Id == "a"));
			Assert.Equal(new[] { "b" }, C3aTree.RenderedIds(host).ToArray());

			toasts.Items.Clear();
			host.Editor.ResetPage();

			Assert.Equal(new[] { "a", "b" }, C3aTree.RenderedIds(host).ToArray());
			Assert.Equal(new[] { "c" }, host.HiddenNow.ToArray());
			Assert.True(changed > 0);
			Assert.Contains(toasts.Items, toast => toast.Title == I18n.L("布局已重置"));
		});
	}

	/// <summary>来源:Windows 源「保存 → 重读」往返:顺序与显隐都保持。</summary>
	[Fact]
	public void Save_then_reload_round_trip_keeps_the_exact_order_and_visibility()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));

			host.EnterEdit();
			host.Editor.HideBlock(host.OrderNow.First(spec => spec.Id == "c"));
			host.Editor.MutatePage(page =>
			{
				BlockLayout moved = page.Blocks.First(block => block.Id == "b");
				page.Blocks.Remove(moved);
				page.Blocks.Insert(0, moved);
			});
			host.Editor.ExitEdit();
			Assert.Equal(new[] { "b", "a" }, C3aTree.RenderedIds(host).ToArray());

			home.ForgetCache();
			BlockHost reloaded = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			Assert.Equal(new[] { "b", "a" }, C3aTree.RenderedIds(reloaded).ToArray());
			Assert.Contains("c", reloaded.HiddenNow);
		});
	}

	/// <summary>来源:Windows 源 <c>Persist</c> 写 JSON(UTF-8 无 BOM);R32 之后 v2 是真源、v1 是写通投影,
	/// 两份都必须落在注入目录里(不碰用户数据目录)。</summary>
	[Fact]
	public void Persist_writes_v2_and_v1_projection_as_utf8_without_bom()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			host.EnterEdit();
			host.Editor.HideBlock(host.OrderNow.First(spec => spec.Id == "b"));
			host.Editor.ExitEdit();

			string[] files = Directory.GetFiles(home.Dir, "*.json", SearchOption.AllDirectories);
			Assert.Equal(2, files.Length);
			Assert.Contains(files, file => string.Equals(Path.GetFileName(file), "ui_layout_v2.json", StringComparison.OrdinalIgnoreCase));
			Assert.Contains(files, file => string.Equals(Path.GetFileName(file), "ui_page_layouts.json", StringComparison.OrdinalIgnoreCase));

			UTF8Encoding strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
			foreach (string file in files)
			{
				byte[] bytes = File.ReadAllBytes(file);
				Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, file + " 不应带 BOM");
				string json = strict.GetString(bytes);
				Assert.Contains("\"p\"", json, StringComparison.Ordinal);
				Assert.Contains("\"a\"", json, StringComparison.Ordinal);
			}
		});
	}

	/// <summary>来源:Windows 源 <c>ApplyDoc</c> 的归一化 —— 文档里未知的块丢弃、缺的块按 DefaultVisible 补到末尾。</summary>
	[Fact]
	public void Stored_document_with_unknown_ids_is_normalised_against_the_current_specs()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			LayoutV2 doc = new LayoutV2 { Layout = new LayoutBody() };
			doc.Layout.Pages["p"] = new PageLayoutV2
			{
				Blocks = new List<BlockLayout>
				{
					new BlockLayout { Id = "a" },
					new BlockLayout { Id = "zzz" },
					new BlockLayout { Id = "b", Hidden = true }
				}
			};
			Assert.True(LayoutStoreV2.Save(doc));

			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"), C3aBuild.Spec("c"));
			Assert.Equal(new[] { "a", "b", "c" }, host.Editor.State.Order.ToArray());
			Assert.Equal(new[] { "b" }, host.HiddenNow.ToArray());
			Assert.Equal(new[] { "a", "c" }, C3aTree.RenderedIds(host).ToArray());
		});
	}

	// ————— 测试视窗投影 —————

	/// <summary>来源:旧 v1 <c>OrderNow</c>/<c>HiddenNow</c> 的只读语义 —— 顺序跟模型(隐藏项保留原槽位)、
	/// 快照不回头变;渲染列表另看 RenderedIds(隐藏项不进)。</summary>
	[Fact]
	public void OrderNow_and_HiddenNow_are_read_only_projections_of_the_editor_state()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));

			Assert.Equal(new[] { "a", "b" }, host.OrderNow.Select(spec => spec.Id).ToArray());
			IReadOnlyCollection<string> before = host.HiddenNow;
			Assert.Empty(before);

			host.EnterEdit();
			host.Editor.HideBlock(host.OrderNow.First(spec => spec.Id == "a"));

			Assert.Empty(before);
			Assert.Equal(new[] { "a", "b" }, host.OrderNow.Select(spec => spec.Id).ToArray());
			Assert.Equal(new[] { "b" }, C3aTree.RenderedIds(host).ToArray());
			Assert.Contains("a", host.HiddenNow);
		});
	}

	// ————— 接线(静态事件 / 主题) —————

	/// <summary>来源:Windows 源 <c>App.ThemeVisualsChanged</c> 的 mac 等价物
	/// <c>ActualThemeVariantChanged</c> —— 编辑态蒙版要按新主题色重刷。</summary>
	[Fact]
	public void Theme_variant_change_refreshes_the_edit_tint()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			using var ui = UiHost.Show(host);
			host.EnterEdit();
			UiHost.Flush();

			LayoutCard card = C3aTree.RenderedCards(host)[0];
			Color before = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
			// 编辑态蒙版 = 主题强调色 + alpha 10(LayoutEditor.LayoutCard.ApplyLook)
			Assert.Equal(10, (int)before.A);
			Color lightAccent = BlockHost.AccentColor();
			Assert.Equal(lightAccent.R, before.R);
			Assert.Equal(lightAccent.G, before.G);
			Assert.Equal(lightAccent.B, before.B);

			Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
			UiHost.Flush();

			Color after = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
			Assert.Equal(10, (int)after.A);
			Color darkAccent = BlockHost.AccentColor();
			Assert.Equal(darkAccent.R, after.R);
			Assert.Equal(darkAccent.G, after.G);
			Assert.Equal(darkAccent.B, after.B);
			// 深浅两套色板的强调色不同 → 蒙版真的跟着主题重刷了(不是留着上一次的笔刷)
			Assert.NotEqual(before, after);
		});
	}

	/// <summary>来源:Windows 源 <c>LayoutStoreV2.LayoutChanged</c>(D5)—— 别的编辑器提交了布局,
	/// 已挂树的宿主靠它当场重排(不重建页面)。</summary>
	[Fact]
	public void LayoutChanged_from_another_editor_reloads_this_page()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost first = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			BlockHost second = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			using var uiA = UiHost.Show(first);
			using var uiB = UiHost.Show(second);
			Assert.Equal(new[] { "a", "b" }, C3aTree.RenderedIds(first).ToArray());

			second.EnterEdit();
			second.Editor.HideBlock(second.OrderNow.First(spec => spec.Id == "b"));
			UiHost.Flush();

			Assert.Equal(new[] { "a" }, C3aTree.RenderedIds(first).ToArray());
			Assert.Contains("b", first.HiddenNow);
		});
	}

	// ————— 仍留在宿主里的静态助手 —————

	/// <summary>来源:Windows 源 <c>AccentColor()</c> —— 取色板 <c>B.Accent</c>,取不到才回落。</summary>
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

	/// <summary>来源:Windows 源 <c>BlockCard</c> 构造尾部 —— 内容已挂在别的 <c>ContentControl</c> 上时先摘下来。</summary>
	[Fact]
	public void DetachFromForeignHost_removes_the_content_from_another_control()
	{
		OnAvalonia(() =>
		{
			Border content = new Border { Height = 20.0 };
			ContentControl foreign = new ContentControl { Content = content };
			using var shown = UiHost.Show(foreign);
			Assert.NotNull(content.Parent);

			BlockHost.DetachFromForeignHost(new ContentControl(), content);

			Assert.Null(foreign.Content);
		});
	}

	/// <summary>来源:同上 —— 内容已经在自己这具 <c>ContentControl</c> 里时不许摘(Avalonia 走 ContentPresenter 的
	/// <c>TemplatedParent</c> 认亲)。</summary>
	[Fact]
	public void DetachFromForeignHost_keeps_the_content_in_its_own_control()
	{
		OnAvalonia(() =>
		{
			Border content = new Border { Height = 20.0 };
			ContentControl mine = new ContentControl();
			using var shown = UiHost.Show(mine);
			mine.Content = content;
			UiHost.Flush();
			Assert.NotNull(content.Parent);

			BlockHost.DetachFromForeignHost(mine, content);

			Assert.Same(content, mine.Content);
		});
	}

	/// <summary>来源:mac 的编辑工具条(R32-MED 版,8 个动作;Windows 的四个在最后)。</summary>
	[Fact]
	public void Toolbar_carries_the_eight_editor_actions_with_the_four_windows_ones_last()
	{
		OnAvalonia(() =>
		{
			using var home = new C3aHome();
			BlockHost host = C3aBuild.Host("p", C3aBuild.Spec("a"), C3aBuild.Spec("b"));
			host.EnterEdit();

			Grid barGrid = Assert.IsAssignableFrom<Grid>(C3aTree.BarHostOf(host).Child);
			WrapPanel row = Assert.IsAssignableFrom<WrapPanel>(barGrid.Children[1]);
			string[] labels = row.Children.OfType<Button>().Select(ButtonLabel).ToArray();
			Assert.Equal(8, labels.Length);
			// 吸附档按钮的文案是「吸附 N」(当前档位),其余七个是固定文案(顺序照 LayoutEditor 的工具条)
			Assert.True(labels[2].StartsWith(I18n.L("吸附"), StringComparison.Ordinal), labels[2]);
			Assert.Equal(
				new[] { "撤销", "重做", "全部折叠", "完成编辑", "重置本页", "导出布局", "导入布局" }.Select(I18n.L).ToArray(),
				new[] { labels[0], labels[1], labels[3], labels[4], labels[5], labels[6], labels[7] });
		});
	}

	/// <summary>工具条按钮的文案:普通按钮是字符串;吸附档按钮在 RefreshToolbar 里被换成 TextBlock。</summary>
	private static string ButtonLabel(Button button)
	{
		return button.Content switch
		{
			string text => text,
			TextBlock block => block.Text ?? "",
			_ => ""
		};
	}
}