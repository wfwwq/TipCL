using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// U6:把「设计样张」页注册进 <see cref="PageRegistry"/>(页面 id = <c>design-sheet</c>)。
///
/// 【Windows 源】<c>MainWindow.xaml.cs:817-828</c> 处理 <c>id == "design-sheet"</c>,注释原文
/// 「dev-only 样张页,不在导航注册」—— mac 侧没有那段"外壳认 id"的代码(它是 C2b 用页面注册表替代的),
/// 所以这里的等价物就是注册表里的一条工厂:<c>PageRegistry.Create("design-sheet")</c> 建得出这一页,
/// 而 <c>MainWindow.NavDefs</c> 一个字不动 ⇒ 侧栏与导航里不会多出任何入口(用户可见面为零,与 Windows 一致)。
///
/// 为什么用 <c>[ModuleInitializer]</c>:与 <see cref="TasksPageRegistration"/> /
/// <see cref="DownloadPageRegistration"/> 同一套理由(D-125)—— 模块加载时注册好,外壳构造、
/// <c>--smoke</c> 与测试里的 <c>PageRegistry.Snapshot()</c> 都自动看得到;同一 id 重复注册是**覆盖**语义。
/// </summary>
internal static class DesignSheetPageRegistration
{
	/// <summary>页面 id:与 Windows <c>MainWindow.xaml.cs:817-828</c> 认的那个字符串一字不差。</summary>
	internal const string PageId = "design-sheet";

	/// <summary>注册工厂(幂等:同一 id 重复注册 = 覆盖)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new DesignSheetPage());
	}
}
