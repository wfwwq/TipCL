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
using TipCL.Core.Launcher;
using TipCL.Core.Layout;

namespace TipCL.App.Pages.Layout;

public static class LayoutFx
{
    private static bool _wired;

    /// <summary>
    /// 「导出布局」文件对话框标题(走词表:Core 侧 I18nCore)。
    /// <para>R32-I18NFIX:此前这里是裸字面量 <c>"导出布局"</c> —— 同一个串在工具条**按钮**上已走
    /// <c>I18nCore.Tr("导出布局")</c>(<c>LayoutEditor.cs</c>),只有对话框标题漏了 i18n,
    /// 英文界面下会出现中文标题。对位 Windows <c>Pages/PageLayoutIO.cs:25</c> 的 <c>I18n.L("导出布局")</c>。</para>
    /// <para>抽成方法只是为了能在无头测试里断言中英切换(与 mac 的 <c>LayoutShellHooks.BuildContextMenu</c> 同一做法)。</para>
    /// </summary>
    internal static string ExportDialogTitle() => I18nCore.Tr("导出布局");

    /// <summary>「导入布局」文件对话框标题(走词表);理由同 <see cref="ExportDialogTitle"/>。</summary>
    internal static string ImportDialogTitle() => I18nCore.Tr("导入布局");

    /// <summary>在应用启动早期调用一次(App.OnFrameworkInitializationCompleted)。</summary>
    public static void Wire()
    {
        if (_wired) return;
        _wired = true;
        PageLayoutIO.SaveFileDialog = (filter, suggestedName) => Dlg.SaveFile(ExportDialogTitle(), suggestedName).GetAwaiter().GetResult();
        PageLayoutIO.PickFileDialog = filter => Dlg.PickFile(ImportDialogTitle(), "*.json").GetAwaiter().GetResult();
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
