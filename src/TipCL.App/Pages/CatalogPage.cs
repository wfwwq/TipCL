using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C6a:Windows 版 <c>Pages\CatalogPage.cs</c>(35399 字节 / 1309 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **目录页**(模组 / 整合包 / 数据包 / 资源包 / 光影 / 世界,六个种类共用这一个类,由 <see cref="CatalogKind"/> 参数化)。
///
/// <b>它不是导航页</b>:Windows 的导航表里没有它的 id,它是被 <see cref="DownloadPage"/> 的分段**换进页内容**里的
/// (mac 侧照 Windows 一样处理 —— 不注册页面、不给 id)。
///
/// 形状(逐条照抄 Windows):标题行(种类标题 + 副说明 + 搜索/已安装/收藏三个模式钮)+ 筛选卡(搜索框 + 搜索钮 +
/// 重置条件 / 链接安装 / 导入本地 / 检查更新 / 全部导出 / 刷新 + 来源 / 类型 / 游戏版本 / 实例 / 已装于版本五个下拉)
/// + 结果区(虚拟化列表 / 占位面板二选一)。
///
/// 行为(与 Windows 一致):三个模式(搜索下载 / 已安装 / 收藏)各走各的后端方法(种类里的 <c>SearchMethod</c> /
/// <c>InstalledMethod</c> / <c>catalog_favorites</c>);搜索带 320ms 防抖 + 超时令牌(超时 / 失败都给可重试的占位);
/// 收藏走 <c>toggle_favorite</c>;安装走「拿到版本文件列表 → 选文件 → <c>_kind.InstallMethod</c> 起任务」,
/// 数据包多问一次「装进哪个存档」、世界走 <c>SavesDialog.PickSaveTargetAsync</c>;已安装模式可启用/禁用/删除/导出
/// (<c>enable_mod</c> / <c>disable_mod</c> / <c>_kind.DeleteMethod</c> / <c>export_content</c> / <c>export_contents</c>)。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Style = Ui.S("键")</c> → <c>StyleKeys.Apply(...)</c>;<c>SetResourceReference</c> → <c>.Dyn(...)</c>;
///   <c>Visibility</c> → <c>IsVisible</c>;</item>
/// <item><c>RowCard(grid, (object, MouseButtonEventArgs) =&gt; ...)</c> → mac 的 <c>Ui.RowCard</c> 收的是
///   <see cref="PointerReleasedEventArgs"/>,所以"这层是不是点在子按钮上"改判 <c>e.Source as Visual</c> 并沿
///   <c>GetVisualParent()</c> 上溯(WPF 的 <c>VisualTreeHelper.GetParent</c>,见 D-131 那一族差异);
///   <c>is ButtonBase</c> → <c>is Button</c>(Avalonia 的 <c>ToggleButton</c> 派生自 <c>Button</c>,覆盖面相同);</item>
/// <item><c>Clipboard.GetText()</c>(WPF 有静态剪贴板入口,Avalonia 11 **没有**)→ 注入缝 <see cref="ClipboardReader"/>,
///   与 <c>CrashUi.ClipboardWriter</c> 同一模式:测试与离屏(<c>Dlg.SmokeActive</c>)换掉 / 跳过,**绝不碰真剪贴板**;</item>
/// <item><c>Ui.VirtualList&lt;object&gt;</c> 的委托类型收成 <c>Func&lt;object, Control&gt;</c>(同 DownloadPage 的说明)。</item>
/// </list>
/// </summary>
public sealed class CatalogPage : PageBase
{
	/// <summary>三种模式(Windows 的私有嵌套枚举,顺序一字不改)。mac 侧降为 internal:切换模式是**测试缝**
	/// (D-115 / D-127 的口径 —— 用例驱动 internal 状态机,不模拟真实输入设备)。</summary>
	internal enum Mode
	{
		Search,
		Installed,
		Favorites,
	}

	/// <summary>【Windows 源】<c>:28-51</c> 游戏版本下拉的候选(21 项,顺序一字不改)。</summary>
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
		"1.7.10",
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

	/// <summary>搜索框的 320ms 防抖(Windows 是 <c>System.Windows.Threading.DispatcherTimer</c>,Avalonia 同名同义)。</summary>
	private readonly DispatcherTimer _debounce = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(320.0),
	};

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private HashSet<string> _favKeys = new HashSet<string>();

	private bool _sync;

	private Mode _mode;

	private int _searchToken;

	/// <summary>搜索超时(Windows 是 <c>Smoke --smoke-search-timeout</c> 会同步调整的静态)。</summary>
	public static int SearchTimeoutMs = 9000;

	private CancellationTokenSource? _searchCts;

	private bool _searchInFlight;

	private bool _searchInterrupted;

	private List<string> _installedVerValues = new List<string>();

	private List<string> _installedNames = new List<string>();

	public override string Title => _kind.Title;

	// ================================================================= 注入缝(C6a · D 类)

	/// <summary>
	/// 【C6a 注入缝】读剪贴板(Windows 是静态的 <c>Clipboard.GetText()</c>;Avalonia 11 没有那个入口)。
	/// 返回 "" 表示"没有 / 拿不到"。测试与离屏换掉它,**绝不碰真剪贴板**(与 <c>CrashUi.ClipboardWriter</c> 同一模式)。
	/// </summary>
	internal static Func<string>? ClipboardReader { get; set; }

	public CatalogPage(CatalogKind kind)
	{
		_kind = kind;
		_mode = Mode.Search;
		_search.Tag = I18n.L("搜索{0},留空看热门", kind.Title);
		string[] array = new string[3]
		{
			I18n.L("全部"),
			"Modrinth",
			"CurseForge",
		};
		_source = Ui.Combo(array, array.Contains(kind.DefaultSource) ? kind.DefaultSource : I18n.L("全部"), 116.0);
		_type = Ui.Combo(kind.Types.Select((string t) => I18n.L(t)), I18n.L("全部"), 116.0);
		_modeSearch = new ToggleButton
		{
			Content = I18n.L("搜索下载"),
			IsChecked = true,
		};
		StyleKeys.Apply(_modeSearch, "TabItemBtn");
		_modeInstalled = new ToggleButton
		{
			Content = I18n.L("已安装"),
		};
		StyleKeys.Apply(_modeInstalled, "TabItemBtn");
		_modeFav = new ToggleButton
		{
			Content = I18n.L("收藏"),
		};
		StyleKeys.Apply(_modeFav, "TabItemBtn");
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
		Button searchBtn = Ui.Btn(I18n.L("搜索"), BtnKind.Primary, delegate
		{
			PageBase.Run(SearchAsync, I18n.L("搜索失败"));
		}, "\ue721");
		Button resetBtn = Ui.Btn(I18n.L("重置条件"), BtnKind.Chip, delegate
		{
			ResetFilters();
		}, "\ue72c");
		Button checkBtn = Ui.Btn(I18n.L("检查更新"), BtnKind.Chip, delegate
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
		checkBtn.IsVisible = _kind.Key == "mod";
		Button exportAllBtn = Ui.Btn(I18n.L("全部导出"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => ExportAllInstalledAsync(_installedNames), I18n.L("导出失败"));
		}, "\uede1");
		exportAllBtn.IsVisible = ExportKind().Length > 0;
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
		Button refreshBtn = Ui.IconBtn("\ue72c", I18n.L("刷新"), delegate
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
		_search.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
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
		grid.Add(searchBtn.M(8.0, 0.0, 0.0, 0.0), 0, 1);
		WrapPanel filters = new WrapPanel();
		Labeled(I18n.L("来源"), _source);
		Labeled(I18n.L("类型"), _type);
		Labeled(I18n.L("游戏版本"), _version);
		Labeled(I18n.L("实例"), _inst);
		if (_kind.InstalledMethod.Length > 0)
		{
			Labeled(I18n.L("已装于版本"), _installedVer);
		}
		filters.Children.Add(Ui.H(7.0, resetBtn, linkBtn, localBtn, checkBtn, exportAllBtn, refreshBtn).M(0.0, 0.0, 0.0, 8.0));
		Border filterCard = Ui.Card(Ui.V(10.0, grid, filters), 14.0);
		SPanel right = Ui.H(6.0, _modeSearch, (_kind.InstalledMethod.Length > 0) ? _modeInstalled : null, _modeFav);
		Grid section = Ui.Section(Title, SubTitle(), right);
		_results = Ui.VirtualList<object>(BuildRow);
		_listHost.Children.Add(_results);
		_listHost.Children.Add(Ui.Scroll(_placeholder));
		Grid root = Ui.G("Auto,Auto,*");
		root.Add(section, 0);
		root.Add(filterCard.M(0.0, 14.0, 0.0, 14.0), 1);
		root.Add(_listHost, 2);
		root.Margin = new Thickness(26.0, 20.0, 26.0, 20.0);
		base.Content = root;
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = CurrentInstance(),
			PreferKind = PreferredKind(),
		});
		void Labeled(string label, Control ctl)
		{
			filters.Children.Add(Ui.H(6.0, Ui.Txt(label, 12.0, bold: false, "B.InkMuted").VCenter(), ctl.VCenter()).M(0.0, 0.0, 14.0, 8.0));
		}
	}

	/// <summary>【Windows 源】<c>:296-308</c>:种类 → 拖放首选的落点类型。</summary>
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

	/// <summary>【Windows 源】<c>:310-321</c>:标题下的副说明(逐个种类一句话)。</summary>
	private string SubTitle()
	{
		return _kind.Key switch
		{
			"mod" => I18n.L("从 Modrinth / CurseForge 搜模组,装进所选实例"),
			"modpack" => I18n.L("一键安装整合包到独立实例"),
			"datapack" => I18n.L("数据包会放进实例 datapacks 目录"),
			"resourcepack" => I18n.L("资源包会放进实例 resourcepacks 目录"),
			"shader" => I18n.L("光影包会放进实例 shaderpacks 目录"),
			_ => I18n.L("世界地图会解压成实例存档"),
		};
	}

	/// <summary>【Windows 源】<c>:323-334</c>:空态图标(逐个种类一个字形码位)。</summary>
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

	/// <summary>【Windows 源】<c>:351-361</c>:重置筛选条件(置 <c>_sync</c> 免得逐个触发搜索)。</summary>
	internal void ResetFilters()
	{
		_sync = true;
		_search.Clear();
		_source.SelectedItem = _source.Items.Contains(_kind.DefaultSource) ? _kind.DefaultSource : I18n.L("全部");
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

	/// <summary>【Windows 源】<c>:393-404</c>:列表与占位面板二选一(占位面板是 <c>_listHost.Children[1]</c>)。</summary>
	private void ShowPlaceholder(params Control[] kids)
	{
		_results.ItemsSource = null;
		_placeholder.Children.Clear();
		foreach (Control element in kids)
		{
			_placeholder.Children.Add(element);
		}
		_placeholder.IsVisible = true;
		_listHost.Children[1].IsVisible = true;
		_results.IsVisible = false;
	}

	private void ShowRows(IEnumerable rows)
	{
		_placeholder.Children.Clear();
		_listHost.Children[1].IsVisible = false;
		_results.IsVisible = true;
		_results.ItemsSource = rows;
		if (_results.Items.Count > 0)
		{
			_results.ScrollIntoView(_results.Items[0]!);
		}
	}

	/// <summary>【Windows 源】<c>:418-433</c> 行工厂:三种数据(目录项 / 已装模组 / 已装文件名)各画各的。</summary>
	private Control BuildRow(object item)
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

	/// <summary>【Windows 源】<c>:435-443</c>:首屏 = 实例列表 → 收藏集合 → 当前模式重载一次。</summary>
	protected override async Task LoadAsync()
	{
		_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_sync = true;
		_inst.Fill(_instances.Select((InstanceInfo i) => i.Name), PageBase.Win?.Prefs.CatalogInstance);
		_sync = false;
		await ReloadFavoritesAsync();
		PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
	}

	/// <summary>【Windows 源】<c>:445-469</c>:每次搜索换一个超时令牌,并把上一次的取消掉。</summary>
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
		CancellationTokenSource? searchCts = _searchCts;
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

	/// <summary>【Windows 源】<c>:471-480</c>:失败 / 超时的占位面板 + 一颗「重试」。</summary>
	private void ShowFailure(string title, string detail)
	{
		Button retry = Ui.Btn(I18n.L("重试"), BtnKind.Soft, delegate
		{
			PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
		}, "\ue72c");
		retry.HorizontalAlignment = HorizontalAlignment.Center;
		retry.Margin = new Thickness(0.0, -30.0, 0.0, 0.0);
		ShowPlaceholder(Ui.Empty("\ue7ba", title, detail), retry);
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

	/// <summary>【Windows 源】<c>:492-503</c>:切模式(种类没有「已安装」方法时退回搜索模式)。</summary>
	internal void SetMode(Mode mode)
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
		_favKeys = source.Select(FavKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>【Windows 源】<c>:511-514</c>:收藏的去重键(来源 + slug/id/名字)。</summary>
	private static string FavKey(CatalogItem item)
	{
		return item.Source + "|" + (item.Slug ?? item.IdValue?.ToString() ?? item.Name);
	}

	/// <summary>【Windows 源】<c>:516-537</c>:收藏 / 取消收藏(<c>toggle_favorite</c> 回全量列表,再本地重算)。</summary>
	private async Task ToggleFavAsync(CatalogItem item, ToggleButton star)
	{
		List<CatalogItem> source = (await PageBase.Api.CallAsync<List<CatalogItem>>("toggle_favorite", new
		{
			item = new
			{
				name = item.Name,
				source = item.Source,
				slug = item.Slug,
				id = item.IdValue,
			},
		})) ?? new List<CatalogItem>();
		_favKeys = source.Select(FavKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
			_favKeys = list.Select(FavKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

	/// <summary>【Windows 源】<c>:559-611</c>:搜索(带超时令牌;超时 / 失败都给可重试的占位,且只认最后一次令牌)。</summary>
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
				query,
				source = SourceParam(),
				extra = new
				{
					game_version = GameVersionFilter(),
					category = CategoryParam(),
				},
			}, ct)) ?? new List<CatalogItem>();
			if (token == _searchToken)
			{
				if (list.Count == 0)
				{
					ShowPlaceholder(Ui.Empty(Glyph(), _kind.Empty, I18n.L("换个关键词,或切换来源 / 游戏版本试试")));
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

	/// <summary>【Windows 源】<c>:613-668</c>:一张结果卡(缩略图 + 名字/作者/下载量/来源/简介/标签 + 收藏星 + 安装)。</summary>
	private Control ResultCard(CatalogItem item)
	{
		ThumbTile thumbTile = new ThumbTile(item.Name, 46.0);
		thumbTile.SetUrl(item.IconUrl);
		thumbTile.VerticalAlignment = VerticalAlignment.Center;
		SPanel meta = Ui.H(7.0,
			Ui.Muted(string.IsNullOrEmpty(item.Author) ? I18n.L("佚名") : item.Author).VCenter(),
			Ui.Muted("·").VCenter(),
			Ui.Muted(Fmt.Downloads(item.Downloads) + I18n.L(" 次下载")).VCenter(),
			string.IsNullOrEmpty(item.Source) ? null : Ui.Tag(item.Source, "B.AccentText", "B.AccentSoft"));
		SPanel body = Ui.V(3.0, Ui.Txt(item.Name, 13.5, bold: true).Trim(), meta);
		if (!string.IsNullOrWhiteSpace(item.Description))
		{
			body.Children.Add(Ui.Muted(item.Description).Trim());
		}
		List<string>? tags = item.Tags;
		if (tags != null && tags.Count > 0)
		{
			SPanel tagRow = Ui.H(5.0);
			foreach (string tag in item.Tags!.Take(5))
			{
				tagRow.Children.Add(Ui.Tag(tag));
			}
			body.Children.Add(tagRow);
		}
		bool fav = _favKeys.Contains(FavKey(item));
		ToggleButton star = new ToggleButton
		{
			IsChecked = fav,
			Content = Ui.Glyph(fav ? "\ue735" : "\ue734", 13.0, fav ? "B.Accent" : "B.InkMuted"),
		};
		StyleKeys.Apply(star, "Btn.Icon");
		ToolTip.SetTip(star, I18n.L("收藏"));
		star.Click += delegate(object? s, RoutedEventArgs e)
		{
			e.Handled = true;
			PageBase.Run(() => ToggleFavAsync(item, (ToggleButton)s!), I18n.L("收藏失败"));
		};
		Button install = Ui.Btn(I18n.L("安装"), BtnKind.Soft, null, "\ue896");
		install.Click += delegate(object? _, RoutedEventArgs e)
		{
			e.Handled = true;
			PageBase.Run(() => InstallAsync(item, install), I18n.L("安装失败"));
		};
		Grid grid = Ui.G(null, "Auto,*,Auto,Auto");
		grid.Add(thumbTile.M(0.0, 0.0, 12.0), 0);
		grid.Add(body.VCenter(), 0, 1);
		grid.Add(star.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 2);
		grid.Add(install.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 3);
		Border card = string.IsNullOrWhiteSpace(item.ProjectUrl)
			? Ui.RowCard(grid, null, 13.0)
			: Ui.RowCard(grid, delegate(object? _, PointerReleasedEventArgs e)
			{
				if (!IsFromChildButton(e.Source as Visual))
				{
					Ui.OpenUrl(item.ProjectUrl);
				}
			}, 13.0);
		ToolTip.SetTip(card, string.IsNullOrWhiteSpace(item.ProjectUrl) ? null : I18n.L("打开介绍页"));
		Motion.HoverLift(card, 1.004, 1.0, 16.0);
		return card;
	}

	/// <summary>【Windows 源】<c>:670-681</c>:这一点是不是落在子按钮上(WPF 看 <c>ButtonBase</c> 祖先)。</summary>
	private static bool IsFromChildButton(Visual? src)
	{
		Visual? d = src;
		while (d != null)
		{
			if (d is Button)
			{
				return true;
			}
			d = d.GetVisualParent();
		}
		return false;
	}

	/// <summary>【Windows 源】<c>:683-694</c>:安装要发给后端的那批 extra 字段。</summary>
	private Dictionary<string, object?> BaseExtra(string name)
	{
		return new Dictionary<string, object?>
		{
			["name"] = name,
			["source"] = SourceParam(),
			["instance"] = CurrentInstance(),
			["version"] = InstalledVersion(),
			["game_version"] = GameVersionFilter(),
			["kind"] = _kind.FileKind,
		};
	}

	/// <summary>【Windows 源】<c>:696-726</c>:安装一条结果(先挑版本文件;数据包 / 世界各多一步)。</summary>
	private async Task InstallAsync(CatalogItem item, Control anchor)
	{
		Dictionary<string, object?> extra = BaseExtra(item.Name);
		extra["source"] = string.IsNullOrEmpty(item.Source) ? SourceParam() : item.Source;
		extra["slug"] = item.Slug;
		extra["id"] = item.IdValue;
		string? slug = item.Slug;
		if ((slug != null && slug.Length > 0) || item.IdValue != null)
		{
			Dictionary<string, object?>? chosen = await PickVersionFileAsync(item, extra);
			if (chosen == null)
			{
				return;
			}
			extra = chosen;
		}
		if (_kind.Key == "datapack")
		{
			extra = await PickSaveAsync(extra);
		}
		else if (_kind.Key == "world")
		{
			string? target = await SavesDialog.PickSaveTargetAsync(CurrentInstance(), I18n.L("装进哪里 · {0}", item.Name));
			if (target == null)
			{
				return;
			}
			extra["version"] = target;
		}
		await DoInstallAsync(extra, anchor);
	}

	/// <summary>
	/// 【C6a 测试缝】驱动"点一行里的安装"那一步(处理器体抽成 internal —— D-115 / D-127 的口径)。
	/// 需要它的原因:离屏夹具里 <c>Ui.VirtualList</c> 的**行控件不会实例化**(视口为 0 的平台限制,
	/// 见 C6a 的 DECISIONS),所以"点那一行"只能驱动这一层;真机走的是同一段代码(事件处理器只是薄壳)。
	/// </summary>
	internal Task InstallRowForTest(CatalogItem item)
	{
		return InstallAsync(item, new Button());
	}

	/// <summary>【C6a 测试缝】驱动"点一行里的收藏星"那一步(同 <see cref="InstallRowForTest"/>)。</summary>
	internal Task ToggleFavRowForTest(CatalogItem item)
	{
		return ToggleFavAsync(item, new ToggleButton());
	}

	/// <summary>【Windows 源】<c>:728-754</c>:数据包问一次「装进哪个存档」(可选,不选就只放 datapacks 目录)。</summary>
	private async Task<Dictionary<string, object?>> PickSaveAsync(Dictionary<string, object?> extra)
	{
		string current = CurrentInstance();
		if (string.IsNullOrEmpty(current))
		{
			return extra;
		}
		List<SaveRow> list = (await PageBase.Api.TryCallAsync("list_saves", new
		{
			instance = current,
			version = "",
		}, new List<SaveRow>())) ?? new List<SaveRow>();
		if (list.Count == 0)
		{
			return extra;
		}
		ComboBox pick = Ui.Combo(new string[1] { I18n.L("不装进存档") }.Concat(list.Select((SaveRow s) => s.Name)));
		if (!(await Dlg.Ask(I18n.L("装进存档"), Ui.V(8.0, Ui.Muted(I18n.L("可选:把数据包直接装进某个世界,或只放到 datapacks 文件夹。")), pick), I18n.L("继续"))))
		{
			return extra;
		}
		if (pick.SelectedIndex <= 0)
		{
			return extra;
		}
		return new Dictionary<string, object?>(extra) { ["save"] = pick.Str() };
	}

	/// <summary>
	/// 【Windows 源】<c>:756-890</c>:拉版本文件列表 → 让用户按游戏版本 / 加载器筛一遍再选一个
	/// (取消 → null;「安装最新」→ 原样;「安装所选」→ 带上 version_id / filename)。
	/// </summary>
	private async Task<Dictionary<string, object?>?> PickVersionFileAsync(CatalogItem item, Dictionary<string, object?> extra)
	{
		List<CatalogFile> files;
		using (Dlg.Busy(I18n.L("正在获取版本文件...")))
		{
			files = (await PageBase.Api.TryCallAsync("list_catalog_files", new
			{
				extra,
			}, new List<CatalogFile>())) ?? new List<CatalogFile>();
		}
		if (files.Count == 0)
		{
			return (await Dlg.Confirm(I18n.L("没有找到版本文件"), I18n.L("在线列表为空,要直接尝试安装最新版吗?"), I18n.L("安装最新"), I18n.L("取消"))) ? extra : null;
		}
		CatalogFile chosen = files[0];
		SPanel listHost = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(listHost);
		smoothScroll.BindWindowMaxHeight(0.42, 200.0);
		smoothScroll.MinWidth = 560.0;
		List<string> versions = new List<string> { I18n.L("全部") };
		List<string> loaders = new List<string> { I18n.L("全部") };
		foreach (CatalogFile f in files)
		{
			foreach (string gv in f.GameVersions ?? new List<string>())
			{
				if (!versions.Contains(gv))
				{
					versions.Add(gv);
				}
			}
			foreach (string ld in f.Loaders ?? new List<string>())
			{
				if (!loaders.Contains(ld))
				{
					loaders.Add(ld);
				}
			}
		}
		ComboBox gvBox = Ui.Combo(versions, I18n.L("全部"), 130.0);
		ComboBox loaderBox = Ui.Combo(loaders, I18n.L("全部"), 130.0);
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
		SPanel bar = Ui.H(8.0,
			Ui.Txt(I18n.L("游戏版本"), 12.0, bold: false, "B.InkMuted").VCenter(),
			gvBox.VCenter(),
			Ui.Txt(I18n.L("加载器"), 12.0, bold: false, "B.InkMuted").VCenter(),
			loaderBox.VCenter(),
			count.VCenter());
		switch (await Dlg.Choose(I18n.L("选择版本文件 · {0}", item.Name), Ui.V(10.0, bar, smoothScroll), new string[3]
		{
			I18n.L("取消"),
			I18n.L("安装最新"),
			I18n.L("安装所选"),
		}, 660.0))
		{
			case 1:
				return extra;
			case 2:
				if (chosen != null)
				{
					Dictionary<string, object?> result = new Dictionary<string, object?>(extra)
					{
						["version_id"] = chosen.IdValue,
						["filename"] = chosen.Filename,
					};
					if ((item.Source ?? "").ToLowerInvariant().StartsWith("curse") || chosen.Source == "curseforge")
					{
						result["file_id"] = chosen.IdValue;
					}
					return result;
				}
				break;
		}
		return null;

		Border FileRow(CatalogFile f)
		{
			TextBlock head = Ui.Txt(string.IsNullOrEmpty(f.VersionNumber) ? f.Name : f.VersionNumber, 12.5, bold: true).Trim();
			TextBlock sub = Ui.Small(string.Join(", ", (f.GameVersions ?? new List<string>()).Take(4)) + " · " + string.Join(", ", f.Loaders ?? new List<string>()) + " · " + f.Date + " · " + Fmt.Downloads(f.Downloads) + " · " + (string.IsNullOrEmpty(f.ReleaseType) ? "release" : f.ReleaseType));
			Border border = Ui.RowCard(Ui.V(2.0, head, sub.Wrap()), null, 9.0);
			border.Tag = f;
			border.PointerReleased += delegate
			{
				chosen = f;
				MarkSelected();
			};
			return border;
		}

		void MarkSelected()
		{
			foreach (Border row in listHost.Children.OfType<Border>())
			{
				bool on = ReferenceEquals(row.Tag, chosen);
				row.Dyn(Border.BorderBrushProperty, on ? "B.Accent" : "B.Line");
				row.Dyn(Border.BackgroundProperty, on ? "B.AccentSoft" : "B.Paper");
			}
		}

		void Refill()
		{
			string gv = gvBox.Str();
			string ld = loaderBox.Str();
			bool anyGv = gvBox.SelectedIndex <= 0;
			bool anyLd = loaderBox.SelectedIndex <= 0;
			List<CatalogFile> shown = files.Where(delegate(CatalogFile f)
			{
				if (!anyGv)
				{
					List<string>? gameVersions = f.GameVersions;
					if (gameVersions != null && gameVersions.Count > 0 && !f.GameVersions!.Contains(gv))
					{
						return false;
					}
				}
				if (!anyLd)
				{
					List<string>? fileLoaders = f.Loaders;
					if (fileLoaders != null && fileLoaders.Count > 0 && !f.Loaders!.Any((string l) => string.Equals(l, ld, StringComparison.OrdinalIgnoreCase)))
					{
						return false;
					}
				}
				return true;
			}).ToList();
			if (shown.Count > 0 && !shown.Contains(chosen))
			{
				chosen = shown[0];
			}
			listHost.Children.Clear();
			foreach (CatalogFile f in shown.Take(80))
			{
				listHost.Children.Add(FileRow(f));
			}
			count.Text = I18n.L("{0} 个匹配 / 共 {1} 个文件", shown.Count, files.Count);
			MarkSelected();
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:892-936</c>:起安装任务(整合包那一支多带一个 <c>source</c>),成功再飞任务页。
	/// 【mac 差异】Windows 在飞之前看 <c>SettingsPage.FlyEnabled</c>(C5a 给这个开关补了消费方,见 D-145)。
	/// </summary>
	private async Task<string> DoInstallAsync(Dictionary<string, object?> extra, Control? anchor)
	{
		string name = extra.TryGetValue("name", out object? value) ? (value?.ToString() ?? _kind.Title) : _kind.Title;
		string result;
		if (_kind.IsModpack)
		{
			string source = (extra.TryGetValue("source", out object? src) && src?.ToString() is string s && s.Length > 0) ? s : "Modrinth";
			result = await PageBase.Api.StartTaskAsync(_kind.InstallMethod, new
			{
				name,
				source,
				extra,
			});
		}
		else
		{
			result = await PageBase.Api.StartTaskAsync(_kind.InstallMethod, new
			{
				name,
				instance = CurrentInstance(),
				extra,
			});
		}
		if (anchor != null && SettingsPage.FlyEnabled)
		{
			PageBase.Win?.FlyToTasks(anchor, name);
		}
		return result;
	}

	/// <summary>【Windows 源】<c>:938-948</c>:链接安装(问一个 URL,塞进 extra 后走同一条安装路)。</summary>
	private async Task InstallFromLinkAsync(Control anchor)
	{
		string? text = await Dlg.Prompt(_kind.Title + I18n.L(" · 链接安装"), _kind.LinkHint, "", "https://...");
		if (!string.IsNullOrWhiteSpace(text))
		{
			Dictionary<string, object?> dictionary = BaseExtra(text.Trim());
			dictionary["url"] = text.Trim();
			await DoInstallAsync(dictionary, anchor);
			PageBase.Win?.Navigate("tasks");
		}
	}

	/// <summary>【Windows 源】<c>:950-986</c>:导入本地文件(逐个起任务,再等任务结束)。</summary>
	private void ImportLocal(Control anchor)
	{
		string[]? files = Dlg.PickFiles(_kind.LocalFilter, I18n.L("导入本地") + _kind.Title);
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
			foreach (string file in files)
			{
				Dictionary<string, object?> dictionary = BaseExtra(Path.GetFileName(file));
				dictionary["path"] = file;
				dictionary["source"] = "本地";
				string tid = await DoInstallAsync(dictionary, null);
				if (!string.IsNullOrEmpty(tid))
				{
					await PageBase.Api.TryCallAsync<OpResult>("wait_task", new
					{
						task_id = tid,
						timeout = 1800,
					});
				}
			}
			if (_mode != Mode.Search)
			{
				await ReloadCurrentAsync();
			}
		}, I18n.L("导入失败"));
	}

	/// <summary>【Windows 源】<c>:988-1047</c>:已安装模式(mod 走 <c>get_installed_mod_entries</c>,其余走 <c>_kind.InstalledMethod</c>)。</summary>
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
					version,
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
				instance = inst,
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

	/// <summary>【Windows 源】<c>:1049-1070</c>:已装于版本下拉(来自 <c>get_mods_targets</c>;空列表补一个「实例共享目录」)。</summary>
	private async Task FillInstalledVersionsAsync(string instance)
	{
		List<ModsTarget> list = (await PageBase.Api.TryCallAsync("get_mods_targets", new
		{
			instance,
		}, new List<ModsTarget>())) ?? new List<ModsTarget>();
		if (list.Count == 0)
		{
			list.Add(new ModsTarget
			{
				Label = I18n.L("实例共享目录"),
				Value = "",
			});
		}
		int selectedIndex = _installedVer.SelectedIndex;
		_sync = true;
		_installedVer.Items.Clear();
		foreach (ModsTarget target in list)
		{
			_installedVer.Items.Add(target.Label);
		}
		_installedVer.SelectedIndex = (selectedIndex >= 0 && selectedIndex < list.Count) ? selectedIndex : 0;
		_installedVerValues = list.Select((ModsTarget t) => t.Value).ToList();
		_sync = false;
	}

	/// <summary>【Windows 源】<c>:1072-1106</c>:已装模组一行(开关 + 文件名/版本/大小 + 已禁用 + 删除)。</summary>
	private Control ModRow(ModEntry m)
	{
		string inst = CurrentInstance();
		string ver = InstalledVersion();
		string file = m.Filename;
		CheckBox toggle = Ui.Switch(m.Enabled);
		// 【Avalonia 差异】Windows 分别接 Checked / Unchecked;Avalonia 11.3 标成过时(CS0618),
		// 等价物是 IsCheckedChanged + 判 IsChecked(与 C4 / C5a 同一口径)。
		toggle.IsCheckedChanged += delegate
		{
			bool on = toggle.IsChecked == true;
			PageBase.Run(() => PageBase.Api.TryCallAsync<object>(on ? "enable_mod" : "disable_mod", new
			{
				instance = inst,
				filename = file,
				version = ver,
			}));
		};
		Button del = Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteInstalledAsync(file), I18n.L("删除失败"));
		});
		Grid grid = Ui.G(null, "Auto,*,Auto,Auto");
		grid.Add(toggle.M(0.0, 0.0, 10.0).VCenter(), 0);
		grid.Add(Ui.V(1.0,
			Ui.Txt(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name).Trim(),
			Ui.Small(string.Join(" · ", new string[3] { file, m.Version, m.Size }.Where((string x) => !string.IsNullOrEmpty(x))))).VCenter(), 0, 1);
		grid.Add(Ui.Small(m.Enabled ? "" : I18n.L("已禁用")).VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 2);
		grid.Add(del.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 3);
		return Ui.RowCard(grid, null, 10.0);
	}

	/// <summary>【Windows 源】<c>:1108-1125</c>:已装条目一行(整合包是「移除标记」,其余是「删除」;可导出的多一颗「导出」)。</summary>
	private Control InstalledRow(string file)
	{
		Button del = Ui.Btn(_kind.IsModpack ? I18n.L("移除标记") : I18n.L("删除"), BtnKind.Ghost, delegate
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
		grid.Add(del.VCenter(), 0, 2);
		return Ui.RowCard(grid, null, 10.0);
	}

	/// <summary>【Windows 源】<c>:1127-1138</c>:种类的导出键(整合包不给导出)。</summary>
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

	/// <summary>【Windows 源】<c>:1140-1164</c>:导出单个(<c>default_export_dir</c> → 选目录 → <c>export_content</c>)。</summary>
	private async Task ExportInstalledAsync(string name)
	{
		string kind = ExportKind();
		if (kind.Length != 0)
		{
			string last = (await PageBase.Api.TryCallAsync("default_export_dir", null, "")) ?? "";
			string? folder = Dlg.PickFolder(string.IsNullOrEmpty(last) ? I18n.L("选择导出位置") : I18n.L("选择导出位置(上次:{0})", last));
			if (folder != null)
			{
				await PageBase.Api.TryCallAsync("remember_export_dir", new
				{
					path = folder,
				}, folder);
				string dest = await PageBase.Api.CallAsync<string>("export_content", new
				{
					kind,
					name,
					dest_dir = folder,
					version = InstalledVersion(),
					instance = CurrentInstance(),
				});
				PageBase.Toast(I18n.L("已导出"), dest ?? folder, ToastKind.Success);
			}
		}
	}

	/// <summary>【Windows 源】<c>:1166-1206</c>:全部导出(<c>export_contents</c>,失败清单要弹出来)。</summary>
	private async Task ExportAllInstalledAsync(List<string> names)
	{
		string kind = ExportKind();
		if (kind.Length == 0 || names.Count == 0)
		{
			return;
		}
		string last = (await PageBase.Api.TryCallAsync("default_export_dir", null, "")) ?? "";
		string? folder = Dlg.PickFolder(string.IsNullOrEmpty(last) ? I18n.L("选择导出位置") : I18n.L("选择导出位置(上次:{0})", last));
		if (folder == null)
		{
			return;
		}
		await PageBase.Api.TryCallAsync("remember_export_dir", new
		{
			path = folder,
		}, folder);
		ExportResult? exportResult;
		using (Dlg.Busy(I18n.L("正在导出 {0} 个...", names.Count)))
		{
			exportResult = await PageBase.Api.CallAsync<ExportResult>("export_contents", new
			{
				kind,
				names,
				dest_dir = folder,
				version = InstalledVersion(),
				instance = CurrentInstance(),
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
				await Dlg.Alert(I18n.L("部分没能导出"), I18n.L("成功 {0} 个,失败 {1} 个:\n\n", exportResult.Ok.Count, exportResult.Failed.Count) + string.Join("\n", exportResult.Failed));
			}
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:1208-1233</c>:切到本页时嗅一下剪贴板里的 Modrinth / CurseForge 链接。
	/// 【mac 差异 · D 类】WPF 有静态 <c>Clipboard.GetText()</c>,Avalonia 11 没有 ⇒ 走注入缝
	/// <see cref="ClipboardReader"/>;离屏(<c>Dlg.SmokeActive</c>)与没接缝时**一律不碰真剪贴板**。
	/// </summary>
	private void SniffClipboard()
	{
		string text;
		Func<string>? reader = ClipboardReader;
		if (reader != null)
		{
			try
			{
				text = (reader() ?? "").Trim();
			}
			catch
			{
				return;
			}
		}
		else
		{
			if (Dlg.SmokeActive)
			{
				return;
			}
			try
			{
				TopLevel? top = TopLevel.GetTopLevel(this);
				if (top?.Clipboard is null)
				{
					return;
				}
				// Avalonia 的剪贴板读取是异步的;这个方法本身是同步的(Windows 那边也是同步的静态入口),
				// 所以这里**只发起读取、结果不阻塞界面** —— 拿到之后再走同一条判定。
				Task<string?> pending = top.Clipboard.GetTextAsync();
				pending.ContinueWith(delegate(Task<string?> t)
				{
					if (t.Status == TaskStatus.RanToCompletion)
					{
						Dispatcher.UIThread.Post(() => Consider(t.Result));
					}
				}, TaskScheduler.Default);
				return;
			}
			catch
			{
				return;
			}
		}
		Consider(text);

		void Consider(string? value)
		{
			string seen = (value ?? "").Trim();
			if (seen.Length == 0 || seen == _clipSeen)
			{
				return;
			}
			string lower = seen.ToLowerInvariant();
			if (lower.Contains("modrinth.com") || lower.Contains("curseforge.com"))
			{
				_clipSeen = seen;
				if ((_search.Text ?? "").Trim().Length == 0)
				{
					_search.Text = seen;
				}
				PageBase.Toast(I18n.L("识别到剪贴板链接"), (seen.Length > 96) ? (seen.Substring(0, 96) + "...") : seen);
			}
		}
	}

	/// <summary>【Windows 源】<c>:1235-1281</c>:删除(整合包是「清掉已安装标记」,其余按种类调 <c>DeleteMethod</c>)。</summary>
	private async Task DeleteInstalledAsync(string filename)
	{
		if (string.IsNullOrEmpty(_kind.DeleteMethod))
		{
			return;
		}
		string inst = CurrentInstance();
		if (_kind.DeleteMethod == "delete_modpack")
		{
			if (!(await Dlg.Confirm(I18n.L("移除整合包标记"), I18n.L("只清掉「已安装整合包」这条记录,游戏目录里的模组与存档一个不动。\n要腾空间的话,到「原版游戏」里逐个卸载版本。"), I18n.L("移除标记"), I18n.L("取消"))))
			{
				return;
			}
			await PageBase.Api.CallAsync("delete_modpack", new
			{
				instance = inst,
				filename,
				purge_instance = false,
			});
		}
		else
		{
			if (!(await Dlg.Confirm(I18n.L("删除确认"), I18n.L("将删除「{0}」,不可恢复。", filename), I18n.L("删除"), I18n.L("取消"), danger: true)))
			{
				return;
			}
			if (!(_kind.DeleteMethod == "delete_mod"))
			{
				await PageBase.Api.CallAsync(_kind.DeleteMethod, new
				{
					instance = inst,
					filename,
				});
			}
			else
			{
				await PageBase.Api.CallAsync(_kind.DeleteMethod, new
				{
					instance = inst,
					filename,
					version = "",
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
