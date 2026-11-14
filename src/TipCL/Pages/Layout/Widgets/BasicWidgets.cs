using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TipCL.Services;

namespace TipCL.Pages.Layout;

// 三类简单自建区块:便签 / 时钟 / 分隔线(§4.F 的首期白名单 note / clock / divider)。
internal static class BasicWidgets
{
	/// <summary>便签:显示 settings.text,空则给一句引导。</summary>
	public static UIElement Note(WidgetDef widget)
	{
		string text = WidgetCommon.Str(widget.Settings, "text", "").Trim();
		if (text.Length == 0)
		{
			text = I18n.L("在布局 JSON 里给这个区块填 settings.text 即可写便签。");
		}
		if (text.Length > 4000)
		{
			text = text.Substring(0, 4000) + "…";
		}
		TextBlock body = Ui.Txt(text, 12.5);
		body.TextWrapping = TextWrapping.Wrap;
		body.LineHeight = 19.0;
		SPanel panel = Ui.V(6.0, Ui.Small(WidgetUI.TitleOf(widget)), body);
		return Ui.Card(panel, 10.0);
	}

	/// <summary>时钟:按 settings.format(.NET 时间格式,默认 HH:mm)每秒刷新;离开可视树就停表。</summary>
	public static UIElement Clock(WidgetDef widget)
	{
		string format = WidgetCommon.Str(widget.Settings, "format", "HH:mm").Trim();
		if (format.Length == 0 || format.Length > 32)
		{
			format = "HH:mm";
		}
		TextBlock big = Ui.Txt(SafeTime(format), 26.0, bold: true);
		TextBlock caption = Ui.Small(WidgetUI.TitleOf(widget));
		SPanel panel = Ui.V(2.0, big, caption);
		DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		timer.Tick += delegate
		{
			string next = SafeTime(format);
			if (!string.Equals(big.Text, next, StringComparison.Ordinal))
			{
				big.Text = next;
			}
		};
		// 只在可视时走表:摘下来就停,避免计时器把内容一直挂在内存里
		panel.Loaded += delegate
		{
			timer.Start();
		};
		panel.Unloaded += delegate
		{
			timer.Stop();
		};
		return Ui.Card(panel, 10.0);
	}

	/// <summary>分隔线:复用 Ui.Sep() 的 B.LineSoft 1px 上分隔线。</summary>
	public static UIElement Divider(WidgetDef widget)
	{
		return Ui.Sep();
	}

	// 时间格式非法时退回 HH:mm(绝不因为用户格式串抛异常)
	private static string SafeTime(string format)
	{
		try
		{
			return DateTime.Now.ToString(format);
		}
		catch (FormatException)
		{
			return DateTime.Now.ToString("HH:mm");
		}
		catch (Exception)
		{
			return "";
		}
	}
}
