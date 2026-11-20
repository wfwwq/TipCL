// AI-new-features:--ai-selftest 的本包检查项(纯逻辑,不联网、不写盘)。AiSelfTest.VerifyAsync 末尾调一行。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

internal static class NewFeaturesSelfTest
{
    internal static void Verify(Action<string, bool, string> check)
    {
        try
        {
            List<string> names = Tools.Names();
            var missing = NewFeatureEffects.TOOL_NAMES.Where(n => !names.Contains(n) || ToolMeta.Get(n) == null).ToList();
            check("新功能工具已注册(升级/预检/服务器/光影/诊断报告)", missing.Count == 0, string.Join(",", missing));
            check("新功能写工具非只读、删除类判删除",
                ToolMeta.Get("apply_instance_upgrade") is { @readonly: false, risk: "high" }
                && ToolMeta.Get("remove_server") is { @readonly: false, side_effect: "delete" }
                && ToolMeta.Get("export_diagnostic_report") is { @readonly: false }
                && ToolMeta.Get("plan_instance_upgrade") is { @readonly: true }
                && ToolMeta.Get("preflight_check") is { @readonly: true }, "");
            check("危险类不给「本对话都允许」", !ToolRunner.AllowsAlways("remove_server") && !ToolRunner.AllowsAlways("apply_instance_upgrade"), "");

            PyDict ping = ServerPing.Parse("mc.test", 25565,
                "{\"version\":{\"name\":\"1.21.1\",\"protocol\":767},\"players\":{\"online\":3,\"max\":20},\"description\":{\"text\":\"§aHello \",\"extra\":[{\"text\":\"World\"}]}}", 42);
            check("服务器状态解析(人数/版本/MOTD)",
                Py.Int(ping.Get("online")) == 3 && Py.S(ping.Get("version")) == "1.21.1" && Py.S(ping.Get("motd")) == "Hello World", PyJson.Dumps(ping));
            using (var ms = new MemoryStream())
            {
                ServerPing.WriteVarInt(ms, 300);
                byte[] bytes = ms.ToArray();
                int off = 0;
                check("VarInt 编解码", ServerPing.ReadVarInt(bytes, ref off) == 300 && off == 2, "");
            }

            string low = ShaderAdvisor.HardwareTier(PyDict.Of("ram_mb", 8192L, "gpu", "Intel(R) UHD Graphics 620", "vram_mb", 128L), out _);
            string high = ShaderAdvisor.HardwareTier(PyDict.Of("ram_mb", 32768L, "gpu", "NVIDIA GeForce RTX 4070", "vram_mb", 12288L), out _);
            check("硬件分档(核显=低配 / RTX4070+32G=高配)", low == "low" && high == "high", low + "/" + high);
            check("光影分档(PTGI=高 / Lite=低)",
                ShaderAdvisor.ItemTier(PyDict.Of("name", "SEUS PTGI"), "shader") == "high"
                && ShaderAdvisor.ItemTier(PyDict.Of("name", "Sildur's Vibrant Lite"), "shader") == "low", "");

            string red = DiagnosticReport.Redact("--accessToken eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefgh C:\\Users\\Steve\\AppData x@y.com", new[] { "Steve" });
            check("诊断报告脱敏", !red.Contains("eyJ") && !red.Contains("Steve") && !red.Contains("x@y.com"), red);

            string prompt = AiPreflight.BuildFixPrompt(PyDict.Of("instance", "demo", "version", "1.20.1", "summary", "发现 1 个",
                "items", new List<object> { PyDict.Of("level", "error", "title", "缺少前置模组", "detail", "sodium 需要 fabric-api", "fix", "装 fabric-api") }));
            check("预检交给 AI 的开场上下文", prompt.Contains("demo") && prompt.Contains("缺少前置模组") && prompt.Contains("preflight_check"), "");

            var addArgs = PyDict.Of("address", "mc.example.com:25566", "name", "测试服");
            PyDict? preview = NewFeatureEffects.Preview("add_server", addArgs, "");
            check("添加服务器有改前改后预览", preview != null && Preview.Flatten(preview).Any(l => l.Contains("mc.example.com:25566")), "");
        }
        catch (Exception ex)
        {
            check("新功能自检异常:" + Py.ExcStr(ex), false, "");
        }
    }
}
