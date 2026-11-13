using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C6b:把设置页**分段 4/5/6** 内嵌的三个子页接到 C5a 留下的注入缝上。
///
/// 为什么用 <c>[ModuleInitializer]</c>(与 D-125 的页面注册同一套理由):
/// 模块加载时、任何代码之前把缝接好,于是真桌面启动、`--smoke`、以及测试里 `new SettingsPage()` 都自动看得到;
/// 而接线写进 `SettingsPage` 或外壳文件都要改别人的文件(红线)。
/// 没有接线时设置页显示的是 C5a 那句如实的占位说明(见 `SettingsPage.PlaceholderFor`)—— 接了才换真页。
///
/// <b>本包接了四个</b>(`java` / `servers` / `playtime` / `feedback`);另外两处缝不归本包:
/// <list type="bullet">
/// <item><see cref="SettingsPage.AccountPageFactory"/>(分段 1)—— 页面由 C5b 交付,接缝归 C5b/Lead;</item>
/// <item><see cref="SettingsPage.WizardFactory"/>(「重新运行启动向导」)—— 向导由 C5b 交付,接缝归 C5b/Lead。</item>
/// </list>
///
/// 另外注意:`FeedbackPage` 在 Windows 里是**字段初始化**(`new FeedbackPage()` 跟着 `SettingsPage` 的构造函数一起建),
/// 这里交出去的是 <b>工厂</b> —— 设置页只在**切到第 6 段**时才调它(Lead 在 C5a 裁决里点名要求的口径),
/// 所以构造设置页不会连带构造反馈页。
/// </summary>
internal static class C6bSegmentRegistration
{
	/// <summary>接上分段 2/4/5/6 的四个工厂(幂等:重复调用只是重新赋值)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		SettingsPage.JavaPageFactory = () => new JavaPage();
		SettingsPage.ServersPageFactory = () => new ServersPage();
		SettingsPage.PlaytimePageFactory = () => new PlaytimePage();
		SettingsPage.FeedbackPageFactory = () => new FeedbackPage();
	}
}
