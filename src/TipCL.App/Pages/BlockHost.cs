using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

// 页面区块描述:页面把可重排的内容切成若干 BlockSpec 交给 BlockHost。
// Id 是稳定英文 id(进布局文档);Title 必须是已经过 I18n.L 的现成文案;
// Content 由页面自己构建,BlockHost 只负责搬运与重排,不关心内容内部结构。
public sealed class BlockSpec
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public Control? Content { get; set; }

	public bool DefaultVisible { get; set; } = true;
}

// R32-MWG:Windows 版 Pages\BlockHost.cs(WPF / net48,181 行,layout-shell-hooks @ fa1b3d7)
// 到 mac(Avalonia 11 / net8.0)的移植。L1 起本类只是 LayoutEditor(布局引擎 + v2 持久化)的薄壳:
//   * 不再直接读写 ui_page_layouts.json —— 真源是 ui_layout_v2.json(LayoutStoreV2),
//     v1 由 v2 单向写通(见 Pages\LayoutV1BridgePages.cs 与 App.OnFrameworkInitializationCompleted 的装配);
//   * BlockSpec / 构造签名 / Root / Changed / PageId / EnterEdit 均与 C3a 的 v1 版本逐字一致,
//     页面(AccountPage / DownloadPage / FeedbackPage / JavaPage / LaunchPage)的调用形状零改动。
//
// Windows → mac 差异(逐条):
// 1) 主题刷新:Windows 订阅 App.ThemeVisualsChanged;mac 的 App 没有这个全局事件(C2b 起外壳用
//    ActualThemeVariantChanged + ApplyThemeVisuals 自己刷),所以这里订阅控件自己的
//    StyledElement.ActualThemeVariantChanged —— 同样是"有效主题变体变了"这一刻,
//    同样是 Loaded 订阅 / Unloaded 取消 / 订阅后立刻刷一次,顺序与 Windows 逐行对应。
// 2) Toast:AppServices.Window?.Toast(...) → AppServices.Toast(...)(C2b 的统一门面)。
// 3) 线程:base.Dispatcher.CheckAccess()/BeginInvoke → Dispatcher.UIThread.CheckAccess()/Post。
// 4) FrameworkElement / UIElement → Control。
// 5) E-01(与 Linux 端口同口径):宿主**不再自挂 ContextMenu** —— 右键「自定义布局...」统一由主窗口的
//    LayoutShellHooks 全局钩子弹(钩子沿祖先链遇到自带菜单就让位)。Avalonia 的 ContextMenu 不做属性继承链
//    合并,宿主一挂,整页右键就只剩这一项。Windows 是每宿主自挂菜单,这是有意的平台差异。
// 6) 入口条件:Windows 用 specs.Count >= 1 才挂菜单;现在由 LayoutEditor.CanEdit(内部 specs.Count >= 1)把关,
//    EnterEdit() 返回 false 表示这一页没有可编辑的区块(调用方据此给提示)。
public sealed class BlockHost : UserControl
{
	private readonly LayoutEditor _editor;

	// 构造时收下的区块描述(与 LayoutEditor 同一条过滤规则:非 null 且 Id 非空)。
	// 只给下面的测试视窗 OrderNow 用,生产路径(Root / EnterEdit / Changed)不读它。
	private readonly List<BlockSpec> _specs = new List<BlockSpec>();

	// 主题刷新的稳定委托引用(静态事件必须能对称 -=,所以缓存一份而不是每次都新建闭包)
	private readonly EventHandler _themeRefresh;

	// 布局文档变更的稳定委托引用(LayoutStoreV2.LayoutChanged 是静态事件,同样必须对称 -=)
	private readonly Action _storeRefresh;

	public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null, PageLayoutV2? defaultPage = null)
	{
		if (specs != null)
		{
			foreach (BlockSpec spec in specs)
			{
				if (spec != null && !string.IsNullOrEmpty(spec.Id))
				{
					_specs.Add(spec);
				}
			}
		}
		_editor = new LayoutEditor(pageId, specs, contentMargin, defaultPage);
		_themeRefresh = OnThemeVariantChanged;
		_storeRefresh = OnLayoutStoreChanged;
		_editor.Changed += delegate
		{
			Changed?.Invoke();
		};
		base.Focusable = false;
		base.Content = _editor.Root;
		// 入口:右键菜单统一由主窗口的全局钩子弹(LayoutShellHooks:「自定义布局...」/ 编辑中的「完成编辑」
		// 「重置本页布局」/「布局设置...」)。宿主自己不再挂 ContextMenu(E-01)。
		base.Loaded += delegate
		{
			ActualThemeVariantChanged -= _themeRefresh;
			ActualThemeVariantChanged += _themeRefresh;
			_editor.RefreshThemeVisuals();
			// 布局文档变了(设置页应用整机方案 / 别的编辑器提交)→ 自己重排:
			// 这是"一键切换方案后页面区块当场刷新"的接线(D1);Unloaded 时对称退订,
			// 避免页面销毁后还挂着静态事件。
			LayoutStoreV2.LayoutChanged -= _storeRefresh;
			LayoutStoreV2.LayoutChanged += _storeRefresh;
			// 进可视树时补读一次:页面是缓存复用的(切走再切回来只会触发 Loaded),
			// 期间别人改过布局文档 / 窗口跨过窄窗阈值时,这一读保证拿到的是当前变体那一份。
			_editor.ReloadFromStore();
		};
		base.Unloaded += delegate
		{
			ActualThemeVariantChanged -= _themeRefresh;
			LayoutStoreV2.LayoutChanged -= _storeRefresh;
			// 页面被切走(缓存复用页面的宿主进了 ContentControl 的更替)时,还停在编辑态就自动收尾 ——
			// 保存布局、收起工具栏、提示「布局已保存」,避免"切页面回来还残留编辑态"。
			// 进入编辑后什么都没动的话,布局会照常还原(见 LayoutEditor.ExitEdit)。
			if (_editor.IsEditing)
			{
				try
				{
					_editor.ExitEdit();
				}
				catch
				{
					// 窗口正在销毁 / 调度器已关:收尾失败也不能崩
				}
			}
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
			if (Dispatcher.UIThread.CheckAccess())
			{
				editor.ReloadFromStore();
			}
			else
			{
				Dispatcher.UIThread.Post(editor.ReloadFromStore);
			}
		}
		catch
		{
			// 页面正在销毁 / 调度器已关:不重排也不能崩
		}
	}

	// 有效主题变体变了 → 让编辑器把主题相关的笔刷/蒙版重刷一遍(C3a 的 RefreshThemeVisuals 语义)。
	// Avalonia 的 ActualThemeVariantChanged 是 EventHandler(object?, EventArgs),不是无参 Action。
	private void OnThemeVariantChanged(object? sender, EventArgs e)
	{
		_editor.RefreshThemeVisuals();
	}

	/// <summary>
	/// 页面挂可视树用的根元素。Windows 的返回类型是 <c>FrameworkElement</c>;Avalonia 里对应的
	/// 基类是 <see cref="Control"/>(<c>Layoutable.MaxHeight</c> 绑定照样写在它身上)。
	/// </summary>
	public Control Root => this;

	/// <summary>本宿主的页面 id(与布局文档里的 pageId 一致)。</summary>
	public string PageId => _editor.State.PageId;

	/// <summary>进入编辑态并提示(全局右键菜单 / 页面自己的入口走这里)。
	/// 返回 false = 这一页没有可编辑的区块(调用方据此给"没有可拖动的区块"的提示)。
	/// 提示只用现成的 Toast(title, body, kind) —— 不要自己拼图标 / 富文本。</summary>
	public bool EnterEdit()
	{
		if (!_editor.CanEdit)
		{
			return false;
		}
		if (!_editor.IsEditing)
		{
			_editor.EnterEdit();
			AppServices.Toast(I18n.L("已进入自定义布局"), I18n.L("按住区块拖动即可移动,完成编辑后自动保存"), ToastKind.Success);
		}
		return true;
	}

	public event Action? Changed;

	/// <summary>内部编辑器(调试与宿主内测试用)。</summary>
	internal LayoutEditor Editor => _editor;

	// 测试视窗(内部):C3c / C5a / C6a 这些"页面接线"集成测试是按 v1 宿主的
	// OrderNow(区块顺序)/ HiddenNow(隐藏 id 集合)断言的。v1 版这两个事实由
	// BlockHost 自己持有;现在只在 LayoutEditor.State 里 —— 这里做只读投影,不参与任何生产路径,
	// 也不产生可写的第二份事实。
	// 口径与 v1 一致:隐藏块**也留在顺序里**(隐藏项保留原槽位,恢复时回到原位),只是不进渲染列表;
	// 隐藏块的 Spec 仍然能拿到(Title/Content 由页面自己构建),这正是设置页「区块标题 / 内容非空」
	// 断言需要的。显隐另看 HiddenNow。
	internal IReadOnlyList<BlockSpec> OrderNow
	{
		get
		{
			LayoutPageState state = _editor.State;
			List<BlockSpec> ordered = new List<BlockSpec>(_specs.Count);
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string id in state.Order)
			{
				BlockSpec? spec = FindSpec(id);
				if (spec == null)
				{
					continue;
				}
				seen.Add(id);
				ordered.Add(spec);
			}
			// State.Order 只装"装载这一页时认识的 id"(文档里未知的 id 会被丢掉);
			// 兜底补上还没进状态的块,保持页面声明顺序。
			foreach (BlockSpec spec in _specs)
			{
				if (!seen.Contains(spec.Id))
				{
					ordered.Add(spec);
				}
			}
			return ordered;
		}
	}

	/// <summary>隐藏区块 id 的只读快照(测试视窗,同上)。</summary>
	internal IReadOnlyCollection<string> HiddenNow => new List<string>(_editor.State.Hidden);

	private BlockSpec? FindSpec(string id)
	{
		foreach (BlockSpec spec in _specs)
		{
			if (string.Equals(spec.Id, id, StringComparison.Ordinal))
			{
				return spec;
			}
		}
		return null;
	}

	// Windows 兜底色(取不到 B.Accent 时用它,不是 Palette 的兜底灰)。
	// 留在 BlockHost 是因为 LayoutVisualTokens.AccentColor() 与 LayoutEditor.AccentColor() 都转调这里,
	// 搬走会形成 LayoutEditor → LayoutVisualTokens → LayoutEditor 的循环。
	internal static Color AccentColor()
	{
		if (Palette.TryGet("B.Accent", Palette.Current, out object? value))
		{
			switch (value)
			{
				case ISolidColorBrush solid:
					return solid.Color;
				case Color color:
					return color;
			}
		}
		return Color.FromRgb(46, 155, 107);
	}

	/// <summary>
	/// "内容已挂在别的宿主上就先摘下来"(Windows 的
	/// <c>spec.Content is FrameworkElement { Parent: ContentControl parent }</c>)。
	/// Avalonia 的差别:ContentControl 的内容先落进模板里的 ContentPresenter,所以内容控件的
	/// <c>Parent</c> 是 ContentPresenter 而不是 ContentControl —— 两种父级都认,
	/// 并且用 <c>TemplatedParent</c> 认出"是不是自己这具 ContentControl"。
	/// 现只被 DashHost.SetContent 使用(LayoutEditor 的 LayoutCard 有同构的内联版本)。
	/// </summary>
	internal static void DetachFromForeignHost(ContentControl mine, Control? content)
	{
		if (content?.Parent is not { } parent)
		{
			return;
		}
		switch (parent)
		{
			case ContentControl owner when owner != mine:
				owner.Content = null;
				break;
			case ContentPresenter presenter when presenter.TemplatedParent != mine:
				presenter.Content = null;
				break;
		}
	}
}