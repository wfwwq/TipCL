using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TipCL.Themes;

namespace TipCL.Services;

/// <summary>
/// 对话框/浮层服务。公开面与 Windows 版一一对应(<c>Services\Dlg.cs</c>,516 行):
/// <c>Layer</c>(Root/Card/Close)/ <c>Host</c> / <c>BlurTarget</c> / <c>AnyOpen</c> /
/// <c>Confirm</c> / <c>Ask</c> / <c>Alert</c> / <c>Prompt</c> / <c>Choose</c> / <c>Form&lt;T&gt;</c> /
/// <c>Panel</c> / <c>Busy</c> / <c>PickFile</c> / <c>PickFiles</c> / <c>SaveFile</c> / <c>PickFolder</c>。
/// 方法名、参数名、顺序、默认值与回调形状全部照抄。
/// 所有卡片式对话框(Confirm / Ask / Alert / Prompt / Choose / Form)都渲染在同一张
/// <see cref="DialogCard"/> 上(C2c1 把 C2a 内联在 <c>Shell</c> 里的那段提成具名类型,
/// 结构等价,见 D-107);<see cref="Panel"/> 与 <see cref="Busy"/> 各自的内联卡片照旧。
///
/// 四类移植差异(逐条记在 PROGRESS.md 的 C2a 差异表):
/// 1. WPF 的 <c>UIElement</c>/<c>FrameworkElement</c> → <c>Control</c>;<c>Visibility</c> → <c>IsVisible</c>;
///    <c>SetResourceReference</c> → <c>UiExt.Dyn</c>(<c>DynamicResourceExtension</c>);
///    <c>MouseLeftButtonDown</c> → <c>PointerPressed</c>(只认左键)。
/// 2. 卡片 MaxHeight 的绑定:WPF 绑 <c>Window.ActualHeight</c>(converter × 0.9),
///    mac 侧绑**宿主 Grid 的 <c>Bounds.Height</c>**(Avalonia 没有 ActualHeight,见 D-82)。
/// 3. 文件/文件夹选择:WPF 的 <c>Microsoft.Win32.*Dialog</c> / <c>FolderBrowserDialog</c> 在 Avalonia 是
///    <c>IStorageProvider</c> 且**只有异步 API**,所以这里在等它的同时把 Dispatcher 的排队工作跑掉
///    (等价 WPF 的同步嵌套消息循环)。过滤器语法仍是 WPF 那串 <c>标签|模式</c>,由
///    <see cref="ParseFilter"/> 转成 <c>FilePickerFileType[]</c>。拿不到提供者(离屏 / 测试 / 还没起窗口)
///    时返回 null 并只写调试输出 —— 不抛异常、不起进程。
/// 4. 离屏(<c>--smoke</c>)自动答答案:Windows 版直接调 <c>Smoke.AutoAnswerDialog</c>;
///    mac 的 <c>Smoke\</c> 里还没有这两个入口(属 C2b/D1),所以改成可注入的挂点
///    <see cref="SmokeAutoAnswer"/> / <see cref="SmokeAutoCancelSystemDialog"/>,未接线时这一步不生效。
/// </summary>
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
		private readonly Action _close;

		private int _done;

		public Scope(Action close)
		{
			_close = close;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _done, 1) == 0)
			{
				_close();
			}
		}
	}

	/// <summary>浮层宿主(C2b 的外壳把主窗口里的浮层 Grid 挂到这里)。</summary>
	public static Grid? Host { get; set; }

	/// <summary>浮层打开时被模糊的底图(可选)。</summary>
	public static Control? BlurTarget { get; set; }

	public static bool AnyOpen => Host?.Children.Count > 0;

	/// <summary>离屏(<c>--smoke</c>)模式。由 Smoke 那条路径置位(属 C2b/D1)。</summary>
	internal static bool SmokeActive { get; set; }

	/// <summary>离屏下自动回答对话框(等价 Windows 的 <c>Smoke.AutoAnswerDialog</c>)。</summary>
	internal static Action<Control, Action>? SmokeAutoAnswer { get; set; }

	/// <summary>离屏下自动取消系统文件/文件夹对话框(等价 <c>Smoke.AutoCancelSystemDialog</c>)。</summary>
	internal static Action<string>? SmokeAutoCancelSystemDialog { get; set; }

	private static Layer Push(Control card, Action? onDismiss, bool dismissable = true, bool smokeAutoAnswer = true)
	{
		Grid host = Host ?? throw new InvalidOperationException(I18n.L("对话层未初始化"));
		Border mask = new Border
		{
			Opacity = 0.0
		};
		mask.Dyn(Border.BackgroundProperty, "B.Mask");
		Grid wrap = new Grid();
		wrap.Children.Add(mask);
		Grid holder = new Grid
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(24.0)
		};
		holder.Children.Add(card);
		wrap.Children.Add(holder);
		// 卡片最高不超过宿主高度的 90%(扣掉 holder 24px 边距),防止矮窗口下按钮被裁掉(WP-S11)
		card.Bind(Border.MaxHeightProperty, new Binding("Bounds.Height")
		{
			Source = host,
			Converter = new Ui.WindowPctConverter(0.9)
		});
		host.Children.Add(wrap);
		host.IsVisible = true;
		if (BlurTarget != null && host.Children.Count == 1)
		{
			BlurTarget.Effect = new BlurEffect
			{
				Radius = 8.0
			};
		}
		Motion.Fade(mask, 1.0, 150.0);
		Motion.PopIn(holder);
		if (dismissable)
		{
			mask.PointerPressed += delegate(object? _, PointerPressedEventArgs e)
			{
				if (!e.GetCurrentPoint(mask).Properties.IsLeftButtonPressed)
				{
					return;
				}
				onDismiss?.Invoke();
				Close();
			};
		}
		wrap.Focusable = true;
		wrap.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.Key == Key.Escape && dismissable)
			{
				onDismiss?.Invoke();
				Close();
				e.Handled = true;
			}
		};
		if (!SmokeActive)
		{
			wrap.Loaded += delegate
			{
				wrap.Focus();
			};
		}
		if (SmokeActive && smokeAutoAnswer)
		{
			SmokeAutoAnswer?.Invoke(card, delegate
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
						host.IsVisible = false;
						if (BlurTarget != null)
						{
							BlurTarget.Effect = null;
						}
					}
				});
			}
		}
	}

	// 卡片本身在 Services\DialogCard.cs(C2c1 把 C2a 内联在这里的那段逐行等价的搬运提成具名类型,
	// 好让"对话框卡片"能被单独钉住;控件树、取值、行为一个字没改,见 D-107)。
	// 行结构 Auto,*,Auto:按钮行(footer)钉在卡片底部,body 行吃剩余高度并在超出时滚动,
	// 避免高内容把 footer 挤出窗口(WP-S11)。
	private static Border Shell(string title, Control body, Control? footer, double width)
	{
		return new DialogCard(title, body, footer, width);
	}

	public static Task<bool> Confirm(string title, string body, string? ok = null, string? cancel = null, bool danger = false)
	{
		return Ask(title, Ui.Muted(body).Wrap().MinW(320.0), ok, cancel, danger);
	}

	public static Task<bool> Ask(string title, Control body, string? ok = null, string? cancel = null, bool danger = false, double width = 520.0)
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		DialogCard.ConfirmButtons buttons = DialogCard.ConfirmFooter(ok, cancel, danger);
		Button button = buttons.Ok;
		Button button2 = buttons.Cancel;
		SPanel footer = buttons.Footer;
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
		Button button = Ui.Btn(I18n.L("知道了"), BtnKind.Primary);
		TextBox textBox = new TextBox
		{
			Text = body,
			IsReadOnly = true,
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 380.0,
			FontSize = 13.0
		};
		// WPF 版写的是 TextBox.VerticalScrollBarVisibility;Avalonia 的 TextBox 没有这个属性,
		// 等价写法是 ScrollViewer 的附加属性(尽力而为:是否被 TextBox 模板采纳见 C2a 未验证项)。
		ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
		textBox.Dyn(TextBox.ForegroundProperty, "B.InkSoft");
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
		TextBox input = Ui.Input(placeholder, initial).MinW(340.0);
		SPanel body = Ui.V(6.0, Ui.Muted(label), input);
		Button button = Ui.Btn(I18n.L("确定"), BtnKind.Primary);
		Button button2 = Ui.Btn(I18n.L("取消"));
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
		input.KeyDown += delegate(object? _, KeyEventArgs e)
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

	public static Task<int> Choose(string title, Control body, string[] options, double width = 560.0, bool dismissable = true)
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
			Button button = Ui.Btn(options[num], (num != options.Length - 1) ? BtnKind.Normal : BtnKind.Primary);
			button.Click += delegate
			{
				tcs.TrySetResult(idx);
				layer.Close();
			};
			sPanel.Children.Add(button);
		}
		return tcs.Task;
	}

	public static async Task<T?> Form<T>(string title, Control body, Func<T> collect, string? ok = null, double width = 560.0) where T : class
	{
		T result = null;
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		Button button = Ui.Btn(ok ?? I18n.L("保存"), BtnKind.Primary);
		Button button2 = Ui.Btn(I18n.L("取消"));
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

	public static Layer Panel(string title, Control body, double width = 720.0, Action? onClose = null, bool dismissable = true)
	{
		Button button = Ui.IconBtn("\ue711", I18n.L("关闭"));
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(Ui.H2(title).VCenter(), 0);
		grid.Add(button, 0, 1);
		// body 超出时滚动(WP-S11):Panel 的 body 不再直接撑高卡片,配合 Push 里的宿主高度上限
		ScrollViewer bodyScroll = new ScrollViewer
		{
			Content = body,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		Border border = new Border();
		border.CornerRadius = new CornerRadius(10.0);
		border.Padding = new Thickness(20.0, 16.0, 20.0, 18.0);
		border.BorderThickness = new Thickness(1.0);
		border.MaxWidth = width;
		border.MinWidth = Math.Min(width, 420.0);
		border.Child = Ui.V(12.0, grid, bodyScroll);
		Border border2 = border;
		border2.Dyn(Border.BackgroundProperty, "B.GlassStrong");
		border2.Dyn(Border.BorderBrushProperty, "B.GlassBorder");
		Motion.Shadow(border2, 40.0, 0.28, 10.0);
		Layer layer = Push(border2, onClose, dismissable);
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
			text = I18n.L("处理中...");
		}
		ProgressBar progressBar = Ui.Prog();
		progressBar.IsIndeterminate = true;
		progressBar.Width = 220.0;
		Border border = new Border();
		border.CornerRadius = new CornerRadius(10.0);
		border.Padding = new Thickness(24.0, 20.0, 24.0, 20.0);
		border.Child = Ui.V(12.0, Ui.Txt(text).Center(), progressBar);
		border.Dyn(Border.BackgroundProperty, "B.Paper");
		Motion.Shadow(border, 30.0, 0.22, 6.0);
		return new Scope(Push(border, null, dismissable: false, smokeAutoAnswer: false).Close);
	}

	public static string? PickFile(string filter, string? title = null)
	{
		string[]? picked = PickFiles(filter, title, multi: false);
		return (picked != null && picked.Length != 0) ? picked[0] : null;
	}

	public static string[]? PickFiles(string filter, string? title = null)
	{
		return PickFiles(filter, title, multi: true);
	}

	private static string[]? PickFiles(string filter, string? title, bool multi)
	{
		if (title == null)
		{
			title = I18n.L("选择文件");
		}
		if (SmokeActive)
		{
			SmokeAutoCancelSystemDialog?.Invoke(title);
			return null;
		}
		IStorageProvider? provider = Provider();
		if (provider == null)
		{
			Debug.WriteLine("[dlg] 没有可用窗口,文件对话框不可用(未接线): " + title);
			return null;
		}
		try
		{
			FilePickerOpenOptions options = new FilePickerOpenOptions
			{
				Title = title,
				AllowMultiple = multi,
				FileTypeFilter = ParseFilter(filter)
			};
			IReadOnlyList<IStorageFile> files = WaitForPicker(provider.OpenFilePickerAsync(options));
			if (files == null || files.Count == 0)
			{
				return null;
			}
			List<string> paths = new List<string>();
			foreach (IStorageFile file in files)
			{
				string? path = file.TryGetLocalPath();
				if (!string.IsNullOrEmpty(path))
				{
					paths.Add(path);
				}
			}
			return (paths.Count != 0) ? paths.ToArray() : null;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[dlg] 选择文件失败: " + ex.Message);
			return null;
		}
	}

	public static string? SaveFile(string filter, string name, string? title = null)
	{
		if (title == null)
		{
			title = I18n.L("保存到");
		}
		if (SmokeActive)
		{
			SmokeAutoCancelSystemDialog?.Invoke(title);
			return null;
		}
		IStorageProvider? provider = Provider();
		if (provider == null)
		{
			Debug.WriteLine("[dlg] 没有可用窗口,保存对话框不可用(未接线): " + title);
			return null;
		}
		try
		{
			FilePickerSaveOptions options = new FilePickerSaveOptions
			{
				Title = title,
				SuggestedFileName = name,
				FileTypeChoices = ParseFilter(filter)
			};
			IStorageFile? file = WaitForPicker(provider.SaveFilePickerAsync(options));
			return file?.TryGetLocalPath();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[dlg] 保存失败: " + ex.Message);
			return null;
		}
	}

	public static string? PickFolder(string? title = null)
	{
		if (title == null)
		{
			title = I18n.L("选择文件夹");
		}
		if (SmokeActive)
		{
			SmokeAutoCancelSystemDialog?.Invoke(title);
			return null;
		}
		IStorageProvider? provider = Provider();
		if (provider == null)
		{
			Debug.WriteLine("[dlg] 没有可用窗口,选择文件夹不可用(未接线): " + title);
			return null;
		}
		try
		{
			FolderPickerOpenOptions options = new FolderPickerOpenOptions
			{
				Title = title,
				AllowMultiple = false
			};
			IReadOnlyList<IStorageFolder> folders = WaitForPicker(provider.OpenFolderPickerAsync(options));
			if (folders == null || folders.Count == 0)
			{
				return null;
			}
			return folders[0].TryGetLocalPath();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[dlg] 选择文件夹失败: " + ex.Message);
			return null;
		}
	}

	private static IStorageProvider? Provider()
	{
		return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
	}

	/// <summary>
	/// 等一个存储提供者的异步结果,同时把 Dispatcher 的排队工作跑掉 —— 等价 WPF 里
	/// <c>ShowDialog()</c> 的同步嵌套消息循环(Avalonia 只给了异步 API)。上限 10 分钟,超时按取消处理。
	/// </summary>
	private static T? WaitForPicker<T>(Task<T> task) where T : class
	{
		Stopwatch sw = Stopwatch.StartNew();
		while (!task.IsCompleted && sw.Elapsed < TimeSpan.FromMinutes(10.0))
		{
			try
			{
				Dispatcher.UIThread.RunJobs();
			}
			catch (Exception)
			{
			}
			Thread.Sleep(1);
		}
		return task.IsCompleted ? ((task.Status == TaskStatus.RanToCompletion) ? task.Result : null) : null;
	}

	/// <summary>
	/// WPF 的过滤器串 → Avalonia 的 <c>FilePickerFileType[]</c>。
	/// 语法照 WPF:<c>标签1|模式1;模式2|标签2|模式3</c>;只有一个字段时整个串当模式表。
	/// <c>*.*</c> 换成 <c>*</c>(Avalonia 的"全部文件"写法)。空串返回空数组(不过滤)。
	/// </summary>
	internal static FilePickerFileType[] ParseFilter(string? filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return Array.Empty<FilePickerFileType>();
		}

		string[] segments = filter.Split('|');
		List<FilePickerFileType> types = new List<FilePickerFileType>();
		if (segments.Length == 1)
		{
			FilePickerFileType single = BuildType(I18n.L("文件"), segments[0]);
			return (single != null) ? new FilePickerFileType[1] { single } : Array.Empty<FilePickerFileType>();
		}

		for (int i = 0; i + 1 < segments.Length; i += 2)
		{
			FilePickerFileType? type = BuildType(segments[i].Trim(), segments[i + 1]);
			if (type != null)
			{
				types.Add(type);
			}
		}
		return types.ToArray();
	}

	private static FilePickerFileType? BuildType(string label, string patterns)
	{
		List<string> list = new List<string>();
		foreach (string raw in patterns.Split(';'))
		{
			string pattern = raw.Trim();
			if (pattern.Length == 0)
			{
				continue;
			}
			list.Add((pattern == "*.*") ? "*" : pattern);
		}
		if (list.Count == 0)
		{
			return null;
		}
		return new FilePickerFileType(label.Length == 0 ? I18n.L("文件") : label)
		{
			Patterns = list
		};
	}
}
