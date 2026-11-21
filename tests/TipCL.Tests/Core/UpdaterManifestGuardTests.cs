// M4-H R22 收尾(task-21):把「清单里的占位 sha256 不能再是一个"看起来合法"的值」钉成守卫用例。
//
// 背景(R22 抓到的真洞,PROGRESS.md:1094-1097 / docs/PARITY.md:658-659):
//   `Updater.ValidSha256` 只校验 `\A[0-9a-fA-F]{64}\z`,所以当年那个占位值
//   `"sha256": "000...0"`(64 个 0)**完全合法** —— 客户端会照常弹「发现新版本」,一路走到下载才失败,
//   "格式不对就拒绝"这层兜底在最常见的占位值上失效了。修法是把占位值写成**格式非法**的形状
//   (`REPLACE-WITH-THE-REAL-SHA256`),当天也补了真机证据(T2)。
//
// 但"占了位的那份清单长什么样"是个**仓库里的事实**,不是代码里的事实:任何人手一滑把 sha256 换回
// 64 个 0(或真去填一个没发布过的 sha),行为就会悄悄退回旧洞。所以本文件直接读**仓库里那份
// `update-linux.json`**,把它当输入喂给 `Updater.Check`,断言它被当场拒绝。
//
// ────────────────────────────────────────────────────────────────────────────────────────────
// R31-B2 修订(2026-11-15,用户拍板;成员 rel-guard 执行)——把「什么时候允许真 sha」这半边
// 改成与发版纪律自洽。**注意:这不是"为了过测试而放宽",是旧断言与已定稿的发版口径自相矛盾。**
//
// 修订前的判据(原文):`if (!Updater.Newer(version, AppInfo.APP_VERSION)) { sha256 必须格式非法 }`
//   ⇒ 只要清单 `version` **不比客户端新**(`Newer(version, APP_VERSION)` 为假),sha 就必须是格式非法值。
//   ⇒ R31 要发的版本就是 **1.2.0**(= `AppInfo.APP_VERSION`,用户 2026-11-15 定稿),而发版日清单必须写
//     `"version": "1.2.0"` + 那支 AppImage 的**真 sha256**;此时 `Newer("1.2.0", "1.2.0")` 为假
//     (**相等不算"更新"**)⇒ 旧规则会把"如实回填真值"判成**伪造发布状态** ⇒ **这条守卫禁止了它自己
//     为之设计的那个发版动作**,Linux 全量套件(487)在发版日会当场变红。
//     它注释里给的唯一出路(「要真闭环自动更新,得发一个版本号高于 1.2.0 的包」)在当前流程里**不可达**:
//     发版日发的就是 1.2.0,清单 version 天然等于客户端版本。
//   ⇒ 用户拍板:**修订守卫规则,两份清单都填真值**(`update.json` / `update-linux.json`)。
//
// 修订后的规则(逐条,本文件按这四条实现):
//   R-a(原样保留):`sha256` **不许是 64 个 0** —— R22 那个洞不许回来。
//   R-b(原样保留):`notes` **不许为空**。
//   R-c(收紧 + 新增结构校验):**当 `sha256` 格式合法时**,必须同时满足
//     1. `version` **不早于**本构建 —— `!Updater.Newer(AppInfo.APP_VERSION, version)`,
//        即"清单指向当前版本**或更新**的版本"。发版日 `version == AppInfo.APP_VERSION` 属**合规**状态。
//     2. `url` 非空,且形如 `https://github.com/wfwwq/TipCL/releases/download/v<version>/<资产名>`
//        (新增:挡住"sha 和 version 都改了、url 却指到别处/别的 tag 目录"这种改一半的清单 ——
//         客户端就是按这个 url 下载的,指错了要么 404、要么装错版本的包)。
//   R-d(保留旧洞口,只把边界从"≤"收到"<"):**清单 `version` 严格早于本构建**
//        (`Updater.Newer(AppInfo.APP_VERSION, version)` 为真,即"还没发版的占位状态")时,
//        `sha256` **必须是格式非法值** —— 继续拦住"填一个 64 位十六进制假值"这条 R22 老洞(伪造发布状态)。
//
// 一句话:修订只动了**一处**边界 —— `version == AppInfo.APP_VERSION` 从"算未发版、必须留占位"变成
// "算已发版、允许真值";两端语义都没变(`<` 仍必须占位、`>` 仍允许真值)。
// ────────────────────────────────────────────────────────────────────────────────────────────
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

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class UpdaterManifestGuardTests
{
    /// <summary>R22 那个洞的形状:64 个 0。格式合法 ⇒ 会被 `ValidSha256` 放行 ⇒ 兜底失效。</summary>
    private const string AllZeroSha = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>合成的**格式非法**占位值(R22 修法里给仓库清单用的就是它这个形状)。第三个用例拿它当输入,与仓库清单当前状态解耦。</summary>
    private const string PlaceholderSha = "REPLACE-WITH-THE-REAL-SHA256";

    /// <summary>发版资产 url 的固定前缀:`https://github.com/wfwwq/TipCL/releases/download/v{version}/`(后面接资产名)。</summary>
    private const string ReleaseUrlPrefix = "https://github.com/wfwwq/TipCL/releases/download/v";

    /// <summary>仓库根那份清单:从测试输出目录往上找(仓库布局:`update-linux.json` 在仓库根)。</summary>
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
    /// 仓库里那份清单必须在**两种状态**下都自洽(两种状态都要能过,这就是 R31-B2 修订的目的):
    /// <br/>① **未发版占位态**(`version` 严格早于本构建)⇒ `sha256` 必须是**格式非法**的占位值,
    ///     绝不许是"64 个 0"这种能过格式校验的假值(R-a + R-d);
    /// <br/>② **已发版态**(`version` 不早于本构建,例如发版日 `1.2.0 == AppInfo.APP_VERSION` 配真 sha256)
    ///     ⇒ 允许真值,但 `url` 必须指向**同一个 version** 的 release 资产(R-c)。
    /// <br/>本用例读的是**仓库里那份真清单** ⇒ 两种状态都成立 = 发版前"留占位"与发版日"回填真值"**都不会判红**。
    /// </summary>
    [Fact]
    public void CommittedManifest_IsConsistentInBothStates_PlaceholderBeforeRelease_RealShaAtRelease()
    {
        (string version, string sha, string url, string notes) = ReadManifest();

        // ── R-a(原样保留):全零占位值这条老洞不许回来。
        //    它格式合法,会被当成真 sha 接受,一路走到下载才失败。
        Assert.False(
            sha.Length == 64 && sha.All(c => c == '0'),
            "update-linux.json 的 sha256 又变回 64 个 0 了 —— 这正好是 R22 修掉的那个洞(格式合法 ⇒ 兜底失效)");

        // ── R-b(原样保留):两种状态都要在 notes 里把话说清楚(避免下一位当成"忘了填"):
        //    占位态要说清"这是占位",已发版态要说清这版更新了什么。
        Assert.False(
            string.IsNullOrWhiteSpace(notes),
            "update-linux.json 的 notes 不许为空 —— 它是给读清单的人看的");

        // 先证明下面那两条「sha 必须格式非法」的断言不是摆设:`ValidSha256` 对"64 个 0"这种假值**是**
        // 返回 true 的,所以 R-a / R-d 真的拦得住它(当年就是这里漏了)。
        Assert.True(
            Updater.ValidSha256(AllZeroSha),
            "`ValidSha256` 竟然不认 64 个 0 了 —— 那 R22 那个洞的前提变了,请复核这条守卫到底在守什么");

        bool shaIsFormatLegal = Updater.ValidSha256(sha);
        bool versionIsStrictlyBehindBuild = Updater.Newer(AppInfo.APP_VERSION, version);

        // ── R-d(保留洞口,边界从"≤"收到"<"):version 严格早于本构建 ⇒ 还没发过版 ⇒ sha 只能是格式非法值。
        if (versionIsStrictlyBehindBuild)
        {
            Assert.False(
                shaIsFormatLegal,
                $"清单的 version({version})**严格早于**本构建({AppInfo.APP_VERSION}),说明它还没发版,"
                + "sha256 却是一个格式合法的 64 位十六进制值 —— 要么它是个假的真值(伪造发布状态:那个 release 资产"
                + "并不存在,而 `ValidSha256` 会把它当可用更新接受),要么该真发一次版、把 version 抬到本构建之上。"
                + "(注:修订前这条的判据是「不比本构建新」,连 version **相等**都算未发版,于是禁止了 version=1.2.0 的发版回填)");
        }

        // ── R-c(收紧 + 新增结构校验):sha 格式合法 ⇒ 清单必须自称「当前版本或更新的版本」,且 url 指对。
        if (shaIsFormatLegal)
        {
            // R-c-1:version **不早于**本构建(相等合规 —— 发版日填的就是 AppInfo.APP_VERSION)。
            Assert.False(
                versionIsStrictlyBehindBuild,
                $"清单的 sha256 已经是格式合法的真值,但 version({version})**早于**本构建({AppInfo.APP_VERSION})"
                + " —— 这是「sha 填真了、version 还是旧的」的改一半状态:客户端根本不会认这个更新,而 url 还会指着旧 tag");

            // R-c-2:url 非空,且形如 https://github.com/wfwwq/TipCL/releases/download/v<version>/<资产名>
            Assert.False(
                string.IsNullOrWhiteSpace(url),
                $"清单的 version({version})已指向发版状态(sha256 是格式合法的真值),url 却是空的 —— 客户端拿不到下载地址,更新链断在第一步");

            string expectedPrefix = ReleaseUrlPrefix + version + "/";
            Assert.True(
                url.StartsWith(expectedPrefix, StringComparison.Ordinal),
                $"清单的 url 必须形如 `{ReleaseUrlPrefix}<version>/<资产名>`,即前缀必须是 `{expectedPrefix}`"
                + $"(此刻清单 version = {version}),实际是 `{url}` —— url 里的 tag 目录必须与清单 version 是同一个版本,"
                + "否则客户端会下到别的版本(或直接 404)");
            Assert.True(
                url.Length > expectedPrefix.Length,
                $"清单的 url(`{url}`)只到 tag 目录、没带资产名 —— 形如 `{expectedPrefix}<资产名>`");
        }
    }

    /// <summary>
    /// 仓库里那份清单**原样**喂给 `Updater.Check()`:它**绝不能**让客户端以为「有更新」。
    ///
    /// R31-B2 修订:本用例要求**两种状态下都成立**——
    /// <br/>· 占位态(`version` 比客户端旧,如 `0.1.0-m3`):`Newer` 为假 ⇒ `has_update=false`、`ok=true`、文案「已是最新版本」;
    /// <br/>· 已发版态(`version` = `1.2.0` = `AppInfo.APP_VERSION`,配真 sha256):`Newer("1.2.0", "1.2.0")`
    ///    **仍为假**(相等不算"更新")⇒ 同样 `has_update=false`、文案「已是最新版本」。
    ///
    /// 读法:这份清单是**中性**的 —— 既不伪造更新,也不会误报「被拒绝」。跑在 1.2.0 上的客户端读到自己的
    /// release 清单,正确行为本来就是「已是最新版本」(它已经是最新的了)。
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
    /// R22 那条安全网的回归:拿**合成的格式非法占位值**(`REPLACE-WITH-THE-REAL-SHA256`)**配上一个更高的 version**
    /// (模拟"有人抬了版本号、却忘了换掉占位 sha"—— 这正是当年踩进去的路径),客户端必须**当场拒绝**并说明原因,
    /// 而不是「发现新版本」→ 下载 → 失败。真机证据:T2(截图 `scratch/update-e2e2/t2-10-toast-02.png`)。
    ///
    /// R31-B2 修订:本用例**不再拿仓库清单里的 sha256 当输入**(那样一旦发版日把仓库清单回填成真 sha,
    /// 本用例的模拟前提就崩了 —— 它会变成"用一个真 sha 去测占位拒绝",测的东西根本不是 R22 那条),
    /// 改用合成的占位常量 ⇒ 无论仓库那份清单是**占位**还是**已回填**,这条防御都被守住。
    /// </summary>
    [Fact]
    public void SyntheticPlaceholderSha_WithABumpedVersion_IsRefusedByCheck()
    {
        (string Version, string Sha256, string Url, string Notes) manifest = ReadManifest();

        // 前提断言(按新语义 = 只针对**合成输入**本身,不再依赖仓库清单的当前状态):
        // 这个合成占位值必须**格式非法**,否则本用例退化成"格式合法的 sha 被拒",测不到 R22 那条。
        Assert.False(
            Updater.ValidSha256(PlaceholderSha),
            $"合成占位值 `{PlaceholderSha}` 必须是格式非法的 —— 它是本用例的前提,前提变了就得重写这条用例");

        Updater.JsonFetcher = _ => PyDict.Of(
            "version", "99.9.9", "notes", manifest.Notes, "url", manifest.Url, "sha256", PlaceholderSha);
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
