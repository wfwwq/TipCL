// 来源: Windows 版参考仓库 src/TipCL/Pages/SavesDialog.cs(M4-H P2 移植)。
// 存档管理:存档行(备份 / 导出 / 打开 / 装数据包 / 删除)+ 备份列表(还原 / 删除)+
// 截图 / 崩溃报告 / 日志(打开 / 导出)。
// 差异(见 DECISIONS D-19):Windows 侧存档行按 SaveRow POCO 取 Size/LastPlayed/Mode,
// 而 Core 实际返回的是 {name,path,icon,bytes,mtime} → 那三列在 Windows 上是空的;
// Linux 侧按实际键格式化(字节数 + 修改时间),不假装有「上次游玩 / 模式」。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.App.Common;
using TipCL.App.Pages.Download;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Version;

/// <summary>存档管理对话框(照 Windows 版 SavesDialog)。</summary>
public static class SavesDialog
{
    private static readonly string[] MediaLabels = { "截图", "崩溃报告", "日志" };

    public static async Task ShowAsync(string instance, string version)
    {
        var api = InProcessApi.Instance;
        var list = UiClass.V(8);
        var backups = UiClass.V(6);
        var media = UiClass.V(6);
        var kind = UiClass.Combo(MediaLabels, "截图", 130);
        var mediaCount = UiClass.Small("");

        async Task ReloadAsync()
        {
            list.Children.Clear();
            backups.Children.Clear();
            var saves = await api.TryCallAsync<List<object?>>("list_saves", null, new { instance, version }) ?? new();
            if (saves.Count == 0)
                list.Children.Add(UiClass.Empty("World", "这个版本还没有存档", "进游戏创建世界后会出现在这里"));
            foreach (var item in saves)
            {
                if (item is not PyDict d) continue;
                string name = Py.S(d.Get("name"));
                var col = UiClass.V(2,
                    UiClass.Txt(name, 13, bold: true).Trim(),
                    UiClass.Small(JoinMeta(GlobalModsDialog.SizeText(Py.Int(d.Get("bytes"))), TimeText(Py.Int(d.Get("mtime"))))));

                var acts = UiClass.H(6);
                acts.Children.Add(Chip("备份", async () =>
                {
                    await api.CallAsync<string>("backup_save", new { instance, name, version });
                    ToastHost.Show("已开始备份", name);
                    await ReloadAsync();
                }, "备份失败"));
                acts.Children.Add(Chip("导出", async () =>
                {
                    string? dest = await Dlg.SaveFile("导出存档", name + ".zip");
                    if (dest == null) return;
                    await api.CallAsync<string>("export_save", new { instance, name, dest, version });
                    ToastHost.Show("已导出", dest, ToastKind.Success);
                }, "导出失败"));
                acts.Children.Add(Chip("打开", async () => { await api.CallAsync<string>("open_save", new { instance, name, version }); }));
                acts.Children.Add(Chip("装数据包", () => InstallDatapackAsync(instance, version, name), "安装失败"));
                var del = UiClass.Btn("删除", BtnKind.Danger, null);
                del.Click += (_, _) => _ = Run(async () =>
                {
                    if (await Dlg.Confirm("删除存档", $"「{name}」会被永久删除,其中的建筑与进度都无法恢复。", "永久删除", "取消", danger: true))
                    {
                        await api.CallAsync<object>("delete_save", new { instance, name, version });
                        await ReloadAsync();
                    }
                }, "删除失败");
                acts.Children.Add(del);

                var grid = UiClass.G(null, "*,Auto");
                col.VCenter();
                grid.Add(col, 0, 0);
                acts.VCenter();
                grid.Add(acts, 0, 1);
                list.Children.Add(UiClass.RowCard(grid));
            }

            var backRows = await api.TryCallAsync<List<object?>>("list_save_backups", null, new { instance, version }) ?? new();
            backups.Children.Add(UiClass.Txt($"备份({backRows.Count})", 12.5, bold: true));
            if (backRows.Count == 0) backups.Children.Add(UiClass.Muted("还没有备份"));
            foreach (var item in backRows.Take(20))
            {
                if (item is not PyDict d) continue;
                string bn = Py.S(d.Get("name"));
                var col = UiClass.V(1,
                    UiClass.Txt(bn, 12).Trim(),
                    UiClass.Small(JoinMeta(GlobalModsDialog.SizeText(Py.Int(d.Get("bytes"))), TimeText(Py.Int(d.Get("mtime"))))));
                var acts = UiClass.H(6);
                acts.Children.Add(Chip("还原", async () =>
                {
                    if (await Dlg.Confirm("还原备份", $"用「{bn}」覆盖现有存档?", "还原"))
                    {
                        await api.CallAsync<PyDict>("restore_save_backup", new { instance, backup_name = bn, version, overwrite = true });
                        ToastHost.Show("已还原", bn, ToastKind.Success);
                        await ReloadAsync();
                    }
                }, "还原失败"));
                acts.Children.Add(Chip("删除", async () =>
                {
                    await api.CallAsync<object>("delete_save_backup", new { instance, backup_name = bn, version });
                    await ReloadAsync();
                }, "删除失败"));
                var grid = UiClass.G(null, "*,Auto");
                col.VCenter();
                grid.Add(col, 0, 0);
                acts.VCenter();
                grid.Add(acts, 0, 1);
                backups.Children.Add(grid);
            }
        }

        async Task ReloadMediaAsync()
        {
            media.Children.Clear();
            string label = kind.SelectedItem as string ?? "截图";
            string key = label == "崩溃报告" ? "crash-reports" : label == "日志" ? "logs" : "screenshots";
            var rows = await api.TryCallAsync<List<object?>>("list_media", null, new { instance, kind = key, version }) ?? new();
            mediaCount.Text = $"{rows.Count} 个文件";
            if (rows.Count == 0)
            {
                media.Children.Add(UiClass.Muted("这个目录还是空的"));
                return;
            }
            foreach (var item in rows.Take(60))
            {
                if (item is not PyDict d) continue;
                string path = Py.S(d.Get("path"));
                var col = UiClass.V(1,
                    UiClass.Txt(Py.S(d.Get("name")), 12.5).Trim(),
                    UiClass.Small(GlobalModsDialog.SizeText(Py.Int(d.Get("bytes")))));
                var acts = UiClass.H(6);
                acts.Children.Add(Chip("打开", async () => { await api.TryCallAsync<bool>("open_media", false, new { path }); }));
                acts.Children.Add(Chip("导出", async () =>
                {
                    string? dest = await Dlg.SaveFile("导出文件", Py.S(d.Get("name")));
                    if (dest == null) return;
                    try
                    {
                        File.Copy(path, dest, overwrite: true);
                        ToastHost.Show("已导出", dest, ToastKind.Success);
                    }
                    catch (Exception ex)
                    {
                        ToastHost.Show("导出失败", ex.Message, ToastKind.Error);
                    }
                }));
                var grid = UiClass.G(null, "*,Auto");
                col.VCenter();
                grid.Add(col, 0, 0);
                acts.VCenter();
                grid.Add(acts, 0, 1);
                media.Children.Add(grid);
            }
        }

        kind.SelectionChanged += (_, _) => _ = Run(ReloadMediaAsync, "读取失败");

        var kindBar = UiClass.G(null, "Auto,Auto,*");
        kindBar.Add(UiClass.Txt("截图 / 日志", 12.5, bold: true).VCenter(), 0, 0);
        kind.Mk(10, 0, 10, 0).VCenter();
        kindBar.Add(kind, 0, 1);
        kindBar.Add(mediaCount.VCenter(), 0, 2);

        var body = UiClass.V(12,
            Scroll(list, 200),
            UiClass.Sep(),
            UiClass.V(6, Scroll(backups, 150)),
            UiClass.Sep(),
            kindBar,
            Scroll(media, 170));

        await ReloadAsync();
        await ReloadMediaAsync();
        await Dlg.Panel($"存档管理 · {instance}" + (string.IsNullOrEmpty(version) ? "" : " / " + version), body, 780);

        static ScrollViewer Scroll(Control inner, double h) => new()
        {
            Content = inner,
            MaxHeight = h,
            MinWidth = 560,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Button Chip(string text, Func<Task> act, string? err = null)
        {
            var b = UiClass.Btn(text, BtnKind.Chip);
            b.Click += (_, _) => _ = Run(act, err);
            return b;
        }
    }

    /// <summary>把数据包装进某个存档的 datapacks(照 Windows InstallDatapackAsync)。</summary>
    private static async Task InstallDatapackAsync(string instance, string version, string save)
    {
        var api = InProcessApi.Instance;
        var files = await api.TryCallAsync<List<string>>("get_installed_datapacks", null, new { instance }) ?? new();
        if (files.Count == 0)
        {
            await Dlg.Alert("没有数据包", "先到「下载 → 数据包」装一个,再回来装进存档。");
            return;
        }
        var pick = UiClass.Combo(files, files[0], 320);
        if (!await Dlg.Ask($"装进存档 · {save}", UiClass.V(8, UiClass.Muted("选择要装进这个世界的数据包"), pick), "安装")) return;
        string file = pick.SelectedItem as string ?? files[0];
        string dest = await api.CallAsync<string>("install_datapack_into_save", new
        {
            instance,
            filename = file,
            save_name = save,
            version,
        }) ?? "";
        ToastHost.Show("已安装", dest.Length > 0 ? dest : file, ToastKind.Success);
    }

    /// <summary>装进哪个存档目录(隔离版本各有自己的 saves;照 Windows PickSaveTargetAsync)。</summary>
    public static async Task<string?> PickSaveTargetAsync(string instance, string title)
    {
        var api = InProcessApi.Instance;
        var rows = await api.TryCallAsync<List<ModsTarget>>("get_saves_targets", null, new { instance }) ?? new();
        if (rows.Count <= 1) return rows.Count == 1 ? rows[0].Value : "";
        var pick = UiClass.Combo(rows.Select(r => r.Label), rows[0].Label, 320);
        if (!await Dlg.Ask(title, UiClass.V(8, UiClass.Muted("装进哪个版本的存档目录?"), pick), "继续")) return null;
        int i = Math.Max(0, pick.SelectedIndex);
        return i < rows.Count ? rows[i].Value : "";
    }

    private static string TimeText(long unixSeconds)
    {
        if (unixSeconds <= 0) return "";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string JoinMeta(params string[] parts)
        => string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static async Task Run(Func<Task> work, string? errTitle = null)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            if (errTitle != null) ToastHost.Show(errTitle, ex.Message, ToastKind.Warning);
            else AppLog.Warn("存档管理失败: " + ex.Message);
        }
    }
}
