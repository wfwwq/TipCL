// R32-DEGRADE-FIX(W11):插件区块(kind = "block")在 Linux 的降级文案必须是「区块不可用」,
// **不许**再落到占位 switch 的兜底句「该插件注册了一块自定义界面,类型未知。」。
//
// 两条用例对应本包的两处改动:
//  1 直接打 UiDispatch.Placeholder —— 就是生产消费者 PluginBlocks.cs:111 传进去的那一支(switch 缺 "block" 分支);
//  2 走完整链:插件清单 contributes.blocks → 插件启用 → PluginUi.Refresh → PluginBlocks.Build(plugin:<id>:<id>)
//    —— 这一条同时钉住 PluginManifest.UiKinds 里 "block" 的存在(缺了它贡献根本不会产生,块只会显示「插件区块不可用」)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL.App.Common.Plugins;
using TipCL.App.Pages.Layout;
using TipCL.App.Pages.Plugins;
using TipCL.Core.Launcher;
using TipCL.Core.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TipCL.Tests.App;

public class R32DegfixBlockTests : IDisposable
{
    private readonly ITestOutputHelper _out;

    private readonly string _id = "t-degfix-" + Guid.NewGuid().ToString("N").Substring(0, 6);

    private readonly PluginDirs _dirs = new PluginDirs();

    public R32DegfixBlockTests(ITestOutputHelper output)
    {
        _out = output;
    }

    public void Dispose()
    {
        TestApp.Run(() =>
        {
            try { PluginRuntime.Uninstall(_id, deleteData: true); } catch (Exception) { }
            PluginUi.Refresh();
        });
    }

    private static UiContribution Contribution(string kind, string id, string label, string url = "")
        => UiContribution.From(JsonDocument.Parse(
            "{\"plugin\":\"demo\",\"kind\":\"" + kind + "\",\"id\":\"" + id + "\",\"label\":\"" + label + "\",\"url\":\"" + url + "\"}").RootElement);

    private static List<string> Texts(Control root)
        => root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

    /// <summary>
    /// W11 正题:一个 kind = "block" 的贡献走 <see cref="UiDispatch.Placeholder"/>(生产消费者
    /// <c>PluginBlocks.Shell.Sync</c> 的唯一调用点)时,文案必须是「区块不可用」。
    /// **把 "block" 分支去掉就变红**(断言会拿到兜底句「…类型未知。」)。
    /// </summary>
    [Fact]
    public void BlockContribution_Placeholder_SaysBlockUnavailable_NotTypeUnknown() => TestApp.Run(() =>
    {
        Control box = UiDispatch.Placeholder(Contribution("block", "weather", "天气块"));
        List<string> texts = Texts(box);
        _out.WriteLine("R32-DEGFIX linux block placeholder texts=[" + string.Join(" | ", texts) + "]");

        Assert.Contains(I18nCore.Tr("区块不可用"), texts);
        Assert.DoesNotContain(texts, t => t.Contains("类型未知", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("自定义界面", StringComparison.Ordinal));
    });

    /// <summary>
    /// 端到端:插件清单声明 <c>contributes.blocks</c> → 贡献真的产生(kind=block)→ 布局里
    /// <c>plugin:&lt;id&gt;:&lt;blockId&gt;</c> 这个块经内容工厂造出来时显示「区块不可用」。
    /// **把 UiKinds 里的 "block" 去掉**(贡献不产生 ⇒ 块落「插件区块不可用」)或**把 switch 的 "block"
    /// 分支去掉**(⇒ 块落「类型未知」)都会变红。**
    /// </summary>
    [Fact]
    public void PluginBlock_FromManifest_RendersAsBlockUnavailable() => TestApp.Run(() =>
    {
        string dir = _dirs.PluginDir(_id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName),
            "{\"id\":\"" + _id + "\",\"name\":\"测试区块插件\",\"version\":\"1.0.0\",\"runtime\":\"node\"," +
            "\"main\":\"index.js\",\"permissions\":[\"ui\"],\"contributes\":{\"blocks\":[{\"id\":\"hello\",\"label\":\"示例区块\"}]}}");
        PluginRuntime.SetEnabled(_id, true);
        PluginUi.Refresh();

        UiContribution? c = PluginUi.Find("block", _id, "hello");
        Assert.NotNull(c);

        Control shell = PluginBlocks.Build("plugin:" + _id + ":hello", "launch")!;
        List<string> texts = Texts(shell);
        _out.WriteLine("R32-DEGFIX linux plugin-block shell texts=[" + string.Join(" | ", texts) + "]");

        Assert.Contains(I18nCore.Tr("区块不可用"), texts);
        Assert.DoesNotContain(texts, t => t.Contains("类型未知", StringComparison.Ordinal));
        // 反向锚:块保留语义(插件被禁用/卸载)走的是另一句,别把两者混成一句
        Assert.DoesNotContain(texts, t => t.Contains("被禁用或卸载", StringComparison.Ordinal));
    });
}
