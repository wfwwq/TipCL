using TipCL.Core;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>PyDifflib 纯文本相似度逻辑单测（M1-A5）。</summary>
public class PyDifflibTests
{
    [Fact]
    public void CodePoints_HandlesSurrogatePairs()
    {
        // "𝐀" 是一个增补平面字符（两个 UTF-16 char）
        int[] cps = PyDifflib.CodePoints("a𝐀b");
        Assert.Equal(3, cps.Length);
        Assert.Equal('a', cps[0]);
        Assert.Equal('b', cps[2]);
    }

    [Fact]
    public void PyLen_CountsCodePointsNotChars()
    {
        Assert.Equal(3, PyDifflib.PyLen("a𝐀b"));
        Assert.Equal(2, PyDifflib.PyLen("ab"));
    }

    [Fact]
    public void Ratio_Identical_IsOne()
    {
        Assert.Equal(1.0, PyDifflib.Ratio("minecraft", "minecraft"), 10);
    }

    [Fact]
    public void Ratio_BothEmpty_IsOne()
    {
        Assert.Equal(1.0, PyDifflib.Ratio("", ""), 10);
    }

    [Fact]
    public void Ratio_Disjoint_IsZero()
    {
        Assert.Equal(0.0, PyDifflib.Ratio("abc", "xyz"), 10);
    }

    [Fact]
    public void Ratio_Similar_BetweenZeroAndOne()
    {
        double r = PyDifflib.Ratio("minecraft1.20", "minecraft1.20.1");
        Assert.InRange(r, 0.0, 1.0);
        Assert.True(r > 0.8, $"相似串 ratio 过低: {r}");
    }
}
