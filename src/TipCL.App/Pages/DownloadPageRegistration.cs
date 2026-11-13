using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C6a:把下载页注册进 <see cref="PageRegistry"/>(页面 id = <c>download</c>,与导航表里的那一行一致)。
///
/// 注册方式与 <see cref="LaunchPageRegistration"/> / <see cref="InstancePageRegistration"/> /
/// <see cref="SettingsPageRegistration"/> 完全一致(<c>[ModuleInitializer]</c>,D-125):模块加载时就注册好,
/// 所以外壳构造函数里的首屏 <c>Navigate("launch")</c> 与 <c>--smoke</c>(它只 <c>new MainWindow</c>)都自动生效,
/// 不必动外壳一个字;测试里重复调用也安全(<c>PageRegistry.Register</c> 对同一 id 是覆盖语义)。
///
/// <b>为什么三页都到位才注册</b>:<see cref="DownloadPage"/> 只是分段宿主 —— 第 0 段是它自己画的版本列表,
/// 其余六段直接 <c>new CatalogPage(...)</c>,「安装」再换进 <see cref="InstallPage"/>。少了后两个,
/// 注册出来的就是一个"六段全是空壳"的假页面 —— 所以三页一起交付、一起注册(C6a 的范围就是这三页)。
///
/// <b><see cref="CatalogPage"/> 与 <see cref="InstallPage"/> 不在这里注册</b>:Windows 的导航表里没有它们的 id,
/// 它们是被下载页换进页内容 / 分段里的子视图(照 Windows 一样处理)。
/// </summary>
internal static class DownloadPageRegistration
{
	/// <summary>页面 id:与 <c>NavModel.PageForNavKey["download"]</c> / <c>MainWindow.NavDefs</c> 里那一行相同。</summary>
	internal const string PageId = "download";

	/// <summary>注册工厂(幂等)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new DownloadPage());
	}
}
