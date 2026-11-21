// AI2-i18n-ux(perf-saves 遗留):界面上编辑「实例内存 / GC 预设」用的读写入口。
//
// tune_jvm 写的是实例目录 .instance.json 的 memory_mb / gc_preset(见 JvmTuning),但界面上原来没有地方看和改。
// JvmTuning.ApplyToInstance 只能「两项一起写」且不能清空;界面需要「留空 = 不在实例层设置、交给下一层」,
// 所以这里补一个能分别设置 / 清除两项的入口。校验规则与 JvmTuning 相同(512~32768 MB、Gc.LABELS 里的预设)。
// 启动时的读取顺序不变(LaunchFlow.Prepare):内存 版本 → 实例 → 调用方 → 全局 → 4096;GC 版本 → 实例 → 全局 → auto。
// i18n:ignore-file —— 本文件的中文只在异常信息里(界面拿到异常后自己用词表里的文案提示)。
using System;

namespace TipCL.Core.Launcher;

/// <summary>实例级内存 / GC 的读取与保存(可单独清除某一项)。</summary>
public static class InstanceJvmSettings
{
    /// <summary>实例层自己设置的值:<c>(memoryMb 或 null, gcPreset 或 null)</c>。</summary>
    public static (long? MemoryMb, string? GcPreset) Read(Instance instance)
    {
        long? mb = JvmTuning.InstanceMemory(instance) is long m && m > 0 ? m : null;
        string? gc = JvmTuning.InstanceGc(instance) as string;
        return (mb, gc);
    }

    /// <summary>把界面输入框里的文字解析成内存值:空 = null(不设置);不是正整数抛 ValueError。</summary>
    public static long? ParseMemory(string? text)
    {
        string s = (text ?? "").Trim();
        if (s.Length == 0)
        {
            return null;
        }
        if (s.EndsWith("mb", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(0, s.Length - 2).Trim();
        }
        if (!long.TryParse(s, out long mb))
        {
            throw new ValueError("内存要写整数 MB");
        }
        return mb;
    }

    /// <summary>
    /// 保存实例层的内存与 GC:<paramref name="memoryMb"/> 为 null 时删掉 memory_mb 键,<paramref name="gcPreset"/> 为 null / 空时删掉 gc_preset 键。
    /// 值非法抛 ValueError;实例目录不存在抛 InstanceError(不凭空造实例)。返回保存后的 <see cref="JvmTuning.Effective"/>(不含版本层)。
    /// </summary>
    public static PyDict Save(Instance instance, long? memoryMb, string? gcPreset)
    {
        if (!LocalFs.IsDir(instance.path))
        {
            throw new InstanceError(Py.Fmt("实例 {0} 不存在。", instance.name));
        }
        if (memoryMb is long mb && (mb < JvmTuning.MinMemoryMb || mb > JvmTuning.MaxMemoryMb))
        {
            throw new ValueError(Py.Fmt("内存要在 {0}~{1} MB 之间", JvmTuning.MinMemoryMb, JvmTuning.MaxMemoryMb));
        }
        string gc = Py.Lower(Py.Strip(gcPreset ?? ""));
        if (gc.Length > 0 && !JvmTuning.IsGcPreset(gc))
        {
            throw new ValueError("gc 只能是 auto / g1 / g1_tuned / zgc / none");
        }
        PyDict meta = instance.Meta();
        if (memoryMb is long value)
        {
            meta[JvmTuning.MetaMemory] = value;
        }
        else
        {
            meta.Remove(JvmTuning.MetaMemory);
        }
        if (gc.Length > 0)
        {
            meta[JvmTuning.MetaGc] = gc;
        }
        else
        {
            meta.Remove(JvmTuning.MetaGc);
        }
        Utils.WriteJson(JvmTuning.InstanceMetaPath(instance), meta);
        return JvmTuning.Effective(instance);
    }
}
