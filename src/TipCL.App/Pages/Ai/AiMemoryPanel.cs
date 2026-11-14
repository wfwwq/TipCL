// M4-H P5+（R2）：会话记忆面板 —— 查看/编辑/删除 AI 记住的内容。
//
// 记忆本身存在 `$TIPCL_HOME/ai_memory.md`（Core 的 FileMemory，一行一条 `- 日期 内容`），
// 与设置页「AI 助手 → 记忆」卡读的是同一份文件；这里只是把它搬到对话页旁边，
// 用户看到助手说「我记住了」时不用跳去设置页。行为与设置页那张卡一致:
// 编辑走 Dlg.Prompt、删除/清空前确认、写完重新渲染。
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>AI 长期记忆面板(非模态,由 Dlg.OpenPanel 承载)。</summary>
internal sealed class AiMemoryPanel : Border
{
    private readonly StackPanel _list = UiClass.V(4);
    private readonly TextBlock _count = UiClass.Muted("");
    private readonly Button _clear = UiClass.Btn("清空全部", BtnKind.Danger);

    public AiMemoryPanel()
    {
        _clear.Click += async (_, _) =>
        {
            if (await Dlg.Confirm("清空全部记忆?", "助手记下的偏好、配置和修好过的问题都会删除,无法恢复。", "清空", null, danger: true))
            {
                int n = FileMemory.Clear();
                ToastHost.Show("已清空记忆", "删除了 " + n + " 条", ToastKind.Success);
                Render();
            }
        };
        var add = UiClass.Btn("手动记一条", BtnKind.Chip, (_, _) => _ = AddAsync());
        var refresh = UiClass.Btn("刷新", BtnKind.Chip, (_, _) => Render());

        Child = UiClass.V(10,
            UiClass.Section("助手记住的事", "跨对话生效;新对话开始时会把最近的记忆带给助手",
                UiClass.H(8, add, refresh, _clear)),
            _count,
            _list);
        Padding = new Avalonia.Thickness(4);
        Render();
    }

    private async System.Threading.Tasks.Task AddAsync()
    {
        string? text = await Dlg.Prompt("记一条", "内容", "", "例如:我的电脑只有 8G 内存,默认给 3G");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        string result = FileMemory.Remember(text);
        ToastHost.Show("已写入记忆", result, ToastKind.Success);
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        List<string> entries = FileMemory.Entries();
        _count.Text = entries.Count == 0
            ? "还没有记忆。对话里说出偏好,或修好问题后让助手「记住」,就会出现在这里"
            : "共 " + entries.Count + " 条(存在数据目录的 ai_memory.md,也可以直接编辑那个文件)";
        _clear.IsEnabled = entries.Count > 0;

        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            string entry = entries[i];
            string body = FileMemory.TextOf(entry);
            string date = entry.Length > body.Length ? entry.Substring(0, entry.Length - body.Length).Trim() : "";

            var text = UiClass.Txt(body, 12.5);
            text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var row = UiClass.G(null, "Auto,*,Auto,Auto");
            row.Add(UiClass.Txt(date, 11, false, "B.InkFaint").VCenter(), 0, 0);
            row.Add(text.VCenter(), 0, 1);
            row.Add(UiClass.Btn("编辑", BtnKind.Chip, (_, _) => _ = EditAsync(index, entry, body)).VCenter(), 0, 2);
            row.Add(UiClass.Btn("删除", BtnKind.Ghost, (_, _) => RemoveAt(index, entry)).VCenter(), 0, 3);
            _list.Children.Add(UiClass.Pane(row, "B.Paper2", 8, 8));
        }
    }

    private async System.Threading.Tasks.Task EditAsync(int index, string entry, string body)
    {
        string? edited = await Dlg.Prompt("编辑记忆", "内容", body);
        if (edited == null)
        {
            return;
        }
        if (!Same(index, entry))
        {
            return;
        }
        FileMemory.Update(index, edited);
        Render();
    }

    private void RemoveAt(int index, string entry)
    {
        if (!Same(index, entry))
        {
            return;
        }
        FileMemory.RemoveAt(index);
        Render();
    }

    /// <summary>编辑/删除前核对下标还对得上(文件可能刚被别的窗口改过)。</summary>
    private static bool Same(int index, string entry)
    {
        List<string> now = FileMemory.Entries();
        if (index < now.Count && now[index] == entry)
        {
            return true;
        }
        ToastHost.Show("记忆已变化", "文件刚被改过,已刷新列表", ToastKind.Warning);
        return false;
    }
}
