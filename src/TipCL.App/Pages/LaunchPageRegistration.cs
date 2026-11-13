using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C3c:把启动页注册进 <see cref="PageRegistry"/>(页面 id = <c>launch</c>,见 <c>MainWindow.NavDefs</c> 的第 0 项)。
///
/// 为什么用 <c>[ModuleInitializer]</c>:
/// <list type="number">
/// <item><c>MainWindow</c> 的构造函数第一件事就是 <c>Navigate("launch", instant: true)</c>(与 Windows 的首屏一致),
///   而在 mac 侧页面工厂只能来自 <see cref="PageRegistry"/>(D-94)。所以注册必须发生在**任何人碰这个程序集之前**
///   —— 模块初始化器就是"模块加载时、任何代码之前"这一刻;</item>
/// <item>任务书的红线是"不许动外壳",把注册写进 <c>Program</c> / <c>App</c> / <c>MainWindow</c> 都要改外壳文件;
///   放在页面自己的文件里,`--smoke`(它只 new MainWindow)与真桌面启动都自动生效;</item>
/// <item>测试里也可以直接调 <see cref="Register"/>(<c>PageRegistry.Register</c> 对同一 id 是覆盖语义),不产生重复。</item>
/// </list>
/// 页面的 id 必须与导航表一致,否则 <c>Navigate</c> 不会认(序号比较、大小写敏感,D-94)。
/// </summary>
internal static class LaunchPageRegistration
{
	/// <summary>页面 id:与 <c>MainWindow.NavDefs[0]</c> / <c>NavModel.PageForNavKey["launch"]</c> 相同。</summary>
	internal const string PageId = "launch";

	/// <summary>注册工厂(幂等:<c>Register</c> 对同一 id 是覆盖,重复调用不会抛)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new LaunchPage());
	}
}
