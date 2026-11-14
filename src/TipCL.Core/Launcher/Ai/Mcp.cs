// M4-H（AI 服务层）：MCP 工具接入（照 Windows 版 Launcher/Ai/Mcp.cs 搬运）。
//
// 配置键与参考版一致:config 的 ai_mcp_servers = [{name, command, args[], env{}}]。
// 工具命名与路由规则也照抄:外部工具在模型眼里叫 mcp_<server>_<tool>，调用时按最长的 server 名前缀反查，
// 这样 server 名里带下划线也不会被切错。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>按配置连接 MCP server，并把它们的工具并进工具表。</summary>
public static class Mcp
{
    /// <summary>解析 ai_mcp_servers（名字只允许字母数字 - _，缺 name/command 的条目跳过并记轨迹）。</summary>
    public static List<PyDict> ServerConfigs(PyDict? settings)
    {
        var rows = new List<PyDict>();
        if ((settings ?? new PyDict()).Get("ai_mcp_servers") is not List<object> list)
        {
            return rows;
        }
        foreach (object item in list)
        {
            if (item is not PyDict raw)
            {
                continue;
            }
            string name = Py.Strip(Py.S(raw.Get("name")));
            string command = Py.Strip(Py.S(raw.Get("command")));
            if (name.Length == 0 || command.Length == 0)
            {
                continue;
            }
            bool okName = PyDifflib.CodePoints(name).All(cp =>
                (cp < 65536 && char.IsLetterOrDigit((char)cp)) || cp == '-' || cp == '_');
            if (!okName)
            {
                Trace.Record("mcp_config_skipped", null, "", name, null, null, null, "reason", "server 名只允许字母数字-_");
                continue;
            }
            PyDict? env = null;
            if (raw.Get("env") is PyDict rawEnv)
            {
                env = new PyDict();
                foreach (KeyValuePair<string, object> kv in rawEnv)
                {
                    env[kv.Key] = Py.Str(kv.Value);
                }
            }
            rows.Add(PyDict.Of(
                "name", name,
                "command", command,
                "args", raw.Get("args") as List<object> ?? new List<object>(),
                "env", env));
        }
        return rows;
    }

    /// <summary>连上配置里的全部 server（单个失败不影响其它，失败记轨迹）。</summary>
    public static List<McpClient> ConnectServers(PyDict? settings)
    {
        var clients = new List<McpClient>();
        foreach (PyDict cfg in ServerConfigs(settings))
        {
            var client = new McpClient(
                (string)cfg["name"],
                (string)cfg["command"],
                cfg["args"] as List<object>,
                cfg["env"] as PyDict);
            try
            {
                client.Connect();
                clients.Add(client);
            }
            catch (Exception exc)
            {
                client.Close();
                Trace.Record("mcp_connect_failed", null, "", (string)cfg["name"], exc);
            }
        }
        return clients;
    }

    /// <summary>全部关掉（一轮结束时调）。</summary>
    public static void CloseAll(List<McpClient>? clients)
    {
        foreach (McpClient client in clients ?? new List<McpClient>())
        {
            client.Close();
        }
    }

    /// <summary>把各 server 的工具转成 OpenAI function schema（名字 mcp_<server>_<tool>）。</summary>
    public static List<object> McpToolSchemas(List<McpClient>? clients)
    {
        var schemas = new List<object>();
        foreach (McpClient client in clients ?? new List<McpClient>())
        {
            List<object> tools;
            try
            {
                tools = client.ListTools();
            }
            catch (Exception exc)
            {
                Trace.Record("mcp_list_tools_failed", null, "", client.name, exc);
                continue;
            }
            foreach (object item in tools)
            {
                if (item is not PyDict tool || !Py.Truthy(tool.Get("name")))
                {
                    continue;
                }
                string full = "mcp_" + client.name + "_" + Py.Str(tool["name"]);
                schemas.Add(PyDict.Of(
                    "type", "function",
                    "function", PyDict.Of(
                        "name", full,
                        "description", Py.Slice(Py.Str(Py.Or(tool.Get("description"), full)), null, 800),
                        "parameters", tool.Get("inputSchema") is PyDict schema
                            ? schema
                            : PyDict.Of("type", "object", "properties", new PyDict()))));
            }
        }
        return schemas;
    }

    /// <summary>是不是 MCP 工具名。</summary>
    public static bool IsMcpTool(string? name) => (name ?? "").StartsWith("mcp_", StringComparison.Ordinal);

    /// <summary>按名字把调用路由到对应的 server；名字不对就抛 McpError。</summary>
    public static string RouteCall(List<McpClient>? clients, string name, PyDict? args)
    {
        if (!IsMcpTool(name))
        {
            return "";
        }
        string rest = name.Substring(4);
        // server 名可能自带下划线:按名字长的优先匹配，避免切错
        foreach (McpClient client in (clients ?? new List<McpClient>()).OrderByDescending(c => c.name.Length))
        {
            string prefix = client.name + "_";
            if (rest.StartsWith(prefix, StringComparison.Ordinal))
            {
                return client.CallTool(rest.Substring(prefix.Length), args);
            }
        }
        throw new McpError("没有叫 " + name + " 的 MCP 工具(server 可能已崩)");
    }
}
