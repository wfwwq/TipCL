using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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

// C3a:Windows 版 Pages\BlockHost.cs(WPF)到 Avalonia 11 的移植。契约(WP-U1 冻结)一字未改:
//   常态按布局文档顺序渲染非隐藏区块,视觉零装饰(间距/边距对齐 PageBase.ScrollBody,默认渲染与旧版逐像素等价);
//   Root 右键「自定义布局...」(specs.Count >= 2 才有该菜单项)进入编辑模式:
//   每块顶部操作条 = 拖拽手柄 + 块标题 + 显隐按钮;按鼠标 Y 与相邻块中点比较实时交换重排;
//   底部「已隐藏区块」托盘(+ 标题 chip,点击恢复)与悬浮工具条(完成 / 重置本页 / 导出 / 导入)。
//   任何变更组装 PageLayoutDoc 即时 PageLayoutStore.Save 并触发 Changed;退出编辑自动保存。
//   页面内容若自带 ContextMenu,就近者优先,互不冲突。
//
// mac 侧移植差异(逐条也记在 PROGRESS.md 的 C3a 差异表里):
// 1. 框架类型:UIElement / FrameworkElement → Control;Visibility.Collapsed → IsVisible = false;
//    MouseButtonEventArgs → PointerPressedEventArgs、MouseEventArgs → PointerEventArgs、
//    MouseLeftButtonUp → PointerReleased、LostMouseCapture → PointerCaptureLost。
// 2. 光标:WPF 的 Cursors.SizeAll → new Cursor(StandardCursorType.SizeAll)(Avalonia 11 没有
//    Cursors 静态类,Ui.RowCard 已经是这个写法)。
// 3. 事件阶段:WPF 用 PreviewMouseLeftButtonDown(隧道)。Avalonia 的 Button 类处理器会把
//    PointerPressed / PointerReleased 标记为已处理,所以本类统一用
//    AddHandler(..., RoutingStrategies.Bubble, handledEventsToo: true) —— 语义等价(手柄拿得到按下),
//    而且只在本块是当前拖拽块时才动状态。
// 4. 主题变更:Windows 订阅 App.ThemeVisualsChanged;mac 的 App 没有这个全局事件
//    (C2b 起外壳用 ActualThemeVariantChanged + ApplyThemeVisuals 自己刷,见 MainWindow.cs:1066 的注释),
//    所以这里订阅控件自己的 StyledElement.ActualThemeVariantChanged —— 同样是"有效主题变体变了"这一刻,
//    同样是 Loaded 订阅 / Unloaded 取消 / 订阅后立刻刷一次,顺序与 Windows 逐行对应。
// 5. 取主题色:Application.Current.TryFindResource("B.Accent") → Palette.TryGet(..., "B.Accent", ...);
//    取不到时沿用 Windows 的兜底绿 Color.FromRgb(46,155,107)(**不是** Palette 的兜底灰)。
// 6. Toast:AppServices.Window?.Toast(...) → AppServices.Toast(...)(C2a 起的统一门面)。
// 7. BlockCard 里 `Ui.G("Auto", "*")` 与 `grid.Add(_bodyWrap, 1)` 对不上(只定义了 1 行却放进第 2 行)。
//    这是 Windows 源本身的写法,本包**逐字照抄**并加了用例钉住实际落位(WPF 与本机 Avalonia 都把
//    越界行号夹到最后一行),没有借移植之机改引擎行为。
//
// 测试可见性:Windows 里这些成员是 private;mac 侧把其中"布局模型必须能驱动/断言"的那几个降为
// internal(不会出现在 public 面上,页面的调用形状完全不变),因为要写 D-115 说的注入缝用例。
public sealed class BlockHost : UserControl
{
	// 块间距,对齐 DashHost 的卡片间隙
	private const double Gap = 10.0;

	private readonly string _pageId;

	private readonly List<BlockSpec> _specs = new List<BlockSpec>();

	// 每个区块一张卡(含编辑态操作条),常驻缓存,重排/显隐只动它在可视树里的位置
	private readonly Dictionary<string, BlockCard> _cards = new Dictionary<string, BlockCard>();

	// 当前显示顺序(含隐藏项:隐藏项留在原槽位,恢复时回到原位置)
	private List<BlockSpec> _order = new List<BlockSpec>();

	private HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal);

	private readonly SPanel _list = new SPanel
	{
		Orientation = Orientation.Vertical,
		Spacing = Gap
	};

	private readonly Border _trayHost = new Border();

	private readonly WrapPanel _tray = new WrapPanel();

	private readonly Border _barHost = new Border();

	private bool _edit;

	// 拖拽手势状态:手柄捕获指针期间按缓存高度计算相邻块中点
	private BlockCard? _dragCard;

	private IPointer? _dragPointer;

	private readonly Dictionary<string, double> _dragHeights = new Dictionary<string, double>();

	// 挂在手柄上的三个处理器:存成字段,WPF 用 += / -= 的那一对在 Avalonia 里就是
	// AddHandler / RemoveHandler,字段化保证取消订阅时拿到的是**同一个**委托实例(不泄漏)。
	private readonly EventHandler<PointerEventArgs> _onDragMove;

	private readonly EventHandler<PointerReleasedEventArgs> _onDragEnd;

	private readonly EventHandler<PointerCaptureLostEventArgs> _onDragLost;

	public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null)
	{
		_onDragMove = OnDragMove;
		_onDragEnd = OnDragEnd;
		_onDragLost = OnDragLost;
		_pageId = pageId ?? "";
		foreach (BlockSpec item in specs ?? Array.Empty<BlockSpec>())
		{
			if (item != null && !string.IsNullOrEmpty(item.Id))
			{
				_specs.Add(item);
			}
		}
		base.Focusable = false;
		// null → (18,14,18,16),对齐 PageBase.ScrollBody 的内容边距
		_list.Margin = contentMargin ?? new Thickness(18.0, 14.0, 18.0, 16.0);
		foreach (BlockSpec spec in _specs)
		{
			_cards[spec.Id] = new BlockCard(this, spec);
		}
		// 底部托盘(编辑态):已隐藏区块以「+ 标题」chip 呈现,点击恢复
		SPanel sPanel = Ui.V(6.0, Ui.Muted(I18n.L("已隐藏的区块")), _tray);
		_trayHost.CornerRadius = new CornerRadius(8.0);
		_trayHost.Padding = new Thickness(10.0);
		_trayHost.IsVisible = false;
		_trayHost.Dyn(Border.BackgroundProperty, "B.Paper2");
		_trayHost.Child = sPanel;
		// 底部悬浮工具条(编辑态):完成 / 重置本页 / 导出布局 / 导入布局
		SPanel sPanel2 = Ui.H(8.0, Ui.Btn(I18n.L("完成编辑"), BtnKind.Primary, delegate
		{
			ExitEdit();
		}), Ui.Btn(I18n.L("重置本页"), BtnKind.Normal, delegate
		{
			ResetPage();
		}), Ui.Btn(I18n.L("导出布局"), BtnKind.Normal, delegate
		{
			PageLayoutIO.ExportToFileDialog(_pageId);
		}), Ui.Btn(I18n.L("导入布局"), BtnKind.Normal, delegate
		{
			ImportLayout();
		}));
		sPanel2.HorizontalAlignment = HorizontalAlignment.Right;
		_barHost.CornerRadius = new CornerRadius(10.0);
		_barHost.Padding = new Thickness(10.0, 8.0, 10.0, 8.0);
		_barHost.IsVisible = false;
		_barHost.Dyn(Border.BackgroundProperty, "B.Paper");
		_barHost.Dyn(Border.BorderBrushProperty, "B.Line");
		_barHost.BorderThickness = new Thickness(1.0);
		_barHost.Child = sPanel2;
		Motion.Shadow(_barHost, 16.0, 0.07, 3.0);
		// 根结构:上滚动内容区,下托盘/工具条(仅编辑态占位出现,不悬浮遮挡内容)
		SPanel bottom = Ui.V(8.0, _trayHost, _barHost);
		bottom.Margin = new Thickness(_list.Margin.Left, 8.0, _list.Margin.Right, _list.Margin.Bottom);
		Grid grid = Ui.G("*,Auto");
		grid.Add(Ui.Scroll(_list), 0);
		grid.Add(bottom, 1);
		base.Content = grid;
		// 入口:右键菜单(specs.Count >= 2 才有「自定义布局...」;页面内容自带 ContextMenu 时就近者优先)
		if (_specs.Count >= 2)
		{
			ContextMenu contextMenu = new ContextMenu();
			MenuItem menuItem = new MenuItem
			{
				Header = I18n.L("自定义布局...")
			};
			menuItem.Click += OnCustomizeMenuClick;
			contextMenu.Items.Add(menuItem);
			base.ContextMenu = contextMenu;
		}
		base.Loaded += delegate
		{
			ActualThemeVariantChanged -= OnThemeVariantChanged;
			ActualThemeVariantChanged += OnThemeVariantChanged;
			RefreshThemeVisuals();
		};
		base.Unloaded += delegate
		{
			ActualThemeVariantChanged -= OnThemeVariantChanged;
		};
		ApplyStored();
		Render();
	}

	/// <summary>
	/// 页面挂可视树用的根元素。Windows 的返回类型是 <c>FrameworkElement</c>;Avalonia 里对应的
	/// 基类是 <see cref="Control"/>(<c>Layoutable.MaxHeight</c> 绑定照样写在它身上)。
	/// </summary>
	public Control Root => this;

	public event Action? Changed;

	// ————— 测试可见的模型视图(C3a 新增,只有 getter,不影响 Windows 的任何成员) —————

	/// <summary>当前显示顺序(<c>_order</c> 的只读快照;隐藏项也在这里,保留原槽位)。</summary>
	internal IReadOnlyList<BlockSpec> OrderNow => _order.ToArray();

	/// <summary>当前被隐藏的块 id 集合(只读快照)。</summary>
	internal IReadOnlyCollection<string> HiddenNow => new List<string>(_hidden);

	/// <summary>是否处于编辑模式。</summary>
	internal bool EditNow => _edit;

	/// <summary>滚动列表里此刻真正渲染出来的块卡(按下标)。</summary>
	internal IReadOnlyList<BlockCard> CardsNow => Snapshot(_list.Children);

	/// <summary>取某个 id 的块卡(测试用;不存在的 id 返回 null)。</summary>
	internal BlockCard? CardOf(string id)
	{
		return _cards.TryGetValue(id ?? "", out BlockCard? card) ? card : null;
	}

	/// <summary>底部「已隐藏区块」托盘里此刻有哪些 chip(按块 id;测试用)。</summary>
	internal IReadOnlyList<string> TrayIdsNow
	{
		get
		{
			List<string> list = new List<string>();
			foreach (Control child in _tray.Children)
			{
				// chip 的 Tag 就是块 id(RefreshTray 里写的);用 Tag 而不是读文本,
				// 免得把按钮里那个图标字体的私有码位当成标题。
				if (child is Button button && button.Tag is string id)
				{
					list.Add(id);
				}
			}
			return list;
		}
	}

	/// <summary>托盘 / 工具条此刻是否可见(测试用)。</summary>
	internal bool TrayShownNow => _trayHost.IsVisible;

	internal bool BarShownNow => _barHost.IsVisible;

	private static IReadOnlyList<BlockCard> Snapshot(Avalonia.Controls.Controls children)
	{
		List<BlockCard> list = new List<BlockCard>();
		foreach (Control child in children)
		{
			if (child is BlockCard blockCard)
			{
				list.Add(blockCard);
			}
		}
		return list;
	}

	// ————— 布局装载 —————

	internal List<string> KnownIds()
	{
		List<string> list = new List<string>();
		foreach (BlockSpec spec in _specs)
		{
			list.Add(spec.Id);
		}
		return list;
	}

	internal List<string> VisibleIds()
	{
		List<string> list = new List<string>();
		foreach (BlockSpec spec in _specs)
		{
			if (spec.DefaultVisible)
			{
				list.Add(spec.Id);
			}
		}
		return list;
	}

	internal PageLayoutDoc DefaultDoc()
	{
		return PageLayoutDoc.Default(KnownIds(), VisibleIds());
	}

	// 读当前页布局文档(缺失 = 全默认),未知块丢弃、缺失块按 DefaultVisible 补尾
	internal void ApplyStored()
	{
		ApplyDoc(PageLayoutStore.Load(_pageId) ?? DefaultDoc());
	}

	internal void ApplyDoc(PageLayoutDoc doc)
	{
		doc.Normalize(KnownIds(), VisibleIds());
		Dictionary<string, BlockSpec> dictionary = new Dictionary<string, BlockSpec>(StringComparer.Ordinal);
		foreach (BlockSpec spec in _specs)
		{
			dictionary[spec.Id] = spec;
		}
		List<BlockSpec> list = new List<BlockSpec>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (BlockState block in doc.Blocks)
		{
			if (dictionary.TryGetValue(block.Id, out BlockSpec? value))
			{
				list.Add(value);
				if (block.Hidden)
				{
					hashSet.Add(block.Id);
				}
			}
		}
		// 防御补漏:Normalize 后理论完整,但单 spec 页面等异常输入下保底不丢块
		foreach (BlockSpec item in _specs)
		{
			if (!list.Contains(item))
			{
				list.Add(item);
				if (!item.DefaultVisible)
				{
					hashSet.Add(item.Id);
				}
			}
		}
		_order = list;
		_hidden = hashSet;
	}

	// ————— 渲染 —————

	// 按当前顺序重建可视列表(隐藏块不进列表,编辑态去托盘)
	internal void Render()
	{
		_list.Children.Clear();
		foreach (BlockSpec spec in _order)
		{
			if (_hidden.Contains(spec.Id))
			{
				continue;
			}
			BlockCard blockCard = _cards[spec.Id];
			blockCard.SetEdit(_edit);
			_list.Children.Add(blockCard);
		}
		if (_edit)
		{
			RefreshTray();
		}
	}

	// 把 spec 的卡插回可视列表的正确位置(_order 中排在它前面的可见块数)
	internal void InsertVisual(BlockSpec spec)
	{
		BlockCard card = _cards[spec.Id];
		int num = 0;
		foreach (BlockSpec item in _order)
		{
			if (item == spec)
			{
				break;
			}
			if (!_hidden.Contains(item.Id))
			{
				num++;
			}
		}
		_list.Children.Remove(card);
		_list.Children.Insert(Math.Min(num, _list.Children.Count), card);
		card.SetEdit(_edit);
	}

	internal void RefreshThemeVisuals()
	{
		foreach (BlockCard value in _cards.Values)
		{
			value.RefreshTint();
		}
	}

	private void OnThemeVariantChanged(object? sender, EventArgs e)
	{
		RefreshThemeVisuals();
	}

	private void OnCustomizeMenuClick(object? sender, RoutedEventArgs e)
	{
		EnterEdit();
	}

	// Windows 兜底色(取不到 B.Accent 时用它,不是 Palette 的兜底灰)
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

	// ————— 编辑模式 —————

	internal void EnterEdit()
	{
		if (_edit || _specs.Count == 0)
		{
			return;
		}
		_edit = true;
		Render();
		_barHost.IsVisible = true;
	}

	internal void ExitEdit()
	{
		if (!_edit)
		{
			return;
		}
		_edit = false;
		Persist();
		Render();
		_trayHost.IsVisible = false;
		_barHost.IsVisible = false;
		AppServices.Toast(I18n.L("布局已保存"), "", ToastKind.Success);
	}

	internal void RefreshTray()
	{
		_tray.Children.Clear();
		foreach (BlockSpec spec in _order)
		{
			if (!_hidden.Contains(spec.Id))
			{
				continue;
			}
			Button button = Ui.Btn(spec.Title, BtnKind.Chip, delegate
			{
				Restore(spec);
			}, Ico.Add);
			// Tag = 块 id:托盘 chip 与块卡一一对应,测试与将来的页面都能据此定位
			button.Tag = spec.Id;
			button.M(0.0, 0.0, 8.0, 6.0);
			_tray.Children.Add(button);
		}
		_trayHost.IsVisible = _edit && _tray.Children.Count > 0;
	}

	internal void HideBlock(BlockSpec spec)
	{
		if (!_edit || _hidden.Contains(spec.Id))
		{
			return;
		}
		_hidden.Add(spec.Id);
		_list.Children.Remove(_cards[spec.Id]);
		RefreshTray();
		Persist();
	}

	// 从托盘恢复:隐藏项一直留在 _order 原槽位,插回原位置对应可视索引
	internal void Restore(BlockSpec spec)
	{
		if (!_edit || !_hidden.Contains(spec.Id))
		{
			return;
		}
		_hidden.Remove(spec.Id);
		InsertVisual(spec);
		RefreshTray();
		Persist();
	}

	// 重置本页 = 保存 Default 文档并重渲染
	internal void ResetPage()
	{
		PageLayoutDoc pageLayoutDoc = DefaultDoc();
		PageLayoutStore.Save(_pageId, pageLayoutDoc);
		ApplyDoc(pageLayoutDoc);
		Render();
		Changed?.Invoke();
		AppServices.Toast(I18n.L("布局已重置"), "", ToastKind.Success);
	}

	// 导入后当前页立即生效;其他页面是缓存实例,下次打开时按新文档渲染(Toast 已说明)
	internal void ImportLayout()
	{
		int? num = PageLayoutIO.ImportFromFileDialog();
		if (num == null)
		{
			return;
		}
		ApplyStored();
		Render();
		Changed?.Invoke();
	}

	// ————— 持久化 —————

	// 任何变更 → 组装文档 → Store 落盘(失败被吞)→ Changed
	internal void Persist()
	{
		PageLayoutDoc pageLayoutDoc = new PageLayoutDoc();
		foreach (BlockSpec spec in _order)
		{
			pageLayoutDoc.Blocks.Add(new BlockState
			{
				Id = spec.Id,
				Hidden = _hidden.Contains(spec.Id)
			});
		}
		PageLayoutStore.Save(_pageId, pageLayoutDoc);
		Changed?.Invoke();
	}

	// ————— 拖拽重排(编辑态) —————

	/// <summary>
	/// 手柄按下(WPF 的 PreviewMouseLeftButtonDown → Avalonia 的 PointerPressed)。
	/// 缓存各可视块高度,捕获指针,之后 PointerMoved 按相邻块中点实时交换。
	/// 不做动画插值,交换即落位——Motion.Enabled=false 也完全一致。
	/// </summary>
	internal void BeginDrag(BlockCard card, Control src, PointerPressedEventArgs e)
	{
		BeginDragCore(card, src, e.Pointer);
		e.Handled = true;
	}

	/// <summary>
	/// 拖拽的真正实现(与 <see cref="BeginDrag"/> 同一条路径,只是不吃事件对象):
	/// 事件处理器只负责取事件里的指针与坐标,状态机全在这里 —— 测试驱动的就是这一层
	/// (见 DECISIONS.md D-115)。
	/// </summary>
	internal void BeginDragCore(BlockCard card, Control src, IPointer? pointer)
	{
		if (!_edit || _dragCard != null)
		{
			return;
		}
		_dragCard = card;
		_dragPointer = pointer;
		DragSourceNow = src;
		_dragHeights.Clear();
		foreach (Control child in _list.Children)
		{
			if (child is BlockCard blockCard && blockCard.Bounds.Height > 1.0)
			{
				_dragHeights[blockCard.Spec.Id] = blockCard.Bounds.Height;
			}
		}
		card.Opacity = 0.75;
		pointer?.Capture(src);
		src.AddHandler(InputElement.PointerMovedEvent, _onDragMove, RoutingStrategies.Bubble, handledEventsToo: true);
		src.AddHandler(InputElement.PointerReleasedEvent, _onDragEnd, RoutingStrategies.Bubble, handledEventsToo: true);
		// PointerCaptureLost 是 Direct 路由事件(没有隧道/冒泡阶段),用两参重载订阅。
		src.AddHandler(InputElement.PointerCaptureLostEvent, _onDragLost);
	}

	/// <summary>当前是否正在拖拽(测试断言"拖拽结束后状态已清干净"用)。</summary>
	internal bool DraggingNow => _dragCard != null;

	/// <summary>拖拽期间被按下手柄的那个元素(测试断言订阅已摘干净用)。</summary>
	internal Control? DragSourceNow { get; private set; }

	// WPF: MouseEventArgs + e.LeftButton != MouseButtonState.Pressed。
	// Avalonia: PointerEventArgs + GetCurrentPoint(...).Properties.IsLeftButtonPressed。
	private void OnDragMove(object? sender, PointerEventArgs e)
	{
		if (_dragCard == null || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed)
		{
			return;
		}
		DragTo(e.GetPosition(_list).Y);
		e.Handled = true;
	}

	/// <summary>
	/// 拖拽到列表坐标系的 <paramref name="pointerY"/>(Windows 的 OnDragMove 正文,一字未改;
	/// 只是把"从事件里取坐标"这一步留在了 <see cref="OnDragMove"/> 里)。
	/// </summary>
	internal void DragTo(double pointerY)
	{
		BlockCard? blockCard = _dragCard;
		if (blockCard == null)
		{
			return;
		}
		double y = pointerY;
		int num = VisibleIndexOf(blockCard.Spec);
		if (num < 0)
		{
			return;
		}
		// 一次移动可能跨多格:逐格与相邻块中点比较,直到不再交换
		while (num > 0 && y < MidY(num - 1))
		{
			MoveVisible(num, -1);
			num--;
		}
		int num2 = VisibleCount();
		while (num < num2 - 1 && y > MidY(num + 1))
		{
			MoveVisible(num, 1);
			num++;
		}
	}

	private void OnDragEnd(object? sender, PointerReleasedEventArgs e)
	{
		EndDrag((Control)sender);
	}

	private void OnDragLost(object? sender, PointerCaptureLostEventArgs e)
	{
		EndDrag((Control)sender);
	}

	internal void EndDrag(Control src)
	{
		BlockCard? blockCard = _dragCard;
		_dragCard = null;
		src.RemoveHandler(InputElement.PointerMovedEvent, _onDragMove);
		src.RemoveHandler(InputElement.PointerReleasedEvent, _onDragEnd);
		src.RemoveHandler(InputElement.PointerCaptureLostEvent, _onDragLost);
		// WPF: if (src.IsMouseCaptured) src.ReleaseMouseCapture();
		// Avalonia: 捕获是按指针记的,放开同一个指针的捕获即可。
		_dragPointer?.Capture(null);
		_dragPointer = null;
		DragSourceNow = null;
		if (blockCard == null)
		{
			return;
		}
		blockCard.Opacity = 1.0;
		Persist();
	}

	// 可视序号 k 的块中点 Y(相对 _list 顶部;高度用拖拽开始时的缓存,不依赖未完成的布局)
	internal double MidY(int k)
	{
		double num = 0.0;
		int num2 = 0;
		foreach (BlockSpec spec in _order)
		{
			if (_hidden.Contains(spec.Id))
			{
				continue;
			}
			if (num2 == k)
			{
				return num + H(spec) / 2.0;
			}
			num += H(spec) + Gap;
			num2++;
		}
		return num;
	}

	internal double H(BlockSpec spec)
	{
		if (_dragHeights.TryGetValue(spec.Id, out double value) && value > 1.0)
		{
			return value;
		}
		return 120.0;
	}

	// 把可视序号 k 的块搬到相邻位置:_order 里插到目标可见块旁边,可视树同步搬家
	internal void MoveVisible(int k, int dir)
	{
		List<BlockSpec> list = VisibleSpecs();
		BlockSpec blockSpec = list[k];
		BlockSpec other = list[k + dir];
		_order.Remove(blockSpec);
		_order.Insert(_order.IndexOf(other) + ((dir > 0) ? 1 : 0), blockSpec);
		InsertVisual(blockSpec);
	}

	internal int VisibleIndexOf(BlockSpec spec)
	{
		if (_hidden.Contains(spec.Id))
		{
			return -1;
		}
		int num = 0;
		foreach (BlockSpec item in _order)
		{
			if (item == spec)
			{
				return num;
			}
			if (!_hidden.Contains(item.Id))
			{
				num++;
			}
		}
		return -1;
	}

	internal int VisibleCount()
	{
		return VisibleSpecs().Count;
	}

	internal List<BlockSpec> VisibleSpecs()
	{
		List<BlockSpec> list = new List<BlockSpec>();
		foreach (BlockSpec item in _order)
		{
			if (!_hidden.Contains(item.Id))
			{
				list.Add(item);
			}
		}
		return list;
	}

	// 单个区块卡:常态零装饰(厚度 0 的透明描边 + 无底色),仅承载内容;
	// 编辑态出现操作条(拖拽手柄 + 块标题 + 显隐按钮),主题色描边 + 10 透明度蒙版(对齐 DashHost.CardView)。
	internal sealed class BlockCard : Border
	{
		private readonly BlockHost _host;

		private readonly SPanel _bar;

		private readonly Border _bodyWrap;

		private readonly Button _handle;

		private readonly ContentControl _body = new ContentControl
		{
			Focusable = false
		};

		private bool _edit;

		public BlockSpec Spec { get; }

		public BlockCard(BlockHost host, BlockSpec spec)
		{
			_host = host;
			Spec = spec;
			CornerRadius = new CornerRadius(10.0);
			this.Dyn(Border.BorderBrushProperty, "B.Accent");
			// 操作条:拖拽手柄(Ico.Sort)+ 块标题 + 显隐按钮
			Button uIElement = Ui.IconBtn(Ico.Sort, I18n.L("拖拽排序"), null, 12.0);
			uIElement.Cursor = new Cursor(StandardCursorType.SizeAll);
			uIElement.AddHandler(InputElement.PointerPressedEvent, OnHandlePressed, RoutingStrategies.Bubble, handledEventsToo: true);
			_handle = uIElement;
			Button button = Ui.IconBtn(Ico.Close, I18n.L("隐藏"), delegate
			{
				_host.HideBlock(Spec);
			}, 12.0);
			TextBlock textBlock = Ui.Txt(spec.Title, 12.5, bold: true);
			textBlock.VerticalAlignment = VerticalAlignment.Center;
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			_bar = Ui.H(8.0, uIElement, textBlock, Ui.Spring(), button);
			_bar.Margin = new Thickness(6.0, 6.0, 6.0, 2.0);
			// 内容体外壳:常态零边距(逐像素等价旧版),编辑态留一点呼吸边距
			_bodyWrap = new Border
			{
				Padding = new Thickness(0.0),
				Child = _body
			};
			Grid grid = Ui.G("Auto", "*");
			grid.Add(_bar, 0);
			grid.Add(_bodyWrap, 1);
			Child = grid;
			_bar.IsVisible = false;
			// 内容已挂在别的 ContentControl 上时先摘下来(对齐 DashHost.SetContent)
			DetachFromForeignHost(_body, spec.Content);
			_body.Content = spec.Content;
		}

		/// <summary>本条卡此刻的拖拽手柄(测试用;WPF 版没有这个成员)。</summary>
		internal Button HandleNow => _handle;

		/// <summary>本条卡此刻的操作条与内容体外壳(测试用)。</summary>
		internal SPanel BarNow => _bar;

		internal Border BodyWrapNow => _bodyWrap;

		internal ContentControl BodyNow => _body;

		private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
		{
			_host.BeginDrag(this, (Control)sender!, e);
		}

		public void SetEdit(bool edit)
		{
			_edit = edit;
			_bar.IsVisible = edit;
			_body.IsHitTestVisible = !edit;
			_bodyWrap.Padding = (edit ? new Thickness(6.0, 0.0, 6.0, 8.0) : new Thickness(0.0));
			BorderThickness = new Thickness(edit ? 1.4 : 0.0);
			RefreshTint();
		}

		// 编辑态蒙版:主题色 10 透明度平铺(对齐 CardView.RefreshEditMask;跟着主题刷新)
		public void RefreshTint()
		{
			if (!_edit)
			{
				Background = null;
				return;
			}
			Color color = BlockHost.AccentColor();
			Background = new SolidColorBrush(Color.FromArgb(10, color.R, color.G, color.B));
		}

		internal bool EditNow => _edit;
	}

	/// <summary>
	/// "内容已挂在别的宿主上就先摘下来"(Windows 的
	/// <c>spec.Content is FrameworkElement { Parent: ContentControl parent }</c>)。
	/// Avalonia 的差别:ContentControl 的内容先落进模板里的 ContentPresenter,所以内容控件的
	/// <c>Parent</c> 是 ContentPresenter 而不是 ContentControl —— 两种父级都认,
	/// 并且用 <c>TemplatedParent</c> 认出"是不是自己这具 ContentControl"。
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