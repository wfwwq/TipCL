using System;
using Avalonia;
using Avalonia.Styling;
using TipCL.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Ui;
// 命名空间 TipCL.App.Pages.* 下 `Ui` 会被解析成命名空间而非类，用别名指到类（B0 同款约定）
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Library;

// 实例详情（移植自 Windows 版 src/TipCL/Pages/InstanceDetailPage.cs）：
// 返回 / 标题 / 启动；分段 模组·存档·资源包·光影·设置。
// 由实例库「实例详情」在页内切入（不注册导航）。
// 差异说明（按 R1 拆包处理）：ModsDialog / SavesDialog / VersionManagePage 未移植，
// 「更多管理」入口暂缺，数据列表即精简版管理；「设置」分段为只读版本列表 + 打开文件夹。
public sealed class InstanceDetailPage : PageBase
{
    private static readonly string[] Segs = { "模组", "存档", "资源包", "光影", "设置" };

    private readonly string _inst;
    private readonly Action _back;

    private readonly ToggleButton[] _segBtns = new ToggleButton[5];
    private readonly ContentControl _host = new();
    private readonly StackPanel _rows = UiClass.V(0);

    // _rows 的**唯一**容器:整个页面生命周期只有一个 ScrollViewer 拥有它,且只赋值一次。
    // 原因(Avalonia 硬约束):一个控件只能有一个父级。以前 ShowSeg 每次 new 一个 ScrollViewer
    // 并把同一个 _rows 塞进去 —— 旧的 ScrollViewer 被换下时它的 ScrollContentPresenter 仍然
    // 持有 _rows,下一次布局就会抛「already has a visual parent」。
    private readonly ScrollViewer _rowsHost = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Margin = new Thickness(22, 0, 22, 0),
    };
    private readonly TextBlock _note = UiClass.Txt("", 12.5, fg: "B.InkMuted");
    private readonly ComboBox _verBox = new() { MinWidth = 200 };
    private readonly TextBlock _meta = UiClass.Txt("", 12, fg: "B.InkMuted");

    private readonly List<string> _vers = new();
    private int _seg;
    private bool _loadingVersions;
    private string _focusVer = "";

    public InstanceDetailPage(LibraryPage owner, InstInfo info, Action back, string? focusVer)
        : base("instance-detail", info.Name)
    {
        _inst = info.Name;
        _back = back;
        _focusVer = focusVer ?? "";

        var backBtn = UiClass.IconBtn("ChevronLeft", "返回", (_, _) => _back(), 16);
        var title = UiClass.Txt(_inst, 21, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        _meta.Text = $"{info.Versions} 个版本";
        var launch = UiClass.Btn("启动", BtnKind.Primary, (_, _) => Run(LaunchNowAsync));

        var head = UiClass.G(null, "Auto,*,Auto");
        head.Margin = new Thickness(22, 16, 22, 12);
        head.Add(backBtn, 0, 0).Children.OfType<Control>().Last().VerticalAlignment = VerticalAlignment.Center;
        backBtn.Margin = new Thickness(0, 0, 8, 0);
        var titleCol = UiClass.V(2, title, _meta);
        titleCol.VerticalAlignment = VerticalAlignment.Center;
        head.Add(titleCol, 0, 1);
        head.Add(launch, 0, 2).Children.OfType<Control>().Last().VerticalAlignment = VerticalAlignment.Center;

        _note.TextWrapping = TextWrapping.Wrap;
        _note.MaxWidth = 420;
        _note.VerticalAlignment = VerticalAlignment.Center;
        _note.IsVisible = false;

        var rail = UiClass.H(0);
        for (int i = 0; i < 5; i++)
        {
            int k = i;
            var t = new ToggleButton { Theme = UiClass.Theme("Seg.Item"), Content = Segs[i] };
            t.Click += (_, _) => ShowSeg(k);
            _segBtns[i] = t;
            rail.Children.Add(t);
        }
        _verBox.SelectionChanged += (_, _) => ApplyVersionSegs();

        var bar = UiClass.G(null, "Auto,*,Auto");
        bar.Margin = new Thickness(22, 0, 22, 12);
        var railBd = new Border { Theme = UiClass.Theme("Seg.Rail"), Child = rail, HorizontalAlignment = HorizontalAlignment.Left };
        bar.Add(railBd, 0, 0);
        bar.Add(_note, 0, 1);
        _verBox.VerticalAlignment = VerticalAlignment.Center;
        _verBox.Margin = new Thickness(12, 0, 0, 0);
        bar.Add(_verBox, 0, 2);

        // _rows 的唯一容器:挂一次就不再动(ShowSeg 只换分段状态与数据,不再重建容器)
        _rowsHost.Content = _rows;
        _host.Content = _rowsHost;

        var shell = UiClass.G("Auto,Auto,*");
        shell.Add(head, 0);
        shell.Add(bar, 1);
        shell.Add(_host, 2);
        Content = shell;
        ShowSeg(0);
        Run(async () =>
        {
            await LoadVersionsAsync();
            ApplyVersionSegs();
        });
    }

    /// <summary>实例信息（由实例库传入，避免二次扫描）。</summary>
    public sealed class InstInfo
    {
        public string Name = "";
        public int Versions;
    }

    // ———————————————— 版本选择 ————————————————

    private async Task LoadVersionsAsync()
    {
        _loadingVersions = true;
        _vers.Clear();
        // 修缺陷 B:本页是**这个实例**的详情,版本列表必须取实例目录(以前不传 instance →
        // 拿到根实例 <TIPCL_HOME>/.minecraft/versions 的版本,与下面的本地扫描、与启动预检都不一致)
        var raw = await Api.TryCallAsync<List<object?>>("get_version_rows", null, new { instance = _inst, include_hidden = false });
        if (raw is { Count: > 0 })
        {
            foreach (var item in raw)
                if (item is PyDict r)
                {
                    string id = Py.S(r.Get("id"));
                    if (id.Length > 0) _vers.Add(id);
                }
        }
        else
        {
            await Task.Run(() =>
            {
                try
                {
                    var inst = new Instance(_inst);
                    foreach (var idObj in inst.InstalledIds())
                    {
                        string id = Py.S(idObj);
                        if (id.Length > 0 && !VersionOps.IsHidden(inst, id)) _vers.Add(id);
                    }
                }
                catch (Exception) { /* 空实例 */ }
            });
        }
        _verBox.Items.Clear();
        foreach (var id in _vers)
            _verBox.Items.Add(IsolationLabelOf(id) is { Length: > 0 } lb ? $"{id} · {lb}" : id);
        int idx = _focusVer.Length > 0 ? _vers.IndexOf(_focusVer) : -1;
        _verBox.SelectedIndex = idx >= 0 ? idx : (_vers.Count > 0 ? 0 : -1);
        _verBox.IsVisible = _vers.Count > 0;
        _loadingVersions = false;
    }

    private string CurrentVersion => (_verBox.SelectedIndex >= 0 && _verBox.SelectedIndex < _vers.Count)
        ? _vers[_verBox.SelectedIndex] : "";

    private string IsolationLabelOf(string ver)
    {
        try
        {
            var settings = VersionSettings.Load(new Instance(_inst), ver);
            if (!VersionSettings.IsIsolated(settings)) return "";
            string iso = Py.S(settings.Get("isolation"));
            return Py.S(VersionSettings.ISOLATION_LABELS.Get(iso, iso));
        }
        catch (Exception) { return ""; }
    }

    // 选中版本隔离关闭（共享/大锅饭）：隐藏前 4 个分段，只留设置并显示灰字说明
    private void ApplyVersionSegs()
    {
        if (_loadingVersions) return;
        string cur = CurrentVersion;
        bool shared = cur.Length > 0 && IsolationLabelOf(cur).Length == 0 && !IsIsolated(cur);
        for (int i = 0; i < 4; i++)
            _segBtns[i].IsVisible = !shared;
        if (shared)
        {
            _note.Text = "该版本为共享（大锅饭）模式，模组/存档使用实例共享目录；开启独立隔离请见版本设置";
            _note.IsVisible = true;
            if (_seg < 4) ShowSeg(4);
        }
        else if (cur.Length > 0)
        {
            _note.Text = $"当前版本「{cur}」" + (IsIsolated(cur) ? "已开启隔离：模组/存档等位于版本独立目录" : "");
            _note.IsVisible = true;
        }
        else
        {
            _note.IsVisible = false;
        }
    }

    private bool IsIsolated(string ver)
    {
        try { return VersionSettings.IsIsolated(VersionSettings.Load(new Instance(_inst), ver)); }
        catch (Exception) { return false; }
    }

    // ———————————————— 分段切换 ————————————————

    private void ShowSeg(int k)
    {
        _seg = k;
        for (int i = 0; i < 5; i++)
            _segBtns[i].IsChecked = i == k;
        // 不在这里重建容器:容器是 _rowsHost(见字段注释),_rows 始终只属于它
        Run(LoadSegAsync, "读取失败");
    }

    private async Task LoadSegAsync()
    {
        int seg = _seg;
        var rows = new List<Control>();
        string ver = CurrentVersion;

        if (seg == 0)
        {
            foreach (var m in await FetchModsAsync())
            {
                string file = m.Filename;
                var sw = UiClass.Switch(m.Enabled);
                sw.IsCheckedChanged += (_, _) =>
                {
                    bool on = sw.IsChecked == true;
                    Run(() => Api.TryCallAsync<object>(on ? "enable_mod" : "disable_mod", null,
                        new { instance = _inst, filename = file }));
                };
                rows.Add(Row(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name, file, sw));
            }
        }
        else if (seg == 1)
        {
            foreach (var s in await FetchSavesAsync())
            {
                var open = UiClass.Btn("打开", BtnKind.Chip, (_, _) =>
                    LibraryPage.OpenInFileManager(s.Path, "打开失败"));
                rows.Add(Row(s.Name, s.SizeText, open));
            }
        }
        else if (seg == 2 || seg == 3)
        {
            string kind = seg == 2 ? "resourcepacks" : "shaderpacks";
            foreach (var f in await FetchMediaAsync(kind))
            {
                var del = UiClass.Btn("删除", BtnKind.Ghost, (_, _) => Run(async () =>
                {
                    if (await Dlg.Confirm("删除", f, "删除", "取消", danger: true))
                    {
                        await Task.Run(() =>
                        {
                            if (Directory.Exists(f)) Directory.Delete(f, true);
                            else if (File.Exists(f)) File.Delete(f);
                        });
                        await LoadSegAsync();
                    }
                }, "删除失败"));
                rows.Add(Row(Path.GetFileName(f.TrimEnd(Path.DirectorySeparatorChar)), null, del));
            }
        }
        else if (seg == 4)
        {
            // 设置分段（精简版版本管理）：每个版本一行 —— 隔离标签 / Java / 打开文件夹
            string java = "";
            try { java = new Instance(_inst).JavaPref(); } catch (Exception) { }
            foreach (var id in _vers)
            {
                var open = UiClass.IconBtn("Open", "打开版本文件夹", (_, _) =>
                {
                    string dir = VersionDir(id);
                    LibraryPage.OpenInFileManager(Directory.Exists(dir) ? dir : InstancePath(), "打开失败");
                }, 16);
                string sub = JoinParts(IsolationLabelOf(id) is { Length: > 0 } lb ? lb : "共享（大锅饭）",
                    string.IsNullOrWhiteSpace(java) ? "" : "Java " + java);
                rows.Add(Row(id, sub, open));
            }
        }

        if (seg != _seg) return; // 已切走
        _rows.Children.Clear();
        if (rows.Count == 0)
        {
            var empty = UiClass.Empty("○", "这里还是空的", "到「下载」页搜索安装");
            empty.Margin = new Thickness(0, 18, 0, 18);
            _rows.Children.Add(empty);
            return;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0 && rows[i] is Border b)
                b.BorderThickness = new Thickness(0, 1, 0, 0);
            _rows.Children.Add(rows[i]);
        }
    }

    // ———————————————— 数据（IApi 优先，本地 Core 回落） ————————————————

    private sealed class ModRow
    {
        public string Filename = "";
        public string Name = "";
        public bool Enabled = true;
    }

    private sealed class SaveRow
    {
        public string Name = "";
        public string Path = "";
        public string SizeText = "";
    }

    private async Task<List<ModRow>> FetchModsAsync()
    {
        var list = new List<ModRow>();
        var raw = await Api.TryCallAsync<List<object?>>("get_installed_mod_entries", null, new { instance = _inst });
        if (raw != null)
        {
            foreach (var item in raw)
            {
                if (item is not PyDict d) continue;
                list.Add(new ModRow
                {
                    Filename = Py.S(d.Get("filename")),
                    Name = Py.S(d.Get("name")),
                    Enabled = Py.Truthy(d.Get("enabled", true)),
                });
            }
            return list;
        }
        await Task.Run(() =>
        {
            try
            {
                foreach (var item in Mods.ListInstanceModEntries(new Instance(_inst)))
                    if (item is PyDict d)
                        list.Add(new ModRow
                        {
                            Filename = Py.S(d.Get("filename")),
                            Name = Py.S(d.Get("name")),
                            Enabled = Py.Truthy(d.Get("enabled", true)),
                        });
            }
            catch (Exception) { }
        });
        return list;
    }

    private async Task<List<SaveRow>> FetchSavesAsync()
    {
        var raw = await Api.TryCallAsync<List<object?>>("list_saves", null, new { instance = _inst });
        var list = new List<SaveRow>();
        if (raw != null)
        {
            foreach (var item in raw)
                if (item is PyDict d)
                    list.Add(new SaveRow
                    {
                        Name = Py.S(d.Get("name")),
                        Path = Py.S(d.Get("path")),
                        SizeText = FormatBytes(Py.Int(d.Get("bytes"))),
                    });
            return list;
        }
        await Task.Run(() =>
        {
            try
            {
                foreach (var item in Saves.ListSaves(new Instance(_inst), null!))
                    if (item is PyDict d)
                        list.Add(new SaveRow
                        {
                            Name = Py.S(d.Get("name")),
                            Path = Py.S(d.Get("path")),
                            SizeText = FormatBytes(Py.Int(d.Get("bytes"))),
                        });
            }
            catch (Exception) { }
        });
        return list;
    }

    // 资源包 / 光影：IApi 未实现 → 本地扫描实例（或隔离版本）目录下的 zip 与文件夹
    private Task<List<string>> FetchMediaAsync(string kind)
    {
        return Task.Run(() =>
        {
            var list = new List<string>();
            try
            {
                string dir = Path.Combine(MediaGameDir(kind), kind);
                if (!Directory.Exists(dir)) return list;
                foreach (var p in Directory.EnumerateFileSystemEntries(dir))
                {
                    string name = Path.GetFileName(p);
                    if (name.StartsWith(".")) continue;
                    if (Directory.Exists(p) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        list.Add(p);
                }
            }
            catch (Exception) { }
            return list.OrderByDescending(p => File.GetLastWriteTimeUtc(p)).Take(200).ToList();
        });
    }

    private string MediaGameDir(string kind)
    {
        string ver = CurrentVersion;
        try
        {
            if (ver.Length > 0 && IsIsolated(ver))
                return VersionSettings.GameDir(new Instance(_inst), ver);
        }
        catch (Exception) { }
        return InstancePath();
    }

    private string InstancePath()
    {
        try { return new Instance(_inst).path; }
        catch (Exception) { return _inst; }
    }

    private string VersionDir(string ver)
    {
        try { return Path.Combine(new Instance(_inst).VersionsDir(), ver); }
        catch (Exception) { return ""; }
    }

    private async Task LaunchNowAsync()
    {
        await LibraryPage.SetDefaultInstanceAsync(_inst);
        LibraryPage.NavRequested?.Invoke("launch");
        Toast("启动", "启动链路（preflight/launch_game）在 M3 落地", ToastKind.Info);
    }

    // ———————————————— 小工具 ————————————————

    private static string JoinParts(params string?[] parts)
        => string.Join(" · ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.#} {units[u]}";
    }

    // 白卡内一行：高 46，行间 B.RowLine 细线，悬停 B.RowHover（同 Windows 版）
    private static Border Row(string name, string? sub, Control tail)
    {
        var nameTb = UiClass.Txt(name, 13.5, bold: true);
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        TextBlock? subTb = null;
        if (!string.IsNullOrEmpty(sub))
        {
            subTb = UiClass.Txt(sub, 11.5, fg: "B.InkFaint");
            subTb.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        var left = UiClass.V(1, nameTb, subTb);
        left.VerticalAlignment = VerticalAlignment.Center;
        tail.VerticalAlignment = VerticalAlignment.Center;
        tail.Margin = new Thickness(12, 0, 0, 0);
        var g = UiClass.G(null, "*,Auto");
        g.Add(left, 0, 0);
        g.Add(tail, 0, 1);
        var row = new Border
        {
            MinHeight = 46,
            Padding = new Thickness(18, 4),
            Background = Brushes.Transparent,
            Child = g,
        };
        row[!Border.BorderBrushProperty] = row.GetResourceObservable("B.RowLine").ToBinding();
        row.PointerEntered += (_, _) => row[!Border.BackgroundProperty] = row.GetResourceObservable("B.RowHover").ToBinding();
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private async void Run(Func<Task> work, string? errTitle = null)
    {
        try { await work(); }
        catch (Exception ex)
        {
            ToastHost.Show(errTitle ?? "操作失败", ex.Message, ToastKind.Error);
        }
    }
}
