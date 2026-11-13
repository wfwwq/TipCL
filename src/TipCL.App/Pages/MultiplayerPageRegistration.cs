using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C6b:把「联机」页注册进 <see cref="PageRegistry"/>(页面 id = <c>multiplayer</c>,见 <c>MainWindow.NavDefs</c> 里
/// <c>new NavDef("multiplayer", I18n.L("联机"), "\ue701", …)</c> 那一项;mac 外壳的工厂是 <c>null</c>,只认注册表)。
///
/// 与 <see cref="LaunchPageRegistration"/> / <c>InstancePageRegistration</c> / <c>LibraryPageRegistration</c> /
/// <see cref="TasksPageRegistration"/> / <see cref="AccountPageRegistration"/> 同一套 <c>[ModuleInitializer]</c>
/// 口径(D-125):模块加载时、任何代码之前注册好,于是外壳构造函数、<c>--smoke --page multiplayer</c>
/// 与测试都自动看得到它;而注册写进 <c>Program</c> / <c>App</c> / <c>MainWindow</c> 都要改外壳文件(红线)。
/// 同一 id 的重复注册是**覆盖**语义,测试里也可以直接调 <see cref="Register"/>。
///
/// 注:在它之前,<c>--page multiplayer</c> 与不带 <c>--page</c> 的首屏**逐字节相同**
/// (那个 id 没有工厂 ⇒ <c>Navigate</c> 静默不动,与 C3c 记的"其它 id 什么都不切"一致);注册之后它会变成
/// 一个**新基准**(C6b 的冒烟表里登记)。
/// </summary>
internal static class MultiplayerPageRegistration
{
	/// <summary>页面 id:与 <c>MainWindow.NavDefs</c> 的 <c>"multiplayer"</c> / <c>NavModel.PageForNavKey["multiplayer"]</c> 相同。</summary>
	internal const string PageId = "multiplayer";

	/// <summary>注册工厂(幂等:<c>Register</c> 对同一 id 是覆盖,重复调用不会抛)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new MultiplayerPage());
	}
}
