// 来源: Windows 版参考仓库 src/TipCL/Pages/BlockHost.cs（M2-B4，WPF 646 行 → Avalonia 11 重写）。
// 拖拽算法逐行对齐：手柄按下缓存各可视块高度（Bounds.Height），指针移动时逐格与相邻块中点
// 比较（MidY = 缓存高度 + Gap=10 累加）循环交换直到不满足，不做动画插值；
// WPF Mouse API → Avalonia Pointer API（e.Pointer.Capture / PointerPressed /
// PointerMoved / PointerReleased / PointerCaptureLost，见 R3 §6）。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Ui;
using TipCL.Core.Layout;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 页面区块描述：页面把可重排的内容切成若干 BlockSpec 交给 BlockHost。
/// Id 是稳定英文 id（进布局文档）；Title 必须是已经过文案门面的现成文案；
/// Content 由页面自己构建，BlockHost 只负责搬运与重排，不关心内容内部结构。
/// </summary>
public sealed class BlockSpec
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public Control? Content { get; set; }

    public bool DefaultVisible { get; set; } = true;
}

/// <summary>
/// 页面区块宿主（WP-U1 冻结契约，Avalonia 版）：
/// 常态按布局文档顺序渲染非隐藏区块，视觉零装饰；根右键「自定义布局…」（specs.Count >= 2 才注册）
/// 进入编辑模式：每块顶部操作条 = 拖拽手柄 + 块标题 + 显隐按钮；拖拽按相邻块中点实时交换；
/// 底部「已隐藏区块」托盘（+ 标题 chip，点击恢复）与工具条（完成 / 重置本页 / 导出布局 / 导入布局）。
/// 任何变更组装 PageLayoutDoc 即时 PageLayoutStore.Save 并触发 Changed；退出编辑自动保存。
/// 注意（Avalonia 差异，R3 §6.5）：Avalonia 的 ContextMenu 不做属性继承链合并——
/// 挂 BlockHost 的页面不得再在根上挂自己的 ContextMenu。
/// </summary>
public sealed class BlockHost : UserControl
{
    // 块间距，对齐 DashHost 的卡片间隙
    private const double Gap = 10.0;

    private const string GripGlyph = "⠿";
    private const string CloseGlyph = "✕";
    private const string AddGlyph = "+";

    private readonly string _pageId;

    private readonly List<BlockSpec> _specs = new List<BlockSpec>();

    // 每个区块一张卡（含编辑态操作条），常驻缓存，重排/显隐只动它在可视树里的位置
    private readonly Dictionary<string, BlockCard> _cards = new Dictionary<string, BlockCard>();

    // 当前显示顺序（含隐藏项：隐藏项留在原槽位，恢复时回到原位置）
    private List<BlockSpec> _order = new List<BlockSpec>();

    private HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal);

    private readonly StackPanel _list = new StackPanel
    {
        Orientation = Orientation.Vertical,
        Spacing = Gap
    };

    private readonly Border _trayHost = new Border();

    private readonly WrapPanel _tray = new WrapPanel();

    private readonly Border _barHost = new Border();

    private bool _edit;

    // 拖拽手势状态：手柄捕获指针期间按缓存高度计算相邻块中点
    private BlockCard? _dragCard;

    private readonly Dictionary<string, double> _dragHeights = new Dictionary<string, double>();

    public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null)
    {
        _pageId = pageId ?? "";
        foreach (BlockSpec item in specs ?? Array.Empty<BlockSpec>())
        {
            if (item != null && !string.IsNullOrEmpty(item.Id))
            {
                _specs.Add(item);
            }
        }
        Focusable = false;
        // null → (18,14,18,16)，对齐 PageBase.ScrollBody 的内容边距
        _list.Margin = contentMargin ?? new Thickness(18.0, 14.0, 18.0, 16.0);
        foreach (BlockSpec spec in _specs)
        {
            _cards[spec.Id] = new BlockCard(this, spec);
        }
        // 底部托盘（编辑态）：已隐藏区块以「+ 标题」chip 呈现，点击恢复
        var trayPanel = U.V(6.0, U.Muted("已隐藏的区块"), _tray);
        _trayHost.CornerRadius = new CornerRadius(8.0);
        _trayHost.Padding = new Thickness(10.0);
        _trayHost.IsVisible = false;
        _trayHost.Child = trayPanel;
        _trayHost.Dyn(Border.BackgroundProperty, "B.Paper2");
        // 底部工具条（编辑态）：完成 / 重置本页 / 导出布局 / 导入布局
        var bar = U.H(8.0,
            U.Btn("完成编辑", BtnKind.Primary, delegate { ExitEdit(); }),
            U.Btn("重置本页", BtnKind.Normal, delegate { ResetPage(); }),
            U.Btn("导出布局", BtnKind.Normal, delegate { PageLayoutIO.ExportToFileDialog(_pageId); }),
            U.Btn("导入布局", BtnKind.Normal, delegate { ImportLayout(); }));
        bar.HorizontalAlignment = HorizontalAlignment.Right;
        _barHost.CornerRadius = new CornerRadius(10.0);
        _barHost.Padding = new Thickness(10.0, 8.0, 10.0, 8.0);
        _barHost.IsVisible = false;
        _barHost.Child = bar;
        _barHost.Dyn(Border.BackgroundProperty, "B.Paper");
        _barHost.Dyn(Border.BorderBrushProperty, "B.Line");
        _barHost.BorderThickness = new Thickness(1.0);
        // 根结构：上滚动内容区，下托盘/工具条（仅编辑态占位出现，不悬浮遮挡内容）
        var bottom = U.V(8.0, _trayHost, _barHost);
        bottom.Margin = new Thickness(_list.Margin.Left, 8.0, _list.Margin.Right, _list.Margin.Bottom);
        Grid grid = U.G("*,Auto");
        grid.Add(new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 0);
        grid.Add(bottom, 1);
        Content = grid;
        // 入口：右键菜单（specs.Count >= 2 才有「自定义布局…」）
        if (_specs.Count >= 2)
        {
            var menu = new ContextMenu();
            var mi = new MenuItem { Header = "自定义布局…" };
            mi.Click += delegate { EnterEdit(); };
            menu.Items.Add(mi);
            ContextMenu = menu;
        }
        DetachedFromVisualTree += delegate { if (_edit) ExitEdit(saveToast: false); };
        ApplyStored();
        Render();
    }

    public Control Root => this;

    public event Action? Changed;

    // ————— 布局装载 —————

    private List<string> KnownIds()
    {
        var list = new List<string>();
        foreach (BlockSpec spec in _specs) list.Add(spec.Id);
        return list;
    }

    private List<string> VisibleIds()
    {
        var list = new List<string>();
        foreach (BlockSpec spec in _specs)
            if (spec.DefaultVisible) list.Add(spec.Id);
        return list;
    }

    private PageLayoutDoc DefaultDoc() => PageLayoutDoc.Default(KnownIds(), VisibleIds());

    // 读当前页布局文档（缺失 = 全默认），未知块丢弃、缺失块按 DefaultVisible 补尾
    private void ApplyStored()
    {
        ApplyDoc(PageLayoutStore.Load(_pageId) ?? DefaultDoc());
    }

    private void ApplyDoc(PageLayoutDoc doc)
    {
        doc.Normalize(KnownIds(), VisibleIds());
        var byId = new Dictionary<string, BlockSpec>(StringComparer.Ordinal);
        foreach (BlockSpec spec in _specs) byId[spec.Id] = spec;
        var list = new List<BlockSpec>();
        var hiddenSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (BlockState block in doc.Blocks)
        {
            if (byId.TryGetValue(block.Id, out BlockSpec? spec))
            {
                list.Add(spec);
                if (block.Hidden) hiddenSet.Add(block.Id);
            }
        }
        // 防御补漏：Normalize 后理论完整，但单 spec 页面等异常输入下保底不丢块
        foreach (BlockSpec spec in _specs)
        {
            if (!list.Contains(spec))
            {
                list.Add(spec);
                if (!spec.DefaultVisible) hiddenSet.Add(spec.Id);
            }
        }
        _order = list;
        _hidden = hiddenSet;
    }

    // ————— 渲染 —————

    // 按当前顺序重建可视列表（隐藏块不进列表，编辑态去托盘）
    private void Render()
    {
        _list.Children.Clear();
        foreach (BlockSpec spec in _order)
        {
            if (_hidden.Contains(spec.Id)) continue;
            BlockCard card = _cards[spec.Id];
            card.SetEdit(_edit);
            _list.Children.Add(card);
        }
        if (_edit) RefreshTray();
    }

    // 把 spec 的卡插回可视列表的正确位置（_order 中排在它前面的可见块数）
    private void InsertVisual(BlockSpec spec)
    {
        BlockCard card = _cards[spec.Id];
        int visible = 0;
        foreach (BlockSpec item in _order)
        {
            if (item == spec) break;
            if (!_hidden.Contains(item.Id)) visible++;
        }
        _list.Children.Remove(card);
        _list.Children.Insert(Math.Min(visible, _list.Children.Count), card);
        card.SetEdit(_edit);
    }

    private static Color AccentColor()
    {
        if (U.Brush("B.Accent") is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Color.FromRgb(46, 155, 107);
    }

    // ————— 编辑模式 —————

    private void EnterEdit()
    {
        if (_edit || _specs.Count == 0) return;
        _edit = true;
        Render();
        _barHost.IsVisible = true;
    }

    private void ExitEdit(bool saveToast = true)
    {
        if (!_edit) return;
        _edit = false;
        Persist();
        Render();
        _trayHost.IsVisible = false;
        _barHost.IsVisible = false;
        if (saveToast) LayoutFx.ShowToast("布局已保存", "", ToastKind.Success);
    }

    private void RefreshTray()
    {
        _tray.Children.Clear();
        foreach (BlockSpec spec in _order)
        {
            if (!_hidden.Contains(spec.Id)) continue;
            BlockSpec captured = spec;
            Button b = U.Btn(spec.Title, BtnKind.Chip, delegate { Restore(captured); });
            b.Margin = new Thickness(0, 0, 8, 6);
            _tray.Children.Add(b);
        }
        _trayHost.IsVisible = _edit && _tray.Children.Count > 0;
    }

    private void HideBlock(BlockSpec spec)
    {
        if (!_edit || _hidden.Contains(spec.Id)) return;
        _hidden.Add(spec.Id);
        _list.Children.Remove(_cards[spec.Id]);
        RefreshTray();
        Persist();
    }

    // 从托盘恢复：隐藏项一直留在 _order 原槽位，插回原位置对应可视索引
    private void Restore(BlockSpec spec)
    {
        if (!_edit || !_hidden.Contains(spec.Id)) return;
        _hidden.Remove(spec.Id);
        InsertVisual(spec);
        RefreshTray();
        Persist();
    }

    // 重置本页 = 保存 Default 文档并重渲染
    private void ResetPage()
    {
        PageLayoutDoc doc = DefaultDoc();
        PageLayoutStore.Save(_pageId, doc);
        ApplyDoc(doc);
        Render();
        Changed?.Invoke();
        LayoutFx.ShowToast("布局已重置", "", ToastKind.Success);
    }

    // 导入后当前页立即生效；其他页面是缓存实例，下次打开时按新文档渲染（Toast 已说明）
    private void ImportLayout()
    {
        int? count = PageLayoutIO.ImportFromFileDialog();
        if (count == null) return;
        ApplyStored();
        Render();
        Changed?.Invoke();
    }

    // ————— 持久化 —————

    // 任何变更 → 组装文档 → Store 落盘（失败被吞）→ Changed
    private void Persist()
    {
        var doc = new PageLayoutDoc();
        foreach (BlockSpec spec in _order)
        {
            doc.Blocks.Add(new BlockState { Id = spec.Id, Hidden = _hidden.Contains(spec.Id) });
        }
        PageLayoutStore.Save(_pageId, doc);
        Changed?.Invoke();
    }

    // ————— 拖拽重排（编辑态，Avalonia Pointer API） —————

    // 手柄按下：缓存各可视块高度，捕获指针，之后 PointerMoved 按相邻块中点实时交换。
    // 不做动画插值，交换即落位——Motion.Enabled=false 也完全一致。
    internal void BeginDrag(BlockCard card, InputElement src, PointerPressedEventArgs e)
    {
        if (!_edit || _dragCard != null) return;
        _dragCard = card;
        _dragHeights.Clear();
        foreach (Control child in _list.Children)
        {
            if (child is BlockCard bc && bc.Bounds.Height > 1.0)
            {
                _dragHeights[bc.Spec.Id] = bc.Bounds.Height;
            }
        }
        card.Opacity = 0.75;
        src.PointerMoved += OnDragMove;
        src.PointerReleased += OnDragEnd;
        src.PointerCaptureLost += OnDragLost;
        e.Pointer.Capture(src);
        e.Handled = true;
    }

    private void OnDragMove(object? sender, PointerEventArgs e)
    {
        BlockCard? card = _dragCard;
        if (card == null || sender is not InputElement src) return;
        if (!e.GetCurrentPoint(src).Properties.IsLeftButtonPressed) return;
        double y = e.GetPosition(_list).Y;
        int k = VisibleIndexOf(card.Spec);
        if (k < 0) return;
        // 一次移动可能跨多格：逐格与相邻块中点比较，直到不再交换
        while (k > 0 && y < MidY(k - 1))
        {
            MoveVisible(k, -1);
            k--;
        }
        int n = VisibleCount();
        while (k < n - 1 && y > MidY(k + 1))
        {
            MoveVisible(k, 1);
            k++;
        }
    }

    private void OnDragEnd(object? sender, PointerReleasedEventArgs e) => EndDrag(sender);

    private void OnDragLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag(sender);

    private void EndDrag(object? sender)
    {
        BlockCard? card = _dragCard;
        _dragCard = null;
        if (sender is InputElement src)
        {
            src.PointerMoved -= OnDragMove;
            src.PointerReleased -= OnDragEnd;
            src.PointerCaptureLost -= OnDragLost;
        }
        if (card == null) return;
        card.Opacity = 1.0;
        Persist();
    }

    // 可视序号 k 的块中点 Y（相对 _list 顶部；高度用拖拽开始时的缓存，不依赖未完成的布局）
    private double MidY(int k)
    {
        double y = 0.0;
        int i = 0;
        foreach (BlockSpec spec in _order)
        {
            if (_hidden.Contains(spec.Id)) continue;
            if (i == k) return y + H(spec) / 2.0;
            y += H(spec) + Gap;
            i++;
        }
        return y;
    }

    private double H(BlockSpec spec)
    {
        if (_dragHeights.TryGetValue(spec.Id, out double h) && h > 1.0) return h;
        return 120.0;
    }

    // 把可视序号 k 的块搬到相邻位置：_order 里插到目标可见块旁边，可视树同步搬家
    private void MoveVisible(int k, int dir)
    {
        var visible = VisibleSpecs();
        BlockSpec spec = visible[k];
        BlockSpec other = visible[k + dir];
        _order.Remove(spec);
        _order.Insert(_order.IndexOf(other) + ((dir > 0) ? 1 : 0), spec);
        InsertVisual(spec);
    }

    private int VisibleIndexOf(BlockSpec spec)
    {
        if (_hidden.Contains(spec.Id)) return -1;
        int n = 0;
        foreach (BlockSpec item in _order)
        {
            if (item == spec) return n;
            if (!_hidden.Contains(item.Id)) n++;
        }
        return -1;
    }

    private int VisibleCount() => VisibleSpecs().Count;

    private List<BlockSpec> VisibleSpecs()
    {
        var list = new List<BlockSpec>();
        foreach (BlockSpec item in _order)
            if (!_hidden.Contains(item.Id)) list.Add(item);
        return list;
    }

    // 单个区块卡：常态零装饰（厚度 0 的透明描边 + 无底色），仅承载内容；
    // 编辑态出现操作条（拖拽手柄 + 块标题 + 显隐按钮），主题色描边 + 10 透明度蒙版。
    internal sealed class BlockCard : Border
    {
        private readonly BlockHost _host;

        private readonly StackPanel _bar;

        private readonly Border _bodyWrap;

        private readonly ContentControl _body = new ContentControl { Focusable = false };

        private bool _edit;

        public BlockSpec Spec { get; }

        public BlockCard(BlockHost host, BlockSpec spec)
        {
            _host = host;
            Spec = spec;
            CornerRadius = new CornerRadius(10.0);
            this.Dyn(Border.BorderBrushProperty, "B.Accent");
            // 操作条：拖拽手柄 + 块标题 + 显隐按钮
            Button grip = U.IconBtn(GripGlyph, "拖拽排序", null, 12.0);
            grip.Cursor = Cursor.Parse("SizeAll");
            grip.PointerPressed += delegate (object? s, PointerPressedEventArgs e)
            {
                if (s is InputElement el && e.GetCurrentPoint(el).Properties.IsLeftButtonPressed)
                {
                    _host.BeginDrag(this, el, e);
                }
            };
            Button hide = U.IconBtn(CloseGlyph, "隐藏", delegate { _host.HideBlock(Spec); }, 12.0);
            TextBlock title = U.Txt(spec.Title, 12.5, bold: true);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            _bar = U.H(8.0, grip, title, U.Spring(), hide);
            _bar.Margin = new Thickness(6.0, 6.0, 6.0, 2.0);
            // 内容体外壳：常态零边距（逐像素等价旧版），编辑态留一点呼吸边距
            _bodyWrap = new Border { Padding = new Thickness(0.0), Child = _body };
            Grid grid = U.G("Auto", "*");
            grid.Add(_bar, 0);
            grid.Add(_bodyWrap, 1);
            Child = grid;
            _bar.IsVisible = false;
            _body.Content = spec.Content;
        }

        public void SetEdit(bool edit)
        {
            _edit = edit;
            _bar.IsVisible = edit;
            _body.IsHitTestVisible = !edit;
            _bodyWrap.Padding = edit ? new Thickness(6.0, 0.0, 6.0, 8.0) : new Thickness(0.0);
            BorderThickness = new Thickness(edit ? 1.4 : 0.0);
            RefreshTint();
        }

        // 编辑态蒙版：主题色 10 透明度平铺（对齐 Windows 版 CardView.RefreshEditMask）
        public void RefreshTint()
        {
            if (!_edit)
            {
                Background = null;
                return;
            }
            Color c = AccentColor();
            Background = new SolidColorBrush(Color.FromArgb(10, c.R, c.G, c.B));
        }
    }
}
