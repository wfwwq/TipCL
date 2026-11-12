using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TipCL.Core;
using TipCL.Core.Platform;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1a:<c>Plugins\SecretStore</c> 的非 Windows 分支(TCS2 = AES-256-GCM,主密钥存凭据库)。
///
/// 本机是 Windows,只能用注入入口 <c>internal SecretStore(string, ISecretStore, bool)</c> 把「按非 Windows 处理」
/// 打开来测 —— 没有真的 Keychain,凭据库用假实现;公开构造函数在 Windows 上仍然写 TCS1(既有 A5 用例覆盖)。
/// 所有文件都落在 <see cref="TestHome.Root"/> 下,不碰真实数据目录。
/// </summary>
public class B1aPluginSecretStoreAesTests
{
    private const string MasterKeyService = "TipCL plugin secrets";

    private const string MasterKeyAccount = "master-key";

    private sealed class FakeKeyStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public List<(string Service, string Account, string Value)> Writes { get; } = new List<(string, string, string)>();

        public int Reads { get; private set; }

        public bool ThrowOnGet { get; set; }

        public string Seed { get; set; }

        public string GetPassword(string service, string account)
        {
            Reads++;
            if (ThrowOnGet)
            {
                throw new OSError("(1, 'security find-generic-password', 'keychain unavailable')");
            }
            if (_values.TryGetValue(service + "|" + account, out string stored))
            {
                return stored;
            }
            return Seed;
        }

        public void SetPassword(string service, string account, string password)
        {
            Writes.Add((service, account, password));
            _values[service + "|" + account] = password;
        }

        public void DeletePassword(string service, string account)
        {
            _values.Remove(service + "|" + account);
        }
    }

    private static string TempPath(string name)
    {
        string dir = Path.Combine(TestHome.Root, "b1a-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static string Header(string path)
    {
        return Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4);
    }

    // ─────────────── 写入与往返 ───────────────

    [Fact]
    public void Aes_gcm_is_available_in_this_runtime()
    {
        // TCS2 的运行时前提(SecretStore.AesGcmEncrypt 会先判断它);mac 上是否支持未验证
        Assert.True(AesGcm.IsSupported);
    }

    [Fact]
    public void Non_windows_with_a_key_store_writes_the_TCS2_header_and_round_trips()
    {
        string path = TempPath("plugin.bin");
        var keys = new FakeKeyStore();
        var store = new SecretStore(path, keys, treatAsWindows: false);

        store.Set("config:token", "s3cret-value");

        Assert.True(File.Exists(path));
        Assert.StartsWith(TestHome.Root, path, StringComparison.Ordinal);
        Assert.Equal("TCS2", Header(path));
        Assert.Equal("s3cret-value", store.Get("config:token"));
        Assert.Equal(new List<string> { "config:token" }, store.Names());
        Assert.Equal("s3cret-value", new SecretStore(path, keys, treatAsWindows: false).Get("config:token"));

        store.DeleteAll();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TCS2_body_is_magic_nonce_tag_and_ciphertext()
    {
        string path = TempPath("layout.bin");
        var store = new SecretStore(path, new FakeKeyStore(), treatAsWindows: false);
        const string canary = "TAMPER-CANARY-0123456789";

        store.Set("k", canary);

        byte[] raw = File.ReadAllBytes(path);
        Assert.Equal("TCS2", Encoding.ASCII.GetString(raw, 0, 4));
        // 4 魔数 + 12 nonce + 16 tag + 密文(至少是 {"k": "..."} 的长度)
        Assert.True(raw.Length > 4 + 12 + 16, "密文段不该为空,实际长度 " + raw.Length);
        Assert.False(Contains(raw, Encoding.UTF8.GetBytes(canary)), "密文里不该出现明文");
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    hit = false;
                    break;
                }
            }
            if (hit)
            {
                return true;
            }
        }
        return false;
    }

    [Fact]
    public void Every_write_uses_a_fresh_nonce()
    {
        string path = TempPath("nonce.bin");
        var store = new SecretStore(path, new FakeKeyStore(), treatAsWindows: false);

        store.Set("k", "same-value");
        byte[] first = File.ReadAllBytes(path);
        store.Set("k", "same-value");
        byte[] second = File.ReadAllBytes(path);

        Assert.Equal("TCS2", Encoding.ASCII.GetString(first, 0, 4));
        Assert.Equal(first.Length, second.Length);
        Assert.NotEqual(first, second);
        // 只有前 4 字节(魔数)相同,nonce 起就分叉
        Assert.Equal(first.Take(4), second.Take(4));
        Assert.NotEqual(first.Skip(4).Take(12), second.Skip(4).Take(12));
    }

    [Fact]
    public void A_different_master_key_cannot_read_the_file()
    {
        string path = TempPath("otherkey.bin");
        new SecretStore(path, new FakeKeyStore(), treatAsWindows: false).Set("config:token", "s3cret-value");

        var otherKeys = new FakeKeyStore
        {
            Seed = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray())
        };
        var reader = new SecretStore(path, otherKeys, treatAsWindows: false);

        Assert.Null(reader.Get("config:token"));
        Assert.Empty(reader.Names());
    }

    [Fact]
    public void A_tampered_ciphertext_reads_as_empty_instead_of_returning_garbage()
    {
        string path = TempPath("tamper.bin");
        var keys = new FakeKeyStore();
        new SecretStore(path, keys, treatAsWindows: false).Set("config:token", "s3cret-value");
        byte[] raw = File.ReadAllBytes(path);
        raw[raw.Length - 1] ^= 0x5A;
        File.WriteAllBytes(path, raw);

        var reader = new SecretStore(path, keys, treatAsWindows: false);

        Assert.Null(reader.Get("config:token"));
        Assert.Empty(reader.Names());
    }

    [Fact]
    public void A_tampered_header_reads_as_empty_too()
    {
        string path = TempPath("head.bin");
        var keys = new FakeKeyStore();
        new SecretStore(path, keys, treatAsWindows: false).Set("config:token", "s3cret-value");
        byte[] raw = File.ReadAllBytes(path);
        raw[3] = (byte)'X';                    // 魔数被改 → 三个已知魔数都不匹配
        File.WriteAllBytes(path, raw);

        Assert.Null(new SecretStore(path, keys, treatAsWindows: false).Get("config:token"));
    }

    // ─────────────── 主密钥 ───────────────

    [Fact]
    public void The_master_key_is_created_once_and_reused()
    {
        string path = TempPath("master.bin");
        var keys = new FakeKeyStore();
        var store = new SecretStore(path, keys, treatAsWindows: false);

        store.Set("a", "1");
        store.Set("b", "2");

        Assert.Single(keys.Writes);                       // 只写过一次主密钥
        Assert.True(keys.Reads >= 2);                     // 每次写入都要先读主密钥
        Assert.Equal("1", store.Get("a"));
        Assert.Equal("2", store.Get("b"));
    }

    [Fact]
    public void The_master_key_is_32_random_bytes_stored_as_base64_under_the_expected_names()
    {
        string path = TempPath("master-names.bin");
        var keys = new FakeKeyStore();

        new SecretStore(path, keys, treatAsWindows: false).Set("k", "v");

        (string service, string account, string value) = keys.Writes.Single();
        Assert.Equal(MasterKeyService, service);
        Assert.Equal(MasterKeyAccount, account);
        Assert.Equal(32, Convert.FromBase64String(value).Length);
    }

    [Fact]
    public void A_corrupt_master_key_throws_and_is_not_overwritten()
    {
        string path = TempPath("corrupt-master.bin");
        var keys = new FakeKeyStore { Seed = "not-base64-at-all" };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        Assert.Throws<InvalidDataException>(() => store.Set("k", "v"));

        Assert.Empty(keys.Writes);                        // 不覆盖已有的(坏)条目
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_wrong_length_master_key_throws()
    {
        string path = TempPath("short-master.bin");
        var keys = new FakeKeyStore { Seed = Convert.ToBase64String(new byte[16]) };

        Assert.Throws<InvalidDataException>(() => new SecretStore(path, keys, treatAsWindows: false).Set("k", "v"));
        Assert.Empty(keys.Writes);
    }

    // ─────────────── 降级与兼容 ───────────────

    [Fact]
    public void Without_a_key_store_the_store_keeps_writing_TCS0()
    {
        string path = TempPath("plain.bin");
        var store = new SecretStore(path, null, treatAsWindows: false);

        store.Set("config:token", "s3cret-value");

        Assert.Equal("TCS0", Header(path));
        Assert.Equal("s3cret-value", new SecretStore(path, null, treatAsWindows: false).Get("config:token"));
    }

    [Fact]
    public void A_key_store_failure_makes_Set_throw_and_leaves_no_file_behind()
    {
        string path = TempPath("fail.bin");
        var keys = new FakeKeyStore { ThrowOnGet = true };
        var store = new SecretStore(path, keys, treatAsWindows: false);

        OSError error = Assert.Throws<OSError>(() => store.Set("config:token", "s3cret-value"));

        Assert.Contains("keychain unavailable", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path), "绝不能降级成 TCS0 落盘");
        Assert.False(File.Exists(path + ".tmp"), "临时文件也不该留下");
        Assert.False(Directory.EnumerateFiles(Path.GetDirectoryName(path)).Any());
    }

    [Fact]
    public void Old_TCS0_files_stay_readable_with_a_key_store_present()
    {
        string path = TempPath("legacy.bin");
        new SecretStore(path, null, treatAsWindows: false).Set("config:token", "legacy-value");
        Assert.Equal("TCS0", Header(path));

        Assert.Equal("legacy-value", new SecretStore(path, new FakeKeyStore(), treatAsWindows: false).Get("config:token"));
    }

    [Fact]
    public void TCS1_files_are_unreadable_off_windows()
    {
        string path = TempPath("dpapi.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("TCS1").Concat(new byte[16]).ToArray());

        var store = new SecretStore(path, new FakeKeyStore(), treatAsWindows: false);

        Assert.Null(store.Get("config:token"));
        Assert.Empty(store.Names());
    }

    [Fact]
    public void Short_and_unknown_headers_still_read_as_empty()
    {
        string directory = Path.GetDirectoryName(TempPath("anchor.bin"));
        string shortPath = Path.Combine(directory, "short.bin");
        File.WriteAllBytes(shortPath, new byte[] { 1, 2 });
        string unknown = Path.Combine(directory, "unknown.bin");
        File.WriteAllBytes(unknown, Encoding.ASCII.GetBytes("XXXX{}"));

        Assert.Null(new SecretStore(shortPath, new FakeKeyStore(), treatAsWindows: false).Get("k"));
        Assert.Null(new SecretStore(unknown, new FakeKeyStore(), treatAsWindows: false).Get("k"));
    }

    [Fact]
    public void Removing_the_last_key_deletes_the_file()
    {
        string path = TempPath("remove.bin");
        var store = new SecretStore(path, new FakeKeyStore(), treatAsWindows: false);
        store.Set("a", "1");
        store.Set("b", "2");

        store.Set("a", "");
        Assert.True(File.Exists(path));
        store.Set("b", null);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Numbers_and_line_breaks_survive_a_TCS2_round_trip()
    {
        string path = TempPath("utf8.bin");
        var store = new SecretStore(path, new FakeKeyStore(), treatAsWindows: false);
        string value = "中文令牌\nline2\t\"quoted\" 100%";

        store.Set("配置:令牌", value);

        Assert.Equal(value, store.Get("配置:令牌"));
        Assert.Equal(new List<string> { "配置:令牌" }, store.Names());
    }

    /// <summary>公开构造函数的行为按平台走:Windows 写 TCS1;非 Windows 无凭据库时写 TCS0 —— 与 A5 的用例同结论。</summary>
    [Fact]
    public void The_public_constructor_keeps_the_platform_behaviour()
    {
        string path = TempPath("public.bin");
        var store = new SecretStore(path);

        store.Set("k", "v");

        Assert.Equal(SecretStore.IsWindows ? "TCS1" : "TCS0", Header(path));
        Assert.Equal("v", store.Get("k"));
    }
}
