using System;
using System.Text;
using TipCL.Packager;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B3:`Contents/MacOS/TipCL` 启动脚本的字节形态。
/// 期望文本逐字来自任务书 B3 的「Bundle layout」小节(LF、无 BOM、结尾一个换行)。
/// </summary>
public class B3LauncherScriptTests
{
    /// <summary>任务书给定的脚本正文(逐字)。</summary>
    private const string Expected =
        "#!/bin/sh\n"
        + "DIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n"
        + "case \"$(uname -m)\" in\n"
        + "  arm64) exec \"$DIR/arm64/TipCL\" \"$@\" ;;\n"
        + "  *) exec \"$DIR/x64/TipCL\" \"$@\" ;;\n"
        + "esac\n";

    [Fact]
    public void Text_is_byte_identical_to_the_script_given_by_the_brief()
    {
        Assert.Equal(Expected, LauncherScript.Text);
    }

    [Fact]
    public void Bytes_are_the_plain_utf8_of_the_text()
    {
        byte[] bytes = LauncherScript.Bytes();

        Assert.Equal(new UTF8Encoding(false).GetBytes(Expected), bytes);
        Assert.Equal(Expected.Length, bytes.Length);
        Assert.Equal((byte)'#', bytes[0]);
    }

    [Fact]
    public void Bytes_have_no_bom_no_cr_and_end_with_a_single_lf()
    {
        byte[] bytes = LauncherScript.Bytes();

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(-1, Array.IndexOf(bytes, (byte)'\r'));
        Assert.Equal((byte)'\n', bytes[bytes.Length - 1]);
        Assert.NotEqual((byte)'\n', bytes[bytes.Length - 2]);
    }

    [Fact]
    public void Shebang_is_the_first_line_and_the_script_has_six_lines()
    {
        string[] lines = LauncherScript.Lines;

        Assert.Equal(6, lines.Length);
        Assert.Equal("#!/bin/sh", lines[0]);
        Assert.Equal("esac", lines[5]);
        Assert.StartsWith("#!/bin/sh\n", LauncherScript.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  arm64) exec \"$DIR/arm64/TipCL\" \"$@\" ;;")]
    [InlineData("  *) exec \"$DIR/x64/TipCL\" \"$@\" ;;")]
    public void Both_architecture_branches_exec_the_matching_apphost(string branch)
    {
        Assert.Contains(branch, LauncherScript.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_resolves_its_own_directory_instead_of_using_the_cwd()
    {
        Assert.Contains("DIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"", LauncherScript.Text, StringComparison.Ordinal);
        Assert.Contains("case \"$(uname -m)\" in", LauncherScript.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_byte_is_ascii()
    {
        foreach (byte value in LauncherScript.Bytes())
        {
            Assert.InRange(value, 0x09, 0x7E);
        }
    }
}
