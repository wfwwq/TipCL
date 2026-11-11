using System;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A4:`Ai\TomlLite.cs`(A4 起参与编译)的用例。
/// TomlLite 是 internal,靠 TipCL.Core.csproj 里的 InternalsVisibleTo 访问(见 D-18)。
///
/// 期望值全部来自读过的源码 TomlLite.cs(1-546 行):
///   Parse 33-36(先把 \r\n 归一成 \n)、Doc 92-190(表头 [T] / [[A]]、键值、EndLine)、
///   Key 225-260(裸键只认 ASCII 字母数字与 _ -、支持点分与引号键)、
///   Value 262-315(true/false/整数/浮点,整数正则 304、浮点正则 308,都不匹配就 "unsupported value" 312)、
///   Assign 211-223(重复键 "duplicate key",表重复定义 "table redefined" 158)、
///   BasicString 326-353(转义见 Escape 355-393;不允许字面控制字符)、
///   LiteralString 395-410(不处理转义)、MultiBasic 412-464、MultiLiteral 466-481、
///   Array 483-509(允许尾逗号与跨行)、Inline 511-545。
/// 所有异常都是 FormatException(Err 38-41 返回 FormatException)。
/// 这些用例不联网、不起进程、不读真实目录。
/// </summary>
public class AiTomlLiteTests
{
    // ---------- 键值 ----------

    [Fact]
    public void Parses_basic_key_values_of_every_scalar_kind()
    {
        PyDict d = TomlLite.Parse(
            "s = \"hi\"\n" +
            "lit = 'raw\\n'\n" +
            "i = 42\n" +
            "neg = -5\n" +
            "zero = 0\n" +
            "big = 1_000\n" +
            "f = 1.5\n" +
            "e = 1e3\n" +
            "t = true\n" +
            "fa = false\n");

        Assert.Equal("hi", d.Get("s"));
        // 字面字符串不处理转义:LiteralString 395-410 直接取原文
        Assert.Equal("raw\\n", d.Get("lit"));
        Assert.Equal(42L, d.Get("i"));
        Assert.Equal(-5L, d.Get("neg"));
        Assert.Equal(0L, d.Get("zero"));
        // 下划线只在校验里允许,parse 前被去掉(306 行)
        Assert.Equal(1000L, d.Get("big"));
        Assert.Equal(1.5d, d.Get("f"));
        Assert.Equal(1000d, d.Get("e"));
        Assert.True((bool)d.Get("t"));
        Assert.False((bool)d.Get("fa"));
    }

    [Fact]
    public void Basic_string_escapes_follow_the_escape_table()
    {
        PyDict d = TomlLite.Parse(
            "a = \"x\\ty\"\n" +
            "b = \"l1\\nl2\"\n" +
            "c = \"q\\\"q\"\n" +
            "d = \"back\\\\slash\"\n" +
            "e = \"\\u4e2d\"\n" +
            "f = \"\\U0001F600\"\n");

        Assert.Equal("x\ty", d.Get("a"));
        Assert.Equal("l1\nl2", d.Get("b"));
        Assert.Equal("q\"q", d.Get("c"));
        Assert.Equal("back\\slash", d.Get("d"));
        Assert.Equal("\u4e2d", d.Get("e"));
        Assert.Equal(char.ConvertFromUtf32(0x1F600), d.Get("f"));
    }

    [Fact]
    public void Multiline_strings_strip_the_first_newline_and_keep_the_rest()
    {
        PyDict d = TomlLite.Parse(
            "a = \"\"\"\nline1\nline2\"\"\"\n" +
            "b = '''\nraw\\nkept'''\n");

        Assert.Equal("line1\nline2", d.Get("a"));
        // MultiLiteral 466-481:完全按原文取,反斜杠不转义
        Assert.Equal("raw\\nkept", d.Get("b"));
    }

    // ---------- 表 ----------

    [Fact]
    public void Tables_dotted_keys_and_quoted_keys_build_nested_dicts()
    {
        PyDict d = TomlLite.Parse(
            "# 注释与空行要跳过(WsNlComments 51-70)\n" +
            "\n" +
            "top = 1\n" +
            "a.b = 2\n" +
            "\"quoted key\" = 3\n" +
            "'lit key' = 4\n" +
            "\n" +
            "[tbl]\n" +
            "x = \"in table\"\n" +
            "\n" +
            "[tbl.sub]\n" +
            "y = true\n");

        Assert.Equal(1L, d.Get("top"));
        Assert.Equal(2L, ((PyDict)d.Get("a")).Get("b"));
        Assert.Equal(3L, d.Get("quoted key"));
        Assert.Equal(4L, d.Get("lit key"));

        PyDict tbl = (PyDict)d.Get("tbl");
        Assert.Equal("in table", tbl.Get("x"));
        Assert.True((bool)((PyDict)tbl.Get("sub")).Get("y"));
    }

    [Fact]
    public void Array_of_tables_appends_one_dict_per_header()
    {
        PyDict d = TomlLite.Parse(
            "[[fruits]]\n" +
            "name = \"apple\"\n" +
            "[[fruits]]\n" +
            "name = \"pear\"\n");

        var list = (System.Collections.Generic.List<object>)d.Get("fruits");
        Assert.Equal(2, list.Count);
        Assert.Equal("apple", ((PyDict)list[0]).Get("name"));
        Assert.Equal("pear", ((PyDict)list[1]).Get("name"));
    }

    // ---------- 数组与内联表 ----------

    [Fact]
    public void Arrays_allow_trailing_commas_newlines_and_comments()
    {
        PyDict d = TomlLite.Parse(
            "one = [1, 2, 3]\n" +
            "trail = [1, 2,]\n" +
            "multi = [\n  1, # 注释\n  2,\n]\n" +
            "empty = []\n" +
            "mixed = [\"a\", 1, true]\n");

        Assert.Equal(3, ((System.Collections.Generic.List<object>)d.Get("one")).Count);
        Assert.Equal(2, ((System.Collections.Generic.List<object>)d.Get("trail")).Count);
        Assert.Equal(2, ((System.Collections.Generic.List<object>)d.Get("multi")).Count);
        Assert.Empty((System.Collections.Generic.List<object>)d.Get("empty"));
        var mixed = (System.Collections.Generic.List<object>)d.Get("mixed");
        Assert.Equal("a", mixed[0]);
        Assert.Equal(1L, mixed[1]);
        Assert.True((bool)mixed[2]);
    }

    [Fact]
    public void Inline_tables_parse_including_the_empty_one()
    {
        PyDict d = TomlLite.Parse(
            "e = {}\n" +
            "t = {x = 1, y = \"z\", n = {deep = true}}\n");

        Assert.Equal(0, ((PyDict)d.Get("e")).Count);

        PyDict t = (PyDict)d.Get("t");
        Assert.Equal(1L, t.Get("x"));
        Assert.Equal("z", t.Get("y"));
        Assert.True((bool)((PyDict)t.Get("n")).Get("deep"));
    }

    // ---------- 空输入与 CRLF ----------

    [Fact]
    public void Empty_or_comment_only_documents_produce_an_empty_dict()
    {
        Assert.Equal(0, TomlLite.Parse("").Count);
        Assert.Equal(0, TomlLite.Parse("\n\n   \n").Count);
        Assert.Equal(0, TomlLite.Parse("# only a comment\n").Count);
    }

    [Fact]
    public void Crlf_is_normalised_before_parsing()
    {
        PyDict d = TomlLite.Parse("a = 1\r\nb = 2\r\n");

        Assert.Equal(1L, d.Get("a"));
        Assert.Equal(2L, d.Get("b"));
    }

    // ---------- 错误输入 ----------

    [Theory]
    [InlineData("a\n", "expected =")]
    [InlineData("a = \n", "bad value")]
    [InlineData("a = \"unterminated\n", "unterminated string")]
    [InlineData("a = \"x\\q\"\n", "bad escape")]
    [InlineData("a = 0x10\n", "unsupported value")]
    [InlineData("a = @\n", "bad value")]
    [InlineData("a = 1\na = 2\n", "duplicate key")]
    [InlineData("[t]\n[t]\n", "table redefined")]
    [InlineData("[t\na = 1\n", "expected ]")]
    [InlineData("= 1\n", "bad key")]
    public void Bad_input_throws_FormatException_with_the_documented_reason(string text, string expectedPrefix)
    {
        FormatException ex = Assert.Throws<FormatException>(() => TomlLite.Parse(text));
        Assert.StartsWith(expectedPrefix, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_key_is_reported_even_inside_an_inline_table()
    {
        // Assign 211-223 对内联表用的是同一个方法,所以重复键一样报 "duplicate key"
        FormatException ex = Assert.Throws<FormatException>(() => TomlLite.Parse("a = {x = 1, x = 2}\n"));
        Assert.StartsWith("duplicate key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Array_of_tables_conflicts_with_a_plain_key_of_the_same_name()
    {
        // 136-150 行:同名已有值且不是「PyDict 列表」时抛 "not an array of tables"
        FormatException ex = Assert.Throws<FormatException>(() => TomlLite.Parse("fruits = 1\n[[fruits]]\n"));
        Assert.StartsWith("not an array of tables", ex.Message, StringComparison.Ordinal);
    }
}
