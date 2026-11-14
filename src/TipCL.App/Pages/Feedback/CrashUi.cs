// 来源: Windows 版参考仓库 src/TipCL/Pages/CrashUi.cs(M4-H P4 移植)。
// 崩溃界面:标题 / 摘要 / 退出码 / 提示 / 详情日志 + 报告自带的修复动作 + 打开日志文件 /
// 导出报告 / 上传给开发者 / 附说明反馈 / 复制全文;返回 true = 用户点了「重新启动」。
// M4-H P5:「交给 AI 修复」已接上(Core 的 AiFixChat + Pages/Ai/AiFixView)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Feedback;

/// <summary>崩溃报告对话框(照 Windows 版 CrashUi)。report 为 Core 崩溃字典。</summary>
public static class CrashUi
{
    public static async Task<bool> ShowAsync(PyDict report, bool gameError = false)
    {
        var api = InProcessApi.Instance;
        string head = Py.S(Py.Or(report.Get("headline"), report.Get("title")));
        if (head.Length == 0) head = "游戏异常退出";

        var body = UiClass.V(12);
        body.MinWidth = 520;
        var title = UiClass.Txt(head, 15, bold: true);
        title.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        body.Children.Add(title);
        string summary = Py.S(report.Get("summary"));
        if (summary.Length > 0) body.Children.Add(UiClass.Muted(summary));
        if (report.Get("exit_code") is { } code && Py.Truthy(code))
        {
            string hint = Py.S(report.Get("exit_hint"));
            body.Children.Add(UiClass.Small($"退出码 {Py.Str(code)}" + (hint.Length > 0 ? " · " + hint : "")));
        }
        string help = Py.S(report.Get("help"));
        if (help.Length > 0) body.Children.Add(UiClass.Muted(help));
        string detail = Py.S(report.Get("detail"));
        if (detail.Length > 0)
        {
            var box = UiClass.LogBox();
            box.Text = detail;
            box.Height = 190;
            box.IsReadOnly = true;
            box.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            body.Children.Add(box);
        }

        var actions = new WrapPanel();
        body.Children.Add(actions);
        string taskId = Py.S(report.Get("task_id"));
        foreach (object item in Py.ListOr(report.Get("actions")))
        {
            if (item is not PyDict act) continue;
            var b = UiClass.Btn(Py.S(act.Get("label")), BtnKind.Soft);
            b.Margin = new Avalonia.Thickness(0, 0, 7, 7);
            b.Click += (_, _) => _ = RunAsync(async () =>
            {
                b.IsEnabled = false;
                try
                {
                    PyDict? res = await api.CallAsync<PyDict>("apply_crash_action", new { action = act, report });
                    bool ok = res != null && Py.Truthy(res.Get("ok"));
                    ToastHost.Show(ok ? "已处理" : "没能处理", res == null ? "" : Py.S(res.Get("message")),
                        ok ? ToastKind.Success : ToastKind.Warning);
                }
                finally
                {
                    b.IsEnabled = true;
                }
            }, "处理失败");
            actions.Children.Add(b);
        }
        if (gameError)
        {
            // M4-H P5:AI 子系统落地后接上「交给 AI 修复」(照 Windows 版 AiFixWindow 的入口)
            Extra("交给 AI 修复", "Lightning", () =>
            {
                var view = new Pages.Ai.AiFixView(api, report);
                var handle = Dlg.OpenPanel("交给 AI 修复", view, 720);
                void Forward(ApiEvent ev) => view.OnEvent(ev);
                api.BridgeEvent += Forward;
                handle.Closed.ContinueWith(_ => api.BridgeEvent -= Forward, TaskScheduler.Default);
                return Task.CompletedTask;
            });
        }

        Extra("打开日志文件", "Folder", async () =>
        {
            string path = await api.CallAsync<string>("open_crash_file", new
            {
                task_id = Py.S(report.Get("task_id")),
                path = Py.S(report.Get("direct_file")),
            }) ?? "";
            ToastHost.Show("已打开", path, ToastKind.Success);
        });
        Extra("导出报告", "Export", async () =>
        {
            string? dest = await Dlg.SaveFile("导出崩溃报告", "tipcl-crash.txt");
            if (dest == null) return;
            string path = await api.CallAsync<string>("export_crash_report", new { task_id = taskId, dest }) ?? dest;
            ToastHost.Show("已导出", path, ToastKind.Success);
        });
        Extra("上传给开发者", "Cloud", async () =>
        {
            string msg = await api.CallAsync<string>("submit_crash_report", new { task_id = taskId }) ?? "";
            ToastHost.Show("已上传", msg, ToastKind.Success);
        });
        Extra("附说明反馈", "Send", async () =>
        {
            var note = UiClass.Multi("崩溃前在做什么?装了什么模组?(可空)", "", 110);
            if (!await Dlg.Ask("反馈这次崩溃",
                    UiClass.V(8, UiClass.Muted("会把崩溃报告连同下面这段说明发给开发者,不含账号密码。"), note), "发送"))
                return;
            PyDict? res = await api.CallAsync<PyDict>("submit_crash_feedback", new
            {
                report,
                extra = note.Text?.Trim() ?? "",
            });
            bool ok = res != null && Py.Truthy(res.Get("ok"));
            ToastHost.Show(ok ? "已反馈" : "反馈失败", res == null ? "" : Py.S(res.Get("message")),
                ok ? ToastKind.Success : ToastKind.Error);
        });
        Extra("复制全文", "Copy", async () =>
        {
            var clipboard = (Avalonia.Application.Current?.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } w
                ? Avalonia.Controls.TopLevel.GetTopLevel(w)?.Clipboard
                : null;
            if (clipboard != null) await clipboard.SetTextAsync(head + "\n" + summary + "\n\n" + detail);
            ToastHost.Show("已复制");
        });

        int choice = await Dlg.Choose("启动出问题了", body, new[] { "关闭", "重新启动" }, 760);
        return choice == 1;

        void Extra(string text, string glyph, Func<Task> work)
        {
            var b = U.Btn(text, BtnKind.Chip, null, glyph);
            b.Margin = new Avalonia.Thickness(0, 0, 7, 7);
            b.Click += (_, _) => _ = RunAsync(work, "失败");
            actions.Children.Add(b);
        }
    }

    private static async Task RunAsync(Func<Task> work, string? errTitle = null)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            ToastHost.Show(errTitle ?? "操作失败", ex.Message, ToastKind.Warning);
        }
    }
}
