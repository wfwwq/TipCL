// AI2-integration:App 里所有「打开外部链接 / 打开文件夹」的统一入口。
//
// 以前 AccountPage / MdView / MultiplayerPage / SettingsPage.Cards / PluginHostPage / PluginsPage /
// PluginModels / DangerousWidgets / LibraryPage 各写一份 Process.Start,测试进程(ClickSweep 点遍按钮)
// 只能靠跳过词表躲开它们,漏一个就会真弹浏览器 / 资源管理器。现在都走这里:
//   * Hook 不为空 → 只交给 Hook(测试用来收集 / 吞掉);
//   * 否则若 Core 的 Crash.ShellOpenHook 不为空(测试隔离 TestNetworkIsolation 已装空操作)→ 同样不真打开;
//   * 都为空才按平台调系统命令。
// Core 侧已有两处统一入口(Crash.ShellOpen 打开文件夹、Auth.OpenBrowser 登录页),这里与它们共用同一个测试钩子。
using System;
using System.Diagnostics;
using System.IO;
using TipCL.Core.Launcher;

namespace TipCL.App.Common;

public static class ShellOpen
{
    /// <summary>测试替换:不为空时所有打开请求只交给它,不起任何进程。</summary>
    public static Action<string>? Hook { get; set; }

    private static Action<string>? ActiveHook => Hook ?? Crash.ShellOpenHook;

    /// <summary>用系统浏览器打开 http(s) 链接。打不开时抛异常,由调用方决定是否提示。</summary>
    public static void Url(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        if (ActiveHook is { } hook)
        {
            hook(url);
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        else
        {
            Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { url })?.Dispose();
        }
    }

    /// <summary>同 <see cref="Url"/>,打不开只当没打开(用户可手动复制地址)。</summary>
    public static bool TryUrl(string url)
    {
        try
        {
            Url(url);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>用系统文件管理器打开目录。打不开时抛异常。</summary>
    public static void Folder(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        if (ActiveHook is { } hook)
        {
            hook(path);
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetFullPath(path) + "\"") { UseShellExecute = true })?.Dispose();
        }
        else
        {
            Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { path })?.Dispose();
        }
    }
}
