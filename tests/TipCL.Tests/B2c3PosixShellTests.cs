using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c3 新文件 <c>Launcher\PosixShell.cs</c> 的用例(<c>Quote</c> / <c>JoinCommand</c>)。
///
/// 引号规则来自任务书给定的 POSIX 事实(不再重新讨论):把一个字符串安全地放进 sh 的单个词,
/// 就是整个用单引号包住、把里面每个单引号换成 '\''(收尾单引号 + 转义单引号 + 重开单引号),
/// 空串写成一对空单引号。
/// 期望值同时对照过源码 PosixShell.cs(本包新写,实现在 tests 之外只有这一处)。
/// 用例不联网、不起进程、不落盘。
/// </summary>
public class B2c3PosixShellTests
{
    /// <summary>逐字对照的引号结果(任务书给定的规则)。</summary>
    [Theory]
    [InlineData("abc", "'abc'")]
    [InlineData("", "''")]
    [InlineData(null, "''")]
    [InlineData("a b", "'a b'")]
    [InlineData("a\tb", "'a\tb'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("a'; rm -rf ~; echo '", "'a'\\''; rm -rf ~; echo '\\'''")]
    [InlineData("say \"hi\"", "'say \"hi\"'")]
    [InlineData("$HOME", "'$HOME'")]
    [InlineData("$(id)", "'$(id)'")]
    [InlineData("`id`", "'`id`'")]
    [InlineData("a\\b", "'a\\b'")]
    [InlineData("a\nb", "'a\nb'")]
    [InlineData("a; rm -rf /", "'a; rm -rf /'")]
    [InlineData("*?[a]", "'*?[a]'")]
    [InlineData("中文 实例", "'中文 实例'")]
    [InlineData("zi dian - 1.20.1", "'zi dian - 1.20.1'")]
    public void Quote_wraps_the_whole_value_in_single_quotes(string? input, string expected)
    {
        Assert.Equal(expected, PosixShell.Quote(input));
    }

    /// <summary>null 与空串一样(任务书:null 按空串处理),不是 null 引用,也不是空串。</summary>
    [Fact]
    public void Quote_of_null_is_the_empty_quoted_token()
    {
        Assert.Equal("''", PosixShell.Quote(null));
        Assert.Equal(PosixShell.Quote(""), PosixShell.Quote(null));
        Assert.Equal(2, PosixShell.Quote(null).Length);
    }

    /// <summary>任何输入的结果都是「以单引号开头、以单引号结尾」的一个词。</summary>
    [Fact]
    public void Quoted_values_start_and_end_with_a_single_quote()
    {
        string[] inputs = { "", "abc", "a b", "it's", "a'; rm -rf ~; echo '", "$HOME", "\n", "中文" };

        foreach (string input in inputs)
        {
            string got = PosixShell.Quote(input);
            Assert.StartsWith("'", got, StringComparison.Ordinal);
            Assert.EndsWith("'", got, StringComparison.Ordinal);
            Assert.True(got.Length >= 2);
        }
    }

    /// <summary>
    /// 最强的那条性质:把结果里所有 '\'' 转义抹掉之后,剩下**恰好两个单引号**(也就是最外那一对)。
    /// 只要还有第三个,就说明有单引号没被转义、可以跳出引号。
    /// </summary>
    [Fact]
    public void Every_inner_single_quote_is_escaped()
    {
        string[] inputs =
        {
            "it's", "''", "a'; rm -rf ~; echo '", "'", "x'y'z", "'开始", "结束'", "\u5154\u5b50'"
        };

        foreach (string input in inputs)
        {
            string got = PosixShell.Quote(input);
            string withoutEscapes = got.Replace("'\\''", "");
            Assert.Equal(2, withoutEscapes.Count(c => c == '\''));
            Assert.StartsWith("'", withoutEscapes, StringComparison.Ordinal);
            Assert.EndsWith("'", withoutEscapes, StringComparison.Ordinal);
        }
    }

    /// <summary>引号里的内容一字不改(中文、空格、$、反引号都原样保留)。</summary>
    [Fact]
    public void The_value_inside_the_quotes_is_verbatim()
    {
        string input = "中文 实例 $HOME `id` \"x\" 1.20.1\\";
        string got = PosixShell.Quote(input);

        Assert.Equal(input, got.Substring(1, got.Length - 2));
        Assert.Contains("中文", got, StringComparison.Ordinal);
        Assert.Contains("$HOME", got, StringComparison.Ordinal);
    }

    /// <summary>JoinCommand:空表 → 空串(null 也一样,不抛)。</summary>
    [Fact]
    public void JoinCommand_of_nothing_is_empty()
    {
        Assert.Equal("", PosixShell.JoinCommand(new List<string>()));
        Assert.Equal("", PosixShell.JoinCommand(null));
        Assert.Equal("", PosixShell.JoinCommand(Array.Empty<string>()));
    }

    /// <summary>JoinCommand:每个参数一个词,词之间恰好一个空格。</summary>
    [Fact]
    public void JoinCommandSeparatesWordsWithSingleSpaces()
    {
        Assert.Equal("'a'", PosixShell.JoinCommand(new[] { "a" }));
        Assert.Equal("'a' 'b' 'c'", PosixShell.JoinCommand(new[] { "a", "b", "c" }));
        Assert.Equal("'a b' 'c'", PosixShell.JoinCommand(new[] { "a b", "c" }));
        Assert.Equal("'' 'x'", PosixShell.JoinCommand(new[] { "", "x" }));
    }

    /// <summary>JoinCommand:null 元素按空串处理(空的一对单引号),不抛。</summary>
    [Fact]
    public void JoinCommand_treats_a_null_element_as_empty()
    {
        Assert.Equal("'' 'x'", PosixShell.JoinCommand(new[] { null, "x" }));
    }

    /// <summary>JoinCommand 保序,而且每个词都等于 Quote 的逐字结果。</summary>
    [Fact]
    public void JoinCommand_keeps_order_and_uses_Quote_for_every_word()
    {
        List<string> args = new List<string> { "$HOME", "a'; rm -rf ~; echo '", "中文", "" };
        string got = PosixShell.JoinCommand(args);

        Assert.Equal(string.Join(" ", args.Select(PosixShell.Quote)), got);
        foreach (string arg in args)
        {
            Assert.Contains(PosixShell.Quote(arg), got, StringComparison.Ordinal);
        }
    }

    /// <summary>危险参数不会以「裸露」的样子出现在结果里(它们只出现在引号内部)。</summary>
    [Fact]
    public void Hostile_words_never_escape_their_quotes()
    {
        string got = PosixShell.JoinCommand(new[] { "-i", "a'; rm -rf ~; echo '", "launch" });

        Assert.Equal("'-i' 'a'\\''; rm -rf ~; echo '\\''' 'launch'", got);
        Assert.DoesNotContain(" rm -rf ~; echo ", got.Replace("'a'\\''; rm -rf ~; echo '\\'''", ""), StringComparison.Ordinal);
    }
}
