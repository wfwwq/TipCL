// 第 7 轮(变异测试补洞):PyJson 对**被截断的关键字**必须兜底。
//
// 这是变异测试抓到的真实覆盖漏洞:把 `Match()` 里"先比长度再比内容"的写法改回
// `_s.Substring(i, w.Length)`(截断时抛 ArgumentOutOfRangeException),**原来的测试全绿** ——
// 因为 PyJsonTests / PyJsonMalformedTests 的数据集里没有 "nul" / "tru" / "fals" / "nu" 这类
// "只写了一半的字面量"。而 LoadsOr 只 catch PyException:`ArgumentOutOfRangeException` 会直接
// 漏给调用方 ⇒ 配置文件被截断一次(断电/磁盘满/并发写)= 启动器崩,不是回默认配置。
//
// 这里把截断字面量的整组输入钉死(含数组/对象里出现截断的情况)。
using System;
using System.Collections.Generic;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests.Core;

public class PyJsonTruncatedKeywordTests
{
    /// <summary>被截断的字面量:从"最后一个字符被切掉"到"只剩第一个字符"都有。</summary>
    public static IEnumerable<object[]> TruncatedLiterals() => new[]
    {
        new object[] { "nul" },       // null 少一个 l
        new object[] { "nu" },
        new object[] { "n" },
        new object[] { "tru" },       // true 少一个 e
        new object[] { "tr" },
        new object[] { "fals" },      // false 少一个 e
        new object[] { "fa" },
        new object[] { "NaN".Substring(0, 2) },
        new object[] { "Infinit" },   // Infinity 少一个 y
        new object[] { "-Infinit" },
        new object[] { "[nul]" },     // 数组里出现截断
        new object[] { "{\"a\":tru}" },
        new object[] { "{\"a\":fals}" },
        new object[] { "[1,nul]" },
        new object[] { "  nul  " },   // 前后空白不该救回来
    };

    [Theory]
    [MemberData(nameof(TruncatedLiterals))]
    public void TruncatedLiteralThrowsOnlyCatchablePyExceptions(string text)
    {
        // 关键:异常类型必须是 PyException 家族。若实现改成"按长度切子串再比",
        // 这里出来的会是 ArgumentOutOfRangeException —— LoadsOr 就兜不住,调用方直接崩。
        Assert.ThrowsAny<PyException>(() => PyJson.Loads(text));
    }

    [Theory]
    [MemberData(nameof(TruncatedLiterals))]
    public void LoadsOrFallsBackToDefaultOnTruncatedLiteral(string text)
    {
        object dflt = PyDict.Of("fallback", true);
        Assert.Same(dflt, PyJson.LoadsOr(text, dflt));
    }

    [Fact]
    public void TruncatedLiteralInsideAConfigLikeDocumentDoesNotEscapeTheCatch()
    {
        // 真实场景:config.json 被截断在 "first_run": tru 处
        const string halfWritten = "{\"first_run\": tru";
        object got = PyJson.LoadsOr(halfWritten, "DEFAULT");
        Assert.Equal("DEFAULT", got);
    }
}