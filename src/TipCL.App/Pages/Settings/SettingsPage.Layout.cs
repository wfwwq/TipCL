// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/SettingsLayoutPage.cs(1670 行,冻结件 @0a304a7)。
// 本文件是它的 Avalonia 移植:设置页第 8 个内层视图「布局」——7 个小节
//   a 外观与密度 / b 页签与标题栏 / c 页面区块入口 / d 分栏比例 / e 自建区块 / f 整机布局方案 / g 高级:布局 JSON 编辑器。
// 三条硬规矩照原文:
//   1) 落盘唯一入口 = LayoutStoreV2(load → 改 → Save);本文件绝不自己写布局文件(导出到用户选定路径除外);
//   2) 方案应用 = **原子替换** layout(整个 LayoutBody 换掉)再 Save,v1 由 v2 单向写通;
//   3) 构建入口 = OpenLayoutView()(壳层右键菜单「布局设置...」走这里)。
//
// 与 Windows 源的差异(全部记在 R32-LINMORE.md 的知情降级表):
//   T-01 WPF 的 `Ui.*` → 本树 `UiClass.*`(同 SettingsPage 既有写法);`I18n.L(x,a)` → `Py.Fmt(I18nCore.Tr(x),a)`。
//   T-02 `DispatcherPriority.Loaded` 保留(Avalonia 11 有同名优先级);`PageBase.Win` → `TopLevel.GetTopLevel(this)`。
//   T-03 【R32-LINSPLIT 已解除降级】Windows 的「分栏比例」表来自 SplitCatalog;LIN-MORE 时期 Linux 树
//        没有 SplitCatalog ⇒ 本节曾降级为「列出文档里已存的比例 + 一键清空」。R32-LINSPLIT 已把
//        `SplitCatalog` / `SplitRatioStore` / `PageLayoutHost` 按 Windows 冻结件搬进本树
//        (`src/TipCL.App/Controls/PageLayoutHost.cs`,13 条站点表 + 4 处接线),
//        本节随之恢复成 Windows 的表驱动形态(逐条现状 + 恢复默认 + 全部复位)。
//   T-04 「添加自建区块」在 Windows 冻结件里是**永久禁用**(L6 未落地);Linux 侧按任务书要求补齐为可用的
//        部件选择器(枚举 SafeTypes → 添加到选定页面 → 立刻上屏)。见 §e。
//   T-05 Windows 标题栏有「桥接」药丸钮,Linux 标题栏没有该控件 ⇒ 开关保留但禁用并写明原因。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    private bool _layoutRebuilding;

    // JSON 编辑器的草稿:重建视图时保留用户还没应用的文本
    private string _layoutJsonText = "";

    private StackPanel? _layoutJsonIssues;

    // ————— 视图装配 —————

    /// <summary>构「布局」视图(每次切到该视图都重建,保证读到最新状态)。</summary>
    private Control BuildLayoutView()
    {
        _layoutRebuilding = true;
        try
        {
            // [layout-fix 第三批] 右侧的分组卡片拆成可拖动的区块 BlockHost("settings:layout")(左侧导航不动)。
            // 原来是 ScrollBody(V(10, 标题, 说明, 6 个小节, JSON 卡)):滚动区自身外边距 20,16,20,16、内容间距 10。
            // 现在宿主自身带同样的外边距、内容边距 0、区块间距 10(标准密度),标题 + 说明合成「标题」块
            // ⇒ 没有存档时逐行位置不变。
            var specs = new List<BlockSpec>
            {
                new BlockSpec { Id = "header", Title = I18nCore.Tr("标题"), Content = UiClass.V(10, PTitle(I18nCore.Tr("布局")),
                    UiClass.Muted(I18nCore.Tr("默认布局 = 各页现状;这里的改动即时写进 ui_layout_v2.json,老版本程序照旧读得懂")).M(0, 0, 0, 4)) },
                new BlockSpec { Id = "appearance", Title = I18nCore.Tr("外观与密度"), Content = LayoutSection(I18nCore.Tr("外观与密度"), LayoutAppearanceRows()) },
                new BlockSpec { Id = "shell", Title = I18nCore.Tr("页签与标题栏"), Content = LayoutSection(I18nCore.Tr("页签与标题栏"), LayoutShellRows()) },
                new BlockSpec { Id = "blocks", Title = I18nCore.Tr("页面区块"), Content = LayoutSection(I18nCore.Tr("页面区块"), LayoutBlockRows()) },
                new BlockSpec { Id = "splits", Title = I18nCore.Tr("分栏比例"), Content = LayoutSection(I18nCore.Tr("分栏比例"), LayoutSplitRows()) },
                new BlockSpec { Id = "widgets", Title = I18nCore.Tr("自建区块"), Content = LayoutSection(I18nCore.Tr("自建区块"), LayoutWidgetRows()) },
                new BlockSpec { Id = "profiles", Title = I18nCore.Tr("布局方案"), Content = LayoutSection(I18nCore.Tr("布局方案"), LayoutProfileRows()) },
                new BlockSpec { Id = "json", Title = I18nCore.Tr("布局 JSON"), Content = LayoutJsonCard() },
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
            LayoutLiveOnBuilt(host); // AI2-i18n-ux:布局文档被别处(AI layout_apply 等)改了时自动刷新本视图
            return host;
        }
        finally
        {
            _layoutRebuilding = false;
        }
    }

    /// <summary>
    /// 公开的「跳到 设置 → 布局」入口(壳层右键菜单的「布局设置...」走这里)。
    /// 与 Windows 同形:只调一次已有的 Select(LayoutView),不动任何静态初始化。
    /// </summary>
    public void OpenLayoutView()
    {
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

    // 一个小节 = 分组标题 + 卡片(行与行之间有通到卡片两端的分隔线,与设置页其它小节同一套视觉)
    private static Control LayoutSection(string title, params Control[] rows)
    {
        return UiClass.V(10, Gt(title), UiClass.Card(Rows(rows)));
    }

    // ————— a 外观与密度 —————

    private Control[] LayoutAppearanceRows()
    {
        ShellLayout shell = ShellTabs.CurrentShell();
        ComboBox density = UiClass.Combo(new string[3] { I18nCore.Tr("紧凑"), I18nCore.Tr("标准"), I18nCore.Tr("宽松") }, DensityLabel(shell.Density), 150);
        density.SelectionChanged += delegate
        {
            if (_layoutRebuilding)
            {
                return;
            }
            WriteLayout(delegate(LayoutV2 doc)
            {
                doc.Layout.Shell.Density = DensityKey(density.SelectedIndex);
            }, I18nCore.Tr("布局已保存"));
            RefreshLayoutView();
        };
        return new Control[1]
        {
            UiClass.Field(I18nCore.Tr("密度"), density, I18nCore.Tr("只影响区块间距档(紧凑 6 / 标准 10 / 宽松 14)"))
        };
    }

    private static string DensityKey(int index)
    {
        return index switch
        {
            0 => LayoutSchema.DensityCompact,
            2 => LayoutSchema.DensityLoose,
            _ => LayoutSchema.DensityStandard
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

    // ————— b 页签与标题栏 —————

    private Control[] LayoutShellRows()
    {
        ShellLayout shell = ShellTabs.CurrentShell();
        List<Control> rows = new();
        List<string> order = ShellTabs.OrderedKeys(shell);
        foreach (string key in ShellTabs.BuiltinKeys)
        {
            string tab = key;
            int index = order.IndexOf(tab);
            CheckBox visible = UiClass.Switch(ShellTabs.IsVisible(shell, tab));
            Avalonia.Automation.AutomationProperties.SetName(visible, "页签显示:" + ShellTabs.Label(tab));
            if (!ShellTabs.CanHide(tab))
            {
                visible.IsEnabled = false;
                ToolTip.SetTip(visible, "「设置」页签始终显示,否则没有入口改回来");
            }
            visible.IsCheckedChanged += delegate
            {
                if (!_layoutRebuilding)
                {
                    ShellTabs.SetTabVisible(tab, visible.IsChecked == true);
                    RefreshShell();
                    RefreshLayoutView();
                }
            };
            Button up = UiClass.IconBtn("↑", I18nCore.Tr("上移"));
            Button down = UiClass.IconBtn("↓", I18nCore.Tr("下移"));
            Avalonia.Automation.AutomationProperties.SetName(up, "上移页签:" + ShellTabs.Label(tab));
            Avalonia.Automation.AutomationProperties.SetName(down, "下移页签:" + ShellTabs.Label(tab));
            up.IsEnabled = index > 0;
            down.IsEnabled = index >= 0 && index < order.Count - 1;
            up.Click += delegate
            {
                MoveTab(order, tab, -1);
            };
            down.Click += delegate
            {
                MoveTab(order, tab, 1);
            };
            rows.Add(UiClass.Field(ShellTabs.Label(tab), UiClass.H(8, visible, up, down), I18nCore.Tr("页签顺序与显隐")));
        }
        TitlebarLayout bar = shell.Titlebar ?? new TitlebarLayout();
        rows.Add(TitlebarRow(I18nCore.Tr("桥接"), bar.Bridge, delegate(bool on)
        {
            ShellTabs.SetTitlebar(bridge: on);
        }, "Linux 标题栏没有桥接药丸钮:开关会写入文档,但本树无宿主可显隐"));
        rows.Add(TitlebarRow("AI", bar.Ai, delegate(bool on)
        {
            ShellTabs.SetTitlebar(ai: on);
        }, I18nCore.Tr("只隐藏 / 显示,不改形态")));
        rows.Add(TitlebarRow(I18nCore.Tr("主题"), bar.Theme, delegate(bool on)
        {
            ShellTabs.SetTitlebar(theme: on);
        }, I18nCore.Tr("只隐藏 / 显示,不改形态")));
        rows.Add(TitlebarRow(I18nCore.Tr("账号"), bar.Account, delegate(bool on)
        {
            ShellTabs.SetTitlebar(account: on);
        }, I18nCore.Tr("只隐藏 / 显示,不改形态")));
        return rows.ToArray();
    }

    private Control TitlebarRow(string label, bool value, Action<bool> write, string hint)
    {
        CheckBox box = UiClass.Switch(value);
        Avalonia.Automation.AutomationProperties.SetName(box, "标题栏:" + label);
        box.IsCheckedChanged += delegate
        {
            if (!_layoutRebuilding)
            {
                write(box.IsChecked == true);
                RefreshShell();
            }
        };
        if (label == I18nCore.Tr("桥接"))
        {
            box.IsEnabled = false;
        }
        return UiClass.Field("标题栏:" + label, box, hint);
    }

    private void MoveTab(List<string> order, string key, int direction)
    {
        List<string> list = new List<string>(order);
        int index = list.IndexOf(key);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }
        list.RemoveAt(index);
        list.Insert(target, key);
        ShellTabs.SetTabs(list);
        RefreshShell();
        RefreshLayoutView();
    }

    // ————— c 页面区块入口(复用 LayoutEditor.EnterEdit) —————

    private Control[] LayoutBlockRows()
    {
        List<Control> rows = new();
        foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
        {
            LayoutBlockEntryCatalog.Entry item = entry;
            Button open = UiClass.Btn(I18nCore.Tr("自定义布局..."), BtnKind.Chip, delegate
            {
                OpenBlockEditor(item);
            });
            Avalonia.Automation.AutomationProperties.SetName(open, "自定义布局:" + item.Label);
            rows.Add(UiClass.Field(item.Label, open, I18nCore.Tr("跳到该页并进入区块编辑(完成编辑后自动保存)")));
        }
        rows.Add(UiClass.Field(I18nCore.Tr("单块页"), UiClass.Muted("单块页(下载与网络)也能进编辑态:可以调外观、标题或先隐藏"), ""));
        return rows.ToArray();
    }

    // 跳到目标页 → 找到那一页的 BlockHost → EnterEdit(等价于点右键菜单里的「自定义布局...」)
    private void OpenBlockEditor(LayoutBlockEntryCatalog.Entry entry)
    {
        TipCL.App.Shell.MainWindow? win = TopLevel.GetTopLevel(this) as TipCL.App.Shell.MainWindow;
        if (win == null)
        {
            return;
        }
        win.Navigate(entry.NavId);
        if (entry.NavId == "settings")
        {
            if (entry.SettingsView >= 0)
            {
                Select(entry.SettingsView);
            }
        }
        else if (entry.NavId == "download" && entry.Segment >= 0)
        {
            (win.PageHostControl?.Content as TipCL.App.Pages.Download.DownloadPage)?.SelectSegment(entry.Segment);
        }
        else if (entry.NavId == "plugins" && entry.Tab.Length > 0)
        {
            (win.PageHostControl?.Content as TipCL.App.Pages.Plugins.PluginsPage)?.ShowTab(entry.Tab);
        }
        // 等页面挂进可视树再找宿主(Avalonia:Loaded 优先级 = 布局完成之后)
        Dispatcher.UIThread.Post(delegate
        {
            EnterBlockEdit(win, entry.PageId, entry.NeedsContext);
        }, DispatcherPriority.Loaded);
    }

    private void EnterBlockEdit(TipCL.App.Shell.MainWindow win, string pageId, string needsContext = "")
    {
        BlockHost? host = FindBlockHost(win, pageId);
        if (host != null && host.Editor.CanEdit)
        {
            host.EnterEdit();
            PageBase.Toast(I18nCore.Tr("已进入自定义布局"), I18nCore.Tr("完成编辑后自动保存"), ToastKind.Success);
            return;
        }
        // [layout-fix 第三批] 要先打开具体对象才有的页(版本安装):告诉用户先去哪儿,而不是笼统的"没找到"
        PageBase.Toast(I18nCore.Tr("没找到可编辑的区块"), needsContext.Length > 0 ? needsContext : I18nCore.Tr("可以在页面上右键「自定义布局...」"), ToastKind.Warning);
    }

    // 视觉树里按 pageId 找区块宿主(页面已渲染的常见路径)
    private static BlockHost? FindBlockHost(TipCL.App.Shell.MainWindow win, string pageId)
    {
        Visual? root = win.PageHostControl?.Content as Visual;
        if (root == null)
        {
            return null;
        }
        return FindBlockHostVisual(root, pageId, 0);
    }

    private static BlockHost? FindBlockHostVisual(Visual root, string pageId, int depth)
    {
        if (depth > 40)
        {
            return null;
        }
        if (root is BlockHost host && string.Equals(host.PageId, pageId, StringComparison.Ordinal))
        {
            return host;
        }
        foreach (Visual child in root.GetVisualChildren())
        {
            BlockHost? found = FindBlockHostVisual(child, pageId, depth + 1);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    // ————— d 分栏比例(现状表直接来自 SplitCatalog,视图里不重复硬编码) —————
    //
    // R32-LINSPLIT:本小节此前是 LIN-MORE 的降级版(该文件 T-03 / R32-LINMORE D-4:「Linux 树没有
    // SplitCatalog(没有可拖动的左右分栏站点)」)。SplitCatalog / SplitRatioStore 已按 Windows 冻结件
    // @0a304a7 搬进本树(src/TipCL.App/Controls/PageLayoutHost.cs)⇒ 本节按 Windows
    // `SettingsLayoutPage.cs:356-440 LayoutSplitRows/ResetAllSplits/SplitNowText/SplitKindLabel`
    // 逐条恢复到"表驱动"形态:13 条现状 + 每条「恢复默认」(仅两栏且有存储时可点)+ 末尾「全部复位」。
    //
    // 与 Windows 的差异(仅格式化,无功能差):
    //   * Windows 用 `I18n.L("当前 {0:P0}", ratio)`,本树没有 I18nCore 的复合格式化门面 ⇒ 手写
    //     `Math.Round(ratio*100) + "%"`(与 `P0` 的可见结果一致,且不受区域设置影响);
    //   * 每条按钮补了 AutomationProperties.Name(Windows 没有),供无头用例按名定位。

    private Control[] LayoutSplitRows()
    {
        List<Control> rows = new();
        foreach (SplitSite site in SplitCatalog.All)
        {
            SplitSite current = site;
            string where = "#" + site.Index + "  " + site.PageId + " / " + site.SplitId;
            double? stored = SplitRatioStore.Read(site.PageId, site.SplitId);
            string now = SplitNowText(site);
            if (!site.IsTwoColumn)
            {
                // 说明(为什么这处不是可拖分栏)放进 ToolTip:表格里保持一行,长文案不把行撑高
                Control plain = UiClass.Field(where, UiClass.Small(now), SplitKindLabel(site.Kind));
                ToolTip.SetTip(plain, site.Note);
                rows.Add(plain);
                continue;
            }
            Button reset = UiClass.Btn(I18nCore.Tr("恢复默认"), BtnKind.Chip, delegate
            {
                if (SplitRatioStore.Clear(current.PageId, current.SplitId))
                {
                    PageBase.Toast(I18nCore.Tr("已恢复默认"), "", ToastKind.Success);
                    RefreshLayoutView();
                }
            });
            Avalonia.Automation.AutomationProperties.SetName(reset, "恢复默认分栏:" + current.PageId + "/" + current.SplitId);
            reset.IsEnabled = stored.HasValue;
            string cur = (stored.HasValue ? ("当前 " + Math.Round(stored.Value * 100.0) + "%") : I18nCore.Tr("当前 = 现状"));
            Control row = UiClass.Field(where, UiClass.H(8, UiClass.Small(now).VCenter(), UiClass.Small(cur).VCenter(), reset), SplitKindLabel(site.Kind));
            ToolTip.SetTip(row, site.Note);
            rows.Add(row);
        }
        Button resetAll = UiClass.Btn(I18nCore.Tr("全部复位"), BtnKind.Normal, delegate
        {
            ResetAllSplits();
        });
        Avalonia.Automation.AutomationProperties.SetName(resetAll, "全部复位分栏");
        rows.Add(UiClass.Field(I18nCore.Tr("分栏比例"), resetAll, I18nCore.Tr("复位只删掉存储比例,回到各页现状列定义")));
        return rows.ToArray();
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
        PageBase.Toast((count > 0) ? I18nCore.Tr("已恢复默认") : I18nCore.Tr("没有需要复位的分栏"), (count > 0) ? ("已复位 " + count + " 处") : "", ToastKind.Success);
        RefreshLayoutView();
    }

    // 现状文本:比例 / 像素 / 留白,全部来自 SplitCatalog 的声明
    private static string SplitNowText(SplitSite site)
    {
        if (!site.IsTwoColumn)
        {
            return I18nCore.Tr("没有左右分栏");
        }
        string size = ((site.DefaultPixels > 0.0) ? (site.DefaultPixels + "px") : ("星号 " + Math.Round(site.DefaultRatio * 100.0) + "%"));
        string side = ((site.PixelsSide == "right") ? I18nCore.Tr("右栏") : I18nCore.Tr("左栏"));
        return "现状:" + side + " " + size + " · 留白 " + site.Gap + "px";
    }

    private static string SplitKindLabel(string? kind)
    {
        return kind switch
        {
            SplitKind.TwoColumn => I18nCore.Tr("左右两栏"),
            SplitKind.Rows => I18nCore.Tr("纵向分行"),
            SplitKind.ListRow => I18nCore.Tr("列表行内列宽"),
            SplitKind.GridCols => I18nCore.Tr("网格列数"),
            SplitKind.HeightClamp => I18nCore.Tr("限宽限高"),
            _ => I18nCore.Tr("单列")
        };
    }

    // ————— e 自建区块(Linux 侧补齐:Windows 冻结件该按钮永久禁用) —————

    private Control[] LayoutWidgetRows()
    {
        LayoutV2 doc = LayoutStoreV2.Load();
        List<WidgetDef> widgets = ((doc.Layout != null && doc.Layout.Widgets != null) ? doc.Layout.Widgets : new List<WidgetDef>());
        List<Control> rows = new();

        // 1) 目标页选择器(所有有 BlockHost 宿主的页面)
        List<LayoutBlockEntryCatalog.Entry> pages = LayoutBlockEntryCatalog.All.ToList();
        ComboBox pageCombo = new ComboBox { Width = 210 };
        foreach (LayoutBlockEntryCatalog.Entry entry in pages)
        {
            pageCombo.Items.Add(entry.Label);
        }
        if (pageCombo.Items.Count > 0)
        {
            pageCombo.SelectedIndex = 0;
        }
        Avalonia.Automation.AutomationProperties.SetName(pageCombo, "自建区块加到页面");
        rows.Add(UiClass.Field("加到页面", pageCombo, "选一个页面;添加后会自动跳过去,区块立刻上屏"));

        // 2) 可选部件清单:逐项类型 id + 中文名 + 添加按钮(任务书要求可逐项枚举)
        foreach (string type in WidgetRegistry.SafeTypes)
        {
            string widgetType = type;
            string label = WidgetUI.TypeLabel(type);
            Button add = UiClass.Btn(I18nCore.Tr("添加"), BtnKind.Soft, delegate
            {
                int index = pageCombo.SelectedIndex >= 0 ? pageCombo.SelectedIndex : 0;
                LayoutBlockEntryCatalog.Entry target = pages[index];
                AddWidget(widgetType, target);
            });
            Avalonia.Automation.AutomationProperties.SetName(add, "添加部件:" + label + "(" + type + ")");
            Control right = UiClass.H(10, UiClass.Txt(label, 13, bold: true).VCenter(), UiClass.Small(type).VCenter(), add);
            rows.Add(UiClass.Field("可选部件", right, ""));
        }

        // 3) 已保存的自建区块(带删除入口 —— WidgetUI 的占位文案承诺「删除入口在设置页」)
        if (widgets.Count == 0)
        {
            rows.Add(UiClass.Field(I18nCore.Tr("已保存"), UiClass.Muted("还没有自建区块:上面任意一行点「添加」即可"), ""));
        }
        else
        {
            foreach (WidgetDef widget in widgets.ToList())
            {
                WidgetDef item = widget;
                string title = WidgetUI.TitleOf(item);
                Button del = UiClass.Btn(I18nCore.Tr("删除"), BtnKind.Ghost, delegate
                {
                    DeleteWidget(item.Id);
                });
                Avalonia.Automation.AutomationProperties.SetName(del, "删除部件:" + title);
                Control right = UiClass.H(10, UiClass.Txt(title, 13, bold: true).VCenter(),
                    UiClass.Small(item.Type + " · " + item.Id).VCenter(), del);
                rows.Add(UiClass.Field(I18nCore.Tr("已保存"), right, ""));
            }
        }
        return rows.ToArray();
    }

    /// <summary>添加一个自建区块:先落定义,再切到目标页把块引用接到页面布局末尾(立刻上屏),最后落盘。</summary>
    private void AddWidget(string type, LayoutBlockEntryCatalog.Entry target)
    {
        LayoutV2 doc = LayoutStoreV2.Load();
        string widgetId;
        try
        {
            widgetId = LayoutWidgetOps.AddWidget(doc, type);
        }
        catch (ArgumentException ex)
        {
            PageBase.Toast("不支持的部件类型", ex.Message, ToastKind.Warning);
            return;
        }
        if (!LayoutStoreV2.Save(doc))
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), "写盘失败", ToastKind.Error);
            return;
        }
        string blockId = LayoutWidgetOps.BlockIdOf(widgetId);

        // 切到目标页 → 在页面自己的布局上追加块引用(保留该页既有块与坐标)
        TipCL.App.Shell.MainWindow? win = TopLevel.GetTopLevel(this) as TipCL.App.Shell.MainWindow;
        if (win == null)
        {
            return;
        }
        win.Navigate(target.NavId);
        if (target.NavId == "settings")
        {
            if (target.SettingsView >= 0)
            {
                Select(target.SettingsView);
            }
        }
        else if (target.NavId == "download" && target.Segment >= 0)
        {
            (win.PageHostControl?.Content as TipCL.App.Pages.Download.DownloadPage)?.SelectSegment(target.Segment);
        }
        else if (target.NavId == "plugins" && target.Tab.Length > 0)
        {
            (win.PageHostControl?.Content as TipCL.App.Pages.Plugins.PluginsPage)?.ShowTab(target.Tab);
        }
        Dispatcher.UIThread.Post(delegate
        {
            BlockHost? host = FindBlockHost(win, target.PageId);
            if (host == null)
            {
                // 兜底:页面还没进可视树(懒建/隐藏分段)—— 直接写文档;引擎会把该页缺失的内置块补在它后面
                AppendBlockToDoc(target.PageId, blockId);
                PageBase.Toast(I18nCore.Tr("已添加") + WidgetUI.TypeLabel(type), "区块已写入布局文档,下次打开该页生效", ToastKind.Success);
                return;
            }
            host.Editor.MutatePage(delegate(PageLayoutV2 page)
            {
                LayoutWidgetOps.AppendBlock(page, blockId);
            });
            PageBase.Toast(I18nCore.Tr("已添加") + WidgetUI.TypeLabel(type), "区块已出现在当前页,可在右键「自定义布局...」里拖动", ToastKind.Success);
        }, DispatcherPriority.Loaded);
    }

    private static void AppendBlockToDoc(string pageId, string blockId)
    {
        LayoutV2 doc = LayoutStoreV2.Load();
        if (!doc.Layout.Pages.TryGetValue(pageId, out PageLayoutV2? page) || page == null)
        {
            page = new PageLayoutV2
            {
                Mode = LayoutSchema.ModeStack,
                Gap = LayoutSchema.GapDefault,
                Pad = LayoutSchema.PadDefaultCopy()
            };
            doc.Layout.Pages[pageId] = page;
        }
        LayoutWidgetOps.AppendBlock(page, blockId);
        LayoutStoreV2.Save(doc);
    }

    /// <summary>删除自建区块定义 + 所有页面里对它的引用(数据整体退场,不留「不可用」占位)。</summary>
    private void DeleteWidget(string widgetId)
    {
        if (string.IsNullOrEmpty(widgetId))
        {
            return;
        }
        LayoutV2 doc = LayoutStoreV2.Load();
        LayoutWidgetOps.RemoveWidget(doc, widgetId);
        if (!LayoutStoreV2.Save(doc))
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), "写盘失败", ToastKind.Error);
            return;
        }
        PageBase.Toast("已删除自建区块", widgetId, ToastKind.Success);
        RefreshLayoutView();
    }

    // ————— f 整机布局方案 —————

    private Control[] LayoutProfileRows()
    {
        List<Control> rows = new();
        Button create = UiClass.Btn(I18nCore.Tr("新建布局方案"), BtnKind.Primary, delegate
        {
            Run(NewProfileAsync, I18nCore.Tr("布局未能保存"));
        });
        Button import = UiClass.Btn(I18nCore.Tr("导入布局"), BtnKind.Chip, delegate
        {
            Run(ImportLayoutFileAsync, I18nCore.Tr("导入失败"));
        });
        Button export = UiClass.Btn(I18nCore.Tr("导出布局"), BtnKind.Chip, delegate
        {
            Run(ExportLayoutAsync, I18nCore.Tr("导出失败"));
        });
        Avalonia.Automation.AutomationProperties.SetName(create, I18nCore.Tr("新建布局方案"));
        Avalonia.Automation.AutomationProperties.SetName(import, "导入布局文件");
        Avalonia.Automation.AutomationProperties.SetName(export, "导出布局文件");
        string active = LayoutProfiles.ActiveName();
        string activeText = ((active.Length > 0) ? ("当前方案:" + active) : I18nCore.Tr("当前是直接编辑态"));
        rows.Add(UiClass.Field(I18nCore.Tr("布局方案"), UiClass.H(8, create, import, export), activeText));
        List<string> names = LayoutProfiles.Names();
        if (names.Count == 0)
        {
            rows.Add(UiClass.Field(I18nCore.Tr("还没有方案"), UiClass.Muted(I18nCore.Tr("点「新建布局方案」把当前这套布局存下来")), ""));
            return rows.ToArray();
        }
        foreach (string name in names)
        {
            rows.Add(ProfileRow(name, name == active));
        }
        return rows.ToArray();
    }

    private Control ProfileRow(string name, bool active)
    {
        StackPanel title = UiClass.H(8, UiClass.Txt(name, 13.5, true).VCenter());
        if (active)
        {
            title.Children.Add(UiClass.Tag(I18nCore.Tr("当前"), "B.OnAccentStrong", "B.AccentStrong").VCenter());
        }
        Button apply = UiClass.Btn(I18nCore.Tr("应用"), BtnKind.Soft, delegate
        {
            ApplyProfile(name);
        });
        Button rename = UiClass.Btn(I18nCore.Tr("重命名"), BtnKind.Chip, delegate
        {
            Run(() => RenameProfileAsync(name), I18nCore.Tr("布局未能保存"));
        });
        Button copy = UiClass.Btn(I18nCore.Tr("复制一份"), BtnKind.Chip, delegate
        {
            Run(() => DuplicateProfileAsync(name), I18nCore.Tr("布局未能保存"));
        });
        Button del = UiClass.Btn(I18nCore.Tr("删除"), BtnKind.Ghost, delegate
        {
            Run(() => DeleteProfileAsync(name), I18nCore.Tr("布局未能保存"));
        });
        Avalonia.Automation.AutomationProperties.SetName(apply, "应用方案:" + name);
        Grid row = UiClass.G(null, "*,Auto");
        row.Add(title.VCenter(), 0);
        row.Add(UiClass.H(6, apply, rename, copy, del), 0, 1);
        return row;
    }

    private async System.Threading.Tasks.Task NewProfileAsync()
    {
        string? name = await Dlg.Prompt(I18nCore.Tr("新建布局方案"), I18nCore.Tr("方案名"), "我的布局 " + DateTime.Now.ToString("MM-dd HH:mm"));
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
        PageBase.Toast("布局方案「" + name + "」已保存", I18nCore.Tr("以当前布局为基线"), ToastKind.Success);
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
        PageBase.Toast("布局方案「" + name + "」已应用", (kept.Length > 0) ? ("原来的布局已存为方案「" + kept + "」,可随时切回") : "", ToastKind.Success);
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
        string? next = await Dlg.Prompt(I18nCore.Tr("复制一份"), I18nCore.Tr("方案名"), name + " 副本");
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
        RefreshLayoutView();
    }

    private async System.Threading.Tasks.Task DeleteProfileAsync(string name)
    {
        if (!await Dlg.Confirm(I18nCore.Tr("删除"), "删除布局方案「" + name + "」?", I18nCore.Tr("删除"), I18nCore.Tr("取消"), danger: true))
        {
            return;
        }
        string error = LayoutProfiles.Delete(name);
        if (error.Length > 0)
        {
            PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
            return;
        }
        PageBase.Toast("布局方案「" + name + "」已删除", "", ToastKind.Success);
        RefreshLayoutView();
    }

    // ————— f2 导入 / 导出 —————

    private async System.Threading.Tasks.Task ExportLayoutAsync()
    {
        int choice = await Dlg.Choose(I18nCore.Tr("导出布局"), UiClass.Muted(I18nCore.Tr("整机方案含页签 / 区块 / 分栏比例 / 自建区块;兼容 v1 的页面布局只含每页区块的顺序与显隐")), new string[3]
        {
            I18nCore.Tr("取消"),
            I18nCore.Tr("导出整机方案(v2)"),
            I18nCore.Tr("导出兼容 v1 的页面布局")
        }, 560);
        if (choice == 1)
        {
            string text = LayoutStoreV2.ExportJson(LayoutStoreV2.Load());
            string? path = await Dlg.SaveFile(I18nCore.Tr("导出整机方案"), "tipcl-layout-v2.json");
            if (path != null && WriteTextFile(path, text))
            {
                PageBase.Toast(I18nCore.Tr("导出布局"), path, ToastKind.Success);
            }
            return;
        }
        if (choice == 2)
        {
            string text = PageLayoutStore.ExportJson(LayoutV1Projection.ProjectV1(LayoutStoreV2.Load()));
            string? path2 = await Dlg.SaveFile(I18nCore.Tr("导出兼容 v1 的页面布局"), "tipcl-page-layout.json");
            if (path2 != null && WriteTextFile(path2, text))
            {
                PageBase.Toast(I18nCore.Tr("导出布局"), path2, ToastKind.Success);
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
            int choice = await Dlg.Choose(I18nCore.Tr("导入布局"), UiClass.Muted(I18nCore.Tr("这是一份整机方案(含页签 / 区块 / 分栏比例)")), new string[3]
            {
                I18nCore.Tr("取消"),
                I18nCore.Tr("应用为当前布局"),
                I18nCore.Tr("存为方案")
            }, 560);
            if (choice == 1)
            {
                string error = LayoutProfiles.ApplySnapshot(whole.Layout, out string kept);
                if (error.Length > 0)
                {
                    PageBase.Toast(I18nCore.Tr("布局未能保存"), error, ToastKind.Warning);
                    return;
                }
                PageBase.Toast("布局已应用", (kept.Length > 0) ? ("原来的布局已存为方案「" + kept + "」,可随时切回") : Path.GetFileName(path), ToastKind.Success);
                RefreshLayoutView();
            }
            else if (choice == 2)
            {
                string? name = await Dlg.Prompt(I18nCore.Tr("存为方案"), I18nCore.Tr("方案名"), Path.GetFileNameWithoutExtension(path));
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
                PageBase.Toast("布局方案「" + name + "」已保存", "", ToastKind.Success);
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
            PageBase.Toast("已导入 " + num + " 个页面的布局(下次打开对应页面生效)", Path.GetFileName(path), ToastKind.Success);
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

    // ————— g 高级:布局 JSON 编辑器 —————

    private Control LayoutJsonCard()
    {
        TextBox box = UiClass.LogBox();
        box.IsReadOnly = false;
        box.AcceptsTab = true;
        box.Height = 220;
        box.Text = ((_layoutJsonText.Length > 0) ? _layoutJsonText : LayoutJsonEditor.Text(LayoutStoreV2.Load()));
        box.TextChanged += delegate
        {
            _layoutJsonText = box.Text ?? "";
        };
        _layoutJsonIssues = UiClass.V(4);
        Button check = UiClass.Btn(I18nCore.Tr("校验"), BtnKind.Normal, delegate
        {
            InspectJson(box, apply: false);
        });
        Button apply = UiClass.Btn(I18nCore.Tr("应用"), BtnKind.Primary, delegate
        {
            InspectJson(box, apply: true);
        });
        Button reload = UiClass.Btn(I18nCore.Tr("刷新"), BtnKind.Chip, delegate
        {
            _layoutJsonText = "";
            box.Text = LayoutJsonEditor.Text(LayoutStoreV2.Load());
            RenderJsonIssues(new List<LayoutJsonIssue>());
        });
        Avalonia.Automation.AutomationProperties.SetName(check, "校验布局 JSON");
        Avalonia.Automation.AutomationProperties.SetName(apply, "应用布局 JSON");
        Avalonia.Automation.AutomationProperties.SetName(reload, "刷新布局 JSON");
        reload.Content = I18nCore.Tr("刷新");
        ToolTip.SetTip(reload, I18nCore.Tr("重新载入当前布局"));
        return UiClass.Card(UiClass.V(8, UiClass.Field(I18nCore.Tr("布局 JSON"), UiClass.V(8, box, UiClass.H(8, check, apply, reload)), "可以直接编辑 / 粘贴;点「校验」看结果,校验通过才会落盘"), _layoutJsonIssues));
    }

    private void InspectJson(TextBox box, bool apply)
    {
        string text = box.Text ?? "";
        LayoutV2? doc;
        List<LayoutJsonIssue> issues = (apply ? LayoutJsonEditor.Apply(text, out doc) : LayoutJsonEditor.Inspect(text, out doc));
        RenderJsonIssues(issues);
        bool failed = issues.Any((LayoutJsonIssue i) => i.Error);
        if (failed)
        {
            PageBase.Toast(I18nCore.Tr("布局 JSON 有错误"), I18nCore.Tr("看下面的具体字段"), ToastKind.Error);
            return;
        }
        if (apply)
        {
            _layoutJsonText = "";
            PageBase.Toast(I18nCore.Tr("布局已保存"), (issues.Count > 0) ? ("有 " + issues.Count + " 处按契约钳位 / 忽略") : "", ToastKind.Success);
            RefreshShell();
            RefreshLayoutView();
        }
        else
        {
            PageBase.Toast(I18nCore.Tr("校验通过"), (issues.Count > 0) ? ("有 " + issues.Count + " 处按契约钳位 / 忽略") : "", ToastKind.Success);
        }
    }

    private void RenderJsonIssues(List<LayoutJsonIssue> issues)
    {
        if (_layoutJsonIssues == null)
        {
            return;
        }
        _layoutJsonIssues.Children.Clear();
        foreach (LayoutJsonIssue issue in issues)
        {
            TextBlock line = UiClass.Txt((issue.Path.Length > 0) ? (issue.Path + ": " + issue.Message) : issue.Message, 12, false, issue.Error ? "B.Danger" : "B.Warn");
            line.TextWrapping = TextWrapping.Wrap;
            _layoutJsonIssues.Children.Add(line);
        }
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