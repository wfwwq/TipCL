// AI2-i18n-ux(perf-saves 遗留):存档对话框的「还原备份」改走 Core 的 SaveBackups.RestoreWithSafetyBackup。
//
// 以前 SavesDialog 调 Api 的 restore_save_backup → Saves.RestoreBackup 直接覆盖,回滚前那份存档就没了;
// AI 工具 restore_save_backup 走的是「先把当前同名存档打一份 zip,再回滚」(备份失败就不回滚)。
// 现在界面与 AI 行为一致。游戏运行中存档还在被写入:此时禁用还原按钮并提示(与 AI 工具「游戏运行中拒绝」同口径)。
// 判定与执行放在这个独立静态类里(不挂在对话框上),测试可以直接调用、也能注入「游戏在运行」。
using System;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Common.Launch;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Version;

/// <summary>存档回滚(先备份再回滚)与「游戏运行中禁止回滚」判定。</summary>
internal static class SavesRestore
{
    /// <summary>测试注入点:替换「游戏是否在运行」的判定。</summary>
    internal static Func<bool>? GameRunningOverride;

    /// <summary>本启动器拉起的游戏是否在运行(LaunchCore.IsRunning,与 AI 工具问宿主的 is_game_running 同一来源)。</summary>
    internal static bool GameRunning()
    {
        Func<bool>? hook = GameRunningOverride;
        if (hook != null)
        {
            return hook();
        }
        try
        {
            return LaunchCore.IsRunning;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>游戏运行中时给用户的提示。</summary>
    internal static string RunningHint => I18nCore.Tr("游戏正在运行,存档还在被写入:先退出游戏再还原备份");

    /// <summary>
    /// 还原(覆盖同名存档):先自动备份当前存档,再回滚;游戏运行中直接拒绝(抛 InvalidOperationException,消息已走词表)。
    /// 返回 SaveBackups.RestoreWithSafetyBackup 的结果 <c>{name, path, from, overwrite, safety_backup, safety_backup_path}</c>。
    /// </summary>
    internal static async Task<PyDict> RestoreAsync(string instance, string version, string backupName)
    {
        if (GameRunning())
        {
            throw new InvalidOperationException(RunningHint);
        }
        var inst = new Instance(string.IsNullOrEmpty(instance) ? null! : instance);
        object? ver = string.IsNullOrEmpty(version) ? null : version;
        PyDict result = await Task.Run(() => SaveBackups.RestoreWithSafetyBackup(inst, backupName, ver, overwrite: true));
        try
        {
            InProcessApi.Instance.RaiseEvent(new ApiEvent { Event = "ui_changed" });
        }
        catch (Exception)
        {
            // 通知其它页面刷新失败不影响回滚结果
        }
        return result;
    }

    /// <summary>还原成功后 Toast 的正文:说明回滚前那份存档备份成了哪个文件。</summary>
    internal static string DoneText(PyDict result, string backupName)
    {
        string safety = Py.S(result.Get("safety_backup"));
        return safety.Length > 0
            ? I18nCore.Tr("已从 {backup} 还原;还原前的存档已备份为 {safety}").Replace("{backup}", backupName).Replace("{safety}", safety)
            : I18nCore.Tr("已从 {backup} 还原").Replace("{backup}", backupName);
    }
}
