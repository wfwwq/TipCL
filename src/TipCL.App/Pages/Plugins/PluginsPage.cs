using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b(批4 块1):Windows 版 <c>Pages\Plugins\PluginsPage.cs</c>(8891 字节 / 365 行 / WPF / net48)+ 它的
/// <c>.Installed</c> / <c>.Market</c> / <c>.Dev</c> / <c>.Settings</c> 四个 partial 到 Avalonia 11 / net8 的移植 ——
/// 导航 id <c>plugins</c> 的「插件」页:顶部页签「已安装 / 市场」,开发者与插件设置收在右上 ⋯ 菜单;
/// 插件设置是本页里的**子视图**(不是对话框)。
///
/// 本文件是**主骨架**(页签 / 切换 / 导航 / 事件 / 任务进度);四个 partial 各自一段内容。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Style = Ui.S("Seg.Rail"/"Seg.Item")</c> → <c>StyleKeys.Apply(control, key)</c>(mac 门面是"把样式键落上去",不是塞 <c>Style</c> 对象);</item>
/// <item><c>ContextMenu.PlacementTarget + IsOpen = true</c> → <c>ContextMenu.Open(anchor)</c>(**D-124**:WPF 那套赋值等价物在 mac 是 <c>Open</c>);</item>
/// <item><b>拖放安装</b>(Windows 的 <c>AllowDrop</c> + <c>PreviewDragOver/PreviewDrop</c> 收 zip / 目录):
///   mac 的拖放由 <c>FileDrop</c> 那条缝统一驱动(C2c2),本块**暂不接** —— 留一条明确的
///   **临时缺口注释**(不是假装能用);接法确认后补;</item>
/// <item><c>OpenFolder</c> 里 Windows 的 <c>Process.Start("explorer.exe")</c> → **复用 B1b 的
///   <c>Crash.ShellOpen</c>**(它自己认平台分支;与 D-147 同一处,**不再写第二套**);</item>
/// <item><c>PluginWebView _subWeb</c>(插件自定义设置页用的内嵌浏览器)→ 按 **GOAL.md 第 34 行** mac 不嵌 WebView,
///   所以这里**没有可释放的浏览器**:<c>DisposeSubView()</c> 如实留空并注明(插件的自定义设置页走既有
///   <see cref="NativePluginUi.Content"/> 那张如实内容卡 + 「在浏览器中打开」);</item>
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;<c>UniformGrid</c> 两平台同名
///   (Avalonia 在 <c>Avalonia.Controls.Primitives</c> 里);<c>Ui.H2</c> / <c>PageBase.ScrollBody</c> 照用。</item>
/// </list>
/// </summary>
public sealed partial class PluginsPage : PageBase, IPluginHostPage
{
	/// <summary>页签(三条)。**internal 而非 private**:<c>SelectTab</c> 是 internal(测试要驱动"切页签"这个
	/// 内部处理器体,而不是模拟点击 —— D-115/D-127 的口径),C# 不许外部可见的方法签名里出现更私有的类型。</summary>
	internal enum Tab
	{
		Installed,
		Market,
		Dev,
	}

	private readonly ToggleButton _tabInstalled;

	private readonly ToggleButton _tabMarket;

	private readonly ContentControl _body = new ContentControl
	{
		Focusable = false,
	};

	private Tab _tab;

	private bool _subView;

	private readonly Control _installedView;

	private readonly Dictionary<string, string> _taskPlugin = new Dictionary<string, string>();

	public override string Title => I18n.L("插件");

	public string TabName => _tab.ToString();

	public PluginsPage()
	{
		_tabInstalled = TabBtn(I18n.L("已安装"), Tab.Installed);
		_tabMarket = TabBtn(I18n.L("市场"), Tab.Market);
		Button more = Ui.IconBtn("\ue712", I18n.L("更多"));
		ContextMenu menu = new ContextMenu();
		AddItem(menu, I18n.L("刷新"), "\ue72c", delegate
		{
			PageBase.Run(ReloadAllAsync, I18n.L("刷新失败"));
		});
		AddItem(menu, I18n.L("开发者"), "\uebe8", delegate
		{
			SelectTab(Tab.Dev);
		});
		AddItem(menu, I18n.L("插件设置"), "\ue713", delegate
		{
			ShowSubView(I18n.L("插件设置"), PageBase.ScrollBody(PluginSettingsCard.Build()));
		});
		more.Click += delegate
		{
			// 【D-124】WPF 的 PlacementTarget + IsOpen = true → Avalonia 的 Open(anchor)
			menu.Open(more);
		};
		Grid head = Ui.G(null, "*,Auto");
		head.Margin = new Avalonia.Thickness(22.0, 16.0, 22.0, 12.0);
		Border rail = new Border
		{
			Child = Ui.H(0.0, _tabInstalled, _tabMarket),
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
		};
		StyleKeys.Apply(rail, "Seg.Rail");
		head.Add(rail, 0);
		head.Add(Ui.H(6.0, Ui.Btn(I18n.L("从文件安装"), BtnKind.Normal, delegate
		{
			string? path = Dlg.PickFile(I18n.L("插件包") + " (*.zip)|*.zip", I18n.L("选择插件包"));
			if (path != null)
			{
				PageBase.Run(() => InstallLocalAsync(path, dev: false, null), I18n.L("安装失败"));
			}
		}), more).VCenter(), 0, 1);
		Grid root = Ui.G("Auto,*");
		root.Add(head, 0);
		root.Add(_body, 1);
		Content = root;
		_installedHost = new UniformGrid
		{
			Columns = 3,
			Margin = new Avalonia.Thickness(15.0, 0.0, 15.0, 16.0),
		};
		_installedView = Ui.Scroll(_installedHost);
		_marketHost = Ui.V(10.0);
		BuildMarketChrome();
		BuildDev();
		HookDrop();
		SelectTab(Tab.Installed);
	}

	/// <summary>
	/// 把插件 zip 或文件夹拖进插件页 = 本地安装(Windows 的 <c>HookDrop</c>)。
	///
	/// 【临时缺口 · 批4 块1】mac 的拖放由既有 <c>FileDrop</c> 缝统一驱动(C2c2:页面用 <c>EnableDrop</c> 声明、
	/// 由缝分发);本块先不接 —— 接法确认后在这里补"zip / 目录 ⇒ <see cref="InstallLocalAsync"/>"
	/// (Windows 的判据:后缀 <c>.zip</c>/<c>.tipcl</c>,或 <c>Directory.Exists</c>)。
	/// 这里**不声明接收拖放**、也不假装能装,免得把"拖进去没反应"当成已实现。
	/// </summary>
	private void HookDrop()
	{
	}

	private ToggleButton TabBtn(string text, Tab tab)
	{
		ToggleButton b = new ToggleButton
		{
			Content = text,
		};
		StyleKeys.Apply(b, "Seg.Item");
		b.Click += delegate
		{
			SelectTab(tab);
		};
		return b;
	}

	internal void SelectTab(Tab tab)
	{
		_tab = tab;
		_subView = false;
		_tabInstalled.IsChecked = tab == Tab.Installed;
		_tabMarket.IsChecked = tab == Tab.Market;
		DisposeSubView();
		switch (tab)
		{
		case Tab.Installed:
			_body.Content = _installedView;
			break;
		case Tab.Market:
			_body.Content = _marketView;
			if (!_marketLoaded && AppServices.Ready)
			{
				PageBase.Run(() => LoadMarketAsync(refresh: false), I18n.L("插件市场加载失败"));
			}

			break;
		case Tab.Dev:
			_body.Content = _devView;
			if (AppServices.Ready)
			{
				PageBase.Run(LoadDevAsync, I18n.L("读取失败"));
			}

			break;
		}
	}

	/// <summary>给外部用:直接打开某个标签(设置页的「模型供应商」入口走它 —— 见 <c>IPluginHostPage</c>)。</summary>
	public void ShowTab(string name)
	{
		SelectTab(name switch
		{
			"market" => Tab.Market,
			"dev" => Tab.Dev,
			_ => Tab.Installed,
		});
	}

	protected override async Task LoadAsync()
	{
		await LoadInstalledAsync();
		if (_tab == Tab.Market)
		{
			await LoadMarketAsync(refresh: false);
		}
		else if (_tab == Tab.Dev)
		{
			await LoadDevAsync();
		}
	}

	private async Task ReloadAllAsync()
	{
		await PluginUi.RefreshAsync();
		await LoadInstalledAsync();
		if (_tab == Tab.Market)
		{
			await LoadMarketAsync(refresh: true);
		}
		else if (_tab == Tab.Dev)
		{
			await LoadDevAsync();
		}
	}

	/// <summary>设置里改了加速镜像后强刷市场缓存(忽略 10 分钟 TTL)。
	/// **public 是接口要求**(<see cref="IPluginHostPage"/> 的成员隐含 public;类本身是 internal,不扩大对外可见面)。</summary>
	public void RefreshMarketSoon()
	{
		if (_tab == Tab.Market)
		{
			PageBase.Run(async delegate
			{
				await LoadMarketAsync(refresh: true);
			}, I18n.L("刷新失败"));
		}
	}

	/// <summary>dev-shot 专用:渲染「⋯ → 插件设置」子视图(右键菜单是独立弹层,离屏截图拍不到)。</summary>
	internal void ShowSettingsSubViewForDevShot()
	{
		ShowSubView(I18n.L("插件设置"), PageBase.ScrollBody(PluginSettingsCard.Build()));
	}

	public override void OnEvent(BridgeEvent ev)
	{
		switch (ev.Event)
		{
		case "plugins.changed":
			PageBase.Run(async delegate
			{
				await LoadInstalledAsync();
				if (_marketLoaded)
				{
					await LoadMarketAsync(refresh: false);
				}
			});
			break;
		case "progress":
		case "task_added":
			if (_taskPlugin.TryGetValue(ev.TaskId, out string? plugin))
			{
				SyncProgress(plugin, ev.TaskId);
			}

			break;
		case "finished":
			if (_taskPlugin.TryGetValue(ev.TaskId, out string? plugin2))
			{
				_taskPlugin.Remove(ev.TaskId);
				SyncProgress(plugin2, ev.TaskId, done: true);
				PageBase.Run(async delegate
				{
					await PluginUi.RefreshAsync();
					await LoadInstalledAsync();
					if (_marketLoaded)
					{
						await LoadMarketAsync(refresh: false);
					}
				});
			}

			break;
		}
	}

	/// <summary>启动一个插件后台任务(安装 / 更新 / 本地安装),把任务与插件对上,好在卡片上显示进度。</summary>
	private async Task StartPluginTaskAsync(string method, object args, string plugin, Control? source)
	{
		JsonElement r = await PageBase.Api.CallAsync(method, args);
		string taskId = J.TaskId(r);
		if (taskId.Length > 0)
		{
			_taskPlugin[taskId] = plugin;
			SyncProgress(plugin, taskId);
			if (source != null)
			{
				PageBase.Win?.FlyToTasks(source, I18n.L("已加入队列"));
			}
		}
		else if (J.TryGet(r, "error", out JsonElement err) && err.ValueKind == JsonValueKind.String)
		{
			PageBase.Toast(I18n.L("操作失败"), err.GetString() ?? "", ToastKind.Error);
		}
		else
		{
			await PluginUi.RefreshAsync();
			await LoadInstalledAsync();
		}
	}

	internal static string PluginDataDir(string id)
	{
		string root;
		try
		{
			root = BridgeHost.FindRoot();
		}
		catch
		{
			root = AppContext.BaseDirectory;
		}

		return Path.Combine(root, "plugin-data", id);
	}

	/// <summary>
	/// 打开一个目录(插件数据目录等)。Windows 是 <c>Process.Start("explorer.exe", …)</c>;
	/// mac **复用 B1b 的 <c>Crash.ShellOpen</c>**(Windows→explorer / mac→open / Linux→xdg-open,
	/// 且认 <c>Crash.ShellOpenHook</c> —— 测试靠它拦截),**不写第二套平台分支**(D-147)。
	/// </summary>
	internal static void OpenFolder(string path)
	{
		try
		{
			Directory.CreateDirectory(path);
			TipCL.Core.Launcher.Crash.ShellOpen(path);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("打不开文件夹"), ex.Message, ToastKind.Warning);
		}
	}

	/// <summary>页面内子视图(插件设置):顶部带返回。</summary>
	private void ShowSubView(string title, Control content, Control? right = null)
	{
		DisposeSubView();
		_subView = true;
		Button back = Ui.IconBtn("\ue76b", I18n.L("返回"), delegate
		{
			SelectTab(_tab);
		});
		Grid head = Ui.G(null, "Auto,*,Auto");
		head.Add(back.VCenter(), 0);
		head.Add(Ui.H2(title).VCenter().M(6.0, 0.0, 0.0, 0.0), 0, 1);
		if (right != null)
		{
			head.Add(right, 0, 2);
		}

		head.Margin = new Avalonia.Thickness(18.0, 12.0, 22.0, 8.0);
		Grid g = Ui.G("Auto,*");
		g.Add(head, 0);
		g.Add(content, 1);
		_body.Content = g;
	}

	/// <summary>
	/// 【mac 差异(GOAL.md 第 34 行)】Windows 这里会 <c>_subWeb?.Dispose()</c> —— 那个 <c>PluginWebView</c>
	/// 是"插件自定义设置页"的内嵌浏览器;mac 不嵌 WebView(那张页走 <see cref="NativePluginUi.Content"/>
	/// 的如实内容卡 + 「在浏览器中打开」),**没有可释放的东西** ⇒ 如实留空。
	/// </summary>
	private void DisposeSubView()
	{
	}

	public override void OnHidden()
	{
		// 离开插件页时关掉自定义设置页的浏览器(设置页不需要常驻)。mac 没有内嵌浏览器 ⇒ 回到普通页签即可。
		if (_subView)
		{
			SelectTab(_tab);
		}
	}
}
