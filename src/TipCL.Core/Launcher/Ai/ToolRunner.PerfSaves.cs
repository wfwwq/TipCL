// AI-perf-saves:ToolRunner 里卡顿优化 + 存档备份那 7 个工具的分发(实现在 PerfSavesTools)。
// i18n:ignore-file —— 本文件的中文只在回给模型的错误信息里。
//
// 这里只多管一件 PerfSavesTools 管不了的事:写存档之前问宿主游戏是不是正在运行(is_game_running)。
// 游戏开着时存档文件还在被写:这时打包得到的是半截存档,回滚则会被游戏下一次自动保存盖掉,
// 所以 backup_save / restore_save_backup 一律拒绝,让模型请用户先退出游戏。
// 耗时的那几个(探测硬件要起 powershell / lspci、打包与解包存档)放到线程池跑,不占调用方线程。
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>游戏运行中拒绝写存档时回给模型的错误码(工具结果里的 error 字段)。</summary>
    public const string GameRunningError = "game_running";

    /// <summary>该工具是否永远不记「本对话都允许」(见 <see cref="PerfSavesTools.NeverAlwaysAllow"/>)。</summary>
    public static bool NeverAlwaysAllow(string tool) => PerfSavesTools.NeverAlwaysAllow(tool);

    private async Task<object?> PerfSavesAsync(string name, PyDict args, CancellationToken ct)
    {
        string current = Instance;
        switch (name)
        {
            case "get_system_info":
                return await Task.Run(() => PerfSavesTools.SystemInfoResult(SystemInfo.Snapshot()), ct).ConfigureAwait(false);

            case "diagnose_lag":
                return await Task.Run(() => JvmTuning.DiagnoseLag(PerfSavesTools.ResolveInstance(args, current), PerfSavesTools.Ver(args)), ct)
                    .ConfigureAwait(false);

            case "tune_jvm":
                return PerfSavesTools.RunTune(args, current);

            case "list_saves":
                return PerfSavesTools.ListSaves(args, current);

            case "list_save_backups":
                return PerfSavesTools.ListBackups(args, current);

            case "backup_save":
            case "restore_save_backup":
            {
                if (await IsGameRunningAsync(ct).ConfigureAwait(false))
                {
                    return ToolErr(name, GameRunningError,
                        "游戏正在运行，存档文件还在被写入：请先退出游戏，再备份或回滚存档", "");
                }
                return await Task.Run(() => name == "backup_save"
                        ? PerfSavesTools.Backup(args, current)
                        : PerfSavesTools.Restore(args, current), ct)
                    .ConfigureAwait(false);
            }

            default:
                return ToolErr(name, "unknown_tool", "没有这个工具: " + name, "");
        }
    }

    /// <summary>问宿主游戏是否在运行;宿主不支持或出错时按「没在运行」处理(不因为查询失败卡死备份)。</summary>
    private async Task<bool> IsGameRunningAsync(CancellationToken ct)
    {
        try
        {
            return await _api.CallAsync<bool>("is_game_running", null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
