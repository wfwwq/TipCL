// 来源: Windows 版参考仓库 src/TipCL/Pages/CatalogPage.cs(1309 行,M4-H P1 移植)。
// 下载页六个分段(Mod / 整合包 / 数据包 / 资源包 / 光影 / 世界)共用这一个目录页:
// 顶部「搜索下载 / 已安装 / 收藏」三态 + 来源/类型/游戏版本/实例 筛选 + 结果列表(收藏 + 安装)。
// 差异与降级(见 DECISIONS D-18):
//   · Windows 的 SPanel/VirtualList/SmoothScroll/Ui.Skeleton/Dlg.Busy/FileDrop 在 Linux 侧尚无对应件,
//     用等价的 StackPanel + ScrollViewer + 圆角占位块实现;拖放安装(EnableDrop)未移植(不呈现)。
//   · 「游戏版本」下拉在 Windows 是可编辑 ComboBox;Avalonia 端保持不可编辑(候选表固定,行为等价)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Shell;
using TipCL.App.Ui;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Download;

/// <summary>目录页(下载页六分段的内容视图)。</summary>
public sealed class CatalogPage : PageBase
{
    private enum Mode { Search, Installed, Favorites }

    /// <summary>「游戏版本」候选(照 Windows 版 CatalogPage.GameVersions)。</summary>
    private static readonly string[] GameVersions =
    {
        "全部", "26.3", "26.2", "26.1.2", "26.1", "1.21.11", "1.21.10", "1.21.8", "1.21.5", "1.21.4",
        "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.12.2", "1.7.10",
    };

    private static readonly string[] Sources = { "全部", "Modrinth", "CurseForge" };

    /// <summary>远程搜索超时(对齐 Windows 版 SearchTimeoutMs)。</summary>
    public static int SearchTimeoutMs = 9000;

    private static string _clipSeen = "";

    private readonly CatalogKind _kind;
    private readonly TextBox _search;
    private readonly ComboBox _source;
    private readonly ComboBox _type;
    private readonly ComboBox _version;
    private readonly ComboBox _inst;
    private readonly ComboBox _installedVer;
    private readonly ToggleButton _modeSearch;
    private readonly ToggleButton _modeInstalled;
    private readonly ToggleButton _modeFav;
    private readonly StackPanel _results = UiClass.V(8);
    private readonly StackPanel _placeholder = UiClass.V(10);
    private readonly ScrollViewer _resultsScroll;
    private readonly ScrollViewer _placeholderScroll;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(320) };

    private List<(string Name, int Versions)> _instances = new();
    private HashSet<string> _favKeys = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _installedVerValues = new();
    private List<string> _installedNames = new();

    private bool _sync;
    private Mode _mode = Mode.Search;
    private int _searchToken;
    private CancellationTokenSource? _searchCts;
    private bool _searchInFlight;
    private bool _searchInterrupted;

    public CatalogPage(CatalogKind kind) : base("catalog-" + kind.Key, kind.Title)
    {
        _kind = kind;
        _search = UiClass.Input("搜索" + kind.Title + ",留空看热门");
        _source = UiClass.Combo(Sources, Sources.Contains(kind.DefaultSource) ? kind.DefaultSource : "全部", 116);
        _type = UiClass.Combo(kind.Types, "全部", 116);
        _version = UiClass.Combo(GameVersions, "全部", 140);
        _inst = UiClass.Combo(null, null, 150);
        _installedVer = UiClass.Combo(null, null, 150);

        _modeSearch = ModeTab("搜索下载", true, () => SetMode(Mode.Search));
        _modeInstalled = ModeTab("已安装", false, () => SetMode(Mode.Installed));
        _modeFav = ModeTab("收藏", false, () => SetMode(Mode.Favorites));

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (_mode == Mode.Search) Run(SearchAsync, "搜索失败");
        };
        _search.TextChanged += (_, _) =>
        {
            if (!_sync && _mode == Mode.Search)
            {
                _debounce.Stop();
                _debounce.Start();
            }
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _debounce.Stop();
                Run(SearchAsync, "搜索失败");
            }
        };
        _source.SelectionChanged += (_, _) => { if (!_sync) Run(SearchAsync, "搜索失败"); };
        _type.SelectionChanged += (_, _) => { if (!_sync) Run(SearchAsync, "搜索失败"); };
        _version.SelectionChanged += (_, _) => { if (!_sync) Run(SearchAsync, "搜索失败"); };
        _inst.SelectionChanged += (_, _) =>
        {
            if (!_sync) Run(ReloadCurrentAsync, "搜索失败");
        };
        _installedVer.SelectionChanged += (_, _) =>
        {
            if (!_sync && _mode == Mode.Installed) Run(LoadInstalledAsync, "搜索失败");
        };

        // ── 搜索行 + 筛选行(卡内)──
        var searchBtn = U.Btn("搜索", BtnKind.Primary, (_, _) => Run(SearchAsync, "搜索失败"), "Search");
        var searchRow = UiClass.G(null, "*,Auto");
        searchRow.Add(_search, 0, 0);
        searchBtn.Mk(8, 0, 0, 0);
        searchRow.Add(searchBtn, 0, 1);

        var resetBtn = U.Btn("重置条件", BtnKind.Chip, (_, _) => ResetFilters(), "Refresh");
        var updateBtn = U.Btn("检查更新", BtnKind.Chip, (_, _) => Run(CheckUpdatesAsync, "检查更新失败"), "Refresh");
        updateBtn.IsVisible = kind.Key == "mod";
        var exportAllBtn = U.Btn("全部导出", BtnKind.Chip, (_, _) => Run(() => ExportAllInstalledAsync(_installedNames), "导出失败"), "Export");
        exportAllBtn.IsVisible = ExportKind().Length > 0;
        var linkBtn = U.Btn("链接安装", BtnKind.Chip, (_, _) => Run(InstallFromLinkAsync, "链接安装失败"), "Open");
        var localBtn = U.Btn("导入本地文件", BtnKind.Chip, (_, _) => ImportLocal(), "Import");
        var refreshBtn = U.IconBtn("Refresh", "刷新", (_, _) => Run(ReloadCurrentAsync, "搜索失败"));

        var filters = new WrapPanel();
        filters.Children.Add(UiClass.H(7, resetBtn, linkBtn, localBtn, updateBtn, exportAllBtn, refreshBtn).Mk(0, 0, 0, 8));
        Labeled("来源", _source);
        Labeled("类型", _type);
        Labeled("游戏版本", _version);
        Labeled("实例", _inst);
        if (kind.InstalledMethod.Length > 0) Labeled("已装于版本", _installedVer);
        var card = UiClass.Card(UiClass.V(10, searchRow, filters), 14);

        // ── 结果区 / 占位区 ──
        _resultsScroll = new ScrollViewer
        {
            Content = _results,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _placeholderScroll = new ScrollViewer
        {
            Content = _placeholder,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsVisible = false,
        };
        var listHost = new Grid();
        listHost.Children.Add(_resultsScroll);
        listHost.Children.Add(_placeholderScroll);

        var modes = UiClass.H(6, _modeSearch,
            kind.InstalledMethod.Length > 0 ? _modeInstalled : null, _modeFav);
        var head = UiClass.Section(kind.Title, SubTitle(), modes);

        var root = UiClass.G("Auto,Auto,*");
        root.Add(head, 0, 0);
        root.Add(card.Mk(0, 14, 0, 14), 1, 0);
        root.Add(listHost, 2, 0);
        root.Margin = new Thickness(26, 20, 26, 20);
        Content = root;

        void Labeled(string label, Control ctl)
        {
            ctl.VerticalAlignment = VerticalAlignment.Center;
            var wrap = UiClass.H(6, UiClass.Txt(label, 12, fg: "B.InkMuted").VCenter(), ctl);
            wrap.Mk(0, 0, 14, 8);
            filters.Children.Add(wrap);
        }
    }

    private static ToggleButton ModeTab(string text, bool on, Action click)
    {
        var t = new ToggleButton { Theme = UiClass.Theme("Seg.Item"), Content = UiClass.Txt(text, 13), IsChecked = on };
        t.Click += (_, _) => click();
        return t;
    }

    // ───────────────────────────── 生命周期 ─────────────────────────────

    protected override void OnCreate() => Run(LoadAsync, "搜索失败");

    protected override void OnActivate()
    {
        FileDrop.Attach(this, () => new FileDrop.DropContext { Instance = CurrentInstance() });
        // 照 Windows OnShown:识别剪贴板链接 + 被中断的搜索重来一次
        SniffClipboard();
        if (_searchInterrupted)
        {
            _searchInterrupted = false;
            Run(ReloadCurrentAsync, "搜索失败");
        }
    }

    protected override void OnDeactivate()
    {
        _debounce.Stop();
        if (_searchInFlight)
        {
            _searchInterrupted = true;
            CancelSearch();
        }
    }

    protected override void OnEvent(ApiEvent ev)
    {
        if (ev.Event == "finished" && ev.Success && _mode == Mode.Installed)
            Run(LoadInstalledAsync);
    }

    // ───────────────────────────── 取值 ─────────────────────────────

    private string SubTitle() => _kind.Key switch
    {
        "mod" => "从 Modrinth / CurseForge 搜模组,装进所选实例",
        "modpack" => "一键安装整合包到独立实例",
        "datapack" => "数据包会放进实例 datapacks 目录",
        "resourcepack" => "资源包会放进实例 resourcepacks 目录",
        "shader" => "光影包会放进实例 shaderpacks 目录",
        _ => "世界地图会解压成实例存档",
    };

    private string CurrentInstance() => _inst.SelectedItem as string ?? "";

    private string InstalledVersion()
    {
        int i = _installedVer.SelectedIndex;
        return i >= 0 && i < _installedVerValues.Count ? _installedVerValues[i] : "";
    }

    private string GameVersionFilter()
    {
        string t = (_version.SelectedItem as string ?? "").Trim();
        return t.Length == 0 || t == "全部" ? "" : t;
    }

    private string SourceParam()
    {
        string t = _source.SelectedItem as string ?? "全部";
        return t == "全部" ? "all" : t;
    }

    private string CategoryParam() => _type.SelectedIndex <= 0 ? "" : _type.SelectedItem as string ?? "";

    private string ExportKind() => _kind.Key switch
    {
        "mod" => "mod",
        "shader" => "shader",
        "resourcepack" => "resourcepack",
        "datapack" => "datapack",
        "world" => "world",
        _ => "",
    };

    // ───────────────────────────── 数据 ─────────────────────────────

    private async Task LoadAsync()
    {
        var instances = await Api.TryCallAsync<List<object?>>("get_instances", null) ?? new();
        _instances = new List<(string, int)>();
        foreach (var it in instances)
            if (it is TipCL.Core.PyDict d)
            {
                string name = TipCL.Core.Py.S(d.Get("name"));
                if (name.Length == 0) continue;
                int n = d.Get("versions") is System.Collections.ICollection c ? c.Count : 0;
                _instances.Add((name, n));
            }
        _sync = true;
        _inst.Items.Clear();
        foreach (var (name, _) in _instances) _inst.Items.Add(name);
        _inst.SelectedIndex = _instances.Count > 0 ? 0 : -1;
        _sync = false;
        await ReloadFavoritesAsync();
        await ReloadCurrentAsync();
    }

    private Task ReloadCurrentAsync() => _mode switch
    {
        Mode.Installed => LoadInstalledAsync(),
        Mode.Favorites => LoadFavoritesAsync(),
        _ => SearchAsync(),
    };

    private void SetMode(Mode mode)
    {
        if (mode == Mode.Installed && _kind.InstalledMethod.Length == 0)
            mode = Mode.Search;
        _mode = mode;
        _modeSearch.IsChecked = mode == Mode.Search;
        _modeInstalled.IsChecked = mode == Mode.Installed;
        _modeFav.IsChecked = mode == Mode.Favorites;
        Run(ReloadCurrentAsync, "搜索失败");
    }

    private void ResetFilters()
    {
        _sync = true;
        _search.Text = "";
        _source.SelectedItem = Sources.Contains(_kind.DefaultSource) ? _kind.DefaultSource : "全部";
        _type.SelectedIndex = 0;
        _version.SelectedIndex = 0;
        _sync = false;
        Run(ReloadCurrentAsync, "搜索失败");
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
        var cts = _searchCts;
        _searchCts = null;
        if (cts == null) return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    private async Task ReloadFavoritesAsync()
    {
        var rows = await Api.TryCallAsync<List<CatalogItem>>("catalog_favorites", null) ?? new();
        _favKeys = rows.Select(x => x.FavKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SearchAsync()
    {
        int token = ++_searchToken;
        var ct = NextSearchToken();
        ShowPlaceholder(Skeleton(64), Skeleton(64), Skeleton(64));
        string query = _search.Text?.Trim() ?? "";
        _searchInFlight = true;
        try
        {
            var rows = await Api.CallAsync<List<CatalogItem>>(_kind.SearchMethod, new
            {
                query,
                source = SourceParam(),
                extra = new { game_version = GameVersionFilter(), category = CategoryParam() },
            }, ct) ?? new();
            if (token != _searchToken) return;
            if (rows.Count == 0)
                ShowPlaceholder(UiClass.Empty(_kind.Icon, _kind.Empty, "换个关键词,或切换来源 / 游戏版本试试"));
            else
                ShowRows(rows.Cast<object>().ToList());
        }
        catch (OperationCanceledException)
        {
            if (token == _searchToken && !_searchInterrupted)
                ShowFailure("搜索超时", $"{(SearchTimeoutMs + 999) / 1000} 秒内没等到结果。检查网络或换个来源再试。");
        }
        catch (Exception ex)
        {
            if (token == _searchToken) ShowFailure("搜索失败", ex.Message);
        }
        finally
        {
            if (token == _searchToken) _searchInFlight = false;
        }
    }

    private async Task LoadFavoritesAsync()
    {
        int token = ++_searchToken;
        CancelSearch();
        ShowPlaceholder(Skeleton(56));
        var rows = await Api.TryCallAsync<List<CatalogItem>>("catalog_favorites", null) ?? new();
        if (token != _searchToken) return;
        _favKeys = rows.Select(x => x.FavKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (rows.Count == 0)
            ShowPlaceholder(UiClass.Empty("Star", "还没有收藏", "在搜索结果里点右边的星星就能收藏"));
        else
            ShowRows(rows.Cast<object>().ToList());
    }

    private async Task LoadInstalledAsync()
    {
        int token = ++_searchToken;
        CancelSearch();
        ShowPlaceholder(Skeleton(46));
        string inst = CurrentInstance();
        if (inst.Length == 0)
        {
            ShowPlaceholder(UiClass.Empty(_kind.Icon, "还没有实例", "先到「实例库」页新建一个"));
            return;
        }
        await FillInstalledVersionsAsync(inst);
        string version = InstalledVersion();
        try
        {
            if (_kind.Key == "mod")
            {
                var mods = await Api.TryCallAsync<List<ModEntry>>("get_installed_mod_entries", null,
                    new { instance = inst, version }) ?? new();
                if (token != _searchToken) return;
                if (mods.Count == 0)
                    ShowPlaceholder(UiClass.Empty(_kind.Icon, "这个实例还没有 Mod", "切回「搜索下载」装一个"));
                else
                    ShowRows(mods.Cast<object>().ToList());
                return;
            }
            if (_kind.InstalledMethod.Length == 0)
            {
                ShowPlaceholder(UiClass.Empty(_kind.Icon, "这类内容没有列表", "世界装好即出现在存档里"));
                return;
            }
            var files = await Api.TryCallAsync<List<string>>(_kind.InstalledMethod, null, new { instance = inst }) ?? new();
            if (token != _searchToken) return;
            if (files.Count == 0)
                ShowPlaceholder(UiClass.Empty(_kind.Icon, $"这个实例还没有{_kind.Title}", "切回「搜索下载」装一个"));
            else
            {
                _installedNames = files;
                ShowRows(files.Cast<object>().ToList());
            }
        }
        catch (Exception ex)
        {
            if (token == _searchToken) ShowPlaceholder(UiClass.Empty("Warning", "读取失败", ex.Message));
        }
    }

    private async Task FillInstalledVersionsAsync(string instance)
    {
        var targets = await Api.TryCallAsync<List<ModsTarget>>("get_mods_targets", null, new { instance }) ?? new();
        if (targets.Count == 0) targets.Add(new ModsTarget { Label = "实例共享目录", Value = "" });
        int prev = _installedVer.SelectedIndex;
        _sync = true;
        _installedVer.Items.Clear();
        foreach (var t in targets) _installedVer.Items.Add(t.Label);
        _installedVer.SelectedIndex = prev >= 0 && prev < targets.Count ? prev : 0;
        _installedVerValues = targets.Select(t => t.Value).ToList();
        _sync = false;
    }

    private async Task CheckUpdatesAsync()
    {
        var rows = await Api.CallAsync<List<object?>>("check_mod_updates", new { instance = CurrentInstance() }) ?? new();
        if (rows.Count == 0) Toast("没有可更新的模组");
        else Toast("有可用更新", $"{rows.Count} 个模组可以更新(到「实例详情」页处理)");
        if (_mode == Mode.Installed) await LoadInstalledAsync();
    }

    // ───────────────────────────── 结果渲染 ─────────────────────────────

    private void ShowPlaceholder(params Control[] kids)
    {
        _placeholder.Children.Clear();
        foreach (var k in kids) _placeholder.Children.Add(k);
        _resultsScroll.IsVisible = false;
        _placeholderScroll.IsVisible = true;
    }

    private void ShowRows(List<object> rows)
    {
        _results.Children.Clear();
        foreach (var row in rows) _results.Children.Add(BuildRow(row));
        _placeholder.Children.Clear();
        _resultsScroll.IsVisible = true;
        _placeholderScroll.IsVisible = false;
        _resultsScroll.Offset = new Vector(0, 0);
    }

    private void ShowFailure(string title, string detail)
    {
        var retry = U.Btn("重试", BtnKind.Soft, (_, _) => Run(ReloadCurrentAsync, "搜索失败"), "Refresh");
        retry.HorizontalAlignment = HorizontalAlignment.Center;
        ShowPlaceholder(UiClass.Empty("Warning", title, detail), retry);
    }

    private static Border Skeleton(double h)
    {
        var b = new Border { Height = h, CornerRadius = new CornerRadius(10) };
        b.Dyn(Border.BackgroundProperty, "B.LineSoft");
        return b;
    }

    private Control BuildRow(object item) => item switch
    {
        CatalogItem ci => ResultCard(ci),
        ModEntry m => ModRow(m),
        string file => InstalledRow(file),
        _ => UiClass.Txt(item?.ToString() ?? ""),
    };

    private Control ResultCard(CatalogItem item)
    {
        var thumb = new ThumbTile(item.Name, 46) { VerticalAlignment = VerticalAlignment.Center };
        thumb.SetUrl(item.IconUrl);
        thumb.Mk(0, 0, 12, 0);

        var meta = UiClass.H(7,
            UiClass.Muted(string.IsNullOrEmpty(item.Author) ? "佚名" : item.Author).VCenter(),
            UiClass.Muted("·").VCenter(),
            UiClass.Muted(CatalogFmt.Downloads(item.Downloads) + " 次下载").VCenter(),
            string.IsNullOrEmpty(item.Source) ? null : UiClass.Tag(item.Source, "B.AccentText", "B.AccentSoft"));
        var col = UiClass.V(3, UiClass.Txt(item.Name, 13.5, bold: true), meta);
        if (!string.IsNullOrWhiteSpace(item.Description))
            col.Children.Add(UiClass.Muted(item.Description));
        if (item.Tags is { Count: > 0 })
        {
            var tags = UiClass.H(5);
            foreach (var t in item.Tags.Take(5)) tags.Children.Add(UiClass.Tag(t));
            col.Children.Add(tags);
        }
        col.VerticalAlignment = VerticalAlignment.Center;

        bool fav = _favKeys.Contains(item.FavKey);
        var star = new ToggleButton
        {
            Theme = UiClass.Theme("Btn.Icon"),
            IsChecked = fav,
            Content = IconLib.Make("Star", fav ? "B.Accent" : "B.InkMuted", 13),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(star, "收藏");
        star.Click += (_, e) =>
        {
            e.Handled = true;
            Run(() => ToggleFavAsync(item, star), "收藏失败");
        };

        var install = U.Btn("安装", BtnKind.Soft, null, "Download");
        install.VerticalAlignment = VerticalAlignment.Center;
        install.Mk(8, 0, 0, 0);
        install.Click += (_, e) =>
        {
            e.Handled = true;
            Run(() => InstallAsync(item), "安装失败");
        };

        var grid = UiClass.G(null, "Auto,*,Auto,Auto");
        grid.Add(thumb, 0, 0);
        grid.Add(col, 0, 1);
        star.Mk(8, 0, 0, 0);
        grid.Add(star, 0, 2);
        grid.Add(install, 0, 3);

        var rowCard = UiClass.RowCard(grid, null, 13);
        if (!string.IsNullOrWhiteSpace(item.ProjectUrl))
        {
            ToolTip.SetTip(rowCard, "打开介绍页");
            rowCard.Cursor = new Cursor(StandardCursorType.Hand);
            rowCard.PointerReleased += (_, e) =>
            {
                if (HitButton(e.Source as Control)) return;
                try { TipCL.Core.Launcher.Crash.ShellOpen(item.ProjectUrl); }
                catch (Exception ex) { Toast("打开失败", ex.Message, ToastKind.Warning); }
            };
            Motion.HoverLift(rowCard, 1.004);
        }
        return rowCard;
    }

    private Control ModRow(ModEntry m)
    {
        string inst = CurrentInstance();
        string ver = InstalledVersion();
        string file = m.Filename;
        var sw = UiClass.Switch(m.Enabled);
        sw.IsCheckedChanged += (_, _) =>
        {
            bool on = sw.IsChecked == true;
            // 启停会改名(启用态别 jar / 禁用态 *.disabled),成功后重新列一遍,
            // 否则连续点两次会拿着旧文件名去改(Windows 版这里不刷新,第二次点会报「文件不存在」)。
            Run(async () =>
            {
                await Api.TryCallAsync<object>(on ? "enable_mod" : "disable_mod", null,
                    new { instance = inst, filename = file, version = ver });
                if (_mode == Mode.Installed) await LoadInstalledAsync();
            });
        };
        var del = UiClass.Btn("删除", BtnKind.Ghost, (_, _) => Run(() => DeleteInstalledAsync(file), "删除失败"));

        var col = UiClass.V(1,
            UiClass.Txt(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name),
            UiClass.Small(string.Join(" · ", new[] { file, m.Version, m.Size }.Where(x => !string.IsNullOrEmpty(x)))));
        col.VerticalAlignment = VerticalAlignment.Center;

        var grid = UiClass.G(null, "Auto,*,Auto,Auto");
        sw.Mk(0, 0, 10, 0);
        sw.VerticalAlignment = VerticalAlignment.Center;
        grid.Add(sw, 0, 0);
        grid.Add(col, 0, 1);
        var state = UiClass.Small(m.Enabled ? "" : "已禁用");
        state.Mk(8, 0, 0, 0);
        state.VerticalAlignment = VerticalAlignment.Center;
        grid.Add(state, 0, 2);
        del.Mk(8, 0, 0, 0);
        del.VerticalAlignment = VerticalAlignment.Center;
        grid.Add(del, 0, 3);
        return UiClass.RowCard(grid, null, 10);
    }

    private Control InstalledRow(string file)
    {
        var del = UiClass.Btn(_kind.IsModpack ? "移除标记" : "删除", BtnKind.Ghost,
            (_, _) => Run(() => DeleteInstalledAsync(file), "删除失败"));
        del.VerticalAlignment = VerticalAlignment.Center;
        var grid = UiClass.G(null, "*,Auto,Auto");
        var name = UiClass.Txt(file, 12.5);
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Add(name, 0, 0);
        if (ExportKind().Length > 0)
        {
            var exp = U.Btn("导出", BtnKind.Chip, (_, _) => Run(() => ExportInstalledAsync(file), "导出失败"), "Export");
            exp.Mk(0, 0, 6, 0);
            exp.VerticalAlignment = VerticalAlignment.Center;
            grid.Add(exp, 0, 1);
        }
        grid.Add(del, 0, 2);
        return UiClass.RowCard(grid, null, 10);
    }

    private static bool HitButton(Control? source)
    {
        while (source != null)
        {
            if (source is Button || source is ToggleButton || source is ComboBox) return true;
            source = source.Parent as Control;
        }
        return false;
    }

    // ───────────────────────────── 收藏 / 安装 ─────────────────────────────

    private async Task ToggleFavAsync(CatalogItem item, ToggleButton star)
    {
        var rows = await Api.CallAsync<List<CatalogItem>>("toggle_favorite", new
        {
            item = new { name = item.Name, source = item.Source, slug = item.Slug, id = item.IdValue },
        }) ?? new();
        _favKeys = rows.Select(x => x.FavKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool fav = _favKeys.Contains(item.FavKey);
        star.IsChecked = fav;
        star.Content = IconLib.Make("Star", fav ? "B.Accent" : "B.InkMuted", 13);
        Toast(fav ? "已收藏" : "已取消收藏", item.Name);
        if (_mode == Mode.Favorites) await LoadFavoritesAsync();
    }

    private Dictionary<string, object?> BaseExtra(string name) => new()
    {
        ["name"] = name,
        ["source"] = SourceParam(),
        ["instance"] = CurrentInstance(),
        ["version"] = InstalledVersion(),
        ["game_version"] = GameVersionFilter(),
        ["kind"] = _kind.FileKind,
    };

    private async Task InstallAsync(CatalogItem item)
    {
        var extra = BaseExtra(item.Name);
        extra["source"] = string.IsNullOrEmpty(item.Source) ? SourceParam() : item.Source;
        extra["slug"] = item.Slug;
        extra["id"] = item.IdValue;
        if (!string.IsNullOrEmpty(item.Slug) || item.IdValue != null)
        {
            var picked = await PickVersionFileAsync(item, extra);
            if (picked == null) return;
            extra = picked;
        }
        if (_kind.Key == "datapack")
        {
            extra = await PickSaveAsync(extra);
        }
        else if (_kind.Key == "world")
        {
            var saves = await Api.TryCallAsync<List<SaveRow>>("list_saves", null,
                new { instance = CurrentInstance(), version = "" }) ?? new();
            if (saves.Count > 0)
            {
                var names = new[] { "装进实例存档目录" }.Concat(saves.Select(s => s.Name)).ToArray();
                var pick = UiClass.Combo(names, names[0], 260);
                bool go = await Dlg.Ask($"装进哪里 · {item.Name}", UiClass.V(8,
                    UiClass.Muted("世界会解压到该实例的 saves 目录。"), pick), "继续");
                if (!go) return;
                extra["version"] = "";
            }
        }
        await DoInstallAsync(extra);
    }

    private async Task<Dictionary<string, object?>> PickSaveAsync(Dictionary<string, object?> extra)
    {
        string inst = CurrentInstance();
        if (inst.Length == 0) return extra;
        var saves = await Api.TryCallAsync<List<SaveRow>>("list_saves", null, new { instance = inst, version = "" }) ?? new();
        if (saves.Count == 0) return extra;
        var names = new[] { "不装进存档" }.Concat(saves.Select(s => s.Name)).ToArray();
        var pick = UiClass.Combo(names, names[0], 260);
        bool ok = await Dlg.Ask("装进存档", UiClass.V(8,
            UiClass.Muted("可选:把数据包直接装进某个世界,或只放到 datapacks 文件夹。"), pick), "继续");
        if (!ok || pick.SelectedIndex <= 0) return extra;
        return new Dictionary<string, object?>(extra) { ["save"] = pick.SelectedItem as string ?? "" };
    }

    private async Task<Dictionary<string, object?>?> PickVersionFileAsync(CatalogItem item, Dictionary<string, object?> extra)
    {
        var files = await Api.TryCallAsync<List<CatalogFile>>("list_catalog_files", null, new { extra }) ?? new();
        if (files.Count == 0)
        {
            bool go = await Dlg.Confirm("没有找到版本文件", "在线列表为空,要直接尝试安装最新版吗?", "安装最新", "取消");
            return go ? extra : null;
        }

        var chosen = files[0];
        var listHost = UiClass.V(6);
        var gvBox = UiClass.Combo(new[] { "全部" }.Concat(files.SelectMany(f => f.GameVersions ?? new()).Distinct()), "全部", 130);
        var loaderBox = UiClass.Combo(new[] { "全部" }.Concat(files.SelectMany(f => f.Loaders ?? new()).Distinct()), "全部", 130);
        var count = UiClass.Muted("");
        var scroll = new ScrollViewer
        {
            Content = listHost,
            MinWidth = 560,
            MaxHeight = 260,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        gvBox.SelectionChanged += (_, _) => Refill();
        loaderBox.SelectionChanged += (_, _) => Refill();
        Refill();
        var bar = UiClass.H(8,
            UiClass.Txt("游戏版本", 12, fg: "B.InkMuted").VCenter(), gvBox,
            UiClass.Txt("加载器", 12, fg: "B.InkMuted").VCenter(), loaderBox, count.VCenter());

        int choice = await Dlg.Choose($"选择版本文件 · {item.Name}", UiClass.V(10, bar, scroll),
            new[] { "取消", "安装最新", "安装所选" }, 700);
        if (choice == 1) return extra;
        if (choice != 2) return null;
        var picked = new Dictionary<string, object?>(extra)
        {
            ["version_id"] = chosen.IdValue,
            ["filename"] = chosen.Filename,
        };
        if ((item.Source ?? "").ToLowerInvariant().StartsWith("curse") || chosen.Source == "curseforge")
            picked["file_id"] = chosen.IdValue;
        return picked;

        void MarkSelected()
        {
            foreach (var b in listHost.Children.OfType<Border>())
            {
                bool on = ReferenceEquals(b.Tag, chosen);
                b[!Border.BorderBrushProperty] = Res.Bind(on ? "B.Accent" : "B.Line");
                b[!Border.BackgroundProperty] = Res.Bind(on ? "B.AccentSoft" : "B.Paper");
            }
        }

        void Refill()
        {
            string gv = gvBox.SelectedItem as string ?? "全部";
            string ld = loaderBox.SelectedItem as string ?? "全部";
            bool anyGv = gvBox.SelectedIndex <= 0;
            bool anyLd = loaderBox.SelectedIndex <= 0;
            var filtered = files.Where(f =>
            {
                if (!anyGv && f.GameVersions is { Count: > 0 } && !f.GameVersions.Contains(gv)) return false;
                if (!anyLd && f.Loaders is { Count: > 0 } && !f.Loaders.Any(l => string.Equals(l, ld, StringComparison.OrdinalIgnoreCase))) return false;
                return true;
            }).ToList();
            if (filtered.Count > 0 && !filtered.Contains(chosen)) chosen = filtered[0];
            listHost.Children.Clear();
            foreach (var f in filtered.Take(80)) listHost.Children.Add(FileRow(f));
            count.Text = $"{filtered.Count} 个匹配 / 共 {files.Count} 个文件";
            MarkSelected();
        }

        Border FileRow(CatalogFile f)
        {
            var sub = UiClass.Small(string.Join(", ", (f.GameVersions ?? new()).Take(4)) + " · "
                + string.Join(", ", f.Loaders ?? new()) + " · " + f.Date + " · "
                + CatalogFmt.Downloads(f.Downloads) + " · "
                + (string.IsNullOrEmpty(f.ReleaseType) ? "release" : f.ReleaseType));
            var card = UiClass.RowCard(UiClass.V(2,
                UiClass.Txt(string.IsNullOrEmpty(f.VersionNumber) ? f.Name : f.VersionNumber, 12.5, bold: true), sub), null, 9);
            card.Tag = f;
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerReleased += (_, _) =>
            {
                chosen = f;
                MarkSelected();
            };
            return card;
        }
    }

    private async Task<string> DoInstallAsync(Dictionary<string, object?> extra)
    {
        string name = extra.TryGetValue("name", out var n) ? n?.ToString() ?? _kind.Title : _kind.Title;
        string method = _kind.InstallMethod;
        object payload = _kind.IsModpack
            ? new
            {
                name,
                source = extra.TryGetValue("source", out var s) && s?.ToString() is { Length: > 0 } ss ? ss : "Modrinth",
                extra,
            }
            : new { name, instance = CurrentInstance(), extra };
        string taskId = await Api.CallAsync<string>(method, payload) ?? "";
        Toast("已加入队列", name, ToastKind.Success);
        if (this.GetVisualRoot() is MainWindow win) win.Navigate("tasks");
        return taskId;
    }

    private async Task InstallFromLinkAsync()
    {
        string? url = await Dlg.Prompt(_kind.Title + " · 链接安装", _kind.LinkHint, "", "https://...");
        if (string.IsNullOrWhiteSpace(url)) return;
        var extra = BaseExtra(url.Trim());
        extra["url"] = url.Trim();
        await DoInstallAsync(extra);
    }

    private async void ImportLocal()
    {
        try
        {
            var patterns = _kind.LocalFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
            string? path = await Dlg.PickFile("导入本地" + _kind.Title, patterns.Length > 0 ? patterns : new[] { "*.*" });
            if (string.IsNullOrWhiteSpace(path)) return;
            var extra = BaseExtra(System.IO.Path.GetFileName(path));
            extra["path"] = path;
            extra["source"] = "本地";
            await DoInstallAsync(extra);
        }
        catch (Exception ex)
        {
            Toast("导入失败", ex.Message, ToastKind.Warning);
        }
    }

    private async Task DeleteInstalledAsync(string filename)
    {
        if (_kind.DeleteMethod.Length == 0) return;
        string inst = CurrentInstance();
        if (_kind.DeleteMethod == "delete_modpack")
        {
            bool ok = await Dlg.Confirm("移除整合包标记",
                "只清掉「已安装整合包」这条记录,游戏目录里的模组与存档一个不动。\n要腾空间的话,到「实例库」里逐个卸载版本。",
                "移除标记", "取消");
            if (!ok) return;
            await Api.CallAsync<object>("delete_modpack", new { instance = inst, filename, purge_instance = false });
        }
        else
        {
            bool ok = await Dlg.Confirm("删除确认", $"将删除「{filename}」,不可恢复。", "删除", "取消", danger: true);
            if (!ok) return;
            if (_kind.DeleteMethod == "delete_mod")
                await Api.CallAsync<object>(_kind.DeleteMethod, new { instance = inst, filename, version = "" });
            else
                await Api.CallAsync<object>(_kind.DeleteMethod, new { instance = inst, filename, version = InstalledVersion() });
        }
        Toast("已处理", filename, ToastKind.Success);
        await LoadInstalledAsync();
    }

    private async Task ExportInstalledAsync(string name)
    {
        string kind = ExportKind();
        if (kind.Length == 0) return;
        string last = await Api.TryCallAsync<string>("default_export_dir", null) ?? "";
        string? folder = await Dlg.PickFolder(last.Length == 0 ? "选择导出位置" : $"选择导出位置(上次:{last})");
        if (folder == null) return;
        await Api.TryCallAsync<string>("remember_export_dir", folder, new { path = folder });
        string? dest = await Api.CallAsync<string>("export_content", new
        {
            kind,
            name,
            dest_dir = folder,
            version = InstalledVersion(),
            instance = CurrentInstance(),
        });
        Toast("已导出", dest ?? folder, ToastKind.Success);
    }

    private async Task ExportAllInstalledAsync(List<string> names)
    {
        string kind = ExportKind();
        if (kind.Length == 0 || names.Count == 0) return;
        string last = await Api.TryCallAsync<string>("default_export_dir", null) ?? "";
        string? folder = await Dlg.PickFolder(last.Length == 0 ? "选择导出位置" : $"选择导出位置(上次:{last})");
        if (folder == null) return;
        await Api.TryCallAsync<string>("remember_export_dir", folder, new { path = folder });
        // ContentExport.ExportMany 返回 {dir, exported[], failed[{name,error}]}:
        // 按 PyDict 直接读(Windows 侧按 ExportResult POCO 取,failed 元素是字典 → 反序列化失败,
        // 结果是「全部导出」静默什么都不显示;这里按实际结构呈现,见 DECISIONS D-18)。
        var res = await Api.CallAsync<PyDict>("export_contents", new
        {
            kind,
            names,
            dest_dir = folder,
            version = InstalledVersion(),
            instance = CurrentInstance(),
        });
        if (res == null) return;
        var exported = Py.ListOr(res.Get("exported"));
        var failed = Py.ListOr(res.Get("failed"));
        string dest = Py.S(Py.Or(res.Get("dir"), folder));
        if (failed.Count == 0)
        {
            Toast("已导出", $"{exported.Count} 个 → {dest}", ToastKind.Success);
        }
        else
        {
            var lines = failed.OfType<PyDict>()
                .Select(f => Py.S(f.Get("name")) + " — " + Py.S(f.Get("error")));
            await Dlg.Alert("部分没能导出", $"成功 {exported.Count} 个,失败 {failed.Count} 个:\n\n" + string.Join("\n", lines));
        }
    }

    /// <summary>识别剪贴板里的 Modrinth / CurseForge 链接(照 Windows SniffClipboard)。</summary>
    private async void SniffClipboard()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            string text = (await clipboard.GetTextAsync() ?? "").Trim();
            if (text.Length == 0 || text == _clipSeen) return;
            string low = text.ToLowerInvariant();
            if (!low.Contains("modrinth.com") && !low.Contains("curseforge.com")) return;
            _clipSeen = text;
            if ((_search.Text ?? "").Trim().Length == 0) _search.Text = text;
            Toast("识别到剪贴板链接", text.Length > 96 ? text[..96] + "..." : text);
        }
        catch (Exception ex)
        {
            AppLog.Warn("剪贴板识别失败: " + ex.Message);
        }
    }

    // ───────────────────────────── 小工具 ─────────────────────────────

    private async void Run(Func<Task> work, string? errTitle = null)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            if (errTitle != null) Toast(errTitle, ex.Message, ToastKind.Warning);
            else AppLog.Warn("目录页操作失败: " + ex.Message);
        }
    }
}
