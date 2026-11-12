using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A5:RpcDispatcher(本轮才进编译的 RpcDispatcher.cs)。
///
/// 期望值全部由 RpcDispatcher.cs 源码推出:
///   1) 只暴露「不以 _ 开头、且名字里没有大写字母」的 public 实例/静态方法(IsExposedName,80-94 行);
///   2) IsSpecialName(属性访问器)与 object 自己的方法一律跳过(51 行);
///   3) 同名重载直接抛 InvalidOperationException("RPC 方法重载不受支持:...",57 行);
///   4) 方法名以 _ 开头 → RpcError(-32601, "hidden method")(103-106 行);
///      未知方法 / 被 [RpcHidden] 标记的方法 → RpcError(-32601, "unknown method: <名字>");
///   5) 位置参数与关键字参数(参数名)两种绑定,默认值来自 [Def(...)] / C# 可选参数;
///   6) [VarArgs] 收多余位置参数、[VarKw] 收多余关键字参数;
///   7) 类型转换:bool↔long、double(整值)↔long、long→double、long→int(越界报 TypeError);
///   8) 被调方法抛出的异常原样抛出(拆掉 TargetInvocationException,128-132 行)。
/// 目标类是本文件里自定义的,不实例化 BackendApi(它的构造函数会往 login.live.com 发预热请求)。
/// </summary>
public class A5RpcDispatcherTests
{
    // ── 测试用的目标类:方法名必须全小写才会被暴露 ──

    private sealed class Target
    {
        public long add(long a, long b) => a + b;

        public string concat(string s, long n = 2L) => s + n;

        public string withdef([Def("fallback")] string s) => s;

        public long sum_all([VarArgs] List<object> rest) => rest == null ? -1L : rest.Count;

        public string kw([VarKw] PyDict extra) => extra == null ? "<null>" : string.Join(",", extra.Keys);

        public string kw2(string a, [VarKw] PyDict extra) => a + "|" + extra.Count;

        public string strarg(string a) => a;

        public double dbl(double x) => x;

        public int int32(int x) => x;

        public bool flag(bool b) => b;

        public object any(object v) => v;

        public long? nullable_int(long? n) => n;

        public string thrower() => throw new ValueError("boom");

        public static string stat() => "static-ok";

        [RpcHidden]
        public string secret() => "nope";

        public string _private_ish() => "nope";

        public string CamelCase() => "nope";

        public string name { get; set; } = "prop";
    }

    private sealed class Overloaded
    {
        public long same(long a) => a;

        public long same(long a, long b) => a + b;
    }

    // ── 方法发现 ──

    [Fact]
    public void Only_all_lowercase_public_methods_are_exposed()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Contains("add", d.MethodNames);
        Assert.Contains("stat", d.MethodNames);
        Assert.True(d.Has("add"));
    }

    [Fact]
    public void Uppercase_underscore_hidden_and_object_members_are_not_exposed()
    {
        var d = new RpcDispatcher(new Target());

        Assert.False(d.Has("CamelCase"));      // 有大写字母
        Assert.False(d.Has("_private_ish"));   // 下划线开头
        Assert.False(d.Has("secret"));         // [RpcHidden]
        Assert.False(d.Has("name"));           // 属性不是方法
        Assert.False(d.Has("ToString"));       // object 自己的方法
        Assert.False(d.Has("GetHashCode"));
        Assert.False(d.Has("nope"));
        Assert.DoesNotContain("name", d.MethodNames);
        Assert.DoesNotContain("secret", d.MethodNames);
    }

    [Theory]
    [InlineData("add", true)]
    [InlineData("lower_case1", true)]
    [InlineData("_leading", false)]
    [InlineData("hasUpper", false)]
    [InlineData("", false)]
    public void IsExposedName_matches_the_source_rule(string name, bool expected)
    {
        Assert.Equal(expected, RpcDispatcher.IsExposedName(name));
    }

    [Fact]
    public void IsExposedName_rejects_null()
    {
        Assert.False(RpcDispatcher.IsExposedName(null));
    }

    [Fact]
    public void Overloaded_method_names_are_rejected_at_construction()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new RpcDispatcher(new Overloaded()));
        // 源码 RpcDispatcher.cs:57 的冒号是全角 U+FF1A(D-16 的坑),测试里用转义写死
        Assert.Equal("RPC \u65B9\u6CD5\u91CD\u8F7D\u4E0D\u53D7\u652F\u6301\uFF1Asame", ex.Message);
    }

    [Fact]
    public void PyClassName_constant_is_BackendAPI()
    {
        Assert.Equal("BackendAPI", RpcDispatcher.PyClassName);
    }

    // ── 位置参数绑定 ──

    [Fact]
    public void Positional_arguments_are_bound_in_formal_order()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal(3L, d.Call("add", new List<object> { 1L, 2L }));
    }

    [Fact]
    public void Optional_parameter_uses_the_csharp_default_when_omitted()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal("x2", d.Call("concat", new List<object> { "x" }));
        Assert.Equal("x9", d.Call("concat", new List<object> { "x", 9L }));
    }

    [Fact]
    public void DefAttribute_supplies_the_default_value()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal("fallback", d.Call("withdef", new List<object>()));
        Assert.Equal("given", d.Call("withdef", new List<object> { "given" }));
    }

    [Fact]
    public void Too_many_positional_arguments_raise_TypeError_with_python_style_message()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("add", new List<object> { 1L, 2L, 3L }));
        Assert.Equal("BackendAPI.add() takes 3 positional arguments but 4 were given", ex.Message);
    }

    [Fact]
    public void Missing_required_argument_raises_TypeError()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("strarg", new List<object>()));
        Assert.Equal("BackendAPI.strarg() missing 1 required positional argument: 'a'", ex.Message);
    }

    [Fact]
    public void Two_missing_arguments_are_joined_with_and()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("add", new List<object>()));
        Assert.Equal("BackendAPI.add() missing 2 required positional arguments: 'a' and 'b'", ex.Message);
    }

    // ── 关键字参数绑定 ──

    [Fact]
    public void Keyword_arguments_are_bound_by_parameter_name()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal(7L, d.Call("add", PyDict.Of("b", 5L, "a", 2L)));
    }

    [Fact]
    public void Keyword_binding_also_uses_csharp_defaults()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal("z3", d.Call("concat", PyDict.Of("s", "z", "n", 3L)));
        Assert.Equal("z2", d.Call("concat", PyDict.Of("s", "z")));
    }

    [Fact]
    public void VarArgs_collects_extra_positional_arguments()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal(0L, d.Call("sum_all", new List<object>()));
        Assert.Equal(3L, d.Call("sum_all", new List<object> { 1L, 2L, 3L }));
    }

    [Fact]
    public void VarKw_collects_unknown_keyword_arguments()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal("x,y", d.Call("kw", PyDict.Of("x", 1L, "y", 2L)));
        Assert.Equal("A|1", d.Call("kw2", PyDict.Of("a", "A", "extraKey", 1L)));
    }

    [Fact]
    public void VarArgs_gets_an_empty_list_in_keyword_binding()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal(0L, d.Call("sum_all", PyDict.Of()));
    }

    [Fact]
    public void Unknown_keyword_arguments_are_warned_about_and_ignored()
    {
        var d = new RpcDispatcher(new Target());
        Action<string> original = RpcDispatcher.Warn;
        List<string> warnings = new List<string>();
        try
        {
            RpcDispatcher.Warn = s => warnings.Add(s);
            Assert.Equal(3L, d.Call("add", PyDict.Of("a", 1L, "b", 2L, "zz", 9L)));
        }
        finally
        {
            RpcDispatcher.Warn = original;
        }

        Assert.Single(warnings);
        Assert.StartsWith("rpc add: 忽略了未知参数", warnings[0], StringComparison.Ordinal);
        Assert.Contains("zz", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Params_that_are_neither_list_nor_dict_raise_TypeError()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("add", "not-a-container"));
        Assert.Equal("params must be object or array", ex.Message);
    }

    // ── 错误形态 ──

    [Fact]
    public void Unknown_method_raises_RpcError_minus_32601()
    {
        var d = new RpcDispatcher(new Target());

        RpcError ex = Assert.Throws<RpcError>(() => d.Call("nope", PyDict.Of()));
        Assert.Equal(-32601L, ex.Code);
        Assert.Equal("unknown method: nope", ex.Message);
    }

    [Fact]
    public void RpcHidden_method_is_not_reachable_and_reads_as_unknown()
    {
        var d = new RpcDispatcher(new Target());

        RpcError ex = Assert.Throws<RpcError>(() => d.Call("secret", PyDict.Of()));
        Assert.Equal(-32601L, ex.Code);
        Assert.Equal("unknown method: secret", ex.Message);
    }

    [Fact]
    public void Underscore_prefixed_name_raises_hidden_method()
    {
        var d = new RpcDispatcher(new Target());

        RpcError ex = Assert.Throws<RpcError>(() => d.Call("_private_ish", PyDict.Of()));
        Assert.Equal(-32601L, ex.Code);
        Assert.Equal("hidden method", ex.Message);
    }

    [Fact]
    public void Exception_thrown_inside_the_target_method_is_unwrapped()
    {
        var d = new RpcDispatcher(new Target());

        ValueError ex = Assert.Throws<ValueError>(() => d.Call("thrower", PyDict.Of()));
        Assert.Equal("boom", ex.Message);
        Assert.Equal("ValueError", ex.PyType);
    }

    // ── 类型转换 ──

    [Fact]
    public void Bool_double_and_biginteger_convert_to_long()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal(3L, d.Call("add", new List<object> { true, 2L }));
        Assert.Equal(3L, d.Call("add", new List<object> { 1.0, 2L }));
        Assert.Equal(5L, d.Call("add", new List<object> { new System.Numerics.BigInteger(2), 3L }));
    }

    [Fact]
    public void Non_integral_double_for_a_long_parameter_raises_TypeError()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("add", new List<object> { 1.5, 2L }));
        Assert.Equal("BackendAPI.add() \u53C2\u6570 a \u7C7B\u578B\u4E0D\u7B26\uFF1A\u6536\u5230 float", ex.Message);
    }

    [Fact]
    public void String_for_a_long_parameter_raises_TypeError_with_the_runtime_type_name()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("add", new List<object> { "x", 2L }));
        Assert.Equal("BackendAPI.add() \u53C2\u6570 a \u7C7B\u578B\u4E0D\u7B26\uFF1A\u6536\u5230 str", ex.Message);
    }

    [Fact]
    public void Long_converts_to_double_and_int_when_in_range()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal(2.5, d.Call("dbl", new List<object> { 2.5 }));
        Assert.Equal(2.0, d.Call("dbl", new List<object> { 2L }));
        Assert.Equal(5, d.Call("int32", new List<object> { 5L }));
    }

    [Fact]
    public void Long_out_of_int_range_raises_TypeError()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("int32", new List<object> { 5000000000L }));
        Assert.Contains("参数 x 类型不符", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bool_parameters_use_python_truthiness()
    {
        var d = new RpcDispatcher(new Target());

        Assert.True((bool)d.Call("flag", new List<object> { "x" }));
        Assert.False((bool)d.Call("flag", new List<object> { "" }));
    }

    [Fact]
    public void Object_parameters_pass_the_value_through_unchanged()
    {
        var d = new RpcDispatcher(new Target());

        var value = PyDict.Of("k", 1L);
        Assert.Same(value, d.Call("any", new List<object> { value }));
        Assert.Null(d.Call("any", new List<object> { null }));
    }

    [Fact]
    public void Nullable_value_type_accepts_null()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Null(d.Call("nullable_int", new List<object> { null }));
        Assert.Equal(4L, d.Call("nullable_int", new List<object> { 4L }));
    }

    [Fact]
    public void Null_for_a_non_nullable_value_type_raises_TypeError()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("add", new List<object> { null, 1L }));
        Assert.Equal("BackendAPI.add() \u53C2\u6570 a \u7C7B\u578B\u4E0D\u7B26\uFF1A\u6536\u5230 NoneType", ex.Message);
    }

    // ── 静态方法 ──

    [Fact]
    public void Static_methods_are_called_without_a_target_instance()
    {
        var d = new RpcDispatcher(new Target());

        Assert.Equal("static-ok", d.Call("stat", new List<object>()));
        Assert.Equal("static-ok", d.Call("stat", PyDict.Of()));
    }

    [Fact]
    public void Static_method_argument_count_error_does_not_reserve_a_self_slot()
    {
        var d = new RpcDispatcher(new Target());

        TypeError ex = Assert.Throws<TypeError>(() => d.Call("stat", new List<object> { 1L }));
        Assert.Equal("BackendAPI.stat() takes 0 positional arguments but 1 was given", ex.Message);
    }
}
