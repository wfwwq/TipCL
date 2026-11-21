// AI2-i18n-ux(perf-saves 遗留):实例详情 → 设置 里的「实例内存 / GC」一行与编辑对话框。
//
// tune_jvm 写的是实例的 .instance.json(memory_mb / gc_preset),以前界面上没有地方看和改。
// 这里给出:一行摘要(实例层当前值 + 有几个版本单独设置过)+「编辑」按钮 → 对话框(内存输入框 + GC 下拉,
// 留空 / 选「未设置」= 实例不单独设置,交给下一层)。保存走 Core 的 InstanceJvmSettings.Save(用户亲手改的,不走 AI 确认卡)。
// 表单的构造与提交拆成两个可单测的静态方法(BuildForm / Apply),对话框本身只是把表单放进 Dlg.Ask。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Library;

/// <summary>实例级内存 / GC 的摘要与编辑入口。</summary>
internal static class InstanceJvmEditor
{
    /// <summary>GC 下拉的取值:第 0 项 = 不在实例层设置(null)。</summary>
    internal static readonly string?[] GcValues = { null, "auto", "g1", "g1_tuned", "zgc", "none" };

    private static string Tr(string key) => I18nCore.Tr(key);

    internal static string Title => Tr("实例内存 / GC");

    /// <summary>读取顺序说明(对话框里的灰字;与 LaunchFlow.Prepare 的顺序一致)。</summary>
    internal static string OrderHint => Tr("留空 = 实例不单独设置。启动时内存按 版本设置 → 实例设置 → 启动页内存 → 全局默认 → 4096 MB 的顺序取第一个设置了的值;GC 按 版本设置 → 实例设置 → 全局设置 → 自动 的顺序取。");

    internal static string[] GcLabels()
        => GcValues.Select(v => v == null ? Tr("未设置(跟随全局)") : Tr(JvmTuning.GcLabel(v))).ToArray();

    /// <summary>设置分段那一行的副标题:「内存:6144 MB · GC:调优 G1」+ 版本覆盖提示。</summary>
    internal static string Summary(string instance)
    {
        try
        {
            var inst = new Instance(instance);
            (long? mb, string? gc) = InstanceJvmSettings.Read(inst);
            string mem = mb is long m ? Tr("{mb} MB").Replace("{mb}", m.ToString()) : Tr("未设置(跟随全局)");
            string gcText = gc != null ? Tr(JvmTuning.GcLabel(gc)) : Tr("未设置(跟随全局)");
            string text = Tr("内存:{mem} · GC:{gc}").Replace("{mem}", mem).Replace("{gc}", gcText);
            int overrides = JvmTuning.VersionOverrides(inst).Count;
            if (overrides > 0)
            {
                text += " · " + Tr("{n} 个版本单独设置过内存或 GC,这些版本以版本设置为准").Replace("{n}", overrides.ToString());
            }
            return text;
        }
        catch (Exception)
        {
            return Tr("读取失败");
        }
    }

    /// <summary>「编辑」按钮;保存成功后回调 <paramref name="saved"/>(页面据此刷新这一行)。</summary>
    internal static Button EditButton(string instance, Action saved)
    {
        var btn = UiClass.Btn(Tr("编辑"), BtnKind.Chip, async (_, _) =>
        {
            try
            {
                if (await EditAsync(instance))
                {
                    saved();
                }
            }
            catch (Exception ex)
            {
                ToastHost.Show(Tr("保存失败"), ex.Message, ToastKind.Warning);
            }
        });
        btn.Name = "InstanceJvmEditButton";
        return btn;
    }

    /// <summary>对话框里的表单控件(测试直接构造它,不弹窗)。</summary>
    internal sealed class Form
    {
        public required TextBox Memory { get; init; }
        public required ComboBox Gc { get; init; }
        public required Control Body { get; init; }
    }

    internal static Form BuildForm(string instance)
    {
        (long? mb, string? gc) = InstanceJvmSettings.Read(new Instance(instance));
        var memory = UiClass.Input(Tr("留空则跟随全局设置"), mb?.ToString() ?? "", 160);
        memory.Name = "InstanceJvmMemory";
        string[] labels = GcLabels();
        int gcIndex = Math.Max(0, Array.IndexOf(GcValues, gc));
        var gcBox = UiClass.Combo(labels, labels[gcIndex], 200);
        gcBox.Name = "InstanceJvmGc";
        var hint = UiClass.Small(OrderHint);
        hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        hint.MaxWidth = 420;
        var body = UiClass.V(4,
            UiClass.Field(Tr("实例内存"), memory, Tr("单位 MB")),
            UiClass.Field(Tr("垃圾回收器"), gcBox),
            hint);
        return new Form { Memory = memory, Gc = gcBox, Body = body };
    }

    /// <summary>提交表单:成功返回 null,失败返回给用户看的提示(已走词表)。</summary>
    internal static string? Apply(string instance, Form form)
    {
        long? mb;
        try
        {
            mb = InstanceJvmSettings.ParseMemory(form.Memory.Text);
        }
        catch (Exception)
        {
            return Tr("内存要写整数 MB(留空 = 不单独设置)");
        }
        if (mb is long v && (v < JvmTuning.MinMemoryMb || v > JvmTuning.MaxMemoryMb))
        {
            return Tr("内存要在 {min}~{max} MB 之间").Replace("{min}", JvmTuning.MinMemoryMb.ToString()).Replace("{max}", JvmTuning.MaxMemoryMb.ToString());
        }
        string? gc = GcValues[Math.Clamp(form.Gc.SelectedIndex, 0, GcValues.Length - 1)];
        try
        {
            InstanceJvmSettings.Save(new Instance(instance), mb, gc);
        }
        catch (Exception)
        {
            return Tr("保存失败:实例目录不存在或不可写");
        }
        return null;
    }

    /// <summary>弹编辑对话框;保存成功返回 true。</summary>
    internal static async Task<bool> EditAsync(string instance)
    {
        Form form = BuildForm(instance);
        if (!await Dlg.Ask(Tr("实例内存 / GC") + " · " + instance, form.Body, Tr("保存")))
        {
            return false;
        }
        string? error = Apply(instance, form);
        if (error != null)
        {
            ToastHost.Show(Tr("保存失败"), error, ToastKind.Warning);
            return false;
        }
        ToastHost.Show(Tr("已保存"), Summary(instance), ToastKind.Success);
        return true;
    }
}
