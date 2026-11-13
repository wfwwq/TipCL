using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C5b:把「下载任务」页注册进 <see cref="PageRegistry"/>(页面 id = <c>tasks</c>,见 <c>MainWindow.NavDefs</c> 里
/// <c>new NavDef("tasks", I18n.L("下载任务"), "\ue9d5", …)</c> 那一项;mac 外壳的工厂是 <c>null</c>,只认注册表)。
///
/// 为什么用 <c>[ModuleInitializer]</c>:与 <see cref="LaunchPageRegistration"/> / <c>InstancePageRegistration</c>
/// / <c>LibraryPageRegistration</c> 同一套理由(D-125)——
/// 模块加载时、任何代码之前注册好,于是外壳构造函数、<c>--smoke</c> 的 <c>new MainWindow()</c>
/// 以及测试里的 <c>PageRegistry.Snapshot()</c> 都自动看得到它;而注册写进 <c>Program</c> / <c>App</c> /
/// <c>MainWindow</c> 都要改外壳文件(红线:不许动外壳)。
/// 同一 id 的重复注册是**覆盖**语义,测试里也可以直接调 <see cref="Register"/>。
/// </summary>
internal static class TasksPageRegistration
{
	/// <summary>页面 id:与 <c>MainWindow.NavDefs</c> 的 <c>"tasks"</c> / <c>NavModel.PageForNavKey["tasks"]</c> 相同。</summary>
	internal const string PageId = "tasks";

	/// <summary>注册工厂(幂等:<c>Register</c> 对同一 id 是覆盖,重复调用不会抛)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new TasksPage());
	}
}
