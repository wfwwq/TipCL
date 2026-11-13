using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C3c:Windows 版 <c>Pages\LaunchPage.Popups.cs</c>(9479 字节)的移植 —— 启动页的三个浮层
/// (配置 / 实例切换 / 公告)+ 两个 dev 截图用的挂点。
/// Windows 那边就是从 <c>LaunchPage.cs</c> 机械提取的 partial(D-24),mac 侧保持同样的拆法。
///
/// Windows → Avalonia 的差异(只有下面三处,其余逐行照抄):
/// <list type="number">
/// <item><c>Popup.StaysOpen = false</c> → <c>IsLightDismissEnabled = true</c>:
///   Avalonia 的 <c>Popup</c> 没有 <c>StaysOpen</c>,"点外面就关"对应的是这个属性(WPF 的语义就是
///   <c>StaysOpen=false</c>);</item>
/// <item><c>Popup.AllowsTransparency = true</c> → 去掉:Avalonia 的浮层本来就带透明层,没有这个属性;</item>
/// <item><c>FrameworkElement.MaxHeightProperty</c> → <c>Layoutable.MaxHeightProperty</c>,
///   <c>Cursors.Arrow</c> → <c>new Cursor(StandardCursorType.Arrow)</c>,
///   <c>MouseEnter</c> / <c>MouseLeave</c> / <c>MouseLeftButtonUp</c> →
///   <c>PointerEntered</c> / <c>PointerExited</c> / <c>PointerReleased</c>(与主文件同一套换法)。</item>
/// </list>
/// 浮层的定位口径一字未改:配置与实例切换浮层挂在「配置钮 / 大标题行」下方(<c>PlacementMode.Bottom</c>),
/// 公告浮层挂在新闻卡上方(<c>PlacementMode.Top</c>),总高都按宿主窗口的 86% 实时缩放。
/// </summary>
public sealed partial class LaunchPage
{
	private static Border PopShell(Control content, double width, double maxHeight, Thickness margin)
	{
		Border b = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(12.0),
			Width = width,
			MaxHeight = maxHeight,
			Margin = margin,
			Child = content,
		};
		b.Dyn(Border.BackgroundProperty, "B.Paper");
		b.Dyn(Border.BorderBrushProperty, "B.Line");
		Motion.Shadow(b, 22.0, 0.16, 4.0);
		return b;
	}

	private static Popup MakePopup(Control target, PlacementMode mode, Border shell)
	{
		// 弹层总高不超出窗口(随宿主窗口实时缩放),取代固定 MaxHeight——极矮窗口下按钮不再被裁。
		// 【机械改动点】WPF 绑的是 PageBase.Win.ActualHeight;Avalonia 没有 ActualHeight,
		// 用 Visual.Bounds.Height(口径与 C1c 的 SmoothScroll.BindWindowMaxHeight 完全一致)。
		Binding h = new Binding("Bounds.Height")
		{
			Source = PageBase.Win,
			Converter = new Ui.WindowPctConverter(0.86),
		};
		// WPF 是 shell.SetBinding(FrameworkElement.MaxHeightProperty, h);
		// Avalonia 的等价物是 AvaloniaObject.Bind(Layoutable.MaxHeightProperty, binding)。
		shell.Bind(Layoutable.MaxHeightProperty, h);
		return new Popup
		{
			PlacementTarget = target,
			Placement = mode,
			// WPF:StaysOpen = false(点外面就关);Avalonia 没有这个属性,对应 IsLightDismissEnabled。
			IsLightDismissEnabled = true,
			Child = shell,
		};
	}

	// ————— 配置浮层 —————

	private void OpenConfigPopup()
	{
		if (_configPopup == null)
		{
			TextBlock head = Ui.Txt(I18n.L("启动配置"), 15.0, bold: true);
			Button close = Ui.IconBtn("\ue711", I18n.L("关闭"), delegate
			{
				if (_configPopup != null)
				{
					_configPopup.IsOpen = false;
				}
			});
			Grid g = Ui.G(null, "*,Auto");
			g.Add(head.VCenter(), 0);
			g.Add(close, 0, 1);
			_configPopup = MakePopup(_configPill ?? (Control?)_titleRow ?? this, PlacementMode.Bottom,
				PopShell(Ui.V(10.0, g, _configBody ?? Ui.Empty("\ue713", I18n.L("配置加载中"))), 360.0, 440.0, new Thickness(0.0, 6.0, 14.0, 14.0)));
		}
		_configPopup.IsOpen = true;
	}

	// ————— 实例切换浮层 —————

	private void OpenSwitchPopup()
	{
		if (_switchPopup == null)
		{
			_switchSearch = Ui.Input(I18n.L("搜索实例..."));
			_switchSearch.Height = 30.0;
			_switchSearch.Padding = new Thickness(6.0, 5.0, 6.0, 5.0);
			_switchSearch.TextChanged += delegate
			{
				RenderSwitchRows();
			};
			Grid search = Ui.G(null, "Auto,*");
			search.Add(Ui.Glyph("\ue721", 12.0, "B.InkFaint").M(2.0, 0.0, 6.0, 0.0).VCenter(), 0);
			search.Add(_switchSearch, 0, 1);
			_switchList = new StackPanel();
			SmoothScroll listScroll = Ui.Scroll(_switchList);
			// MaxHeight 由 MakePopup 统一绑窗口百分比,不再写死
			Button openLib = Ui.Btn(I18n.L("打开实例库"), BtnKind.Ghost, delegate
			{
				_switchPopup!.IsOpen = false;
				PageBase.Win?.Navigate("library");
			}, "\ue80a");
			openLib.FontSize = 12.0;
			openLib.HorizontalAlignment = HorizontalAlignment.Left;
			_switchPopup = MakePopup((Control?)_titleRow ?? this, PlacementMode.Bottom,
				PopShell(Ui.V(8.0, search, Ui.Sep(), listScroll, Ui.Sep(), openLib), 400.0, 430.0, new Thickness(0.0, 6.0, 14.0, 14.0)));
		}
		_switchSearch.Text = "";
		RenderSwitchRows();
		_switchPopup.IsOpen = true;
	}

	private void RenderSwitchRows()
	{
		if (_switchList == null)
		{
			return;
		}
		_switchList.Children.Clear();
		string q = (_switchSearch?.Text?.Trim() ?? "");
		string current = _inst.Str();
		string curVer = _ver.Str();
		List<string> vers = _ver.Items.Cast<string>().ToList();
		int shown = 0;
		foreach (InstanceInfo i in _instances)
		{
			bool isCurrent = (i.Name == current);
			bool nameHit = (q.Length == 0 || i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
			// 当前实例行下方展开版本子列表(数据 = ReloadVersionsAsync 灌好的 _ver.Items,不另调后端)
			List<string> subs = new List<string>();
			if (isCurrent && vers.Count > 0)
			{
				foreach (string v in vers)
				{
					if (nameHit || q.Length == 0 || v.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						subs.Add(v);
					}
				}
			}
			if (!nameHit && subs.Count == 0)
			{
				continue;
			}
			shown++;
			SPanel subParts = Ui.V(1.0, Ui.Txt(i.Name, 13.5, bold: isCurrent, "B.Ink").Trim(), InstanceSub(i, 11.0, "B.InkFaint"));
			Control right = (isCurrent ? CurrentMark() : ManageLink(i.Name));
			right.VerticalAlignment = VerticalAlignment.Center;
			Grid row = Ui.G(null, "*,Auto");
			row.Add(subParts, 0);
			row.Add(right.M(8.0, 0.0, 0.0, 0.0), 0, 1);
			Border wrap = new Border
			{
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(8.0, 6.0, 8.0, 7.0),
				Cursor = new Cursor(isCurrent ? StandardCursorType.Arrow : StandardCursorType.Hand),
				Child = row,
			};
			string instName = i.Name;
			wrap.PointerEntered += delegate
			{
				wrap.Dyn(Border.BackgroundProperty, "B.Hover");
			};
			wrap.PointerExited += delegate
			{
				wrap.Background = Brushes.Transparent;
			};
			if (!isCurrent)
			{
				wrap.PointerReleased += delegate
				{
					PickInstanceFromSwitch(instName);
				};
			}
			_switchList.Children.Add(wrap);
			if (!isCurrent)
			{
				continue;
			}
			if (vers.Count == 0)
			{
				_switchList.Children.Add(Ui.Muted(I18n.L("这个实例还没有版本,先去「下载」装一个")).M(24.0, 4.0, 4.0, 8.0));
				continue;
			}
			foreach (string v in subs)
			{
				_switchList.Children.Add(VerRow(v, v == curVer));
			}
		}
		if (shown == 0)
		{
			_switchList.Children.Add(Ui.Muted(I18n.L("没有匹配的实例")).M(4.0, 6.0, 4.0, 6.0));
		}
	}

	// 实例切换浮层里的版本子行:行高 46,选中 = 主题色 + 勾;点击即选为启动版本
	private Border VerRow(string v, bool sel)
	{
		Grid g = Ui.G(null, "*,Auto");
		g.Add(Ui.Txt(v, 13.0, bold: sel, sel ? "B.AccentText" : "B.Ink").Trim().VCenter(), 0);
		if (sel)
		{
			g.Add(Ui.Glyph(Ico.Check, 13.0, "B.Accent").VCenter(), 0, 1);
		}
		Border row = new Border
		{
			MinHeight = 46.0,
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(26.0, 4.0, 12.0, 4.0),
			Background = Brushes.Transparent,
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = g,
		};
		row.PointerEntered += delegate
		{
			row.Dyn(Border.BackgroundProperty, "B.RowHover");
		};
		row.PointerExited += delegate
		{
			row.Background = Brushes.Transparent;
		};
		row.PointerReleased += delegate
		{
			PickVersionFromSwitch(v);
		};
		return row;
	}

	/// <summary>
	/// 【Windows 源】<c>LaunchPage.Popups.cs:175-185</c>:实例切换浮层里点某一行 = 关浮层 + 切实例。
	/// 抽成成员是为了能被告知式地驱动(不构造 Pointer 事件,同 D-115)。
	/// </summary>
	internal void PickInstanceFromSwitch(string instName)
	{
		if (_switchPopup != null)
		{
			_switchPopup.IsOpen = false;
		}
		if (_inst.Str() != instName)
		{
			_inst.SelectedItem = instName;
		}
	}

	/// <summary>【Windows 源】<c>LaunchPage.Popups.cs:234-244</c>:版本子行点击 = 关浮层 + 选为启动版本。</summary>
	internal void PickVersionFromSwitch(string v)
	{
		if (_switchPopup != null)
		{
			_switchPopup.IsOpen = false;
		}
		if (_ver.Str() != v)
		{
			_ver.SelectedItem = v;
		}
	}

	private static SPanel CurrentMark()
	{
		Ellipse dot = new Ellipse
		{
			Width = 7.0,
			Height = 7.0,
		};
		dot.Dyn(Shape.FillProperty, "B.Accent");
		SPanel p = Ui.H(5.0, dot, Ui.Txt(I18n.L("当前"), 11.5, bold: false, "B.InkMuted"));
		return p;
	}

	private Button ManageLink(string instName)
	{
		Button b = Ui.Btn(I18n.L("管理"), BtnKind.Ghost, delegate
		{
			if (_switchPopup != null)
			{
				_switchPopup.IsOpen = false;
			}
			PageBase.Win?.Navigate("instance");
		});
		b.FontSize = 11.5;
		b.Padding = new Thickness(8.0, 2.0, 8.0, 3.0);
		return b;
	}

	private static TextBlock InstanceSub(InstanceInfo i, double size, string fg)
	{
		List<string> parts = new List<string>();
		if (!string.IsNullOrEmpty(i.Pack))
		{
			parts.Add(i.Pack);
		}
		if (!string.IsNullOrEmpty(i.Mc))
		{
			parts.Add("Minecraft " + i.Mc);
		}
		TextBlock t = Ui.Txt(string.Join(" · ", parts), size, bold: false, fg).Trim();
		return t;
	}

	// ————— 公告浮层 —————

	private void OpenNewsPopup()
	{
		if (_newsPopup == null)
		{
			_newsPopup = MakePopup((Control?)_newsCard ?? this, PlacementMode.Top,
				PopShell(Ui.Scroll(_newsHost), 420.0, 360.0, new Thickness(0.0, 14.0, 14.0, 6.0)));
		}
		_newsPopup.IsOpen = true;
	}

	// dev-only:按名字打开对应浮层(--pop 截图用)
	public void OpenPopupForDevShot(string what)
	{
		switch (what)
		{
		case "config":
			OpenConfigPopup();
			break;
		case "instances":
			OpenSwitchPopup();
			break;
		case "news":
			OpenNewsPopup();
			break;
		}
	}

	// dev-only:把浮层内容摘出来交给 DevShot 挂进窗口渲染。
	// Windows 的理由是"Popup 是独立 HWND,RenderTargetBitmap 拍不到";Avalonia 的浮层在 OverlayLayer 里,
	// 但摘出来照进可视树渲染仍然是最稳的截法,所以这个挂点照搬(返回类型 FrameworkElement → Control)。
	public Control? DetachPopupForShot(string what)
	{
		Popup? p = (what switch
		{
			"config" => _configPopup,
			"instances" => _switchPopup,
			"news" => _newsPopup,
			_ => null,
		});
		if (p?.Child is Border b)
		{
			p.Child = null;
			p.IsOpen = false;
			return b;
		}
		return null;
	}

	// ————— 测试用的只读快照(浮层部分) —————

	/// <summary>三个浮层此刻开没开(顺序:配置 / 实例切换 / 公告)。</summary>
	internal bool ConfigPopupOpenNow => _configPopup?.IsOpen == true;

	internal bool SwitchPopupOpenNow => _switchPopup?.IsOpen == true;

	internal bool NewsPopupOpenNow => _newsPopup?.IsOpen == true;

	/// <summary>实例切换浮层里的行(未打开时为 null)。</summary>
	internal StackPanel? SwitchListNow => _switchList;

	/// <summary>实例切换浮层的搜索框(未打开时为 null)。</summary>
	internal TextBox? SwitchSearchNow => _switchSearch;

	/// <summary>配置浮层里的面板(未打开时为 null)。</summary>
	internal Border? ConfigShellNow => _configPopup?.Child as Border;

	/// <summary>公告浮层里的面板(未打开时为 null)。</summary>
	internal Border? NewsShellNow => _newsPopup?.Child as Border;
}
