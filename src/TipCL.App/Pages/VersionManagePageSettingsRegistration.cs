using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C7a:把实例详情页「设置」分段内嵌的那一页接上 —— **D-134 记的那处用户可见降级由此消除**。
///
/// Windows 的形状(`InstanceDetailPage.cs:189`)是 <c>new VersionManagePage { Folder = _inst }</c>:
/// <see cref="VersionManagePage.Folder"/> 的 setter 既预选实例、又置"嵌入态"(嵌入态下左栏的文件夹列表会收起来,
/// 因为详情页已经在给出实例上下文)。这里逐字照抄成工厂:
/// <c>folder =&gt; new VersionManagePage { Folder = folder }</c>。
///
/// 为什么用 <c>[ModuleInitializer]</c>(与 D-125 的页面注册同一套理由):模块加载时就接上,不必动外壳一个字;
/// 测试里重复调用也安全(赋值语义)。**没接缝时**详情页那一栏显示的是 C5a 写的如实占位
/// (「版本管理页还没移植」)——从本文件起,那句占位在正常启动路径上不会再出现;
/// 依赖那句占位的用例按 D-126 的口径处理(测试内先把缝置空还原前提、原断言一字不改)。
/// </summary>
internal static class VersionManagePageSettingsRegistration
{
	/// <summary>接上详情页的「设置」分段工厂(幂等:重复赋值同一个形状的工厂没副作用)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		InstanceDetailPage.SettingsPageFactory = folder => new VersionManagePage
		{
			Folder = folder,
		};
	}
}
