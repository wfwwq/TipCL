// AI-new-features:启动页「启动前 AI 预检」。
//
// 点启动 → (设置开关 ai_preflight_enabled 开着时,默认开)先跑 Core 的 AiPreflight.Check
// (底子是已有的 Preflight.CheckLaunch + Conflict.ScanConflicts,再补 Java 匹配 / 物理内存)。
//   · 没问题:直接启动,不打扰;
//   · 只有提醒:英雄区下方出提示条,照常启动;
//   · 有会导致失败的问题:这次先不启动,提示条给「交给 AI 一键修复」(打开 AI 页并把预检结果当开场带过去)
//     和「仍然启动」(跳过 AI 预检一次,仍走启动链自己的预检)。
// 共享的 LaunchPage.cs 只在 LaunchCoreAsync 里加了一行调用。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Pages.Ai;
using TipCL.App.Shell;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Launch;

public sealed partial class LaunchPage
{
    private Border? _preflightBar;
    private bool _skipPreflightOnce;

    /// <summary>最近一次预检结果(测试 / 「交给 AI」用)。</summary>
    internal PyDict? LastPreflight { get; private set; }

    /// <summary>测试注入点:替换真正的预检(不碰真实实例目录与 Java)。</summary>
    internal static Func<string, string, long, PyDict>? PreflightOverride;

    /// <summary>
    /// 启动前 AI 预检闸门。返回 false = 这次不启动(发现 error,提示条已经显示)。
    /// 预检本身出异常一律放行:它是辅助检查,不能因为它坏了就启动不了。
    /// </summary>
    internal async Task<bool> AiPreflightGateAsync(string instance, string version, long memoryMb)
    {
        if (_skipPreflightOnce)
        {
            _skipPreflightOnce = false;
            HidePreflightBar();
            return true;
        }
        if (!AiPreflight.IsEnabled())
        {
            return true;
        }
        PyDict report;
        try
        {
            Func<string, string, long, PyDict>? hook = PreflightOverride;
            report = hook != null
                ? hook(instance, version, memoryMb)
                : await Task.Run(() => AiPreflight.Check(new Instance(instance.Length > 0 ? instance : null!), version, memoryMb));
        }
        catch (Exception)
        {
            return true;
        }
        LastPreflight = report;
        long errors = Py.Int(Py.Or(report.Get("error_count"), 0L));
        long warns = Py.Int(Py.Or(report.Get("warn_count"), 0L));
        if (errors == 0 && warns == 0)
        {
            HidePreflightBar();
            return true;
        }
        ShowPreflightBar(report, errors > 0);
        return errors == 0;
    }

    private void HidePreflightBar()
    {
        if (_preflightBar != null)
        {
            _preflightBar.IsVisible = false;
        }
    }

    /// <summary>提示条:标题 + 前三条问题 + 「交给 AI 一键修复」/「仍然启动」/「关闭」。</summary>
    internal void ShowPreflightBar(PyDict report, bool blocking)
    {
        if (_hero == null)
        {
            return;
        }
        if (_preflightBar == null)
        {
            _preflightBar = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 620,
            };
            _hero.Children.Add(_preflightBar);
        }
        _preflightBar.Dyn(Border.BackgroundProperty, blocking ? "B.DangerSoft" : "B.WarnSoft");
        _preflightBar.Dyn(Border.BorderBrushProperty, blocking ? "B.Danger" : "B.Warn");

        string title = blocking
            ? I18nCore.Tr("启动前 AI 预检:发现会导致启动失败的问题,这次先不启动")
            : I18nCore.Tr("启动前 AI 预检:有几条提醒,游戏照常启动");
        var col = UiClass.V(4, UiClass.Txt(title, 13, bold: true, "B.Ink"));
        foreach (object? raw in Py.ListOr(report.Get("items")).Take(3))
        {
            PyDict it = Py.D(raw);
            string mark = Py.S(it.Get("level")) == "error" ? "✗ " : "! ";
            var line = UiClass.Txt(mark + Py.S(it.Get("title")) + " — " + AiUiText.Trim(Py.S(it.Get("detail")), 90), 12, false, "B.Ink");
            line.TextWrapping = TextWrapping.Wrap;
            col.Children.Add(line);
        }
        int more = Py.ListOr(report.Get("items")).Count - 3;
        if (more > 0)
        {
            col.Children.Add(UiClass.Txt(I18nCore.Tr("还有 {n} 条,交给 AI 一起看").Replace("{n}", more.ToString()), 12, false, "B.InkMuted"));
        }

        var fix = UiClass.Btn(I18nCore.Tr("交给 AI 一键修复"), BtnKind.Primary, (_, _) => HandToAi(report));
        var force = UiClass.Btn(I18nCore.Tr("仍然启动"), BtnKind.Soft, (_, _) =>
        {
            _skipPreflightOnce = true;
            _ = LaunchAsync();
        });
        force.IsVisible = blocking;
        var close = UiClass.Btn(I18nCore.Tr("关闭"), BtnKind.Ghost, (_, _) => HidePreflightBar());
        var actions = UiClass.H(8, fix, force, close);
        actions.Margin = new Thickness(0, 6, 0, 0);
        col.Children.Add(actions);
        _preflightBar.Child = col;
        _preflightBar.IsVisible = true;
    }

    /// <summary>「交给 AI 一键修复」:把预检结果排进 AI 页,切过去后自动发出。</summary>
    private void HandToAi(PyDict report)
    {
        AiPage.QueuePrompt(AiPreflight.BuildFixPrompt(report));
        HidePreflightBar();
        if (TopLevel.GetTopLevel(this) is MainWindow win)
        {
            win.Navigate("ai");
        }
        else
        {
            Toast(I18nCore.Tr("预检结果已交给 AI"), I18nCore.Tr("打开「AI 助手」页即可继续"), ToastKind.Info);
        }
    }
}
