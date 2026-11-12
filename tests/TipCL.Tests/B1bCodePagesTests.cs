using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using TipCL.Core.Launcher;
using TipCL.Core.Platform;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1b:代码页注册(A3 清点表 E1–E5)。
///
/// net8.0 默认没有 437 / 936 / 54936,<c>Encoding.GetEncoding</c> 会抛,源码各处的 catch 把它静默降级成
/// UTF-8 / ASCII;<c>PlatformBootstrap.Init</c> 现在第一句就调 <see cref="CodePages.EnsureRegistered"/>。
///
/// <b>本文件自带一个 ModuleInitializer</b>:<c>Utils.Cp437</c> 把结果缓存在静态字段里,谁先访问谁决定结果,
/// 所以「注册」必须发生在任何用例访问编码之前 —— 不能依赖用例执行顺序,也不改 <c>TestHome.Init()</c>
/// (见 DECISIONS.md D-16)。
/// </summary>
internal static class B1bCodePagesModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        CodePages.EnsureRegistered();
    }
}

public class B1bCodePagesTests
{
    // 中文 = U+4E2D U+6587,GBK 字节是 D6 D0 CE C4
    private const string Chinese = "\u4E2D\u6587";

    [Fact]
    public void EnsureRegistered_can_be_called_repeatedly()
    {
        for (int i = 0; i < 5; i++)
        {
            CodePages.EnsureRegistered();
        }

        Assert.Equal(437, Encoding.GetEncoding(437).CodePage);
        Assert.Equal(936, Encoding.GetEncoding(936).CodePage);
        Assert.Equal(54936, Encoding.GetEncoding(54936).CodePage);
    }

    [Theory]
    [InlineData(437)]
    [InlineData(936)]
    [InlineData(54936)]
    public void The_windows_code_pages_are_available_by_number(int codePage)
    {
        Encoding encoding = Encoding.GetEncoding(codePage);

        Assert.Equal(codePage, encoding.CodePage);
    }

    [Theory]
    [InlineData("gbk", 936)]
    [InlineData("gb2312", 936)]
    [InlineData("GB2312", 936)]
    [InlineData("gb18030", 54936)]
    public void The_windows_code_pages_are_available_by_name(string name, int codePage)
    {
        Assert.Equal(codePage, Encoding.GetEncoding(name).CodePage);
    }

    [Fact]
    public void Cp437_decodes_byte_0x80_as_the_ccedilla_character()
    {
        // CP437 的 0x80 是 Ç(U+00C7);回落成 UTF-8 时这里会变成 U+FFFD
        string decoded = Encoding.GetEncoding(437).GetString(new byte[] { 0x80 });

        Assert.Equal("\u00C7", decoded);
    }

    [Fact]
    public void Code_page_936_decodes_the_two_chinese_bytes()
    {
        byte[] gbk = new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 };

        Assert.Equal(Chinese, Encoding.GetEncoding(936).GetString(gbk));
    }

    [Fact]
    public void Utils_Cp437_is_now_the_real_code_page_437()
    {
        // E1:Utils.cs 的 Cp437 拿到的不再是 catch 里的 UTF8 回落
        Assert.Equal(437, Utils.Cp437.CodePage);
        Assert.Equal("ABC", Utils.Cp437.GetString(new byte[] { 0x41, 0x42, 0x43 }));
    }

    [Fact]
    public void Java_Gbk_returns_code_page_936_and_encodes_chinese()
    {
        // E2:Java.cs 的 internal Gbk()
        Encoding gbk = Java.Gbk();

        Assert.Equal(936, gbk.CodePage);
        Assert.Equal(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }, gbk.GetBytes(Chinese));
    }

    [Fact]
    public void Crash_ReadText_falls_back_to_gb18030_for_non_utf8_bytes()
    {
        // E3:Crash.cs 的 Strict(54936) —— 严格 UTF-8 解不开时按 GB18030 解
        string path = Path.Combine(TestHome.Root, "b1b-gbk-" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllBytes(path, new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 });

        Assert.Equal(Chinese, Crash.ReadText(path));
    }

    private static NetResponse Response(byte[] body, string charset)
    {
        ByteArrayContent content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = charset };
        HttpResponseMessage message = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        return new NetResponse(message, "http://127.0.0.1/b1b-in-memory", 5.0, CancellationToken.None);
    }

    [Fact]
    public void NetResponse_Text_decodes_a_gbk_charset_body()
    {
        // E4:NetResponse.cs 按 Content-Type 的 charset 名取编码
        using NetResponse response = Response(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }, "gbk");

        Assert.Equal("gbk", response.Encoding());
        Assert.Equal(Chinese, response.Text());
    }

    [Fact]
    public void NetResponse_Text_decodes_a_gb2312_charset_body()
    {
        using NetResponse response = Response(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }, "gb2312");

        Assert.Equal(Chinese, response.Text());
    }

    [Fact]
    public void NetResponse_Json_decodes_a_gbk_charset_body()
    {
        byte[] body = Encoding.ASCII.GetBytes("{\"k\": \"");
        byte[] tail = Encoding.ASCII.GetBytes("\"}");
        byte[] json = new byte[body.Length + 4 + tail.Length];
        Array.Copy(body, 0, json, 0, body.Length);
        Array.Copy(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }, 0, json, body.Length, 4);
        Array.Copy(tail, 0, json, body.Length + 4, tail.Length);
        using NetResponse response = Response(json, "gbk");

        var parsed = (TipCL.Core.PyDict)response.Json();

        Assert.Equal(Chinese, parsed.Get("k") as string);
    }
}
