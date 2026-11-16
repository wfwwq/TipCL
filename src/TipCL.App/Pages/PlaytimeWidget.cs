using System;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// R32-MWG:Windows 版 <c>Pages\Layout\Widgets\PlaytimeWidget.cs</c>(WPF / net48)到 mac(Avalonia 11 / net8.0)的移植。
/// 游戏时长区块(§4.F 的 playtime):读启动器自己的 playtime.json(只读,不写),
/// 不给 instance 就显示所有实例的累计时长,给了就显示那个实例的。
/// 卡片在界面上常驻,不能只在建卡时读一次:重新显示(Loaded)时重读,一局游戏结束时由壳层调 NotifyChanged 重读。
///
/// Windows → mac 差异(逐条):
/// <list type="number">
/// <item><b>namespace</b> → <c>TipCL.Pages</c>(R32-D7 扁平);</item>
/// <item><b>UIElement → Control</b>;返回的是 <c>ContentControl</c>(及其内容),都是 Control;</item>
/// <item>用到的桥接件在 mac 同名同签名:<c>Py.Float(object)</c> / <c>PyDict.Get(string, object)</c> /
///   <c>Playtime.GetTotalPlaytime()</c> / <c>Playtime.GetPlaytime(object)</c> /
///   <c>Playtime.FormatDuration(object)</c>(TipCL.Core.Launcher)。零改写;</item>
/// <item><c>HorizontalAlignment.Stretch</c> / <c>VerticalAlignment.Stretch</c>:Avalonia 的同名枚举在
///   <c>Avalonia.Layout</c> 命名空间下,写法一致。</item>
/// </list>
/// </summary>
internal static class PlaytimeWidget
{
	private static event Action? Changed;

	/// <summary>
	/// 游戏时长变了:界面上的时长块各自重读。只在 UI 线程调(壳层收到 game_exited 时;
	/// 后端先停计时写盘再发这个事件,所以此时读到的已经含刚结束的一局)。
	/// </summary>
	public static void NotifyChanged()
	{
		Changed?.Invoke();
	}

	/// <summary>渲染:大号时长 + 说明。</summary>
	public static Control Build(WidgetDef widget)
	{
		string instance = WidgetCommon.Str(widget.Settings, "instance", "").Trim();
		string title = WidgetUI.TitleOf(widget);
		ContentControl host = new ContentControl
		{
			Focusable = false,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			VerticalContentAlignment = VerticalAlignment.Stretch
		};
		Action refresh = delegate
		{
			host.Content = Render(title, instance);
		};
		refresh();
		bool shown = false;
		host.Loaded += delegate
		{
			// 首次 Loaded 用建卡时读的那份;之后每次回到界面(切页回来、编辑态重挂)都重读
			if (shown)
			{
				refresh();
			}
			shown = true;
			Changed -= refresh;
			Changed += refresh;
		};
		// 离开界面就退订:静态事件不能拖住已经丢弃的卡片
		host.Unloaded += delegate
		{
			Changed -= refresh;
		};
		return host;
	}

	private static Control Render(string title, string instance)
	{
		double seconds = -1.0;
		try
		{
			if (instance.Length == 0)
			{
				seconds = Py.Float(Playtime.GetTotalPlaytime());
			}
			else if (instance.Length <= 128)
			{
				PyDict data = Playtime.GetPlaytime(instance);
				seconds = Py.Float(data.Get("total") ?? 0L);
			}
		}
		catch (Exception)
		{
			// 读不到时长数据(文件损坏 / 首次运行)不能让整页渲染失败
			seconds = -1.0;
		}
		string caption = (instance.Length == 0) ? I18n.L("全部实例累计") : instance;
		if (seconds < 0.0)
		{
			return WidgetUI.Placeholder(title, I18n.L("读不到游戏时长数据。"));
		}
		TextBlock big = (seconds > 0.0) ? Ui.Txt(Playtime.FormatDuration(seconds), 22.0, bold: true) : Ui.Muted(I18n.L("还没有游戏时长记录"));
		SPanel panel = Ui.V(4.0, Ui.Small(title), big, Ui.Small(caption));
		return Ui.Card(panel, 10.0);
	}
}
