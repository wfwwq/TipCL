using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

public sealed partial class PluginsPage
{
	private readonly SPanel _installedHost;

	private List<PluginInfo> _installed = new List<PluginInfo>();

	private readonly Dictionary<string, List<ProgressBar>> _bars = new Dictionary<string, List<ProgressBar>>();

	private UIElement InstalledHeader()
	{
		return Ui.Section(I18n.L("已安装的插件"), I18n.L("插件可以给侧栏加页面、给 AI 加工具和预设、在首页加卡片、在窗口上加浮层"), Ui.H(8.0, Ui.Btn(I18n.L("从文件安装"), BtnKind.Soft, delegate
		{
			string path = Dlg.PickFile(I18n.L("插件包") + " (*.zip)|*.zip", I18n.L("选择插件包"));
			if (path != null)
			{
				PageBase.Run(() => InstallLocalAsync(path, dev: false, null), I18n.L("安装失败"));
			}
		}, "\ue8b5"), Ui.Btn(I18n.L("去插件市场"), BtnKind.Primary, delegate
		{
			SelectTab(Tab.Market);
		}, "\ue896")));
	}

	private async Task LoadInstalledAsync()
	{
		if (!AppServices.Ready)
		{
			return;
		}
		// 后端还不支持插件时（旧桥 / 内核未就绪）不报错，只显示空列表
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("plugins_list");
		_installed = J.Items(r).Select(PluginInfo.From).Where((PluginInfo p) => p.Id.Length > 0).ToList();
		RenderInstalled();
	}

	private void RenderInstalled()
	{
		_installedHost.Children.Clear();
		foreach (List<ProgressBar> list in _bars.Values)
		{
			list.RemoveAll((ProgressBar b) => b.Tag as string == "installed");
		}
		if (_installed.Count == 0)
		{
			SPanel empty = Ui.Empty("\uea86", I18n.L("还没有安装插件"), I18n.L("去插件市场看看，或者把插件 zip 拖进这个页面。"));
			_installedHost.Children.Add(empty);
			return;
		}
		foreach (PluginInfo p in _installed.OrderBy((PluginInfo x) => x.DisplayName, StringComparer.CurrentCulture))
		{
			_installedHost.Children.Add(InstalledCard(p));
		}
		if (Motion.Enabled)
		{
			Motion.Stagger(_installedHost, 22.0, 180.0, 8.0);
		}
	}

	private static Border StateBadge(PluginInfo p)
	{
		if (!p.Enabled && p.State != "restart_required")
		{
			return Ui.Tag(I18n.L("已禁用"));
		}
		return p.State switch
		{
			"running" => Ui.Tag(I18n.L("运行中"), "B.Ok", "B.AccentSoft"),
			"error" => Ui.Tag(I18n.L("出错"), "B.Danger", "B.DangerSoft"),
			"restart_required" => Ui.Tag(I18n.L("重启后生效"), "B.Warn", "B.WarnSoft"),
			"loading" => Ui.Tag(I18n.L("加载中"), "B.Info", "B.InfoSoft"),
			"stopped" => Ui.Tag(I18n.L("已停止")),
			_ => Ui.Tag((p.State.Length > 0) ? p.State : I18n.L("未知")),
		};
	}

	private Border InstalledCard(PluginInfo p)
	{
		FrameworkElement icon = PluginIcon.Make(p.Icon, 36.0, "B.AccentText");
		Border iconBox = new Border
		{
			Width = 48.0,
			Height = 48.0,
			CornerRadius = new CornerRadius(10.0),
			Child = icon.Center().VCenter()
		};
		iconBox.SetResourceReference(Border.BackgroundProperty, "B.AccentTint");
		TextBlock name = Ui.Txt(p.DisplayName, 14.0, bold: true).Trim();
		SPanel title = Ui.H(6.0, name.VCenter(), (p.Version.Length > 0) ? Ui.Tag("v" + p.Version) : null, StateBadge(p), (p.Source == "dev") ? Ui.Tag(I18n.L("开发中"), "B.Info", "B.InfoSoft").Tip(p.DevPath) : null, (p.Source == "local") ? Ui.Tag(I18n.L("本地")) : null, (p.Runtime == "dotnet") ? Ui.Tag(".NET") : null, p.UpdateAvailable ? Ui.Tag(I18n.L("有更新"), "B.Warn", "B.WarnSoft") : null);
		string meta = string.Join(" · ", new string[2]
		{
			(p.Author.Length > 0) ? I18n.L("作者 {0}", p.Author) : "",
			p.Id
		}.Where((string s) => s.Length > 0));
		TextBlock desc = Ui.Muted(p.Description).Wrap();
		desc.MaxHeight = 40.0;
		desc.TextTrimming = TextTrimming.CharacterEllipsis;
		SPanel info = Ui.V(3.0, title, Ui.Small(meta).Trim(), (p.Description.Length > 0) ? desc : null);
		if (p.Error.Length > 0)
		{
			info.Children.Add(Ui.Txt(p.Error, 12.0, bold: false, "B.Danger").Wrap());
		}
		if (p.State == "restart_required")
		{
			info.Children.Add(Ui.Small(I18n.L(".NET 插件的启用、禁用、更新和卸载要重启 TipCL 才生效")).Wrap());
		}
		ProgressBar bar = Ui.Prog();
		bar.Height = 4.0;
		bar.Tag = "installed";
		bar.Visibility = Visibility.Collapsed;
		Track(p.Id, bar);
		info.Children.Add(bar.M(0.0, 4.0, 0.0, 0.0));
		CheckBox sw = Ui.Switch(p.Enabled);
		sw.ToolTip = p.Enabled ? I18n.L("点击禁用") : I18n.L("点击启用");
		sw.Click += delegate
		{
			bool on = sw.IsChecked == true;
			PageBase.Run(async delegate
			{
				try
				{
					await PageBase.Api.CallAsync("plugins_set_enabled", new
					{
						id = p.Id,
						enabled = on
					});
				}
				catch
				{
					sw.IsChecked = !on;
					throw;
				}
				await PluginUi.RefreshAsync();
				await LoadInstalledAsync();
			}, on ? I18n.L("启用失败") : I18n.L("禁用失败"));
		};
		Button settings = Ui.IconBtn("\ue713", I18n.L("设置"), delegate
		{
			ShowSettings(p, forceAuto: false);
		});
		Button reload = Ui.IconBtn("\ue72c", I18n.L("重新加载"), delegate
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.CallAsync("plugins_reload", new { id = p.Id });
				PageBase.Toast(I18n.L("已重新加载"), p.DisplayName, ToastKind.Success);
				await PluginUi.RefreshAsync();
				await LoadInstalledAsync();
			}, I18n.L("重新加载失败"));
		});
		reload.IsEnabled = p.Enabled;
		Button? update = null;
		if (p.UpdateAvailable)
		{
			Button upd = Ui.Btn(I18n.L("更新"), BtnKind.Soft, null, "\ue896");
			upd.Click += delegate
			{
				PageBase.Run(() => StartPluginTaskAsync("plugins_install", new { id = p.Id }, p.Id, upd), I18n.L("更新失败"));
			};
			update = upd;
		}
		Button more = Ui.IconBtn("\ue712", I18n.L("更多"));
		ContextMenu menu = new ContextMenu();
		AddItem(menu, I18n.L("打开数据目录"), "\ue8b7", delegate
		{
			OpenFolder(PluginDataDir(p.Id));
		});
		AddItem(menu, I18n.L("查看日志"), "\ue9d5", delegate
		{
			PageBase.Run(() => ShowLogAsync(p.Id, p.DisplayName));
		});
		AddItem(menu, I18n.L("权限"), "\uea18", delegate
		{
			ShowPermissions(p);
		});
		if (p.Source == "dev" && p.DevPath.Length > 0)
		{
			AddItem(menu, I18n.L("打开开发文件夹"), "\ue838", delegate
			{
				OpenFolder(p.DevPath);
			});
		}
		if (p.Homepage.Length > 0)
		{
			AddItem(menu, I18n.L("插件主页"), "\ue8a7", delegate
			{
				Ui.OpenUrl(p.Homepage);
			});
		}
		menu.Items.Add(new Separator());
		AddItem(menu, (p.Source == "dev") ? I18n.L("取消链接") : I18n.L("卸载"), "\ue74d", delegate
		{
			PageBase.Run(() => UninstallAsync(p), I18n.L("卸载失败"));
		});
		more.ContextMenu = menu;
		more.Click += delegate
		{
			menu.PlacementTarget = more;
			menu.IsOpen = true;
		};
		SPanel actions = Ui.H(4.0, update, settings, reload, more, sw.M(8.0, 0.0, 0.0, 0.0));
		Grid g = Ui.G(null, "Auto,*,Auto");
		g.Add(iconBox.VTop(), 0);
		g.Add(info.M(14.0, 0.0, 12.0, 0.0), 0, 1);
		g.Add(actions.VCenter(), 0, 2);
		return Ui.RowCard(g, null, 14.0);
	}

	internal static void AddItem(ItemsControl menu, string header, string glyph, Action act)
	{
		MenuItem mi = new MenuItem
		{
			Header = header,
			Icon = Ui.Glyph(glyph, 13.0)
		};
		mi.Click += delegate
		{
			act();
		};
		menu.Items.Add(mi);
	}

	private void Track(string plugin, ProgressBar bar)
	{
		if (!_bars.TryGetValue(plugin, out List<ProgressBar> list))
		{
			list = (_bars[plugin] = new List<ProgressBar>());
		}
		list.Add(bar);
		string? running = _taskPlugin.FirstOrDefault((KeyValuePair<string, string> kv) => kv.Value == plugin).Key;
		if (running != null)
		{
			ApplyProgress(bar, TaskStore.Get(running), done: false);
		}
	}

	private void SyncProgress(string plugin, string taskId, bool done = false)
	{
		if (!_bars.TryGetValue(plugin, out List<ProgressBar> list))
		{
			return;
		}
		TaskRow? row = TaskStore.Get(taskId);
		foreach (ProgressBar bar in list)
		{
			ApplyProgress(bar, row, done);
		}
	}

	private static void ApplyProgress(ProgressBar bar, TaskRow? row, bool done)
	{
		if (done)
		{
			bar.Visibility = Visibility.Collapsed;
			return;
		}
		bar.Visibility = Visibility.Visible;
		bar.IsIndeterminate = row?.Indeterminate ?? true;
		if (row != null && !row.Indeterminate)
		{
			Motion.Progress(bar, row.Progress);
		}
		bar.ToolTip = row?.Status;
	}

	private async Task UninstallAsync(PluginInfo p)
	{
		CheckBox del = Ui.Check(I18n.L("同时删除数据（插件数据目录和保存的凭据）"));
		SPanel body = Ui.V(10.0, Ui.Muted((p.Source == "dev") ? I18n.L("取消链接后开发文件夹本身不会被删除。") : I18n.L("会删除插件文件；之后可以从市场重新安装。")).Wrap(), del);
		body.MinWidth = 360.0;
		if (await Dlg.Ask(I18n.L("卸载「{0}」？", p.DisplayName), body, (p.Source == "dev") ? I18n.L("取消链接") : I18n.L("卸载"), null, danger: true))
		{
			bool deleteData = del.IsChecked == true;
			await PageBase.Api.CallAsync("plugins_uninstall", new
			{
				id = p.Id,
				delete_data = deleteData
			});
			PageBase.Toast(I18n.L("已卸载"), p.DisplayName, ToastKind.Success);
			await PluginUi.RefreshAsync();
			await LoadInstalledAsync();
		}
	}

	private void ShowPermissions(PluginInfo p)
	{
		List<string> perms = PluginPerms.Effective((p.Granted.Count > 0) ? p.Granted : p.Permissions, p.Runtime);
		Dlg.Panel(I18n.L("「{0}」的权限", p.DisplayName), new ScrollViewer
		{
			Content = PluginPerms.List(perms),
			MaxHeight = 420.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		}, 560.0);
	}

	internal static async Task ShowLogAsync(string id, string title)
	{
		TextBox box = Ui.LogBox();
		box.Height = 380.0;
		box.MinWidth = 640.0;
		box.Text = await ReadLogAsync(id);
		box.ScrollToEnd();
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, null, "\ue72c");
		refresh.Click += delegate
		{
			PageBase.Run(async delegate
			{
				box.Text = await ReadLogAsync(id);
				box.ScrollToEnd();
			});
		};
		Dlg.Panel(I18n.L("{0} · 日志", title), Ui.V(8.0, refresh.Left(), box), 860.0);
	}

	internal static async Task<string> ReadLogAsync(string id, int lines = 400)
	{
		JsonElement r = await PageBase.Api.CallAsync("plugins_log", new { id, lines });
		string text = r.ValueKind switch
		{
			JsonValueKind.String => r.GetString() ?? "",
			JsonValueKind.Array => string.Join("\n", from x in r.EnumerateArray()
				select (x.ValueKind == JsonValueKind.String) ? x.GetString() : x.GetRawText()),
			JsonValueKind.Object => J.Str(r, "text", J.Str(r, "log")),
			_ => "",
		};
		if (text.Length != 0)
		{
			return text;
		}
		return I18n.L("（日志是空的）");
	}

	private async Task InstallLocalAsync(string path, bool dev, FrameworkElement? source)
	{
		string label = System.IO.Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
		JsonElement r = await PageBase.Api.CallAsync("plugins_install_local", new { path, dev });
		string taskId = J.TaskId(r);
		if (taskId.Length > 0)
		{
			_taskPlugin[taskId] = label;
			if (source != null)
			{
				PageBase.Win?.FlyToTasks(source, I18n.L("已加入队列"));
			}
			PageBase.Toast(dev ? I18n.L("正在链接开发文件夹") : I18n.L("正在安装插件"), label);
		}
		else
		{
			PageBase.Toast(dev ? I18n.L("已链接开发文件夹") : I18n.L("插件已安装"), J.Str(r, "id", label), ToastKind.Success);
			await PluginUi.RefreshAsync();
			await LoadInstalledAsync();
		}
	}
}
