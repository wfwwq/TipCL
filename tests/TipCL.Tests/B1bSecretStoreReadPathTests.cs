using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Platform;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1b:插件 <c>SecretStore</c> **读取路径**的缺陷回归(Lead 在 B1a 验收时读代码发现)。
///
/// 缺陷:<c>ReadAll()</c> 整体包在 <c>catch (Exception)</c> 里,凭据库调用失败(钥匙串被锁、
/// 用户在授权弹窗点了拒绝)也被当成「文件读不出来」返回空表。而 <c>Set(name, value)</c> 是先
/// <c>ReadAll()</c> 再 <c>WriteAll()</c> —— 读失败而紧接着写成功时,文件会被「只含新条目」的内容覆盖,
/// 该插件原有的其它密钥全部丢失;同理 <c>Get()</c> 把「暂时读不到」说成「没存过」。
/// 附带:读取一个 <c>TCS2</c> 文件时如果凭据库里没有主密钥,原实现会生成并写入一把新密钥(读产生了写)。
///
/// 本机是 Windows,用 B1a 的注入入口 <c>internal SecretStore(path, ISecretStore, bool)</c> 走「按非 Windows 处理」;
/// 凭据库是进程内假实现(不起进程、不联网),所有文件都落在 <see cref="TestHome.Root"/> 下。
/// </summary>
public class B1bSecretStoreReadPathTests
{
    private const string KeyStoreFailure = "keychain unavailable";

    /// <summary>固定的合法 32 字节主密钥(b64),用来在多个假凭据库之间共享同一把密钥。</summary>
    private static readonly string SharedMasterKey =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private sealed class FakeKeyStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public int Gets { get; private set; }

        public int Sets { get; private set; }

        /// <summary>前 N 次 GetPassword 抛异常(模拟「读失败、紧接着写成功」的时序)。</summary>
        public int FailFirstGets { get; set; }

        public bool ThrowOnGet { get; set; }

        public string Seed { get; set; }

        public string GetPassword(string service, string account)
        {
            Gets++;
            if (ThrowOnGet || Gets <= FailFirstGets)
            {
                throw new OSError("(1, 'security find-generic-password', '" + KeyStoreFailure + "')");
            }
            if (_values.TryGetValue(service + "|" + account, out string stored))
            {
                return stored;
            }
            return Seed;
        }

        public void SetPassword(string service, string account, string password)
        {
            Sets++;
            _values[service + "|" + account] = password;
        }

        public void DeletePassword(string service, string account)
        {
            _values.Remove(service + "|" + account);
        }
    }

    private static string TempPath(string name)
    {
        string dir = Path.Combine(TestHome.Root, "b1b-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>用共享主密钥写两条密钥,返回文件字节。</summary>
    private static byte[] SeedTwoSecrets(string path)
    {
        var store = new SecretStore(path, new FakeKeyStore { Seed = SharedMasterKey }, treatAsWindows: false);
        store.Set("alpha", "1");
        store.Set("beta", "2");
        Assert.Equal("TCS2", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        return File.ReadAllBytes(path);
    }

    private static List<string> SortedNames(SecretStore store)
    {
        return store.Names().OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    // ─────────────── 核心回归:凭据库失败 ───────────────

    [Fact]
    public void A_key_store_failure_makes_Get_Names_and_Set_throw_and_leaves_the_file_byte_for_byte()
    {
        string path = TempPath("fail.bin");
        byte[] before = SeedTwoSecrets(path);
        var keys = new FakeKeyStore { Seed = SharedMasterKey, ThrowOnGet = true };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        Assert.Throws<OSError>(() => store.Get("alpha"));
        Assert.Throws<OSError>(() => store.Names());
        // 缺陷本体:读失败之后 Set 不能把文件覆盖成「只含 c」的内容
        Assert.Throws<OSError>(() => store.Set("gamma", "3"));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(0, keys.Sets);

        // 凭据库恢复后两条旧密钥都还在(证明上面不是「文件本来就读不出来」)
        keys.ThrowOnGet = false;
        Assert.Equal("1", store.Get("alpha"));
        Assert.Equal("2", store.Get("beta"));
        Assert.Equal(new List<string> { "alpha", "beta" }, SortedNames(store));
        Assert.Null(store.Get("gamma"));
    }

    [Fact]
    public void A_transient_read_failure_makes_Set_throw_before_any_write()
    {
        string path = TempPath("transient.bin");
        byte[] before = SeedTwoSecrets(path);
        // 第一次 GetPassword 抛、第二次成功:"读失败 + 写成功」的时序
        var keys = new FakeKeyStore { Seed = SharedMasterKey, FailFirstGets = 1 };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        OSError error = Assert.Throws<OSError>(() => store.Set("gamma", "3"));

        Assert.Contains(KeyStoreFailure, error.Message, StringComparison.Ordinal);
        Assert.Equal(1, keys.Gets);
        Assert.Equal(0, keys.Sets);
        Assert.Equal(before, File.ReadAllBytes(path));
        // 第二次读成功:真的是「被挡住」而不是「永远读不到」
        Assert.Equal("1", store.Get("alpha"));
        Assert.Equal(2, keys.Gets);
    }

    [Fact]
    public void The_exception_from_the_key_store_is_the_original_one()
    {
        string path = TempPath("identity.bin");
        SeedTwoSecrets(path);
        var keys = new FakeKeyStore { Seed = SharedMasterKey, ThrowOnGet = true };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        OSError error = Assert.Throws<OSError>(() => store.Get("alpha"));

        Assert.Contains(KeyStoreFailure, error.Message, StringComparison.Ordinal);
        Assert.IsType<OSError>(error);
    }

    // ─────────────── 读取不创建主密钥 ───────────────

    [Fact]
    public void A_missing_master_key_reads_as_empty_and_never_writes_one()
    {
        string path = TempPath("nokey.bin");
        SeedTwoSecrets(path);
        var keys = new FakeKeyStore();          // 凭据库里什么都没有
        var store = new SecretStore(path, keys, treatAsWindows: false);

        Assert.Null(store.Get("alpha"));
        Assert.Empty(store.Names());
        Assert.Equal(0, keys.Sets);             // 读操作绝不生成 / 写入主密钥
        Assert.True(keys.Gets >= 1);
    }

    [Fact]
    public void A_missing_master_key_leaves_the_file_byte_for_byte()
    {
        string path = TempPath("nokey-bytes.bin");
        byte[] before = SeedTwoSecrets(path);
        var store = new SecretStore(path, new FakeKeyStore(), treatAsWindows: false);

        Assert.Null(store.Get("alpha"));
        Assert.Empty(store.Names());

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Reading_a_TCS2_file_never_writes_to_the_key_store()
    {
        string path = TempPath("readonly.bin");
        SeedTwoSecrets(path);
        var keys = new FakeKeyStore { Seed = SharedMasterKey };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        Assert.Equal("1", store.Get("alpha"));
        Assert.Equal(new List<string> { "alpha", "beta" }, SortedNames(store));

        Assert.Equal(0, keys.Sets);
    }

    // ─────────────── 坏主密钥:抛而不是空 ───────────────

    [Fact]
    public void A_corrupt_master_key_makes_reading_throw_instead_of_reporting_empty()
    {
        string path = TempPath("badkey.bin");
        byte[] before = SeedTwoSecrets(path);
        var store = new SecretStore(path, new FakeKeyStore { Seed = "not-base64-at-all" }, treatAsWindows: false);

        Assert.Throws<InvalidDataException>(() => store.Get("alpha"));
        Assert.Throws<InvalidDataException>(() => store.Names());
        Assert.Throws<InvalidDataException>(() => store.Set("gamma", "3"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_wrong_length_master_key_makes_reading_throw()
    {
        string path = TempPath("shortkey.bin");
        SeedTwoSecrets(path);
        var store = new SecretStore(
            path,
            new FakeKeyStore { Seed = Convert.ToBase64String(new byte[16]) },
            treatAsWindows: false);

        Assert.Throws<InvalidDataException>(() => store.Get("alpha"));
        Assert.Throws<InvalidDataException>(() => store.Names());
    }

    // ─────────────── 仍然是「读不出来 → 空表」的四种情况 ───────────────

    [Fact]
    public void A_tampered_TCS2_ciphertext_still_reads_as_empty()
    {
        string path = TempPath("tamper.bin");
        SeedTwoSecrets(path);
        byte[] raw = File.ReadAllBytes(path);
        raw[raw.Length - 1] ^= 0x5A;
        File.WriteAllBytes(path, raw);
        var store = new SecretStore(path, new FakeKeyStore { Seed = SharedMasterKey }, treatAsWindows: false);

        Assert.Null(store.Get("alpha"));
        Assert.Null(store.Get("beta"));
        Assert.Empty(store.Names());
    }

    [Fact]
    public void A_different_master_key_still_reads_as_empty()
    {
        string path = TempPath("otherkey.bin");
        SeedTwoSecrets(path);
        var other = new FakeKeyStore { Seed = Convert.ToBase64String(Enumerable.Repeat((byte)0xA5, 32).ToArray()) };
        var store = new SecretStore(path, other, treatAsWindows: false);

        Assert.Null(store.Get("alpha"));
        Assert.Empty(store.Names());
    }

    [Fact]
    public void A_truncated_TCS2_payload_still_reads_as_empty()
    {
        string path = TempPath("truncated.bin");
        SeedTwoSecrets(path);
        byte[] raw = File.ReadAllBytes(path);
        // 只留魔数 + 4 字节(gcm 需要 12 nonce + 16 tag)
        File.WriteAllBytes(path, raw.Take(8).ToArray());
        var store = new SecretStore(path, new FakeKeyStore { Seed = SharedMasterKey }, treatAsWindows: false);

        Assert.Null(store.Get("alpha"));
        Assert.Empty(store.Names());
    }

    [Fact]
    public void A_truncated_TCS2_payload_does_not_touch_the_key_store()
    {
        string path = TempPath("truncated-keys.bin");
        SeedTwoSecrets(path);
        byte[] raw = File.ReadAllBytes(path);
        File.WriteAllBytes(path, raw.Take(8).ToArray());
        var keys = new FakeKeyStore { Seed = SharedMasterKey, ThrowOnGet = true };

        // 截断的判定在取主密钥之前,所以凭据库坏了也不影响这条路径的结果
        Assert.Null(new SecretStore(path, keys, treatAsWindows: false).Get("alpha"));
        Assert.Equal(0, keys.Gets);
    }

    [Fact]
    public void A_TCS2_file_without_any_key_store_makes_reads_throw()
    {
        string path = TempPath("nokeychain.bin");
        SeedTwoSecrets(path);
        var store = new SecretStore(path, null, treatAsWindows: false);

        // 没有凭据库就问不出主密钥:报错比返回空表安全(返回空表会让 Set 把文件覆盖成 TCS0)
        Assert.Throws<PlatformNotSupportedException>(() => store.Get("alpha"));
        Assert.Throws<PlatformNotSupportedException>(() => store.Names());
    }

    // ─────────────── 只影响 TCS2:TCS0 / 空文件不受影响 ───────────────

    [Fact]
    public void TCS0_files_stay_readable_even_when_the_key_store_is_broken()
    {
        string path = TempPath("legacy.bin");
        new SecretStore(path, null, treatAsWindows: false).Set("alpha", "legacy-value");
        Assert.Equal("TCS0", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        var keys = new FakeKeyStore { ThrowOnGet = true };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        Assert.Equal("legacy-value", store.Get("alpha"));
        Assert.Equal(new List<string> { "alpha" }, store.Names());
        Assert.Equal(0, keys.Gets);             // TCS0 根本不需要凭据库
    }

    [Fact]
    public void Reading_a_missing_file_never_touches_the_key_store()
    {
        string path = TempPath("missing.bin");
        var keys = new FakeKeyStore { ThrowOnGet = true };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        Assert.Null(store.Get("alpha"));
        Assert.Empty(store.Names());
        Assert.Equal(0, keys.Gets);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_failed_read_does_not_create_or_delete_the_file()
    {
        string path = TempPath("no-touch.bin");
        byte[] before = SeedTwoSecrets(path);
        var store = new SecretStore(path, new FakeKeyStore { ThrowOnGet = true }, treatAsWindows: false);

        Assert.Throws<OSError>(() => store.Get("alpha"));
        Assert.Throws<OSError>(() => store.Set("alpha", null));

        Assert.True(File.Exists(path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
