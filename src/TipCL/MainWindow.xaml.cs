using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;

namespace TipCL;

public partial class MainWindow : Window
{
	private struct POINT
	{
		public int x;

		public int y;
	}

	private struct RECT
	{
		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	private struct MINMAXINFO
	{
		public POINT ptReserved;

		public POINT ptMaxSize;

		public POINT ptMaxPosition;

		public POINT ptMinTrackSize;

		public POINT ptMaxTrackSize;
	}

	private struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public int dwFlags;
	}

	private readonly Dictionary<string, PageBase> _pages = new Dictionary<string, PageBase>();

	private PageBase? _current;

	private string _currentId = "";

	private Border? _dockCard;

	private ProgressBar? _dockBar;

	private TextBlock? _dockTitle;

	private TextBlock? _dockStatus;

	private bool _bridgeReady;

	private bool _drained;

	private readonly TaskCompletionSource<object?> _bridgeSettled = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

	private bool _reconnecting;

	private DispatcherTimer? _refreshTimer;

	private bool? _pendingDark;

	private readonly Dictionary<string, bool> _downloadTitle = new Dictionary<string, bool>();

	public const int SideMinWidth = 140;

	public const int SideMaxWidth = 320;

	private const double SideDefaultWidth = 196.0;

	// 顶栏页签壳（action-first）：侧栏不再挂可视树，但导航数据结构与行缓存保留，
	public static readonly List<NavDef> NavDefs = new List<NavDef>
	{
		new NavDef("launch", I18n.L("启动"), "\ue7c1", () => new LaunchPage()),
		new NavDef("library", I18n.L("实例库"), "\ue80a", () => new LibraryPage()),
		new NavDef("instance", I18n.L("版本管理"), "\ue80a", () => new VersionManagePage()),
		new NavDef("account", I18n.L("账号"), "\ue77b", () => new AccountPage()),
		new NavDef("ai", I18n.L("AI 助手"), "\ue99a", () => new AiPage()),
		new NavDef("multiplayer", I18n.L("联机"), "\ue701", () => new MultiplayerPage()),
		new NavDef("settings", I18n.L("设置"), "\ue713", () => new SettingsPage()),
		new NavDef("tasks", I18n.L("下载任务"), "\ue9d5", () => new TasksPage()),
		new NavDef("download", I18n.L("下载"), "\ue896", () => new DownloadPage()),
		new NavDef("plugins", I18n.L("插件"), "\uea86", () => new TipCL.Pages.Plugins.PluginsPage())
	};

	private UiPrefs _prefs = new UiPrefs();

	private NavConfig _navCfg = new NavConfig();

	public string CurrentPageId => _currentId;

	public Task BridgeSettled => _bridgeSettled.Task;

	private static string PrefsPath
	{
		get
		{
			string path;
			try
			{
				path = BridgeHost.FindRoot();
			}
			catch
			{
				path = AppContext.BaseDirectory;
			}
			return System.IO.Path.Combine(path, "wpf-ui.json");
		}
	}

	public UiPrefs Prefs => _prefs;

	public NavConfig NavConfig => _navCfg;

	public MainWindow()
	{
		InitializeComponent();
		FitToWorkArea();
		SetResourceReference(Control.ForegroundProperty, "B.Ink");
		base.Title = I18n.L("TipCL 启动器");
		ThemeBtn.ToolTip = I18n.L("切换深色 / 浅色");
		AiBtn.ToolTip = I18n.L("AI 助手") + " Ctrl+K";
		// 可访问性：纯图标按钮给屏幕阅读器一个名字；标题栏按钮可 Tab 到达（XAML 里 IsTabStop=True）
		System.Windows.Automation.AutomationProperties.SetName(AiBtn, I18n.L("AI 助手"));
		System.Windows.Automation.AutomationProperties.SetName(MinBtn, I18n.L("最小化"));
		System.Windows.Automation.AutomationProperties.SetName(MaxBtn, I18n.L("最大化"));
		System.Windows.Automation.AutomationProperties.SetName(CloseBtn, I18n.L("关闭"));
		System.Windows.Automation.AutomationProperties.SetName(AccBtn, I18n.L("未登录"));
		AccName.Text = I18n.L("未登录");
		AccInitial.Text = AccName.Text.Substring(0, 1);
		SyncThemeIndicator();
		DropVeilTitle.Text = I18n.L("松手就装");
		DropVeilHint.Text = I18n.L("整合包、模组、资源包、光影、数据包、存档、皮肤、壁纸都认；认不准会问你一句。");
		AppServices.Window = this;
		Dlg.Host = DialogHost;
		Dlg.BlurTarget = ContentLayer;
		base.SourceInitialized += delegate
		{
			Backdrop.Apply(this);
			ApplyWindowCorner();
		};
		Wallpaper.Attach(WallHost, RootBorder, TopBar, null, PageWash);
		Wallpaper.ChromeInvalidated = delegate
		{
			SyncMaxGlyph();
		};
		TabLaunch.Click += delegate
		{
			Navigate("launch");
		};
		TabLibrary.Click += delegate
		{
			Navigate("library");
		};
		TabTasks.Click += delegate
		{
			Navigate("download");
		};
		TabPlugins.Click += delegate
		{
			Navigate("plugins");
		};
		TabSettings.Click += delegate
		{
			Navigate("settings");
		};
		AiBtn.Click += delegate
		{
			Navigate("ai");
		};
		AccBtn.Click += delegate
		{
			Navigate("account");
		};
		MinBtn.Click += delegate
		{
			base.WindowState = WindowState.Minimized;
		};
		MaxBtn.Click += delegate
		{
			ToggleMax();
		};
		CloseBtn.Click += delegate
		{
			Close();
		};
		ThemeBtn.Click += delegate
		{
			ToggleTheme();
		};
		base.StateChanged += delegate
		{
			SyncMaxGlyph();
			Wallpaper.SetWindowActive(base.WindowState != WindowState.Minimized);
		};
		base.Activated += delegate
		{
			Wallpaper.SetWindowActive(base.WindowState != WindowState.Minimized);
		};
		base.Deactivated += delegate
		{
			Wallpaper.SetWindowActive(active: false);
		};
		base.PreviewDragEnter += delegate(object _, DragEventArgs e)
		{
			ShowDropVeil(e.Data.GetDataPresent(DataFormats.FileDrop));
		};
		base.PreviewDragOver += delegate(object _, DragEventArgs e)
		{
			ShowDropVeil(e.Data.GetDataPresent(DataFormats.FileDrop));
		};
		base.PreviewDragLeave += delegate
		{
			ShowDropVeil(on: false);
		};
		base.PreviewDrop += delegate
		{
			ShowDropVeil(on: false);
		};
		base.DragEnter += OnDragOver;
		base.DragOver += OnDragOver;
		base.Drop += OnDrop;
		base.PreviewKeyDown += OnShortcut;
		base.Closing += OnClosing;
		base.Closed += delegate
		{
			if (_stagedUpdate != null)
			{
				try
				{
					TipCL.Core.Launcher.Updater.ApplyStaged(_stagedUpdate, restart: false);
				}
				catch (Exception)
				{
				}
				_stagedUpdate = null;
			}
			Wallpaper.Shutdown();
			AppServices.Host?.Dispose();
		};
		TaskStore.Added += delegate
		{
			base.Dispatcher.Invoke(SyncTasks);
		};
		TaskStore.Updated += delegate
		{
			base.Dispatcher.Invoke(SyncDock);
		};
		TaskStore.Cleared += delegate
		{
			base.Dispatcher.Invoke(SyncTasks);
		};
		LoadUiPrefs();
		BuildDock();
		Navigate("launch", instant: true);
		base.Loaded += delegate
		{
			Run(() => ConnectAsync());
		};
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (_drained || !_bridgeReady)
		{
			return;
		}
		e.Cancel = true;
		_drained = true;
		Run(async delegate
		{
			try
			{
				Task<object> task = AppServices.Client.TryCallAsync<object>("shutdown", new
				{
					timeout_ms = 800
				});
				await Task.WhenAny(task, Task.Delay(1500));
			}
			catch
			{
			}
			Close();
		});
	}

	private async Task ConnectAsync(bool interactive = true)
	{
		SetBridgeState(I18n.L("连接中"), "B.Warn");
		DetachHost();
		try
		{
			BridgeHost host = await BridgeHost.StartAsync();
			AppServices.Host = host;
			AppServices.Client = host.Client;
			host.Client.EventReceived += OnBridgeEvent;
			host.Client.StreamStateChanged += delegate(object _, bool ok)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					SetBridgeState(ok ? host.Backend : I18n.L("重连中"), ok ? "B.Accent" : "B.Warn");
					BridgePill.ToolTip = (ok ? $"{host.Backend} · 127.0.0.1:{host.Port}" : (I18n.L("事件流断开：") + host.Client.LastStreamError));
					if (ok)
					{
						Run(ReconcileTasksAsync);
					}
				});
			};
			host.Exited += delegate
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					SetBridgeState(I18n.L("后端已退出"), "B.Danger");
					BridgePill.ToolTip = I18n.L("桥进程已退出，正在重连…");
					Run(ReconnectAfterDeathAsync);
				});
			};
			_bridgeReady = true;
			SetBridgeState(host.Backend, "B.Accent");
			await SyncAppearanceFromBridgeAsync();
			await LoadNavFromBridgeAsync();
			LoadPluginsAfterConnect();
			await Wallpaper.ReloadAsync();
			await ReconcileTasksAsync();
			await ReloadCurrentAsync();
			await SyncAccountChipAsync();
			_bridgeSettled.TrySetResult(null);
			if (!Smoke.Active)
			{
				await FirstRunWizard.MaybeShowAsync();
			}
			if (!Smoke.Active)
			{
				await FeedbackConsent.MaybeAskAsync();
			}
			_ = WarmUpAsync();
			if (!Smoke.Active)
			{
				Run(AutoCheckUpdateAsync);
			}
		}
		catch (Exception ex)
		{
			SetBridgeState(I18n.L("未连接"), "B.Danger");
			_bridgeSettled.TrySetResult(null);
			if (Smoke.Active)
			{
				Smoke.Note("bridge", ex.ToString());
				return;
			}
			if (!interactive)
			{
				BridgePill.ToolTip = ex.Message;
				return;
			}
			if (await Dlg.Confirm(I18n.L("连接后端失败"), ex.Message + I18n.L("\n\n可以点「重试」；如果反复出现，请关掉所有 TipCL 窗口后再打开。"), I18n.L("重试"), I18n.L("退出")))
			{
				await ConnectAsync();
			}
			else
			{
				Close();
			}
		}
	}

	private async Task SyncAppearanceFromBridgeAsync()
	{
		bool? pendingDark = _pendingDark;
		if (pendingDark.HasValue)
		{
			bool valueOrDefault = pendingDark == true;
			_pendingDark = null;
			await PushDarkAsync(valueOrDefault);
		}
		Dictionary<string, JsonElement> dictionary = await AppServices.Client.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
		if (dictionary != null)
		{
			bool flag = App.IsDark;
			JsonElement value;
			bool flag2 = dictionary.TryGetValue("ui_dark", out value);
			if (flag2)
			{
				flag2 = (int)(value.ValueKind - 5) <= 1;
			}
			if (flag2)
			{
				flag = value.GetBoolean();
			}
			string color = null;
			if (dictionary.TryGetValue("theme_color", out var value2) && value2.ValueKind == JsonValueKind.String)
			{
				color = value2.GetString();
			}
			string preset = null;
			if (dictionary.TryGetValue("ui_bg_preset", out var value3) && value3.ValueKind == JsonValueKind.String)
			{
				preset = value3.GetString();
			}
			bool flag3 = SettingsPage.SyncPersistedThemeColor(color);
			bool flag4 = SettingsPage.SyncPersistedBackground(preset);
			bool num = flag != App.IsDark;
			if (num)
			{
				ApplyThemeState(flag, persistLocal: false);
			}
			else if (flag3 || flag4)
			{
				SettingsPage.ApplyVisuals();
			}
			SyncThemeIndicator();
			if (num)
			{
				SaveUiPrefs();
			}
		}
	}

	private void DetachHost()
	{
		BridgeHost host = AppServices.Host;
		if (host != null)
		{
			try
			{
				host.Client.EventReceived -= OnBridgeEvent;
			}
			catch
			{
			}
			try
			{
				host.Dispose();
			}
			catch
			{
			}
			AppServices.Host = null;
			_bridgeReady = false;
		}
	}

	private async Task ReconnectAfterDeathAsync()
	{
		if (_reconnecting || _drained)
		{
			return;
		}
		_reconnecting = true;
		try
		{
			await Task.Delay(400);
			await ConnectAsync(interactive: false);
		}
		catch (Exception ex)
		{
			SetBridgeState(I18n.L("未连接"), "B.Danger");
			BridgePill.ToolTip = ex.Message;
		}
		finally
		{
			_reconnecting = false;
		}
	}

	private async Task ReconcileTasksAsync()
	{
		if (!_bridgeReady)
		{
			return;
		}
		JsonElement jsonElement = await AppServices.Client.TryCallAsync<JsonElement>("list_tasks");
		if (jsonElement.ValueKind != JsonValueKind.Object)
		{
			return;
		}
		List<(string, string)> list = new List<(string, string)>();
		if (jsonElement.TryGetProperty("running", out var value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item4 in value.EnumerateArray())
			{
				JsonElement value2;
				string text = (item4.TryGetProperty("task_id", out value2) ? (value2.GetString() ?? "") : "");
				JsonElement value3;
				string item = (item4.TryGetProperty("title", out value3) ? (value3.GetString() ?? "") : "");
				if (text.Length > 0)
				{
					list.Add((text, item));
				}
			}
		}
		List<(string, bool, string)> list2 = new List<(string, bool, string)>();
		if (jsonElement.TryGetProperty("finished", out var value4) && value4.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item5 in value4.EnumerateArray())
			{
				JsonElement value5;
				string text2 = (item5.TryGetProperty("task_id", out value5) ? (value5.GetString() ?? "") : "");
				if (text2.Length != 0)
				{
					JsonElement value6;
					bool item2 = item5.TryGetProperty("success", out value6) && value6.ValueKind == JsonValueKind.True;
					JsonElement value7;
					string item3 = (item5.TryGetProperty("message", out value7) ? (value7.GetString() ?? "") : "");
					list2.Add((text2, item2, item3));
				}
			}
		}
		if (TaskStore.Reconcile(list, list2))
		{
			SyncTasks();
			SyncDock();
		}
	}

	private async Task WarmUpAsync()
	{
		await Task.Delay(900);
		string[] array = new string[2] { "instance", "tasks" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (!(_currentId == text))
			{
				PageBase page = GetPage(text);
				if (page != null)
				{
					await page.EnsureLoadedAsync();
				}
				await Task.Delay(140);
			}
		}
	}

	// 标题栏不再常驻「C 桥」：只在断开时 Logo 右上角亮红点，详情放 Logo 的 ToolTip
	private void SetBridgeState(string text, string brushKey)
	{
		BridgePill.ToolTip = text;
		BridgeDot.Visibility = ((brushKey == "B.Danger" || (brushKey == "B.Warn" && _bridgeReady)) ? Visibility.Visible : Visibility.Collapsed);
	}

	// 标题栏账号：当前账号（AccountGate 把 active 排第一）的首字母圆 + 名称
	internal async Task SyncAccountChipAsync()
	{
		string name = (await AccountGate.ListAsync()).FirstOrDefault() ?? I18n.L("未登录");
		AccName.Text = name;
		AccInitial.Text = name.Substring(0, 1).ToUpperInvariant();
		System.Windows.Automation.AutomationProperties.SetName(AccBtn, name);
	}

	private static string? _stagedUpdate;

	private static bool _autoUpdateChecked;

	/// <summary>启动后按「自动检查更新」设置静默检查一次，有新版就问一句。</summary>
	private async Task AutoCheckUpdateAsync()
	{
		if (_autoUpdateChecked)
		{
			return;
		}
		_autoUpdateChecked = true;
		try
		{
			if (!TipCL.Core.Py.Truthy(TipCL.Core.Launcher.Config.CONFIG.Get("auto_check_update", true)))
			{
				return;
			}
			await Task.Delay(3000);
			UpdateInfo info = await AppServices.Client.TryCallAsync<UpdateInfo>("check_update");
			if (info == null || !info.HasUpdate)
			{
				return;
			}
			string body = (I18n.L("TipCL {0} 已发布。", info.Version) + "\n" + info.Notes).Trim();
			if (await Dlg.Confirm(I18n.L("发现新版本"), body + I18n.L("\n\n现在下载更新？下载完会提示你重启。"), I18n.L("下载更新"), I18n.L("以后再说")))
			{
				await AppServices.Client.StartTaskAsync("start_self_update");
			}
		}
		catch (Exception)
		{
		}
	}

	private async Task OfferRestartForUpdateAsync(BridgeEvent ev)
	{
		string package = "";
		string version = "";
		try
		{
			if (ev.Payload.ValueKind == JsonValueKind.Object)
			{
				if (ev.Payload.TryGetProperty("package", out JsonElement p))
				{
					package = p.GetString() ?? "";
				}
				if (ev.Payload.TryGetProperty("version", out JsonElement v))
				{
					version = v.ToString();
				}
			}
		}
		catch
		{
		}
		if (package.Length == 0)
		{
			return;
		}
		_stagedUpdate = package;
		if (await Dlg.Confirm(I18n.L("更新已下载"), I18n.L("重启 TipCL 即可更新到 {0}。", version), I18n.L("立即重启"), I18n.L("关闭时再装")))
		{
			try
			{
				TipCL.Core.Launcher.Updater.ApplyStaged(package, restart: true);
				_stagedUpdate = null;
				Application.Current.Shutdown();
			}
			catch (Exception ex)
			{
				Toast(I18n.L("更新失败"), ex.Message, ToastKind.Error);
			}
		}
		else
		{
			Toast(I18n.L("更新已就绪"), I18n.L("关闭启动器时会自动装好，下次打开就是新版"), ToastKind.Success);
		}
	}

	private void OnBridgeEvent(object? sender, BridgeEvent ev)
	{
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
			{
				OnBridgeEvent(sender, ev);
			});
			return;
		}
		TaskStore.Handle(ev);
		if (ev.Event.StartsWith("plugins.", StringComparison.Ordinal))
		{
			TipCL.Pages.Plugins.PluginEvents.Handle(ev);
		}
		switch (ev.Event)
		{
		case "update_staged":
			Run(() => OfferRestartForUpdateAsync(ev));
			break;
		case "task_count_changed":
			SyncTasks();
			break;
		case "ui_changed":
			ScheduleRefresh();
			break;
		case "finished":
			if (!ev.Success)
			{
				string message = ev.Message;
				if (message != null && message.Length > 0 && ev.Message != "已取消")
				{
					Toast(TaskStore.Get(ev.TaskId)?.Title ?? I18n.L("任务失败"), ev.Message, ToastKind.Error);
					break;
				}
			}
			if (ev.Success)
			{
				TaskRow taskRow = TaskStore.Get(ev.TaskId);
				if (taskRow != null && !taskRow.Title.StartsWith("启动游戏", StringComparison.Ordinal))
				{
					Toast(taskRow.Title, string.IsNullOrEmpty(ev.Message) ? I18n.L("已完成") : ev.Message, ToastKind.Success);
				}
			}
			break;
		}
		foreach (PageBase value in _pages.Values)
		{
			value.OnEvent(ev);
		}
		SyncDock();
	}

	private void ScheduleRefresh()
	{
		if (_refreshTimer == null)
		{
			_refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromMilliseconds(420.0)
			};
		}
		_refreshTimer.Stop();
		_refreshTimer.Tick -= RefreshTick;
		_refreshTimer.Tick += RefreshTick;
		_refreshTimer.Start();
	}

	private void RefreshTick(object? sender, EventArgs e)
	{
		_refreshTimer?.Stop();
		PageBase current = _current;
		if (current != null)
		{
			Run(current.RefreshAsync);
		}
	}

	private async Task ReloadCurrentAsync()
	{
		if (_current != null)
		{
			await _current.EnsureLoadedAsync();
			_current.OnShown();
		}
	}

	public void Navigate(string id, bool instant = false)
	{
		if (_currentId == id)
		{
			return;
		}
		PageBase page = GetPage(id);
		if (page == null)
		{
			return;
		}
		bool forward = NavOrderIndex(id) >= NavOrderIndex(_currentId);
		if (_currentId == "account" && _bridgeReady)
		{
			Run(SyncAccountChipAsync);
		}
		_current?.OnHidden();
		_currentId = id;
		_current = page;
		SyncTabs();
		if (instant || !Motion.Enabled)
		{
			PageHost.Content = page;
		}
		else
		{
			Motion.PageSwap(PageHost, page, forward);
		}
		SyncDock();
		if (_bridgeReady)
		{
			Run(async delegate
			{
				await page.EnsureLoadedAsync();
				page.OnShown();
			});
		}
	}

	private static void Run(Func<Task> work)
	{
		PageBase.Run(work, I18n.L("出错"));
	}

	public PageBase? GetPage(string id)
	{
		if (_pages.TryGetValue(id, out PageBase value))
		{
			return value;
		}
		if (id == "design-sheet")
		{
			// dev-only 样张页，不在导航注册
			value = new DesignSheetPage
			{
				Id = id
			};
			_pages[id] = value;
			return value;
		}
		NavDef navDef = NavDefs.FirstOrDefault((NavDef d) => d.Id == id);
		if ((object)navDef == null)
		{
			value = CreatePluginPage(id);
			if (value != null)
			{
				_pages[id] = value;
			}
			return value;
		}
		value = navDef.Create();
		value.Id = id;
		_pages[id] = value;
		return value;
	}

	private void ToggleMax()
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	// 窗口圆角 8：Win11 (build≥22000) 走 DWMWA_WINDOW_CORNER_PREFERENCE=33（Backdrop.TryRoundWin11）；
	// Win10：Backdrop 的亚克力铺满整个矩形 HWND，只裁 WPF 内容挡不住四角 → 再用 SetWindowRgn 把 HWND 本身裁成圆角，
	// 按 DPI 换算、尺寸变化时重设；最大化清掉区域（直角）。Win7 直角。
	private double _rootRadius = -1.0;

	private int _rgnW = -1;

	private int _rgnH = -1;

	private int _rgnR = -1;

	// WP-W：拖拽/缩放中不重设窗口区域（防闪烁），WM_EXITSIZEMOVE 结束时补设
	private static bool _inSizeMove;

	private static MainWindow? _wnd;

	private void ApplyWindowCorner()
	{
		try
		{
			_wnd = this;
			IntPtr handle = new WindowInteropHelper(this).Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}
			_rootRadius = Math.Max(0.0, Backdrop.RootCornerRadius);
			if (Environment.OSVersion.Version.Build >= 22000)
			{
				Backdrop.TryRoundWin11(handle);
			}
			RootBorder.SizeChanged += delegate
			{
				ApplyRootCorner();
			};
			ApplyRootCorner();
		}
		catch
		{
		}
	}

	private void ApplyRootCorner()
	{
		if (_rootRadius < 0.0 || RootBorder == null)
		{
			return;
		}
		bool maximized = base.WindowState == WindowState.Maximized;
		double r = (maximized ? 0.0 : _rootRadius);
		RootBorder.CornerRadius = new CornerRadius(r);
		if (RootBorder.Child is UIElement el)
		{
			double w = el.RenderSize.Width;
			double h = el.RenderSize.Height;
			el.Clip = ((r > 0.0 && w > 0.0 && h > 0.0) ? new RectangleGeometry(new Rect(0.0, 0.0, w, h), r, r) : null);
		}
		if (_rootRadius <= 0.0)
		{
			return;
		}
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rc))
		{
			return;
		}
		int rw = rc.right - rc.left;
		int rh = rc.bottom - rc.top;
		int rr = (int)Math.Round(r * VisualTreeHelper.GetDpi(this).DpiScaleX);
		if (rw != _rgnW || rh != _rgnH || rr != _rgnR)
		{
			if (_inSizeMove)
			{
				// 拖拽/缩放中区域保持旧值（缓存字段不更新），结束补设
				return;
			}
			_rgnW = rw;
			_rgnH = rh;
			_rgnR = rr;
			Backdrop.SetRoundRegion(hwnd, rw, rh, rr);
		}
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

	private const string GeoMax = "M2,0.5 H8 A1.5,1.5 0 0 1 9.5,2 V8 A1.5,1.5 0 0 1 8,9.5 H2 A1.5,1.5 0 0 1 0.5,8 V2 A1.5,1.5 0 0 1 2,0.5 Z";

	private const string GeoRestore = "M1.7,2.5 H6.3 A1.2,1.2 0 0 1 7.5,3.7 V8.3 A1.2,1.2 0 0 1 6.3,9.5 H1.7 A1.2,1.2 0 0 1 0.5,8.3 V3.7 A1.2,1.2 0 0 1 1.7,2.5 Z M2.5,2.5 V1.7 A1.2,1.2 0 0 1 3.7,0.5 H8.3 A1.2,1.2 0 0 1 9.5,1.7 V6.3 A1.2,1.2 0 0 1 8.3,7.5 H7.5";

	private const string GeoMoon = "M13,9.5 A5.5,5.5 0 0 1 6.5,3 A5.5,5.5 0 1 0 13,9.5 Z";

	private const string GeoSun = "M5,8 A3,3 0 1 0 11,8 A3,3 0 1 0 5,8 Z M8,1.5 V2.8 M8,13.2 V14.5 M1.5,8 H2.8 M13.2,8 H14.5 M3.4,3.4 L4.3,4.3 M11.7,11.7 L12.6,12.6 M3.4,12.6 L4.3,11.7 M11.7,4.3 L12.6,3.4";

	private void SyncMaxGlyph()
	{
		bool flag = base.WindowState == WindowState.Maximized;
		MaxIco.Data = Geometry.Parse(flag ? GeoRestore : GeoMax);
		// WP-S12：壁纸激活时 RootBorder 自身描边归零（细边交给 DWMWA_BORDER_COLOR，见 Backdrop.Apply），防双线
		RootBorder.BorderThickness = new Thickness(((!flag && !Wallpaper.Active)) ? 1 : 0);
		ApplyRootCorner();
	}

	// 深色时显示太阳（点了变浅），浅色时显示月亮
	internal void SyncThemeIndicator()
	{
		ThemeIco.Data = Geometry.Parse(App.IsDark ? GeoSun : GeoMoon);
		System.Windows.Automation.AutomationProperties.SetName(ThemeBtn, I18n.L("切换深色 / 浅色"));
	}

	internal void ApplyThemeState(bool dark, bool persistLocal = true)
	{
		App.ApplyTheme(dark);
		Backdrop.Refresh(this);
		SyncThemeIndicator();
		if (persistLocal)
		{
			SaveUiPrefs();
		}
	}

	internal void SetDark(bool dark, bool persistBackend = true)
	{
		if (dark != App.IsDark)
		{
			ApplyThemeState(dark);
		}
		else
		{
			SyncThemeIndicator();
		}
		if (persistBackend)
		{
			Run(() => PushDarkAsync(dark));
		}
	}

	private async Task PushDarkAsync(bool dark)
	{
		if (!_bridgeReady)
		{
			_pendingDark = dark;
			return;
		}
		try
		{
			await AppServices.Client.CallAsync<object>("save_settings", new
			{
				data = new Dictionary<string, object> { ["ui_dark"] = dark }
			});
		}
		catch (Exception ex)
		{
			Toast(I18n.L("深浅色没能保存到后端"), ex.Message, ToastKind.Warning);
		}
	}

	private void ToggleTheme()
	{
		SetDark(!App.IsDark);
		Motion.Pulse(ThemeBtn, 1.14);
	}

	// 默认 1180×740 在 1366×768（Win7 笔记本常见）上减掉任务栏就放不下：按工作区最多占 92%
	private void FitToWorkArea()
	{
		Rect work = SystemParameters.WorkArea;
		if (work.Width <= 0.0 || work.Height <= 0.0)
		{
			return;
		}
		base.MinWidth = Math.Min(base.MinWidth, Math.Floor(work.Width * 0.92));
		base.MinHeight = Math.Min(base.MinHeight, Math.Floor(work.Height * 0.92));
		base.Width = Math.Max(base.MinWidth, Math.Min(base.Width, Math.Floor(work.Width * 0.92)));
		base.Height = Math.Max(base.MinHeight, Math.Min(base.Height, Math.Floor(work.Height * 0.92)));
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
		SyncMaxGlyph();
	}

	private void OnShortcut(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.F5)
		{
			if (_current != null)
			{
				Run(async delegate
				{
					await _current.RefreshAsync();
				});
			}
			e.Handled = true;
		}
		else
		{
			if (Keyboard.Modifiers != ModifierKeys.Control)
			{
				return;
			}
			int num = e.Key switch
			{
				Key.D1 => 0, 
				Key.D2 => 1, 
				Key.D3 => 2, 
				Key.D4 => 3, 
				Key.D5 => 4, 
				Key.D6 => 5, 
				Key.D7 => 6, 
				Key.D8 => 7, 
				Key.D9 => 8, 
				_ => -1, 
			};
			if (num >= 0)
			{
				List<string> list = VisibleFlatOrder();
				if (num < list.Count)
				{
					Navigate(list[num]);
				}
				e.Handled = true;
			}
			else if (e.Key == Key.OemComma)
			{
				Navigate("settings");
				e.Handled = true;
			}
			else if (e.Key == Key.K)
			{
				// Ctrl+K → AI 页；对话框打开或文本框（含密码框）聚焦时不抢键
				DependencyObject focus = Keyboard.FocusedElement as DependencyObject;
				if (DialogHost.Visibility != Visibility.Visible && !(focus is TextBoxBase) && !(focus is PasswordBox))
				{
					Navigate("ai");
					e.Handled = true;
				}
			}
		}
	}

	private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == 0x0231)
		{
			_inSizeMove = true;
		}
		if (msg == 0x0232)
		{
			_inSizeMove = false;
			_wnd?.ApplyRootCorner();
		}
		if (msg == 561)
		{
			Backdrop.PmSetMoving(hwnd, moving: true);
		}
		if (msg == 562)
		{
			Backdrop.PmSetMoving(hwnd, moving: false);
		}
		if (msg != 36)
		{
			return IntPtr.Zero;
		}
		MINMAXINFO structure = Marshal.PtrToStructure<MINMAXINFO>(lParam);
		IntPtr intPtr = MonitorFromWindow(hwnd, 2);
		if (intPtr != IntPtr.Zero)
		{
			MONITORINFO info = new MONITORINFO
			{
				cbSize = Marshal.SizeOf<MONITORINFO>()
			};
			if (GetMonitorInfo(intPtr, ref info))
			{
				RECT rcWork = info.rcWork;
				RECT rcMonitor = info.rcMonitor;
				structure.ptMaxPosition.x = Math.Abs(rcWork.left - rcMonitor.left);
				structure.ptMaxPosition.y = Math.Abs(rcWork.top - rcMonitor.top);
				structure.ptMaxSize.x = Math.Abs(rcWork.right - rcWork.left);
				structure.ptMaxSize.y = Math.Abs(rcWork.bottom - rcWork.top);
				// 最小尺寸以 XAML 的 MinWidth/MinHeight 为唯一真相（FitToWorkArea 会按工作区钳制），
				// 不再硬编码 900×580——否则默认 900×550 打开后拖不回原高、最大化还原被抬高。
				structure.ptMinTrackSize.x = (int)Math.Ceiling(_wnd?.MinWidth ?? 720.0);
				structure.ptMinTrackSize.y = (int)Math.Ceiling(_wnd?.MinHeight ?? 480.0);
				Marshal.StructureToPtr(structure, lParam, fDeleteOld: true);
				handled = true;
			}
		}
		return IntPtr.Zero;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

	private void ShowDropVeil(bool on)
	{
		if (!_bridgeReady)
		{
			on = false;
		}
		if (on == (DropVeil.Visibility == Visibility.Visible))
		{
			return;
		}
		if (on)
		{
			DropVeil.Visibility = Visibility.Visible;
			Motion.Fade(DropVeil, 1.0, 120.0);
		}
		else
		{
			Motion.FadeOut(DropVeil, 110.0, delegate
			{
				DropVeil.Visibility = Visibility.Collapsed;
			});
		}
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = ((_bridgeReady && e.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
		e.Handled = true;
	}

	private void OnDrop(object sender, DragEventArgs e)
	{
		e.Handled = true;
		if (!_bridgeReady || !e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			return;
		}
		object data = e.Data.GetData(DataFormats.FileDrop);
		string[] paths = data as string[];
		if (paths != null && paths.Length != 0)
		{
			Activate();
			Run(() => FileDrop.HandleAsync(this, paths));
		}
	}

	public void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
	{
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				Toast(title, body, kind);
			});
			return;
		}
		while (ToastHost.Children.Count >= 4)
		{
			ToastHost.Children.RemoveAt(0);
		}
		// 反编译产物原先把元组装箱后用指针读取，读到的是引用本身，导致 Glyph 收到 "(, B.Danger)" 而崩溃
		var (item, item2) = kind switch
		{
			ToastKind.Success => ("\uec61", "B.Ok"), 
			ToastKind.Warning => ("\ue7ba", "B.Warn"), 
			ToastKind.Error => ("\uea39", "B.Danger"), 
			_ => ("\ue946", "B.Info"), 
		};
		Border border = new Border
		{
			Width = 3.0,
			CornerRadius = new CornerRadius(2.0)
		};
		border.SetResourceReference(Border.BackgroundProperty, item2);
		SPanel sPanel = Ui.V(2.0, Ui.Txt(title, 13.0, bold: true).Wrap(), string.IsNullOrWhiteSpace(body) ? null : Ui.Muted((body.Length > 220) ? (body.Substring(0, 220) + "…") : body));
		sPanel.MaxWidth = 300.0;
		Border card = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(12.0, 10.0, 14.0, 11.0),
			BorderThickness = new Thickness(1.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			Cursor = Cursors.Hand,
			Child = Ui.H(10.0, border, Ui.Glyph(item, 15.0, item2), sPanel)
		};
		card.SetResourceReference(Border.BackgroundProperty, "B.GlassStrong");
		card.SetResourceReference(Border.BorderBrushProperty, "B.GlassBorder");
		Motion.Shadow(card, 22.0, 0.14, 4.0);
		ToastHost.Children.Add(card);
		Motion.SlideIn(card);
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds((kind == ToastKind.Error) ? 7 : 4)
		};
		timer.Tick += delegate
		{
			Kill();
		};
		timer.Start();
		card.MouseLeftButtonUp += delegate
		{
			Kill();
		};
		card.MouseEnter += delegate
		{
			timer.Stop();
		};
		card.MouseLeave += delegate
		{
			timer.Start();
		};
		void Kill()
		{
			timer.Stop();
			if (ToastHost.Children.Contains(card))
			{
				Motion.SlideOut(card, delegate
				{
					ToastHost.Children.Remove(card);
				});
			}
		}
	}

	private void BuildDock()
	{
		_dockTitle = Ui.Txt("", 12.5, bold: true).Trim();
		_dockStatus = Ui.Small("");
		_dockBar = Ui.Prog();
		_dockBar.Height = 4.0;
		Button button = Ui.Btn(I18n.L("查看"), BtnKind.Soft, delegate
		{
			Navigate("tasks");
		});
		button.Padding = new Thickness(11.0, 4.0, 11.0, 5.0);
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(Ui.Glyph("\ue896", 15.0, "B.Accent").M(0.0, 0.0, 10.0), 0);
		grid.Add(Ui.V(3.0, _dockTitle, _dockStatus, _dockBar).VCenter(), 0, 1);
		grid.Add(button.M(12.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		_dockCard = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(14.0, 10.0, 12.0, 11.0),
			BorderThickness = new Thickness(1.0),
			Child = grid
		};
		_dockCard.SetResourceReference(Border.BackgroundProperty, "B.GlassStrong");
		_dockCard.SetResourceReference(Border.BorderBrushProperty, "B.GlassBorder");
		Motion.Shadow(_dockCard, 26.0, 0.14, 5.0);
		DockHost.Child = _dockCard;
	}

	private void SyncDock()
	{
		TaskRow taskRow = TaskStore.Rows.LastOrDefault((TaskRow r) => !r.Finished && IsDownloadTitle(r.Title));
		if (taskRow == null || !(_currentId != "tasks"))
		{
			if (DockHost.Visibility == Visibility.Visible)
			{
				Motion.FadeOut(DockHost, 140.0, delegate
				{
					DockHost.Visibility = Visibility.Collapsed;
				});
			}
			return;
		}
		if (DockHost.Visibility != Visibility.Visible)
		{
			DockHost.Visibility = Visibility.Visible;
			Motion.FadeIn(DockHost, 220.0, 14.0);
		}
		_dockTitle.Text = taskRow.Title;
		_dockStatus.Text = (string.IsNullOrEmpty(taskRow.Speed) ? taskRow.Status : (taskRow.Status + "    " + taskRow.Speed));
		_dockBar.IsIndeterminate = taskRow.Indeterminate;
		if (!taskRow.Indeterminate)
		{
			Motion.Progress(_dockBar, taskRow.Progress);
		}
	}

	private bool IsDownloadTitle(string title)
	{
		if (string.IsNullOrEmpty(title))
		{
			return true;
		}
		if (_downloadTitle.TryGetValue(title, out var value))
		{
			return value;
		}
		_downloadTitle[title] = true;
		Run(async delegate
		{
			bool flag = await AppServices.Client.TryCallAsync("is_download_title", new { title }, fallback: true);
			if (!_downloadTitle.TryGetValue(title, out var value2) || value2 != flag)
			{
				_downloadTitle[title] = flag;
				SyncDock();
			}
		});
		return true;
	}

	public void FlyToTasks(FrameworkElement source, string text = "", string? colorKey = null)
	{
		if (!Motion.Enabled || !base.IsLoaded)
		{
			return;
		}
		ToggleButton target = TabTasks;
		if (target == null || !source.IsVisible || !target.IsVisible)
		{
			return;
		}
		Point point;
		Point point2;
		try
		{
			point = source.TransformToAncestor(this).Transform(new Point(source.ActualWidth / 2.0, source.ActualHeight / 2.0));
			point2 = target.TransformToAncestor(this).Transform(new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0));
		}
		catch
		{
			return;
		}
		Border chip = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 5.0),
			Child = Ui.Txt(string.IsNullOrEmpty(text) ? I18n.L("已加入队列") : text, 11.5, bold: true, "B.OnAccentStrong")
		};
		chip.SetResourceReference(Border.BackgroundProperty, colorKey ?? "B.AccentStrong");
		Motion.Shadow(chip, 16.0, 0.3, 3.0);
		FlyHost.Children.Add(chip);
		chip.Measure(new Size(500.0, 60.0));
		double width = chip.DesiredSize.Width;
		double height = chip.DesiredSize.Height;
		Canvas.SetLeft(chip, point.X - width / 2.0);
		Canvas.SetTop(chip, point.Y - height / 2.0);
		TranslateTransform translateTransform = new TranslateTransform();
		ScaleTransform scaleTransform = new ScaleTransform(0.7, 0.7);
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scaleTransform);
		transformGroup.Children.Add(translateTransform);
		chip.RenderTransform = transformGroup;
		chip.RenderTransformOrigin = new Point(0.5, 0.5);
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(620.0);
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		DoubleAnimation animation = new DoubleAnimation(0.0, point2.X - point.X, timeSpan)
		{
			EasingFunction = easingFunction
		};
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
		{
			Duration = timeSpan
		};
		double num = point2.Y - point.Y;
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(num * 0.28 - 46.0, KeyTime.FromPercent(0.45), new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		}));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(num, KeyTime.FromPercent(1.0), new CubicEase
		{
			EasingMode = EasingMode.EaseIn
		}));
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames2 = new DoubleAnimationUsingKeyFrames
		{
			Duration = timeSpan
		};
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.15)));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.72)));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.7, 0.42, timeSpan)
		{
			EasingFunction = easingFunction
		};
		doubleAnimation.Completed += delegate
		{
			FlyHost.Children.Remove(chip);
			if (target != null)
			{
				Motion.Pulse(target);
			}
		};
		translateTransform.BeginAnimation(TranslateTransform.XProperty, animation);
		translateTransform.BeginAnimation(TranslateTransform.YProperty, doubleAnimationUsingKeyFrames);
		chip.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames2);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, doubleAnimation);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimation.Clone());
	}

	private void LoadUiPrefs()
	{
		try
		{
			_prefs = (File.Exists(PrefsPath) ? (JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(PrefsPath)) ?? new UiPrefs()) : new UiPrefs());
		}
		catch
		{
			_prefs = new UiPrefs();
		}
		Motion.Enabled = _prefs.Anim;
		Motion.Scale = Clamp.Of(_prefs.AnimScale, 0.4, 2.2);
		ApplyThemeState(_prefs.Dark, persistLocal: false);
	}

	public void SaveUiPrefs()
	{
		_prefs.Dark = App.IsDark;
		_prefs.Anim = Motion.UserEnabled;
		_prefs.AnimScale = Motion.Scale;
		try
		{
			File.WriteAllText(PrefsPath, JsonSerializer.Serialize(_prefs, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch
		{
		}
	}

	public async Task LoadNavFromBridgeAsync()
	{
		JsonElement settings;
		try
		{
			settings = await AppServices.Client.CallAsync("get_settings");
		}
		catch (Exception ex)
		{
			Toast(I18n.L("侧栏设置读取失败"), ex.Message, ToastKind.Warning);
			return;
		}
		ApplyNavSettings(settings);
	}

	public void ApplyNavSettings(JsonElement settings)
	{
		_navCfg = TipCL.NavConfig.FromJson(settings);
	}

	private static string? PageOf(string navKey)
	{
		if (!NavModel.PageForNavKey.TryGetValue(navKey, out string value))
		{
			return null;
		}
		return value;
	}

	private List<string> VisibleFlatOrder()
	{
		List<string> list = new List<string>();
		Dictionary<string, List<string>> dictionary = NavModel.SectionMembersFromConfig(_navCfg);
		foreach (NavEntry item in NavModel.NavItemsFromConfig(_navCfg))
		{
			if (item.Kind != NavEntryKind.Item)
			{
				continue;
			}
			if (NavModel.IsSection(item.Key))
			{
				foreach (string item2 in dictionary[item.Key])
				{
					string text = PageOf(item2);
					if (text != null && !list.Contains(text))
					{
						list.Add(text);
					}
				}
			}
			else
			{
				string text2 = PageOf(item.Key);
				if (text2 != null && !list.Contains(text2))
				{
					list.Add(text2);
				}
			}
		}
		return list;
	}

	private int NavOrderIndex(string pageId)
	{
		int num = VisibleFlatOrder().IndexOf(pageId);
		if (num >= 0)
		{
			return num;
		}
		return 99;
	}

	// 顶栏 5 页签 + 插件动态页签的激活态与当前页联动（其余高亮全部清掉）
	private void SyncTabs()
	{
		TabLaunch.IsChecked = (_currentId == "launch");
		TabLibrary.IsChecked = (_currentId == "library");
		TabTasks.IsChecked = (_currentId == "download" || _currentId == "tasks");
		TabPlugins.IsChecked = (_currentId == "plugins");
		TabSettings.IsChecked = (_currentId == "settings" || _currentId == "account");
		foreach (ToggleButton t in _pluginTabs)
		{
			t.IsChecked = ((string)t.Tag == _currentId);
		}
	}

	// 任务数变化只需刷新底部任务条（侧栏角标已随离屏侧栏删除）
	private void SyncTasks()
	{
		SyncDock();
	}

}
