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
using TipCL.App.Common.Plugins;
using TipCL.Core;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

/// <summary>
/// 顶部页签「插件」：已安装 / 市场 分段；开发者、插件设置（市场源等）收在右上 ⋯ 菜单。
/// 插件设置是本页里的子视图（Windows 版同构：不是对话框）。
/// </summary>
public sealed partial class PluginsPage : PageBase
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
    /// <summary>「有更新」徽标只在读市场索引时顺带取一次(避免每次进已安装分段都联网)。</summary>
    private bool _badgeFetching;
    private readonly Dictionary<string, PyDict> _entries = new Dictionary<string, PyDict>(StringComparer.Ordinal);
    private TextBlock _marketInfo = null!;
    private string _tag = "";
    private Control _marketView = null!;

    private StackPanel _providerHost = new StackPanel { Spacing = 8 };
    private readonly ComboBox _logPick = new ComboBox { Width = 240 };
    private readonly TextBox _logBox = U.LogBox();
    private readonly List<string> _logIds = new List<string>();
    private Control _devView = null!;

    private readonly PluginDirs _dirs;
    private readonly PluginStateStore _store;

    public PluginsPage() : base("plugins", "插件")
    {
        _dirs = PluginRuntime.Host.Dirs;
        _store = PluginRuntime.Host.StateStore;
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

    // ————— 拖放安装(M4-H P7:统一入口 Common/FileDrop) —————

    private void HookDrop()
    {
        // 插件页的偏好是 plugin:认不出的 zip/文件夹按插件包处理(与参考版 FileDrop.DropContext.PreferKind 同义)
        FileDrop.Attach(this, () => new FileDrop.DropContext { PreferKind = FileKinds.Plugin });
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
                _body.Content = new ScrollViewer { Content = U.V(10, RuntimeNotice(), _installedHost).Mk(0, 4, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
    /// <summary>
    /// 已安装分段的固定说明:Linux 版没有插件运行时,插件文件能装能卸但不会被执行。
    /// 放在最上面而不是埋在「开发者」里 —— 用户切到这一页就该知道插件不会跑(不做假按钮的同一口径)。
    /// </summary>
    private static Control RuntimeNotice()
    {
        var note = U.V(4,
            U.Section("Linux 版没有插件运行时"),
            U.Small("插件文件可以在这里安装、更新、卸载、启停(落盘)与查看日志,但不会有插件代码在本进程里运行:"
                    + "Node 插件缺 Node 宿主,.NET 插件的参考契约基于 WPF / .NET Framework,都加载不了。"
                    + "插件给出的网页界面可以按「在浏览器打开」访问。详见「⋯ → 开发者」。").Wrap());
        var card = U.Card(note, 12);
        card.Margin = new Thickness(15, 8, 15, 4);
        return card;
    }

}
