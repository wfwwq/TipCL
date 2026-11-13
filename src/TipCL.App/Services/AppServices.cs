using System;
using System.Diagnostics;
using Avalonia.Controls;

namespace TipCL.Services;

/// <summary>
/// 全局服务定位器。与 Windows 版同名同位置(namespace TipCL.Services),公开成员:
/// <c>Client</c> / <c>Host</c> / <c>Window</c> / <c>Ready</c> / <c>Toast</c>。
///
/// mac 侧有两处**被迫的差异**(逐条记在 PROGRESS.md 的 C2a 差异表):
/// 1. <c>Window</c> 的静态类型是 <see cref="Window"/>(Avalonia 的窗口基类),不是 <c>MainWindow?</c> ——
///    mac 的 <see cref="TipCL.MainWindow"/> 是 <c>internal</c>(CS0053:公开成员不能暴露 internal 类型),
///    而 C2b 的外壳仍然是 MainWindow;同程序集内的页面写 <c>AppServices.Window as MainWindow</c> 即可取到外壳。
/// 2. Toast 走 <see cref="ToastSink"/> 委托(C2b 的外壳在接线时赋值),不再直接调
///    <c>Window.Toast(...)</c> —— 外壳类还没落地,本包不能凭空依赖它的成员;宿主为空时降级成
///    <see cref="Debug.WriteLine(string)"/>(不抛异常、不改状态,与 D-84 的 Ui.OpenUrl 同一口径)。
/// </summary>
public static class AppServices
{
	public static BridgeClient Client { get; set; }

	public static BridgeHost? Host { get; set; }

	public static Window? Window { get; set; }

	/// <summary>Toast 宿主。C2b 的外壳落地后把它设成自己的 Toast 方法(签名见下);为空时只写调试输出。</summary>
	internal static Action<string, string, ToastKind>? ToastSink { get; set; }

	public static bool Ready => Client != null;

	public static void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
	{
		Action<string, string, ToastKind>? sink = ToastSink;
		if (sink != null)
		{
			sink(title, body, kind);
			return;
		}

		Debug.WriteLine($"[toast:{kind}] {title} {body}");
	}
}
