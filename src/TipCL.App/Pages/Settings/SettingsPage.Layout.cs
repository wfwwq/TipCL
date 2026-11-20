// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/SettingsLayoutPage.cs(1670 行,冻结件 @0a304a7)。
// 本文件是它的 Avalonia 移植:设置页第 8 个内层视图「布局」。
//
// [LAYOUT-V3-D 2026-11-20] 按「自定义布局 v3 效果图」(tmp/layout3_noimg.html 的 secB 段)重排:
//   从「配置清单」改成「按要做的事分组」—— 页头 / 外观 / 导航 / 页面 / 方案 / 高级(默认折叠)六块。
//   功能语义一条不丢:密度三档、页签顺序与显隐、方案 CRUD 与导入导出、分栏复位、JSON 校验与应用照旧,
//   只改呈现与交互。v3 以效果图为准**覆盖**了本文件早先的几条呈现决定(原因同时写进 progress/LAYOUT-V3-D.md):
//   T-03 「13 条分栏站点表 + 每条恢复默认」→ 一行「已调整 N 处:<名字>…双击复位」+「全部复位」。
//        13 条里有 7 条写着「没有左右分栏」,对用户是噪音;SplitCatalog 这张事实表本身一条不动
//        (PageLayoutHost.cs 仍是 13 条,LayoutSplitTests 照旧钉住它),只是设置页不再逐条展示。
//   T-04 设置页里的「自建区块」选择器移走:v3 由编辑态的「添加」抽屉负责加组件、垃圾桶负责删组件
//        (LayoutEditor 等归 lin-edit 代理)。数据层 LayoutWidgetOps 不动。
//   T-05 「标题栏:桥接」开关(禁用 + 解释)直接去掉:Linux 标题栏没有桥接控件,v3 也要求删除「桥接」说法;
//        文档里的 titlebar.bridge 字段照旧读写(存档兼容),只是界面不再提供。
//   去掉全部内部说法:文件名 ui_layout_v2.json、「标题栏:xxx」「只隐藏/显示,不改形态」、#编号、(L6)。
// 三条硬规矩照原文:
//   1) 落盘唯一入口 = LayoutStoreV2(load → 改 → Save);本文件绝不自己写布局文件(导出到用户选定路径除外);
//   2) 方案应用 = **原子替换** layout(整个 LayoutBody 换掉)再 Save,v1 由 v2 单向写通;
//   3) 构建入口 = OpenLayoutView()(壳层右键菜单「布局设置...」走这里)。
//
// 与 Windows 源的差异(只记仍然成立的):
//   T-01 WPF 的 `Ui.*` → 本树 `UiClass.*`(同 SettingsPage 既有写法);`I18n.L(x,a)` → `Py.Fmt(I18nCore.Tr(x),a)`。
//   T-02 `PageBase.Win` → `TopLevel.GetTopLevel(this)`。
//   T-06 「编辑」按钮走 LayoutShellHooks.BeginEditPage / CanEditPage(lin-edit 代理按 v3 约定签名提供,
//        见 tmp/v3_handoff.md「Linux 设置」小节);单块页面不出卡片,只在说明里点名。
//   T-07 JSON 编辑器不再常驻页面:改成独立窗口 LayoutJsonWindow(校验/应用仍是 LayoutJsonEditor)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Controls;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using UiClass = TipCL.App.Ui.Ui;
using ToastKind = TipCL.App.Common.ToastKind;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage
{
    /// <summary>「布局」视图在视图表/左导航里的下标(第 8 个)。</summary>
    internal const int LayoutView = 7;

    /// <summary>[layout-fix 第三批]「设置 → 布局」这一视图自己的区块页 id。</summary>
    internal const string LayoutViewPageId = "settings:layout";

    /// <summary>「高级」折叠态在 ui_prefs.expanded 里的键(默认折叠)。</summary>
    private const string LayoutAdvancedPrefKey = "settings.layout.advanced";

    /// <summary>「页面」默认只显示的常用几页(顺序即卡片顺序);其余点「显示全部 N 个页面」展开。</summary>
    private static readonly string[] LayoutCommonPages = { "launch", "download", "account", "settings:general", "playtime", "multiplayer" };

    /// <summary>缩略图的「出厂样子」(6 列网格:跨列数 / 高度 / 种类);只是示意,没有的页面用通用三条。</summary>
    private static readonly Dictionary<string, (int Span, double H, string Kind)[]> LayoutDefaultThumbs = new(StringComparer.Ordinal)
    {
        ["launch"] = new[] { (6, 44.0, "hero"), (3, 14.0, ""), (3, 14.0, "") },
        ["download"] = new[] { (6, 14.0, ""), (6, 52.0, "") },
        ["account"] = new[] { (2, 40.0, ""), (4, 40.0, ""), (6, 22.0, "") },
        ["settings:general"] = new[] { (6, 16.0, ""), (6, 16.0, ""), (6, 16.0, ""), (6, 16.0, "") },
        ["playtime"] = new[] { (6, 30.0, ""), (6, 38.0, "") },
        ["multiplayer"] = new[] { (6, 28.0, ""), (3, 34.0, ""), (3, 34.0, "") },
        ["tasks"] = new[] { (6, 14.0, ""), (6, 54.0, "") },
        ["java"] = new[] { (6, 20.0, ""), (6, 46.0, "") },
        ["servers"] = new[] { (6, 16.0, ""), (6, 50.0, "") },
        ["feedback"] = new[] { (4, 66.0, ""), (2, 66.0, "") },
    };

    private bool _layoutRebuilding;

    // 「显示全部 N 个页面」展开态:视图每次重建都读它,所以放实例字段
    private bool _layoutShowAllPages;

    // ————— 视图装配 —————

    /// <summary>构「布局」视图(每次切到该视图都重建,保证读到最新状态)。</summary>
    private Control BuildLayoutView()
    {
        _layoutRebuilding = true;
        try
        {
            // [layout-fix 第三批] 右侧的分组卡片是可拖动的区块 BlockHost("settings:layout")(左侧导航不动)。
            // v3:分组改成 页头 / 外观 / 导航 / 页面 / 方案 / 高级;id 尽量沿用旧的(shell / blocks / profiles),
            // 已存档的本页布局里找不到的旧 id(splits / widgets / json)由引擎忽略。
            var specs = new List<BlockSpec>
            {
                new BlockSpec { Id = "header", Title = I18nCore.Tr("标题"), Content = UiClass.V(2, PTitle(I18nCore.Tr("布局")),
                    UiClass.Muted(I18nCore.Tr("调整导航、各页面的排列，或者保存成方案。改动即时生效，随时可以恢复默认。")).Wrap().M(0, 0, 0, 4)) },
                new BlockSpec { Id = "appearance", Title = I18nCore.Tr("外观"), Content = LayoutSection(I18nCore.Tr("外观"), null, LayoutAppearanceRows()) },
                new BlockSpec { Id = "shell", Title = I18nCore.Tr("导航"), Content = LayoutNavSection() },
                new BlockSpec { Id = "blocks", Title = I18nCore.Tr("页面"), Content = LayoutPagesSection() },
                new BlockSpec { Id = "profiles", Title = I18nCore.Tr("方案"), Content = LayoutProfilesSection() },
                new BlockSpec { Id = "advanced", Title = I18nCore.Tr("高级"), Content = LayoutAdvancedSection() },
            };
            PageLayoutV2 page = new()
            {
                Mode = LayoutSchema.ModeStack,
                Gap = LayoutSchema.GapDefault,
                Pad = new double[] { 0.0, 0.0, 0.0, 0.0 },
            };
            for (int i = 0; i < specs.Count; i++)
            {
                page.Blocks.Add(new BlockLayout { Id = specs[i].Id, Span = 1, Z = i });
            }
            var host = new BlockHost(LayoutViewPageId, specs, new Thickness(0), page)
            {
                Margin = new Thickness(BodyMargin, 16, BodyMargin, 16),
                // 非编辑态收起底部托盘外壳 ⇒ 滚动视口的下沿与原 ScrollBody 同一行像素
                CollapseIdleBottom = true,
            };
            // 视图一重建就顺手把标题栏对齐一次(构造期还没有 TopLevel ⇒ 空操作)
            RefreshShell();
            return host;
        }
        finally
        {
            _layoutRebuilding = false;
        }
    }

    /// <summary>
    /// 公开的「跳到 设置 → 布局」入口(壳层右键菜单的「布局设置...」、编辑态「⋯ → 整套布局与方案…」走这里)。
    /// 与 Windows 同形:只调一次已有的 Select(LayoutView),不动任何静态初始化。
    /// </summary>
    public void OpenLayoutView()
    {
        Select(LayoutView);
    }

    /// <summary>
    /// [LAYOUT-V3-D] 跳到「设置 → 布局」并展开「高级」(编辑态「添加」抽屉底部的
    /// 「可在 设置 → 布局 → 高级 里开启」点这里)。
    /// </summary>
    public void OpenLayoutAdvanced()
    {
        WriteAdvancedOpen(true);
        Select(LayoutView);
    }

    /// <summary>重建「布局」视图;当前正显示它时同步换掉宿主内容。</summary>
    private void RefreshLayoutView()
    {
        object? current = _view?.Content;
        Control? old = _views[LayoutView];
        Control fresh = BuildLayoutView();
        _views[LayoutView] = fresh;
        if (_view != null && ReferenceEquals(current, old))
        {
            _view.Content = fresh;
        }
    }

    /// <summary>把标题栏对齐到当前布局文档(只有本页挂在窗口上时才有效)。</summary>
    private void RefreshShell()
    {
        (TopLevel.GetTopLevel(this) as TipCL.App.Shell.MainWindow)?.ApplyShellLayout();
    }

    // ————— 共用小件 —————

    // 一个小节 = 分组标题(右侧可带按钮)+ 卡片(行与行之间有通到卡片两端的分隔线,与设置页其它小节同一套视觉)
    private static Control LayoutSection(string title, Control? right, params Control[] rows)
    {
        return UiClass.V(10, LayoutGroupHead(title, right), UiClass.Card(Rows(rows)));
    }

    private static Control LayoutGroupHead(string title, Control? right)
    {
        if (right == null)
        {
            return Gt(title);
        }
        Grid head = UiClass.G(null, "*,Auto");
        head.Add(Gt(title).VCenter(), 0);
        head.Add(right.VCenter(), 0, 1);
        return head;
    }

    // 一行 = 左侧「粗体标题 + 灰色说明」,右侧操作件(效果图 .row)
    private static Control LayoutRow(string title, string? sub, Control right)
    {
        StackPanel text = UiClass.V(2, UiClass.Txt(title, 13, bold: true));
        if (!string.IsNullOrEmpty(sub))
        {
            text.Children.Add(UiClass.Small(sub).Wrap());
        }
        Grid row = UiClass.G(null, "*,Auto");
        row.Add(text.VCenter(), 0);
        right.Margin = new Thickness(16, 0, 0, 0);
        row.Add(right.VCenter(), 0, 1);
        return row;
    }

    private static void LayoutA11y(Control control, string name)
    {
        Avalonia.Automation.AutomationProperties.SetName(control, name);
    }

    // ————— 外观:区块间距三档分段按钮 —————

    private Control[] LayoutAppearanceRows()
    {
        ShellLayout shell = ShellTabs.CurrentShell();
        string current = LayoutSchema.IsKnownDensity(shell.Density) ? shell.Density : LayoutSchema.DensityStandard;
        StackPanel rail = UiClass.H(0);
        foreach (string key in new[] { LayoutSchema.DensityCompact, LayoutSchema.DensityStandard, LayoutSchema.DensityLoose })
        {
            string density = key;
            var item = new Avalonia.Controls.Primitives.ToggleButton
            {
                Theme = UiClass.Theme("Seg.Item"),
                Content = UiClass.Txt(DensityLabel(density), 13),
                IsChecked = density == current,
            };
            LayoutA11y(item, Py.Fmt(I18nCore.Tr("区块间距:{0}"), DensityLabel(density)));
            item.Click += delegate
            {
                if (_layoutRebuilding)
                {
                    return;
                }
                if (density == current)
                {
                    item.IsChecked = true;
                    return;
                }
                WriteLayout(delegate (LayoutV2 doc)
                {
                    doc.Layout.Shell.Density = density;
                }, Py.Fmt(I18nCore.Tr("间距：{0}"), DensityLabel(density)));
                // 重建视图 ⇒ 页面缩略图的间距跟着变;各页 BlockHost 经 LayoutStoreV2.LayoutChanged 自己跟上
                RefreshLayoutView();
            };
            rail.Children.Add(item);
        }
        Border seg = new Border { Theme = UiClass.Theme("Seg.Rail"), Child = rail, HorizontalAlignment = HorizontalAlignment.Right };
        return new Control[1]
        {
            LayoutRow(I18nCore.Tr("区块间距"), I18nCore.Tr("所有可以自定义的页面一起生效"), seg)
        };
    }

    private static string DensityLabel(string? density)
    {
        return density switch
        {
            LayoutSchema.DensityCompact => I18nCore.Tr("紧凑"),
            LayoutSchema.DensityLoose => I18nCore.Tr("宽松"),
            _ => I18nCore.Tr("标准")
        };
    }

    // 区块间距档位 → 像素(与 LayoutEngine 的三档一致:紧凑 6 / 标准 10 / 宽松 14),缩略图按比例缩小
    private static double DensityGap(string? density)
    {
        return density switch
        {
            LayoutSchema.DensityCompact => 6.0,
            LayoutSchema.DensityLoose => 14.0,
            _ => 10.0
        };
    }

    // ————— 导航:页签 chip(拖动排序 + 眼睛显隐)与标题栏开关 —————

    private Control LayoutNavSection()
    {
        ShellLayout shell = ShellTabs.CurrentShell();
        Button reset = UiClass.Btn(I18nCore.Tr("恢复默认"), BtnKind.Ghost, delegate
        {
            ShellTabs.SetTabs(ShellTabs.BuiltinKeys);
            PageBase.Toast(I18nCore.Tr("页签已恢复默认"), "", ToastKind.Success);
            RefreshLayoutView();
        });
        LayoutA11y(reset, I18nCore.Tr("页签恢复默认"));
        Control tabs = UiClass.V(10,
            LayoutRow(I18nCore.Tr("顶部页签"), I18nCore.Tr("拖动调整顺序，点眼睛隐藏；「设置」不能隐藏"), reset),
            LayoutTabChips(shell));

        TitlebarLayout bar = shell.Titlebar ?? new TitlebarLayout();
        StackPanel opts = UiClass.H(8,
            TitlebarOption(I18nCore.Tr("AI 按钮"), bar.Ai, delegate (bool on) { ShellTabs.SetTitlebar(ai: on); }),
            TitlebarOption(I18nCore.Tr("主题切换"), bar.Theme, delegate (bool on) { ShellTabs.SetTitlebar(theme: on); }),
            TitlebarOption(I18nCore.Tr("账号名称"), bar.Account, delegate (bool on) { ShellTabs.SetTitlebar(account: on); }));
        Control titlebar = LayoutRow(I18nCore.Tr("标题栏"), I18nCore.Tr("隐藏 AI 按钮后仍可按 Ctrl+K 打开"), opts);
        return LayoutSection(I18nCore.Tr("导航"), null, tabs, titlebar);
    }

    private Control TitlebarOption(string label, bool value, Action<bool> write)
    {
        CheckBox box = UiClass.Check(label, value);
        LayoutA11y(box, Py.Fmt(I18nCore.Tr("标题栏显示:{0}"), label));
        box.IsCheckedChanged += delegate
        {
            if (!_layoutRebuilding)
            {
                write(box.IsChecked == true);
                // 标题栏马上变
                RefreshShell();
            }
        };
        Border pill = new Border
        {
            Child = box,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 1, 10, 1),
        };
        pill.Dyn(Border.BorderBrushProperty, "B.Line");
        return pill;
    }

    // 全部内置页签按「可见顺序 + 被隐藏的(按默认顺序)」排成一排 chip
    private Control LayoutTabChips(ShellLayout shell)
    {
        List<string> visible = ShellTabs.OrderedKeys(shell);
        List<string> full = new List<string>(visible);
        foreach (string key in ShellTabs.HiddenKeys(shell))
        {
            full.Add(key);
        }
        WrapPanel panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (string key in full)
        {
            panel.Children.Add(TabChip(panel, full, visible, key));
        }
        return panel;
    }

    private Control TabChip(WrapPanel panel, List<string> full, List<string> visible, string key)
    {
        bool shown = visible.Contains(key);
        bool locked = !ShellTabs.CanHide(key);
        string label = ShellTabs.Label(key);
        TextBlock name = UiClass.Txt(label, 13);
        if (!shown)
        {
            name.TextDecorations = TextDecorations.Strikethrough;
        }
        Control grip = IconLib.Make("Grip", "B.InkFaint", 13);
        Button eye = new Button
        {
            Theme = UiClass.Theme("Btn.Icon"),
            Content = EyeIcon(shown),
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            IsEnabled = !locked,
        };
        if (locked)
        {
            ToolTip.SetTip(eye, I18nCore.Tr("「设置」页签始终显示，否则没有入口改回来"));
        }
        else
        {
            ToolTip.SetTip(eye, shown ? I18nCore.Tr("隐藏这个页签") : I18nCore.Tr("显示这个页签"));
        }
        LayoutA11y(eye, Py.Fmt(shown ? I18nCore.Tr("隐藏页签:{0}") : I18nCore.Tr("显示页签:{0}"), label));
        eye.Click += delegate
        {
            ToggleTab(full, visible, key, !shown);
        };
        Border chip = new Border
        {
            Child = UiClass.H(6, grip.VCenter(), name.VCenter(), eye.VCenter()),
            Height = 32,
            Padding = new Thickness(8, 0, 4, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
            Opacity = shown ? 1.0 : 0.5,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        chip.Dyn(Border.BackgroundProperty, "B.Paper2");
        chip.Dyn(Border.BorderBrushProperty, "B.Line");
        LayoutA11y(chip, Py.Fmt(I18nCore.Tr("页签:{0}"), label));
        WireChipDrag(panel, chip, full, visible, key);
        return chip;
    }

    // 眼睛图标:IconLib 没有眼睛,这里按 IconLib 的 16 单位描边口径画一个(闭眼 = 加一道斜线)
    private static Control EyeIcon(bool open)
    {
        const string eye = "M1.5,8 C3.4,4.6 5.6,3.2 8,3.2 C10.4,3.2 12.6,4.6 14.5,8 C12.6,11.4 10.4,12.8 8,12.8 C5.6,12.8 3.4,11.4 1.5,8 Z M8,5.9 A2.1,2.1 0 1 1 8,10.1 A2.1,2.1 0 1 1 8,5.9 Z";
        Geometry geometry = Geometry.Parse(open ? eye : eye + " M2.5,2.5 L13.5,13.5");
        geometry.Transform = new ScaleTransform(14.0 / 16.0, 14.0 / 16.0);
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Width = 14,
            Height = 14,
            StrokeThickness = 1.3,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        path[!Avalonia.Controls.Shapes.Path.StrokeProperty] = Res.Bind("B.InkMuted");
        return path;
    }

    // 拖动排序:按下抓住 chip,移动时跟手(TranslateTransform),松手按指针落点算插入位置后一次性写回
    private void WireChipDrag(WrapPanel panel, Border chip, List<string> full, List<string> visible, string key)
    {
        Point start = default;
        bool pressed = false;
        bool moved = false;
        chip.PointerPressed += delegate (object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed)
            {
                return;
            }
            pressed = true;
            moved = false;
            start = e.GetPosition(panel);
            e.Pointer.Capture(chip);
            e.Handled = true;
        };
        chip.PointerMoved += delegate (object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (!pressed)
            {
                return;
            }
            Point now = e.GetPosition(panel);
            double dx = now.X - start.X;
            double dy = now.Y - start.Y;
            if (!moved && Math.Abs(dx) < 4.0 && Math.Abs(dy) < 4.0)
            {
                return;
            }
            moved = true;
            chip.ZIndex = 10;
            chip.RenderTransform = new TranslateTransform(dx, dy);
        };
        chip.PointerReleased += delegate (object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            if (!pressed)
            {
                return;
            }
            pressed = false;
            e.Pointer.Capture(null);
            chip.RenderTransform = null;
            chip.ZIndex = 0;
            if (!moved)
            {
                return;
            }
            e.Handled = true;
            MoveTabTo(full, visible, key, ChipDropIndex(panel, chip, e.GetPosition(panel)));
        };
        chip.PointerCaptureLost += delegate
        {
            if (pressed)
            {
                pressed = false;
                chip.RenderTransform = null;
                chip.ZIndex = 0;
            }
        };
    }

    // 落点 → 插入下标(不含被拖的那一个):数一数指针「之前」有几个 chip(上一行的全算,同一行看中线)
    private static int ChipDropIndex(Panel panel, Control dragged, Point p)
    {
        int index = 0;
        foreach (Control child in panel.Children)
        {
            if (ReferenceEquals(child, dragged))
            {
                continue;
            }
            Rect b = child.Bounds;
            if (p.Y > b.Bottom || (p.Y >= b.Top && p.X > b.X + b.Width / 2.0))
            {
                index++;
            }
        }
        return index;
    }

    private void MoveTabTo(List<string> full, List<string> visible, string key, int target)
    {
        List<string> list = new List<string>(full);
        int from = list.IndexOf(key);
        if (from < 0)
        {
            return;
        }
        list.RemoveAt(from);
        target = Math.Max(0, Math.Min(target, list.Count));
        list.Insert(target, key);
        if (list.SequenceEqual(full))
        {
            return;
        }
        ShellTabs.ReorderTabs(list, visible);
        PageBase.Toast(I18nCore.Tr("页签顺序已更新"), "", ToastKind.Success);
        RefreshLayoutView();
    }

    private void ToggleTab(List<string> full, List<string> visible, string key, bool show)
    {
        if (show)
        {
            // 显示时按 chip 当前所在位置插回去(而不是一律追加到末尾)
            HashSet<string> next = new HashSet<string>(visible, StringComparer.Ordinal) { key };
            ShellTabs.ReorderTabs(full, next);
        }
        else if (ShellTabs.CanHide(key))
        {
            ShellTabs.SetTabVisible(key, false);
        }
        else
        {
            return;
        }
        RefreshLayoutView();
    }

    // ————— 页面:缩略图卡片 —————

    // 卡片顺序 = 常用几页在前,其余按入口清单顺序;「设置 · 布局」自己不列
    private static List<LayoutBlockEntryCatalog.Entry> OrderedPageEntries()
    {
        List<LayoutBlockEntryCatalog.Entry> list = new();
        foreach (string id in LayoutCommonPages)
        {
            LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(id);
            if (entry != null)
            {
                list.Add(entry);
            }
        }
        foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
        {
            if (!list.Contains(entry) && !string.Equals(entry.PageId, LayoutViewPageId, StringComparison.Ordinal))
            {
                list.Add(entry);
            }
        }
        return list;
    }

    private Control LayoutPagesSection()
    {
        LayoutV2 doc = LayoutStoreV2.Load();
        double gap = DensityGap(doc.Layout.Shell?.Density);
        List<LayoutBlockEntryCatalog.Entry> editable = new();
        List<string> single = new();
        foreach (LayoutBlockEntryCatalog.Entry entry in OrderedPageEntries())
        {
            // 单块页面(下载与网络 / 反馈与关于 / 只有一个块的页)不提供编辑入口,只在说明里点名
            if (CanEditLayoutPage(entry))
            {
                editable.Add(entry);
            }
            else
            {
                single.Add(entry.Label);
            }
        }
        List<LayoutBlockEntryCatalog.Entry> common = editable.Where(e => LayoutCommonPages.Contains(e.PageId)).ToList();
        List<LayoutBlockEntryCatalog.Entry> shown = (_layoutShowAllPages || common.Count == 0) ? editable : common;

        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) };
        foreach (LayoutBlockEntryCatalog.Entry entry in shown)
        {
            grid.Children.Add(PageCard(doc, entry, gap));
        }

        Button resetAll = UiClass.Btn(I18nCore.Tr("全部恢复默认"), BtnKind.Ghost, delegate
        {
            Run(ResetAllPagesAsync, I18nCore.Tr("布局未能保存"));
        });
        LayoutA11y(resetAll, I18nCore.Tr("所有页面恢复默认"));
        resetAll.IsEnabled = editable.Any(e => doc.Layout.Pages.ContainsKey(e.PageId));

        Grid foot = UiClass.G(null, "*,Auto");
        foot.Margin = new Thickness(0, -2, 0, 0);
        if (single.Count > 0)
        {
            string names = string.Join("", single.Select(s => Py.Fmt(I18nCore.Tr("「{0}」"), s)));
            foot.Add(UiClass.Small(Py.Fmt(I18nCore.Tr("{0}只有一块内容，不提供编辑。"), names)).Wrap().VCenter(), 0);
        }
        if (editable.Count > common.Count && common.Count > 0)
        {
            Button more = UiClass.Btn(_layoutShowAllPages ? I18nCore.Tr("收起") : Py.Fmt(I18nCore.Tr("显示全部 {0} 个页面"), editable.Count), BtnKind.Ghost, delegate
            {
                _layoutShowAllPages = !_layoutShowAllPages;
                RefreshLayoutView();
            });
            foot.Add(more.VCenter(), 0, 1);
        }
        return UiClass.V(10, LayoutGroupHead(I18nCore.Tr("页面"), resetAll), grid, foot);
    }

    private Control PageCard(LayoutV2 doc, LayoutBlockEntryCatalog.Entry entry, double gap)
    {
        bool custom = doc.Layout.Pages.TryGetValue(entry.PageId, out PageLayoutV2? page) && page != null;
        List<(int Span, double H, string Kind)> blocks = (custom && page != null) ? ThumbFromPage(page) : ThumbDefault(entry.PageId);

        TextBlock name = UiClass.Txt(entry.Label, 13, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Border state = custom
            ? UiClass.Tag(I18nCore.Tr("已自定义"), "B.AccentText", "B.AccentSoft")
            : UiClass.Tag(I18nCore.Tr("默认"));
        Grid head = UiClass.G(null, "*,Auto");
        head.Add(name.VCenter(), 0);
        head.Add(state.VCenter(), 0, 1);

        TextBlock note = UiClass.Small((custom && page != null) ? CustomNote(page) : I18nCore.Tr("和出厂时一样"));
        note.TextTrimming = TextTrimming.CharacterEllipsis;

        Button edit = UiClass.Btn(I18nCore.Tr("编辑"), BtnKind.Soft, delegate
        {
            // 跳到该页并直接进入编辑态(lin-edit 代理提供的约定入口)
            BeginLayoutPageEdit(entry);
        });
        edit.HorizontalAlignment = HorizontalAlignment.Stretch;
        edit.HorizontalContentAlignment = HorizontalAlignment.Center;
        LayoutA11y(edit, Py.Fmt(I18nCore.Tr("编辑页面布局:{0}"), entry.Label));
        Grid actions = UiClass.G(null, custom ? "*,*" : "*");
        actions.Add(edit, 0, 0);
        if (custom)
        {
            Button reset = UiClass.Btn(I18nCore.Tr("恢复默认"), BtnKind.Normal, delegate
            {
                ResetPageLayout(entry);
            });
            reset.HorizontalAlignment = HorizontalAlignment.Stretch;
            reset.HorizontalContentAlignment = HorizontalAlignment.Center;
            reset.Margin = new Thickness(6, 0, 0, 0);
            LayoutA11y(reset, Py.Fmt(I18nCore.Tr("页面恢复默认:{0}"), entry.Label));
            actions.Add(reset, 0, 1);
        }

        Border card = UiClass.Card(UiClass.V(8, PageThumb(blocks, gap), head, note, actions), 10);
        card.Margin = new Thickness(0, 0, 12, 12);
        return card;
    }

    // 单块页面返回 false(不出卡片);判断本身抛错时按「入口表的单块标记」兜底 —— 不让整块「页面」消失
    private static bool CanEditLayoutPage(LayoutBlockEntryCatalog.Entry entry)
    {
        try
        {
            return LayoutShellHooks.CanEditPage(entry.PageId);
        }
        catch (Exception)
        {
            return !entry.SingleBlock;
        }
    }

    // 「编辑」= 跳到该页并直接进入编辑态(约定签名,见 tmp/v3_handoff.md「Linux 设置」);入口失败只提示,不崩设置页
    private static void BeginLayoutPageEdit(LayoutBlockEntryCatalog.Entry entry)
    {
        try
        {
            LayoutShellHooks.BeginEditPage(entry.PageId);
        }
        catch (Exception ex)
        {
            PageBase.Toast(I18nCore.Tr("没能进入编辑"), ex.Message, ToastKind.Warning);
        }
    }

    // 缩略图:6 列网格,块按顺序流式排布;块间距 = 当前区块间距 × 0.3(改间距时跟着变)
    private static Control PageThumb(List<(int Span, double H, string Kind)> blocks, double gap)
    {
        double g = Math.Max(1.0, gap * 0.3);
        Grid grid = new Grid { VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < 6; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1.0, GridUnitType.Star));
        }
        int row = -1;
        int col = 6;
        foreach ((int Span, double H, string Kind) block in blocks)
        {
            int span = Math.Max(1, Math.Min(6, block.Span));
            if (col + span > 6)
            {
                row++;
                col = 0;
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            Border cell = new Border
            {
                Height = block.H,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(g / 2.0),
            };
            cell.Dyn(Border.BackgroundProperty, block.Kind == "w" ? "B.AccentSoft" : (block.Kind == "hero" ? "B.InkFaint" : "B.Line"));
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            Grid.SetColumnSpan(cell, span);
            grid.Children.Add(cell);
            col += span;
        }
        Border frame = new Border
        {
            Height = 92,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            ClipToBounds = true,
            Child = grid,
        };
        frame.Dyn(Border.BackgroundProperty, "B.Paper2");
        return frame;
    }

    private static List<(int Span, double H, string Kind)> ThumbDefault(string pageId)
    {
        if (LayoutDefaultThumbs.TryGetValue(pageId, out (int Span, double H, string Kind)[]? preset) && preset != null)
        {
            return preset.ToList();
        }
        return new List<(int Span, double H, string Kind)> { (6, 22.0, ""), (6, 22.0, ""), (6, 22.0, "") };
    }

    // 已自定义的页:按存档里的块画(隐藏的不画;网格按列换算到 6 列;老的画布存档按宽度比例换算成整数列)
    private static List<(int Span, double H, string Kind)> ThumbFromPage(PageLayoutV2 page)
    {
        List<(int Span, double H, string Kind)> list = new();
        int cols = page.Cols > 0 ? page.Cols : LayoutSchema.ColsDefault;
        int index = 0;
        foreach (BlockLayout block in page.Blocks)
        {
            if (block == null || block.Hidden)
            {
                continue;
            }
            int span = 6;
            if (page.Mode == LayoutSchema.ModeGrid)
            {
                span = (int)Math.Round(Math.Max(1, block.Span) * 6.0 / cols);
            }
            else if (page.Mode == LayoutSchema.ModeCanvas)
            {
                span = (int)Math.Round(Math.Max(0.04, block.W) * 6.0);
            }
            span = Math.Max(1, Math.Min(6, span));
            string kind = IsExtraBlock(block.Id) ? "w" : "";
            double h = block.MinH > 0.0 ? Math.Max(10.0, Math.Min(40.0, block.MinH / 6.0)) : (index == 0 ? 22.0 : 16.0);
            list.Add((span, h, kind));
            index++;
            if (list.Count >= 12)
            {
                break;
            }
        }
        if (list.Count == 0)
        {
            list.Add((6, 16.0, ""));
        }
        return list;
    }

    private static bool IsExtraBlock(string? blockId)
    {
        string id = blockId ?? "";
        return id.StartsWith(LayoutEngine.CustomPrefix, StringComparison.Ordinal) || id.StartsWith(LayoutEngine.PluginPrefix, StringComparison.Ordinal);
    }

    private static string CustomNote(PageLayoutV2 page)
    {
        List<string> bits = new();
        if (page.Mode == LayoutSchema.ModeGrid || page.Mode == LayoutSchema.ModeCanvas)
        {
            bits.Add(I18nCore.Tr("网格"));
        }
        int widgets = page.Blocks.Count(b => b != null && !b.Hidden && IsExtraBlock(b.Id));
        if (widgets > 0)
        {
            bits.Add(Py.Fmt(I18nCore.Tr("{0} 个组件"), widgets));
        }
        int hidden = page.Blocks.Count(b => b != null && b.Hidden);
        if (hidden > 0)
        {
            bits.Add(Py.Fmt(I18nCore.Tr("隐藏了 {0} 项"), hidden));
        }
        return bits.Count > 0 ? string.Join(" · ", bits) : I18nCore.Tr("调整了顺序");
    }

    private void ResetPageLayout(LayoutBlockEntryCatalog.Entry entry)
    {
        try
        {
            LayoutV2 doc = LayoutStoreV2.Load();
            ResetPageInDoc(doc, entry.PageId);
            if (!LayoutStoreV2.Save(doc))
            {
                PageBase.Toast(I18nCore.Tr("布局未能保存"), I18nCore.Tr("写盘失败"), ToastKind.Error);
                return;
            }
            PageBase.Toast(Py.Fmt(I18nCore.Tr("「{0}」已恢复默认"), entry.Label), "", ToastKind.Success);
        }
        catch (Exception ex)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), ex.Message, ToastKind.Error);
            return;
        }
        RefreshLayoutView();
    }

    // 一页恢复默认 = 删掉这一页的存档(引擎回到宿主出厂布局);页上的自建 / 插件块只清几何、引用保留
    // (与编辑态「重置本页」同一口径:组件数据不跟着丢)
    private static void ResetPageInDoc(LayoutV2 doc, string pageId)
    {
        if (!doc.Layout.Pages.TryGetValue(pageId, out PageLayoutV2? page) || page == null)
        {
            return;
        }
        List<string> keep = page.Blocks.Where(b => b != null && IsExtraBlock(b.Id)).Select(b => b.Id).ToList();
        if (keep.Count == 0)
        {
            doc.Layout.Pages.Remove(pageId);
            return;
        }
        PageLayoutV2 fresh = new PageLayoutV2
        {
            Mode = LayoutSchema.ModeStack,
            Gap = LayoutSchema.GapDefault,
            Pad = LayoutSchema.PadDefaultCopy()
        };
        foreach (string id in keep)
        {
            LayoutWidgetOps.AppendBlock(fresh, id);
        }
        doc.Layout.Pages[pageId] = fresh;
    }

    private async System.Threading.Tasks.Task ResetAllPagesAsync()
    {
        if (!await Dlg.Confirm(I18nCore.Tr("全部恢复默认"), I18nCore.Tr("所有页面的排列都会回到出厂时的样子；页签、分栏和方案不受影响。"), I18nCore.Tr("全部恢复默认"), I18nCore.Tr("取消")))
        {
            return;
        }
        // 先把现在这套留底成「上一次布局」,恢复错了可以从「方案」里切回
        string kept = LayoutProfiles.KeepCurrentAsPrevious();
        LayoutV2 doc = LayoutStoreV2.Load();
        foreach (string id in doc.Layout.Pages.Keys.ToList())
        {
            ResetPageInDoc(doc, id);
        }
        if (!LayoutStoreV2.Save(doc))
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), I18nCore.Tr("写盘失败"), ToastKind.Error);
            return;
        }
        PageBase.Toast(I18nCore.Tr("所有页面已恢复默认"), (kept.Length > 0) ? Py.Fmt(I18nCore.Tr("原来的布局已存为方案「{0}」，可以从「方案」里切回"), kept) : "", ToastKind.Success);
        RefreshLayoutView();
    }

    // ————— 方案:卡片 —————

    private Control LayoutProfilesSection()
    {
        Button import = UiClass.Btn(I18nCore.Tr("导入…"), BtnKind.Ghost, delegate
        {
            Run(ImportLayoutFileAsync, I18nCore.Tr("导入失败"));
        });
        Button export = UiClass.Btn(I18nCore.Tr("导出整套布局…"), BtnKind.Ghost, delegate
        {
            Run(ExportLayoutAsync, I18nCore.Tr("导出失败"));
        });
        Button save = UiClass.Btn(I18nCore.Tr("存为新方案"), BtnKind.Soft, delegate
        {
            Run(NewProfileAsync, I18nCore.Tr("布局未能保存"));
        });
        LayoutA11y(import, I18nCore.Tr("导入布局文件"));
        LayoutA11y(export, I18nCore.Tr("导出整套布局"));
        LayoutA11y(save, I18nCore.Tr("存为新方案"));

        LayoutV2 doc = LayoutStoreV2.Load();
        string active = doc.ActiveProfile ?? "";
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -12, 0) };
        if (active.Length == 0 || doc.GetProfile(active) == null)
        {
            // 直接编辑态(没有激活方案):给「现在这套」一张描边卡,提示可以存成方案
            grid.Children.Add(CurrentLayoutCard(doc.Layout));
        }
        foreach (KeyValuePair<string, LayoutBody> kv in doc.Profiles)
        {
            grid.Children.Add(ProfileCard(kv.Key, kv.Value, string.Equals(kv.Key, active, StringComparison.Ordinal)));
        }
        return UiClass.V(10, LayoutGroupHead(I18nCore.Tr("方案"), UiClass.H(6, import, export, save)), grid);
    }

    private static Border SchemeCardShell(Control content, bool active)
    {
        Border card = UiClass.Card(content, 12);
        card.Margin = new Thickness(0, 0, 12, 12);
        card.BorderThickness = new Thickness(1.5);
        if (active)
        {
            card.Dyn(Border.BorderBrushProperty, "B.Accent");
        }
        return card;
    }

    private Control CurrentLayoutCard(LayoutBody body)
    {
        Border tag = UiClass.Tag(I18nCore.Tr("正在使用"), "B.AccentText", "B.AccentSoft");
        tag.HorizontalAlignment = HorizontalAlignment.Left;
        TextBlock title = UiClass.Txt(I18nCore.Tr("当前布局"), 13, bold: true);
        TextBlock meta = UiClass.Small(Py.Fmt(I18nCore.Tr("{0} · 还没存成方案"), LayoutProfiles.Describe(body))).Wrap();
        meta.MinHeight = 34;
        Button save = UiClass.Btn(I18nCore.Tr("存为新方案"), BtnKind.Soft, delegate
        {
            Run(NewProfileAsync, I18nCore.Tr("布局未能保存"));
        });
        save.HorizontalAlignment = HorizontalAlignment.Stretch;
        save.HorizontalContentAlignment = HorizontalAlignment.Center;
        LayoutA11y(save, I18nCore.Tr("把当前布局存为新方案"));
        return SchemeCardShell(UiClass.V(4, tag, title, meta, save.M(0, 6, 0, 0)), active: true);
    }

    private Control ProfileCard(string name, LayoutBody body, bool active)
    {
        bool ai = LayoutProfiles.IsAiBackup(name, body);
        string tagText = active ? I18nCore.Tr("正在使用") : (ai ? I18nCore.Tr("AI 备份") : I18nCore.Tr("方案"));
        Border tag = active ? UiClass.Tag(tagText, "B.AccentText", "B.AccentSoft") : UiClass.Tag(tagText);
        tag.HorizontalAlignment = HorizontalAlignment.Left;
        TextBlock title = UiClass.Txt(name, 13, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(title, name);
        TextBlock meta = UiClass.Small(LayoutProfiles.Describe(body)).Wrap();
        meta.MinHeight = 34;

        Button apply = UiClass.Btn(active ? I18nCore.Tr("当前方案") : I18nCore.Tr("应用"), active ? BtnKind.Ghost : BtnKind.Soft, delegate
        {
            if (!active)
            {
                ApplyProfile(name);
            }
        });
        apply.IsEnabled = !active;
        apply.HorizontalAlignment = HorizontalAlignment.Stretch;
        apply.HorizontalContentAlignment = HorizontalAlignment.Center;
        LayoutA11y(apply, Py.Fmt(I18nCore.Tr("应用方案:{0}"), name));
        Button more = UiClass.IconBtn("More", I18nCore.Tr("更多"));
        LayoutA11y(more, Py.Fmt(I18nCore.Tr("方案菜单:{0}"), name));
        more.Click += delegate
        {
            OpenProfileMenu(more, name);
        };
        Grid actions = UiClass.G(null, "*,Auto");
        actions.Margin = new Thickness(0, 6, 0, 0);
        actions.Add(apply, 0);
        actions.Add(more.M(6, 0, 0, 0), 0, 1);
        return SchemeCardShell(UiClass.V(4, tag, title, meta, actions), active);
    }

    // 卡片菜单:重命名 / 复制一份 / —— / 删除
    private void OpenProfileMenu(Control anchor, string name)
    {
        ContextMenu menu = new ContextMenu();
        MenuItem rename = new MenuItem { Header = I18nCore.Tr("重命名") };
        rename.Click += delegate
        {
            Run(() => RenameProfileAsync(name), I18nCore.Tr("布局未能保存"));
        };
        MenuItem copy = new MenuItem { Header = I18nCore.Tr("复制一份") };
        copy.Click += delegate
        {
            Run(() => DuplicateProfileAsync(name), I18nCore.Tr("布局未能保存"));
        };
        MenuItem del = new MenuItem { Header = I18nCore.Tr("删除") };
        del.Click += delegate
        {
            Run(() => DeleteProfileAsync(name), I18nCore.Tr("布局未能保存"));
        };
        menu.Items.Add(rename);
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(del);
        menu.PlacementTarget = anchor;
        menu.Placement = Avalonia.Controls.PlacementMode.Bottom;
        menu.Open(anchor);
    }

    private async System.Threading.Tasks.Task NewProfileAsync()
    {
        string? name = await Dlg.Prompt(I18nCore.Tr("存为新方案"), I18nCore.Tr("方案名"), Py.Fmt(I18nCore.Tr("我的布局 {0}"), DateTime.Now.ToString("MM-dd HH:mm")));
        if (name == null)
        {
            return;
        }
        string error = LayoutProfiles.Create(name, fromCurrent: true);
        if (error.Length > 0)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
            return;
        }
        PageBase.Toast(Py.Fmt(I18nCore.Tr("已把当前布局存为方案「{0}」"), name.Trim()), "", ToastKind.Success);
        RefreshLayoutView();
    }

    private void ApplyProfile(string name)
    {
        string error = LayoutProfiles.Apply(name, out string kept);
        if (error.Length > 0)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
            return;
        }
        PageBase.Toast(Py.Fmt(I18nCore.Tr("已切换到「{0}」，所有页面已重排"), name), (kept.Length > 0) ? Py.Fmt(I18nCore.Tr("原来的布局已存为方案「{0}」，可以随时切回"), kept) : "", ToastKind.Success);
        RefreshLayoutView();
    }

    private async System.Threading.Tasks.Task RenameProfileAsync(string name)
    {
        string? next = await Dlg.Prompt(I18nCore.Tr("重命名"), I18nCore.Tr("方案名"), name);
        if (next == null || next == name)
        {
            return;
        }
        string error = LayoutProfiles.Rename(name, next);
        if (error.Length > 0)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
            return;
        }
        RefreshLayoutView();
    }

    private async System.Threading.Tasks.Task DuplicateProfileAsync(string name)
    {
        string? next = await Dlg.Prompt(I18nCore.Tr("复制一份"), I18nCore.Tr("方案名"), Py.Fmt(I18nCore.Tr("{0} 副本"), name));
        if (next == null)
        {
            return;
        }
        string error = LayoutProfiles.Duplicate(name, next);
        if (error.Length > 0)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
            return;
        }
        PageBase.Toast(I18nCore.Tr("已复制一份"), "", ToastKind.Success);
        RefreshLayoutView();
    }

    private async System.Threading.Tasks.Task DeleteProfileAsync(string name)
    {
        if (!await Dlg.Confirm(I18nCore.Tr("删除"), Py.Fmt(I18nCore.Tr("删除布局方案「{0}」?"), name), I18nCore.Tr("删除"), I18nCore.Tr("取消"), danger: true))
        {
            return;
        }
        string error = LayoutProfiles.Delete(name);
        if (error.Length > 0)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
            return;
        }
        PageBase.Toast(Py.Fmt(I18nCore.Tr("已删除「{0}」"), name), "", ToastKind.Success);
        RefreshLayoutView();
    }

    // ————— 方案:导入 / 导出整套布局 —————

    private async System.Threading.Tasks.Task ExportLayoutAsync()
    {
        int choice = await Dlg.Choose(I18nCore.Tr("导出整套布局"), UiClass.Muted(I18nCore.Tr("整套布局含页签、页面排列、分栏和组件；兼容 v1 的页面布局只含每页区块的顺序与显隐")).Wrap(), new string[3]
        {
            I18nCore.Tr("取消"),
            I18nCore.Tr("导出兼容 v1 的页面布局"),
            I18nCore.Tr("导出整套布局")
        }, 560);
        if (choice == 2)
        {
            string text = LayoutStoreV2.ExportJson(LayoutStoreV2.Load());
            string? path = await Dlg.SaveFile(I18nCore.Tr("导出整套布局"), "tipcl-layout-v2.json");
            if (path != null && WriteTextFile(path, text))
            {
                PageBase.Toast(I18nCore.Tr("已导出整套布局"), path, ToastKind.Success);
            }
            return;
        }
        if (choice == 1)
        {
            string text = PageLayoutStore.ExportJson(LayoutV1Projection.ProjectV1(LayoutStoreV2.Load()));
            string? path2 = await Dlg.SaveFile(I18nCore.Tr("导出兼容 v1 的页面布局"), "tipcl-page-layout.json");
            if (path2 != null && WriteTextFile(path2, text))
            {
                PageBase.Toast(I18nCore.Tr("已导出页面布局"), path2, ToastKind.Success);
            }
        }
    }

    private async System.Threading.Tasks.Task ImportLayoutFileAsync()
    {
        string? path = await Dlg.PickFile(I18nCore.Tr("导入布局"), "*.json");
        if (path == null)
        {
            return;
        }
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            PageBase.Toast(I18nCore.Tr("布局读取失败"), ex.Message, ToastKind.Error);
            return;
        }
        LayoutV2? whole = LayoutV2.FromJsonText(text);
        if (whole != null)
        {
            int choice = await Dlg.Choose(I18nCore.Tr("导入布局"), UiClass.Muted(I18nCore.Tr("这是一整套布局(含页签、页面排列、分栏)。要怎么用?")).Wrap(), new string[3]
            {
                I18nCore.Tr("取消"),
                I18nCore.Tr("存为新方案"),
                I18nCore.Tr("应用为当前布局")
            }, 560);
            if (choice == 2)
            {
                string error = LayoutProfiles.ApplySnapshot(whole.Layout, out string kept);
                if (error.Length > 0)
                {
                    PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
                    return;
                }
                PageBase.Toast(I18nCore.Tr("布局已应用"), (kept.Length > 0) ? Py.Fmt(I18nCore.Tr("原来的布局已存为方案「{0}」，可以随时切回"), kept) : Path.GetFileName(path), ToastKind.Success);
                RefreshLayoutView();
            }
            else if (choice == 1)
            {
                string? name = await Dlg.Prompt(I18nCore.Tr("存为新方案"), I18nCore.Tr("方案名"), Path.GetFileNameWithoutExtension(path));
                if (name == null)
                {
                    return;
                }
                string error2 = LayoutProfiles.CreateFrom(name, whole.Layout);
                if (error2.Length > 0)
                {
                    PageBase.Toast(I18nCore.Tr("布局未能保存"), error2, ToastKind.Warning);
                    return;
                }
                PageBase.Toast(Py.Fmt(I18nCore.Tr("已存为方案「{0}」"), name.Trim()), "", ToastKind.Success);
                RefreshLayoutView();
            }
            return;
        }
        // 兼容 v1 的页面布局:把每页的 {id,hidden} 与顺序并进当前文档的 pages
        try
        {
            IReadOnlyDictionary<string, PageLayoutDoc> pages = PageLayoutStore.ParseImport(text);
            LayoutV2 doc = LayoutStoreV2.Load();
            int num = LayoutProfiles.MergeV1Pages(doc, pages);
            LayoutStoreV2.Save(doc);
            PageBase.Toast(Py.Fmt(I18nCore.Tr("已导入 {0} 个页面的布局(下次打开对应页面生效)"), num), Path.GetFileName(path), ToastKind.Success);
            RefreshLayoutView();
        }
        catch (Exception ex2)
        {
            PageBase.Toast(I18nCore.Tr("不是有效的布局文件"), ex2.Message, ToastKind.Error);
        }
    }

    private static bool WriteTextFile(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text);
            return true;
        }
        catch (Exception ex)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), ex.Message, ToastKind.Error);
            return false;
        }
    }

    // ————— 高级(默认折叠):分栏 / 网页与按钮组件 / 布局文件 —————

    private static bool ReadAdvancedOpen()
    {
        try
        {
            return UiPrefs.Load().GetExpanded(LayoutAdvancedPrefKey, false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteAdvancedOpen(bool open)
    {
        try
        {
            UiPrefs prefs = UiPrefs.Load();
            prefs.SetExpanded(LayoutAdvancedPrefKey, open);
            prefs.Save();
        }
        catch (Exception)
        {
            // 折叠态写不进去不影响本次使用
        }
    }

    private Control LayoutAdvancedSection()
    {
        bool open = ReadAdvancedOpen();
        Control body = Rows(LayoutSplitRow(), LayoutDangerRow(), LayoutFileRow());
        body.IsVisible = open;
        body.Margin = new Thickness(0, 8, 0, 0);

        Control chevron = IconLib.Make("ChevronDown", "B.InkMuted", 14);
        chevron.RenderTransform = open ? new RotateTransform(180) : null;
        Grid headGrid = UiClass.G(null, "*,Auto");
        headGrid.Add(UiClass.V(2, UiClass.Txt(I18nCore.Tr("分栏、网页与按钮组件、布局文件"), 13, bold: true), UiClass.Small(I18nCore.Tr("一般不需要动"))).VCenter(), 0);
        headGrid.Add(chevron.VCenter(), 0, 1);
        Button head = new Button
        {
            Theme = UiClass.Theme("Btn.Ghost"),
            Content = headGrid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 4, 0, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        LayoutA11y(head, I18nCore.Tr("展开或收起高级设置"));
        head.Click += delegate
        {
            bool next = !body.IsVisible;
            body.IsVisible = next;
            chevron.RenderTransform = next ? new RotateTransform(180) : null;
            WriteAdvancedOpen(next);
        };
        return UiClass.V(10, Gt(I18nCore.Tr("高级")), UiClass.Card(UiClass.V(0, head, body)));
    }

    // —— 分栏宽度:一行汇总 + 全部复位(13 条站点表不再逐条展示,见文件头 T-03) ——

    private Control LayoutSplitRow()
    {
        List<SplitSite> adjusted = SplitCatalog.All.Where(s => s.IsTwoColumn && SplitRatioStore.IsStored(s.PageId, s.SplitId)).ToList();
        string how = I18nCore.Tr("在页面上拖两栏中间的分隔线即可调整，双击复位。");
        string text = adjusted.Count == 0
            ? I18nCore.Tr("都是默认宽度。") + how
            : Py.Fmt(I18nCore.Tr("已调整 {0} 处：{1}。"), adjusted.Count, string.Join(I18nCore.Tr("、"), adjusted.Select(SplitLabel))) + how;
        Button resetAll = UiClass.Btn(I18nCore.Tr("全部复位"), BtnKind.Normal, delegate
        {
            ResetAllSplits();
        });
        resetAll.IsEnabled = adjusted.Count > 0;
        LayoutA11y(resetAll, I18nCore.Tr("全部复位分栏"));
        return LayoutRow(I18nCore.Tr("分栏宽度"), text, resetAll);
    }

    private void ResetAllSplits()
    {
        int count = 0;
        foreach (SplitSite site in SplitCatalog.All)
        {
            if (site.IsTwoColumn && SplitRatioStore.Clear(site.PageId, site.SplitId))
            {
                count++;
            }
        }
        PageBase.Toast((count > 0) ? I18nCore.Tr("分栏宽度已全部复位") : I18nCore.Tr("没有需要复位的分栏"), (count > 0) ? Py.Fmt(I18nCore.Tr("已复位 {0} 处"), count) : "", ToastKind.Success);
        RefreshLayoutView();
    }

    // 两栏站点的用户叫法(SplitCatalog 只有 pageId / splitId 这种内部名)
    private static string SplitLabel(SplitSite site)
    {
        return site.PageId switch
        {
            "library" => I18nCore.Tr("实例库文件夹栏"),
            "settings" => I18nCore.Tr("设置左侧导航"),
            "version-manage" => I18nCore.Tr("版本管理左栏"),
            "ai" => I18nCore.Tr("AI 对话历史栏"),
            "feedback" => I18nCore.Tr("反馈页右栏"),
            "account" => I18nCore.Tr("账号头像卡"),
            _ => site.PageId
        };
    }

    // —— 网页和按钮组件:全局开关(本机偏好,不进布局文件)+ 警示 ——

    private Control LayoutDangerRow()
    {
        CheckBox toggle = UiClass.Switch(LayoutProfiles.DangerousWidgetsEnabled);
        LayoutA11y(toggle, I18nCore.Tr("网页和按钮组件"));
        TextBlock warnText = UiClass.Txt(I18nCore.Tr("导入的布局里如果带网页或按钮组件，仍然默认关闭，需要你逐个打开。"), 12, false, "B.Warn").Wrap();
        Border warn = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 8, 0, 4),
            Child = warnText,
            IsVisible = toggle.IsChecked == true,
        };
        warn.Dyn(Border.BackgroundProperty, "B.WarnSoft");
        toggle.IsCheckedChanged += delegate
        {
            if (_layoutRebuilding)
            {
                return;
            }
            bool on = toggle.IsChecked == true;
            LayoutProfiles.DangerousWidgetsEnabled = on;
            warn.IsVisible = on;
            PageBase.Toast(on ? I18nCore.Tr("已开启：「添加」抽屉里多了网页和按钮") : I18nCore.Tr("已关闭网页和按钮组件"), "", ToastKind.Success);
        };
        return UiClass.V(0, LayoutRow(I18nCore.Tr("网页和按钮组件"), I18nCore.Tr("开启后可以添加嵌入网页（只接受 https）和执行动作的按钮"), toggle), warn);
    }

    // —— 布局文件:打开所在文件夹 / 在编辑器中打开…(独立窗口) ——

    private Control LayoutFileRow()
    {
        Button folder = UiClass.Btn(I18nCore.Tr("打开所在文件夹"), BtnKind.Normal, delegate
        {
            OpenLayoutFolder();
        });
        Button json = UiClass.Btn(I18nCore.Tr("在编辑器中打开…"), BtnKind.Normal, delegate
        {
            LayoutJsonWindow.ShowFor(TopLevel.GetTopLevel(this) as Window, delegate
            {
                RefreshLayoutView();
            });
        });
        LayoutA11y(folder, I18nCore.Tr("打开布局文件所在文件夹"));
        LayoutA11y(json, I18nCore.Tr("在编辑器中打开布局文件"));
        return LayoutRow(I18nCore.Tr("布局文件"), I18nCore.Tr("用 JSON 编辑整套布局，适合批量修改或分享给别人"), UiClass.H(8, folder, json));
    }

    private static void OpenLayoutFolder()
    {
        string dir = LayoutStoreV2.DirPath;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception)
        {
            // 建不出来就交给文件管理器报错
        }
        TipCL.App.Pages.Library.LibraryPage.OpenInFileManager(dir, I18nCore.Tr("打开失败"));
    }

    // ————— 共用:落盘唯一入口 = LayoutStoreV2 —————

    // load → 改 → Save;写不进去只提示,不崩界面(与 PageLayoutStore 的既有范式一致)
    private static void WriteLayout(Action<LayoutV2> edit, string toast)
    {
        try
        {
            LayoutV2 doc = LayoutStoreV2.Load();
            edit(doc);
            LayoutStoreV2.Save(doc);
            PageBase.Toast(toast, "", ToastKind.Success);
        }
        catch (Exception ex)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), ex.Message, ToastKind.Error);
        }
    }
}
