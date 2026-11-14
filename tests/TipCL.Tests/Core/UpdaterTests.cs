// M4-H R22:启动器自更新的回归。
//
// 为什么需要这个文件:移植侧此前**只有** `Updater` 类本身、没有任何调用方,也没有测试;
// 这一轮把链路接上之后,三件事必须被钉住 ——
//   1 取清单走的是 **Linux 那份清单**(`update-linux.json`),而不是 Windows 那份(`update.json` 指向 `TipCL.exe`);
//   2 `Check` 的**安全兜底**:清单里的 sha256 不合法就拒绝更新(照参考版);
//   3 `ApplyStaged` 的**替换层**(Linux 新写的):同目录原子替换 + 保留可执行位;目标不可写时**不硬来**,
//     返回 `download-only` 让用户自己装(.deb 那种)。
using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

public class UpdaterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tipcl-upd-" + Guid.NewGuid().ToString("N").Substring(0, 8));

    public UpdaterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Updater.JsonFetcher = null;
        Updater.TargetOverride = "";
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    private string Write(string name, string content)
    {
        string p = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    /// <summary>注入一份假清单,并在测试结束时还原(静态状态不能漏给别的用例)。</summary>
    private static PyDict WithManifest(object manifest, Func<PyDict> body)
    {
        Updater.JsonFetcher = _ => manifest;
        try { return body(); }
        finally { Updater.JsonFetcher = null; }
    }

    /// <summary>造一份假清单。`sha256 == null` 表示**清单里根本没有这个键**(缺 sha256 的那条分支)。</summary>
    private static PyDict Manifest(string version, string? sha256 = null, string notes = "notes")
    {
        var d = PyDict.Of("version", version, "notes", notes,
                          "url", "https://example.invalid/TipCL-linux-x64.AppImage");
        if (sha256 != null)
            d["sha256"] = sha256;
        return d;
    }

    // ───────────────── 1) 清单地址 ─────────────────

    [Fact]
    public void Manifest_DefaultsToTheLinuxManifest_NotTheWindowsOne()
    {
        Assert.Contains("update-linux.json", Config.DEFAULT_UPDATE_URL);
        Assert.DoesNotContain("main/update.json", Config.DEFAULT_UPDATE_URL);
    }

    // ───────────────── 2) 版本比较(照参考版同一批用例) ─────────────────

    [Theory]
    [InlineData("1.0.10", "1.0.9", true)]
    [InlineData("1.0.9", "1.0.10", false)]
    [InlineData("1.1.0", "1.0.99", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    public void Newer_ComparesSegmentBySegment(string remote, string local, bool expected)
        => Assert.Equal(expected, Updater.Newer(remote, local));

    /// <summary>移植自己的版本号带 `-m3` 这种后缀(预发布标记),不能因此把它判成"比同号新版还新"。</summary>
    [Fact]
    public void Newer_HandlesThePortOwnVersionSuffix()
    {
        Assert.False(Updater.Newer("0.1.0", "0.1.0-m3"));   // 清单写同号 → 不算有更新
        Assert.True(Updater.Newer("0.1.1", "0.1.0-m3"));
        Assert.True(Updater.Newer("0.2", "0.1.0-m3"));
    }

    // ───────────────── 3) Check:有新版 / 无新版 / sha256 兜底 / 取清单失败 ─────────────────

    [Fact]
    public void Check_NewerVersionWithValidSha_ReportsUpdate()
    {
        var res = WithManifest(Manifest("99.0.0", new string('a', 64)), () => Updater.Check());
        Assert.True(Py.Truthy(res["has_update"]));
        Assert.True(Py.Truthy(res["ok"]));
        Assert.Equal("99.0.0", Py.S(res["latest"]));
        Assert.Contains("发现 99.0.0", Py.S(res["message"]));
        Assert.Equal(new string('a', 64), Py.S(res["sha256"]));
    }

    [Fact]
    public void Check_SameVersion_NoUpdate()
    {
        var res = WithManifest(Manifest(AppInfo.APP_VERSION), () => Updater.Check());
        Assert.False(Py.Truthy(res["has_update"]));
        Assert.True(Py.Truthy(res["ok"]));
        Assert.Contains("已是最新版本", Py.S(res["message"]));
    }

    /// <summary>安全兜底:清单说"有新版"但没有合法的 sha256 ⇒ **拒绝**更新(照参考版)。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zzzz")]
    [InlineData("abc123")]
    public void Check_NewerVersionWithoutValidSha_RefusesUpdate(string? sha)
    {
        var res = WithManifest(Manifest("99.0.0", sha), () => Updater.Check());
        Assert.False(Py.Truthy(res["has_update"]));
        Assert.False(Py.Truthy(res["ok"]));
        Assert.Contains("SHA-256", Py.S(res["message"]));
    }

    [Fact]
    public void Check_FetchFails_ReturnsNotOkWithMessage()
    {
        Updater.JsonFetcher = _ => throw new InvalidOperationException("boom");
        try
        {
            var res = Updater.Check();
            Assert.False(Py.Truthy(res["ok"]));
            Assert.False(Py.Truthy(res["has_update"]));
            Assert.Contains("检查更新失败", Py.S(res["message"]));
        }
        finally { Updater.JsonFetcher = null; }
    }

    // ───────────────── 4) 替换层(Linux 新写的那一半) ─────────────────

    [Fact]
    public void UpdateTarget_PrefersTheAppImagePath()
    {
        string fake = Write("Fake.AppImage", "APPIMAGE-BYTES");
        string? old = Environment.GetEnvironmentVariable("APPIMAGE");
        try
        {
            Environment.SetEnvironmentVariable("APPIMAGE", fake);
            Assert.True(Updater.IsAppImage());
            Assert.Equal(fake, Updater.UpdateTarget());
        }
        finally { Environment.SetEnvironmentVariable("APPIMAGE", old); }
    }

    [Fact]
    public void CanReplace_IsFalseWhenTheTargetDirectoryDoesNotExist()
    {
        Assert.False(Updater.CanReplace(Path.Combine(_dir, "nope", "TipCL.App")));
        Assert.False(Updater.CanReplace(""));
    }

    /// <summary>可写目标:同目录原子替换,内容真的换了,返回 ok 且 mode 说明是哪种形态。</summary>
    [Fact]
    public void Apply_ReplacesInPlace_AndKeepsTheExecutableBit()
    {
        string target = Write("TipCL.App", "OLD");
        string pkg = Write("pkg.bin", "NEW");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Updater.TargetOverride = target;
        try
        {
            var res = Updater.ApplyStaged(pkg, restart: false);

            Assert.True(Py.Truthy(res["ok"]));
            Assert.Equal("NEW", File.ReadAllText(target));                 // 真换了
            Assert.Equal(target, Py.S(res["target"]));
            Assert.Contains(Py.S(res["mode"]), new[] { "inplace", "appimage" });
            Assert.False(File.Exists(target + ".new"), "临时文件没清干净");
            Assert.True(File.Exists(pkg), "更新包是复制出去的,不该被搬走");
            if (!OperatingSystem.IsWindows())
            {
                // 权限跟着旧文件走:下载下来的包是 0644,不补可执行位换完就跑不起来
                var mode = File.GetUnixFileMode(target);
                Assert.True((mode & UnixFileMode.UserExecute) != 0, "替换后可执行位丢了");
            }
        }
        finally { Updater.TargetOverride = ""; }
    }

    /// <summary>目标不可写(典型:.deb 装到 /opt)⇒ 不硬来,返回 download-only 并把包留在原处。</summary>
    [Fact]
    public void Apply_UnwritableTarget_ReportsDownloadOnly_AndLeavesThePackageAlone()
    {
        string pkg = Write("pkg2.bin", "NEW");
        Updater.TargetOverride = Path.Combine(_dir, "root-owned", "TipCL.App");   // 父目录不存在 = 不可写
        try
        {
            var res = Updater.ApplyStaged(pkg, restart: false);

            Assert.False(Py.Truthy(res["ok"]));
            Assert.Equal("download-only", Py.S(res["mode"]));
            Assert.Equal(pkg, Py.S(res["package"]));
            Assert.Contains("不可写", Py.S(res["message"]));
            Assert.True(File.Exists(pkg));
        }
        finally { Updater.TargetOverride = ""; }
    }

    [Fact]
    public void Apply_MissingPackage_Throws()
    {
        Updater.TargetOverride = Write("TipCL2.App", "OLD");
        try
        {
            var ex = Assert.Throws<RuntimeError>(() => Updater.ApplyStaged(Path.Combine(_dir, "missing.bin"), restart: false));
            Assert.Contains("更新包不存在", ex.Message);
        }
        finally { Updater.TargetOverride = ""; }
    }

    [Fact]
    public void Download_RefusesAManifestWithoutUrlOrValidSha()
    {
        var noUrl = Assert.Throws<RuntimeError>(() => Updater.Download(PyDict.Of("sha256", new string('a', 64))));
        Assert.Contains("没有下载地址", noUrl.Message);

        var badSha = Assert.Throws<RuntimeError>(() => Updater.Download(PyDict.Of("url", "https://example.invalid/x", "sha256", "nope")));
        Assert.Contains("SHA-256", badSha.Message);
    }
}
