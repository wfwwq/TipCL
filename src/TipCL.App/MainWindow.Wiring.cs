using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using TipCL.Pages;
using TipCL.Services;

namespace TipCL;

/// <summary>
/// M8(parity 收口):启动路径上那两次「首启询问」的接线 —— 首次启动向导与反馈同意。
///
/// 【Windows 源】<c>MainWindow.xaml.cs:378-385</c>(Windows 在 connect 那条尾巴上顺序 await 这两句):
/// <code>
/// if (!Smoke.Active) { await FirstRunWizard.MaybeShowAsync(); }
/// if (!Smoke.Active) { await FeedbackConsent.MaybeAskAsync(); }
/// </code>
///
/// mac 侧三处差异(逐条,理由写在行内):
/// <list type="number">
/// <item><b>两道闸</b>(<see cref="StartupPromptsAllowed"/>):① <c>!Smoke.SmokeState.Active</c>
///   (与 Windows 的 <c>if (!Smoke.Active)</c> 同口径);②
///   <c>ApplicationLifetime is IClassicDesktopStyleApplicationLifetime</c> —— <b>只认真桌面启动路径</b>。
///   测试进程与 <c>--smoke</c> 都走 <c>AppBuilder.SetupWithoutStarting()</c>,那里
///   <c>Application.Current.ApplicationLifetime</c> 恒为 <c>null</c>(本仓实测,D-69:
///   C2aDlgMotionTests.cs:498 直接断言 null、C2bWiringTests.cs:84-85 写明了原因),
///   所以这两条接线对测试与冒烟像素**不可见**。少了闸 ②,<c>C2dShellWiringTests</c> 那 5 处
///   <c>ConnectBlocking</c> 会在连上之后真的弹卡,而那些用例**没挂 <c>Dlg.Host</c>** —— 属没被覆盖过的路径;</item>
/// <item><b>fire-and-forget</b>:Windows 敢 <c>await</c> 是因为它的 connect 链本来就是 UI 线程上的 async 尾巴;
///   而这两次询问都要**等人点**,mac 不能让启动链停在这儿 ⇒ 走 <see cref="PageBase.Run"/>,而且两次**各自**兜异常
///   (向导那一步失败不该把「反馈同意」那一问一起吞掉 —— Windows 的 await 会直接把它抛进 connect 链,
///   这里改成记日志,由 <c>PageBase.Run</c> 兜底并如实 Toast);</item>
/// <item><b>顺序照 Windows</b>:先向导、后反馈同意;并顺手接上 <see cref="FirstRunWizard.SettingsRefresh"/>
///   注入缝(Windows 那句是 <c>AppServices.Window?.GetPage("settings") is SettingsPage page → page.RefreshAsync()</c>,
///   mac 侧走 <see cref="MainWindow.GetPage"/>)。</item>
/// </list>
///
/// <b>需真 macOS 手验</b>(测试与 <c>--smoke</c> 都看不见这条路径):见
/// <c>_tmp/tasks/mac-widgets/REPORT.md</c> 的 M8 一节 —— 首次启动会弹四步向导、向导走完/跳过后弹
/// 「是否上传诊断数据」那张确认卡、向导完成后设置页会自己刷新一次。
/// </summary>
internal sealed partial class MainWindow
{
	/// <summary>
	/// 首启询问放不放行(= 两道闸同时成立)。抽成成员是为了**能被证伪**:非桌面生命周期下它就是 <c>false</c>
	/// (影子树探针 <c>M8StartupGateProbeTests</c> 直接断言这条,并配一条"直接调询问就会出现卡"的对照)。
	/// </summary>
	internal static bool StartupPromptsAllowed()
	{
		if (Smoke.SmokeState.Active)
		{
			return false;
		}

		return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;
	}

	/// <summary>
	/// 两次首启询问:先向导、后反馈同意(顺序照 Windows <c>:378-385</c>);各自兜异常,不互相拖累。
	/// </summary>
	private async Task AskStartupQuestionsAsync()
	{
		try
		{
			await FirstRunWizard.MaybeShowAsync();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[wizard] 首启向导失败: " + ex);
		}

		try
		{
			await FeedbackConsent.MaybeAskAsync();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[consent] 反馈同意询问失败: " + ex);
		}
	}

	/// <summary>接上向导的收尾刷新缝,然后起那两次询问(只在真桌面路径上调,判据见 <see cref="StartupPromptsAllowed"/>)。</summary>
	private void AskStartupQuestions()
	{
		// 【Windows 源】MainWindow.xaml.cs:662-670:向导收尾会刷新设置页
		// (AppServices.Window?.GetPage("settings") is SettingsPage page → page.RefreshAsync())。
		// mac 侧这一步是注入缝(FirstRunWizard.SettingsRefresh),只有真桌面路径才接得上设置页。
		FirstRunWizard.SettingsRefresh = delegate
		{
			PageBase? settings = GetPage("settings");
			return (settings is null) ? Task.CompletedTask : settings.RefreshAsync();
		};

		PageBase.Run(AskStartupQuestionsAsync, I18n.L("向导没能打开"));
	}
}
