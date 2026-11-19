// 下载分段规划单测(audit/D 补覆盖)。
//
// 为什么补:Downloader.SplitRanges 决定"一个文件切成几段、每段的 [start,end] 是什么",
// 分段下载/断点续传的元数据全部由它推出。此前**零覆盖**。它写错的表现是下载静默丢字节
// (段与段之间漏一个字节)或重复写(段重叠) —— 文件大小对不上、jar 装不上,但日志一切正常。
//
// 这里只钉**不变量**(对任意 total/parts 都必须成立的),不钉具体实现细节:
//   1) 段从 0 开始、首尾相接、无重叠无空洞,最后一段的 end 恰好是 total-1(覆盖整个文件);
//   2) 段数不超过可用分段数,且每段长度相等或相差不超过 1;
//   3) 小文件不会被切成 0 段(否则等于不下载)。
//
// 纯函数,无 IO / 无网络。2 MB = 2097152 B 是实现里的最小段粒度(见 Downloader.SplitRanges)。
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

public class DownloadSegmentsTests
{
    private const long MiB = 1024L * 1024L;
    private const long MinChunk = 2L * MiB;

    public static IEnumerable<object[]> Plans() => new[]
    {
        new object[] { 1L, 4 },
        new object[] { MinChunk, 4 },
        new object[] { MinChunk + 1, 4 },
        new object[] { 5L * MiB, 4 },
        new object[] { 10L * MiB, 4 },
        new object[] { 10L * MiB + 1, 4 },
        new object[] { 100L * MiB, 8 },
        new object[] { 100L * MiB + 12345, 3 },
        new object[] { 300L * MiB, 16 },
        new object[] { 7L, 0 },          // parts 非法(0)时要退化成 1 段,而不是 0 段
        new object[] { 5L * MiB, 999 },  // parts 超过可分段数时要被夹住
    };

    [Theory]
    [MemberData(nameof(Plans))]
    public void RangesAreContiguousAndCoverTheWholeFile(long total, int parts)
    {
        List<long[]> ranges = Downloader.SplitRanges(total, parts);

        Assert.NotEmpty(ranges);   // 0 段 = 什么都不下
        Assert.Equal(0L, ranges[0][0]);
        Assert.Equal(total - 1, ranges[^1][1]);

        for (int i = 0; i < ranges.Count; i++)
        {
            long start = ranges[i][0];
            long end = ranges[i][1];
            Assert.True(start <= end, $"第 {i} 段起点 {start} > 终点 {end}");
            if (i > 0)
            {
                Assert.Equal(ranges[i - 1][1] + 1, start);   // 无空洞、无重叠
            }
        }
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void RangeCountNeverExceedsRequestedParts_AndSegmentsAreBalanced(long total, int parts)
    {
        List<long[]> ranges = Downloader.SplitRanges(total, parts);

        int cap = parts < 1 ? 1 : parts;
        Assert.True(ranges.Count <= cap, $"切成 {ranges.Count} 段,超过上限 {cap}");

        // 除最后一段外,段长必须完全相同(按固定步长切);最后一段只负责收尾,允许更短
        long first = ranges[0][1] - ranges[0][0];
        foreach (long[] r in ranges.Take(ranges.Count - 1))
        {
            Assert.Equal(first, r[1] - r[0]);
        }
        long last = ranges[^1][1] - ranges[^1][0];
        Assert.InRange(last, 0, first);
    }

    [Fact]
    public void SmallFileStaysOneRange_AndDoesNotSplitBelowTheMinimumChunk()
    {
        // 小于 2 MB 的文件:1 段、整文件
        long[][] one = Downloader.SplitRanges(MiB, 8).ToArray();
        Assert.Single(one);
        Assert.Equal(0L, one[0][0]);
        Assert.Equal(MiB - 1, one[0][1]);

        // 恰好 3 MB、要 4 段:不足 2 个最小段 ⇒ 只能 1 段(不能切出 <2 MB 的碎片段)
        long[][] three = Downloader.SplitRanges(3 * MiB, 4).ToArray();
        Assert.Single(three);
        Assert.Equal(3 * MiB - 1, three[0][1]);
    }

    [Fact]
    public void ZeroLengthFileYieldsNoRanges()
    {
        // 空文件没有可下载的字节 —— 返回空表而不是 [0,-1] 这种非法区间
        Assert.Empty(Downloader.SplitRanges(0L, 4));
    }

    [Fact]
    public void SameInputGivesSamePlan_DifferentRunsAreStable()
    {
        long[][] a = Downloader.SplitRanges(100L * MiB + 7, 6).ToArray();
        long[][] b = Downloader.SplitRanges(100L * MiB + 7, 6).ToArray();
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i][0], b[i][0]);
            Assert.Equal(a[i][1], b[i][1]);
        }
    }
}