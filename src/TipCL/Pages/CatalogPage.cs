using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

public sealed class CatalogPage : PageBase
{
	private enum Mode
	{
		Search,
		Installed,
		Favorites
	}

	private static readonly string[] GameVersions = new string[21]
	{
		I18n.L("全部"),
		"26.3",
		"26.2",
		"26.1.2",
		"26.1",
		"1.21.11",
		"1.21.10",
		"1.21.8",
		"1.21.5",
		"1.21.4",
		"1.21.1",
		"1.20.6",
		"1.20.4",
		"1.20.1",
		"1.19.4",
		"1.19.2",
		"1.18.2",
		"1.17.1",
		"1.16.5",
		"1.12.2",
		"1.7.10"
	};

	private readonly CatalogKind _kind;

	private readonly TextBox _search = Ui.Input();

	private readonly ComboBox _source;

	private readonly ComboBox _type;

	private readonly ComboBox _version = Ui.Combo(GameVersions, I18n.L("全部"), 140.0, "", editable: true);

	private readonly ComboBox _inst = Ui.Combo(null, null, 150.0);

	private readonly ComboBox _installedVer = Ui.Combo(null, null, 150.0);

	private static string _clipSeen = "";

	private readonly ListBox _results;

	private readonly SPanel _placeholder = Ui.V(10.0);

	private readonly Grid _listHost = new Grid();

	private readonly ToggleButton _modeSearch;

	private readonly ToggleButton _modeInstalled;

	private readonly ToggleButton _modeFav;

	private readonly DispatcherTimer _debounce = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(320.0)
	};

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private HashSet<string> _favKeys = new HashSet<string>();

	private bool _sync;

	private Mode _mode;

	private readonly bool _installedFirst;

	private int _searchToken;

	public static int SearchTimeoutMs = 9000;

	private CancellationTokenSource? _searchCts;

	private bool _searchInFlight;

	private bool _searchInterrupted;

	private List<string> _installedVerValues = new List<string>();

	private List<string> _installedNames = new List<string>();

	public override string Title
	{
		get
		{
			if (!_installedFirst)
			{
				return _kind.Title;
			}
			return I18n.L("模组");
		}
	}

	public CatalogPage(CatalogKind kind, bool installedFirst = false)
	{
		_kind = kind;
		_installedFirst = installedFirst && kind.InstalledMethod.Length > 0;
		_mode = (_installedFirst ? Mode.Installed : Mode.Search);
		_search.Tag = I18n.L("搜索{0}，留空看热门", kind.Title);
		string[] array = new string[3]
		{
			I18n.L("全部"),
			"Modrinth",
			"CurseForge"
		};
		_source = Ui.Combo(array, array.Contains<string>(kind.DefaultSource) ? kind.DefaultSource : I18n.L("全部"), 116.0);
		_type = Ui.Combo(kind.Types.Select((string t) => I18n.L(t)), I18n.L("全部"), 116.0);
		_modeSearch = new ToggleButton
		{
			Content = I18n.L("搜索下载"),
			Style = Ui.S("TabItemBtn"),
			IsChecked = !_installedFirst
		};
		_modeInstalled = new ToggleButton
		{
			Content = I18n.L("已安装"),
			Style = Ui.S("TabItemBtn"),
			IsChecked = _installedFirst
		};
		_modeFav = new ToggleButton
		{
			Content = I18n.L("收藏"),
			Style = Ui.S("TabItemBtn")
		};
		_modeSearch.Click += delegate
		{
			SetMode(Mode.Search);
		};
		_modeInstalled.Click += delegate
		{
			SetMode(Mode.Installed);
		};
		_modeFav.Click += delegate
		{
			SetMode(Mode.Favorites);
		};
		Button el = Ui.Btn(I18n.L("搜索"), BtnKind.Primary, delegate
		{
			PageBase.Run(SearchAsync, I18n.L("搜索失败"));
		}, "\ue721");
		Button button = Ui.Btn(I18n.L("重置条件"), BtnKind.Chip, delegate
		{
			ResetFilters();
		}, "\ue72c");
		Button button2 = Ui.Btn(I18n.L("检查更新"), BtnKind.Chip, delegate
		{
			PageBase.Run(async delegate
			{
				await ModsDialog.CheckUpdatesAsync(CurrentInstance());
				if (_mode == Mode.Installed)
				{
					await LoadInstalledAsync();
				}
			}, I18n.L("检查更新失败"));
		}, "\ue72c");
		button2.Visibility = ((!(_kind.Key == "mod")) ? Visibility.Collapsed : Visibility.Visible);
		Button button3 = Ui.Btn(I18n.L("全部导出"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => ExportAllInstalledAsync(_installedNames), I18n.L("导出失败"));
		}, "\uede1");
		button3.Visibility = ((ExportKind().Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
		_installedVer.SelectionChanged += delegate
		{
			if (!_sync && _mode == Mode.Installed)
			{
				PageBase.Run(LoadInstalledAsync);
			}
		};
		Button linkBtn = Ui.Btn(I18n.L("链接安装"), BtnKind.Chip, null, "\ue71b");
		linkBtn.Click += delegate
		{
			PageBase.Run(() => InstallFromLinkAsync(linkBtn), I18n.L("链接安装失败"));
		};
		Button localBtn = Ui.Btn(I18n.L("导入本地文件"), BtnKind.Chip, null, "\ue8b5");
		localBtn.Click += delegate
		{
			ImportLocal(localBtn);
		};
		Button button4 = Ui.IconBtn("\ue72c", I18n.L("刷新"), delegate
		{
			PageBase.Run(ReloadCurrentAsync);
		});
		_debounce.Tick += delegate
		{
			_debounce.Stop();
			if (_mode == Mode.Search)
			{
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_search.TextChanged += delegate
		{
			if (!_sync && _mode == Mode.Search)
			{
				_debounce.Stop();
				_debounce.Start();
			}
		};
		_search.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				_debounce.Stop();
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_source.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_type.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_version.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_inst.SelectionChanged += delegate
		{
			if (!_sync)
			{
				if (PageBase.Win != null)
				{
					PageBase.Win.Prefs.CatalogInstance = CurrentInstance();
					PageBase.Win.SaveUiPrefs();
				}
				PageBase.Run(ReloadCurrentAsync);
			}
		};
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(_search, 0);
		grid.Add(el.M(8.0, 0.0, 0.0, 0.0), 0, 1);
		WrapPanel filters = new WrapPanel();
		Labeled(I18n.L("来源"), _source);
		Labeled(I18n.L("类型"), _type);
		Labeled(I18n.L("游戏版本"), _version);
		Labeled(I18n.L("实例"), _inst);
		if (_kind.InstalledMethod.Length > 0)
		{
			Labeled(I18n.L("已装于版本"), _installedVer);
		}
		filters.Children.Add(Ui.H(7.0, button, linkBtn, localBtn, button2, button3, button4).M(0.0, 0.0, 0.0, 8.0));
		Border el2 = Ui.Card(Ui.V(10.0, grid, filters), 14.0);
		SPanel right = Ui.H(6.0, _modeSearch, (_kind.InstalledMethod.Length > 0) ? _modeInstalled : null, _modeFav);
		Grid el3 = Ui.Section(Title, SubTitle(), right);
		_results = Ui.VirtualList<object>(BuildRow);
		_listHost.Children.Add(_results);
		_listHost.Children.Add(Ui.Scroll(_placeholder));
		Grid grid2 = Ui.G("Auto,Auto,*");
		grid2.Add(el3, 0);
		grid2.Add(el2.M(0.0, 14.0, 0.0, 14.0), 1);
		grid2.Add(_listHost, 2);
		grid2.Margin = new Thickness(26.0, 20.0, 26.0, 20.0);
		base.Content = grid2;
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = CurrentInstance(),
			PreferKind = PreferredKind()
		});
		void Labeled(string label, FrameworkElement ctl)
		{
			filters.Children.Add(Ui.H(6.0, Ui.Txt(label, 12.0, bold: false, "B.InkMuted").VCenter(), ctl.VCenter()).M(0.0, 0.0, 14.0, 8.0));
		}
	}

	private string PreferredKind()
	{
		return _kind.Key switch
		{
			"mod" => "mod", 
			"modpack" => "modpack", 
			"datapack" => "datapack", 
			"resourcepack" => "resourcepack", 
			"shader" => "shaderpack", 
			"world" => "world", 
			_ => "", 
		};
	}

	private string SubTitle()
	{
		if (_installedFirst)
		{
			return I18n.L("管理所选实例里已装的模组；要装新的切到「搜索下载」");
		}
		return _kind.Key switch
		{
			"mod" => I18n.L("从 Modrinth / CurseForge 搜模组，装进所选实例"), 
			"modpack" => I18n.L("一键安装整合包到独立实例"), 
			"datapack" => I18n.L("数据包会放进实例 datapacks 目录"), 
			"resourcepack" => I18n.L("资源包会放进实例 resourcepacks 目录"), 
			"shader" => I18n.L("光影包会放进实例 shaderpacks 目录"), 
			_ => I18n.L("世界地图会解压成实例存档"), 
		};
	}

	private string Glyph()
	{
		return _kind.Key switch
		{
			"mod" => "\uea86", 
			"modpack" => "\ue7b8", 
			"datapack" => "\ue8f1", 
			"resourcepack" => "\ueb9f", 
			"shader" => "\ue706", 
			_ => "\ue909", 
		};
	}

	private string CurrentInstance()
	{
		return _inst.Str();
	}

	private string InstalledVersion()
	{
		int selectedIndex = _installedVer.SelectedIndex;
		if (selectedIndex < 0 || selectedIndex >= _installedVerValues.Count)
		{
			return "";
		}
		return _installedVerValues[selectedIndex];
	}

	private void ResetFilters()
	{
		_sync = true;
		_search.Clear();
		_source.SelectedItem = (_source.Items.Contains(_kind.DefaultSource) ? _kind.DefaultSource : I18n.L("全部"));
		_type.SelectedIndex = 0;
		_version.Text = I18n.L("全部");
		_version.SelectedIndex = 0;
		_sync = false;
		PageBase.Run(ReloadCurrentAsync);
	}

	private string GameVersionFilter()
	{
		string text = (_version.Text ?? "").Trim();
		if (text.Length == 0 || text == I18n.L("全部"))
		{
			return "";
		}
		return text;
	}

	private string SourceParam()
	{
		string text = _source.Str();
		if (!(text == I18n.L("全部")))
		{
			return text;
		}
		return "all";
	}

	private string CategoryParam()
	{
		if (_type.SelectedIndex <= 0)
		{
			return "";
		}
		string shown = _type.Str();
		return _kind.Types.FirstOrDefault((string t) => I18n.L(t) == shown) ?? shown;
	}

	private void ShowPlaceholder(params UIElement[] kids)
	{
		_results.ItemsSource = null;
		_placeholder.Children.Clear();
		foreach (UIElement element in kids)
		{
			_placeholder.Children.Add(element);
		}
		_placeholder.Visibility = Visibility.Visible;
		_listHost.Children[1].Visibility = Visibility.Visible;
		_results.Visibility = Visibility.Collapsed;
	}

	private void ShowRows(IEnumerable rows)
	{
		_placeholder.Children.Clear();
		_listHost.Children[1].Visibility = Visibility.Collapsed;
		_results.Visibility = Visibility.Visible;
		_results.ItemsSource = rows;
		if (_results.Items.Count > 0)
		{
			_results.ScrollIntoView(_results.Items[0]);
		}
	}

	private UIElement BuildRow(object item)
	{
		if (!(item is CatalogItem item2))
		{
			if (!(item is ModEntry m))
			{
				if (item is string file)
				{
					return InstalledRow(file);
				}
				return Ui.Txt(item?.ToString() ?? "");
			}
			return ModRow(m);
		}
		return ResultCard(item2);
	}

	protected override async Task LoadAsync()
	{
		_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_sync = true;
		_inst.Fill(_instances.Select((InstanceInfo i) => i.Name), PageBase.Win?.Prefs.CatalogInstance);
		_sync = false;
		await ReloadFavoritesAsync();
		PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
	}

	private CancellationToken NextSearchToken()
	{
		CancelSearch();
		_searchCts = new CancellationTokenSource(SearchTimeoutMs);
		_searchInterrupted = false;
		return _searchCts.Token;
	}

	private void CancelSearch()
	{
		_searchInFlight = false;
		CancellationTokenSource searchCts = _searchCts;
		_searchCts = null;
		if (searchCts != null)
		{
			try
			{
				searchCts.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
			searchCts.Dispose();
		}
	}

	private void ShowFailure(string title, string detail)
	{
		Button button = Ui.Btn(I18n.L("重试"), BtnKind.Soft, delegate
		{
			PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
		}, "\ue72c");
		button.HorizontalAlignment = HorizontalAlignment.Center;
		button.Margin = new Thickness(0.0, -30.0, 0.0, 0.0);
		ShowPlaceholder(Ui.Empty("\ue7ba", title, detail), button);
	}

	private Task ReloadCurrentAsync()
	{
		return _mode switch
		{
			Mode.Installed => LoadInstalledAsync(), 
			Mode.Favorites => LoadFavoritesAsync(), 
			_ => SearchAsync(), 
		};
	}

	private void SetMode(Mode mode)
	{
		if (mode == Mode.Installed && _kind.InstalledMethod.Length == 0)
		{
			mode = Mode.Search;
		}
		_mode = mode;
		_modeSearch.IsChecked = mode == Mode.Search;
		_modeInstalled.IsChecked = mode == Mode.Installed;
		_modeFav.IsChecked = mode == Mode.Favorites;
		PageBase.Run(ReloadCurrentAsync);
	}

	private async Task ReloadFavoritesAsync()
	{
		List<CatalogItem> source = (await PageBase.Api.TryCallAsync("catalog_favorites", null, new List<CatalogItem>())) ?? new List<CatalogItem>();
		_favKeys = source.Select(FavKey).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	private static string FavKey(CatalogItem item)
	{
		return item.Source + "|" + (item.Slug ?? item.IdValue?.ToString() ?? item.Name);
	}

	private async Task ToggleFavAsync(CatalogItem item, ToggleButton star)
	{
		List<CatalogItem> source = (await PageBase.Api.CallAsync<List<CatalogItem>>("toggle_favorite", new
		{
			item = new
			{
				name = item.Name,
				source = item.Source,
				slug = item.Slug,
				id = item.IdValue
			}
		})) ?? new List<CatalogItem>();
		_favKeys = source.Select(FavKey).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool flag = _favKeys.Contains(FavKey(item));
		star.IsChecked = flag;
		star.Content = Ui.Glyph(flag ? "\ue735" : "\ue734", 13.0, flag ? "B.Accent" : "B.InkMuted");
		PageBase.Toast(flag ? I18n.L("已收藏") : I18n.L("已取消收藏"), item.Name);
		if (_mode == Mode.Favorites)
		{
			await LoadFavoritesAsync();
		}
	}

	private async Task LoadFavoritesAsync()
	{
		int token = ++_searchToken;
		CancelSearch();
		ShowPlaceholder(Ui.Skeleton(56.0));
		List<CatalogItem> list = (await PageBase.Api.TryCallAsync("catalog_favorites", null, new List<CatalogItem>())) ?? new List<CatalogItem>();
		if (token == _searchToken)
		{
			_favKeys = list.Select(FavKey).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (list.Count == 0)
			{
				ShowPlaceholder(Ui.Empty("\ue734", I18n.L("还没有收藏"), I18n.L("在搜索结果里点右边的星星就能收藏")));
			}
			else
			{
				ShowRows(list);
			}
		}
	}

	private async Task SearchAsync()
	{
		int token = ++_searchToken;
		CancellationToken ct = NextSearchToken();
		ShowPlaceholder(Ui.Skeleton(64.0), Ui.Skeleton(64.0), Ui.Skeleton(64.0));
		string query = _search.Text?.Trim() ?? "";
		_searchInFlight = true;
		try
		{
			List<CatalogItem> list = (await PageBase.Api.CallAsync<List<CatalogItem>>(_kind.SearchMethod, new
			{
				query = query,
				source = SourceParam(),
				extra = new
				{
					game_version = GameVersionFilter(),
					category = CategoryParam()
				}
			}, ct)) ?? new List<CatalogItem>();
			if (token == _searchToken)
			{
				if (list.Count == 0)
				{
					ShowPlaceholder(Ui.Empty(Glyph(), _kind.Empty, I18n.L("换个关键词，或切换来源 / 游戏版本试试")));
				}
				else
				{
					ShowRows(list);
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (token == _searchToken && !_searchInterrupted)
			{
				ShowFailure(I18n.L("搜索超时"), I18n.L("{0} 秒内没等到结果。检查网络或换个来源再试。", Math.Max(1, (SearchTimeoutMs + 999) / 1000)));
			}
		}
		catch (Exception ex2)
		{
			if (token == _searchToken)
			{
				ShowFailure(I18n.L("搜索失败"), ex2.Message);
			}
		}
		finally
		{
			if (token == _searchToken)
			{
				_searchInFlight = false;
			}
		}
	}

	private UIElement ResultCard(CatalogItem item)
	{
		ThumbTile thumbTile = new ThumbTile(item.Name, 46.0);
		thumbTile.SetUrl(item.IconUrl);
		thumbTile.VerticalAlignment = VerticalAlignment.Center;
		SPanel sPanel = Ui.H(7.0, Ui.Muted(string.IsNullOrEmpty(item.Author) ? I18n.L("佚名") : item.Author).VCenter(), Ui.Muted("·").VCenter(), Ui.Muted(Fmt.Downloads(item.Downloads) + I18n.L(" 次下载")).VCenter(), string.IsNullOrEmpty(item.Source) ? null : Ui.Tag(item.Source, "B.AccentText", "B.AccentSoft"));
		SPanel sPanel2 = Ui.V(3.0, Ui.Txt(item.Name, 13.5, bold: true).Trim(), sPanel);
		if (!string.IsNullOrWhiteSpace(item.Description))
		{
			sPanel2.Children.Add(Ui.Muted(item.Description).Trim());
		}
		List<string> tags = item.Tags;
		if (tags != null && tags.Count > 0)
		{
			SPanel sPanel3 = Ui.H(5.0);
			foreach (string item2 in item.Tags.Take(5))
			{
				sPanel3.Children.Add(Ui.Tag(item2));
			}
			sPanel2.Children.Add(sPanel3);
		}
		bool flag = _favKeys.Contains(FavKey(item));
		ToggleButton toggleButton = new ToggleButton
		{
			Style = Ui.S("Btn.Icon"),
			IsChecked = flag,
			ToolTip = I18n.L("收藏"),
			Content = Ui.Glyph(flag ? "\ue735" : "\ue734", 13.0, flag ? "B.Accent" : "B.InkMuted")
		};
		toggleButton.Click += delegate(object s, RoutedEventArgs e)
		{
			e.Handled = true;
			PageBase.Run(() => ToggleFavAsync(item, (ToggleButton)s), I18n.L("收藏失败"));
		};
		Button install = Ui.Btn(I18n.L("安装"), BtnKind.Soft, null, "\ue896");
		install.Click += delegate(object _, RoutedEventArgs e)
		{
			e.Handled = true;
			PageBase.Run(() => InstallAsync(item, install), I18n.L("安装失败"));
		};
		Grid grid = Ui.G(null, "Auto,*,Auto,Auto");
		grid.Add(thumbTile.M(0.0, 0.0, 12.0), 0);
		grid.Add(sPanel2.VCenter(), 0, 1);
		grid.Add(toggleButton.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 2);
		grid.Add(install.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 3);
		Border obj = (string.IsNullOrWhiteSpace(item.ProjectUrl) ? Ui.RowCard(grid, null, 13.0) : Ui.RowCard(grid, delegate(object _, MouseButtonEventArgs e)
		{
			if (!IsFromChildButton(e.OriginalSource as DependencyObject))
			{
				Ui.OpenUrl(item.ProjectUrl);
			}
		}, 13.0));
		obj.ToolTip = (string.IsNullOrWhiteSpace(item.ProjectUrl) ? null : I18n.L("打开介绍页"));
		Motion.HoverLift(obj, 1.004, 1.0, 16.0);
		return obj;
	}

	private static bool IsFromChildButton(DependencyObject? src)
	{
		while (src != null)
		{
			if (src is ButtonBase)
			{
				return true;
			}
			src = VisualTreeHelper.GetParent(src);
		}
		return false;
	}

	private Dictionary<string, object?> BaseExtra(string name)
	{
		return new Dictionary<string, object>
		{
			["name"] = name,
			["source"] = SourceParam(),
			["instance"] = CurrentInstance(),
			["version"] = InstalledVersion(),
			["game_version"] = GameVersionFilter(),
			["kind"] = _kind.FileKind
		};
	}

	private async Task InstallAsync(CatalogItem item, FrameworkElement anchor)
	{
		Dictionary<string, object?> extra = BaseExtra(item.Name);
		extra["source"] = (string.IsNullOrEmpty(item.Source) ? SourceParam() : item.Source);
		extra["slug"] = item.Slug;
		extra["id"] = item.IdValue;
		string slug = item.Slug;
		if ((slug != null && slug.Length > 0) || item.IdValue != null)
		{
			Dictionary<string, object> dictionary = await PickVersionFileAsync(item, extra);
			if (dictionary == null)
			{
				return;
			}
			extra = dictionary;
		}
		if (_kind.Key == "datapack")
		{
			extra = await PickSaveAsync(extra);
		}
		else if (_kind.Key == "world")
		{
			string text = await SavesDialog.PickSaveTargetAsync(CurrentInstance(), I18n.L("装进哪里 · {0}", item.Name));
			if (text == null)
			{
				return;
			}
			extra["version"] = text;
		}
		await DoInstallAsync(extra, anchor);
	}

	private async Task<Dictionary<string, object?>> PickSaveAsync(Dictionary<string, object?> extra)
	{
		string text = CurrentInstance();
		if (string.IsNullOrEmpty(text))
		{
			return extra;
		}
		List<SaveRow> list = (await PageBase.Api.TryCallAsync("list_saves", new
		{
			instance = text,
			version = ""
		}, new List<SaveRow>())) ?? new List<SaveRow>();
		if (list.Count == 0)
		{
			return extra;
		}
		ComboBox pick = Ui.Combo(new string[1] { I18n.L("不装进存档") }.Concat<string>(list.Select((SaveRow s) => s.Name)));
		if (!(await Dlg.Ask(I18n.L("装进存档"), Ui.V(8.0, Ui.Muted(I18n.L("可选：把数据包直接装进某个世界，或只放到 datapacks 文件夹。")), pick), I18n.L("继续"))))
		{
			return extra;
		}
		if (pick.SelectedIndex <= 0)
		{
			return extra;
		}
		return new Dictionary<string, object>(extra) { ["save"] = pick.Str() };
	}

	private async Task<Dictionary<string, object?>?> PickVersionFileAsync(CatalogItem item, Dictionary<string, object?> extra)
	{
		List<CatalogFile> files;
		using (Dlg.Busy(I18n.L("正在获取版本文件…")))
		{
			files = (await PageBase.Api.TryCallAsync("list_catalog_files", new { extra }, new List<CatalogFile>())) ?? new List<CatalogFile>();
		}
		if (files.Count == 0)
		{
			return (await Dlg.Confirm(I18n.L("没有找到版本文件"), I18n.L("在线列表为空，要直接尝试安装最新版吗？"), I18n.L("安装最新"), I18n.L("取消"))) ? extra : null;
		}
		CatalogFile chosen = files[0];
		SPanel listHost = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(listHost);
		smoothScroll.MaxHeight = 340.0;
		smoothScroll.MinWidth = 560.0;
		List<string> list = new List<string> { I18n.L("全部") };
		List<string> list2 = new List<string> { I18n.L("全部") };
		foreach (CatalogFile item2 in files)
		{
			foreach (string item3 in item2.GameVersions ?? new List<string>())
			{
				if (!list.Contains(item3))
				{
					list.Add(item3);
				}
			}
			foreach (string item4 in item2.Loaders ?? new List<string>())
			{
				if (!list2.Contains(item4))
				{
					list2.Add(item4);
				}
			}
		}
		ComboBox gvBox = Ui.Combo(list, I18n.L("全部"), 130.0);
		ComboBox loaderBox = Ui.Combo(list2, I18n.L("全部"), 130.0);
		TextBlock count = Ui.Muted("");
		gvBox.SelectionChanged += delegate
		{
			Refill();
		};
		loaderBox.SelectionChanged += delegate
		{
			Refill();
		};
		Refill();
		SPanel sPanel = Ui.H(8.0, Ui.Txt(I18n.L("游戏版本"), 12.0, bold: false, "B.InkMuted").VCenter(), gvBox.VCenter(), Ui.Txt(I18n.L("加载器"), 12.0, bold: false, "B.InkMuted").VCenter(), loaderBox.VCenter(), count.VCenter());
		switch (await Dlg.Choose(I18n.L("选择版本文件 · {0}", item.Name), Ui.V(10.0, sPanel, smoothScroll), new string[3]
		{
			I18n.L("取消"),
			I18n.L("安装最新"),
			I18n.L("安装所选")
		}, 660.0))
		{
		case 1:
			return extra;
		case 2:
			if (chosen != null)
			{
				Dictionary<string, object> dictionary = new Dictionary<string, object>(extra)
				{
					["version_id"] = chosen.IdValue,
					["filename"] = chosen.Filename
				};
				if ((item.Source ?? "").ToLowerInvariant().StartsWith("curse") || chosen.Source == "curseforge")
				{
					dictionary["file_id"] = chosen.IdValue;
				}
				return dictionary;
			}
			break;
		}
		return null;
		Border FileRow(CatalogFile f)
		{
			TextBlock textBlock = Ui.Txt(string.IsNullOrEmpty(f.VersionNumber) ? f.Name : f.VersionNumber, 12.5, bold: true).Trim();
			TextBlock el = Ui.Small(string.Join(", ", (f.GameVersions ?? new List<string>()).Take(4)) + " · " + string.Join(", ", f.Loaders ?? new List<string>()) + " · " + f.Date + " · " + Fmt.Downloads(f.Downloads) + " · " + (string.IsNullOrEmpty(f.ReleaseType) ? "release" : f.ReleaseType));
			Border border = Ui.RowCard(Ui.V(2.0, textBlock, el.Wrap()), null, 9.0);
			border.Tag = f;
			border.MouseLeftButtonUp += delegate
			{
				chosen = f;
				MarkSelected();
			};
			return border;
		}
		void MarkSelected()
		{
			foreach (Border item5 in listHost.Children.OfType<Border>())
			{
				bool flag = item5.Tag == chosen;
				item5.SetResourceReference(Border.BorderBrushProperty, flag ? "B.Accent" : "B.Line");
				item5.SetResourceReference(Border.BackgroundProperty, flag ? "B.AccentSoft" : "B.Paper");
			}
		}
		void Refill()
		{
			string gv = gvBox.Str();
			string ld = loaderBox.Str();
			bool anyGv = gvBox.SelectedIndex <= 0;
			bool anyLd = loaderBox.SelectedIndex <= 0;
			List<CatalogFile> list3 = files.Where(delegate(CatalogFile f)
			{
				if (!anyGv)
				{
					List<string> gameVersions = f.GameVersions;
					if (gameVersions != null && gameVersions.Count > 0 && !f.GameVersions.Contains(gv))
					{
						return false;
					}
				}
				if (!anyLd)
				{
					List<string> loaders = f.Loaders;
					if (loaders != null && loaders.Count > 0 && !f.Loaders.Any((string l) => string.Equals(l, ld, StringComparison.OrdinalIgnoreCase)))
					{
						return false;
					}
				}
				return true;
			}).ToList();
			if (list3.Count > 0 && !list3.Contains(chosen))
			{
				chosen = list3[0];
			}
			listHost.Children.Clear();
			foreach (CatalogFile item6 in list3.Take(80))
			{
				listHost.Children.Add(FileRow(item6));
			}
			count.Text = I18n.L("{0} 个匹配 / 共 {1} 个文件", list3.Count, files.Count);
			MarkSelected();
		}
	}

	private async Task<string> DoInstallAsync(Dictionary<string, object?> extra, FrameworkElement? anchor)
	{
		object value;
		string name = ((!extra.TryGetValue("name", out value)) ? _kind.Title : (value?.ToString() ?? _kind.Title));
		BridgeClient api;
		string installMethod;
		object obj;
		if (_kind.IsModpack)
		{
			api = PageBase.Api;
			installMethod = _kind.InstallMethod;
			if (extra.TryGetValue("source", out object value2))
			{
				string text = value2?.ToString();
				if (text != null && text.Length > 0)
				{
					obj = text;
					goto IL_015a;
				}
			}
			obj = "Modrinth";
			goto IL_015a;
		}
		string result = await PageBase.Api.StartTaskAsync(_kind.InstallMethod, new
		{
			name = name,
			instance = CurrentInstance(),
			extra = extra
		});
		goto IL_01df;
		IL_015a:
		result = await api.StartTaskAsync(installMethod, new
		{
			name = name,
			source = (string)obj,
			extra = extra
		});
		goto IL_01df;
		IL_01df:
		if (anchor != null && SettingsPage.FlyEnabled)
		{
			PageBase.Win?.FlyToTasks(anchor, name);
		}
		return result;
	}

	private async Task InstallFromLinkAsync(FrameworkElement anchor)
	{
		string text = await Dlg.Prompt(_kind.Title + I18n.L(" · 链接安装"), _kind.LinkHint, "", "https://…");
		if (!string.IsNullOrWhiteSpace(text))
		{
			Dictionary<string, object> dictionary = BaseExtra(text.Trim());
			dictionary["url"] = text.Trim();
			await DoInstallAsync(dictionary, anchor);
			PageBase.Win?.Navigate("tasks");
		}
	}

	private void ImportLocal(FrameworkElement anchor)
	{
		string[] files = Dlg.PickFiles(_kind.LocalFilter, I18n.L("导入本地") + _kind.Title);
		if (files == null || files.Length == 0)
		{
			return;
		}
		PageBase.Run(async delegate
		{
			if (SettingsPage.FlyEnabled)
			{
				PageBase.Win?.FlyToTasks(anchor, I18n.L("{0} 个文件", files.Length));
			}
			PageBase.Win?.Navigate("tasks");
			string[] array = files;
			string[] array2 = array;
			foreach (string text in array2)
			{
				Dictionary<string, object> dictionary = BaseExtra(Path.GetFileName(text));
				dictionary["path"] = text;
				dictionary["source"] = "本地";
				string text2 = await DoInstallAsync(dictionary, null);
				if (!string.IsNullOrEmpty(text2))
				{
					await PageBase.Api.TryCallAsync<OpResult>("wait_task", new
					{
						task_id = text2,
						timeout = 1800
					});
				}
			}
			if (_mode != Mode.Search)
			{
				await ReloadCurrentAsync();
			}
		}, I18n.L("导入失败"));
	}

	private async Task LoadInstalledAsync()
	{
		int token = ++_searchToken;
		CancelSearch();
		ShowPlaceholder(Ui.Skeleton(46.0));
		string inst = CurrentInstance();
		if (string.IsNullOrEmpty(inst))
		{
			ShowPlaceholder(Ui.Empty(Glyph(), I18n.L("还没有实例"), I18n.L("先到「实例」页新建一个")));
			return;
		}
		await FillInstalledVersionsAsync(inst);
		string version = InstalledVersion();
		try
		{
			if (_kind.Key == "mod")
			{
				List<ModEntry> list = (await PageBase.Api.TryCallAsync("get_installed_mod_entries", new
				{
					instance = inst,
					version = version
				}, new List<ModEntry>())) ?? new List<ModEntry>();
				if (token == _searchToken)
				{
					if (list.Count == 0)
					{
						ShowPlaceholder(Ui.Empty(Glyph(), I18n.L("这个实例还没有 Mod"), I18n.L("切回「搜索下载」装一个")));
					}
					else
					{
						ShowRows(list);
					}
				}
				return;
			}
			List<string> list2 = (await PageBase.Api.TryCallAsync(_kind.InstalledMethod, new
			{
				instance = inst
			}, new List<string>())) ?? new List<string>();
			if (token == _searchToken)
			{
				if (list2.Count == 0)
				{
					ShowPlaceholder(Ui.Empty(Glyph(), I18n.L("这个实例还没有{0}", _kind.Title), I18n.L("切回「搜索下载」装一个")));
				}
				else
				{
					_installedNames = list2;
					ShowRows(list2);
				}
			}
		}
		catch (Exception ex)
		{
			if (token == _searchToken)
			{
				ShowPlaceholder(Ui.Empty("\ue7ba", I18n.L("读取失败"), ex.Message));
			}
		}
	}

	private async Task FillInstalledVersionsAsync(string instance)
	{
		List<ModsTarget> list = (await PageBase.Api.TryCallAsync("get_mods_targets", new { instance }, new List<ModsTarget>())) ?? new List<ModsTarget>();
		if (list.Count == 0)
		{
			list.Add(new ModsTarget
			{
				Label = I18n.L("实例共享目录"),
				Value = ""
			});
		}
		int selectedIndex = _installedVer.SelectedIndex;
		_sync = true;
		_installedVer.Items.Clear();
		foreach (ModsTarget item in list)
		{
			_installedVer.Items.Add(item.Label);
		}
		_installedVer.SelectedIndex = ((selectedIndex >= 0 && selectedIndex < list.Count) ? selectedIndex : 0);
		_installedVerValues = list.Select((ModsTarget t) => t.Value).ToList();
		_sync = false;
	}

	private UIElement ModRow(ModEntry m)
	{
		string inst = CurrentInstance();
		string ver = InstalledVersion();
		string file = m.Filename;
		CheckBox checkBox = Ui.Switch(m.Enabled);
		checkBox.Checked += delegate
		{
			PageBase.Run(() => PageBase.Api.TryCallAsync<object>("enable_mod", new
			{
				instance = inst,
				filename = file,
				version = ver
			}));
		};
		checkBox.Unchecked += delegate
		{
			PageBase.Run(() => PageBase.Api.TryCallAsync<object>("disable_mod", new
			{
				instance = inst,
				filename = file,
				version = ver
			}));
		};
		Button el = Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteInstalledAsync(file), I18n.L("删除失败"));
		});
		Grid grid = Ui.G(null, "Auto,*,Auto,Auto");
		grid.Add(checkBox.M(0.0, 0.0, 10.0).VCenter(), 0);
		grid.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name).Trim(), Ui.Small(string.Join(" · ", new string[3] { file, m.Version, m.Size }.Where((string x) => !string.IsNullOrEmpty(x))))).VCenter(), 0, 1);
		grid.Add(Ui.Small(m.Enabled ? "" : I18n.L("已禁用")).VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 2);
		grid.Add(el.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 3);
		return Ui.RowCard(grid, null, 10.0);
	}

	private UIElement InstalledRow(string file)
	{
		Button el = Ui.Btn(_kind.IsModpack ? I18n.L("移除标记") : I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteInstalledAsync(file), I18n.L("删除失败"));
		});
		Grid grid = Ui.G(null, "*,Auto,Auto");
		grid.Add(Ui.Txt(file, 12.5).Trim().VCenter(), 0);
		if (ExportKind().Length > 0)
		{
			grid.Add(Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
			{
				PageBase.Run(() => ExportInstalledAsync(file), I18n.L("导出失败"));
			}, "\uede1").VCenter().M(0.0, 0.0, 6.0), 0, 1);
		}
		grid.Add(el.VCenter(), 0, 2);
		return Ui.RowCard(grid, null, 10.0);
	}

	private string ExportKind()
	{
		return _kind.Key switch
		{
			"mod" => "mod", 
			"shader" => "shader", 
			"resourcepack" => "resourcepack", 
			"datapack" => "datapack", 
			"world" => "world", 
			_ => "", 
		};
	}

	private async Task ExportInstalledAsync(string name)
	{
		string kind = ExportKind();
		if (kind.Length != 0)
		{
			string text = (await PageBase.Api.TryCallAsync("default_export_dir", null, "")) ?? "";
			string folder = Dlg.PickFolder(string.IsNullOrEmpty(text) ? I18n.L("选择导出位置") : I18n.L("选择导出位置（上次：{0}）", text));
			if (folder != null)
			{
				await PageBase.Api.TryCallAsync("remember_export_dir", new
				{
					path = folder
				}, folder);
				string text2 = await PageBase.Api.CallAsync<string>("export_content", new
				{
					kind = kind,
					name = name,
					dest_dir = folder,
					version = InstalledVersion(),
					instance = CurrentInstance()
				});
				PageBase.Toast(I18n.L("已导出"), text2 ?? folder, ToastKind.Success);
			}
		}
	}

	private async Task ExportAllInstalledAsync(List<string> names)
	{
		string kind = ExportKind();
		if (kind.Length == 0 || names.Count == 0)
		{
			return;
		}
		string text = (await PageBase.Api.TryCallAsync("default_export_dir", null, "")) ?? "";
		string folder = Dlg.PickFolder(string.IsNullOrEmpty(text) ? I18n.L("选择导出位置") : I18n.L("选择导出位置（上次：{0}）", text));
		if (folder == null)
		{
			return;
		}
		await PageBase.Api.TryCallAsync("remember_export_dir", new
		{
			path = folder
		}, folder);
		ExportResult exportResult;
		using (Dlg.Busy(I18n.L("正在导出 {0} 个…", names.Count)))
		{
			exportResult = await PageBase.Api.CallAsync<ExportResult>("export_contents", new
			{
				kind = kind,
				names = names,
				dest_dir = folder,
				version = InstalledVersion(),
				instance = CurrentInstance()
			});
		}
		if (exportResult != null)
		{
			if (exportResult.Failed.Count == 0)
			{
				PageBase.Toast(I18n.L("已导出"), I18n.L("{0} 个 → {1}", exportResult.Ok.Count, folder), ToastKind.Success);
			}
			else
			{
				await Dlg.Alert(I18n.L("部分没能导出"), I18n.L("成功 {0} 个，失败 {1} 个：\n\n", exportResult.Ok.Count, exportResult.Failed.Count) + string.Join("\n", exportResult.Failed));
			}
		}
	}

	private void SniffClipboard()
	{
		string text;
		try
		{
			text = (Clipboard.GetText() ?? "").Trim();
		}
		catch
		{
			return;
		}
		if (text.Length == 0 || text == _clipSeen)
		{
			return;
		}
		string text2 = text.ToLowerInvariant();
		if (text2.Contains("modrinth.com") || text2.Contains("curseforge.com"))
		{
			_clipSeen = text;
			if ((_search.Text ?? "").Trim().Length == 0)
			{
				_search.Text = text;
			}
			PageBase.Toast(I18n.L("识别到剪贴板链接"), (text.Length > 96) ? (text.Substring(0, 96) + "…") : text);
		}
	}

	private async Task DeleteInstalledAsync(string filename)
	{
		if (string.IsNullOrEmpty(_kind.DeleteMethod))
		{
			return;
		}
		string inst = CurrentInstance();
		if (_kind.DeleteMethod == "delete_modpack")
		{
			if (!(await Dlg.Confirm(I18n.L("移除整合包标记"), I18n.L("只清掉「已安装整合包」这条记录，游戏目录里的模组与存档一个不动。\n要腾空间的话，到「原版游戏」里逐个卸载版本。"), I18n.L("移除标记"), I18n.L("取消"))))
			{
				return;
			}
			await PageBase.Api.CallAsync("delete_modpack", new
			{
				instance = inst,
				filename = filename,
				purge_instance = false
			});
		}
		else
		{
			if (!(await Dlg.Confirm(I18n.L("删除确认"), I18n.L("将删除「{0}」，不可恢复。", filename), I18n.L("删除"), I18n.L("取消"), danger: true)))
			{
				return;
			}
			if (!(_kind.DeleteMethod == "delete_mod"))
			{
				await PageBase.Api.CallAsync(_kind.DeleteMethod, new
				{
					instance = inst,
					filename = filename
				});
			}
			else
			{
				await PageBase.Api.CallAsync(_kind.DeleteMethod, new
				{
					instance = inst,
					filename = filename,
					version = ""
				});
			}
		}
		PageBase.Toast(I18n.L("已处理"), filename, ToastKind.Success);
		await LoadInstalledAsync();
	}

	public override void OnEvent(BridgeEvent ev)
	{
		if (ev.Event == "finished" && ev.Success && _mode == Mode.Installed)
		{
			PageBase.Run(LoadInstalledAsync);
		}
	}

	public override void OnShown()
	{
		SniffClipboard();
		if (_searchInterrupted)
		{
			PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
		}
	}

	public override void OnHidden()
	{
		_debounce.Stop();
		if (_searchInFlight)
		{
			_searchInterrupted = true;
			CancelSearch();
		}
	}
}
