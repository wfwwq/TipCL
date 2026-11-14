using System.Collections.Generic;
using Avalonia.Media;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>M2-B0：IconLib 全量 Path Data 可解析 + 覆盖 M2 页面所需图标。</summary>
public class IconLibTests
{
    // 这里原先有个静态构造:Application.Current == null 时自己 AppBuilder.Configure<Application>()
    // .UseHeadless(...).UseSkia().SetupWithoutStarting()。那是在**测试线程**上另起一次无头初始化,
    // 和 TestApp 的会话抢同一份进程级状态:它会把一份「归测试线程所有」的 ManagedDispatcherImpl 留在
    // 根 locator 作用域里;之后任何线程在 HeadlessUnitTestSession 的派发窗口里抢先创建
    // Dispatcher.UIThread,装进来的调度器就归测试线程所有(而不是会话线程),会话线程随后在
    // AvaloniaHeadlessPlatform.Initialize -> Compositor..ctor -> RenderLoop.Add 上偶发报
    // "Call from invalid thread"。本类四个用例本来就走 TestApp.Run 落在会话上,Geometry.Parse 需要的
    // 渲染接口在会话里已经就绪,那次额外初始化是多余的,故删掉。

    [Fact]
    public void All_Path_Data_Parse() => TestApp.Run(() =>
    {
        Assert.Empty(IconLib.ValidateAll());
    });

    [Fact]
    public void Semantic_Keys_Match_Windows_Ico_Families() => TestApp.Run(() =>
    {
        // M2 六个页面用到的核心语义键必须齐（命名对齐 Windows 版 Services/Ico.cs）
        string[] required =
        {
            "Play", "Stop", "Gear", "Search", "Refresh", "Back", "Chevron", "ChevronRight",
            "ChevronLeft", "Add", "Check", "Close", "Trash", "Folder", "Download", "Import",
            "Export", "Star", "Grid", "More", "Wifi", "User", "Box", "Package", "Puzzle",
            "World", "Clock", "List", "Edit", "Copy", "Save", "Info", "Warning", "Error",
            "Success", "Palette", "Server", "Broom", "Lightning", "Home", "Open", "Sort",
            "Filter", "Image", "Cloud", "Chat", "Send", "Moon", "Sun", "Shield",
        };
        foreach (string key in required)
            Assert.True(IconLib.All.ContainsKey(key), "缺图标键 " + key);
    });

    [Fact]
    public void Of_Falls_Back_To_Info() => TestApp.Run(() =>
    {
        Assert.Equal(IconLib.InfoData, IconLib.Of("不存在"));
        Assert.Equal(IconLib.PlayData, IconLib.Of("Play"));
    });

    [Fact]
    public void Parse_Geometries_Are_NonEmpty() => TestApp.Run(() =>
    {
        foreach (var kv in IconLib.All)
        {
            Geometry g = Geometry.Parse(kv.Value);
            Assert.NotNull(g);
        }
    });
}
