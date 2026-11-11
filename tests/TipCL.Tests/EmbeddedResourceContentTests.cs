using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A2 步骤 4-2:嵌入内容与 Windows 源文件逐字节一致。
/// 期望哈希 = 对本机 Windows 源文件 <c>D:\项目\tipcl\windos\tipcl\src\TipCL\Resources\...</c>
/// 执行 <c>Get-FileHash -Algorithm SHA256</c> 的结果(资源必须全部一致)。
/// </summary>
public class EmbeddedResourceContentTests
{
    [Theory]
    [InlineData("TipCL.Core.locales.en.json", "EFD11008BF44BBE0D9D83A2BC617E678424B50FDAFFF8CC4ABE3D9BA43B64468")]
    [InlineData("TipCL.Core.locales.zh_CN.json", "E00BFB8BA5D9D2E0B7EDA1D598BF54785BAD9B89AEC8639C3F7FFA986761FD53")]
    [InlineData("TipCL.Core.ai.model_rules.json", "152B2780D4CD08ABCB0A903472F4E357080F61E6E8185BD776846B9D5BFBC4DC")]
    [InlineData("TipCL.Core.plugin-host.host.mjs", "978A08460765687C0984A87777A4F9B35188716649FBA7C2CC705F5D75A5F38D")]
    [InlineData("TipCL.Core.plugin-host.host-console.mjs", "D660FCC13DE5A86C7362F8BA55BB4E700C14CF88C793B51C1CB2B6C7CA07AA2E")]
    [InlineData("TipCL.Core.plugin-host.host-loader.mjs", "A980A38E3ED9E263659AA035344400B64CDC26601FB2F2D4B48DA381AAD37892")]
    [InlineData("TipCL.Core.plugin-host.host-plugin.mjs", "3D6B89B1D26C417374D689FD658FAC1428FD69A31679D9D0B30F73899ACEAF57")]
    [InlineData("TipCL.Core.plugin-host.host-rpc.mjs", "850A41E7A86A3AD0B36E03DBAC5D27DCAC937104FFC51C521CBBCE3F522C7C96")]
    [InlineData("TipCL.Core.plugin-host.host-sessions.mjs", "4A9C6758029A4617A0E6906CAFEB149F7F6836F9A1926FF8DEA4A0961D55BDCE")]
    [InlineData("TipCL.Core.plugin-host.host-tools.mjs", "8B20C8CD3AB7A077E096B500F5130C6CEE225F6D684F73DC7466F7FFF1B22DBA")]
    [InlineData("TipCL.Core.plugin-host.host-util.mjs", "660B8F4008FF95D14DC664D83C5F8052638180AB87CAE65C1A046506AB82670F")]
    [InlineData("TipCL.Core.plugin-host.host-web.mjs", "8CCF206BF7EB02C5EE4429D86324AED9C559FF3F9D22A5BEBFFEBD22769B1642")]
    [InlineData("TipCL.Core.plugin-host.sdk.js", "2DB4FE00439117260F64EE8D64BD89A16E784788817B10D47E9AE193D99824AA")]
    [InlineData("TipCL.Core.plugin-host.sdk-events-worker.js", "2E1E74346F6A0F6C882DCFD051A19A3E86E3D0D252CE6885389054636D45D14D")]
    [InlineData("TipCL.Core.plugin-host.tipcl-plugin.d.ts", "5BF19988BC3F512CB9FCA70DC884A3496A6BED2A696EA8B5325FE791DF5C6A64")]
    public void EmbeddedBytes_MatchWindowsSourceSha256(string resourceName, string expectedSha256)
    {
        byte[] bytes = ReadAll(resourceName);

        string actual = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(expectedSha256, actual);
    }

    [Fact]
    public void ChineseLocale_IsValidUtf8AndStillCarriesTheChineseTranslations()
    {
        byte[] bytes = ReadAll("TipCL.Core.locales.zh_CN.json");

        // 严格 UTF-8 解码:任何被换行符转换或编码改写都会在这里炸。
        string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

        // 不要用 Assert.DoesNotContain(string, string) 查 U+FEFF:xunit 走 CurrentCulture,
        // 而 U+FEFF 是可忽略字符,任何字符串都会被判成「在 0 处找到」。
        // 这里用 char 比较(序数)才可靠;逐字节一致性已由上面的 SHA256 用例覆盖。
        Assert.NotEqual('\uFEFF', text[0]);
        Assert.True(text.IndexOf('\uFEFF') < 0, "解码后的文本里出现了 U+FEFF");
        Assert.Equal('{', text[0]);

        Assert.Contains("中文", text);
        Assert.StartsWith("{", text.TrimStart());
    }

    private static byte[] ReadAll(string resourceName)
    {
        Assembly asm = typeof(PyJson).Assembly;
        using Stream? s = asm.GetManifestResourceStream(resourceName);
        Assert.NotNull(s);
        using MemoryStream ms = new MemoryStream();
        s!.CopyTo(ms);
        return ms.ToArray();
    }
}
