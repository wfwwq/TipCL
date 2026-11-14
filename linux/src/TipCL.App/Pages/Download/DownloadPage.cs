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
using TipCL.Core;
using TipCL.App.Pages.Install;
using TipCL.App.Shell;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui; // 命名空间 TipCL.App.Pages.* 下 `Ui` 会被解析成命名空间而非类（B0/B2 同款约定）

namespace TipCL.App.Pages.Download;

// 下载页 v2（移植自 Windows 版 Pages/DownloadPage.cs，download2 设计）：
// 灰色轨道分段（原版 / Mod / 整合包 / 数据包 / 资源包 / 光影 / 世界）+ 右上「任务 N」。
// 原版分段 = 筛选行 + 版本列表卡；「安装」整页切 InstallPage 子视图。
// 其余分段为目录页占位（CatalogPage 属 R3/目录工作包，到位后由本页懒挂）。
public sealed class DownloadPage : PageBase
{
    /// <summary>远程版本列表拉取超时（对齐 Windows 版 FetchTimeoutMs=9000）。</summary>
    public static int FetchTimeoutMs = 9000;

    private static readonly string[] Segs = { "原版", "Mod", "整合包", "数据包", "资源包", "光影", "世界" };

    /// <summary>分段 → 目录类别(下标 0 = 原版,无目录页;照 Windows 版 DownloadPage.Kinds)。</summary>
    private static readonly CatalogKind?[] Kinds =
    {
        null, CatalogKind.Mod, CatalogKind.Modpack, CatalogKind.Datapack,
        CatalogKind.ResourcePack, CatalogKind.Shader, CatalogKind.World,
    };

    private static readonly string[] FilterNames = { "正式版", "快照", "远古" };

    private readonly ToggleButton[] _segBtns = new ToggleButton[7];

    private readonly ContentControl _host = new();

    /// <summary>六个目录分段的页面(懒建,常驻缓存;照 Windows 版 _subs)。</summary>
    private readonly CatalogPage?[] _subs = new CatalogPage?[7];

    private readonly Grid _shell;

    private readonly TextBox _search;

    private readonly Button[] _filters = new Button[3];

    private readonly TextBlock[] _filterTexts = new TextBlock[3];

    private readonly ListBox _list;

    private readonly TextBlock _taskN;

    private readonly StackPanel _vanillaView;

    private InstallPage? _install;

    private List<VersionRow> _all = new();

    private int _seg;

    private int _filter;

    private bool _loaded;

    public DownloadPage() : base("download", "下载")
    {
        _taskN = new TextBlock
        {
            Text = "0",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }.Dyn(TextBlock.ForegroundProperty, "B.OnAccentStrong");
        _search = UiClass.Input("搜索版本");

        // ── 顶行：分段轨道 + 任务按钮 ──
        var rail = UiClass.H(0);
        for (int i = 0; i < 7; i++)
        {
            int k = i;
            var t = new ToggleButton { Theme = UiClass.Theme("Seg.Item"), Content = UiClass.Txt(Segs[i], 13) };
            t.Click += (_, _) => ShowSeg(k);
            _segBtns[i] = t;
            rail.Children.Add(t);
        }
        var segRail = new Border { Theme = UiClass.Theme("Seg.Rail"), Child = rail, HorizontalAlignment = HorizontalAlignment.Left };
        var badge = UiClass.Pane(_taskN, "B.AccentStrong", 8, 0);
        badge.Padding = new Thickness(6, 0, 6, 1);
        var task = UiClass.Btn("", BtnKind.Normal, (_, _) => (this.GetVisualRoot() as MainWindow)?.Navigate("tasks"));
        task.Content = UiClass.H(6, IconLib.Make("Download", "B.Ink", 13), UiClass.Txt("任务", 13, bold: true), badge);
        task.BorderThickness = new Thickness(0);
        var top = UiClass.G(null, "*,Auto");
        top.Margin = new Thickness(22, 16, 22, 12);
        top.Add(segRail, 0);
        top.Add(task, 0, 1);
        task.VerticalAlignment = VerticalAlignment.Center;

        // ── 筛选行 + 列表卡（原版分段；BlockHost 布局自定义属 B4，先按固定排布）──
        _search.TextChanged += (_, _) => Apply();
        var bar = UiClass.G(null, "*,Auto");
        var fs = UiClass.H(4);
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var txt = UiClass.Txt(FilterNames[i], 13);
            _filterTexts[i] = txt;
            _filters[i] = new Button { Theme = UiClass.Theme("Btn.Ghost"), Content = txt };
            _filters[i].Click += (_, _) => { _filter = k; Apply(); };
            fs.Children.Add(_filters[i]);
        }
        bar.Add(_search, 0);
        bar.Add(fs, 0, 1);
        fs.Margin = new Thickness(8, 0, 0, 0);
        // 旧筛选行下间距 12 = 区块间距 10 + 行自留下边距 2（Windows 版注释原话）
        bar.Margin = new Thickness(22, 0, 22, 2);
        _list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var card = UiClass.Card(_list, 0);
        card.Padding = new Thickness(0, 4, 0, 4);
        card.Margin = new Thickness(22, 0, 22, 16);
        _vanillaView = UiClass.V(0, bar, card);

        _shell = UiClass.G("Auto,*");
        _shell.Add(top, 0);
        _shell.Add(_host, 1);
        Body = _shell;

        TaskStore.Added += _ => Dispatcher.UIThread.Post(SyncTasks);
        TaskStore.Updated += r => { if (r.Finished) Dispatcher.UIThread.Post(SyncTasks); };

        ShowSeg(0);
    }

    private void SyncTasks() => _taskN.Text = TaskStore.RunningCount.ToString();

    /// <summary>切到某个分段(0 = 原版,1..6 = 六个目录分段);冒烟脚本按 SMOKE_SEG 调它。</summary>
    public void SelectSegment(int k) => ShowSeg(k);

    // ───────────────────────────── 分段 ─────────────────────────────

    private void ShowSeg(int k)
    {
        // 照 Windows:离开分段先通知旧分段(停掉它的防抖与在途搜索)
        if (_seg >= 1 && _seg < _subs.Length) _subs[_seg]?.ShellDetach();
        _seg = k;
        for (int i = 0; i < 7; i++)
            _segBtns[i].IsChecked = i == k;
        if (k == 0)
        {
            _host.Content = _vanillaView;
            return;
        }
        // 目录页(Mod / 整合包 / 数据包 / 资源包 / 光影 / 世界):懒建并常驻,再挂进内容区
        var sub = _subs[k] ??= new CatalogPage(Kinds[k]!);
        _host.Content = sub;
        sub.ShellAttach(null);
    }

    // ───────────────────────────── 版本列表 ─────────────────────────────

    // 行控件直接构建（对齐 Windows 版 RowUi：首行无顶分隔线、行 hover 换按钮样式），ListBox 容器虚拟化保留。
    private Control RowUi(VersionRow r, bool isFirst)
    {
        var b = new Button { Theme = UiClass.Theme("Btn.Soft"), Content = UiClass.H(6, IconLib.Make("Download", "B.Ink", 13), UiClass.Txt("安装", 13)) };
        b.Click += (_, _) => OpenInstall(r);

        var g = UiClass.G(null, "130,*,110,Auto");
        var ver = UiClass.Txt(r.Version, 13.5, bold: true).Dyn(TextBlock.ForegroundProperty, "B.Ink");
        ver.VerticalAlignment = VerticalAlignment.Center;
        ver.TextTrimming = TextTrimming.CharacterEllipsis;
        g.Add(ver, 0);
        bool hot = r.Type == "snapshot" || (_all.Count > 0 && r == _all.FirstOrDefault(x => x.Type == "release"));
        g.Add(hot ? UiClass.Tag(r.TypeLabel, "B.AccentText", "B.AccentSoft") : UiClass.Tag(r.TypeLabel), 0, 1);
        var date = UiClass.Txt(r.Date, 12.5).Dyn(TextBlock.ForegroundProperty, "B.InkFaint");
        date.VerticalAlignment = VerticalAlignment.Center;
        g.Add(date, 0, 2);
        b.VerticalAlignment = VerticalAlignment.Center;
        g.Add(b, 0, 3);

        var row = new Border
        {
            Height = 46,
            Padding = new Thickness(18, 0, 18, 0),
            BorderThickness = new Thickness(0, isFirst ? 0 : 1, 0, 0),
            Background = Brushes.Transparent,
            Child = g,
        }.Dyn(Border.BorderBrushProperty, "B.RowLine");
        row.PointerEntered += (_, _) =>
        {
            row.Background = UiClass.Brush("B.RowHover");
            b.Theme = UiClass.Theme("Btn.Primary");
        };
        row.PointerExited += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            b.Theme = UiClass.Theme("Btn.Soft");
        };
        return row;
    }

    private void Apply()
    {
        for (int i = 0; i < 3; i++)
        {
            _filters[i].Theme = UiClass.Theme(i == _filter ? "Btn.Soft" : "Btn.Ghost");
            _filterTexts[i].FontWeight = i == _filter ? FontWeight.SemiBold : FontWeight.Normal;
        }
        string q = _search.Text?.Trim() ?? "";
        IEnumerable<VersionRow> rows = _filter switch
        {
            1 => _all.Where(r => r.Type == "snapshot"),
            2 => _all.Where(r => r.Type.StartsWith("old_", StringComparison.Ordinal)),
            _ => _all.Where(r => r.Type == "release"),
        };
        if (q.Length > 0)
            rows = rows.Where(r => r.Version.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        var list = rows.ToList();
        var controls = new List<Control>(list.Count);
        for (int i = 0; i < list.Count; i++)
            controls.Add(RowUi(list[i], isFirst: i == 0));
        _list.ItemsSource = controls;
    }

    public void OpenInstall(VersionRow row)
    {
        _install = new InstallPage(row.Version, row.TypeLabel, () =>
        {
            _install = null;
            Content = _shell;
        });
        Content = _install;
    }

    // ───────────────────────────── 数据 ─────────────────────────────

    protected override async void OnCreate()
    {
        SyncTasks();
        await LoadVersionsAsync(force: false);
    }

    protected override void OnActivate()
    {
        SyncTasks();
        if (!_loaded) _ = LoadVersionsAsync(force: false);
        // 回到下载页时把激活通知转给当前目录分段(照 Windows OnShown)
        if (_seg >= 1 && _seg < _subs.Length) _subs[_seg]?.ShellAttach(null);
    }

    protected override void OnDeactivate()
    {
        if (_seg >= 1 && _seg < _subs.Length) _subs[_seg]?.ShellDetach();
    }

    protected override void OnEvent(ApiEvent ev)
    {
        foreach (var sub in _subs) sub?.ShellEvent(ev);
    }

    /// <summary>两段式加载：先本地缓存清单立即渲染，再远程刷新覆盖（超时 9s）。</summary>
    private async Task LoadVersionsAsync(bool force)
    {
        try
        {
            var local = await Api.TryCallAsync<object?>("get_version_list", null);
            var rows = ParseVersionRows(local);
            if (rows.Count > 0 && !force)
            {
                _all = rows;
                _loaded = true;
                Apply();
            }
        }
        catch (Exception) { /* 本地缓存坏档按空处理 */ }
        if (force) return;
        try
        {
            // core 侧每个镜像自带连接/读超时(4s/15s)并会换镜像重试,这里只做 UI 兜底:
            // 超过 FetchTimeoutMs 先提示"仍在后台刷新",不丢弃结果——完成后再补渲染。
            var fetch = Api.CallAsync<object?>("fetch_version_list", null);
            var first = await Task.WhenAny(fetch, Task.Delay(FetchTimeoutMs));
            if (first != fetch)
            {
                if (_all.Count == 0)
                    Toast("版本清单刷新较慢", "仍在后台拉取,完成后会自动更新", ToastKind.Info);
                try
                {
                    var late = ParseVersionRows(await fetch);
                    if (late.Count > 0)
                    {
                        _all = late;
                        _loaded = true;
                        if (_install == null) Apply();
                    }
                }
                catch (Exception ex)
                {
                    Common.AppLog.Warn("版本清单后台刷新失败: " + ex.Message);
                }
                return;
            }
            var rows = ParseVersionRows(await fetch);
            if (rows.Count > 0)
            {
                _all = rows;
                _loaded = true;
                if (_install == null) Apply();
            }
        }
        catch (Exception ex)
        {
            Common.AppLog.Warn("版本清单刷新失败: " + ex.Message);
            if (_all.Count == 0)
                Toast("版本清单刷新失败", ex.Message, ToastKind.Warning);
        }
    }

    /// <summary>
    /// 解析 get/fetch_version_list 返回（PyDict：id → {type, releaseTime/time}），
    /// 归一为 Windows 版 _version_rows 语义的行（type 归并 release/snapshot/old_*，日期取前 10 位，按日期倒序）。
    /// </summary>
    internal static List<VersionRow> ParseVersionRows(object? data)
    {
        var result = new List<VersionRow>();
        IEnumerable<KeyValuePair<string, object>> items;
        if (data is PyDict d)
            items = d.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value ?? new PyDict()));
        else if (data is System.Collections.IDictionary idict)
            items = idict.Keys.Cast<object>().Select(k => new KeyValuePair<string, object>(k?.ToString() ?? "", idict[k!] ?? new object()));
        else
            return result;
        foreach (var kv in items)
        {
            string id = kv.Key;
            if (string.IsNullOrEmpty(id)) continue;
            string type = AsStr(GetDict(kv.Value, "type")) switch
            {
                "release" => "release",
                "old_alpha" => "old_alpha",
                "old_beta" => "old_beta",
                _ => "snapshot",
            };
            string raw = AsStr(GetDict(kv.Value, "releaseTime"));
            if (raw.Length == 0) raw = AsStr(GetDict(kv.Value, "time"));
            result.Add(new VersionRow
            {
                Version = id,
                Type = type,
                Date = raw.Length >= 10 ? raw[..10] : raw,
            });
        }
        return result.OrderByDescending(r => r.Date, StringComparer.Ordinal).ToList();

        static object? GetDict(object o, string key) => (o as PyDict)?.Get(key);
        static string AsStr(object? o) => o?.ToString() ?? "";
    }

    // ───────────────────────────── 模型 ─────────────────────────────

    public sealed class VersionRow
    {
        public string Version { get; init; } = "";
        public string Type { get; init; } = "";
        public string Date { get; init; } = "";
        public string TypeLabel => Type switch
        {
            "release" => "正式版",
            "snapshot" => "快照",
            "old_alpha" => "远古 Alpha",
            "old_beta" => "远古 Beta",
            _ => Type,
        };
    }
}
