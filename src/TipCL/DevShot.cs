using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TipCL.Pages;
using TipCL.Services;

namespace TipCL;

// 开发截图工具（dev-only）：--shot <pageId> --out <png> --theme <light|dark> [--size 默认档|WxH]
// 启动主窗口 → 应用主题 → 导航到指定页 → RenderTargetBitmap 存 PNG → 退出。
// 页面数据走真实 BridgeClient；"design-sheet" 为 WP1 样张页专用 id。
public static class DevShot
{
	public static int Run(string[] args)
	{
		string pageId = ArgVal(args, "--shot") ?? ArgVal(args, "--design-sheet") ?? "launch";
		string outPath = ArgVal(args, "--out") ?? Path.GetFullPath($"shot-{Sanitize(pageId)}.png");
		bool dark = !string.Equals(ArgVal(args, "--theme"), "light", StringComparison.OrdinalIgnoreCase);
		double w = -1.0;
		double h = -1.0;
		string size = ArgVal(args, "--size") ?? "";
		if (size != "" && size != "default")
		{
			string[] parts = size.Split('x', 'X');
			if (parts.Length == 2 && double.TryParse(parts[0], out w) && double.TryParse(parts[1], out h))
			{
				w = Math.Max(320.0, w);
				h = Math.Max(240.0, h);
			}
			else
			{
				w = -1.0;
				h = -1.0;
			}
		}
		Motion.Enabled = false;
		MainWindow win = new MainWindow();
		win.ShowActivated = false;
		win.ShowInTaskbar = false;
		win.WindowStartupLocation = WindowStartupLocation.Manual;
		win.Left = SystemParameters.VirtualScreenLeft - win.Width - 200.0;
		win.Top = SystemParameters.VirtualScreenTop - win.Height - 200.0;
		win.Show();
		Application.Current.Dispatcher.InvokeAsync((Func<Task>)async delegate
		{
			int code = 0;
			try
			{
				await DriveAsync(win, pageId, dark, outPath, w, h);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("DevShot FAIL: " + ex);
				code = 3;
			}
			// 强制进程退出：BridgeHost 子进程等可能让常规 Shutdown 挂住，残留实例会锁住 obj 里的 pdb/exe
			Environment.Exit(code);
		}, DispatcherPriority.ApplicationIdle);
		return 0;
	}

	private static async Task DriveAsync(MainWindow win, string pageId, bool dark, string outPath, double w, double h)
	{
		try
		{
			await Task.WhenAny(win.BridgeSettled, Task.Delay(40000));
		}
		catch
		{
		}
		await DismissWizard(win);
		await win.Dispatcher.InvokeAsync(delegate
		{
			if (w > 0.0)
			{
				win.MinWidth = w;
				win.MinHeight = h;
				win.Width = w;
				win.Height = h;
			}
			win.Navigate(pageId, instant: true);
		}, DispatcherPriority.ContextIdle);
		PageBase page = win.GetPage(pageId);
		if (page != null)
		{
			Task load = page.EnsureLoadedAsync();
			if (await Task.WhenAny(load, Task.Delay(15000)) != load)
			{
				Console.Error.WriteLine("DevShot: 页面加载超时 " + pageId);
			}
			else
			{
				await load;
			}
			await win.Dispatcher.InvokeAsync(delegate
			{
				page.OnShown();
			}, DispatcherPriority.ContextIdle);
		}
		// 后端主题同步可能晚到，渲染前再钉一次主题
		await win.Dispatcher.InvokeAsync(delegate
		{
			win.ApplyThemeState(dark, persistLocal: false);
		}, DispatcherPriority.ContextIdle);
		await Task.Delay(250);
		Render(win, outPath);
		Console.WriteLine("DevShot OK: " + outPath);
	}

	// 首启向导会盖住截图：找到「跳过向导」按钮以程序方式点掉（等价用户点击，不碰业务代码）
	private static async Task DismissWizard(MainWindow win)
	{
		for (int i = 0; i < 4 && Dlg.AnyOpen; i++)
		{
			ButtonBase? skip = FindButton(Dlg.Host, "跳过向导");
			if (skip == null)
			{
				break;
			}
			await win.Dispatcher.InvokeAsync(delegate
			{
				skip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(350);
			await win.Dispatcher.InvokeAsync(delegate
			{
			}, DispatcherPriority.ContextIdle);
		}
	}

	private static ButtonBase? FindButton(DependencyObject? root, string label)
	{
		if (root == null)
		{
			return null;
		}
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is ButtonBase b && (b.Content as string)?.Contains(label) == true)
			{
				return b;
			}
			ButtonBase? deep = FindButton(child, label);
			if (deep != null)
			{
				return deep;
			}
		}
		return null;
	}

	private static void Render(Window win, string outPath)
	{
		int pixelWidth = (int)Math.Max(1.0, win.ActualWidth);
		int pixelHeight = (int)Math.Max(1.0, win.ActualHeight);
		RenderTargetBitmap rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96.0, 96.0, PixelFormats.Pbgra32);
		rtb.Render(win);
		string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(rtb));
		using FileStream stream = File.Create(outPath);
		encoder.Save(stream);
	}

	private static string? ArgVal(string[] args, string name)
	{
		int idx = Array.IndexOf(args, name);
		if (idx < 0 || idx + 1 >= args.Length)
		{
			return null;
		}
		return args[idx + 1];
	}

	private static string Sanitize(string id)
	{
		return new string(id.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_').ToArray());
	}
}
