// R30-WP3c:锁住 R30-WP3b 接上的那根「配置 -> I18nCore.CurrentLang」的线。
//
// 为什么需要这条守卫:这条修复**没有任何可见像素能证明它** —— 8 个冒烟页的可见文案
// 一处都不走词表(97.35% 未接线),唯一随语言变化的设置页「语言」行又落在主窗
// 900x550 的折叠线以下。详见 progress/R30-WP3b.md 的 §3(逐像素对照)与 §8(对账)。
// 没有守卫的话,App.axaml.cs 里那一行 InitLanguage() 被下一个重构顺手删掉,
// 484 条用例会全绿,谁也不会发现 —— 设置页写的 language 会再次没人读回来。
//
// 隔离:全部在同进程内的**独立临时数据根**上做,收尾必还原 Utils.ROOT / Config / CurrentLang
// 并删掉临时目录。不碰 TestIsolation 给整个程序集的那份共享根,避免 R1 那种隔离缺陷。
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class I18nLanguageWiringTests
{
    // ————————————————————————— 夹具 —————————————————————————

    /// <summary>
    /// 造一个独立数据根:里面写一份预置 language 的 config.json,并让进程内 Config 从它重读。
    /// 返回该根的绝对路径,调用方负责在 finally 里 <see cref="Restore"/>。
    /// </summary>
    private static string SeedHome(string languageLiteral)
    {
        string dir = Path.Combine(Path.GetTempPath(), "tipcl-wp3c-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "config.json"),
            "{\"language\":" + languageLiteral + "}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Utils.SetRoot(dir);
        Config.Reset(); // 让 Config.CONFIG 从新根重新走一遍 LauncherConfig.Load()
        return dir;
    }

    private static void Restore(string dir, string prevRoot, string prevLang)
    {
        I18nCore.CurrentLang = prevLang;
        Utils.SetRoot(prevRoot);
        Config.Reset();
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception) { /* 收尾尽力而为,不影响断言 */ }
    }

    /// <summary>从测试输出目录往上找源码树里的文件。找不到返回 null。</summary>
    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ————————————————— 机制:配置真的决定 CurrentLang —————————————————

    [Fact]
    public void InitLanguage_AppliesSavedLanguage_AndNormalisesItOnDisk()
    {
        string prevRoot = Utils.ROOT;
        string prevLang = I18nCore.CurrentLang;
        // 两侧故意带空格:LauncherConfig.Load 会原样保留该值,只有 SetLanguage 的
        // strip + 校验会把它规范化。所以「落盘变干净」本身就是「这一行真的执行了」的证据。
        string dir = SeedHome("\" en \"");
        try
        {
            I18nCore.InitLanguage();

            Assert.Equal("en", I18nCore.CurrentLang);
            Assert.Equal("en", I18nCore.CurrentLanguage());

            using var doc = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(dir, "config.json"), Encoding.UTF8));
            Assert.Equal("en", doc.RootElement.GetProperty("language").GetString());
        }
        finally { Restore(dir, prevRoot, prevLang); }
    }

    [Fact]
    public void InitLanguage_UnknownLanguage_FallsBackToDefaultLanguage()
    {
        string prevRoot = Utils.ROOT;
        string prevLang = I18nCore.CurrentLang;
        string dir = SeedHome("\" fr \"");
        try
        {
            I18nCore.InitLanguage();

            // 词表里没有 fr => 回落 I18nCore._DEFAULT_LANG
            Assert.Equal(I18nCore._DEFAULT_LANG, I18nCore.CurrentLang);

            using var doc = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(dir, "config.json"), Encoding.UTF8));
            Assert.Equal(I18nCore._DEFAULT_LANG,
                doc.RootElement.GetProperty("language").GetString());
        }
        finally { Restore(dir, prevRoot, prevLang); }
    }

    [Fact]
    public void InitLanguage_ThenTr_ResolvesThroughTheWordTable()
    {
        string prevRoot = Utils.ROOT;
        string prevLang = I18nCore.CurrentLang;
        string dir = SeedHome("\" en \"");
        try
        {
            I18nCore.InitLanguage();

            // 「设置」在 en.json 里有真实译文;不带显式 lang 的 Tr 必须跟着 CurrentLang 走。
            // 这一条把「配置 -> CurrentLang -> 取词结果」整条链串起来断言。
            Assert.Equal("Settings", I18nCore.Tr("设置"));

            I18nCore.SetLanguage("zh_CN");
            Assert.Equal("设置", I18nCore.Tr("设置"));
        }
        finally { Restore(dir, prevRoot, prevLang); }
    }

    // ————————————————— 接线:App 启动路径必须真的调它 —————————————————

    /// <summary>
    /// 源码级接线守卫。为什么不做成运行时用例:要真跑到 App.OnFrameworkInitializationCompleted()
    /// 就得在进程内再构造一个 Application 实例,那会动 Application.Current 这种全局静态 ——
    /// 正是 R1 那类隔离缺陷的成因,代价是 480 条用例的稳定性。而这条修复的**唯一**故障模式
    /// 就是「那一行被删掉/被挪进 lifetime 判断里」,源码断言恰好覆盖它,且完全确定性。
    /// </summary>
    [Fact]
    public void AppStartupPath_CallsInitLanguage_OutsideTheLifetimeBranch()
    {
        string? path = FindRepoFile(Path.Combine("src", "TipCL.App", "App.axaml.cs"));
        Assert.True(path != null,
            "找不到 src/TipCL.App/App.axaml.cs(本用例需要源码树位于测试输出目录的上级)");

        string[] lines = File.ReadAllLines(path!, Encoding.UTF8);

        // 只看真正的代码行:被注释掉的 InitLanguage() 不算接线。
        int callAt = Array.FindIndex(lines, l =>
        {
            string t = l.Trim();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal)
                && t.Contains("I18nCore.InitLanguage()", StringComparison.Ordinal);
        });
        int lifetimeAt = Array.FindIndex(lines, l =>
            !l.Trim().StartsWith("//", StringComparison.Ordinal)
            && l.Contains("is IClassicDesktopStyleApplicationLifetime", StringComparison.Ordinal));

        Assert.True(callAt >= 0,
            "App.axaml.cs 里没有 I18nCore.InitLanguage() 的**未注释**调用 —— R30-WP3b 的语言接线"
            + "(提交 e273e5c38e42)被删掉了,设置页写进 config.json 的 language 会再次没人读回来。"
            + "详见 progress/R30-WP3b.md §8(对账)。");

        Assert.True(lifetimeAt >= 0,
            "App.axaml.cs 里找不到 lifetime 判断,本用例需要跟着改");

        Assert.True(callAt < lifetimeAt,
            "--smoke 走 SetupWithoutStarting(),此时没有 classic desktop lifetime;"
            + "InitLanguage() 必须留在 lifetime 判断之外,否则无头截图路径覆盖不到,修复无法自证。");
    }
}
