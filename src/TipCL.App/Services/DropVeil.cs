using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TipCL.Services;

/// <summary>
/// 拖放浮层(= Windows <c>MainWindow.xaml</c> 78-86 行的 <c>&lt;Border Name="DropVeil"&gt;</c>
/// 加 <c>MainWindow.xaml.cs:1133-1155</c> 的 <c>ShowDropVeil</c>)。
///
/// 逐项照 Windows 抄下来的取值:
/// <list type="bullet">
/// <item>整块是 <c>B.Mask</c> 底色、<c>IsHitTestVisible = false</c>、<c>Margin = 0</c>、初始收起;</item>
/// <item>中间那张卡片:圆角 10、内边距 (20,16,20,18)、描边 2、居中,玻璃底 + 玻璃描边;</item>
/// <item>卡片里三行:图标(30,<c>B.Accent</c>)、标题(15 / SemiBold / 上边距 8)、提示
///   (12 / 上边距 3 / 最宽 360 / 换行 / 居中 / <c>B.InkMuted</c>);</item>
/// <item>文案在构造里走 <c>I18n.L</c>(Windows 也是构造里写的):「松手就装」与那句提示;</item>
/// <item>淡入 120ms、淡出 110ms,淡出结束时收起(Windows 先 <c>Fade</c> 再在完成回调里 <c>Collapsed</c>);</item>
/// <item>Windows 那句 <c>if (on == (Visibility == Visible)) return;</c> 的「已经是这个状态就早退」照抄
///   —— 连它在「淡出进行中又拖进来」时不会打断淡出的毛病也一并保留(不修,免得与 Windows 行为不同)。</item>
/// </list>
///
/// 框架差异(与 C2c1 的 <c>ToastCard</c> 同一套):<c>SetResourceReference</c> → <see cref="UiExt.Dyn{T}"/>、
/// <c>Visibility</c> → <see cref="Avalonia.Visual.IsVisible"/>、WPF 的 <c>StackPanel</c> → Avalonia 同类型。
/// 图标那一行 Windows 的 <c>Text</c> 是空串(没画字形),这里照抄,不自己编一个字形。
/// </summary>
internal sealed class DropVeil : Border
{
	/// <summary>【Windows 源】<c>MainWindow.xaml.cs:1146</c> —— 淡入 120ms。</summary>
	internal const double FadeInMs = 120.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml.cs:1150</c> —— 淡出 110ms。</summary>
	internal const double FadeOutMs = 110.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml:79</c> —— 卡片圆角 10。</summary>
	internal const double CardCornerRadius = 10.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml:79</c> —— 卡片描边 2。</summary>
	internal const double CardBorderThickness = 2.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml:79</c> —— 卡片内边距 (20,16,20,18)。</summary>
	internal static readonly Thickness CardPadding = new Thickness(20.0, 16.0, 20.0, 18.0);

	/// <summary>【Windows 源】<c>MainWindow.xaml:81</c> —— 图标 30。</summary>
	internal const double IconSize = 30.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml:82</c> —— 标题 15。</summary>
	internal const double TitleSize = 15.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml:83</c> —— 提示 12。</summary>
	internal const double HintSize = 12.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml:83</c> —— 提示最宽 360。</summary>
	internal const double HintMaxWidth = 360.0;

	/// <summary>【Windows 源】<c>MainWindow.xaml:82</c> —— 标题上边距 8。</summary>
	internal static readonly Thickness TitleMargin = new Thickness(0.0, 8.0, 0.0, 0.0);

	/// <summary>【Windows 源】<c>MainWindow.xaml:83</c> —— 提示上边距 3。</summary>
	internal static readonly Thickness HintMargin = new Thickness(0.0, 3.0, 0.0, 0.0);

	/// <summary>【Windows 源】<c>MainWindow.xaml.cs:166</c> —— 标题文案。</summary>
	internal const string TitleSource = "松手就装";

	/// <summary>【Windows 源】<c>MainWindow.xaml.cs:167</c> —— 提示文案。</summary>
	internal const string HintSource = "整合包、模组、资源包、光影、数据包、存档、皮肤、壁纸都认;认不准会问你一句。";

	private bool _on;

	private bool _fadeOutPending;

	internal DropVeil()
	{
		Name = "DropVeil";
		IsVisible = false;
		IsHitTestVisible = false;
		Margin = new Thickness(0.0);
		this.Dyn(Border.BackgroundProperty, "B.Mask");

		Icon = Ui.Glyph("", IconSize, "B.Accent").Center();

		Title = Ui.Txt(I18n.L(TitleSource), TitleSize, bold: true).Center();
		Title.Margin = TitleMargin;

		Hint = Ui.Muted(I18n.L(HintSource)).Wrap().Center();
		Hint.FontSize = HintSize;
		Hint.Margin = HintMargin;
		Hint.MaxWidth = HintMaxWidth;
		Hint.TextAlignment = Avalonia.Media.TextAlignment.Center;

		Card = new Border
		{
			CornerRadius = new CornerRadius(CardCornerRadius),
			Padding = CardPadding,
			BorderThickness = new Thickness(CardBorderThickness),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Child = new StackPanel { Children = { Icon, Title, Hint } },
		};
		Card.Dyn(Border.BackgroundProperty, "B.GlassStrong");
		Card.Dyn(Border.BorderBrushProperty, "B.GlassBorder");
		Child = Card;
	}

	/// <summary>图标行(Windows 的 <c>Text=""</c>,只占位不画字形)。</summary>
	internal TextBlock Icon { get; }

	/// <summary>标题(测试断言它的文案与字号)。</summary>
	internal TextBlock Title { get; }

	/// <summary>提示(测试断言它的文案、字号、最大宽度与换行)。</summary>
	internal TextBlock Hint { get; }

	/// <summary>中间那张卡片(测试断言圆角 / 内边距 / 描边 / 玻璃取色)。</summary>
	internal Border Card { get; }

	/// <summary>逻辑上是不是「正在拖拽」。与 <see cref="Avalonia.Visual.IsVisible"/> 分开记:
	/// 淡出期间 <c>IsVisible</c> 还是 true(Windows 也是这个口径)。</summary>
	internal bool IsOn => _on;

	/// <summary>对外显示的可见标志(= Windows 的 <c>Visibility == Visible</c>)。</summary>
	internal bool IsShown => IsVisible;

	/// <summary>还有没有没落地的淡出(测试用它断言「没留下残留的过渡」:没有时钟的环境里是同步落的)。</summary>
	internal bool FadeOutPending => _fadeOutPending;

	/// <summary><see cref="Show"/> 真的改了状态的次数(早退不计)。</summary>
	internal int ShowCount { get; private set; }

	/// <summary>
	/// 显示 / 收起(= Windows <c>ShowDropVeil</c>)。返回「这次真的改了状态吗」。
	/// </summary>
	internal bool Show(bool on)
	{
		// Windows: if (on == (DropVeil.Visibility == Visibility.Visible)) return;
		if (on == IsVisible)
		{
			_on = on;
			return false;
		}

		ShowCount++;
		_on = on;
		if (on)
		{
			_fadeOutPending = false;
			IsVisible = true;
			Motion.Fade(this, 1.0, FadeInMs);
		}
		else
		{
			_fadeOutPending = true;
			Motion.FadeOut(this, FadeOutMs, delegate
			{
				_fadeOutPending = false;
				IsVisible = false;
			});
		}

		return true;
	}

	/// <summary>收摊:把状态复位(外壳关窗时调;不抛、不留状态)。</summary>
	internal void Reset()
	{
		_on = false;
		_fadeOutPending = false;
		IsVisible = false;
		Opacity = 0.0;
	}
}
