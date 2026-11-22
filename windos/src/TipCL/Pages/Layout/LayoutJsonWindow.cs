using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

/// <summary>
/// 设置 → 布局 → 高级 →「布局文件 · 在编辑器中打开…」的独立窗口(R34 §5.4):承载 <see cref="LayoutJsonEditor"/>
/// 的校验 / 应用 / 刷新,规则一行没改,只是不再常驻在设置页上。
///   - 同一时间只开一个窗口:再点一次只是把它提到前面;
///   - 关窗时没应用的文本留作草稿(进程内),下次打开先恢复 —— 与原来设置页上那个编辑框的行为一致;
///   - 没改动时跟着布局文档刷新(别处改了布局,这里看到的就是最新的);改了一半不会被覆盖;
///   - 文案不出现文件名之类的内部说法。
/// </summary>
internal sealed class LayoutJsonWindow : Window
{
	private static LayoutJsonWindow? _open;

	// 关窗时还没应用的文本(下次打开时恢复)
	private static string? _draft;

	private readonly TextBox _box;

	private readonly SPanel _issues = Ui.V(4.0);

	private readonly TextBlock _status = Ui.Small("");

	private readonly Action _storeChanged;

	private bool _dirty;

	private bool _loading;

	/// <summary>打开(或提到前面)布局编辑窗口。<paramref name="owner"/> 可空:没有主窗口时居中屏幕。</summary>
	public static void ShowFor(Window? owner)
	{
		try
		{
			if (_open != null)
			{
				if (_open.WindowState == WindowState.Minimized)
				{
					_open.WindowState = WindowState.Normal;
				}
				_open.Activate();
				return;
			}
			LayoutJsonWindow window = new LayoutJsonWindow();
			if (owner != null && owner.IsVisible)
			{
				window.Owner = owner;
				window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
			}
			_open = window;
			window.Closed += delegate
			{
				if (ReferenceEquals(_open, window))
				{
					_open = null;
				}
			};
			window.Show();
		}
		catch (Exception ex)
		{
			_open = null;
			AppServices.Window?.Toast(I18n.L("没能打开布局编辑窗口"), ex.Message, ToastKind.Error);
		}
	}

	private LayoutJsonWindow()
	{
		SetResourceReference(Control.ForegroundProperty, "B.Ink");
		base.Title = I18n.L("编辑布局");
		base.Width = 760.0;
		base.Height = 640.0;
		base.MinWidth = 520.0;
		base.MinHeight = 420.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.CanResize;
		base.UseLayoutRounding = true;
		SetResourceReference(Control.BackgroundProperty, "B.Canvas");
		SetResourceReference(Control.BorderBrushProperty, "B.Line");
		base.BorderThickness = new Thickness(1.0);
		WindowChrome chrome = new WindowChrome
		{
			CaptionHeight = 40.0,
			ResizeBorderThickness = new Thickness(6.0),
			GlassFrameThickness = new Thickness(0.0),
			CornerRadius = new CornerRadius(0.0),
			UseAeroCaptionButtons = false
		};
		WindowChrome.SetWindowChrome(this, chrome);
		Button close = new Button
		{
			Style = Ui.S("Btn.CaptionClose"),
			Content = "\ue8bb"
		};
		close.Click += delegate
		{
			Close();
		};
		System.Windows.Automation.AutomationProperties.SetName(close, I18n.L("关闭"));
		WindowChrome.SetIsHitTestVisibleInChrome(close, hitTestVisible: true);
		Grid bar = Ui.G(null, "*,Auto").Add(Ui.Txt(base.Title, 13.5, bold: true, "B.Ink").M(14.0, 0.0, 0.0, 0.0).VCenter(), 0).Add(close, 0, 1);
		Border head = new Border
		{
			Height = 40.0,
			Child = bar
		};
		head.SetResourceReference(Border.BackgroundProperty, "B.Chrome");
		head.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		head.BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0);
		// 编辑框:Input.Log 样式默认只读(那是日志框),这里显式打开编辑
		_box = Ui.LogBox();
		_box.IsReadOnly = false;
		_box.AcceptsReturn = true;
		_box.AcceptsTab = true;
		_box.TextChanged += delegate
		{
			if (!_loading)
			{
				_dirty = true;
			}
		};
		Button check = Ui.Btn(I18n.L("校验"), BtnKind.Normal, delegate
		{
			Inspect(apply: false);
		});
		Button apply = Ui.Btn(I18n.L("应用"), BtnKind.Primary, delegate
		{
			Inspect(apply: true);
		});
		Button reload = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			Reload();
		});
		reload.ToolTip = I18n.L("重新载入当前布局（没应用的修改会丢掉）");
		TextBlock hint = Ui.Small(I18n.L("用 JSON 编辑整套布局。校验通过才会保存，有问题时按字段列出原因；应用后各页面立即按新布局显示。Ctrl+S 应用。")).Wrap();
		ScrollViewer issues = new ScrollViewer
		{
			Content = _issues,
			MaxHeight = 150.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		_status.TextWrapping = TextWrapping.Wrap;
		Grid actions = Ui.G(null, "*,Auto");
		actions.Add(_status.VCenter().M(0.0, 0.0, 12.0, 0.0), 0);
		actions.Add(Ui.H(8.0, check, apply, reload), 0, 1);
		Grid body = Ui.G("Auto,*,Auto,Auto");
		body.Add(hint, 0);
		body.Add(_box.M(0.0, 8.0, 0.0, 8.0), 1);
		body.Add(issues, 2);
		body.Add(actions.M(0.0, 8.0, 0.0, 0.0), 3);
		base.Content = Ui.G("Auto,*").Add(head, 0).Add(Ui.Card(body, 12.0).M(12.0, 10.0, 12.0, 12.0), 1);
		if (!string.IsNullOrEmpty(_draft))
		{
			SetText(_draft!);
			_dirty = true;
			SetStatus(I18n.L("已恢复上次没应用的修改"), "B.InkSoft");
		}
		else
		{
			SetText(LayoutJsonEditor.Text(LayoutStoreV2.Load()));
		}
		base.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
			{
				e.Handled = true;
				Inspect(apply: true);
			}
			else if (e.Key == Key.Escape)
			{
				e.Handled = true;
				Close();
			}
		};
		// 布局在别处变了(编辑态提交 / 应用方案 / AI 改布局):没改动就跟着刷新
		_storeChanged = OnStoreChanged;
		LayoutStoreV2.LayoutChanged += _storeChanged;
		base.Closed += delegate
		{
			LayoutStoreV2.LayoutChanged -= _storeChanged;
			_draft = (_dirty ? (_box.Text ?? "") : null);
		};
		base.Loaded += delegate
		{
			_box.Focus();
		};
	}

	private void OnStoreChanged()
	{
		try
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (!_dirty && base.IsLoaded)
				{
					SetText(LayoutJsonEditor.Text(LayoutStoreV2.Load()));
				}
			});
		}
		catch
		{
			// 窗口正在关闭:不刷新也不能崩
		}
	}

	private void SetText(string text)
	{
		_loading = true;
		try
		{
			_box.Text = text;
		}
		finally
		{
			_loading = false;
		}
	}

	private void Reload()
	{
		SetText(LayoutJsonEditor.Text(LayoutStoreV2.Load()));
		_dirty = false;
		_draft = null;
		RenderIssues(new List<LayoutJsonIssue>());
		SetStatus(I18n.L("已重新载入当前布局"), "B.InkSoft");
	}

	// 校验 / 应用(规则全在 LayoutJsonEditor:有一条硬错误就不落盘)
	private void Inspect(bool apply)
	{
		string text = _box.Text ?? "";
		LayoutV2? doc;
		List<LayoutJsonIssue> issues = (apply ? LayoutJsonEditor.Apply(text, out doc) : LayoutJsonEditor.Inspect(text, out doc));
		RenderIssues(issues);
		if (issues.Any((LayoutJsonIssue i) => i.Error))
		{
			SetStatus(I18n.L("有错误，没有保存：看下面列出的字段"), "B.Danger");
			return;
		}
		int warned = issues.Count;
		if (!apply)
		{
			SetStatus((warned > 0) ? I18n.L("校验通过，有 {0} 处会被自动修正或忽略", warned) : I18n.L("校验通过"), "B.Ok");
			return;
		}
		PageLayoutStore.Reload();
		// 落盘的是规范化之后的文档(越界值已夹取):把它载回来,所见即所存
		SetText(LayoutJsonEditor.Text(LayoutStoreV2.Load()));
		_dirty = false;
		_draft = null;
		SetStatus((warned > 0) ? I18n.L("已保存，有 {0} 处已自动修正或忽略", warned) : I18n.L("已保存"), "B.Ok");
		AppServices.Window?.Toast(I18n.L("布局已保存"), "", ToastKind.Success);
	}

	private void RenderIssues(List<LayoutJsonIssue> issues)
	{
		_issues.Children.Clear();
		foreach (LayoutJsonIssue issue in issues)
		{
			TextBlock line = Ui.Txt((issue.Path.Length > 0) ? (issue.Path + ": " + issue.Message) : issue.Message, 12.0, bold: false, issue.Error ? "B.Danger" : "B.Warn");
			line.TextWrapping = TextWrapping.Wrap;
			_issues.Children.Add(line);
		}
	}

	private void SetStatus(string text, string brush)
	{
		_status.Text = text;
		_status.SetResourceReference(TextBlock.ForegroundProperty, brush);
	}
}
