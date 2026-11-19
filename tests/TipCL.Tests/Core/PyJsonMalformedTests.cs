// PyJson 畸形输入单测(audit/D 补覆盖)。
//
// 为什么补:PyJson 是**所有** 配置 / 词表 / 布局 / 更新清单的解析入口,它的 LoadsOr 是
// "读坏了就用默认值"的唯一兜底。既有 PyJsonTests 只钉了一条 "{bad json"。
// 这里钉三件事:
//   1) 真的坏掉的输入必须抛 PyException 家族的异常(而不是 NullReference / 越界访问);
//   2) LoadsOr 对**任何**坏输入都必须回落默认值 —— 它只 catch PyException,
//      若有非 PyException 漏出来,用户配置文件被截断一次就是启动器直接崩;
//   3) 合法但奇葩的输入(Python 方言:NaN / Infinity / 超大整数 / 重复键)要按 Python json 的口径解析。
//
// 纯内存字符串,无 IO / 无网络。
using System;
using System.Collections.Generic;
using System.Numerics;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests.Core;

public class PyJsonMalformedTests
{
    /// <summary>一份能覆盖解析器各分支的坏输入清单。</summary>
    public static IEnumerable<object[]> BadInputs() => new[]
    {
        new object[] { "" },
        new object[] { "   " },
        new object[] { "{" },
        new object[] { "}" },
        new object[] { "[1,]" },
        new object[] { "{\"a\":1,}" },
        new object[] { "{\"a\":}" },
        new object[] { "{,}" },
        new object[] { "\"unterminated" },
        new object[] { "1 2" },
        new object[] { "{} {}" },
        new object[] { "'single'" },       // 单引号不是 JSON
        new object[] { "\ufeff{}" },        // BOM:Python 明确报错
        new object[] { "{\"a\":1}}"},
    };

    [Theory]
    [MemberData(nameof(BadInputs))]
    public void Loads_ThrowsPyException_OnMalformedInput(string text)
    {
        // 只要求"是 PyException 家族" —— 具体子类(JSONDecodeError / TypeError)由解析路径决定
        Assert.ThrowsAny<PyException>(() => PyJson.Loads(text));
    }

    [Theory]
    [MemberData(nameof(BadInputs))]
    public void LoadsOr_FallsBackToDefault_OnMalformedInput(string text)
    {
        object dflt = PyDict.Of("fallback", true);
        // 关键断言:这里若抛出非 PyException(越界/空引用),说明解析器在坏输入上会**越过**兜底
        object got = PyJson.LoadsOr(text, dflt);
        Assert.Same(dflt, got);
    }

    [Fact]
    public void Loads_NullInput_ThrowsPyException_AndLoadsOrFallsBack()
    {
        Assert.ThrowsAny<PyException>(() => PyJson.Loads(null!));
        object dflt = PyDict.Of("d", 1L);
        Assert.Same(dflt, PyJson.LoadsOr(null!, dflt));
    }

    // ————— Python 方言:这些**不是**坏输入,但也不是标准 JSON —————

    [Fact]
    public void Loads_AcceptsPythonNanAndInfinity()
    {
        var dict = (PyDict)PyJson.Loads("{\"n\":NaN,\"p\":Infinity,\"m\":-Infinity}");
        Assert.True(double.IsNaN((double)dict.Req("n")));
        Assert.True(double.IsPositiveInfinity((double)dict.Req("p")));
        Assert.True(double.IsNegativeInfinity((double)dict.Req("m")));
    }

    [Fact]
    public void Loads_KeepsIntegersExact_EvenBeyondInt64()
    {
        object v = PyJson.Loads("123456789012345678901234567890");
        Assert.True(v is long || v is BigInteger, $"超大整数被解析成 {v?.GetType().Name}");
        string back = PyJson.Dumps(v!);
        Assert.Equal("123456789012345678901234567890", back);
    }

    [Fact]
    public void Loads_DuplicateKeys_LastOneWins()
    {
        var dict = (PyDict)PyJson.Loads("{\"k\":1,\"k\":2}");
        Assert.Equal(2L, dict.Req("k"));
    }

    [Fact]
    public void Loads_HandlesEmptyContainersAndDeepNesting()
    {
        Assert.Equal("{}", PyJson.Dumps(PyJson.Loads("{}")));
        Assert.Equal("[]", PyJson.Dumps(PyJson.Loads("[]")));

        string nested = new string('[', 40) + new string(']', 40);
        object deep = PyJson.Loads(nested);
        Assert.IsType<List<object>>(deep);
    }

    [Fact]
    public void Loads_RejectsTrailingNewlineOnlyAfterWhitespaceToleranceIsExact()
    {
        // 结构化空白(空格/制表/CR/LF)容忍;非空白尾巴必须报 Extra data
        Assert.Equal(1L, PyJson.Loads(" \r\n\t1 \n"));
        Assert.ThrowsAny<PyException>(() => PyJson.Loads("1x"));
    }

    [Fact]
    public void Dumps_IsAsciiEscapedByDefault_AndRoundTripsNonAscii()
    {
        string s = PyJson.Dumps(PyDict.Of("中文", "值"));
        Assert.DoesNotContain("中", s, StringComparison.Ordinal);
        // 反向:不转义模式下原文可见,且能再解析回来
        string u = PyJson.DumpsU(PyDict.Of("中文", "值"));
        Assert.Contains("中文", u, StringComparison.Ordinal);
        Assert.Equal("值", ((PyDict)PyJson.Loads(u)).Req("中文"));
    }

    [Fact]
    public void Dumps_NonSerializableTypeThrowsPyException()
    {
        Assert.ThrowsAny<PyException>(() => PyJson.Dumps(new object()));
    }
}