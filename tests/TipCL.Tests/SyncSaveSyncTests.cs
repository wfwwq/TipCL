using System;
using System.Text;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A4:`Sync\SaveSync.cs`、`Sync\PhoneClient.cs`、`Sync\PhoneLink.cs`(A4 起参与编译)里纯函数的用例。
/// 全部不联网、不起进程、不读真实目录。
///
/// 期望值来自读过的源码:
///   SaveSync.Decide 119-156(三方比较:两端都没有 → Same;只有一端 → OnlyLocal/OnlyRemote;
///     相同 → Same;不同且没有 base → Conflict;remote==base → Push;local==base → Pull;都不等 → Conflict)、
///   SaveSync.Sha256Hex 103-117(小写十六进制)、
///   SaveSync.SafeWorldName 183-191(空/空白/以 . 开头/超 200 字符/含 \/:*?"&lt;&gt;| 或 NUL/首尾有空白都算不安全)、
///   SaveSync.LockFile 33(固定 "session.lock")、
///   PhoneClient.NormalizeAddress 92-107(补 http://、去掉结尾 /、没写端口就用 DefaultPort 27531;空地址抛 ArgumentException)、
///   PhoneClient.Enc 109-112(Uri.EscapeDataString)、
///   PhoneLink.Decide 211-228(有 base 时按「指纹变没变 / 修改时间变没变」映射成 B/L/R 再交给 SaveSync.Decide;
///     没 base 时两边修改时间相差 ≤2000ms 算 Same,否则 Conflict)。
/// </summary>
public class SyncSaveSyncTests
{
    // ---------- Decide:三方比较 ----------

    [Fact]
    public void Decide_returns_Same_when_neither_side_has_the_world()
    {
        Assert.Equal(SyncDecision.Same, SaveSync.Decide(null, null, null));
        Assert.Equal(SyncDecision.Same, SaveSync.Decide("", "", "hash"));
    }

    [Fact]
    public void Decide_returns_OnlyLocal_when_the_remote_side_is_missing()
    {
        Assert.Equal(SyncDecision.OnlyLocal, SaveSync.Decide("aaa", null, null));
        Assert.Equal(SyncDecision.OnlyLocal, SaveSync.Decide("aaa", "", "aaa"));
    }

    [Fact]
    public void Decide_returns_OnlyRemote_when_the_local_side_is_missing()
    {
        Assert.Equal(SyncDecision.OnlyRemote, SaveSync.Decide(null, "bbb", null));
        Assert.Equal(SyncDecision.OnlyRemote, SaveSync.Decide("", "bbb", "bbb"));
    }

    [Fact]
    public void Decide_returns_Same_when_both_sides_have_the_same_fingerprint()
    {
        Assert.Equal(SyncDecision.Same, SaveSync.Decide("same", "same", null));
        Assert.Equal(SyncDecision.Same, SaveSync.Decide("same", "same", "other"));
    }

    [Fact]
    public void Decide_is_a_Conflict_when_both_sides_differ_and_there_is_no_base()
    {
        Assert.Equal(SyncDecision.Conflict, SaveSync.Decide("local", "remote", null));
        Assert.Equal(SyncDecision.Conflict, SaveSync.Decide("local", "remote", ""));
    }

    [Fact]
    public void Decide_pushes_when_only_the_local_side_changed_since_the_base()
    {
        Assert.Equal(SyncDecision.Push, SaveSync.Decide("local", "base", "base"));
    }

    [Fact]
    public void Decide_pulls_when_only_the_remote_side_changed_since_the_base()
    {
        Assert.Equal(SyncDecision.Pull, SaveSync.Decide("base", "remote", "base"));
    }

    [Fact]
    public void Decide_is_a_Conflict_when_both_sides_changed_since_the_base()
    {
        Assert.Equal(SyncDecision.Conflict, SaveSync.Decide("local", "remote", "base"));
    }

    [Theory]
    [InlineData("board", "board", "board", SyncDecision.Same)]
    [InlineData("new", "board", "board", SyncDecision.Push)]
    [InlineData("board", "new", "board", SyncDecision.Pull)]
    [InlineData("newL", "newR", "board", SyncDecision.Conflict)]
    public void Decide_truth_table(string local, string remote, string baseHash, SyncDecision expected)
    {
        Assert.Equal(expected, SaveSync.Decide(local, remote, baseHash));
    }

    // ---------- 指纹与名字校验 ----------

    [Fact]
    public void Sha256Hex_is_lowercase_hex_of_the_sha256_digest()
    {
        // SHA256 的公开常量,用来固定「小写 hex」这个约定(Hex 109-117 用的是 "x2")
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            SaveSync.Sha256Hex(new byte[0]));
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            SaveSync.Sha256Hex(Encoding.ASCII.GetBytes("abc")));
    }

    [Theory]
    [InlineData("New World", true)]
    [InlineData("world_1", true)]
    [InlineData("a", true)]
    public void SafeWorldName_accepts_plain_names(string name, bool expected)
    {
        Assert.Equal(expected, SaveSync.SafeWorldName(name));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(".hidden", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("C:name", false)]
    [InlineData("a*b", false)]
    [InlineData("a?b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a<b", false)]
    [InlineData("a>b", false)]
    [InlineData("a|b", false)]
    [InlineData(" leading", false)]
    [InlineData("trailing ", false)]
    [InlineData("nul\0name", false)]
    public void SafeWorldName_rejects_unsafe_names(string? name, bool expected)
    {
        Assert.Equal(expected, SaveSync.SafeWorldName(name));
    }

    [Fact]
    public void SafeWorldName_allows_exactly_200_chars_but_not_201()
    {
        Assert.True(SaveSync.SafeWorldName(new string('w', 200)));
        Assert.False(SaveSync.SafeWorldName(new string('w', 201)));
    }

    [Fact]
    public void LockFile_name_is_the_one_the_game_uses()
    {
        Assert.Equal("session.lock", SaveSync.LockFile);
    }

    // ---------- PhoneClient.NormalizeAddress / Enc ----------

    [Theory]
    [InlineData("192.168.1.5", "http://192.168.1.5:27531")]
    [InlineData("192.168.1.5:27532", "http://192.168.1.5:27532")]
    [InlineData("192.168.1.5/", "http://192.168.1.5:27531")]
    [InlineData(" 192.168.1.5 ", "http://192.168.1.5:27531")]
    [InlineData("http://a.b:8080", "http://a.b:8080")]
    [InlineData("http://a.b:80", "http://a.b:80")]
    [InlineData("https://a.b", "https://a.b:27531")]
    public void NormalizeAddress_adds_the_scheme_and_the_default_port(string input, string expected)
    {
        Assert.Equal(expected, PhoneClient.NormalizeAddress(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalizeAddress_rejects_an_empty_address(string? input)
    {
        Assert.Throws<ArgumentException>(() => PhoneClient.NormalizeAddress(input));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "a%20b")]
    [InlineData("a/b?c", "a%2Fb%3Fc")]
    [InlineData(null, "")]
    public void Enc_escapes_with_EscapeDataString(string? input, string expected)
    {
        Assert.Equal(expected, PhoneClient.Enc(input));
    }

    // ---------- PhoneLink.Decide ----------

    [Fact]
    public void PhoneLink_Decide_is_Same_when_nothing_changed_since_the_base()
    {
        SyncDecision d = PhoneLink.Decide(
            "h", 100L,
            new PhoneClient.World { LastModified = 50L },
            new PhoneLink.Base { local_hash = "h", remote_mtime = 50L });

        // lc=false rc=false → SaveSync.Decide("B","B","B") → 两端相同
        Assert.Equal(SyncDecision.Same, d);
    }

    [Fact]
    public void PhoneLink_Decide_pushes_when_only_the_local_fingerprint_changed()
    {
        SyncDecision d = PhoneLink.Decide(
            "changed", 100L,
            new PhoneClient.World { LastModified = 50L },
            new PhoneLink.Base { local_hash = "h", remote_mtime = 50L });

        Assert.Equal(SyncDecision.Push, d);
    }

    [Fact]
    public void PhoneLink_Decide_pulls_when_only_the_remote_mtime_changed()
    {
        SyncDecision d = PhoneLink.Decide(
            "h", 100L,
            new PhoneClient.World { LastModified = 999L },
            new PhoneLink.Base { local_hash = "h", remote_mtime = 50L });

        Assert.Equal(SyncDecision.Pull, d);
    }

    [Fact]
    public void PhoneLink_Decide_is_a_Conflict_when_both_sides_changed()
    {
        SyncDecision d = PhoneLink.Decide(
            "changed", 100L,
            new PhoneClient.World { LastModified = 999L },
            new PhoneLink.Base { local_hash = "h", remote_mtime = 50L });

        Assert.Equal(SyncDecision.Conflict, d);
    }

    [Fact]
    public void PhoneLink_Decide_without_a_base_uses_a_two_second_tolerance()
    {
        // 223 行:没有基准时 |localMtime - remote.LastModified| <= 2000 算一样
        Assert.Equal(SyncDecision.Same, PhoneLink.Decide(
            "h", 1000L, new PhoneClient.World { LastModified = 3000L }, null));
        Assert.Equal(SyncDecision.Conflict, PhoneLink.Decide(
            "h", 1000L, new PhoneClient.World { LastModified = 3001L }, null));
    }

    [Fact]
    public void PhoneLink_Decide_without_a_local_hash_falls_back_to_the_remote_only_branch()
    {
        // 217-220 行:localHash == null 时直接交给 SaveSync.Decide,只有手机端有 → OnlyRemote
        Assert.Equal(SyncDecision.OnlyRemote, PhoneLink.Decide(
            null, 0L, new PhoneClient.World { LastModified = 1L }, null));
        // 两端都没有 → Same
        Assert.Equal(SyncDecision.Same, PhoneLink.Decide(null, 0L, null, null));
        // 只有电脑端有 → OnlyLocal
        Assert.Equal(SyncDecision.OnlyLocal, PhoneLink.Decide("h", 0L, null, null));
    }
}
