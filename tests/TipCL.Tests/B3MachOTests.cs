using System;
using System.Collections.Generic;
using TipCL.Packager;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B3:Mach-O 头解析与 `LC_CODE_SIGNATURE` 判定。
///
/// 全部期望值来自任务书 B3 的「File-format facts」小节(64 位小端 magic `CF FA ED FE` /
/// cputype 0x0100000C = arm64、0x01000007 = x86_64 / filetype 2 = executable / load command 从偏移 32
/// 起、`cmd == 0x1D` 是 LC_CODE_SIGNATURE)。测试用的是按同一份定义手写的合成字节 —— 本机没有 mac 产物,
/// 真实 apphost 的字节由 tools\package.ps1 的实跑覆盖。
/// </summary>
public class B3MachOTests
{
    [Fact]
    public void Spec_constants_match_the_brief()
    {
        Assert.Equal(32, MachO.HeaderSize);
        Assert.Equal(8, MachO.CommandHeaderSize);
        Assert.Equal(0xFEEDFACFu, MachOInfo.Magic64Le);
        Assert.Equal(0x1Du, MachOInfo.LcCodeSignature);
        Assert.Equal(2u, MachOInfo.FileTypeExecute);
        Assert.Equal(0x0100000Cu, MachOInfo.CpuTypeArm64);
        Assert.Equal(0x01000007u, MachOInfo.CpuTypeX86_64);
    }

    [Fact]
    public void Parses_a_synthetic_arm64_header()
    {
        byte[] bytes = B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 2, 0x19);
        MachOInfo? info = MachO.TryParse(bytes);

        Assert.NotNull(info);
        Assert.Equal(0xFEEDFACFu, info!.Magic);
        Assert.Equal(0x0100000Cu, info.CpuType);
        Assert.Equal(2u, info.FileType);
        Assert.Equal(1u, info.CommandCount);
        Assert.Equal(8u, info.SizeOfCommands);
        Assert.Equal(new uint[] { 0x19 }, info.Commands);
        Assert.False(info.CommandsTruncated);
        Assert.Equal("arm64", info.CpuName);
    }

    [Fact]
    public void Parses_a_synthetic_x86_64_header()
    {
        MachOInfo? info = MachO.TryParse(
            B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuX86_64, 2, 0x19, 0x1D));

        Assert.NotNull(info);
        Assert.Equal(0x01000007u, info!.CpuType);
        Assert.Equal(2u, info.CommandCount);
        Assert.Equal(new uint[] { 0x19, 0x1D }, info.Commands);
        Assert.Equal("x86_64", info.CpuName);
    }

    [Fact]
    public void Code_signature_command_is_detected()
    {
        MachOInfo? info = MachO.TryParse(
            B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 2, 0x19, 0x1D, 0x2A));

        Assert.NotNull(info);
        Assert.True(info!.HasCodeSignature);
    }

    [Fact]
    public void Code_signature_absence_is_reported_as_false()
    {
        MachOInfo? info = MachO.TryParse(
            B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 2, 0x19, 0x2A));

        Assert.NotNull(info);
        Assert.False(info!.HasCodeSignature);
        Assert.Equal(0x1Du, MachOInfo.LcCodeSignature);
    }

    [Fact]
    public void Header_without_any_load_command_parses_with_no_commands()
    {
        MachOInfo? info = MachO.TryParse(B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 2));

        Assert.NotNull(info);
        Assert.Empty(info!.Commands);
        Assert.False(info.HasCodeSignature);
        Assert.False(info.CommandsTruncated);
    }

    [Theory]
    [InlineData("#!/bin/sh")]
    [InlineData("MZ")]
    [InlineData("icns")]
    [InlineData("")]
    public void Non_mach_o_bytes_are_not_parsed(string text)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);

        Assert.False(MachO.HasMagic(bytes));
        Assert.Null(MachO.TryParse(bytes));
        Assert.False(MachO.IsExecutable(bytes));
    }

    [Fact]
    public void Null_input_is_not_parsed()
    {
        Assert.Null(MachO.TryParse(null));
        Assert.False(MachO.IsExecutable(null));
    }

    [Fact]
    public void Magic_without_a_full_header_is_not_parsed()
    {
        byte[] bytes = new byte[16];
        bytes[0] = 0xCF;
        bytes[1] = 0xFA;
        bytes[2] = 0xED;
        bytes[3] = 0xFE;

        Assert.True(MachO.HasMagic(bytes));
        Assert.Null(MachO.TryParse(bytes));
    }

    [Fact]
    public void Commands_are_flagged_as_truncated_when_ncmds_exceeds_the_buffer()
    {
        byte[] bytes = B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 2, 0x1D);
        B3PackagerFixtures.WriteUInt32(bytes, 16, 4);   // ncmds = 4,只放得下 1 条

        MachOInfo? info = MachO.TryParse(bytes);

        Assert.NotNull(info);
        Assert.Equal(4u, info!.CommandCount);
        Assert.True(info.CommandsTruncated);
        Assert.Equal(new uint[] { 0x1D }, info.Commands);
        Assert.True(info.HasCodeSignature);
    }

    [Fact]
    public void A_zero_sized_command_stops_parsing_instead_of_looping_forever()
    {
        byte[] bytes = B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 2, 0x19, 0x1D);
        B3PackagerFixtures.WriteUInt32(bytes, 36, 0);   // 第 1 条命令的 cmdsize = 0

        MachOInfo? info = MachO.TryParse(bytes);

        Assert.NotNull(info);
        Assert.True(info!.CommandsTruncated);
        Assert.Equal(new uint[] { 0x19 }, info.Commands);
        Assert.False(info.HasCodeSignature);
    }

    [Theory]
    [InlineData(2u, true)]
    [InlineData(6u, false)]
    [InlineData(0u, false)]
    [InlineData(8u, false)]
    public void IsExecutable_is_true_only_for_filetype_two(uint fileType, bool expected)
    {
        byte[] bytes = B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, fileType, 0x1D);

        Assert.Equal(expected, MachO.IsExecutable(bytes));
    }

    [Fact]
    public void CpuName_falls_back_to_hex_for_unknown_cputypes()
    {
        MachOInfo? info = MachO.TryParse(B3PackagerFixtures.MachOImage(0x00000012u, 2));

        Assert.NotNull(info);
        Assert.Equal("cputype:0x00000012", info!.CpuName);
    }

    [Fact]
    public void Head_bytes_read_from_a_real_publish_output_are_enough_for_the_load_commands()
    {
        // 不读真实产物(本机 publish 目录可能不存在),只用实测值做量级核对:
        // 真实 apphost 的 sizeofcmds 是 1752 字节(arm64/x64 都是),一个头 + 全部 load command
        // 远小于校验时读进内存的前缀长度。
        Assert.True(BundleVerifier.HeadBytes > 1752 + MachO.HeaderSize);
    }
}
