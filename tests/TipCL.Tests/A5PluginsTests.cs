using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A5:Plugins 里的纯逻辑(SemVer / PluginManifest / PluginDirs / SecretStore)。
/// 期望值全部由源码推出:SemVer.cs、PluginManifest.cs、PluginDirs.cs、SecretStore.cs。
/// 所有落盘都在 TestHome.Root 下的子目录里,不改 TIPCL_HOME。
/// </summary>
public class A5PluginsTests
{
    // ─────────────── SemVer ───────────────

    [Theory]
    [InlineData("1", 1L, 0L, 0L)]
    [InlineData("1.2", 1L, 2L, 0L)]
    [InlineData("v1.2.3", 1L, 2L, 3L)]
    [InlineData("  1.2.3  ", 1L, 2L, 3L)]
    [InlineData("1.2.3+build.9", 1L, 2L, 3L)]
    public void TryParse_fills_missing_parts_with_zero_and_ignores_the_v_prefix(string text, long major, long minor, long patch)
    {
        SemVer v = SemVer.TryParse(text);

        Assert.NotNull(v);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Empty(v.Pre);
    }

    [Fact]
    public void TryParse_reads_prerelease_identifiers_and_drops_build_metadata()
    {
        SemVer v = SemVer.TryParse("1.2.3-beta.1+build.7");

        Assert.NotNull(v);
        Assert.Equal(new[] { "beta", "1" }, v.Pre);
        Assert.Equal("1.2.3-beta.1", v.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x")]
    [InlineData("1.2.3-")]
    [InlineData("-1")]
    [InlineData("1..2")]
    public void TryParse_returns_null_for_illegal_versions(string text)
    {
        Assert.Null(SemVer.TryParse(text));
        Assert.False(SemVer.IsValid(text));
    }

    /// <summary>单独一条:null 不能走 [InlineData](xUnit1012 会报警告,而 NoWarn 只许保持现有 6 项)。</summary>
    [Fact]
    public void TryParse_returns_null_for_null()
    {
        Assert.Null(SemVer.TryParse(null));
        Assert.False(SemVer.IsValid(null));
    }

    [Fact]
    public void Parse_throws_ValueError_for_an_illegal_version()
    {
        ValueError ex = Assert.Throws<ValueError>(() => SemVer.Parse("nope"));
        Assert.Equal("\u4E0D\u662F\u5408\u6CD5\u7684\u7248\u672C\u53F7\uFF1Anope", ex.Message);
    }

    [Fact]
    public void Of_builds_a_release_version()
    {
        Assert.Equal("1.0.0", SemVer.Of(1).ToString());
        Assert.Equal("1.2.3", SemVer.Of(1, 2, 3).ToString());
    }

    [Fact]
    public void Release_is_greater_than_its_prerelease()
    {
        Assert.True(SemVer.Compare("1.2.3", "1.2.3-beta") > 0);
        Assert.True(SemVer.Compare("1.2.3-beta", "1.2.3") < 0);
        Assert.Equal(0, SemVer.Compare("1.2.3", "1.2.3+build"));
    }

    [Fact]
    public void Numeric_prerelease_identifiers_compare_numerically()
    {
        Assert.True(SemVer.Compare("1.0.0-alpha.2", "1.0.0-alpha.10") < 0);
        Assert.True(SemVer.Compare("1.0.0-alpha", "1.0.0-alpha.1") < 0);
        Assert.True(SemVer.Compare("1.0.0-alpha", "1.0.0-beta") < 0);
        // 数字标识比字母标识小
        Assert.True(SemVer.Compare("1.0.0-1", "1.0.0-alpha") < 0);
    }

    [Fact]
    public void Compare_falls_back_to_ordinal_string_comparison()
    {
        Assert.True(SemVer.Compare("abc", "abd") < 0);
        Assert.Equal(0, SemVer.Compare("junk", "junk"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("x")]
    public void Wildcard_ranges_match_anything(string range)
    {
        Assert.True(SemVer.Satisfies("9.9.9", range));
    }

    [Fact]
    public void Unparsable_version_never_satisfies_a_range()
    {
        Assert.False(SemVer.Satisfies("nope", "*"));
        Assert.False(SemVer.Satisfies(null, "^1.0.0"));
    }

    [Fact]
    public void Exact_range_requires_the_same_three_parts()
    {
        Assert.True(SemVer.Satisfies("1.2.3", "1.2.3"));
        Assert.False(SemVer.Satisfies("1.2.4", "1.2.3"));
    }

    [Theory]
    [InlineData("1.2.9", true)]
    [InlineData("1.9.0", true)]
    [InlineData("2.0.0", false)]
    [InlineData("1.2.2", false)]
    public void Caret_range_keeps_the_major(string version, bool expected)
    {
        Assert.Equal(expected, SemVer.Satisfies(version, "^1.2.3"));
    }

    [Fact]
    public void Caret_range_with_zero_major_locks_minor_and_patch()
    {
        Assert.True(SemVer.Satisfies("0.2.9", "^0.2.3"));
        Assert.False(SemVer.Satisfies("0.3.0", "^0.2.3"));
        Assert.True(SemVer.Satisfies("0.0.3", "^0.0.3"));
        Assert.False(SemVer.Satisfies("0.0.4", "^0.0.3"));
    }

    [Fact]
    public void Tilde_range_locks_minor()
    {
        Assert.True(SemVer.Satisfies("1.2.9", "~1.2.3"));
        Assert.False(SemVer.Satisfies("1.3.0", "~1.2.3"));
        Assert.True(SemVer.Satisfies("1.9.9", "~1"));
        Assert.False(SemVer.Satisfies("2.0.0", "~1"));
    }

    [Fact]
    public void Wildcard_parts_behave_like_a_partial_range()
    {
        Assert.True(SemVer.Satisfies("1.5.0", "1.x"));
        Assert.False(SemVer.Satisfies("2.0.0", "1.x"));
        Assert.True(SemVer.Satisfies("1.2.99", "1.2.*"));
        Assert.False(SemVer.Satisfies("1.3.0", "1.2.*"));
    }

    [Fact]
    public void Comparator_ranges_can_be_combined_with_spaces()
    {
        Assert.True(SemVer.Satisfies("1.5.0", ">=1.2.3 <2.0.0"));
        Assert.False(SemVer.Satisfies("2.0.0", ">=1.2.3 <2.0.0"));
        Assert.False(SemVer.Satisfies("1.2.3", ">1.2.3"));
        Assert.True(SemVer.Satisfies("1.2.4", ">1.2.3"));
    }

    [Fact]
    public void Operator_and_version_may_be_separated_by_a_space()
    {
        Assert.True(SemVer.Satisfies("1.5.0", ">= 1.2"));
        Assert.False(SemVer.Satisfies("1.0.0", ">= 1.2"));
    }

    [Fact]
    public void Partial_comparators_bump_the_missing_parts()
    {
        Assert.True(SemVer.Satisfies("1.3.0", ">1.2"));
        Assert.False(SemVer.Satisfies("1.2.9", ">1.2"));
        Assert.True(SemVer.Satisfies("1.2.9", "<=1.2"));
        Assert.False(SemVer.Satisfies("1.3.0", "<=1.2"));
    }

    [Fact]
    public void Hyphen_range_is_inclusive_on_the_upper_bound_when_fully_specified()
    {
        Assert.True(SemVer.Satisfies("1.5.0", "1.0.0 - 2.0.0"));
        Assert.True(SemVer.Satisfies("2.0.0", "1.0.0 - 2.0.0"));
        Assert.False(SemVer.Satisfies("2.0.1", "1.0.0 - 2.0.0"));
    }

    [Fact]
    public void Hyphen_range_with_a_partial_upper_bound_excludes_the_next_minor()
    {
        Assert.True(SemVer.Satisfies("2.0.5", "1.0 - 2.0"));
        Assert.False(SemVer.Satisfies("2.1.0", "1.0 - 2.0"));
    }

    [Fact]
    public void Double_pipe_is_an_or()
    {
        Assert.True(SemVer.Satisfies("1.0.0", "1.0.0 || 2.0.0"));
        Assert.True(SemVer.Satisfies("2.0.0", "1.0.0 || 2.0.0"));
        Assert.False(SemVer.Satisfies("3.0.0", "1.0.0 || 2.0.0"));
    }

    [Fact]
    public void Prerelease_only_matches_a_range_that_names_the_same_prerelease_base()
    {
        Assert.False(SemVer.Satisfies("1.2.3-beta.1", "^1.2.3"));
        Assert.True(SemVer.Satisfies("1.2.3-beta.2", ">=1.2.3-beta.1"));
        Assert.True(SemVer.Satisfies("1.2.3-beta.1", "^1.2.3-beta.1"));
    }

    [Fact]
    public void IsValidRange_detects_a_malformed_range()
    {
        Assert.True(SemVer.IsValidRange("^1.2.3"));
        Assert.True(SemVer.IsValidRange("*"));
        Assert.True(SemVer.IsValidRange(""));
        // 解析不了的通配/比较符会抛 FormatException → 判为非法范围
        Assert.False(SemVer.IsValidRange(">=abc"));
        Assert.False(SemVer.IsValidRange("1.0.0 ||"));
    }

    [Fact]
    public void Satisfies_returns_false_instead_of_throwing_for_a_malformed_range()
    {
        Assert.False(SemVer.Satisfies("1.2.3", ">=abc"));
        // 单个 "|" 不是合法的或运算:第二个 token 解析不了 → 整体 false(而不是抛异常)
        Assert.False(SemVer.Satisfies("1.2.3", "1.2.3 | 2.0.0"));
        Assert.True(SemVer.IsValidRange("1.2.3 | 2.0.0"));   // 但第一个 token 就直接判失败了,所以范围“合法”
    }

    // ─────────────── PluginManifest ───────────────

    private const string ValidNodeManifest = """
    {
      "id": "my-plugin",
      "name": "My Plugin",
      "version": "1.2.3",
      "runtime": "node",
      "main": "index.mjs",
      "engines": { "tipcl": ">=1.0.0" },
      "permissions": ["ai.tools"],
      "config": { "token": { "type": "string", "secret": true } },
      "dependencies": { "other-plugin": "^1.0.0" },
      "optionalDependencies": { "maybe-plugin": "~2.0.0" },
      "contributes": { "pages": [ { "id": "main", "label": "Home" } ] }
    }
    """;

    [Fact]
    public void Parse_reads_the_documented_fields_of_a_node_manifest()
    {
        PluginManifest m = PluginManifest.Parse(ValidNodeManifest);

        Assert.Equal("my-plugin", m.Id);
        Assert.Equal("My Plugin", m.Name);
        Assert.Equal("1.2.3", m.Version);
        Assert.Equal("node", m.Runtime);
        Assert.True(m.IsNode);
        Assert.False(m.IsDotnet);
        Assert.Equal(new[] { "index.mjs" }, m.Main);
        Assert.Equal(">=1.0.0", m.EngineTipcl);
        Assert.Equal("^1.0.0", m.Dependencies["other-plugin"]);
        Assert.Equal("~2.0.0", m.OptionalDependencies["maybe-plugin"]);
    }

    [Fact]
    public void Node_plugins_always_get_the_system_permission()
    {
        PluginManifest m = PluginManifest.Parse(ValidNodeManifest);

        Assert.Contains(PluginPermissions.AiTools, m.Permissions);
        Assert.Contains(PluginPermissions.System, m.Permissions);
        Assert.Equal(2, m.Permissions.Count);
    }

    [Fact]
    public void Ui_contributions_are_collected_with_their_kind()
    {
        PluginManifest m = PluginManifest.Parse(ValidNodeManifest);

        (string kind, PyDict spec) = Assert.Single(m.UiContributions);
        Assert.Equal("page", kind);
        Assert.Equal("main", Py.S(spec.Get("id")));
    }

    [Fact]
    public void Secret_config_flags_and_defaults_come_from_the_declaration()
    {
        PluginManifest m = PluginManifest.Parse(ValidNodeManifest);

        Assert.True(m.IsSecretConfig("token"));
        Assert.False(m.IsSecretConfig("nope"));
        Assert.Equal("", m.ConfigDefaults()["token"]);
    }

    [Fact]
    public void Default_config_values_follow_the_declared_type()
    {
        PluginManifest m = PluginManifest.Parse("""
        {
          "id": "cfg-plugin",
          "version": "1.0.0",
          "main": "m.mjs",
          "config": {
            "flag": { "type": "boolean" },
            "count": { "type": "integer" },
            "ratio": { "type": "number" },
            "text": { "type": "string" },
            "explicit": { "type": "string", "default": "given" }
          }
        }
        """);

        PyDict d = m.ConfigDefaults();
        Assert.Equal(false, d["flag"]);
        Assert.Equal(0L, d["count"]);
        Assert.Equal(0.0, d["ratio"]);
        Assert.Equal("", d["text"]);
        Assert.Equal("given", d["explicit"]);
    }

    [Fact]
    public void Coerce_config_validates_enum_and_range()
    {
        PluginManifest m = PluginManifest.Parse("""
        {
          "id": "coerce-plugin",
          "version": "1.0.0",
          "main": "m.mjs",
          "config": {
            "level": { "type": "integer", "min": 1, "max": 5 },
            "mode": { "type": "string", "enum": ["a", "b"] }
          }
        }
        """);

        Assert.Equal(3L, m.CoerceConfig("level", 3L));
        Assert.Equal(3L, m.CoerceConfig("level", "3"));
        Assert.Throws<ValueError>(() => m.CoerceConfig("level", 9L));
        Assert.Throws<ValueError>(() => m.CoerceConfig("level", 0L));
        Assert.Equal("a", m.CoerceConfig("mode", "a"));
        Assert.Throws<ValueError>(() => m.CoerceConfig("mode", "z"));
        Assert.Throws<ValueError>(() => m.CoerceConfig("undeclared", 1L));
    }

    [Fact]
    public void Missing_runtime_defaults_to_node_and_missing_name_to_the_id()
    {
        PluginManifest m = PluginManifest.Parse("""{ "id": "bare-plugin", "version": "1.0.0", "main": "m.mjs" }""");

        Assert.Equal("node", m.Runtime);
        Assert.Equal("bare-plugin", m.Name);
    }

    [Fact]
    public void Parse_rejects_json_that_is_not_an_object()
    {
        ValueError ex = Assert.Throws<ValueError>(() => PluginManifest.Parse("[1,2,3]"));
        Assert.Equal("tipcl-plugin.json 必须是 JSON 对象", ex.Message);
    }

    [Fact]
    public void Parse_rejects_broken_json()
    {
        ValueError ex = Assert.Throws<ValueError>(() => PluginManifest.Parse("{ not json"));
        Assert.StartsWith("tipcl-plugin.json 不是合法 JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_reports_an_empty_object_as_invalid()
    {
        ValueError ex = Assert.Throws<ValueError>(() => PluginManifest.Parse("{}"));
        Assert.StartsWith("\u63D2\u4EF6\u6E05\u5355\u6709\u8BEF\uFF1A", ex.Message, StringComparison.Ordinal);
        Assert.Contains("id \u5FC5\u987B\u662F 2~64", ex.Message, StringComparison.Ordinal);
        Assert.Contains("version \u5FC5\u987B\u662F semver", ex.Message, StringComparison.Ordinal);
        Assert.Contains("main \u6216 client", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_reports_each_problem_class()
    {
        List<string> errors = PluginManifest.Validate(PyJson.Loads("""
        {
          "id": "Bad-Id",
          "version": "1.0",
          "runtime": "python",
          "permissions": ["nope"]
        }
        """) as PyDict);

        Assert.Contains(errors, e => e.Contains("version \u5FC5\u987B\u662F semver", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("runtime \u53EA\u80FD\u662F node \u6216 dotnet", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("\u672A\u77E5\u6743\u9650", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_the_reserved_host_id()
    {
        List<string> errors = PluginManifest.Validate(PyJson.Loads(
            """{ "id": "host", "version": "1.0.0", "main": "m.mjs" }""") as PyDict);

        Assert.Contains(errors, e => e.Contains("id \u4E0D\u80FD\u662F host", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_requires_assembly_and_entry_type_for_dotnet_plugins()
    {
        List<string> errors = PluginManifest.Validate(PyJson.Loads(
            """{ "id": "dotnet-plugin", "version": "1.0.0", "runtime": "dotnet" }""") as PyDict);

        Assert.Contains(errors, e => e.Contains("\u5FC5\u987B\u58F0\u660E assembly", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("\u5FC5\u987B\u58F0\u660E entryType", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_an_entry_point_that_escapes_the_plugin_directory()
    {
        List<string> errors = PluginManifest.Validate(PyJson.Loads("""
        { "id": "escape-plugin", "version": "1.0.0", "main": "../evil.mjs" }
        """) as PyDict);

        Assert.Contains(errors, e => e.Contains("\u5165\u53E3\u8DEF\u5F84\u4E0D\u80FD\u8DF3\u51FA\u63D2\u4EF6\u76EE\u5F55", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_a_bad_main_type()
    {
        List<string> errors = PluginManifest.Validate(PyJson.Loads(
            """{ "id": "main-plugin", "version": "1.0.0", "main": 5 }""") as PyDict);

        Assert.Contains(errors, e => e.Contains("main \u5FC5\u987B\u662F\u5B57\u7B26\u4E32\u6216\u5B57\u7B26\u4E32\u6570\u7EC4", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_checks_dependency_ids_and_ranges()
    {
        List<string> errors = PluginManifest.Validate(PyJson.Loads("""
        {
          "id": "dep-plugin",
          "version": "1.0.0",
          "main": "m.mjs",
          "dependencies": { "BAD": "^1.0.0", "ok-plugin": "not a range |" },
          "optionalDependencies": "nope"
        }
        """) as PyDict);

        Assert.Contains(errors, e => e.Contains(" \u91CC\u7684 id \u4E0D\u5408\u6CD5\uFF1ABAD", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("\u4E0D\u662F\u5408\u6CD5\u7684\u7248\u672C\u8303\u56F4", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("optionalDependencies \u5FC5\u987B\u662F\u5BF9\u8C61", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_checks_config_fields()
    {
        List<string> errors = PluginManifest.Validate(PyJson.Loads("""
        {
          "id": "cfg-bad",
          "version": "1.0.0",
          "main": "m.mjs",
          "config": { "a": { "type": "weird" }, "b": "not-a-dict" }
        }
        """) as PyDict);

        Assert.Contains(errors, e => e.Contains("config.a.type \u53EA\u80FD\u662F", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("config.b \u5FC5\u987B\u662F\u5BF9\u8C61", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("my-plugin", true)]
    [InlineData("ab", true)]
    [InlineData("a1", true)]
    [InlineData("a", false)]
    [InlineData("Bad", false)]
    [InlineData("1", false)]
    [InlineData("..", false)]
    [InlineData("a..", false)]
    [InlineData("", false)]
    public void IsValidId_follows_the_id_regex(string id, bool expected)
    {
        Assert.Equal(expected, PluginManifest.IsValidId(id));
    }

    /// <summary>同上:null 单独一条,避免 xUnit1012。</summary>
    [Fact]
    public void IsValidId_rejects_null()
    {
        Assert.False(PluginManifest.IsValidId(null));
    }

    [Theory]
    [InlineData("index.mjs", true)]
    [InlineData("sub/dir/x.js", true)]
    [InlineData("sub\\dir\\x.js", true)]
    [InlineData("../x.js", false)]
    [InlineData("/abs/x.js", false)]
    [InlineData("C:/x.js", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    public void IsSafeRelPath_rejects_absolute_and_parent_traversal(string rel, bool expected)
    {
        Assert.Equal(expected, PluginManifest.IsSafeRelPath(rel));
    }

    [Fact]
    public void EngineOk_compares_against_the_running_launcher_version()
    {
        PluginManifest m = PluginManifest.Parse("""
        { "id": "engine-plugin", "version": "1.0.0", "main": "m.mjs", "engines": { "tipcl": ">=2.0.0" } }
        """);

        Assert.False(m.EngineOk("1.0.0", out string error));
        Assert.Contains("\u9700\u8981 TipCL >=2.0.0", error, StringComparison.Ordinal);
        Assert.True(m.EngineOk("2.1.0", out string okError));
        Assert.Null(okError);
    }

    [Fact]
    public void LoadDir_reads_the_manifest_from_the_given_directory()
    {
        string dir = NewTempDir("manifest");
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), ValidNodeManifest, new UTF8Encoding(false));

        PluginManifest m = PluginManifest.LoadDir(dir);

        Assert.Equal("my-plugin", m.Id);
    }

    [Fact]
    public void LoadDir_rejects_a_directory_without_a_manifest()
    {
        string dir = NewTempDir("manifest-missing");

        ValueError ex = Assert.Throws<ValueError>(() => PluginManifest.LoadDir(dir));
        Assert.StartsWith("\u627E\u4E0D\u5230 tipcl-plugin.json\uFF1A", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_skips_a_utf8_bom()
    {
        PluginManifest m = PluginManifest.Parse("\uFEFF" + ValidNodeManifest);

        Assert.Equal("my-plugin", m.Id);
    }

    [Fact]
    public void Permission_constants_are_the_documented_nine()
    {
        Assert.Equal(9, PluginPermissions.All.Length);
        Assert.True(PluginPermissions.IsKnown(PluginPermissions.System));
        Assert.False(PluginPermissions.IsKnown("made-up"));
        Assert.Equal("made-up", PluginPermissions.Describe("made-up"));
    }

    // ─────────────── PluginDirs ───────────────

    [Fact]
    public void PluginDirs_builds_every_documented_subdirectory_under_the_given_root()
    {
        string home = NewTempDir("dirs");
        var dirs = new PluginDirs(home);

        Assert.Equal(Path.GetFullPath(home), dirs.Home);
        Assert.Equal(Path.Combine(home, "plugins"), dirs.PluginsRoot);
        Assert.Equal(Path.Combine(home, "plugins", ".staging"), dirs.StagingRoot);
        Assert.Equal(Path.Combine(home, "plugins", ".backup"), dirs.BackupRoot);
        Assert.Equal(Path.Combine(home, "plugin-data"), dirs.DataRoot);
        Assert.Equal(Path.Combine(home, "plugin-logs"), dirs.LogsRoot);
        Assert.Equal(Path.Combine(home, "plugin-secrets"), dirs.SecretsRoot);
        Assert.Equal(Path.Combine(home, "plugins.json"), dirs.StateFile);
        Assert.Equal(Path.Combine(home, "runtime"), dirs.RuntimeRoot);
        Assert.Equal(Path.Combine(home, "runtime", "node"), dirs.NodeDir);
        Assert.Equal(Path.Combine(home, "runtime", "plugin-host"), dirs.HostDir);
    }

    [Fact]
    public void PluginDirs_per_plugin_paths_are_derived_from_the_root()
    {
        string home = NewTempDir("dirs-id");
        var dirs = new PluginDirs(home);

        Assert.Equal(Path.Combine(home, "plugins", "my-plugin"), dirs.PluginDir("my-plugin"));
        Assert.Equal(Path.Combine(home, "plugin-data", "my-plugin"), dirs.DataDir("my-plugin"));
        Assert.Equal(Path.Combine(home, "plugin-data", "my-plugin", "cache"), dirs.CacheDir("my-plugin"));
        Assert.Equal(Path.Combine(home, "plugin-data", "my-plugin", "cache", "attachments"), dirs.AttachmentsDir("my-plugin"));
        Assert.Equal(Path.Combine(home, "plugin-logs", "my-plugin.log"), dirs.LogFile("my-plugin"));
        Assert.Equal(Path.Combine(home, "plugin-secrets", "my-plugin.bin"), dirs.SecretsFile("my-plugin"));
        Assert.Equal(Path.Combine(home, "plugin-secrets", "_host.bin"), dirs.HostSecretsFile);
    }

    [Fact]
    public void PluginDirs_rejects_an_invalid_plugin_id_before_building_a_path()
    {
        var dirs = new PluginDirs(NewTempDir("dirs-bad"));

        ValueError ex = Assert.Throws<ValueError>(() => dirs.PluginDir("../escape"));
        Assert.StartsWith("\u63D2\u4EF6 id \u4E0D\u5408\u6CD5\uFF1A", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ValueError>(() => dirs.DataDir("BAD"));
        Assert.Throws<ValueError>(() => dirs.LogFile(""));
        Assert.Throws<ValueError>(() => dirs.SecretsFile("a"));
    }

    [Fact]
    public void PluginDirs_defaults_to_the_isolated_test_home()
    {
        var dirs = new PluginDirs();

        Assert.Equal(Path.GetFullPath(TestHome.Root), dirs.Home);
        Assert.StartsWith(TestHome.Root, dirs.PluginsRoot, StringComparison.Ordinal);
    }

    // ─────────────── SecretStore ───────────────

    [Fact]
    public void SecretStore_round_trips_values_in_a_file_next_to_the_given_path()
    {
        string path = Path.Combine(NewTempDir("secrets"), "my-plugin.bin");
        var store = new SecretStore(path);
        try
        {
            store.Set("config:token", "s3cret-value");

            Assert.True(File.Exists(path), "密钥文件必须落在指定路径:" + path);
            Assert.StartsWith(TestHome.Root, path, StringComparison.Ordinal);
            Assert.Equal("s3cret-value", store.Get("config:token"));
            Assert.Equal(new List<string> { "config:token" }, store.Names());

            // 重新打开同一个文件仍然读得出来(走平台的加解密分支)
            Assert.Equal("s3cret-value", new SecretStore(path).Get("config:token"));

            store.Set("config:token", "");
            Assert.Null(store.Get("config:token"));
            Assert.False(File.Exists(path), "最后一个 key 删掉后文件应被删除");
        }
        finally
        {
            store.DeleteAll();
        }
    }

    [Fact]
    public void SecretStore_file_header_names_the_platform_protection_used()
    {
        string path = Path.Combine(NewTempDir("secrets-head"), "head.bin");
        var store = new SecretStore(path);
        try
        {
            store.Set("k", "v");

            byte[] raw = File.ReadAllBytes(path);
            string head = Encoding.ASCII.GetString(raw, 0, 4);
            // SecretStore.cs 19/21/84-93 行:写文件时 Windows 用 MagicDpapi("TCS1"),
            // 其它平台用 MagicPlain("TCS0")+XOR(B1 会把 XOR 换成 AES-GCM + Keychain)。
            bool runningOnWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            Assert.Equal(runningOnWindows ? "TCS1" : "TCS0", head);
        }
        finally
        {
            store.DeleteAll();
        }
    }

    [Fact]
    public void SecretStore_path_property_is_the_ctor_argument()
    {
        string path = Path.Combine(TestHome.Root, "plugin-secrets", "probe.bin");

        Assert.Equal(path, new SecretStore(path).Path);
    }

    [Fact]
    public void SecretStore_reads_defaults_to_empty_for_a_missing_or_short_file()
    {
        string dir = NewTempDir("secrets-empty");
        var store = new SecretStore(Path.Combine(dir, "missing.bin"));
        Assert.Null(store.Get("anything"));
        Assert.Empty(store.Names());

        string shortPath = Path.Combine(dir, "short.bin");
        File.WriteAllBytes(shortPath, new byte[] { 1, 2 });
        Assert.Null(new SecretStore(shortPath).Get("anything"));

        string badMagic = Path.Combine(dir, "bad.bin");
        File.WriteAllBytes(badMagic, Encoding.ASCII.GetBytes("XXXXjunkjunk"));
        Assert.Null(new SecretStore(badMagic).Get("anything"));
    }

    // ─────────────── 帮助函数 ───────────────

    /// <summary>在 TestHome.Root 下开一个子目录 —— 不碰 TIPCL_HOME,也不碰用户真实数据目录。</summary>
    private static string NewTempDir(string name)
    {
        string dir = Path.Combine(TestHome.Root, "a5-plugins", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
