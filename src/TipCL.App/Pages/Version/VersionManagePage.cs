// 来源: Windows 版参考仓库 src/TipCL/Pages/VersionManagePage.cs(1110 行,M4-H P2 移植)。
// 版本管理页(外壳 NavDefs 的 "instance",标题「版本管理」,由启动页版本弹层的「管理」进来):
// 左栏文件夹(实例)列表卡 + 右栏按来源分组的版本卡(收藏 / Fabric / Forge / Quilt / NeoForge /
// OptiFine / LiteLoader / 原版 / 不常用 / 隐藏),组可折叠且记住折叠态。
// 行操作:收藏星、隔离开关、卸载、版本设置;双击启动;右键菜单(打开版本/游戏文件夹、存档管理、
// 重命名、复制、隐藏、桌面快捷方式、导出启动脚本、修复、卸载)。文件夹右键菜单(打开、设为默认、
// 选择 Java、管理模组、存档管理、导出整合包、重命名、删除)。
// 折叠态与收藏(R15):原「进程内静态表」降级项已补 —— 落 config.json 的 ui_prefs 键
// (Windows 版落独立文件 wpf-ui.json;Linux 侧沿用唯一用户配置文件,见 TipCL.Core.Launcher.UiPrefs)。
// M4-H P3.5:「创建桌面快捷方式」已恢复 —— Linux 侧补了真的 CLI 入口(见 DECISIONS D-23)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using TipCL.App.Pages.Library;
using TipCL.App.Shell;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Version;

/// <summary>版本管理页。</summary>
public sealed class VersionManagePage : PageBase
{
    private sealed class Ver
    {
        public string Id = "";
        public string Loader = "";
        public string Mc = "";
        public string Type = "release";
        public bool Hidden;
        public bool Isolated;
        public int Mods;
        public string IsolationLabel = "";
    }

    private sealed class Group
    {
        public string Key = "";
        public bool Open;
        public Border Card = null!;
        public StackPanel Body = null!;
        public TextBlock HeadText = null!;
        public Avalonia.Controls.Shapes.Path Chevron = null!;
        public Border Sep = null!;
    }

    private sealed class FolderRow
    {
        public string Name = "";
        public int Versions;
        public Border Root = null!;
        public bool Hover;

        public void Paint(bool selected)
        {
            Root[!Border.BackgroundProperty] = Res.Bind(selected || Hover ? "B.AccentSoft" : "B.Paper2");
            Root[!Border.BorderBrushProperty] = Res.Bind(selected ? "B.Accent" : Hover ? "B.AccentLite" : "B.Line");
        }
    }

    private static readonly string[] LoaderNames = { "Fabric", "Forge", "Quilt", "NeoForge", "OptiFine", "LiteLoader" };

    private static readonly (string Key, string Title, bool Open)[] Defs =
    {
        ("fav", "收藏的版本", true),
        ("fabric", "Fabric 版本", true),
        ("forge", "Forge 版本", true),
        ("quilt", "Quilt 版本", true),
        ("neoforge", "NeoForge 版本", true),
        ("optifine", "OptiFine 版本", true),
        ("liteloader", "LiteLoader 版本", true),
        ("vanilla", "原版版本", true),
        ("uncommon", "不常用版本", false),
        ("hidden", "隐藏的版本", false),
    };

    // 折叠态与收藏(R15):落 UiPrefs -> config.json 的 ui_prefs 键(缺口 B 已补),
    // 现读现写不留静态副本;折叠态键 = "vm:" + 分组键(对齐 Windows Pages/VersionManagePage.cs:508,516)

    private readonly StackPanel _folders = new();
    private readonly TextBlock _folderPath = UiClass.Small("");
    private readonly TextBlock _count = UiClass.Muted("");
    private readonly TextBox _search = UiClass.Input("过滤版本名", "", 170);
    private readonly StackPanel _groupsHost = new();
    private readonly StackPanel _empty = UiClass.Empty("Folder", "这个文件夹还没有版本", "点左侧「添加或导入」,或到「下载」页装一个");
    private readonly List<FolderRow> _folderRows = new();

    private List<(string Name, int Versions, string Path)> _instances = new();
    private List<Ver> _vers = new();
    private string _folder = "";
    private string _defaultInstance = "";
    private string _rootPath = "";
    private Control? _shell;

    public VersionManagePage() : base("instance", "版本管理")
    {
        // ── 左栏:文件夹列表卡 ──
        var add = U.Btn("添加已有文件夹", BtnKind.Soft, (_, _) => Run(AddFolderAsync, "导入失败"), "Folder");
        var pack = U.Btn("导入整合包", BtnKind.Soft, (_, _) => Run(ImportPackAsync), "Box");
        var create = U.Btn("新建实例", BtnKind.Soft, (_, _) => Run(CreateAsync, "创建失败"), "Add");
        var migrate = U.Btn("导入官方启动器", BtnKind.Chip, (_, _) => Run(MigrateAsync), "Import");
        foreach (var b in new[] { add, pack, create, migrate }) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        var h = UiClass.H3("文件夹列表");
        _folderPath.Mk(0, 0, 0, 0).Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
        var folderCard = UiClass.Card(UiClass.V(10, h, _folderPath, _folders, UiClass.Sep(),
            UiClass.Txt("添加或导入", 13, bold: true), add, pack, create, migrate), 14);
        folderCard.VerticalAlignment = VerticalAlignment.Top;
        folderCard.MaxWidth = 272;

        // ── 右栏:版本选择 ──
        var refresh = U.Btn("刷新", BtnKind.Chip, (_, _) => Run(LoadAsync), "Refresh");
        _search.TextChanged += (_, _) => RenderGroups();
        var head = UiClass.Section("版本选择", "双击版本直接启动;卡片右侧:收藏 / 卸载 / 设置",
            UiClass.H(8, _count.VCenter(), _search.VCenter(), refresh));
        var right = UiClass.V(12, head, _groupsHost, _empty);
        right.VerticalAlignment = VerticalAlignment.Top;
        _empty.IsVisible = false;

        var grid = UiClass.G(null, "272,18,*");
        grid.Add(folderCard, 0, 0);
        grid.Add(right, 0, 2);
        grid.Margin = new Thickness(22, 20, 22, 20);
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _shell = scroll;
        Body = scroll;
    }

    // ───────────────────────────── 生命周期 ─────────────────────────────

    protected override void OnCreate()
    {
        FileDrop.Attach(this);
        Run(LoadAsync);
    }

    protected override void OnActivate() => Run(LoadAsync);

    private async Task LoadAsync()
    {
        var rows = await Api.TryCallAsync<List<object?>>("get_instances", null) ?? new();
        _instances = new List<(string, int, string)>();
        foreach (var item in rows)
        {
            if (item is not PyDict d) continue;
            string name = Py.S(d.Get("name"));
            if (name.Length == 0) continue;
            int n = d.Get("versions") is System.Collections.ICollection c ? c.Count : 0;
            _instances.Add((name, n, Py.S(d.Get("path"))));
        }
        _defaultInstance = await Api.TryCallAsync<string>("get_setting", "", new { key = "default_instance", dflt = "" }) ?? "";
        _rootPath = await Api.TryCallAsync<string>("game_root_path", "") ?? "";

        if (!_instances.Any(i => i.Name == _folder))
            _folder = _instances.Any(i => i.Name == _defaultInstance)
                ? _defaultInstance
                : _instances.FirstOrDefault().Name ?? "";

        // 照 Windows EnsureCurrentAsync:切文件夹即把它设成默认实例
        if (_folder.Length > 0 && _folder != _defaultInstance)
        {
            await Api.TryCallAsync<object>("save_settings", null, new { data = new { default_instance = _folder } });
            _defaultInstance = _folder;
        }

        await ReloadVersionsAsync();
        RenderFolders();
        RenderGroups();
    }

    private async Task ReloadVersionsAsync()
    {
        // 修缺陷 B:必须带 instance —— 不传 instance 时 Core 的 Instance("") 落到**根实例**
        // (<TIPCL_HOME>/.minecraft/versions),而这一页左栏写着「当前文件夹 <实例目录>」、
        // 行操作(uninstall/rename/copy/hide)传的也是 _folder,启动预检同样只看实例目录。
        var rows = await Api.TryCallAsync<List<object?>>("get_version_rows", null, new { instance = _folder, include_hidden = true }) ?? new();
        _vers = new List<Ver>();
        foreach (var item in rows)
        {
            if (item is not PyDict d) continue;
            string id = Py.S(d.Get("id"));
            if (id.Length == 0) continue;
            _vers.Add(new Ver
            {
                Id = id,
                Loader = Py.S(d.Get("loader")),
                Mc = Py.S(d.Get("mc")),
                Type = InferType(id),
                Hidden = Py.Truthy(d.Get("hidden")),
                Isolated = Py.Truthy(d.Get("isolated")),
                Mods = (int)Py.Int(d.Get("mods")),
                IsolationLabel = Py.S(d.Get("isolation_label")),
            });
        }
    }

    // ───────────────────────────── 文件夹栏 ─────────────────────────────

    private string FolderPathFor(string name)
    {
        string known = _instances.FirstOrDefault(i => i.Name == name).Path ?? "";
        if (known.Length > 0) return known;
        if (_rootPath.Length == 0) return "";
        if (name.Length == 0 || name == RootNameOf(_rootPath)) return _rootPath;
        return _rootPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar + name;
    }

    private static string RootNameOf(string rootPath)
    {
        string t = rootPath.TrimEnd('\\', '/');
        int i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        return i < 0 ? t : t[(i + 1)..];
    }

    private void RenderFolders()
    {
        _folders.Children.Clear();
        _folderRows.Clear();
        foreach (var (name, versions, _) in _instances)
        {
            var row = BuildFolderRow(name, versions);
            _folderRows.Add(row);
            _folders.Children.Add(row.Root);
        }
        UpdateFolderPath();
        PaintFolders();
    }

    private void UpdateFolderPath()
    {
        string path = FolderPathFor(_folder);
        _folderPath.Text = path.Length > 0 ? "当前文件夹 " + path : "当前文件夹";
        _folderPath.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(_folderPath, path.Length > 0 ? path : null);
    }

    private FolderRow BuildFolderRow(string name, int versions)
    {
        var title = UiClass.Txt(name, 13, bold: true).Trim();
        var sub = UiClass.Small($"{versions} 个版本").Trim();
        var col = UiClass.V(1, title, sub);
        col.Mk(10, 0, 0, 0);

        var icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        icon.Dyn(Border.BackgroundProperty, "B.AccentSoft");
        icon.Child = IconLib.Make("Folder", "B.AccentText", 14);

        var chips = UiClass.H(4) as StackPanel;
        chips.VerticalAlignment = VerticalAlignment.Center;
        if (name == _defaultInstance)
            chips.Children.Add(UiClass.Tag("默认", "B.OnAccentStrong", "B.AccentStrong"));
        var more = UiClass.IconBtn("More", "实例操作", null, 13);
        more.Click += (_, _) => FolderMenu(more, name);
        chips.Children.Add(more);

        var grid = UiClass.G(null, "Auto,*,Auto");
        grid.Add(icon, 0, 0);
        col.VCenter();
        grid.Add(col, 0, 1);
        grid.Add(chips, 0, 2);

        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 8, 9, 8),
            BorderThickness = new Thickness(1),
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 0, 0, 6),
        };
        ToolTip.SetTip(root, FolderPathFor(name));
        var row = new FolderRow { Name = name, Versions = versions, Root = root };
        root.PointerEntered += (_, _) => { row.Hover = true; row.Paint(name == _folder); };
        root.PointerExited += (_, _) => { row.Hover = false; row.Paint(name == _folder); };
        root.PointerReleased += (_, e) =>
        {
            if (HitButton(e.Source as Control)) return;
            SelectFolder(name);
        };
        return row;
    }

    private void PaintFolders()
    {
        foreach (var row in _folderRows) row.Paint(row.Name == _folder);
    }

    private void SelectFolder(string name)
    {
        if (_folder == name) return;
        _folder = name;
        UpdateFolderPath();
        PaintFolders();
        Run(LoadAsync);
    }

    // ───────────────────────────── 版本分组 ─────────────────────────────

    private void RenderGroups()
    {
        _groupsHost.Children.Clear();
        string q = _search.Text?.Trim() ?? "";
        var buckets = new Dictionary<string, List<Ver>>();
        foreach (var v in _vers)
        {
            if (q.Length > 0 && v.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            string key = GroupKeyOf(v);
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<Ver>();
            list.Add(v);
        }
        bool searching = q.Length > 0;
        int total = 0;
        foreach (var (key, title, def) in Defs)
        {
            if (!buckets.TryGetValue(key, out var list) || list.Count == 0) continue;
            var group = BuildGroup(key, searching || PrefsOpen(key, def));
            group.HeadText.Text = $"{title} ({list.Count})";
            foreach (var v in list) group.Body.Children.Add(BuildVerRow(v));
            ApplyOpen(group, group.Open);
            _groupsHost.Children.Add(group.Card);
            total += list.Count;
        }
        _empty.IsVisible = total == 0;
        _count.Text = $"共 {total} 个版本";
    }

    private Group BuildGroup(string key, bool open)
    {
        var group = new Group { Key = key, Open = open };
        var head = UiClass.Txt("", 13.5, bold: true, "B.AccentText");
        var chevron = IconLib.Make("ChevronUp", "B.InkMuted", 12);
        var row = UiClass.H(8, head, chevron);
        row.VerticalAlignment = VerticalAlignment.Center;
        var headBar = new Border { Padding = new Thickness(12, 9, 12, 9), Cursor = new Cursor(StandardCursorType.Hand), Child = row };
        var body = new StackPanel { Margin = new Thickness(8, 2, 8, 8) };
        var sep = UiClass.Sep();
        headBar.PointerEntered += (_, _) => headBar[!Border.BackgroundProperty] = Res.Bind("B.Paper2");
        headBar.PointerExited += (_, _) => headBar.Background = null;
        headBar.PointerReleased += (_, _) => ToggleGroup(group);
        var card = UiClass.Card(UiClass.V(0, headBar, sep, body), 0);
        card.Margin = new Thickness(0, 0, 0, 10);
        group.Card = card;
        group.Body = body;
        group.HeadText = head;
        group.Chevron = chevron;
        group.Sep = sep;
        return group;
    }

    private void ApplyOpen(Group g, bool open)
    {
        g.Open = open;
        g.Body.IsVisible = open;
        g.Sep.IsVisible = open;
        g.Chevron.Data = IconLib.Geo(open ? "ChevronUp" : "ChevronDown", 12);
    }

    private void ToggleGroup(Group g)
    {
        ApplyOpen(g, !g.Open);
        var prefs = UiPrefs.Load();
        prefs.SetExpanded(UiPrefs.VM_PREFIX + g.Key, g.Open);
        prefs.Save();
    }

    private static bool PrefsOpen(string key, bool def)
        => UiPrefs.Load().GetExpanded(UiPrefs.VM_PREFIX + key, def);

    private static bool _dummyKeepAwake;

    private Control BuildVerRow(Ver v)
    {
        string inst = _folder;
        string id = v.Id;
        bool fav = IsFav(id);

        var tags = UiClass.H(6);
        if (v.Mods > 0) tags.Children.Add(UiClass.Tag($"模组 {v.Mods}"));
        if (v.Isolated && v.IsolationLabel.Length > 0) tags.Children.Add(UiClass.Tag(v.IsolationLabel, "B.AccentDeep", "B.AccentSoft"));

        var titleRow = UiClass.H(6, UiClass.Txt(id, 13, bold: true).Trim());
        if (tags.Children.Count > 0) titleRow.Children.Add(tags);
        var col = UiClass.V(1, titleRow, UiClass.Small(SubLine(v)).Trim());

        var star = new ToggleButton
        {
            Theme = UiClass.Theme("Btn.Icon"),
            IsChecked = fav,
            Content = IconLib.Make("Star", fav ? "B.Accent" : "B.InkMuted", 13),
        };
        ToolTip.SetTip(star, fav ? "取消收藏" : "收藏");
        star.Click += (_, _) => ToggleFav(v);

        var uninstall = UiClass.IconBtn("Trash", "卸载这个版本", null, 13);
        uninstall.Click += (_, _) => Run(() => UninstallAsync(inst, id), "卸载失败");
        var setup = UiClass.IconBtn("Gear", "版本设置", null, 13);
        setup.Click += (_, _) => Run(async () =>
        {
            await VersionSetupDialog.ShowAsync(inst, id);
            await LoadAsync();
        });

        var isoTag = IsolationTag(v);
        var isoSwitch = UiClass.Switch(v.Isolated);
        ToolTip.SetTip(isoSwitch, "开:这个版本用自己的 mods / config;关:与实例里其它版本共用那一份");
        isoSwitch.IsCheckedChanged += (_, _) => Run(() => ToggleIsolationAsync(id, isoSwitch.IsChecked == true), "切换失败");

        var actions = UiClass.H(6, isoTag.VCenter(), isoSwitch, star, uninstall, setup);
        actions.VerticalAlignment = VerticalAlignment.Center;

        var thumb = new ThumbTile(id, 34);
        thumb.VerticalAlignment = VerticalAlignment.Center;

        var grid = UiClass.G(null, "Auto,*,Auto");
        grid.Add(thumb, 0, 0);
        col.Mk(9, 0, 9, 0).VCenter();
        grid.Add(col, 0, 1);
        grid.Add(actions, 0, 2);

        var rowCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 7, 8, 7),
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 0, 0, 4),
        };
        rowCard.PointerEntered += (_, _) => rowCard[!Border.BackgroundProperty] = Res.Bind("B.Paper2");
        rowCard.PointerExited += (_, _) => rowCard.Background = null;
        rowCard.DoubleTapped += (_, e) =>
        {
            if (HitButton(e.Source as Control)) return;
            e.Handled = true;
            Run(() => LaunchVerAsync(inst, id));
        };
        rowCard.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right && !HitButton(e.Source as Control))
            {
                e.Handled = true;
                VerMenu(rowCard, v);
            }
        };
        return rowCard;
    }

    private static Border IsolationTag(Ver v)
        => v.Isolated
            ? UiClass.Tag(v.IsolationLabel.Length > 0 ? v.IsolationLabel : "完全独立", "B.AccentDeep", "B.AccentSoft")
            : UiClass.Tag("大锅饭(与其他版本共用)");

    /// <summary>切换版本隔离(照 Windows ToggleIsolationVerAsync:开隔离时问要不要复制现有模组)。</summary>
    private async Task ToggleIsolationAsync(string version, bool isolated)
    {
        bool seed = false;
        if (isolated)
        {
            int pick = await Dlg.Choose("转为独立模组",
                UiClass.V(6,
                    UiClass.Muted($"「{version}」将拥有自己的 mods / config 目录。"),
                    UiClass.Muted("要把游戏目录里现有的共享模组复制一份过去吗?"),
                    UiClass.Muted("选「复制一份」保持现在能玩的样子;选「留空」从零开始装。")),
                new[] { "取消", "留空", "复制一份" }, 540);
            if (pick <= 0)
            {
                await LoadAsync();
                return;
            }
            seed = pick == 2;
        }
        await Api.CallAsync<PyDict>("toggle_version_isolation", new { version, isolated, seed });
        Toast(isolated ? "已转为独立模组" : "已改回共用",
            isolated
                ? $"「{version}」以后用自己的 mods / config" + (seed ? ",已复制一份现有模组" : "")
                : $"「{version}」与实例里其它版本共用 mods",
            ToastKind.Success);
        await LoadAsync();
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
        string s = type.Length > 0 ? type + " " + v.Mc : v.Mc;
        if (LoaderNames.Contains(v.Loader))
        {
            string ver = LoaderVerOf(v.Id, v.Loader);
            s = s + "," + v.Loader + (ver.Length > 0 ? " " + ver : "");
        }
        return s;
    }

    private static string LoaderVerOf(string id, string loader)
    {
        int i = id.IndexOf(loader, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        string s = id[(i + loader.Length)..].Trim(' ', '-', '_', ':');
        return s.Length == 0 || s.Length > 24 ? "" : s;
    }

    private static string InferType(string id)
    {
        string t = id.ToLowerInvariant();
        if (t.StartsWith("rd-") || t.StartsWith("a1.") || t.StartsWith("c0.") || t.StartsWith("inf-") || t.StartsWith("alpha"))
            return "old_alpha";
        if (t.StartsWith("b1.")) return "old_beta";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+w\d+[a-z]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || t.Contains("-pre") || t.Contains("-rc"))
            return "snapshot";
        return "release";
    }

    private string GroupKeyOf(Ver v)
    {
        if (v.Hidden) return "hidden";
        if (IsFav(v.Id)) return "fav";
        return v.Loader switch
        {
            "Fabric" => "fabric",
            "Forge" => "forge",
            "Quilt" => "quilt",
            "NeoForge" => "neoforge",
            "OptiFine" => "optifine",
            "LiteLoader" => "liteloader",
            _ => v.Type == "release" ? "vanilla" : "uncommon",
        };
    }

    private bool IsFav(string id) => UiPrefs.Load().IsFavorite(_folder, id);

    private void ToggleFav(Ver v)
    {
        var prefs = UiPrefs.Load();
        prefs.ToggleFavorite(_folder, v.Id);
        prefs.Save();
        RenderGroups();
    }

    // ───────────────────────────── 动作 ─────────────────────────────

    private async Task LaunchVerAsync(string inst, string version)
    {
        await Api.TryCallAsync<object>("save_settings", null, new { data = new { default_instance = inst } });
        _defaultInstance = inst;
        if (this.GetVisualRoot() is MainWindow win)
        {
            win.PreferLaunch(inst, version);
            win.Navigate("launch");
        }
    }

    private async Task UninstallAsync(string inst, string version)
    {
        if (!await Dlg.Confirm("卸载版本",
                $"确定卸载「{version}」?该版本目录下的独立模组与存档会一并删除。", "卸载", "取消", danger: true))
            return;
        await Api.CallAsync<object>("uninstall_version", new { spec = inst + " / " + version });
        await LoadAsync();
        Toast("已卸载", version, ToastKind.Success);
    }

    private void VerMenu(Control anchor, Ver v)
    {
        string inst = _folder;
        string id = v.Id;
        var menu = new List<MenuItemFx>
        {
            new MenuItemFx("打开版本文件夹", async () => { await Api.CallAsync<string>("open_version_folder", new { instance = inst, version = id, which = "root" }); }),
            new MenuItemFx("打开游戏文件夹", async () => { await Api.CallAsync<string>("open_version_folder", new { instance = inst, version = id, which = "game" }); }),
            new MenuItemFx("存档管理...", () => SavesDialog.ShowAsync(inst, id)),
            new MenuItemFx("重命名", async () =>
            {
                string? name = await Dlg.Prompt("重命名版本", "新版本 ID", id);
                if (string.IsNullOrWhiteSpace(name) || name == id) return;
                await Api.CallAsync<string>("rename_version", new { instance = inst, version = id, new_id = name });
                await LoadAsync();
            }),
            new MenuItemFx("复制一份", async () =>
            {
                string? name = await Dlg.Prompt("复制版本", "新版本 ID", id + "-copy");
                if (string.IsNullOrWhiteSpace(name)) return;
                await Api.CallAsync<string>("copy_version", new { instance = inst, version = id, new_id = name });
                await LoadAsync();
            }),
            new MenuItemFx(v.Hidden ? "取消隐藏" : "在列表中隐藏", async () =>
            {
                await Api.CallAsync<PyDict>("hide_version", new { instance = inst, version = id, hidden = !v.Hidden });
                await LoadAsync();
                Toast(v.Hidden ? "已取消隐藏" : "已隐藏", id, ToastKind.Success);
            }),
            new MenuItemFx(Shortcut.Exists(inst, id) ? "删除桌面快捷方式" : "创建桌面快捷方式",
                () => ShortcutAsync(inst, id)),
            new MenuItemFx("导出启动脚本", async () =>
            {
                string? dest = await Dlg.SaveFile("导出启动脚本", $"launch-{inst}-{id}.sh");
                if (dest == null) return;
                await Api.CallAsync<string>("export_launch_script", new { instance = inst, version = id, dest });
                ToastHost.Show("已导出", dest, ToastKind.Success);
            }),
            new MenuItemFx("修复(补全缺失文件)", async () =>
            {
                await Api.CallAsync<string>("repair_version", new { instance = inst, version = id });
                ToastHost.Show("已开始修复", id);
                if (this.GetVisualRoot() is MainWindow w) w.Navigate("tasks");
            }),
            new MenuItemFx("卸载这个版本", () => UninstallAsync(inst, id)),
        };
        ContextMenuFx.Show(anchor, menu, 7);
    }

    private void FolderMenu(Control anchor, string instance)
    {
        var menu = new List<MenuItemFx>
        {
            new MenuItemFx("打开实例文件夹", async () => { await Api.CallAsync<string>("open_instance_folder", new { name = instance }); }),
            new MenuItemFx("设为默认实例", async () =>
            {
                _folder = instance;
                await LoadAsync();
                Toast("默认实例", $"之后的启动将默认使用「{instance}」", ToastKind.Success);
            }),
            new MenuItemFx("选择 Java...", () => PickJavaAsync(instance)),
            new MenuItemFx("管理模组", () => ModsDialog.ShowAsync(instance)),
            new MenuItemFx("存档管理...", () => SavesDialog.ShowAsync(instance, "")),
            new MenuItemFx("导出为整合包", async () =>
            {
                string? dest = await Dlg.SaveFile("导出整合包", instance + ".mrpack");
                if (dest == null) return;
                await Api.CallAsync<string>("export_modpack", new { instance, dest });
                ToastHost.Show("已开始导出", instance);
                if (this.GetVisualRoot() is MainWindow w) w.Navigate("tasks");
            }),
            new MenuItemFx("重命名...", async () =>
            {
                string? name = await Dlg.Prompt("重命名实例", "新名称", instance);
                if (string.IsNullOrWhiteSpace(name) || name == instance) return;
                await Api.CallAsync<object>("rename_instance", new { name = instance, new_name = name });
                await LoadAsync();
            }),
            new MenuItemFx("删除实例", async () =>
            {
                if (!await Dlg.Confirm("删除实例",
                        $"「{instance}」下的版本、模组、存档都会被删除,且不可恢复。", "永久删除", "取消", danger: true))
                    return;
                await Api.CallAsync<object>("delete_instance", new { name = instance });
                await LoadAsync();
                Toast("已删除", instance, ToastKind.Success);
            }),
        };
        ContextMenuFx.Show(anchor, menu, 7);
    }

    /// <summary>
    /// 创建 / 删除「双击直接启动该版本」的桌面快捷方式(M4-H P3.5)。
    /// 目标参数由 Core 的 Shortcut.LaunchArgs 产出(`-i &lt;实例&gt; launch &lt;版本&gt;`),
    /// 由 TipCL.App 的 CLI 入口消费 —— 双击真的会把那个版本启动起来,不只开启动器。
    /// </summary>
    private async Task ShortcutAsync(string inst, string ver)
    {
        string name = Shortcut.ShortcutName(inst, ver);
        if (Shortcut.Exists(inst, ver))
        {
            if (!await Dlg.Confirm("删除桌面快捷方式", $"确定删除「{name}」?", "删除", "取消", danger: true)) return;
            var r = await Api.CallAsync<PyDict>("delete_desktop_shortcut", new { name });
            if (r != null && Py.Truthy(r.Get("ok")))
                Toast("已删除", Py.S(r.Get("path")), ToastKind.Success);
            else
                Toast("没找到快捷方式", name, ToastKind.Warning);
            return;
        }
        try
        {
            string path = await Api.CallAsync<string>("create_desktop_shortcut", new { instance = inst, version = ver }) ?? "";
            await Dlg.Alert("已创建", $"桌面快捷方式:\n{path}\n\n双击即可直接启动该版本。");
        }
        catch (ShortcutError ex)
        {
            Toast("创建失败", ex.Message, ToastKind.Warning);
        }
    }

    private async Task PickJavaAsync(string instance)
    {
        var rawOpts = await Api.TryCallAsync<List<object?>>("java_combo_options", null, new { instance, scan_system = true }) ?? new();
        var opts = rawOpts.OfType<PyDict>().ToList();
        if (opts.Count == 0)
        {
            Toast("没找到 Java", "去「设置 → Java」下载一个", ToastKind.Warning);
            return;
        }
        var labels = opts.Select(o => Py.S(o.Get("label"))).ToList();
        string selected = await Api.TryCallAsync<string>("java_combo_label_for", "自动选择", new { instance, options = opts }) ?? "自动选择";
        var combo = UiClass.Combo(labels, selected, 320);
        string current = await Api.TryCallAsync<string>("instance_java_label", "", new { name = instance }) ?? "";
        var body = UiClass.V(8,
            UiClass.Muted("留「自动选择」由启动器按版本匹配"),
            current.Length > 0 ? UiClass.Small("当前记录:" + current) : null,
            combo);
        if (!await Dlg.Ask($"为「{instance}」选择 Java", body, "保存")) return;
        int i = Math.Max(0, combo.SelectedIndex);
        await Api.CallAsync<string>("set_instance_java", new { name = instance, java = i < opts.Count ? Py.S(opts[i].Get("value")) : "自动选择" });
        await LoadAsync();
    }

    // ───────────────────────────── 文件夹/实例操作 ─────────────────────────────

    private async Task AddFolderAsync()
    {
        string? path = await Dlg.PickFolder("选择一个已有的游戏文件夹(含 versions 的 .minecraft)");
        if (string.IsNullOrWhiteSpace(path)) return;
        string fileName = Path.GetFileName(path.TrimEnd('\\', '/'));
        string? name = await Dlg.Prompt("导入为文件夹", "显示名称", fileName);
        if (string.IsNullOrWhiteSpace(name)) return;
        await Api.CallAsync<PyDict>("import_existing_folder", new { path, name });
        ToastHost.Show("已开始导入", "导入进度看「下载任务」", ToastKind.Success);
        await LoadAsync();
        if (this.GetVisualRoot() is MainWindow w) w.Navigate("tasks");
    }

    private async Task ImportPackAsync()
    {
        string? path = await Dlg.PickFile("导入整合包", "*.mrpack", "*.zip");
        if (string.IsNullOrWhiteSpace(path)) return;
        await Api.CallAsync<string>("install_modpack", new
        {
            name = Path.GetFileName(path),
            source = "本地",
            extra = new { path, instance = _folder, name = Path.GetFileName(path), source = "本地" },
        });
        ToastHost.Show("已加入队列", Path.GetFileName(path), ToastKind.Success);
        if (this.GetVisualRoot() is MainWindow w) w.Navigate("tasks");
    }

    private async Task CreateAsync()
    {
        string? name = await Dlg.Prompt("新建实例", "实例名称(会创建独立的 .minecraft)", "", "例如:1.20.1 生存");
        if (string.IsNullOrWhiteSpace(name)) return;
        await Api.CallAsync<object>("create_instance", new { name });
        await LoadAsync();
        Toast("已创建", name, ToastKind.Success);
    }

    private async Task MigrateAsync()
    {
        bool found = await Api.TryCallAsync<bool>("detect_official_launcher", false);
        if (!found)
        {
            Toast("没找到官方启动器", "未检测到 .minecraft 目录", ToastKind.Warning);
            return;
        }
        string dir = await Api.TryCallAsync<string>("official_launcher_dir", "") ?? "";
        var versions = await Api.TryCallAsync<List<string>>("scan_official_versions", null) ?? new();
        var target = UiClass.Input("default", _folder.Length > 0 ? _folder : "default");
        var body = UiClass.V(8,
            UiClass.Muted($"检测到:{dir}"),
            UiClass.Muted($"可导入 {versions.Count} 个版本:{string.Join("、", versions.Take(8))}" + (versions.Count > 8 ? " ..." : "")),
            UiClass.Txt("导入到实例", 12, fg: "B.InkMuted"),
            target);
        if (!await Dlg.Ask("导入官方启动器", body, "开始导入")) return;
        await Api.CallAsync<string>("migrate_official_launcher", new { instance = target.Text?.Trim() ?? "default" });
        Toast("已开始导入", "进度看下载任务");
        if (this.GetVisualRoot() is MainWindow w) w.Navigate("tasks");
    }

    // ───────────────────────────── 小工具 ─────────────────────────────

    private static bool HitButton(Control? source)
    {
        while (source != null)
        {
            if (source is Button || source is ToggleButton || source is ComboBox) return true;
            source = source.Parent as Control;
        }
        return false;
    }

    private async void Run(Func<Task> work, string? errTitle = null)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            if (errTitle != null) ToastHost.Show(errTitle, ex.Message, ToastKind.Warning);
            else AppLog.Warn("版本管理操作失败: " + ex.Message);
        }
    }

    internal static void Touch() => _dummyKeepAwake = !_dummyKeepAwake;
}
