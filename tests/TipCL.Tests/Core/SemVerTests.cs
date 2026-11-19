// SemVer 单测(audit/D 补覆盖)。
//
// 为什么补:插件更新徽标(PluginUpdateBadgeTests)与更新检查都拿 SemVer 判"新版/旧版",
// 而此前只有 4 条 Compare 断言 —— 解析容错(1 / 1.2 / v1.2.3 / +build)、预发布排序、
// 范围匹配(^ ~ x.* || - )完全没有守卫。这类逻辑写错的后果是"更新永远不提示"或"永远提示",
// 而不是崩溃 —— 正是最容易被真实用户先发现的沉默型缺陷。
//
// 全部是纯函数,无 IO / 无网络 / 无全局状态,毫秒级。
using System;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests.Core;

public class SemVerTests
{
    // ————— 解析 —————

    [Theory]
    [InlineData("1", "1.0.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("  1.2.3  ", "1.2.3")]
    [InlineData("1.2.3+build.7", "1.2.3")]
    [InlineData("1.2.3-beta.1+sha.abc", "1.2.3-beta.1")]
    public void TryParse_NormalizesAndDropsBuildMetadata(string input, string expected)
    {
        SemVer? v = SemVer.TryParse(input);
        Assert.NotNull(v);
        Assert.Equal(expected, v!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("1.-2")]
    [InlineData("1.x")]
    [InlineData("1.2.3-")]      // 空的预发布段
    public void TryParse_RejectsGarbageWithoutThrowing(string input)
    {
        Assert.Null(SemVer.TryParse(input));
        Assert.False(SemVer.IsValid(input));
    }

    [Fact]
    public void Parse_ThrowsValueError_OnGarbage()
    {
        Assert.Throws<ValueError>(() => SemVer.Parse("nope"));
    }

    // ————— 比较 —————

    [Theory]
    [InlineData("1.10.0", "1.9.0")]                 // 数值比较,不是字符串比较
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.2.10", "1.2.9")]
    [InlineData("1.2.0", "1.2.0-beta.9")]           // 正式版 > 预发布
    [InlineData("1.2.0-beta.10", "1.2.0-beta.2")]   // 预发布段里的数字也按数值比
    [InlineData("1.2.0-alpha", "1.2.0-1")]          // 数字段 < 字母段
    public void Compare_IsNumericAndPrereleaseAware(string bigger, string smaller)
    {
        Assert.True(SemVer.Compare(bigger, smaller) > 0, bigger + " 应大于 " + smaller);
        Assert.True(SemVer.Compare(smaller, bigger) < 0, smaller + " 应小于 " + bigger);
    }

    [Fact]
    public void Compare_EqualVersionsReturnZeroRegardlessOfBuildMetadata()
    {
        Assert.Equal(0, SemVer.Compare("1.2.3", "v1.2.3+build.9"));
    }

    [Fact]
    public void Compare_FallsBackToOrdinalStringCompare_WhenEitherSideIsNotAVersion()
    {
        // 解析不了就按字符串比 —— 顺序必须稳定(不能出现 Compare(a,b)>0 且 Compare(b,a)>0)
        int ab = SemVer.Compare("banana", "apple");
        int ba = SemVer.Compare("apple", "banana");
        Assert.True(ab > 0);
        Assert.True(ba < 0);
        Assert.Equal(0, SemVer.Compare("banana", "banana"));

        // 一边能解析、一边不能:走字符串分支,只要不抛就行
        Assert.Equal(0, SemVer.Compare("1.2.3", "1.2.3"));
        _ = SemVer.Compare("1.2.3", "banana");
    }

    // ————— 范围 —————

    [Theory]
    [InlineData("1.2.3", "")]                    // 空范围 = 都满足
    [InlineData("1.2.3", "*")]
    [InlineData("1.2.3", "latest")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.5.0", ">=1.0.0 <2.0.0")]
    [InlineData("2.0.0", "1.0.0 - 2.0.0")]
    [InlineData("1.9.9", "1.x")]
    [InlineData("1.2.9", "1.2.*")]
    [InlineData("1.2.3", "^1.2.3")]
    [InlineData("1.2.3", "~1.2.3")]
    [InlineData("1.2.99", "~1.2.3")]             // ~ 允许同 minor 下的补丁号上浮
    public void Satisfies_Matches(string version, string range)
    {
        Assert.True(SemVer.Satisfies(version, range), version + " 应满足 " + range);
    }

    [Theory]
    [InlineData("2.0.0", "^1.2.3")]
    [InlineData("1.2.2", "^1.2.3")]
    [InlineData("1.3.0", "~1.2.3")]              // ~1.2.3 的上界是 1.3.0
    [InlineData("2.0.0", "1.x")]
    [InlineData("1.3.0", "1.2.*")]
    [InlineData("2.0.0", ">=1.0.0 <2.0.0")]
    [InlineData("2.0.1", "1.0.0 - 2.0.0")]
    [InlineData("1.2.0-alpha", "^1.2.0")]        // 预发布版不落进非预发布范围
    public void Satisfies_Rejects(string version, string range)
    {
        Assert.False(SemVer.Satisfies(version, range), version + " 不该满足 " + range);
    }

    [Fact]
    public void Satisfies_SupportsOrAndPartialBoundaries()
    {
        Assert.True(SemVer.Satisfies("0.9.5", "^1.0.0 || ^0.9.0"));
        Assert.False(SemVer.Satisfies("0.8.0", "^1.0.0 || ^0.9.0"));

        // ">=22" 等价 ">=22.0.0"(缺的段补 0)
        Assert.True(SemVer.Satisfies("22.0.0", ">=22"));
        Assert.False(SemVer.Satisfies("21.9.9", ">=22"));

        // 部分段的上界按"下一个主/次版本"算:1.0.0 - 2.0 → < 2.1.0
        Assert.True(SemVer.Satisfies("2.0.5", "1.0.0 - 2.0"));
        Assert.False(SemVer.Satisfies("2.1.0", "1.0.0 - 2.0"));
    }

    [Fact]
    public void Satisfies_ReturnsFalse_OnUnparsableVersionOrRange()
    {
        Assert.False(SemVer.Satisfies("banana", "^1.0.0"));
        Assert.False(SemVer.Satisfies("1.0.0", "banana"));
        Assert.False(SemVer.IsValidRange("banana"));
        Assert.True(SemVer.IsValidRange(">=1.2.0 <2.0.0"));
        Assert.True(SemVer.IsValidRange("^1.2.3 || ~2.0.0"));
    }

    [Fact]
    public void CompareTo_NullIsSmallerThanAnything()
    {
        Assert.True(SemVer.Of(0).CompareTo(null!) > 0);
    }
}