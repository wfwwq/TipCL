// AI2-stability:启动时「实际生效」的内存 / GC 只算一处。
//
// 以前三处各算各的:LaunchFlow.Prepare(拼命令行,真正生效的那份)、JvmTuning.Effective(只给 AI 看,不含调用方那一层)、
// Preflight.CheckLaunch / AiPreflight.Check(直接拿调用方传的内存去比物理内存)。实例或版本单独设过内存时,
// 预检比的数和真正 -Xmx 的数不是一回事 —— 例如实例 tune_jvm 成 7000 MB、启动页滑块是 2048,预检说「没问题」,
// 游戏却按 7000 起。现在三处都调 Resolve,顺序只写在这里:
//   内存:版本设置 tipcl.json → 实例 .instance.json → 调用方传的(启动页滑块 / launch_game 参数)→ 全局 default_memory_mb → 4096
//   GC  :版本设置 tipcl.json → 实例 .instance.json → 全局 gc_preset → auto
// 每层只认正整数(内存)/ 非空字符串(GC);非法值落到下一层(以前非法字符串会原样传给 McLauncher)。
using System;

namespace TipCL.Core.Launcher;

/// <summary>某实例某版本启动时实际会用的内存与 GC(以及各自来自哪一层)。</summary>
public static class EffectiveJvm
{
    /// <summary>来源:版本设置。</summary>
    public const string SourceVersion = "version";

    /// <summary>来源:实例设置(.instance.json)。</summary>
    public const string SourceInstance = "instance";

    /// <summary>来源:调用方传入(启动页滑块、launch_game / preflight_check 的 memory_mb)。</summary>
    public const string SourceCaller = "caller";

    /// <summary>来源:全局设置。</summary>
    public const string SourceGlobal = "global";

    /// <summary>来源:兜底默认值。</summary>
    public const string SourceDefault = "default";

    /// <summary>
    /// 算生效值。<paramref name="versionSettings"/> 已经读过版本设置的调用方(LaunchFlow)可以直接传进来,免得再读一遍盘;
    /// 不传就按 <paramref name="version"/> 读。返回 <c>{memory_mb, memory_source, gc_preset, gc_label, gc_source}</c>。
    /// </summary>
    public static PyDict Resolve(Instance instance, string? version = null, object? callerMemoryMb = null, PyDict? versionSettings = null)
    {
        PyDict vs = versionSettings ?? LoadVersionSettings(instance, version);

        long memory;
        string memSource;
        if (PositiveLong(vs.Get("memory_mb")) is long v)
        {
            memory = v;
            memSource = SourceVersion;
        }
        else if (PositiveLong(SafeInstanceMemory(instance)) is long i)
        {
            memory = i;
            memSource = SourceInstance;
        }
        else if (PositiveLong(callerMemoryMb) is long c)
        {
            memory = c;
            memSource = SourceCaller;
        }
        else if (PositiveLong(Config.CONFIG.Get("default_memory_mb")) is long g)
        {
            memory = g;
            memSource = SourceGlobal;
        }
        else
        {
            memory = JvmTuning.LauncherDefaultMemoryMb;
            memSource = SourceDefault;
        }

        string gc;
        string gcSource;
        string vGc = Py.Lower(Py.Strip(Py.S(vs.Get("gc"))));
        string iGc = SafeInstanceGc(instance);
        string gGc = Py.Lower(Py.Strip(Py.S(Config.CONFIG.Get("gc_preset"))));
        if (vGc.Length > 0)
        {
            gc = vGc;
            gcSource = SourceVersion;
        }
        else if (iGc.Length > 0)
        {
            gc = iGc;
            gcSource = SourceInstance;
        }
        else if (gGc.Length > 0)
        {
            gc = gGc;
            gcSource = SourceGlobal;
        }
        else
        {
            gc = "auto";
            gcSource = SourceDefault;
        }
        return PyDict.Of("memory_mb", memory, "memory_source", memSource, "gc_preset", gc, "gc_label", JvmTuning.GcLabel(gc), "gc_source", gcSource);
    }

    /// <summary>只要内存(MB)。<paramref name="callerMemoryMb"/> ≤ 0 视为没传。</summary>
    public static long MemoryMb(Instance instance, string? version = null, long callerMemoryMb = 0)
        => Py.Int(Resolve(instance, version, callerMemoryMb > 0 ? callerMemoryMb : null).Get("memory_mb"));

    private static PyDict LoadVersionSettings(Instance instance, string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return new PyDict();
        }
        try
        {
            return VersionSettings.Load(instance, version) ?? new PyDict();
        }
        catch (Exception)
        {
            return new PyDict();
        }
    }

    private static object? SafeInstanceMemory(Instance instance)
    {
        try
        {
            return JvmTuning.InstanceMemory(instance);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SafeInstanceGc(Instance instance)
    {
        try
        {
            return Py.S(JvmTuning.InstanceGc(instance));
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>正整数(可以是数字或数字字符串);其余一律 null(交给下一层)。</summary>
    internal static long? PositiveLong(object? raw)
    {
        if (raw == null || raw is bool)
        {
            return null;
        }
        try
        {
            if (raw is string s && Py.Strip(s).Length == 0)
            {
                return null;
            }
            long n = Py.Int(raw);
            return n > 0 ? n : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
