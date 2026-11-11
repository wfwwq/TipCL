using System;
using System.Collections.Generic;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// Launcher 里纯函数的移植用例(不联网、不起进程、不碰真实用户目录)。
/// 每个期望值都来自读过的源码:
///   Utils.ReplacePlaceholders 1289-1301, CheckRules 1303-1363, SafeVersionId 467-475,
///   SafeChildPath 477-526, LooksLikePathSpelling 528-536, PosixNormPath 538-572,
///   DashedUuid 1020-1038, OfflineUuid 1040-1050, FormatSize 1052-1081, Hex 926-928,
///   MavenArtifactPath 1274-1287(引用 ParseMavenName 的 group:artifact:version:classifier 语义)。
/// </summary>
public class LauncherPureTests
{
    // ---------- 占位符替换 ${name} ----------

    [Theory]
    [InlineData("no placeholder here", false)]
    [InlineData("${name}", true)]
    [InlineData("a ${b} c", true)]
    [InlineData("${", false)]      // 必须是 ${ 加至少一个非 } 字符
    [InlineData("${}", false)]     // [^}]+ 至少要一个字符
    [InlineData("$name", false)]
    public void HasPlaceholder_matches_only_dollar_brace_forms(string text, bool expected)
    {
        Assert.Equal(expected, Utils.HasPlaceholder(text));
    }

    [Fact]
    public void ReplacePlaceholders_replaces_known_keys_and_keeps_the_rest()
    {
        PyDict mapping = PyDict.Of("name", "Steve", "ver", 20);

        Assert.Equal("hi Steve!", Utils.ReplacePlaceholders("hi ${name}!", mapping));
        // 未知键原样保留(ReplacePlaceholders 1289-1295)
        Assert.Equal("hi ${nope}!", Utils.ReplacePlaceholders("hi ${nope}!", mapping));
        // 值不是字符串时走 Py.Str
        Assert.Equal("v20", Utils.ReplacePlaceholders("v${ver}", mapping));
    }

    [Fact]
    public void ReplacePlaceholders_keeps_placeholders_whose_value_is_null_and_replaces_every_occurrence()
    {
        PyDict mapping = PyDict.Of("a", null, "b", "B");

        Assert.Equal("${a}", Utils.ReplacePlaceholders("${a}", mapping));
        Assert.Equal("B-B-B", Utils.ReplacePlaceholders("${b}-${b}-${b}", mapping));
    }

    // ---------- 版本 ID / 子路径安全 ----------

    [Fact]
    public void SafeVersionId_strips_and_accepts_plain_ids()
    {
        Assert.Equal("1.20.1", Utils.SafeVersionId("1.20.1"));
        Assert.Equal("abc", Utils.SafeVersionId("  abc  "));   // Py.Strip
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:20")]        // 含 ':' 一律拒绝
    public void SafeVersionId_rejects_illegal_ids(string raw)
    {
        PathEscapeError ex = Assert.Throws<PathEscapeError>(() => Utils.SafeVersionId(raw));
        Assert.Contains("非法", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeChildPath_accepts_a_child_and_blocks_escapes()
    {
        string baseDir = new PPath(TestHome.Root, "instances").ToString();

        Assert.Equal(new PPath(baseDir, "my-instance").ToString(), Utils.SafeChildPath(baseDir, "my-instance"));
        Assert.Throws<PathEscapeError>(() => Utils.SafeChildPath(baseDir, "../evil"));
    }

    // ---------- 路径拼写 / posix 规范化 ----------

    [Theory]
    [InlineData("abc", false)]
    [InlineData("abc.", true)]      // TrimEnd(' ', '.') 后变了
    [InlineData("abc ", true)]
    [InlineData("a/b", true)]
    [InlineData(".", true)]
    [InlineData("..", true)]
    public void LooksLikePathSpelling_is_true_for_anything_path_shaped(string text, bool expected)
    {
        Assert.Equal(expected, Utils.LooksLikePathSpelling(text));
    }

    [Theory]
    [InlineData("", ".")]
    [InlineData(".", ".")]
    [InlineData("/", "/")]
    [InlineData("a/b", "a/b")]
    [InlineData("a/./b", "a/b")]
    [InlineData("a/b/../c", "a/c")]
    [InlineData("//x", "//x")]           // 双斜杠是 POSIX 的 // 前缀,保留
    [InlineData("../../a", "../../a")]
    [InlineData("a/../../b", "../b")]
    [InlineData("/a/../..", "/")]        // 根目录吃不下 ..
    public void PosixNormPath_normalizes_like_posixpath_normpath(string path, string expected)
    {
        Assert.Equal(expected, Utils.PosixNormPath(path));
    }

    // ---------- UUID ----------

    [Fact]
    public void DashedUuid_normalizes_32_hex_chars_and_passes_anything_else_through()
    {
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", Utils.DashedUuid("0123456789ABCDEF0123456789abcdef"));
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", Utils.DashedUuid("01234567-89ab-cdef-0123-456789abcdef"));
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", Utils.DashedUuid("  0123456789abcdef0123456789ABCDEF  "));
        Assert.Equal("xyz", Utils.DashedUuid("xyz"));
        Assert.Equal(
            "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz",
            Utils.DashedUuid("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"));
    }

    /// <summary>
    /// offline UUID = MD5("OfflinePlayer:" + name),第 7 字节高 4 位置 3、第 9 字节高 2 位置 10,
    /// 再按 dashed 形式小写输出(Utils.cs 1040-1050)。期望值用 .NET MD5 独立算过一遍。
    /// </summary>
    [Fact]
    public void OfflineUuid_matches_the_offline_player_hash()
    {
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", Utils.OfflineUuid("Notch"));
    }

    [Fact]
    public void Hex_is_lowercase_two_digits_per_byte()
    {
        Assert.Equal("000fabff", Utils.Hex(new byte[] { 0x00, 0x0F, 0xAB, 0xFF }));
        Assert.Equal("", Utils.Hex(Array.Empty<byte>()));
    }

    // ---------- 体积格式化 ----------

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(-1L, "-1 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1.0 MB")]
    public void FormatSize_uses_binary_steps(object n, string expected)
    {
        Assert.Equal(expected, Utils.FormatSize(n));
    }

    [Fact]
    public void FormatSize_returns_question_mark_on_bad_input()
    {
        Assert.Equal("?", Utils.FormatSize("abc"));
        Assert.Equal("?", Utils.FormatSize(null));       // Py.Float(null) 抛 TypeError → "?"
    }

    // ---------- maven 坐标 ----------

    [Fact]
    public void MavenArtifactPath_builds_the_repository_layout()
    {
        Assert.Equal(
            "net/fabricmc/fabric-loader/0.15.0/fabric-loader-0.15.0.jar",
            Utils.MavenArtifactPath("net.fabricmc:fabric-loader:0.15.0"));
        Assert.Equal(
            "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar",
            Utils.MavenArtifactPath("org.lwjgl:lwjgl:3.3.1:natives-windows"));
        Assert.Equal(
            "com/example/lib/1.0/lib-1.0.json",
            Utils.MavenArtifactPath("com.example:lib:1.0", "json"));
    }

    // ---------- rules(version json 的 os 规则) ----------

    private static List<object> Rules(params PyDict[] rules)
    {
        List<object> list = new List<object>();
        foreach (PyDict rule in rules)
        {
            list.Add(rule);
        }
        return list;
    }

    private static PyDict OsRule(string name, string action)
    {
        return PyDict.Of("os", PyDict.Of("name", name), "action", action);
    }

    private static string OtherOs()
    {
        return Utils.OS_NAME == "windows" ? "osx" : "windows";
    }

    [Fact]
    public void CheckRules_is_true_for_empty_rules()
    {
        Assert.True(Utils.CheckRules(null));
        Assert.True(Utils.CheckRules(new List<object>()));
    }

    [Fact]
    public void CheckRules_matches_the_current_os_name()
    {
        Assert.True(Utils.CheckRules(Rules(OsRule(Utils.OS_NAME, "allow"))));
        Assert.False(Utils.CheckRules(Rules(OsRule(OtherOs(), "allow"))));
    }

    [Fact]
    public void CheckRules_honours_the_action_key()
    {
        Assert.False(Utils.CheckRules(Rules(OsRule(Utils.OS_NAME, "disallow"))));
        Assert.True(Utils.CheckRules(Rules(PyDict.Of("action", "allow"))));
        Assert.False(Utils.CheckRules(Rules(PyDict.Of("action", "disallow"))));
    }

    [Fact]
    public void CheckRules_lets_the_last_matching_rule_win()
    {
        Assert.True(Utils.CheckRules(Rules(OsRule(OtherOs(), "allow"), OsRule(Utils.OS_NAME, "allow"))));
        Assert.False(Utils.CheckRules(Rules(OsRule(Utils.OS_NAME, "allow"), OsRule(Utils.OS_NAME, "disallow"))));
    }

    [Fact]
    public void CheckRules_matches_os_arch()
    {
        Assert.True(Utils.CheckRules(Rules(PyDict.Of("os", PyDict.Of("arch", Utils.ARCH), "action", "allow"))));
        Assert.False(Utils.CheckRules(Rules(PyDict.Of("os", PyDict.Of("arch", "not-an-arch"), "action", "allow"))));
    }

    /// <summary>os.version 是正则(Utils.cs 1329-1345),".*" 一定匹配当前系统版本串。</summary>
    [Fact]
    public void CheckRules_matches_os_version_as_a_regex()
    {
        Assert.True(Utils.CheckRules(Rules(PyDict.Of("os", PyDict.Of("version", ".*"), "action", "allow"))));
        Assert.False(Utils.CheckRules(Rules(PyDict.Of("os", PyDict.Of("version", "(?!)"), "action", "allow"))));
    }

    [Fact]
    public void CheckRules_matches_the_features_map()
    {
        List<object> rules = Rules(PyDict.Of(
            "os", PyDict.Of("name", Utils.OS_NAME),
            "features", PyDict.Of("has_custom_resolution", true),
            "action", "allow"));

        Assert.True(Utils.CheckRules(rules, PyDict.Of("has_custom_resolution", true)));
        Assert.False(Utils.CheckRules(rules, PyDict.Of("has_custom_resolution", false)));
        Assert.False(Utils.CheckRules(rules));                       // features 缺键 == false
    }

    [Fact]
    public void CheckRules_requires_both_os_and_features_to_match()
    {
        List<object> rules = Rules(PyDict.Of(
            "os", PyDict.Of("name", OtherOs()),
            "features", PyDict.Of("f", true),
            "action", "allow"));

        Assert.False(Utils.CheckRules(rules, PyDict.Of("f", true)));
    }
}

/// <summary>与 Utils 无关但共用一组纯函数断言的补充用例(字符串 / 编码)。</summary>
public class LauncherStringTests
{
    [Fact]
    public void DecodeUtf8Strict_can_start_at_an_offset_and_rejects_broken_utf8()
    {
        byte[] raw = Encoding.UTF8.GetBytes("xx中文");

        Assert.Equal("中文", Utils.DecodeUtf8Strict(raw, 2));
        // 截断的 3 字节序列 → UnicodeDecodeError(Utils.cs 605-624)
        Assert.Throws<UnicodeDecodeError>(() => Utils.DecodeUtf8Strict(new byte[] { 0xE4, 0xB8 }));
    }

    [Fact]
    public void DecodeUtf8Sig_accepts_a_utf8_bom()
    {
        byte[] withBom = new byte[] { 0xEF, 0xBB, 0xBF, 0x61 };
        Assert.Equal("a", Utils.DecodeUtf8Sig(withBom));
    }

    [Fact]
    public void Cp437_is_available_or_silently_falls_back_to_utf8()
    {
        // net8 默认没有 437 代码页(Utils.cs 289-296 的 catch 分支);两种结果都算符合源码行为,
        // 只断言"不抛异常且能解码 ASCII"。
        Encoding cp = Utils.Cp437;
        Assert.NotNull(cp);
        Assert.Equal("ABC", cp.GetString(new byte[] { 0x41, 0x42, 0x43 }));
    }
}
