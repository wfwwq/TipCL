using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// M7:把「AI 助手」页注册进 <see cref="PageRegistry"/>(页面 id = <c>ai</c>)—— 这一页**首次可见**的唯一前置。
///
/// 【Windows 源】<c>MainWindow.xaml.cs:114</c> 的导航表那一行:
/// <c>new NavDef("ai", I18n.L("AI 助手"), "\ue99a", () => new AiPage())</c> —— Windows 把工厂直接写在
/// 导航表的 <c>NavDef</c> 里;mac 的外壳表(<c>MainWindow.cs:90</c>)那一行的工厂是 <c>null</c>,
/// 页面一律靠页面注册表提供(D-125 / D-92),所以这里就是那行的 mac 等价物。
///
/// <b>注册之前的状态(如实记录)</b>:mac 侧 `MainWindow.cs:90` 的 `new NavDef("ai", I18n.L("AI 助手"), "\ue99a", null)`
/// 一直在导航表里、`NavModel.TopKeys` / `DefaultNavOrder` / `PageForNavKey` 也都有 `"ai"`,
/// 但 `PageRegistry.Create("ai")` 返回 null ⇒ 点导航**静默不切页**(`PageHost.Navigate` 的既有口径:
/// 未注册即不切、不报错)。`PluginEvents.cs:317` 与 `MainWindow.cs:242` / `:1162` 那三处
/// `Navigate("ai")` 也是同一个下场。注册之后它们**全都自动生效**,无需改一行外壳代码。
///
/// 为什么用 <c>[ModuleInitializer]</c>:与 <see cref="TasksPageRegistration"/> /
/// <see cref="DesignSheetPageRegistration"/> 同一套理由(D-125)—— 模块加载时、任何代码之前注册好,
/// 于是外壳构造函数、<c>--smoke</c> 的 <c>new MainWindow()</c> 以及测试里的 <c>PageRegistry.Snapshot()</c>
/// 都自动看得到它;同一 id 的重复注册是**覆盖**语义,测试里也可以直接调 <see cref="Register"/>。
///
/// 注册之后 <c>C7aNavTableTests</c> 那条 <c>[InlineData("ai","AiPage")]</c> 会**自动从"未注册也绿"
/// 升级为"类型名必须是 AiPage"**(该用例就是这么写的:未注册返回 null 时走允许分支,注册了就必须类型相符)
/// —— 不需要改测试。
/// </summary>
internal static class AiPageRegistration
{
	/// <summary>页面 id:与 `MainWindow.cs:90` 的 <c>new NavDef("ai", …)</c> /
	/// <c>NavModel.PageForNavKey["ai"]</c> / Windows 导航表那一行的 <c>"ai"</c> 一字不差。</summary>
	internal const string PageId = "ai";

	/// <summary>注册工厂(幂等:同一 id 重复注册 = 覆盖)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new AiPage());
	}
}
