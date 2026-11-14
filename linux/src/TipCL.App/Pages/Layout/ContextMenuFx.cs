// M4-H P2:右键菜单门面(Windows 版 VersionManagePage / InstancePage 各自内联写了一遍,
// Linux 侧抽成一个共用件:菜单项顺序与分割线位置由调用方给)。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace TipCL.App.Pages.Layout;

/// <summary>一个菜单项:文案 + 点击后的异步动作。</summary>
public sealed class MenuItemFx
{
    public MenuItemFx(string head, Func<Task> act)
    {
        Head = head;
        Act = act;
    }

    public string Head { get; }

    public Func<Task> Act { get; }
}

/// <summary>右键菜单小门面。</summary>
public static class ContextMenuFx
{
    /// <summary>
    /// 在 anchor 上弹出菜单。<paramref name="separatorBefore"/> 是分割线插在第几项之前(-1 = 不插)。
    /// </summary>
    public static void Show(Control anchor, IReadOnlyList<MenuItemFx> items, int separatorBefore = -1)
    {
        var menu = new ContextMenu();
        for (int i = 0; i < items.Count; i++)
        {
            if (i == separatorBefore) menu.Items.Add(new Separator());
            var item = items[i];
            var mi = new MenuItem { Header = item.Head };
            mi.Click += (_, _) => _ = Invoke(item.Act);
            menu.Items.Add(mi);
        }
        anchor.ContextMenu = menu;
        menu.Open(anchor);
    }

    private static async Task Invoke(Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (Exception)
        {
            // 错误提示由各动作自己负责(Toast),这里只保证不把异常抛到事件处理器外
        }
    }
}
