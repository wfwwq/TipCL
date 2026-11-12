using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2d 第 2 项:每个调用点改成读「本次启动的目标架构」之后的行为。
/// 期望值来源分两种,每条用例的注释里都写明是哪一种:
/// <list type="bullet">
/// <item>源码行为:读 <c>Installer.SelectNativeClassifier</c> / <c>NativesJarRelpath</c> /
/// <c>Preflight._natives_entry</c> / <c>Cleaner._lib_relpaths</c> / <c>OfficialMigrate._library_relpaths</c>
/// 的当前实现(与 Windows 源的同一段逐句等价,只有 arch 从静态字段换成入参)。</item>
/// <item>B2b 线上证据:8 份版本 JSON 里 mac 的 natives 分类器写法与 <c>rules</c>(PROGRESS.md 的 B2b 第 0/3 项)。</item>
/// </list>
/// 全是纯函数调用(只有 <c>_natives_to_extract</c> 会在 <see cref="TestHome.Dir"/> 之下造小文件并检查它们存在),
/// 不联网、不起进程、不碰用户真实数据。
/// </summary>
public class B2dCallSiteArchTests
{
    private static string LibsDir()
    {
        return Path.Combine(TestHome.Root, "b2d-libs");
    }

    /// <summary>夹具里第一条带 mac(natives.osx)的老式 natives 库。</summary>
    private static PyDict FirstOsxNativesLib(PyDict versionJson)
    {
        return Installer.Libs(versionJson).First((PyDict lib) => Py.Truthy(Py.DictOr(Py.Or(lib.Get("natives"), new PyDict())).Get("osx")));
    }

    /// <summary>1.16.5 形态里那条只有 mac natives 的 LWJGL 条目(带具体 jar 路径)。</summary>
    private static PyDict Lwjgl321()
    {
        return NamedLib(B2dFixtures.V1165WithJars(), "org.lwjgl:lwjgl:3.2.1");
    }

    private static PyDict NamedLib(PyDict versionJson, string name)
    {
        return Installer.Libs(versionJson).First((PyDict lib) => Py.Str(Py.Or(lib.Get("name"), "")) == name);
    }

    private static PyDict Downloads(PyDict lib)
    {
        return Py.DictOr(Py.Or(lib.Get("downloads"), new PyDict()));
    }

    // ——— 1. 分类器选择:osx + arm64 上按目标架构 x64 选,结果与 osx + x64 相同 ———

    /// <summary>6 个老版本:Apple 芯片上目标架构 = x64,分类器选到的就是 x86_64 那一份,
    /// 与 Intel Mac(osx + x64)完全一致;用进程架构 arm64 直接选(改动前的行为)仍然是 null。</summary>
    [Theory]
    [InlineData("1.7.10", "natives-osx")]
    [InlineData("1.8.9", "natives-osx")]
    [InlineData("1.12.2", "natives-osx")]
    [InlineData("1.13.2", "natives-macos")]
    [InlineData("1.16.5", "natives-macos")]
    [InlineData("1.18.2", "natives-macos")]
    public void Osx_arm64_selects_the_x86_64_classifier(string id, string expected)
    {
        PyDict json = B2dFixtures.ById(id);
        PyDict lib = FirstOsxNativesLib(json);
        string arch = LaunchArch.For(json, "osx", "arm64", "14.5");
        Assert.Equal("x64", arch);
        Assert.Equal(expected, Installer.SelectNativeClassifier(lib, "osx", arch));
        // 「与 osx + x64 的结果相同」。
        Assert.Equal(Installer.SelectNativeClassifier(lib, "osx", "x64"), Installer.SelectNativeClassifier(lib, "osx", arch));
        // 改动前的行为(用进程架构 arm64):老式分类器选不到 —— 这条钉住 B2b 的现状没有被 B2d 改掉。
        Assert.Null(Installer.SelectNativeClassifier(lib, "osx", "arm64"));
    }

    /// <summary>1.19.4 形态(新式库:没有 natives 字段、库名第 4 段是 classifier):
    /// 目标架构是 arm64,分类器在任何架构下都是 null,与改动前相同;1.19.4 的两个 mac jar 靠 rules 进 classpath。</summary>
    [Fact]
    public void Modern_versions_are_unchanged_on_apple_silicon()
    {
        PyDict json = B2dFixtures.V1194();
        Assert.Equal("arm64", LaunchArch.For(json, "osx", "arm64", "14.5"));
        Assert.Equal("x64", LaunchArch.For(json, "osx", "x64", "14.5"));
        foreach (PyDict lib in Installer.Libs(json))
        {
            Assert.Null(Installer.SelectNativeClassifier(lib, "osx", "arm64"));
            Assert.Null(Installer.SelectNativeClassifier(lib, "osx", LaunchArch.For(json, "osx", "arm64", "14.5")));
            Assert.Null(Installer.SelectNativeClassifier(lib, "osx", "x64"));
        }
    }

    /// <summary>Windows 钉子:本机(进程 windows + x64)下 _lib_relpaths / 分类器在改动前后是同一个值 ——
    /// 注入本机三字段 == 注入字面量 windows + x64。</summary>
    [Fact]
    public void Classifier_results_are_unchanged_on_this_machine()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        foreach (string id in B2dFixtures.LiveIds)
        {
            PyDict json = B2dFixtures.ById(id);
            Assert.Equal(
                LaunchArch.For(json, "windows", "x64", Utils.OS_VERSION),
                LaunchArch.For(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
            foreach (PyDict lib in Installer.Libs(json))
            {
                Assert.Equal(
                    Installer.SelectNativeClassifier(lib, Utils.OS_NAME, LaunchArch.For(json)),
                    Installer.SelectNativeClassifier(lib, "windows", "x64"));
            }
        }
    }

    /// <summary>Windows 钉子的第二种形式:公开入口 SelectNativeClassifier(lib) 与「本机三字段」一致。</summary>
    [Fact]
    public void Public_classifier_entry_matches_the_injected_one()
    {
        foreach (PyDict lib in Installer.Libs(B2dFixtures.V1165()))
        {
            Assert.Equal(Installer.SelectNativeClassifier(lib), Installer.SelectNativeClassifier(lib, Utils.OS_NAME, Utils.ARCH));
        }
    }

    // ——— 2. Installer._natives_to_extract(ExtractNatives 的调用点) ———

    /// <summary>1.16.5 形态 + osx + arm64:目标架构 x64,要解压的正是两个 x86_64 mac natives jar,
    /// 与 Intel Mac(osx + x64)同一份清单。文件在 TestHome.Dir 之下自造(空文件,不联网、不下载)。</summary>
    [Fact]
    public void Extract_plan_on_apple_silicon_uses_the_x86_64_jars()
    {
        using TestHome testHome = new TestHome();
        string libs = Path.Combine(testHome.Dir, "libraries");
        Directory.CreateDirectory(Path.Combine(libs, "org", "lwjgl", "lwjgl", "3.2.1"));
        Directory.CreateDirectory(Path.Combine(libs, "ca", "weblite", "java-objc-bridge", "1.0.0"));
        File.WriteAllText(Path.Combine(libs, Path.Combine("org", "lwjgl", "lwjgl", "3.2.1", "lwjgl-3.2.1-natives-macos.jar")), "");
        File.WriteAllText(Path.Combine(libs, Path.Combine("ca", "weblite", "java-objc-bridge", "1.0.0", "java-objc-bridge-1.0.0-natives-osx.jar")), "");

        PyDict json = B2dFixtures.V1165WithJars();
        List<KeyValuePair<string, PyDict>> list = Installer._natives_to_extract(json, libs, "osx", "arm64", "14.5");
        List<string> actual = list.Select((KeyValuePair<string, PyDict> p) => p.Key).ToList();
        List<string> expected = new List<string> { PPath.Join(libs, B2dFixtures.LwjglNativesJar), PPath.Join(libs, B2dFixtures.ObjcBridgeJar) };
        Assert.Equal(expected, actual);
        // 与 Intel Mac 完全一致。
        Assert.Equal(
            Installer._natives_to_extract(json, libs, "osx", "x64", "14.5").Select((KeyValuePair<string, PyDict> p) => p.Key).ToList(),
            actual);
        // 与注入本机三字段一致(Windows 行为不变)。
        Assert.Equal(
            Installer._natives_to_extract(json, libs, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION).Select((KeyValuePair<string, PyDict> p) => p.Key).ToList(),
            Installer._natives_to_extract(json, libs, "windows", "x64", Utils.OS_VERSION).Select((KeyValuePair<string, PyDict> p) => p.Key).ToList());
    }

    /// <summary>natives jar 不存在时跳过并告警(源码行为:IsFile 检查),返回值里不含它 —— 证明上面那条用例
    /// 真的是靠自造的文件命中的,不是函数凭空返回路径。</summary>
    [Fact]
    public void Extract_plan_skips_missing_jars()
    {
        using TestHome testHome = new TestHome();
        string libs = Path.Combine(testHome.Dir, "libraries");
        Directory.CreateDirectory(libs);
        Assert.Empty(Installer._natives_to_extract(B2dFixtures.V1165WithJars(), libs, "osx", "arm64", "14.5"));
    }

    /// <summary>Windows 目标(x64):1.16.5 形态里 3.2.2 与 text2speech 的 natives-windows 被选中,
    /// mac 专有那两条选不到 —— 与改动前的 windows 行为一致(源码行为 + B2b 第 0 步的 JSON 写法)。</summary>
    [Fact]
    public void Extract_plan_on_windows_selects_windows_natives()
    {
        using TestHome testHome = new TestHome();
        string libs = Path.Combine(testHome.Dir, "libraries");
        string rel = Path.Combine("org", "example", "natives-windows.jar");
        Directory.CreateDirectory(Path.Combine(libs, "org", "example"));
        File.WriteAllText(Path.Combine(libs, rel), "");

        List<string> actual = Installer._natives_to_extract(B2dFixtures.V1165WithJars(), libs, "windows", "x64", Utils.OS_VERSION)
            .Select((KeyValuePair<string, PyDict> p) => p.Key)
            .ToList();
        Assert.Equal(2, actual.Count);
        Assert.All(actual, (string p) => Assert.EndsWith("natives-windows.jar", p, System.StringComparison.Ordinal));
    }

    // ——— 3. Installer._native_classifier_entry(_install_libraries 的调用点) ———

    /// <summary>1.16.5 形态 + osx + 目标架构 x64:分类器与下载条目都取 x86_64 那一份。</summary>
    [Fact]
    public void Install_libraries_entry_on_apple_silicon_uses_the_x86_64_classifier()
    {
        PyDict lib = Lwjgl321();
        PyDict downloads = Downloads(lib);
        string arch = LaunchArch.For(B2dFixtures.V1165WithJars(), "osx", "arm64", "14.5");
        Assert.Equal("x64", arch);
        PyDict entry = Installer._native_classifier_entry(lib, downloads, "osx", arch, out string classifier);
        Assert.Equal("natives-macos", classifier);
        Assert.Equal(B2dFixtures.LwjglNativesJar, Py.Str(entry.Get("path")));
        // 与 Intel Mac(osx + x64)同一个分类器与同一条下载条目。
        PyDict intel = Installer._native_classifier_entry(lib, downloads, "osx", "x64", out string intelClassifier);
        Assert.Equal("natives-macos", intelClassifier);
        Assert.Equal(Py.Str(intel.Get("path")), Py.Str(entry.Get("path")));
    }

    /// <summary>用进程架构 arm64 直接问(改动前的行为):老式分类器选不到,helper 返回 null。</summary>
    [Fact]
    public void Install_libraries_entry_returns_null_for_the_process_architecture_on_apple_silicon()
    {
        PyDict lib = Lwjgl321();
        Assert.Null(Installer._native_classifier_entry(lib, Downloads(lib), "osx", "arm64", out string classifier));
        Assert.True(string.IsNullOrEmpty(classifier));
    }

    /// <summary>没有 mac natives 的库(text2speech 形态):osx 下返回 null。</summary>
    [Fact]
    public void Install_libraries_entry_returns_null_without_mac_natives()
    {
        PyDict lib = NamedLib(B2dFixtures.V1165WithJars(), "com.mojang:text2speech:1.11.3");
        Assert.Null(Installer._native_classifier_entry(lib, Downloads(lib), "osx", "x64", out string classifier));
        Assert.True(string.IsNullOrEmpty(classifier));
    }

    /// <summary>${arch} 键:分类器里的 ${arch} 与回退查找都用传入的 arch 替换(源码行为:
    /// <c>NativeArchToken</c> 只把 x86 换成 "32",其余一律 "64";rank 里 arm64 只认 arm64 / aarch64 后缀)。
    /// mac 的老式键没有 ${arch},所以这条钉的是「参数确实流到了这两处替换」,以及 B2b 记过的现状:
    /// 带 ${arch} 的 Windows 键在 arm64 下换成 "-64" 之后同样选不到。</summary>
    [Theory]
    [InlineData("x64", "natives-windows-64")]
    [InlineData("x86", "natives-windows-32")]
    [InlineData("arm64", null)]
    public void Arch_token_is_used_for_substitution(string arch, string? expected)
    {
        PyDict lib = B2dFixtures.LibWithPathlessClassifier("com.example:archy:1.0", B2dFixtures.Dict("windows", "natives-windows-${arch}"), "natives-windows-${arch}");
        PyDict entry = Installer._native_classifier_entry(lib, Downloads(lib), "windows", arch, out string classifier);
        Assert.Equal(expected, classifier);
        Assert.Equal(expected == null, entry == null);
        if (expected != null)
        {
            // classifiers 里那条只有键、没有 path → 条目是空字典(调用方随后走 maven 兜底)。
            Assert.Equal(0, entry.Count);
            Assert.Equal(expected, Installer.SubstNativeKey("natives-windows-${arch}", arch));
        }
    }

    // ——— 4. Installer.NativesJarRelpath 的 arch 参数 ———

    /// <summary>1.16.5 形态:classifiers 里有该键 → 直接取它的 path,与 arch 无关。</summary>
    [Fact]
    public void Natives_jar_relpath_takes_the_classifier_path()
    {
        PyDict lib = Lwjgl321();
        Assert.Equal(B2dFixtures.LwjglNativesJar, Installer.NativesJarRelpath(lib, "natives-macos", "x64"));
        Assert.Equal(B2dFixtures.LwjglNativesJar, Installer.NativesJarRelpath(lib, "natives-macos", "arm64"));
    }

    /// <summary>回退查找(分类器键带 ${arch} 时)按传入的 arch 替换;maven 兜底路径也跟着变。</summary>
    [Theory]
    [InlineData("x64", "natives-macos-64")]
    [InlineData("x86", "natives-macos-32")]
    public void Natives_jar_relpath_falls_back_with_the_injected_arch(string arch, string expected)
    {
        PyDict lib = B2dFixtures.LibWithPathlessClassifier("com.example:archy:1.0", B2dFixtures.Dict("osx", "natives-macos-${arch}"), "natives-macos-${arch}");
        Assert.Equal(Utils.MavenArtifactPath("com.example:archy:1.0:" + expected), Installer.NativesJarRelpath(lib, expected, arch));
    }

    /// <summary>Windows 钉子:公开入口(不传 arch)与显式传本机 Utils.ARCH 的返回值逐项相等。</summary>
    [Fact]
    public void Natives_jar_relpath_public_entry_matches_the_injected_arch()
    {
        PyDict lib = Lwjgl321();
        Assert.Equal(Installer.NativesJarRelpath(lib, "natives-macos"), Installer.NativesJarRelpath(lib, "natives-macos", Utils.ARCH));
        PyDict archy = B2dFixtures.LibWithPathlessClassifier("com.example:archy:1.0", B2dFixtures.Dict("windows", "natives-windows-${arch}"), "natives-windows-${arch}");
        Assert.Equal(
            Installer.NativesJarRelpath(archy, "natives-windows-64"),
            Installer.NativesJarRelpath(archy, "natives-windows-64", Utils.ARCH));
    }

    // ——— 5. Cleaner._lib_relpaths(清理时的保留集合) ———

    /// <summary>Lead 明确要求的断言:osx + arm64 下 1.16.5 形态的 x86_64 natives jar 路径**在保留集合里**
    /// (清理时保留集合少一项就会误删文件)。
    /// 注:这个形状的 classifiers 里带 path,所以那条路径同时也被「classifiers 全量保留」那一段覆盖;
    /// 真正只靠目标架构才能算出来的情况见下面那条 ${arch} 用例。</summary>
    [Fact]
    public void Cleaner_keeps_the_x86_64_natives_jar_on_apple_silicon()
    {
        string libs = LibsDir();
        HashSet<string> keep = Cleaner._lib_relpaths(B2dFixtures.V1165WithJars(), libs, "osx", "arm64", "14.5");
        Assert.Contains(B2dFixtures.Low(libs, B2dFixtures.LwjglNativesJar), keep);
        Assert.Contains(B2dFixtures.Low(libs, B2dFixtures.ObjcBridgeJar), keep);
        Assert.Contains(keep, (string p) => p.EndsWith("lwjgl-3.2.1-natives-macos.jar", System.StringComparison.Ordinal));
        Assert.Contains(keep, (string p) => p.EndsWith("java-objc-bridge-1.0.0-natives-osx.jar", System.StringComparison.Ordinal));
        // 与 Intel Mac(osx + x64)完全一致。
        Assert.Equal(Cleaner._lib_relpaths(B2dFixtures.V1165WithJars(), libs, "osx", "x64", "14.5"), keep);
    }

    /// <summary>${arch} 分类器、且 classifiers 条目没有 path:保留集合里那一条只能由目标架构算出来
    /// (NativesJarRelpath 的 maven 兜底),所以这条能证明 arch 参数流到了 Cleaner 的 natives 分支。</summary>
    [Fact]
    public void Cleaner_keep_set_depends_on_the_target_architecture()
    {
        string libs = LibsDir();
        PyDict json = B2dFixtures.Version(B2dFixtures.LibWithPathlessClassifier("com.example:archy:1.0", B2dFixtures.Dict("osx", "natives-macos-${arch}"), "natives-macos-${arch}"));
        Assert.Equal("x64", LaunchArch.For(json, "osx", "arm64", "14.5"));
        HashSet<string> keep = Cleaner._lib_relpaths(json, libs, "osx", "arm64", "14.5");
        string mavenRel = Utils.MavenArtifactPath("com.example:archy:1.0:natives-macos-64");
        Assert.Contains(B2dFixtures.Low(libs, mavenRel), keep);
        Assert.Contains(keep, (string p) => p.EndsWith("archy-1.0-natives-macos-64.jar", System.StringComparison.Ordinal));
        Assert.DoesNotContain(keep, (string p) => p.Contains("natives-macos-arm64", System.StringComparison.Ordinal));
    }

    /// <summary>1.19.4 形态:保留集合与改动前一致(arm64 进程与 x64 进程算出来一样)。</summary>
    [Fact]
    public void Cleaner_keep_set_is_unchanged_for_modern_versions()
    {
        string libs = LibsDir();
        Assert.Equal(
            Cleaner._lib_relpaths(B2dFixtures.V1194(), libs, "osx", "x64", "14.5"),
            Cleaner._lib_relpaths(B2dFixtures.V1194(), libs, "osx", "arm64", "14.5"));
    }

    /// <summary>Windows 钉子:注入本机三字段 == 注入字面量 windows + x64。</summary>
    [Fact]
    public void Cleaner_keep_set_is_unchanged_on_this_machine()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        string libs = LibsDir();
        foreach (string id in B2dFixtures.LiveIds)
        {
            PyDict json = B2dFixtures.ById(id);
            Assert.Equal(
                Cleaner._lib_relpaths(json, libs, "windows", "x64", Utils.OS_VERSION),
                Cleaner._lib_relpaths(json, libs, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        }
    }

    // ——— 6. McLauncher._needs_natives(缺 natives 预检的判定) ———

    /// <summary>B2b 记的风险在 B2d 之后消失:osx + arm64 + 1.16.5 形态 → 需要 natives(预检重新生效)。
    /// 对照:用进程架构 arm64 直接判(改动前的写法)是 false,所以才会带着空 natives 目录启动。
    /// 期望值来源:源码行为(判定式)+ B2b 第 0 步(1.16.5 的 mac natives 写法)。</summary>
    [Fact]
    public void Needs_natives_is_true_on_apple_silicon_for_1_16_5()
    {
        PyDict json = B2dFixtures.V1165();
        Assert.True(McLauncher._needs_natives(json, "osx", "arm64", "14.5"));
        bool beforeB2d = Installer.Libs(json)
            .Where((PyDict lib) =>
            {
                object clientreq = lib.Get("clientreq");
                return (!(clientreq is bool) || (bool)clientreq) && Utils.CheckRules(lib.Get("rules"), null, "osx", "arm64", "14.5");
            })
            .Any((PyDict lib) => !string.IsNullOrEmpty(Installer.SelectNativeClassifier(lib, "osx", "arm64")));
        Assert.False(beforeB2d);
    }

    /// <summary>6 个老版本在 Apple 芯片上都需要 natives;1.19.4 与 26.3 不需要(新式库没有 natives 字段,
    /// 与 Windows 上的行为一致)。</summary>
    [Theory]
    [InlineData("1.7.10", true)]
    [InlineData("1.8.9", true)]
    [InlineData("1.12.2", true)]
    [InlineData("1.13.2", true)]
    [InlineData("1.16.5", true)]
    [InlineData("1.18.2", true)]
    [InlineData("1.19.4", false)]
    [InlineData("26.3", false)]
    public void Needs_natives_matches_the_version_shape_on_apple_silicon(string id, bool expected)
    {
        Assert.Equal(expected, McLauncher._needs_natives(B2dFixtures.ById(id), "osx", "arm64", "14.5"));
    }

    /// <summary>Windows:两个形状与改动前一致(1.16.5 → 需要;1.19.4 → 不需要),而且注入本机三字段与
    /// 注入字面量 windows + x64 逐项相等。</summary>
    [Theory]
    [InlineData("1.16.5", true)]
    [InlineData("1.19.4", false)]
    public void Needs_natives_is_unchanged_on_windows(string id, bool expected)
    {
        PyDict json = B2dFixtures.ById(id);
        Assert.Equal(expected, McLauncher._needs_natives(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        Assert.Equal(expected, McLauncher._needs_natives(json, "windows", "x64", Utils.OS_VERSION));
        Assert.Equal(
            McLauncher._needs_natives(json, "windows", "x64", Utils.OS_VERSION),
            McLauncher._needs_natives(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
    }

    // ——— 7. Preflight._natives_required / _natives_entry ———

    /// <summary>_check_natives 的判定:8 个版本按形状给出与 _needs_natives 相同的结论
    /// (这些夹具里没有 clientreq 字段,两处口径的差别在这批数据上不体现;差别本身写在源码注释里)。</summary>
    [Theory]
    [InlineData("1.7.10", true)]
    [InlineData("1.8.9", true)]
    [InlineData("1.12.2", true)]
    [InlineData("1.13.2", true)]
    [InlineData("1.16.5", true)]
    [InlineData("1.18.2", true)]
    [InlineData("1.19.4", false)]
    [InlineData("26.3", false)]
    public void Natives_required_follows_the_target_architecture(string id, bool expected)
    {
        PyDict json = B2dFixtures.ById(id);
        Assert.Equal(expected, Preflight._natives_required(json, "osx", "arm64", "14.5"));
        Assert.Equal(expected, Preflight._natives_required(json, "windows", "x64", Utils.OS_VERSION));
    }

    /// <summary>Windows 钉子:注入本机三字段 == 注入字面量 windows + x64。</summary>
    [Fact]
    public void Natives_required_is_unchanged_on_this_machine()
    {
        foreach (string id in B2dFixtures.LiveIds)
        {
            PyDict json = B2dFixtures.ById(id);
            Assert.Equal(
                Preflight._natives_required(json, "windows", "x64", Utils.OS_VERSION),
                Preflight._natives_required(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        }
    }

    /// <summary>_check_libraries 的 natives 分支:osx + 目标架构 x64 下 relpath 与下载条目都是 x86_64 那一份。</summary>
    [Fact]
    public void Preflight_natives_entry_on_apple_silicon_uses_the_x86_64_classifier()
    {
        PyDict lib = Lwjgl321();
        string arch = LaunchArch.For(B2dFixtures.V1165WithJars(), "osx", "arm64", "14.5");
        PyDict entry = Preflight._natives_entry(lib, Downloads(lib), "org.lwjgl:lwjgl:3.2.1", "osx", arch, out string relpath);
        Assert.Equal(B2dFixtures.LwjglNativesJar, relpath);
        Assert.Equal(B2dFixtures.LwjglNativesJar, Py.Str(entry.Get("path")));
        // 没有 natives 分类器时 relpath 与条目都是 null。
        Assert.Null(Preflight._natives_entry(lib, Downloads(lib), "org.lwjgl:lwjgl:3.2.1", "osx", "arm64", out string none));
        Assert.Null(none);
    }

    /// <summary>没有 mac natives 的库 + ${arch} 兜底:前者返回 null,后者用 maven 兜底路径。</summary>
    [Fact]
    public void Preflight_natives_entry_falls_back_to_maven()
    {
        PyDict archy = B2dFixtures.LibWithPathlessClassifier("com.example:archy:1.0", B2dFixtures.Dict("osx", "natives-macos-${arch}"), "natives-macos-${arch}");
        PyDict entry = Preflight._natives_entry(archy, Downloads(archy), "com.example:archy:1.0", "osx", "x64", out string relpath);
        Assert.NotNull(entry);
        Assert.Equal(Utils.MavenArtifactPath("com.example:archy:1.0:natives-macos-64"), relpath);
    }

    /// <summary>Windows 钉子:注入本机三字段 == 注入字面量 windows + x64(逐库比较 relpath)。</summary>
    [Fact]
    public void Preflight_natives_entry_is_unchanged_on_this_machine()
    {
        foreach (string id in B2dFixtures.LiveIds)
        {
            PyDict json = B2dFixtures.ById(id);
            string arch = LaunchArch.For(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION);
            foreach (PyDict lib in Installer.Libs(json))
            {
                object name = lib.Get("name");
                PyDict nativeEntry = Preflight._natives_entry(lib, Downloads(lib), name, Utils.OS_NAME, arch, out string relA);
                PyDict windowsEntry = Preflight._natives_entry(lib, Downloads(lib), name, "windows", "x64", out string relB);
                Assert.Equal(windowsEntry == null, nativeEntry == null);
                Assert.Equal(relB, relA);
            }
        }
    }

    // ——— 8. OfficialMigrate._library_relpaths(迁移时的库相对路径清单) ———

    /// <summary>混合链(子版本有 arm64 原生库 + 父版本 1.16.5 只有 x86_64)→ 整条链退 x64:
    /// 清单里出现父版本那条 x86_64 的 natives jar 相对路径,而且与 Intel Mac(osx + x64)完全一致。</summary>
    [Fact]
    public void Migrate_relpaths_are_computed_for_the_target_architecture()
    {
        List<PyDict> chain = B2dFixtures.ChainMixed();
        Assert.Equal("x64", LaunchArch.For(chain[1], "osx", "arm64", "14.5"));
        List<string> apple = OfficialMigrate._library_relpaths(chain, "osx", "arm64", "14.5");
        List<string> intel = OfficialMigrate._library_relpaths(chain, "osx", "x64", "14.5");
        Assert.Equal(intel, apple);
        Assert.Contains(apple, (string p) => p.EndsWith("natives-macos.jar", System.StringComparison.Ordinal));
        Assert.Contains(apple, (string p) => p.EndsWith("natives-osx.jar", System.StringComparison.Ordinal));
    }

    /// <summary>1.19.4 形态的链:目标架构 arm64,清单里没有老式 natives(新式库靠 classpath),
    /// 与改动前一致。</summary>
    [Fact]
    public void Migrate_relpaths_are_unchanged_for_modern_chains()
    {
        List<PyDict> chain = B2dFixtures.ChainModern();
        Assert.Equal("arm64", LaunchArch.For(chain[1], "osx", "arm64", "14.5"));
        Assert.Equal(OfficialMigrate._library_relpaths(chain, "osx", "x64", "14.5"), OfficialMigrate._library_relpaths(chain, "osx", "arm64", "14.5"));
    }

    /// <summary>顺序与去重照旧:子版本的库在前、父版本在后,重复路径只留第一次出现的那次
    /// (源码行为:原循环里的 HashSet + List 没有动)。</summary>
    [Fact]
    public void Migrate_relpaths_keep_the_original_order_and_dedup()
    {
        PyDict shared = PyDict.Of("name", "com.example:shared:1.0");
        List<PyDict> chain = new List<PyDict>
        {
            B2dFixtures.Version(shared, PyDict.Of("name", "com.example:child:1.0")),
            B2dFixtures.Version(shared, PyDict.Of("name", "com.example:parent:1.0"))
        };
        List<string> list = OfficialMigrate._library_relpaths(chain, "osx", "arm64", "14.5");
        Assert.Equal(
            new List<string>
            {
                Utils.MavenArtifactPath("com.example:shared:1.0"),
                Utils.MavenArtifactPath("com.example:child:1.0"),
                Utils.MavenArtifactPath("com.example:parent:1.0")
            },
            list);
    }

    /// <summary>Windows 钉子:注入本机三字段 == 注入字面量 windows + x64(逐项相等)。</summary>
    [Fact]
    public void Migrate_relpaths_are_unchanged_on_this_machine()
    {
        List<PyDict> chain = B2dFixtures.ChainMixed();
        Assert.Equal(
            OfficialMigrate._library_relpaths(chain, "windows", "x64", Utils.OS_VERSION),
            OfficialMigrate._library_relpaths(chain, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
    }
}
