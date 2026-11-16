using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using TipCL.Services;

namespace TipCL.Controls;

/// <summary>
/// C9a:两个栏位 + **可拖动分隔条 + 比例记忆**(= Windows <c>Pages\Layout\PageLayoutHost.cs</c> 的**那一部分能力**)。
///
/// <b>为什么单独做这一个控件</b>:Windows 里它被 **六个页面**依赖(`VersionManagePage` / `AccountPage` /
/// `LibraryPage` / `SettingsPage` / `FeedbackPage` / 以及插件市场那条列数口径),而 mac 侧六个页面**各自写死了
/// 栏宽**(如 <c>Grid("272,18,*")</c>)⇒ 拖动与记忆这层能力在六处**静默丢失**。Lead 裁决:**一次修在共用的地方**。
///
/// <b>取值全部照 Windows 逐值对齐</b>(<c>LayoutSchema.cs:131/134</c> + <c>PageLayoutHost.cs:297-306/573</c>):
/// <code>
/// RatioMin = 0.15   RatioMax = 0.85   RatioDefault = 0.5   DefaultGap = 18.0
/// 夹取 = Clamp(ratio, Min, Max, Default) ⇒ NaN / Infinity 落 Default
/// </code>
/// 注意 **<c>DefaultGap = 18.0</c> 与 mac 现在写死的 <c>Grid("272,18,*")</c> 里那个 18 是同一个值** ——
/// 这也是"默认渲染逐字节不变"能成立的一半原因(另一半是:没有存储比例时**直接用声明的列宽**)。
///
/// 三级回退(与 <c>SplitRatioStore.Resolve</c> 同口径):**有存储 → 用存储;否则用声明的默认列宽;再否则 0.5**。
///
/// 交互(照 Windows <c>:628-686</c>,一条不少):
/// <list type="bullet">
/// <item><b>拖动</b>改比例(<c>DragStarted/DragCompleted</c>),松手时写回(<c>Writable=false</c> 时不写);</item>
/// <item><b>双击分隔条 = 恢复默认</b>(清掉存储比例,回到声明列宽;Windows <c>:816</c> 的 <c>Clear</c>);</item>
/// <item><b>光标</b> <c>SizeWE</c>;悬停或拖动时中间显一条 1px 细线(色 <c>B.LineSoft</c>);</item>
/// <item><b>ToolTip</b>「自由拖动 · 恢复默认」;</item>
/// <item><b>没有</b>键盘快捷键(Windows 那份也没有 —— 逐条核过)。</item>
/// </list>
///
/// <b>与 Windows 的差异</b>(逐条登记):
/// <list type="number">
/// <item><b>存储位置</b>(R32-MWG 已统一):与 Windows **同一处** —— 布局 v2 文档
///   <c>Layout.Splits[pageId].Ratios[splitId]</c>(经 <c>LayoutStoreV2.Save</c> 落 <c>ui_layout_v2.json</c>,
///   并单向写通 v1 投影);列数落同文档的 <c>Layout.Pages[pageId].Cols</c>(需 <c>Mode == grid</c>);
///   C9a 时期写在 <c>UiPrefs.Splits</c>(键 <c>"&lt;pageId&gt;/&lt;splitId&gt;"</c>)与 <c>UiPrefs.Columns</c> 的旧值
///   **仍读得到**:v2 里没有该处时按遗留值代入并就地迁进 v2,写入成功后删掉旧键(落点统一,见各类注释);</item>
/// <item><b>比例→像素的时机</b>:Windows 在布局回调里随时算;mac 在拿到 <c>Bounds.Width</c> 后应用(首次布局前先按声明列宽,
///   所以**没有存储时渲染与写死版逐字节相同**);</item>
/// <item><b>Auto 左栏</b>(<c>AccountPage</c> 的 <c>GridLength.Auto</c>):无存储时**不用比例算**,保持 Auto(与 Windows 一致:
///   它只在有存储比例时才切到像素列);</item>
/// <item>Windows 的 <c>SplitSite.Gap/LeftMin/LeftMax/RightMin/RightMax</c> 等"声明表"机制来自
///   <c>SplitCatalog</c>(布局家族,本版不移植)⇒ mac 侧只保留默认 18px 间隙;若将来要每处不同的间隙,再补一张小表。</item>
/// </list>
/// </summary>
internal sealed class PageLayoutHost : Grid
{
	/// <summary>比例下限(Windows <c>LayoutSchema.RatioMin</c>)。</summary>
	public const double MinRatio = 0.15;

	/// <summary>比例上限(Windows <c>LayoutSchema.RatioMax</c>)。</summary>
	public const double MaxRatio = 0.85;

	/// <summary>契约默认比例(Windows <c>LayoutSchema.RatioDefault</c>)。</summary>
	public const double DefaultRatio = 0.5;

	/// <summary>两栏之间的默认间隙(Windows <c>PageLayoutHost.DefaultGap</c>;= 现状写死的 18)。</summary>
	public const double DefaultGap = 18.0;

	private readonly string _pageId;

	private readonly string _splitId;

	private readonly GridLength _declaredLeft;

	private readonly GridLength _declaredRight;

	private readonly ColumnDefinition _colLeft = new ColumnDefinition();

	private readonly ColumnDefinition _colGap = new ColumnDefinition();

	private readonly ColumnDefinition _colRight = new ColumnDefinition();

	private readonly Border _line;

	private readonly GridSplitter _splitter;

	private Control? _left;

	private Control? _right;

	private bool _dragging;

	private double _appliedWidth = double.NaN;

	/// <summary>存储里读到的比例(NaN = 没存过 ⇒ 用声明列宽)。</summary>
	private double _storedRatio = double.NaN;

	private bool _embedded;

	/// <summary>
	/// 内嵌态(Windows <c>:249-250</c> 那条 Note:**内嵌态必须隐藏分隔条且不写比例,回落双栏时按 272px 现状还原**)。
	/// 进内嵌态:左栏与间隙收 0、分隔条隐藏(拖动入口不存在)、<see cref="Writable"/> 置 false;
	/// 退出内嵌态:按声明列宽还原(272 / 18 / *)。
	/// </summary>
	internal void SetEmbedded(bool embedded)
	{
		_embedded = embedded;
		Writable = !embedded;
		_splitter.IsVisible = !embedded;
		if (embedded)
		{
			_dragging = false;
			_line.IsVisible = false;
		}
		ApplyColumns();
	}

	/// <summary>当前是否内嵌态(测试与页面自检用)。</summary>
	internal bool Embedded => _embedded;

	/// <summary>分隔条是否可见(内嵌态应不可见 ⇒ 拖动入口不存在)。</summary>
	internal bool SplitterVisible => _splitter.IsVisible;

	/// <summary>分隔条的命中宽度(0 = 只占间隙列;设置页传 8 ⇒ 居中压在边界上)。语义值,不是视觉 Width
	/// (默认 0 时视觉 Widt h仍是 `NaN` = 拉伸占满间隙列)。</summary>
	internal double SplitterHitWidth => _splitterHitWidth;

	/// <summary>分隔条上那条"悬停/拖动才显"的细线当前是否可见(静止时应为 false ⇒ 零像素变化的证据)。</summary>
	internal bool HoverLineVisible => _line.IsVisible;

	/// <summary>该处是否允许写比例(Windows <c>SplitSite.Writable</c>;默认允许)。</summary>
	public bool Writable { get; set; } = true;

	private double _gap = DefaultGap;

	private double _leftMinWidth;

	private readonly double _splitterHitWidth;

	/// <summary>【Windows 源】<c>:628</c> 的构造:pageId / splitId / 左右栏的**声明**列宽。
	/// <paramref name="gap"/> 默认 <see cref="DefaultGap"/>(18,**永远是 Windows 那个值**,不许为某页改);
	/// 某页的间距若原本由孩子自己的外边距提供就传 0,保像素不变(Windows 的 per-site 间距来自未移植的 <c>SplitCatalog</c>)。
	/// <paramref name="leftMinWidth"/> 默认 0;`LibraryPage` 传 300 —— 那是 C5c 的修正
	/// (「左列要兑现左卡的最小宽 300,否则窄页面上卡片会溢出自己的星列」),收起左栏时会被临时置 0。</summary>
	/// <param name="splitterHitWidth">默认 **0**(= 只占间隙列,与现在行为一致);`SettingsPage` 传 8 —— 该页 `gap: 0`,
	/// 所以让分隔条**自己带命中宽度**、居中压在边界上,静止时透明 ⇒ **零像素变化但能拖**。
	/// <b>如实登记</b>:命中区因此**边界两侧各压住内容 4px**(Windows 是 18px 带、不重叠 ⇒ 机制差异、可见结果等价);
	/// 真机上若"贴着边界点内容被分隔条吃掉"很明显,回来报 Lead 再定加宽 gap 还是改非对称。</param>
	public PageLayoutHost(string pageId, string splitId, GridLength left, GridLength right, double gap = DefaultGap, double leftMinWidth = 0.0, double splitterHitWidth = 0.0)
	{
		_pageId = pageId ?? "";
		_splitId = splitId ?? "";
		_declaredLeft = left;
		_declaredRight = right;
		_gap = gap;
		_leftMinWidth = leftMinWidth;
		_splitterHitWidth = splitterHitWidth;
		_colLeft.Width = left;
		_colLeft.MinWidth = leftMinWidth;
		_colGap.Width = new GridLength(gap);
		_colRight.Width = right;
		base.ColumnDefinitions.Add(_colLeft);
		base.ColumnDefinitions.Add(_colGap);
		base.ColumnDefinitions.Add(_colRight);
		_line = new Border
		{
			Width = 1.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Stretch,
			IsVisible = false,
			IsHitTestVisible = false,
		};
		_line.Dyn(Border.BackgroundProperty, "B.LineSoft");
		// 【Avalonia 差异】WPF 用 Panel.SetZIndex(附加属性);Avalonia 里是 Visual.ZIndex 属性
		_line.ZIndex = 1;
		// 分隔条:透明但可命中(Transparent 参与命中测试),悬停/拖动时中间才显一条细线
		_splitter = new GridSplitter
		{
			ResizeDirection = GridResizeDirection.Columns,
			ResizeBehavior = GridResizeBehavior.PreviousAndNext,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			Background = Brushes.Transparent,
			Cursor = new Cursor(StandardCursorType.SizeWestEast),
			Focusable = false,
		};
		ToolTip.SetTip(_splitter, I18n.L("自由拖动") + " · " + I18n.L("恢复默认"));
		if (splitterHitWidth > 0.0)
		{
			// 该页 gap 为 0(如 SettingsPage)⇒ 让分隔条自己带命中宽度,居中压在边界上:
			// 静止时它透明、细线不显示 ⇒ 零像素变化;真机上仍可拖、可双击复位。
			_splitter.Width = splitterHitWidth;
			_splitter.HorizontalAlignment = HorizontalAlignment.Center;
		}
		_splitter.DragStarted += delegate
		{
			_dragging = true;
			ShowLine();
		};
		_splitter.DragCompleted += delegate
		{
			_dragging = false;
			HideLine();
			// 松手才写(与 Windows 的 CommitDrag 同口径):按当前左栏像素宽 / 可用宽 算比例,夹取后落盘
			CommitDrag();
		};
		_splitter.PointerPressed += delegate(object? _, PointerPressedEventArgs e)
		{
			// 双击 = 恢复默认(Windows :816 的 SplitRatioStore.Clear;提示语「自由拖动 · 恢复默认」就是这句)
			if (e.ClickCount == 2)
			{
				ResetToDefault();
				e.Handled = true;
			}
		};
		_splitter.PointerEntered += delegate
		{
			ShowLine();
		};
		_splitter.PointerExited += delegate
		{
			if (!_dragging)
			{
				HideLine();
			}
		};
		_splitter.ZIndex = 2;
		Grid.SetColumn(_line, 1);
		Grid.SetColumn(_splitter, 1);
		base.Children.Add(_line);
		base.Children.Add(_splitter);
		_storedRatio = ReadStored();
		ApplyColumns();
		// 首次拿到宽度后再按比例对一次(有存储时才会真的改列宽)
		base.SizeChanged += delegate
		{
			ApplyColumns();
		};
	}

	/// <summary>左栏(进第 0 列)。Windows <c>SetLeft</c>。**只负责挂孩子**:显隐跟随孩子自己(见 <see cref="LeftCollapsed"/>)。</summary>
	public void SetLeft(Control el)
	{
		_left = el;
		Grid.SetColumn(el, 0);
		base.Children.Insert(0, el);
		// 【Windows 判据】"折叠状态以'孩子自己的 Visibility'为准" ⇒ 孩子可见性一变就重算列
		el.PropertyChanged += delegate(object? _, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == Visual.IsVisibleProperty)
			{
				ApplyColumns();
			}
		};
		ApplyColumns();
	}

	/// <summary>左栏是否处于"收起"状态:内嵌态之外,**左栏孩子自己不可见、或已被移出可视树**都算收起
	/// (Windows <c>LeftVisible</c> 的同一条判据);收起时由本控件把左列与间隙收 0、右栏吃满。</summary>
	internal bool LeftCollapsed => _left != null && (!_left.IsVisible || !base.Children.Contains(_left));

	/// <summary>左栏的声明最小宽(Windows/C5c:`LibraryPage` 传 300;收起时临时置 0)。</summary>
	internal double LeftMinWidth => _leftMinWidth;

	/// <summary>右栏(进第 2 列)。Windows <c>SetRight</c>。</summary>
	public void SetRight(Control el)
	{
		_right = el;
		Grid.SetColumn(el, 2);
		base.Children.Add(el);
		ApplyColumns();
	}

	/// <summary>当前是否已有存储比例(有 = 按比例布局,无 = 现状列定义)。</summary>
	public bool HasStoredRatio => !double.IsNaN(_storedRatio);

	/// <summary>分隔条宽度(现状留白;Windows <c>GapWidth</c>)。</summary>
	public double GapWidth => _gap;

	/// <summary>左栏是否可见(Windows <c>LeftVisible</c>:孩子自己的可见性说了算)。</summary>
	public bool LeftVisible => (_left != null) ? _left.IsVisible : true;

	/// <summary>夹到 0.15~0.85;NaN / Infinity 落默认值(与 Windows <c>ClampRatio</c> 同一实现)。</summary>
	public static double ClampRatio(double ratio)
	{
		return double.IsNaN(ratio) || double.IsInfinity(ratio)
			? DefaultRatio
			: Math.Min(MaxRatio, Math.Max(MinRatio, ratio));
	}

	/// <summary>【Windows 源】<c>SplitRatioStore.RatioFromPixels</c>:左栏像素宽 → 比例。</summary>
	public static double RatioFromPixels(double leftWidth, double available)
	{
		return (available <= 0.0) ? DefaultRatio : ClampRatio(leftWidth / available);
	}

	/// <summary>【Windows 源】<c>SplitRatioStore.PixelsFromRatio</c>:比例 → 左栏像素宽(拿不到宽度时 NaN)。</summary>
	public static double PixelsFromRatio(double ratio, double available)
	{
		return (available <= 0.0) ? double.NaN : ratio * available;
	}

	// ————— 落点统一(R32-MWG):布局 v2 文档是分栏比例/列数的**唯一真源** —————
	//
	// 分工:v2 文档 = Windows 那一份(Splits[pageId][splitId] / Pages[pageId].Cols);
	//       UiPrefs.Splits / UiPrefs.Columns = C9a 时期的旧落点,**只读兜底 + 读时迁移**,写成功即删旧键。
	// 所有 IO 都吞异常:读失败当"没存过"、写失败退旧落点(与 Windows SplitRatioStore 的"读失败 = 没存过"同口径)。

	/// <summary>读 v2 文档(副本;失败一律当"没有")。</summary>
	private static LayoutV2? V2Doc()
	{
		try
		{
			return LayoutStoreV2.Load();
		}
		catch
		{
			return null;
		}
	}

	/// <summary>从 v2 文档读该处比例(没有 / NaN 返回 null;读到就夹取)。</summary>
	private static double? V2ReadRatio(string pageId, string splitId)
	{
		try
		{
			LayoutV2? doc = V2Doc();
			if (doc == null || !doc.Layout.Splits.TryGetValue(pageId, out SplitState? state) || state == null)
			{
				return null;
			}
			if (!state.Ratios.TryGetValue(splitId, out double ratio) || double.IsNaN(ratio))
			{
				return null;
			}
			return ClampRatio(ratio);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>把比例写进 v2 文档并落盘(先夹再写);返回 v2 文件是否写成功。</summary>
	private static bool V2WriteRatio(string pageId, string splitId, double ratio)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (!doc.Layout.Splits.TryGetValue(pageId, out SplitState? state) || state == null)
			{
				state = new SplitState();
				doc.Layout.Splits[pageId] = state;
			}
			state.Ratios[splitId] = ClampRatio(ratio);
			return LayoutStoreV2.Save(doc);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>从 v2 文档删该处比例(空掉的 pageId 一并清掉);返回是否真的删掉了。</summary>
	private static bool V2ClearRatio(string pageId, string splitId)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (!doc.Layout.Splits.TryGetValue(pageId, out SplitState? state) || state == null)
			{
				return false;
			}
			if (!state.Ratios.Remove(splitId))
			{
				return false;
			}
			if (state.Ratios.Count == 0)
			{
				doc.Layout.Splits.Remove(pageId);
			}
			LayoutStoreV2.Save(doc);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 该页在 v2 文档里的列数。返回 <c>false</c> = **文档里没有这一页** ⇒ 调用方该走遗留回退;
	/// 返回 <c>true</c> 而 <paramref name="cols"/> 为 0 = 文档里有这一页但它没声明格子模式(或列数非法)⇒
	/// 按 Windows 原文回落 fallback,**不再看遗留值**(v2 里有这一页就由 v2 说了算)。
	/// </summary>
	private static bool V2ReadColumns(string pageId, out int cols)
	{
		cols = 0;
		try
		{
			LayoutV2? doc = V2Doc();
			if (doc == null || !doc.Layout.Pages.TryGetValue(pageId, out PageLayoutV2? page) || page == null)
			{
				return false;
			}
			cols = (page.Mode == LayoutSchema.ModeGrid) ? page.Cols : 0;
			return true;
		}
		catch
		{
			cols = 0;
			return false;
		}
	}

	/// <summary>该处是否已有存储比例(Windows <c>SplitRatioStore.IsStored</c>)。</summary>
	public static bool IsStored(UiPrefs? prefs, string pageId, string splitId)
	{
		return Read(prefs, pageId, splitId).HasValue;
	}

	/// <summary>
	/// 读该处已存比例;没有存过(或读失败)返回 null(Windows <c>Read</c>,失败一律按"没存过")。
	/// 顺序:**v2 文档 → 遗留 <c>UiPrefs.Splits</c>**;命中遗留值时顺手迁进 v2(迁成功才删旧键)。
	/// </summary>
	public static double? Read(UiPrefs? prefs, string pageId, string splitId)
	{
		if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(splitId))
		{
			return null;
		}
		double? stored = V2ReadRatio(pageId, splitId);
		if (stored.HasValue)
		{
			return stored;
		}
		if (prefs == null)
		{
			return null;
		}
		try
		{
			if (!prefs.Splits.TryGetValue(Key(pageId, splitId), out double legacy) || double.IsNaN(legacy))
			{
				return null;
			}
			double clamped = ClampRatio(legacy);
			if (V2WriteRatio(pageId, splitId, clamped))
			{
				// 迁移成功才删旧键:调用方随后照旧 SaveUiPrefs(),这次删除也就落了盘
				prefs.Splits.Remove(Key(pageId, splitId));
			}
			return clamped;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// 三级回退(Windows <c>SplitRatioStore.Resolve</c>):有存储用存储 → 否则用声明的现状比例 → 再否则 0.5。
	/// mac 侧"声明的现状比例"由调用方按声明列宽与可用宽现算传入(拿不到就传 <c>double.NaN</c>)。
	/// </summary>
	public static double Resolve(UiPrefs? prefs, string pageId, string splitId, double declaredRatio = double.NaN)
	{
		double? stored = Read(prefs, pageId, splitId);
		if (stored.HasValue)
		{
			return ClampRatio(stored.Value);
		}
		if (!double.IsNaN(declaredRatio) && declaredRatio > 0.0)
		{
			return ClampRatio(declaredRatio);
		}
		return DefaultRatio;
	}

	/// <summary>
	/// 写该处比例(唯一入口;Windows <c>SplitRatioStore.Write</c>):先夹再写,失败吞掉并返回 false。
	/// <b>未声明可写(Writable=false)一律不写</b>(Windows <c>:361-375</c>)。
	/// 落点是 **v2 文档**(真源);写成功就删掉遗留偏好键(落点统一),v2 写不进去时才退回偏好,免得这一次拖动白拖。
	/// </summary>
	public static bool Write(UiPrefs? prefs, string pageId, string splitId, double ratio, bool writable = true)
	{
		if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(splitId) || !writable)
		{
			return false;
		}
		double clamped = ClampRatio(ratio);
		bool ok = V2WriteRatio(pageId, splitId, clamped);
		if (prefs == null)
		{
			return ok;
		}
		try
		{
			if (ok)
			{
				prefs.Splits.Remove(Key(pageId, splitId));
				return true;
			}
			prefs.Splits[Key(pageId, splitId)] = clamped;
			return true;
		}
		catch
		{
			return ok;
		}
	}

	/// <summary>复位:删掉该处比例(Windows <c>SplitRatioStore.Clear</c>);**v2 与遗留两处都删**,
	/// 返回是否真的删掉了东西(两处都没有 = false)。</summary>
	public static bool Clear(UiPrefs? prefs, string pageId, string splitId)
	{
		if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(splitId))
		{
			return false;
		}
		bool removed = V2ClearRatio(pageId, splitId);
		if (prefs == null)
		{
			return removed;
		}
		try
		{
			return prefs.Splits.Remove(Key(pageId, splitId)) || removed;
		}
		catch
		{
			return removed;
		}
	}

	/// <summary>存储键(<c>"&lt;pageId&gt;/&lt;splitId&gt;"</c>)。</summary>
	public static string Key(string pageId, string splitId)
	{
		return (pageId ?? "") + "/" + (splitId ?? "");
	}

	/// <summary>
	/// 格子类布局的**列数**(Windows <c>SplitRatioStore.PageColumns</c>,`:536-562`)。
	/// Windows 原文规则:**默认 <paramref name="fallback"/>;只有该页在布局文档里声明了 `Mode == grid` 且 `Cols &gt; 0`
	/// 时才用存储值**;存储值 `&lt;= 0` 回落 fallback;最后夹到 <c>[min, max]</c>。
	/// R32-MWG:mac 侧现在**读同一张布局文档**(<c>Layout.Pages[pageId]</c>)—— 文档里有这一页就由它说了算;
	/// 文档里没有这一页才回退到旧偏好 <c>UiPrefs.Columns</c>(C9a 时期的落点,读得到就不算丢)。
	/// <paramref name="gridMode"/> 默认 **true**:理由 —— **mac 侧凡是调用本方法的页面,本身就是"声明自己是格子"的那一方**
	/// (目前只有插件页已安装网格);将来若出现"只想拿 fallback、不想读存储"的调用点,显式传 <c>false</c>。
	/// </summary>
	public static int PageColumns(UiPrefs? prefs, string pageId, int fallback, int min, int max, bool gridMode = true)
	{
		int cols = fallback;
		try
		{
			if (gridMode && !string.IsNullOrEmpty(pageId))
			{
				if (V2ReadColumns(pageId, out int fromDoc))
				{
					cols = fromDoc;
				}
				else if (prefs != null && prefs.Columns.TryGetValue(pageId, out int legacy))
				{
					cols = legacy;
				}
			}
		}
		catch
		{
			cols = fallback;
		}
		if (cols <= 0)
		{
			cols = fallback;
		}
		return Math.Min(max, Math.Max(min, cols));
	}

	/// <summary>写该页的列数(夹到 <c>[min, max]</c> 后落盘;落点是布局 v2 文档的 <c>Pages[pageId].Cols</c>,
	/// 并把该页声明成格子模式 <c>Mode = grid</c> —— Windows 的读侧就是这么筛的;旧偏好键一并删掉)。
	/// R32-MWG 实测:**保存会被 schema 归一化**,<c>LayoutSchema.SanitizePage</c> 用 <c>SnapCols</c>
	/// 把列数吸附到 <c>ColChoices</c> = {2,3,4,6,12}(Windows 同一套词表,列数真源是 L2/L5 的格子编辑器)
	/// —— 所以本方法写 5 会落成 4、写 1 会落成 2;要断言"只夹不吸"就传正好落在词表里的值。</summary>
	public static bool WritePageColumns(UiPrefs? prefs, string pageId, int columns, int min, int max)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return false;
		}
		int clamped = Math.Min(max, Math.Max(min, columns));
		bool ok = false;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (!doc.Layout.Pages.TryGetValue(pageId, out PageLayoutV2? page) || page == null)
			{
				page = new PageLayoutV2();
				doc.Layout.Pages[pageId] = page;
			}
			page.Mode = LayoutSchema.ModeGrid;
			page.Cols = clamped;
			ok = LayoutStoreV2.Save(doc);
		}
		catch
		{
			ok = false;
		}
		if (prefs == null)
		{
			return ok;
		}
		try
		{
			if (ok)
			{
				prefs.Columns.Remove(pageId);
				return true;
			}
			prefs.Columns[pageId] = clamped;
			return true;
		}
		catch
		{
			return ok;
		}
	}

	/// <summary>当前生效比例(给测试与设置页用):有存储用存储,否则按声明与可用宽现算,再不行 0.5。</summary>
	public double CurrentRatio
	{
		get
		{
			if (!double.IsNaN(_storedRatio))
			{
				return _storedRatio;
			}
			double available = AvailableWidth();
			if (double.IsNaN(available) || !(_declaredLeft.IsAbsolute))
			{
				return DefaultRatio;
			}
			return RatioFromPixels(_declaredLeft.Value, available);
		}
	}

	/// <summary>按当前状态把列宽落下去:**收起**(内嵌态,或左栏孩子自己不可见/已被移出可视树)⇒ 左列与间隙收 0、
	/// 右栏吃满(页面**不要**再自己动列定义/列号,见类注释的归属说明);有存储比例 ⇒ 像素列;否则 ⇒ 声明列宽。</summary>
	private void ApplyColumns()
	{
		if (_embedded || LeftCollapsed)
		{
			_colLeft.MinWidth = 0.0;
			_colLeft.Width = new GridLength(0.0);
			_colGap.Width = new GridLength(0.0);
			_colRight.Width = new GridLength(1.0, GridUnitType.Star);
			return;
		}
		_colLeft.MinWidth = _leftMinWidth;
		double available = AvailableWidth();
		if (!double.IsNaN(_storedRatio) && !double.IsNaN(available) && available > 0.0)
		{
			double pixels = PixelsFromRatio(_storedRatio, available);
			if (!double.IsNaN(pixels))
			{
				_colLeft.Width = new GridLength(pixels);
				_colRight.Width = new GridLength(Math.Max(0.0, available - pixels - _gap));
				_appliedWidth = available;
				return;
			}
		}
		// 没有存储(或还拿不到宽度)⇒ **声明的现状列宽**(默认渲染与写死版逐字节相同的原因)
		_colLeft.Width = _declaredLeft;
		_colRight.Width = _declaredRight;
	}

	private double AvailableWidth()
	{
		return base.Bounds.Width;
	}

	/// <summary>拖动松手:按当前左栏像素宽算比例并写回(不可写时只改本次会话的显示)。</summary>
	private void CommitDrag()
	{
		CommitDragCore();
	}

	/// <summary>
	/// 【测试缝 · D-127】拖动松手那条路的入口(与真实拖动**同一段代码**):真实路径是
	/// <c>GridSplitter.DragCompleted</c> → <see cref="CommitDrag"/>;这里只把"松手"这一步直接暴露给用例,
	/// 不去模拟鼠标拖拽(D-115:驱动内部处理器体,不模拟输入设备)。
	/// </summary>
	internal void CommitDragForTest()
	{
		CommitDragCore();
	}

	/// <summary>
	/// 【测试缝 · D-127】双击分隔条那条路的入口(与真实路径同一段代码):真实路径是
	/// <c>PointerPressed</c> 且 <c>ClickCount == 2</c> → <see cref="ResetToDefault"/>。
	/// </summary>
	internal void ResetToDefaultForTest()
	{
		ResetToDefault();
	}

	private void CommitDragCore()
	{
		// 收起状态拖不了(分隔条本身也隐藏)⇒ 自然不该落盘
		if (_embedded || LeftCollapsed)
		{
			return;
		}
		double available = AvailableWidth();
		if (double.IsNaN(available) || available <= 0.0)
		{
			return;
		}
		double leftPixels = _colLeft.Width.IsAbsolute ? _colLeft.Width.Value : available * DefaultRatio;
		double ratio = RatioFromPixels(leftPixels, available);
		if (Writable)
		{
			MainWindow? win = PageBase.Win;
			if (win != null && Write(win.Prefs, _pageId, _splitId, ratio))
			{
				win.SaveUiPrefs();
			}
		}
		_storedRatio = ratio;
	}

	/// <summary>双击分隔条:清掉存储比例并回到声明列宽(Windows 的"恢复默认")。</summary>
	private void ResetToDefault()
	{
		MainWindow? win = PageBase.Win;
		if (win != null && Writable && Clear(win.Prefs, _pageId, _splitId))
		{
			win.SaveUiPrefs();
		}
		_storedRatio = double.NaN;
		ApplyColumns();
	}

	private double ReadStored()
	{
		MainWindow? win = PageBase.Win;
		if (win == null)
		{
			return double.NaN;
		}
		return Read(win.Prefs, _pageId, _splitId) ?? double.NaN;
	}

	private void ShowLine()
	{
		_line.IsVisible = true;
	}

	private void HideLine()
	{
		_line.IsVisible = false;
	}
}
