using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace TipCL.App.Shell;

/// <summary>
/// 左侧 196px 分组导航：通用（仪表盘/账号/Java）、游戏（实例库/下载/任务）、系统（插件/设置/关于）。
/// 选中态白底胶囊粗体。
/// </summary>
public partial class SideNav : UserControl
{
    /// <summary>导航项被点击（参数为项 id）。</summary>
    public event EventHandler<string>? NavClicked;

    public SideNav()
    {
        InitializeComponent();
        BuildItems();
    }

    private void BuildItems()
    {
        foreach (var (group, items) in NavRegistry.SideGroups)
        {
            NavPanel.Children.Add(new TextBlock
            {
                Text = group,
                Classes = { "navGroup" },
            });
            foreach (var item in items)
            {
                NavPanel.Children.Add(MakeItem(item));
            }
        }
    }

    private Button MakeItem(NavItem item)
    {
        var button = new Button
        {
            Classes = { "navItem" },
            Tag = item.Id,
        };
        var icon = new Path
        {
            Data = Geometry.Parse(item.Icon),
            Width = 16,
            Height = 16,
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
            // 跟随按钮前景色（选中态变 Ink）
            [!Path.StrokeProperty] = button.GetObservable(Button.ForegroundProperty).ToBinding(),
        };
        var text = new TextBlock
        {
            Text = item.Title,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        panel.Children.Add(icon);
        panel.Children.Add(text);
        button.Content = panel;
        button.Click += (_, _) => NavClicked?.Invoke(this, item.Id);
        return button;
    }

    /// <summary>同步选中态。</summary>
    public void SetSelected(string? id)
    {
        foreach (var child in NavPanel.Children)
        {
            if (child is Button b)
                TitleBar.SetClass(b, "on", Equals(b.Tag, id));
        }
    }
}
