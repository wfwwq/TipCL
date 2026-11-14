using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C4:实例管理页的注册文件。<b>【C7a 更正,Lead 裁决】它不再是模块初始化器</b> —— 见下面的说明。
///
/// <b>为什么不再自注册</b>:Windows 的导航表(`MainWindow.xaml.cs:108-117`)里 <c>instance</c> 那一行是
/// <c>new NavDef("instance", I18n.L("版本管理"), …, () =&gt; new VersionManagePage())</c> —— **原版里
/// <c>instance</c> 对应的页面是 <see cref="VersionManagePage"/>**;而 <see cref="InstancePage"/> 在整个
/// Windows 工程里**只出现在它自己的构造函数那一行**、**没有任何构造点**(全工程扫过)⇒ **它在原版是死代码**
/// (很可能是 WP-F 重构后的遗留)。C4 当时把 <c>instance</c> 注册成了本页,只对上了键名
/// (<c>NavModel.PageForNavKey["instance"]</c> 确实是 <c>"instance"</c>)、没对上页面。
/// 所以从 C7a 起:<c>instance</c> 由 <see cref="VersionManagePageRegistration"/> 注册成
/// <see cref="VersionManagePage"/>;<b>本页不注册、也不提供任何入口</b>,但**文件与它已有的 43 条测试全部保留**。
///
/// <b>为什么还留着 <see cref="PageId"/> 与 <see cref="Register"/></b>:那 43 条测试里凡是依赖
/// "`instance` 就是 InstancePage"的用例,按 D-126 的纪律要**先把前提还原再跑原断言**(测试内显式调用
/// <see cref="Register"/> 把本页装回 <c>instance</c>,用完还原),所以这两个成员是**给测试用的还原入口**,
/// 不再是启动时行为(它们没有 <c>[ModuleInitializer]</c>)。
/// </summary>
internal static class InstancePageRegistration
{
	/// <summary>页面 id(与 <c>NavModel.PageForNavKey["instance"]</c> 同名);<b>不再注册</b>,只给测试还原前提用。</summary>
	internal const string PageId = "instance";

	/// <summary>
	/// 把本页装回 <c>instance</c> —— **测试专用**的还原入口(D-126):依赖"`instance` 就是 InstancePage"的用例
	/// 先调它还原前提,收尾再把 <see cref="VersionManagePageRegistration.Register"/> 调回来(或靠作用域的快照还原)。
	/// 启动时**不会**被调用(没有 <c>[ModuleInitializer]</c>)。
	/// </summary>
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new InstancePage());
	}
}
