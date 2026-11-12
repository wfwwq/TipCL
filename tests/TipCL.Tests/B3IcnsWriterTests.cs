using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Packager;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B3:`.icns` 的组装、读回与尺寸选择(`Contents/Resources/TipCL.icns`)。
///
/// 结构定义来自任务书 B3 的「File-format facts」:magic `icns` + 大端 uint32 总长度 +
/// 若干「4 字节类型 + 大端 uint32 长度(含这 8 字节头)+ 数据」;PNG 数据合法于
/// ic07(128)/ ic08(256)/ ic13(256,128@2x),任务书明确**不产出** ic09。
/// 末尾两条用例读真实图标 `packaging\TipCL.ico`,期望值是 2026-11-12 在本机用探针实测出来的
/// (见 PROGRESS.md 的 B3「.ico 取证」小节),不是凭记忆写的。
/// </summary>
public class B3IcnsWriterTests
{
    [Fact]
    public void Build_writes_the_magic_and_the_total_length_as_big_endian()
    {
        byte[] icns = IcnsWriter.Build(new[]
        {
            new IcnsElement { Type = IcnsWriter.Type128, Png = B3PackagerFixtures.FakePng(64) },
        });

        Assert.Equal(8 + 8 + 64, icns.Length);
        Assert.Equal((byte)'i', icns[0]);
        Assert.Equal((byte)'c', icns[1]);
        Assert.Equal((byte)'n', icns[2]);
        Assert.Equal((byte)'s', icns[3]);
        Assert.Equal((uint)icns.Length, IcnsWriter.ReadBigEndianUInt32(icns, 4));
    }

    [Fact]
    public void Build_writes_element_types_and_lengths_in_the_given_order()
    {
        byte[] first = B3PackagerFixtures.FakePng(16);
        byte[] second = B3PackagerFixtures.FakePng(32);

        byte[] icns = IcnsWriter.Build(new[]
        {
            new IcnsElement { Type = IcnsWriter.Type128, Png = first },
            new IcnsElement { Type = IcnsWriter.Type256, Png = second },
        });

        IcnsParseResult parsed = IcnsWriter.Read(icns);

        Assert.Equal(new[] { "ic07", "ic08" }, parsed.Types);
        Assert.Equal(new[] { (uint)(8 + 16), (uint)(8 + 32) }, parsed.ElementLengths);
        Assert.True(parsed.LengthsConsistent);
        Assert.Equal((uint)icns.Length, parsed.TotalLength);
    }

    [Theory]
    [InlineData("ic0")]
    [InlineData("ic077")]
    public void Build_rejects_a_type_that_is_not_four_characters(string type)
    {
        Assert.Throws<ArgumentException>(() => IcnsWriter.Build(new[]
        {
            new IcnsElement { Type = type, Png = B3PackagerFixtures.FakePng(8) },
        }));
    }

    [Fact]
    public void Read_round_trips_an_empty_element_list()
    {
        IcnsParseResult parsed = IcnsWriter.Read(IcnsWriter.Build(Array.Empty<IcnsElement>()));

        Assert.Equal("icns", parsed.Magic);
        Assert.Equal(8u, parsed.TotalLength);
        Assert.Empty(parsed.Types);
        Assert.True(parsed.LengthsConsistent);
    }

    [Fact]
    public void Read_throws_when_the_file_is_shorter_than_the_header()
    {
        Assert.Throws<ArgumentException>(() => IcnsWriter.Read(new byte[] { 0x69, 0x63, 0x6E }));
    }

    [Fact]
    public void Read_reports_inconsistent_lengths_when_an_element_field_is_too_small()
    {
        byte[] icns = IcnsWriter.Build(new[]
        {
            new IcnsElement { Type = IcnsWriter.Type128, Png = B3PackagerFixtures.FakePng(64) },
        });

        IcnsWriter.WriteBigEndianUInt32(icns, 12, 4);   // 元素长度字段 < 8(头部都放不下)

        Assert.False(IcnsWriter.Read(icns).LengthsConsistent);
    }

    [Fact]
    public void Read_reports_inconsistent_lengths_when_an_element_overflows_the_file()
    {
        byte[] icns = IcnsWriter.Build(new[]
        {
            new IcnsElement { Type = IcnsWriter.Type128, Png = B3PackagerFixtures.FakePng(64) },
        });

        IcnsWriter.WriteBigEndianUInt32(icns, 12, (uint)icns.Length + 1);

        Assert.False(IcnsWriter.Read(icns).LengthsConsistent);
    }

    [Fact]
    public void Read_reports_inconsistent_lengths_when_bytes_trail_the_last_element()
    {
        byte[] icns = IcnsWriter.Build(new[]
        {
            new IcnsElement { Type = IcnsWriter.Type128, Png = B3PackagerFixtures.FakePng(64) },
        });

        byte[] padded = new byte[icns.Length + 1];
        Buffer.BlockCopy(icns, 0, padded, 0, icns.Length);

        Assert.False(IcnsWriter.Read(padded).LengthsConsistent);
    }

    [Fact]
    public void Read_reports_a_wrong_magic_without_throwing()
    {
        byte[] icns = IcnsWriter.Build(Array.Empty<IcnsElement>());
        icns[0] = (byte)'x';

        Assert.Equal("xcns", IcnsWriter.Read(icns).Magic);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(256)]
    public void SelectSlots_for_a_256_or_larger_source_uses_ic07_ic08_ic13_and_never_ic09(int largest)
    {
        IReadOnlyList<IcnsSlot> slots = IcnsWriter.SelectSlots(largest);

        Assert.Equal(3, slots.Count);
        Assert.Equal("ic07", slots[0].Type);
        Assert.Equal(128, slots[0].PixelSize);
        Assert.Equal("ic08", slots[1].Type);
        Assert.Equal(256, slots[1].PixelSize);
        Assert.Equal("ic13", slots[2].Type);
        Assert.Equal(256, slots[2].PixelSize);
        Assert.DoesNotContain("ic09", new[] { slots[0].Type, slots[1].Type, slots[2].Type });
    }

    [Fact]
    public void SelectSlots_for_a_128_source_only_emits_ic07()
    {
        IReadOnlyList<IcnsSlot> slots = IcnsWriter.SelectSlots(128);

        Assert.Single(slots);
        Assert.Equal("ic07", slots[0].Type);
        Assert.Equal(128, slots[0].PixelSize);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(127)]
    [InlineData(0)]
    public void SelectSlots_for_a_source_below_128_returns_nothing_so_the_caller_must_stop(int largest)
    {
        Assert.Empty(IcnsWriter.SelectSlots(largest));
    }

    [Fact]
    public void Big_endian_helpers_round_trip_a_full_uint32()
    {
        byte[] buffer = new byte[4];
        IcnsWriter.WriteBigEndianUInt32(buffer, 0, 0x0102FFFEu);

        Assert.Equal(new byte[] { 0x01, 0x02, 0xFF, 0xFE }, buffer);
        Assert.Equal(0x0102FFFEu, IcnsWriter.ReadBigEndianUInt32(buffer, 0));
    }

    // ---- 以下是实测(2026-11-12 本机):packaging\TipCL.ico 的真实字节 ----

    [Fact]
    public void Real_windows_icon_has_seven_png_entries_from_16_to_256()
    {
        byte[] ico = File.ReadAllBytes(B3PackagerFixtures.RepoPath("packaging", "TipCL.ico"));
        IReadOnlyList<IcoImageInfo> images = IconBuilder.IcoSizes(ico);

        Assert.Equal(94953, ico.Length);
        Assert.Equal(7, images.Count);
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, Sizes(images));
        foreach (IcoImageInfo image in images)
        {
            Assert.True(image.IsPng, "本机这个 .ico 的每个条目都是 PNG 数据");
            Assert.Equal(image.Width, image.Height);
        }
    }

    [Fact]
    public void Real_windows_icon_decodes_to_256_and_yields_the_three_brief_slots()
    {
        IconBuildResult icon = IconBuilder.Build(B3PackagerFixtures.RepoPath("packaging", "TipCL.ico"));

        Assert.Equal(256, icon.SourceWidth);
        Assert.Equal(256, icon.SourceHeight);
        Assert.Equal(new[] { "ic07", "ic08", "ic13" }, Types(icon.Slots));

        IcnsParseResult parsed = IcnsWriter.Read(icon.IcnsBytes);
        Assert.Equal("icns", parsed.Magic);
        Assert.Equal((uint)icon.IcnsBytes.Length, parsed.TotalLength);
        Assert.Equal(new[] { "ic07", "ic08", "ic13" }, parsed.Types);
        Assert.True(parsed.LengthsConsistent);

        int offset = IcnsWriter.HeaderSize;
        for (int i = 0; i < parsed.Types.Count; i++)
        {
            int payload = offset + IcnsWriter.ElementHeaderSize;
            Assert.Equal((byte)0x89, icon.IcnsBytes[payload]);
            Assert.Equal((byte)'P', icon.IcnsBytes[payload + 1]);
            Assert.Equal((byte)'N', icon.IcnsBytes[payload + 2]);
            Assert.Equal((byte)'G', icon.IcnsBytes[payload + 3]);
            offset = payload + (int)(parsed.ElementLengths[i] - IcnsWriter.ElementHeaderSize);
        }

        Assert.Equal(icon.IcnsBytes.Length, offset);
    }

    [Fact]
    public void Real_icon_build_is_deterministic()
    {
        string path = B3PackagerFixtures.RepoPath("packaging", "TipCL.ico");

        Assert.Equal(IconBuilder.Build(path).IcnsBytes, IconBuilder.Build(path).IcnsBytes);
    }

    private static int[] Sizes(IReadOnlyList<IcoImageInfo> images)
    {
        int[] sizes = new int[images.Count];
        for (int i = 0; i < images.Count; i++)
        {
            sizes[i] = images[i].Width;
        }

        return sizes;
    }

    private static string[] Types(IReadOnlyList<IcnsSlot> slots)
    {
        string[] types = new string[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            types[i] = slots[i].Type;
        }

        return types;
    }
}
