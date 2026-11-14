using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Services;

namespace TipCL.Pages.Ai;

/// <summary>
/// K1(本包第一个落地的件,也是 `AiPage` 的**唯一硬前置**):工具步骤组
/// (= Windows <c>Pages\Ai\StepGroup.cs</c>,134 行)—— 标题行(层叠图标 + 摘要 + 计数 + 箭头)+ 可折叠的
/// <see cref="ToolLine"/> 列表;执行中标题走 <c>AiUiText.StepSummary</c>,完成时按"用户没手动切过且步数 &gt; 3"自动收起。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item><c>MouseLeftButtonUp</c> → <c>PointerReleased</c>;</item>
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>SetResourceReference</c> → <c>Dyn</c>;</item>
/// <item><c>line.Parent is Panel</c> → <c>line.GetVisualParent() as Panel</c>(Avalonia 里可视父级用扩展方法取,
///   项目既有写法);</item>
/// <item>颜色键、圆角、内边距、外边距、字号、图标 path 数据与 Windows **逐值相同**(图标走 <see cref="AiIcons"/>)。</item>
/// </list>
/// </summary>
internal sealed class StepGroup : Border
{
	private readonly SPanel _body = Ui.V(0.0);

	private readonly Border _bodyHost;

	private readonly TextBlock _title;

	private readonly TextBlock _count;

	private readonly Shape _chev;

	private readonly Stopwatch _sw = Stopwatch.StartNew();

	private bool _open = true;

	private bool _running = true;

	private bool _userToggled;

	internal int Count => _body.Children.OfType<ToolLine>().Count();

	internal bool IsOpen => _open;

	internal string TitleText => _title.Text ?? "";

	public StepGroup()
	{
		_title = Ui.Txt(I18n.L("正在执行"), 12.5, bold: true, "B.Ink");
		_title.TextTrimming = TextTrimming.CharacterEllipsis;
		_count = Ui.Txt("", 12.0, bold: false, "B.InkFaint");
		_chev = AiIcons.Icon("M6 9l6 6 6-6", 14.0, "B.InkFaint");
		_chev.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
		_chev.RenderTransform = new RotateTransform(0.0);
		Grid head = Ui.G(null, "Auto,Auto,Auto,*,Auto");
		head.Add(AiIcons.Icon(AiIcons.Layers, 14.0, "B.InkSoft").VCenter(), 0);
		head.Add(_title.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		head.Add(_count.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		head.Add(_chev.VCenter(), 0, 4);
		head.Background = Brushes.Transparent;
		head.Cursor = new Cursor(StandardCursorType.Hand);
		head.Margin = new Thickness(12.0, 8.0, 12.0, 8.0);
		head.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
		{
			_userToggled = true;
			SetOpen(!_open);
			e.Handled = true;
		};
		_bodyHost = new Border
		{
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Padding = new Thickness(6.0, 4.0, 6.0, 6.0),
			Child = _body,
		};
		_bodyHost.Dyn(Border.BorderBrushProperty, "B.LineSoft");
		base.CornerRadius = new CornerRadius(8.0);
		base.BorderThickness = new Thickness(1.0);
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		this.Dyn(Border.BorderBrushProperty, "B.Line");
		this.Dyn(Border.BackgroundProperty, "B.Paper");
		base.Child = Ui.V(0.0, head, _bodyHost);
		Refresh();
	}

	/// <summary>把一行挂进来(已经在本组里 ⇒ 只刷新;挂在别处 ⇒ 先摘下来)。Windows <c>AddLine</c>。</summary>
	public void AddLine(ToolLine line)
	{
		if (line.GetVisualParent() is Panel panel)
		{
			if (panel == _body)
			{
				Refresh();
				return;
			}
			panel.Children.Remove(line);
		}
		_body.Children.Add(line);
		Refresh();
	}

	/// <summary>子代理分组(带一个缩进的小卡片),返回它内部的容器。Windows <c>AddSubagent</c>。</summary>
	public SPanel AddSubagent(string title)
	{
		SPanel host = Ui.V(0.0);
		Border box = new Border
		{
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(4.0),
			Margin = new Thickness(35.0, 2.0, 0.0, 4.0),
			Child = Ui.V(2.0,
				Ui.H(6.0,
					AiIcons.Icon(AiIcons.Branch, 13.0).VCenter(),
					Ui.Txt(title, 11.5, bold: false, "B.InkMuted").VCenter()).M(6.0, 3.0, 0.0, 3.0),
				host),
		};
		box.Dyn(Border.BorderBrushProperty, "B.Line");
		_body.Children.Add(box);
		return host;
	}

	/// <summary>刷新标题与计数(摘要口径来自 <c>AiUiText.StepSummary</c>,与自己维护的秒表一起用)。Windows <c>Refresh</c>。</summary>
	public void Refresh()
	{
		ToolLine[] lines = _body.Children.OfType<ToolLine>().ToArray();
		ToolLine? running = lines.LastOrDefault((ToolLine l) => l.State == ToolState.Running || l.State == ToolState.Prepare);
		bool busy = _running && running != null;
		_title.Text = AiUiText.StepSummary(lines.Length, _sw.Elapsed.TotalSeconds, busy, running?.LabelText ?? "");
		int failed = lines.Count((ToolLine l) => l.State == ToolState.Failed);
		_count.Text = (busy ? $"· {lines.Length} 步" : ((failed > 0) ? $"· {failed} 步失败" : ""));
		_count.Dyn(TextBlock.ForegroundProperty, (failed > 0 && !busy) ? "B.Danger" : "B.InkFaint");
	}

	/// <summary>结束:停表、刷新;**用户没手动切过且步数 &gt; 3 ⇒ 自动收起**(Windows <c>Complete</c>)。</summary>
	public void Complete()
	{
		_running = false;
		_sw.Stop();
		Refresh();
		if (!_userToggled && Count > 3)
		{
			SetOpen(open: false);
		}
	}

	/// <summary>
	/// 【测试缝 · D-127】标题行点击那条路的入口(与真实路径**同一段代码**):真实路径是
	/// <c>PointerReleased</c> → 置 <c>_userToggled</c> 再 <see cref="SetOpen"/>;这里把它直接暴露给用例,
	/// 不去模拟鼠标(而且"用户手动切过 ⇒ 完成时不自动收起"这条分支只有它能钉住)。
	/// </summary>
	internal void ToggleForTest()
	{
		_userToggled = true;
		SetOpen(!_open);
	}

	/// <summary>展开/收起(箭头同时旋转 −90°)。Windows <c>SetOpen</c>。</summary>
	public void SetOpen(bool open)
	{
		_open = open;
		_bodyHost.IsVisible = open;
		if (_chev.RenderTransform is RotateTransform rotate)
		{
			rotate.Angle = open ? 0 : -90;
		}
	}
}
