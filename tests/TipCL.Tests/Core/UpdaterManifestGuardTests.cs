// M4-H R22 收尾(task-21):把「清单里的占位 sha256 不能再是一个"看起来合法"的值」钉成守卫用例。
//
// 背景(R22 抓到的真洞,PROGRESS.md:1094-1097 / docs/PARITY.md:658-659):
//   `Updater.ValidSha256` 只校验 `\A[0-9a-fA-F]{64}\z`,所以当年那个占位值
//   `"sha256": "000…0"`(64 个 0)**完全合法** —— 客户端会照常弹「发现新版本」,一路走到下载才失败,
//   "格式不对就拒绝"这层兜底在最常见的占位值上失效了。修法是把占位值写成**格式非法**的形状
//   (`REPLACE-WITH-THE-REAL-SHA256`),当天也补了真机证据(T2)。
//
// 但"占了位的那份清单长什么样"是个**仓库里的事实**,不是代码里的事实:任何人手一滑把 sha256 换回
// 64 个 0(或真去填一个没发布过的 sha),行为就会悄悄退回旧洞。所以本文件直接读**仓库里那份
// `update-linux.json`**,把它当输入喂给 `Updater.Check`,断言它被当场拒绝。
//
// 与 `Core/UpdaterTests.cs` 的分工:那个文件测 `Updater` 的逻辑(注入假清单);
// 本文件测**仓库那份真清单**与逻辑的一致性(发版纪律的守卫)。
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

public class UpdaterManifestGuardTests
{
    /// <summary>仓库根目录:从测试输出目录往上找那份清单(仓库布局:`linux/update-linux.json`)。</summary>
    private static string ManifestPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "update-linux.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException(
            "从 " + AppContext.BaseDirectory + " 往上找不到 update-linux.json —— 这个守卫用例必须能读到仓库里那份清单");
    }

    private static (string Version, string Sha256, string Url, string Notes) ReadManifest()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        JsonElement root = doc.RootElement;
        static string Str(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) ? v.GetString() ?? "" : "";
        return (Str(root, "version"), Str(root, "sha256"), Str(root, "url"), Str(root, "notes"));
    }

    /// <summary>
    /// 清单里的 sha256 必须是**格式非法**的占位值(在还没发版、清单的 `version` 都没超过
    /// 客户端 `AppInfo.APP_VERSION` 的时候),而且**绝不允许**是"64 个 0"这种能通过格式校验的假值。
    /// </summary>
    [Fact]
    public void CommittedManifest_PlaceholderShaMustBeFormatIllegal_NotAValueThatRuns()
    {
        (string version, string sha, _, string notes) = ReadManifest();

        // 1) 全零占位值这条老洞不许回来:它格式合法,会被当成真 sha 接受,一路走到下载才失败。
        Assert.False(
            sha.Length == 64 && sha.All(c => c == '0'),
            "update-linux.json 的 sha256 又变回 64 个 0 了 —— 这正好是 R22 修掉的那个洞(格式合法 ⇒ 兜底失效)");

        // 2) 只要这份清单**还没有发过版**(它自己都不比客户端新 ⇒ `Newer` 为假),
        //    那份 sha256 就只能是格式非法值:填一个 64 位十六进制的"真值"等于伪造发布状态
        //    (那个 release 资产并不存在),而且它会被 `ValidSha256` 当成可用更新接受。
        //    注意判据是 `Newer` 为假、不是"版本号相等":当前清单写 `0.1.0-m3` 而客户端
        //    `AppInfo.APP_VERSION` 已经到 `1.1.0`,两者并不相等,但同样属于"没发过版"。
        if (!Updater.Newer(version, AppInfo.APP_VERSION))
        {
            // 先证明这条断言不是摆设:`ValidSha256` 对"64 个 0"这种假值**是**返回 true 的,
            // 所以下面那句 Assert.False 真的拦得住它(当年就是这里漏了)。
            Assert.True(
                Updater.ValidSha256(new string('0', 64)),
                "`ValidSha256` 竟然不认 64 个 0 了 —— 那 R22 那个洞的前提变了,请复核这条守卫到底在守什么");

            Assert.False(
                Updater.ValidSha256(sha),
                $"清单的 version({version})还没超过客户端版本({AppInfo.APP_VERSION}),说明它没发过版,"
                + "sha256 却是一个格式合法的 64 位十六进制值 —— 要么它是个假的真值(伪造发布状态),"
                + "要么该真发一次版并把 version 抬到客户端之上");
        }

        // 3) 占位值自己要在 notes 里把道理写清楚(避免下一位当成"忘了填")
        Assert.False(string.IsNullOrWhiteSpace(notes));
    }

    /// <summary>
    /// 仓库里那份清单**原样**喂给 `Updater.Check()`:它**绝不能**让客户端以为"有更新"。
    ///
    /// 实测现状(这条也把真实行为记下来,免得下一位误解):仓库里那份清单写的是 `0.1.0-m3`,
    /// 而客户端 `AppInfo.APP_VERSION` 已经是 `1.1.0` —— 清单**比客户端旧**,`Newer` 为假
    /// ⇒ `has_update=false`、`ok=true`、文案是「已是最新版本」。
    /// 也就是说这份未发版的占位清单是**中性**的:既不伪造更新,也不会误报"被拒绝"。
    /// (要真正闭环自动更新,得发一个**版本号高于 `1.1.0`** 的包并填上它自己的 sha256。)
    /// </summary>
    [Fact]
    public void CommittedManifest_AsIs_NeverClaimsAnUpdateIsAvailable()
    {
        (string version, string sha, string url, string notes) = ReadManifest();
        Assert.False(string.IsNullOrEmpty(version));

        Updater.JsonFetcher = _ => PyDict.Of("version", version, "notes", notes, "url", url, "sha256", sha);
        try
        {
            PyDict res = Updater.Check();

            Assert.False(Py.Truthy(res["has_update"]));
            Assert.Equal(AppInfo.APP_VERSION, Py.S(res["current"]));
        }
        finally
        {
            Updater.JsonFetcher = null;
        }
    }

    /// <summary>
    /// R22 那条安全网的回归:把仓库里那份清单的 `sha256` **配上一个更高的 version**
    /// (模拟"有人抬了版本号、却忘了换掉占位 sha"—— 这正是当年踩进去的路径),
    /// 客户端必须**当场拒绝**并说明原因,而不是"发现新版本"→ 下载 → 失败。
    /// 真机证据:T2(截图 `scratch/update-e2e2/t2-10-toast-02.png`)。
    /// </summary>
    [Fact]
    public void CommittedManifest_WithABumpedVersion_IsRefusedByCheck()
    {
        (string version, string sha, string url, string notes) = ReadManifest();

        // 先确认清单现值确实"不比客户端新"(未发版占位),否则本用例的模拟前提不成立
        Assert.False(Updater.Newer(version, AppInfo.APP_VERSION),
            "清单的 version 已经比客户端新了 —— 那它就该配一个真 sha,先去看发版流程");

        Updater.JsonFetcher = _ => PyDict.Of("version", "99.9.9", "notes", notes, "url", url, "sha256", sha);
        try
        {
            PyDict res = Updater.Check();

            Assert.False(Py.Truthy(res["has_update"]), "占位 sha 竟然被当成了可用更新");
            Assert.False(Py.Truthy(res["ok"]), "占位 sha 没有被判成'拒绝'");
            Assert.Contains("SHA-256", Py.S(res["message"]));
        }
        finally
        {
            Updater.JsonFetcher = null;
        }
    }
}
