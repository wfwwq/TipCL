using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 自建区块的界面层:按类型分派到具体渲染器,并给"查不到 / 已禁用 / 动作不合法"三种情况统一占位。
// 铁律:占位只影响显示,**绝不动布局文档**(用户数据永远保留),删除入口在设置页(L5)。
internal static class WidgetUI
{
	/// <summary>按类型分派渲染。</summary>
	public static UIElement Build(WidgetDef widget, string pageId)
	{
		if (widget == null)
		{
			return Placeholder(I18n.L("自建区块不可用"), I18n.L("布局文档里没有这个区块的定义。"));
		}
		switch (widget.Type)
		{
		case LayoutSchema.WidgetNote:
			return BasicWidgets.Note(widget);
		case LayoutSchema.WidgetClock:
			return BasicWidgets.Clock(widget);
		case LayoutSchema.WidgetDivider:
			return BasicWidgets.Divider(widget);
		case LayoutSchema.WidgetShortcut:
			return ShortcutWidget.Build(widget);
		case LayoutSchema.WidgetPlaytime:
			return PlaytimeWidget.Build(widget);
		case LayoutSchema.WidgetWeb:
		case LayoutSchema.WidgetButton:
			return DangerousWidgets.Build(widget);
		default:
			return Placeholder(I18n.L("自建区块不可用"), I18n.L("未知的区块类型:{0}", widget.Type ?? ""));
		}
	}

	/// <summary>布局里引用了 custom:&lt;id&gt; 但文档里没有这个定义(被外部工具删了):占位,不删引用。</summary>
	public static UIElement MissingWidget(string blockId)
	{
		return Placeholder(I18n.L("自建区块不可用"), I18n.L("布局文档里找不到 {0} 的定义;可以重新添加同名区块。", blockId ?? ""));
	}

	/// <summary>危险类型被 Q5 默认关闭时的占位。</summary>
	public static UIElement DisabledDangerous(WidgetDef widget)
	{
		return Placeholder(I18n.L("已禁用的自建区块"), I18n.L("web / button 属于危险类型,默认关闭;要在设置里显式开启(填 settings.enabled = true)才会渲染。"));
	}

	/// <summary>统一的占位样式(和其它"不可用"提示一致)。</summary>
	public static UIElement Placeholder(string title, string hint)
	{
		Border card = Ui.Card(Ui.V(4.0, Ui.Txt(title, 13.0, bold: true), Ui.Small(hint).Wrap()), 10.0);
		card.HorizontalAlignment = HorizontalAlignment.Stretch;
		return card;
	}

	/// <summary>区块标题(有用户标题就用它,否则用类型的中文名)。</summary>
	public static string TitleOf(WidgetDef widget)
	{
		if (widget != null && !string.IsNullOrEmpty(widget.Title))
		{
			return widget.Title;
		}
		return TypeLabel((widget != null) ? widget.Type : "");
	}

	/// <summary>类型的中文名。</summary>
	public static string TypeLabel(string type)
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
}

/// <summary>
/// 编辑态感知外壳:向上找到最近的 ContentControl(L1 的 LayoutCard._body),跟它的 IsHitTestVisible 判断编辑态。
/// WebView(HwndHost)会盖住拖拽手柄与操作条,所以编辑态必须临时藏起来 ——
/// 做法照抄既有的 PluginDashCards.CardShell(PluginDashCards.cs:122-158)。
/// </summary>
internal abstract class EditAwareShell : ContentControl
{
	private ContentControl? _owner;

	// 内容是不是"必须显式 Dispose"的东西(WebView2):是的话离开可视树后要主动销毁,
	// 否则进程退出时 HwndHost 的终结器会在 COM 已经拆掉的线程上 Dispose → 未处理异常
	// (实测:带 enabled 的 web 区块跑 --shot,退出码 -532462766,栈里是 WebView2Base.Uninitialize)
	private readonly bool _disposeOnDetach;

	private static readonly List<WeakReference> _live = new List<WeakReference>();

	private static bool _shutdownHooked;

	protected EditAwareShell(bool disposeOnDetach = false)
	{
		_disposeOnDetach = disposeOnDetach;
		base.Focusable = false;
		base.HorizontalContentAlignment = HorizontalAlignment.Stretch;
		base.VerticalContentAlignment = VerticalAlignment.Stretch;
		if (disposeOnDetach)
		{
			HookShutdown();
			lock (_live)
			{
				_live.Add(new WeakReference(this));
			}
		}
		base.Loaded += delegate
		{
			HookOwner();
		};
		base.Unloaded += delegate
		{
			if (_disposeOnDetach)
			{
				// 不能立刻销毁:L1 编辑态每次 Render 都会把卡片摘下来再挂回去;
				// 用一次低优先级回调确认它真的离开了可视树再销毁(顺手让 WebView 及时释放浏览器进程)
				base.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate
				{
					if (!base.IsLoaded)
					{
						DisposeContent();
					}
				}));
			}
		};
	}

	// 进程正常关闭时主动 Dispose 掉 WebView2:否则 HwndHost 终结器会在 COM 已经拆掉的线程上
	// Dispose → 未处理异常、进程退出码 -532462766(实测)。
	// 注意:DevShot 用 Environment.Exit 结束,不走 Application.Exit,所以开发截图那条路径仍会看到这个栈。
	private static void HookShutdown()
	{
		if (_shutdownHooked)
		{
			return;
		}
		_shutdownHooked = true;
		try
		{
			if (Application.Current != null)
			{
				Application.Current.Exit += delegate
				{
					List<EditAwareShell> shells = new List<EditAwareShell>();
					lock (_live)
					{
						foreach (WeakReference item in _live)
						{
							if (item.Target is EditAwareShell shell)
							{
								shells.Add(shell);
							}
						}
					}
					foreach (EditAwareShell shell in shells)
					{
						try
						{
							shell.DisposeContent();
						}
						catch (Exception)
						{
						}
					}
				};
			}
		}
		catch (Exception)
		{
		}
	}

	/// <summary>当前是否在编辑态(宿主把内容设成了不可点)。</summary>
	protected bool Editing
	{
		get
		{
			return _owner != null && !_owner.IsHitTestVisible;
		}
	}

	/// <summary>销毁内容(默认把 IDisposable 内容 Dispose 掉);子类可重写以便重建。</summary>
	protected virtual void DisposeContent()
	{
		(base.Content as IDisposable)?.Dispose();
		base.Content = null;
	}

	private void HookOwner()
	{
		DependencyObject d = VisualTreeHelper.GetParent(this);
		while (d != null && !(d is ContentControl))
		{
			d = VisualTreeHelper.GetParent(d);
		}
		ContentControl? owner = d as ContentControl;
		if (owner == _owner)
		{
			return;
		}
		if (_owner != null)
		{
			_owner.IsHitTestVisibleChanged -= OnOwnerHit;
		}
		_owner = owner;
		if (_owner != null)
		{
			_owner.IsHitTestVisibleChanged += OnOwnerHit;
		}
		OnEditChanged();
	}

	private void OnOwnerHit(object sender, DependencyPropertyChangedEventArgs e)
	{
		OnEditChanged();
	}

	/// <summary>编辑态变化时调用(子类在这里藏 / 显 WebView)。</summary>
	protected virtual void OnEditChanged()
	{
	}
}
