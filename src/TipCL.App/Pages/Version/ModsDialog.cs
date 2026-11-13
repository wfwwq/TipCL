// 来源: Windows 版参考仓库 src/TipCL/Pages/ModsDialog.cs(M4-H P2 移植)。
// 模组管理:目标目录(实例共享 / 各隔离版本的独立 mods)切换、启停、删除、导入 jar、
// 检查更新(勾选后逐个更新)、打开目录、跳全局模组。
// 差异:Windows 的 Dlg.Busy 忙碌遮罩在 Linux 侧没有对应件,拉列表期间改为禁用按钮 + 状态文字。
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

/// <summary>模组管理对话框(照 Windows 版 ModsDialog)。</summary>
public static class ModsDialog
{
    public static async Task ShowAsync(string instance, string version = "")
    {
        var api = InProcessApi.Instance;
        var list = UiClass.V(6);
        var count = UiClass.Muted("");
        var target = UiClass.Combo(null, null, 230);
        var targets = new List<ModsTarget>();
        bool syncTarget = false;
        string status = "";

        var importBtn = U.Btn("导入 jar", BtnKind.Soft, null, "Import");
        importBtn.Click += (_, _) => _ = ImportAsync();
        var updateBtn = U.Btn("检查更新", BtnKind.Chip, null, "Refresh");
        updateBtn.Click += (_, _) => _ = Run(async () =>
        {
            await CheckUpdatesAsync(instance);
            await Reload();
        }, "检查更新失败");
        var openBtn = U.Btn("打开目录", BtnKind.Chip, null, "Folder");
        openBtn.Click += (_, _) => _ = Run(async () =>
        {
            string dir = await api.CallAsync<string>("open_mods_folder", new { instance, version = CurrentVersion() }) ?? "";
            ToastHost.Show("已打开", dir, ToastKind.Success);
        });
        var globalBtn = U.Btn("全局模组", BtnKind.Chip, null, "Box");
        globalBtn.Click += (_, _) => _ = GlobalModsDialog.ShowAsync();
        var refreshBtn = U.Btn("刷新", BtnKind.Chip, null, "Refresh");
        refreshBtn.Click += (_, _) => _ = Run(Reload, "读取失败");

        target.SelectionChanged += (_, _) => { if (!syncTarget) _ = Run(Reload, "读取失败"); };

        var bar = UiClass.G(null, "Auto,Auto,*");
        bar.Add(UiClass.Txt("目标", 12, fg: "B.InkMuted").VCenter(), 0, 0);
        target.Mk(8, 0, 12, 0).VCenter();
        bar.Add(target, 0, 1);
        bar.Add(count.VCenter(), 0, 2);

        var body = UiClass.V(12, bar,
            UiClass.H(8, importBtn, updateBtn, openBtn, globalBtn, refreshBtn),
            new ScrollViewer { Content = list, MaxHeight = 300, MinWidth = 560, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        api.BridgeEvent += OnEvent;
        await ReloadTargets();
        await Dlg.Panel($"模组管理 · {instance}", body, 800);
        api.BridgeEvent -= OnEvent;

        string CurrentVersion()
        {
            int i = target.SelectedIndex;
            return i < 0 || i >= targets.Count ? version : targets[i].Value;
        }

        void OnEvent(ApiEvent ev)
        {
            if (ev.Event == "finished" && ev.Success) _ = Run(Reload, "读取失败");
        }

        async Task Reload()
        {
            string ver = CurrentVersion();
            list.Children.Clear();
            var rows = await api.TryCallAsync<List<object?>>("get_installed_mod_entries", null,
                new { instance, version = ver }) ?? new();
            count.Text = $"共 {rows.Count} 个模组" + (status.Length > 0 ? " · " + status : "");
            if (rows.Count == 0)
                list.Children.Add(UiClass.Empty("Puzzle", "还没有模组", "到「下载 → Mod」搜索安装,或直接导入 jar"));
            foreach (var item in rows)
            {
                if (item is not PyDict d) continue;
                string file = Py.S(d.Get("filename"));
                var sw = UiClass.Switch(Py.Truthy(d.Get("enabled", true)));
                sw.IsCheckedChanged += (_, _) => _ = Run(async () =>
                {
                    bool on = sw.IsChecked == true;
                    await api.TryCallAsync<object>(on ? "enable_mod" : "disable_mod", null,
                        new { instance, filename = file, version = ver });
                    await Reload();
                });
                var del = UiClass.Btn("删除", BtnKind.Ghost, null);
                del.Click += (_, _) => _ = Run(async () =>
                {
                    if (await Dlg.Confirm("删除模组", file, "删除", "取消", danger: true))
                    {
                        await api.CallAsync<object>("delete_mod", new { instance, filename = file, version = ver });
                        await Reload();
                    }
                }, "删除失败");

                var text = UiClass.V(1,
                    UiClass.Txt(Py.S(d.Get("name")) is { Length: > 0 } n ? n : file).Trim(),
                    UiClass.Small(file + " · " + GlobalModsDialog.SizeText(Py.Int(d.Get("bytes")))));
                var grid = UiClass.G(null, "Auto,*,Auto");
                sw.Mk(0, 0, 12, 0).VCenter();
                grid.Add(sw, 0, 0);
                text.VCenter();
                grid.Add(text, 0, 1);
                del.VCenter();
                grid.Add(del, 0, 2);
                list.Children.Add(UiClass.RowCard(grid, null, 10));
            }
        }

        async Task ReloadTargets()
        {
            targets = await api.TryCallAsync<List<ModsTarget>>("get_mods_targets", null, new { instance }) ?? new();
            if (targets.Count == 0) targets.Add(new ModsTarget { Label = "实例共享 mods 目录", Value = "" });
            syncTarget = true;
            int sel = Math.Max(0, targets.FindIndex(t => t.Value == version));
            target.Items.Clear();
            foreach (var t in targets) target.Items.Add(t.Label);
            target.SelectedIndex = sel;
            syncTarget = false;
            await Reload();
        }

        async Task ImportAsync()
        {
            var batch = await ImportJarsFromPickerAsync(api, instance);
            if (batch.Canceled) return;
            if (batch.Total == 1)
            {
                if (batch.AnyFailed)
                {
                    // 单个文件失败:与改动前一致 —— 只提示,不改状态、不重载(原先异常直接冒到外层 Run)
                    ToastHost.Show("导入失败", batch.Failures[0].Reason, ToastKind.Warning);
                    return;
                }
                status = "已加入队列";
                ToastHost.Show("已加入队列", batch.Ok[0], ToastKind.Success);
            }
            else
            {
                status = batch.AnyFailed
                    ? $"已加入队列 {batch.OkCount} 个(失败 {batch.FailedCount} 个)"
                    : $"已加入队列 {batch.OkCount} 个";
                ToastHost.Show(batch.AnyFailed ? "导入失败" : "已加入队列", batch.SummaryText(),
                    batch.AnyFailed ? ToastKind.Warning : ToastKind.Success);
            }
            await Reload();
        }
    }

    /// <summary>
    /// 「导入 jar」入口:多选后逐个排队(对齐 Windows `ModsDialog` 的 foreach:一个 jar 一个
    /// install_mod 任务)。取消(空数组)什么都不做、也不报错 —— 返回的 batch 里 Canceled 为真。
    /// </summary>
    public static async Task<ImportBatch> ImportJarsFromPickerAsync(IApi api, string instance)
    {
        var files = await Dlg.PickFiles("导入模组", "*.jar");
        return await ImportJarsAsync(api, instance, files);
    }

    /// <summary>
    /// 逐个导入 jar(测试直接喂真实临时文件)。只做安装、不碰界面:文案/状态/重载归调用方,
    /// 单个文件时的提示沿用改动前的逐字行为。
    /// </summary>
    public static async Task<ImportBatch> ImportJarsAsync(IApi api, string instance, IReadOnlyList<string> files)
    {
        var batch = new ImportBatch(files.Count);
        foreach (string path in files)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string name = Path.GetFileName(path);
            try
            {
                await api.CallAsync<string>("install_mod", new
                {
                    name,
                    instance,
                    extra = new { path, instance, name },
                });
                batch.Success(name);
            }
            catch (Exception ex)
            {
                batch.Failure(name, ex.Message);
            }
        }
        return batch;
    }

    /// <summary>比对实例模组的可用更新,勾选后逐个应用(照 Windows CheckUpdatesAsync)。</summary>
    public static async Task CheckUpdatesAsync(string instance)
    {
        var api = InProcessApi.Instance;
        var rows = await api.TryCallAsync<List<object?>>("check_mod_updates", null, new { instance }) ?? new();
        if (rows.Count == 0)
        {
            ToastHost.Show("都是最新的", "没有找到可更新的模组", ToastKind.Success);
            return;
        }
        var picks = new List<(CheckBox Box, PyDict Row)>();
        var host = UiClass.V(6);
        foreach (var item in rows)
        {
            if (item is not PyDict d) continue;
            var box = UiClass.Check("", true);
            string name = Py.S(d.Get("name")) is { Length: > 0 } n ? n : Py.S(d.Get("filename"));
            var grid = UiClass.G(null, "Auto,*,Auto");
            box.Mk(0, 0, 10, 0).VCenter();
            grid.Add(box, 0, 0);
            var col = UiClass.V(1,
                UiClass.Txt(name, 13, bold: true).Trim(),
                UiClass.Small($"{Py.S(d.Get("current"))} → {Py.S(d.Get("latest"))}  ·  {Py.S(d.Get("source"))}  ·  {GlobalModsDialog.SizeText(Py.Int(d.Get("size")))}"));
            col.VCenter();
            grid.Add(col, 0, 1);
            grid.Add(UiClass.Tag(Py.S(d.Get("mc_version")) is { Length: > 0 } mc ? mc : "—").VCenter(), 0, 2);
            host.Children.Add(UiClass.RowCard(grid, null, 10));
            picks.Add((box, d));
        }
        var scroll = new ScrollViewer { Content = host, MaxHeight = 240, MinWidth = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (!await Dlg.Ask($"{rows.Count} 个模组有更新",
                UiClass.V(10, UiClass.Muted("勾上要更新的,旧文件会在新包校验通过后才删除。"), scroll), "更新所选"))
            return;
        var chosen = picks.Where(p => p.Box.IsChecked == true).Select(p => p.Row).ToList();
        if (chosen.Count == 0) return;
        int done = 0;
        var failed = new List<string>();
        foreach (var row in chosen)
        {
            try
            {
                await api.CallAsync<string>("apply_mod_update", new { instance, row });
                done++;
            }
            catch (Exception ex)
            {
                failed.Add($"{Py.S(row.Get("filename"))}:{ex.Message}");
            }
        }
        if (failed.Count == 0) ToastHost.Show("已更新", $"{done} 个模组", ToastKind.Success);
        else await Dlg.Alert("部分模组没能更新", $"成功 {done} 个,失败 {failed.Count} 个:\n\n" + string.Join("\n", failed));
    }

    private static async Task Run(Func<Task> work, string? errTitle = null)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            if (errTitle != null) ToastHost.Show(errTitle, ex.Message, ToastKind.Warning);
            else AppLog.Warn("模组管理失败: " + ex.Message);
        }
    }
}
