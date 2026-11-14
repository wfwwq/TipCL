using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C7a:把**版本管理页**注册进 <see cref="PageRegistry"/>(页面 id = <c>instance</c>)。
///
/// <b>为什么是 <c>instance</c> 而不是别的</b>:Windows 的导航表(`MainWindow.xaml.cs:108-117`)逐行是
/// <c>launch→LaunchPage</c> / <c>library→LibraryPage</c> / <c>instance→VersionManagePage</c> /
/// <c>account→AccountPage</c> / <c>ai→AiPage</c> / <c>multiplayer→MultiplayerPage</c> /
/// <c>settings→SettingsPage</c> / <c>tasks→TasksPage</c> / <c>download→DownloadPage</c> / <c>plugins→PluginsPage</c>。
/// 侧栏那一行的标题是「版本管理」(<c>NavModel.SubTitles["instance"]</c>),本页自己的
/// <see cref="VersionManagePage.Title"/> 也正好是「版本管理」——换页之后**侧栏与页面标题自洽**。
///
/// <b>【C7a 更正】</b>本 id 原先是 C4 注册给 <see cref="InstancePage"/> 的;核对 Windows 源码后发现
/// <c>InstancePage</c> 在原版**没有任何构造点(死代码)**,而 <c>instance</c> 在原版就是本页 ⇒ 由本文件接管。
/// <see cref="InstancePageRegistration"/>(原注册方)已改成**非模块初始化器**、只留测试用的还原入口(D-126)。
///
/// 注册方式与 <see cref="LaunchPageRegistration"/> 等完全一致(<c>[ModuleInitializer]</c>,D-125):模块加载时就
/// 注册好,所以外壳构造函数里的首屏 <c>Navigate("launch")</c> 与 <c>--smoke</c>(它只 <c>new MainWindow</c>)
/// 都自动生效,不必动外壳一个字;测试里重复调用也安全(<c>Register</c> 对同一 id 是覆盖语义)。
///
/// 另外<b>本页也被实例详情页的「设置」分段内嵌</b>(Windows <c>InstanceDetailPage.cs:189</c> 的
/// <c>new VersionManagePage { Folder = _inst }</c>)——那条缝由 <see cref="VersionManagePageSettingsRegistration"/>
/// 接上,C7a 顺带消掉了 D-134 记的那处用户可见降级。
/// </summary>
internal static class VersionManagePageRegistration
{
	/// <summary>页面 id:与 <c>NavModel.PageForNavKey["instance"]</c> / <c>MainWindow.NavDefs</c> 那一行相同。</summary>
	internal const string PageId = "instance";

	/// <summary>注册工厂(幂等)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new VersionManagePage());
	}
}
