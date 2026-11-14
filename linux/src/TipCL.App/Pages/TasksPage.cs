using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages;

// 下载任务页(移植自 Windows 版 Pages/TasksPage.cs,Avalonia 重写):
// 概览(进行中 N · 共 M + 清除已完成)+ 任务列表;每张卡 = 标题 / 状态 / 进度条 / 速度
// + 取消(完成后变「清除」)+ 日志折叠框。数据源 = TaskStore(事件驱动增量刷新)。
// 与 Windows 的差异:崩溃分析钮(Windows 走 CrashUi)未移植,本版不呈现该钮(不做死按钮)。
public sealed class TasksPage : PageBase
{
    private sealed class Card
    {
        public bool Expanded;

        public DateTime LogPainted;

        public Border Root = null!;

        public TextBlock Title = null!;

        public TextBlock Status = null!;

        public TextBlock Speed = null!;

        public ProgressBar Bar = null!;

        public Button Cancel = null!;

        public Button Toggle = null!;

        public Border LogWrap = null!;

        public TextBox Log = null!;
    }

    private static readonly TimeSpan LogInterval = TimeSpan.FromMilliseconds(100);

    private readonly StackPanel _list = UiClass.V(10);

    private readonly Dictionary<string, Card> _cards = new();

    private readonly TextBlock _summary = UiClass.Muted("");

    private bool _wired;

    public TasksPage() : base("tasks", "下载任务")
    {
        var clear = UiClass.Btn("清除已完成", BtnKind.Chip, (_, _) =>
        {
            TaskStore.ClearFinished();
            Rebuild();
        });
        var header = UiClass.Section("下载任务", "安装、下载、启动都在这里排队",
            UiClass.H(8, _summary.VCenter(), clear));
        Body = new BlockHost("tasks", new List<BlockSpec>
        {
            new() { Id = "header", Title = "概览", Content = header },
            new() { Id = "list", Title = "任务列表", Content = _list },
        }).Root;
    }

    protected override void OnCreate()
    {
        if (!_wired)
        {
            _wired = true;
            // TaskStore 的事件统一在 UI 线程派发(Emit),这里直接改控件
            TaskStore.Added += _ => Rebuild();
            TaskStore.Updated += Sync;
            TaskStore.Cleared += Rebuild;
        }
        Rebuild();
    }

    protected override void OnActivate() => Rebuild();

    private void Rebuild()
    {
        _list.Children.Clear();
        _cards.Clear();
        var rows = TaskStore.Rows.Reverse().ToList();
        _summary.Text = rows.Count == 0 ? "" : $"进行中 {TaskStore.RunningCount} · 共 {rows.Count}";
        if (rows.Count == 0)
        {
            _list.Children.Add(UiClass.Empty("\ue896", "还没有任务", "到「下载」页装个版本或模组试试"));
            return;
        }
        foreach (var row in rows)
        {
            var card = Build(row);
            _cards[row.Id] = card;
            _list.Children.Add(card.Root);
            Sync(row);
        }
        Motion.Stagger(_list, 24);
    }

    private Card Build(TaskRow row)
    {
        var title = UiClass.Txt(row.Title, 13.5, bold: true).Trim();
        var status = UiClass.Muted("");
        var speed = UiClass.Small("");
        var bar = UiClass.Prog();
        var cancel = UiClass.Btn("取消", BtnKind.Ghost);
        var toggle = UiClass.Btn("日志", BtnKind.Chip);

        var log = UiClass.LogBox();
        log.Height = 160;
        log.BorderThickness = new Thickness(0);
        log.Background = Avalonia.Media.Brushes.Transparent;
        var logWrap = UiClass.Pane(log, "B.Paper2", 9, 10);
        logWrap.IsVisible = false;

        var head = UiClass.G(null, "*,Auto");
        head.Add(UiClass.V(3, title, status).VCenter(), 0, 0);
        head.Add(UiClass.H(6, toggle, cancel).VCenter(), 0, 1);

        var barRow = UiClass.G(null, "*,Auto");
        barRow.Add(bar.VCenter(), 0, 0);
        barRow.Add(speed.Mk(10, 0, 0, 0).VCenter(), 0, 1);

        var root = UiClass.Card(UiClass.V(9, head, barRow, logWrap), 14);
        Motion.HoverLift(root, 1.004);

        var card = new Card
        {
            Root = root,
            Title = title,
            Status = status,
            Speed = speed,
            Bar = bar,
            Cancel = cancel,
            Toggle = toggle,
            LogWrap = logWrap,
            Log = log,
        };

        cancel.Click += (_, _) =>
        {
            if (row.Finished)
            {
                TaskStore.Remove(row.Id);
                Rebuild();
            }
            else
            {
                cancel.IsEnabled = false;
                Api.CancelTask(row.Id);
            }
        };

        toggle.Click += (_, _) =>
        {
            card.Expanded = !card.Expanded;
            if (card.Expanded)
            {
                card.Log.Text = row.LogText;
                card.Log.CaretIndex = card.Log.Text?.Length ?? 0;
                logWrap.IsVisible = true;
                Motion.FadeIn(logWrap, 200, 8);
            }
            else
            {
                logWrap.IsVisible = false;
            }
        };

        return card;
    }

    private void Sync(TaskRow row)
    {
        if (!_cards.TryGetValue(row.Id, out var card))
        {
            Rebuild();
            return;
        }
        card.Title.Text = row.Title;
        card.Status.Text = row.Status;
        card.Speed.Text = row.Speed;
        card.Bar.IsIndeterminate = row.Indeterminate && !row.Finished;
        if (!card.Bar.IsIndeterminate)
            Motion.Progress(card.Bar, row.Progress);
        card.Cancel.IsEnabled = !row.Finished;
        card.Cancel.Content = row.Finished ? "清除" : "取消";
        card.Bar.Dyn(ProgressBar.ForegroundProperty, row.Finished && !row.Success ? "B.Danger" : "B.Accent");
        if (card.Expanded && (row.Finished || DateTime.UtcNow - card.LogPainted >= LogInterval))
        {
            card.LogPainted = DateTime.UtcNow;
            card.Log.Text = row.Log.ToString();
            card.Log.CaretIndex = card.Log.Text?.Length ?? 0;
        }
        _summary.Text = $"进行中 {TaskStore.RunningCount} · 共 {TaskStore.Rows.Count}";
    }
}
