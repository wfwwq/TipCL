// M4-H P6:插件安装器(Core.Plugins.PluginInstaller)契约测试。
// 覆盖:zip / 文件夹安装、暂存→原子替换、清单校验(id/version/engines/依赖)、
// 开发链接、卸载(可选删数据)、市场安装的未审核门与索引信息校验。
// 全部走临时目录,不碰真实 plugins / plugins.json,也不联网。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests.Core;

public class PluginInstallerTests : IDisposable
{
    private readonly string _home;
    private readonly string _src;
    private readonly PluginDirs _dirs;
    private readonly PluginStateStore _store;
    private readonly PluginInstaller _installer;

    public PluginInstallerTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "tipcl-pluginstall-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(root, "home");
        _src = Path.Combine(root, "src");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_src);
        _dirs = new PluginDirs(_home);
        _store = new PluginStateStore(_dirs.StateFile);
        _installer = new PluginInstaller(_dirs, _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_home)!, recursive: true); } catch (Exception) { }
    }

    private static string Manifest(string id, string version = "1.0.0", string extra = "")
        => "{\"id\":\"" + id + "\",\"name\":\"测试 " + id + "\",\"version\":\"" + version + "\"," +
           "\"runtime\":\"node\",\"main\":\"index.js\",\"description\":\"测试用\"" + extra + "}";

    /// <summary>在临时目录里摊开一个插件目录(带清单 + 一个文件)。</summary>
    private string PluginDir(string id, string version = "1.0.0", string extra = "")
    {
        string dir = Path.Combine(_src, id + "-" + version);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), Manifest(id, version, extra));
        File.WriteAllText(Path.Combine(dir, "index.js"), "console.log('hi')");
        return dir;
    }

    private string ZipOf(string dir)
    {
        string zip = dir + ".zip";
        ZipFile.CreateFromDirectory(dir, zip);
        return zip;
    }

    // ───────────────────────── 安装 ─────────────────────────

    [Fact]
    public void InstallPackage_FromZip_LandsInPluginsRootWithStateRecord()
    {
        string zip = ZipOf(PluginDir("t-zip"));
        PluginManifest m = _installer.InstallPackage(zip, "market", "https://market.example/index.json", "t-zip", "1.0.0");

        Assert.Equal("t-zip", m.Id);
        Assert.Equal("1.0.0", m.Version);
        Assert.True(Directory.Exists(_dirs.PluginDir("t-zip")));
        Assert.True(File.Exists(Path.Combine(_dirs.PluginDir("t-zip"), "index.js")));
        // 暂存目录不许留下东西
        Assert.False(Directory.Exists(Path.Combine(_dirs.StagingRoot, "t-zip")));
        Assert.False(Directory.Exists(Path.Combine(_dirs.BackupRoot, "t-zip")));

        var rec = new PluginStateStore(_dirs.StateFile).Get("t-zip");
        Assert.NotNull(rec);
        Assert.Equal("market", rec!.Source);
        Assert.Equal("1.0.0", rec.Version);
        Assert.Equal("https://market.example/index.json", rec.Market);
        Assert.True(rec.Enabled);
        Assert.False(string.IsNullOrEmpty(rec.InstalledAt));
    }

    [Fact]
    public void InstallPackage_FromZipWithSingleFolderPrefix_IsUnwrapped()
    {
        // 市场上的包常常把内容套在一层目录里(ReadZipManifest 认这种)
        string dir = PluginDir("t-prefix");
        string nested = Path.Combine(_src, "nested");
        Directory.CreateDirectory(nested);
        string pkg = Path.Combine(nested, "t-prefix");
        Directory.CreateDirectory(pkg);
        foreach (string f in Directory.GetFiles(dir))
            File.Copy(f, Path.Combine(pkg, Path.GetFileName(f)));
        string zip = Path.Combine(_src, "nested.zip");
        ZipFile.CreateFromDirectory(nested, zip);

        _installer.InstallPackage(zip, "local", "", "", "");
        Assert.True(File.Exists(Path.Combine(_dirs.PluginDir("t-prefix"), "index.js")),
            "包内一层目录应被剥掉,index.js 落在插件根目录");
    }

    [Fact]
    public void InstallPackage_FromFolder_CopiesAndSkipsGitDir()
    {
        string dir = PluginDir("t-folder");
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        File.WriteAllText(Path.Combine(dir, ".git", "HEAD"), "ref: refs/heads/main");

        _installer.InstallPackage(dir, "local", "", "", "");
        string dest = _dirs.PluginDir("t-folder");
        Assert.True(File.Exists(Path.Combine(dest, "index.js")));
        Assert.False(Directory.Exists(Path.Combine(dest, ".git")), "开发目录的 .git 不该被复制进插件目录");
    }

    [Fact]
    public void InstallPackage_IsAtomicReplace_OldFilesDisappear()
    {
        string v1 = PluginDir("t-replace", "1.0.0");
        _installer.InstallPackage(v1, "market", "m", "t-replace", "1.0.0");
        string old = Path.Combine(_dirs.PluginDir("t-replace"), "only-in-v1.txt");
        File.WriteAllText(old, "v1");

        string v2 = PluginDir("t-replace", "2.0.0");
        File.WriteAllText(Path.Combine(v2, "only-in-v2.txt"), "v2");
        _installer.InstallPackage(v2, "market", "m", "t-replace", "2.0.0");

        string dest = _dirs.PluginDir("t-replace");
        Assert.False(File.Exists(old), "旧版本的文件必须随替换消失(不是原地覆盖)");
        Assert.True(File.Exists(Path.Combine(dest, "only-in-v2.txt")));
        Assert.Equal("2.0.0", _store.Get("t-replace")!.Version);
    }

    // ───────────────────── 清单 / 依赖 / engines ─────────────────────

    [Fact]
    public void InstallPackage_RejectsIdMismatch()
    {
        string zip = ZipOf(PluginDir("t-real"));
        var ex = Assert.Throws<ValueError>(() => _installer.InstallPackage(zip, "market", "m", "t-other", "1.0.0"));
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void InstallPackage_RejectsVersionMismatch()
    {
        string zip = ZipOf(PluginDir("t-ver", "1.0.0"));
        var ex = Assert.Throws<ValueError>(() => _installer.InstallPackage(zip, "market", "m", "t-ver", "2.0.0"));
        Assert.Contains("版本", ex.Message);
    }

    [Fact]
    public void InstallPackage_RejectsUnsatisfiedEngine()
    {
        string zip = ZipOf(PluginDir("t-engine", "1.0.0", ",\"engines\":{\"tipcl\":\">=99.0.0\"}"));
        var ex = Assert.Throws<ValueError>(() => _installer.InstallPackage(zip, "local", "", "", ""));
        Assert.Contains("99.0.0", ex.Message);
        Assert.False(Directory.Exists(_dirs.PluginDir("t-engine")), "engines 不满足时不该落盘");
    }

    [Fact]
    public void InstallPackage_RejectsMissingDependency()
    {
        string zip = ZipOf(PluginDir("t-dep", "1.0.0", ",\"dependencies\":{\"base-plugin\":\">=1.0.0\"}"));
        var ex = Assert.Throws<ValueError>(() => _installer.InstallPackage(zip, "local", "", "", ""));
        Assert.Contains("base-plugin", ex.Message);
    }

    [Fact]
    public void InstallPackage_AcceptsSatisfiedDependency()
    {
        _installer.InstallPackage(PluginDir("base-plugin", "1.2.0"), "local", "", "", "");
        string zip = ZipOf(PluginDir("t-dep2", "1.0.0", ",\"dependencies\":{\"base-plugin\":\">=1.0.0\"}"));
        _installer.InstallPackage(zip, "local", "", "", "");
        Assert.True(Directory.Exists(_dirs.PluginDir("t-dep2")));
    }

    // ───────────────────────── 开发链接 / 卸载 ─────────────────────────

    [Fact]
    public void LinkDev_RecordsPathWithoutCopying()
    {
        string dir = PluginDir("t-dev");
        _installer.LinkDev(dir);
        Assert.False(Directory.Exists(_dirs.PluginDir("t-dev")), "开发链接不该把目录复制进 plugins/");
        var rec = _store.Get("t-dev");
        Assert.NotNull(rec);
        Assert.Equal("dev", rec!.Source);
        Assert.Equal(Path.GetFullPath(dir), rec.DevPath);
        Assert.True(rec.Enabled);
    }

    [Fact]
    public void Uninstall_RemovesFilesAndRecord_AndOnlyDeletesDataWhenAsked()
    {
        string zip = ZipOf(PluginDir("t-uninstall"));
        _installer.InstallPackage(zip, "local", "", "", "");
        Directory.CreateDirectory(_dirs.DataDir("t-uninstall"));
        File.WriteAllText(Path.Combine(_dirs.DataDir("t-uninstall"), "cache.json"), "{}");

        _installer.Uninstall("t-uninstall", deleteData: false);
        Assert.False(Directory.Exists(_dirs.PluginDir("t-uninstall")));
        Assert.Null(_store.Get("t-uninstall"));
        Assert.True(Directory.Exists(_dirs.DataDir("t-uninstall")), "不勾「同时删除数据」时数据目录要留着");

        // 第二次卸载(记录与目录都没了)应当报错,不静默成功
        Assert.Throws<ValueError>(() => _installer.Uninstall("t-uninstall", deleteData: false));
    }

    [Fact]
    public void Uninstall_WithDeleteData_RemovesDataDir()
    {
        _installer.InstallPackage(PluginDir("t-data"), "local", "", "", "");
        Directory.CreateDirectory(_dirs.DataDir("t-data"));
        _installer.Uninstall("t-data", deleteData: true);
        Assert.False(Directory.Exists(_dirs.DataDir("t-data")));
    }

    [Fact]
    public void Uninstall_DevLink_KeepsDevFolder()
    {
        string dir = PluginDir("t-dev2");
        _installer.LinkDev(dir);
        _installer.Uninstall("t-dev2", deleteData: true);
        Assert.True(Directory.Exists(dir), "取消链接不该删开发文件夹");
        Assert.Null(_store.Get("t-dev2"));
    }

    // ───────────────────────── 市场安装的前置校验 ─────────────────────────

    [Fact]
    public void InstallFromMarket_RequiresDownloadUrlAndSha()
    {
        var entry = PyDict.Of("id", "t-market", "version", "1.0.0", "verified", true,
            "download", PyDict.Of("url", "", "sha256", ""));
        var ex = Assert.Throws<ValueError>(() => _installer.InstallFromMarket(entry));
        Assert.Contains("t-market", ex.Message);
    }

    [Fact]
    public void InstallFromMarket_RejectsUnverifiedWhenNotAllowed()
    {
        // AllowUnverified() 读 config.json 的 plugin_allow_unverified;测试隔离目录里没有该键 → 未允许
        Assert.False(PluginMarket.AllowUnverified());
        var entry = PyDict.Of("id", "t-unverified", "version", "1.0.0", "verified", false,
            "download", PyDict.Of("url", "https://example.invalid/x.zip", "sha256", new string('a', 64)));
        var ex = Assert.Throws<ValueError>(() => _installer.InstallFromMarket(entry));
        Assert.Contains("未审核", ex.Message);
    }

    [Fact]
    public void Sha256Matches_IsCaseInsensitiveAndRejectsWrongHash()
    {
        string zip = ZipOf(PluginDir("t-sha"));
        string sha = TipCL.Core.Launcher.Utils.Sha256File(zip);
        Assert.True(PluginMarket.Sha256Matches(zip, sha));
        Assert.True(PluginMarket.Sha256Matches(zip, sha.ToUpperInvariant()));
        Assert.False(PluginMarket.Sha256Matches(zip, new string('0', 64)));
        Assert.False(PluginMarket.Sha256Matches(zip, ""));
    }
}