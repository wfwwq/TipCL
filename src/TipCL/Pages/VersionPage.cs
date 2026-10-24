using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

public sealed class VersionPage : PageBase
{
	private sealed class Installed
	{
		public string Id = "";

		public bool Hidden;

		public bool Isolated;

		public int Mods;

		public string Loader = "";

		public string Mc = "";

		public string IsolationLabel = "";
	}

	private sealed class Row
	{
		public string Version { get; init; } = "";

		public string Type { get; init; } = "";

		public string Date { get; init; } = "";

		public string TypeLabel => Type switch
		{
			"release" => I18n.L("正式版"), 
			"snapshot" => I18n.L("快照"), 
			"old_beta" => I18n.L("旧测试"), 
			"old_alpha" => I18n.L("旧阿尔法"), 
			_ => string.IsNullOrEmpty(Type) ? I18n.L("未知") : Type, 
		};
	}

	private static readonly string[] Loaders = new string[5]
	{
		I18n.L("无"),
		"Fabric",
		"Forge",
		"Quilt",
		"NeoForge"
	};

	private readonly TextBox _search = Ui.Input(I18n.L("过滤版本号，例如 1.20"));

	private readonly ListBox _list = new ListBox
	{
		Style = Ui.S("PlainList"),
		ItemTemplate = (DataTemplate)Application.Current.FindResource("Tpl.Version")
	};

	private readonly ComboBox _inst = Ui.Combo();

	private readonly ComboBox _loader = Ui.Combo(Loaders);

	private readonly ComboBox _loaderVer = Ui.Combo(new string[1] { I18n.L("最新") });

	private readonly CheckBox _release = Ui.Check(I18n.L("正式版"), value: true);

	private readonly CheckBox _snapshot = Ui.Check(I18n.L("快照"));

	private readonly CheckBox _oldBeta = Ui.Check(I18n.L("旧测试"));

	private readonly CheckBox _oldAlpha = Ui.Check(I18n.L("旧阿尔法"));

	private readonly CheckBox _optifine = Ui.Check("OptiFine");

	private readonly CheckBox _lite = Ui.Check("LiteLoader");

	private readonly Button _install;

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBlock _instCount = Ui.Muted("");

	private readonly WrapPanel _cards = new WrapPanel();

	private readonly TextBox _mineSearch = Ui.Input(I18n.L("过滤已安装版本"), "", 170.0);

	private readonly CheckBox _showHidden = Ui.Switch();

	private readonly SPanel _installedEmpty = Ui.Empty("\ue7b8", I18n.L("这个实例还没有版本"), I18n.L("在左边挑一个装上"));

	private List<Row> _all = new List<Row>();

	private List<Installed> _mine = new List<Installed>();

	public static int FetchTimeoutMs = 9000;

	private CancellationTokenSource? _fetchCts;

	private bool _fetchInFlight;

	private bool _fetchInterrupted;

	private readonly TextBlock _fetchErrorText = Ui.Muted("");

	private readonly Border _fetchError;

	public override string Title => I18n.L("原版游戏");

	private static Row Map(VersionRow r)
	{
		return new Row
		{
			Version = r.Version,
			Type = r.Type,
			Date = r.Date
		};
	}

	public VersionPage()
	{
		_install = Ui.Btn(I18n.L("安装所选版本"), BtnKind.Primary, delegate
		{
			PageBase.Run(InstallAsync);
		}, "\ue896");
		_install.IsEnabled = false;
		Button button = Ui.Btn(I18n.L("刷新清单"), BtnKind.Chip, delegate
		{
			PageBase.Run(FetchAsync);
		}, "\ue72c");
		Grid grid = Ui.Section(I18n.L("原版游戏"), I18n.L("Mojang 全量版本 + Fabric / Forge / Quilt / NeoForge 加载器"), Ui.H(8.0, _count.VCenter(), button));
		_search.TextChanged += delegate
		{
			ApplyFilter();
		};
		CheckBox[] array = new CheckBox[4] { _release, _snapshot, _oldBeta, _oldAlpha };
		foreach (CheckBox obj in array)
		{
			obj.Checked += delegate
			{
				ApplyFilter();
			};
			obj.Unchecked += delegate
			{
				ApplyFilter();
			};
		}
		_list.SelectionChanged += delegate
		{
			_install.IsEnabled = _list.SelectedItem is Row;
			if (_loader.Str() != I18n.L("无"))
			{
				PageBase.Run(LoadLoaderVersionsAsync);
			}
		};
		_list.MouseDoubleClick += delegate
		{
			PageBase.Run(InstallAsync);
		};
		_loader.SelectionChanged += delegate
		{
			string text = _loader.Str();
			_loaderVer.IsEnabled = text != I18n.L("无");
			_optifine.IsEnabled = text == I18n.L("无") || text == "Forge";
			if (!_optifine.IsEnabled)
			{
				_optifine.IsChecked = false;
			}
			PageBase.Run(LoadLoaderVersionsAsync);
		};
		SPanel el = Ui.H(14.0, _release, _snapshot, _oldBeta, _oldAlpha);
		Button el2 = Ui.Btn(I18n.L("重试"), BtnKind.Soft, delegate
		{
			PageBase.Run(FetchAsync, I18n.L("版本清单刷新失败"));
		}, "\ue72c");
		Grid grid2 = Ui.G(null, "Auto,*,Auto");
		grid2.Add(Ui.Glyph("\ue7ba", 14.0, "B.Warn").VCenter().M(0.0, 0.0, 8.0), 0);
		grid2.Add(_fetchErrorText.Wrap().VCenter(), 0, 1);
		grid2.Add(el2.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 2);
		_fetchError = Ui.RowCard(grid2, null, 10.0);
		_fetchError.Visibility = Visibility.Collapsed;
		Border border = Ui.Card(Ui.V(10.0, Ui.H(10.0, _search, el.VCenter()), _fetchError, _list), 14.0);
		_search.MinWidth = 240.0;
		_list.Height = 330.0;
		Border border2 = Ui.Card(Ui.V(10.0, Ui.H3(I18n.L("安装设置")), Ui.Field(I18n.L("目标实例"), _inst, null, 88.0), Ui.Field(I18n.L("加载器"), _loader, null, 88.0), Ui.Field(I18n.L("加载器版本"), _loaderVer, null, 88.0), Ui.H(16.0, _optifine, _lite), _install.Stretch()), 14.0);
		_mineSearch.TextChanged += delegate
		{
			RenderInstalled();
		};
		_showHidden.Checked += delegate
		{
			PageBase.Run(ReloadInstalledAsync);
		};
		_showHidden.Unchecked += delegate
		{
			PageBase.Run(ReloadInstalledAsync);
		};
		Button button2 = Ui.Btn(I18n.L("打开游戏目录"), BtnKind.Chip, delegate
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.CallAsync("open_version_folder", new
				{
					instance = _inst.Str(),
					version = "",
					which = "game"
				});
			}, I18n.L("打开失败"));
		}, "\ue8b7");
		Grid grid3 = Ui.Section(I18n.L("已安装"), I18n.L("每个版本一张卡：启动、模组、设置都在卡上，「更多」里是重命名 / 复制 / 快捷方式 / 卸载"), Ui.H(8.0, _instCount.VCenter(), _mineSearch.VCenter(), Ui.H(6.0, Ui.Txt(I18n.L("显示隐藏"), 12.0, bold: false, "B.InkMuted").VCenter(), _showHidden).VCenter(), button2));
		Border border3 = Ui.Card(Ui.V(10.0, grid3, _cards, _installedEmpty), 14.0);
		Grid grid4 = Ui.G(null, "*,320");
		grid4.Add(Ui.V(14.0, border, border3), 0);
		grid4.Add(Ui.V(14.0, border2).M(14.0, 0.0, 0.0, 0.0), 0, 1);
		base.Content = PageBase.ScrollBody(grid, grid4);
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = _inst.Str(),
			PreferKind = "modpack"
		});
	}

	protected override async Task LoadAsync()
	{
		List<InstanceInfo> source = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_inst.Fill(source.Select((InstanceInfo i) => i.Name));
		List<VersionRow> list = (await PageBase.Api.TryCallAsync("get_version_list", null, new List<VersionRow>())) ?? new List<VersionRow>();
		if (list.Count > 0)
		{
			_all = list.Select(Map).ToList();
			ApplyFilter();
		}
		await ReloadInstalledAsync();
		PageBase.Run(FetchAsync, I18n.L("版本清单刷新失败"));
	}

	private async Task FetchAsync()
	{
		CancelFetch();
		CancellationTokenSource cts = (_fetchCts = new CancellationTokenSource(FetchTimeoutMs));
		_fetchInterrupted = false;
		_fetchInFlight = true;
		_fetchError.Visibility = Visibility.Collapsed;
		_count.Text = I18n.L("正在拉取版本清单…");
		try
		{
			List<VersionRow> list = (await PageBase.Api.CallAsync<List<VersionRow>>("fetch_version_list", null, cts.Token)) ?? new List<VersionRow>();
			if (cts == _fetchCts)
			{
				if (list.Count > 0)
				{
					_all = list.Select(Map).ToList();
					ApplyFilter();
				}
				else
				{
					ShowFetchFailure(I18n.L("版本清单刷新失败"), I18n.L("拉取失败，检查网络后点刷新"));
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (cts == _fetchCts && !_fetchInterrupted)
			{
				ShowFetchFailure(I18n.L("拉取超时"), I18n.L("{0} 秒内没等到版本清单。检查网络后重试。", Math.Max(1, (FetchTimeoutMs + 999) / 1000)));
			}
		}
		catch (Exception ex2)
		{
			if (cts == _fetchCts)
			{
				ShowFetchFailure(I18n.L("版本清单刷新失败"), ex2.Message);
			}
		}
		finally
		{
			if (cts == _fetchCts)
			{
				_fetchInFlight = false;
			}
		}
	}

	private void CancelFetch()
	{
		_fetchInFlight = false;
		CancellationTokenSource fetchCts = _fetchCts;
		_fetchCts = null;
		if (fetchCts != null)
		{
			try
			{
				fetchCts.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
			fetchCts.Dispose();
		}
	}

	private void ShowFetchFailure(string title, string detail)
	{
		_fetchErrorText.Text = title + " · " + detail;
		_fetchError.Visibility = Visibility.Visible;
		if (_all.Count == 0)
		{
			_count.Text = I18n.L("拉取失败，检查网络后点刷新");
			return;
		}
		ApplyFilter();
		PageBase.Toast(I18n.L("版本清单刷新失败"), I18n.L("沿用已缓存的清单"), ToastKind.Warning);
	}

	private void ApplyFilter()
	{
		string q = _search.Text?.Trim() ?? "";
		HashSet<string> want = new HashSet<string>();
		if (_release.IsChecked == true)
		{
			want.Add("release");
		}
		if (_snapshot.IsChecked == true)
		{
			want.Add("snapshot");
		}
		if (_oldBeta.IsChecked == true)
		{
			want.Add("old_beta");
		}
		if (_oldAlpha.IsChecked == true)
		{
			want.Add("old_alpha");
		}
		IEnumerable<Row> source = _all.Where((Row r) => want.Contains(r.Type));
		if (q.Length > 0)
		{
			source = source.Where((Row r) => r.Version.Contains(q, StringComparison.OrdinalIgnoreCase));
		}
		List<Row> list = source.ToList();
		_list.ItemsSource = list;
		_count.Text = I18n.L("{0} / {1} 个版本", list.Count, _all.Count);
		if (list.Count > 0 && _list.SelectedIndex < 0)
		{
			_list.SelectedIndex = 0;
		}
	}

	private async Task ReloadInstalledAsync()
	{
		string value = _inst.Str();
		bool showHidden = _showHidden.IsChecked == true;
		if (string.IsNullOrEmpty(value))
		{
			_mine = new List<Installed>();
			RenderInstalled();
			return;
		}
		List<VersionRowDto> source = (await PageBase.Api.TryCallAsync("get_version_rows", new
		{
			include_hidden = true
		}, new List<VersionRowDto>())) ?? new List<VersionRowDto>();
		_mine = (from r in source
			where showHidden || !r.Hidden
			select new Installed
			{
				Id = r.Id,
				Hidden = r.Hidden,
				Isolated = r.Isolated,
				Mods = r.Mods,
				Loader = (string.IsNullOrEmpty(r.Loader) ? I18n.L("原版") : r.Loader),
				Mc = r.Mc,
				IsolationLabel = r.IsolationLabel
			}).ToList();
		RenderInstalled();
	}

	private void RenderInstalled()
	{
		string q = _mineSearch.Text?.Trim() ?? "";
		List<Installed> list = ((q.Length == 0) ? _mine : _mine.Where((Installed r) => r.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList());
		_instCount.Text = ((q.Length == 0) ? I18n.L("{0} 个", _mine.Count) : I18n.L("{0} / {1} 个", list.Count, _mine.Count));
		_cards.Children.Clear();
		_installedEmpty.Show(list.Count == 0);
		foreach (Installed item in list)
		{
			_cards.Children.Add(VersionCard(item));
		}
		Motion.StaggerItems(_cards.Children.Cast<UIElement>(), 26.0, 210.0);
	}

	private UIElement VersionCard(Installed row)
	{
		string inst = _inst.Str();
		string id = row.Id;
		SPanel sPanel = Ui.H(6.0, Ui.Tag(row.Loader, "B.AccentDeep", "B.AccentSoft"), Ui.Tag(I18n.L("模组 {0}", row.Mods)), string.IsNullOrEmpty(row.IsolationLabel) ? null : Ui.Tag(row.IsolationLabel), row.Hidden ? Ui.Tag(I18n.L("已隐藏")) : null);
		CheckBox checkBox = Ui.Switch(row.Isolated);
		checkBox.Checked += delegate
		{
			PageBase.Run(() => ToggleIsolationAsync(id, isolated: true), I18n.L("切换失败"));
		};
		checkBox.Unchecked += delegate
		{
			PageBase.Run(() => ToggleIsolationAsync(id, isolated: false), I18n.L("切换失败"));
		};
		SPanel sPanel2 = Ui.H(8.0, Ui.Txt(I18n.L("独立模组"), 12.0, bold: false, "B.InkMuted").VCenter(), checkBox);
		sPanel2.ToolTip = I18n.L("开：这个版本用自己的 mods / config；关：与实例里其它版本共用那一份");
		Button button = Ui.Btn(I18n.L("启动"), BtnKind.Soft, delegate
		{
			PageBase.Run(() => LaunchAsync(id));
		}, "\ue768");
		Button button2 = Ui.IconBtn("\uea86", I18n.L("管理这个版本的模组"), delegate
		{
			PageBase.Run(async delegate
			{
				await ModsDialog.ShowAsync(inst, id);
				await ReloadInstalledAsync();
			});
		});
		Button button3 = Ui.IconBtn("\ue713", I18n.L("版本设置"), delegate
		{
			PageBase.Run(async delegate
			{
				await VersionSetupDialog.ShowAsync(inst, id);
				await ReloadInstalledAsync();
			});
		});
		Button button4 = Ui.IconBtn("\ue712", I18n.L("更多"), delegate(object s, RoutedEventArgs _)
		{
			MoreMenu((FrameworkElement)s, id, row);
		});
		Border border = Ui.Card(Ui.V(8.0, Ui.H(10.0, new ThumbTile(id, 38.0), Ui.V(2.0, Ui.Txt(id, 13.5, bold: true).Trim(), Ui.Small((!string.IsNullOrEmpty(row.Mc)) ? ("Minecraft " + row.Mc) : (row.Hidden ? I18n.L("在列表中隐藏") : I18n.L("已安装")))).VCenter()), sPanel, sPanel2, Ui.Sep(), Ui.H(6.0, button, button2, button3, button4)), 14.0);
		border.Width = 252.0;
		border.Margin = new Thickness(0.0, 0.0, 12.0, 12.0);
		Motion.HoverLift(border, 1.006, 2.0, 18.0);
		return border;
	}

	private async Task ToggleIsolationAsync(string version, bool isolated)
	{
		bool seed = false;
		if (isolated)
		{
			int num = await Dlg.Choose(I18n.L("转为独立模组"), Ui.Muted(I18n.L("「{0}」将拥有自己的 mods / config 目录。\n\n", version) + I18n.L("要把游戏目录里现有的共享模组复制一份过去吗？\n") + I18n.L("选「复制一份」保持现在能玩的样子；选「留空」从零开始装。")).Wrap().MinW(380.0), new string[3]
			{
				I18n.L("取消"),
				I18n.L("留空"),
				I18n.L("复制一份")
			}, 540.0);
			if (num <= 0)
			{
				await ReloadInstalledAsync();
				return;
			}
			seed = num == 2;
		}
		await PageBase.Api.CallAsync("toggle_version_isolation", new { version, isolated, seed });
		PageBase.Toast(isolated ? I18n.L("已转为独立模组") : I18n.L("已改回共用"), isolated ? (I18n.L("「{0}」以后用自己的 mods / config", version) + (seed ? I18n.L("，已复制一份现有模组") : "")) : I18n.L("「{0}」与实例里其它版本共用 mods", version), ToastKind.Success);
		await ReloadInstalledAsync();
	}

	private async Task LaunchAsync(string version)
	{
		string inst = _inst.Str();
		await InstancePage.SetDefaultAsync(inst);
		if (PageBase.Win?.GetPage("launch") is LaunchPage launchPage)
		{
			launchPage.PreferInstance(inst, version);
		}
		PageBase.Win?.Navigate("launch");
	}

	private void MoreMenu(FrameworkElement anchor, string version, Installed row)
	{
		string inst = _inst.Str();
		ContextMenu menu = new ContextMenu();
		Item(I18n.L("打开版本文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_version_folder", new
			{
				instance = inst,
				version = version,
				which = "root"
			});
		});
		Item(I18n.L("打开游戏文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_version_folder", new
			{
				instance = inst,
				version = version,
				which = "game"
			});
		});
		Item(I18n.L("存档管理…"), async delegate
		{
			await SavesDialog.ShowAsync(inst, version);
		});
		Item(I18n.L("隔离细分…"), () => IsolationDetailAsync(version));
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名"), async delegate
		{
			string text = await Dlg.Prompt(I18n.L("重命名版本"), I18n.L("新版本 ID"), version);
			if (!string.IsNullOrWhiteSpace(text) && !(text == version))
			{
				await PageBase.Api.CallAsync("rename_version", new
				{
					instance = inst,
					version = version,
					new_id = text
				});
				await ReloadInstalledAsync();
			}
		});
		Item(I18n.L("复制一份"), async delegate
		{
			string text = await Dlg.Prompt(I18n.L("复制版本"), I18n.L("新版本 ID"), version + "-copy");
			if (!string.IsNullOrWhiteSpace(text))
			{
				await PageBase.Api.CallAsync("copy_version", new
				{
					instance = inst,
					version = version,
					new_id = text
				});
				await ReloadInstalledAsync();
			}
		});
		Item(row.Hidden ? I18n.L("取消隐藏") : I18n.L("在列表中隐藏"), async delegate
		{
			await PageBase.Api.CallAsync("hide_version", new
			{
				instance = inst,
				version = version,
				hidden = !row.Hidden
			});
			await ReloadInstalledAsync();
			PageBase.Toast(row.Hidden ? I18n.L("已取消隐藏") : I18n.L("已隐藏"), version, ToastKind.Success);
		});
		Item(I18n.L("创建桌面快捷方式"), async delegate
		{
			string text = await PageBase.Api.CallAsync<string>("create_desktop_shortcut", new
			{
				instance = inst,
				version = version
			});
			await Dlg.Alert(I18n.L("已创建"), I18n.L("桌面快捷方式：\n{0}\n\n双击即可直接启动该版本。", text));
		});
		Item(I18n.L("导出启动脚本"), async delegate
		{
			string text = Dlg.SaveFile(I18n.L("批处理 (*.bat)|*.bat"), "launch-" + inst + "-" + version + ".bat", I18n.L("导出启动脚本"));
			if (text != null)
			{
				await PageBase.Api.StartTaskAsync("export_launch_script", new
				{
					instance = inst,
					version = version,
					dest = text
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		Item(I18n.L("修复（补全缺失文件）"), async delegate
		{
			await PageBase.Api.StartTaskAsync("repair_version", new
			{
				instance = inst,
				version = version
			});
			PageBase.Win?.FlyToTasks(anchor, I18n.L("修复中"));
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("卸载这个版本"), async delegate
		{
			if (await Dlg.Confirm(I18n.L("卸载版本"), I18n.L("确定卸载「{0}」？该版本目录下的独立模组与存档会一并删除。", version), I18n.L("卸载"), I18n.L("取消"), danger: true))
			{
				await PageBase.Api.CallAsync("uninstall_version", new
				{
					spec = inst + " / " + version
				});
				await ReloadInstalledAsync();
				PageBase.Toast(I18n.L("已卸载"), version, ToastKind.Success);
			}
		});
		menu.PlacementTarget = anchor;
		menu.IsOpen = true;
		void Item(string head, Func<Task> act)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = head
			};
			menuItem.Click += delegate
			{
				PageBase.Run(act);
			};
			menu.Items.Add(menuItem);
		}
	}

	private async Task IsolationDetailAsync(string version)
	{
		string[] labels = new string[4]
		{
			I18n.L("关闭（共用游戏目录）"),
			I18n.L("隔离存档"),
			I18n.L("隔离 Mod 与配置"),
			I18n.L("隔离全部")
		};
		string[] values = new string[4] { "none", "saves", "mods", "all" };
		string value = (await PageBase.Api.TryCallAsync("get_version_isolation", new { version }, "none")) ?? "none";
		int num = Math.Max(0, Array.IndexOf<string>(values, value));
		ComboBox pick = Ui.Combo(labels, labels[num]);
		CheckBox seed = Ui.Check(I18n.L("把共享池里的模组复制一份过去"));
		if (await Dlg.Ask(I18n.L("隔离细分 · {0}", version), Ui.V(8.0, Ui.Muted(I18n.L("决定这个版本的存档 / 模组是自己一份，还是与游戏目录里其它版本共用")), pick, seed), I18n.L("保存")))
		{
			await PageBase.Api.CallAsync("set_version_isolation", new
			{
				version = version,
				mode = values[Math.Max(0, pick.SelectedIndex)],
				seed = (seed.IsChecked == true)
			});
			await ReloadInstalledAsync();
		}
	}

	private async Task LoadLoaderVersionsAsync()
	{
		string text = _loader.Str();
		if (text == I18n.L("无") || !(_list.SelectedItem is Row row))
		{
			_loaderVer.Fill(new string[1] { I18n.L("最新") });
			return;
		}
		_loaderVer.Fill(new string[1] { I18n.L("最新（加载中…）") });
		List<LoaderVer> list = (await PageBase.Api.TryCallAsync("list_loader_versions", new
		{
			mc_version = row.Version,
			loader = text.ToLowerInvariant()
		}, new List<LoaderVer>())) ?? new List<LoaderVer>();
		List<string> list2 = new List<string> { I18n.L("最新") };
		list2.AddRange(list.Select((LoaderVer r) => (!string.IsNullOrEmpty(r.Label)) ? r.Label : r.Id));
		_loaderVer.Fill(list2, I18n.L("最新"));
		_loaderVer.Tag = list;
	}

	private async Task InstallAsync()
	{
		object selectedItem = _list.SelectedItem;
		if (!(selectedItem is Row row))
		{
			PageBase.Toast(I18n.L("先选一个版本"), "", ToastKind.Warning);
			return;
		}
		string loader = _loader.Str();
		bool none = loader == I18n.L("无");
		string loader_version = "";
		if (!none && _loaderVer.SelectedIndex > 0 && _loaderVer.Tag is List<LoaderVer> list)
		{
			int num = _loaderVer.SelectedIndex - 1;
			if (num >= 0 && num < list.Count)
			{
				loader_version = list[num].Id;
			}
		}
		Dictionary<string, object> extra = new Dictionary<string, object>
		{
			["optifine"] = _optifine.IsChecked == true,
			["liteloader"] = _lite.IsChecked == true
		};
		await PageBase.Api.StartTaskAsync("install_game", new
		{
			version = row.Version,
			loader = (none ? "" : loader),
			loader_version = loader_version,
			instance = _inst.Str(),
			extra = extra
		});
		PageBase.Win?.FlyToTasks(_install, row.Version);
		PageBase.Toast(I18n.L("已加入队列"), row.Version + (none ? "" : (" + " + loader)), ToastKind.Success);
		PageBase.Win?.Navigate("tasks");
	}

	public override async Task RefreshAsync()
	{
		await ReloadInstalledAsync();
	}

	public override void OnShown()
	{
		_inst.SelectionChanged -= InstChanged;
		_inst.SelectionChanged += InstChanged;
		if (_fetchInterrupted)
		{
			PageBase.Run(FetchAsync, I18n.L("版本清单刷新失败"));
		}
	}

	public override void OnHidden()
	{
		if (_fetchInFlight)
		{
			_fetchInterrupted = true;
			CancelFetch();
		}
	}

	private void InstChanged(object? s, SelectionChangedEventArgs e)
	{
		PageBase.Run(ReloadInstalledAsync);
	}
}
