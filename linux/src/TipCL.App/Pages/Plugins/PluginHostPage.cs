// M4-H P6:插件页面宿主(kind "page")。
// 对齐 Windows Pages/Plugins/PluginHostPage.cs 的骨架:`plugin:<plugin>:<id>` 页面对象被外壳缓存,
// 插件被禁用/卸载后标成不可用(MarkGone)而不是留一个死页面。
//
// 与参考版的差别(见 docs/PARITY.md §7.1):参考版用 WPF WebView2 承载插件网页、用
// NativePluginUi 取 .NET 插件的原生 WPF 界面;Linux 侧两者都不做(没有 WebView 宿主、
// 参考契约基于 WPF),所以内容区如实说明 + 给「在浏览器打开」(地址由插件宿主提供)。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;

namespace TipCL.App.Pages.Plugins;

/// <summary>插件注册的页面。</summary>
public sealed class PluginHostPage : PageBase
{
    private UiContribution _c;
    private readonly Border _frame = new() { BorderThickness = new Thickness(1), ClipToBounds = true };
    private bool _gone;

    public PluginHostPage(UiContribution c) : base(c.PageId, c.Label)
    {
        _c = c;
        _frame.Theme = U.Theme("Card");
        Content = _frame;
        Build();
    }

    public UiContribution Contribution => _c;

    /// <summary>插件已经被禁用/卸载,本页只剩「不可用」提示。</summary>
    public bool IsGone => _gone;

    /// <summary>插件重载/界面列表刷新后:地址与标题没变就不重建。</summary>
    public void Update(UiContribution c)
    {
        bool wasGone = _gone;
        _gone = false;
        UiContribution old = _c;
        _c = c;
        if (wasGone || old.Url != c.Url || old.Label != c.Label) Build();
    }

    /// <summary>插件被禁用 / 卸载:显示提示,不假装页面还在。</summary>
    public void MarkGone()
    {
        if (_gone) return;
        _gone = true;
        var manage = U.Btn("管理插件", BtnKind.Soft, delegate
        {
            (TopLevel.GetTopLevel(this) as TipCL.App.Shell.MainWindow)?.Navigate("plugins");
        });
        var empty = U.Empty("◇", "这个插件页面已经不可用", "插件可能被禁用、卸载,或者它的文件已经被删掉。");
        empty.Children.Add(U.H(0, manage));
        _frame.Child = empty;
    }

    private void Build()
    {
        var body = U.V(12);
        body.Margin = new Thickness(22, 18, 22, 18);
        body.Children.Add(U.H(8,
            U.Txt(_c.Label.Length > 0 ? _c.Label : _c.Id, 16, bold: true),
            U.Tag(PluginUi.PluginName(_c.Plugin))));
        body.Children.Add(U.Small($"插件页面 · {_c.Plugin} / {_c.Id}"));

        var cardBody = U.V(8,
            U.Section("这个页面由插件提供"),
            U.Muted(Explain()).Wrap());
        if (HasUrl())
        {
            cardBody.Children.Add(U.H(0, U.Btn("在浏览器打开", BtnKind.Soft, delegate { OpenUrl(_c.Url); })));
        }
        body.Children.Add(U.Card(cardBody));
        _frame.Child = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private bool HasUrl()
        => _c.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || _c.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private string Explain()
    {
        if (_gone) return "插件不可用。";
        if (HasUrl())
        {
            return "插件把界面放在自己的服务里(参考版用 WPF WebView2 承载)。"
                 + "Linux 版不引入 CEF / WebKitGTK 这类网页宿主,所以这里不内嵌网页;"
                 + "可以按下面的按钮让插件在浏览器里打开(能否用取决于插件自己的实现)。";
        }
        return "插件没有给出可打开的网页地址。Linux 版没有插件运行时"
             + "(Node 插件缺宿主,.NET 插件的参考契约基于 WPF),这个页面在应用内没有内容可显示。"
             + "插件文件仍可安装、保留、卸载(见「插件 → 已安装」)。";
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ToastHost.Show("打不开链接", ex.Message, ToastKind.Warning);
        }
    }
}
