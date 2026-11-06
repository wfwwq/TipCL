using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

public sealed class TasksPage : PageBase
{
	private sealed class Card
	{
		public bool Expanded;

		public DateTime LogPainted;

		public required Border Root { get; init; }

		public required TextBlock Title { get; init; }

		public required TextBlock Status { get; init; }

		public required TextBlock Speed { get; init; }

		public required ProgressBar Bar { get; init; }

		public required Button Cancel { get; init; }

		public required Button Toggle { get; init; }

		public required Button Crash { get; init; }

		public required Border LogWrap { get; init; }

		public required TextBox Log { get; init; }
	}

	private readonly SPanel _list = Ui.V(10.0);

	private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();

	private readonly TextBlock _summary = Ui.Muted("");

	private static readonly TimeSpan LogInterval = TimeSpan.FromMilliseconds(100.0);

	public override string Title => I18n.L("下载任务");

	public TasksPage()
	{
		Button button = Ui.Btn(I18n.L("清除已完成"), BtnKind.Chip, delegate
		{
			TaskStore.ClearFinished();
			Rebuild();
		}, "\uea99");
		Grid grid = Ui.Section(I18n.L("下载任务"), I18n.L("安装、下载、启动都在这里排队"), Ui.H(8.0, _summary.VCenter(), button));
		base.Content = PageBase.ScrollBody(HubRow(), grid, _list);
		EnableDrop(() => FileDrop.DropContext.None);
		TaskStore.Added += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(Rebuild));
		};
		TaskStore.Updated += delegate(TaskRow r)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				Sync(r);
			});
		};
		TaskStore.Cleared += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(Rebuild));
		};
	}

	protected override Task LoadAsync()
	{
		Rebuild();
		return Task.CompletedTask;
	}

	// 下载枢纽：顶栏 4 页签壳砍掉了旧侧栏「下载」分区，这里把该分区的全部子页
	// （原版游戏 / Mod / 模组管理 / 整合包 / 数据包 / 资源包 / 光影包 / 世界 / Java）
	// 做成一行 chip，保证旧版每个入口在页签内 1 击可达、功能零缺失。
	// 不造数据：只是导航到既有页面，页面内容仍走各自的数据源。
	private static UIElement HubRow()
	{
		(string Id, string Label, string Glyph)[] items = new(string, string, string)[9]
		{
			("version", I18n.L("原版游戏"), "\ue7fc"),
			("mod", "Mod", "\uea86"),
			("mods", I18n.L("模组管理"), "\ue8fd"),
			("modpack", I18n.L("整合包"), "\ue7b8"),
			("datapack", I18n.L("数据包"), "\ue8f1"),
			("resourcepack", I18n.L("资源包"), "\ueb9f"),
			("shader", I18n.L("光影包"), "\ue706"),
			("world", I18n.L("世界"), "\ue909"),
			("java", "Java", "\uec32"),
		};
		WrapPanel wrap = new WrapPanel();
		foreach ((string id, string label, string glyph) in items)
		{
			Button b = Ui.Btn(label, BtnKind.Chip, delegate
			{
				PageBase.Win?.Navigate(id);
			}, glyph);
			b.Padding = new Thickness(12.0, 4.0, 13.0, 5.0);
			b.Margin = new Thickness(0.0, 0.0, 8.0, 8.0);
			wrap.Children.Add(b);
		}
		return wrap;
	}

	public override void OnShown()
	{
		Rebuild();
	}

	private void Rebuild()
	{
		_list.Children.Clear();
		_cards.Clear();
		List<TaskRow> list = TaskStore.Rows.Reverse().ToList();
		_summary.Text = ((list.Count == 0) ? "" : I18n.L("进行中 {0} · 共 {1}", TaskStore.RunningCount, list.Count));
		if (list.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue896", I18n.L("还没有任务"), I18n.L("点上方入口装个版本或模组试试")));
			return;
		}
		foreach (TaskRow item in list)
		{
			Card card = Build(item);
			_cards[item.Id] = card;
			_list.Children.Add(card.Root);
			Sync(item);
		}
		Motion.Stagger(_list, 24.0);
		PageBase.Run(ResolveTitlesAsync);
	}

	private async Task ResolveTitlesAsync()
	{
		foreach (TaskRow row in TaskStore.Rows.Where((TaskRow r) => r.Title == TaskStore.PlaceholderTitle).ToList())
		{
			string text = (await PageBase.Api.TryCallAsync("task_title", new
			{
				task_id = row.Id
			}, "")) ?? "";
			if (text.Length != 0 && !(text == row.Id) && !(text == row.Title))
			{
				row.Title = text;
				if (_cards.TryGetValue(row.Id, out Card value))
				{
					value.Title.Text = text;
				}
			}
		}
	}

	private Card Build(TaskRow row)
	{
		TextBlock textBlock = Ui.Txt(row.Title, 13.5, bold: true).Trim();
		TextBlock textBlock2 = Ui.Muted("");
		TextBlock textBlock3 = Ui.Small("");
		ProgressBar progressBar = Ui.Prog();
		Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost);
		Button button = Ui.Btn(I18n.L("日志"), BtnKind.Chip, null, "\ue8fd");
		Button button2 = Ui.Btn(I18n.L("崩溃分析"), BtnKind.Chip, null, "\uebe8");
		button2.Visibility = Visibility.Collapsed;
		TextBox textBox = Ui.LogBox();
		textBox.Height = 160.0;
		textBox.BorderThickness = new Thickness(0.0);
		textBox.Background = Brushes.Transparent;
		Border logWrap = Ui.Pane(textBox, "B.Paper2", 9.0, 10.0);
		logWrap.Visibility = Visibility.Collapsed;
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(Ui.V(3.0, textBlock, textBlock2).VCenter(), 0);
		grid.Add(Ui.H(6.0, button2, button, cancel).VCenter(), 0, 1);
		Grid grid2 = Ui.G(null, "*,Auto");
		grid2.Add(progressBar.VCenter(), 0);
		grid2.Add(textBlock3.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Border border = Ui.Card(Ui.V(9.0, grid, grid2, logWrap), 14.0);
		Motion.HoverLift(border, 1.004, 1.0, 16.0);
		Card card = new Card
		{
			Root = border,
			Title = textBlock,
			Status = textBlock2,
			Speed = textBlock3,
			Bar = progressBar,
			Cancel = cancel,
			Toggle = button,
			Crash = button2,
			LogWrap = logWrap,
			Log = textBox
		};
		cancel.Click += delegate
		{
			PageBase.Run(async delegate
			{
				if (row.Finished)
				{
					TaskStore.Remove(row.Id);
					Rebuild();
				}
				else
				{
					cancel.IsEnabled = false;
					await PageBase.Api.TryCallAsync<object>("cancel_task", new
					{
						task_id = row.Id
					});
				}
			});
		};
		button2.Click += delegate
		{
			PageBase.Run(async delegate
			{
				CrashReport crashReport = await PageBase.Api.TryCallAsync<CrashReport>("get_crash", new
				{
					task_id = row.Id
				});
				if (crashReport == null || string.IsNullOrWhiteSpace(crashReport.Title + crashReport.Headline + crashReport.Summary + crashReport.Detail))
				{
					PageBase.Toast(I18n.L("没有崩溃分析"), I18n.L("这个任务没有留下崩溃报告"));
				}
				else
				{
					if (string.IsNullOrEmpty(crashReport.TaskId))
					{
						crashReport.TaskId = row.Id;
					}
					await CrashUi.ShowAsync(crashReport, gameError: true);
				}
			});
		};
		button.Click += delegate
		{
			card.Expanded = !card.Expanded;
			if (card.Expanded)
			{
				card.Log.Text = row.Log.ToString();
				card.Log.ScrollToEnd();
				logWrap.Visibility = Visibility.Visible;
				Motion.FadeIn(logWrap, 200.0, 8.0);
			}
			else
			{
				logWrap.Visibility = Visibility.Collapsed;
			}
		};
		return card;
	}

	private void Sync(TaskRow row)
	{
		if (!_cards.TryGetValue(row.Id, out Card value))
		{
			Rebuild();
			return;
		}
		value.Title.Text = row.Title;
		value.Status.Text = row.Status;
		value.Speed.Text = row.Speed;
		value.Bar.IsIndeterminate = row.Indeterminate && !row.Finished;
		if (!value.Bar.IsIndeterminate)
		{
			Motion.Progress(value.Bar, row.Progress);
		}
		value.Cancel.IsEnabled = !row.Finished;
		value.Cancel.Content = (row.Finished ? I18n.L("清除") : I18n.L("取消"));
		value.Crash.Show(row.Finished && !row.Success);
		value.Bar.SetResourceReference(Control.ForegroundProperty, (row.Finished && !row.Success) ? "B.Danger" : "B.Accent");
		if (value.Expanded && (row.Finished || DateTime.UtcNow - value.LogPainted >= LogInterval))
		{
			value.LogPainted = DateTime.UtcNow;
			bool num = value.Log.VerticalOffset >= value.Log.ExtentHeight - value.Log.ViewportHeight - 20.0;
			value.Log.Text = row.Log.ToString();
			if (num)
			{
				value.Log.ScrollToEnd();
			}
		}
		_summary.Text = I18n.L("进行中 {0} · 共 {1}", TaskStore.RunningCount, TaskStore.Rows.Count);
	}
}
