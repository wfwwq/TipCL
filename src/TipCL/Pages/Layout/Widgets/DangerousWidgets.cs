using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Core.Launcher;
using TipCL.Services;

namespace TipCL.Pages.Layout;

// 危险类型区块(§0.1 Q5):web(网页)与 button(按钮),**默认关闭**。
// 只有布局文档里显式写 settings.enabled = true 才渲染内容;否则显示「已禁用的自建区块」占位(数据保留)。
// 两条安全线:
//   web    —— 只允许 https(禁 file:// / javascript: / data:),复用插件的 WebView 外壳(只在该源内导航,外链交系统浏览器);
//   button —— 只允许内部动作白名单:切页 / 启动指定实例 / 打开启动器数据目录 / 触发只读 RPC,其它一律拒绝并给中文原因。
internal static class DangerousWidgets
{
	/// <summary>按类型分派(两种都先过 Q5 开关)。</summary>
	public static UIElement Build(WidgetDef widget)
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

	private static UIElement Web(WidgetDef widget)
	{
		string raw = WidgetCommon.Str(widget.Settings, "url", "");
		if (!WidgetCommon.TryAcceptWebUrl(raw, out string url, out string reason))
		{
			return WidgetUI.Placeholder(I18n.L("网页地址不合法"), reason);
		}
		if (!WebViewHost.Available)
		{
			Button open = Ui.Btn(I18n.L("在浏览器中打开"), BtnKind.Soft, delegate
			{
				Ui.OpenUrl(url);
			}, Ico.Play2);
			return Ui.Card(Ui.V(6.0, Ui.Small(I18n.L("当前系统不能内嵌网页")).Wrap(), Ui.H(8.0, open, Ui.Spring())), 10.0);
		}
		return new WebShell(url);
	}

	// 复用插件的网页视图:它自己算 origin,只允许停留在该源内,其它链接交给系统浏览器
	private sealed class WebShell : EditAwareShell
	{
		private readonly string _url;

		private PluginWebView? _view;

		public WebShell(string url)
			: base(disposeOnDetach: true)
		{
			_url = url;
			base.Loaded += delegate
			{
				EnsureView();
			};
		}

		private void EnsureView()
		{
			if (_view != null)
			{
				return;
			}
			_view = new PluginWebView(_url, "layout-widget", null);
			_view.Suppressed = Editing;
			base.Content = _view;
		}

		// WebView2(HwndHost)会盖住拖拽手柄,编辑态临时藏起来
		protected override void OnEditChanged()
		{
			if (_view != null)
			{
				_view.Suppressed = Editing;
			}
		}

		// 被摘下来(切页 / 编辑态重排):销毁浏览器,下次挂回来时重建
		protected override void DisposeContent()
		{
			PluginWebView? view = _view;
			_view = null;
			base.Content = null;
			try
			{
				view?.Dispose();
			}
			catch (Exception)
			{
			}
		}
	}

	// ————— button —————

	private static UIElement Button(WidgetDef widget)
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
			AppServices.Window?.Navigate(arg1);
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
			AppServices.Window?.Navigate("launch");
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
				text = text.Substring(0, 200) + "…";
			}
			AppServices.Toast(name, text, ToastKind.Info);
		}
		catch (Exception ex)
		{
			AppServices.Toast(I18n.L("这个动作没能执行"), ex.Message, ToastKind.Error);
		}
	}
}
