// 设置页 · 记忆卡 / 手机同步卡 / 关于与反馈卡 / 清理与推荐（M2-B5）。
// 记忆走 Core 的 FileMemory（B0 已搬）；手机同步走 Core 的 PhoneLink（B0 已搬）。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Launcher.Sync;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage
{
    /// <summary>设置 → AI 助手 → 记忆：列出 ai_memory.md 的全部条目，可编辑、删除、清空。</summary>
    private Border BuildMemoryCard()
    {
        var list = UiClass.V(4);
        var count = UiClass.Muted("");
        Button clear = null!;
        void Render()
        {
            list.Children.Clear();
            List<string> entries = FileMemory.Entries();
            count.Text = entries.Count == 0
                ? "还没有记忆。对话里说出偏好或修好问题后，助手会自己记下"
                : $"共 {entries.Count} 条，新对话开始时会带给助手";
            clear.IsEnabled = entries.Count > 0;
            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                string entry = entries[i];
                string body = FileMemory.TextOf(entry);
                string date = entry.Length > body.Length ? entry[..^body.Length].Trim() : "";
                var text = UiClass.Txt(body, 12.5, fg: "B.Ink").Wrap();
                var row = UiClass.G(null, "Auto,*,Auto,Auto");
                row.Add(UiClass.Txt(date, 11.5, fg: "B.InkFaint").M(0, 0, 10, 0).VCenter(), 0);
                row.Add(text.VCenter(), 0, 1);
                row.Add(UiClass.Btn("编辑", BtnKind.Chip, async (_, _) =>
                {
                    string? edited = await Dlg.Prompt("编辑记忆", "内容", body);
                    if (edited != null && Same(index, entry))
                        FileMemory.Update(index, edited);
                    Render();
                }).M(8, 0, 0, 0).VCenter(), 0, 2);
                row.Add(UiClass.Btn("删除", BtnKind.Ghost, (_, _) =>
                {
                    if (Same(index, entry))
                        FileMemory.RemoveAt(index);
                    Render();
                }).M(4, 0, 0, 0).VCenter(), 0, 3);
                list.Children.Add(UiClass.Pane(row, "B.Paper2", 8, 8));
            }
        }
        static bool Same(int index, string entry)
        {
            var now = FileMemory.Entries();
            if (index < now.Count && now[index] == entry) return true;
            ToastHost.Show("记忆已变化", "文件刚被改过，已刷新列表", ToastKind.Warning);
            return false;
        }
        clear = UiClass.Btn("清空全部", BtnKind.Danger, async (_, _) =>
        {
            if (await Dlg.Confirm("清空全部记忆？", "助手记下的偏好、配置和修好过的问题都会删除，无法恢复。", "清空", null, danger: true))
            {
                int n = FileMemory.Clear();
                ToastHost.Show("已清空记忆", $"删除了 {n} 条", ToastKind.Success);
            }
            Render();
        });
        var refresh = UiClass.Btn("刷新", BtnKind.Chip, (_, _) => Render());
        Render();
        AttachedToVisualTree += (_, _) => Render();
        return UiClass.Card(Rows(
            UiClass.Field("记忆", UiClass.H(8, refresh, clear), "AI 助手跨对话记住的偏好和配置（数据目录 ai_memory.md，一行一条）"),
            count, list));
    }

    /// <summary>手机同步卡：IP+配对码 / USB（adb）连接，走 Core 的 PhoneLink。</summary>
    private Border BuildPhoneSyncCard()
    {
        var status = UiClass.Small("");
        var ip = UiClass.Input("手机上显示的地址，例如 192.168.1.5:8765", "", 240);
        var token = UiClass.Input("配对码", "", 120);
        void SetStatus(string text) => status.Text = text;
        var connect = UiClass.Btn("连接", BtnKind.Soft, (_, _) => Run(async () =>
        {
            SetStatus("连接中…");
            var info = await Task.Run(() => PhoneLink.Connect(ip.Text!.Trim(), token.Text!.Trim()));
            SetStatus($"已连接：{info.Device}");
            Toast("已连接手机", info.Device ?? "", ToastKind.Success);
        }, "连接失败"));
        var usb = UiClass.Btn("USB 自动连接", BtnKind.Chip, (_, _) => Run(async () =>
        {
            SetStatus("USB 连接中…");
            var info = await Task.Run(() => PhoneLink.ConnectUsb(token.Text!.Trim(), log => SetStatus(log)));
            SetStatus($"已连接：{info.Device}");
            Toast("已连接手机", info.Device ?? "", ToastKind.Success);
        }, "USB 连接失败"));
        var disconnect = UiClass.Btn("断开", BtnKind.Ghost, (_, _) => Run(async () =>
        {
            await Task.Run(PhoneLink.Disconnect);
            SetStatus("已断开");
        }, "断开失败"));
        if (PhoneLink.Current != null)
            SetStatus($"已连接：{PhoneLink.LastDevice}");
        else
            SetStatus("未连接。手机端装 TipCL 同步 App，同一局域网内输入地址与配对码");
        return UiClass.Card(UiClass.V(4, UiClass.Small("电脑和手机互传存档、截图与配置").Wrap(),
            Rows(
                UiClass.Field("手机地址", UiClass.H(8, ip, token, connect.VCenter()), "格式 地址:端口"),
                UiClass.Field("USB", UiClass.H(8, usb, disconnect.VCenter()), "手机插 USB 且开了 USB 调试（adb）时可用"),
                UiClass.Field("状态", status))));
    }

    private static Border BuildAboutCard()
    {
        var thanks = UiClass.Muted("@368****168 — 帮忙测试了正版（微软）验证").Wrap();
        return UiClass.Card(UiClass.V(2,
            UiClass.Section("关于", "TipCL 启动器"),
            UiClass.Field("版本", UiClass.Muted(AppInfo.APP_VERSION + "（Linux 移植版）")),
            UiClass.Field("致谢", thanks, "感谢每一位帮忙测试的朋友")));
    }

    /// <summary>反馈卡（Windows 版是独立 FeedbackPage；M2 先落提交卡，完整反馈流 M3 补）。</summary>
    private Border BuildFeedbackCard()
    {
        var body = UiClass.Multi("说说你的使用体验、遇到的问题或想要的功能…", "", 110);
        var info = UiClass.Small("");
        Button? send = null;
            send = UiClass.Btn("发送反馈", BtnKind.Primary, (_, _) => Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
            {
                Toast("先写点内容", "反馈内容不能为空", ToastKind.Warning);
                return;
            }
            send!.IsEnabled = false;
            info.Text = "发送中…";
            object? r = await Api.TryCallAsync<object?>("feedback_submit", null, new { text = body.Text.Trim() });
            send!.IsEnabled = true;
            if (r == null)
            {
                info.Text = "反馈通道后端 M3 接入，暂时发送不了。也可以到 GitHub 仓库提 issue。";
                return;
            }
            body.Text = "";
            info.Text = "已发送，谢谢你的反馈！";
            Toast("已发送", "感谢你的反馈", ToastKind.Success);
        }, "发送失败"));
        return UiClass.Card(UiClass.V(6,
            UiClass.Section("反馈", "问题与建议直通车"),
            body,
            UiClass.H(8, send, info.VCenter())).M(18, 8, 18, 16));
    }

    private async Task CleanAsync()
    {
        _clean.IsEnabled = false;
        _clean.Content = UiClass.Txt("扫描中…");
        var preview = await Api.TryCallAsync<CleanerPreviewStub>("cleaner_preview");
        _clean.IsEnabled = true;
        _clean.Content = UiClass.Txt("扫描并清理");
        if (preview == null || preview.Count == 0)
        {
            _cleanInfo.Text = "没有可清理的文件";
            Toast("很干净", "没有找到可清理的文件", ToastKind.Success);
            return;
        }
        _cleanInfo.Text = $"可清理 {preview.Count} 个文件";
        if (await Dlg.Confirm("清理文件", $"共 {preview.Count} 个文件。删掉的文件下次用到会自动重新下载。", "开始清理", "取消", danger: true))
        {
            var result = await Api.TryCallAsync<CleanerResultStub>("cleaner_apply", null, new { kinds = Py.L("unused_libraries", "parts", "cache") });
            _cleanInfo.Text = result == null ? "清理后端 M3 接入" : $"已清理 {result.Removed} 个文件";
            Toast("清理完成", _cleanInfo.Text, ToastKind.Success);
        }
    }

    private async Task RecommendAsync()
    {
        _rec.IsEnabled = false;
        var rec = await Api.TryCallAsync<SmartRecStub>("get_smart_recommendation");
        _rec.IsEnabled = true;
        if (rec == null)
        {
            Toast("智能推荐", "推荐后端 M3 接入，暂不可用", ToastKind.Warning);
            return;
        }
        var body = UiClass.V(6,
            UiClass.Muted($"本机：{rec.TotalRamGb} GB 内存 / {rec.CpuCount} 核 CPU"),
            UiClass.Txt($"推荐内存 {rec.MemoryMb} MB", 14, bold: true),
            UiClass.Small($"推荐 Java {rec.JavaMajor} · 窗口 {rec.WindowWidth}×{rec.WindowHeight} · 回收器 {rec.GcPreset}"));
        if (await Dlg.Ask("智能推荐", body, "应用推荐"))
        {
            _mem.Value = Math.Clamp(rec.MemoryMb, 512, 32768);
            if (rec.WindowWidth > 0) _w.Text = rec.WindowWidth.ToString();
            if (rec.WindowHeight > 0) _h.Text = rec.WindowHeight.ToString();
            SelectKey(_gc, string.IsNullOrEmpty(rec.GcPreset) ? "auto" : rec.GcPreset);
            Toast("已应用", $"内存已设为 {rec.MemoryMb} MB，保存设置后生效", ToastKind.Success);
        }
    }

    /// <summary>清理/推荐返回模型（cleaner_* 与 get_smart_recommendation 后端 M3 接，TryCall 失败为 null）。</summary>
    private sealed class CleanerPreviewStub
    {
        public int Count { get; set; }
        public long Bytes { get; set; }
    }

    private sealed class CleanerResultStub
    {
        public int Removed { get; set; }
        public long Bytes { get; set; }
    }

    private sealed class SmartRecStub
    {
        public double TotalRamGb { get; set; }
        public int CpuCount { get; set; }
        public int MemoryMb { get; set; }
        public int JavaMajor { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public string GcPreset { get; set; } = "auto";
    }
}
