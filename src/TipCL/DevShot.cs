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
				await DriveAsync(win, pageId, dark, outPath, w, h, ArgVal(args, "--pop") ?? "", args);
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

	private static async Task DriveAsync(MainWindow win, string pageId, bool dark, string outPath, double w, double h, string pop, string[] args)
	{
		try
		{
			await Task.WhenAny(win.BridgeSettled, Task.Delay(40000));
		}
		catch
		{
		}
		await DismissWizard(win);
		if (args?.Contains("--probe") == true)
		{
			await ProbeAsync();
		}
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
		// --prefer <inst>|<ver>：模拟实例库「设为默认/双击版本」跳启动页的用户路径（ver 可空）
		string prefer = ArgVal(args, "--prefer") ?? "";
		if (prefer.Length > 0 && page is LaunchPage lp2)
		{
			string[] pp = prefer.Split('|');
			System.Reflection.FieldInfo? verFld = typeof(LaunchPage).GetField("_ver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var cb = (System.Windows.Controls.ComboBox?)verFld?.GetValue(lp2);
			lp2.PreferInstance(pp[0], (pp.Length > 1 && pp[1].Length > 0) ? pp[1] : null);
			int last = -1;
			for (int i = 0; i < 45; i++)
			{
				await Task.Delay(100);
				int n = -2;
				string th = "";
				await win.Dispatcher.InvokeAsync(delegate
				{
					n = cb?.Items.Count ?? -3;
					th = Environment.CurrentManagedThreadId.ToString();
				}, DispatcherPriority.Send);
				if (n != last)
				{
					Console.WriteLine("DBG POLL t=" + i + " count=" + n + " thread=" + th);
					if (n == 0 && last > 0)
					{
						Console.WriteLine("DROP STACK:\n" + Environment.StackTrace);
					}
					last = n;
				}
			}
		}
		// --pop <config|instances|news>：浮层打开态。Popup 是独立 HWND，RenderTargetBitmap 拍不到，
		// 把浮层内容临时挂进页面根 Grid 模拟浮层位置后再渲染。
		if (pop.Length > 0 && page is LaunchPage lp)
		{
			await win.Dispatcher.InvokeAsync(delegate
			{
				lp.OpenPopupForDevShot(pop);
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(500);
			await win.Dispatcher.InvokeAsync(delegate
			{
				FrameworkElement? el = lp.DetachPopupForShot(pop);
				if (el != null && lp.Content is Grid root)
				{
					Grid host = new Grid();
					switch (pop)
					{
					case "config":
						host.HorizontalAlignment = HorizontalAlignment.Right;
						host.VerticalAlignment = VerticalAlignment.Top;
						host.Margin = new Thickness(0.0, 56.0, 16.0, 0.0);
						break;
					case "instances":
						host.HorizontalAlignment = HorizontalAlignment.Left;
						host.VerticalAlignment = VerticalAlignment.Center;
						host.Margin = new Thickness(56.0, 0.0, 0.0, 24.0);
						break;
					default:
						host.HorizontalAlignment = HorizontalAlignment.Center;
						host.VerticalAlignment = VerticalAlignment.Bottom;
						host.Margin = new Thickness(0.0, 0.0, 0.0, 44.0);
						break;
					}
					host.Children.Add(el);
					root.Children.Add(host);
				}
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(250);
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

	// dev-only：桥实测——对比 get_installed_versions 与 get_version_rows 的返回（只读）
	private static async Task ProbeAsync()
	{
		var client = AppServices.Client;
		for (int i = 0; i < 50 && client == null; i++)
		{
			await Task.Delay(200);
			client = AppServices.Client;
		}
		if (client == null)
		{
			Console.Error.WriteLine("PROBE: bridge client 未就绪");
			return;
		}
		try
		{
			var insts = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_instances", null, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE get_instances = " + System.Text.Json.JsonSerializer.Serialize(insts));
			var cfg = await client.TryCallAsync<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>("get_settings", null, new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE default_instance = " + ((cfg != null && cfg.TryGetValue("default_instance", out var di)) ? di.ToString() : "<missing>"));
			string instName = (cfg != null && cfg.TryGetValue("default_instance", out var di2) && di2.ValueKind == System.Text.Json.JsonValueKind.String && di2.GetString()!.Length > 0)
				? di2.GetString()!
				: ".minecraft";
			var iv = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_installed_versions", new { instance = instName }, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine($"PROBE get_installed_versions(instance={instName}) = " + System.Text.Json.JsonSerializer.Serialize(iv));
			var ivAll = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_installed_versions", new { }, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE get_installed_versions() = " + System.Text.Json.JsonSerializer.Serialize(ivAll));
			var vr = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_version_rows", null, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE get_version_rows = " + System.Text.Json.JsonSerializer.Serialize(vr));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("PROBE FAIL: " + ex);
		}
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
