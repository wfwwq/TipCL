using System.Runtime.CompilerServices;
using TipCL.Pages.Plugins;

namespace TipCL.Pages;

/// <summary>
/// C7b(批4 块1):把「插件」页注册进 <see cref="PageRegistry"/>(页面 id = <c>plugins</c>)。
///
/// 为什么用 <c>[ModuleInitializer]</c>:与 <see cref="TasksPageRegistration"/> / `LibraryPageRegistration` 等同一套理由(D-125)——
/// 模块加载时、任何代码之前注册好,于是外壳构造函数、<c>--smoke</c> 的 <c>new MainWindow()</c>
/// 以及测试里的 <c>PageRegistry.Snapshot()</c> 都自动看得到它;而把注册写进 <c>Program</c>/<c>App</c>/<c>MainWindow</c>
/// 都要改外壳文件。同一 id 的重复注册是**覆盖**语义,测试里也可以直接调 <see cref="Register"/>。
/// </summary>
internal static class PluginsPageRegistration
{
	/// <summary>页面 id:与 Windows <c>MainWindow.NavDefs</c> 里 <c>"plugins"</c> 那一项相同。</summary>
	internal const string PageId = "plugins";

	/// <summary>注册工厂(幂等:<c>Register</c> 对同一 id 是覆盖,重复调用不会抛)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new PluginsPage());
	}
}
