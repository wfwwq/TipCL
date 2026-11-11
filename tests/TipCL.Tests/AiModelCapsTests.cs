using System;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A4:`Ai\ModelCaps.cs`(A4 起参与编译)的用例 —— 即「读嵌入的 ai/model_rules.json 并按模型名匹配能力」那部分。
/// 全部不联网:ModelCaps 只读嵌入资源 CoreResources.ReadText("ai/model_rules.json")(源码 62 行),
/// 联网的能力探测在 VisionCaps 里,本文件不碰。
///
/// 期望值来自读过的源码 ModelCaps.cs:
///   _load 47-88 按 modelRules → modelApiRules → providerSiteRules 三层顺序叠加;
///   _compile_rule 90-114 把 modelMatch 编译成 ^(?:pat)\z(IgnoreCase,35-45 行);
///   _slim_props 116-166 只留 contextWindow / inputFormat / outputFormat / supportsToolCall 四个键
///     (inputFormat/outputFormat 只留 supportsText|Image|Video|Audio|Pdf),整条规则因此可能被丢弃(161-164);
///   Resolve 258-278 从 _Fallback(30-33:窗口 200000、只有文本、工具调用 true)开始,
///     按规则顺序叠加(后面的覆盖前面的),最后再叠 settings 的 ai_model_caps_overrides(223-251,键也是全匹配正则);
///   NormalizeBaseUrl 168-193;ApiTypeForMode 280-283;ResetCache 285-291。
///
/// 规则里每一层的具体匹配串与属性值,是直接读 Resources/ai/model_rules.json 得到的
/// (84 条 modelRules / 1 条 modelApiRules / 52 条 providerSiteRules)。
/// </summary>
public class AiModelCapsTests
{
    private static long Cw(PyDict caps) => (long)caps.Get("contextWindow");

    private static bool Flag(PyDict caps, string section, string key)
    {
        return (bool)((PyDict)caps.Get(section)).Get(key);
    }

    // ---------- 兜底与规则叠加 ----------

    [Fact]
    public void An_unknown_model_gets_the_fallback_values_overlaid_by_the_catch_all_rule()
    {
        PyDict caps = ModelCaps.Resolve("zzz-no-such-model-anywhere");

        // _Fallback 的窗口是 200000,第一条规则 ".*" 也把它设成 200000
        Assert.Equal(200000L, Cw(caps));
        Assert.False(Flag(caps, "inputFormat", "supportsImage"));
        Assert.True(Flag(caps, "inputFormat", "supportsText"));
        Assert.True(Flag(caps, "outputFormat", "supportsText"));
        Assert.True((bool)caps.Get("supportsToolCall"));
    }

    [Fact]
    public void Later_rules_win_over_earlier_ones()
    {
        // "glm-5.3" 命中 ".*" 与 ".*glm-5\.3(?:-flash)?(?:[.\-:/\[].*)?":后者把窗口抬到 1000000
        Assert.Equal(1000000L, Cw(ModelCaps.Resolve("glm-5.3")));
        Assert.False(Flag(ModelCaps.Resolve("glm-5.3"), "inputFormat", "supportsImage"));
    }

    [Fact]
    public void The_most_specific_rule_of_a_family_is_applied_last()
    {
        // "glm-5.3-flash" 依次命中 ".*" → glm-5 → glm-5.3 → glm-5.3-flash,最后把看图打开
        PyDict caps = ModelCaps.Resolve("glm-5.3-flash");

        Assert.Equal(1000000L, Cw(caps));
        Assert.True(Flag(caps, "inputFormat", "supportsImage"));
    }

    [Fact]
    public void A_context_window_smaller_than_the_fallback_is_applied()
    {
        Assert.Equal(131072L, Cw(ModelCaps.Resolve("glm-4.5")));
        Assert.Equal(8192L, Cw(ModelCaps.Resolve("moonshot-v1-8k")));
        Assert.Equal(262144L, Cw(ModelCaps.Resolve("kimi-k2.6")));
    }

    [Fact]
    public void A_vision_variant_keeps_the_window_of_its_base_rule_and_turns_images_on()
    {
        // "moonshot-v1-8k-vision-preview":先命中 ".*moonshot-v1-8k(?:...)?"(窗口 8192、不看图),
        // 再命中 ".*moonshot-v1-8k-vision-preview(?:...)?"(只看图)→ 8192 + 能看图
        PyDict caps = ModelCaps.Resolve("moonshot-v1-8k-vision-preview");

        Assert.Equal(8192L, Cw(caps));
        Assert.True(Flag(caps, "inputFormat", "supportsImage"));
    }

    [Fact]
    public void Model_ids_are_matched_by_a_full_regex_not_by_a_prefix()
    {
        // Full() 包的是 ^(?:pat)\z:"glm-4.5zzz" 不再命中 glm-4.5 那条(尾巴必须是以 . - : / [ 开头),
        // 只剩 ".*" 生效
        Assert.Equal(200000L, Cw(ModelCaps.Resolve("glm-4.5zzz")));
        // 规则里允许的分隔符尾巴("[1m]")能命中 ".*\[1m\]" 那条
        Assert.Equal(1000000L, Cw(ModelCaps.Resolve("unknown-model[1m]")));
        // 顺序仍然有效:更靠后的 ".*kimi-k3(?:[.\-:/\[].*)?" 覆盖了 "[1m]" 那条
        Assert.Equal(1048576L, Cw(ModelCaps.Resolve("kimi-k3[1m]")));
    }

    [Fact]
    public void Input_format_keeps_only_the_five_known_keys()
    {
        PyDict caps = ModelCaps.Resolve("glm-5.3-flash");
        PyDict input = (PyDict)caps.Get("inputFormat");

        Assert.Equal(
            new[] { "supportsAudio", "supportsImage", "supportsPdf", "supportsText", "supportsVideo" },
            input.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_model_api_rule_layer_contributes_nothing_after_slimming()
    {
        // modelApiRules 只有一条,它设的 supportsMidConversationSystem 不在 _PROP_KEYS 里
        // → _slim_props 返回 null → 整条规则被丢掉。所以 apiType 传什么都一样。
        PyDict withAnthropic = ModelCaps.Resolve("claude-opus-5", "anthropic-messages");
        PyDict withOpenAi = ModelCaps.Resolve("claude-opus-5", "openai-chat-completions");

        Assert.Equal(1000000L, Cw(withAnthropic));
        Assert.True(Flag(withAnthropic, "inputFormat", "supportsImage"));
        Assert.Equal(Cw(withAnthropic), Cw(withOpenAi));
        Assert.Equal(Flag(withAnthropic, "inputFormat", "supportsImage"), Flag(withOpenAi, "inputFormat", "supportsImage"));
    }

    [Fact]
    public void Api_type_for_mode_is_always_openai_chat_completions()
    {
        Assert.Equal("openai-chat-completions", ModelCaps.ApiTypeForMode(null));
        Assert.Equal("openai-chat-completions", ModelCaps.ApiTypeForMode("whatever"));
    }

    [Fact]
    public void Resolve_still_works_after_ResetCache()
    {
        ModelCaps.ResetCache();

        Assert.Equal(1000000L, Cw(ModelCaps.Resolve("glm-5.3-flash")));
    }

    // ---------- 用户的 overrides ----------

    [Fact]
    public void Overrides_from_settings_are_applied_after_the_rules()
    {
        PyDict settings = new PyDict();
        settings["ai_model_caps_overrides"] = PyDict.Of(".*", PyDict.Of("contextWindow", 4242L));

        Assert.Equal(4242L, Cw(ModelCaps.Resolve("zzz-no-such-model-anywhere", null, null, settings)));
        // 覆盖也压过规则里算出来的值
        Assert.Equal(4242L, Cw(ModelCaps.Resolve("glm-5.3-flash", null, null, settings)));
    }

    [Fact]
    public void Override_keys_are_full_match_regexes_too()
    {
        PyDict settings = new PyDict();
        settings["ai_model_caps_overrides"] = PyDict.Of("glm-5\\.3", PyDict.Of("contextWindow", 4242L));

        // ^(?:glm-5\.3)\z 匹配不到 "glm-5.3-flash" → 覆盖不生效,保留规则算出的 1000000
        Assert.Equal(1000000L, Cw(ModelCaps.Resolve("glm-5.3-flash", null, null, settings)));
        // 完全同名的模型才生效
        Assert.Equal(4242L, Cw(ModelCaps.Resolve("glm-5.3", null, null, settings)));
    }

    [Fact]
    public void A_broken_regex_override_key_is_ignored_without_killing_the_good_ones()
    {
        PyDict settings = new PyDict();
        PyDict overrides = new PyDict();
        overrides["("] = PyDict.Of("contextWindow", 1L);                 // 编不出来的正则 → Full() 返回 null → 跳过
        overrides["kimi-k2\\.6"] = PyDict.Of("supportsToolCall", false);
        settings["ai_model_caps_overrides"] = overrides;

        PyDict caps = ModelCaps.Resolve("kimi-k2.6", null, null, settings);

        Assert.False((bool)caps.Get("supportsToolCall"));
        Assert.Equal(262144L, Cw(caps));
    }

    [Fact]
    public void Overrides_are_ignored_when_the_settings_key_is_not_a_dict()
    {
        PyDict settings = new PyDict();
        settings["ai_model_caps_overrides"] = "not a dict";

        Assert.Equal(131072L, Cw(ModelCaps.Resolve("glm-4.5", null, null, settings)));
    }

    // ---------- NormalizeBaseUrl ----------

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("https://a.b/v1/", "https://a.b/v1")]
    [InlineData("https://a.b/v1", "https://a.b/v1")]
    [InlineData("https://a.b/", "https://a.b")]
    [InlineData("https://a.b", "https://a.b")]
    [InlineData("https://a.b/v1/?x=1", "https://a.b/v1?x=1")]
    [InlineData("https://a.b/v1#frag", "https://a.b/v1#frag")]
    [InlineData("a.b/v1/", "a.b/v1")]
    public void NormalizeBaseUrl_strips_trailing_slashes_and_keeps_query_and_fragment(object? input, string? expected)
    {
        Assert.Equal(expected, ModelCaps.NormalizeBaseUrl(input));
    }
}
