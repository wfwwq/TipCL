using System;

namespace TipCL.App.Shell;

/// <summary>侧边导航项。</summary>
public sealed record NavItem(string Id, string Title, string Icon);

/// <summary>导航注册表：M1 为占位页注册，M2 换成真实页面。</summary>
public static class NavRegistry
{
    /// <summary>侧边分组导航（GOAL §5-M1-A3）。</summary>
    public static readonly (string Group, NavItem[] Items)[] SideGroups =
    {
        ("通用", new[]
        {
            new NavItem("dashboard", "仪表盘", "M2,2 H7 V7 H2 Z M9,2 H14 V7 H9 Z M2,9 H7 V14 H2 Z M9,9 H14 V14 H9 Z"),
            new NavItem("account", "账号", "M8,3 A2.5,2.5 0 1 1 8,8 A2.5,2.5 0 0 1 8,3 Z M3,13.5 C3,10.5 5,9 8,9 C11,9 13,10.5 13,13.5"),
            new NavItem("java", "Java", "M3,6 H12 V11 A2,2 0 0 1 10,13 H5 A2,2 0 0 1 3,11 Z M12,7 C14,7 14,9.5 12,9.5 M5.5,2 C5,3 6,3.5 5.5,4.5 M8,2 C7.5,3 8.5,3.5 8,4.5"),
        }),
        ("游戏", new[]
        {
            new NavItem("library", "实例库", "M3,2 H6 V14 H3 Z M8,2 H13 V14 H8 Z"),
            new NavItem("download", "下载", "M8,2 V10 M5,7.5 L8,10.5 L11,7.5 M2.5,13.5 H13.5"),
            new NavItem("tasks", "任务", "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 Z M8,5 V8 L10.5,9.5"),
        }),
        ("系统", new[]
        {
            new NavItem("plugins", "插件", "M6,2 V5 M10,2 V5 M4,5 H12 V8 A4,4 0 0 1 4,8 Z M8,12 V14"),
            new NavItem("settings", "设置", "M8,5.5 A2.5,2.5 0 1 1 8,10.5 A2.5,2.5 0 0 1 8,5.5 Z M8,1.5 V3 M8,13 V14.5 M1.5,8 H3 M13,8 H14.5 M3.4,3.4 L4.5,4.5 M11.5,11.5 L12.6,12.6 M3.4,12.6 L4.5,11.5 M11.5,4.5 L12.6,3.4"),
            new NavItem("about", "关于", "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 Z M8,7 V11.5 M8,4.5 V4.6"),
        }),
    };

    /// <summary>顶部页签（占位）：与 Windows 版顶栏 5 页签一致。</summary>
    public static readonly (string Id, string Title)[] TopTabs =
    {
        ("launch", "启动"),
        ("library", "实例库"),
        ("download", "下载"),
        ("plugins", "插件"),
        ("settings", "设置"),
    };
}
