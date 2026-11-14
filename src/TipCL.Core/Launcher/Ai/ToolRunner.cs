// M4-H P5:AI 工具执行器(Tools.ExecuteTool 的 Linux 版)。
//
// Windows 版的 Tools.ExecuteTool(1264 行)把每个工具直接接到 BackendApi 上。Linux 侧没有 BackendApi,
// 顶层能力都在 App 的 IApi 门面(Common/Api.cs)后面 —— 所以这里按同一批工具名接 **IApi**,
// 再加几个直接走 Core 的只读工具(诊断/冲突/配置)。接 IApi 还有个好处:测试能塞假门面,不联网。
//
// 权限:只读工具直接跑;写/删/启动类工具先问宿主(UI 的 AiPermissionPanel / ConfirmCard),
// 用户可选「仅这一次」或「这个工具以后都允许」(与 Windows 版 Permission 的 scope 语义一致)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.Core.Launcher.Ai;

/// <summary>
/// 工具执行器。宿主(页面)通过回调提供三件事:权限确认、ask_user 结构化选择、进度事件。
/// </summary>
public sealed class ToolRunner
{
    /// <summary>调用 IApi 的门面(接口保持最小:只用到 CallAsync)。</summary>
    public interface IBackend
    {
        Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default);
    }

    private readonly IBackend _api;

    /// <summary>「这个工具以后都允许」的进程内授权表(照 Windows 版 Permission 的 always 档)。</summary>
    private readonly HashSet<string> _alwaysAllowed = new(StringComparer.Ordinal);

    /// <summary>
    /// 权限档位(面板「照 Windows 版 AiPermissionPanel」只提供本地这几档):
    /// default 每次确认 / acceptEdits 写直接执行 / plan 只看不动 / yolo 全自动 / custom 走自定义规则。
    /// 裁决还认参考版 Permission.PermissionMode.Values 里的别名(auto / autoEdit / build / edit /
    /// bypassPermissions / dontAsk)——它们只可能来自手改 config.json 或从 Windows 迁来的配置。
    /// 空串表示读配置(config.json 的 ai_permission_mode)。
    /// </summary>
    public string PermissionMode { get; set; } = "";

    /// <summary>
    /// 档位归一化(config.json 的 ai_permission_mode 原始值 → 实际档位名;空 / 缺失 → default)。
    /// 裁决(<see cref="Mode"/>)、给模型看的权限提示、自检都走这一份 —— 不另造第二套:
    /// 两份真源一定会漂(这也正是本轮把「归一化」从 Mode 里提出来做公开静态方法的原因)。
    /// </summary>
    public static string NormalizeMode(object? raw)
    {
        string mode = Py.S(raw);
        return mode.Length > 0 ? mode : "default";
    }

    private string Mode
        => PermissionMode.Length > 0 ? PermissionMode : NormalizeMode(Config.CONFIG.Get("ai_permission_mode", "default"));

    /// <summary>
    /// 一次权限裁决:<see cref="decision"/> 是 allow / ask / deny,<see cref="reason"/> 是给用户看的
    /// 中文理由(照 Windows 版 Permission.PermissionResult)。界面把理由显示在确认卡的「为什么需要你确认」
    /// 那一行 —— 裁决与理由**必须一起产出**:分开返回就会重演「卡片有那一行、调用点却给空」的断链。
    /// </summary>
    public readonly record struct PermissionVerdict(string decision, string reason);

    /// <summary>
    /// 该工具按当前档位与自定义规则算出的裁决(allow / ask / deny)+ 理由。
    /// 理由文案与 Windows 版 Permission.Decide 的 switch 逐字对位(见下面 switch 里各分支的判据);
    /// 档位集合与参考版一致(含别名),面板仍只写本地那几档。
    /// </summary>
    public PermissionVerdict Decide(string tool) => ApplyDontAsk(DecideRaw(tool));

    /// <summary>
    /// 「不询问」开关(<c>config.json</c> 的 <c>ai_permission_dont_ask</c>):需要确认的写操作**直接拒绝**。
    /// Windows 版在 Agent 里消费同一个键(<c>Agent.cs:571</c> / <c>:1149-1152</c>),理由文案逐字相同。
    /// 只作用于 ask —— allow 与 deny 一律保持原样;Linux 侧的 Config 一直写着这个键的默认值,
    /// 不读它等于「配置里有开关、拉了没反应」。
    /// </summary>
    private static PermissionVerdict ApplyDontAsk(PermissionVerdict verdict)
    {
        if (verdict.decision == "ask" && Py.Truthy(Config.CONFIG.Get("ai_permission_dont_ask")))
        {
            return new PermissionVerdict("deny", "用户开启了「不询问」模式");
        }
        return verdict;
    }

    /// <summary>按档位与自定义规则算出的原始裁决(「不询问」开关由 <see cref="Decide"/> 叠加)。</summary>
    private PermissionVerdict DecideRaw(string tool)
    {
        // 「只看不动」是**硬停**:参考版把这一步放在规则之前(Permission.cs:186-190:
        // `if (mode == "plan" && !readonly) return deny`),所以连 allow 规则也突破不了它。
        // Linux 原来把规则放在前面,于是一条 allow 规则能把 plan 档压过去 —— 用户口径的
        // 「只看不动」就形同虚设。这里与参考版同序;只读工具不受影响(参考版同样先看 readonly)。
        if (Mode == "plan" && ToolMeta.Get(tool) is { } planMeta && !planMeta.@readonly)
        {
            return new PermissionVerdict("deny", "当前是「只看不动」模式，不能执行这类操作");
        }
        string rule = RuleFor(tool);
        if (rule.Length > 0)
        {
            // 规则三条理由与 Windows 版逐字相同
            return rule switch
            {
                "allow" => new PermissionVerdict("allow", "规则允许"),
                "deny" => new PermissionVerdict("deny", "这条操作被你的规则禁止"),
                _ => new PermissionVerdict("ask", "规则要求先询问"),
            };
        }
        ToolMetaInfo? meta = ToolMeta.Get(tool);
        if (meta == null)
        {
            // Linux 独有的一支:工具表里没有的名字直接拒(Windows 版 Decide 没有 meta 为空的判断,
            // 故这句文案是 Linux 自定的,不假装与参考版一致)
            return new PermissionVerdict("deny", "未知工具");
        }
        if (meta.@readonly)
        {
            return new PermissionVerdict("allow", "只读操作");
        }
        return Mode switch
        {
            // 下面每一档的**档位名、side_effect 判据、理由文案**都与 Windows 版 Permission.Decide
            // (Permission.cs:220-253)逐条对位:手改 config.json 或从 Windows 迁来的配置里写了
            // auto / autoEdit / build / edit / bypassPermissions / dontAsk 时,裁决要与 PermissionNote
            // 说的是同一件事(面板仍只提供本地那几档,别名只可能来自配置文件)。
            "yolo" or "bypassPermissions" => new PermissionVerdict("allow", "免确认模式"),
            // plan 已在上面拦掉（与参考版同序）；这一支是兜底,防「那条前置判断被误删」
            "plan" => new PermissionVerdict("deny", "当前是「只看不动」模式，不能执行这类操作"),
            "auto" or "acceptEdits" or "autoEdit" => meta.side_effect == "delete"
                ? new PermissionVerdict("ask", "删除操作仍需确认")
                : new PermissionVerdict("allow", "写操作直接执行"),
            "build" => meta.side_effect is "delete" or "launch"
                ? new PermissionVerdict("ask", "删除/启动操作仍需确认")
                : new PermissionVerdict("allow", "安装/下载类直接执行"),
            "edit" => meta.side_effect == "write_local"
                ? new PermissionVerdict("allow", "文件类修改直接执行")
                : new PermissionVerdict("ask", "这类操作需要确认"),
            // dontAsk 档的拒绝理由与 ApplyDontAsk 那句逐字相同:一个是档位本身就是「不询问」,
            // 一个是独立开关把 ask 翻成 deny,给用户看的话是同一句(Permission.cs:245-246)
            "dontAsk" => new PermissionVerdict("deny", "用户开启了「不询问」模式"),
            // default / custom / 空档:一律要确认,理由按风险等级分句(判据同 Windows 版 default 分支的 risk == "high")
            _ => meta.risk == "high"
                ? new PermissionVerdict("ask", "高风险操作需要确认")
                : new PermissionVerdict("ask", "写操作需要确认"),
        };
    }

    /// <summary>自定义规则(config.json 的 ai_permission_rules,照 Windows 版 ai_permission_rule_add)。</summary>
    private static string RuleFor(string tool)
    {
        foreach (object? raw in Py.ListOr(Config.CONFIG.Get("ai_permission_rules")))
        {
            PyDict rule = Py.D(raw);
            if (!Py.Eq(rule.Get("tool"), tool))
            {
                continue;
            }
            string behavior = Py.S(rule.Get("behavior"));
            if (behavior is "allow" or "deny" or "ask")
            {
                return behavior;
            }
        }
        return "";
    }

    /// <summary>当前实例(工具参数里没写 instance 时用)。</summary>
    public string Instance { get; set; } = "";

    /// <summary>
    /// 当前权限档位写成一句话,给模型看(照 Windows 版 Permission.PermissionNote,Permission.cs:256-276;
    /// 参考版的消费点在 Agent.cs:348 / :368,作为一条独立的 system 消息)。
    /// 键位一律用 Linux 自己那一份:档位走 <see cref="NormalizeMode"/>(和裁决同一个入口),
    /// 「不询问」走 ai_permission_dont_ask —— **不读 ai_confirm_writes**:Linux 的裁决根本不看那个键,
    /// 读了它这句话就会与真实裁决对着说(见 6 句的档位归属)。
    /// </summary>
    public static string PermissionNote()
        => PermissionNote(Config.CONFIG.Get("ai_permission_mode", "default"),
                          Py.Truthy(Config.CONFIG.Get("ai_permission_dont_ask")));

    /// <summary>
    /// 档位 → 那句话(档位名与参考版 PermissionNote 的 switch 逐字对位;句中的全角标点也是照抄的)。
    /// <paramref name="dontAsk"/> 是 config 的 ai_permission_dont_ask:Linux 侧「不询问」是一个**独立开关**
    /// 而不是档位名,它把「需要确认」直接改成「拒绝」(D-48),所以模型看到的档位描述也要跟着换,
    /// 否则模型会以为还有确认卡可等。yolo(全放行)与 plan(本来就拒)两档下这个开关不生效,保持原句更准。
    /// 别名(bypassPermissions / auto / autoEdit / build / edit)照参考版给同一句:裁决现在也认这些
    /// 别名(见 DecideRaw 的 switch),所以「告知」与「裁决」说的是同一件事(两面的回归分别在
    /// Core/AiPermissionNoteTests 与 Core/AiPermissionReasonTests)。
    /// </summary>
    public static string PermissionNote(object? mode, bool dontAsk = false)
    {
        string name = NormalizeMode(mode);
        if (dontAsk && name is not ("yolo" or "plan"))
        {
            name = "dontAsk";
        }
        return name switch
        {
            "yolo" or "bypassPermissions" => "[权限设置] 用户选了「全自动」：写操作直接执行，不会弹确认。你仍要先用一句话说明将要做什么。",
            "plan" => "[权限设置] 用户选了「只看不动」：你只能查看和诊断，不能安装/删除/改配置。",
            "auto" or "acceptEdits" or "autoEdit" or "build" => "[权限设置] 写操作会直接执行；删除实例、删除模组前仍会先询问，要等用户点了才执行。",
            "dontAsk" => "[权限设置] 用户选了「不询问」：需要确认的写操作会被直接拒绝，只读查看照常。",
            "edit" => "[权限设置] 文件类修改直接执行；安装、删除、启动前会弹确认，要等用户点了才执行。",
            _ => "[权限设置] 安装、删除、禁用、改配置、启动这类写操作执行前都会弹确认，用户点了才会真的执行；查看类操作不弹。被拒绝时不要重复发起同一操作，改为说明原因。",
        };
    }

    /// <summary>
    /// 已连上的 MCP server(由 AiAgent 在每轮开始时按 config 的 ai_mcp_servers 连好)。
    /// 名字形如 mcp_&lt;server&gt;_&lt;tool&gt; 的调用走这些外部 server，见 Mcp.RouteCall。
    /// </summary>
    public List<McpClient> McpClients { get; set; } = new();

    /// <summary>
    /// dispatch_subagent 的执行体(由 AiAgent 注入:起一个只读的嵌套会话，把结论带回来)。
    /// 没注入时该工具明确报错，不假装能做。
    /// </summary>
    public Func<PyDict, CancellationToken, Task<string>>? Subagent { get; set; }

    /// <summary>
    /// 权限确认回调:参数依次是工具调用、给人看的**标签**(<see cref="Tools.Label">Tools.Label</see>)、
    /// **裁决理由**(<see cref="Decide">Decide</see> 算出来的「为什么需要你确认」)与「改前改后」的预览。
    /// 返回 true 表示允许执行;always=true 时本次之后不再问同一工具。
    /// 预览在这里算而不是在页面算:与参考版「后端算好再发事件」同一条口径,也保证只有一份实现
    /// (见 DECISIONS D-31);没有可预览的东西时为 null,界面把它摊平成行后放进确认卡。
    /// 标签与理由是确认卡上的两个槽(卡片也是 label / reason 两行),各就各位 —— 别把标签当理由传。
    /// </summary>
    public Func<ToolCall, string, string, PyDict?, Task<(bool ok, bool always)>>? Confirm { get; set; }

    /// <summary>ask_user 回调:收结构化题面,回用户的选择(PyDict:answers/other)。</summary>
    public Func<PyDict, Task<object?>>? AskUser { get; set; }

    /// <summary>计划更新回调(update_plan 工具)。</summary>
    public Action<List<(string Title, string Status)>>? PlanChanged { get; set; }

    /// <summary>
    /// 长任务起好后通知宿主(装模组/装游戏 → 任务卡):任务 id、给人看的标签、**工具名**。
    /// 工具名一定要传:任务卡靠它决定显示哪个后续动作(参考版的任务事件也带 tool)。
    /// </summary>
    public Action<string, string, string>? TaskStarted { get; set; }

    /// <summary>日志(宿主显示在步骤行里)。</summary>
    public Action<string>? Log { get; set; }

    public ToolRunner(IBackend api) => _api = api;

    /// <summary>宿主门面（AiAgent 派生子代理时要拿同一个后端另起一个只读执行器）。</summary>
    internal IBackend Api => _api;

    /// <summary>该工具现在是否免确认。</summary>
    public bool IsAllowedAlways(string name) => _alwaysAllowed.Contains(name);

    /// <summary>手动授权(页面「一直允许」按钮 / 测试用)。</summary>
    public void AllowAlways(string name) => _alwaysAllowed.Add(name);

    /// <summary>
    /// 执行一次工具调用。返回值是回给模型的文本(工具消息的 content)。
    /// 这里是**唯一**会改机器状态的入口,所有写操作都先过 <see cref="Confirm"/>。
    /// </summary>
    public async Task<string> RunAsync(ToolCall call, CancellationToken ct = default)
    {
        bool mcp = Mcp.IsMcpTool(call.name);
        if (!mcp && !ToolMeta.Map.ContainsKey(call.name))
        {
            call.Mark(ToolCallStatus.FAILED, error: "未知工具");
            return ToolErr(call.name, "unknown_tool", "没有这个工具: " + call.name, "");
        }

        // MCP 工具是外部进程，本机无法判定它到底动没动东西:一律按写操作对待（除 yolo 档）
        bool readOnly = !mcp && ToolMeta.Get(call.name)!.@readonly;
        if (!readOnly && !_alwaysAllowed.Contains(call.name))
        {
            PermissionVerdict verdict = mcp ? ApplyDontAsk(McpVerdict()) : Decide(call.name);
            if (verdict.decision == "deny")
            {
                // 步骤行右侧那格要说明「为什么被拒」(照 Windows 的 "denied" 工具状态带 reason),
                // 别再写一句和判定无关的泛泛文案 —— 理由已经由裁决带出来了。
                call.Mark(ToolCallStatus.DENIED, error: "已拒绝:" + verdict.reason);
                return ToolErr(call.name, "permission_denied",
                    Mode == "plan" ? "当前是「只看不动」档位,不能执行写操作" : "当前权限档位不允许这一步", "");
            }
            if (verdict.decision == "ask")
            {
                if (Confirm == null)
                {
                    call.Mark(ToolCallStatus.DENIED, error: "需要用户确认");
                    return ToolErr(call.name, "permission_denied", "这一步需要用户确认,但界面没有接确认回调", "");
                }
                call.Mark(ToolCallStatus.WAITING_PERMISSION);
                // 两个槽别混:label 是「做什么」(如「装模组 钠」),reason 是「为什么这一步要你点」
                string label = Tools.Label(call.name, call.args);
                PyDict? preview = null;
                try
                {
                    preview = Preview.ChangePreview(call.name, call.args, Instance);
                }
                catch (Exception)
                {
                    // 预览算不出来就当没有:它是给用户看的辅助信息,不能挡住这一步的执行
                    preview = null;
                }
                (bool ok, bool always) = await Confirm(call, label, verdict.reason, preview).ConfigureAwait(false);
                if (always) _alwaysAllowed.Add(call.name);
                if (!ok)
                {
                    call.Mark(ToolCallStatus.DENIED, error: "用户拒绝");
                    return ToolErr(call.name, "permission_denied", "用户拒绝了这一步", "");
                }
            }
        }

        call.Mark(ToolCallStatus.RUNNING);
        Log?.Invoke(Tools.Label(call.name, call.args));
        try
        {
            object? result = await ExecuteAsync(call, ct).ConfigureAwait(false);
            string text = Clip(AsText(result), call.name);
            call.Mark(ToolCallStatus.COMPLETED, result: text);
            return text;
        }
        catch (OperationCanceledException)
        {
            call.Mark(ToolCallStatus.DENIED, error: "已停止");
            return ToolErr(call.name, "cancelled", "用户停止了这次操作", "");
        }
        catch (Exception ex)
        {
            string code = Tools.ClassifyException(ex);
            call.Mark(ToolCallStatus.FAILED, error: Py.ExcStr(ex));
            Log?.Invoke("失败: " + Tools.ErrorText(code) + " — " + Py.ExcStr(ex));
            return Tools.ErrorPayload(ex, call.name, code);
        }
    }

    /// <summary>MCP 工具的裁决：plan 档不动、yolo 档放行，其余都要用户点一下(**这是 Linux 独有的一支**:参考版 Permission.Decide 里没有 MCP 分支,这里的档位集合与理由文案都是 Linux 自定的)。</summary>
    private PermissionVerdict McpVerdict() => Mode switch
    {
        // bypassPermissions 与 yolo 同等看待(它是同一个意思);plan 档仍然拒绝,其余要确认
        "yolo" or "bypassPermissions" => new PermissionVerdict("allow", "免确认模式"),
        "plan" => new PermissionVerdict("deny", "当前是「只看不动」模式，不能执行这类操作"),
        _ => new PermissionVerdict("ask", "外部 MCP 工具能力未知，需确认"),
    };

    /// <summary>
    /// 工具结果太长（超过 8000 字）就整体落盘，只把「文件名 + 前 120 行」回给模型
    /// （照 Windows 版 Tools._clip）:长结果塞进上下文会把窗口挤爆，模型需要细看时用 read_artifact 读回来。
    /// </summary>
    private static string Clip(string text, string toolName)
    {
        if ((text ?? "").Length > 8000)
        {
            return Artifacts.Store(text, toolName);
        }
        return text ?? "";
    }

    private static string ToolErr(string name, string code, string message, string detail)
        => PyJson.Dumps(PyDict.Of("ok", false, "error", code, "message", message, "detail", detail, "tool", name));

    private static string AsText(object? result)
    {
        if (result == null)
        {
            return "";
        }
        if (result is string s)
        {
            return s;
        }
        if (result is PyDict || result is List<object>)
        {
            return PyJson.Dumps(result);
        }
        return Py.Str(result);
    }

    private string InstName(PyDict args)
        => Py.Or(args.Get("instance"), Instance) as string is { Length: > 0 } n ? n : Instance;

    private async Task<object?> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        PyDict args = call.args ?? new PyDict();
        switch (call.name)
        {
            // ── 只读:状态 / 查询 ──
            case "get_launcher_state":
                return LauncherState();

            case "list_instances":
                return await _api.CallAsync<List<object?>>("get_instances", null, ct).ConfigureAwait(false) ?? new List<object?>();

            case "list_installed_versions":
                return await _api.CallAsync<List<object?>>("get_installed_versions", new { instance = InstName(args) }, ct).ConfigureAwait(false) ?? new List<object?>();

            case "list_mods":
                return await _api.CallAsync<List<object?>>("get_installed_mod_entries", new { instance = InstName(args) }, ct).ConfigureAwait(false) ?? new List<object?>();

            case "get_java_list":
                return await _api.CallAsync<List<object?>>("get_java_list", new { scan_system = false }, ct).ConfigureAwait(false) ?? new List<object?>();

            case "search_versions":
                return await _api.CallAsync<List<object?>>("get_version_list",
                    new { query = Py.S(args.Get("query")), kind = Py.Or(args.Get("kind"), "release") }, ct).ConfigureAwait(false) ?? new List<object?>();

            case "search_mods":
            case "search_modpacks":
            case "search_worlds":
            {
                string method = call.name switch
                {
                    "search_modpacks" => "search_modpacks",
                    "search_worlds" => "search_worlds",
                    _ => "search_mods",
                };
                return await _api.CallAsync<List<object?>>(method,
                    new { query = Py.S(args.Get("query")), source = Py.Or(args.Get("source"), "全部") }, ct).ConfigureAwait(false) ?? new List<object?>();
            }

            case "search_content":
            {
                string kind = Py.Lower(Py.S(Py.Or(args.Get("kind"), "shader")));
                string method = kind switch
                {
                    "resourcepack" or "resourcepacks" or "资源包" => "search_resourcepacks",
                    "datapack" or "datapacks" or "数据包" => "search_datapacks",
                    _ => "search_shaders",
                };
                return await _api.CallAsync<List<object?>>(method,
                    new { query = Py.S(args.Get("query")), source = Py.Or(args.Get("source"), "全部") }, ct).ConfigureAwait(false) ?? new List<object?>();
            }

            // ── 诊断 / 冲突 / 配置(直接走 Core,不走 IApi)──
            case "diagnose_launch":
                return Diagnose(args);

            case "scan_mod_conflicts":
                return Conflict.ScanConflicts(new Instance(InstName(args)));

            case "inspect_mod":
                return InspectMod(args);

            case "list_mod_configs":
                return ListConfigs(args);

            case "read_mod_config":
            {
                string text = ReadConfig(args, out string readPath);
                return PyDict.Of("path", readPath, "content", text);
            }

            // ── 写操作(已过权限)──
            case "write_mod_config":
                return WriteConfig(args);

            case "install_game":
                return Start("install_game", new
                {
                    version = Py.S(args.Get("version")),
                    loader = Py.Or(args.Get("loader"), "无"),
                    loader_version = args.Get("loader_version"),
                    instance = InstName(args),
                }, ct, out string gameTask, "安装 " + Py.S(args.Get("version")));

            case "install_mod":
                return Start("install_mod", new
                {
                    name = Py.S(Py.Or(args.Get("slug"), args.Get("name"))),
                    instance = InstName(args),
                    source = Py.Or(args.Get("source"), "Modrinth"),
                }, ct, out string modTask, "安装模组 " + Py.S(args.Get("name")));

            case "install_modpack":
                return Start("install_modpack", new
                {
                    name = Py.S(Py.Or(args.Get("slug"), args.Get("name"))),
                    instance = InstName(args),
                    source = Py.Or(args.Get("source"), "Modrinth"),
                }, ct, out string packTask, "安装整合包 " + Py.S(args.Get("name")));

            case "install_shader":
            case "install_resourcepack":
            case "install_datapack":
            case "install_world":
                return Start(call.name, new
                {
                    name = Py.S(Py.Or(args.Get("slug"), args.Get("name"))),
                    instance = InstName(args),
                    source = Py.Or(args.Get("source"), "Modrinth"),
                }, ct, out string contentTask, "安装 " + Py.S(args.Get("name")));

            case "create_instance":
            {
                object? created = await _api.CallAsync<object>("create_instance", new { name = Py.S(args.Get("name")) }, ct).ConfigureAwait(false);
                return PyDict.Of("ok", true, "instance", Py.S(args.Get("name")), "detail", AsText(created));
            }

            case "delete_instance":
            {
                await _api.CallAsync<object>("delete_instance", new { name = Py.S(args.Get("name")) }, ct).ConfigureAwait(false);
                return PyDict.Of("ok", true, "deleted", Py.S(args.Get("name")));
            }

            case "delete_mod":
                await _api.CallAsync<object>("delete_mod", new { instance = InstName(args), filename = Py.S(args.Get("filename")) }, ct).ConfigureAwait(false);
                return PyDict.Of("ok", true, "filename", Py.S(args.Get("filename")));

            case "disable_mod":
                await _api.CallAsync<object>("disable_mod", new { instance = InstName(args), filename = Py.S(args.Get("filename")) }, ct).ConfigureAwait(false);
                return PyDict.Of("ok", true, "filename", Py.S(args.Get("filename")), "enabled", false);

            case "enable_mod":
                await _api.CallAsync<object>("enable_mod", new { instance = InstName(args), filename = Py.S(args.Get("filename")) }, ct).ConfigureAwait(false);
                return PyDict.Of("ok", true, "filename", Py.S(args.Get("filename")), "enabled", true);

            case "download_java":
                return Start("download_java", new { major = Py.S(args.Get("major")) }, ct, out string javaTask, "下载 Java " + Py.S(args.Get("major")));

            case "launch_game":
            {
                // version 留空时交给启动链自己挑(LaunchCore 会取实例的默认版本设置),
                // 这里不额外查一次,避免多一次无谓的 API 往返
                return Start("launch_game", new
                {
                    instance = InstName(args),
                    version = Py.S(args.Get("version")),
                    username = args.Get("username"),
                    memory_mb = args.Get("memory_mb"),
                }, ct, out string launchTask, "启动游戏");
            }

            // ── 会话内工具 ──
            case "update_plan":
            {
                var items = new List<(string Title, string Status)>();
                foreach (object? raw in Py.ListOr(args.Get("items")))
                {
                    PyDict item = Py.D(raw);
                    string title = Py.S(item.Get("title"));
                    if (title.Length == 0)
                    {
                        continue;
                    }
                    items.Add((title, Py.S(Py.Or(item.Get("status"), "pending"))));
                }
                PlanChanged?.Invoke(items);
                (int done, int total) = AiUiText.PlanProgress(items);
                return PyDict.Of("ok", true, "done", (long)done, "total", (long)total);
            }

            case "ask_user":
            {
                if (AskUser == null)
                {
                    return PyDict.Of("ok", false, "error", "no_ui", "message", "界面没有接 ask_user");
                }
                PyDict normalized = Tools.NormalizeAskArgs(call.args ?? new PyDict());
                object? answer = await AskUser(normalized).ConfigureAwait(false);
                return Tools.NormalizeAskAnswer(Py.ListOr(normalized.Get("questions")), answer);
            }

            case "remember":
                MemoryAdd(Py.S(args.Get("text")));
                return PyDict.Of("ok", true, "memories", (long)AiMemory.All().Count);

            case "forget":
                return PyDict.Of("ok", true,
                    "forgotten", (long)AiMemory.Forget(Py.S(args.Get("text"))),
                    "memories", (long)AiMemory.All().Count);

            // ── 产物回读 / 子代理 ──
            case "read_artifact":
                return Artifacts.ReadArtifact(args.Get("artifact_id"), Py.Or(args.Get("offset"), 0L), Py.Or(args.Get("limit"), 200L));

            case "dispatch_subagent":
            {
                if (Subagent == null)
                {
                    return ToolErr(call.name, "unknown_tool", "当前会话没有子代理能力", "");
                }
                if (Py.Strip(Py.S(args.Get("task"))).Length == 0)
                {
                    throw new ValueError("缺少子任务描述(task 字段)");
                }
                return await Subagent(args, ct).ConfigureAwait(false);
            }

            default:
                // 兜底:MCP 工具（mcp_<server>_<tool>）路由到对应的外部 server
                if (Mcp.IsMcpTool(call.name))
                {
                    return Mcp.RouteCall(McpClients, call.name, args);
                }
                return ToolErr(call.name, "unknown_tool", "没有这个工具: " + call.name, "");
        }
    }

    /// <summary>起一个后台任务并把 id 交给宿主做任务卡(照 Windows 版 BackendApi.StartTask 语义)。</summary>
    private object Start(string method, object args, CancellationToken ct, out string taskId, string label)
    {
        object? result = _api.CallAsync<object>(method, args, ct).ConfigureAwait(false).GetAwaiter().GetResult();
        PyDict row = Py.D(result);
        taskId = Py.S(Py.Or(row.Get("task_id"), row.Get("id")));
        if (taskId.Length > 0)
        {
            // method 就是工具名(install_mod / install_game / download_java / launch_game …),任务卡要它
            TaskStarted?.Invoke(taskId, label, method);
        }
        return PyDict.Of("ok", true, "task_id", taskId, "message", label + " 已开始,可以在「下载任务」里看进度");
    }

    private PyDict LauncherState()
    {
        string def = Py.S(Config.CONFIG.Get("default_instance", "default"));
        var instances = new List<object>();
        foreach (object raw in Instances.ListInstances())
        {
            PyDict item = Py.D(raw);
            string name = Py.S(Py.Or(item.Get("name"), item.Get("id")));
            if (name.Length == 0)
            {
                continue;
            }
            var inst = new Instance(name);
            instances.Add(PyDict.Of(
                "name", name,
                "versions", (long)inst.InstalledIds().Count,
                "mods", (long)Mods.ListInstanceMods(inst).Count,
                "is_default", name == def));
        }
        return PyDict.Of(
            "default_instance", def,
            "instances", instances,
            "data_root", Utils.ROOT,
            "game_root", Instances.RootName(),
            "java_default", Py.S(Config.CONFIG.Get("default_java", "")),
            "memory_mb", Py.Or(Config.CONFIG.Get("default_memory_mb"), 0L));
    }

    private PyDict Diagnose(PyDict args)
    {
        var inst = new Instance(InstName(args));
        PyDict report = Crash.AnalyzeInstance(inst, "", true, Py.S(args.Get("version")));
        var reasons = new List<object>();
        foreach (object raw in Py.ListOr(report.Get("reasons")).Take(6))
        {
            PyDict item = Py.D(raw);
            reasons.Add(PyDict.Of("code", Py.S(item.Get("code")), "title", Py.S(item.Get("title")), "extra", Py.S(item.Get("extra"))));
        }
        return PyDict.Of(
            "instance", inst.name,
            "is_crash", Py.Truthy(report.Get("is_crash")),
            "headline", Py.S(report.Get("headline")),
            "summary", Py.S(report.Get("summary")),
            "exit_code", report.Get("exit_code"),
            "exit_hint", Py.S(report.Get("exit_hint")),
            "reasons", reasons,
            "has_latest_log", Py.Truthy(report.Get("has_latest")),
            "has_crash_report", Py.Truthy(report.Get("has_crash")),
            "output_tail", Py.S(report.Get("output_tail")));
    }

    private PyDict InspectMod(PyDict args)
    {
        var inst = new Instance(InstName(args));
        string modsDir = Mods.ContentDir(inst, "mods");
        string file = Mods._mod_file_at(modsDir, args.Get("filename"));
        if (!LocalFs.IsFile(file))
        {
            throw new ModError(Py.Fmt("模组文件不存在: {0}", args.Get("filename")));
        }
        PyDict info = Conflict.InspectJar(file);
        var copy = new PyDict(info) { ["filename"] = new PPath(file).Name };
        return copy;
    }

    /// <summary>实例 config 目录(写操作只允许落在这里,照 Windows 版 ExtraTools.FsRoots 的白名单)。</summary>
    private static string ConfigDir(PyDict args, string instance)
    {
        var inst = new Instance(instance);
        string dir = PPath.Join(inst.path, "config");
        Directory.CreateDirectory(PPath.Abs(dir));
        return dir;
    }

    private static string ResolveConfigPath(PyDict args, out string configDir)
    {
        string instance = Py.Or(args.Get("instance"), Config.CONFIG.Get("default_instance", "default")) as string ?? "default";
        configDir = ConfigDir(args, instance);
        string rel = Py.Strip(Py.S(args.Get("path"))).Replace('\\', '/').TrimStart('/');
        if (rel.Length == 0 || rel.Contains(".."))
        {
            throw new ValueError("配置路径只能在该实例的 config 目录下");
        }
        string full = PPath.Norm(PPath.Join(configDir, rel));
        if (!full.StartsWith(PPath.Norm(configDir), StringComparison.Ordinal))
        {
            throw new ValueError("配置路径只能在该实例的 config 目录下");
        }
        return full;
    }

    private List<object> ListConfigs(PyDict args)
    {
        string instance = InstName(args);
        string dir = ConfigDir(args, instance);
        string prefix = Py.S(args.Get("prefix"));
        var rows = new List<object>();
        foreach (string file in LocalFs.RglobFiles(dir).OrderBy(x => x, StringComparer.Ordinal).Take(200))
        {
            string rel = new PPath(file).RelativeTo(dir).ToString().Replace('\\', '/');
            if (prefix.Length > 0 && !rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            rows.Add(PyDict.Of("path", rel, "bytes", LocalFs.Size(file)));
        }
        return rows;
    }

    private string ReadConfig(PyDict args, out string path)
    {
        path = ResolveConfigPath(args, out _);
        if (!LocalFs.IsFile(path))
        {
            throw new FileNotFoundError(Py.Fmt("配置文件不存在: {0}", args.Get("path")));
        }
        return Crash.ReadText(path, 200_000);
    }

    private PyDict WriteConfig(PyDict args)
    {
        string path = ResolveConfigPath(args, out _);
        string content = Py.S(args.Get("content"));
        if (LocalFs.IsFile(path))
        {
            LocalFs.Copy2(path, path + ".bak");
        }
        Directory.CreateDirectory(PPath.Abs(new PPath(path).Parent.ToString()));
        File.WriteAllText(PPath.Abs(path), content);
        return PyDict.Of("ok", true, "path", path, "bytes", (long)content.Length, "backup", path + ".bak");
    }

    private void MemoryAdd(string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        AiMemory.Add(text);
    }
}
