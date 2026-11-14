// ————— 已安装分段(卡片 / 启停 / 卸载 / 本地安装) —————
// M4-H P6:从 PluginsPage.cs 拆出,与 Windows 版 Pages/Plugins/PluginsPage.Installed.cs 同名同分段。

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
using TipCL.App.Common.Plugins;
using TipCL.Core;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

public sealed partial class PluginsPage
{
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
        ApplyMarketBadges();
        await Dispatcher.UIThread.InvokeAsync(RenderInstalled);
        // 已经读过市场索引就顺带标上「有更新」;没读过就后台读一次(失败静默,不影响已安装列表)
        if (!_marketLoaded && !_badgeFetching)
        {
            _badgeFetching = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await LoadMarketAsync(refresh: false);
                }
                catch (Exception)
                {
                    // 市场打不开时「有更新」徽标不出现,不打扰用户
                }
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyMarketBadges();
                    RenderInstalled();
                });
            });
        }
    }

    /// <summary>「有更新」徽标的文案(有市场版本号就带上,便于一眼看出更新到哪一版)。</summary>
    internal static string UpdateBadgeText(PluginInfo p)
        => p.MarketVersion.Length > 0 ? "有更新 → v" + p.MarketVersion : "有更新";

    /// <summary>把市场索引里的版本信息贴到已安装列表上(有更新就显示徽标)。</summary>
    private void ApplyMarketBadges()
    {
        if (!_marketLoaded)
        {
            return;
        }
        var byId = new Dictionary<string, MarketItem>(StringComparer.Ordinal);
        foreach (MarketItem m in _market)
        {
            if (m.Id.Length > 0)
            {
                byId[m.Id] = m;
            }
        }
        foreach (PluginInfo p in _installed)
        {
            if (byId.TryGetValue(p.Id, out MarketItem? m))
            {
                p.UpdateAvailable = m.Updatable;
                p.MarketVersion = m.Updatable ? m.Version : "";
            }
            else
            {
                p.UpdateAvailable = false;
                p.MarketVersion = "";
            }
        }
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
                var r = PluginRuntime.SetEnabled(p.Id, on);
                if (on && !Py.Truthy(r.Get("runnable")))
                {
                    // Linux 版没有插件运行时:启用状态照落盘,但如实告诉用户它不会被执行
                    ToastHost.Show(p.DisplayName, Py.S(r.Get("reason")), ToastKind.Warning);
                }
                else
                {
                    ToastHost.Show(on ? "已启用" : "已禁用", p.DisplayName, ToastKind.Success);
                }
            }
            catch
            {
                sw.IsChecked = !on;
                ToastHost.Show(on ? "启用失败" : "禁用失败", "", ToastKind.Error);
                return;
            }
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
        AddItem(menu, "权限", "⚿", delegate { ShowPermissions(p); });
        if (p.Source == "market")
        {
            AddItem(menu, p.MarketVersion.Length > 0 ? "更新到 v" + p.MarketVersion : "从市场更新", "↑", async delegate { await UpdateFromMarketAsync(p); });
        }
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
            p.UpdateAvailable ? U.Tag(UpdateBadgeText(p), "B.Warn", "B.WarnSoft").Tip("市场里有比本机更新的版本,在右侧菜单里更新") : null,
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
            PluginRuntime.Uninstall(p.Id, delData.IsChecked == true);
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
        string err = "";
        try
        {
            // 走 Core 的 PluginInstaller:清单校验 → 暂存目录 → 原子替换(与市场安装同一条链)
            await Task.Run(delegate { PluginRuntime.InstallLocal(path, dev, null, line => AppLog.Info("[插件安装] " + line)); });
        }
        catch (Exception ex)
        {
            err = ex.Message;
        }
        if (err.Length > 0)
        {
            ToastHost.Show("安装失败", err, ToastKind.Error);
            return;
        }
        ToastHost.Show(dev ? "已链接开发文件夹" : "插件已安装", label, ToastKind.Success);
        (TopLevel.GetTopLevel(this) as TipCL.App.Shell.MainWindow)?.RefreshPluginUi();
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

    /// <summary>权限面板(参考版是 plugins_permissions 的只读展示;Linux 侧读清单 + 落盘授权)。</summary>
    private void ShowPermissions(PluginInfo p)
    {
        var perms = EffectivePermissionsOf(p.Granted.Count > 0 ? p.Granted : p.Permissions, p.Runtime, true);
        _ = Dlg.Panel($"「{p.DisplayName}」的权限",
            new ScrollViewer { Content = PluginPermsView.List(perms), MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            560);
    }

    /// <summary>从市场更新:现取一次索引条目(离线/索引里没有就如实报错),再走与市场页相同的安装链。</summary>
    private async Task UpdateFromMarketAsync(PluginInfo p)
    {
        PyDict? entry = null;
        try
        {
            entry = await Task.Run(() => new PluginMarket().Find(p.Id, false));
        }
        catch (Exception ex)
        {
            ToastHost.Show("更新失败", ex.Message, ToastKind.Error);
            return;
        }
        if (entry == null)
        {
            ToastHost.Show("市场里没有这个插件", p.Id, ToastKind.Warning);
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(PyJson.Dumps(entry, ensureAscii: false));
            await InstallFromMarketAsync(MarketItem.From(doc.RootElement.Clone()));
        }
        catch (Exception ex)
        {
            ToastHost.Show("更新失败", ex.Message, ToastKind.Error);
        }
    }

}
