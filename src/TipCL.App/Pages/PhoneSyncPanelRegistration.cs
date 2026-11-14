using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// U6:把「手机同步」真面板接到设置页的注入缝上 ——
/// <c>SettingsPage.PhoneSyncPanelRunner</c>(= <c>SettingsPage.PhoneSync.cs:25</c>)从此不再是 null,
/// 设置页 :97 的「打开同步面板」按钮直接弹 <see cref="PhoneSyncPanel.ShowAsync"/>,那条如实降级
/// Toast(「手机同步面板还没移植」)随之关掉 —— 与 Windows 版 <c>SettingsPage.PhoneSync.cs:71</c> 的
/// <c>PageBase.Run(PhoneSyncPanel.ShowAsync, ...)</c> 同一形状。
///
/// 为什么用 <c>[ModuleInitializer]</c>:与 <see cref="TasksPageRegistration"/> /
/// <see cref="LaunchPageRegistration"/> 同一套理由(D-125)—— 模块加载时、任何代码之前接好,
/// 于是外壳构造函数、<c>--smoke</c> 的 <c>new MainWindow()</c> 以及测试里的
/// <c>new SettingsPage()</c> 都自动看得到它;而把这行写进 <c>Program</c> / <c>App</c> /
/// <c>MainWindow</c> 都要改外壳或设置页的共享文件(红线:本次只许新增/改自己的文件)。
///
/// 一处需要留档的副作用:给这个静态属性赋值会**提前**触发 <c>SettingsPage</c> 的类型初始化
/// (它的 <c>Extras</c> 会读一次 <c>wpf-theme.json</c>,只读 + try/catch 兜底)。
/// 测试进程里 <c>TestHomeIsolation</c> 的模块初始化器在本程序集之前跑完,所以读的还是隔离 home
/// (C5aFixtures.cs:105 本来也会在作用域构造时触碰同一批静态量)。
/// </summary>
internal static class PhoneSyncPanelRegistration
{
	/// <summary>接上设置页的「打开同步面板」缝(幂等:重复赋值只是写同一个方法组)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		SettingsPage.PhoneSyncPanelRunner = PhoneSyncPanel.ShowAsync;
	}
}
