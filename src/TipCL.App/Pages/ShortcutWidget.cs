using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// R32-MWG:Windows 版 <c>Pages\Layout\Widgets\ShortcutWidget.cs</c>(WPF / net48)到 mac(Avalonia 11 / net8.0)的移植。
/// 快捷启动区块(§4.F 的 shortcut):一键启动指定实例 / 版本。
/// 只调用启动器自己的 launch_game 任务,不碰任意命令。
///
/// Windows → mac 差异(逐条):
/// <list type="number">
/// <item><b>namespace</b> → <c>TipCL.Pages</c>(R32-D7 扁平);</item>
/// <item><b>UIElement → Control</b>;</item>
/// <item><b>导航</b>:Windows 用 <c>AppServices.Window?.Navigate(id)</c>(窗口类型直接暴露 Navigate)。
///   mac 的 <c>AppServices.Window</c> 是 <c>Avalonia.Controls.Window?</c>,没有 Navigate;
///   导航方法在 <c>TipCL.MainWindow.Navigate(string id, bool instant = false)</c> 上,
///   故改写为 <c>(AppServices.Window as MainWindow)?.Navigate(...)</c>;</item>
/// <item>其余(Ui.Btn / BtnKind / Ico / BridgeClient.StartTaskAsync / AppServices.Toast / I18n.L)同名同签名。</item>
/// </list>
/// </summary>
internal static class ShortcutWidget
{
	/// <summary>渲染:标题 + 一个启动按钮。</summary>
	public static Control Build(WidgetDef widget)
	{
		string instance = WidgetCommon.Str(widget.Settings, "instance", "").Trim();
		string version = WidgetCommon.Str(widget.Settings, "version", "").Trim();
		string label = WidgetUI.TitleOf(widget);
		if (instance.Length == 0)
		{
			return WidgetUI.Placeholder(I18n.L("快捷启动没配置实例"), I18n.L("在布局 JSON 里给这个区块填 settings.instance(可选 settings.version)。"));
		}
		if (instance.Length > 128 || version.Length > 128)
		{
			return WidgetUI.Placeholder(I18n.L("快捷启动配置太长"), I18n.L("实例名 / 版本号都不应超过 128 个字符。"));
		}
		Button button = Ui.Btn(label, BtnKind.Primary, delegate
		{
			_ = LaunchAsync(instance, version);
		}, Ico.Play);
		TextBlock hint = Ui.Small((version.Length > 0) ? instance + " · " + version : instance);
		SPanel panel = Ui.V(8.0, Ui.Small(WidgetUI.TypeLabel(LayoutSchema.WidgetShortcut)), Ui.H(8.0, button, Ui.Spring()), hint.Wrap());
		return Ui.Card(panel, 10.0);
	}

	// 启动走既有任务入口:参数只给实例与版本,其余(账号 / 内存 / Java)由后端按当前配置补默认
	private static async Task LaunchAsync(string instance, string version)
	{
		BridgeClient? client = AppServices.Client;
		if (client == null)
		{
			AppServices.Toast(I18n.L("启动器还没准备好"), I18n.L("稍等一下再点这个按钮。"), ToastKind.Warning);
			return;
		}
		try
		{
			Dictionary<string, object?> args = new Dictionary<string, object?>
			{
				["instance"] = instance
			};
			if (version.Length > 0)
			{
				args["version"] = version;
			}
			await client.StartTaskAsync("launch_game", args);
			(AppServices.Window as MainWindow)?.Navigate("launch");
		}
		catch (Exception ex)
		{
			AppServices.Toast(I18n.L("启动失败"), ex.Message, ToastKind.Error);
		}
	}
}
