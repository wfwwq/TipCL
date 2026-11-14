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
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Pages.Download;
using TipCL.App.Shell;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui; // 命名空间 TipCL.App.Pages.* 下 `Ui` 会被解析成命名空间而非类（B0/B2 同款约定）
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Install;

// 安装页（移植自 Windows 版 Pages/InstallPage.cs compact 分支，install2 设计）：
// 由下载页原版列表「安装」进入，作为下载页的子视图（不进导航注册）。
// 业务：list_loader_versions 探测（并发）/ create_instance / install_game（IApi StartTask 队列）/
// Fabric API（Modrinth fabric-api，主任务成功后追加安装任务）。
public sealed class InstallPage : UserControl
{
    private static readonly string[] Names = { "不安装", "Fabric", "Forge", "NeoForge", "Quilt" };

    private static readonly string[] LoaderKeys = { "fabric", "forge", "neoforge", "quilt", "optifine", "liteloader" };

    /// <summary>粗略体积估算（MB，不含首次下载的共享资源文件）。</summary>
    private static readonly int[] Mb = { 30, 1, 15, 15, 2 };

    private readonly string _ver;

    private readonly TextBox _name;

    private readonly TextBlock _nameHint = UiClass.Small("");

    private readonly Border[] _cards = new Border[5];

    private readonly Border?[] _chks = new Border[5];

    private readonly TextBlock[] _subs = new TextBlock[5];

    private readonly List<LoaderRow>?[] _vers = new List<LoaderRow>?[5];

    private readonly ComboBox _loaderVer = UiClass.Combo(null, null, 220);

    private readonly TextBlock _verLabel = UiClass.Txt("", 13.5);

    private readonly CheckBox _api = UiClass.Switch();

    private readonly CheckBox _of = UiClass.Switch();

    private readonly CheckBox _lite = UiClass.Switch();

    private readonly TextBlock _ofWhy = UiClass.Small("");

    private readonly TextBlock _ofName = UiClass.Txt("OptiFine", 13.5);

    private readonly Control _verRow;

    private readonly Control _apiRow;

    private readonly Control _liteRow;

    private readonly TextBlock _sum = UiClass.Txt("", 13, bold: true);

    private readonly TextBlock _size = UiClass.Muted("");

    private readonly Button _go;

    private List<string> _instances = new();

    private int _sel;

    private bool _hasOf;

    private bool _autoName = true;

    private string _lastAutoName = "";

    public InstallPage(string version, string typeLabel, Action back)
    {
        _ver = version;
        _name = UiClass.Input("", version);
        _name.Height = 38;
        _name.Padding = new Thickness(10, 3, 10, 3);
        _name.FontWeight = FontWeight.SemiBold;
        _name.TextChanged += (_, _) =>
        {
            if (_name.Text != _lastAutoName)
                _autoName = false;
            SyncName();
        };
        var bk = UiClass.Btn("", BtnKind.Normal, (_, _) => back());
        bk.Content = IconLib.Make("Back", "B.Ink", 14);
        bk.Width = bk.Height = 32;
        bk.Padding = new Thickness(0);
        var title = UiClass.Txt($"安装 Minecraft {version}", 20, bold: true);
        title.FontWeight = FontWeight.ExtraBold;
        var head = UiClass.H(10, bk, title, UiClass.Tag(typeLabel, "B.Ok", "B.AccentSoft"));
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Margin = new Thickness(22, 14, 22, 10);

        // ── 加载器 5 卡 ──
        var lds = UiClass.G(null, "*,*,*,*,*");
        for (int i = 0; i < 5; i++)
        {
            int k = i;
            _subs[i] = UiClass.Small(i == 0 ? "纯原版" : "…");
            var chk = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 9, 10, 0),
                Child = IconLib.Make("Check", "B.OnAccent", 9),
            }.Dyn(Border.BackgroundProperty, "B.Accent");
            var name = UiClass.Txt(Names[i], 14, bold: true);
            var tile = LoaderIcons.Tile(i == 0 ? "vanilla" : Names[i], Names[i]);
            var g = new Grid();
            var left = UiClass.H(10, tile, UiClass.V(1, name, _subs[i]));
            left.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(left);
            g.Children.Add(chk);
            var card = UiClass.Card(g, 11);
            card.BorderThickness = new Thickness(1.5);
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.Margin = new Thickness(i == 0 ? 0 : 5, 0, i == 4 ? 0 : 5, 0);
            card.PointerReleased += (_, e) =>
            {
                if (card.IsEnabled)
                    Select(k);
                e.Handled = true;
            };
            _cards[i] = card;
            _chks[i] = chk;
            lds.Add(card, 0, i);
        }

        // ── 开关卡 ──
        _loaderVer.SelectionChanged += (_, _) => Sync();
        _api.IsCheckedChanged += (_, _) => Sync();
        _of.IsCheckedChanged += (_, _) => Sync();
        _lite.IsCheckedChanged += (_, _) => Sync();
        _verRow = Row(_verLabel, null, _loaderVer);
        _apiRow = SepRow(Row(UiClass.Txt("Fabric API", 13.5), "大多数 Fabric 模组都需要", _api));
        var ofGrid = Row(_ofName, null, _of);
        var why = _ofWhy;
        why.VerticalAlignment = VerticalAlignment.Center;
        why.Margin = new Thickness(8, 0, 0, 0);
        ((Grid)ofGrid).Add(why, 0, 1);
        _liteRow = SepRow(Row(UiClass.Txt("LiteLoader", 13.5), "仅老版本可用", _lite));
        _liteRow.IsVisible = false;
        var vbox = UiClass.Card(UiClass.V(0, _verRow, _apiRow, SepRow(ofGrid), _liteRow), 0);
        vbox.Padding = new Thickness(0, 4, 0, 4);
        vbox.Margin = new Thickness(0, 10, 0, 0);

        // ── 实例名称 ──
        var nm = new Grid();
        nm.Children.Add(_name);
        var hint = _nameHint;
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(0, 0, 14, 0);
        hint.IsHitTestVisible = false;
        nm.Children.Add(hint);

        var body = UiClass.V(0, Label("实例名称", 8), nm, Label("模组加载器", 14), lds, vbox);
        body.Margin = new Thickness(22, 0, 22, 16);

        // ── 底部固定栏 ──
        _go = UiClass.Btn("", BtnKind.Primary, (_, _) => _ = InstallAsync());
        _go.Content = UiClass.H(8, IconLib.Make("Download", "B.OnAccent", 14), UiClass.Txt("开始安装", 13, bold: true).Dyn(TextBlock.ForegroundProperty, "B.OnAccent"));
        _go.Height = 38;
        _go.Padding = new Thickness(20, 0, 20, 0);
        ToolTip.SetTip(_size, "估算值，不含首次下载的共享资源文件");
        var foot = UiClass.G(null, "Auto,*,Auto");
        foot.Add(UiClass.H(6, UiClass.Muted("将安装"), _sum, _size), 0);
        ((StackPanel)foot.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        foot.Add(_go, 0, 2);
        var footBar = new Border
        {
            Child = foot,
            Padding = new Thickness(22, 12, 22, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
        }.Dyn(Border.BackgroundProperty, "B.GlassStrong")
         .Dyn(Border.BorderBrushProperty, "B.RowLine");

        var root = UiClass.G("Auto,*,Auto");
        root.Add(head, 0);
        root.Add(new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        }, 1);
        root.Add(footBar, 2);
        Content = root;
        Select(0);
        _ = LoadAsync();
    }

    // ───────────────────────────── 构建小工具 ─────────────────────────────

    private static TextBlock Label(string text, double top)
    {
        var t = UiClass.Txt(text, 12.5, bold: true, "B.InkMuted");
        t.Margin = new Thickness(2, top, 0, 7);
        return t;
    }

    private static Grid Row(TextBlock name, string? hint, Control right)
    {
        var g = UiClass.G(null, "Auto,*,Auto");
        g.Height = 42;
        g.Margin = new Thickness(16, 0, 16, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        g.Add(name, 0);
        if (hint != null)
        {
            var h = UiClass.Small(hint);
            h.VerticalAlignment = VerticalAlignment.Center;
            h.Margin = new Thickness(8, 0, 0, 0);
            g.Add(h, 0, 1);
        }
        right.VerticalAlignment = VerticalAlignment.Center;
        g.Add(right, 0, 2);
        return g;
    }

    /// <summary>行上方 1px 分隔线（install2 .vr，随行一起折叠）。</summary>
    private static Control SepRow(Control row)
    {
        var b = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
        b.Dyn(Border.BorderBrushProperty, "B.RowLine");
        return b;
    }

    // ───────────────────────────── 数据加载 ─────────────────────────────

    private async Task LoadAsync()
    {
        var infos = await InProcessApi.Instance.TryCallAsync<object?>("get_instances", null);
        _instances = ParseNames(infos);
        SyncName();
        // 加载器版本探测：6 请求并发（fabric/forge/neoforge/quilt/optifine/liteloader × 当前 MC 版本）
        var tasks = LoaderKeys.Select(k => InProcessApi.Instance.TryCallAsync<object?>("list_loader_versions", null,
            new { mc_version = _ver, loader = k })).ToArray();
        var res = await Task.WhenAll(tasks);
        for (int i = 1; i < 5; i++)
        {
            var l = ParseLoaderRows(res[i - 1]);
            _vers[i] = l;
            _subs[i].Text = l.Count == 0 ? "不支持此版本" : Best(l).Id;
            _cards[i].IsEnabled = l.Count > 0;
            _cards[i].Opacity = l.Count > 0 ? 1.0 : 0.45;
        }
        _hasOf = ParseLoaderRows(res[4]).Count > 0;
        _liteRow.IsVisible = ParseLoaderRows(res[5]).Count > 0;
        Select(_sel);
    }

    private static List<string> ParseNames(object? data)
    {
        var names = new List<string>();
        if (data is System.Collections.IEnumerable en && data is not string)
        {
            foreach (var item in en)
            {
                if (item is PyDict d && Py.S(d.Get("name")) is { Length: > 0 } n)
                    names.Add(n);
            }
        }
        return names;
    }

    private static List<LoaderRow> ParseLoaderRows(object? data)
    {
        var rows = new List<LoaderRow>();
        if (data is System.Collections.IEnumerable en && data is not string)
        {
            foreach (var item in en)
            {
                if (item is PyDict d)
                {
                    string id = Py.S(d.Get("id"));
                    if (id.Length == 0) continue;
                    rows.Add(new LoaderRow(id, Py.Truthy(d.Get("stable"))));
                }
            }
        }
        return rows;
    }

    private static LoaderRow Best(List<LoaderRow> l) => l.FirstOrDefault(v => v.Stable) ?? l[0];

    // ───────────────────────────── 选择联动 ─────────────────────────────

    private void Select(int k)
    {
        _sel = k;
        for (int i = 0; i < 5; i++)
        {
            bool on = i == k;
            _cards[i].Dyn(Border.BorderBrushProperty, on ? "B.Accent" : "B.CardBorder");
            _cards[i].Dyn(Border.BackgroundProperty, on ? "B.AccentTint" : "B.Card");
            if (_chks[i] != null)
                _chks[i]!.IsVisible = on;
        }
        var l = _vers[k];
        _verRow.IsVisible = k != 0;
        _verLabel.Text = $"{Names[k]} 版本";
        if (k > 0)
        {
            var items = new List<string>();
            if (l == null || l.Count == 0)
            {
                items.Add("最新");
            }
            else
            {
                var best = Best(l);
                items.AddRange(l.Select(v => v == best ? $"{v.Id} · 推荐" : v.Id));
            }
            _loaderVer.Items.Clear();
            foreach (var it in items) _loaderVer.Items.Add(it);
            _loaderVer.SelectedIndex = 0;
            if (l != null && l.Count > 0)
                _loaderVer.SelectedIndex = l.IndexOf(Best(l));
        }
        bool fab = k == 1 || k == 4;
        _apiRow.IsVisible = fab;
        _api.IsChecked = fab;
        bool ofOk = _hasOf && (k == 0 || k == 2);
        _of.IsEnabled = ofOk;
        if (!ofOk)
            _of.IsChecked = false;
        _ofName.Opacity = ofOk ? 1.0 : 0.5;
        _ofWhy.Text = ofOk ? "" : (!_hasOf ? "此版本没有可用的 OptiFine" : $"与 {Names[k]} 不兼容，可改装 Sodium + Iris");
        if (_autoName)
        {
            _lastAutoName = k == 0 ? _ver : $"{Names[k]} {_ver}";
            _name.Text = _lastAutoName;
        }
        Sync();
    }

    private string LoaderVersion()
    {
        var l = _vers[_sel];
        int i = _loaderVer.SelectedIndex;
        if (_sel == 0 || l == null || i < 0 || i >= l.Count)
            return "";
        return l[i].Id;
    }

    // ───────────────────────────── 汇总 ─────────────────────────────

    private void Sync()
    {
        var parts = new List<string> { _ver };
        int mb = Mb[0];
        if (_sel > 0)
        {
            string v = LoaderVersion();
            parts.Add(Names[_sel] + (v.Length > 0 ? $" {v}" : ""));
            mb += Mb[_sel];
        }
        if (_sel > 0 && (_sel == 1 || _sel == 4) && _api.IsChecked == true)
        {
            parts.Add("Fabric API");
            mb += 2;
        }
        if (_of.IsChecked == true)
        {
            parts.Add("OptiFine");
            mb += 7;
        }
        if (_lite.IsChecked == true && _liteRow.IsVisible)
        {
            parts.Add("LiteLoader");
            mb += 1;
        }
        _sum.Text = string.Join(" + ", parts);
        _size.Text = $"· 约 {mb} MB";
    }

    private void SyncName()
    {
        string n = _name.Text?.Trim() ?? "";
        _nameHint.Text = n.Length == 0 ? "" : _instances.Contains(n) ? "装入已有实例" : "将新建实例";
    }

    // ───────────────────────────── 安装流 ─────────────────────────────

    private async Task InstallAsync()
    {
        string name = _name.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            ToastHost.Show("请填写实例名称", "", ToastKind.Warning);
            return;
        }
_go.IsEnabled = false;
        // 任务体跑在线程池:控件状态必须先在 UI 线程取出再进任务,
        // 否则 _of.IsChecked / IsVisible 会触发 Avalonia VerifyAccess("Call from invalid thread")。
        bool optifine = _of.IsChecked == true;
        bool liteloader = _lite.IsChecked == true && _liteRow.IsVisible;
        bool installFabricApi = _api.IsChecked == true;
        int sel = _sel;
        string ver = _ver;
        string loader = sel == 0 ? "" : Names[sel];
        string? loaderVersion = LoaderVersion();
        try
        {
            if (!_instances.Contains(name))
                await InProcessApi.Instance.CallAsync<object?>("create_instance", new { name });

            string title = ComposeTaskTitle(name);
            var mainRow = await InProcessApi.Instance.StartTaskAsync(title, async (progress, ct) =>
            {
                void Pump(ApiEvent ev)
                {
                    // 只搬运「后端原始事件」(不带 taskId);progress.Report 自己会再发一条带
                    // taskId 的事件,若不过滤会 Report→BridgeEvent→Pump→Report 无限递归(栈溢出)。
                    if (ev.TaskId.Length > 0) return;
                    if (ev.Event == "progress")
                        progress.Report(ev.Message, ev.Current, ev.Total);
                    else if (ev.Event == "log")
                        progress.Log(ev.Text);
                }
                var api = InProcessApi.Instance;
                api.BridgeEvent += Pump;
                try
                {
                    var extra = PyDict.Of(
                        "optifine", optifine,
                        "liteloader", liteloader);
                    await api.CallAsync<object?>("install_game", new
                    {
                        version = ver,
                        loader,
                        loader_version = loaderVersion,
                        instance = name,
                        extra,
                    }, ct);
                    // Fabric API:主任务成功后追加(对齐 Windows 版 install_mod 链)
                    if ((sel == 1 || sel == 4) && installFabricApi)
                    {
                        await api.StartTaskAsync("安装模组 Fabric API", (p2, ct2) => Task.Run(() =>
                        {
                            var dm = new DownloadManager(2, (msg, cur, total) => p2.Report(msg, cur, total));
                            Mods.InstallModrinthMod(dm, "fabric-api", new Instance(name), ver, loader: loader == "Quilt" ? "quilt" : "fabric");
                        }, ct2));
                    }
                }
                finally
                {
                    api.BridgeEvent -= Pump;
                }
            });

            // 飞行动画 FlyToTasks 在 Linux 降级为直接跳转（R2 §2.3）
            ToastHost.Show("已加入队列", _sum.Text ?? "", ToastKind.Success);
            if (this.GetVisualRoot() is MainWindow win)
                win.Navigate("tasks");
        }
        catch (Exception ex)
        {
            ToastHost.Show("安装失败", ex.Message, ToastKind.Warning);
        }
        finally
        {
            _go.IsEnabled = true;
        }
    }

    /// <summary>任务标题（对齐 Windows 版 install_game 后端的拼接文案）。</summary>
    private string ComposeTaskTitle(string name)
    {
        var list = new List<string> { _ver };
        if (_sel > 0) list.Add(Names[_sel]);
        if (_of.IsChecked == true) list.Add("OptiFine");
        if (_lite.IsChecked == true && _liteRow.IsVisible) list.Add("LiteLoader");
        return "安装游戏 " + string.Join(" + ", list);
    }

    private sealed record LoaderRow(string Id, bool Stable);
}
