using System;
using System.CodeDom.Compiler;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Platform;
using TipCL.Core.Platform.Wpf;
using TipCL.Pages;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL;

public partial class App : Application
{
	private enum Elevation
	{
		NotNeeded,
		Restarted,
		Declined
	}

	// 自检/截图类参数一律免提权(app.manifest 是 asInvoker,提权全靠 RequestElevation):
	// 少了 --design-sheet 时,windows/DECISIONS.md:44「--shot/--design-sheet 加入 SelfCheckArgs」的
	// 决定在代码里是断的 —— 非管理员 shell 跑 `TipCL.exe --design-sheet` 会弹 UAC 并重启一个分离进程,
	// DevShot 的 stdout / 退出码全部丢给那个新进程,截图工具直接失效。
	// --ai-ui-selftest 同口径补上:它同样是"跑完靠退出码/输出交差"的自检 CLI
	// (Pages/Ai/AiSelfTest.cs:29 解析该参数),被 UAC 重启分离后结果一样丢失;且它只用
	// %LOCALAPPDATA% 下的数据目录,不需要管理员权限。CI/验收按 DECISIONS.md:412/498/506 的口径
	// 直接调 CLI,不该被提权打断。
	private static readonly string[] SelfCheckArgs = new string[8] { "--nav-dump", "--ime-check", "--i18n-check", "--consent-check", "--smoke", "--shot", "--design-sheet", "--ai-ui-selftest" };

	private const int DeclineExitCode = 1223;

	public static bool IsDark { get; private set; }

	// 桌面快捷方式带过来的「直接启动」参数(-i <实例> launch <版本>,形状由 Core/Launcher/Shortcut.LaunchArgs
	// 产出;Linux 侧由 src/TipCL.App/Session/CommandLine.cs 消费,Windows 侧以前没人读 —— 双击 .lnk 只是
	// 把启动器打开。这里只把实例/版本记下来,等后端连上、启动页就绪后由 MainWindow 真正拉起。
	// 见 PROGRESS.md 记录「Windows 的创建桌面快捷方式本身就是坏的」。
	private static string? _startInstance;

	private static string? _startVersion;

	/// <summary>取走(并清空)快捷方式带来的启动参数;没有就返回 false(取一次就清,重连/换窗口都不会再触发一次启动)。</summary>
	internal static bool TakeStartArgs(out string? instance, out string? version)
	{
		instance = _startInstance;
		version = _startVersion;
		_startInstance = null;
		_startVersion = null;
		return !string.IsNullOrEmpty(instance);
	}

	// 解析规则挪进 Services/StartArgs(纯函数 + 单测覆盖畸形输入);这里只落状态。
	private static void ParseStartArgs(string[] args)
	{
		if (TipCL.Services.StartArgs.TryParse(args, out string instance, out string version))
		{
			_startInstance = instance;
			_startVersion = version;
		}
	}

	internal static ResourceDictionary? PaletteDictionary
	{
		get
		{
			int num = PaletteIndex();
			if (num < 0)
			{
				return null;
			}
			return Application.Current.Resources.MergedDictionaries[num];
		}
	}

	public static event Action? ThemeVisualsChanged;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		switch (RequestElevation(e.Args))
		{
		case Elevation.Declined:
			Shutdown(1223);
			return;
		case Elevation.Restarted:
			Shutdown(0);
			return;
		}
		if (e.Args.Contains("--nav-dump"))
		{
			Environment.ExitCode = NavDump.Run();
			Shutdown(Environment.ExitCode);
			return;
		}
		if (e.Args.Contains("--ime-check"))
		{
			Environment.ExitCode = ImeGuard.SelfTest();
			Shutdown(Environment.ExitCode);
			return;
		}
		if (e.Args.Contains("--i18n-check"))
		{
			Environment.ExitCode = I18nCheck.Run(e.Args);
			Shutdown(Environment.ExitCode);
			return;
		}
		if (e.Args.Contains("--consent-check"))
		{
			Environment.ExitCode = FeedbackConsent.SelfTest();
			Shutdown(Environment.ExitCode);
			return;
		}
		InitPlatform();
		I18n.Init(e.Args);
		// L6:注册自建区块 / 插件区块的内容工厂(显式调用,不用 ModuleInitializer)
		TipCL.Pages.Layout.WidgetRegistry.EnsureRegistered();
		// P1:把 v1 写通(<数据目录>/ui_page_layouts.json)的注入缝装配给 Core 的 LayoutStoreV2 ——
		// Core 不再认识 Pages 的 v1 类型(PageLayoutDoc / PageLayoutStore),装配后行为与重构前一致
		TipCL.Core.Launcher.Layout.LayoutStoreV2.V1Bridge = new TipCL.Pages.Layout.PagesLayoutV1Bridge();
		// 升级首跑的一次性迁移:此时 v1 注入缝已装配(数据目录/语义与旧版一致)、任何页面/编辑器
		// 都还没读过布局 —— 只有 v1 时在这里把结果落成 v2,之后所有读取走 v2 真源;已有 v2 时等价于 Load。
		try
		{
			TipCL.Core.Launcher.Layout.LayoutStoreV2.LoadMigrating();
		}
		catch
		{
			// 迁移失败不能挡住启动:LayoutStoreV2 的读路径本身有 v1 回落,下次保存时还会再走迁移入口
		}
		// 动画帧率上限:默认**不设**(WPF 不覆写 DesiredFrameRate 时,动画跟随合成器/显示器刷新率,
		// 120/144/165Hz 屏上才可能真的顺)。此前这里硬写 60,等于给所有 WPF 动画(页面切换、
		// 悬停、进度条)装了个 60fps 天花板 —— 在高刷屏上和系统、浏览器一对比就是"说不上哪里不顺"。
		// 需要省电/回到旧行为的用户可设环境变量 TIPCL_FPS_CAP=60(24..480 之间生效)。
		string capText = Environment.GetEnvironmentVariable("TIPCL_FPS_CAP");
		if (int.TryParse(capText, out int capFps) && capFps >= 24 && capFps <= 480)
		{
			Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(capFps));
		}
		RenderOptions.ProcessRenderMode = RenderMode.Default;
		base.DispatcherUnhandledException += OnUnhandled;
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs args)
		{
			LogCrash(args.ExceptionObject as Exception);
		};
		if (e.Args.Contains("--ai-ui-selftest"))
		{
			Environment.ExitCode = AiSelfTest.Run(e.Args);
			Shutdown(Environment.ExitCode);
		}
		else if (e.Args.Contains("--smoke"))
		{
			Smoke.Run(e.Args);
		}
		else if (e.Args.Contains("--shot") || e.Args.Contains("--design-sheet"))
		{
			// dev-only：单页截图（WP1 样张 / WP3 逐页回归矩阵）
			Environment.ExitCode = DevShot.Run(e.Args);
		}
		else
		{
			// 桌面快捷方式 / 命令行直接启动:参数在开窗之前解析好,连上后端后由 MainWindow 消费
			ParseStartArgs(e.Args);
			new MainWindow().Show();
		}
	}

	internal static void InitPlatform()
	{
		try
		{
			PlatformServices.ImageCodec = new WpfImageCodec();
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				PlatformServices.SecretStore = new WinCredSecretStore();
			}
			Crash.ShellOpenHook = ShellOpen;
			Auth.OpenBrowserHook = delegate(string url)
			{
				// 统一走 Ui.OpenUrl(节流 + 落日志),别再自己 Process.Start:
				// 登录链接被"连点重试"时同样会开出多个浏览器标签。
				Ui.OpenUrl(url, "auth.browser");
			};
		}
		catch (Exception ex)
		{
			LogCrash(ex);
		}
	}

	private static void ShellOpen(string path)
	{
		try
		{
			using (Process.Start(new ProcessStartInfo(PPath.Abs(path))
			{
				UseShellExecute = true
			}))
			{
			}
		}
		catch (Win32Exception ex)
		{
			throw new OSError(ex.Message, ex);
		}
		catch (InvalidOperationException ex2)
		{
			throw new OSError(ex2.Message, ex2);
		}
	}

	public static bool IsElevated()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	private static bool NeedsElevation(string[] args)
	{
		// dev-only：UI 自动化验收用，跳过运行期自提权（正常启动仍按 manifest requireAdministrator 提权）
		if (args.Contains<string>("--no-elevate"))
		{
			return false;
		}
		string[] selfCheckArgs = SelfCheckArgs;
		foreach (string value in selfCheckArgs)
		{
			if (args.Contains<string>(value))
			{
				return false;
			}
		}
		return true;
	}

	private static Elevation RequestElevation(string[] args)
	{
		if (!NeedsElevation(args) || IsElevated())
		{
			return Elevation.NotNeeded;
		}
		string text;
		try
		{
			text = Process.GetCurrentProcess().MainModule?.FileName ?? "";
		}
		catch
		{
			text = "";
		}
		if (string.IsNullOrEmpty(text))
		{
			return Elevation.NotNeeded;
		}
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = text,
			UseShellExecute = true,
			Verb = "runas",
			WorkingDirectory = AppContext.BaseDirectory,
			Arguments = JoinArgs(args)
		};
		try
		{
			Process.Start(startInfo);
			return Elevation.Restarted;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			MessageBox.Show(I18n.L("TipCL 需要管理员权限才能运行。\n\n已取消提权，程序即将退出。"), I18n.L("需要管理员权限"), MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return Elevation.Declined;
		}
		catch (Exception ex2)
		{
			LogCrash(ex2);
			MessageBox.Show(I18n.L("TipCL 需要管理员权限才能运行，但提权失败：\n\n{0}", ex2.Message), I18n.L("需要管理员权限"), MessageBoxButton.OK, MessageBoxImage.Hand);
			return Elevation.Declined;
		}
	}

	private static string JoinArgs(string[] args)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string text in args)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(' ');
			}
			if (text.Length == 0)
			{
				stringBuilder.Append("\"\"");
				continue;
			}
			if (text.IndexOfAny(new char[3] { ' ', '\t', '"' }) < 0)
			{
				stringBuilder.Append(text);
				continue;
			}
			stringBuilder.Append('"');
			int num = 0;
			string text2 = text;
			foreach (char c in text2)
			{
				switch (c)
				{
				case '\\':
					num++;
					break;
				case '"':
					stringBuilder.Append('\\', num * 2 + 1);
					stringBuilder.Append('"');
					num = 0;
					break;
				default:
					stringBuilder.Append('\\', num);
					num = 0;
					stringBuilder.Append(c);
					break;
				}
			}
			stringBuilder.Append('\\', num * 2);
			stringBuilder.Append('"');
		}
		return stringBuilder.ToString();
	}

	private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		LogCrash(e.Exception);
		Smoke.Note("unhandled", e.Exception.ToString());
		e.Handled = true;
		try
		{
			(Application.Current?.MainWindow as MainWindow)?.Toast(I18n.L("出错了"), e.Exception.Message, ToastKind.Error);
		}
		catch
		{
		}
	}

	private static void LogCrash(Exception? ex)
	{
		if (ex == null)
		{
			return;
		}
		string contents = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
		string[] array = new string[3]
		{
			AppContext.BaseDirectory,
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			Path.GetTempPath()
		};
		foreach (string text in array)
		{
			if (!string.IsNullOrEmpty(text))
			{
				try
				{
					File.AppendAllText(Path.Combine(text, "tipcl-wpf-error.log"), contents);
					break;
				}
				catch
				{
				}
			}
		}
	}

	internal static void RaiseThemeVisualsChanged()
	{
		try
		{
			App.ThemeVisualsChanged?.Invoke();
		}
		catch (Exception)
		{
		}
	}

	internal static Uri PaletteUri(bool dark)
	{
		return new Uri(dark ? "Themes/Palette.Dark.xaml" : "Themes/Palette.Light.xaml", UriKind.Relative);
	}

	internal static int PaletteIndex()
	{
		Collection<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
		for (int i = 0; i < mergedDictionaries.Count; i++)
		{
			if ((mergedDictionaries[i].Source?.OriginalString ?? "").IndexOf("Themes/Palette.", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return i;
			}
		}
		for (int j = 0; j < mergedDictionaries.Count; j++)
		{
			if (mergedDictionaries[j].Contains("C.Paper") && mergedDictionaries[j].Contains("B.Accent"))
			{
				return j;
			}
		}
		return -1;
	}

	public static void ApplyTheme(bool dark)
	{
		IsDark = dark;
		Collection<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
		ResourceDictionary resourceDictionary = new ResourceDictionary
		{
			Source = PaletteUri(dark)
		};
		int num = PaletteIndex();
		if (num >= 0)
		{
			mergedDictionaries[num] = resourceDictionary;
		}
		else
		{
			mergedDictionaries.Insert(0, resourceDictionary);
		}
		SettingsPage.ApplyVisuals();
	}

}
