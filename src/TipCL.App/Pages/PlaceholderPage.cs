using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TipCL.App.Pages;

/// <summary>
/// M1 占位页基类：卡片里放页标题 + 提示文字，M2 换成真实页面。
/// </summary>
public class PlaceholderPage : UserControl
{
    public const string Hint = "该页面将在 M2 移植实现。";

    public string PageId { get; }

    public string PageTitle { get; }

    protected PlaceholderPage(string pageId, string pageTitle)
    {
        PageId = pageId;
        PageTitle = pageTitle;

        var title = new TextBlock
        {
            Text = pageTitle,
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 8),
            [!TextBlock.ForegroundProperty] = this.GetResourceObservable("Shell.Ink").ToBinding(),
        };
        var hint = new TextBlock
        {
            Text = Hint,
            FontSize = 13,
            [!TextBlock.ForegroundProperty] = this.GetResourceObservable("Shell.InkMuted").ToBinding(),
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(title);
        panel.Children.Add(hint);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(20),
            Padding = new Thickness(24, 20),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            Child = panel,
        };
        card[!Border.BackgroundProperty] = this.GetResourceObservable("Shell.CardBg").ToBinding();
        card[!Border.BorderBrushProperty] = this.GetResourceObservable("Shell.CardBorder").ToBinding();

        Content = card;
    }
}
