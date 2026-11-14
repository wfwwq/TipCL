using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui; // 命名空间 TipCL.App.Pages.* 下 `Ui` 会被解析成命名空间而非类（B0/B2 同款约定）

namespace TipCL.App.Pages.Download;

// 加载器官方图标（移植自 Windows 版 Services/LoaderIcons.cs + Thumbs.cs/ThumbTile.cs 的 PREMIUM 路线）。
// R2 报告 §3 建议：5 张官方图直接嵌入（grass/fabric/forge/neoforge/quilt，共 ~23KB，Linux 无体积红线），
// 离线也有图；解码缓存复用；解码失败 / 未知加载器回落名称首字母方块（B.AccentSoft 底 / B.AccentDeep 字）。
// 嵌入字节数组免改 csproj（资源项改动属依赖申请，base64 常量等效且零依赖）。
public static class LoaderIcons
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

    /// <summary>图标 Tile：36×36 圆角 9（install2 卡内 / 列表行复用），异常静默回落首字母。</summary>
    public static Control Tile(string loader, string name, double size = 36.0)
    {
        var panel = new Panel { Width = size, Height = size };
        var letter = new TextBlock
        {
            Text = string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name[0]).ToString(),
            FontSize = Math.Max(12, size * 0.4),
            FontWeight = FontWeight.SemiBold,
            FontFamily = new FontFamily(UiClass.FontUi),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }.Dyn(TextBlock.ForegroundProperty, "B.AccentDeep");
        var box = new Border
        {
            CornerRadius = new CornerRadius(9),
            Child = letter,
        }.Dyn(Border.BackgroundProperty, "B.AccentSoft");
        panel.Children.Add(box);
        var bmp = Embedded(loader, (int)Math.Ceiling(size) * 2);
        if (bmp != null)
        {
            box.Child = new Image
            {
                Source = bmp,
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        return panel;
    }

    private static Bitmap? Embedded(string loader, int px)
    {
        string key = (loader ?? "").ToLowerInvariant() + "|" + px;
        return _cache.GetOrAdd(key, _ =>
        {
            byte[]? bytes = (loader ?? "").ToLowerInvariant() switch
            {
                "fabric" => Convert.FromBase64String(FabricBytes),
                "forge" => Convert.FromBase64String(ForgeBytes),
                "neoforge" => Convert.FromBase64String(NeoforgeBytes),
                "quilt" => Convert.FromBase64String(QuiltBytes),
                "" or "vanilla" or "无" => Convert.FromBase64String(VanillaBytes),
                _ => null,
            };
            if (bytes == null) return null;
            try
            {
                using var ms = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(ms, px);
            }
            catch
            {
                return null;
            }
        });
    }


    // ── 嵌入 PNG（来源 docs/mockups-hark/v2/grass.png、fabric_gh.png、forge_gh.png、neoforge_gh.png、quilt.svg）──
    private const string VanillaBytes =
        "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";

    private const string FabricBytes =
        "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";

    private const string ForgeBytes =
        "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" +
        "r/Azas2S1IpjzXAAAAAElFTkSuQmCC";

    private const string NeoforgeBytes =
        "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" +
        "357wDwE1z+F/3SLnNFxgFHAAAAAElFTkSuQmCC";

    private const string QuiltBytes =
        "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";
}
