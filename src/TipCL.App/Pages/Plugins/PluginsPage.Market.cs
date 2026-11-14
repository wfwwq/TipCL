// ————— 市场分段(索引 / 标签 / 卡片 / 详情 / 安装) —————
// M4-H P6:从 PluginsPage.cs 拆出,与 Windows 版 Pages/Plugins/PluginsPage.Market.cs 同名同分段。

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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.App.Common;
using TipCL.App.Common.Plugins;
using TipCL.Core;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

public sealed partial class PluginsPage
{
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
        List<string> errors = new List<string>();
        Dictionary<string, PyDict> entries = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        try
        {
            (items, errors, entries) = await Task.Run(delegate
            {
                var market = new PluginMarket
                {
                    InstalledVersionOf = id => _store.Get(id)?.Version ?? "",
                };
                // Rows 比 Entries 多了 installed / updatable / permissionRows,LINUX 侧就地取用
                var rows = market.Rows(refresh);
                var list = new List<MarketItem>();
                var errs = new List<string>();
                var ents = new Dictionary<string, PyDict>(StringComparer.Ordinal);
                foreach (PyDict row in Py.ListOr(rows.Get("plugins")).OfType<PyDict>())
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(PyJson.Dumps(row, ensureAscii: false));
                        var mi = MarketItem.From(doc.RootElement.Clone());
                        if (mi.Id.Length == 0) continue;
                        list.Add(mi);
                        ents[mi.Id] = row;
                    }
                    catch { }
                }
                foreach (PyDict e in Py.ListOr(rows.Get("errors")).OfType<PyDict>())
                {
                    errs.Add(Py.S(e.Get("market")) + ": " + Py.S(e.Get("error")));
                }
                return (list, errs, ents);
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
        _entries.Clear();
        foreach (var kv in entries) _entries[kv.Key] = kv.Value;
        await Dispatcher.UIThread.InvokeAsync(delegate
        {
            _marketInfo.Text = errors.Count > 0 ? $"{errors.Count} 个市场地址读取失败" : "";
            ToolTip.SetTip(_marketInfo, errors.Count > 0 ? string.Join("\n", errors) : null);
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
        string q = _search.Text!.Trim();
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
        b.Click += delegate { _ = InstallFromMarketAsync(m); };
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
            body.Children.Add(U.H3("截图"));
            body.Children.Add(BuildScreenshots(m.Screenshots));
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

    /// <summary>
    /// 市场详情的截图:能下就显示缩略图(Common/Thumbs 的远程图 + 内存缓存),
    /// 下不动时留一块带「在浏览器打开」的占位 —— 不用假图,也不静默空着。
    /// </summary>
    private static Control BuildScreenshots(List<string> urls)
    {
        var wrap = new WrapPanel();
        foreach (string url in urls.Take(6))
        {
            string link = url;
            var frame = new Border
            {
                Width = 200,
                Height = 118,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
            };
            frame[!Border.BorderBrushProperty] = Res.Bind("B.LineSoft");
            frame[!Border.BackgroundProperty] = Res.Bind("B.Paper2");

            var pending = U.Small("加载中...").Center();
            frame.Child = pending;
            frame.Cursor = new Cursor(StandardCursorType.Hand);
            frame.PointerReleased += (_, _) => OpenUrl(link);
            wrap.Children.Add(frame);

            _ = LoadShotAsync(frame, link);
        }
        return wrap;
    }

    private static async Task LoadShotAsync(Border frame, string url)
    {
        Bitmap? bmp = null;
        try
        {
            bmp = await Thumbs.LoadAsync(url, 400);
        }
        catch (Exception)
        {
            bmp = null;
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (bmp != null)
            {
                frame.Child = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.UniformToFill,
                };
                ToolTip.SetTip(frame, "点击在浏览器里看大图");
                return;
            }
            // 加载失败(网络/防盗链):如实说明并给浏览器入口,不显示破图
            var fallback = U.V(4,
                U.Txt("图片加载失败", 11.5, false, "B.InkMuted").Center(),
                U.Txt("点击用浏览器打开", 11, false, "B.AccentText").Center()).Center();
            frame.Child = fallback;
            ToolTip.SetTip(frame, url);
        });
    }

    // ————— 安装 / 更新(真实现:下载 + sha256 + 原子替换,见 Core/Plugins/PluginInstaller) —————

    /// <summary>市场安装 / 更新:未审核门 → 权限确认 → 后台安装 → 刷新列表。</summary>
    private async Task InstallFromMarketAsync(MarketItem m)
    {
        PyDict? entry = _entries.TryGetValue(m.Id, out var e) ? e : null;
        if (entry == null)
        {
            ToastHost.Show("安装失败", "市场条目已经过期,请刷新后重试。", ToastKind.Warning);
            return;
        }
        bool verified = Py.Truthy(entry.Get("verified"));
        if (!verified && !PluginMarket.AllowUnverified())
        {
            bool go = await Dlg.Confirm("未审核的插件",
                $"「{m.DisplayName}」没有经过市场审核。安装未审核插件需要先打开「允许安装未审核插件」。" +
                "\n\n现在打开并继续安装?");
            if (!go) return;
            Config.CONFIG.Set(PluginMarket.SettingAllowUnverified, true);
            Config.CONFIG.Save();
        }
        List<string> perms = EffectivePermissionsOf(Py.ListOr(entry.Get("permissions")).Select(Py.S).ToList(), Py.S(Py.Or(entry.Get("runtime"), "node")), verified);
        if (!await ConfirmPermissionsAsync(m, perms, verified)) return;

        ToastHost.Show(m.Updatable ? "正在更新" : "正在安装", m.DisplayName);
        string err = "";
        try
        {
            await Task.Run(delegate
            {
                PluginRuntime.InstallFromMarket(entry, perms, line => AppLog.Info("[插件安装] " + line));
            });
        }
        catch (Exception ex)
        {
            err = ex.Message;
        }
        if (err.Length > 0)
        {
            ToastHost.Show(m.Updatable ? "更新失败" : "安装失败", err, ToastKind.Error);
            return;
        }
        ToastHost.Show(m.Updatable ? "已更新" : "已安装", m.DisplayName + " v" + m.Version, ToastKind.Success);
        PluginUiRefresh();
        await LoadInstalledAsync();
        await LoadMarketAsync(refresh: false);
    }

    /// <summary>权限确认(参考版由内核在下载完成时发 plugins.confirm;Linux 没有宿主,装之前先问)。</summary>
    private static async Task<bool> ConfirmPermissionsAsync(MarketItem m, List<string> perms, bool verified)
    {
        var body = U.V(10,
            U.H(8, U.Txt(m.DisplayName, 15, bold: true), m.Version.Length > 0 ? U.Tag("v" + m.Version) : null, VerifiedTag(verified)));
        if (!verified)
        {
            body.Children.Add(U.Small("这个插件没有经过市场审核,来源和代码都没人检查过。").Wrap());
        }
        if (perms.Contains(PluginPermissions.System))
        {
            body.Children.Add(U.Pane(U.Txt("这个插件会拿到完整的电脑权限(文件、网络、运行程序)。只安装你信任的来源。", 12.5, bold: false, "B.Danger").Wrap(), "B.DangerSoft", 10, 10));
        }
        body.Children.Add(U.Muted("它需要这些权限:"));
        body.Children.Add(new ScrollViewer
        {
            Content = PluginPermsView.List(perms),
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        body.MinWidth = 420;
        return await Dlg.Ask("安装插件", body, "允许并安装", "取消", danger: perms.Contains(PluginPermissions.System) && !verified, width: 560);
    }

    /// <summary>Node 插件自动带 system(参考版 PluginPerms.Effective 的语义)。</summary>
    internal static List<string> EffectivePermissionsOf(IEnumerable<string> perms, string runtime, bool verified)
    {
        var list = perms.Distinct().ToList();
        if ((runtime.Length == 0 ? "node" : runtime) == "node" && !list.Contains(PluginPermissions.System)) list.Add(PluginPermissions.System);
        return list;
    }

    /// <summary>装完后让外壳的插件快照与顶部页签跟上。</summary>
    private void PluginUiRefresh()
    {
        (TopLevel.GetTopLevel(this) as TipCL.App.Shell.MainWindow)?.RefreshPluginUi();
    }

}
