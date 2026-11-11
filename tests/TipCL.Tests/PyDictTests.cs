using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A2 步骤 4-3:PyDict 行为。期望值全部由 <c>PyDict.cs</c> / <c>Py.cs</c> 源码推出:
/// 键序是插入序(_keys 列表)、_map 用 StringComparer.Ordinal、缺键取值为 null、
/// Req 抛 KeyError 且 key 存在 Key 属性上、ToString 走 Py.Repr。
/// </summary>
public class PyDictTests
{
    [Fact]
    public void KeysFollowInsertionOrder_AndOverwritingDoesNotDuplicate()
    {
        var d = new PyDict();
        d["b"] = 1L;
        d["a"] = 2L;
        d["c"] = 3L;
        d["b"] = 9L;   // 覆盖:不新增键,顺序不动

        Assert.Equal(3, d.Count);
        Assert.Equal(new[] { "b", "a", "c" }, d.Keys);
        Assert.Equal(9L, d["b"]);
    }

    [Fact]
    public void KeyComparerIsOrdinalSoCaseMatters()
    {
        var d = PyDict.Of("a", 1);

        Assert.True(d.ContainsKey("a"));
        Assert.False(d.ContainsKey("A"));
        Assert.Null(d["A"]);
        d["A"] = 2L;
        Assert.Equal(2, d.Count);
        Assert.Equal(new[] { "a", "A" }, d.Keys);
    }

    [Fact]
    public void MissingKeyReadsAsNull_EvenForANullKey()
    {
        var d = PyDict.Of("a", 1);

        Assert.Null(d["missing"]);
        Assert.Null(d[null]);
        Assert.Null(d.Get("missing"));
        Assert.Null(d.Get(null));
        Assert.Equal("dflt", d.Get("missing", "dflt"));
        Assert.Equal(1L, d.Get("a", "dflt"));
    }

    [Fact]
    public void NullKeyWriteThrowsArgumentNullException()
    {
        var d = new PyDict();

        var ex = Assert.Throws<ArgumentNullException>(() => d[null] = 1L);
        Assert.Equal("key", ex.ParamName);
        Assert.Equal(0, d.Count);
    }

    [Fact]
    public void ReqReturnsTheValue_OrThrowsKeyErrorCarryingTheKey()
    {
        var d = PyDict.Of("k", "v");

        Assert.Equal("v", d.Req("k"));

        KeyError ex = Assert.Throws<KeyError>(() => d.Req("missing"));
        Assert.Equal("KeyError", ex.PyType);
        Assert.Equal("missing", ex.Key);
        Assert.Equal("'missing'", ex.Message);   // 消息是 Py.Repr(key)
        Assert.Throws<KeyError>(() => d.Req(null));
    }

    [Fact]
    public void ContainsKeyAndHasTreatNullAsAbsent()
    {
        var d = PyDict.Of("a", 1);

        Assert.True(d.Has("a"));
        Assert.False(d.Has("z"));
        Assert.False(d.ContainsKey(null));
        Assert.False(d.Has(null));
    }

    [Fact]
    public void RemoveReportsWhetherItRemoved_AndKeepsKeysInSync()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        Assert.True(d.Remove("a"));
        Assert.False(d.Remove("a"));
        Assert.False(d.Remove(null));
        Assert.Equal(new[] { "b" }, d.Keys);
        Assert.Equal(1, d.Count);
        Assert.Null(d["a"]);
    }

    [Fact]
    public void PopRemovesAndReturns_OrFallsBackToTheDefault()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        Assert.Equal(1L, d.Pop("a"));
        Assert.Equal(1, d.Count);
        Assert.Null(d.Pop("a"));
        Assert.Equal("dflt", d.Pop("a", "dflt"));
        Assert.Null(d.Pop(null));
        Assert.Equal(2L, d.Pop("b", "dflt"));
        Assert.Equal(0, d.Count);
    }

    [Fact]
    public void SetDefaultKeepsTheExistingValue()
    {
        PyDict d = PyDict.Of("a", 1);

        Assert.Equal(1L, d.SetDefault("a", 9L));
        Assert.Equal(1L, d["a"]);

        Assert.Equal(9L, d.SetDefault("b", 9L));
        Assert.Equal(9L, d["b"]);
        Assert.Equal(new[] { "a", "b" }, d.Keys);
    }

    [Fact]
    public void UpdateOverwritesAndAppendsNewKeysInOrder()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        d.Update(PyDict.Of("b", 20, "c", 30));

        Assert.Equal(new[] { "a", "b", "c" }, d.Keys);
        Assert.Equal(20L, d["b"]);
        Assert.Equal(30L, d["c"]);

        d.Update(null);   // 源码里对 null 是空操作
        Assert.Equal(3, d.Count);
    }

    [Fact]
    public void CopyIsAnIndependentSnapshot()
    {
        PyDict d = PyDict.Of("a", 1);
        PyDict c = d.Copy();

        c["a"] = 2L;
        c["b"] = 3L;

        Assert.Equal(1L, d["a"]);
        Assert.Equal(1, d.Count);
        Assert.Equal(2, c.Count);
        Assert.Equal(new[] { "a", "b" }, c.Keys);

        Assert.Equal(0, new PyDict((PyDict)null).Count);
        Assert.Equal(0, new PyDict((IEnumerable<KeyValuePair<string, object>>)null).Count);
    }

    [Fact]
    public void TryGetValueAndValuesAndClear()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        Assert.True(d.TryGetValue("a", out object? v));
        Assert.Equal(1L, v);
        Assert.False(d.TryGetValue("z", out object? w));
        Assert.Null(w);
        Assert.False(d.TryGetValue(null, out object? x));
        Assert.Null(x);

        Assert.Equal(new object[] { 1L, 2L }, d.Values.ToArray());

        d.Clear();
        Assert.Equal(0, d.Count);
        Assert.Empty(d.Keys);
        Assert.False(d.ContainsKey("a"));
    }

    [Fact]
    public void EnumerationYieldsInsertionOrderPairs()
    {
        PyDict d = PyDict.Of("a", 1, "b", 2);

        var seen = new List<KeyValuePair<string, object>>();
        foreach (KeyValuePair<string, object> kv in d)
        {
            seen.Add(kv);
        }

        Assert.Equal(new[] { "a", "b" }, seen.Select(kv => kv.Key).ToArray());
        Assert.Equal(new object[] { 1L, 2L }, seen.Select(kv => kv.Value).ToArray());
    }

    [Fact]
    public void EnumerationSnapshotsTheKeysSoLaterWritesDoNotBreakIt()
    {
        PyDict d = PyDict.Of("a", 1);

        using (IEnumerator<KeyValuePair<string, object>> e = d.GetEnumerator())
        {
            Assert.True(e.MoveNext());
            Assert.Equal("a", e.Current.Key);
            d["b"] = 2L;                     // 枚举器取的是 _keys.ToArray() 快照
            Assert.False(e.MoveNext());
        }

        Assert.Equal(2, d.Count);
    }

    [Fact]
    public void OfBoxesValuesLikePyBox()
    {
        PyDict d = PyDict.Of("i", 5, "f", 1.5f, "b", true, "s", "x", "n", null);

        Assert.IsType<long>(d["i"]);
        Assert.Equal(5L, d["i"]);
        Assert.IsType<double>(d["f"]);     // float → double
        Assert.Equal(1.5d, d["f"]);
        Assert.IsType<bool>(d["b"]);
        Assert.Equal(true, d["b"]);
        Assert.Equal("x", d["s"]);
        Assert.Null(d["n"]);

        // 奇数个参数时最后一个被忽略(源码 for 条件是 i + 1 < kv.Length)
        Assert.Equal(0, PyDict.Of("only").Count);
        PyDict odd = PyDict.Of("a", 1, "b");
        Assert.Equal(1, odd.Count);
        Assert.Equal(new[] { "a" }, odd.Keys);
    }

    [Fact]
    public void ToStringUsesPyRepr()
    {
        Assert.Equal("{'a': 1, 'b': '文字'}", PyDict.Of("a", 1, "b", "文字").ToString());
        Assert.Equal("{}", new PyDict().ToString());
        Assert.Equal("{'m': None}", PyDict.Of("m", null).ToString());
    }

    [Fact]
    public void ToStringOfInnerListAndNestedDictMatchesPyRepr()
    {
        var inner = PyDict.Of("k", "v");
        PyDict d = PyDict.Of("list", new List<object> { 1L, true, null }, "dict", inner);

        Assert.Equal("{'list': [1, True, None], 'dict': {'k': 'v'}}", d.ToString());
    }

    [Fact]
    public void ReprStrPicksDoubleQuotesWhenTheValueContainsASingleQuote()
    {
        Assert.Equal("\"it's\"", Py.ReprStr("it's"));
        Assert.Equal("'say \"hi\"'", Py.ReprStr("say \"hi\""));
    }
}
