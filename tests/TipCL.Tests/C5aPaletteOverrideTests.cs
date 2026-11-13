using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C5a(零):<see cref="PaletteOverride"/> 的自测 —— 色板可写覆盖层的**机制**证据。
///
/// 这个类只管一件事:把"覆盖层压得过后挂在 ThemeDictionaries 里的内置色板"以及
/// "<c>{DynamicResource}</c> 会当场重解析"这两条**实测**钉住(不是猜 Avalonia 的查找顺序)。
/// 设置页自己的行为在 <c>C5aSettingsTests</c> 里验。
///
/// 每条碰 Avalonia 的用例体都走 <c>AvaloniaSession.Run(...)</c>(D-79);全局静态由
/// <see cref="C5aScope"/> 快照还原(D-69),所以"改主题色"不会漏给别的用例。
/// </summary>
public class C5aPaletteOverrideTests
{
    /// <summary>
    /// 【机制探针】后合并的那一层优先,而且 <c>{DynamicResource}</c> 会**立刻**重解析:
    /// 覆盖前 = 内置色板的颜色,覆盖后 = 覆盖层的颜色,<c>Reset</c> 之后又回到内置色。
    /// </summary>
    [Fact]
    public void The_merged_layer_wins_over_the_built_in_palette_and_dynamic_resources_follow_it()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Color builtin = C1cExpected.ColourOf(Palette.Brush("B.AccentStrong"));

            Border probe = new Border { Width = 10.0, Height = 10.0 };
            probe.Dyn(Border.BackgroundProperty, "B.AccentStrong");
            using var ui = UiHost.Show(probe);
            C5aFiles.Settle(2);
            Assert.Equal(builtin, C1cExpected.ColourOf(probe.Background!));

            Color over = Color.Parse("#D64545");
            PaletteOverride.SetBrush("B.AccentStrong", new SolidColorBrush(over));
            C5aFiles.Settle(2);
            Assert.Equal(over, C1cExpected.ColourOf(probe.Background!));

            PaletteOverride.Reset();
            C5aFiles.Settle(2);
            Assert.Equal(builtin, C1cExpected.ColourOf(probe.Background!));
        });
    }

    /// <summary>【机制探针】全局读取口(<see cref="Palette.Brush"/> / <see cref="Palette.Color"/>)也看得见覆盖。</summary>
    [Fact]
    public void Global_readers_see_the_override()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Color over = Color.Parse("#2F72C4");
            PaletteOverride.SetColor("C.Accent", over);
            PaletteOverride.SetBrush("B.Accent", new SolidColorBrush(over));
            C5aFiles.Settle();
            Assert.Equal(over, Palette.Color("C.Accent"));
            Assert.Equal(over, C1cExpected.ColourOf(Palette.Brush("B.Accent")));
        });
    }

    /// <summary>
    /// 【机制探针】<see cref="PaletteOverride.BuiltinColor"/> 必须**绕开**覆盖层 ——
    /// 这正是 Windows 每次直读色板 axaml 的那一步(否则"把主题色恢复成默认绿"会读到被改过的值)。
    /// </summary>
    [Fact]
    public void Builtin_read_bypasses_the_override()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Color builtin = PaletteOverride.BuiltinColor("B.AccentStrong");
            Color over = Color.Parse("#7C5CFC");
            PaletteOverride.SetBrush("B.AccentStrong", new SolidColorBrush(over));
            C5aFiles.Settle();
            Assert.Equal(over, C1cExpected.ColourOf(Palette.Brush("B.AccentStrong")));
            Assert.Equal(builtin, PaletteOverride.BuiltinColor("B.AccentStrong"));
        });
    }

    /// <summary>覆盖层的生命周期:有条目才挂、清空就摘(应用资源的合并字典数要回到原样)。</summary>
    [Fact]
    public void The_layer_is_attached_only_while_it_has_entries()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            int before = Application.Current!.Resources.MergedDictionaries.Count;
            Assert.False(PaletteOverride.AttachedNow);
            Assert.Equal(0, PaletteOverride.CountNow);

            PaletteOverride.SetBrush("B.Paper", new SolidColorBrush(Color.Parse("#010203")));
            Assert.True(PaletteOverride.AttachedNow);
            Assert.Equal(1, PaletteOverride.CountNow);
            Assert.Equal(before + 1, Application.Current.Resources.MergedDictionaries.Count);

            PaletteOverride.Reset();
            Assert.False(PaletteOverride.AttachedNow);
            Assert.Equal(0, PaletteOverride.CountNow);
            Assert.Empty(PaletteOverride.KeysNow);
            Assert.Equal(before, Application.Current.Resources.MergedDictionaries.Count);
        });
    }

    /// <summary>覆盖是**按 key** 的:<c>Remove</c> 只撤那一条,别的还在;写 <c>null</c> 等于撤掉。</summary>
    [Fact]
    public void Overrides_are_per_key_and_null_removes()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Color a = Color.Parse("#D68A17");
            PaletteOverride.SetBrush("B.Accent", new SolidColorBrush(a));
            PaletteOverride.SetBrush("B.AccentSoft", new SolidColorBrush(a));
            Assert.Equal(new[] { "B.Accent", "B.AccentSoft" }, PaletteOverride.KeysNow.ToArray());

            PaletteOverride.Set("B.Accent", null);
            Assert.Equal(1, PaletteOverride.CountNow);
            Assert.Equal(a, C1cExpected.ColourOf(Palette.Brush("B.AccentSoft")));
            Assert.Equal(PaletteOverride.BuiltinColor("B.Accent"), C1cExpected.ColourOf(Palette.Brush("B.Accent")));
        });
    }

    /// <summary>空 key 是编程错误:立刻抛(不静默写一条谁也查不到的垃圾)。</summary>
    [Fact]
    public void An_empty_key_is_rejected()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Assert.Throws<ArgumentException>(() => PaletteOverride.Set("", Brushes.Red));
        });
    }

    /// <summary>
    /// 深色 / 浅色两张内置色板读的是**各自那本**(<c>C.Paper</c> 深浅不同),
    /// 而且 <see cref="PaletteOverride.IsDarkNow"/> 跟当前变体走。
    /// </summary>
    [Fact]
    public void The_built_in_reader_follows_the_theme_variant()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Border probe = new Border();
            using var ui = UiHost.Show(probe);
            C5aFiles.Settle(2);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            C5aFiles.Settle(2);
            Assert.False(PaletteOverride.IsDarkNow);
            Color lightPaper = PaletteOverride.BuiltinColor("C.Paper");
            Color lightCanvas = PaletteOverride.BuiltinColor("C.Canvas");

            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            C5aFiles.Settle(2);
            Assert.True(PaletteOverride.IsDarkNow);
            Assert.Equal(ThemeVariant.Dark, PaletteOverride.VariantNow);
            Color darkPaper = PaletteOverride.BuiltinColor("C.Paper");
            Color darkCanvas = PaletteOverride.BuiltinColor("C.Canvas");

            Assert.NotEqual(lightPaper, darkPaper);
            Assert.NotEqual(lightCanvas, darkCanvas);
        });
    }

    /// <summary>写入未知键也不抛(与 C1a 的口径一致:键名写错不该把窗口打崩),但它能被读到 —— 证明覆盖层是"透传"的。</summary>
    [Fact]
    public void An_unknown_key_is_accepted_and_readable()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            PaletteOverride.SetBrush("B.这不是一个内置键", new SolidColorBrush(Color.Parse("#149B9B")));
            Assert.True(PaletteOverride.TryGetNow("B.这不是一个内置键", out object? value));
            Assert.IsType<SolidColorBrush>(value);
        });
    }
}
