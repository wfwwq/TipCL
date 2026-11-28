using System;
using System.Threading.Tasks;
using System.Windows;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Services;

/// <summary>
/// SEC-MCP-2：外部 agent 经 MCP 发起写操作时，在启动器里弹确认（与 Linux 版一致）。
/// 回调在 MCP 后台线程上被调用，这里切到 UI 线程弹 <see cref="Dlg.Confirm"/>，并把主窗口置前。
/// </summary>
public static class McpConfirmUi
{
	public static void Install()
	{
		McpServer.ConfirmWrite = Confirm;
	}

	private static Task<bool> Confirm(string tool, string argsJson)
	{
		Application app = Application.Current;
		if (app == null) return Task.FromResult(false);
		return app.Dispatcher.Invoke(delegate
		{
			try
			{
				Window w = app.MainWindow;
				if (w != null)
				{
					if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
					w.Activate();
				}
			}
			catch (Exception)
			{
			}
			string args = argsJson ?? "{}";
			if (args.Length > 400) args = args.Substring(0, 400) + "…";
			string body = I18n.L("外部 AI 工具请求修改你的游戏数据。") + "\n\n" + tool + "\n" + args + "\n\n" + I18n.L("不认识这个请求就点「拒绝」。");
			return Dlg.Confirm(I18n.L("允许外部写操作？"), body, I18n.L("允许"), I18n.L("拒绝"), danger: true);
		});
	}
}
