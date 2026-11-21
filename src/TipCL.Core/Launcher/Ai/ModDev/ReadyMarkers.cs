// AI3-moddev:run_test_launch 判定「进入主菜单」的日志特征行表(按加载器 × MC 版本段)。 // i18n:ignore-file(来源说明是给维护者看的注释与工具结果)
//
// 用法:TestLaunch 按实例的加载器与 MC 版本取适用的几条(ReadyMarkers.For),日志里**任一条**以子串出现即判 ready;
// 调用方给了 ready_pattern(正则)时整张表让位给它。表是代码常量,改这里即可,不需要改监视循环。
//
// 每条都写了来源;Inferred = true 的表示「这行一定会打,但它离标题画面真正出现还有多远」或「从哪个小版本开始有」
// 没在真游戏上逐版核对,是按公开日志样例 / 源码推断的。本仓库测试不真启动游戏,所以这张表仍需要真机补核
// (见 docs/MCP接入指南.md §6)。
//
// 为什么不给 Fabric / Quilt / 新版 Forge / NeoForge 单列特征行:这些加载器没有「已进入标题画面」的固定输出,
// 它们自己的固定输出都在加载早期(反例,见测试):
//   · Fabric Loader / Quilt Loader 启动时的 "Loading N mods:"(FabricLoaderImpl / QuiltLoaderImpl 的 dumpModList);
//   · 新版 Forge / NeoForge(FML 2+)的 "ModLauncher running: args …"、早期加载窗口的
//     "Loading ImmediateWindowProvider fmlearlywindow"、"Launching target 'forgeclient' …"。
// 它们进入标题画面前跑的都是原版客户端的声音系统初始化,所以全部沿用原版那几条("*" 行)。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一条「进入主菜单」特征行。</summary>
/// <param name="Loader">适用加载器:"*" = 所有(含原版);否则 fabric / quilt / forge / neoforge / vanilla。</param>
/// <param name="MinMc">适用 MC 版本下限(含),空 = 不限。</param>
/// <param name="MaxMc">适用 MC 版本上限(含),空 = 不限。</param>
/// <param name="Text">日志里要出现的子串(区分大小写)。</param>
/// <param name="Source">来源说明。</param>
/// <param name="Inferred">true = 推断(时机 / 版本边界没在真游戏上逐版核对)。</param>
public sealed record ReadyMarker(string Loader, string MinMc, string MaxMc, string Text, string Source, bool Inferred)
{
    public PyDict ToDict() => PyDict.Of(
        "loader", Loader,
        "mc", (MinMc.Length > 0 ? MinMc : "*") + ".." + (MaxMc.Length > 0 ? MaxMc : "*"),
        "text", Text,
        "inferred", Inferred);
}

/// <summary>特征行表与按加载器 / 版本取用。</summary>
public static class ReadyMarkers
{
    /// <summary>特征行表(顺序 = 优先级:先列的先报为命中的 marker)。</summary>
    public static readonly IReadOnlyList<ReadyMarker> Table = new List<ReadyMarker>
    {
        // ── 原版(所有加载器都会走到) ──
        new("*", "", "", "Sound engine started",
            "原版声音系统启动完成的固定输出:1.7–1.12 是 SoundManager(线程 Sound Library Loader),"
            + "1.13+ 是 SoundEngine.loadLibrary(线程 Client thread / Render thread);公开 latest.log 样例"
            + "(各加载器 issue / mclo.gs 贴的日志)里都出现在标题画面显示前的首轮资源加载末段。"
            + "它比标题画面略早(之后还有图集创建等),在标题画面 init 时才崩的模组会被误判为 ready —— 这一点是推断",
            true),
        new("*", "", "1.12.2", "OpenAL initialized",
            "LWJGL2 时代(≤1.12.2)paulscode SoundSystem 库 LibraryLWJGLOpenAL 经 STDOUT 打出的 \"OpenAL initialized.\""
            + "(日志里形如 [Sound Library Loader/INFO]: [STDOUT]: OpenAL initialized.),紧挨着 Sound engine started",
            false),
        new("*", "1.13", "", "OpenAL initialized on device",
            "1.13+ 的 com.mojang.blaze3d.audio.Library 打开声卡后的输出 \"OpenAL initialized on device …\";"
            + "具体从哪个小版本开始带 on device 没逐版核对",
            true),
        new("*", "", "", "Error starting SoundSystem. Turning off sounds & music",
            "SoundManager / SoundEngine 打不开声卡时的固定输出(没有音频设备的机器、远程桌面);"
            + "此时不会有 Sound engine started,但游戏照样进标题画面(只是没声音)",
            true),

        // ── 旧版 Forge(FML 1.x,1.7.10–1.12.2) ──
        new("forge", "1.7", "1.12.2", "Forge Mod Loader has successfully loaded",
            "FML Loader.initializeMods 末尾的固定输出 \"Forge Mod Loader has successfully loaded N mods\""
            + "(1.7.10–1.12.2,线程 Client thread),之后加载进度条结束、显示标题画面;新版 FML(1.13+)不再打这行",
            true),
    };

    /// <summary>表里所有特征行文字(去重,保持顺序)。</summary>
    public static string[] AllTexts() => Table.Select(m => m.Text).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 适用于某加载器 + MC 版本的特征行。加载器认不出(空)或版本认不出(快照号等)时放宽:
    /// 加载器不限 / 版本段不限,宁可多认几条也不让测试必然超时。
    /// </summary>
    public static List<ReadyMarker> For(string loader, string mc)
    {
        string l = ModJarInfo.NormLoader(loader ?? "");
        int[]? v = ParseMc(mc ?? "");
        return Table.Where(m =>
                (m.Loader == "*" || l.Length == 0 || string.Equals(m.Loader, l, StringComparison.OrdinalIgnoreCase))
                && (v == null || InRange(v, m.MinMc, m.MaxMc)))
            .ToList();
    }

    /// <summary>日志里找第一条命中的特征行;没有返回 null。</summary>
    public static ReadyMarker? Find(string log, IEnumerable<ReadyMarker> markers)
    {
        string text = log ?? "";
        foreach (ReadyMarker m in markers)
        {
            if (text.Contains(m.Text, StringComparison.Ordinal))
            {
                return m;
            }
        }
        return null;
    }

    /// <summary>"1.20.1" → [1,20,1];"1.21-pre1" → [1,21];"26.1" → [26,1];快照 "24w14a" 等认不出 → null。</summary>
    public static int[]? ParseMc(string mc)
    {
        string s = (mc ?? "").Trim();
        int cut = s.IndexOfAny(new[] { '-', ' ', '+', '_' });
        if (cut >= 0)
        {
            s = s.Substring(0, cut);
        }
        if (s.Length == 0)
        {
            return null;
        }
        string[] parts = s.Split('.');
        var nums = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0)
            {
                return null;
            }
        }
        return nums;
    }

    private static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0;
            int y = i < b.Length ? b[i] : 0;
            if (x != y)
            {
                return x < y ? -1 : 1;
            }
        }
        return 0;
    }

    private static bool InRange(int[] v, string min, string max)
    {
        if (min.Length > 0 && ParseMc(min) is { } lo && Compare(v, lo) < 0)
        {
            return false;
        }
        if (max.Length > 0 && ParseMc(max) is { } hi && Compare(v, hi) > 0)
        {
            return false;
        }
        return true;
    }
}
