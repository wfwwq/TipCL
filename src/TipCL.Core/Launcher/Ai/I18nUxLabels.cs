// AI2-i18n-ux:上一轮新增工具(AI-logs-fs 6 条 + AI-new-features 9 条)的步骤行 / 确认卡标签走词表。
//
// 这些标签原来写在 Tools.cs 的 Labels / Formats 两张字典里,是裸中文 —— 英文界面下步骤行、确认卡标题
// 仍显示中文(PARITY §10.4 第 1 条)。字典是静态只读的,值在类型初始化时就定死了,切语言改不动,
// 所以这里改成「每次取标签时现查词表」的 switch,与 PerfSavesPreview.Label 同一写法;
// Tools.Label 开头一行转发过来(其余老工具仍走 Tools 自己的表,只是在返回前也过一遍 I18nCore.Tr)。
// 模板占位({path} 之类)在词表的 en 译文里原样保留;参数缺失时整条回落成通用标签(同 Tools.FillTemplate)。
using System;

namespace TipCL.Core.Launcher.Ai;

/// <summary>上一轮新增工具的标签与参数模板(全部走 I18nCore.Tr)。</summary>
public static class I18nUxLabels
{
    private static string Tr(string key) => I18nCore.Tr(key);

    /// <summary>通用标签;不是本表负责的工具返回 null。</summary>
    public static string? Plain(string? name) => name switch
    {
        // AI-logs-fs(标签照 Windows 版 Tools._TOOL_LABELS / ExtraTools.Labels)
        "get_latest_log" => Tr("读最新日志"),
        "get_crash_report" => Tr("读崩溃报告"),
        "fs_list" => Tr("列文件"),
        "fs_read" => Tr("读文件"),
        "fs_write" => Tr("写文件"),
        "open_folder" => Tr("打开文件夹"),
        // AI-new-features
        "plan_instance_upgrade" => Tr("查升级计划"),
        "apply_instance_upgrade" => Tr("升级实例"),
        "preflight_check" => Tr("启动前预检"),
        "list_servers" => Tr("查服务器列表"),
        "add_server" => Tr("添加服务器"),
        "remove_server" => Tr("删除服务器"),
        "ping_server" => Tr("查服务器状态"),
        "recommend_shaders" => Tr("按硬件推荐光影"),
        "export_diagnostic_report" => Tr("导出诊断报告"),
        _ => null,
    };

    /// <summary>带参数的模板;没有模板返回 null。</summary>
    public static string? Template(string? name) => name switch
    {
        "fs_list" => Tr("列文件 {path}"),
        "fs_read" => Tr("读文件 {path}"),
        "fs_write" => Tr("写文件 {path}"),
        "open_folder" => Tr("打开文件夹 {kind}"),
        "plan_instance_upgrade" => Tr("查升级计划 {instance} → {target_version}"),
        "apply_instance_upgrade" => Tr("升级实例 {instance} → {target_version}"),
        "add_server" => Tr("添加服务器 {address}"),
        "remove_server" => Tr("删除服务器 #{index}"),
        "ping_server" => Tr("查服务器 {address}"),
        "export_diagnostic_report" => Tr("导出诊断报告 → {dest_dir}"),
        _ => null,
    };

    /// <summary>Tools.Label 的转发入口:不是本表的工具返回 null(交回 Tools 原逻辑)。</summary>
    public static string? Label(string? name, PyDict? args)
    {
        string? label = Plain(name);
        if (label == null || args == null)
        {
            return label;
        }
        string? fmt = Template(name);
        if (fmt == null)
        {
            return label;
        }
        string filled = Fill(fmt, args);
        return filled.Length > 0 ? filled : label;
    }

    /// <summary>按 {字段} 填参数;任一字段缺值返回 ""(调用方回落通用标签),与 Tools.FillTemplate 同口径。</summary>
    public static string Fill(string fmt, PyDict args)
    {
        string result = fmt;
        int at = 0;
        while (true)
        {
            int open = result.IndexOf('{', at);
            if (open < 0)
            {
                break;
            }
            int close = result.IndexOf('}', open + 1);
            if (close < 0)
            {
                break;
            }
            string field = result.Substring(open + 1, close - open - 1);
            string value = Py.S(args.Get(field));
            if (value.Length == 0)
            {
                return "";
            }
            result = result.Substring(0, open) + value + result.Substring(close + 1);
            at = open + value.Length;
        }
        return result.Replace("  ", " ").Trim();
    }
}
