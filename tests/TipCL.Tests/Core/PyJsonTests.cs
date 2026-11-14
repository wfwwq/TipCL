using System;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>PyJson 解析/序列化单测（M1-A5，承接 M1-A4 CoreReuseSmokeTests 的 xunit 化）。</summary>
public class PyJsonTests
{
    [Fact]
    public void Loads_Parses_Types()
    {
        var obj = (PyDict)PyJson.Loads("{\"a\":1,\"b\":[true,null,\"x\\ny\"],\"c\":{\"d\":2.5}}");
        Assert.Equal(1L, obj.Req("a"));
        Assert.True(obj.Req("b") is System.Collections.Generic.List<object> l && (bool)l[0]);
        Assert.Null((obj.Req("b") as System.Collections.Generic.List<object>)![1]);
        Assert.Equal("x\ny", obj.Req("b") is System.Collections.Generic.List<object> l2 ? l2[2] : null);
        var c = (PyDict)obj.Req("c");
        Assert.Equal(2.5, c.Req("d"));
    }

    [Fact]
    public void Roundtrip_Object_IsStable()
    {
        string src = "{\"a\":1,\"b\":[true,null,\"x\\ny\"],\"c\":{\"d\":2.5}}";
        object obj = PyJson.Loads(src);
        string back = PyJson.Dumps(obj);
        object again = PyJson.Loads(back);
        Assert.Equal(Py.Str(((PyDict)obj).Req("a")), Py.Str(((PyDict)again).Req("a")));
        Assert.Equal(
            Py.Str(((PyDict)((PyDict)obj).Req("c")).Req("d")),
            Py.Str(((PyDict)((PyDict)again).Req("c")).Req("d")));
    }

    [Fact]
    public void Loads_Invalid_Throws()
    {
        Assert.ThrowsAny<Exception>(() => PyJson.Loads("{bad json"));
    }

    [Fact]
    public void LoadsOr_ReturnsDefault_OnInvalid()
    {
        object dflt = PyDict.Of("k", "v");
        Assert.Same(dflt, PyJson.LoadsOr("{bad", dflt));
    }

    [Fact]
    public void Dumps_EscapeControlChars()
    {
        string s = PyJson.Dumps(PyDict.Of("k", "a\nb"));
        Assert.DoesNotContain("\n", s);
        Assert.Contains("\\n", s, StringComparison.Ordinal);
    }
}
