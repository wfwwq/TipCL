using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TipCL.Pages.Layout;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages;

// 页面区块描述:页面把可重排的内容切成若干 BlockSpec 交给 BlockHost。
// Id 是稳定英文 id(进布局文档);Title 必须是已经过 I18n.L 的现成文案;
// Content 由页面自己构建,BlockHost 只负责搬运与重排,不关心内容内部结构。
public sealed class BlockSpec
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public UIElement? Content { get; set; }

	public bool DefaultVisible { get; set; } = true;

	/// <summary>
	/// 占满剩余高度(网格 / 画布里):内容自带滚动(AI 对话区、历史列表这类)时置 true ——
	/// 区块不再套外层滚动、也不按内容需求高无限撑开,而是拿到一个有限高度,内容自己的滚动与吸底照常工作。
	/// 缺省 false = 现状(跟随内容高度)。
	/// </summary>
	public bool Fill { get; set; }

	/// <summary>
	/// 网格模式下的最小列宽(12 列制;0 = 只用全局下限 2 列)。
	/// 内容本身需要一定宽度才看得见的块(插件列表这类)设一下:旧存档里被缩成 1 列的块渲染时自动撑回来,
	/// 不再出现"内容区块被压成一条细线、插件全看不见"(2026-11-19 录屏 1:06)。只影响渲染,不改写用户存档。
	/// </summary>
	public int MinCols { get; set; }
}

/// <summary>
/// 页面的「原生默认排版」:有些页面的默认外观不是纵向流(左右分栏、上中下三段且中间那段要占满高度),
/// 用 stack / grid 都没法做到与改动前逐像素一致。这类页面把自己原来的排版根交给宿主:
///   布局文档里还没有这一页(从没自定义过)且不在编辑态时,宿主直接显示 <see cref="Root"/>
///   (先调 <see cref="Attach"/> 把各块内容挂回原来的位置)—— 默认外观与改动前完全相同;
///   进编辑态 / 已有自定义布局时,先调 <see cref="Detach"/> 把内容从原排版里摘下来,再由区块卡片承载。
/// Attach / Detach 必须成对、可重复调用(宿主会在两种形态之间来回切换)。
/// </summary>
public sealed class BlockNativeLayout
{
	/// <summary>原排版的根元素(宿主把它放在内容区,占满整页高度)。</summary>
	public FrameworkElement? Root { get; set; }

	/// <summary>把各块内容挂回原排版(此时内容已经不在任何区块卡片里)。</summary>
	public Action? Attach { get; set; }

	/// <summary>把各块内容从原排版里摘下来(之后交给区块卡片)。</summary>
	public Action? Detach { get; set; }
}

/// <summary>
/// 各页「页面默认布局」(R34 §5.5 / D-81 Q4):布局文档里**还没有**这一页时,宿主用它代替「白名单纵向流」;
/// 文档里有这一页(用户存过 / 导入过 / 应用过方案)就完全照文档,这里不参与(判定在 LayoutEditor.LoadFromStore)。
/// 只管联机 / 游戏时长 / Java / 启动下方内容区这 4 页;实例库的默认仍在 LibraryPage 里。
/// 纯数据、不碰 WPF 类型 —— net8 测试宿主可以直接调用(页面类本身是 UserControl,测试里加载不了)。
/// 约定:
///   • 网格一律 12 列(与实例库同制;编辑态把 stack 折成网格时也是 12 列),效果图里的 6 列只是示意;
///   • 可见块写显式坐标(x / y / w = span),和 LibraryPage 的默认同一种写法;
///   • 默认隐藏的块只给列跨度、不给坐标(契约默认矩形 0,0,1,1 = 自动流,见 LayoutGrid.Pack),并且排在最后:
///     在「添加」里显示出来时按顺序接在已有块后面、两两并排,不会都挤进第 0 列或压到别的块上。
/// 每次调用都返回新对象(宿主会再 Clone 一次,这里不共享实例)。
/// </summary>
public static class PageDefaultLayouts
{
	/// <summary>这几份默认网格的列数。</summary>
	public const int Cols = 12;

	/// <summary>联机:说明(整行)/ 联机状态 + 房间(各半)/ 快捷操作(整行)/ 玩家 + 节点说明(4:2 = 8:4 列)。</summary>
	public static PageLayoutV2 Multiplayer()
	{
		return Grid(At("header", 0, 0, 12), At("state", 0, 1, 6), At("room", 6, 1, 6), At("actions", 0, 2, 12), At("players", 0, 3, 8), At("nodes", 8, 3, 4));
	}

	/// <summary>游戏时长:累计时长 + 时间范围(各半)/ 明细(整行);概览默认隐藏。</summary>
	public static PageLayoutV2 Playtime()
	{
		return Grid(At("total", 0, 0, 6), At("range", 6, 0, 6), At("list", 0, 1, 12), Off("header", 12));
	}

	/// <summary>Java:安装方式 + 实例默认(各半)/ 已检测的 Java(整行);说明默认隐藏。</summary>
	public static PageLayoutV2 Java()
	{
		return Grid(At("install", 0, 0, 6), At("defaults", 6, 0, 6), At("list", 0, 1, 12), Off("intro", 12));
	}

	/// <summary>启动页下方内容区(英雄区不进布局):新闻整行;下载任务 / 游戏时长 / 便签显示后两两并排,日志占整行,四块都默认隐藏。</summary>
	public static PageLayoutV2 LaunchContent()
	{
		return Grid(At("news", 0, 0, 12), Off("tasks", 6), Off("playtime", 6), Off("notes", 6), Off("log", 12));
	}

	/// <summary>按页面 id 取默认布局;不在这 4 页里的返回 null(= 白名单纵向流,或页面自己给的默认)。</summary>
	public static PageLayoutV2? ForPage(string? pageId)
	{
		switch (pageId)
		{
		case "multiplayer":
			return Multiplayer();
		case "playtime":
			return Playtime();
		case "java":
			return Java();
		case "launch":
			return LaunchContent();
		default:
			return null;
		}
	}

	private static PageLayoutV2 Grid(params BlockLayout[] blocks)
	{
		return new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = Cols,
			Blocks = new List<BlockLayout>(blocks)
		};
	}

	// 可见块:显式位置,宽 = 列跨度
	private static BlockLayout At(string id, int x, int y, int span)
	{
		return new BlockLayout
		{
			Id = id,
			X = x,
			Y = y,
			W = span,
			H = 1.0,
			Span = span
		};
	}

	// 默认隐藏块:只给列跨度(坐标保持契约默认 0,0,1,1 → 显示时自动流到末尾的空位)
	private static BlockLayout Off(string id, int span)
	{
		return new BlockLayout
		{
			Id = id,
			Hidden = true,
			Span = span
		};
	}
}

/// <summary>区块宿主的入口规则(纯函数,net8 测试可直接调用)。</summary>
public static class BlockHostRules
{
	/// <summary>至少几个块才挂「自定义布局...」右键菜单(D-81 Q5)。</summary>
	public const int MinBlocksForLayoutMenu = 2;

	/// <summary>只有一块内容的页(设置里的「下载与网络」「反馈与关于」)没什么可排的,右键不给「自定义布局...」。</summary>
	public static bool OffersLayoutMenu(int blockCount)
	{
		return blockCount >= MinBlocksForLayoutMenu;
	}
}

// 页面区块宿主(WP-U1 冻结契约,公共 API 一字不改):
//   常态按布局文档顺序渲染非隐藏区块,视觉零装饰(间距/边距对齐 PageBase.ScrollBody,默认渲染与旧版逐像素等价);
//   Root 右键「自定义布局…」(specs.Count >= 2 才有该菜单项,见 BlockHostRules;R34 按 D-81 Q5 改回这条)进入编辑模式;
//   编辑态的拖拽重排 / 显隐 / 托盘 / 工具条在 LayoutEditor 里。
//   L1 起:本类只是 LayoutEditor(布局引擎 + v2 持久化)的薄壳 —— 不再直接读写 ui_page_layouts.json,
//   真源是 ui_layout_v2.json(LayoutStoreV2),v1 由 v2 单向写通;BlockSpec / 构造签名 / Root / Changed 均未变。
public sealed class BlockHost : UserControl
{
	private readonly LayoutEditor _editor;

	// 主题刷新的稳定委托引用(静态事件必须能对称 -=,所以缓存一份而不是每次都新建闭包)
	private readonly Action _themeRefresh;

	// 布局文档变更的稳定委托引用(LayoutStoreV2.LayoutChanged 是静态事件,同样必须对称 -=)
	private readonly Action _storeRefresh;

	// 页面交进来的块数(只数页面自己的内置块,不含文档里的自建 / 插件块)
	private readonly int _specCount;

	public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null, PageLayoutV2? defaultPage = null)
		: this(pageId, specs, contentMargin, defaultPage, null)
	{
	}

	/// <summary>
	/// 带「原生默认排版」的构造(AI 页 / 版本管理页这类默认不是纵向流的页面用):
	/// 没有自定义过布局时显示 <paramref name="native"/> 的原排版(默认外观逐像素不变),
	/// 进编辑态时按 <paramref name="defaultPage"/>(网格)把各块摆出来供拖动。
	/// </summary>
	public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin, PageLayoutV2? defaultPage, BlockNativeLayout? native)
	{
		PageId = pageId ?? "";
		_editor = new LayoutEditor(pageId, specs, contentMargin, defaultPage, native);
		_themeRefresh = _editor.RefreshThemeVisuals;
		_storeRefresh = OnLayoutStoreChanged;
		_editor.Changed += delegate
		{
			Changed?.Invoke();
		};
		base.Focusable = false;
		base.Content = _editor.Root;
		_specCount = specs?.Count ?? 0;
		// 入口:右键菜单 —— 至少 2 个块才有「自定义布局...」(D-81 Q5;页面内容自带 ContextMenu 时天然对方优先)。
		// 第八轮曾放宽到 ≥1,原因是当时启动页宿主里只有一块;现在启动页宿主里有 5 块,不受影响。
		// 只剩「下载与网络」「反馈与关于」这类单块页:整页就是一块,没什么可排的。
		// 菜单项在打开前按状态重建:常态 =「自定义布局...」;编辑态 =「完成编辑」「重置本页」。
		if (BlockHostRules.OffersLayoutMenu(_specCount))
		{
			ContextMenu contextMenu = new ContextMenu();
			contextMenu.Items.Add(new MenuItem
			{
				Header = I18n.L("自定义布局...")
			});
			base.ContextMenu = contextMenu;
			base.ContextMenuOpening += delegate
			{
				RebuildContextMenu(contextMenu);
			};
			RebuildContextMenu(contextMenu);
		}
		else if (_specCount > 0)
		{
			// 单块页:挂一个空的占位菜单并把它禁用。
			// ① 壳级全局右键(LayoutShellHooks)见到「自带菜单」就让路,不会在这里弹「自定义布局...」;
			// ② ContextMenuService.IsEnabled = false:占位菜单自己永远不弹,右键事件照常往外层冒泡;
			// ③ 内容里自带 ContextMenu 的控件(离鼠标更近)不受影响。
			// 设置 → 布局里的入口按钮走 Editor.CanEdit,不经过这里。
			base.ContextMenu = new ContextMenu();
			ContextMenuService.SetIsEnabled(this, false);
		}
		base.Loaded += delegate
		{
			App.ThemeVisualsChanged -= _themeRefresh;
			App.ThemeVisualsChanged += _themeRefresh;
			_editor.RefreshThemeVisuals();
			// 布局文档变了(设置页应用整机方案 / 别的编辑器提交)→ 自己重排:
			// 这是"一键切换方案后页面区块当场刷新"的接线(D1);Unloaded 时对称退订,避免页面销毁后还挂着静态事件。
			LayoutStoreV2.LayoutChanged -= _storeRefresh;
			LayoutStoreV2.LayoutChanged += _storeRefresh;
			// 进可视树时补读一次:页面是缓存复用的(切走再切回来只会触发 Loaded),
			// 期间别人改过布局文档 / 窗口跨过窄窗阈值时,这一读保证拿到的是当前变体那一份。
			_editor.ReloadFromStore();
		};
		base.Unloaded += delegate
		{
			App.ThemeVisualsChanged -= _themeRefresh;
			LayoutStoreV2.LayoutChanged -= _storeRefresh;
			// 切页面/关窗口时若还在编辑态就退出(落盘 + 收掉工具条)。
			// 原来不退:切走再切回来,编辑态和工具条还残留着(录屏一里的"编辑态残留")。
			try
			{
				if (_editor.IsEditing)
				{
					_editor.ExitEdit();
				}
			}
			catch
			{
				// 页面正在销毁,不能因为收尾抛异常崩窗口
			}
		};
	}

	/// <summary>本宿主的页面 id(v2 文档里的键,例如 <c>launch</c> / <c>library:main</c>)。</summary>
	public string PageId { get; }

	/// <summary>
	/// 右键是否给「自定义布局...」(≥2 块,D-81 Q5)。壳级全局右键挑目标宿主时也应跳过这里为 false 的宿主
	/// (鼠标不在任何宿主里、靠「最大的宿主」兜底的那条路径)。internal:不动 WP-U1 冻结的公共 API。
	/// </summary>
	internal bool OffersLayoutMenu
	{
		get
		{
			return BlockHostRules.OffersLayoutMenu(_specCount);
		}
	}

	/// <summary>是否提供编辑入口(v3:≥ 2 个块才可编辑;单块页返回 false)。</summary>
	public bool CanEdit
	{
		get
		{
			return _editor.CanEdit;
		}
	}

	private void RebuildContextMenu(ContextMenu menu)
	{
		menu.Items.Clear();
		if (_editor.IsEditing)
		{
			MenuItem done = new MenuItem
			{
				Header = I18n.L("完成编辑")
			};
			done.Click += delegate
			{
				_editor.ExitEdit();
			};
			menu.Items.Add(done);
			MenuItem reset = new MenuItem
			{
				Header = I18n.L("重置本页")
			};
			reset.Click += delegate
			{
				_editor.ResetPageFromMenu();
			};
			menu.Items.Add(reset);
			return;
		}
		MenuItem open = new MenuItem
		{
			Header = I18n.L("自定义布局...")
		};
		open.Click += delegate
		{
			_editor.EnterEdit();
		};
		menu.Items.Add(open);
	}

	/// <summary>是否处于编辑态。</summary>
	public bool IsEditing
	{
		get
		{
			return _editor.IsEditing;
		}
	}

	/// <summary>进入编辑态(右键菜单、设置页入口、全局右键都走这里)。</summary>
	public void EnterEdit()
	{
		_editor.EnterEdit();
	}

	/// <summary>退出编辑态(落盘 + 收工具条)。</summary>
	public void ExitEdit()
	{
		_editor.ExitEdit();
	}

	/// <summary>某块当前是否隐藏。</summary>
	public bool IsBlockHidden(string id)
	{
		return _editor.IsBlockHidden(id);
	}

	/// <summary>直接显隐某块并落盘(不必先进编辑态)。</summary>
	public void SetBlockHidden(string id, bool hidden)
	{
		_editor.SetBlockHidden(id, hidden);
	}

	/// <summary>当前是否正显示页面的原生默认排版(没自定义过布局、也不在编辑态)。</summary>
	public bool IsNativeDefault
	{
		get
		{
			return _editor.IsNativeDefault;
		}
	}

	/// <summary>按滚轮增量滚动区块内容区(能滚才滚;返回是否滚了)。鼠标在宿主之外时页面可以把滚轮转交过来。</summary>
	public bool ScrollBy(int delta)
	{
		return _editor.ScrollBy(delta);
	}

	/// <summary>重置本页为出厂布局(右键菜单第二入口)。</summary>
	public void ResetPage()
	{
		_editor.ResetPageFromMenu();
	}

	/// <summary>就地改当前页布局并落盘(铺满整行这类调整)。</summary>
	public void MutatePage(Action<PageLayoutV2> mutate)
	{
		_editor.MutatePage(mutate);
	}

	// LayoutChanged 是静态事件,可能从任意线程来 → 统一回到 UI 线程重排;
	// 自己刚提交过的那一次跳过(编辑器已经重排过了,避免同一次改动渲染两遍)。
	private void OnLayoutStoreChanged()
	{
		LayoutEditor editor = _editor;
		if (editor.IsSaving)
		{
			return;
		}
		try
		{
			System.Windows.Threading.Dispatcher dispatcher = base.Dispatcher;
			if (dispatcher == null)
			{
				return;
			}
			if (dispatcher.CheckAccess())
			{
				editor.ReloadFromStore();
			}
			else
			{
				dispatcher.BeginInvoke((Action)editor.ReloadFromStore);
			}
		}
		catch
		{
			// 页面正在销毁 / 调度器已关:不重排也不能崩
		}
	}

	public FrameworkElement Root
	{
		get
		{
			return this;
		}
	}

	public event Action? Changed;

	/// <summary>内部编辑器(调试与宿主内测试用)。</summary>
	internal LayoutEditor Editor
	{
		get
		{
			return _editor;
		}
	}
}
