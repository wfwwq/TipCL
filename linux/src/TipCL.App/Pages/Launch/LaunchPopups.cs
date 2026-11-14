using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Shell;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core;

namespace TipCL.App.Pages.Launch;

// 启动页浮层（移植自 Windows 版 Pages/LaunchPage.Popups.cs，Avalonia Popup 重写）：
// 实例切换（标题行下方，当前实例展开版本子行，选中即启动）/ 启动配置 / 公告。
public sealed partial class LaunchPage
{
    private Popup? _configPopup;

    private Popup? _switchPopup;

    private Popup? _newsPopup;

    private StackPanel? _switchList;

    private TextBox? _switchSearch;

    private Control? _configBody;

    // 统一外壳：圆角 12、边 1、投影；MaxHeight 在打开时按窗口高度 86% 收口
    private static Border PopShell(Control content, double width, double maxHeight, Thickness margin)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Width = width,
            MaxHeight = maxHeight,
            Margin = margin,
            Child = content,
        };
        b.Dyn(Border.BackgroundProperty, "B.Paper");
        b.Dyn(Border.BorderBrushProperty, "B.Line");
        b.BoxShadow = new BoxShadows(BoxShadow.Parse("0 8 22 0 #28000000"));
        return b;
    }

    /// <summary>
    /// 建浮层。**必须把 Popup 挂进页面树**,否则浮层里的控件一个都画不出来。
    ///
    /// 机制:Avalonia 11 的隐式 ControlTheme(FluentTheme 里的 {x:Type TextBox} / {x:Type ComboBox} /
    /// {x:Type Slider} 等)走「控件 → 逻辑/可视父链 → TopLevel → Application.Styles」这条宿主链查找
    /// (<c>TryFindResource</c>,连 IStyleHost.Styles 一起查)。没挂树的 Popup 拿不到宿主,它的 Child
    /// 继承不到任何样式:TextBox/ComboBox/Slider 模板为空、量出 0×0、整行空白;而 TextBlock 不需要模板、
    /// <c>Ui.Btn/Switch/Multi</c> 是构造时直接赋了 ControlTheme 对象、<c>Dyn()</c> 走 Application 级
    /// 资源观察绑定 —— 这三类照常显示。**「显式主题能画、隐式主题画不出」正是这条链断掉的指纹**,
    /// 与真机浮层截图逐条吻合(逐像素扫描:只剩 TextBlock 与显式主题按钮)。
    ///
    /// 挂树后 Popup 由主窗口的 OverlayLayer 承载(探针里 Child 的 <c>GetVisualRoot()</c> == MainWindow),
    /// 宿主链完整。回归探针:<c>SMOKE_PROBE=popup-theme</c>(Session/SmokeRunner.cs)。
    /// Popup 自身在布局里不占位(浮层内容渲染在 OverlayLayer,不在它的布局槽里):挂树前后
    /// launch 页的 smoke.png 逐位一致,对启动页布局没有影响。
    /// </summary>
    private Popup MakePopup(Control target, PlacementMode mode, Border shell)
    {
        var p = new Popup
        {
            PlacementTarget = target,
            Placement = mode,
            IsLightDismissEnabled = true,
            Child = shell,
        };
        if (Content is Panel host)
        {
            if (!host.Children.Contains(p))
                host.Children.Add(p);
        }
        else
        {
            // 不该发生(BuildHero 把内容设成根 Grid):没挂树 = 浮层里靠隐式主题的控件又画不出来
            AppLog.Warn("启动页浮层没能挂进页面树,浮层里的输入控件会渲染不出来");
        }
        return p;
    }

    // 打开时按窗口高度收口浮层总高（对应 Windows 版 WindowPctConverter(0.86)）
    private void BoundShell(Popup p)
    {
        if (p.Child is Border shell && TopLevel.GetTopLevel(this) is { } tl)
            shell.MaxHeight = Math.Max(240, tl.ClientSize.Height * 0.86);
    }

    // ————— 配置浮层 —————

    private void OpenConfigPopup()
    {
        if (_configBody == null)
            // BuildConfig 返回的是 StackPanel(不是 Border):这里原来强转成 Border,
            // 一点齿轮就 InvalidCastException,整个「启动配置」浮层(含分辨率)都打不开
            _configBody = BuildConfig();
        if (_configPopup == null)
        {
            var head = UiClass.Txt("启动配置", 15, bold: true);
            var close = new Button
            {
                Theme = UiClass.Theme("Btn.Icon"),
                VerticalAlignment = VerticalAlignment.Center,
                Content = IconLib.Make("Close", "B.Ink", 13),
            };
            ToolTip.SetTip(close, "关闭");
            close.Click += (_, _) =>
            {
                if (_configPopup != null)
                    _configPopup.IsOpen = false;
            };
            var g = UiClass.G(null, "*,Auto");
            g.Add(head, 0, 0);
            g.Add(close, 0, 1);
            var body = UiClass.V(10, g, _configBody);
            _configPopup = MakePopup(
                _configPill ?? (Control?)_titleRow ?? this,
                PlacementMode.Bottom,
                PopShell(body, 360, 440, new Thickness(0, 6, 14, 14)));
        }
        BoundShell(_configPopup);
        _configPopup.IsOpen = true;
    }

    // 常用的只有实例/版本/用户；内存、直连收进「更多设置」，默认折叠
    // 排列照 Windows 版 BuildConfig(1504):Java → 内存 → 分辨率 → 直连
    private Control BuildConfig()
    {
        var memRow = UiClass.G(null, "*,Auto");
        memRow.Add(_mem, 0, 0);
        memRow.Add(_memLbl, 0, 1);
        _mem.VerticalAlignment = VerticalAlignment.Center;
        _memLbl.Margin = new Thickness(10, 0, 0, 0);
        _memLbl.VerticalAlignment = VerticalAlignment.Center;

        var times = UiClass.Txt("×");
        times.VerticalAlignment = VerticalAlignment.Center;
        var resRow = UiClass.H(6, _wBox, times, _hBox);

        var advanced = UiClass.V(9,
            UiClass.Field("Java", _java, labelWidth: 58),
            UiClass.Field("内存", memRow, labelWidth: 58),
            UiClass.Field("分辨率", resRow, labelWidth: 58),
            UiClass.Field("直连", _server, labelWidth: 58));
        advanced.IsVisible = false;
        Button? toggle = null;
        toggle = UiClass.Btn("更多设置", BtnKind.Ghost, (_, _) =>
        {
            bool open = !advanced.IsVisible;
            advanced.IsVisible = open;
            toggle!.Content = UiClass.Txt(open ? "收起" : "更多设置");
        });
        toggle.HorizontalAlignment = HorizontalAlignment.Left;

        var msLogin = UiClass.Btn("微软登录", BtnKind.Soft, (_, _) =>
            Toast("微软登录", "账号能力将在 M3 接入", ToastKind.Info));
        var authlib = UiClass.Btn("皮肤站", BtnKind.Chip, (_, _) =>
            Toast("皮肤站登录", "账号能力将在 M3 接入", ToastKind.Info));

        return UiClass.V(9,
            UiClass.Field("实例", _inst, labelWidth: 58),
            UiClass.Field("版本", _ver, labelWidth: 58),
            UiClass.Field("用户", _user, labelWidth: 58),
            toggle,
            advanced,
            UiClass.Sep(),
            UiClass.H(7, msLogin, authlib));
    }

    // ————— 实例切换浮层 —————

    private void OpenSwitchPopup()
    {
        if (_switchPopup == null)
        {
            _switchSearch = UiClass.Input("搜索实例…");
            _switchSearch.Height = 30;
            _switchSearch.Padding = new Thickness(6, 5, 6, 5);
            _switchSearch.TextChanged += (_, _) => RenderSwitchRows();
            var search = UiClass.G(null, "Auto,*");
            var searchIcon = IconLib.Make("Search", "B.InkFaint", 13);
            searchIcon.Margin = new Thickness(2, 0, 6, 0);
            searchIcon.VerticalAlignment = VerticalAlignment.Center;
            search.Add(searchIcon, 0, 0);
            search.Add(_switchSearch, 0, 1);
            _switchList = new StackPanel();
            var listScroll = new ScrollViewer
            {
                Content = _switchList,
                Padding = new Thickness(0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 300,
            };
            var openLib = UiClass.Btn("打开实例库", BtnKind.Ghost, (_, _) =>
            {
                _switchPopup!.IsOpen = false;
                (TopLevel.GetTopLevel(this) as MainWindow)?.Navigate("library");
            });
            openLib.FontSize = 12;
            openLib.HorizontalAlignment = HorizontalAlignment.Left;
            // 照 Windows ManageLink:浮层里直接进「版本管理」
            var manage = UiClass.Btn("管理", BtnKind.Ghost, (_, _) =>
            {
                _switchPopup!.IsOpen = false;
                (TopLevel.GetTopLevel(this) as MainWindow)?.Navigate("instance");
            });
            manage.FontSize = 12;
            manage.HorizontalAlignment = HorizontalAlignment.Left;
            var footer = UiClass.H(8, openLib, manage);
            _switchPopup = MakePopup(
                _titleRow ?? (Control)this,
                PlacementMode.Bottom,
                PopShell(UiClass.V(8, search, UiClass.Sep(), listScroll, UiClass.Sep(), footer), 400, 430, new Thickness(0, 6, 14, 14)));
        }
        _switchSearch!.Text = "";
        RenderSwitchRows();
        BoundShell(_switchPopup);
        _switchPopup.IsOpen = true;
    }

    private void RenderSwitchRows()
    {
        if (_switchList == null)
            return;
        _switchList.Children.Clear();
        string q = _switchSearch?.Text?.Trim() ?? "";
        string current = CurInst();
        string curVer = CurVer();
        var vers = _versions;
        int shown = 0;
        foreach (var i in _instances)
        {
            bool isCurrent = i.Name == current;
            bool nameHit = q.Length == 0 || i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            // 当前实例行下方展开版本子列表（数据 = ReloadVersionsAsync 灌好的 _versions，不另调后端）
            var subs = new List<string>();
            if (isCurrent)
            {
                foreach (string v in vers)
                    if (nameHit || q.Length == 0 || v.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        subs.Add(v);
            }
            if (!nameHit && subs.Count == 0)
                continue;
            shown++;
            var nameTxt = UiClass.Txt(i.Name, 13.5, bold: isCurrent, "B.Ink");
            nameTxt.TextTrimming = TextTrimming.CharacterEllipsis;
            var subTxt = UiClass.Txt(InstanceSub(i), 11, fg: "B.InkFaint");
            subTxt.TextTrimming = TextTrimming.CharacterEllipsis;
            var subParts = UiClass.V(1, nameTxt, subTxt);
            Control right = isCurrent ? CurrentMark() : ManageLink();
            right.VerticalAlignment = VerticalAlignment.Center;
            var row = UiClass.G(null, "*,Auto");
            row.Add(subParts, 0, 0);
            row.Add(right, 0, 1);
            right.Margin = new Thickness(8, 0, 0, 0);
            var wrap = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 7),
                Child = row,
            };
            if (!isCurrent)
            {
                wrap.Cursor = new Cursor(StandardCursorType.Hand);
                wrap.PointerEntered += (_, _) => wrap.Dyn(Border.BackgroundProperty, "B.Hover");
                wrap.PointerExited += (_, _) => wrap.Background = Brushes.Transparent;
                string instName = i.Name;
                wrap.PointerReleased += (_, e) =>
                {
                    if (_switchPopup != null)
                        _switchPopup.IsOpen = false;
                    if (CurInst() != instName)
                    {
                        _syncing = true;
                        _inst.SelectedItem = instName;
                        _syncing = false;
                        _ = ReloadVersionsAsync();
                        _ = CountModsAsync();
                    }
                    e.Handled = true;
                };
            }
            _switchList.Children.Add(wrap);
            if (!isCurrent)
                continue;
            if (vers.Count == 0)
            {
                var hint = UiClass.Muted("这个实例还没有版本，先去「下载」装一个");
                hint.Margin = new Thickness(24, 4, 4, 8);
                _switchList.Children.Add(hint);
                continue;
            }
            foreach (string v in subs)
                _switchList.Children.Add(VerRow(v, v == curVer));
        }
        if (shown == 0)
        {
            var none = UiClass.Muted("没有匹配的实例");
            none.Margin = new Thickness(4, 6, 4, 6);
            _switchList.Children.Add(none);
        }
    }

    // 实例切换浮层里的版本子行：行高 46，选中 = 主题色 + 勾；点击即选中并启动
    private Border VerRow(string v, bool sel)
    {
        var g = UiClass.G(null, "*,Auto");
        var txt = UiClass.Txt(v, 13, bold: sel, sel ? "B.AccentText" : "B.Ink");
        txt.TextTrimming = TextTrimming.CharacterEllipsis;
        txt.VerticalAlignment = VerticalAlignment.Center;
        g.Add(txt, 0, 0);
        if (sel)
        {
            var check = IconLib.Make("Check", "B.Accent", 13);
            check.VerticalAlignment = VerticalAlignment.Center;
            g.Add(check, 0, 1);
        }
        var row = new Border
        {
            MinHeight = 46,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(26, 4, 12, 4),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = g,
        };
        row.PointerEntered += (_, _) => row.Dyn(Border.BackgroundProperty, "B.RowHover");
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, e) =>
        {
            if (_switchPopup != null)
                _switchPopup.IsOpen = false;
            if (CurVer() != v)
            {
                _syncing = true;
                _ver.SelectedItem = v;
                _syncing = false;
                SyncHero();
            }
            // 选中即启动（M2-B1 任务书行为）
            _ = LaunchAsync();
            e.Handled = true;
        };
        return row;
    }

    private static StackPanel CurrentMark()
    {
        var dot = new Ellipse { Width = 7, Height = 7 };
        dot.Dyn(Shape.FillProperty, "B.Accent");
        return UiClass.H(5, dot, UiClass.Txt("当前", 11.5, fg: "B.InkMuted"));
    }

    private Button ManageLink()
    {
        var b = UiClass.Btn("管理", BtnKind.Ghost, (_, _) =>
        {
            if (_switchPopup != null)
                _switchPopup.IsOpen = false;
            (TopLevel.GetTopLevel(this) as MainWindow)?.Navigate("library");
        });
        b.FontSize = 11.5;
        b.Padding = new Thickness(8, 2, 8, 3);
        return b;
    }

    private static string InstanceSub(InstRow i)
    {
        var parts = new List<string>();
        if (i.Versions.Count > 0)
            parts.Add($"{i.Versions.Count} 个版本");
        parts.Add(i.Path.Length > 0 ? i.Path : i.Name);
        return string.Join(" · ", parts);
    }

    // ————— 公告浮层 —————

    private void OpenNewsPopup()
    {
        if (_newsPopup == null)
        {
            _newsPopup = MakePopup(
                _newsCard ?? (Control)this,
                PlacementMode.Top,
                PopShell(new ScrollViewer
                {
                    Content = _newsHost,
                    Padding = new Thickness(0),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                }, 420, 360, new Thickness(0, 14, 14, 6)));
        }
        BoundShell(_newsPopup);
        _newsPopup.IsOpen = true;
    }

    // ————— 冒烟探针入口(SMOKE_PROBE=popup-theme 专用,产品流程不调用) —————
    // 让探针直接开到**真实**的「启动配置」浮层,而不是另造一个仿制品:浮层里出事的正是
    // 这批靠隐式 ControlTheme 取模板的控件(TextBox/ComboBox/Slider),换仿制品就验不到真路径。

    /// <summary>探针:打开真实「启动配置」浮层并返回它。</summary>
    internal Popup OpenConfigPopupForProbe()
    {
        OpenConfigPopup();
        return _configPopup!;
    }

    /// <summary>探针:配置浮层里那些「没有显式 Theme」的数据控件(界面名 + 控件)。</summary>
    internal (string Label, Control Control)[] ConfigProbeControls() => new (string, Control)[]
    {
        ("实例 ComboBox", _inst),
        ("版本 ComboBox", _ver),
        ("用户 ComboBox", _user),
        ("直连 TextBox", _server),
        ("Java ComboBox", _java),
        ("内存 Slider", _mem),
        ("内存数值 TextBlock", _memLbl),
        ("分辨率宽 TextBox", _wBox),
        ("分辨率高 TextBox", _hBox),
    };
}
