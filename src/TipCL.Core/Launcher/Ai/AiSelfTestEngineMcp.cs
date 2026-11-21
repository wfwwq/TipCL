// AI-engine-mcp:--ai-selftest 里本包的那几项(放独立文件,AiSelfTest.cs 只加一行调用)。
// 不联网、不碰用户数据:调度用假执行体,钩子注册后立即注销,规则匹配直接喂字典(不改 config),
// MCP 只走 Dispatch 的协议面(不起端口)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

internal static class AiSelfTestEngineMcp
{
    public static async Task VerifyAsync(Action<string, bool> check)
    {
        try
        {
            // 调度:相邻只读并行成组,写/删/启动/ask_user 单独成组,拼回去顺序不变
            var calls = new List<ToolCall>
            {
                new("a", "list_mods", new PyDict()),
                new("b", "search_mods", new PyDict()),
                new("c", "install_mod", new PyDict()),
                new("d", "list_instances", new PyDict()),
                new("e", "delete_mod", new PyDict()),
                new("f", "ask_user", new PyDict()),
            };
            List<List<ToolCall>> groups = Scheduler.Group(calls);
            check("调度:只读并行、写删独占(" + string.Join("|", groups.Select(g => string.Join(",", g.Select(c => c.id)))) + ")",
                groups.Count == 5 && groups[0].Count == 2 && groups.SelectMany(g => g).Select(c => c.id).SequenceEqual(new[] { "a", "b", "c", "d", "e", "f" }));
            string[] results = await Scheduler.RunGroupAsync(groups[0], async c =>
            {
                await Task.Delay(c.id == "a" ? 60 : 1).ConfigureAwait(false);
                return c.id;
            }).ConfigureAwait(false);
            check("调度:结果按原顺序回填", results.SequenceEqual(new[] { "a", "b" }));

            // 钩子:改写参数生效、钩子报错进 errors 不抛
            string hookName = "selftest-" + Guid.NewGuid().ToString("N");
            Hooks.Register(new Hook(hookName, (tool, args) =>
            {
                if (tool == "boom") throw new InvalidOperationException("钩子炸了");
                var copy = new PyDict(args) { ["selftest"] = true };
                return copy;
            }));
            try
            {
                (PyDict rewritten, List<string> none) = Hooks.RunBefore("list_mods", new PyDict());
                (_, List<string> errors) = Hooks.RunBefore("boom", new PyDict());
                check("钩子:改写参数", Py.Truthy(rewritten.Get("selftest")) && none.Count == 0);
                check("钩子:报错只进 errors", errors.Count == 1 && errors[0].StartsWith(hookName, StringComparison.Ordinal));
            }
            finally
            {
                Hooks.Unregister(hookName);
            }

            // 规则按参数匹配(照参考版 RuleContentFromInput 的键序 + NormPathSpelling)
            PyDict rule = PyDict.Of("tool", "write_mod_config", "behavior", "allow", "content", "Sodium/Options.JSON", "instance", "");
            check("规则:限定参数命中",
                PermissionRules.Matches(rule, "write_mod_config", PyDict.Of("path", "sodium/options.json"), "")
                && !PermissionRules.Matches(rule, "write_mod_config", PyDict.Of("path", "other.json"), ""));
            check("规则:删除类无限定 allow 不生效",
                PermissionRules.Pick(new List<object?> { PyDict.Of("tool", "delete_mod", "behavior", "allow") }, "delete_mod", PyDict.Of("filename", "a.jar"), "", "delete") == "");

            // 本机 MCP Server 协议面
            string init = McpServer.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}") ?? "";
            check("MCP Server:initialize 协议版本", init.Contains(McpServer.ProtocolVersion, StringComparison.Ordinal));
            string list = McpServer.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}") ?? "";
            check("MCP Server:tools/list 动态读工具表", list.Contains("\"list_mods\"", StringComparison.Ordinal) && !list.Contains("\"ask_user\"", StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            check("引擎/MCP 自检异常:" + Py.ExcStr(ex), false);
        }
    }
}
