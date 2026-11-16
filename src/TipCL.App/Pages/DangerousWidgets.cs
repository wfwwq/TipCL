using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// R32-MWG:Windows 版 <c>Pages\Layout\Widgets\DangerousWidgets.cs</c>(WPF / net48)到 mac(Avalonia 11 / net8.0)的移植。
/// 危险类型区块(§0.1 Q5):web(网页)与 button(按钮),**默认关闭**。
/// 只有布局文档里显式写 settings.enabled = true 才渲染内容;否则显示「已禁用的自建区块」占位(数据保留)。
/// 两条安全线:
///   web    —— 只允许 https(禁 file:// / javascript: / data:);
///   button —— 只允许内部动作白名单:切页 / 启动指定实例 / 打开启动器数据目录 / 触发只读 RPC,
///             其它一律拒绝并给中文原因。
///
/// Windows → mac 差异(逐条,含降级):
/// <list type="number">
/// <item><b>namespace</b> → <c>TipCL.Pages</c>(R32-D7 扁平);UIElement → Control;</item>
/// <item><b>【降级 D2 / GOAL.md 34 (D-154)】web 类型永远走"在浏览器中打开"分支</b>:
///   Windows 在 <c>WebViewHost.Available</c> 为真时 new 一个内嵌 WebShell(HwndHost + WebView2)。
///   mac 树没有 WebView2 / 没有 <c>WebViewHost</c> / 没有 <c>PluginWebView</c>(全树 grep 证实),
///   内嵌浏览器降级为"交系统浏览器打开"——这也是 GOAL.md 第 34 行给 mac 定的既有结论
///   (先例:Pages/Plugins/PluginDashCards.cs 的插件卡片同样不内嵌)。
///   因此本文件**不含 WebShell**;</item>
/// <item><b>导航</b>:<c>AppServices.Window?.Navigate(id)</c> → <c>(AppServices.Window as MainWindow)?.Navigate(id)</c>
///   (mac 的 Window 是 Avalonia Window,没有 Navigate;见 ShortcutWidget 同类说明);</item>
/// <item><b>文件夹动作</b>:<c>Ui.OpenUrl(Utils.ROOT)</c>;mac 同名(<c>Utils.ROOT</c> 在 TipCL.Core.Launcher,
///   <c>Ui.OpenUrl(string)</c> 在 TipCL.Services);</item>
/// <item>CallRpcAsync 的 <c>BridgeClient.CallAsync(string)</c> 在 mac 返回 <c>Task&lt;JsonElement&gt;</c>
///   (签名一致);ToastKind / AppServices.Toast / I18n.L 同名。</item>
/// </list>
/// </summary>
internal static class DangerousWidgets
{
	/// <summary>按类型分派(两种都先过 Q5 开关)。</summary>
	public static Control Build(WidgetDef widget)
	{
		if (!WidgetCommon.IsEnabled(widget))
		{
			return WidgetUI.DisabledDangerous(widget);
		}
		if (widget.Type == LayoutSchema.WidgetWeb)
		{
			return Web(widget);
		}
		return Button(widget);
	}

	// ————— web —————

	private static Control Web(WidgetDef widget)
	{
		string raw = WidgetCommon.Str(widget.Settings, "url", "");
		if (!WidgetCommon.TryAcceptWebUrl(raw, out string url, out string reason))
		{
			return WidgetUI.Placeholder(I18n.L("网页地址不合法"), reason);
		}
		// 【mac 降级 D2】没有内嵌浏览器:直接给"在浏览器中打开"入口(Windows 在这里按 WebViewHost.Available 二选一)
		Button open = Ui.Btn(I18n.L("在浏览器中打开"), BtnKind.Soft, delegate
		{
			Ui.OpenUrl(url);
		}, Ico.Play2);
		return Ui.Card(Ui.V(6.0, Ui.Small(I18n.L("当前系统不能内嵌网页")).Wrap(), Ui.H(8.0, open, Ui.Spring())), 10.0);
	}

	// ————— button —————

	private static Control Button(WidgetDef widget)
	{
		string action = WidgetCommon.Str(widget.Settings, "action", "");
		if (!WidgetCommon.TryParseAction(action, out string kind, out string arg1, out string arg2, out string reason))
		{
			return WidgetUI.Placeholder(I18n.L("按钮动作不合法"), reason);
		}
		Button button = Ui.Btn(WidgetUI.TitleOf(widget), BtnKind.Normal, delegate
		{
			Run(kind, arg1, arg2);
		}, Ico.Play);
		SPanel panel = Ui.V(6.0, Ui.Small(WidgetUI.TypeLabel(LayoutSchema.WidgetButton)), Ui.H(8.0, button, Ui.Spring()), Ui.Small(kind + ":" + arg1));
		return Ui.Card(panel, 10.0);
	}

	// 白名单动作执行:全部走启动器自己的入口,不开任何外部命令
	private static void Run(string kind, string arg1, string arg2)
	{
		switch (kind)
		{
		case "page":
			(AppServices.Window as MainWindow)?.Navigate(arg1);
			break;
		case "folder":
			// 只允许启动器数据目录(WidgetCommon 已把别的值挡在外面)
			Ui.OpenUrl(Utils.ROOT);
			break;
		case "launch":
			_ = LaunchAsync(arg1, arg2);
			break;
		case "rpc":
			_ = CallRpcAsync(arg1);
			break;
		}
	}

	private static async Task LaunchAsync(string instance, string version)
	{
		BridgeClient? client = AppServices.Client;
		if (client == null)
		{
			AppServices.Toast(I18n.L("启动器还没准备好"), "", ToastKind.Warning);
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

	// 只读 RPC:结果截断成一行 Toast(按钮不该弹大段内容)
	private static async Task CallRpcAsync(string name)
	{
		BridgeClient? client = AppServices.Client;
		if (client == null)
		{
			AppServices.Toast(I18n.L("启动器还没准备好"), "", ToastKind.Warning);
			return;
		}
		try
		{
			JsonElement result = await client.CallAsync(name);
			string text = result.GetRawText();
			if (text.Length > 200)
			{
				text = text.Substring(0, 200) + "...";
			}
			AppServices.Toast(name, text, ToastKind.Info);
		}
		catch (Exception ex)
		{
			AppServices.Toast(I18n.L("这个动作没能执行"), ex.Message, ToastKind.Error);
		}
	}
}
