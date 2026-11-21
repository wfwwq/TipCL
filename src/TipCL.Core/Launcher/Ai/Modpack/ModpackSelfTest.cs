// AI2-modpack:--ai-selftest 的本包检查项(纯逻辑,不联网、不写盘)。AiSelfTest.VerifyAsync 末尾调一行。
// 检查项名字是会打印出来的文字,走 I18nCore.Tr。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

internal static class ModpackSelfTest
{
    private static string Tr(string key) => I18nCore.Tr(key);

    internal static void Verify(Action<string, bool, string> check)
    {
        try
        {
            List<string> names = Tools.Names();
            var missing = ModpackTools.Names.Where(n => !names.Contains(n) || ToolMeta.Get(n) == null).ToList();
            check(Tr("整合包工具已注册(导出 / 许可检查 / 造包 / 批量安装)"), missing.Count == 0, string.Join(",", missing));
            check(Tr("整合包写工具要确认且不给「本对话都允许」"),
                ToolMeta.Get("export_modpack") is { @readonly: false }
                && ToolMeta.Get("install_mods_batch") is { @readonly: false }
                && ToolMeta.Get("check_modpack_distribution") is { @readonly: true }
                && ToolMeta.Get("plan_modpack") is { @readonly: true }
                && !ToolRunner.AllowsAlways("export_modpack") && !ToolRunner.AllowsAlways("install_mods_batch"), "");
            check(Tr("mrpack 下载地址只认规范允许的域名"),
                ModpackDistribution.AllowedMrpackUrl("https://cdn.modrinth.com/data/AANobbMI/versions/x/sodium.jar")
                && !ModpackDistribution.AllowedMrpackUrl("https://edge.forgecdn.net/files/1/2/x.jar")
                && !ModpackDistribution.AllowedMrpackUrl("http://cdn.modrinth.com/x.jar"), "");

            var plan = PyDict.Of(
                "name", "Demo", "version", "1.0.0", "summary", "s", "mc_version", "1.20.1", "loader", "fabric", "loader_version", "0.15.11",
                "linked", new List<object> { PyDict.Of("path", "mods/a.jar", "size", 10L, "sha1", "aa", "sha512", "bb", "url", "https://cdn.modrinth.com/a.jar") });
            PyDict index = ModpackExport.MrpackIndex(plan);
            PyDict file0 = Py.D(Py.ListOr(index.Get("files"))[0]);
            PyDict deps = Py.D(index.Get("dependencies"));
            check(Tr("modrinth.index.json 字段符合 Modrinth 规范"),
                Py.Int(index.Get("formatVersion")) == 1 && Py.S(index.Get("game")) == "minecraft"
                && Py.S(Py.D(file0.Get("hashes")).Get("sha512")) == "bb" && Py.ListOr(file0.Get("downloads")).Count == 1
                && Py.Int(file0.Get("fileSize")) == 10 && Py.S(deps.Get("minecraft")) == "1.20.1" && Py.S(deps.Get("fabric-loader")) == "0.15.11",
                PyJson.Dumps(index));

            List<string> cats = ModpackPlanner.ThemeCategories("轻量科技 + 优化"); // i18n:ignore(自检样例输入,模拟用户原话)
            check(Tr("主题识别分类(轻量科技 + 优化 → technology, optimization;默认 small)"),
                cats.SequenceEqual(new[] { "technology", "optimization" }) && ModpackPlanner.SizeTarget("", "轻量科技") == 8, string.Join(",", cats)); // i18n:ignore(自检样例输入)
        }
        catch (Exception ex)
        {
            check(Tr("整合包自检异常") + ":" + Py.ExcStr(ex), false, "");
        }
    }
}
