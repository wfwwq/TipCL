using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8a:「思考过程」折叠块(= Windows <c>Pages\ThinkFold.cs</c>,98 行)——
/// 标题行(火花图标 + 标签 + 箭头)+ 可折叠的只读正文;思考中标签走 <c>Lucide.Shimmer</c>、箭头走 <c>Lucide.Rotate</c>。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item><c>MouseLeftButtonUp</c> → <c>PointerReleased</c>(同一时机);</item>
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>SetResourceReference</c> → <c>Dyn</c>(两者都是 <c>Control</c>,可直接用);</item>
/// <item>状态色/边框/圆角/内边距/字号与 Windows **逐值相同**。</item>
/// </list>
/// </summary>
internal sealed class ThinkFold : Border
{
	private readonly TextBlock _label;

	private readonly Path _chevron;

	private readonly TextBox _body;

	private bool _open;

	private bool _thinking;

	public bool IsOpen => _open;

	public string Text => _body.Text ?? "";

	internal string Header => _label.Text ?? "";

	internal bool BodyShown => _body.IsVisible;

	public ThinkFold()
	{
		_label = Ui.Txt("", 12.0, bold: true, "B.Ink");
		_chevron = Lucide.Icon("M9 18l6-6-6-6");
		Grid head = Ui.G(null, "Auto,*,Auto");
		head.Add(Lucide.Icon("M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77").VCenter(), 0);
		head.Add(_label.M(8.0, 0.0, 8.0).VCenter(), 0, 1);
		head.Add(_chevron.VCenter(), 0, 2);
		head.Background = Brushes.Transparent;
		head.Cursor = new Cursor(StandardCursorType.Hand);
		head.MinHeight = 22.0;
		head.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
		{
			Toggle();
			e.Handled = true;
		};
		_body = new TextBox
		{
			IsReadOnly = true,
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			TextWrapping = TextWrapping.Wrap,
			FontSize = 12.0,
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			IsVisible = false,
		};
		_body.Dyn(TextBox.ForegroundProperty, "B.InkMuted");
		base.CornerRadius = new CornerRadius(8.0);
		base.Padding = new Thickness(10.0, 5.0, 10.0, 6.0);
		base.BorderThickness = new Thickness(1.0);
		// 注意:扩展方法不能通过 base. 调用(CS0117),这里必须写 this.(或直接用变量名)
		this.Dyn(Border.BackgroundProperty, "B.Hover");
		this.Dyn(Border.BorderBrushProperty, "B.Line");
		base.Child = Ui.V(0.0, head, _body);
		SyncHead();
	}

	public void Toggle()
	{
		SetOpen(!_open);
	}

	public void SetOpen(bool on)
	{
		if (_open == on)
		{
			SyncHead();
			return;
		}
		_open = on;
		_body.IsVisible = on;
		SyncHead();
	}

	public void SetThink(string text, bool live, bool open)
	{
		_thinking = live && open;
		_body.Text = text ?? "";
		SetOpen(open);
	}

	private void SyncHead()
	{
		int length = (_body.Text ?? "").Length;
		_label.Text = (_thinking ? I18n.L("思考中...") : (I18n.L("思考过程") + ((length > 0) ? (I18n.L("(") + length + I18n.L(")")) : "")));
		Lucide.Shimmer(_label, _thinking, "B.Ink");
		Lucide.Rotate(_chevron, _open);
	}
}
