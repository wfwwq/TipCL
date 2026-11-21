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
/// v3「设置 → 布局 → 高级 → 布局文件 → 在编辑器中打开…」:整套布局的 JSON 在**单独的窗口**里编辑,
/// 不再常驻在设置页里。校验 / 应用仍然只走 <see cref="LayoutJsonEditor"/>(硬错误不落盘、越界只警告),
/// 落盘唯一入口仍是 LayoutStoreV2 —— 本窗口自己不碰文件。
/// 进程内只开一扇:再点「在编辑器中打开…」只是把已开的窗口提到前面(草稿不丢)。
/// </summary>
internal sealed class LayoutJsonWindow : Window
{
	private static LayoutJsonWindow? _open;

	private readonly TextBox _box;

	private readonly SPanel _issues = Ui.V(4.0);

	private readonly TextBlock _status = Ui.Small("");

	private readonly Action _storeChanged;

	private Action? _applied;

	private bool _loading;

	private bool _dirty;

	// 自己刚应用过:随后那一次 LayoutChanged 不算「别处改了布局」
	private DateTime _selfSavedAt = DateTime.MinValue;

	/// <summary>打开(或提到前面)布局 JSON 编辑窗口;<paramref name="applied"/> 在成功应用后回调(设置页据此重建)。</summary>
	public static void ShowFor(Action? applied)
	{
		try
		{
			LayoutJsonWindow? open = _open;
			if (open != null)
			{
				open._applied = applied ?? open._applied;
				if (open.WindowState == WindowState.Minimized)
				{
					open.WindowState = WindowState.Normal;
				}
				open.Activate();
				return;
			}
			LayoutJsonWindow window = new LayoutJsonWindow(applied);
			_open = window;
			window.Show();
		}
		catch (Exception ex)
		{
			_open = null;
			AppServices.Toast(I18n.L("打不开布局编辑器"), ex.Message, ToastKind.Error);
		}
	}

	private LayoutJsonWindow(Action? applied)
	{
		_applied = applied;
		_storeChanged = OnStoreChanged;
		base.Title = I18n.L("编辑布局文件");
		base.Width = 720.0;
		base.Height = 640.0;
		base.MinWidth = 480.0;
		base.MinHeight = 400.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.CanResize;
		base.UseLayoutRounding = true;
		base.ShowInTaskbar = true;
		try
		{
			Window? owner = AppServices.Window;
			if (owner != null && owner.IsLoaded && !ReferenceEquals(owner, this))
			{
				base.Owner = owner;
			}
			else
			{
				base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
			}
		}
		catch
		{
			base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		}
		SetResourceReference(Control.ForegroundProperty, "B.Ink");
		SetResourceReference(Control.BackgroundProperty, "B.Canvas");
		SetResourceReference(Control.BorderBrushProperty, "B.Line");
		base.BorderThickness = new Thickness(1.0);
		WindowChrome.SetWindowChrome(this, new WindowChrome
		{
			CaptionHeight = 40.0,
			ResizeBorderThickness = new Thickness(6.0),
			GlassFrameThickness = new Thickness(0.0),
			CornerRadius = new CornerRadius(0.0),
			UseAeroCaptionButtons = false
		});

		// 标题栏:标题 + 关闭
		Button close = new Button
		{
			Style = Ui.S("Btn.CaptionClose"),
			Content = "\ue8bb"
		};
		close.Click += delegate
		{
			Close();
		};
		WindowChrome.SetIsHitTestVisibleInChrome(close, hitTestVisible: true);
		Grid caption = Ui.G(null, "*,Auto")
			.Add(Ui.Txt(base.Title, 13.5, bold: true, "B.Ink").M(14.0, 0.0, 0.0, 0.0).VCenter(), 0)
			.Add(close, 0, 1);
		Border captionBar = new Border
		{
			Height = 40.0,
			Child = caption,
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0)
		};
		captionBar.SetResourceReference(Border.BackgroundProperty, "B.Chrome");
		captionBar.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");

		// 编辑框:Input.Log 样式默认只读,这里必须显式打开编辑
		_box = Ui.LogBox();
		_box.IsReadOnly = false;
		_box.AcceptsReturn = true;
		_box.AcceptsTab = true;
		_box.FontSize = 12.0;
		_box.SetResourceReference(Control.ForegroundProperty, "B.Ink");
		_box.TextChanged += delegate
		{
			if (_loading)
			{
				return;
			}
			_dirty = true;
			SetStatus(I18n.L("有还没应用的修改"), "B.InkMuted");
		};

		TextBlock hint = Ui.Small(I18n.L("这里是整套布局：页签、各页面的排列、分栏和组件。先「校验」看看有没有问题，再「应用」；有错误时不会保存。")).Wrap();
		Button check = Ui.Btn(I18n.L("校验"), BtnKind.Normal, delegate
		{
			Run(apply: false);
		});
		Button apply = Ui.Btn(I18n.L("应用"), BtnKind.Primary, delegate
		{
			Run(apply: true);
		});
		Button reload = Ui.Btn(I18n.L("重新载入"), BtnKind.Ghost, delegate
		{
			ReloadFromStore(askIfDirty: true);
		});
		reload.ToolTip = I18n.L("丢掉这里的修改，重新读取现在正在用的布局");
		Button done = Ui.Btn(I18n.L("关闭"), BtnKind.Normal, delegate
		{
			Close();
		});
		Grid footer = Ui.G(null, "*,Auto");
		footer.Add(Ui.H(8.0, check, apply, reload), 0);
		footer.Add(done, 0, 1);

		ScrollViewer issuesScroll = new ScrollViewer
		{
			Content = _issues,
			MaxHeight = 150.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};

		Grid body = Ui.G("Auto,*,Auto,Auto,Auto");
		body.Margin = new Thickness(14.0, 12.0, 14.0, 14.0);
		body.Add(hint.M(0.0, 0.0, 0.0, 8.0), 0);
		body.Add(_box, 1);
		body.Add(_status.M(0.0, 8.0, 0.0, 0.0), 2);
		body.Add(issuesScroll.M(0.0, 4.0, 0.0, 0.0), 3);
		body.Add(footer.M(0.0, 10.0, 0.0, 0.0), 4);

		Grid root = Ui.G("Auto,*");
		root.Add(captionBar, 0);
		root.Add(body, 1);
		base.Content = root;

		ReloadFromStore(askIfDirty: false);

		base.PreviewKeyDown += OnPreviewKeyDown;
		base.Loaded += delegate
		{
			LayoutStoreV2.LayoutChanged -= _storeChanged;
			LayoutStoreV2.LayoutChanged += _storeChanged;
			_box.Focus();
		};
		base.Closing += OnClosingWindow;
		base.Closed += delegate
		{
			LayoutStoreV2.LayoutChanged -= _storeChanged;
			if (ReferenceEquals(_open, this))
			{
				_open = null;
			}
		};
	}

	// Ctrl+S = 应用;Esc = 关闭
	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.None)
		{
			e.Handled = true;
			Run(apply: true);
		}
		else if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
	}

	private void OnClosingWindow(object? sender, System.ComponentModel.CancelEventArgs e)
	{
		if (!_dirty)
		{
			return;
		}
		MessageBoxResult answer = MessageBox.Show(this, I18n.L("修改还没有应用，关闭后会丢掉。确定要关闭吗？"), base.Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (answer != MessageBoxResult.Yes)
		{
			e.Cancel = true;
		}
	}

	private void ReloadFromStore(bool askIfDirty)
	{
		if (askIfDirty && _dirty)
		{
			MessageBoxResult answer = MessageBox.Show(this, I18n.L("这里的修改还没有应用，重新载入会丢掉它们。继续吗？"), base.Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (answer != MessageBoxResult.Yes)
			{
				return;
			}
		}
		_loading = true;
		try
		{
			_box.Text = LayoutJsonEditor.Text(LayoutStoreV2.Load());
		}
		catch (Exception ex)
		{
			_box.Text = "{}";
			SetStatus(ex.Message, "B.Danger");
		}
		finally
		{
			_loading = false;
		}
		_dirty = false;
		RenderIssues(new List<LayoutJsonIssue>());
		SetStatus(I18n.L("已载入现在正在用的布局"), "B.InkMuted");
	}

	private void Run(bool apply)
	{
		string text = _box.Text ?? "";
		LayoutV2? doc;
		List<LayoutJsonIssue> issues;
		if (apply)
		{
			_selfSavedAt = DateTime.UtcNow;
		}
		try
		{
			issues = (apply ? LayoutJsonEditor.Apply(text, out doc) : LayoutJsonEditor.Inspect(text, out doc));
		}
		catch (Exception ex)
		{
			issues = new List<LayoutJsonIssue>
			{
				new LayoutJsonIssue
				{
					Error = true,
					Message = ex.Message
				}
			};
		}
		RenderIssues(issues);
		bool failed = issues.Any((LayoutJsonIssue i) => i.Error);
		int warnings = issues.Count((LayoutJsonIssue i) => !i.Error);
		if (failed)
		{
			SetStatus(apply ? I18n.L("有错误，没有保存。看下面标红的字段") : I18n.L("有错误：看下面标红的字段"), "B.Danger");
			return;
		}
		if (!apply)
		{
			SetStatus((warnings > 0) ? I18n.L("校验通过，有 {0} 处取值超出范围，应用时会自动修正或忽略", warnings) : I18n.L("校验通过"), "B.Ok");
			return;
		}
		_selfSavedAt = DateTime.UtcNow;
		_dirty = false;
		SetStatus((warnings > 0) ? I18n.L("已应用，有 {0} 处取值超出范围，已自动修正或忽略", warnings) : I18n.L("已应用"), "B.Ok");
		AppServices.Toast(I18n.L("布局已保存"), (warnings > 0) ? I18n.L("有 {0} 处取值超出范围，已自动修正或忽略", warnings) : "", ToastKind.Success);
		try
		{
			_applied?.Invoke();
		}
		catch
		{
			// 设置页已经不在了:不影响本窗口
		}
	}

	// 别处(编辑态、方案切换、AI)改了布局:不覆盖用户正在编辑的草稿,只提示
	private void OnStoreChanged()
	{
		try
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if ((DateTime.UtcNow - _selfSavedAt).TotalSeconds < 2.0)
				{
					return;
				}
				if (!_dirty)
				{
					ReloadFromStore(askIfDirty: false);
					SetStatus(I18n.L("布局在别处改过了，已自动载入最新内容"), "B.InkMuted");
				}
				else
				{
					SetStatus(I18n.L("布局在别处改过了。点「重新载入」可以拿到最新内容（会丢掉这里的修改）"), "B.Warn");
				}
			});
		}
		catch
		{
			// 窗口正在关闭
		}
	}

	private void SetStatus(string text, string brushKey)
	{
		_status.Text = text;
		_status.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
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
}
