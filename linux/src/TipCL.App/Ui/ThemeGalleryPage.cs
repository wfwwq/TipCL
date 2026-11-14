using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace TipCL.App.Ui;

using static TipCL.App.Ui.Ui;

/// <summary>
/// 主题样张页（M1 临时页）：展示全部设计令牌与控件样式的明暗两态。
/// A5 冒烟截图用：把本页设为窗口内容后跑 --smoke 即可。
/// </summary>
public sealed class ThemeGalleryPage : UserControl
{
    private static readonly string[] SwatchKeys =
    {
        "B.Accent", "B.AccentStrong", "B.AccentText", "B.AccentLite", "B.AccentSoft", "B.AccentSoft2",
        "B.AccentBorder", "B.OnAccent", "B.Canvas", "B.Paper", "B.Paper2", "B.Side", "B.Chrome",
        "B.Line", "B.LineSoft", "B.Hover", "B.Ink", "B.InkSoft", "B.InkMuted", "B.InkFaint",
        "B.Danger", "B.DangerSoft", "B.Warn", "B.WarnSoft", "B.Info", "B.InfoSoft", "B.Ok", "B.Card",
        "B.RowLine", "B.RowHover", "B.SegRail", "B.SegOn", "B.CaptionInk", "B.CaptionHot"
    };

    public ThemeGalleryPage()
    {
        var content = V(14,
            Header(),
            TextSection(),
            ButtonSection(),
            InputSection(),
            PickSection(),
            ContainerSection(),
            ChipSection(),
            SwatchSection());
        content.HorizontalAlignment = HorizontalAlignment.Center;
        var root = new ScrollViewer { Content = content };
        root.Padding = new Thickness(0);
        Content = root;
    }

    private static StackPanel Header()
    {
        var toggle = Ui.Btn("切换明暗主题", BtnKind.Normal, (_, _) => Ui.ApplyTheme(!Ui.IsDark));
        toggle.VerticalAlignment = VerticalAlignment.Center;
        return H(10, Ui.PageTitle("主题样张 Theme Gallery"), Ui.Spring(), toggle);
    }

    private static Control TextSection() =>
        Card(V(6,
            Ui.Mega("24 大标题 T.Mega"),
            Ui.H1("21 页面标题 T.H1 / T.PageTitle"),
            Ui.H2("17 二级标题 T.H2"),
            Ui.H3("14 三级标题 T.H3"),
            Ui.Txt("13 正文默认字号"),
            Ui.GroupTitle("12.5 分组标题 T.GroupTitle"),
            Ui.Muted("12 次要说明 T.Muted：换行文案给长句用"),
            Ui.Small("11 小字 T.Small"),
            Ui.Mono("11.5 等宽 T.Mono · Cascadia Mono / monospace")),
            padding: 14);

    private static Control ButtonSection() => Card(V(10,
        Ui.H3("按钮"),
        H(8,
            Ui.Btn("主要操作", BtnKind.Primary),
            Ui.Btn("柔和", BtnKind.Soft),
            Ui.Btn("普通", BtnKind.Normal),
            Ui.Btn("管理", BtnKind.Ghost),
            Ui.Btn("删除", BtnKind.Danger),
            Ui.Btn("筛选", BtnKind.Chip),
            Ui.IconBtn("\uE712")),
        H(8,
            Disabled(Ui.Btn("禁用态", BtnKind.Primary)),
            Disabled(Ui.Btn("禁用态", BtnKind.Normal)),
            new Border
            {
                Child = new Panel(),
                Background = Brushes.Transparent
            }),
        H(8,
            (Control)SegRail("全部", "官方", "镜像"),
            (Control)H(2, TopTab("启动", true), TopTab("实例库", false), TopTab("下载", false)))),
        padding: 14);

    private static Control InputSection() => Card(V(10,
        Ui.H3("输入"),
        Ui.Field("用户名", Ui.Input("邮箱或用户名")),
        Ui.Field("密码", Ui.Pw()),
        Ui.Field("备注", Ui.Multi("选填…")),
        Ui.Field("日志", Ui.LogBox(), hint: "只读等宽", labelWidth: 80)),
        padding: 14);

    private static Control PickSection() => Card(V(10,
        Ui.H3("选择"),
        Ui.Field("版本", Ui.Combo(new[] { "1.21.4", "1.20.6", "1.20.1" }, selected: "1.21.4"), hint: "下拉框"),
        Ui.Field("加载器", Ui.Combo(placeholder: "选择加载器"), hint: "空下拉占位"),
        H(16, Ui.Check("自动选择内存", false), Ui.Check("保持登录", true),
            Ui.Switch(true), Ui.Switch(false))),
        padding: 14);

    private static Control ContainerSection() => Card(V(10,
        Ui.Section("卡片与分区", "Section + Card + RowCard + Pane"),
        Ui.Card(V(8,
            Ui.H3("卡片 Card · 圆角 12"),
            Ui.Muted("内边距 14，白底无边框"),
            new ProgressBar { Value = 62, Maximum = 100 },
            Ui.Small("进度条 Prog 6px"))),
        Ui.RowCard(H(10, Ui.Glyph("\uE7C9"), V(2, Ui.Txt("可点击行卡 RowCard"), Ui.Small("圆角 8 · 1px 描边")),
            Ui.Spring(), Ui.Tag("推荐"))),
        Ui.RowCard(H(10, Ui.Glyph("\uE8FB"), V(2, Ui.Txt("再来一张行卡"), Ui.Small("整行可点")),
            Ui.Spring(), Ui.Badge("3"))),
        Ui.Pane(V(4, Ui.Txt("Pane 内嵌面板"), Ui.Small("B.Paper2 底 · 圆角 8 · 内边距 10"))),
        Ui.Sep(),
        Ui.Field("滑杆", Ui.Sld(0, 100, 40), hint: "滑杆暂用默认主题", labelWidth: 80)),
        padding: 14);

    private static Control ChipSection() => Card(V(10,
        Ui.H3("标签与徽章"),
        H(8,
            Ui.Tag("正式版"),
            Ui.Tag("快照", "B.Info", "B.InfoSoft"),
            Ui.Tag("警告", "B.Warn", "B.WarnSoft"),
            Ui.Tag("危险", "B.Danger", "B.DangerSoft"),
            Ui.Tag("成功", "B.Ok", "B.AccentSoft"),
            Ui.Badge("12"),
            Ui.Badge("99+"))),
        padding: 14);

    private static Control SwatchSection()
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var key in SwatchKeys)
        {
            var sw = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 0),
                BorderBrush = Ui.Brush("B.Line"),
                BorderThickness = new Thickness(1)
            };
            sw.Dyn(Border.BackgroundProperty, key);
            wrap.Children.Add(H(0,
                sw,
                new TextBlock { Text = key, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) }
                    .Dyn(TextBlock.ForegroundProperty, "B.InkMuted")));
        }
        return Card(V(10, Ui.H3("色板（B.* 画刷令牌，随主题切换）"), wrap), padding: 14);
    }

    private static Control Disabled(Control c) { c.IsEnabled = false; return c; }

    private static Border SegRail(params string[] items)
    {
        var rail = new Border
        {
            Theme = Ui.Theme("Seg.Rail"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < items.Length; i++)
        {
            var tb = new ToggleButton
            {
                Theme = Ui.Theme("Seg.Item"),
                Content = items[i],
                IsChecked = i == 0
            };
            panel.Children.Add(tb);
        }
        rail.Child = panel;
        return rail;
    }

    private static ToggleButton TopTab(string text, bool isChecked) => new()
    {
        Theme = Ui.Theme("Tab.Top"),
        Content = text,
        IsChecked = isChecked
    };
}
