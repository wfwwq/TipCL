// AI-acct-layout-mp:AI 页面在「工具做完 / 撤回一轮」之后要补的界面刷新(独立文件,AiPage 里只各留一行调用)。
//
// 为什么要它:Core 的 layout_apply 走 LayoutStoreV2.Save,会触发 LayoutStoreV2.LayoutChanged ——
// 页面区块(BlockHost)订阅了它,会即时重排;但**标题栏页签**不订阅这个静态事件(MainWindow 构造里写明了原因:
// 静态事件会把测试里大量不 Close 的窗口永久钉住),约定是「改完由调用方显式调 MainWindow.ApplyShellLayout()」——
// 设置页「布局」视图就是这么做的(SettingsPage.Layout.cs)。AI 页面是另一个改布局的入口,这里补上同一步。
using Avalonia.Controls;
using TipCL.App.Shell;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Ai;

internal static class AiAcctLayoutMpHooks
{
    /// <summary>工具调用完成后:layout_apply 成功执行过 → 重排标题栏页签(区块由 LayoutChanged 自己刷新)。</summary>
    public static void AfterTool(Control page, ToolCall? call)
    {
        if (call == null || call.status != ToolCallStatus.COMPLETED)
        {
            return;
        }
        if (call.name == LayoutTools.ApplyName)
        {
            RefreshShell(page);
        }
    }

    /// <summary>
    /// 撤回一轮之后:检查点可能把 ui_layout_v2.json 还原成了旧内容,但 LayoutStoreV2 的进程内缓存还是新的 ——
    /// 不处理的话界面不变,下一次保存还会把还原的文件再盖回去。这里清缓存重读;内容确实变了才落一次盘
    /// (顺带写通 v1 并触发 LayoutChanged,让区块重排),再重排标题栏。内容没变什么都不做。
    /// </summary>
    public static void AfterRewind(Control page)
    {
        try
        {
            string before = LayoutStoreV2.ExportJson(LayoutStoreV2.Load());
            LayoutStoreV2.ResetCache();
            LayoutV2 onDisk = LayoutStoreV2.Load();
            if (LayoutStoreV2.ExportJson(onDisk) == before)
            {
                return;
            }
            LayoutStoreV2.Save(onDisk);
            LayoutStoreV2.RefreshV1Cache();
            RefreshShell(page);
        }
        catch (System.Exception)
        {
            // 刷新失败不影响撤回本身(撤回已经成功;最坏情况是重启后才看到还原的布局)
        }
    }

    private static void RefreshShell(Control page)
    {
        (TopLevel.GetTopLevel(page) as MainWindow)?.ApplyShellLayout();
    }
}
