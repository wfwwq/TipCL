using System;
using System.IO;
using System.Runtime.InteropServices;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c2 第 1 项:NodeRuntime 在 mac 上的自动下载。
///
/// 期望值来源(逐条标明):
/// <list type="bullet">
/// <item><c>DownloadTargetFor</c> / <c>PortableExePath</c> / <c>FindExtractedNodeDir</c> 的 Windows 期望值 =
/// **读过的源码行为**(改动前就是
/// <c>Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86"</c> 配 <c>.zip</c>、
/// <c>Dir/node.exe</c>、以及 <c>Directory.GetDirectories(tmp).FirstOrDefault(d =&gt; File.Exists(d/node.exe))</c>);</item>
/// <item>mac 的 <c>darwin-arm64</c> / <c>darwin-x64</c> + <c>.tar.gz</c> = **线上证据**:
/// 2026-11-12 21:35:33 +08:00 用两个 GET 抓下的
/// <c>https://nodejs.org/dist/latest-v22.x/SHASUMS256.txt</c>(v22.23.5,3981 字节)与
/// <c>https://npmmirror.com/mirrors/node/latest-v22.x/SHASUMS256.txt</c>(v22.20.0,3777 字节),
/// 下面 <c>NodejsOrgShasums</c> / <c>NpmmirrorShasums</c> 里的行是从这两份文件里**逐字抄**来的(纯 ASCII);</item>
/// <item><c>&lt;node 目录&gt;/bin/node</c> = **源码约定**(Locate 里「非 Windows 且不存在 dirs.NodeDir/node 时改看
/// bin/node」),mac 上的真实 tar 包布局**未经真机验证**。</item>
/// </list>
///
/// 用例不联网、不起进程、不建符号链接;用到的目录树都建在 <see cref="TestHome.Root"/> 之下,文件都是空文件。
/// </summary>
public class B2c2NodeRuntimeTests
{
    // ---------- 线上取证夹具(2026-11-12 21:35:33 +08:00 抓取,逐字抄写) ----------

    /// <summary>nodejs.org/dist/latest-v22.x/SHASUMS256.txt 里的 6 行(v22.23.5)。</summary>
    private const string NodejsOrgShasums =
        "e7131995759b61a0170b646010d2ad35a5d37b4cb0d471f3e8317eac6f742f3d  node-v22.23.5-darwin-arm64.tar.gz\n" +
        "d0c68c321161bb2c6483efea9a343010dea8e5e7f60d73c6a7eba140016d662f  node-v22.23.5-darwin-arm64.tar.xz\n" +
        "c53383238206b9ae4db23c5d8a63645e2e753bec8ab51a49cc81939aa64e9422  node-v22.23.5-darwin-x64.tar.gz\n" +
        "d68bf234a07781141dfca61ae4461c24560f70bd53223d76a41529eb02e8554e  node-v22.23.5-darwin-x64.tar.xz\n" +
        "c54118104acc524633b5abe106bbf44a53126ec8b4e5fa105cb92b5d7de8a1a1  node-v22.23.5-linux-x64.tar.gz\n" +
        "4a191082e9706866830379d577d2cc8a50c776fcd9436a8d061b063ed0d9a0e3  node-v22.23.5-win-x64.zip\n";

    /// <summary>npmmirror.com/mirrors/node/latest-v22.x/SHASUMS256.txt 里的 6 行(v22.20.0)。</summary>
    private const string NpmmirrorShasums =
        "cc04a76a09f79290194c0646f48fec40354d88969bec467789a5d55dd097f949  node-v22.20.0-darwin-arm64.tar.gz\n" +
        "f2ed1fbdfb79afb9343dfbb609efde657b41e7efcd5215264ce78f8906356b4d  node-v22.20.0-darwin-arm64.tar.xz\n" +
        "00df9c5df3e4ec6848c26b70fb47bf96492f342f4bed6b17f12d99b3a45eeecc  node-v22.20.0-darwin-x64.tar.gz\n" +
        "2a291f0a9555f5d6685d96ce9429da3d0ea3cd896c012702c6ee0015f818684e  node-v22.20.0-darwin-x64.tar.xz\n" +
        "eeaccb0378b79406f2208e8b37a62479c70595e20be6b659125eb77dd1ab2a29  node-v22.20.0-linux-x64.tar.gz\n" +
        "bb819d6eb8f5bfda294bbc83a7e4ec6539da67c4233d54b0d655b9248b15e29d  node-v22.20.0-win-x64.zip\n";

    // ---------- 1) DownloadTargetFor ----------

    /// <summary>
    /// Windows 钉子:两个取值与改动前那句内联表达式**逐字相同**,扩展名恒为 .zip
    /// (期望值来自源码 NodeRuntime.cs 改动前 <c>Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86"</c>)。
    /// </summary>
    [Theory]
    [InlineData(true, "win-x64")]
    [InlineData(false, "win-x86")]
    public void Windows_keeps_the_exact_old_platform_and_extension(bool is64BitOperatingSystem, string expectedPlatform)
    {
        var got = NodeRuntime.DownloadTargetFor("windows", Architecture.X64, is64BitOperatingSystem);

        Assert.NotNull(got);
        Assert.Equal(expectedPlatform, got.Value.platform);
        Assert.Equal(".zip", got.Value.ext);
    }

    /// <summary>Windows 不看进程架构(改动前也没看):arm64 / x64 / x86 / arm 四种进程架构结果一样。</summary>
    [Theory]
    [InlineData(Architecture.X64)]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm64)]
    [InlineData(Architecture.Arm)]
    public void Windows_ignores_the_process_architecture(Architecture processArch)
    {
        var got = NodeRuntime.DownloadTargetFor("windows", processArch, is64BitOperatingSystem: true);

        Assert.NotNull(got);
        Assert.Equal("win-x64", got.Value.platform);
        Assert.Equal(".zip", got.Value.ext);
    }

    /// <summary>Apple 芯片 mac → darwin-arm64 的 tar.gz(线上证据:两份 SHASUMS256 里都是这个名字)。</summary>
    [Fact]
    public void Apple_silicon_mac_downloads_the_darwin_arm64_tarball()
    {
        var got = NodeRuntime.DownloadTargetFor("osx", Architecture.Arm64, is64BitOperatingSystem: true);

        Assert.NotNull(got);
        Assert.Equal("darwin-arm64", got.Value.platform);
        Assert.Equal(".tar.gz", got.Value.ext);
    }

    /// <summary>Intel mac → darwin-x64 的 tar.gz(同上,线上证据)。</summary>
    [Fact]
    public void Intel_mac_downloads_the_darwin_x64_tarball()
    {
        var got = NodeRuntime.DownloadTargetFor("osx", Architecture.X64, is64BitOperatingSystem: true);

        Assert.NotNull(got);
        Assert.Equal("darwin-x64", got.Value.platform);
        Assert.Equal(".tar.gz", got.Value.ext);
    }

    /// <summary>mac 按**进程架构**取,不看是否 64 位系统(Node 是启动器自己用的,与 B2d 的目标架构无关)。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mac_uses_the_process_architecture_and_not_the_os_bitness(bool is64BitOperatingSystem)
    {
        var arm = NodeRuntime.DownloadTargetFor("osx", Architecture.Arm64, is64BitOperatingSystem);
        var intel = NodeRuntime.DownloadTargetFor("osx", Architecture.X64, is64BitOperatingSystem);

        Assert.NotNull(arm);
        Assert.NotNull(intel);
        Assert.Equal("darwin-arm64", arm.Value.platform);
        Assert.Equal("darwin-x64", intel.Value.platform);
    }

    /// <summary>mac 上没有对应包的进程架构 → 不支持(调用方抛原来那句 RuntimeError)。</summary>
    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.Wasm)]
    public void Mac_architectures_without_a_package_are_unsupported(Architecture processArch)
    {
        Assert.Null(NodeRuntime.DownloadTargetFor("osx", processArch, is64BitOperatingSystem: true));
    }

    /// <summary>其它平台(含 linux)→ 不支持:Linux 分支本包不新增功能,原来抛错的继续抛错。</summary>
    [Theory]
    [InlineData("linux")]
    [InlineData("")]
    [InlineData("freebsd")]
    [InlineData("windows-arm64")]
    [InlineData("OSX")]
    public void Other_platforms_are_unsupported(string os)
    {
        Assert.Null(NodeRuntime.DownloadTargetFor(os, Architecture.Arm64, is64BitOperatingSystem: true));
    }

    // ---------- 2) PortableExePath ----------

    /// <summary>Windows 钉子:&lt;node 目录&gt;/node.exe(改动前 Download 里就是这句)。</summary>
    [Fact]
    public void Portable_exe_path_on_windows_is_node_exe()
    {
        string dir = Path.Combine(TestHome.Root, "portable-win");

        Assert.Equal(Path.Combine(dir, "node.exe"), NodeRuntime.PortableExePath(dir, isWindows: true));
    }

    /// <summary>非 Windows:&lt;node 目录&gt;/bin/node,与 Locate 的兜底拼法一致(D-42 同型接缝)。</summary>
    [Fact]
    public void Portable_exe_path_on_unix_is_bin_slash_node()
    {
        string dir = Path.Combine(TestHome.Root, "portable-unix");
        string got = NodeRuntime.PortableExePath(dir, isWindows: false);

        Assert.Equal(Path.Combine(dir, "bin", "node"), got);
        Assert.Equal("node", Path.GetFileName(got));
        Assert.Equal("bin", Path.GetFileName(Path.GetDirectoryName(got)));
    }

    /// <summary>两个分支都只用当前平台的目录分隔符拼(不写死 / 或 \)。</summary>
    [Fact]
    public void Portable_exe_path_uses_the_platform_separator()
    {
        string dir = Path.Combine(TestHome.Root, "portable-sep");

        Assert.Equal(dir + Path.DirectorySeparatorChar + "node.exe", NodeRuntime.PortableExePath(dir, isWindows: true));
        Assert.Equal(
            dir + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + "node",
            NodeRuntime.PortableExePath(dir, isWindows: false));
    }

    // ---------- 3) FindExtractedNodeDir(真实目录树,都在 TestHome.Root 之下) ----------

    private static string NewTree(params string[] relativeFiles)
    {
        string root = Path.Combine(TestHome.Root, "b2c2-node-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (string rel in relativeFiles)
        {
            string full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, Array.Empty<byte>());
        }
        return root;
    }

    /// <summary>Windows:一层子目录里有 node.exe 的那层就是它(zip 里是 node-v22.x.y-win-x64/)。</summary>
    [Fact]
    public void Windows_layer_is_the_subdirectory_containing_node_exe()
    {
        string tmp = NewTree("node-v22.23.5-win-x64/node.exe", "node-v22.23.5-win-x64/README.md");

        Assert.Equal(Path.Combine(tmp, "node-v22.23.5-win-x64"), NodeRuntime.FindExtractedNodeDir(tmp, isWindows: true));
    }

    /// <summary>Windows:没有一个子目录含 node.exe → null(调用方按原来的 <c>?? tmp</c> 处理)。</summary>
    [Fact]
    public void Windows_layer_without_node_exe_returns_null()
    {
        string tmp = NewTree("node-v22.23.5-win-x64/README.md");

        Assert.Null(NodeRuntime.FindExtractedNodeDir(tmp, isWindows: true));
    }

    /// <summary>Windows 只看**一层子目录**:临时目录根上的 node.exe 不算改动前的行为。</summary>
    [Fact]
    public void Windows_layer_ignores_a_node_exe_at_the_temporary_root()
    {
        string tmp = NewTree("node.exe", "note/readme.txt");

        Assert.Null(NodeRuntime.FindExtractedNodeDir(tmp, isWindows: true));
    }

    /// <summary>非 Windows:一层子目录里有 bin/node 的那层就是它(tar.gz 顶层是 node-v22.x.y-darwin-*/)。</summary>
    [Fact]
    public void Unix_layer_is_the_subdirectory_containing_bin_slash_node()
    {
        string tmp = NewTree("node-v22.23.5-darwin-arm64/bin/node", "node-v22.23.5-darwin-arm64/LICENSE");

        Assert.Equal(Path.Combine(tmp, "node-v22.23.5-darwin-arm64"), NodeRuntime.FindExtractedNodeDir(tmp, isWindows: false));
    }

    /// <summary>非 Windows:只有顶层的 node(没有 bin/node)→ null。</summary>
    [Fact]
    public void Unix_layer_requires_bin_slash_node_not_a_top_level_node()
    {
        string tmp = NewTree("node-v22.23.5-darwin-arm64/node", "node-v22.23.5-darwin-arm64/bin/npm");

        Assert.Null(NodeRuntime.FindExtractedNodeDir(tmp, isWindows: false));
    }

    /// <summary>非 Windows 不认 node.exe 那一层(同一个临时目录用两种 isWindows 得到不同结论)。</summary>
    [Fact]
    public void Unix_layer_ignores_a_node_exe_only_tree()
    {
        string tmp = NewTree("node-v22.23.5-win-x64/node.exe");

        Assert.NotNull(NodeRuntime.FindExtractedNodeDir(tmp, isWindows: true));
        Assert.Null(NodeRuntime.FindExtractedNodeDir(tmp, isWindows: false));
    }

    /// <summary>多个层都匹配时取**第一个**;只有后面的匹配时也能找到(与改动前的 FirstOrDefault 一致)。</summary>
    [Fact]
    public void The_first_matching_layer_wins()
    {
        string tmp = NewTree("a/node.exe", "b/node.exe", "c/node.exe");

        Assert.Equal(Path.Combine(tmp, "a"), NodeRuntime.FindExtractedNodeDir(tmp, isWindows: true));

        string only = NewTree("a/readme.txt", "b/readme.txt", "c/node.exe");
        Assert.Equal(Path.Combine(only, "c"), NodeRuntime.FindExtractedNodeDir(only, isWindows: true));
    }

    /// <summary>空的临时目录 → null(不抛)。</summary>
    [Fact]
    public void An_empty_temporary_directory_returns_null()
    {
        string tmp = NewTree();

        Assert.Null(NodeRuntime.FindExtractedNodeDir(tmp, isWindows: true));
        Assert.Null(NodeRuntime.FindExtractedNodeDir(tmp, isWindows: false));
    }

    /// <summary>两个委托被真的用上了(不碰文件系统):注入假目录表 + 假存在性判断即可定位那一层。</summary>
    [Fact]
    public void Injected_delegates_are_used_instead_of_the_file_system()
    {
        string[] dirs = new string[] { "/fake/a", "/fake/b" };
        string seen = null;

        string got = NodeRuntime.FindExtractedNodeDir(
            "/fake/tmp",
            isWindows: false,
            listDirs: delegate(string d) { Assert.Equal("/fake/tmp", d); return dirs; },
            fileExists: delegate(string p) { seen = p; return p == Path.Combine("/fake/b", "bin", "node"); });

        Assert.Equal("/fake/b", got);
        Assert.Equal(Path.Combine("/fake/b", "bin", "node"), seen);
    }

    /// <summary>Windows 入参下注入的委托收到的是 node.exe 那一层(证明相对标记按 isWindows 选)。</summary>
    [Fact]
    public void Injected_delegates_see_node_exe_on_windows()
    {
        string seen = null;

        NodeRuntime.FindExtractedNodeDir(
            "/fake/tmp",
            isWindows: true,
            listDirs: delegate { return new string[] { "/fake/a" }; },
            fileExists: delegate(string p) { seen = p; return true; });

        Assert.Equal(Path.Combine("/fake/a", "node.exe"), seen);
    }

    /// <summary>列目录的委托抛异常 → 原样往上抛(与改动前 Directory.GetDirectories 一致,由镜像循环 catch)。</summary>
    [Fact]
    public void A_throwing_directory_lister_propagates()
    {
        IOException ex = Assert.Throws<IOException>(delegate
        {
            NodeRuntime.FindExtractedNodeDir("/fake/tmp", isWindows: true, listDirs: delegate { throw new IOException("boom"); });
        });

        Assert.Equal("boom", ex.Message);
    }

    // ---------- 4) PickFromShasums(用线上夹具) ----------

    /// <summary>夹具自检:6 行都逐字在,且每行都是「64 位十六进制 + 两个空格 + node-v... 文件名」。</summary>
    [Fact]
    public void The_fixture_lines_are_the_ones_transcribed_from_the_online_files()
    {
        string[] lines = NodejsOrgShasums.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(6, lines.Length);
        Assert.All(lines, delegate(string line)
        {
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, parts.Length);
            Assert.Equal(64, parts[0].Length);
            Assert.StartsWith("node-v22.23.5-", parts[1]);
        });
        Assert.Contains("node-v22.23.5-darwin-arm64.tar.gz", NodejsOrgShasums);
        Assert.Contains("node-v22.23.5-darwin-x64.tar.gz", NodejsOrgShasums);
        Assert.Contains("4a191082e9706866830379d577d2cc8a50c776fcd9436a8d061b063ed0d9a0e3", NodejsOrgShasums);
        Assert.Contains("cc04a76a09f79290194c0646f48fec40354d88969bec467789a5d55dd097f949", NpmmirrorShasums);
    }

    /// <summary>darwin-arm64 + .tar.gz:挑到的那一行与线上文件逐字一致(nodejs.org,v22.23.5)。</summary>
    [Fact]
    public void Picks_the_darwin_arm64_tarball()
    {
        var got = NodeRuntime.PickFromShasums(NodejsOrgShasums, "darwin-arm64", ".tar.gz");

        Assert.Equal("node-v22.23.5-darwin-arm64.tar.gz", got.file);
        Assert.Equal("e7131995759b61a0170b646010d2ad35a5d37b4cb0d471f3e8317eac6f742f3d", got.sha256);
    }

    /// <summary>darwin-x64 + .tar.gz 同理。</summary>
    [Fact]
    public void Picks_the_darwin_x64_tarball()
    {
        var got = NodeRuntime.PickFromShasums(NodejsOrgShasums, "darwin-x64", ".tar.gz");

        Assert.Equal("node-v22.23.5-darwin-x64.tar.gz", got.file);
        Assert.Equal("c53383238206b9ae4db23c5d8a63645e2e753bec8ab51a49cc81939aa64e9422", got.sha256);
    }

    /// <summary>扩展名真的参与筛选:挑 .tar.gz 时绝不返回同一架构的 .tar.xz 那一行。</summary>
    [Fact]
    public void The_extension_really_filters_out_the_tar_xz_lines()
    {
        var gz = NodeRuntime.PickFromShasums(NodejsOrgShasums, "darwin-arm64", ".tar.gz");
        var xz = NodeRuntime.PickFromShasums(NodejsOrgShasums, "darwin-arm64", ".tar.xz");

        Assert.Equal("node-v22.23.5-darwin-arm64.tar.gz", gz.file);
        Assert.Equal("node-v22.23.5-darwin-arm64.tar.xz", xz.file);
        Assert.NotEqual(gz.sha256, xz.sha256);
    }

    /// <summary>
    /// Windows 钉子:不传 platform / ext 时默认仍是 <c>win-x64</c> + <c>.zip</c>
    /// (改动前调用点就是 <c>PickFromShasums(shasums, platform)</c>,默认参数没动)。
    /// </summary>
    [Fact]
    public void The_old_default_arguments_still_pick_the_windows_zip()
    {
        var got = NodeRuntime.PickFromShasums(NodejsOrgShasums);

        Assert.Equal("node-v22.23.5-win-x64.zip", got.file);
        Assert.Equal("4a191082e9706866830379d577d2cc8a50c776fcd9436a8d061b063ed0d9a0e3", got.sha256);
    }

    /// <summary>两个镜像的夹具都能挑出 win-x64 zip(版本号 / 哈希不同,镜像会滞后)。</summary>
    [Fact]
    public void Both_mirrors_offer_a_win_x64_zip()
    {
        var fromNodejs = NodeRuntime.PickFromShasums(NodejsOrgShasums, "win-x64", ".zip");
        var fromMirror = NodeRuntime.PickFromShasums(NpmmirrorShasums, "win-x64", ".zip");

        Assert.Equal("node-v22.23.5-win-x64.zip", fromNodejs.file);
        Assert.Equal("node-v22.20.0-win-x64.zip", fromMirror.file);
        Assert.Equal("bb819d6eb8f5bfda294bbc83a7e4ec6539da67c4233d54b0d655b9248b15e29d", fromMirror.sha256);
    }

    /// <summary>npmmirror 夹具里也挑得到 darwin-arm64 的 tar.gz(v22.20.0)。</summary>
    [Fact]
    public void The_mirror_fixture_picks_the_darwin_arm64_tarball()
    {
        var got = NodeRuntime.PickFromShasums(NpmmirrorShasums, "darwin-arm64", ".tar.gz");

        Assert.Equal("node-v22.20.0-darwin-arm64.tar.gz", got.file);
        Assert.Equal("cc04a76a09f79290194c0646f48fec40354d88969bec467789a5d55dd097f949", got.sha256);
    }

    /// <summary>没有这个平台的行 → (null, null)(调用方抛「SHASUMS256.txt 里没有 ...」)。</summary>
    [Fact]
    public void An_unknown_platform_returns_nulls()
    {
        var got = NodeRuntime.PickFromShasums(NodejsOrgShasums, "darwin-ppc64", ".tar.gz");

        Assert.Null(got.file);
        Assert.Null(got.sha256);
    }

    /// <summary>sha 一律转小写(改动前就是 ToLowerInvariant)。</summary>
    [Fact]
    public void The_digest_is_lowercased()
    {
        string upper = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789  node-v22.23.5-darwin-arm64.tar.gz\n";

        var got = NodeRuntime.PickFromShasums(upper, "darwin-arm64", ".tar.gz");

        Assert.Equal("node-v22.23.5-darwin-arm64.tar.gz", got.file);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", got.sha256);
    }

    /// <summary>sha 长度不是 64 的行不认(改动前就是 Length == 64)。</summary>
    [Fact]
    public void A_line_with_a_bad_digest_length_is_ignored()
    {
        string bad = "abcd  node-v22.23.5-darwin-arm64.tar.gz\n";

        Assert.Null(NodeRuntime.PickFromShasums(bad, "darwin-arm64", ".tar.gz").file);
    }

    // ---------- 5) npm 布局核对(第 1 项要求表,只读代码 + 这几条钉子) ----------

    /// <summary>
    /// mac 的 tar 包布局:可执行文件在 &lt;NodeDir&gt;/bin/node,npm 在
    /// &lt;NodeDir&gt;/lib/node_modules/npm/bin/npm-cli.js —— 改动前的 FindNpmCli 第 2 个候选
    /// (<c>dir/../lib/node_modules/npm/bin/npm-cli.js</c>,dir = &lt;NodeDir&gt;/bin)正好命中,
    /// 所以**不需要改**。这条用例用 TestHome.Root 下的空文件钉住它。
    /// </summary>
    [Fact]
    public void Npm_cli_is_found_for_the_mac_tarball_layout()
    {
        string nodeDir = NewTree(
            "bin/node",
            "lib/node_modules/npm/bin/npm-cli.js",
            "lib/node_modules/npm/bin/npm.js");

        string got = NodeRuntime.FindNpmCli(Path.Combine(nodeDir, "bin", "node"));

        Assert.Equal(Path.Combine(nodeDir, "lib", "node_modules", "npm", "bin", "npm-cli.js"), got);
    }

    /// <summary>Windows 的 zip 布局:&lt;NodeDir&gt;/node_modules/npm/bin/npm-cli.js(改动前第 1 个候选)。</summary>
    [Fact]
    public void Npm_cli_is_found_for_the_windows_zip_layout()
    {
        string nodeDir = NewTree("node.exe", "node_modules/npm/bin/npm-cli.js");

        string got = NodeRuntime.FindNpmCli(Path.Combine(nodeDir, "node.exe"));

        Assert.Equal(Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js"), got);
    }

    /// <summary>两个候选都在时按候选顺序取第 1 个(改动前的 Select(...).FirstOrDefault 语义)。</summary>
    [Fact]
    public void Npm_cli_prefers_the_candidate_next_to_node()
    {
        string nodeDir = NewTree(
            "bin/node",
            "bin/node_modules/npm/bin/npm-cli.js",
            "lib/node_modules/npm/bin/npm-cli.js");

        Assert.Equal(
            Path.Combine(nodeDir, "bin", "node_modules", "npm", "bin", "npm-cli.js"),
            NodeRuntime.FindNpmCli(Path.Combine(nodeDir, "bin", "node")));
    }

    /// <summary>都不存在 → null(NpmInstall 于是回落到 PATH 上的 npm / npm.cmd)。</summary>
    [Fact]
    public void Npm_cli_is_null_when_there_is_no_npm_install()
    {
        string nodeDir = NewTree("bin/node");

        Assert.Null(NodeRuntime.FindNpmCli(Path.Combine(nodeDir, "bin", "node")));
    }

    // ---------- 6) 结构钉子:Download 真的接上了这些接缝(读源码,不执行生产入口) ----------

    /// <summary>
    /// 生产代码路径(读源码核对,不执行 —— <c>Download</c> 会联网、起下载器,红线禁止在测试里调):
    /// Windows 仍走 <c>Utils.SafeExtractZip</c>,非 zip 走 <c>JavaRuntimeFiles.SafeExtractTarGz</c>;
    /// 平台/扩展名来自可注入的 <c>DownloadTargetFor</c> 且传的是**进程架构**;
    /// 那一层目录用 <c>FindExtractedNodeDir</c>,可执行文件路径用 <c>PortableExePath</c>。
    /// </summary>
    [Fact]
    public void Download_delegates_to_the_new_seams()
    {
        string source = File.ReadAllText(RepoFile("src", "TipCL.Core", "Plugins", "NodeRuntime.cs"));

        Assert.Contains("DownloadTargetFor(Utils.OS_NAME, RuntimeInformation.ProcessArchitecture, Environment.Is64BitOperatingSystem)", source);
        Assert.Contains("PickFromShasums(shasums, platform, ext)", source);
        Assert.Contains("Utils.SafeExtractZip(zip, tmp);", source);
        Assert.Contains("JavaRuntimeFiles.SafeExtractTarGz(zip, tmp);", source);
        Assert.Contains("FindExtractedNodeDir(tmp, IsWindows)", source);
        Assert.Contains("PortableExePath(dirs.NodeDir, IsWindows)", source);
        Assert.Contains("(ext == \".zip\" ? \"zip\" : ext)", source);
    }

    /// <summary>不支持的系统仍然抛原来那句 RuntimeError(文案逐字在源码里)。</summary>
    [Fact]
    public void The_unsupported_platform_still_throws_the_original_message()
    {
        string source = File.ReadAllText(RepoFile("src", "TipCL.Core", "Plugins", "NodeRuntime.cs"));

        Assert.Contains("throw new RuntimeError(\"\u5F53\u524D\u7CFB\u7EDF\u4E0D\u652F\u6301\u81EA\u52A8\u4E0B\u8F7D Node", source);
    }

    /// <summary>从输出目录往上找到仓库根,再拼出源文件路径(只在仓库内跑测试)。</summary>
    private static string RepoFile(params string[] relative)
    {
        DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string[] parts = new string[relative.Length + 1];
            parts[0] = dir.FullName;
            Array.Copy(relative, 0, parts, 1, relative.Length);
            string candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("没找到仓库里的源文件: " + string.Join("/", relative));
    }
}
