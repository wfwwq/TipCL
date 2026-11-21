// AI-new-features:AI 页的两处新入口 ——
//   1. 标题栏「导出诊断报告」:选目录 → Core 的 DiagnosticReport.Export(对话摘要 + 日志片段 + 实例/模组 + 硬件,已脱敏);
//   2. 启动页「交给 AI 一键修复」:LaunchPage 把预检结果写进 QueuePrompt,切到 AI 页时(OnActivate)自动发出。
// 共享的 AiPage.cs 只改了:class 加 partial、标题栏按钮列表加一个、OnActivate 加一行、执行器初始化加一行。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;

namespace TipCL.App.Pages.Ai;

public sealed partial class AiPage
{
    private static string _pendingPrompt = "";

    /// <summary>排一句「下次打开 AI 页就发出去」的话(启动页预检的「交给 AI 一键修复」用)。</summary>
    public static void QueuePrompt(string text) => Interlocked.Exchange(ref _pendingPrompt, text ?? "");

    /// <summary>当前排着的那句(测试用)。</summary>
    internal static string PendingPrompt => Volatile.Read(ref _pendingPrompt);

    /// <summary>
    /// 对话快照(给 export_diagnostic_report)。工具在后台线程调它:List.ToList 走数组拷贝,不会因并发追加而抛,
    /// 最多少拿最新一条,对报告无影响 —— 不用 Dispatcher.Invoke,免得无头测试里没人泵 UI 线程时卡死。
    /// </summary>
    private List<object> SnapshotHistory()
    {
        try
        {
            return _history.ToList();
        }
        catch (Exception)
        {
            return new List<object>();
        }
    }

    private Control ExportReportButton()
    {
        var btn = U.Btn(I18nCore.Tr("导出诊断报告"), BtnKind.Chip, (_, _) => _ = ExportReportAsync(), "Export");
        ToolTip.SetTip(btn, I18nCore.Tr("把这次排查的对话摘要、日志关键片段、模组清单和硬件摘要导出成 zip(账号令牌与用户名会打码)"));
        return btn;
    }

    /// <summary>用户自己点的导出:选目录本身就是确认,不再弹 AI 的确认卡。</summary>
    private async Task ExportReportAsync()
    {
        string? dir = await Dlg.PickFolder(I18nCore.Tr("选择诊断报告的保存位置"));
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }
        try
        {
            string name = _runner.Instance;
            var inst = new Instance(name.Length > 0 ? name : null!);
            var args = PyDict.Of("dest_dir", dir, "instance", inst.name);
            string path = DiagnosticReport.ResolveOutPath(args, inst.name);
            List<object> history = SnapshotHistory();
            PyDict result = await Task.Run(() => DiagnosticReport.Export(inst, history, path));
            ToastHost.Show(I18nCore.Tr("诊断报告已导出"), Py.S(result.Get("path")), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastHost.Show(I18nCore.Tr("导出失败"), Py.ExcStr(ex), ToastKind.Warning);
        }
    }

    /// <summary>有排着的话就放进输入框并发出(没配 AI 时只放进输入框,提示用户配好再发)。</summary>
    private void ConsumePendingPrompt()
    {
        string text = Interlocked.Exchange(ref _pendingPrompt, "");
        if (text.Length == 0)
        {
            return;
        }
        _input.Text = text;
        if (_run != null)
        {
            return;
        }
        if (Configured())
        {
            Send();
        }
        else
        {
            ToastHost.Show(I18nCore.Tr("预检结果已放进输入框"), I18nCore.Tr("配置好 AI 接入后点发送即可"), ToastKind.Info);
        }
    }
}
