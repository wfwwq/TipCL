using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C8b(块 4):把「交给 AI 修复」接上 —— **<see cref="CrashUi"/> 里那条用户可见降级
/// (点按钮只弹一条「暂未实现 / AI 修复窗口尚未移植」)由此消除**。
///
/// Windows 的形状(<c>CrashUi.cs</c> 里那个按钮的 Click)就是一句 <c>new AiFixWindow(report).Show()</c>。
/// mac 侧把它放进既有注入缝 <see cref="CrashUi.AiFixLauncher"/>(测试与离屏要能换掉它,口径同
/// <see cref="CrashUi.ClipboardWriter"/>),这里按与 Windows 一模一样的形状接线:
/// <c>report =&gt; new AiFixWindow(report).Show()</c>。
///
/// 为什么用 <c>[ModuleInitializer]</c>(与 D-125 的页面注册同一套理由):模块加载时就接上,
/// 不必动外壳一个字;重复调用也安全(赋值语义)。
/// **缝没接时**那条 Toast 仍然留在 <see cref="CrashUi.ShowAsync"/> 里,但它的身份从"未移植的降级"
/// 变成<b>防御性兜底</b> —— 正常启动路径上到不了那里(只有把缝显式置回 <c>null</c> 才走,见 C3b 用例)。
/// </summary>
internal static class AiFixLauncher
{
	/// <summary>
	/// 接上「交给 AI 修复」的窗口工厂(幂等:重复赋值同一个形状的工厂没有副作用)。
	/// 【Windows 源】<c>CrashUi.cs</c>:<c>new AiFixWindow(report).Show()</c> —— 一字不差。
	/// </summary>
	[ModuleInitializer]
	internal static void Register()
	{
		CrashUi.AiFixLauncher = report => new AiFixWindow(report).Show();
	}
}
