using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// M6:把「启动向导」接到设置页的注入缝上 —— <c>SettingsPage.FirstRunWizardRunner</c>
/// (= <c>SettingsPage.cs:401</c>)从此不再是 null:<c>SettingsPage.cs:2270</c> 的「重新运行」按钮
/// 直接弹 <see cref="FirstRunWizard.ShowAsync"/>,那条如实降级 Toast(「启动向导还没移植」,
/// <c>SettingsPage.cs:2274</c>)随之关掉 —— 与 Windows 版 <c>Pages\SettingsPage.cs:718</c> 的
/// <c>PageBase.Run(FirstRunWizard.ShowAsync, I18n.L("向导没能打开"))</c> 是同一条入口、同一个方法。
///
/// <b>为什么是 <c>ShowAsync</c> 而不是 <c>MaybeShowAsync</c></b>:Windows 有两条不同的接线点 ——
/// 设置页这条用 <see cref="FirstRunWizard.ShowAsync"/>(点了就开,语义是「重新运行向导」),
/// 而启动路径那条是 <c>MainWindow.xaml.cs:380</c> 的 <c>FirstRunWizard.MaybeShowAsync();</c>
/// (先问后端 <c>get_setting("first_run")</c>,为真才弹)。这里只接设置页这条:接错的后果是
/// "点了没反应"(用户在设置里主动点按钮,却因为 first_run 已经是 false 而不弹)。
///
/// <b>启动路径未接(如实留档)</b>:mac 外壳里没有对应 <c>MainWindow.xaml.cs:380</c> 的调用点,
/// 而那是**产品行为变更**(首次启动会自动弹窗),落在 mac 外壳文件里、不在本包的写范围 ⇒
/// 本文件只关掉设置页这条假绿,启动那条另案(lead 拍板)。
///
/// 为什么用 <c>[ModuleInitializer]</c>:与 <see cref="PhoneSyncPanelRegistration"/> /
/// <see cref="TasksPageRegistration"/> 同一套理由(D-125)—— 模块加载时、任何代码之前接好,
/// 于是外壳构造、<c>--smoke</c> 与测试里的 <c>new SettingsPage()</c> 都自动看得到它;
/// 而把这行写进 <c>Program</c> / <c>App</c> / <c>MainWindow</c> 都要改外壳或设置页的共享文件。
///
/// 一处副作用(与 <see cref="PhoneSyncPanelRegistration"/> 同一处,已留档):给这个静态属性赋值会
/// **提前**触发 <c>SettingsPage</c> 的类型初始化(它的 <c>Extras</c> 会读一次 <c>wpf-theme.json</c>,
/// 只读 + try/catch 兜底)。测试进程里 <c>TestHomeIsolation</c> 的模块初始化器在本程序集之前跑完,
/// 所以读的还是隔离 home。
/// </summary>
internal static class FirstRunWizardRegistration
{
	/// <summary>接上设置页的「重新运行启动向导」缝(幂等:重复赋值只是写同一个方法组)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		SettingsPage.FirstRunWizardRunner = FirstRunWizard.ShowAsync;
	}
}
