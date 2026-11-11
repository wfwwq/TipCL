using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// zh_CN.json / en.json 里真实存在的一个键。用 \u 转义写死,避免手抄 CJK 标点出错:
/// 附(U+9644) 件(U+4EF6) ：(U+FF1A,**全角冒号**)。
/// 实测该键的码位是 000A 000A 9644 4EF6 FF1A 000A —— 用半角冒号会差一个字符。
/// </summary>
internal static class LocaleFixture
{
    public const string AttachmentKey = "\n\n\u9644\u4EF6\uFF1A\n";
}

/// <summary>
/// CoreResources(A3 起参与编译)+ I18nCore 读取内嵌资源的用例。
///
/// 期望值来自源码:
///   CoreResources.ListDir 48-56 —— 规则是「前缀匹配 + 名字里只能有一个点」,
///   所以 plugin-host 目录下 12 个文件里 tipcl-plugin.d.ts 有**两个**点,会被过滤掉,只剩 11 个;
///   CoreResources.ReadText 38-46 —— 读不到返回 null,读到就用 Utils.DecodeUtf8Strict 解码;
///   I18nCore.LoadStrings 33-68 —— 用 CoreResources.ListDir("locales") 加载全部语言。
/// </summary>
public class CoreResourcesTests
{
    private static readonly string[] PluginHostExpected = new[]
    {
        "host-console.mjs",
        "host-loader.mjs",
        "host-plugin.mjs",
        "host-rpc.mjs",
        "host-sessions.mjs",
        "host-tools.mjs",
        "host-util.mjs",
        "host-web.mjs",
        "host.mjs",
        "sdk-events-worker.js",
        "sdk.js"
    };

    [Fact]
    public void Resources_live_in_the_core_assembly()
    {
        Assert.Equal(typeof(CoreResources).Assembly, typeof(PyJson).Assembly);
        Assert.Equal("TipCL.Core.", CoreResources.Prefix);
        Assert.Equal("", CoreResources.Suffix);
    }

    [Fact]
    public void ListDir_plugin_host_returns_the_eleven_single_dot_names_in_ordinal_order()
    {
        List<string> list = CoreResources.ListDir("plugin-host");

        Assert.Equal(PluginHostExpected.Length, list.Count);
        Assert.Equal(PluginHostExpected, list);
        // 两个点的名字(ListDir 的第二段过滤条件)不会出现
        Assert.DoesNotContain("tipcl-plugin.d.ts", list);
    }

    [Fact]
    public void ListDir_locales_returns_the_two_locale_files()
    {
        Assert.Equal(new[] { "en.json", "zh_CN.json" }, CoreResources.ListDir("locales"));
    }

    [Fact]
    public void ListDir_of_an_unknown_prefix_is_empty()
    {
        Assert.Empty(CoreResources.ListDir("no-such-dir"));
    }

    [Fact]
    public void ReadText_reads_zh_CN_json_as_utf8_and_parses_to_a_flat_dict()
    {
        string text = CoreResources.ReadText("locales/zh_CN.json");

        Assert.NotNull(text);
        Assert.NotEqual('\uFEFF', text[0]);                       // 内嵌的 json 不带 BOM(序数比较,不走 culture)

        object parsed = PyJson.Loads(text);
        PyDict dict = Assert.IsType<PyDict>(parsed);
        Assert.Equal(2773, dict.Count);                            // 平铺 2773 个键
        Assert.Contains(LocaleFixture.AttachmentKey, dict.Keys);
        Assert.Equal(LocaleFixture.AttachmentKey, dict[LocaleFixture.AttachmentKey]);
    }

    [Fact]
    public void ReadBytes_and_ReadByResourceName_agree_and_missing_returns_null()
    {
        byte[] viaRel = CoreResources.ReadBytes("locales/en.json");
        byte[] viaName = CoreResources.ReadByResourceName("TipCL.Core.locales.en.json");

        Assert.NotNull(viaRel);
        Assert.Equal(viaRel, viaName);
        Assert.Equal(viaRel, CoreResources.ReadBytes("locales\\en.json"));   // '/' 与 '\' 都接受(ResName 16-19)

        Assert.Null(CoreResources.ReadBytes("locales/nope.json"));
        Assert.Null(CoreResources.ReadText("locales/nope.json"));
    }

    [Fact]
    public void ReadText_plugin_host_entry_points_are_readable()
    {
        string sdk = CoreResources.ReadText("plugin-host/sdk.js");
        string manifest = CoreResources.ReadText("plugin-host/tipcl-plugin.d.ts");

        Assert.NotNull(sdk);
        Assert.True(sdk.Length > 0);
        Assert.NotNull(manifest);      // ReadText 按相对路径读,不受 ListDir 过滤影响
        Assert.True(manifest.Length > 0);
    }
}

/// <summary>I18nCore 的语言加载(依赖上面那批内嵌 json)。</summary>
public class I18nCoreTests
{
    /// <summary>键见 <see cref="LocaleFixture"/>。</summary>
    [Fact]
    public void Tr_reads_the_zh_CN_value_and_the_en_value_from_the_embedded_locales()
    {
        string key = LocaleFixture.AttachmentKey;
        Assert.Equal(key, I18nCore.Tr(key, "zh_CN"));                       // zh_CN 是原文(键即值)
        Assert.Equal("\n\nAttachments:\n", I18nCore.Tr(key, "en"));
        Assert.Equal("zh_CN", I18nCore.CurrentLanguage());
    }

    [Fact]
    public void Tr_falls_back_to_the_key_or_the_default()
    {
        Assert.Equal("__no_such_key__", I18nCore.Tr("__no_such_key__"));
        Assert.Equal("fb", I18nCore.Tr("__no_such_key__", "en", "fb"));
        Assert.Equal("__no_such_key__", I18nCore.Tr("__no_such_key__", "fr"));    // 未知语言 → 回落到 en → 再回落到 key
    }

    [Fact]
    public void AvailableLanguages_lists_zh_CN_and_en()
    {
        PyDict langs = I18nCore.AvailableLanguages();

        Assert.Equal("简体中文", langs["zh_CN"]);
        Assert.Equal("English", langs["en"]);
        Assert.True(langs.ContainsKey("zh_CN"));
        Assert.True(langs.ContainsKey("en"));
        Assert.Equal("简体中文", I18nCore.LANGUAGES["zh_CN"]);
    }
}
