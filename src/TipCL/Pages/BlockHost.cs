using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TipCL.Pages.Layout;
using TipCL.Services;

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
}

// 页面区块宿主(WP-U1 冻结契约,公共 API 一字不改):
//   常态按布局文档顺序渲染非隐藏区块,视觉零装饰(间距/边距对齐 PageBase.ScrollBody,默认渲染与旧版逐像素等价);
//   Root 右键「自定义布局…」(specs.Count >= 2 才有该菜单项)进入编辑模式;
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

	public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null)
	{
		_editor = new LayoutEditor(pageId, specs, contentMargin);
		_themeRefresh = _editor.RefreshThemeVisuals;
		_storeRefresh = OnLayoutStoreChanged;
		_editor.Changed += delegate
		{
			Changed?.Invoke();
		};
		base.Focusable = false;
		base.Content = _editor.Root;
		// 入口:右键菜单(specs.Count >= 2 才有「自定义布局...」;页面内容自带 ContextMenu 时天然对方优先)。
		// 注意:门槛与 _editor.CanEdit 解耦 —— 单块页**能编辑**(设置页的入口按钮用 CanEdit),但右键菜单
		// 在单块页上会是个没用的项(连"顺序"都没有),所以这里仍按 >= 2 出现。
		if (specs != null && specs.Count >= 2)
		{
			ContextMenu contextMenu = new ContextMenu();
			MenuItem menuItem = new MenuItem
			{
				Header = I18n.L("自定义布局...")
			};
			menuItem.Click += delegate
			{
				_editor.EnterEdit();
			};
			contextMenu.Items.Add(menuItem);
			base.ContextMenu = contextMenu;
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
		};
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
