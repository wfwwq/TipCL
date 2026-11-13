using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C5a:把设置页注册进 <see cref="PageRegistry"/>(页面 id = <c>settings</c>,与导航表里的那一行一致)。
///
/// 注册方式与 <see cref="LaunchPageRegistration"/> / <see cref="InstancePageRegistration"/> 完全一致
/// (<c>[ModuleInitializer]</c>,D-125):模块加载时就注册好,所以外壳构造函数里的首屏 <c>Navigate("launch")</c>
/// 与 <c>--smoke</c>(它只 <c>new MainWindow</c>)都自动生效,不必动外壳一个字;
/// 测试里重复调用也安全(<c>PageRegistry.Register</c> 对同一 id 是覆盖语义)。
///
/// <b>设置页内嵌的那几个分段页(账号 / Java / 服务器 / 游戏时长 / 反馈与关于)不在这里注册</b> ——
/// 它们在 mac 侧还没移植,而且它们**不是导航页**(Windows 也没有把它们放进导航表),
/// 只经 <see cref="SettingsPage"/> 的注入缝内嵌(见那个类的 A 类差异说明)。
/// </summary>
internal static class SettingsPageRegistration
{
    /// <summary>页面 id:与 <c>NavModel.PageForNavKey["settings"]</c> / <c>MainWindow.NavDefs</c> 里那一行相同。</summary>
    internal const string PageId = "settings";

    /// <summary>注册工厂(幂等)。</summary>
    [ModuleInitializer]
    internal static void Register()
    {
        PageRegistry.Register(PageId, () => new SettingsPage());
    }
}
