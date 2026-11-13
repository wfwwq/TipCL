using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C4:把实例管理页注册进 <see cref="PageRegistry"/>(页面 id = <c>instance</c>)。
///
/// <b>为什么是 <c>instance</c> 而不是别的</b>:mac 的导航表 <c>NavModel.PageForNavKey["instance"]</c>
/// 就是 <c>"instance"</c>(与 Windows 同名同值),侧栏那一行的标题是「版本管理」(<c>NavModel.SubTitles</c>)。
/// 页面类自己的 <c>Title</c> 仍是 Windows 里的「实例」(逐字照抄,不改)。
///
/// 注册方式与 <see cref="LaunchPageRegistration"/> 完全一致:<c>[ModuleInitializer]</c> 在**模块加载时**
/// 就注册好,所以外壳构造函数里那次 <c>Navigate("launch")</c>、以及 <c>--smoke</c> 只 <c>new MainWindow</c>
/// 的路径都自动生效;测试里重复调用也安全(<c>PageRegistry.Register</c> 对同一 id 是覆盖语义,见 D-125)。
///
/// <b>注意</b>:<c>InstanceDetailPage</c> **不在这里注册** —— 它在 Windows 里没有自己的导航 id,
/// 是被实例库「实例详情」按钮换进页内容里的(见 <see cref="InstanceDetailPage"/> 的类注释)。
/// </summary>
internal static class InstancePageRegistration
{
	/// <summary>页面 id:与 <c>NavModel.PageForNavKey["instance"]</c> 相同。</summary>
	internal const string PageId = "instance";

	/// <summary>注册工厂(幂等)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new InstancePage());
	}
}
