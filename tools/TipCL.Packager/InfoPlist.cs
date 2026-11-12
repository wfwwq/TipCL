using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using TipCL.Core.Launcher;

namespace TipCL.Packager;

/// <summary>Info.plist 里要写的键值(纯数据,便于单测塞转义用例)。</summary>
internal sealed class InfoPlistValues
{
    public required string BundleName { get; init; }

    public required string DisplayName { get; init; }

    public required string Executable { get; init; }

    public required string Identifier { get; init; }

    public required string ShortVersion { get; init; }

    public required string BundleVersion { get; init; }

    public required string PackageType { get; init; }

    public required string IconFile { get; init; }

    public required string InfoDictionaryVersion { get; init; }

    public required string MinimumSystemVersion { get; init; }

    public required bool HighResolutionCapable { get; init; }

    public required string PrincipalClass { get; init; }
}

/// <summary>
/// Info.plist 的生成与读取(纯函数:字符串进、字符串/字典出)。
///
/// 事实来源:任务书 B3 的「Info.plist」小节(apple plist 1.0 DOCTYPE、`<plist version="1.0"><dict>`、
/// `<key>` / `<string>` / `<true/>`、UTF-8)+ 同一节点名的键与值。`CFBundleName` / `CFBundleDisplayName` /
/// 版本号三个值来自 <see cref="AppInfo"/>(报告里写的「拿 Core 里的常量,不另抄一份」)。
/// `CFBundleIdentifier = com.tipcl.launcher` 是 Lead 选定的占位值,用户可以改(见 DECISIONS.md)。
/// </summary>
internal static class InfoPlist
{
    public const string BundleIdentifier = "com.tipcl.launcher";

    public const string PackageTypeApplication = "APPL";

    public const string InfoDictionaryVersionValue = "6.0";

    public const string MinimumSystemVersionValue = "10.15";

    public const string PrincipalClassValue = "NSApplication";

    public const bool HighResolutionCapableValue = true;

    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    private const string DocType =
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">";

    /// <summary>用 AppInfo 的常量组出本启动器的 Info.plist 值。</summary>
    public static InfoPlistValues ForTipCL()
    {
        return new InfoPlistValues
        {
            BundleName = AppInfo.APP_NAME,
            DisplayName = AppInfo.APP_DISPLAY_NAME,
            Executable = BundleLayout.ExecutableName,
            Identifier = BundleIdentifier,
            ShortVersion = AppInfo.APP_VERSION,
            BundleVersion = AppInfo.APP_VERSION,
            PackageType = PackageTypeApplication,
            IconFile = BundleLayout.IconFileName,
            InfoDictionaryVersion = InfoDictionaryVersionValue,
            MinimumSystemVersion = MinimumSystemVersionValue,
            HighResolutionCapable = HighResolutionCapableValue,
            PrincipalClass = PrincipalClassValue,
        };
    }

    /// <summary>
    /// 「键 → 期望值」表,顺序与 <see cref="Build"/> 写出来的顺序一致。
    /// 校验(<see cref="BundleVerifier"/>)与单测共用这一份,避免期望值抄两遍。
    /// </summary>
    public static Dictionary<string, string> ExpectedKeyValues(InfoPlistValues v)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CFBundleName"] = v.BundleName,
            ["CFBundleDisplayName"] = v.DisplayName,
            ["CFBundleExecutable"] = v.Executable,
            ["CFBundleIdentifier"] = v.Identifier,
            ["CFBundleShortVersionString"] = v.ShortVersion,
            ["CFBundleVersion"] = v.BundleVersion,
            ["CFBundlePackageType"] = v.PackageType,
            ["CFBundleIconFile"] = v.IconFile,
            ["CFBundleInfoDictionaryVersion"] = v.InfoDictionaryVersion,
            ["LSMinimumSystemVersion"] = v.MinimumSystemVersion,
            ["NSHighResolutionCapable"] = v.HighResolutionCapable ? "true" : "false",
            ["NSPrincipalClass"] = v.PrincipalClass,
        };
    }

    /// <summary>XML 文本转义。五个字符全转(任务书要求),不只是文本节点必须转的那三个。</summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        StringBuilder sb = new StringBuilder(text!.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                case '\'':
                    sb.Append("&apos;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>生成 plist 文本:UTF-8 内容、`\n` 换行、结尾一个换行。</summary>
    public static string Build(InfoPlistValues v)
    {
        StringBuilder sb = new StringBuilder(1024);
        sb.Append(Declaration).Append('\n');
        sb.Append(DocType).Append('\n');
        sb.Append("<plist version=\"1.0\">").Append('\n');
        sb.Append("<dict>").Append('\n');

        AppendString(sb, "CFBundleName", v.BundleName);
        AppendString(sb, "CFBundleDisplayName", v.DisplayName);
        AppendString(sb, "CFBundleExecutable", v.Executable);
        AppendString(sb, "CFBundleIdentifier", v.Identifier);
        AppendString(sb, "CFBundleShortVersionString", v.ShortVersion);
        AppendString(sb, "CFBundleVersion", v.BundleVersion);
        AppendString(sb, "CFBundlePackageType", v.PackageType);
        AppendString(sb, "CFBundleIconFile", v.IconFile);
        AppendString(sb, "CFBundleInfoDictionaryVersion", v.InfoDictionaryVersion);
        AppendString(sb, "LSMinimumSystemVersion", v.MinimumSystemVersion);
        sb.Append('\t').Append("<key>NSHighResolutionCapable</key>").Append('\n');
        sb.Append('\t').Append(v.HighResolutionCapable ? "<true/>" : "<false/>").Append('\n');
        AppendString(sb, "NSPrincipalClass", v.PrincipalClass);

        sb.Append("</dict>").Append('\n');
        sb.Append("</plist>").Append('\n');
        return sb.ToString();
    }

    /// <summary>plist 的 UTF-8 字节,不带 BOM。</summary>
    public static byte[] BuildBytes(InfoPlistValues v)
    {
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Build(v));
    }

    /// <summary>
    /// 把 plist 文本读成「键 → 字符串值」表(`true` 写成字符串 `"true"`)。
    /// 不是通用 plist 解析器:只认 `<dict>` 里 `&lt;key&gt;` 后跟 `&lt;string&gt;` 或 `&lt;true/&gt;` 这一种形态,
    /// 出现别的形态就抛异常(打包校验要的是「能按 XML 解析 + 键值对得上」)。
    /// </summary>
    public static Dictionary<string, string> ReadKeyValues(string xml)
    {
        XDocument doc = XDocument.Parse(xml, LoadOptions.None);
        XElement? dict = doc.Root?.Element("dict");
        if (dict == null)
        {
            throw new InvalidOperationException("Info.plist: no <dict> under <plist>");
        }

        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? pendingKey = null;
        foreach (XNode node in dict.Nodes())
        {
            if (node is not XElement element)
            {
                continue;
            }

            switch (element.Name.LocalName)
            {
                case "key":
                    pendingKey = element.Value;
                    break;
                case "string":
                    Add(result, pendingKey, element.Value);
                    pendingKey = null;
                    break;
                case "true":
                    Add(result, pendingKey, "true");
                    pendingKey = null;
                    break;
                case "false":
                    Add(result, pendingKey, "false");
                    pendingKey = null;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Info.plist: unsupported value element <" + element.Name.LocalName + ">");
            }
        }

        return result;
    }

    private static void Add(Dictionary<string, string> table, string? key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("Info.plist: value without a <key>");
        }

        table[key!] = value;
    }

    private static void AppendString(StringBuilder sb, string key, string value)
    {
        sb.Append('\t').Append("<key>").Append(Escape(key)).Append("</key>").Append('\n');
        sb.Append('\t').Append("<string>").Append(Escape(value)).Append("</string>").Append('\n');
    }
}
