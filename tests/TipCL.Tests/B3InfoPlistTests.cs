using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using TipCL.Core.Launcher;
using TipCL.Packager;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B3:`Contents/Info.plist` 的生成与读回。
/// 键名与值来自任务书 B3 的「Info.plist」小节;三个值取自 Core 的 <see cref="AppInfo"/>
/// (任务书:不许再抄一份常量)。
/// </summary>
public class B3InfoPlistTests
{
    /// <summary>任务书点名的 12 个键。</summary>
    private static readonly string[] BriefKeys =
    {
        "CFBundleName",
        "CFBundleDisplayName",
        "CFBundleExecutable",
        "CFBundleIdentifier",
        "CFBundleShortVersionString",
        "CFBundleVersion",
        "CFBundlePackageType",
        "CFBundleIconFile",
        "CFBundleInfoDictionaryVersion",
        "LSMinimumSystemVersion",
        "NSHighResolutionCapable",
        "NSPrincipalClass",
    };

    [Fact]
    public void Bundle_name_display_name_and_versions_come_from_Core_AppInfo()
    {
        InfoPlistValues values = InfoPlist.ForTipCL();

        Assert.Equal(AppInfo.APP_NAME, values.BundleName);
        Assert.Equal(AppInfo.APP_DISPLAY_NAME, values.DisplayName);
        Assert.Equal(AppInfo.APP_VERSION, values.ShortVersion);
        Assert.Equal(AppInfo.APP_VERSION, values.BundleVersion);
        Assert.Equal("TipCL", values.BundleName);
        Assert.Equal("1.1.0", values.ShortVersion);
    }

    [Fact]
    public void The_other_keys_use_the_placeholder_values_ruled_by_the_lead()
    {
        InfoPlistValues values = InfoPlist.ForTipCL();

        Assert.Equal("TipCL", values.Executable);
        Assert.Equal("com.tipcl.launcher", values.Identifier);
        Assert.Equal("TipCL", values.IconFile);
        Assert.Equal("APPL", values.PackageType);
        Assert.Equal("6.0", values.InfoDictionaryVersion);
        Assert.Equal("10.15", values.MinimumSystemVersion);
        Assert.Equal("NSApplication", values.PrincipalClass);
        Assert.True(values.HighResolutionCapable);
        Assert.Equal("com.tipcl.launcher", InfoPlist.BundleIdentifier);
    }

    [Fact]
    public void Build_opens_with_the_declaration_the_apple_doctype_and_a_plist_dict()
    {
        string[] lines = InfoPlist.Build(InfoPlist.ForTipCL()).Split('\n');

        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", lines[0]);
        Assert.Equal(
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
            lines[1]);
        Assert.Equal("<plist version=\"1.0\">", lines[2]);
        Assert.Equal("<dict>", lines[3]);
        Assert.Equal("</dict>", lines[lines.Length - 3]);
        Assert.Equal("</plist>", lines[lines.Length - 2]);
    }

    [Fact]
    public void BuildBytes_is_valid_xml_with_a_plist_root_and_a_dict_child()
    {
        byte[] bytes = InfoPlist.BuildBytes(InfoPlist.ForTipCL());

        XDocument document = XDocument.Parse(B3PackagerFixtures.Utf8Strict(bytes));
        Assert.Equal("plist", document.Root!.Name.LocalName);
        Assert.Equal("1.0", document.Root.Attribute("version")!.Value);
        Assert.Equal("dict", document.Root.Elements().Single().Name.LocalName);
    }

    [Fact]
    public void BuildBytes_is_utf8_without_a_bom_and_with_lf_only()
    {
        byte[] bytes = InfoPlist.BuildBytes(InfoPlist.ForTipCL());

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(-1, Array.IndexOf(bytes, (byte)'\r'));
        Assert.True(bytes[bytes.Length - 1] == (byte)'\n');

        // 严格 UTF-8 解码(含 AppInfo.APP_DISPLAY_NAME 里的中文)必须成功
        string text = B3PackagerFixtures.Utf8Strict(bytes);
        Assert.Contains(AppInfo.APP_DISPLAY_NAME, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedKeyValues_has_exactly_the_twelve_keys_from_the_brief()
    {
        Dictionary<string, string> expected = InfoPlist.ExpectedKeyValues(InfoPlist.ForTipCL());

        Assert.Equal(BriefKeys.Length, expected.Count);
        foreach (string key in BriefKeys)
        {
            Assert.True(expected.ContainsKey(key), "少了键: " + key);
        }
    }

    [Fact]
    public void Every_written_key_reads_back_with_the_expected_value()
    {
        InfoPlistValues values = InfoPlist.ForTipCL();
        Dictionary<string, string> written = InfoPlist.ReadKeyValues(InfoPlist.Build(values));
        Dictionary<string, string> expected = InfoPlist.ExpectedKeyValues(values);

        Assert.Equal(expected.Count, written.Count);
        foreach (KeyValuePair<string, string> pair in expected)
        {
            Assert.True(written.TryGetValue(pair.Key, out string? actual), "没写出来: " + pair.Key);
            Assert.Equal(pair.Value, actual);
        }
    }

    [Fact]
    public void High_Resolution_Capable_is_a_true_element_not_a_string()
    {
        string text = InfoPlist.Build(InfoPlist.ForTipCL());

        Assert.Contains("<key>NSHighResolutionCapable</key>\n\t<true/>\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<string>true</string>", text, StringComparison.Ordinal);

        Dictionary<string, string> written = InfoPlist.ReadKeyValues(text);
        Assert.Equal("true", written["NSHighResolutionCapable"]);
    }

    [Theory]
    [InlineData("&", "&amp;")]
    [InlineData("<", "&lt;")]
    [InlineData(">", "&gt;")]
    [InlineData("\"", "&quot;")]
    [InlineData("'", "&apos;")]
    public void Escape_covers_the_five_xml_characters(string raw, string escaped)
    {
        Assert.Equal(escaped, InfoPlist.Escape(raw));
    }

    [Fact]
    public void Escaped_values_survive_a_real_xml_round_trip()
    {
        // 期望值是 \uXXXX 转义写的(D-58:全角/非 ASCII 字面量不要整句抄)
        string raw = "A & B < C > D \"quoted\" 'apos' \u4E2D\u6587\uFF0C\u53E5\u53F7\u3002";

        InfoPlistValues values = new InfoPlistValues
        {
            BundleName = raw,
            DisplayName = raw,
            Executable = "TipCL",
            Identifier = "com.tipcl.launcher",
            ShortVersion = "1.1.0",
            BundleVersion = "1.1.0",
            PackageType = "APPL",
            IconFile = "TipCL",
            InfoDictionaryVersion = "6.0",
            MinimumSystemVersion = "10.15",
            HighResolutionCapable = true,
            PrincipalClass = "NSApplication",
        };

        string xml = InfoPlist.Build(values);
        Assert.Contains("&amp;", xml, StringComparison.Ordinal);
        Assert.Contains("&lt;", xml, StringComparison.Ordinal);
        Assert.Contains("&quot;", xml, StringComparison.Ordinal);

        Dictionary<string, string> written = InfoPlist.ReadKeyValues(xml);
        Assert.Equal(raw, written["CFBundleName"]);
        Assert.Equal(raw, written["CFBundleDisplayName"]);

        // 再用 XmlDocument 独立解析一次:转义必须让文档合法
        XmlDocument document = new XmlDocument();
        document.LoadXml(xml);
        Assert.Equal("plist", document.DocumentElement!.Name);
    }

    [Fact]
    public void ReadKeyValues_rejects_an_unsupported_value_element()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\"><dict>\n"
            + "<key>K</key>\n<integer>1</integer>\n</dict></plist>\n";

        Assert.Throws<InvalidOperationException>(() => InfoPlist.ReadKeyValues(xml));
    }

    [Fact]
    public void ReadKeyValues_rejects_a_value_without_a_key()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\"><dict>\n"
            + "<string>orphan</string>\n</dict></plist>\n";

        Assert.Throws<InvalidOperationException>(() => InfoPlist.ReadKeyValues(xml));
    }

    [Fact]
    public void ReadKeyValues_reads_false_as_the_string_false()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\"><dict>\n"
            + "<key>Off</key>\n<false/>\n</dict></plist>\n";

        Assert.Equal("false", InfoPlist.ReadKeyValues(xml)["Off"]);
    }

    [Fact]
    public void ReadKeyValues_throws_when_there_is_no_dict_under_plist()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\"><array/></plist>\n";

        Assert.Throws<InvalidOperationException>(() => InfoPlist.ReadKeyValues(xml));
    }

    [Fact]
    public void ReadKeyValues_reports_a_malformed_document_by_throwing()
    {
        Assert.Throws<XmlException>(() => InfoPlist.ReadKeyValues("<plist><dict></plist>"));
    }
}
