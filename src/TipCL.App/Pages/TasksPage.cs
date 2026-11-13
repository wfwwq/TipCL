using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C5b:Windows 版 <c>Pages\TasksPage.cs</c>(7227 字节 / 263 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「下载任务」页(导航表里 id = <c>tasks</c>,标题「下载任务」,图标 <c>\ue9d5</c>)。
///
/// 形状(逐条照抄 Windows):顶部一张 <c>Ui.Section</c> 概览(「进行中 N · 共 M」+「清除已完成」)/
/// 下面一列任务卡,每张 = 标题 + 状态副行 + 进度条 + 右上的「崩溃分析 / 日志 / 取消(清除)」。
/// 「日志」把这张卡展开成一块只读日志框(最高 100ms 刷一次、贴底才自动滚)。
///
/// 数据源与调用名一字未改:<c>task_title</c> / <c>cancel_task</c> / <c>get_crash</c>;
/// 任务本身来自 <see cref="TaskStore"/>(外壳把 <c>task_added</c> / <c>progress</c> / <c>finished</c>
/// 事件喂进去,本页只订阅它的 <c>Added</c> / <c>Updated</c> / <c>Cleared</c> 三个通知)。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Visibility.Collapsed</c> / <c>Visible</c> → <c>IsVisible = false</c> / <c>true</c>
///   (这里有 <c>UiExt.Show(bool)</c> 的既有门面,与 Windows 的 <c>UIElement.Visibility</c> 一一对应);</item>
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>(本文件只经 <c>Ui</c> 拿控件);</item>
/// <item><c>base.Dispatcher.BeginInvoke(...)</c> → <c>Dispatcher.UIThread.Post(...)</c>(同 D-105 / C2c 的既有口径);</item>
/// <item><c>ProgressBar.SetResourceReference(ForegroundProperty, key)</c> → <c>Dyn(TemplatedControl.ForegroundProperty, key)</c>
///   (WPF 的 <c>SetResourceReference</c> 在 Avalonia 没有等价物,<c>UiExt.Dyn</c> 就是全仓统一的替代);
/// </item>
/// <item><c>TextBox.ScrollToEnd()</c> 与 <c>VerticalOffset</c> / <c>ExtentHeight</c> / <c>ViewportHeight</c>:
///   Avalonia 的 <c>TextBox</c> 没有这四个成员(D-128 已定口径),等价物是它模板里那根 <c>ScrollViewer</c>
///   —— 取不到(还没套模板)就**不滚**,不假装滚过。判据里的 <c>20.0</c> 与 Windows 逐值相同;</item>
/// <item><c>Ui.OpenUrl</c> / <c>Dlg</c> / <c>Motion</c> / <c>I18n</c> 在 mac 侧已是同名同参门面,调用形状一字未改;</item>
/// <item>局部变量名照 Windows 的 <c>textBlock2</c> / <c>button2</c> 那套反编译器风格**改成语义名**
///   (类型、顺序、判据、闭包捕获的时机一字不改)。</item>
/// </list>
/// </summary>
public sealed class TasksPage : PageBase
{
	/// <summary>一张任务卡上的可更新件(Windows 的同名嵌套类,字段逐个照抄)。</summary>
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

	/// <summary>【Windows 源】<c>:48</c>:展开中的日志卡最快 100ms 刷一次(不跟着进度事件抖)。</summary>
	private static readonly TimeSpan LogInterval = TimeSpan.FromMilliseconds(100.0);

	/// <summary>【Windows 源】<c>:50</c>:标题「下载任务」。</summary>
	public override string Title => I18n.L("下载任务");

	/// <summary>【Windows 源】<c>:52-82</c>:概览行 + 两个区块的 <c>BlockHost("tasks")</c> + 三个 TaskStore 通知。</summary>
	public TasksPage()
	{
		Button clear = Ui.Btn(I18n.L("清除已完成"), BtnKind.Chip, delegate
		{
			TaskStore.ClearFinished();
			Rebuild();
		}, "\uea99");
		Grid header = Ui.Section(I18n.L("下载任务"), I18n.L("安装、下载、启动都在这里排队"), Ui.H(8.0, _summary.VCenter(), clear));
		// 布局自定义:概览与任务列表各成一个区块,默认顺序与可见性和旧版完全一致
		Content = new BlockHost("tasks", new List<BlockSpec>
		{
			new BlockSpec { Id = "header", Title = I18n.L("概览"), Content = header },
			new BlockSpec { Id = "list", Title = I18n.L("任务列表"), Content = _list },
		}).Root;
		// 【Windows 源】:这一页收拖放,但什么都不做(DropContext.None)
		EnableDrop(() => FileDrop.DropContext.None);
		TaskStore.Added += delegate
		{
			Dispatcher.UIThread.Post(Rebuild);
		};
		TaskStore.Updated += delegate(TaskRow row)
		{
			Dispatcher.UIThread.Post(delegate
			{
				Sync(row);
			});
		};
		TaskStore.Cleared += delegate
		{
			Dispatcher.UIThread.Post(Rebuild);
		};
	}

	/// <summary>【Windows 源】<c>:84-88</c>:这一页没有网络首屏,只是把已有的任务画出来。</summary>
	protected override Task LoadAsync()
	{
		Rebuild();
		return Task.CompletedTask;
	}

	/// <summary>【Windows 源】<c>:90-93</c>:每次显示都重画(期间可能有任务被清掉)。</summary>
	public override void OnShown()
	{
		Rebuild();
	}

	/// <summary>【Windows 源】<c>:95-115</c>:倒序重画全部任务卡;空的时候给空态。</summary>
	private void Rebuild()
	{
		_list.Children.Clear();
		_cards.Clear();
		List<TaskRow> rows = TaskStore.Rows.Reverse().ToList();
		_summary.Text = ((rows.Count == 0) ? "" : I18n.L("进行中 {0} · 共 {1}", TaskStore.RunningCount, rows.Count));
		if (rows.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue896", I18n.L("还没有任务"), I18n.L("到「下载」页装个版本或模组试试")));
			return;
		}

		foreach (TaskRow row in rows)
		{
			Card card = Build(row);
			_cards[row.Id] = card;
			_list.Children.Add(card.Root);
			Sync(row);
		}

		Motion.Stagger(_list, 24.0);
		Run(ResolveTitlesAsync);
	}

	/// <summary>【Windows 源】<c>:117-134</c>:占位标题的任务逐个问一次 <c>task_title</c>,拿到真标题就换上去。</summary>
	private async Task ResolveTitlesAsync()
	{
		foreach (TaskRow row in TaskStore.Rows.Where((TaskRow r) => r.Title == TaskStore.PlaceholderTitle).ToList())
		{
			string text = (await Api.TryCallAsync("task_title", new
			{
				task_id = row.Id
			}, "")) ?? "";
			// 【Windows 源】:空串 / 等于 id / 等于原标题都算"没拿到",不覆盖
			if (text.Length != 0 && !(text == row.Id) && !(text == row.Title))
			{
				row.Title = text;
				if (_cards.TryGetValue(row.Id, out Card? card))
				{
					card.Title.Text = text;
				}
			}
		}
	}

	/// <summary>【Windows 源】<c>:136-230</c>:造一张任务卡(标题 / 状态 / 进度 / 速度 + 崩溃分析 / 日志 / 取消)。</summary>
	private Card Build(TaskRow row)
	{
		TextBlock title = Ui.Txt(row.Title, 13.5, bold: true).Trim();
		TextBlock status = Ui.Muted("");
		TextBlock speed = Ui.Small("");
		ProgressBar bar = Ui.Prog();
		Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost);
		Button toggle = Ui.Btn(I18n.L("日志"), BtnKind.Chip, null, "\ue8fd");
		Button crash = Ui.Btn(I18n.L("崩溃分析"), BtnKind.Chip, null, "\uebe8");
		// 【Windows 源】:崩溃分析默认藏着,只有"跑完且失败"才 Show(true)(见 Sync)
		crash.IsVisible = false;
		TextBox log = Ui.LogBox();
		log.Height = 160.0;
		log.BorderThickness = new Thickness(0.0);
		log.Background = Brushes.Transparent;
		Border logWrap = Ui.Pane(log, "B.Paper2", 9.0, 10.0);
		logWrap.IsVisible = false;
		Grid head = Ui.G(null, "*,Auto");
		head.Add(Ui.V(3.0, title, status).VCenter(), 0);
		head.Add(Ui.H(6.0, crash, toggle, cancel).VCenter(), 0, 1);
		Grid barRow = Ui.G(null, "*,Auto");
		barRow.Add(bar.VCenter(), 0);
		barRow.Add(speed.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Border root = Ui.Card(Ui.V(9.0, head, barRow, logWrap), 14.0);
		Motion.HoverLift(root, 1.004, 1.0, 16.0);
		Card card = new Card
		{
			Root = root,
			Title = title,
			Status = status,
			Speed = speed,
			Bar = bar,
			Cancel = cancel,
			Toggle = toggle,
			Crash = crash,
			LogWrap = logWrap,
			Log = log,
		};
		cancel.Click += delegate
		{
			PageBase.Run(async delegate
			{
				// 【Windows 源】:跑完的按「清除」(从 TaskStore 里摘掉),没跑完的按「取消」(下发 cancel_task)
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
		crash.Click += delegate
		{
			PageBase.Run(async delegate
			{
				CrashReport? report = await PageBase.Api.TryCallAsync<CrashReport>("get_crash", new
				{
					task_id = row.Id
				});
				if (report == null || string.IsNullOrWhiteSpace(report.Title + report.Headline + report.Summary + report.Detail))
				{
					PageBase.Toast(I18n.L("没有崩溃分析"), I18n.L("这个任务没有留下崩溃报告"));
				}
				else
				{
					if (string.IsNullOrEmpty(report.TaskId))
					{
						report.TaskId = row.Id;
					}

					await CrashUi.ShowAsync(report, gameError: true);
				}
			});
		};
		toggle.Click += delegate
		{
			card.Expanded = !card.Expanded;
			if (card.Expanded)
			{
				card.Log.Text = row.Log.ToString();
				LogScroller(card.Log)?.ScrollToEnd();
				logWrap.IsVisible = true;
				Motion.FadeIn(logWrap, 200.0, 8.0);
			}
			else
			{
				logWrap.IsVisible = false;
			}
		};
		return card;
	}

	/// <summary>【Windows 源】<c>:232-262</c>:把一个任务的最新状态刷到它的卡上(卡不在就整体重画)。</summary>
	private void Sync(TaskRow row)
	{
		if (!_cards.TryGetValue(row.Id, out Card? card))
		{
			Rebuild();
			return;
		}

		card.Title.Text = row.Title;
		card.Status.Text = row.Status;
		card.Speed.Text = row.Speed;
		card.Bar.IsIndeterminate = row.Indeterminate && !row.Finished;
		if (!card.Bar.IsIndeterminate)
		{
			Motion.Progress(card.Bar, row.Progress);
		}

		card.Cancel.IsEnabled = !row.Finished;
		card.Cancel.Content = (row.Finished ? I18n.L("清除") : I18n.L("取消"));
		card.Crash.Show(row.Finished && !row.Success);
		card.Bar.Dyn(TemplatedControl.ForegroundProperty, (row.Finished && !row.Success) ? "B.Danger" : "B.Accent");
		if (card.Expanded && (row.Finished || DateTime.UtcNow - card.LogPainted >= LogInterval))
		{
			card.LogPainted = DateTime.UtcNow;
			// 【Windows 源】:bool atBottom = Log.VerticalOffset >= Log.ExtentHeight - Log.ViewportHeight - 20.0;
			// Avalonia 的 TextBox 没有这三个属性 ⇒ 等价物是它模板里那根 ScrollViewer;取不到就不滚(D-128)。
			ScrollViewer? scroller = LogScroller(card.Log);
			bool atBottom = scroller != null && scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 20.0;
			card.Log.Text = row.Log.ToString();
			if (atBottom)
			{
				scroller!.ScrollToEnd();
			}
		}

		_summary.Text = I18n.L("进行中 {0} · 共 {1}", TaskStore.RunningCount, TaskStore.Rows.Count);
	}

	/// <summary>日志框内部的滚动条(Avalonia 的 TextBox 把滚动交给模板里的 ScrollViewer;D-128 的既有口径)。</summary>
	private static ScrollViewer? LogScroller(TextBox log)
	{
		return log.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
	}
}
