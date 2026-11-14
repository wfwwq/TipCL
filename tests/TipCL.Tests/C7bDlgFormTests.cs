using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C7b:`Dlg.Form&lt;T&gt;` —— **mac 侧补的缺件**(Windows 有、mac 此前没有)的两条路径。
/// 契约来源是 Windows 那两个调用点的实际用法(`PluginSettingsCard` 的"插件市场地址"与"添加自定义镜像"):
/// 校验失败 ⇒ **对话框留着**、错误以 Toast「填写有误」+ 抖卡片的形式给出、任务**不能完成**;
/// 校验通过 ⇒ 正常返回收集到的值并关掉卡片。
/// </summary>
public class C7bDlgFormTests
{
    /// <summary>校验抛异常 ⇒ 卡片还在 + 错误可见 + 任务未完成;随后取消才收尾(结果是 null)。</summary>
    [Fact]
    public void Form_keeps_the_dialog_open_and_reports_the_validation_error()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            int collected = 0;
            Task<List<string>?> task = Dlg.Form<List<string>?>(I18n.L("插件市场地址"), Ui.Muted("正文"), delegate
            {
                collected++;
                throw new ArgumentException(I18n.L("地址要以 http:// 或 https:// 开头"));
            });
            // 【D-113】先把卡片推出来再点它
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<TipCL.Services.DialogCard>().Last();
            Assert.Equal(I18n.L("插件市场地址"), card.Title);
            Button ok = C5bFiles.Descendants(card).OfType<Button>().First(b => string.Equals(b.Content as string, I18n.L("保存"), StringComparison.Ordinal));
            C5bFiles.Click(ok);
            C5bFiles.Pump(() => toasts.Has(I18n.L("填写有误")));
            Assert.True(toasts.Has(I18n.L("填写有误")));
            Assert.Contains(toasts.Items, t => (t.Body ?? "").Contains(I18n.L("地址要以 http:// 或 https:// 开头"), StringComparison.Ordinal));
            Assert.Equal(1, collected);
            // 卡片留着(没有关),任务也没完成
            Assert.Equal(1, C5bFiles.LayerCount());
            Assert.False(task.IsCompleted, "校验失败时 Form 不该收尾");
            // 收尾:点取消 ⇒ 任务完成、结果是 null、卡片关掉
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("取消")));
            C5bFiles.PumpTask(task);
            Assert.Null(task.Result);
            C5bFiles.Pump(() => C5bFiles.LayerCount() == 0);
        });
    }

    /// <summary>校验通过 ⇒ 正常返回收集到的值、卡片关掉、没有错误 Toast。</summary>
    [Fact]
    public void Form_returns_the_collected_value_when_validation_passes()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            Task<Dictionary<string, object>?> task = Dlg.Form<Dictionary<string, object>?>(I18n.L("添加自定义镜像"), Ui.Muted("正文"), delegate
            {
                return new Dictionary<string, object> { ["name"] = "我的镜像" };
            });
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<TipCL.Services.DialogCard>().Last();
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("保存")));
            C5bFiles.PumpTask(task);
            Assert.NotNull(task.Result);
            Assert.Equal("我的镜像", task.Result!["name"]);
            Assert.Empty(toasts.Items);
            C5bFiles.Pump(() => C5bFiles.LayerCount() == 0);
        });
    }

    /// <summary>自定义确认按钮文案与默认宽度照 Windows 契约(<c>ok</c> 缺省是「保存」、宽度 560)。</summary>
    [Fact]
    public void Form_uses_the_windows_default_ok_label_and_width()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            Task<string?> task = Dlg.Form<string>(I18n.L("带校验的表单"), Ui.Muted("正文"), () => "值");
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<TipCL.Services.DialogCard>().Last();
            Assert.True(C5bFiles.HasButton(card, I18n.L("保存")));
            Assert.True(C5bFiles.HasButton(card, I18n.L("取消")));
            Assert.Equal(560.0, card.MaxWidth);
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("取消")));
            C5bFiles.PumpTask(task);
            Assert.Null(task.Result);
        });
    }
}
