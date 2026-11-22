// [LAYOUT-V3-D 2026-11-20] 「设置 → 布局 → 高级 → 布局文件 → 在编辑器中打开…」的独立 JSON 编辑窗口。
// v3 效果图要求 JSON 不再常驻在设置页里,改成单独的编辑器窗口:带校验、保存前先预览。
// 校验 / 落盘逻辑一字不改,全部复用 LayoutJsonEditor(Inspect / Apply,错误时不落盘);
// 本文件只管界面:文本框 + 问题清单 + 「重新载入 / 校验 / 应用并保存 / 关闭」。
// 非模态(Show(owner)),同一时间只开一个:再点一次「在编辑器中打开…」只是把已开的窗口提到前面。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using UiClass = TipCL.App.Ui.Ui;
using ToastKind = TipCL.Core.Layout.ToastKind;

namespace TipCL.App.Pages.Layout;

/// <summary>整套布局的 JSON 编辑器窗口(校验 / 应用走 <see cref="LayoutJsonEditor"/>)。</summary>
public sealed class LayoutJsonWindow : Window
{
    private static LayoutJsonWindow? _current;

    private readonly TextBox _box;

    private readonly StackPanel _issues;

    private readonly TextBlock _status;

    private readonly Action? _applied;

    // 最近一次从磁盘载入 / 成功应用后的文本:与它不同 = 有未保存的修改
    private string _loaded = "";

    private bool _closeConfirmed;

    /// <summary>当前开着的那一个(没有为 null);测试与壳层用。</summary>
    public static LayoutJsonWindow? Current => _current;

    /// <summary>
    /// 打开(或提到前面)JSON 编辑窗口。<paramref name="applied"/> = 成功应用并落盘后的回调
    /// (设置页用它重建「布局」视图 + 对齐标题栏)。
    /// </summary>
    public static LayoutJsonWindow ShowFor(Window? owner, Action? applied)
    {
        if (_current != null)
        {
            _current.Activate();
            return _current;
        }
        LayoutJsonWindow win = new LayoutJsonWindow(applied);
        _current = win;
        win.Closed += delegate
        {
            if (ReferenceEquals(_current, win))
            {
                _current = null;
            }
        };
        if (owner != null)
        {
            win.Show(owner);
        }
        else
        {
            win.Show();
        }
        return win;
    }

    private LayoutJsonWindow(Action? applied)
    {
        _applied = applied;
        Title = I18nCore.Tr("布局文件");
        Width = 760;
        Height = 640;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = UiClass.LogBox();
        _box.IsReadOnly = false;
        _box.AcceptsReturn = true;
        _box.AcceptsTab = true;
        _box.TextWrapping = TextWrapping.NoWrap;
        _box.VerticalAlignment = VerticalAlignment.Stretch;
        _box.HorizontalAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(_box, I18nCore.Tr("布局 JSON"));

        _issues = UiClass.V(4);
        _status = UiClass.Small("");
        _status.TextWrapping = TextWrapping.Wrap;

        Button reload = UiClass.Btn(I18nCore.Tr("重新载入"), BtnKind.Chip, delegate
        {
            Reload();
        });
        ToolTip.SetTip(reload, I18nCore.Tr("丢掉这里的修改，重新读取当前布局"));
        Button check = UiClass.Btn(I18nCore.Tr("校验"), BtnKind.Normal, delegate
        {
            Validate();
        });
        Button apply = UiClass.Btn(I18nCore.Tr("应用并保存"), BtnKind.Primary, delegate
        {
            ApplyAsync();
        });
        Button close = UiClass.Btn(I18nCore.Tr("关闭"), BtnKind.Ghost, delegate
        {
            Close();
        });
        Avalonia.Automation.AutomationProperties.SetName(reload, I18nCore.Tr("重新载入布局 JSON"));
        Avalonia.Automation.AutomationProperties.SetName(check, I18nCore.Tr("校验布局 JSON"));
        Avalonia.Automation.AutomationProperties.SetName(apply, I18nCore.Tr("应用布局 JSON"));

        TextBlock heading = UiClass.Txt(I18nCore.Tr("用 JSON 编辑整套布局"), 17, bold: true, fg: "B.Ink");
        TextBlock hint = UiClass.Small(I18nCore.Tr("可以直接编辑或粘贴。先点「校验」看看会改成什么样，校验通过才会保存；有错误时什么都不会写入。"));
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 4, 0, 10);

        ScrollViewer issueScroll = new ScrollViewer
        {
            Content = _issues,
            MaxHeight = 120,
            Margin = new Thickness(0, 8, 0, 0),
        };

        Grid footer = UiClass.G(null, "*,Auto");
        footer.Margin = new Thickness(0, 12, 0, 0);
        footer.Add(_status.VCenter(), 0);
        footer.Add(UiClass.H(8, reload, check, apply, close), 0, 1);

        Grid root = UiClass.G("Auto,Auto,*,Auto,Auto", null);
        root.Add(heading, 0);
        root.Add(hint, 1);
        root.Add(_box, 2);
        root.Add(issueScroll, 3);
        root.Add(footer, 4);

        Border card = new Border
        {
            Padding = new Thickness(18),
            Child = root,
        };
        card[!Border.BackgroundProperty] = Res.Bind("B.Paper");
        Content = card;

        Reload();
        _box.TextChanged += delegate
        {
            UpdateDirty();
        };
        Closing += (sender, e) =>
        {
            if (_closeConfirmed || !IsDirty)
            {
                return;
            }
            e.Cancel = true;
            ConfirmCloseAsync();
        };
    }

    /// <summary>编辑框里是否有还没应用的修改。</summary>
    public bool IsDirty => !string.Equals(_box.Text ?? "", _loaded, StringComparison.Ordinal);

    private void Reload()
    {
        _loaded = LayoutJsonEditor.Text(LayoutStoreV2.Load());
        _box.Text = _loaded;
        RenderIssues(new List<LayoutJsonIssue>());
        _status.Text = I18nCore.Tr("这是当前正在使用的整套布局。");
    }

    private void UpdateDirty()
    {
        if (IsDirty)
        {
            _status.Text = I18nCore.Tr("有未保存的修改。");
        }
    }

    private void Validate()
    {
        List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(_box.Text ?? "", out LayoutV2? doc);
        RenderIssues(issues);
        if (LayoutJsonEditor.HasErrors(issues))
        {
            _status.Text = I18nCore.Tr("有错误，不能保存：看下面的具体字段。");
            return;
        }
        string summary = LayoutJsonEditor.Summary(doc);
        _status.Text = (issues.Count > 0)
            ? Py.Fmt(I18nCore.Tr("校验通过（{0} 处会按规则自动修正）：{1}"), issues.Count, summary)
            : Py.Fmt(I18nCore.Tr("校验通过：{0}"), summary);
    }

    // 应用 = 先校验 → 预览确认 → LayoutJsonEditor.Apply(无硬错误才落盘)
    private async void ApplyAsync()
    {
        try
        {
            string text = _box.Text ?? "";
            List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(text, out LayoutV2? preview);
            RenderIssues(issues);
            if (LayoutJsonEditor.HasErrors(issues))
            {
                _status.Text = I18nCore.Tr("有错误，不能保存：看下面的具体字段。");
                LayoutFx.ShowToast(I18nCore.Tr("布局 JSON 有错误"), I18nCore.Tr("看编辑器下面的具体字段"), ToastKind.Error);
                return;
            }
            bool ok = await Dlg.Confirm(I18nCore.Tr("应用这份布局"),
                Py.Fmt(I18nCore.Tr("会替换当前的整套布局：{0}。确定吗？"), LayoutJsonEditor.Summary(preview)),
                I18nCore.Tr("应用并保存"), I18nCore.Tr("取消"), owner: this);
            if (!ok)
            {
                return;
            }
            List<LayoutJsonIssue> applied = LayoutJsonEditor.Apply(text, out LayoutV2? _);
            RenderIssues(applied);
            if (LayoutJsonEditor.HasErrors(applied))
            {
                _status.Text = I18nCore.Tr("没能保存：看下面的具体原因。");
                LayoutFx.ShowToast(I18nCore.Tr("布局未能保存"), I18nCore.Tr("看编辑器下面的具体原因"), ToastKind.Error);
                return;
            }
            // 落盘后按存档重新载入(Sanitize 过的样子),修改标记随之清掉
            _loaded = LayoutJsonEditor.Text(LayoutStoreV2.Load());
            _box.Text = _loaded;
            _status.Text = I18nCore.Tr("已保存，所有页面已按新布局重排。");
            LayoutFx.ShowToast(I18nCore.Tr("布局已保存"), (applied.Count > 0) ? Py.Fmt(I18nCore.Tr("有 {0} 处按规则自动修正"), applied.Count) : "", ToastKind.Success);
            _applied?.Invoke();
        }
        catch (Exception ex)
        {
            LayoutFx.ShowToast(I18nCore.Tr("布局未能保存"), ex.Message, ToastKind.Error);
        }
    }

    private async void ConfirmCloseAsync()
    {
        try
        {
            bool discard = await Dlg.Confirm(I18nCore.Tr("关闭布局文件"), I18nCore.Tr("这里的修改还没有保存，关掉就没了。"), I18nCore.Tr("不保存，关闭"), I18nCore.Tr("取消"), danger: true, owner: this);
            if (discard)
            {
                _closeConfirmed = true;
                Close();
            }
        }
        catch (Exception)
        {
            // 确认框起不来就保持窗口打开,修改不丢
        }
    }

    private void RenderIssues(List<LayoutJsonIssue> issues)
    {
        _issues.Children.Clear();
        foreach (LayoutJsonIssue issue in issues)
        {
            TextBlock line = UiClass.Txt(LayoutJsonEditor.Describe(issue), 12, false, issue.Error ? "B.Danger" : "B.Warn");
            line.TextWrapping = TextWrapping.Wrap;
            _issues.Children.Add(line);
        }
    }
}
