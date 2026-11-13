using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1c 的测试基座:把控件挂进**真窗口**并等样式/动态资源求值完。
///
/// 为什么必须挂窗口:类选择器(C1b 的样式表)与 <c>{DynamicResource}</c> 都只在控件进入
/// 可视树、连上资源宿主之后才求值 —— 只 new 一个控件读属性会读到默认值。
///
/// 纪律(与 C1b 的 Hosted 一致,但 C1b 那份是私有嵌套类,这里独立出来给两个 C1c 测试类共用):
/// - 一切 Avalonia 工作都经 <c>AvaloniaSession</c>(D-79),本类只在会话线程上被调用;
/// - 显式把主题变体钉成浅色(色板挂在 ThemeDictionaries 下,Default 变体解析不到东西),
///   <see cref="Dispose"/> 里还原全局静态(D-69 的纪律);
/// - 窗口用完就关,不留可视树。
/// </summary>
internal sealed class UiHost : IDisposable
{
    private readonly ThemeVariant? _previous;

    private UiHost(Window window, ThemeVariant? previous)
    {
        Window = window;
        _previous = previous;
    }

    internal Window Window { get; }

    internal static UiHost Show(Control content)
    {
        AvaloniaSession.Ensure();

        ThemeVariant? previous = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = ThemeVariant.Light;

        var window = new Window { Width = 640, Height = 480, Content = content };
        window.Show();
        Flush();
        return new UiHost(window, previous);
    }

    /// <summary>把排队的布局/样式/绑定工作跑完(headless 下要手动推一次渲染计时器)。</summary>
    internal static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        try
        {
            Window.Close();
        }
        catch (Exception)
        {
        }

        Application.Current!.RequestedThemeVariant = _previous;
        Dispatcher.UIThread.RunJobs();
    }
}

/// <summary>
/// C1c 期望值的**权威副本**(与 C1a 的 C1aFixtures 同一套做法):期望值来自
/// Windows 源 <c>Services\Ui.cs</c> 与 C1b 的 <c>Controls.axaml</c>,在测试里各留一份,
/// 这样"源码改了但期望没跟着改"会立刻红,而不是变成自证。
/// </summary>
internal static class C1cExpected
{
    /// <summary>前景色的兜底灰(C1a 的 Palette.FallbackColor 是同一个值)。</summary>
    internal static readonly Color Fallback = Color.Parse("#808080");

    /// <summary>取一个刷子的颜色(断言用;不是实色刷子直接失败)。</summary>
    internal static Color ColourOf(IBrush? brush)
    {
        Assert.NotNull(brush);
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    /// <summary>字号/字重名 → Avalonia 的 FontWeight。</summary>
    internal static FontWeight Weight(string name)
    {
        return name switch
        {
            "SemiBold" => FontWeight.SemiBold,
            "Bold" => FontWeight.Bold,
            "Normal" => FontWeight.Normal,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未收录的字重名"),
        };
    }
}
