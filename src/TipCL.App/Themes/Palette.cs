using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace TipCL.Themes;

/// <summary>
/// C1a:主题色板的代码侧入口。
///
/// 两件事:
/// 1. 把 Palette.Light.axaml / Palette.Dark.axaml 挂进 <see cref="Application.Resources"/> 的
///    <c>ThemeDictionaries</c>(Avalonia 11 的惯用法),后续 C1b 的 Controls.axaml 可以直接写
///    <c>{DynamicResource B.Ink}</c>;
/// 2. 给"按字符串查 B.* / C.*"的老代码(Ui.cs / Ico.cs 那一层,以及本包移植的 Lucide)一个
///    显式查表接口 <see cref="Brush"/> / <see cref="Color"/>。
///
/// 关键约定(见 DECISIONS.md D-68):
/// - 查不到的键**不抛异常**,返回中性灰兜底 —— 键名写错不该把窗口打崩;
/// - 变体默认取当前主题;传 <see cref="ThemeVariant.Default"/> 时按浅色处理(与 A1 的占位色
///   同一套判定:只有明确 Dark 才走深色)。
/// </summary>
internal static class Palette
{
    /// <summary>浅色色板资源 URI。</summary>
    internal const string LightUri = "avares://TipCL/Themes/Palette.Light.axaml";

    /// <summary>深色色板资源 URI。</summary>
    internal const string DarkUri = "avares://TipCL/Themes/Palette.Dark.axaml";

    /// <summary>内置图标字体的 Avalonia 字体族(assembly name 是 TipCL)。</summary>
    internal const string IconFontFamilyName = "TipCL Icons";

    /// <summary>图标字体资源 URI(avares://assembly/path#family,family 名取自 ttf 的 name 表)。</summary>
    internal const string IconFontUri = "avares://TipCL/Resources/fonts/TipCLIcons.ttf#TipCL Icons";

    /// <summary>图标字体。C1c 的 Ui.GlyphFont 直接复用这个实例。</summary>
    internal static readonly FontFamily IconFont = new FontFamily(IconFontUri);

    /// <summary>查不到键时的兜底色(中性灰,深浅主题都看得见)。</summary>
    internal static readonly Color FallbackColor = Avalonia.Media.Color.Parse("#808080");

    /// <summary>
    /// 色板全部键,顺序与 Windows 版 Themes\Palette.Light.xaml 的书写顺序完全一致
    /// (7 个 <c>C.*</c> 颜色 + 54 个刷子,共 61 个)。C1a 的用例会拿它跟两份 axaml
    /// 以及 Windows 源文件的键集合/键序逐项对拍。
    /// </summary>
    internal static readonly IReadOnlyList<string> Keys = new[]
    {
        "C.Accent",
        "C.AccentStrong",
        "C.AccentText",
        "C.AccentDeep",
        "C.AccentLite",
        "C.Canvas",
        "C.Paper",
        "B.Accent",
        "B.AccentStrong",
        "B.OnAccentStrong",
        "B.AccentText",
        "B.AccentTint",
        "B.AccentDeep",
        "B.AccentLite",
        "B.AccentSoft",
        "B.AccentSoft2",
        "B.AccentBorder",
        "B.OnAccent",
        "B.Canvas",
        "B.Paper",
        "B.Paper2",
        "B.Side",
        "B.Chrome",
        "B.Line",
        "B.LineSoft",
        "B.Hover",
        "B.Ink",
        "B.InkSoft",
        "B.InkMuted",
        "B.InkFaint",
        "B.Danger",
        "B.DangerSoft",
        "B.Warn",
        "B.WarnSoft",
        "B.Info",
        "B.InfoSoft",
        "B.Ok",
        "B.OnOk",
        "B.Scroll",
        "B.ScrollHover",
        "B.Mask",
        "B.Glass",
        "B.GlassStrong",
        "B.GlassBorder",
        "B.BannerFill",
        "B.OnBanner",
        "B.LogoFill",
        "B.OnLogo",
        "B.Card",
        "B.CardBorder",
        "B.TitleBar",
        "B.TitleBarLine",
        "B.TitleSep",
        "B.CaptionInk",
        "B.CaptionHot",
        "B.OnCaptionHot",
        "B.SegRail",
        "B.SegOn",
        "B.RowLine",
        "B.RowHover",
        "B.PageWash",
    };

    private static readonly object Gate = new object();

    private static Application? _app;

    // 直接加载好的两份字典(兜底解析路径用;平时走 Application 的资源树)。
    private static readonly Dictionary<ThemeVariant, IResourceProvider> Direct = new Dictionary<ThemeVariant, IResourceProvider>();

    /// <summary>把两份色板挂进应用资源(ThemeDictionaries → Light / Dark)。重复调用只挂一次。</summary>
    internal static void Install(Application app)
    {
        if (app is null)
        {
            return;
        }

        lock (Gate)
        {
            _app = app;
        }

        var palettes = new ResourceDictionary();
        palettes.ThemeDictionaries[ThemeVariant.Light] = Include(LightUri);
        palettes.ThemeDictionaries[ThemeVariant.Dark] = Include(DarkUri);

        if (!app.Resources.MergedDictionaries.Contains(palettes))
        {
            app.Resources.MergedDictionaries.Add(palettes);
        }
    }

    /// <summary>把主题变体归一化:只有明确 Dark 才当深色,其余(含 Default)都按浅色。</summary>
    internal static ThemeVariant Normalize(ThemeVariant? variant)
    {
        ThemeVariant value = variant ?? Current;
        return value == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    /// <summary>当前主题变体(没有 Application 时按浅色)。</summary>
    internal static ThemeVariant Current
    {
        get
        {
            Application? app = _app ?? Application.Current;
            return app?.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    /// <summary>按变体取一个色板键;取不到返回 false(不抛异常)。</summary>
    internal static bool TryGet(string key, ThemeVariant variant, out object? value)
    {
        value = null;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        ThemeVariant normalized = Normalize(variant);
        Application? app = _app ?? Application.Current;
        if (app is not null && app.TryFindResource(key, normalized, out object? found) && found is not null)
        {
            value = found;
            return true;
        }

        IResourceProvider provider = Load(normalized);
        return provider.TryGetResource(key, normalized, out value) && value is not null;
    }

    /// <summary>按变体取颜色。取不到或取到的不是颜色时返回兜底灰。</summary>
    internal static Color Color(string key, ThemeVariant? variant = null)
    {
        if (TryGet(key, Normalize(variant), out object? value))
        {
            switch (value)
            {
                case Color color:
                    return color;
                case ISolidColorBrush solid:
                    return solid.Color;
            }
        }

        return FallbackColor;
    }

    /// <summary>按变体取刷子。取不到时返回兜底灰刷子(不抛异常,不返回 null)。</summary>
    internal static IBrush Brush(string key, ThemeVariant? variant = null)
    {
        if (TryGet(key, Normalize(variant), out object? value))
        {
            switch (value)
            {
                case IBrush brush:
                    return brush;
                case Color color:
                    return new SolidColorBrush(color);
            }
        }

        return new SolidColorBrush(FallbackColor);
    }

    /// <summary>加载一份色板字典(带缓存)。加载失败时返回空字典 —— 由调用点走兜底色。</summary>
    private static IResourceProvider Load(ThemeVariant variant)
    {
        lock (Gate)
        {
            if (Direct.TryGetValue(variant, out IResourceProvider? cached))
            {
                return cached;
            }

            IResourceProvider provider = LoadUri(variant == ThemeVariant.Dark ? DarkUri : LightUri);
            Direct[variant] = provider;
            return provider;
        }
    }

    private static IResourceProvider LoadUri(string uri)
    {
        try
        {
            object? loaded = AvaloniaXamlLoader.Load(new Uri(uri));
            if (loaded is IResourceProvider provider)
            {
                return provider;
            }

            if (loaded is ResourceDictionary dictionary)
            {
                return dictionary;
            }
        }
        catch (Exception)
        {
            // 资源没编进去或者 XAML 坏了 —— 交给兜底色,不让窗口起不来。
        }

        return new ResourceDictionary();
    }

    /// <summary>包一层 <see cref="ResourceInclude"/>(ThemeDictionaries 要的是 IThemeVariantProvider)。</summary>
    private static ResourceInclude Include(string uri)
    {
        return new ResourceInclude(new Uri(uri)) { Source = new Uri(uri) };
    }
}
