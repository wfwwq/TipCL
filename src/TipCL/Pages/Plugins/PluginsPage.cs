using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 顶部页签「插件」：已安装 / 市场 分段；开发者、插件设置（市场源等）收在右上 ⋯ 菜单。
/// 插件设置是本页里的子视图（不是对话框：自定义设置页是 WebView，放进对话层会被 airspace 挡住）。
/// </summary>
public sealed partial class PluginsPage : PageBase
{
	private enum Tab
	{
		Installed,
		Market,
		Dev
	}

	private readonly ToggleButton _tInstalled;

	private readonly ToggleButton _tMarket;

	private readonly ContentControl _body = new ContentControl
	{
		Focusable = false
	};

	private Tab _tab;

	private bool _subView;

	private readonly UIElement _installedView;

	private readonly Dictionary<string, string> _taskPlugin = new Dictionary<string, string>();

	public override string Title => I18n.L("插件");

	public PluginsPage()
	{
		_tInstalled = TabBtn(I18n.L("已安装"), Tab.Installed);
		_tMarket = TabBtn(I18n.L("市场"), Tab.Market);
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
		more.ContextMenu = menu;
		more.Click += delegate
		{
			menu.PlacementTarget = more;
			menu.IsOpen = true;
		};
		Grid head = Ui.G(null, "*,Auto");
		head.Margin = new Thickness(22.0, 16.0, 22.0, 12.0);
		head.Add(new Border
		{
			Style = Ui.S("Seg.Rail"),
			Child = Ui.H(0.0, _tInstalled, _tMarket),
			HorizontalAlignment = HorizontalAlignment.Left
		}, 0);
		head.Add(Ui.H(6.0, Ui.Btn(I18n.L("从文件安装"), BtnKind.Normal, delegate
		{
			string path = Dlg.PickFile(I18n.L("插件包") + " (*.zip)|*.zip", I18n.L("选择插件包"));
			if (path != null)
			{
				PageBase.Run(() => InstallLocalAsync(path, dev: false, null), I18n.L("安装失败"));
			}
		}), more).VCenter(), 0, 1);
		Grid root = Ui.G("Auto,*");
		root.Add(head, 0);
		root.Add(_body, 1);
		base.Content = root;
		_installedHost = new UniformGrid
		{
			Columns = 3,
			Margin = new Thickness(15.0, 0.0, 15.0, 16.0)
		};
		_installedView = Ui.Scroll(_installedHost);
		_marketHost = Ui.V(10.0);
		BuildMarketChrome();
		BuildDev();
		HookDrop();
		SelectTab(Tab.Installed);
	}

	/// <summary>把插件 zip 或文件夹拖进插件页 = 本地安装。</summary>
	private void HookDrop()
	{
		base.AllowDrop = true;
		base.PreviewDragOver += delegate(object _, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
			}
		};
		base.PreviewDrop += delegate(object _, DragEventArgs e)
		{
			if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths) || paths.Length == 0)
			{
				return;
			}
			string path = paths[0];
			bool zip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tipcl", StringComparison.OrdinalIgnoreCase);
			if (zip || Directory.Exists(path))
			{
				e.Handled = true;
				PageBase.Run(() => InstallLocalAsync(path, dev: false, null), I18n.L("安装失败"));
			}
		};
	}

	private ToggleButton TabBtn(string text, Tab tab)
	{
		ToggleButton b = new ToggleButton
		{
			Content = text,
			Style = Ui.S("Seg.Item")
		};
		b.Click += delegate
		{
			SelectTab(tab);
		};
		return b;
	}

	private void SelectTab(Tab tab)
	{
		_tab = tab;
		_subView = false;
		_tInstalled.IsChecked = tab == Tab.Installed;
		_tMarket.IsChecked = tab == Tab.Market;
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

	/// <summary>给外部用：直接打开某个标签（比如设置页里的「模型供应商」入口）。</summary>
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

	/// <summary>设置里改了加速镜像后强刷市场缓存（忽略 10 分钟 TTL）。</summary>
	internal void RefreshMarketSoon()
	{
		if (_tab == Tab.Market)
		{
			PageBase.Run(async delegate
			{
				await LoadMarketAsync(refresh: true);
			}, I18n.L("刷新失败"));
		}
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
			if (_taskPlugin.TryGetValue(ev.TaskId, out string plugin))
			{
				SyncProgress(plugin, ev.TaskId);
			}
			break;
		case "finished":
			if (_taskPlugin.TryGetValue(ev.TaskId, out string plugin2))
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

	/// <summary>启动一个插件后台任务（安装 / 更新 / 本地安装），把任务和插件对上，好在卡片上显示进度。</summary>
	private async Task StartPluginTaskAsync(string method, object args, string plugin, FrameworkElement? source)
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

	private static string PluginDataDir(string id)
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

	internal static void OpenFolder(string path)
	{
		try
		{
			Directory.CreateDirectory(path);
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("打不开文件夹"), ex.Message, ToastKind.Warning);
		}
	}

	/// <summary>页面内子视图（插件设置）：顶部带返回。</summary>
	private void ShowSubView(string title, UIElement content, UIElement? right = null)
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
		head.Margin = new Thickness(18.0, 12.0, 22.0, 8.0);
		Grid g = Ui.G("Auto,*");
		g.Add(head, 0);
		g.Add(content, 1);
		_body.Content = g;
	}

	private PluginWebView? _subWeb;

	private void DisposeSubView()
	{
		_subWeb?.Dispose();
		_subWeb = null;
	}

	public override void OnHidden()
	{
		// 离开插件页时关掉自定义设置页的浏览器（设置页不需要常驻）
		if (_subView && _subWeb != null)
		{
			SelectTab(_tab);
		}
	}
}
