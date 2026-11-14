// 来源: Windows 版参考仓库 src/TipCL/Pages/GlobalModsDialog.cs(M4-H P2 移植)。
// 全局模组目录:放进去的 jar 会被注入每个实例(与实例 mods 是两套目录)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Version;

/// <summary>全局模组对话框(照 Windows 版 GlobalModsDialog)。</summary>
public static class GlobalModsDialog
{
    public static async Task ShowAsync()
    {
        var api = InProcessApi.Instance;
        var list = UiClass.V(6);

        async Task Reload()
        {
            list.Children.Clear();
            var rows = await api.TryCallAsync<List<object?>>("list_global_mods", null) ?? new();
            if (rows.Count == 0)
            {
                list.Children.Add(UiClass.Empty("Box", "全局模组目录是空的", "放进去的模组会自动注入每个实例"));
                return;
            }
            foreach (var item in rows)
            {
                if (item is not PyDict d) continue;
                string file = Py.S(d.Get("filename"));
                var sw = UiClass.Switch(Py.Truthy(d.Get("enabled", true)));
                sw.IsCheckedChanged += (_, _) =>
                {
                    bool on = sw.IsChecked == true;
                    _ = api.TryCallAsync<object>("set_global_mod_enabled", null, new { filename = file, enabled = on });
                };
                var grid = UiClass.G(null, "Auto,*,Auto");
                sw.Mk(0, 0, 12, 0).VCenter();
                grid.Add(sw, 0, 0);
                grid.Add(UiClass.Txt(file, 12.5).Trim().VCenter(), 0, 1);
                grid.Add(UiClass.Small(SizeText(Py.Int(d.Get("bytes")))).VCenter(), 0, 2);
                list.Children.Add(UiClass.RowCard(grid, null, 9));
            }
        }

        var open = U.Btn("打开目录", BtnKind.Soft, (_, _) => _ = api.CallAsync<string>("open_global_mods"), "Folder");
        var refresh = U.Btn("刷新", BtnKind.Chip, (_, _) => _ = Reload(), "Refresh");
        var body = UiClass.V(12, UiClass.H(8, open, refresh),
            new ScrollViewer { Content = list, MaxHeight = 380, MinWidth = 460, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        await Reload();
        await Dlg.Panel("全局模组", body, 620);
    }

    internal static string SizeText(long bytes)
    {
        if (bytes >= 1048576) return (bytes / 1048576.0).ToString("0.#") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
        return bytes + " B";
    }
}
