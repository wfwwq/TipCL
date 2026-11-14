// M2-B4：Core.Layout（PageLayoutIO）的 App 侧接线。
// 文件对话框 / Toast / 模态框一律转发 B0 的 Common 门面（Dlg / ToastHost），
// 这里只做 Core.Layout 委托与 Common 服务的黏合与 ToastKind 映射。
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.Core.Layout;

namespace TipCL.App.Pages.Layout;

public static class LayoutFx
{
    private static bool _wired;

    /// <summary>在应用启动早期调用一次（App.OnFrameworkInitializationCompleted）。</summary>
    public static void Wire()
    {
        if (_wired) return;
        _wired = true;
        PageLayoutIO.SaveFileDialog = (filter, suggestedName) => Dlg.SaveFile("导出布局", suggestedName).GetAwaiter().GetResult();
        PageLayoutIO.PickFileDialog = filter => Dlg.PickFile("导入布局", "*.json").GetAwaiter().GetResult();
        PageLayoutIO.Toast = (title, detail, kind) => Dispatcher.UIThread.Post(() => ToastHost.Show(title, detail, ToCommon(kind)));
        // 数据目录定位：对齐 Core 侧 PluginDirs 的默认根（Utils.ROOT，可用 TIPCL_HOME 覆盖）
        PageLayoutStore.RootProvider = () => TipCL.Core.Launcher.Utils.ROOT;
    }

    private static Common.ToastKind ToCommon(TipCL.Core.Layout.ToastKind kind) => kind switch
    {
        TipCL.Core.Layout.ToastKind.Success => Common.ToastKind.Success,
        TipCL.Core.Layout.ToastKind.Error => Common.ToastKind.Error,
        TipCL.Core.Layout.ToastKind.Warning => Common.ToastKind.Warning,
        _ => Common.ToastKind.Info,
    };

    /// <summary>Core.Layout 语义的 Toast（BlockHost 等直接调）。</summary>
    public static void ShowToast(string title, string detail, TipCL.Core.Layout.ToastKind kind)
    {
        Dispatcher.UIThread.Post(() => ToastHost.Show(title, detail, ToCommon(kind)));
    }

    /// <summary>跨页导航：取主窗口实例调 Navigate（外壳接 PageBase 生命周期前先这样）。</summary>
    public static void Navigate(string pageId)
    {
        if (MainWindow is Shell.MainWindow mw)
        {
            mw.Navigate(pageId);
        }
    }

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
