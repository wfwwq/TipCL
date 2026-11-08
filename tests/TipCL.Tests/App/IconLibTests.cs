using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>M2-B0：IconLib 全量 Path Data 可解析 + 覆盖 M2 页面所需图标。</summary>
public class IconLibTests
{
    static IconLibTests()
    {
        // Geometry.Parse 需要渲染接口：无头平台一次性初始化
        if (Application.Current == null)
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
    }

    [Fact]
    public void All_Path_Data_Parse()
    {
        Assert.Empty(IconLib.ValidateAll());
    }

    [Fact]
    public void Semantic_Keys_Match_Windows_Ico_Families()
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
    }

    [Fact]
    public void Of_Falls_Back_To_Info()
    {
        Assert.Equal(IconLib.InfoData, IconLib.Of("不存在"));
        Assert.Equal(IconLib.PlayData, IconLib.Of("Play"));
    }

    [Fact]
    public void Parse_Geometries_Are_NonEmpty()
    {
        foreach (var kv in IconLib.All)
        {
            Geometry g = Geometry.Parse(kv.Value);
            Assert.NotNull(g);
        }
    }
}
