// task-30:linux 移植侧「无词表」缺陷的两道防线。
//
// 缺陷:`I18nCore.LoadStrings()`(`src/TipCL.Core/Launcher/I18nCore.cs:34-69`)从**内嵌资源**
// `TipCL.Core.locales.<name>.json` 读词表(`CoreResources.ListDir("locales")`)。移植侧的
// `TipCL.Core.csproj` 里**没有任何 EmbeddedResource**,于是:
//   ListDir("locales") → 空 ⇒ LoadStrings 只塞进两个**空**字典(它还"贴心地"给 en/zh_CN 补了空表)
//   ⇒ `SetLanguage("en")` 会成功(因为 `_strings` 里有 en 这个键)⇒ `Tr(key)` 查不到 ⇒ **回落返回 key 本身**
//   ⇒ 用户把语言切成 English,整站仍显示中文(而那个「语言已切换,重启后生效」的 Toast 会让人以为生效了)。
//
// 本文件干两件事:①钉住"词表真的被嵌进来了、而且是真表"(结构 + 内容),②钉住"en 查英文、zh 查中文"。
// 这两条合起来才够:只钉"资源名存在"挡不住"塞了个空 JSON",只钉"某个 key 有英文"挡不住"表只抄了一半"。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

public class I18nCoreLocalesTests
{
    /// <summary>从词表里挑出来的已知条目(两份表里都有、且 en 与 zh 确实不同)。</summary>
    private static readonly (string Key, string En)[] Known =
    {
        ("任务完成", "Task complete"),
        ("设置", "Settings"),
        ("检查更新", "Check for Updates"),
        ("任务失败", "Task failed"),
        ("游戏已退出", "Game exited"),
    };

    /// <summary>底表键数(2026-11-14 时点)。词表更新时这个下界与上面那份数据文件要一起改。</summary>
    private const int BaseTableKeys = 2951;

    /// <summary>
    /// 数据文件里那 16 个「只有用户压缩包才有」的键(见文件里的 `_note` / `source`)。
    /// **故意从文件读、不写死在代码里**:这些键里混着全角 `;` / `:` / `,`(U+FF1B / U+FF1A / U+FF0C),
    /// 手打很容易落成 ASCII 而**渲染出来一模一样** —— 本用例第一版 16 个里就这么错了 5 个。
    /// 文件是由 `merge-locales.py` 的产物原样生成的,不经人手输入。
    /// </summary>
    private static string[] ArchiveOnlyKeys()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "tests", "TipCL.Tests", "Core", "i18n-archive-only-keys.json");
            if (!File.Exists(p))
            {
                continue;
            }
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(p));
            return doc.RootElement.GetProperty("keys").EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToArray();
        }
        throw new InvalidOperationException(
            "找不到 tests/TipCL.Tests/Core/i18n-archive-only-keys.json(从 " + AppContext.BaseDirectory + " 往上找)");
    }

    [Fact]
    public void Locales_AreEmbedded_UnderTheNamesCoreResourcesLooksFor()
    {
        // CoreResources.ListDir 是按资源名前缀 + "剩余部分恰好一个点"筛的,所以 LogicalName 必须是
        // TipCL.Core.locales.<name>.json —— 这里连"名字对不对"一起钉住。
        List<string> names = CoreResources.ListDir("locales");

        Assert.True(
            names.Contains("en.json") && names.Contains("zh_CN.json"),
            $"内嵌资源里只有 [{string.Join(", ", names)}] —— 词表没接进 TipCL.Core;"
            + "需要 csproj 里 EmbeddedResource + LogicalName=TipCL.Core.locales.<name>.json");

        byte[]? en = CoreResources.ReadBytes("locales/en.json");
        byte[]? zh = CoreResources.ReadBytes("locales/zh_CN.json");
        Assert.NotNull(en);
        Assert.NotNull(zh);
        // 体积也看一眼:占位/截断的空表挡在这里(真表 en≈183 KB、zh≈177 KB)
        Assert.True(en!.Length > 100_000, $"en.json 只有 {en.Length} 字节,不像一份完整词表");
        Assert.True(zh!.Length > 100_000, $"zh_CN.json 只有 {zh.Length} 字节,不像一份完整词表");
    }

    [Fact]
    public void EmbeddedTables_AreRealAndKeyAligned()
    {
        PyDict? zh = PyJson.Loads(CoreResources.ReadText("locales/zh_CN.json")) as PyDict;
        PyDict? en = PyJson.Loads(CoreResources.ReadText("locales/en.json")) as PyDict;

        Assert.True(
            zh != null && en != null,
            "两份词表都必须能从内嵌资源里解析出来;空表就是 Tr() 回落返回中文 key 的根因");

        Assert.True(zh!.Count >= BaseTableKeys,
            $"zh_CN 词条只有 {zh.Count} 条(底表是 {BaseTableKeys} 条)—— 像是抄了旧表/抄漏了");
        Assert.True(en!.Count >= BaseTableKeys,
            $"en 词条只有 {en.Count} 条(底表是 {BaseTableKeys} 条)—— 像是抄了旧表/抄漏了");

        // 那 16 个"只有压缩包才有"的键必须在:三份词表互不包含,底表不是超集(README 与数据文件里都写了)
        string[] onlyInArchive = ArchiveOnlyKeys();
        Assert.True(onlyInArchive.Length == 16, $"数据文件里应有 16 个键,实际 {onlyInArchive.Length} 个");
        List<string> missing = onlyInArchive.Where(k => !zh.ContainsKey(k) || !en.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0,
            $"有 {missing.Count} 个「压缩包独有」的键丢了(底表不是超集,不能只抄底表):{string.Join(" / ", missing)}");

        // 键集合必须一致:不一致就说明某一边有孤儿键(界面切语言时会露出"这条没翻")
        List<string> zhKeys = zh.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        List<string> enKeys = en.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        List<string> onlyZh = zhKeys.Except(enKeys, StringComparer.Ordinal).ToList();
        List<string> onlyEn = enKeys.Except(zhKeys, StringComparer.Ordinal).ToList();
        Assert.True(
            onlyZh.Count == 0 && onlyEn.Count == 0,
            $"两份词表键集合不一致:只在 zh 的 {onlyZh.Count} 条、只在 en 的 {onlyEn.Count} 条");

        // en 必须真的"翻过"(不然就是把中文抄了两份)
        int differing = zhKeys.Count(k => !Equals(en.Get(k), zh.Get(k)));
        Assert.True(differing > 1500, $"en 与 zh 只有 {differing} 条不同,不像真翻译过的词表");
    }

    [Fact]
    public void English_ReturnsEnglish_AndChineseReturnsChinese()
    {
        // 显式给 lang,不依赖全局 SetLanguage —— 这样这条用例不受别的用例改了语言的影响
        string en = I18nCore.Tr(Known[0].Key, "en");
        Assert.True(
            en == Known[0].En,
            $"lang=en 时 Tr(\"{Known[0].Key}\") 返回了 \"{en}\";如果等于 key 本身,就是词表为空时的回落");

        foreach ((string key, string expect) in Known)
        {
            Assert.Equal(expect, I18nCore.Tr(key, "en"));
            Assert.Equal(key, I18nCore.Tr(key, "zh_CN"));
        }
    }

    [Fact]
    public void SwitchingToEnglish_MakesTheDefaultLookupReturnEnglish()
    {
        string before = I18nCore.CurrentLanguage();
        try
        {
            // 这条走的是 UI 实际那条路:SetLanguage 之后用不带 lang 的 Tr(它读 CurrentLanguage)
            I18nCore.SetLanguage("en");
            Assert.Equal("en", I18nCore.CurrentLanguage());
            Assert.True(
                I18nCore.Tr("设置") == "Settings",
                $"切到 en 之后 Tr(\"设置\") 返回了 \"{I18nCore.Tr("设置")}\" —— 界面切英文后仍显示中文就是这个");

            I18nCore.SetLanguage("zh_CN");
            Assert.Equal("设置", I18nCore.Tr("设置"));
        }
        finally
        {
            I18nCore.SetLanguage(before);
        }
    }
}
