using System;
using TipCL.Core.Launcher;

namespace TipCL.App.Shell;

/// <summary>
/// 顶部页签注册表。对齐 Windows v2 外壳(`MainWindow.xaml`):
/// 标题栏 5 页签「启动 / 实例库 / 下载 / 插件 / 设置」,无侧边导航;
/// AI 与账号走标题栏右侧钮(见 `TitleBar`),Java / 关于 是设置页内层导航项。
/// </summary>
public static class NavRegistry
{
    /// <summary>顶部 5 页签(与 Windows `MainWindow.xaml` Tab.Top 同序同文案)。</summary>
    public static readonly (string Id, string Title)[] TopTabs =
    {
        ("launch", I18nCore.Tr("启动")),
        ("library", I18nCore.Tr("实例库")),
        ("download", I18nCore.Tr("下载")),
        ("plugins", I18nCore.Tr("插件")),
        ("settings", I18nCore.Tr("设置")),
    };
}
