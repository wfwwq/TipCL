using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// R34 v3 的「添加」抽屉(§5.2):从编辑器右侧滑出(宽 316),取代原来的底部托盘。
//   上半部分「已隐藏的内容」:本页被隐藏的块,每项 图标 + 名称 + 一句说明 +「显示」;
//   下半部分「组件」:可新建的组件类型(不允许重复的类型已加过就「已添加」置灰),
//   以及「已创建、还没放到本页」的组件;网页 / 按钮两种只在 DangerWidgetsAllowed() 为真时出现。
// 纯规则(标题 / 列表 / 页面名)放在本文件下半部分的零 WPF 静态类里,net8 测试宿主可以直接断言。
public sealed class LayoutAddDrawer
{
	/// <summary>
	/// 网页 / 按钮组件的全局开关(Q6)。本包只提供钩子、默认关;合并后由主 agent 接线:
	/// <c>LayoutAddDrawer.DangerWidgetsAllowed = () =&gt; LayoutEditPrefs.AllowDangerWidgets;</c>
	/// </summary>
	public static Func<bool> DangerWidgetsAllowed = () => false;

	/// <summary>抽屉宽度(效果图 316px)。</summary>
	public const double DrawerWidth = 316.0;

	private readonly Border _root;

	private readonly TextBlock _title;

	private readonly SPanel _hiddenList = Ui.V(6.0);

	private readonly SPanel _widgetList = Ui.V(6.0);

	private readonly SPanel _existingList = Ui.V(6.0);

	private readonly TextBlock _existingHead;

	private readonly Border _dangerNote;

	private readonly TranslateTransform _slide = new TranslateTransform(DrawerWidth + 24.0, 0.0);

	// 当前展开了输入框的那一项(类型名);空 = 都收着
	private string _expanded = "";

	private bool _open;

	private int _animVersion;

	/// <summary>点「显示」:参数是块 id。</summary>
	internal Action<string>? ShowHidden;

	/// <summary>点「添加」新建组件:参数 (类型, 输入值);返回 null = 成功,否则是可直接显示的原因。</summary>
	internal Func<string, string, string?>? AddType;

	/// <summary>点「添加」已创建的组件:参数是 widget id。</summary>
	internal Action<string>? AddExisting;

	/// <summary>抽屉关上之后(宿主把焦点拿回去)。</summary>
	internal Action? Closed;

	/// <summary>「设置 → 布局 → 高级」链接。</summary>
	internal Action? OpenAdvanced;

	/// <summary>全局开关当前是否打开(钩子抛异常一律按关处理)。</summary>
	public static bool IsDangerAllowed()
	{
		try
		{
			Func<bool>? hook = DangerWidgetsAllowed;
			return hook != null && hook();
		}
		catch
		{
			return false;
		}
	}

	internal LayoutAddDrawer()
	{
		_title = Ui.Txt("", 15.0, bold: true);
		_title.TextTrimming = TextTrimming.CharacterEllipsis;
		_title.VerticalAlignment = VerticalAlignment.Center;
		Button close = Ui.IconBtn(Ico.Close, I18n.L("关闭 (Esc)"), delegate
		{
			Close();
		}, 12.0);
		Grid head = Ui.G(null, "*,Auto");
		head.Add(_title, 0, 0);
		head.Add(close, 0, 1);
		head.Margin = new Thickness(16.0, 14.0, 12.0, 10.0);
		_existingHead = SectionHead(I18n.L("已创建、还没放到本页的组件"));
		TextBlock note = Ui.Txt(I18n.L("网页、按钮组件默认关闭,可在「设置 → 布局 → 高级」里开启。"), 12.0, bold: false, "B.InkSoft");
		note.TextWrapping = TextWrapping.Wrap;
		Button go = Ui.Btn(I18n.L("去开启"), BtnKind.Ghost, delegate
		{
			OpenAdvanced?.Invoke();
		});
		go.HorizontalAlignment = HorizontalAlignment.Left;
		go.Margin = new Thickness(-6.0, 4.0, 0.0, 0.0);
		_dangerNote = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(12.0, 10.0, 12.0, 8.0),
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			Child = Ui.V(0.0, note, go)
		};
		_dangerNote.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		SPanel list = Ui.V(0.0, SectionHead(I18n.L("已隐藏的内容")), _hiddenList, SectionHead(I18n.L("组件")), _widgetList, _existingHead, _existingList, _dangerNote);
		list.Margin = new Thickness(16.0, 0.0, 16.0, 16.0);
		SmoothScroll scroll = Ui.Scroll(list);
		scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		Grid body = Ui.G("Auto,*");
		body.Add(head, 0);
		body.Add(scroll, 1);
		_root = new Border
		{
			Width = DrawerWidth,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Stretch,
			Visibility = Visibility.Collapsed,
			RenderTransform = _slide,
			Child = body,
			Focusable = false
		};
		if (!LayoutEditLook.TryApplyStyle(_root, "Layout.Drawer"))
		{
			LayoutEditLook.Bind(_root, Border.BackgroundProperty, "B.EditBarBg", "B.Paper");
			_root.SetResourceReference(Border.BorderBrushProperty, "B.Line");
			_root.BorderThickness = new Thickness(1.0, 0.0, 0.0, 0.0);
			_root.CornerRadius = new CornerRadius(12.0, 0.0, 0.0, 12.0);
			Motion.Shadow(_root, 32.0, 0.14, 0.0);
		}
		// 抽屉里点空白不能穿透到画布(否则会被当成框选 / 取消选择)
		_root.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
		};
	}

	/// <summary>根元素(宿主把它叠在编辑器右侧)。</summary>
	public FrameworkElement Root => _root;

	/// <summary>是否开着。</summary>
	public bool IsOpen => _open;

	/// <summary>页名(标题「添加到「页名」」)。</summary>
	internal void SetPageName(string name)
	{
		_title.Text = I18n.L("添加到「{0}」", name ?? "");
	}

	/// <summary>滑出。</summary>
	internal void Open()
	{
		if (_open)
		{
			return;
		}
		_open = true;
		_expanded = "";
		_root.Visibility = Visibility.Visible;
		++_animVersion;
		if (!Motion.Enabled)
		{
			_slide.BeginAnimation(TranslateTransform.XProperty, null);
			_slide.X = 0.0;
			return;
		}
		DoubleAnimation animation = new DoubleAnimation(DrawerWidth + 24.0, 0.0, TimeSpan.FromMilliseconds(260.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		_slide.BeginAnimation(TranslateTransform.XProperty, animation);
	}

	/// <summary>收回(动画结束后折叠,不再占命中测试)。</summary>
	internal void Close()
	{
		if (!_open)
		{
			return;
		}
		_open = false;
		_expanded = "";
		int version = ++_animVersion;
		if (!Motion.Enabled)
		{
			_slide.BeginAnimation(TranslateTransform.XProperty, null);
			_slide.X = DrawerWidth + 24.0;
			_root.Visibility = Visibility.Collapsed;
			Closed?.Invoke();
			return;
		}
		DoubleAnimation animation = new DoubleAnimation(DrawerWidth + 24.0, TimeSpan.FromMilliseconds(180.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			}
		};
		animation.Completed += delegate
		{
			if (version == _animVersion && !_open)
			{
				_root.Visibility = Visibility.Collapsed;
			}
		};
		_slide.BeginAnimation(TranslateTransform.XProperty, animation);
		Closed?.Invoke();
	}

	/// <summary>立刻收起(退出编辑态时用,不播动画)。</summary>
	internal void CloseNow()
	{
		_open = false;
		_expanded = "";
		++_animVersion;
		_slide.BeginAnimation(TranslateTransform.XProperty, null);
		_slide.X = DrawerWidth + 24.0;
		_root.Visibility = Visibility.Collapsed;
	}

	/// <summary>按最新的页面状态重画两段列表。</summary>
	internal void Refresh(IReadOnlyList<LayoutAddItem> hidden, IReadOnlyList<LayoutAddItem> types, IReadOnlyList<LayoutAddItem> existing, bool dangerAllowed)
	{
		_hiddenList.Children.Clear();
		if (hidden == null || hidden.Count == 0)
		{
			_hiddenList.Children.Add(EmptyBox(I18n.L("没有隐藏的内容")));
		}
		else
		{
			foreach (LayoutAddItem item in hidden)
			{
				LayoutAddItem captured = item;
				_hiddenList.Children.Add(Row(item, I18n.L("显示"), enabled: true, delegate
				{
					ShowHidden?.Invoke(captured.Key);
				}));
			}
		}
		_widgetList.Children.Clear();
		if (types != null)
		{
			foreach (LayoutAddItem item in types)
			{
				_widgetList.Children.Add(TypeRow(item));
			}
		}
		_existingList.Children.Clear();
		if (existing != null)
		{
			foreach (LayoutAddItem item in existing)
			{
				LayoutAddItem captured = item;
				_existingList.Children.Add(Row(item, item.Added ? I18n.L("已添加") : I18n.L("添加"), !item.Added, delegate
				{
					AddExisting?.Invoke(captured.Key);
				}));
			}
		}
		bool anyExisting = _existingList.Children.Count > 0;
		_existingHead.Visibility = (anyExisting ? Visibility.Visible : Visibility.Collapsed);
		_existingList.Visibility = (anyExisting ? Visibility.Visible : Visibility.Collapsed);
		_dangerNote.Visibility = (dangerAllowed ? Visibility.Collapsed : Visibility.Visible);
	}

	// ————— 列表项 —————

	private static TextBlock SectionHead(string text)
	{
		TextBlock head = Ui.Txt(text, 12.0, bold: false, "B.InkFaint");
		head.Margin = new Thickness(0.0, 10.0, 0.0, 6.0);
		return head;
	}

	// 虚线空状态框
	private static FrameworkElement EmptyBox(string text)
	{
		TextBlock label = Ui.Txt(text, 12.0, bold: false, "B.InkFaint");
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.Margin = new Thickness(10.0);
		Rectangle dash = new Rectangle
		{
			RadiusX = 12.0,
			RadiusY = 12.0,
			StrokeThickness = 1.0,
			StrokeDashArray = new DoubleCollection { 4.0, 3.0 },
			IsHitTestVisible = false
		};
		LayoutEditLook.Bind(dash, Shape.StrokeProperty, "B.EditOutline", "B.Scroll");
		Grid grid = new Grid();
		grid.Children.Add(dash);
		grid.Children.Add(label);
		return grid;
	}

	private static FrameworkElement Row(LayoutAddItem item, string action, bool enabled, Action click)
	{
		Border icon = new Border
		{
			Width = 40.0,
			Height = 40.0,
			CornerRadius = new CornerRadius(8.0),
			Child = Ui.Glyph(string.IsNullOrEmpty(item.Glyph) ? Ico.List : item.Glyph, 16.0, "B.InkSoft").Center()
		};
		icon.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		TextBlock name = Ui.Txt(item.Title, 13.0, bold: true);
		name.TextTrimming = TextTrimming.CharacterEllipsis;
		TextBlock desc = Ui.Txt(item.Description, 12.0, bold: false, "B.InkSoft");
		desc.TextTrimming = TextTrimming.CharacterEllipsis;
		desc.ToolTip = item.Description;
		SPanel text = Ui.V(1.0, name, desc);
		text.VerticalAlignment = VerticalAlignment.Center;
		text.Margin = new Thickness(10.0, 0.0, 10.0, 0.0);
		Button button = Ui.Btn(action, enabled ? BtnKind.Soft : BtnKind.Normal, delegate
		{
			click();
		});
		button.IsEnabled = enabled;
		button.VerticalAlignment = VerticalAlignment.Center;
		Grid grid = Ui.G(null, "40,*,Auto");
		grid.Add(icon, 0, 0);
		grid.Add(text, 0, 1);
		grid.Add(button, 0, 2);
		Border row = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(8.0),
			Child = grid
		};
		row.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		return row;
	}

	// 组件类型:不需要参数的一点就加;便签 / 快捷启动 / 网页 / 按钮先展开一个输入行
	private FrameworkElement TypeRow(LayoutAddItem item)
	{
		string type = item.Key;
		bool needsInput = item.NeedsInput;
		string label = item.Added ? I18n.L("已添加") : I18n.L("添加");
		FrameworkElement row = Row(item, label, !item.Added, delegate
		{
			if (needsInput)
			{
				_expanded = (string.Equals(_expanded, type, StringComparison.Ordinal) ? "" : type);
				RefreshExpanded();
				return;
			}
			string? error = AddType?.Invoke(type, "");
			if (!string.IsNullOrEmpty(error))
			{
				AppServices.Window?.Toast(I18n.L("没能添加"), error ?? "", ToastKind.Warning);
			}
		});
		if (!needsInput || item.Added)
		{
			return row;
		}
		SPanel wrap = Ui.V(6.0, row);
		wrap.Tag = type;
		FrameworkElement form = InputForm(type);
		form.Visibility = (string.Equals(_expanded, type, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed);
		wrap.Children.Add(form);
		return wrap;
	}

	// 只切输入行的可见性(不整段重建,输入到一半的内容不丢)
	private void RefreshExpanded()
	{
		foreach (UIElement child in _widgetList.Children)
		{
			if (child is SPanel wrap && wrap.Tag is string type && wrap.Children.Count > 1)
			{
				bool show = string.Equals(_expanded, type, StringComparison.Ordinal);
				wrap.Children[1].Visibility = (show ? Visibility.Visible : Visibility.Collapsed);
				if (show && wrap.Children[1] is FrameworkElement form)
				{
					form.Dispatcher.BeginInvoke(new Action(delegate
					{
						FocusFirstInput(form);
					}), System.Windows.Threading.DispatcherPriority.Input);
				}
			}
		}
	}

	private static void FocusFirstInput(DependencyObject root)
	{
		if (root is TextBox box)
		{
			box.Focus();
			return;
		}
		if (root is ComboBox combo)
		{
			combo.Focus();
			return;
		}
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			FocusFirstInput(VisualTreeHelper.GetChild(root, i));
		}
	}

	private FrameworkElement InputForm(string type)
	{
		Func<string> read;
		FrameworkElement input;
		if (string.Equals(type, LayoutSchema.WidgetButton, StringComparison.Ordinal))
		{
			List<string> labels = new List<string>();
			foreach (KeyValuePair<string, string> preset in LayoutAddCatalog.ButtonPresets())
			{
				labels.Add(preset.Key);
			}
			ComboBox combo = Ui.Combo(labels, (labels.Count > 0) ? labels[0] : null);
			input = combo;
			read = delegate
			{
				string chosen = combo.SelectedItem as string ?? "";
				foreach (KeyValuePair<string, string> preset in LayoutAddCatalog.ButtonPresets())
				{
					if (string.Equals(preset.Key, chosen, StringComparison.Ordinal))
					{
						return preset.Value;
					}
				}
				return "";
			};
		}
		else
		{
			TextBox box = Ui.Input(LayoutAddCatalog.InputPlaceholder(type));
			input = box;
			read = () => box.Text ?? "";
			box.KeyDown += delegate(object _, KeyEventArgs e)
			{
				if (e.Key == Key.Enter)
				{
					Submit(type, read());
					e.Handled = true;
				}
			};
		}
		Button ok = Ui.Btn(I18n.L("添加"), BtnKind.Primary, delegate
		{
			Submit(type, read());
		});
		Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost, delegate
		{
			_expanded = "";
			RefreshExpanded();
		});
		Grid grid = Ui.G(null, "*,Auto,Auto");
		grid.Add(input, 0, 0);
		grid.Add(ok.M(6.0, 0.0, 0.0, 0.0), 0, 1);
		grid.Add(cancel.M(4.0, 0.0, 0.0, 0.0), 0, 2);
		grid.Margin = new Thickness(8.0, 0.0, 0.0, 2.0);
		return grid;
	}

	private void Submit(string type, string value)
	{
		string? error = AddType?.Invoke(type, value ?? "");
		if (!string.IsNullOrEmpty(error))
		{
			AppServices.Window?.Toast(I18n.L("没能添加"), error ?? "", ToastKind.Warning);
			return;
		}
		_expanded = "";
	}
}

// ————— 纯规则(零 WPF;net8 测试宿主直接断言) —————

/// <summary>区块显示标题(§5.1):永远不把块 id 显示给用户。</summary>
public static class LayoutBlockTitles
{
	/// <summary>组件类型的显示名(与 WidgetUI.TypeLabel 同一组词条)。</summary>
	public static string TypeLabel(string? type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return I18n.L("便签");
		case LayoutSchema.WidgetShortcut:
			return I18n.L("快捷启动");
		case LayoutSchema.WidgetClock:
			return I18n.L("时钟");
		case LayoutSchema.WidgetDivider:
			return I18n.L("分隔线");
		case LayoutSchema.WidgetPlaytime:
			return I18n.L("游戏时长");
		case LayoutSchema.WidgetWeb:
			return I18n.L("网页");
		case LayoutSchema.WidgetButton:
			return I18n.L("按钮");
		default:
			return I18n.L("自建区块");
		}
	}

	/// <summary>组件标题:widgets[] 里的 title;没有就用类型名。</summary>
	public static string WidgetTitle(WidgetDef? widget)
	{
		if (widget != null && !string.IsNullOrWhiteSpace(widget.Title))
		{
			return widget.Title.Trim();
		}
		return TypeLabel(widget?.Type);
	}

	/// <summary>
	/// 块的显示标题:页面 spec 给了标题就用它(标题恰好等于 id 的不算);<c>custom:</c> 块取组件标题 → 类型名;
	/// <c>plugin:</c> 块取插件给的标题;都拿不到就用通用名 —— 任何分支都不返回块 id。
	/// </summary>
	public static string Resolve(string? blockId, string? specTitle, Func<string, WidgetDef?>? findWidget, Func<string, string, string?>? pluginTitle)
	{
		string id = blockId ?? "";
		if (!string.IsNullOrWhiteSpace(specTitle) && !string.Equals(specTitle!.Trim(), id, StringComparison.Ordinal))
		{
			return specTitle!.Trim();
		}
		string? widgetId = LayoutEngine.WidgetIdOf(id);
		if (widgetId != null)
		{
			WidgetDef? widget = null;
			try
			{
				widget = findWidget?.Invoke(widgetId);
			}
			catch
			{
				widget = null;
			}
			return (widget != null) ? WidgetTitle(widget) : I18n.L("自建区块");
		}
		if (LayoutEngine.TryParsePluginId(id, out string plugin, out string inner))
		{
			string? title = null;
			try
			{
				title = pluginTitle?.Invoke(plugin, inner);
			}
			catch
			{
				title = null;
			}
			if (!string.IsNullOrWhiteSpace(title))
			{
				string clean = title!.Trim();
				if (!string.Equals(clean, id, StringComparison.Ordinal) && !string.Equals(clean, inner, StringComparison.Ordinal) && !string.Equals(clean, plugin, StringComparison.Ordinal))
				{
					return clean;
				}
			}
			return I18n.L("插件区块");
		}
		return I18n.L("区块");
	}

	/// <summary>浮动标签上标题后面的灰字后缀:自建组件 =「· 组件」,其它为空。</summary>
	public static string LabelSuffix(string? blockId)
	{
		return (LayoutEngine.WidgetIdOf(blockId) != null) ? ("· " + I18n.L("组件")) : "";
	}
}

/// <summary>编辑条 / 抽屉标题里的页面名(pageId → 现有导航文案)。</summary>
public static class LayoutEditPageNames
{
	private static readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["launch"] = "启动",
		["download"] = "下载",
		["library:main"] = "实例库",
		["multiplayer"] = "联机",
		["account"] = "账号",
		["ai"] = "AI 助手",
		["tasks"] = "下载任务",
		["playtime"] = "游戏时长",
		["servers"] = "服务器",
		["settings:general"] = "通用",
		["settings:network"] = "下载与网络",
		["settings:about"] = "反馈与关于",
		["feedback"] = "反馈与帮助",
		["settings:layout"] = "布局",
		["version-manage"] = "版本管理",
		["version-manage:embedded"] = "版本管理",
		["install"] = "版本安装"
	};

	/// <summary>页名;不认识的页返回 null(宿主再按插件页等查,最后用「本页」)。</summary>
	public static string? KnownNameOf(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		if (string.Equals(pageId, "java", StringComparison.Ordinal))
		{
			return "Java";
		}
		return _names.TryGetValue(pageId!, out string? key) ? I18n.L(key) : null;
	}

	/// <summary>页名(不认识 =「本页」)。</summary>
	public static string NameOf(string? pageId)
	{
		return KnownNameOf(pageId) ?? I18n.L("本页");
	}
}

/// <summary>抽屉列表项的类别。</summary>
public enum LayoutAddKind
{
	/// <summary>本页被隐藏的块(Key = 块 id)。</summary>
	Hidden,
	/// <summary>可新建的组件类型(Key = 类型名)。</summary>
	NewWidget,
	/// <summary>已创建、还没放到本页的组件(Key = widget id)。</summary>
	ExistingWidget
}

/// <summary>抽屉里的一项(纯数据)。</summary>
public sealed class LayoutAddItem
{
	public LayoutAddKind Kind { get; set; }

	/// <summary>块 id / 类型名 / widget id(只用于回调,永远不显示)。</summary>
	public string Key { get; set; } = "";

	/// <summary>组件类型(内置块为空)。</summary>
	public string Type { get; set; } = "";

	public string Title { get; set; } = "";

	public string Description { get; set; } = "";

	/// <summary>Ico 字形。</summary>
	public string Glyph { get; set; } = "";

	/// <summary>不允许重复的类型已经在本页了(按钮显示「已添加」并置灰)。</summary>
	public bool Added { get; set; }

	/// <summary>添加前要先填一个参数(便签内容 / 实例名 / 网页地址 / 按钮动作)。</summary>
	public bool NeedsInput { get; set; }
}

/// <summary>「添加」抽屉的列表规则。</summary>
public static class LayoutAddCatalog
{
	/// <summary>抽屉里组件类型的顺序(效果图顺序;网页 / 按钮排最后,且受全局开关控制)。</summary>
	public static readonly string[] TypeOrder = new string[7]
	{
		LayoutSchema.WidgetNote,
		LayoutSchema.WidgetClock,
		LayoutSchema.WidgetShortcut,
		LayoutSchema.WidgetPlaytime,
		LayoutSchema.WidgetDivider,
		LayoutSchema.WidgetWeb,
		LayoutSchema.WidgetButton
	};

	/// <summary>同一页可以放多个的类型(效果图:便签、分隔线);其它类型一页一个。</summary>
	public static bool AllowsMultiple(string? type)
	{
		return string.Equals(type, LayoutSchema.WidgetNote, StringComparison.Ordinal) || string.Equals(type, LayoutSchema.WidgetDivider, StringComparison.Ordinal);
	}

	/// <summary>添加前需要先填参数的类型。</summary>
	public static bool NeedsInput(string? type)
	{
		return string.Equals(type, LayoutSchema.WidgetNote, StringComparison.Ordinal) || string.Equals(type, LayoutSchema.WidgetShortcut, StringComparison.Ordinal) || string.Equals(type, LayoutSchema.WidgetWeb, StringComparison.Ordinal) || string.Equals(type, LayoutSchema.WidgetButton, StringComparison.Ordinal);
	}

	/// <summary>一句说明(效果图文案)。</summary>
	public static string DescriptionOf(string? type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return I18n.L("记一句话,可以加多张");
		case LayoutSchema.WidgetClock:
			return I18n.L("当前时间和日期");
		case LayoutSchema.WidgetShortcut:
			return I18n.L("一键启动指定实例");
		case LayoutSchema.WidgetPlaytime:
			return I18n.L("本周每天玩了多久");
		case LayoutSchema.WidgetDivider:
			return I18n.L("把页面分成两段");
		case LayoutSchema.WidgetWeb:
			return I18n.L("嵌入一个 https 网页");
		case LayoutSchema.WidgetButton:
			return I18n.L("一键执行启动器里的动作");
		default:
			return "";
		}
	}

	/// <summary>输入框占位文字。</summary>
	public static string InputPlaceholder(string? type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return I18n.L("便签内容");
		case LayoutSchema.WidgetShortcut:
			return I18n.L("实例名");
		case LayoutSchema.WidgetWeb:
			return "https://";
		default:
			return "";
		}
	}

	/// <summary>Ico 字形(只复用现有字形)。</summary>
	public static string GlyphOf(string? type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return Ico.Edit;
		case LayoutSchema.WidgetClock:
			return Ico.Clock;
		case LayoutSchema.WidgetShortcut:
			return Ico.Play;
		case LayoutSchema.WidgetPlaytime:
			return Ico.Data;
		case LayoutSchema.WidgetDivider:
			return Ico.Sort;
		case LayoutSchema.WidgetWeb:
			return Ico.World;
		case LayoutSchema.WidgetButton:
			return Ico.Lightning;
		default:
			return Ico.Grid;
		}
	}

	/// <summary>按钮组件可选的动作(显示名 → 动作串;都在 WidgetCommon 的白名单里)。</summary>
	public static List<KeyValuePair<string, string>> ButtonPresets()
	{
		return new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>(I18n.L("打开启动器文件夹"), "folder:data"),
			new KeyValuePair<string, string>(I18n.L("切到「启动」页"), "page:launch"),
			new KeyValuePair<string, string>(I18n.L("切到「下载」页"), "page:download"),
			new KeyValuePair<string, string>(I18n.L("切到「实例库」页"), "page:library")
		};
	}

	/// <summary>页面上某块对应的组件类型(不是 custom: 块或找不到定义时为空串)。</summary>
	public static string TypeOfBlock(string? blockId, IEnumerable<WidgetDef>? widgets)
	{
		string? widgetId = LayoutEngine.WidgetIdOf(blockId);
		if (widgetId == null || widgets == null)
		{
			return "";
		}
		foreach (WidgetDef widget in widgets)
		{
			if (widget != null && string.Equals(widget.Id, widgetId, StringComparison.Ordinal))
			{
				return widget.Type ?? "";
			}
		}
		return "";
	}

	/// <summary>本页已经放了哪些组件类型(隐藏的也算)。</summary>
	public static HashSet<string> TypesOnPage(PageLayoutV2? page, IEnumerable<WidgetDef>? widgets)
	{
		HashSet<string> types = new HashSet<string>(StringComparer.Ordinal);
		if (page?.Blocks == null)
		{
			return types;
		}
		List<WidgetDef> list = (widgets != null) ? new List<WidgetDef>(widgets) : new List<WidgetDef>();
		foreach (BlockLayout block in page.Blocks)
		{
			string type = TypeOfBlock(block?.Id, list);
			if (type.Length > 0)
			{
				types.Add(type);
			}
		}
		return types;
	}

	/// <summary>「已隐藏的内容」:按页面顺序列出被隐藏的块。</summary>
	public static List<LayoutAddItem> Hidden(IEnumerable<string>? order, ICollection<string>? hidden, Func<string, string>? titleOf, IEnumerable<WidgetDef>? widgets = null)
	{
		List<LayoutAddItem> list = new List<LayoutAddItem>();
		if (order == null || hidden == null)
		{
			return list;
		}
		List<WidgetDef> defs = (widgets != null) ? new List<WidgetDef>(widgets) : new List<WidgetDef>();
		foreach (string id in order)
		{
			if (string.IsNullOrEmpty(id) || !hidden.Contains(id))
			{
				continue;
			}
			string type = TypeOfBlock(id, defs);
			string title = ((titleOf != null) ? titleOf(id) : null) ?? "";
			if (title.Length == 0 || string.Equals(title, id, StringComparison.Ordinal))
			{
				title = LayoutBlockTitles.Resolve(id, null, null, null);
			}
			list.Add(new LayoutAddItem
			{
				Kind = LayoutAddKind.Hidden,
				Key = id,
				Type = type,
				Title = title,
				Description = I18n.L("放回它原来的位置"),
				Glyph = (type.Length > 0) ? GlyphOf(type) : Ico.List
			});
		}
		return list;
	}

	/// <summary>「组件」:可新建的类型(网页 / 按钮只在 <paramref name="allowDanger"/> 时出现)。</summary>
	public static List<LayoutAddItem> Types(PageLayoutV2? page, IEnumerable<WidgetDef>? widgets, bool allowDanger)
	{
		HashSet<string> present = TypesOnPage(page, widgets);
		List<LayoutAddItem> list = new List<LayoutAddItem>();
		foreach (string type in TypeOrder)
		{
			if (LayoutSchema.IsDangerousWidgetType(type) && !allowDanger)
			{
				continue;
			}
			list.Add(new LayoutAddItem
			{
				Kind = LayoutAddKind.NewWidget,
				Key = type,
				Type = type,
				Title = LayoutBlockTitles.TypeLabel(type),
				Description = DescriptionOf(type),
				Glyph = GlyphOf(type),
				Added = !AllowsMultiple(type) && present.Contains(type),
				NeedsInput = NeedsInput(type)
			});
		}
		return list;
	}

	/// <summary>「已创建、还没放到本页的组件」(网页 / 按钮同样受全局开关控制)。</summary>
	public static List<LayoutAddItem> Existing(PageLayoutV2? page, IEnumerable<WidgetDef>? widgets, bool allowDanger)
	{
		List<LayoutAddItem> list = new List<LayoutAddItem>();
		if (widgets == null)
		{
			return list;
		}
		List<WidgetDef> defs = new List<WidgetDef>(widgets);
		HashSet<string> onPage = new HashSet<string>(StringComparer.Ordinal);
		if (page?.Blocks != null)
		{
			foreach (BlockLayout block in page.Blocks)
			{
				if (block != null && !string.IsNullOrEmpty(block.Id))
				{
					onPage.Add(block.Id);
				}
			}
		}
		HashSet<string> present = TypesOnPage(page, defs);
		foreach (WidgetDef widget in defs)
		{
			if (widget == null || string.IsNullOrEmpty(widget.Id) || onPage.Contains(LayoutEngine.CustomPrefix + widget.Id))
			{
				continue;
			}
			if (LayoutSchema.IsDangerousWidgetType(widget.Type) && !allowDanger)
			{
				continue;
			}
			list.Add(new LayoutAddItem
			{
				Kind = LayoutAddKind.ExistingWidget,
				Key = widget.Id,
				Type = widget.Type ?? "",
				Title = LayoutBlockTitles.WidgetTitle(widget),
				Description = LayoutBlockTitles.TypeLabel(widget.Type),
				Glyph = GlyphOf(widget.Type),
				Added = !AllowsMultiple(widget.Type) && present.Contains(widget.Type ?? "")
			});
		}
		return list;
	}
}
