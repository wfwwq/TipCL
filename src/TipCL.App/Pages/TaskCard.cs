using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8b:Windows 版 <c>Pages\TaskCard.cs</c>(186 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **AI 触发的下载/安装后台任务卡**:标题、进度条、百分比/速度;完成或失败后给后续按钮
/// (成功:启动游戏 / 打开文件夹;失败:重试,失败原因作参数)。与 `PlanCard`/`AskCard`/`ToolLine` 同一族。
///
/// 依赖审计(落地前核过):**完全自包含** —— 只用 C8a 已落的 `Lucide`/`AiIcons.Tile`/`ToolState`
/// 与 `Ui.*`/`Fmt.SplitMsg`/`Motion.Progress`/`Clamp.Of`;**不碰 `AiPage`、不依赖 `PlanCard`/`AskCard`**,
/// 所以能单独编绿、单独提交。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Visibility.Collapsed/Visible</c> → <c>IsVisible</c>;<c>ToolTip = …</c> → <c>Tip(…)</c>
///   (三处:<c>_title</c>/<c>_status</c>/重试按钮;mac 的 <c>Tip</c> 不接受 null,所以照 Windows 的 <c>?? ""</c> 取值);</item>
/// <item><c>SetResourceReference(Border.BackgroundProperty 等, 调色板键)</c> → <c>Dyn(控件属性, 键)</c>(本仓统一口径);
///   【注意】注释里别写字面量假键名:C1a 的调色板守卫会**扫 .cs 源码(含注释)**,假键名会被判成"查了不存在的键"(我这次就栽在这);</item>
/// <item><c>FrameworkElement</c> → <c>Control</c>;<c>TextWrapping.Wrap</c> → Avalonia 同名枚举;</item>
/// <item>图标/圆角/边距/字号**逐值照抄**(`CornerRadius(10)`:12/10 内边距、`26×26` 图标位、`4.0` 进度条高)。</item>
/// </list>
/// </summary>
internal sealed class TaskCard : Border
{
	private readonly TextBlock _title;

	private readonly TextBlock _status;

	private readonly TextBlock _percent;

	private readonly TextBlock _speed;

	private readonly ProgressBar _bar;

	private readonly SPanel _buttons = Ui.H(6.0);

	private readonly ContentControl _icon = new ContentControl
	{
		Width = 26.0,
		Height = 26.0,
		VerticalAlignment = VerticalAlignment.Top,
	};

	/// <summary>完成后「启动游戏」;为 null 时不显示。</summary>
	public Action? Launch { get; set; }

	/// <summary>完成后「打开文件夹」;为 null 时不显示。</summary>
	public Action? OpenFolder { get; set; }

	/// <summary>失败后「重试」(失败原因作参数);为 null 时不显示。</summary>
	public Action<string>? Retry { get; set; }

	public string TaskId { get; }

	public string Tool { get; }

	public string Instance { get; }

	public string FinishMessage { get; private set; } = "";

	public bool? Succeeded { get; private set; }

	internal string TitleText => _title.Text ?? "";

	public TaskCard(string taskId, string title, string tool, string instance)
	{
		TaskId = taskId ?? "";
		Tool = tool ?? "";
		Instance = instance ?? "";
		_title = Ui.Txt(title ?? "", 13.0, bold: true, "B.Ink");
		_title.TextTrimming = TextTrimming.CharacterEllipsis;
		_status = Ui.Txt(I18n.L("后台进行中"), 11.5, bold: false, "B.InkMuted");
		_status.TextTrimming = TextTrimming.CharacterEllipsis;
		_percent = Ui.Txt("", 11.5, bold: true, "B.AccentText");
		_speed = Ui.Txt("", 11.5, bold: false, "B.InkFaint");
		_bar = Ui.Prog();
		_bar.Height = 4.0;
		_bar.IsIndeterminate = true;
		_buttons.IsVisible = false;
		_buttons.Margin = new Avalonia.Thickness(0.0, 4.0, 0.0, 0.0);
		Grid head = Ui.G(null, "*,Auto,Auto");
		head.Add(_title.VCenter(), 0);
		head.Add(_speed.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		head.Add(_percent.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		Grid grid = Ui.G(null, "Auto,*");
		grid.Add(_icon, 0);
		grid.Add(Ui.V(5.0, head, _bar, _status, _buttons).M(10.0, 0.0, 0.0, 0.0), 0, 1);
		CornerRadius = new Avalonia.CornerRadius(10.0);
		BorderThickness = new Avalonia.Thickness(1.0);
		Padding = new Avalonia.Thickness(12.0, 10.0, 12.0, 10.0);
		HorizontalAlignment = HorizontalAlignment.Stretch;
		this.Dyn(BackgroundProperty, "B.Paper2");
		this.Dyn(BorderBrushProperty, "B.LineSoft");
		Child = grid;
		SetIcon(ToolState.Running);
	}

	public void SetTitle(string title)
	{
		_title.Text = title ?? "";
		_title.Tip(title ?? "");
	}

	private void SetIcon(ToolState state)
	{
		string brush = state switch
		{
			ToolState.Failed => "B.Danger",
			_ => "B.AccentText",
		};
		Control icon = (state == ToolState.Running)
			? Lucide.Spinner(14.0, brush)
			: Lucide.Icon((state == ToolState.Failed) ? "M18 6L6 18 M6 6l12 12" : "M20 6L9 17l-5-5", 14.0, brush);
		_icon.Content = AiIcons.Tile(icon, state);
	}

	public void SetProgress(int current, int total, string? message)
	{
		if (Succeeded.HasValue)
		{
			return;
		}

		_bar.IsIndeterminate = total <= 0;
		if (total > 0)
		{
			double percent = Clamp.Of((double)current * 100.0 / (double)total, 0.0, 100.0);
			Motion.Progress(_bar, percent);
			_percent.Text = $"{percent:0}%";
		}

		Fmt.SplitMsg(message, out string status, out string speed);
		if (!string.IsNullOrEmpty(status))
		{
			_status.Text = status;
			_status.Tip(status);
		}

		_speed.Text = speed;
	}

	public void Finish(bool success, string? message)
	{
		Succeeded = success;
		FinishMessage = message ?? "";
		_bar.IsIndeterminate = false;
		_speed.Text = "";
		if (success)
		{
			Motion.Progress(_bar, 100.0);
			_percent.Text = "100%";
			_bar.IsVisible = false;
			_status.Text = string.IsNullOrWhiteSpace(message) ? I18n.L("完成") : message;
			_status.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		}
		else
		{
			_percent.Text = I18n.L("失败");
			_percent.Dyn(TextBlock.ForegroundProperty, "B.Danger");
			_status.Text = I18n.L("原因:") + (string.IsNullOrWhiteSpace(message) ? I18n.L("未知错误") : message);
			_status.TextWrapping = TextWrapping.Wrap;
			_status.Dyn(TextBlock.ForegroundProperty, "B.Danger");
			this.Dyn(BorderBrushProperty, "B.Danger");
		}

		_status.Tip(message ?? "");
		SetIcon(success ? ToolState.Done : ToolState.Failed);
		_buttons.Children.Clear();
		if (success)
		{
			Action? launch = Launch;
			if (launch != null)
			{
				_buttons.Children.Add(Ui.Btn(I18n.L("启动游戏"), BtnKind.Primary, delegate
				{
					launch();
				}, "\ue768"));
			}

			Action? open = OpenFolder;
			if (open != null)
			{
				_buttons.Children.Add(Ui.Btn(I18n.L("打开文件夹"), BtnKind.Chip, delegate
				{
					open();
				}, "\ue838"));
			}
		}
		else
		{
			Action<string>? retry = Retry;
			if (retry != null && FinishMessage != "已取消")
			{
				Button? retryButton = null;
				retryButton = Ui.Btn(I18n.L("重试"), BtnKind.Chip, delegate
				{
					retryButton!.IsEnabled = false;
					retry(FinishMessage);
				}, "\ue72c");
				retryButton.Tip(I18n.L("让助手重新执行这一步"));
				_buttons.Children.Add(retryButton);
			}
		}

		_buttons.IsVisible = _buttons.Children.Count > 0;
	}
}
