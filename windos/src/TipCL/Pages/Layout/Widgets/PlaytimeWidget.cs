using System;
using System.Windows;
using System.Windows.Controls;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 游戏时长区块(§4.F 的 playtime):读启动器自己的 playtime.json(只读,不写),
// 不给 instance 就显示所有实例的累计时长,给了就显示那个实例的。
internal static class PlaytimeWidget
{
	/// <summary>渲染:大号时长 + 说明。</summary>
	public static UIElement Build(WidgetDef widget)
	{
		string instance = WidgetCommon.Str(widget.Settings, "instance", "").Trim();
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
		string title = WidgetUI.TitleOf(widget);
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
