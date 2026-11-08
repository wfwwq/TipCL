// 来源: Windows 版参考仓库 src/TipCL/Pages/Plugins/PluginsPage*.cs（M2-B4，WPF → Avalonia 移植整合）。
// M2 阶段数据面：列表 / 启停 / 卸载 / 本地安装 / 开发链接走 Core 的 PluginStateStore + PluginDirs +
// PluginManifest（无 RPC 桥）；市场索引经 Core.PluginMarket 直连（镜像语义同后端）；插件运行时
// （Node/.NET 宿主）留 M3，安装/更新按钮与运行状态按 R3 §7 的降级口径给占位提示。
// WebView2 承载面（page/aiView/overlay）按 UiDispatch 降级为「占位 + 在浏览器打开」。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

/// <summary>
/// 顶部页签「插件」：已安装 / 市场 分段；开发者、插件设置（市场源等）收在右上 ⋯ 菜单。
/// 插件设置是本页里的子视图（Windows 版同构：不是对话框）。
/// </summary>
public sealed class PluginsPage : PageBase
{
    private enum Tab { Installed, Market, Dev }

    private const string MoreGlyph = "⋯";
    private const string RefreshGlyph = "⟳";
    private const string SettingsGlyph = "⚙";
    private const string DevGlyph = "⌨";
    private const string SearchGlyph = "⌕";
    private const string BackGlyph = "←";

    private readonly ToggleButton _tInstalled;
    private readonly ToggleButton _tMarket;
    private readonly ContentControl _body = new ContentControl { Focusable = false };

    private Tab _tab;

    private readonly UniformGrid _installedHost = new UniformGrid { Margin = new Thickness(15, 0, 15, 16) };
    private List<PluginInfo> _installed = new List<PluginInfo>();

    private TextBox _search = null!;
    private WrapPanel _tagBar = null!;
    private StackPanel _marketHost = new StackPanel { Spacing = 10 };
    private bool _marketLoaded;
    private List<MarketItem> _market = new List<MarketItem>();
    private string _tag = "";
    private Control _marketView = null!;

    private StackPanel _providerHost = new StackPanel { Spacing = 8 };
    private readonly ComboBox _logPick = new ComboBox { Width = 240 };
    private readonly TextBox _logBox = U.LogBox();
    private readonly List<string> _logIds = new List<string>();
    private Control _devView = null!;

    private readonly PluginDirs _dirs = new PluginDirs();
    private readonly PluginStateStore _store;

    public PluginsPage() : base("plugins", "插件")
    {
        _store = new PluginStateStore(_dirs.StateFile);
        _tInstalled = TabBtn("已安装", Tab.Installed);
        _tMarket = TabBtn("市场", Tab.Market);
        var more = U.IconBtn(MoreGlyph, "更多");
        var menu = new ContextMenu();
        AddItem(menu, "刷新", RefreshGlyph, delegate { _ = ReloadAllAsync(); });
        AddItem(menu, "开发者", DevGlyph, delegate { SelectTab(Tab.Dev); });
        AddItem(menu, "插件设置", SettingsGlyph, delegate { ShowSubView("插件设置", new ScrollViewer { Content = PluginSettingsCard.Build() }); });
        more.ContextMenu = menu;
        more.Click += delegate
        {
            menu.PlacementTarget = more;
            menu.Open(menu.PlacementTarget);
        };
        var head = U.G(null, "*,Auto");
        head.Margin = new Thickness(22, 16, 22, 12);
        head.Add(new Border
        {
            Theme = U.Theme("Seg.Rail"),
            Child = U.H(0, _tInstalled, _tMarket),
            HorizontalAlignment = HorizontalAlignment.Left,
        }, 0);
        head.Add(U.H(6,
            U.Btn("从文件安装", BtnKind.Normal, async delegate
            {
                string? path = await Dlg.PickFile("选择插件包", "*.zip");
                if (path != null) await InstallLocalAsync(path, dev: false);
            }), more).VCenter(), 0, 1);
        var root = U.G("Auto,*");
        root.Add(head, 0);
        root.Add(_body, 1);
        Content = root;
        BuildMarketChrome();
        BuildDev();
        HookDrop();
        SelectTab(Tab.Installed);
        AttachedToVisualTree += delegate { _ = LoadInstalledAsync(); };
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

    // ————— 拖放安装（R3 §6.4：Avalonia DragDrop + GetFiles） —————

    private void HookDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, delegate (object? s, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, delegate (object? s, DragEventArgs e)
        {
            try
            {
                var files = e.Data.GetFiles()?.ToList();
                if (files == null || files.Count == 0) return;
                string path = files[0].Path.LocalPath;
                bool zip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tipcl", StringComparison.OrdinalIgnoreCase);
                if (zip || Directory.Exists(path))
                {
                    e.Handled = true;
                    _ = InstallLocalAsync(path, dev: false);
                }
            }
            catch { }
        });
    }

    private ToggleButton TabBtn(string text, Tab tab)
    {
        var b = new ToggleButton
        {
            Content = U.Txt(text),
            Theme = U.Theme("Seg.Item"),
        };
        b.Click += delegate { SelectTab(tab); };
        return b;
    }

    private void SelectTab(Tab tab)
    {
        _tab = tab;
        _tInstalled.IsChecked = tab == Tab.Installed;
        _tMarket.IsChecked = tab == Tab.Market;
        switch (tab)
        {
            case Tab.Installed:
                _body.Content = new ScrollViewer { Content = _installedHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                _ = LoadInstalledAsync();
                break;
            case Tab.Market:
                _body.Content = _marketView;
                if (!_marketLoaded) _ = LoadMarketAsync(refresh: false);
                break;
            case Tab.Dev:
                _body.Content = _devView;
                _ = LoadDevAsync();
                break;
        }
    }

    private async Task ReloadAllAsync()
    {
        await LoadInstalledAsync();
        if (_tab == Tab.Market) await LoadMarketAsync(refresh: true);
        else if (_tab == Tab.Dev) await LoadDevAsync();
    }

    // ————— 已安装 —————

    private async Task LoadInstalledAsync()
    {
        List<PluginInfo> list = new List<PluginInfo>();
        _store.Load();
        var records = _store.All().ToDictionary(r => r.Id, StringComparer.Ordinal);
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(_dirs.PluginsRoot))
            {
                string id = Path.GetFileName(dir);
                if (id.StartsWith(".")) continue;
                var info = InfoFromDir(id, dir, records.TryGetValue(id, out var rec) ? rec : null);
                if (info != null) list.Add(info);
                records.Remove(id);
            }
        }
        catch { }
        foreach (var rec in records.Values)
        {
            // 记录在但目录没了（开发文件夹被移走等）：仍列出，卡片里给提示
            list.Add(new PluginInfo
            {
                Id = rec.Id,
                Name = rec.Id,
                Version = rec.Version,
                Enabled = rec.Enabled,
                Source = rec.Source,
                DevPath = rec.DevPath,
                Error = (rec.Source == "dev" && rec.DevPath.Length > 0) ? "开发文件夹不存在：" + rec.DevPath : "插件目录缺失",
            });
        }
        _installed = list;
        await Dispatcher.UIThread.InvokeAsync(RenderInstalled);
    }

    private PluginInfo? InfoFromDir(string id, string dir, PluginRecord? rec)
    {
        try
        {
            var m = PluginManifest.LoadDir(dir);
            return new PluginInfo
            {
                Id = id,
                Name = m.Name,
                Version = m.Version,
                Author = m.Author,
                Description = m.Description,
                Runtime = m.Runtime,
                Homepage = m.Homepage,
                Enabled = rec?.Enabled ?? true,
                Source = rec?.Source ?? "local",
                DevPath = rec?.DevPath ?? "",
                Permissions = m.Permissions,
                Granted = rec?.GrantedPermissions ?? new List<string>(),
                State = (rec?.Enabled ?? true) ? "stopped" : "",
            };
        }
        catch (Exception ex)
        {
            return new PluginInfo
            {
                Id = id,
                Name = id,
                Enabled = rec?.Enabled ?? true,
                Source = rec?.Source ?? "local",
                DevPath = rec?.DevPath ?? "",
                Error = "清单读取失败：" + ex.Message,
            };
        }
    }

    private void RenderInstalled()
    {
        _installedHost.Children.Clear();
        _installedHost.Columns = (_installed.Count == 0) ? 1 : 3;
        if (_installed.Count == 0)
        {
            _installedHost.Children.Add(U.Empty("◇", "还没有安装插件", "去插件市场看看，或者把插件 zip 拖进这个页面。"));
            return;
        }
        foreach (PluginInfo p in _installed.OrderBy(x => x.DisplayName, StringComparer.CurrentCulture))
        {
            _installedHost.Children.Add(InstalledCard(p));
        }
    }

    private static Border StateBadge(PluginInfo p)
    {
        if (!p.Enabled && p.State != "restart_required")
        {
            return U.Tag("已禁用");
        }
        return p.State switch
        {
            "running" => U.Tag("运行中", "B.Ok", "B.AccentSoft"),
            "error" => U.Tag("出错", "B.Danger", "B.DangerSoft"),
            "restart_required" => U.Tag("重启后生效", "B.Warn", "B.WarnSoft"),
            "loading" => U.Tag("加载中", "B.Info", "B.InfoSoft"),
            "stopped" => U.Tag("已停止"),
            _ => U.Tag(p.State.Length > 0 ? p.State : "未知"),
        };
    }

    private Border InstalledCard(PluginInfo p)
    {
        string n = p.DisplayName.Trim();
        var letter = U.Txt(n.Length > 0 ? char.ToUpperInvariant(n[0]).ToString() : "?", 14, bold: true, "B.OnAccentStrong");
        letter.FontWeight = FontWeight.ExtraBold;
        var logo = U.Pane(letter.Center(), "B.AccentStrong", 9, 0);
        logo.Background = PluginLogo.BrushFor(p.Id.Length > 0 ? p.Id : n);
        logo.Width = 36;
        logo.Height = 36;
        var name = U.Txt(p.DisplayName, 15, bold: true).Trim();
        name.FontWeight = FontWeight.ExtraBold;
        name.Tip(p.Id);
        var sw = U.Switch(p.Enabled);
        sw.Tip(p.Enabled ? "点击禁用" : "点击启用");
        sw.IsCheckedChanged += delegate
        {
            bool on = sw.IsChecked == true;
            try
            {
                _store.Update(p.Id, r => r.Enabled = on);
            }
            catch
            {
                sw.IsChecked = !on;
                ToastHost.Show(on ? "启用失败" : "禁用失败", "", ToastKind.Error);
                return;
            }
            ToastHost.Show(on ? "已启用" : "已禁用", p.DisplayName + (on ? "（插件运行时 M3 接通后生效）" : ""), ToastKind.Success);
            _ = LoadInstalledAsync();
        };
        var top = U.G(null, "Auto,*,Auto");
        top.Add(logo, 0);
        top.Add(name.VCenter().Mk(10, 0, 8, 0), 0, 1);
        top.Add(sw.VCenter(), 0, 2);
        var desc = U.Muted(p.Description).Wrap();
        desc.FontSize = 12.5;
        desc.Height = 36;
        desc.TextTrimming = TextTrimming.CharacterEllipsis;
        desc.Margin = new Thickness(0, 10, 0, 12);
        var moreBtn = U.IconBtn(MoreGlyph, "更多");
        var menu = new ContextMenu();
        AddItem(menu, "设置", SettingsGlyph, delegate { ShowSettings(p); });
        AddItem(menu, "打开数据目录", "▤", delegate { OpenFolder(_dirs.DataDir(p.Id)); });
        AddItem(menu, "查看日志", "≡", delegate { _ = ShowLogAsync(p.Id, p.DisplayName); });
        if (p.Source == "dev" && p.DevPath.Length > 0)
        {
            AddItem(menu, "打开开发文件夹", "▤", delegate { OpenFolder(p.DevPath); });
        }
        if (p.Homepage.Length > 0)
        {
            AddItem(menu, "插件主页", "⌘", delegate { OpenUrl(p.Homepage); });
        }
        menu.Items.Add(new Separator());
        AddItem(menu, p.Source == "dev" ? "取消链接" : "卸载", "✕", async delegate { await UninstallAsync(p); });
        moreBtn.ContextMenu = menu;
        moreBtn.Click += delegate
        {
            menu.PlacementTarget = moreBtn;
            menu.Open(menu.PlacementTarget);
        };
        var foot = U.G(null, "*,Auto");
        foot.Add(U.H(6,
            U.Muted(p.Source == "dev" ? "开发链接" : (p.Version.Length > 0 ? "v" + p.Version : "")).VCenter(),
            (p.State == "running" && p.Enabled) ? null : StateBadge(p),
            p.Source == "dev" ? U.Tag("开发", "B.Warn", "B.WarnSoft").Tip(p.DevPath) : null,
            p.Source == "local" ? U.Tag("本地") : null,
            p.Runtime == "dotnet" ? U.Tag(".NET") : null).VCenter(), 0);
        foot.Add(moreBtn.VCenter(), 0, 1);
        var body = U.V(0, top, desc, foot);
        if (p.Error.Length > 0)
        {
            body.Children.Add(U.Txt(p.Error, 12, bold: false, "B.Danger").Wrap().Mk(0, 6, 0, 0));
        }
        var card = U.Card(body, 16);
        card.Padding = new Thickness(18, 16, 18, 16);
        // UniformGrid 单元格会把子元素拉满整格高度；卡片要按内容自适应（贴效果图）
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(7);
        return card;
    }

    private async Task UninstallAsync(PluginInfo p)
    {
        var delData = new CheckBox { Content = U.Txt("同时删除数据（插件数据目录）"), IsChecked = false };
        var body = U.V(10,
            U.Muted(p.Source == "dev" ? "取消链接后开发文件夹本身不会被删除。" : "会删除插件文件；之后可以从市场重新安装。").Wrap(),
            delData);
        body.MinWidth = 360;
        if (!await Dlg.Ask(p.Source == "dev" ? $"取消链接「{p.DisplayName}」？" : $"卸载「{p.DisplayName}」？", body)) return;
        try
        {
            if (p.Source != "dev")
            {
                string dir = Path.Combine(_dirs.PluginsRoot, p.Id);
                if (delData.IsChecked == true)
                {
                    TryDelete(_dirs.DataDir(p.Id));
                }
                TryDelete(dir);
            }
            _store.Remove(p.Id);
            ToastHost.Show(p.Source == "dev" ? "已取消链接" : "已卸载", p.DisplayName, ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastHost.Show("卸载失败", ex.Message, ToastKind.Error);
        }
        await LoadInstalledAsync();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private async Task InstallLocalAsync(string path, bool dev)
    {
        string label = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
        try
        {
            string id;
            string version = "";
            if (dev)
            {
                var m = PluginManifest.LoadDir(path);
                id = m.Id;
                version = m.Version;
                _store.Update(id, r =>
                {
                    r.Source = "dev";
                    r.DevPath = path;
                    r.Enabled = true;
                    if (version.Length > 0) r.Version = version;
                });
                ToastHost.Show("已链接开发文件夹", id, ToastKind.Success);
            }
            else
            {
                if (Directory.Exists(path))
                {
                    var m = PluginManifest.LoadDir(path);
                    id = m.Id;
                    version = m.Version;
                    string dest = Path.Combine(_dirs.PluginsRoot, id);
                    TryDelete(dest);
                    CopyDir(path, dest);
                }
                else
                {
                    var m = PluginMarket.ReadZipManifest(path).manifest;
                    id = m.Id;
                    version = m.Version;
                    string dest = Path.Combine(_dirs.PluginsRoot, id);
                    TryDelete(dest);
                    Directory.CreateDirectory(dest);
                    ZipFile.ExtractToDirectory(path, dest, overwriteFiles: true);
                }
                _store.Update(id, r =>
                {
                    r.Source = "local";
                    r.Enabled = true;
                    if (version.Length > 0) r.Version = version;
                });
                ToastHost.Show("插件已安装", id, ToastKind.Success);
            }
        }
        catch (Exception ex)
        {
            ToastHost.Show("安装失败", ex.Message, ToastKind.Error);
        }
        await LoadInstalledAsync();
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, file);
            string target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // ————— 市场 —————

    private void BuildMarketChrome()
    {
        _search = U.Input("搜索插件名、作者、简介", "", 300);
        _search.TextChanged += delegate { RenderMarket(); };
        var refresh = U.Btn("刷新市场", BtnKind.Chip, delegate { _ = LoadMarketAsync(refresh: true); }, RefreshGlyph);
        var sources = U.Btn("市场地址", BtnKind.Chip, async delegate
        {
            var next = await PluginSettingsCard.EditIndexesDialogAsync(PluginSettingsCard.ParseIndexes(Config.CONFIG.Get(PluginMarket.SettingIndexes)));
            if (next != null) await LoadMarketAsync(refresh: true);
        }, "✎");
        var info = U.Small("");
        _tagBar = new WrapPanel();
        var top = U.G(null, "Auto,*,Auto");
        top.Add(U.H(6, U.Txt(SearchGlyph, 13, fg: "B.InkMuted").VCenter(), _search).VCenter(), 0);
        top.Add(info.VCenter().Mk(12, 0, 0, 0), 0, 1);
        top.Add(U.H(6, sources, refresh).VCenter(), 0, 2);
        _marketView = new ScrollViewer
        {
            Content = U.V(10, top, _tagBar, _marketHost).Mk(15, 4, 15, 16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private async Task LoadMarketAsync(bool refresh)
    {
        if (_market.Count == 0)
        {
            await Dispatcher.UIThread.InvokeAsync(delegate
            {
                _marketHost.Children.Clear();
                _marketHost.Children.Add(U.Muted("正在读取市场……"));
            });
        }
        List<MarketItem> items = new List<MarketItem>();
        try
        {
            items = await Task.Run(delegate
            {
                var market = new PluginMarket
                {
                    InstalledVersionOf = id => _store.Get(id)?.Version ?? "",
                };
                var entries = market.Entries(refresh);
                var list = new List<MarketItem>();
                foreach (PyDict entry in entries)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(PyJson.Dumps(entry, ensureAscii: false));
                        var mi = MarketItem.From(doc.RootElement.Clone());
                        if (mi.Id.Length > 0) list.Add(mi);
                    }
                    catch { }
                }
                return list;
            });
        }
        catch (Exception ex)
        {
            _marketLoaded = true;
            _market.Clear();
            await Dispatcher.UIThread.InvokeAsync(delegate
            {
                _marketHost.Children.Clear();
                _marketHost.Children.Add(U.Empty("◇", "插件市场打不开", ex.Message));
            });
            return;
        }
        _marketLoaded = true;
        _market = items;
        await Dispatcher.UIThread.InvokeAsync(delegate
        {
            RenderTags();
            RenderMarket();
        });
    }

    private void RenderTags()
    {
        _tagBar.Children.Clear();
        var tags = _market.SelectMany(m => m.Tags)
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key).Take(24).ToList();
        _tagBar.IsVisible = tags.Count > 0;
        if (_tag.Length > 0 && !tags.Contains(_tag)) _tag = "";
        AddTag("", "全部");
        foreach (string t in tags) AddTag(t, t);

        void AddTag(string key, string text)
        {
            var b = U.Btn(text, _tag == key ? BtnKind.Primary : BtnKind.Chip);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Click += delegate
            {
                _tag = key;
                RenderTags();
                RenderMarket();
            };
            _tagBar.Children.Add(b);
        }
    }

    private void RenderMarket()
    {
        if (!_marketLoaded) return;
        _marketHost.Children.Clear();
        if (_market.Count == 0)
        {
            var empty = U.Empty("◇", "还没有配置插件市场", "填一个或多个插件市场的索引地址（index.json），就能在这里浏览和安装插件。");
            empty.Children.Add(U.Btn("编辑市场地址", BtnKind.Primary, async delegate
            {
                var next = await PluginSettingsCard.EditIndexesDialogAsync(PluginSettingsCard.ParseIndexes(Config.CONFIG.Get(PluginMarket.SettingIndexes)));
                if (next != null) await LoadMarketAsync(refresh: true);
            }).Center());
            _marketHost.Children.Add(empty);
            return;
        }
        string q = _search.Text.Trim();
        var shown = _market
            .Where(m => (_tag.Length == 0 || m.Tags.Contains(_tag))
                        && (q.Length == 0 || Contains(m.DisplayName, q) || Contains(m.Id, q) || Contains(m.Author, q) || Contains(m.Description, q)))
            .OrderByDescending(m => m.Updatable).ThenByDescending(m => m.Verified)
            .ThenByDescending(m => m.Downloads).ThenBy(m => m.DisplayName, StringComparer.CurrentCulture)
            .ToList();
        if (shown.Count == 0)
        {
            _marketHost.Children.Add(U.Empty("⌕", _market.Count == 0 ? "市场里还没有插件" : "没有找到匹配的插件"));
            return;
        }
        foreach (MarketItem m in shown)
        {
            _marketHost.Children.Add(MarketCard(m));
        }

        static bool Contains(string s, string q2)
        {
            return s.IndexOf(q2, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private static Border VerifiedTag(bool verified)
    {
        return verified
            ? U.Tag("已审核", "B.Ok", "B.AccentSoft")
            : U.Tag("未审核", "B.Danger", "B.DangerSoft");
    }

    private Button InstallButton(MarketItem m)
    {
        if (m.Installed && !m.Updatable)
        {
            var done = U.Btn("已安装", BtnKind.Ghost);
            done.IsEnabled = false;
            return done;
        }
        var b = U.Btn(m.Updatable ? "更新" : "安装", m.Updatable ? BtnKind.Soft : BtnKind.Primary);
        b.Click += delegate
        {
            ToastHost.Show("插件运行时未接通", "安装 / 更新要等 M3 的插件宿主落地后可用。", ToastKind.Warning);
        };
        if (m.Updatable && m.InstalledVersion.Length > 0)
        {
            b.Tip($"{m.InstalledVersion} → {m.Version}");
        }
        return b;
    }

    private Border MarketCard(MarketItem m)
    {
        string n = m.DisplayName.Trim();
        var letter = U.Txt(n.Length > 0 ? char.ToUpperInvariant(n[0]).ToString() : "?", 14, bold: true, "B.AccentText");
        var icon = U.Pane(letter.Center(), "B.AccentTint", 10, 0);
        icon.Width = 48;
        icon.Height = 48;
        var title = U.H(6,
            U.Txt(m.DisplayName, 14, bold: true).Trim().VCenter(),
            m.Version.Length > 0 ? U.Tag("v" + m.Version) : null,
            VerifiedTag(m.Verified),
            m.Runtime == "dotnet" ? U.Tag(".NET") : null);
        var metaParts = new List<string>();
        if (m.Author.Length > 0) metaParts.Add("作者 " + m.Author);
        if (m.Downloads > 0) metaParts.Add(m.Downloads.ToString("N0") + " 下载");
        if (m.Size > 0) metaParts.Add(Fmt.Size(m.Size));
        var desc = U.Muted(m.Description).Wrap();
        desc.MaxHeight = 40;
        desc.TextTrimming = TextTrimming.CharacterEllipsis;
        var tags = new WrapPanel();
        foreach (string t in m.Tags.Take(6))
        {
            var tag = U.Tag(t);
            tag.Margin = new Thickness(0, 0, 4, 0);
            tags.Children.Add(tag);
        }
        var info = U.V(3,
            title,
            metaParts.Count > 0 ? U.Small(string.Join(" · ", metaParts)) : null,
            m.Description.Length > 0 ? desc : null,
            tags.Children.Count > 0 ? tags : null);
        var install = InstallButton(m);
        var detail = U.IconBtn("≡", "详情", delegate { ShowMarketDetail(m); });
        var g = U.G(null, "Auto,*,Auto");
        g.Add(icon.VTop(), 0);
        g.Add(info.Mk(14, 0, 12, 0), 0, 1);
        g.Add(U.H(6, detail, install).VCenter(), 0, 2);
        return U.RowCard(g, click: delegate
        {
            ShowMarketDetail(m);
        }, padding: 14);
    }

    private async void ShowMarketDetail(MarketItem m)
    {
        var body = U.V(14);
        string n = m.DisplayName.Trim();
        var letter = U.Txt(n.Length > 0 ? char.ToUpperInvariant(n[0]).ToString() : "?", 14, bold: true, "B.AccentText");
        var icon = U.Pane(letter.Center(), "B.AccentTint", 10, 0);
        icon.Width = 44;
        icon.Height = 44;
        var headParts = new List<string>();
        if (m.Author.Length > 0) headParts.Add("作者 " + m.Author);
        headParts.Add(m.Id);
        if (m.Size > 0) headParts.Add(Fmt.Size(m.Size));
        body.Children.Add(U.H(12, icon.VTop(), U.V(3,
            U.H(6,
                U.Txt(m.DisplayName, 16, bold: true).VCenter(),
                m.Version.Length > 0 ? U.Tag("v" + m.Version) : null,
                VerifiedTag(m.Verified)),
            U.Small(string.Join(" · ", headParts)))));
        if (m.Description.Length > 0)
        {
            body.Children.Add(U.Txt(m.Description, 13, bold: false, "B.InkSoft").Wrap());
        }
        if (m.Screenshots.Count > 0)
        {
            var shots = U.H(8, U.Small(string.Join("  ", m.Screenshots.Take(3))).Wrap());
            body.Children.Add(U.H3("截图"));
            body.Children.Add(shots);
        }
        body.Children.Add(U.H3("需要的权限"));
        body.Children.Add(PluginPermsView.List(m.Permissions));
        if (m.Changelog.Length > 0)
        {
            body.Children.Add(U.H3("更新日志"));
            body.Children.Add(U.Txt(m.Changelog, 12.5, bold: false, "B.InkSoft").Wrap());
        }
        var install = InstallButton(m);
        var actions = U.H(8, install);
        if (m.Homepage.Length > 0)
        {
            actions.Children.Insert(0, U.Btn("插件主页", BtnKind.Normal, delegate { OpenUrl(m.Homepage); }, "⌘"));
        }
        // 操作按钮行放滚动区外，钉在弹窗底部（WP-S11 语义）
        var dlg = new Window
        {
            Title = m.DisplayName,
            Width = 760,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new Border
            {
                Padding = new Thickness(22, 18),
                Child = U.G("*,Auto")
                    .Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 0)
                    .Add(actions, 1),
            },
        };
        var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner != null) await dlg.ShowDialog(owner); else dlg.Show();
    }

    // ————— 开发者 —————

    private void BuildDev()
    {
        var zip = U.Btn("从 zip 安装", BtnKind.Soft, async delegate
        {
            string? path = await Dlg.PickFile("选择插件包", "*.zip");
            if (path != null) await InstallLocalAsync(path, dev: false);
        });
        var folder = U.Btn("从文件夹安装", BtnKind.Soft, async delegate
        {
            string? path = await Dlg.PickFolder("选择插件文件夹（里面有 tipcl-plugin.json）");
            if (path != null) await InstallLocalAsync(path, dev: false);
        });
        var link = U.Btn("链接开发文件夹", BtnKind.Primary, async delegate
        {
            string? path = await Dlg.PickFolder("选择插件开发文件夹（不复制，改了自动重载）");
            if (path != null) await InstallLocalAsync(path, dev: true);
        });
        var local = U.Card(U.V(8,
            U.Section("本地安装", "zip / 文件夹会复制进 TipCL；开发文件夹只链接，插件运行时（M3）接通后热重载"),
            U.H(8, zip, folder, link),
            U.Small("也可以直接把插件 zip 或文件夹拖进这个页面。")));
        var runtime = U.Card(U.V(4,
            U.Section("运行环境", "Node 插件宿主（M3 落地，当前只有占位状态）"),
            U.V(2,
                U.Field("Node 宿主", U.Muted("未安装（M3 接通插件运行时后自动管理）"), null, 140),
                U.Field("网页视图", U.Muted("Linux 版不内置 WebView，插件网页界面在浏览器打开"), null, 140))));
        var addProvider = U.Btn("添加供应商", BtnKind.Chip, delegate
        {
            ToastHost.Show("插件运行时未接通", "模型供应商管理要等 M3 的插件宿主落地后可用。", ToastKind.Warning);
        }, "⬒");
        var providers = U.Card(U.V(8,
            U.Section("模型供应商", "插件直接调用模型时可选的 OpenAI 兼容端点；默认的 tipcl 就是你在 AI 设置里配的模型"),
            addProvider,
            _providerHost));
        _logBox.Height = 300;
        _logPick.SelectionChanged += delegate { _ = LoadLogAsync(); };
        var logRefresh = U.Btn("刷新", BtnKind.Chip, delegate { _ = LoadLogAsync(); }, RefreshGlyph);
        var logFolder = U.Btn("打开日志文件夹", BtnKind.Chip, delegate { OpenFolder(_dirs.LogsRoot); });
        var logs = U.Card(U.V(8,
            U.Section("插件日志", "console.* 和 ctx.logger 的输出"),
            U.H(8, _logPick, logRefresh, logFolder),
            _logBox));
        _devView = new ScrollViewer
        {
            Content = U.V(10, local, runtime, providers, logs).Mk(15, 4, 15, 16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private async Task LoadDevAsync()
    {
        if (_installed.Count == 0) await LoadInstalledAsync();
        string keep = _logPick.SelectedIndex >= 0 && _logPick.SelectedIndex < _logIds.Count ? _logIds[_logPick.SelectedIndex] : "";
        _logIds.Clear();
        _logIds.AddRange(_installed.Select(p => p.Id));
        _logIds.Add("host");
        var names = _installed.Select(p => p.DisplayName).ToList();
        names.Add("Node 宿主");
        await Dispatcher.UIThread.InvokeAsync(delegate
        {
            _logPick.Items.Clear();
            foreach (string n in names) _logPick.Items.Add(n);
            int idx = _logIds.IndexOf(keep);
            _logPick.SelectedIndex = idx >= 0 ? idx : (_logIds.Count > 0 ? 0 : -1);
        });
        _providerHost.Children.Clear();
        var providers = _store.Providers();
        if (providers.Count == 0)
        {
            _providerHost.Children.Add(U.Muted("只有默认的 tipcl（跟随 AI 设置）。"));
        }
        foreach (var p in providers)
        {
            _providerHost.Children.Add(U.RowCard(U.V(2,
                U.H(6, U.Txt(p.Name, 13, bold: true), U.Tag(p.Id)),
                U.Small(p.BaseUrl).Trim()), null, 10));
        }
        await LoadLogAsync();
    }

    private async Task LoadLogAsync()
    {
        int i = _logPick.SelectedIndex;
        if (i < 0 || i >= _logIds.Count)
        {
            _logBox.Text = "";
            return;
        }
        string id = _logIds[i];
        _logBox.Text = id == "host" ? "（Node 宿主要等 M3 接通插件运行时后才有日志。）" : await ReadLogAsync(id);
    }

    private static async Task<string> ReadLogAsync(string id, int lines = 400)
    {
        var dirs = new PluginDirs();
        string file = dirs.LogFile(id);
        try
        {
            if (!File.Exists(file)) return "（日志是空的）";
            var all = File.ReadLines(file).ToList();
            return string.Join("\n", all.Skip(Math.Max(0, all.Count - lines)));
        }
        catch (Exception ex)
        {
            return "读不到日志：" + ex.Message;
        }
    }

    private async Task ShowLogAsync(string id, string title)
    {
        var box = U.LogBox();
        box.Height = 380;
        box.MinWidth = 640;
        box.Text = await ReadLogAsync(id);
        var dlg = new Window
        {
            Title = $"{title} · 日志",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = true,
            Width = 860,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new Border { Padding = new Thickness(16), Child = box },
        };
        dlg.Show();
    }

    // ————— 插件设置子视图 —————

    private void ShowSettings(PluginInfo p)
    {
        // 插件注册了 kind "settings" 的自定义界面 → 占位 + 在浏览器打开（WebView 降级口径）；
        // 自动表单的配置值要等插件运行时（M3）读 plugins_get_config 后填充。
        var custom = SettingsContributionOf(p.Id);
        Control content;
        if (custom != null)
        {
            content = UiDispatch.Placeholder(custom);
        }
        else
        {
            content = U.V(10,
                U.Section("配置", "改完点「保存」，插件里立即生效，不用重启"),
                U.Card(U.V(2, U.Muted("插件配置要等 M3 接通插件运行时后读取（plugins_get_config）。"))),
                U.Card(U.V(2, U.Section("凭据", "API Key 等，加密保存在本机，只有这个插件能读"),
                    U.Muted("凭据读写要等 M3 接通插件运行时。"))));
        }
        content.Mk(26, 6, 26, 22);
        content.MaxWidth = 820;
        ShowSubView($"{p.DisplayName} · 设置", new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    private UiContribution? SettingsContributionOf(string pluginId)
    {
        string dir = Path.Combine(_dirs.PluginsRoot, pluginId);
        if (!Directory.Exists(dir)) return null;
        try
        {
            var m = PluginManifest.LoadDir(dir);
            foreach ((string kind, PyDict spec) in m.UiContributions)
            {
                if (kind != "settings") continue;
                using var doc = JsonDocument.Parse(PyJson.Dumps(spec, ensureAscii: false));
                return UiContribution.From(doc.RootElement.Clone());
            }
        }
        catch { }
        return null;
    }

    /// <summary>页面内子视图：顶部带返回。</summary>
    private void ShowSubView(string title, Control content)
    {
        var back = U.IconBtn(BackGlyph, "返回", delegate { SelectTab(_tab); });
        var head = U.G(null, "Auto,*,Auto");
        head.Add(back.VCenter(), 0);
        head.Add(U.H2(title).VCenter().Mk(6, 0, 0, 0), 0, 1);
        head.Margin = new Thickness(18, 12, 22, 8);
        var g = U.G("Auto,*");
        g.Add(head, 0);
        g.Add(content, 1);
        _body.Content = g;
    }

    // ————— 公共小件 —————

    internal static void AddItem(ItemsControl menu, string header, string glyph, Action act)
    {
        var mi = new MenuItem
        {
            Header = U.H(6, U.Txt(glyph, 13), U.Txt(header)),
        };
        mi.Click += delegate { act(); };
        menu.Items.Add(mi);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            OpenUrl(path);
        }
        catch (Exception ex)
        {
            ToastHost.Show("打不开文件夹", ex.Message, ToastKind.Warning);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ToastHost.Show("打不开链接", ex.Message, ToastKind.Warning);
        }
    }
}
