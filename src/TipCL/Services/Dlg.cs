using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace TipCL.Services;

public static class Dlg
{
	public sealed class Layer
	{
		public required Grid Root { get; init; }

		public required Border Card { get; init; }

		public required Action Close { get; init; }
	}

	private sealed class Scope : IDisposable
	{
		[CompilerGenerated]
		private Action _003Cclose_003EP;

		private int _done;

		public Scope(Action close)
		{
			_003Cclose_003EP = close;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _done, 1) == 0)
			{
				_003Cclose_003EP();
			}
		}
	}

	public static Grid? Host { get; set; }

	public static UIElement? BlurTarget { get; set; }

	public static bool AnyOpen
	{
		get
		{
			Grid host = Host;
			if (host != null)
			{
				UIElementCollection children = host.Children;
				if (children != null)
				{
					return children.Count > 0;
				}
			}
			return false;
		}
	}

	private static Layer Push(UIElement card, Action? onDismiss, bool dismissable = true, bool smokeAutoAnswer = true)
	{
		Grid host = Host ?? throw new InvalidOperationException(I18n.L("对话层未初始化"));
		Border mask = new Border
		{
			Opacity = 0.0
		};
		mask.SetResourceReference(Border.BackgroundProperty, "B.Mask");
		Grid wrap = new Grid();
		wrap.Children.Add(mask);
		Grid holder = new Grid
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(24.0)
		};
		holder.Children.Add(card);
		wrap.Children.Add(holder);
		host.Children.Add(wrap);
		host.Visibility = Visibility.Visible;
		if (BlurTarget != null && host.Children.Count == 1)
		{
			BlurTarget.Effect = new BlurEffect
			{
				Radius = 8.0,
				KernelType = KernelType.Gaussian,
				RenderingBias = RenderingBias.Performance
			};
		}
		Motion.Fade(mask, 1.0, 150.0);
		Motion.PopIn(holder);
		if (dismissable)
		{
			mask.MouseLeftButtonDown += delegate
			{
				onDismiss?.Invoke();
				Close();
			};
		}
		wrap.Focusable = true;
		wrap.KeyDown += delegate(object _, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Escape && dismissable)
			{
				onDismiss?.Invoke();
				Close();
				e.Handled = true;
			}
		};
		if (!Smoke.Active)
		{
			wrap.Loaded += delegate
			{
				wrap.Focus();
			};
		}
		if (Smoke.Active && smokeAutoAnswer)
		{
			Smoke.AutoAnswerDialog(card, delegate
			{
				onDismiss?.Invoke();
				Close();
			});
		}
		return new Layer
		{
			Root = wrap,
			Card = ((card as Border) ?? new Border()),
			Close = Close
		};
		void Close()
		{
			if (host.Children.Contains(wrap))
			{
				Motion.Fade(mask, 0.0, 130.0);
				Motion.PopOut(holder, delegate
				{
					host.Children.Remove(wrap);
					if (host.Children.Count == 0)
					{
						host.Visibility = Visibility.Collapsed;
						if (BlurTarget != null)
						{
							BlurTarget.Effect = null;
						}
					}
				});
			}
		}
	}

	private static Border Shell(string title, UIElement body, UIElement? footer, double width)
	{
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(Ui.H2(title).VCenter(), 0);
		SPanel sPanel = Ui.V(14.0, grid, body);
		if (footer != null)
		{
			sPanel.Children.Add(footer);
		}
		Border border = new Border();
		border.CornerRadius = new CornerRadius(14.0);
		border.Padding = new Thickness(22.0, 18.0, 22.0, 18.0);
		border.BorderThickness = new Thickness(1.0);
		border.MaxWidth = width;
		border.MinWidth = Math.Min(width, 380.0);
		border.Child = sPanel;
		border.SetResourceReference(Border.BackgroundProperty, "B.GlassStrong");
		border.SetResourceReference(Border.BorderBrushProperty, "B.GlassBorder");
		Motion.Shadow(border, 40.0, 0.28, 10.0);
		return border;
	}

	public static Task<bool> Confirm(string title, string body, string? ok = null, string? cancel = null, bool danger = false)
	{
		return Ask(title, Ui.Muted(body).Wrap().MinW(320.0), ok, cancel, danger);
	}

	public static Task<bool> Ask(string title, UIElement body, string? ok = null, string? cancel = null, bool danger = false, double width = 520.0)
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		System.Windows.Controls.Button button = Ui.Btn(ok ?? I18n.L("确定"), danger ? BtnKind.Danger : BtnKind.Primary);
		System.Windows.Controls.Button button2 = Ui.Btn(cancel ?? I18n.L("取消"));
		SPanel footer = Ui.H(8.0, button2, button).Right();
		Border card = Shell(title, body, footer, width);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(result: false);
		});
		button.Click += delegate
		{
			tcs.TrySetResult(result: true);
			layer.Close();
		};
		button2.Click += delegate
		{
			tcs.TrySetResult(result: false);
			layer.Close();
		};
		return tcs.Task;
	}

	public static Task Alert(string title, string body, double width = 520.0)
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		System.Windows.Controls.Button button = Ui.Btn(I18n.L("知道了"), BtnKind.Primary);
		System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox
		{
			Text = body,
			IsReadOnly = true,
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 380.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			FontSize = 13.0
		};
		textBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "B.InkSoft");
		Border card = Shell(title, textBox, Ui.H(8.0, button).Right(), width);
		Layer layer = Push(card, delegate
		{
			tcs.TrySetResult(result: true);
		});
		button.Click += delegate
		{
			tcs.TrySetResult(result: true);
			layer.Close();
		};
		return tcs.Task;
	}

	public static Task<string?> Prompt(string title, string label, string initial = "", string placeholder = "")
	{
		TaskCompletionSource<string?> tcs = new TaskCompletionSource<string>();
		System.Windows.Controls.TextBox input = Ui.Input(placeholder, initial).MinW(340.0);
		SPanel body = Ui.V(6.0, Ui.Muted(label), input);
		System.Windows.Controls.Button button = Ui.Btn(I18n.L("确定"), BtnKind.Primary);
		System.Windows.Controls.Button button2 = Ui.Btn(I18n.L("取消"));
		Border card = Shell(title, body, Ui.H(8.0, button2, button).Right(), 460.0);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(null);
		});
		button.Click += delegate
		{
			Ok();
		};
		button2.Click += delegate
		{
			tcs.TrySetResult(null);
			layer.Close();
		};
		input.KeyDown += delegate(object _, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				Ok();
			}
		};
		input.Loaded += delegate
		{
			input.Focus();
			input.SelectAll();
		};
		return tcs.Task;
		void Ok()
		{
			tcs.TrySetResult(input.Text?.Trim() ?? "");
			layer.Close();
		}
	}

	public static Task<int> Choose(string title, UIElement body, string[] options, double width = 560.0, bool dismissable = true)
	{
		TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
		SPanel sPanel = Ui.H(8.0).Right();
		Border card = Shell(title, body, sPanel, width);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(-1);
		}, dismissable);
		for (int num = 0; num < options.Length; num++)
		{
			int idx = num;
			System.Windows.Controls.Button button = Ui.Btn(options[num], (num != options.Length - 1) ? BtnKind.Normal : BtnKind.Primary);
			button.Click += delegate
			{
				tcs.TrySetResult(idx);
				layer.Close();
			};
			sPanel.Children.Add(button);
		}
		return tcs.Task;
	}

	public static async Task<T?> Form<T>(string title, UIElement body, Func<T> collect, string? ok = null, double width = 560.0) where T : class
	{
		T result = null;
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		System.Windows.Controls.Button button = Ui.Btn(ok ?? I18n.L("保存"), BtnKind.Primary);
		System.Windows.Controls.Button button2 = Ui.Btn(I18n.L("取消"));
		Border card = Shell(title, body, Ui.H(8.0, button2, button).Right(), width);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(result: false);
		});
		button.Click += delegate
		{
			try
			{
				result = collect();
			}
			catch (Exception ex)
			{
				AppServices.Toast(I18n.L("填写有误"), ex.Message, ToastKind.Warning);
				Motion.Shake(card);
				return;
			}
			tcs.TrySetResult(result: true);
			layer.Close();
		};
		button2.Click += delegate
		{
			tcs.TrySetResult(result: false);
			layer.Close();
		};
		await tcs.Task;
		return result;
	}

	public static Layer Panel(string title, UIElement body, double width = 720.0, Action? onClose = null)
	{
		System.Windows.Controls.Button button = Ui.IconBtn("\ue711", I18n.L("关闭"));
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(Ui.H2(title).VCenter(), 0);
		grid.Add(button, 0, 1);
		Border border = new Border();
		border.CornerRadius = new CornerRadius(14.0);
		border.Padding = new Thickness(20.0, 16.0, 20.0, 18.0);
		border.BorderThickness = new Thickness(1.0);
		border.MaxWidth = width;
		border.MinWidth = Math.Min(width, 420.0);
		border.Child = Ui.V(12.0, grid, body);
		Border border2 = border;
		border2.SetResourceReference(Border.BackgroundProperty, "B.GlassStrong");
		border2.SetResourceReference(Border.BorderBrushProperty, "B.GlassBorder");
		Motion.Shadow(border2, 40.0, 0.28, 10.0);
		Layer layer = Push(border2, onClose);
		button.Click += delegate
		{
			onClose?.Invoke();
			layer.Close();
		};
		return layer;
	}

	public static IDisposable Busy(string? text = null)
	{
		if (text == null)
		{
			text = I18n.L("处理中…");
		}
		System.Windows.Controls.ProgressBar progressBar = Ui.Prog();
		progressBar.IsIndeterminate = true;
		progressBar.Width = 220.0;
		Border border = new Border();
		border.CornerRadius = new CornerRadius(12.0);
		border.Padding = new Thickness(24.0, 20.0, 24.0, 20.0);
		border.Child = Ui.V(12.0, Ui.Txt(text).Center(), progressBar);
		border.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		Motion.Shadow(border, 30.0, 0.22, 6.0);
		return new Scope(Push(border, null, dismissable: false, smokeAutoAnswer: false).Close);
	}

	public static string? PickFile(string filter, string? title = null)
	{
		if (title == null)
		{
			title = I18n.L("选择文件");
		}
		if (Smoke.Active)
		{
			Smoke.AutoCancelSystemDialog(title);
			return null;
		}
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Filter = filter,
			Title = title,
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public static string[]? PickFiles(string filter, string? title = null)
	{
		if (title == null)
		{
			title = I18n.L("选择文件");
		}
		if (Smoke.Active)
		{
			Smoke.AutoCancelSystemDialog(title);
			return null;
		}
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Filter = filter,
			Title = title,
			Multiselect = true
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileNames;
	}

	public static string? SaveFile(string filter, string name, string? title = null)
	{
		if (title == null)
		{
			title = I18n.L("保存到");
		}
		if (Smoke.Active)
		{
			Smoke.AutoCancelSystemDialog(title);
			return null;
		}
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Filter = filter,
			FileName = name,
			Title = title
		};
		if (saveFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return saveFileDialog.FileName;
	}

	public static string? PickFolder(string? title = null)
	{
		if (title == null)
		{
			title = I18n.L("选择文件夹");
		}
		if (Smoke.Active)
		{
			Smoke.AutoCancelSystemDialog(title);
			return null;
		}
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = title,
			ShowNewFolderButton = true
		};
		return (folderBrowserDialog.ShowDialog() == DialogResult.OK) ? folderBrowserDialog.SelectedPath : null;
	}
}
