using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2a:<see cref="Fmt"/> 的逐条格式化结果。
///
/// 期望值的来源**逐条注明**:
/// - "Win 源" = 从 Windows 版 <c>src\TipCL\Services\Fmt.cs</c> 的分支逐字读出来的(C2a 是字节级复制,
///   逻辑与原版一致);
/// - "本地实跑" = 本机实测修正过的边界(写测试时改正的,不是猜的)。
///
/// 纪律:本类读 <see cref="I18n.Current"/>,所以**必须**在与 I18n 用例同一个 xunit collection 里
/// (见 <see cref="CollectionAttribute"/>,同类不并发),每条用例自己 Load 再在 finally 还原。
/// </summary>
[Collection("C2aGlobals")]
public class C2aFmtTests
{
    /// <summary>切语言并在 Dispose 时还原(含 <see cref="CultureInfo.CurrentUICulture"/>,它是全局静态)。</summary>
    internal sealed class Lang : IDisposable
    {
        private readonly string _lang = I18n.Current;

        private readonly CultureInfo _culture = CultureInfo.CurrentUICulture;

        internal Lang(string lang)
        {
            I18n.Load(lang);
        }

        public void Dispose()
        {
            I18n.Load(_lang);
            CultureInfo.CurrentUICulture = _culture;
        }
    }

    [Theory]
    // Win 源 Downloads:zh_CN 分支 n>=10000 -> 万;n>=100000000 -> 亿;n>0 -> 原样;否则 "—"
    [InlineData(0L, "\u2014")]
    [InlineData(1L, "1")]
    [InlineData(9999L, "9999")]
    [InlineData(10000L, "1 \u4e07")]
    [InlineData(12345L, "1.2 \u4e07")]
    [InlineData(100000000L, "1 \u4ebf")]
    [InlineData(150000000L, "1.5 \u4ebf")]
    public void Downloads_zh_uses_wan_and_yi(long n, string expected)
    {
        using var lang = new Lang("zh_CN");
        Assert.Equal(expected, Fmt.Downloads(n));
    }

    [Theory]
    // Win 源 Downloads:非 zh_CN 分支 n>=1000 -> K/M/B,n>0 -> 原样,否则 "—"
    [InlineData(0L, "\u2014")]
    [InlineData(999L, "999")]
    [InlineData(1000L, "1K")]
    [InlineData(1500L, "1.5K")]
    [InlineData(1000000L, "1M")]
    [InlineData(1000000000L, "1B")]
    [InlineData(2500000000L, "2.5B")]
    public void Downloads_en_uses_k_m_b(long n, string expected)
    {
        using var lang = new Lang("en");
        Assert.Equal(expected, Fmt.Downloads(n));
    }

    [Fact]
    public void Downloads_uses_invariant_number_spelling_regardless_of_culture()
    {
        // Win 源:两条分支都用 CultureInfo.InvariantCulture(所以小数点恒为 ".")。
        using var lang = new Lang("en");
        Assert.Equal("1.5K", Fmt.Downloads(1500L));
        using var zh = new Lang("zh_CN");
        Assert.Equal("1.5 \u4e07", Fmt.Downloads(15000L));
    }

    [Theory]
    // Win 源 Size:<1024 -> "N B";<1MB -> KB;<1GB -> MB;否则 GB("0.#" KB/MB、"0.##" GB)
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048575L, "1024 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(5242880L, "5 MB")]
    [InlineData(1073741823L, "1024 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1610612736L, "1.5 GB")]
    [InlineData(1099511627776L, "1024 GB")]
    public void Size_switches_units_at_the_documented_thresholds(long bytes, string expected)
    {
        Assert.Equal(expected, Fmt.Size(bytes));
    }

    [Fact]
    public void Duration_zero_and_negative_take_the_zero_minutes_branch()
    {
        // Win 源:seconds <= 0 -> I18n.L("0 分钟")
        using var lang = new Lang("zh_CN");
        Assert.Equal(I18n.L("0 \u5206\u949f"), Fmt.Duration(0));
        Assert.Equal(I18n.L("0 \u5206\u949f"), Fmt.Duration(-5));
    }

    [Fact]
    public void Duration_under_one_hour_rounds_up_to_at_least_one_minute()
    {
        // Win 源:hours==0 -> I18n.L("{0} 分钟", Math.Max(1, minutes))
        using var lang = new Lang("zh_CN");
        Assert.Equal(I18n.L("{0} \u5206\u949f", 1), Fmt.Duration(30));
        Assert.Equal(I18n.L("{0} \u5206\u949f", 2), Fmt.Duration(150));
        Assert.Equal(I18n.L("{0} \u5206\u949f", 59), Fmt.Duration(3599));
    }

    [Fact]
    public void Duration_from_one_hour_uses_hours_and_minutes()
    {
        // Win 源:hours>0 且 hours<24 -> I18n.L("{0} 小时 {1} 分钟", hours, minutes)
        using var lang = new Lang("zh_CN");
        Assert.Equal(I18n.L("{0} \u5c0f\u65f6 {1} \u5206\u949f", 1, 0), Fmt.Duration(3600));
        Assert.Equal(I18n.L("{0} \u5c0f\u65f6 {1} \u5206\u949f", 2, 30), Fmt.Duration(9000));
        Assert.Equal(I18n.L("{0} \u5c0f\u65f6 {1} \u5206\u949f", 23, 59), Fmt.Duration(86399));
    }

    [Fact]
    public void Duration_from_one_day_uses_days_and_hours()
    {
        // Win 源:hours>=24 -> I18n.L("{0} 天 {1} 小时", hours/24, hours%24)
        using var lang = new Lang("zh_CN");
        Assert.Equal(I18n.L("{0} \u5929 {1} \u5c0f\u65f6", 1, 0), Fmt.Duration(86400));
        Assert.Equal(I18n.L("{0} \u5929 {1} \u5c0f\u65f6", 1, 1), Fmt.Duration(90061));
        Assert.Equal(I18n.L("{0} \u5929 {1} \u5c0f\u65f6", 2, 3), Fmt.Duration(183600));
    }

    [Fact]
    public void SplitMsg_splits_on_the_two_space_pipe_two_space_separator()
    {
        // Win 源:IndexOf("  |  ", Ordinal) —— 分隔符是两空格加竖线加两空格,速度取分隔符之后的 Trim。
        Fmt.SplitMsg("downloading  |  1.2 MB/s", out string status, out string speed);
        Assert.Equal("downloading", status);
        Assert.Equal("1.2 MB/s", speed);
    }

    [Fact]
    public void SplitMsg_trims_middle_dots_and_spaces_off_the_status()
    {
        // Win 源:status = status.Substring(0, i).Trim(' ', '·')
        Fmt.SplitMsg("\u00b7 preparing \u00b7  |  3 KB/s", out string status, out string speed);
        Assert.Equal("preparing", status);
        Assert.Equal("3 KB/s", speed);
    }

    [Fact]
    public void SplitMsg_without_separator_keeps_the_whole_message_as_status()
    {
        Fmt.SplitMsg("no speed here", out string status, out string speed);
        Assert.Equal("no speed here", status);
        Assert.Equal("", speed);
    }

    [Fact]
    public void SplitMsg_handles_null_and_empty()
    {
        // Win 源:status = msg ?? "";空串走不进分隔符分支。
        Fmt.SplitMsg(null, out string status, out string speed);
        Assert.Equal("", status);
        Assert.Equal("", speed);

        Fmt.SplitMsg("", out string status2, out string speed2);
        Assert.Equal("", status2);
        Assert.Equal("", speed2);
    }

    [Fact]
    public void SplitMsg_with_only_the_separator_leaves_an_empty_speed()
    {
        Fmt.SplitMsg("done  |  ", out string status, out string speed);
        Assert.Equal("done", status);
        Assert.Equal("", speed);
    }
}

/// <summary>
/// C2a:<see cref="I18n"/> 的查表、兜底、语言切换与"词表来自 Core 内嵌资源"这条接线。
///
/// 与 Windows 版的一处差异见 PROGRESS.md 的 C2a 差异表:词表**不再读磁盘**,改读
/// <c>TipCL.Core.Resources.locales.*.json</c>(A2 按与 Windows csproj 相同的 LogicalName 嵌入)。
/// 因此这里用 <c>CoreResources</c> 与 Core 自己的 <see cref="I18nCore"/> 做**交叉对拍**:
/// 两个出口查同一份资源,结果必须一致。
/// </summary>
[Collection("C2aGlobals")]
public class C2aI18nTests
{
    /// <summary>切语言并在 Dispose 时还原(含全局的 <see cref="CultureInfo.CurrentUICulture"/>)。</summary>
    private sealed class Lang : IDisposable
    {
        private readonly string _lang = I18n.Current;

        private readonly CultureInfo _culture = CultureInfo.CurrentUICulture;

        internal Lang(string lang)
        {
            I18n.Load(lang);
        }

        public void Dispose()
        {
            I18n.Load(_lang);
            CultureInfo.CurrentUICulture = _culture;
        }
    }

    /// <summary>从 zh_CN 词表里找一个"两边都有、且译文不同"的键(找不到就让用例失败,不静默跳过)。</summary>
    private static string FindBilingualKey()
    {
        foreach (string key in I18n.KnownKeys)
        {
            if (I18n.Has("en", key) && I18n.Has("zh_CN", key))
            {
                string zh;
                string en;
                using (new Lang("zh_CN"))
                {
                    zh = I18n.L(key);
                }
                using (new Lang("en"))
                {
                    en = I18n.L(key);
                }

                if (!string.Equals(zh, en, StringComparison.Ordinal) && zh.Length > 0 && en.Length > 0)
                {
                    return key;
                }
            }
        }

        throw new InvalidOperationException("词表里找不到 en/zh_CN 都有且译文不同的键");
    }

    [Fact]
    public void Locale_tables_come_from_the_core_embedded_resource()
    {
        // 期望值来源:本包实现的约定(见 C2a 差异表)——I18n.LocalesDir 现在报资源位置,
        // 而资源的实际存在由 CoreResources 独立证明。
        I18n.Load("zh_CN");
        Assert.StartsWith(CoreResources.Prefix, I18n.LocalesDir, StringComparison.Ordinal);
        Assert.Contains("Resources", I18n.LocalesDir.Split('.'));
        Assert.Contains("zh_CN.json", CoreResources.ListDir("locales"));
        Assert.Contains("en.json", CoreResources.ListDir("locales"));
        Assert.False(string.IsNullOrEmpty(CoreResources.ReadText("locales/zh_CN.json")));
        Assert.False(string.IsNullOrEmpty(CoreResources.ReadText("locales/en.json")));
    }

    [Fact]
    public void Known_keys_and_languages_come_from_the_two_shipped_locales()
    {
        I18n.Load("zh_CN");
        // 词表有 zh_CN/en 两份(EnsureTables 里显式建的两个),Languages 按序数排序。
        Assert.Equal(new[] { "en", "zh_CN" }, I18n.Languages.ToArray());
        Assert.True(I18n.KnownKeys.Count > 100, "zh_CN 词表键数应该远大于 100,实际 " + I18n.KnownKeys.Count);
    }

    [Fact]
    public void L_returns_the_table_value_for_a_key_present_in_both_locales()
    {
        string key = FindBilingualKey();

        using (new Lang("zh_CN"))
        {
            string value = I18n.L(key);
            Assert.False(string.IsNullOrEmpty(value));
            Assert.Equal(I18n.Peek(key), value);
        }

        using (new Lang("en"))
        {
            string value = I18n.L(key);
            Assert.False(string.IsNullOrEmpty(value));
            Assert.Equal(I18n.Peek(key), value);
        }
    }

    [Fact]
    public void Switching_language_changes_the_translation_of_a_bilingual_key()
    {
        string key = FindBilingualKey();
        string zh;
        string en;
        using (new Lang("zh_CN"))
        {
            zh = I18n.L(key);
            Assert.Equal("zh_CN", I18n.Current);
        }

        using (new Lang("en"))
        {
            en = I18n.L(key);
            Assert.Equal("en", I18n.Current);
        }

        Assert.NotEqual(zh, en);
    }

    [Fact]
    public void Missing_key_falls_back_to_the_key_itself()
    {
        // Win 源:L(zh) 查不到就返回 zh 本身(不打日志、不抛)。
        using var lang = new Lang("zh_CN");
        const string missing = "\u4e00\u4e2a\u7edd\u5bf9\u4e0d\u5b58\u5728\u7684\u952e 12345";
        Assert.Equal(missing, I18n.L(missing));
        Assert.Null(I18n.Peek(missing));
    }

    [Fact]
    public void Missing_keys_are_recorded_only_when_cjk_and_not_zh_cn()
    {
        // Win 源:只有 Current != "zh_CN" 且字符串含 CJK 才进 Missing(zh_CN 是原文语言,不进)。
        using var lang = new Lang("en");
        const string missing = "\u4e00\u4e2a\u7edd\u5bf9\u4e0d\u5b58\u5728\u7684\u952e";
        I18n.Load("en");
        Assert.Equal(missing, I18n.L(missing));
        Assert.Contains(missing, I18n.Missing);
    }

    [Fact]
    public void Missing_keys_ignore_ascii_only_strings()
    {
        using var lang = new Lang("en");
        I18n.Load("en");
        const string ascii = "no such key ever 98765";
        Assert.Equal(ascii, I18n.L(ascii));
        Assert.DoesNotContain(ascii, I18n.Missing);
    }

    [Fact]
    public void Load_normalises_dashes_and_falls_back_to_zh_cn_when_unknown()
    {
        // Win 源:lang.Replace('-', '_');不在 tables 里就回落 zh_CN。所以 "en-US" 会变成 "en_US"
        // 而这个键不存在 -> 回落 zh_CN(这一条是实测确认过的行为,不是猜的)。
        using var lang = new Lang("en");
        I18n.Load("en-US");
        Assert.Equal("zh_CN", I18n.Current);

        I18n.Load("en");
        Assert.Equal("en", I18n.Current);

        I18n.Load("zz");
        Assert.Equal("zh_CN", I18n.Current);

        I18n.Load(null);
        Assert.Equal("zh_CN", I18n.Current);

        I18n.Load("  ");
        Assert.Equal("zh_CN", I18n.Current);
    }

    [Fact]
    public void Load_sets_the_ui_culture_for_the_active_language()
    {
        // Win 源:zh_CN -> zh-CN,其余 -> en-US。
        using var lang = new Lang("zh_CN");
        I18n.Load("zh_CN");
        Assert.Equal("zh-CN", CultureInfo.CurrentUICulture.Name);

        I18n.Load("en");
        Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void Load_clears_the_missing_set()
    {
        using var lang = new Lang("en");
        I18n.Load("en");
        I18n.L("\u4e00\u4e2a\u7edd\u5bf9\u4e0d\u5b58\u5728\u7684\u952e");
        Assert.NotEmpty(I18n.Missing);
        I18n.Load("en");
        Assert.Empty(I18n.Missing);
    }

    [Fact]
    public void L_with_args_formats_through_the_translation()
    {
        // Win 源:L(zh, args) = string.Format(CurrentCulture, L(zh), args)。
        using var lang = new Lang("zh_CN");
        const string template = "{0} \u5206\u949f";
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, I18n.L(template), 7), I18n.L(template, 7));
        Assert.Contains("7", I18n.L(template, 7), StringComparison.Ordinal);
    }

    [Fact]
    public void L_with_args_on_a_template_without_placeholders_returns_the_text()
    {
        // Win 源:第一层 string.Format 抛 FormatException -> 拿 zh 原文再试 -> 再抛就返回翻译文本。
        using var lang = new Lang("zh_CN");
        const string noPlaceholder = "\u6ca1\u6709\u5360\u4f4d\u7b26";
        Assert.Equal(I18n.L(noPlaceholder), I18n.L(noPlaceholder, 1, 2));
    }

    [Fact]
    public void L_returns_null_or_empty_unchanged()
    {
        using var lang = new Lang("zh_CN");
        Assert.Null(I18n.L(null));
        Assert.Equal("", I18n.L(""));
    }

    [Fact]
    public void Has_reports_only_non_empty_entries()
    {
        using var lang = new Lang("zh_CN");
        Assert.True(I18n.Has("zh_CN", I18n.KnownKeys.First()));
        Assert.False(I18n.Has("zh_CN", "\u4e0d\u5b58\u5728\u7684\u952e"));
        Assert.False(I18n.Has("fr", I18n.KnownKeys.First()));
    }

    [Fact]
    public void App_table_and_core_table_resolve_the_same_key_identically()
    {
        // 交叉对拍:App 的 I18n 与 Core 的 I18nCore 查的是同一份内嵌资源(A2 落地的那份),
        // 所以同一个键在两条链路上必须给出同一个译文。
        string key = FindBilingualKey();
        using var lang = new Lang("zh_CN");
        string previousCoreLang = I18nCore.CurrentLanguage();
        try
        {
            string appValue = I18n.L(key);
            I18nCore.CurrentLang = "zh_CN";
            Assert.Equal(I18nCore.Tr(key), appValue);

            I18n.Load("en");
            string appEn = I18n.L(key);
            I18nCore.CurrentLang = "en";
            Assert.Equal(I18nCore.Tr(key), appEn);
        }
        finally
        {
            I18nCore.CurrentLang = previousCoreLang;
        }
    }

    [Fact]
    public void Init_with_lang_argument_switches_the_language()
    {
        using var lang = new Lang("zh_CN");
        I18n.Init(new[] { "--lang", "en" });
        Assert.Equal("en", I18n.Current);
    }

    [Fact]
    public void Init_without_arguments_keeps_zh_cn_in_the_isolated_home()
    {
        // 隔离目录(TestHome.Root)里没有 config.json,TIPCL_LANG 也没设 -> 默认 zh_CN。
        using var lang = new Lang("en");
        string? saved = Environment.GetEnvironmentVariable("TIPCL_LANG");
        try
        {
            Environment.SetEnvironmentVariable("TIPCL_LANG", null);
            I18n.Init(Array.Empty<string>());
            Assert.Equal("zh_CN", I18n.Current);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIPCL_LANG", saved);
        }
    }
}
