using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8b:Windows 版 <c>Pages\ToolLine.cs</c>(154 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// AI 的**工具/步骤行**(`ToolLine`):图标 + 标签 + 参数 + 结果 + 进度条,MCP 工具多一个「MCP · 外部」标记。
/// 它是"零散件"里第一个被 AI 线吃掉的件(`AiFixWindow._taskLines` 依赖它)。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Border.Child</c> / <c>Padding</c> / <c>CornerRadius</c> / <c>HorizontalAlignment</c> 两平台同名同义;</item>
/// <item><c>Visibility.Collapsed/Visible</c> → <c>IsVisible</c>;<c>ToolTip = …</c> → <c>Tip(…)</c>
///   (**这里是 `Bar.ToolTip`/`Border.ToolTip` 两处**,后者在 <c>Finish</c> 里);</item>
/// <item><c>SetResourceReference(TextBlock.ForegroundProperty, …)</c> → <c>Dyn(…)</c>(本仓统一口径);</item>
/// <item><c>FrameworkElement</c> → <c>Control</c>;<c>Brushes.Transparent</c> 两平台同名(Avalonia.Media);</item>
/// <item>图标/文字/状态全部复用 **C8a 已落的件**(<c>Lucide</c> / <c>LucideAssets</c> / <c>AiIcons.Tile</c> /
///   <c>MdView.Mono</c> / <c>AiUiText</c> / <c>ToolState</c>),**不在这文件里内联仿制品**。</item>
/// </list>
/// </summary>
internal sealed class ToolLine : Border
{
	private readonly TextBlock _label;

	private readonly TextBlock _arg;

	private readonly TextBlock _result;

	private readonly Border _mcp;

	private readonly ProgressBar _bar;

	private readonly ContentControl _icon = new ContentControl
	{
		Width = 26.0,
		Height = 26.0,
		VerticalAlignment = VerticalAlignment.Top,
	};

	private readonly string? _typeIcon;

	private readonly string? _tool;

	private ToolState _state = (ToolState)(-1);

	internal string LabelText => _label.Text ?? "";

	internal string ArgText => _arg.Text ?? "";

	internal ToolState State => _state;

	public ToolLine(string text, string? tool = null)
	{
		_tool = tool;
		_typeIcon = Lucide.ToolIcon(tool);
		_label = Ui.Txt("", 12.5, bold: false, "B.Ink");
		_label.TextTrimming = TextTrimming.CharacterEllipsis;
		_arg = Ui.Txt("", 11.5, bold: false, "B.InkMuted");
		_arg.FontFamily = MdView.Mono;
		_arg.TextTrimming = TextTrimming.CharacterEllipsis;
		_arg.Margin = new Avalonia.Thickness(8.0, 0.0, 8.0, 0.0);
		_result = Ui.Txt("", 11.5, bold: false, "B.InkFaint");
		_mcp = Ui.Tag(I18n.L("MCP · 外部"), "B.Info", "B.InfoSoft");
		_mcp.Margin = new Avalonia.Thickness(8.0, 0.0, 0.0, 0.0);
		_mcp.IsVisible = AiUiText.IsMcp(tool);
		_bar = Ui.Prog();
		_bar.Height = 3.0;
		_bar.IsVisible = false;
		_bar.Margin = new Avalonia.Thickness(35.0, 2.0, 0.0, 0.0);
		Grid grid = Ui.G(null, "Auto,Auto,Auto,*,Auto");
		grid.Add(_icon, 0);
		grid.Add(_label.M(9.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid.Add(_mcp.VCenter(), 0, 2);
		grid.Add(_arg.VCenter(), 0, 3);
		grid.Add(_result.VCenter(), 0, 4);
		Padding = new Avalonia.Thickness(6.0, 4.0, 6.0, 4.0);
		CornerRadius = new Avalonia.CornerRadius(6.0);
		HorizontalAlignment = HorizontalAlignment.Stretch;
		Background = Brushes.Transparent;
		Child = Ui.V(0.0, grid, _bar);
		SetText(text);
		SetState(ToolState.Prepare);
	}

	public void SetText(string text)
	{
		(string label, string arg) = AiUiText.SplitLabel(text);
		_label.Text = label;
		_arg.Text = arg;
		this.Tip(AiUiText.StripState(text));
	}

	public void SetResult(string text)
	{
		_result.Text = text ?? "";
	}

	public void SetState(ToolState state)
	{
		if (_state != state)
		{
			_state = state;
			SyncIcon();
		}
	}

	private void SyncIcon()
	{
		bool busy = _state == ToolState.Prepare || _state == ToolState.Running;
		string brushKey = _state switch
		{
			ToolState.Failed => "B.Danger",
			ToolState.Skipped => "B.InkMuted",
			_ => "B.AccentText",
		};
		Lucide.Shimmer(_label, busy, (_state == ToolState.Failed) ? "B.Danger" : ((_state == ToolState.Skipped) ? "B.InkMuted" : "B.Ink"));
		Control icon =
			(!string.IsNullOrEmpty(_tool) && LucideAssets.Icons.ContainsKey(_tool))
				? Lucide.Asset(_tool, 14.0, brushKey)
				: ((_typeIcon != null)
					? Lucide.Icon(_typeIcon, 14.0, brushKey)
					: (!AiUiText.IsMcp(_tool)
						? (busy ? Lucide.Spinner(14.0, brushKey) : Lucide.Icon("M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1", 14.0, brushKey))
						: Lucide.Icon("M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z", 14.0, brushKey)));
		_icon.Content = AiIcons.Tile(icon, _state);
		if (_state == ToolState.Skipped && (_result.Text ?? "").Length == 0)
		{
			_result.Text = I18n.L("已跳过");
		}
	}

	/// <summary>【Windows 源】<c>BindTask()</c>:把这一行变成"后台进行中"(不确定进度)。</summary>
	public void BindTask()
	{
		_bar.IsVisible = true;
		_bar.IsIndeterminate = true;
		_result.Text = I18n.L("后台进行中");
	}

	/// <summary>【Windows 源】<c>:120-135</c>:按 current/total 显示百分比,并把消息里的状态段写进参数位。</summary>
	public void SetProgress(int current, int total, string? message)
	{
		_bar.IsVisible = true;
		_bar.IsIndeterminate = total <= 0;
		if (total > 0)
		{
			double percent = Clamp.Of((double)current * 100.0 / (double)total, 0.0, 100.0);
			Motion.Progress(_bar, percent);
			_result.Text = $"{percent:0}%";
		}

		Fmt.SplitMsg(message, out string status, out string _);
		if (!string.IsNullOrEmpty(status))
		{
			_arg.Text = status;
		}
	}

	/// <summary>【Windows 源】<c>:137-153</c>:收尾 —— 成功就把进度条收掉显示「完成」,失败显示「失败」并转红。</summary>
	public void Finish(bool success, string? message)
	{
		_bar.IsIndeterminate = false;
		if (success)
		{
			Motion.Progress(_bar, 100.0);
			_bar.IsVisible = false;
		}

		_result.Text = success ? I18n.L("完成") : I18n.L("失败");
		if (!string.IsNullOrWhiteSpace(message))
		{
			_arg.Text = message;
			this.Tip(message);
		}

		SetState(success ? ToolState.Done : ToolState.Failed);
		_result.Dyn(TextBlock.ForegroundProperty, success ? "B.InkFaint" : "B.Danger");
	}
}
