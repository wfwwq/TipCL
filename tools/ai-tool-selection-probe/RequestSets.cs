// AI5-C3 回放探针的请求集与人工标注。
//
// 标注规则(务必遵守,否则复现不了报告里的数字):
//   Ideal = 「这一轮如果没有这个工具,任务就做不完」的工具集合。
//     · 依据是 AiData.SYSTEM_PROMPT 里写明的流程(例如「下载或安装前先 search_* 确认目标,再 install_*」
//       「用户说卡、掉帧时先 get_system_info + diagnose_lag」「回滚先 list_save_backups 再 restore_save_backup」);
//     · 只标「必须能调用」的,不标「调了更好」的(例如 get_launcher_state 在很多流程里会调,但用户已经说清
//       实例和版本时不算必须,因此通常不标);
//     · 多轮请求标的是**最后一轮**;
//     · 纯问候这类没有任务目标的请求不标注(不计入分母)。
using System;

namespace AiToolSelectionProbe;

/// <summary>一条回放请求。</summary>
internal sealed class Req
{
    public string Id = "";
    public string Topic = "";
    /// <summary>本轮之前的上一轮用户话(空 = 单轮请求)。</summary>
    public string Prev = "";
    /// <summary>上一轮与本轮之间那条工具结果的 content(模拟真实历史里工具回填的 JSON)。</summary>
    public string ToolResult = "{\"results\":[{\"name\":\"Sodium\",\"slug\":\"sodium\"}]}";
    /// <summary>本轮用户话(被评估的那一轮)。</summary>
    public string Text = "";
    /// <summary>本轮「必须能调用」的工具;空数组 = 该轮没有任务目标,不计入分母。</summary>
    public string[] Ideal = Array.Empty<string>();

    public static Req Single(string id, string topic, string text, params string[] ideal)
        => new Req { Id = id, Topic = topic, Text = text, Ideal = ideal };

    public static Req Multi(string id, string topic, string prev, string toolResult, string text, params string[] ideal)
        => new Req { Id = id, Topic = topic, Prev = prev, ToolResult = toolResult, Text = text, Ideal = ideal };
}

internal static class RequestSets
{
    /// <summary>
    /// 典型请求集(40 条):覆盖内置助手的主要意图。这是报告里「17.9/百回合」那一组。
    /// </summary>
    public static readonly Req[] Typical =
    {
        Req.Single("R01", "装模组", "帮我给 1.20.1 的 Fabric 装上钠", "search_mods", "ask_user", "install_mod"),
        Req.Single("R02", "装光影", "帮我装个光影包", "search_content", "install_shader", "ask_user"),
        Req.Single("R03", "按硬件推荐光影", "我这台电脑跑得动什么光影", "recommend_shaders", "ask_user"),
        Req.Single("R04", "换材质包", "帮我把材质包换成舒服点的", "search_content", "install_resourcepack", "ask_user"),
        Req.Single("R05", "数据包改配方", "帮我装个数据包,把钻石配方改简单点", "create_datapack", "validate_content"),
        Req.Single("R06", "闪退排错", "游戏闪退了,帮我看看", "get_launcher_state", "diagnose_launch", "get_latest_log", "get_crash_report"),
        Req.Single("R07", "卡顿优化", "好卡啊,帧数只有 20", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("R08", "内存不够", "我的内存是不是设小了,帮我调大点", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("R09", "禁用模组(命令)", "帮我把这个模组关掉", "disable_mod"),
        Req.Single("R10", "禁用模组(提问)", "怎么把这个模组关掉?", "disable_mod"),
        Req.Single("R11", "模组冲突", "小地图和钠冲突了,帮我看看留哪个", "scan_mod_conflicts", "inspect_mod", "ask_user"),
        Req.Single("R12", "查服务器延迟", "帮我看下服务器 mc.example.com 的延迟", "ping_server"),
        Req.Single("R13", "加服务器", "把 mc.hypixel.net 加进多人游戏列表", "add_server"),
        Req.Single("R14", "备份存档", "帮我备份一下存档再升级", "list_saves", "backup_save", "plan_instance_upgrade", "apply_instance_upgrade"),
        Req.Single("R15", "还原存档", "把我的生存存档还原到昨天的备份", "list_save_backups", "restore_save_backup"),
        Req.Single("R16", "升级到新版本", "升级到 1.21.1 吧", "plan_instance_upgrade", "apply_instance_upgrade", "search_versions"),
        Req.Single("R17", "导出整合包", "帮我把这个整合包导出成 mrpack", "check_modpack_distribution", "export_modpack"),
        Req.Single("R18", "一句话造包", "帮我做一个科技风的整合包", "plan_modpack", "ask_user", "create_instance", "install_game", "install_mods_batch", "scan_mod_conflicts", "preflight_check", "export_modpack"),
        Req.Single("R19", "读 latest.log", "帮我看看 latest.log 里写了啥", "fs_read", "get_latest_log"),
        Req.Single("R20", "打开模组文件夹", "把模组文件夹打开", "open_folder"),
        Req.Single("R21", "改模组配置", "帮我把 config 里的 sodium 配置改一下", "list_mod_configs", "read_mod_config", "write_mod_config"),
        Req.Single("R22", "启动前预检", "启动前帮我检查一下能不能启动", "preflight_check"),
        Req.Single("R23", "联机开房", "帮我开个房和朋友一起玩", "multiplayer"),
        Req.Single("R24", "换皮肤", "我要换个皮肤", "list_accounts", "set_skin"),
        Req.Single("R25", "改界面布局", "布局太乱了,帮我重排一下", "layout_get", "layout_apply"),
        Req.Single("R26", "加离线账号", "我想加个离线账号", "add_offline_account", "list_accounts"),
        Req.Single("R27", "导出诊断报告(口语)", "崩溃原因我搞不懂,想发到群里问问别人", "export_diagnostic_report", "get_crash_report"),
        Req.Single("R28", "查新版本", "帮我看看有没有新的游戏版本", "search_versions", "list_installed_versions"),
        Req.Single("R29", "卸载模组", "帮我把 JEI 卸了", "delete_mod", "disable_mod"),
        Req.Single("R30", "写 KubeJS", "帮我用 KubeJS 写个脚本,玩家进服给提示", "write_kubejs_script", "validate_content"),
        Req.Single("R31", "装地图存档", "帮我装张地图玩玩", "search_worlds", "install_world"),
        Req.Single("R32", "缺 Java(口语)", "游戏说找不到运行环境,帮我弄一下", "download_java", "get_java_list"),
        Req.Single("R33", "看 jar 信息(口语)", "这个 sodium.jar 装的是哪个版本?", "inspect_mod", "list_mods"),
        Req.Single("R34", "建个文件写说明(口语)", "帮我建个文件写点说明", "fs_write"),
        Req.Single("R35", "验证内容包", "我按你说的改了数据包,进游戏没生效", "validate_content"),
        Req.Single("R36", "打招呼(无标注)", "你好"),
        Req.Multi("R37", "续话:再来一个", "帮我装个光影包", "{\"results\":[{\"name\":\"BSL Shaders\",\"slug\":\"bsl\"}]}", "再来一个",
            "search_content", "install_shader"),
        Req.Multi("R38", "续话:怎么才能装好", "帮我安装个整合包", "{\"results\":[{\"name\":\"RLCraft\",\"slug\":\"rlcraft\"}]}", "怎么才能装好呢?",
            "search_modpacks", "install_modpack", "ask_user"),
        Req.Multi("R39", "续话:发我一份", "帮我把这次的崩溃日志导出一下", "{\"ok\":true,\"path\":\"diagnostics/export-2026-11-28.md\"}", "顺便也发我一份",
            "export_diagnostic_report"),
        Req.Multi("R40", "续话:光影也来一个", "装钠", "{\"results\":[{\"name\":\"Sodium\",\"slug\":\"sodium\"}]}", "那光影也来一个",
            "search_content", "install_shader"),
    };

    /// <summary>
    /// 口语变体集(12 条):同一批意图换常见口语说法,用来量「漏选率对说法有多敏感」。
    /// 这是报告里「41.7/百回合」那一组。
    /// </summary>
    public static readonly Req[] Variants =
    {
        Req.Single("V01", "停用模组", "帮我把那个模组停用一下", "disable_mod"),
        Req.Single("V02", "别加载了", "这个模组别加载了,关掉吧", "disable_mod"),
        Req.Single("V03", "卸掉材质包", "帮我把之前那个材质包卸了", "delete_mod", "disable_mod"),
        Req.Single("V04", "日志发给作者", "想把崩溃日志整理一下发给作者", "export_diagnostic_report"),
        Req.Single("V05", "推荐光影(口语)", "我这台电脑装哪个光影包合适", "recommend_shaders", "ask_user"),
        Req.Single("V06", "java 报错", "游戏报错说 java 有问题,帮我处理一下", "download_java", "get_java_list"),
        Req.Single("V07", "这是哪个模组", "这个 jar 是什么模组", "inspect_mod"),
        Req.Single("V08", "写说明文件", "帮我写个说明文件放到实例目录", "fs_write"),
        Req.Single("V09", "备份存档(口语)", "帮我把存档备份到另一个地方,别丢了", "backup_save", "list_saves"),
        Req.Single("V10", "掉帧查内存", "帧数掉得厉害,帮我看看是不是内存问题", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("V11", "换皮肤(口语)", "我想换个更好看的皮肤", "list_accounts", "set_skin"),
        Req.Single("V12", "调页面顺序", "帮我把页面顺序调一下", "layout_get", "layout_apply"),
    };

    /// <summary>
    /// 关键词补丁集(10 条):AI5-C3 方案 1b 补关键词**针对**的这些说法(SH-1 场景 18a 的「好卡」也在内)。
    /// **注意**:这一组是「因」不是「果」——补词就是照它们写的,所以补完必然 0 漏选,
    /// 不能拿它证明真实覆盖率;真实覆盖率要看 <see cref="HoldOut"/>。
    /// </summary>
    public static readonly Req[] KeywordPatch =
    {
        Req.Single("T301", "好卡(t-win 场景 18a 原文)", "好卡", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("T302", "有点卡", "游戏有点卡,帮我看看", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("T303", "一卡一卡的", "一卡一卡的,怎么调", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("T304", "优化模组卸了", "帮我把优化模组卸了", "delete_mod", "disable_mod"),
        Req.Single("T305", "没有运行环境", "游戏说没有运行环境,帮我装一个", "download_java", "get_java_list"),
        Req.Single("T306", "这个 jar 里面是什么模组", "这个 jar 里面是什么模组", "inspect_mod"),
        Req.Single("T307", "日志发到群里", "把这些日志发到群里问问", "export_diagnostic_report", "get_latest_log"),
        Req.Single("T308", "写个说明文件", "帮我写个说明文件", "fs_write"),
        Req.Single("T309", "新建个文件", "新建个文件记点东西", "fs_write"),
        Req.Single("T310", "模组停用了", "把那个模组停用了", "disable_mod"),
    };

    /// <summary>
    /// 对抗性留出集(10 条):**故意**用关键词表里没有的说法,而且不参与补词。
    /// 它的作用只有一个:量「方案 1(补关键词)还剩多少长尾」——1b 之后这一组仍然会漏,
    /// 这是关键词路线的天花板,也是当初把方案 2(目录 + find_tools/load_tools)摆上桌的理由。
    /// </summary>
    public static readonly Req[] HoldOut =
    {
        Req.Single("H01", "把钠关了", "把钠关了", "disable_mod"),
        Req.Single("H02", "从 mods 里拿掉", "帮我把 JEI 从 mods 里拿掉", "delete_mod", "disable_mod"),
        Req.Single("H03", "老是一卡", "游戏怎么老是一卡", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("H04", "发论坛", "日志整理一下,我要发论坛", "export_diagnostic_report"),
        Req.Single("H05", "模组文件是啥版本", "这个模组文件是啥版本", "inspect_mod"),
        Req.Single("H06", "java 版本不对", "java 版本不对,帮我换个", "download_java", "get_java_list"),
        Req.Single("H07", "来份诊断", "给我来份诊断", "export_diagnostic_report"),
        Req.Single("H08", "好卡+电脑配置", "好卡呀,帮我看下电脑配置", "get_system_info", "diagnose_lag", "tune_jvm"),
        Req.Single("H09", "别启用了", "把那个模组先别启用了", "disable_mod"),
        Req.Single("H10", "配置项关掉", "把 config 里的 sodium 项关掉", "write_mod_config"),
    };
}