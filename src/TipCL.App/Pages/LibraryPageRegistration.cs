using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C4:把实例库注册进 <see cref="PageRegistry"/>(页面 id = <c>library</c>)。
///
/// <b>这个 id 的来路(本包实测,与任务书的描述有出入,如实记录)</b>:
/// <list type="bullet">
/// <item>Windows 的 <c>MainWindow.NavDefs</c> 里有 <c>new NavDef("library", I18n.L("实例库"), "\ue80a", () =&gt; new LibraryPage())</c>
///   (Windows <c>MainWindow.xaml.cs:109</c>),所以 <c>library</c> 是**页面 id**;</item>
/// <item>但 Windows 与 mac 的 <c>NavModel.PageForNavKey</c> / <c>NavSpecs</c> / <c>GroupedNav</c> 里
///   **都没有** <c>library</c>(WP4 信息架构把「实例库」换成了「版本管理」那一行)——
///   两边的侧栏因此都不出「实例库」行,<c>PageFor("library") == null</c>,点不到它;</item>
/// <item>所以 <c>library</c> 只能靠 <c>MainWindow.Navigate("library")</c> / <c>--smoke --page library</c>
///   这类**按页面 id 直接切**的入口进入(那条路径在 mac 上走 <c>PageHost</c> → <c>ResolvePage</c> →
///   <see cref="PageRegistry"/>,见 D-94),本包照 Windows 一样把它注册好即可。</item>
/// </list>
/// 外壳 / 导航表一律**没有动**(红线);这条"实例库没有侧栏入口"的现状与 Windows 完全一致,
/// 已在 PROGRESS 的 C4 小节里写明。
///
/// 注册方式与 <see cref="LaunchPageRegistration"/> 一致:<c>[ModuleInitializer]</c> + 幂等覆盖(D-125)。
/// </summary>
internal static class LibraryPageRegistration
{
	/// <summary>页面 id:与 <c>MainWindow.NavDefs</c> 里那条 <c>"library"</c> 相同(Windows 同)。</summary>
	internal const string PageId = "library";

	/// <summary>注册工厂(幂等)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new LibraryPage());
	}
}
