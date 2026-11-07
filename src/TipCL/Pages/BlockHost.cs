using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Services;

namespace TipCL.Pages;

// 页面区块描述：页面把可重排的内容切成若干 BlockSpec 交给 BlockHost。
// Id 是稳定英文 id（进布局文档）；Title 必须是已经过 I18n.L 的现成文案；
// Content 由页面自己构建，BlockHost 只负责搬运与重排，不关心内容内部结构。
public sealed class BlockSpec
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public UIElement? Content { get; set; }

	public bool DefaultVisible { get; set; } = true;
}

// 页面区块宿主（WP-U1 冻结契约）：
//   常态按布局文档顺序渲染非隐藏区块，视觉零装饰（间距/边距对齐 PageBase.ScrollBody，默认渲染与旧版逐像素等价）；
//   Root 右键「自定义布局…」（specs.Count >= 2 才有该菜单项）进入编辑模式：
//   每块顶部操作条 = 拖拽手柄 + 块标题 + 显隐按钮；按鼠标 Y 与相邻块中点比较实时交换重排；
//   底部「已隐藏区块」托盘（+ 标题 chip，点击恢复）与悬浮工具条（完成 / 重置本页 / 导出 / 导入）。
//   任何变更组装 PageLayoutDoc 即时 PageLayoutStore.Save 并触发 Changed；退出编辑自动保存。
//   页面内容若自带 ContextMenu，WPF 取最近的属性值，天然对方优先，互不冲突。
public sealed class BlockHost : UserControl
{
	// 块间距，对齐 DashHost 的卡片间隙
	private const double Gap = 10.0;

	private readonly string _pageId;

	private readonly List<BlockSpec> _specs = new List<BlockSpec>();

	// 每个区块一张卡（含编辑态操作条），常驻缓存，重排/显隐只动它在可视树里的位置
	private readonly Dictionary<string, BlockCard> _cards = new Dictionary<string, BlockCard>();

	// 当前显示顺序（含隐藏项：隐藏项留在原槽位，恢复时回到原位置）
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

	// 拖拽手势状态：手柄捕获鼠标期间按缓存高度计算相邻块中点
	private BlockCard? _dragCard;

	private readonly Dictionary<string, double> _dragHeights = new Dictionary<string, double>();

	public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, System.Windows.Thickness? contentMargin = null)
	{
		_pageId = pageId ?? "";
		foreach (BlockSpec item in specs ?? Array.Empty<BlockSpec>())
		{
			if (item != null && !string.IsNullOrEmpty(item.Id))
			{
				_specs.Add(item);
			}
		}
		base.Focusable = false;
		// null → (18,14,18,16)，对齐 PageBase.ScrollBody 的内容边距
		_list.Margin = contentMargin ?? new Thickness(18.0, 14.0, 18.0, 16.0);
		foreach (BlockSpec spec in _specs)
		{
			_cards[spec.Id] = new BlockCard(this, spec);
		}
		// 底部托盘（编辑态）：已隐藏区块以「+ 标题」chip 呈现，点击恢复
		SPanel sPanel = Ui.V(6.0, Ui.Muted(I18n.L("已隐藏的区块")), _tray);
		_trayHost.CornerRadius = new CornerRadius(8.0);
		_trayHost.Padding = new Thickness(10.0);
		_trayHost.Visibility = Visibility.Collapsed;
		_trayHost.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		_trayHost.Child = sPanel;
		// 底部悬浮工具条（编辑态）：完成 / 重置本页 / 导出布局 / 导入布局
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
		_barHost.Visibility = Visibility.Collapsed;
		_barHost.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_barHost.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_barHost.BorderThickness = new Thickness(1.0);
		_barHost.Child = sPanel2;
		Motion.Shadow(_barHost, 16.0, 0.07, 3.0);
		// 根结构：上滚动内容区，下托盘/工具条（仅编辑态占位出现，不悬浮遮挡内容）
		SPanel bottom = Ui.V(8.0, _trayHost, _barHost);
		bottom.Margin = new Thickness(_list.Margin.Left, 8.0, _list.Margin.Right, _list.Margin.Bottom);
		Grid grid = Ui.G("*,Auto");
		grid.Add(Ui.Scroll(_list), 0);
		grid.Add(bottom, 1);
		base.Content = grid;
		// 入口：右键菜单（specs.Count >= 2 才有「自定义布局…」；页面内容自带 ContextMenu 时天然对方优先）
		if (_specs.Count >= 2)
		{
			ContextMenu contextMenu = new ContextMenu();
			MenuItem menuItem = new MenuItem
			{
				Header = I18n.L("自定义布局…")
			};
			menuItem.Click += delegate
			{
				EnterEdit();
			};
			contextMenu.Items.Add(menuItem);
			base.ContextMenu = contextMenu;
		}
		base.Loaded += delegate
		{
			App.ThemeVisualsChanged -= RefreshThemeVisuals;
			App.ThemeVisualsChanged += RefreshThemeVisuals;
			RefreshThemeVisuals();
		};
		base.Unloaded += delegate
		{
			App.ThemeVisualsChanged -= RefreshThemeVisuals;
		};
		ApplyStored();
		Render();
	}

	public FrameworkElement Root => this;

	public event Action? Changed;

	// ————— 布局装载 —————

	private List<string> KnownIds()
	{
		List<string> list = new List<string>();
		foreach (BlockSpec spec in _specs)
		{
			list.Add(spec.Id);
		}
		return list;
	}

	private List<string> VisibleIds()
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

	private PageLayoutDoc DefaultDoc()
	{
		return PageLayoutDoc.Default(KnownIds(), VisibleIds());
	}

	// 读当前页布局文档（缺失 = 全默认），未知块丢弃、缺失块按 DefaultVisible 补尾
	private void ApplyStored()
	{
		ApplyDoc(PageLayoutStore.Load(_pageId) ?? DefaultDoc());
	}

	private void ApplyDoc(PageLayoutDoc doc)
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
		// 防御补漏：Normalize 后理论完整，但单 spec 页面等异常输入下保底不丢块
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

	// 按当前顺序重建可视列表（隐藏块不进列表，编辑态去托盘）
	private void Render()
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

	// 把 spec 的卡插回可视列表的正确位置（_order 中排在它前面的可见块数）
	private void InsertVisual(BlockSpec spec)
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

	private void RefreshThemeVisuals()
	{
		foreach (BlockCard value in _cards.Values)
		{
			value.RefreshTint();
		}
	}

	private static Color AccentColor()
	{
		if (!(Application.Current?.TryFindResource("B.Accent") is SolidColorBrush solidColorBrush))
		{
			return Color.FromRgb(46, 155, 107);
		}
		return solidColorBrush.Color;
	}

	// ————— 编辑模式 —————

	private void EnterEdit()
	{
		if (_edit || _specs.Count == 0)
		{
			return;
		}
		_edit = true;
		Render();
		_barHost.Visibility = Visibility.Visible;
	}

	private void ExitEdit()
	{
		if (!_edit)
		{
			return;
		}
		_edit = false;
		Persist();
		Render();
		_trayHost.Visibility = Visibility.Collapsed;
		_barHost.Visibility = Visibility.Collapsed;
		AppServices.Window?.Toast(I18n.L("布局已保存"), "", ToastKind.Success);
	}

	private void RefreshTray()
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
			button.M(0.0, 0.0, 8.0, 6.0);
			_tray.Children.Add(button);
		}
		_trayHost.Visibility = ((_edit && _tray.Children.Count > 0) ? Visibility.Visible : Visibility.Collapsed);
	}

	private void HideBlock(BlockSpec spec)
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

	// 从托盘恢复：隐藏项一直留在 _order 原槽位，插回原位置对应可视索引
	private void Restore(BlockSpec spec)
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
	private void ResetPage()
	{
		PageLayoutDoc pageLayoutDoc = DefaultDoc();
		PageLayoutStore.Save(_pageId, pageLayoutDoc);
		ApplyDoc(pageLayoutDoc);
		Render();
		this.Changed?.Invoke();
		AppServices.Window?.Toast(I18n.L("布局已重置"), "", ToastKind.Success);
	}

	// 导入后当前页立即生效；其他页面是缓存实例，下次打开时按新文档渲染（Toast 已说明）
	private void ImportLayout()
	{
		int? num = PageLayoutIO.ImportFromFileDialog();
		if (num == null)
		{
			return;
		}
		ApplyStored();
		Render();
		this.Changed?.Invoke();
	}

	// ————— 持久化 —————

	// 任何变更 → 组装文档 → Store 落盘（失败被吞）→ Changed
	private void Persist()
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
		this.Changed?.Invoke();
	}

	// ————— 拖拽重排（编辑态） —————

	// 手柄按下：缓存各可视块高度，捕获鼠标，之后 MouseMove 按相邻块中点实时交换。
	// 不做动画插值，交换即落位——Motion.Enabled=false 也完全一致。
	internal void BeginDrag(BlockCard card, UIElement src, MouseButtonEventArgs e)
	{
		if (!_edit || _dragCard != null)
		{
			return;
		}
		_dragCard = card;
		_dragHeights.Clear();
		foreach (UIElement child in _list.Children)
		{
			if (child is BlockCard blockCard && blockCard.ActualHeight > 1.0)
			{
				_dragHeights[blockCard.Spec.Id] = blockCard.ActualHeight;
			}
		}
		card.Opacity = 0.75;
		src.CaptureMouse();
		src.MouseMove += OnDragMove;
		src.MouseLeftButtonUp += OnDragEnd;
		src.LostMouseCapture += OnDragLost;
		e.Handled = true;
	}

	private void OnDragMove(object sender, MouseEventArgs e)
	{
		BlockCard blockCard = _dragCard;
		if (blockCard == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		double y = e.GetPosition(_list).Y;
		int num = VisibleIndexOf(blockCard.Spec);
		if (num < 0)
		{
			return;
		}
		// 一次移动可能跨多格：逐格与相邻块中点比较，直到不再交换
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

	private void OnDragEnd(object sender, MouseButtonEventArgs e)
	{
		EndDrag((UIElement)sender);
	}

	private void OnDragLost(object sender, MouseEventArgs e)
	{
		EndDrag((UIElement)sender);
	}

	private void EndDrag(UIElement src)
	{
		BlockCard blockCard = _dragCard;
		_dragCard = null;
		src.MouseMove -= OnDragMove;
		src.MouseLeftButtonUp -= OnDragEnd;
		src.LostMouseCapture -= OnDragLost;
		if (src.IsMouseCaptured)
		{
			src.ReleaseMouseCapture();
		}
		if (blockCard == null)
		{
			return;
		}
		blockCard.Opacity = 1.0;
		Persist();
	}

	// 可视序号 k 的块中点 Y（相对 _list 顶部；高度用拖拽开始时的缓存，不依赖未完成的布局）
	private double MidY(int k)
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

	private double H(BlockSpec spec)
	{
		if (_dragHeights.TryGetValue(spec.Id, out double value) && value > 1.0)
		{
			return value;
		}
		return 120.0;
	}

	// 把可视序号 k 的块搬到相邻位置：_order 里插到目标可见块旁边，可视树同步搬家
	private void MoveVisible(int k, int dir)
	{
		List<BlockSpec> list = VisibleSpecs();
		BlockSpec blockSpec = list[k];
		BlockSpec other = list[k + dir];
		_order.Remove(blockSpec);
		_order.Insert(_order.IndexOf(other) + ((dir > 0) ? 1 : 0), blockSpec);
		InsertVisual(blockSpec);
	}

	private int VisibleIndexOf(BlockSpec spec)
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

	private int VisibleCount()
	{
		return VisibleSpecs().Count;
	}

	private List<BlockSpec> VisibleSpecs()
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

	// 单个区块卡：常态零装饰（厚度 0 的透明描边 + 无底色），仅承载内容；
	// 编辑态出现操作条（拖拽手柄 + 块标题 + 显隐按钮），主题色描边 + 10 透明度蒙版（对齐 DashHost.CardView）。
	private sealed class BlockCard : Border
	{
		private readonly BlockHost _host;

		private readonly SPanel _bar;

		private readonly Border _bodyWrap;

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
			SetResourceReference(Border.BorderBrushProperty, "B.Accent");
			// 操作条：拖拽手柄（Ico.Sort）+ 块标题 + 显隐按钮
			Button uIElement = Ui.IconBtn(Ico.Sort, I18n.L("拖拽排序"), null, 12.0);
			uIElement.Cursor = Cursors.SizeAll;
			uIElement.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				_host.BeginDrag(this, (UIElement)s, e);
			};
			Button button = Ui.IconBtn(Ico.Close, I18n.L("隐藏"), delegate
			{
				_host.HideBlock(Spec);
			}, 12.0);
			TextBlock textBlock = Ui.Txt(spec.Title, 12.5, bold: true);
			textBlock.VerticalAlignment = VerticalAlignment.Center;
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			_bar = Ui.H(8.0, uIElement, textBlock, Ui.Spring(), button);
			_bar.Margin = new Thickness(6.0, 6.0, 6.0, 2.0);
			// 内容体外壳：常态零边距（逐像素等价旧版），编辑态留一点呼吸边距
			_bodyWrap = new Border
			{
				Padding = new Thickness(0.0),
				Child = _body
			};
			Grid grid = Ui.G("Auto", "*");
			grid.Add(_bar, 0);
			grid.Add(_bodyWrap, 1);
			Child = grid;
			_bar.Visibility = Visibility.Collapsed;
			// 内容已挂在别的 ContentControl 上时先摘下来（对齐 DashHost.SetContent）
			if (spec.Content is FrameworkElement { Parent: ContentControl parent } && parent != _body)
			{
				parent.Content = null;
			}
			_body.Content = spec.Content;
		}

		public void SetEdit(bool edit)
		{
			_edit = edit;
			_bar.Show(edit);
			_body.IsHitTestVisible = !edit;
			_bodyWrap.Padding = (edit ? new Thickness(6.0, 0.0, 6.0, 8.0) : new Thickness(0.0));
			BorderThickness = new Thickness(edit ? 1.4 : 0.0);
			RefreshTint();
		}

		// 编辑态蒙版：主题色 10 透明度平铺（对齐 CardView.RefreshEditMask；跟着主题刷新）
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
	}
}
