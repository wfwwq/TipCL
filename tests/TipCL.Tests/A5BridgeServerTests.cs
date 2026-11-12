using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A5:BridgeServer 里的静态纯函数(本轮才进编译的 BridgeServer.cs)。
///
/// **本文件不构造 BridgeServer、不 Start 监听**:BridgeServer.cs 第 102-119 行的构造函数里
/// 第 118 行是 `Dispatcher = new RpcDispatcher(api)`,而 RpcDispatcher 的构造函数第 48 行立刻做
/// `target.GetType()`,所以传 null 会 NullReferenceException;要实例化就必须先有 BackendApi,
/// 而 BackendApi 的构造函数(1738-1749 行)会往后台线程发 `AuthHttp.Warmup("https://login.live.com/")` —— 联网,
/// 红线禁止。所以这里只测不依赖实例的静态成员。
///
/// 期望值由 BridgeServer.cs 源码推出:ReadLine 353-384、SplitTarget 493-524、Phrase 542-560、
/// Unquote 726-747、QueryValues 706-724、NormalizeOrigin 132-177、TokenUrlSafe 121-130、FixedTimeEquals 188-201。
/// </summary>
public class A5BridgeServerTests
{
    // ─────────────── 常量 ───────────────

    [Fact]
    public void Loopback_host_and_server_version_constants()
    {
        Assert.Equal("127.0.0.1", BridgeServer.LOOPBACK_HOST);
        Assert.Equal("BaseHTTP/0.6 TipCL.Core", BridgeServer.ServerVersion);
    }

    // ─────────────── ReadLine ───────────────

    [Fact]
    public void ReadLine_strips_trailing_crlf_and_reports_no_eof()
    {
        var ms = new MemoryStream(Encoding.ASCII.GetBytes("abc\r\n"));

        string line = BridgeServer.ReadLine(ms, out bool eof, out bool tooLong);

        Assert.Equal("abc", line);
        Assert.False(eof);
        Assert.False(tooLong);
    }

    [Fact]
    public void ReadLine_strips_a_lone_lf_and_trims_a_trailing_cr_at_eof()
    {
        var ms = new MemoryStream(Encoding.ASCII.GetBytes("a\nb\r"));

        Assert.Equal("a", BridgeServer.ReadLine(ms, out bool eof1, out _));
        Assert.False(eof1);
        Assert.Equal("b", BridgeServer.ReadLine(ms, out bool eof2, out _));
        Assert.False(eof2);   // 最后一个字节是 \r 而不是流结束,所以 eof 仍然是 false
    }

    [Fact]
    public void ReadLine_on_an_empty_stream_reports_eof_and_an_empty_line()
    {
        var ms = new MemoryStream(Array.Empty<byte>());

        string line = BridgeServer.ReadLine(ms, out bool eof, out bool tooLong);

        Assert.Equal("", line);
        Assert.True(eof);
        Assert.False(tooLong);
    }

    [Fact]
    public void ReadLine_after_a_final_line_without_a_newline_returns_an_empty_eof_line()
    {
        var ms = new MemoryStream(Encoding.ASCII.GetBytes("tail"));

        Assert.Equal("tail", BridgeServer.ReadLine(ms, out bool eof1, out _));
        Assert.False(eof1);
        Assert.Equal("", BridgeServer.ReadLine(ms, out bool eof2, out bool tooLong2));
        Assert.True(eof2);
        Assert.False(tooLong2);
    }

    [Fact]
    public void ReadLine_returns_an_empty_string_for_a_bare_newline()
    {
        var ms = new MemoryStream(Encoding.ASCII.GetBytes("\n"));

        Assert.Equal("", BridgeServer.ReadLine(ms, out bool eof, out bool tooLong));
        Assert.False(eof);
        Assert.False(tooLong);
    }

    [Fact]
    public void ReadLine_decodes_bytes_as_latin1()
    {
        // 0xE4 在 Latin-1 里是 U+00E4;ReadLine 不做 UTF-8 解码(386-394 行的 Latin1)
        var ms = new MemoryStream(new byte[] { 0xE4, (byte)'\n' });

        Assert.Equal("\u00E4", BridgeServer.ReadLine(ms, out _, out _));
    }

    [Fact]
    public void ReadLine_refuses_a_line_longer_than_64k()
    {
        byte[] big = Enumerable.Repeat((byte)'a', 70000).ToArray();
        var ms = new MemoryStream(big);

        string line = BridgeServer.ReadLine(ms, out bool eof, out bool tooLong);

        Assert.Null(line);
        Assert.True(tooLong);
        Assert.False(eof);
    }

    [Fact]
    public void ReadLine_accepts_a_line_of_exactly_65536_bytes()
    {
        byte[] big = Enumerable.Repeat((byte)'a', 65536).Concat(new byte[] { (byte)'\n' }).ToArray();
        var ms = new MemoryStream(big);

        string line = BridgeServer.ReadLine(ms, out _, out bool tooLong);

        Assert.False(tooLong);
        Assert.Equal(65536, line.Length);
    }

    // ─────────────── SplitTarget ───────────────

    [Fact]
    public void SplitTarget_separates_path_and_query_and_drops_the_fragment()
    {
        BridgeServer.SplitTarget("/a?b=1#c", out string path, out string query);

        Assert.Equal("/a", path);
        Assert.Equal("b=1", query);
    }

    [Fact]
    public void SplitTarget_without_a_query_gives_an_empty_query()
    {
        BridgeServer.SplitTarget("/api/events", out string path, out string query);

        Assert.Equal("/api/events", path);
        Assert.Equal("", query);
    }

    [Fact]
    public void SplitTarget_strips_the_scheme_host_and_port()
    {
        BridgeServer.SplitTarget("http://127.0.0.1:8080/p?x=1", out string path, out string query);

        Assert.Equal("/p", path);
        Assert.Equal("x=1", query);
    }

    [Fact]
    public void SplitTarget_handles_the_network_path_form()
    {
        BridgeServer.SplitTarget("//host/p", out string path, out string query);
        Assert.Equal("/p", path);
        Assert.Equal("", query);

        BridgeServer.SplitTarget("//host", out string path2, out string query2);
        Assert.Equal("", path2);
        Assert.Equal("", query2);
    }

    [Fact]
    public void SplitTarget_keeps_a_relative_target_untouched()
    {
        BridgeServer.SplitTarget("rel/path", out string path, out string query);

        Assert.Equal("rel/path", path);
        Assert.Equal("", query);
    }

    [Fact]
    public void SplitTarget_treats_a_leading_token_as_a_scheme_only_before_the_first_slash()
    {
        // 513 行:只有冒号在第一个斜杠之前、且前面那段像协议名时才剥掉
        BridgeServer.SplitTarget("tipcl:custom/rest", out string path, out string query);
        Assert.Equal("custom/rest", path);
        Assert.Equal("", query);

        BridgeServer.SplitTarget("/a:b", out string path2, out string query2);
        Assert.Equal("/a:b", path2);
        Assert.Equal("", query2);
    }

    // ─────────────── Phrase ───────────────

    [Theory]
    [InlineData(100, "Continue")]
    [InlineData(200, "OK")]
    [InlineData(204, "No Content")]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(413, "Request Entity Too Large")]
    [InlineData(414, "Request-URI Too Long")]
    [InlineData(431, "Request Header Fields Too Large")]
    [InlineData(501, "Not Implemented")]
    [InlineData(505, "HTTP Version Not Supported")]
    public void Phrase_returns_the_reason_text_from_the_source_table(int code, string expected)
    {
        Assert.Equal(expected, BridgeServer.Phrase(code));
    }

    [Theory]
    [InlineData(201)]
    [InlineData(500)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Phrase_returns_an_empty_string_for_unlisted_codes(int code)
    {
        Assert.Equal("", BridgeServer.Phrase(code));
    }

    // ─────────────── Unquote ───────────────

    [Fact]
    public void Unquote_turns_plus_into_a_space()
    {
        Assert.Equal("a b", BridgeServer.Unquote("a+b"));
    }

    [Fact]
    public void Unquote_decodes_percent_escapes_as_utf8()
    {
        Assert.Equal("\u4E2D\u6587", BridgeServer.Unquote("%E4%B8%AD%E6%96%87"));
    }

    [Fact]
    public void Unquote_leaves_a_broken_escape_alone()
    {
        Assert.Equal("%zz", BridgeServer.Unquote("%zz"));
        Assert.Equal("100%", BridgeServer.Unquote("100%"));
        Assert.Equal("%2", BridgeServer.Unquote("%2"));
    }

    [Fact]
    public void Unquote_returns_the_input_when_there_is_nothing_to_decode()
    {
        Assert.Equal("plain", BridgeServer.Unquote("plain"));
        Assert.Equal("", BridgeServer.Unquote(""));
    }

    // ─────────────── QueryValues ───────────────

    [Fact]
    public void QueryValues_collects_repeated_keys_and_decodes_them()
    {
        List<string> values = BridgeServer.QueryValues("a=1&b=2&a=x+y", "a");

        Assert.Equal(new[] { "1", "x y" }, values);
    }

    [Fact]
    public void QueryValues_returns_an_empty_list_when_the_key_is_absent()
    {
        Assert.Empty(BridgeServer.QueryValues("a=1", "zz"));
        Assert.Empty(BridgeServer.QueryValues("", "a"));
    }

    [Fact]
    public void QueryValues_accepts_a_key_without_a_value()
    {
        Assert.Equal(new[] { "" }, BridgeServer.QueryValues("flag", "flag"));
    }

    // ─────────────── TokenUrlSafe ───────────────

    [Fact]
    public void TokenUrlSafe_returns_a_url_safe_base64_without_padding()
    {
        string token = BridgeServer.TokenUrlSafe(32);

        Assert.Equal(43, token.Length);                       // 32 字节 → 44 字符含一个 '=' → 去掉后 43
        Assert.DoesNotContain("+", token, StringComparison.Ordinal);
        Assert.DoesNotContain("/", token, StringComparison.Ordinal);
        Assert.DoesNotContain("=", token, StringComparison.Ordinal);
    }

    [Fact]
    public void TokenUrlSafe_returns_a_different_token_each_time()
    {
        Assert.NotEqual(BridgeServer.TokenUrlSafe(32), BridgeServer.TokenUrlSafe(32));
    }

    [Fact]
    public void TokenUrlSafe_can_produce_the_32_character_minimum_the_ctor_requires()
    {
        // 构造函数第 105 行要求 token 至少 32 字符;24 字节 → 32 字符,正好卡在下限
        Assert.Equal(32, BridgeServer.TokenUrlSafe(24).Length);
    }

    // ─────────────── NormalizeOrigin ───────────────

    [Theory]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData("http://127.0.0.1", "http://127.0.0.1")]
    [InlineData("http://127.0.0.1/", "http://127.0.0.1")]
    [InlineData("HTTP://127.0.0.1:80", "http://127.0.0.1:80")]
    [InlineData("http://127.0.0.1:8080/", "http://127.0.0.1:8080")]
    public void NormalizeOrigin_accepts_only_a_loopback_http_origin(string origin, string expected)
    {
        Assert.Equal(expected, BridgeServer.NormalizeOrigin(origin));
    }

    [Theory]
    [InlineData("https://127.0.0.1")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080/path")]
    [InlineData("http://127.0.0.1:8080?x")]
    [InlineData("http://user@127.0.0.1")]
    [InlineData("http://127.0.0.1:70000")]
    [InlineData("http://127.0.0.1:0")]
    [InlineData("http://127.0.0.1:abc")]
    public void NormalizeOrigin_rejects_anything_else(string origin)
    {
        Assert.Throws<ValueError>(() => BridgeServer.NormalizeOrigin(origin));
    }

    // ─────────────── FixedTimeEquals ───────────────

    [Fact]
    public void FixedTimeEquals_compares_content_and_length()
    {
        byte[] a = Encoding.UTF8.GetBytes("token-abcdefghijklmnop");

        Assert.True(BridgeServer.FixedTimeEquals(a, Encoding.UTF8.GetBytes("token-abcdefghijklmnop")));
        Assert.False(BridgeServer.FixedTimeEquals(a, Encoding.UTF8.GetBytes("token-abcdefghijklmnoX")));
        Assert.False(BridgeServer.FixedTimeEquals(a, Encoding.UTF8.GetBytes("token-abcdefghijklmno")));
        Assert.False(BridgeServer.FixedTimeEquals(a, Array.Empty<byte>()));
        Assert.True(BridgeServer.FixedTimeEquals(Array.Empty<byte>(), Array.Empty<byte>()));
    }
}
