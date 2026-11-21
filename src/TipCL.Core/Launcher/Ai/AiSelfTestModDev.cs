// AI2-moddev-mcp:--ai-selftest 里本包的几项(放独立文件,AiSelfTest.cs 只加一行调用)。 // i18n:ignore-file(自检项名字是开发者看的输出)
// 不联网、不碰用户数据、不启动游戏:jar 在临时目录里现造,MCP 只走 Dispatch 的协议面。
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

internal static class AiSelfTestModDev
{
    public static async Task VerifyAsync(Action<string, bool> check)
    {
        string dir = Path.Combine(Path.GetTempPath(), "tipcl-selftest-moddev-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            string jar = Path.Combine(dir, "demo.jar");
            using (var zip = ZipFile.Open(jar, ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
            {
                w.Write("{\"schemaVersion\":1,\"id\":\"demo\",\"version\":\"1.0.0\",\"depends\":{\"minecraft\":\"~1.20.1\",\"fabric-api\":\"*\"}}");
            }
            ModJarInfo info = ModJarInfo.Read(jar);
            check("模组开发:读 fabric.mod.json(id / 版本 / MC 约束 / 依赖)",
                info.IsMod && info.Id == "demo" && info.Version == "1.0.0" && info.McVersion == "~1.20.1" && info.Depends.Count == 1);
            check("模组开发:加载器不匹配会拒(fabric jar → forge 实例)",
                !info.CompatibleWith("forge", "1.20.1", out _) && info.CompatibleWith("quilt", "1.20.1", out _));
            check("模组开发:MC 版本约束判定",
                ModJarInfo.McMatches("~1.20.1", "1.20.4") == true && ModJarInfo.McMatches("[1.20.1,1.21)", "1.21") == false
                && ModJarInfo.McMatches(">=1.20 <1.21", "24w14a") == null);
            string bad = Path.Combine(dir, "bad.jar");
            File.WriteAllText(bad, "not a zip");
            check("模组开发:非 zip 的 jar 被识别", !ModJarInfo.Read(bad).ValidZip);
            PyDict missing = await ModDevTasks.StatusAsync("dev-nope", 5, 0).ConfigureAwait(false);
            check("模组开发:get_task_status 查不到如实报 not_found", Py.S(missing.Get("error")) == "not_found");
            string list = McpServer.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}") ?? "";
            check("MCP Server:对外开放模组开发测试工具",
                list.Contains("\"install_local_mod\"", StringComparison.Ordinal) && list.Contains("\"run_test_launch\"", StringComparison.Ordinal)
                && list.Contains("\"get_task_status\"", StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            check("模组开发自检异常:" + Py.ExcStr(ex), false);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception)
            {
            }
        }
    }
}
