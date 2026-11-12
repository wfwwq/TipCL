// M4-H P5:AI 工具表(OpenAI 兼容的 function schema)。
//
// 与 Windows 版的关系:名字、描述、参数形状**与参考版保持一致**(照 src/TipCL/Core/Launcher/Ai/AiData.cs 的
// TOOL_SCHEMAS_JSON),只去掉 Linux 侧没有落地能力的条目(tune_jvm / backup_save / dispatch_subagent /
// read_artifact 等),不写「点了会报未知工具」的假条目 —— 与 P7 的「逐项判定」同一口径。
// 解析入口与 Windows 相同:PyJson.Loads(见 Tools.Schemas)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.Core.Launcher.Ai;

/// <summary>工具表:名称、schema、文案、参数解析、执行。</summary>
public static class Tools
{
    /// <summary>
    /// 原始 schema(JSON 数组,OpenAI function calling 形状)。分成多段拼接只是为了可读:
    /// 每段都是完整的 `{...}` 对象字面量,拼起来仍是合法 JSON(由 Tools_SchemasAreValidJson 测试锁定)。
    /// </summary>
    private static readonly string[] SchemaChunks =
    {
        """
        [
        {"type":"function","function":{"name":"ask_user","description":"向用户弹出结构化选择题。需要用户选实例、加载器、搜到多个结果、冲突留哪个时必须用这个,不要只在文字里问。界面会自动加「其他」让用户自己填。没选完之前不要调用 install_*。用户选完后你必须立刻再调对应工具,不能结束。","parameters":{"type":"object","properties":{"title":{"type":"string","description":"可选,整组题的小标题"},"prompt":{"type":"string","description":"单题时的问题"},"allow_multiple":{"type":"boolean","description":"单题时是否可多选,默认 false"},"options":{"type":"array","description":"单题选项。每项可以是字符串,或 {id,label}","items":{"type":"object","properties":{"id":{"type":"string"},"label":{"type":"string"}}}},"questions":{"type":"array","description":"多题时用。每题 {id,prompt,allow_multiple,options:[{id,label}]},至少 2 个选项","items":{"type":"object"}}}}}},
        {"type":"function","function":{"name":"get_launcher_state","description":"查看实例、已装版本、Java、模组数量等当前状态","parameters":{"type":"object","properties":{}}}},
        {"type":"function","function":{"name":"list_instances","description":"列出全部实例","parameters":{"type":"object","properties":{}}}},
        {"type":"function","function":{"name":"list_installed_versions","description":"列出某实例已安装的游戏版本","parameters":{"type":"object","properties":{"instance":{"type":"string","description":"实例名,空则用默认"}}}}},
        {"type":"function","function":{"name":"search_versions","description":"搜索可下载的 Minecraft 版本号","parameters":{"type":"object","properties":{"query":{"type":"string","description":"如 1.20.1 或 25w"},"kind":{"type":"string","description":"release / snapshot / all"}},"required":["query"]}}},
        """,
        """
        {"type":"function","function":{"name":"search_mods","description":"搜索模组(支持中文名)。同一轮用户请求只调用一次;搜完必须 ask_user 让用户选,禁止换词再搜。","parameters":{"type":"object","properties":{"query":{"type":"string"},"source":{"type":"string","description":"全部 / Modrinth / CurseForge"}},"required":["query"]}}},
        {"type":"function","function":{"name":"search_modpacks","description":"搜索整合包(支持中文名)。同一轮只调用一次,搜完 ask_user,禁止换词再搜。","parameters":{"type":"object","properties":{"query":{"type":"string"},"source":{"type":"string","description":"全部 / Modrinth / CurseForge"}},"required":["query"]}}},
        {"type":"function","function":{"name":"search_content","description":"搜索光影 / 资源包 / 数据包(支持中文名)。装之前先用它拿 slug 或 id。","parameters":{"type":"object","properties":{"kind":{"type":"string","description":"shader / resourcepack / datapack"},"query":{"type":"string"},"source":{"type":"string","description":"全部 / Modrinth / CurseForge"}},"required":["kind","query"]}}},
        {"type":"function","function":{"name":"search_worlds","description":"搜索地图存档(CurseForge 世界)","parameters":{"type":"object","properties":{"query":{"type":"string"},"source":{"type":"string"}},"required":["query"]}}},
        {"type":"function","function":{"name":"list_mods","description":"列出实例已装模组(含禁用)","parameters":{"type":"object","properties":{"instance":{"type":"string"}}}}},
        {"type":"function","function":{"name":"get_java_list","description":"列出已安装 Java","parameters":{"type":"object","properties":{}}}},
        """,
        """
        {"type":"function","function":{"name":"install_game","description":"真正开始下载/安装 Minecraft。用户已选定版本后必须调用这个,否则不会下载。纯原版 loader 填「无」。","parameters":{"type":"object","properties":{"version":{"type":"string","description":"如 1.20.1"},"loader":{"type":"string","description":"无 / Fabric / Forge / Quilt / NeoForge。纯原版必须填 无"},"loader_version":{"type":"string"},"instance":{"type":"string"}},"required":["version"]}}},
        {"type":"function","function":{"name":"install_mod","description":"安装模组。优先传搜索结果里的 slug 或 id","parameters":{"type":"object","properties":{"name":{"type":"string","description":"显示名或 slug"},"instance":{"type":"string"},"source":{"type":"string"},"slug":{"type":"string"},"id":{"type":"string","description":"CurseForge 数字 id"}},"required":["name"]}}},
        {"type":"function","function":{"name":"install_modpack","description":"安装整合包。建议先 create_instance","parameters":{"type":"object","properties":{"name":{"type":"string"},"instance":{"type":"string"},"source":{"type":"string"},"slug":{"type":"string"},"id":{"type":"string"}},"required":["name"]}}},
        {"type":"function","function":{"name":"install_shader","description":"安装光影包","parameters":{"type":"object","properties":{"name":{"type":"string"},"instance":{"type":"string"},"source":{"type":"string"},"slug":{"type":"string"}},"required":["name"]}}},
        {"type":"function","function":{"name":"install_resourcepack","description":"安装资源包","parameters":{"type":"object","properties":{"name":{"type":"string"},"instance":{"type":"string"},"source":{"type":"string"},"slug":{"type":"string"}},"required":["name"]}}},
        {"type":"function","function":{"name":"install_datapack","description":"安装数据包","parameters":{"type":"object","properties":{"name":{"type":"string"},"instance":{"type":"string"},"source":{"type":"string"},"slug":{"type":"string"}},"required":["name"]}}},
        {"type":"function","function":{"name":"install_world","description":"安装地图存档到实例的 saves","parameters":{"type":"object","properties":{"name":{"type":"string"},"instance":{"type":"string"},"source":{"type":"string"},"slug":{"type":"string"},"id":{"type":"string","description":"CurseForge 数字 id"}},"required":["name"]}}},
        """,
        """
        {"type":"function","function":{"name":"create_instance","description":"新建隔离实例,装整合包前建议先建","parameters":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}}},
        {"type":"function","function":{"name":"delete_instance","description":"删除整个实例(危险)","parameters":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}}},
        {"type":"function","function":{"name":"delete_mod","description":"删除模组文件","parameters":{"type":"object","properties":{"filename":{"type":"string"},"instance":{"type":"string"}},"required":["filename"]}}},
        {"type":"function","function":{"name":"disable_mod","description":"禁用模组(改名为 .disabled,可恢复)","parameters":{"type":"object","properties":{"filename":{"type":"string"},"instance":{"type":"string"}},"required":["filename"]}}},
        {"type":"function","function":{"name":"enable_mod","description":"重新启用已禁用模组","parameters":{"type":"object","properties":{"filename":{"type":"string"},"instance":{"type":"string"}},"required":["filename"]}}},
        {"type":"function","function":{"name":"download_java","description":"下载 Adoptium Java","parameters":{"type":"object","properties":{"major":{"type":"string","description":"8 / 11 / 17 / 21"}},"required":["major"]}}},
        {"type":"function","function":{"name":"launch_game","description":"启动游戏。不填则用默认实例和已装版本","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string"},"username":{"type":"string"},"memory_mb":{"type":"integer"}}}}},
        """,
        """
        {"type":"function","function":{"name":"diagnose_launch","description":"分析启动失败:规则扫 latest.log 和崩溃报告","parameters":{"type":"object","properties":{"instance":{"type":"string"}}}}},
        {"type":"function","function":{"name":"scan_mod_conflicts","description":"扫描模组冲突、缺依赖、加载器不匹配","parameters":{"type":"object","properties":{"instance":{"type":"string"}}}}},
        {"type":"function","function":{"name":"inspect_mod","description":"解析单个模组 jar 的元数据","parameters":{"type":"object","properties":{"filename":{"type":"string"},"instance":{"type":"string"}},"required":["filename"]}}},
        {"type":"function","function":{"name":"list_mod_configs","description":"列出实例 config 下的配置文件","parameters":{"type":"object","properties":{"instance":{"type":"string"},"prefix":{"type":"string","description":"子目录或文件名前缀"}}}}},
        {"type":"function","function":{"name":"read_mod_config","description":"读取某个配置文件","parameters":{"type":"object","properties":{"path":{"type":"string","description":"相对 config/ 的路径"},"instance":{"type":"string"}},"required":["path"]}}},
        {"type":"function","function":{"name":"write_mod_config","description":"写入配置文件(会先备份 .bak)","parameters":{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string","description":"完整文件内容"},"instance":{"type":"string"}},"required":["path","content"]}}},
        {"type":"function","function":{"name":"update_plan","description":"维护本次任务的待办清单并在界面展示。三步以上的活先出计划再动手;每次更新都传完整清单,状态只能是 pending / in_progress / completed。","parameters":{"type":"object","properties":{"items":{"type":"array","description":"完整待办列表","items":{"type":"object","properties":{"title":{"type":"string","description":"这一步要干什么"},"status":{"type":"string","description":"pending / in_progress / completed"}}}}},"required":["items"]}}},
        {"type":"function","function":{"name":"remember","description":"把一条长期有效的事实记到记忆里(用户偏好、常用实例、踩过的坑)","parameters":{"type":"object","properties":{"text":{"type":"string","description":"要记住的内容"}},"required":["text"]}}},
        {"type":"function","function":{"name":"forget","description":"忘掉一条之前记住的内容(按关键词匹配)","parameters":{"type":"object","properties":{"text":{"type":"string","description":"要忘掉的关键词"}},"required":["text"]}}},
        {"type":"function","function":{"name":"read_artifact","description":"回读之前被存盘的超长工具结果。传结果摘要里给的文件名","parameters":{"type":"object","properties":{"artifact_id":{"type":"string","description":"如 20261112-133000-ab12cd34.txt"},"offset":{"type":"integer","description":"起始行(0 起)"},"limit":{"type":"integer","description":"读取行数,默认 200"}},"required":["artifact_id"]}}},
        {"type":"function","function":{"name":"dispatch_subagent","description":"派发一个独立的子任务给只读子代理去跑(查状态、搜资料、读日志、扫冲突这类),子代理跑完把结论带回来。多步骤的调研活用它,别把主对话搞成一长串中间步骤","parameters":{"type":"object","properties":{"task":{"type":"string","description":"要它干什么,一句话说清"},"context":{"type":"string","description":"相关背景(实例名、版本、已知线索),可选"}},"required":["task"]}}}
        ]
        """,
    };

    private static List<object>? _schemas;

    /// <summary>全部工具 schema(解析结果,供 Client.ChatStream 的 tools 参数用)。</summary>
    public static List<object> Schemas
    {
        get
        {
            List<object>? cached = _schemas;
            if (cached == null)
            {
                var sb = new StringBuilder();
                foreach (string chunk in SchemaChunks)
                {
                    sb.Append(Py.Strip(chunk)).Append('\n');
                }
                object parsed = PyJson.Loads(sb.ToString());
                cached = Py.ListOr(parsed);
                _schemas = cached;
            }
            return cached;
        }
    }

    /// <summary>工具名 → 中文标签(照 Windows 版 Tools._TOOL_LABELS)。</summary>
    private static readonly Dictionary<string, string> Labels = new()
    {
        { "ask_user", "请你选一下" },
        { "get_launcher_state", "查启动器状态" },
        { "list_instances", "查实例列表" },
        { "list_installed_versions", "查已装版本" },
        { "search_versions", "搜版本" },
        { "search_mods", "搜模组" },
        { "search_modpacks", "搜整合包" },
        { "search_content", "搜内容" },
        { "search_worlds", "搜地图" },
        { "list_mods", "查模组列表" },
        { "get_java_list", "查 Java 列表" },
        { "install_game", "装游戏" },
        { "install_mod", "装模组" },
        { "install_modpack", "装整合包" },
        { "install_shader", "装光影" },
        { "install_resourcepack", "装资源包" },
        { "install_datapack", "装数据包" },
        { "install_world", "装地图" },
        { "download_java", "下载 Java" },
        { "launch_game", "启动游戏" },
        { "create_instance", "新建实例" },
        { "delete_instance", "删除实例" },
        { "delete_mod", "删除模组" },
        { "disable_mod", "禁用模组" },
        { "enable_mod", "启用模组" },
        { "diagnose_launch", "诊断启动" },
        { "scan_mod_conflicts", "扫模组冲突" },
        { "inspect_mod", "检查模组" },
        { "list_mod_configs", "查模组配置" },
        { "read_mod_config", "读配置" },
        { "write_mod_config", "改配置" },
        { "update_plan", "更新计划" },
        { "remember", "记住" },
        { "forget", "忘掉" },
        { "read_artifact", "读文件" },
        { "dispatch_subagent", "派发子任务" },
    };

    /// <summary>带参数时更好读的标签模板(照 Windows 版 Tools._FMT)。</summary>
    private static readonly Dictionary<string, string> Formats = new()
    {
        { "install_game", "安装游戏 {version} {loader} → {instance}" },
        { "install_mod", "安装模组 {name} → {instance}" },
        { "install_modpack", "安装整合包 {name} → {instance}" },
        { "install_shader", "安装光影 {name} → {instance}" },
        { "install_resourcepack", "安装资源包 {name} → {instance}" },
        { "install_datapack", "安装数据包 {name} → {instance}" },
        { "install_world", "安装地图 {name} → {instance}" },
        { "download_java", "下载 Java {major}" },
        { "launch_game", "启动 {version} @ {instance}" },
        { "create_instance", "新建实例 {name}" },
        { "delete_instance", "删除实例 {name}(不可恢复)" },
        { "delete_mod", "删除模组 {filename} @ {instance}" },
        { "disable_mod", "禁用模组 {filename} @ {instance}" },
        { "enable_mod", "启用模组 {filename} @ {instance}" },
        { "write_mod_config", "改配置 {path} @ {instance}" },
        { "search_mods", "搜模组「{query}」" },
        { "search_modpacks", "搜整合包「{query}」" },
        { "search_content", "搜内容「{query}」" },
        { "search_worlds", "搜地图「{query}」" },
        { "search_versions", "搜版本「{query}」" },
        { "inspect_mod", "检查模组 {filename}" },
        { "read_mod_config", "读配置 {path}" },
    };

    /// <summary>是否写操作(风险分级非只读)。</summary>
    public static bool IsWriteTool(string name) => ToolMeta.Get(name) is { @readonly: false };

    /// <summary>工具的中文标签;args 非空时按模板带出参数。</summary>
    public static string Label(string name, PyDict? args = null)
    {
        string key = name ?? "";
        if (args != null && Formats.TryGetValue(key, out string? fmt))
        {
            string filled = FillTemplate(fmt, args);
            if (filled.Length > 0)
            {
                return filled;
            }
        }
        return Labels.TryGetValue(key, out string? label) ? label : key;
    }

    private static string FillTemplate(string fmt, PyDict args)
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
                // 参数缺失就整条回落成通用标签(不要产出「安装模组 {name}」这种半成品)
                return "";
            }
            result = result.Substring(0, open) + value + result.Substring(close + 1);
            at = open + value.Length;
        }
        // 模板里没被替换的可选占位(如 {loader})直接删掉
        return result.Replace("  ", " ").Trim();
    }

    /// <summary>从 schema 里取工具名(供页面显示与测试断言)。</summary>
    public static string SchemaName(object schema)
    {
        if (schema is not PyDict outer)
        {
            return "";
        }
        PyDict function = Py.D(outer.Get("function"));
        return Py.S(function.Get("name"));
    }

    /// <summary>全部工具名(声明顺序)。</summary>
    public static List<string> Names()
    {
        var names = new List<string>();
        foreach (object schema in Schemas)
        {
            string name = SchemaName(schema);
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }
        return names;
    }

    /// <summary>
    /// 按对话内容挑选要暴露给模型的工具(照 Windows 版 Tools.SelectToolSchemas):
    /// 常驻 CORE_TOOLS 一定带;按用户用词命中 KEYWORD_TOOLS;写操作只在「像在下命令」时带。
    /// </summary>
    public static List<object> SelectToolSchemas(List<object> messages, string? userText = null, bool forceAll = false)
    {
        var allowed = new HashSet<string>(AiData.CORE_TOOLS);
        string text = userText ?? LastUserText(messages);
        foreach ((string[] keys, string[] extra) in AiData.KEYWORD_TOOLS)
        {
            foreach (string key in keys)
            {
                if (text.Contains(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string name in extra)
                    {
                        allowed.Add(name);
                    }
                    break;
                }
            }
        }
        bool action = System.Text.RegularExpressions.Regex.IsMatch(text, AiData.ACTION_RE);
        bool question = System.Text.RegularExpressions.Regex.IsMatch(text, AiData.QUESTION_RE);
        // 上一步的工具结果被存成文件了(太长):这时才把 read_artifact 给它,平时不占位
        foreach (object m in messages)
        {
            string content = m is PyDict d ? Py.S(d.Get("content")) : "";
            if (content.Contains("cache/ai_results/") || content.Contains("[结果过长"))
            {
                allowed.Add("read_artifact");
                break;
            }
        }
        var result = new List<object>();
        foreach (object schema in Schemas)
        {
            string name = SchemaName(schema);
            if (name.Length == 0)
            {
                continue;
            }
            if (forceAll || allowed.Contains(name))
            {
                result.Add(schema);
                continue;
            }
            // 写操作不在关键词表里时:只有明确下命令才带(纯提问不给写工具,减少误装)
            if (!ToolMeta.Get(name)!.@readonly && action && !question)
            {
                result.Add(schema);
            }
        }
        return result;
    }

    private static string LastUserText(List<object> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            PyDict message = Py.D(messages[i]);
            if (Py.S(message.Get("role")) == "user")
            {
                return Py.S(message.Get("content"));
            }
        }
        return "";
    }

    /// <summary>
    /// 解析模型给的工具参数(照 Windows 版 Tools.ParseArgs,含宽容模式):
    /// 优先严格 JSON;失败时按 `key: value` / `key=value` 粗扫(模型有时不写引号)。
    /// </summary>
    public static (PyDict? args, string err) ParseArgs(object? raw, string name = "")
    {
        if (raw == null)
        {
            return (new PyDict(), "");
        }
        if (raw is PyDict dict)
        {
            return (dict, "");
        }
        string text = Py.Strip(Py.S(raw));
        if (text.Length == 0)
        {
            return (new PyDict(), "");
        }
        try
        {
            object parsed = PyJson.Loads(text);
            if (parsed is PyDict ok)
            {
                return (ok, "");
            }
            return (null, "工具参数必须是 JSON 对象");
        }
        catch (System.Exception)
        {
            PyDict? loose = LooseKv(text);
            if (loose != null)
            {
                return (loose, "");
            }
            return (null, "工具参数不是合法 JSON");
        }
    }

    /// <summary>宽容解析:`{key: 值, other: "值"}` 这类没写引号的参数。</summary>
    private static PyDict? LooseKv(string text)
    {
        string body = Py.Strip(text, "{} \n\r\t");
        if (body.Length == 0)
        {
            return new PyDict();
        }
        var result = new PyDict();
        int found = 0;
        foreach (string part in Py.Split(body, ","))
        {
            string item = Py.Strip(part);
            int sep = item.IndexOf(':');
            if (sep <= 0)
            {
                sep = item.IndexOf('=');
            }
            if (sep <= 0)
            {
                continue;
            }
            string key = Py.Strip(Py.Strip(item.Substring(0, sep)), "\"'");
            string value = Py.Strip(item.Substring(sep + 1));
            if (key.Length == 0)
            {
                continue;
            }
            value = Py.Strip(Py.Strip(value), "\"'");
            if (value.Length == 0)
            {
                continue;
            }
            if (long.TryParse(value, out long l))
            {
                result[key] = l;
            }
            else if (value == "true" || value == "false")
            {
                result[key] = value == "true";
            }
            else
            {
                result[key] = value;
            }
            found++;
        }
        return found > 0 ? result : null;
    }

    /// <summary>是不是 ask_user(宿主据此决定「不进步骤组」)。</summary>
    public static bool IsAskTool(string name) => name == "ask_user";

    /// <summary>
    /// 从「可能是字典」的对象上取键(照 Windows 版 Agent.DGet)。
    /// Conflict.cs 是原样搬来的,里面按 Windows 的写法调 `Agent.DGet` —— Linux 侧 Agent 是会话类,
    /// 这个纯函数放这里更合适,搬来的文件只改了调用点。
    /// </summary>
    public static object? DGet(object? d, string key)
    {
        if (d is PyDict dict)
        {
            return dict.Get(key);
        }
        throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
    }

    /// <summary>
    /// 把模型给的 ask_user 参数归一成宿主能直接渲染的题面(照 Windows 版 Tools.NormalizeAskArgs):
    /// 支持单题(prompt/options)与多题(questions);选项可以是字符串或 {id,label};
    /// 自动补「其他」;选项不足 2 个时补「先不选」。
    /// </summary>
    public static PyDict NormalizeAskArgs(PyDict? args)
    {
        args ??= new PyDict();
        object? raw = args.Get("questions");
        List<object> questions = raw is List<object> list ? list : new List<object>();
        if (questions.Count == 0 && !Py.Truthy(raw))
        {
            questions.Add(PyDict.Of(
                "id", Py.Or(args.Get("id"), "q1"),
                "prompt", Py.Or(args.Get("prompt"), args.Get("title")),
                "allow_multiple", Py.Truthy(args.Get("allow_multiple")),
                "options", Py.Or(args.Get("options"), new List<object>())));
        }

        var normalized = new List<object>();
        for (int i = 0; i < questions.Count; i++)
        {
            PyDict question = Py.D(questions[i]);
            var options = new List<object>();
            int n = -1;
            foreach (object? item in Py.ListOr(question.Get("options")))
            {
                n++;
                string id;
                string label;
                if (item is string s)
                {
                    id = "opt_" + n;
                    label = Py.Strip(s);
                }
                else if (item is PyDict opt)
                {
                    id = Py.Str(Py.Or(opt.Get("id"), "opt_" + n));
                    label = Py.Strip(Py.Str(Py.Or(opt.Get("label"), opt.Get("name"), id)));
                }
                else
                {
                    continue;
                }
                if (label.Length > 0)
                {
                    options.Add(PyDict.Of("id", id, "label", label));
                }
            }
            if (!options.Any(o => Py.S(Py.D(o).Get("id")) == "other"
                    || Py.RStrip(Py.S(Py.D(o).Get("label")), "。.") is "其他" or "其它"))
            {
                options.Add(PyDict.Of("id", "other", "label", "其他"));
            }
            if (options.Count < 2)
            {
                options.Insert(0, PyDict.Of("id", "skip", "label", "先不选"));
            }
            normalized.Add(PyDict.Of(
                "id", Py.Str(Py.Or(question.Get("id"), "q" + (i + 1))),
                "prompt", Py.Str(Py.Or(question.Get("prompt"), question.Get("title"))),
                "allow_multiple", Py.Truthy(question.Get("allow_multiple")),
                "options", options));
        }

        if (normalized.Count == 0)
        {
            normalized.Add(PyDict.Of("id", "q1", "prompt", Py.Str(Py.Or(args.Get("prompt"), "请选择")),
                "allow_multiple", false,
                "options", new List<object> { PyDict.Of("id", "skip", "label", "先不选"), PyDict.Of("id", "other", "label", "其他") }));
        }
        return PyDict.Of("title", Py.S(args.Get("title")), "questions", normalized);
    }

    /// <summary>
    /// 把用户的选择归一成模型能读的结果(照 Windows 版 Tools.NormalizeAskAnswer)。
    /// `answered` 形状(宿主提供):`{ 题 id: {picked:[{id,label}]|ids:[...]|other_text:"..."} }`
    /// 或直接的字符串(用户自己填的)。返回 `{answers:{...}, summary:"题: 选项(补充:xxx)\n..."}`。
    /// </summary>
    public static PyDict NormalizeAskAnswer(List<object>? questions, object? answered)
    {
        if (answered == null || answered is string)
        {
            return PyDict.Of("answers", new PyDict(), "summary", Py.Str(answered), "text", Py.Str(answered));
        }
        PyDict given = Py.D(answered);
        // 宿主可能把选择装在 answers 里
        if (given.Get("answers") is PyDict inner)
        {
            given = inner;
        }

        var byId = new Dictionary<string, PyDict>();
        foreach (object? item in questions ?? new List<object>())
        {
            PyDict question = Py.D(item);
            string qid = Py.S(question.Get("id"));
            if (qid.Length > 0)
            {
                byId[qid] = question;
            }
        }

        var answers = new PyDict();
        var summary = new List<string>();
        foreach (KeyValuePair<string, object?> kv in given)
        {
            string qid = kv.Key;
            var ids = new List<string>();
            var labels = new List<string>();
            string other = "";
            if (kv.Value is PyDict pick)
            {
                other = Py.Strip(Py.S(kv.Value is PyDict p1 ? Py.Or(p1.Get("other_text"), p1.Get("other")) : ""));
                foreach (object? p in Py.ListOr(Py.Or(pick.Get("picked"), pick.Get("ids"))))
                {
                    if (p is PyDict one)
                    {
                        string id = Py.S(one.Get("id"));
                        ids.Add(id);
                        labels.Add(Py.Str(Py.Or(one.Get("label"), id)));
                    }
                    else if (p != null)
                    {
                        ids.Add(Py.Str(p));
                    }
                }
            }
            else if (kv.Value is List<object> picked)
            {
                foreach (object? p in picked)
                {
                    if (p is PyDict one)
                    {
                        string id = Py.S(one.Get("id"));
                        ids.Add(id);
                        labels.Add(Py.Str(Py.Or(one.Get("label"), id)));
                    }
                    else if (p != null)
                    {
                        ids.Add(Py.Str(p));
                    }
                }
            }
            else if (kv.Value != null)
            {
                ids.Add(Py.Str(kv.Value));
            }

            PyDict question = byId.TryGetValue(qid, out PyDict? q) ? q : new PyDict();
            var optionLabels = new Dictionary<string, string>();
            foreach (object? raw in Py.ListOr(question.Get("options")))
            {
                PyDict option = Py.D(raw);
                optionLabels[Py.S(option.Get("id"))] = Py.S(option.Get("label"));
            }
            if (labels.Count < ids.Count)
            {
                labels = ids.Select(id => optionLabels.TryGetValue(id, out string? label) && label.Length > 0 ? label : id).ToList();
            }
            if (other.Length > 0 && !ids.Contains("other"))
            {
                ids.Add("other");
                labels.Add(optionLabels.TryGetValue("other", out string? otherLabel) && otherLabel.Length > 0 ? otherLabel : "其他");
            }
            answers[qid] = PyDict.Of("ids", ids.Cast<object>().ToList(), "labels", labels.Cast<object>().ToList(), "other_text", other);

            string prompt = Py.S(Py.Or(question.Get("prompt"), qid));
            string read = string.Join(", ", labels.Where(x => x.Length > 0));
            if (read.Length == 0)
            {
                read = "(未选)";
            }
            if (other.Length > 0)
            {
                read += "(补充:" + other + ")";
            }
            summary.Add(prompt + ": " + read);
        }
        return PyDict.Of("answers", answers, "summary", string.Join("\n", summary));
    }

    /// <summary>异常 → 工具错误码(照 Windows 版 Tools.ClassifyException 的口径)。</summary>
    public static string ClassifyException(System.Exception ex) => ex switch
    {
        FileNotFoundError => "not_found",
        PermissionError => "permission_denied",
        TimeoutError => "timeout",
        TaskCancelled => "cancelled",
        TypeError => "bad_arguments",
        ValueError => "bad_arguments",
        System.IO.FileNotFoundException => "not_found",
        System.IO.DirectoryNotFoundException => "not_found",
        System.IO.IOException => "io_error",
        HTTPError => "upstream_error",
        NetError => "upstream_error",
        _ => "unknown",
    };

    /// <summary>错误码 → 小白能懂的话(照 Windows 版 Tools._ERROR_READABLE)。</summary>
    private static readonly Dictionary<string, string> ErrorReadable = new()
    {
        { "bad_arguments", "工具参数不合适" },
        { "not_found", "目标不存在" },
        { "permission_denied", "没有权限执行" },
        { "timeout", "执行超时" },
        { "cancelled", "已被用户停止" },
        { "io_error", "读写文件失败" },
        { "upstream_error", "上游服务出错" },
        { "unknown", "执行失败" },
    };

    public static string ErrorText(string code)
        => ErrorReadable.TryGetValue(code ?? "", out string? text) ? text : ErrorReadable["unknown"];

    /// <summary>工具失败时回给模型的内容(JSON,让模型能读出「发生了什么 + 下一步」)。</summary>
    public static string ErrorPayload(System.Exception ex, string name, string? code = null)
    {
        string key = code ?? ClassifyException(ex);
        return PyJson.Dumps(PyDict.Of(
            "ok", false,
            "error", key,
            "message", ErrorText(key),
            "detail", Py.ExcStr(ex),
            "tool", name));
    }

    /// <summary>
    /// 这个工具调用可能会改到本机的哪些路径（照 Windows 版 Tools.AffectedPaths）。
    /// 检查点（Checkpoint.Snapshot）在写操作之前按这份清单做快照，回退时才知道要还原什么。
    /// 认不出的工具返回空清单 = 不做检查点（只有真的会落盘的写工具才列在这里）。
    /// </summary>
    public static List<string> AffectedPaths(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        string instName = Py.S(Py.Or(args.Get("instance"), instance));
        string root;
        try
        {
            root = Instances.GetInstancePath(instName.Length > 0 ? instName : Py.Or(Config.CONFIG.Get("default_instance", "default"), "default"));
        }
        catch (Exception)
        {
            return new List<string>();
        }
        string mods = PPath.Join(root, "mods");
        switch (name)
        {
            case "write_mod_config":
            {
                string rel = Py.LStrip(Py.S(args.Get("path")).Replace("\\", "/"), "/");
                if (rel.Length == 0 || new PPath(rel).Parts.Contains("..") || rel.Contains(":"))
                {
                    return new List<string>();
                }
                return new List<string> { PPath.Join(root, "config", rel) };
            }
            case "delete_mod":
            case "enable_mod":
            case "disable_mod":
            {
                string file = Py.S(args.Get("filename"));
                // 启停会把文件名加/去 .disabled:两个名字都要能还原
                return new List<string> { PPath.Join(mods, file), PPath.Join(mods, file + ".disabled") };
            }
            case "delete_instance":
                return new List<string> { root };
            case "create_instance":
                try
                {
                    return new List<string> { PPath.Join(new PPath(root).Parent.ToString(), Instances.UniqueInstanceName(Py.S(Py.Or(args.Get("name"), "游戏")))) };
                }
                catch (Exception)
                {
                    return new List<string>();
                }
            case "install_mod":
                return new List<string> { mods };
            case "install_shader":
                return new List<string> { PPath.Join(root, "shaderpacks") };
            case "install_resourcepack":
                return new List<string> { PPath.Join(root, "resourcepacks") };
            case "install_datapack":
                return new List<string> { PPath.Join(root, "datapacks") };
            case "install_world":
                return new List<string> { PPath.Join(root, "saves") };
            case "install_modpack":
                return new List<string> { root };
            case "install_game":
                return new List<string> { new Instance(instName.Length > 0 ? instName : null).VersionsDir() };
            case "download_java":
                try
                {
                    return new List<string> { Config.CONFIG.java_dir };
                }
                catch (Exception)
                {
                    return new List<string>();
                }
            default:
                return new List<string>();
        }
    }
}
