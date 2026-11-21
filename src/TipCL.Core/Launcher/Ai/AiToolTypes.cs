// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/Ai/{AiData,ToolMetaInfo,ToolMeta,ToolCall,ToolCallStatus}.cs
// (M4-H P5 搬运;纯数据 + 纯逻辑,无 WPF/Win32 依赖)。
//
// Linux 侧改动(见 DECISIONS D-24):
//   1. 工具清单按 Linux 侧**真实存在**的能力裁剪(去掉 tune_jvm / backup_save 这类 Linux 没落地的),
//      不写「点了会报未知工具」的假条目;
//   2. `TOOL_SCHEMAS` 与 Windows 一样是 OpenAI 兼容的 function schema,**解析入口一致**(PyJson.Loads);
//   3. 系统提示词与 Windows 版逐字相同(只把 `__LANG_RULE__` 占位符在取用时替换)。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一个工具的元信息(风险分级、是否只读、是否长任务)。</summary>
public sealed class ToolMetaInfo
{
    public readonly string name;

    public readonly string side_effect;

    public readonly string risk;

    public readonly bool @readonly;

    public readonly bool long_running;

    public ToolMetaInfo(string name, bool ro, string side, string risk, bool lr)
    {
        this.name = name;
        @readonly = ro;
        side_effect = side;
        this.risk = risk;
        long_running = lr;
    }
}

/// <summary>工具元信息表(名字 → 风险/副作用)。</summary>
public static class ToolMeta
{
    private static Dictionary<string, ToolMetaInfo>? _map;

    /// <summary>
    /// Linux 侧的真实工具集。风险分级沿用 Windows 版的判据:
    /// none(不动任何东西)/ read(读文件)/ network(联网)/ write_local(写本机)/ delete / launch。
    /// </summary>
    public static readonly ToolMetaInfo[] ALL =
    {
        new("ask_user", true, "none", "low", false),
        new("get_launcher_state", true, "none", "low", false),
        new("list_instances", true, "none", "low", false),
        new("list_installed_versions", true, "none", "low", false),
        new("search_versions", true, "network", "low", false),
        new("search_mods", true, "network", "low", false),
        new("search_modpacks", true, "network", "low", false),
        new("search_content", true, "network", "low", false),
        new("search_worlds", true, "network", "low", false),
        new("list_mods", true, "read", "low", false),
        new("get_java_list", true, "none", "low", false),
        new("diagnose_launch", true, "read", "low", false),
        new("scan_mod_conflicts", true, "read", "low", false),
        new("inspect_mod", true, "read", "low", false),
        new("list_mod_configs", true, "read", "low", false),
        new("read_mod_config", true, "read", "low", false),
        new("update_plan", true, "none", "low", false),
        new("remember", true, "none", "low", false),
        new("forget", true, "none", "low", false),
        // 读工具产物（超长工具结果落盘后的回读）与派发只读子代理：两者都不动机器状态
        new("read_artifact", true, "read", "low", false),
        new("dispatch_subagent", true, "none", "low", false),
        new("install_mod", false, "write_local", "medium", true),
        new("install_modpack", false, "write_local", "high", true),
        new("install_shader", false, "write_local", "low", true),
        new("install_resourcepack", false, "write_local", "low", true),
        new("install_datapack", false, "write_local", "low", true),
        new("install_world", false, "write_local", "medium", true),
        new("install_game", false, "write_local", "high", true),
        new("download_java", false, "write_local", "medium", true),
        new("write_mod_config", false, "write_local", "high", false),
        new("create_instance", false, "write_local", "medium", false),
        new("disable_mod", false, "write_local", "medium", false),
        new("enable_mod", false, "write_local", "medium", false),
        new("delete_mod", false, "delete", "high", false),
        new("delete_instance", false, "delete", "high", false),
        new("launch_game", false, "launch", "high", false),
        // AI-logs-fs:日志与文件(分级照 Windows 版 ToolMeta.cs:38-39 / ExtraTools.Meta)。fs_write 是高风险写,不给「本对话都允许」
        new("get_latest_log", true, "read", "low", false),
        new("get_crash_report", true, "read", "low", false),
        new("fs_list", true, "read", "low", false),
        new("fs_read", true, "read", "low", false),
        new("fs_write", false, "write_local", "high", false),
        new("open_folder", true, "none", "low", false),
        // 卡顿优化 + 存档备份(AI-perf-saves;分级照 Windows 版 ExtraTools.Meta)
        new("get_system_info", true, "read", "low", false),
        new("diagnose_lag", true, "read", "low", false),
        new("tune_jvm", false, "write_local", "medium", false),
        new("list_saves", true, "read", "low", false),
        new("backup_save", false, "write_local", "low", false),
        new("list_save_backups", true, "read", "low", false),
        new("restore_save_backup", false, "delete", "high", false),
        // AI-acct-layout-mp:账号 / 皮肤 / 联机 / 排版(风险分级照 Windows ExtraTools.Meta;
        // multiplayer 的副作用按 Linux 实际行为记 network:它会拉起联机内核进程并连公共节点,不该被「文件类修改直接执行」档放行)
        new("list_accounts", true, "read", "low", false),
        new("add_offline_account", false, "write_local", "low", false),
        new("switch_account", false, "write_local", "low", false),
        new("set_skin", false, "write_local", "low", false),
        new("multiplayer", false, "network", "medium", false),
        new("layout_get", true, "read", "low", false),
        new("layout_apply", false, "write_local", "medium", false),
        // AI-new-features:升级 / 预检 / 服务器 / 光影推荐 / 诊断报告
        new("plan_instance_upgrade", true, "network", "low", false),
        new("preflight_check", true, "read", "low", false),
        new("list_servers", true, "read", "low", false),
        new("ping_server", true, "network", "low", false),
        new("recommend_shaders", true, "network", "low", false),
        new("add_server", false, "write_local", "medium", false),
        new("export_diagnostic_report", false, "write_local", "medium", false),
        new("apply_instance_upgrade", false, "write_local", "high", true),
        new("remove_server", false, "delete", "high", false),
    };

    public static Dictionary<string, ToolMetaInfo> Map
    {
        get
        {
            Dictionary<string, ToolMetaInfo>? map = _map;
            if (map == null)
            {
                map = ALL.ToDictionary((ToolMetaInfo m) => m.name);
                _map = map;
            }
            return map;
        }
    }

    public static ToolMetaInfo? Get(object? name)
    {
        if (name is not string key)
        {
            return null;
        }
        return Map.TryGetValue(key, out ToolMetaInfo? value) ? value : null;
    }
}

/// <summary>工具调用的生命周期状态(照 Windows 版 ToolCallStatus)。</summary>
public static class ToolCallStatus
{
    public const string SCHEDULED = "scheduled";

    public const string RUNNING = "running";

    public const string WAITING_PERMISSION = "waiting_permission";

    public const string COMPLETED = "completed";

    public const string FAILED = "failed";

    public const string DENIED = "denied";
}

/// <summary>一次工具调用(照 Windows 版 ToolCall)。</summary>
public sealed class ToolCall
{
    public string id;

    public string name;

    public PyDict args;

    public string status = ToolCallStatus.SCHEDULED;

    public string result = "";

    public string error = "";

    public double started_at;

    public double ended_at;

    public ToolCall(string id, string name, PyDict args)
    {
        this.id = id;
        this.name = name;
        this.args = args;
    }

    /// <summary>耗时(秒);还没结束返回 0。</summary>
    public double Elapsed => started_at > 0 && ended_at > 0
        ? Math.Max(0, ended_at - started_at)
        : (started_at > 0 ? Math.Max(0, Py.Monotonic() - started_at) : 0);

    public void Mark(string status, string result = "", string error = "")
    {
        if (status == ToolCallStatus.RUNNING && started_at == 0)
        {
            started_at = Py.Monotonic();
        }
        if (status is ToolCallStatus.COMPLETED or ToolCallStatus.FAILED or ToolCallStatus.DENIED)
        {
            ended_at = Py.Monotonic();
        }
        this.status = status;
        if (!string.IsNullOrEmpty(result))
        {
            this.result = result;
        }
        if (!string.IsNullOrEmpty(error))
        {
            this.error = error;
        }
    }
}
