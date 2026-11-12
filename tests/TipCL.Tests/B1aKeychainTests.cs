using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Platform;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1a:<see cref="MacKeychainSecretStore"/> 的命令行拼装、存储编码与错误语义。
///
/// 本机是 Windows,没有 <c>/usr/bin/security</c> —— 所以这里的用例<b>全部用假运行器</b>,
/// 一次真实进程都不起(红线:测试不许起外部进程)。真实 Keychain 行为未验证。
/// </summary>
public class B1aKeychainTests
{
    // ─────────────── 假运行器 ───────────────

    private sealed class FakeRunner
    {
        private readonly Func<string, IReadOnlyList<string>, SecretStoreCommandResult> _handler;

        public List<(string Exe, IReadOnlyList<string> Args)> Calls { get; } = new List<(string, IReadOnlyList<string>)>();

        public FakeRunner(Func<string, IReadOnlyList<string>, SecretStoreCommandResult> handler)
        {
            _handler = handler;
        }

        public SecretStoreCommandResult Run(string executablePath, IReadOnlyList<string> arguments)
        {
            Calls.Add((executablePath, arguments));
            return _handler(executablePath, arguments);
        }
    }

    private static FakeRunner Ok(string stdout = "")
    {
        return new FakeRunner((_, _) => new SecretStoreCommandResult(0, stdout, ""));
    }

    // ─────────────── 命令行拼装(纯函数) ───────────────

    [Theory]
    [InlineData("TipCL launcher tokens", "token-abc", new[] { "find-generic-password", "-s", "TipCL launcher tokens", "-a", "token-abc", "-w" })]
    [InlineData("a b", "c\"d", new[] { "find-generic-password", "-s", "a b", "-a", "c\"d", "-w" })]
    [InlineData("服务 名", "账号", new[] { "find-generic-password", "-s", "服务 名", "-a", "账号", "-w" })]
    [InlineData("-s", "-a", new[] { "find-generic-password", "-s", "-s", "-a", "-a", "-w" })]
    public void ReadArguments_is_the_argv_verbatim_without_shell_quoting(string service, string account, string[] expected)
    {
        IReadOnlyList<string> args = MacKeychainSecretStore.ReadArguments(service, account);

        Assert.Equal(expected, args);
        // 每个元素都是独立的 argv 项:空格 / 引号 / 中文 / 以 - 开头的值都不做转义
        Assert.Equal(service, args[2]);
        Assert.Equal(account, args[4]);
    }

    [Fact]
    public void ReadArguments_has_exactly_six_items()
    {
        Assert.Equal(6, MacKeychainSecretStore.ReadArguments("s", "a").Count);
    }

    [Theory]
    [InlineData("svc", "acct", "plain")]
    [InlineData("a b", "-x", "")]
    [InlineData("中文 服务", "账号", "中文值 with spaces")]
    public void WriteArguments_is_the_argv_verbatim_with_the_encoded_value_last(string service, string account, string value)
    {
        IReadOnlyList<string> args = MacKeychainSecretStore.WriteArguments(service, account, value);

        Assert.Equal(8, args.Count);
        Assert.Equal(
            new[] { "add-generic-password", "-U", "-s", service, "-a", account, "-w", MacKeychainSecretStore.EncodeValue(value) },
            args);
        Assert.Equal(service, args[3]);
        Assert.Equal(account, args[5]);
        Assert.Equal(MacKeychainSecretStore.EncodeValue(value), args.Last());
    }

    [Fact]
    public void DeleteArguments_is_the_argv_verbatim()
    {
        Assert.Equal(
            new[] { "delete-generic-password", "-s", "TipCL launcher tokens", "-a", "token-1" },
            MacKeychainSecretStore.DeleteArguments("TipCL launcher tokens", "token-1"));
    }

    [Fact]
    public void Security_tool_path_is_absolute()
    {
        FakeRunner runner = Ok();
        var store = new MacKeychainSecretStore(runner.Run);

        store.GetPassword("s", "a");

        Assert.Equal("/usr/bin/security", MacKeychainSecretStore.SecurityToolPath);
        Assert.Equal("/usr/bin/security", runner.Calls.Single().Exe);
    }

    // ─────────────── 存储编码 ───────────────

    [Theory]
    [InlineData("plain-ascii")]
    [InlineData("")]
    [InlineData("中文令牌")]
    [InlineData("line1\nline2\r\nline3")]
    [InlineData("with trailing  ")]
    [InlineData("quote\"and'apostrophe")]
    [InlineData("-leading-dash")]
    public void EncodeValue_and_DecodeValue_round_trip(string value)
    {
        string encoded = MacKeychainSecretStore.EncodeValue(value);

        Assert.StartsWith("b64:", encoded, StringComparison.Ordinal);
        if (value.Length > 0)
        {
            // 明文不出现在命令行参数里(空串是任何字符串的子串,跳过)
            Assert.DoesNotContain(value, encoded, StringComparison.Ordinal);
        }
        Assert.Equal(value, MacKeychainSecretStore.DecodeValue(encoded));
        // security -w 的输出带一个换行
        Assert.Equal(value, MacKeychainSecretStore.DecodeValue(encoded + "\n"));
    }

    [Fact]
    public void EncodeValue_round_trips_a_very_long_value()
    {
        string value = string.Join("", Enumerable.Repeat("令牌-0123456789", 400));   // 5600 字符

        Assert.Equal(value, MacKeychainSecretStore.DecodeValue(MacKeychainSecretStore.EncodeValue(value)));
    }

    [Theory]
    [InlineData("plain-value", "plain-value")]
    [InlineData("plain-value\n", "plain-value")]
    [InlineData("plain-value\r\n", "plain-value")]
    [InlineData("has trailing spaces  \n", "has trailing spaces  ")]
    public void DecodeValue_keeps_values_written_by_other_tools(string output, string expected)
    {
        Assert.Equal(expected, MacKeychainSecretStore.DecodeValue(output));
    }

    [Fact]
    public void DecodeValue_rejects_a_broken_base64_payload_without_echoing_it()
    {
        OSError error = Assert.Throws<OSError>(() => MacKeychainSecretStore.DecodeValue("b64:%%%not-base64%%%\n"));

        Assert.Contains("base64", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("%%%not-base64%%%", error.Message, StringComparison.Ordinal);
    }

    // ─────────────── GetPassword ───────────────

    [Fact]
    public void GetPassword_decodes_the_b64_payload_from_a_single_read_call()
    {
        FakeRunner runner = Ok(MacKeychainSecretStore.EncodeValue("秘密令牌") + "\n");
        var store = new MacKeychainSecretStore(runner.Run);

        Assert.Equal("秘密令牌", store.GetPassword("svc", "acct"));
        Assert.Single(runner.Calls);
        Assert.Equal(MacKeychainSecretStore.ReadArguments("svc", "acct"), runner.Calls[0].Args);
    }

    [Fact]
    public void GetPassword_returns_null_when_the_item_does_not_exist()
    {
        var runner = new FakeRunner((_, _) => new SecretStoreCommandResult(MacKeychainSecretStore.ItemNotFoundExitCode, "", "security: SecKeychainSearchCopyNext"));

        Assert.Equal(44, MacKeychainSecretStore.ItemNotFoundExitCode);
        Assert.Null(new MacKeychainSecretStore(runner.Run).GetPassword("svc", "acct"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(36)]
    [InlineData(128)]
    public void GetPassword_throws_OSError_for_other_exit_codes(int exitCode)
    {
        var runner = new FakeRunner((_, _) => new SecretStoreCommandResult(exitCode, "", "boom"));

        OSError error = Assert.Throws<OSError>(() => new MacKeychainSecretStore(runner.Run).GetPassword("svc", "acct"));

        Assert.Contains(exitCode.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
        // 与 Windows 实现同一条消息形状 (code, 'func', 'stderr')
        Assert.Contains("find-generic-password", error.Message, StringComparison.Ordinal);
    }

    // ─────────────── SetPassword ───────────────

    [Fact]
    public void SetPassword_writes_the_b64_encoded_value_and_never_the_raw_one()
    {
        FakeRunner runner = Ok();
        var store = new MacKeychainSecretStore(runner.Run);

        store.SetPassword("svc", "acct", "秘密");

        IReadOnlyList<string> args = runner.Calls.Single().Args;
        Assert.Equal(MacKeychainSecretStore.WriteArguments("svc", "acct", "秘密"), args);
        Assert.Equal("秘密", MacKeychainSecretStore.DecodeValue(args[7]));
        Assert.DoesNotContain("秘密", string.Join("|", args), StringComparison.Ordinal);
    }

    /// <summary>对齐 WinCredSecretStore.SetPassword 的 <c>password ?? "None"</c>。</summary>
    [Fact]
    public void SetPassword_stores_the_literal_None_for_a_null_password()
    {
        FakeRunner runner = Ok();

        new MacKeychainSecretStore(runner.Run).SetPassword("svc", "acct", null);

        Assert.Equal(MacKeychainSecretStore.EncodeValue("None"), runner.Calls.Single().Args[7]);
    }

    [Fact]
    public void SetPassword_keeps_the_password_out_of_the_error_message()
    {
        const string secret = "super-secret-token";
        var runner = new FakeRunner((_, args) => new SecretStoreCommandResult(1, "out", "error while storing " + args[args.Count - 1] + " for svc"));

        OSError error = Assert.Throws<OSError>(() => new MacKeychainSecretStore(runner.Run).SetPassword("svc", "acct", secret));

        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MacKeychainSecretStore.EncodeValue(secret), error.Message, StringComparison.Ordinal);
        Assert.Contains("<redacted>", error.Message, StringComparison.Ordinal);
        Assert.Contains("add-generic-password", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetPassword_treats_an_empty_password_as_the_empty_string()
    {
        FakeRunner runner = Ok();

        new MacKeychainSecretStore(runner.Run).SetPassword("svc", "acct", "");

        Assert.Equal("b64:", runner.Calls.Single().Args[7]);
        Assert.Equal("", MacKeychainSecretStore.DecodeValue("b64:\n"));
    }

    // ─────────────── DeletePassword ───────────────

    [Fact]
    public void DeletePassword_succeeds_on_exit_zero()
    {
        FakeRunner runner = Ok();
        var store = new MacKeychainSecretStore(runner.Run);

        store.DeletePassword("svc", "acct");

        Assert.Equal(MacKeychainSecretStore.DeleteArguments("svc", "acct"), runner.Calls.Single().Args);
    }

    [Fact]
    public void DeletePassword_throws_SecretNotFoundException_when_the_item_is_missing()
    {
        var runner = new FakeRunner((_, _) => new SecretStoreCommandResult(MacKeychainSecretStore.ItemNotFoundExitCode, "", ""));

        SecretNotFoundException error = Assert.Throws<SecretNotFoundException>(() => new MacKeychainSecretStore(runner.Run).DeletePassword("svc-9", "acct"));

        // WinCredSecretStore.DeletePassword 就是 new SecretNotFoundException(service)
        Assert.Equal("PasswordDeleteError", error.PyType);
        Assert.Equal("svc-9", error.Message);
    }

    [Fact]
    public void DeletePassword_throws_OSError_for_other_exit_codes()
    {
        var runner = new FakeRunner((_, _) => new SecretStoreCommandResult(1, "", "denied"));

        OSError error = Assert.Throws<OSError>(() => new MacKeychainSecretStore(runner.Run).DeletePassword("svc", "acct"));

        Assert.Contains("delete-generic-password", error.Message, StringComparison.Ordinal);
        Assert.Contains("denied", error.Message, StringComparison.Ordinal);
    }

    // ─────────────── 其它 ───────────────

    [Fact]
    public void A_runner_that_cannot_start_the_process_becomes_an_OSError()
    {
        var runner = new FakeRunner((_, _) => throw new InvalidOperationException("no such file"));

        OSError error = Assert.Throws<OSError>(() => new MacKeychainSecretStore(runner.Run).GetPassword("svc", "acct"));

        Assert.Contains("InvalidOperationException", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_can_be_constructed_without_starting_anything()
    {
        // 构造只保存委托:本机没有 /usr/bin/security,构造不该抛、也不该起进程
        var store = new MacKeychainSecretStore();

        Assert.IsAssignableFrom<ISecretStore>(store);
        Assert.Throws<ArgumentNullException>(() => new MacKeychainSecretStore(null));
    }

    [Fact]
    public void Every_ISecretStore_member_is_exercised_through_the_injected_runner()
    {
        var runner = new FakeRunner((_, args) => args[0] == "find-generic-password"
            ? new SecretStoreCommandResult(0, MacKeychainSecretStore.EncodeValue("v") + "\n", "")
            : new SecretStoreCommandResult(0, "", ""));
        ISecretStore store = new MacKeychainSecretStore(runner.Run);

        Assert.Equal("v", store.GetPassword("s", "a"));
        store.SetPassword("s", "a", "v");
        store.DeletePassword("s", "a");

        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(new[] { "find-generic-password", "add-generic-password", "delete-generic-password" },
            runner.Calls.Select(c => c.Args[0]).ToArray());
    }

    [Fact]
    public void Utf8_bytes_are_the_storage_unit_not_utf16()
    {
        // b64: 后面必须是密码的 UTF-8 字节(不是 UTF-16),否则别的工具读出来是乱码
        string value = "中";
        string payload = MacKeychainSecretStore.EncodeValue(value).Substring(4);

        Assert.Equal(Encoding.UTF8.GetBytes(value), Convert.FromBase64String(payload));
        Assert.Equal(value, Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }
}
