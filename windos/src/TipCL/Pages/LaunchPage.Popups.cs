using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 启动页浮层：配置/实例切换/新闻 Popup 与 DevShot 摘取（WP10 拆分）
// 从 LaunchPage.cs 机械提取（partial class，行为不变；见 DECISIONS D-24）。
public sealed partial class LaunchPage
{
	private static Border PopShell(UIElement content, double width, double maxHeight, Thickness margin)
	{
		Border b = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(12.0),
			Width = width,
			MaxHeight = maxHeight,
			Margin = margin,
			Child = content
		};
		b.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		b.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		Motion.Shadow(b, 22.0, 0.16, 4.0);
		return b;
	}

	private static Popup MakePopup(FrameworkElement target, PlacementMode mode, Border shell)
	{
		// 弹层总高不超出窗口（随宿主窗口实时缩放），取代固定 MaxHeight——极矮窗口下按钮不再被裁
		Binding h = new Binding("ActualHeight")
		{
			Source = PageBase.Win,
			Converter = new Ui.WindowPctConverter(0.86)
		};
		shell.SetBinding(FrameworkElement.MaxHeightProperty, h);
		return new Popup
		{
			PlacementTarget = target,
			Placement = mode,
			StaysOpen = false,
			AllowsTransparency = true,
			Child = shell
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
			_configPopup = MakePopup(_configPill ?? (FrameworkElement?)_titleRow ?? this, PlacementMode.Bottom,
				PopShell(Ui.V(10.0, g, _configBody ?? Ui.Empty("\ue713", I18n.L("配置加载中"))), 360.0, 440.0, new Thickness(0.0, 6.0, 14.0, 14.0)));
		}
		_configPopup.IsOpen = true;
	}

	// ————— 实例切换浮层 —————

	private void OpenSwitchPopup()
	{
		if (_switchPopup == null)
		{
			_switchSearch = Ui.Input(I18n.L("搜索实例…"));
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
			// MaxHeight 由 MakePopup 统一绑窗口百分比，不再写死
			Button openLib = Ui.Btn(I18n.L("打开实例库"), BtnKind.Ghost, delegate
			{
				_switchPopup.IsOpen = false;
				PageBase.Win?.Navigate("library");
			}, "\ue80a");
			openLib.FontSize = 12.0;
			openLib.HorizontalAlignment = HorizontalAlignment.Left;
			_switchPopup = MakePopup((FrameworkElement?)_titleRow ?? this, PlacementMode.Bottom,
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
			// 当前实例行下方展开版本子列表（数据 = ReloadVersionsAsync 灌好的 _ver.Items，不另调后端）
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
			FrameworkElement right = (isCurrent ? CurrentMark() : ManageLink(i.Name));
			right.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			Grid row = Ui.G(null, "*,Auto");
			row.Add(subParts, 0);
			row.Add(right.M(8.0, 0.0, 0.0, 0.0), 0, 1);
			Border wrap = new Border
			{
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(8.0, 6.0, 8.0, 7.0),
				Cursor = (isCurrent ? Cursors.Arrow : Cursors.Hand),
				Child = row
			};
			string instName = i.Name;
			wrap.MouseEnter += delegate
			{
				wrap.SetResourceReference(Border.BackgroundProperty, "B.Hover");
			};
			wrap.MouseLeave += delegate
			{
				wrap.Background = Brushes.Transparent;
			};
			if (!isCurrent)
			{
				wrap.MouseLeftButtonUp += delegate
				{
					if (_switchPopup != null)
					{
						_switchPopup.IsOpen = false;
					}
					if (_inst.Str() != instName)
					{
						_inst.SelectedItem = instName;
					}
				};
			}
			_switchList.Children.Add(wrap);
			if (!isCurrent)
			{
				continue;
			}
			if (vers.Count == 0)
			{
				_switchList.Children.Add(Ui.Muted(I18n.L("这个实例还没有版本，先去「下载」装一个")).M(24.0, 4.0, 4.0, 8.0));
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

	// 实例切换浮层里的版本子行：行高 46，选中 = 主题色 + 勾；点击即选为启动版本
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
			Cursor = Cursors.Hand,
			Child = g
		};
		row.MouseEnter += delegate
		{
			row.SetResourceReference(Border.BackgroundProperty, "B.RowHover");
		};
		row.MouseLeave += delegate
		{
			row.Background = Brushes.Transparent;
		};
		row.MouseLeftButtonUp += delegate
		{
			if (_switchPopup != null)
			{
				_switchPopup.IsOpen = false;
			}
			if (_ver.Str() != v)
			{
				_ver.SelectedItem = v;
			}
		};
		return row;
	}

	private static SPanel CurrentMark()
	{
		Ellipse dot = new Ellipse
		{
			Width = 7.0,
			Height = 7.0
		};
		dot.SetResourceReference(Shape.FillProperty, "B.Accent");
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
			_newsPopup = MakePopup((FrameworkElement?)_newsCard ?? this, PlacementMode.Top,
				PopShell(Ui.Scroll(_newsHost), 420.0, 360.0, new Thickness(0.0, 14.0, 14.0, 6.0)));
		}
		_newsPopup.IsOpen = true;
	}

	// dev-only：按名字打开对应浮层（--pop 截图用）
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

	// dev-only：把浮层内容摘出来交给 DevShot 挂进窗口渲染（Popup 是独立 HWND，RenderTargetBitmap 拍不到）
	public FrameworkElement? DetachPopupForShot(string what)
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
}

