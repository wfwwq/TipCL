using System;
using Avalonia;
using Avalonia.Styling;
using TipCL.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
// 命名空间 TipCL.App.Pages.* 下 `Ui` 会被解析成命名空间而非类，用别名指到类（B0 同款约定）
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Library;

// 实例库（移植自 Windows 版 src/TipCL/Pages/LibraryPage.cs，2026-11 双栏版）：
// 左栏文件夹（实例）列表卡 + 右栏版本选择（按加载器分组折叠）。
// 数据走 PageBase.Api（snake_case 契约）；get_version_rows / open_instance_folder 等
// IApi 尚未实现的方法回落到 TipCL.Core 直调（见 progress/M2-B2.md 依赖申请）。
public sealed class LibraryPage : PageBase
{
    private sealed class InstRow
    {
        public string Name = "";
        public string Path = "";
        public int Versions;
    }

    private sealed class Ver
    {
        public string Id = "";
        public string Loader = "";
        public string Type = "release";
        public bool Isolated;
        public string IsolationLabel = "";
        public int Mods;
    }

    private sealed class Group
    {
        public string Key = "";
        public bool Open;
        public Border Card = null!;
        public StackPanel Body = null!;
        public Avalonia.Controls.Shapes.Path Chevron = null!;
        public TextBlock Folded = null!;
    }

    private sealed class FolderRowView
    {
        public InstRow Info = new();
        public Border Root = null!;
        public bool Hover;

        public void Paint(bool selected)
        {
            Root[!Border.BackgroundProperty] = Root.GetResourceObservable((selected || Hover) ? "B.AccentSoft" : "B.Paper").ToBinding();
            Root[!Border.BorderBrushProperty] = Root.GetResourceObservable(selected ? "B.Accent" : (Hover ? "B.AccentLite" : "B.Line")).ToBinding();
        }
    }

    private static readonly string[] LoaderKeys = { "fabric", "forge", "quilt", "neoforge", "optifine", "liteloader" };
    private static readonly string[] LoaderNames = { "Fabric", "Forge", "Quilt", "NeoForge", "OptiFine", "LiteLoader" };
    private static readonly (string Key, string Title, bool Open)[] Defs =
    {
        ("fabric", "Fabric 版本", true),
        ("forge", "Forge 版本", true),
        ("quilt", "Quilt 版本", true),
        ("neoforge", "NeoForge 版本", true),
        ("optifine", "OptiFine 版本", true),
        ("liteloader", "LiteLoader 版本", true),
        ("vanilla", "原版版本", true),
    };

    /// <summary>页面发起跳转请求（双击版本 → 启动页等）。外壳接线，见 progress/M2-B2.md 依赖申请。</summary>
    public static Action<string>? NavRequested; // 外壳接线（依赖申请见 progress/M2-B2.md）

    // 折叠态与收藏：B0 未提供 UiPrefs（缺口 B），先进程内记忆，B0 落 UiPrefs 后换持久化
    private static readonly Dictionary<string, bool> Expanded = new();
    private static readonly Dictionary<string, List<string>> Favorites = new();

    private readonly StackPanel _folders = new();
    private readonly TextBlock _folderPath = UiClass.Small("");
    private readonly TextBlock _count = UiClass.Muted("");
    private readonly TextBox _search = UiClass.Input("过滤版本名", "", 170);
    private readonly StackPanel _groupsHost = new();
    private readonly StackPanel _empty = UiClass.Empty("○", "这个文件夹还没有版本", "点左侧「添加或导入」，或到「下载」页装一个");
    private readonly List<FolderRowView> _folderRows = new();

    private List<InstRow> _instances = new();
    private List<Ver> _vers = new();
    private string _folder = "";
    private string _defaultInstance = "";
    private string _rootPath = "";

    // 页内切换前的原始内容（进入实例详情后返回时恢复）
    private Control? _shell;

    private readonly Grid _body;
    private readonly Border _leftCard;
    private readonly StackPanel _rightCol;

    public LibraryPage() : base("library", "实例库")
    {
        // —— 左栏：文件夹列表卡 ——
        var add = UiClass.Btn("添加已有文件夹", BtnKind.Chip, (_, _) => Run(AddFolderAsync, "导入失败"));
        var pack = UiClass.Btn("导入整合包", BtnKind.Chip, (_, _) => Run(ImportPackAsync));
        var create = UiClass.Btn("新建实例", BtnKind.Soft, (_, _) => Run(CreateAsync, "创建失败"));
        var migrate = UiClass.Btn("导入官方启动器", BtnKind.Chip, (_, _) => Run(MigrateAsync));
        _folderPath[!TextBlock.ForegroundProperty] = _folderPath.GetResourceObservable("B.InkMuted").ToBinding();
        var h = UiClass.Txt("文件夹列表", 18, bold: true);
        h.FontWeight = FontWeight.ExtraBold;
        var left = UiClass.Card(UiClass.V(10, h, _folderPath, _folders, UiClass.Sep(), UiClass.Txt("添加或导入", 13, bold: true), add, pack, create, migrate), 14);
        left.MinWidth = 300;
        left.MaxWidth = 380;
        left.VerticalAlignment = VerticalAlignment.Top;

        // —— 右栏头部：标题 + 计数 + 搜索 + 实例详情 + 刷新 ——
        var h2 = UiClass.Txt("版本选择", 21, bold: true);
        h2.FontWeight = FontWeight.ExtraBold;
        var refresh = UiClass.Btn("刷新", BtnKind.Chip, (_, _) => Run(LoadAsync));
        var detail = UiClass.Btn("实例详情", BtnKind.Chip, (_, _) => Run(() => OpenDetailAsync(), "打开失败"));
        _search.TextChanged += (_, _) => RenderGroups();
        var head = UiClass.G("Auto,Auto", "Auto,Auto,*,Auto,Auto,Auto");
        var subHead = UiClass.Txt("双击版本直接启动；行尾可收藏/删除/设置", 12.5, fg: "B.InkMuted");
        subHead.TextWrapping = TextWrapping.Wrap;
        head.Add(h2, 0, 0).Children.OfType<Control>().Last().VerticalAlignment = VerticalAlignment.Center;
        _count.Margin = new Thickness(10, 0, 10, 0);
        head.Add(_count, 0, 1);
        head.Add(_search, 0, 3);
        head.Add(detail, 0, 4);
        head.Add(refresh, 0, 5);
        head.Add(subHead, 1, 0);
        Grid.SetColumnSpan(subHead, 6);
        Grid.SetColumnSpan(subHead, 6);
        _count.VerticalAlignment = VerticalAlignment.Center;
        _search.VerticalAlignment = VerticalAlignment.Center;
        detail.VerticalAlignment = VerticalAlignment.Center;
        refresh.VerticalAlignment = VerticalAlignment.Center;
        _empty.IsVisible = false;

        _rightCol = UiClass.V(12, head, _groupsHost, _empty);
        _rightCol.VerticalAlignment = VerticalAlignment.Top;
        _leftCard = left;
        _body = UiClass.G("Auto");
        _body.Add(_leftCard, 0, 0);
        _body.Add(_rightCol, 0, 2);
        _body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(18)));
        _body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse("2*")));
        Content = PageBase.ScrollBody(_body);
        _shell = (Control)Content;
    }

    // ———————————————— 数据 ————————————————

    protected override void OnCreate() => Run(LoadAsync);

    public override string ToString() => PageTitle;

    private async Task LoadAsync()
    {
        _instances = await FetchInstancesAsync();
        _defaultInstance = await Api.TryCallAsync("get_setting", "", new { key = "default_instance", dflt = "" }) ?? "";
        _rootPath = SafeRootPath();
        if (_instances.All(i => i.Name != _folder))
            _folder = _instances.Any(i => i.Name == _defaultInstance) ? _defaultInstance : (_instances.FirstOrDefault()?.Name ?? "");
        await EnsureCurrentAsync();
        await ReloadVersionsAsync();
        RenderFolders();
        RenderGroups();
    }

    private async Task<List<InstRow>> FetchInstancesAsync()
    {
        var rows = new List<InstRow>();
        foreach (var d in await Api.TryCallAsync<List<object?>>("get_instances", new List<object?>()) ?? new List<object?>())
        {
            if (d is not PyDict dict) continue;
            var info = new InstRow
            {
                Name = Py.S(dict.Get("name")),
                Path = Py.S(dict.Get("path")),
                Versions = (dict.Get("versions") as List<object?> ?? new List<object?>()).Count,
            };
            if (info.Name.Length > 0) rows.Add(info);
        }
        return rows;
    }

    private static string SafeRootPath()
    {
        try { return Instances.RootDir(); }
        catch (Exception) { return ""; }
    }

    // 与 Windows 版一致：切文件夹先把选中实例设为默认（get_version_rows/版本扫描都只认默认实例）
    private async Task EnsureCurrentAsync()
    {
        if (!string.IsNullOrEmpty(_folder) && _folder != _defaultInstance)
        {
            await SetDefaultInstanceAsync(_folder);
            _defaultInstance = _folder;
        }
    }

    internal static Task SetDefaultInstanceAsync(string instance)
        => Api.CallAsync<object>("save_settings", new { data = new { default_instance = instance } });

    private async Task ReloadVersionsAsync()
    {
        // 首选 IApi 契约 get_version_rows；B0 方法表尚未实现 → 本地扫描实例版本目录
        var raw = await Api.TryCallAsync<List<object?>>("get_version_rows", null, new { include_hidden = false });
        var list = new List<Ver>();
        if (raw is { Count: > 0 })
        {
            foreach (var item in raw)
            {
                if (item is not PyDict r) continue;
                list.Add(new Ver
                {
                    Id = Py.S(r.Get("id")),
                    Loader = Py.S(r.Get("loader")),
                    Type = InferType(Py.S(r.Get("id"))),
                    Isolated = Py.Truthy(r.Get("isolated")),
                    IsolationLabel = Py.S(r.Get("isolation_label")),
                    Mods = (int)Py.Int(r.Get("mods")),
                });
            }
        }
        else
        {
            list = await Task.Run(() => ScanLocalVersionsAsync(_folder));
        }
        _vers = list;
    }

    // 本地版本扫描：InstalledIds + VersionSettings（隔离标签）+ id/版本 JSON 推加载器
    private static List<Ver> ScanLocalVersionsAsync(string instance)
    {
        var result = new List<Ver>();
        if (string.IsNullOrEmpty(instance)) return result;
        try
        {
            var inst = new Instance(instance);
            foreach (var idObj in inst.InstalledIds())
            {
                string id = Py.S(idObj);
                if (id.Length == 0 || VersionOps.IsHidden(inst, id)) continue;
                var ver = new Ver { Id = id, Type = InferType(id) };
                try
                {
                    string json = inst.VersionJson(id)?.ToString() ?? "";
                    foreach (var name in LoaderNames)
                        if (json.Contains(name, StringComparison.OrdinalIgnoreCase))
                        {
                            ver.Loader = name;
                            break;
                        }
                    var settings = VersionSettings.Load(inst, id);
                    ver.Isolated = VersionSettings.IsIsolated(settings);
                    string iso = Py.S(settings.Get("isolation"));
                    if (ver.Isolated)
                        ver.IsolationLabel = Py.S(VersionSettings.ISOLATION_LABELS.Get(iso, iso));
                    string modsDir = VersionSettings.ModsDir(inst, id);
                    if (Directory.Exists(modsDir))
                        ver.Mods = Directory.GetFiles(modsDir, "*.jar").Length
                                 + Directory.GetFiles(modsDir, "*.jar.disabled").Length;
                }
                catch (Exception) { /* 单版本异常不拖垮整表 */ }
                result.Add(ver);
            }
        }
        catch (Exception) { /* 实例不存在等情况 */ }
        return result;
    }

    private static string InferType(string id)
    {
        string t = id.ToLowerInvariant();
        if (t.StartsWith("rd-") || t.StartsWith("a1.") || t.StartsWith("c0.") || t.StartsWith("inf-") || t.StartsWith("alpha"))
            return "old_alpha";
        if (t.StartsWith("b1."))
            return "old_beta";
        if (t.Contains("-pre") || t.Contains("-rc") || (t.Length > 5 && char.IsDigit(t[0]) && t.Contains('w')))
            return "snapshot";
        return "release";
    }

    private string GroupKeyOf(Ver v)
    {
        int i = Array.IndexOf(LoaderNames, v.Loader);
        return i >= 0 ? LoaderKeys[i] : "vanilla";
    }

    // ———————————————— 渲染 ————————————————

    private void RenderFolders()
    {
        _folders.Children.Clear();
        _folderRows.Clear();
        foreach (var info in _instances)
        {
            var row = BuildFolderRow(info);
            _folderRows.Add(row);
            _folders.Children.Add(row.Root);
        }
        UpdateFolderPath();
        PaintFolders();
    }

    private string FolderPathFor(string name)
    {
        string known = _instances.FirstOrDefault(i => i.Name == name)?.Path ?? "";
        if (!string.IsNullOrEmpty(known)) return known;
        if (string.IsNullOrEmpty(_rootPath)) return "";
        return _rootPath.TrimEnd('/', '\\') + Path.DirectorySeparatorChar + name;
    }

    private void UpdateFolderPath()
    {
        string text = FolderPathFor(_folder);
        _folderPath.Text = text.Length > 0 ? text : "当前文件夹";
        ToolTip.SetTip(_folderPath, text.Length > 0 ? text : null);
    }

    private FolderRowView BuildFolderRow(InstRow info)
    {
        string instName = info.Name;
        var name = UiClass.Txt(instName, 13.5, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var count = UiClass.Small($"{info.Versions} 个版本");
        count.TextTrimming = TextTrimming.CharacterEllipsis;
        var mid = UiClass.V(1, name, count);
        mid.Margin = new Thickness(10, 0, 0, 0);

        var icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8) };
        icon[!Border.BackgroundProperty] = icon.GetResourceObservable("B.AccentSoft").ToBinding();
        icon.Child = Center(IconLib.Make("Folder", "B.AccentText", 16));

        var tail = UiClass.H(6);
        tail.VerticalAlignment = VerticalAlignment.Center;
        if (instName == _defaultInstance)
            tail.Children.Add(UiClass.Tag("默认", "B.OnAccentStrong", "B.AccentStrong"));
        var moreBtn = UiClass.IconBtn("More", "实例操作", null, 16);
        moreBtn.Click += (_, _) => FolderMenu(moreBtn, instName);
        tail.Children.Add(moreBtn);

        var g = UiClass.G(null, "Auto,*,Auto");
        g.Add(icon, 0, 0).Children.OfType<Control>().Last().VerticalAlignment = VerticalAlignment.Center;
        g.Add(mid, 0, 1);
        g.Add(tail, 0, 2);

        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 9),
            BorderThickness = new Thickness(1),
            Child = g,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 0, 0, 6),
        };
        ToolTip.SetTip(root, FolderPathFor(instName));
        var view = new FolderRowView { Info = info, Root = root };
        root.PointerEntered += (_, _) => { view.Hover = true; view.Paint(instName == _folder); };
        root.PointerExited += (_, _) => { view.Hover = false; view.Paint(instName == _folder); };
        root.Tapped += (_, e) =>
        {
            if (!HitButton(e.Source as Control)) SelectFolder(instName);
        };
        return view;
    }

    private static Control Center(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    private void PaintFolders()
    {
        foreach (var row in _folderRows)
            row.Paint(row.Info.Name == _folder);
    }

    private void SelectFolder(string name)
    {
        if (_folder == name) return;
        _folder = name;
        UpdateFolderPath();
        PaintFolders();
        Run(async () =>
        {
            await EnsureCurrentAsync();
            await ReloadVersionsAsync();
            RenderFolders();
            RenderGroups();
        });
    }

    private void RenderGroups()
    {
        _groupsHost.Children.Clear();
        string q = _search.Text?.Trim() ?? "";
        var map = new Dictionary<string, List<Ver>>();
        foreach (var ver in _vers)
        {
            if (q.Length == 0 || ver.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                string key = GroupKeyOf(ver);
                if (!map.TryGetValue(key, out var value))
                    map[key] = value = new List<Ver>();
                value.Add(ver);
            }
        }
        int total = 0;
        var cards = new List<Control>();
        foreach (var (key, title, def) in Defs)
        {
            if (!map.TryGetValue(key, out var list) || list.Count == 0)
                continue;
            bool searching = q.Length > 0;
            var group = BuildGroup(key, $"{title} ({list.Count})", searching || PrefsOpen(key, def));
            int ri = 0;
            foreach (var item in list.OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase))
            {
                var row = BuildVerRow(item);
                if (ri++ > 0)
                {
                    row.BorderThickness = new Thickness(0, 1, 0, 0);
                    row[!Border.BorderBrushProperty] = row.GetResourceObservable("B.RowLine").ToBinding();
                }
                group.Body.Children.Add(row);
            }
            ApplyOpen(group, group.Open);
            _groupsHost.Children.Add(group.Card);
            cards.Add(group.Card);
            total += list.Count;
        }
        _empty.IsVisible = total == 0;
        _count.Text = $"共 {total} 个版本";
        if (cards.Count > 0)
            Motion.Stagger(_groupsHost);
    }

    private Group BuildGroup(string key, string title, bool open)
    {
        var group = new Group { Key = key, Open = open };
        var head = UiClass.Txt(title, 13.5, bold: true, "B.AccentText");
        head.TextTrimming = TextTrimming.CharacterEllipsis;
        string iconKey = Array.IndexOf(LoaderKeys, key) >= 0 ? "Package" : "Grid";
        var icon = IconLib.Make(iconKey, "B.AccentText", 16);
        var chev = IconLib.Make("ChevronDown", "B.InkMuted", 12);
        var folded = UiClass.Small("已折叠");
        folded[!TextBlock.ForegroundProperty] = folded.GetResourceObservable("B.InkFaint").ToBinding();
        var bar = UiClass.H(8, icon, head, UiClass.Spring(), folded, chev);
        bar.VerticalAlignment = VerticalAlignment.Center;

        var hd = new Border { Padding = new Thickness(12, 8), Cursor = new Cursor(StandardCursorType.Hand), Child = bar };
        var body = new StackPanel { Margin = new Thickness(8, 2, 8, 8) };
        var sep = UiClass.Sep();
        hd.PointerEntered += (_, _) => hd[!Border.BackgroundProperty] = hd.GetResourceObservable("B.Paper").ToBinding();
        hd.PointerExited += (_, _) => hd.Background = null;
        hd.Tapped += (_, _) => ToggleGroup(group);
        var card = UiClass.Card(UiClass.V(0, hd, sep, body), 0);
        card.Margin = new Thickness(0, 0, 0, 10);
        group.Card = card;
        group.Body = body;
        group.Chevron = chev;
        group.Folded = folded;
        return group;
    }

    private void ApplyOpen(Group g, bool open)
    {
        g.Open = open;
        g.Body.IsVisible = open;
        g.Folded.IsVisible = !open;
        g.Chevron.Data = Geometry.Parse(open ? IconLib.ChevronDownData : IconLib.ChevronRightData);
    }

    private void ToggleGroup(Group g)
    {
        ApplyOpen(g, !g.Open);
        Expanded["lib:" + g.Key] = g.Open;
    }

    private static bool PrefsOpen(string key, bool def)
        => Expanded.TryGetValue("lib:" + key, out var v) ? v : def;

    private Border BuildVerRow(Ver v)
    {
        string inst = _folder;
        string id = v.Id;
        bool fav = IsFav(id);
        var tags = UiClass.H(6);
        if (v.Type == "snapshot")
            tags.Children.Add(UiClass.Tag("快照"));
        if (v.Mods > 0)
            tags.Children.Add(UiClass.Tag($"模组 {v.Mods}"));
        if (v.Isolated && !string.IsNullOrEmpty(v.IsolationLabel))
            tags.Children.Add(UiClass.Tag(v.IsolationLabel));

        var nameRow = UiClass.H(6, Name(id, 15));
        if (tags.Children.Count > 0)
            nameRow.Children.Add(tags);
        var subTb = UiClass.Txt(SubLine(v), 12.5, fg: "B.InkMuted");
        subTb.TextTrimming = TextTrimming.CharacterEllipsis;
        var mid = UiClass.V(2, nameRow, subTb);

        var star = UiClass.IconBtn("Star", fav ? "取消收藏" : "收藏", null, 16);
        if (fav) star.Content = IconLib.Make("Star", "B.Accent", 16);
        star.Click += (_, _) => { ToggleFav(v); };
        var del = UiClass.IconBtn("Trash", "卸载这个版本", null, 16);
        del.Click += (_, _) => Run(() => UninstallAsync(inst, id), "卸载失败");
        var gear = UiClass.IconBtn("Gear", "版本设置", null, 16);
        gear.Click += (_, _) => Run(() => OpenDetailAsync(id), "打开失败");
        var tail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        tail.Children.Add(star);
        tail.Children.Add(del);
        tail.Children.Add(gear);

        var g = UiClass.G(null, "Auto,*,Auto");
        var tile = LoaderTile(v.Loader, 36);
        g.Add(tile, 0, 0).Children.OfType<Control>().Last().VerticalAlignment = VerticalAlignment.Center;
        mid.Margin = new Thickness(10, 0, 10, 0);
        mid.VerticalAlignment = VerticalAlignment.Center;
        g.Add(mid, 0, 1);
        g.Add(tail, 0, 2);
        tail.VerticalAlignment = VerticalAlignment.Center;

        var row = new Border
        {
            MinHeight = 72,
            Padding = new Thickness(10, 8),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = g,
        };
        row.PointerEntered += (_, _) => row[!Border.BackgroundProperty] = row.GetResourceObservable("B.RowHover").ToBinding();
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.DoubleTapped += (_, e) =>
        {
            if (!HitButton(e.Source as Control))
            {
                e.Handled = true;
                Run(() => LaunchVerAsync(inst, id));
            }
        };
        return row;
    }

    private static TextBlock Name(string text, double size)
    {
        var t = UiClass.Txt(text, size, bold: true);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    /// <summary>加载器图标块：圆角方块 + 矢量图标（无官方加载器图时的等价物）。</summary>
    private Border LoaderTile(string loader, double size)
    {
        string key = loader.ToLowerInvariant() switch
        {
            "fabric" => "Lightning",
            "forge" => "Server",
            "quilt" => "Puzzle",
            "neoforge" => "Server",
            "optifine" => "Filter",
            "liteloader" => "Lightning",
            _ => "Box",
        };
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(8),
        };
        b[!Border.BackgroundProperty] = b.GetResourceObservable("B.AccentSoft").ToBinding();
        b.Child = IconLib.Make(key, "B.AccentText", Math.Max(14, size * 0.45));
        return b;
    }

    private static string SubLine(Ver v)
    {
        string type = v.Type switch
        {
            "release" => "正式版",
            "snapshot" => "快照",
            "old_beta" => "旧测试",
            "old_alpha" => "旧阿尔法",
            _ => "",
        };
        string s = type.Length > 0 ? $"{type} {v.Id}" : v.Id;
        if (Array.IndexOf(LoaderNames, v.Loader) >= 0)
            s += "，" + v.Loader;
        if (v.Isolated && !string.IsNullOrEmpty(v.IsolationLabel))
            s += " · " + v.IsolationLabel;
        return s;
    }

    private List<string> Favs()
    {
        if (!Favorites.TryGetValue(_folder, out var value))
            Favorites[_folder] = value = new List<string>();
        return value;
    }

    private bool IsFav(string id) => Favs().Contains(id);

    private void ToggleFav(Ver v)
    {
        var list = Favs();
        if (!list.Remove(v.Id)) list.Add(v.Id);
        RenderGroups();
    }

    private async Task LaunchVerAsync(string inst, string ver)
    {
        await SetDefaultInstanceAsync(inst);
        _defaultInstance = inst;
        NavRequested?.Invoke("launch");
        Toast("已设为默认", $"{inst} · {ver}（启动逻辑 M3 落地）", ToastKind.Info);
    }

    // ———————————————— 页内切换：实例详情 ————————————————

    private async Task OpenDetailAsync(string? focusVer = null)
    {
        if (string.IsNullOrEmpty(_folder)) return;
        if (_folder != _defaultInstance)
        {
            await SetDefaultInstanceAsync(_folder);
            _defaultInstance = _folder;
        }
        var found = _instances.FirstOrDefault(i => i.Name == _folder);
        var info = new InstanceDetailPage.InstInfo
        {
            Name = _folder,
            Versions = found?.Versions ?? _vers.Count,
        };
        _shell ??= (Control)Content;
        Content = new InstanceDetailPage(this, info, () =>
        {
            if (_shell != null) Content = _shell;
            Run(LoadAsync);
        }, focusVer);
    }

    // ———————————————— 实例操作 ————————————————

    private async Task UninstallAsync(string inst, string ver)
    {
        if (!await Dlg.Confirm("卸载版本", $"确定卸载「{ver}」？该版本目录下的独立模组与存档会一并删除。", "卸载", "取消", danger: true))
            return;
        var done = await Api.TryCallAsync("uninstall_version", false, new { spec = $"{inst} / {ver}" });
        if (!done)
        {
            // IApi 未实现 uninstall_version → 本地删除 versions/<id> 目录
            await Task.Run(() =>
            {
                var dir = Path.Combine(new Instance(inst).VersionsDir(), ver);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            });
        }
        await LoadAsync();
        Toast("已卸载", ver, ToastKind.Success);
    }

    private void FolderMenu(Control anchor, string instance)
    {
        var menu = new ContextMenu();
        void Item(string head, Action act)
        {
            var mi = new MenuItem { Header = head };
            mi.Click += (_, _) => Run(() => { act(); return Task.CompletedTask; });
            menu.Items.Add(mi);
        }
        Item("打开文件夹", () => OpenInFileManager(FolderPathFor(instance), "打开失败"));
        Item("设为默认实例", () => Run(async () =>
        {
            _folder = instance;
            await EnsureCurrentAsync();
            await LoadAsync();
            Toast("默认实例", $"之后的启动将默认使用「{instance}」", ToastKind.Success);
        }));
        Item("实例详情", () => Run(() => OpenDetailAsync()));
        menu.Items.Add(new Separator());
        Item("重命名…", () => Run(async () =>
        {
            string text = await Dlg.Prompt("重命名实例", "新名称", instance);
            if (!string.IsNullOrWhiteSpace(text) && text != instance)
            {
                await Api.CallAsync<object>("rename_instance", new { name = instance, new_name = text });
                await LoadAsync();
            }
        }));
        Item("删除实例", () => Run(async () =>
        {
            if (!await Dlg.Confirm("删除实例", $"「{instance}」下的版本、模组、存档都会被删除，且不可恢复。", "永久删除", "取消", danger: true))
                return;
            await Api.CallAsync<object>("delete_instance", new { name = instance });
            await LoadAsync();
            Toast("已删除", instance, ToastKind.Success);
        }));
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.Open(anchor);
    }

    private async Task AddFolderAsync()
    {
        string path = await Dlg.PickFolder("选择一个已有的游戏文件夹（含 versions 的 .minecraft）");
        if (string.IsNullOrWhiteSpace(path)) return;
        string fileName = Path.GetFileName(path.TrimEnd('\\', '/'));
        string name = await Dlg.Prompt("导入为文件夹", "显示名称", fileName);
        if (!string.IsNullOrWhiteSpace(name))
        {
            // IApi 未实现 import_existing_folder（M3 任务化）→ 本地登记：建同名实例元数据
            await Task.Run(() =>
            {
                var inst = Instances.CreateUniqueInstance(name, "游戏");
                try { inst.SetMeta("source_path", path); } catch (Exception) { /* 元数据可选 */ }
            });
            await LoadAsync();
            Toast("已登记文件夹", "版本迁移在 M3 任务化", ToastKind.Info);
        }
    }

    private async Task ImportPackAsync()
    {
        string file = await Dlg.PickFile("导入整合包", "*.mrpack", "*.zip");
        if (file == null) return;
        // 整合包安装器属 M3（Catalog/Modpack 未入 IApi）
        Toast("整合包导入", "安装器将在 M3 落地", ToastKind.Warning);
        await Task.CompletedTask;
    }

    private async Task CreateAsync()
    {
        string name = await Dlg.Prompt("新建实例", "实例名称（会创建独立的 .minecraft）", "", "例如：1.20.1 生存");
        if (!string.IsNullOrWhiteSpace(name))
        {
            await Api.CallAsync<object>("create_instance", new { name });
            await LoadAsync();
            Toast("已创建", name, ToastKind.Success);
        }
    }

    // GameDirDetector 是 Core internal → 本地探测常见官方启动器目录
    private static string? DetectOfficialDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft") }
            : OperatingSystem.IsMacOS()
                ? new[] { Path.Combine(home, "Library", "Application Support", "minecraft") }
                : new[] { Path.Combine(home, ".minecraft") };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private async Task MigrateAsync()
    {
        string dir = await Task.Run(() => DetectOfficialDir() ?? "");
        if (dir.Length == 0)
        {
            Toast("没找到官方启动器", "未检测到 .minecraft 目录", ToastKind.Warning);
            return;
        }
        Toast("检测到官方启动器", "版本迁移在 M3 任务化：" + dir, ToastKind.Info);
        await Task.CompletedTask;
    }

    // ———————————————— 跨平台工具 ————————————————

    /// <summary>跨平台打开文件夹：Windows explorer / macOS open / Linux xdg-open。</summary>
    public static void OpenInFileManager(string path, string errTitle = "打开失败")
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            ToastHost.Show(errTitle, "目录不存在：" + path, ToastKind.Error);
            return;
        }
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", new[] { path });
            else
                Process.Start("xdg-open", new[] { path });
        }
        catch (Exception ex)
        {
            ToastHost.Show(errTitle, ex.Message, ToastKind.Error);
        }
    }

    // ———————————————— 小工具 ————————————————

    private async void Run(Func<Task> work, string? errTitle = null)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            ToastHost.Show(errTitle ?? "操作失败", ex.Message, ToastKind.Error);
        }
    }

    // 命中测试：点在按钮/菜单等交互控件上时不算行点击
    private static bool HitButton(Control? source)
    {
        while (source != null)
        {
            if (source is Button || source is ToggleButton || source is MenuItem || source is ComboBox)
                return true;
            source = source.GetVisualParent() as Control;
        }
        return false;
    }
}
