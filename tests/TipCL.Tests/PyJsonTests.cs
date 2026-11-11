using System;
using System.Collections.Generic;
using System.Numerics;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A2 步骤 4-3:PyJson 行为。期望值全部由 <c>PyJson.cs</c> / <c>Py.cs</c> 源码行为推出
/// (解析类型:整数→long、超 long→BigInteger、小数→double;序列化分隔符 ", " / ": ";
/// ensureAscii 默认把 &gt;~ 的字符写成小写 \uXXXX;NaN/Infinity 走 Python 拼写)。
/// </summary>
public class PyJsonTests
{
    [Fact]
    public void Loads_PicksTheSameClrTypesAsTheParserSource()
    {
        var v = (List<object>)PyJson.Loads("[1, -7, 2.5, \"x\", true, false, null]");

        Assert.Equal(7, v.Count);
        Assert.IsType<long>(v[0]);
        Assert.Equal(1L, v[0]);
        Assert.IsType<long>(v[1]);
        Assert.Equal(-7L, v[1]);
        Assert.IsType<double>(v[2]);
        Assert.Equal(2.5d, v[2]);
        Assert.IsType<string>(v[3]);
        Assert.Equal("x", v[3]);
        Assert.IsType<bool>(v[4]);
        Assert.Equal(true, v[4]);
        Assert.IsType<bool>(v[5]);
        Assert.Equal(false, v[5]);
        Assert.Null(v[6]);
    }

    [Fact]
    public void Loads_IntsInsideLongRangeStayLong_BeyondItBecomeBigInteger()
    {
        Assert.Equal(9223372036854775807L, PyJson.Loads("9223372036854775807"));

        object big = PyJson.Loads("123456789012345678901234567890");
        Assert.IsType<BigInteger>(big);
        Assert.Equal(BigInteger.Parse("123456789012345678901234567890"), (BigInteger)big);
    }

    [Fact]
    public void Loads_ReadsPythonStyleSpecialFloats()
    {
        Assert.Equal(double.NaN, (double)PyJson.Loads("NaN"));
        Assert.Equal(double.PositiveInfinity, (double)PyJson.Loads("Infinity"));
        Assert.Equal(double.NegativeInfinity, (double)PyJson.Loads("-Infinity"));
    }

    [Fact]
    public void Dumps_UsesCommaSpaceAndColonSpaceByDefault()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        Assert.Equal("{\"a\": 1, \"b\": 2}", PyJson.Dumps(d));
        Assert.Equal("[1, 2.5, \"x\", true, null]", PyJson.Dumps(new List<object> { 1L, 2.5d, "x", true, null }));
    }

    [Fact]
    public void Dumps_ScalarLiteralsFollowTheWriteSource()
    {
        Assert.Equal("null", PyJson.Dumps(null));
        Assert.Equal("true", PyJson.Dumps(true));
        Assert.Equal("false", PyJson.Dumps(false));
        Assert.Equal("1", PyJson.Dumps(1));
        Assert.Equal("-5", PyJson.Dumps(-5L));
        Assert.Equal("2.5", PyJson.Dumps(2.5d));
        Assert.Equal("0.1", PyJson.Dumps(0.1d));
        Assert.Equal("[]", PyJson.Dumps(new List<object>()));
        Assert.Equal("{}", PyJson.Dumps(new PyDict()));
        Assert.Equal("\"s\"", PyJson.Dumps("s"));
    }

    [Fact]
    public void Dumps_NonFiniteFloatsUsePythonSpelling()
    {
        Assert.Equal("NaN", PyJson.Dumps(double.NaN));
        Assert.Equal("Infinity", PyJson.Dumps(double.PositiveInfinity));
        Assert.Equal("-Infinity", PyJson.Dumps(double.NegativeInfinity));
        // NaN/Infinity 能原样往返(Loads 认得这三个字面量)
        Assert.Equal("NaN", PyJson.Dumps(PyJson.Loads("NaN")));
        Assert.Equal("Infinity", PyJson.Dumps(PyJson.Loads("Infinity")));
        Assert.Equal("-Infinity", PyJson.Dumps(PyJson.Loads("-Infinity")));
    }

    [Fact]
    public void Dumps_DefaultEscapesNonAsciiAsLowercaseHex()
    {
        // 名 U+540D 字 U+5B57 中 U+4E2D 文 U+6587
        Assert.Equal("\"\\u540d\\u5b57\"", PyJson.Dumps("名字"));
        Assert.Equal("\"\\u4e2d\\u6587\"", PyJson.Dumps("中文"));
        Assert.Equal("\"\\u00e9\"", PyJson.Dumps("\u00e9"));
        // '~' 是边界:源码里判的是 c > '~',所以 '~' 原样保留
        Assert.Equal("\"~\"", PyJson.Dumps("~"));
    }

    [Fact]
    public void DumpsU_KeepsNonAsciiUnescaped()
    {
        Assert.Equal("\"名字\"", PyJson.DumpsU("名字"));

        PyDict d = PyDict.Of("name", "中文", "n", 1);
        Assert.Equal("{\"name\": \"中文\", \"n\": 1}", PyJson.DumpsU(d));
    }

    [Fact]
    public void Dumps_EscapesTheControlAndQuoteCharactersFromWriteStr()
    {
        Assert.Equal("\"a\\\"b\\\\c\\nd\"", PyJson.Dumps("a\"b\\c\nd"));
        Assert.Equal("\"\\t\"", PyJson.Dumps("\t"));
        Assert.Equal("\"\\r\"", PyJson.Dumps("\r"));
        Assert.Equal("\"\\b\"", PyJson.Dumps("\b"));
        Assert.Equal("\"\\f\"", PyJson.Dumps("\f"));
        // 源码里 case 走完之后还有 c < ' ' 的兜底分支
        Assert.Equal("\"\\u0001\"", PyJson.Dumps("\u0001"));
    }

    [Fact]
    public void Dumps_IndentInsertsNewlinesAndSpacing()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n}", PyJson.Dumps(d, 2));
        Assert.Equal("{\n    \"a\": 1,\n    \"b\": 2\n}", PyJson.Dumps(d, 4));
        Assert.Equal("[\n  1,\n  2\n]", PyJson.Dumps(new List<object> { 1L, 2L }, 2));
    }

    [Fact]
    public void Dumps_SortKeysOrdersByOrdinalNotInsertion()
    {
        PyDict d = PyDict.Of("b", 1, "a", 2, "C", 3);

        // 插入顺序
        Assert.Equal("{\"b\": 1, \"a\": 2, \"C\": 3}", PyJson.Dumps(d));
        // 序数排序:'C'(0x43) < 'a'(0x61) < 'b'(0x62)
        Assert.Equal("{\"C\": 3, \"a\": 2, \"b\": 1}", PyJson.Dumps(d, null, true, true));
    }

    [Fact]
    public void Dumps_ItemSepAndKeySepOverridesWin()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        Assert.Equal("{\"a\":1,\"b\":2}", PyJson.Dumps(d, null, true, false, ",", ":"));
    }

    [Fact]
    public void Dumps_BigIntegerAndPlainDictionaryUseTheInvariantFormatter()
    {
        Assert.Equal("123456789012345678901234567890",
                     PyJson.Dumps(BigInteger.Parse("123456789012345678901234567890")));

        var pairs = new Dictionary<string, object> { { "a", 1L } };
        Assert.Equal("{\"a\": 1}", PyJson.Dumps(pairs));
    }

    [Fact]
    public void Dumps_UnserializableObjectThrowsTypeError()
    {
        TypeError ex = Assert.Throws<TypeError>(() => PyJson.Dumps(new object()));
        Assert.Equal("TypeError", ex.PyType);
        Assert.Equal("Object of type Object is not JSON serializable", ex.Message);
    }

    [Fact]
    public void Loads_NullInputThrowsTheSameTypeErrorAsSource()
    {
        TypeError ex = Assert.Throws<TypeError>(() => PyJson.Loads(null));
        Assert.Equal("the JSON object must be str, bytes or bytearray, not NoneType", ex.Message);
    }

    [Fact]
    public void Loads_ExtraDataThrowsJsonDecodeErrorWithTheSourcePositionFormat()
    {
        JSONDecodeError ex = Assert.Throws<JSONDecodeError>(() => PyJson.Loads("{} {}"));

        Assert.IsAssignableFrom<ValueError>(ex);
        Assert.Equal("JSONDecodeError", ex.PyType);
        // pos=3,num2=-1 → line 1 column (3-(-1)) (char 3)
        Assert.Equal("Extra data: line 1 column 4 (char 3)", ex.Message);
    }

    [Fact]
    public void Loads_LeadingBomIsRejected()
    {
        JSONDecodeError ex = Assert.Throws<JSONDecodeError>(() => PyJson.Loads("\uFEFF{}"));
        Assert.Equal("Unexpected UTF-8 BOM (decode using utf-8-sig): line 1 column 1 (char 0)", ex.Message);
    }

    [Fact]
    public void Loads_EmptyInputThrowsJsonDecodeError()
    {
        JSONDecodeError ex = Assert.Throws<JSONDecodeError>(() => PyJson.Loads(""));
        Assert.Equal("Expecting value: line 1 column 1 (char 0)", ex.Message);
    }

    [Fact]
    public void Loads_RejectsJson5StyleTolerance()
    {
        // 单引号、尾随逗号、无引号键都不接受(与 Python json 一致)
        Assert.Throws<JSONDecodeError>(() => PyJson.Loads("{'a': 1}"));
        Assert.Throws<JSONDecodeError>(() => PyJson.Loads("[1,]"));
        Assert.Throws<JSONDecodeError>(() => PyJson.Loads("{a: 1}"));
        Assert.Throws<JSONDecodeError>(() => PyJson.Loads("\"unterminated"));
    }

    [Fact]
    public void LoadsOr_SwallowsOnlyPyExceptions()
    {
        Assert.Equal("dflt", PyJson.LoadsOr("not json", "dflt"));
        Assert.Null(PyJson.LoadsOr("not json", null));
        Assert.Equal(1L, PyJson.LoadsOr("1", "dflt"));
        Assert.Equal("ok", PyJson.LoadsOr("\"ok\"", "dflt"));
    }

    [Fact]
    public void RoundTrip_NestedChineseDocumentSurvivesDumpsThenLoads()
    {
        const string src = "{\"list\": [1, {\"k\": [true, null, \"中文\"]}], \"n\": 3, \"f\": 0.5}";

        object first = PyJson.Loads(src);
        Assert.Equal(src, PyJson.DumpsU(first));

        object second = PyJson.Loads(PyJson.DumpsU(first));
        var d1 = Assert.IsType<PyDict>(first);
        var d2 = Assert.IsType<PyDict>(second);
        Assert.Equal(3, d2.Count);
        Assert.Equal(3L, d2["n"]);
        Assert.Equal(0.5d, d2["f"]);
        Assert.Equal(PyJson.DumpsU(d1), PyJson.DumpsU(d2));

        var list = Assert.IsType<List<object>>(d2["list"]);
        var inner = Assert.IsType<PyDict>(list[1]);
        var innerList = Assert.IsType<List<object>>(inner["k"]);
        Assert.Equal("中文", innerList[2]);
    }

    [Fact]
    public void RoundTrip_AsciiEscapedOutputLoadsBackToTheSameValue()
    {
        PyDict d = PyDict.Of("名字", "中文");

        string escaped = PyJson.Dumps(d);
        Assert.Equal("{\"\\u540d\\u5b57\": \"\\u4e2d\\u6587\"}", escaped);

        var back = Assert.IsType<PyDict>(PyJson.Loads(escaped));
        Assert.Equal("中文", back["名字"]);
    }

    [Fact]
    public void Loads_AcceptsWhitespaceAroundTheDocumentButNotInside()
    {
        var d = Assert.IsType<PyDict>(PyJson.Loads(" \t\r\n {\"a\" : 1} \n"));
        Assert.Equal(1L, d["a"]);
    }
}
