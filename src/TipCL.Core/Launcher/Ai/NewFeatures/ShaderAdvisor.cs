// AI-new-features:按硬件给光影 / 资源包分档推荐(recommend_shaders 的纯逻辑部分)。
//
// 硬件信息(AI-integration 合并后):统一走 AI-perf-saves 的 Core 静态类 SystemInfo(Launcher/SystemInfo.cs),
// 原先为独立编译临时写的 NfHardwareProbe 已删除。ShaderAdvisor.ProbeHardware / ProbeMemory 只做字段换算
// (SystemInfo 的 total_mb / avail_mb / gpus[name,vram_mb] → 本包用的 ram_mb / ram_avail_mb / gpu / vram_mb / gpus / summary),
// 测试注入点是 ShaderAdvisor.HardwareHook。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher.Ai;

/// <summary>光影 / 资源包按硬件分档(低 / 中 / 高配)。</summary>
public static class ShaderAdvisor
{
    /// <summary>测试注入点:非空时不碰真实机器(ProbeHardware / ProbeMemory 都走它)。</summary>
    internal static Func<PyDict>? HardwareHook;

    /// <summary>
    /// 硬件摘要:ram_mb / ram_avail_mb / gpu(有独显取独显)/ vram_mb / gpus(名字列表)/ summary。
    /// 数据来自 <see cref="SystemInfo.Snapshot"/>(带 120 秒缓存);探测失败时各字段为 0 / 空。
    /// </summary>
    internal static PyDict ProbeHardware()
    {
        Func<PyDict>? hook = HardwareHook;
        if (hook != null)
        {
            return hook();
        }
        PyDict snap;
        try
        {
            snap = SystemInfo.Snapshot();
        }
        catch (Exception)
        {
            snap = new PyDict();
        }
        string gpu = "";
        long vram = 0;
        var names = new List<object>();
        foreach (object? raw in Py.ListOr(snap.Get("gpus")))
        {
            PyDict g = Py.D(raw);
            string name = Py.S(g.Get("name"));
            if (name.Length == 0)
            {
                continue;
            }
            names.Add(name);
            long v = Py.Int(Py.Or(g.Get("vram_mb"), 0L));
            // 有独显时以独显为准(笔记本常见「核显 + 独显」)
            if (gpu.Length == 0 || (IsIntegrated(gpu) && !IsIntegrated(name)) || v > vram)
            {
                gpu = name;
                vram = v;
            }
        }
        return PyDict.Of(
            "ram_mb", Py.Int(Py.Or(snap.Get("total_mb"), 0L)),
            "ram_avail_mb", Py.Int(Py.Or(snap.Get("avail_mb"), 0L)),
            "gpu", gpu, "vram_mb", vram, "gpus", names,
            "summary", Py.S(snap.Get("summary")));
    }

    /// <summary>只探内存(快:不跑显卡 / Java 查询;启动前预检只要这个)。数据来自 <see cref="SystemInfo.Memory"/>。</summary>
    internal static PyDict ProbeMemory()
    {
        Func<PyDict>? hook = HardwareHook;
        if (hook != null)
        {
            return hook();
        }
        try
        {
            PyDict mem = SystemInfo.Memory();
            return PyDict.Of("ram_mb", Py.Int(Py.Or(mem.Get("total_mb"), 0L)), "ram_avail_mb", Py.Int(Py.Or(mem.Get("avail_mb"), 0L)));
        }
        catch (Exception)
        {
            return PyDict.Of("ram_mb", 0L, "ram_avail_mb", 0L);
        }
    }

    public static readonly string[] TIERS = { "low", "mid", "high" };

    public static string TierLabel(string tier) => tier switch
    {
        "low" => "低配",
        "high" => "高配",
        _ => "中配",
    };

    private static int Rank(string tier) => Array.IndexOf(TIERS, tier) is int i && i >= 0 ? i : 1;

    /// <summary>核显判断(Intel UHD/HD/Iris、AMD 集成 Radeon Graphics / Vega、无名显卡)。</summary>
    public static bool IsIntegrated(string gpu)
    {
        string g = Py.Lower(gpu ?? "");
        if (g.Length == 0)
        {
            return true;
        }
        if (g.Contains("nvidia") || g.Contains("geforce") || g.Contains("rtx") || g.Contains("gtx") || g.Contains("quadro"))
        {
            return false;
        }
        if (Regex.IsMatch(g, "radeon\\s*(rx|pro|r9|r7|vii)") || Regex.IsMatch(g, "\\barc\\s*a\\d"))
        {
            return false;
        }
        return g.Contains("intel") || g.Contains("uhd") || g.Contains("iris") || g.Contains("vega")
               || g.Contains("radeon(tm) graphics") || g.Contains("radeon graphics") || g.Contains("llvmpipe")
               || g.Contains("microsoft basic") || g.Contains("basic render") || g.Contains("virtual") || g.Contains("vmware");
    }

    /// <summary>独显里够跑重光影的那一档(RTX 20 起、RX 6000 起、Arc A7xx,或显存 ≥ 6 GB)。</summary>
    private static bool IsStrongGpu(string gpu, long vramMb)
    {
        string g = Py.Lower(gpu ?? "");
        if (vramMb >= 6144)
        {
            return true;
        }
        return Regex.IsMatch(g, "rtx\\s*[2-9]\\d{2,3}") || Regex.IsMatch(g, "rx\\s*[6-9]\\d{3}") || Regex.IsMatch(g, "arc\\s*a7");
    }

    /// <summary>按硬件定档,<paramref name="reason"/> 是给用户看的一句话依据。</summary>
    public static string HardwareTier(PyDict hw, out string reason)
    {
        long ram = Py.Int(Py.Or(hw.Get("ram_mb"), 0L));
        long vram = Py.Int(Py.Or(hw.Get("vram_mb"), 0L));
        string gpu = Py.S(hw.Get("gpu"));
        string ramText = ram > 0 ? Math.Round(ram / 1024.0) + " GB 内存" : "内存未知";
        string gpuText = gpu.Length > 0 ? gpu : "显卡未识别";
        if (IsIntegrated(gpu) || (ram > 0 && ram < 8000) || (vram > 0 && vram < 2048))
        {
            reason = gpuText + " · " + ramText + ":核显 / 小显存 / 内存不足 8 GB,按低配推荐";
            return "low";
        }
        if (IsStrongGpu(gpu, vram) && ram >= 15000)
        {
            reason = gpuText + " · " + ramText + ":独显较强且内存 ≥ 16 GB,可以上高配光影";
            return "high";
        }
        reason = gpuText + " · " + ramText + ":独显 + 常规内存,按中配推荐";
        return "mid";
    }

    private static readonly string[] HeavyShaderKeys =
    {
        "ptgi", "seus", "rethinking voxels", "photon", "bliss", "nostalgia", "kappa", "solas", "path trac", "raytrac", "ray trac", "voxel", "lux",
    };

    private static readonly string[] LightShaderKeys =
    {
        "lite", "fast", "potato", "low end", "lowend", "low-end", "makeup", "enhanced default", "vanilla plus", "super duper vanilla", "toon", "performance",
    };

    /// <summary>单个搜索结果的档位:光影按名称关键词,资源包按分辨率(16x/32x 低、64x 中、128x+ 高)。</summary>
    public static string ItemTier(PyDict row, string kind)
    {
        string text = Py.Lower(Py.S(row.Get("name")) + " " + Py.S(row.Get("slug")) + " " + Py.S(row.Get("description")));
        if (kind == "resourcepack")
        {
            Match m = Regex.Match(text, "(\\d{2,4})\\s*x(?![a-z])");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int res))
            {
                return res <= 32 ? "low" : (res <= 64 ? "mid" : "high");
            }
            return "low"; // 没写分辨率的多半是 16x 原版风格
        }
        if (LightShaderKeys.Any(k => text.Contains(k)))
        {
            return "low";
        }
        if (HeavyShaderKeys.Any(k => text.Contains(k)))
        {
            return "high";
        }
        return "mid";
    }

    /// <summary>实例里装没装光影加载器(Iris / Oculus / OptiFine),以及没装时该装哪个。</summary>
    public static PyDict ShaderLoader(Instance inst)
    {
        bool iris = false;
        bool oculus = false;
        bool optifine = false;
        try
        {
            foreach (object raw in Mods.ListInstanceModEntries(inst))
            {
                PyDict e = Py.D(raw);
                if (!Py.Truthy(e.Get("enabled")))
                {
                    continue;
                }
                string f = Py.Lower(Py.S(e.Get("filename")));
                iris |= f.StartsWith("iris", StringComparison.Ordinal);
                oculus |= f.StartsWith("oculus", StringComparison.Ordinal);
                optifine |= f.Contains("optifine");
            }
        }
        catch (Exception)
        {
        }
        try
        {
            optifine |= inst.InstalledIds().Any(v => Py.Lower(Py.S(v)).Contains("optifine"));
        }
        catch (Exception)
        {
        }
        string loader = "";
        try
        {
            loader = Mods.DetectLoader(inst) ?? "";
        }
        catch (Exception)
        {
        }
        string current = iris ? "iris" : (oculus ? "oculus" : (optifine ? "optifine" : ""));
        string hint = current.Length > 0
            ? "已装 " + (iris ? "Iris" : (oculus ? "Oculus" : "OptiFine")) + ",装好光影包后在游戏的「视频设置 → 光影」里选"
            : loader switch
            {
                "fabric" or "quilt" => "还没有光影加载器:Fabric / Quilt 实例先装 Iris(install_mod iris),再装光影",
                "forge" or "neoforge" => "还没有光影加载器:Forge / NeoForge 实例装 Oculus(或 OptiFine),再装光影",
                _ => "原版实例跑不了光影:需要先装 OptiFine,或装 Fabric + Iris",
            };
        return PyDict.Of("current", current, "iris", iris, "oculus", oculus, "optifine", optifine, "loader", loader, "ready", current.Length > 0, "hint", hint);
    }

    /// <summary>
    /// 把 search_content 的结果按档位分好,并挑出适合当前硬件的推荐(同档优先,其次低一档)。
    /// </summary>
    public static PyDict Recommend(List<object> results, PyDict hw, string kind, PyDict? loader = null, int limit = 6)
    {
        kind = kind == "resourcepack" ? "resourcepack" : "shader";
        string tier = HardwareTier(hw, out string reason);
        var buckets = new Dictionary<string, List<object>> { ["low"] = new(), ["mid"] = new(), ["high"] = new() };
        var scored = new List<(PyDict Row, string Tier)>();
        foreach (object? raw in results)
        {
            if (raw is not PyDict row)
            {
                continue;
            }
            string t = ItemTier(row, kind);
            var item = PyDict.Of(
                "name", Py.S(row.Get("name")),
                "slug", Py.S(Py.Or(row.Get("slug"), row.Get("id"))),
                "source", Py.S(row.Get("source")),
                "downloads", Py.Int(Py.Or(row.Get("downloads"), 0L)),
                "tier", t,
                "tier_label", TierLabel(t),
                "fits", Rank(t) <= Rank(tier));
            buckets[t].Add(item);
            scored.Add((item, t));
        }
        var picks = scored.Where(s => s.Tier == tier).Select(s => (object)s.Row)
            .Concat(scored.Where(s => Rank(s.Tier) < Rank(tier)).OrderByDescending(s => Rank(s.Tier)).Select(s => (object)s.Row))
            .Take(limit).ToList();
        var tooHeavy = scored.Where(s => Rank(s.Tier) > Rank(tier)).Select(s => Py.S(s.Row.Get("name"))).Cast<object>().ToList();
        var result = PyDict.Of(
            "kind", kind,
            "hardware", PyDict.Of(
                "ram_mb", Py.Int(Py.Or(hw.Get("ram_mb"), 0L)),
                "gpu", Py.S(hw.Get("gpu")),
                "vram_mb", Py.Int(Py.Or(hw.Get("vram_mb"), 0L)),
                "tier", tier,
                "tier_label", TierLabel(tier),
                "reason", reason),
            "recommended", picks,
            "by_tier", PyDict.Of("low", buckets["low"], "mid", buckets["mid"], "high", buckets["high"]),
            "too_heavy", tooHeavy,
            "note", kind == "shader"
                ? "档位按名称关键词推断(PTGI/SEUS/Photon 等算高配,Lite/Fast 算低配,其余算中配),装之前请跟用户说明是推断"
                : "资源包按分辨率分档:16x/32x 低配、64x 中配、128x 以上高配");
        if (loader != null)
        {
            result["shader_loader"] = loader;
        }
        return result;
    }
}
