// R32-DEGRADE-FIX(W5-mac):插件注册的原生区块(kind = "block")在 mac 的 native 分支上,
// **不许**再落到「插件没有提供这个界面」,而要按 Windows 的区块口径处理:
//  1 有工厂 ⇒ 走工厂(Windows `NativePluginUi.CreateBlock`,`0a304a7:PluginUi.cs:279/298` 同款);
//  2 没工厂 ⇒ 显示既有词表里的「区块不可用」占位。
//
// 两条用例分别钉住这两半:**去掉 "block" 分支** ⇒ 第 2 条红(拿到的还是「插件没有提供这个界面」);
// **把兜底文案改回「插件没有提供这个界面」** ⇒ 第 1 条红。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using TipCL.Pages.Plugins;
using TipCL.Plugins;
using TipCL.Services;
using Xunit;
using Xunit.Abstractions;

namespace TipCL.Tests;

public class R32DegfixBlockUiTests
{
    private readonly ITestOutputHelper _out;

    public R32DegfixBlockUiTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>一个"原生区块"贡献(native=true、没有网页地址 —— 正是 mac 上走到 empty 占位的那一支)。</summary>
    private static UiContribution NativeBlock(string plugin, string id, string label)
        => UiContribution.From(JsonDocument.Parse(
            "{\"plugin\":\"" + plugin + "\",\"kind\":\"block\",\"id\":\"" + id + "\",\"label\":\"" + label + "\",\"native\":true}").RootElement);

    private static List<string> Texts(Control root)
        => C5bFiles.Texts(root).Select(t => t.Text ?? "").ToList();

    /// <summary>
    /// 没有原生工厂时:必须显示**既有词表条目**「区块不可用」(Windows `LayoutEditor` 对造不出内容的
    /// 块用的同一句),而不是对 page/dashCard/aiView 才正确的「插件没有提供这个界面」。
    /// </summary>
    [Fact]
    public void NativeBlock_WithoutFactory_ShowsBlockUnavailable_NotPluginDidNotProvide() => AvaloniaSession.Run(() =>
    {
        Control card = NativePluginUi.Content(NativeBlock("degfix-demo", "nofactory", "天气块"))!;
        List<string> texts = Texts(card);
        _out.WriteLine("R32-DEGFIX mac native-block(no factory) texts=[" + string.Join(" | ", texts) + "]");

        Assert.True(C5bFiles.HasText(card, I18n.L("区块不可用")), "texts=[" + string.Join(" | ", texts) + "]");
        Assert.False(C5bFiles.HasText(card, I18n.L("插件没有提供这个界面")), "texts=[" + string.Join(" | ", texts) + "]");
    });

    /// <summary>
    /// 有原生工厂时:必须走工厂(Windows 的 <c>CreateBlock</c> 口径),把插件注册的控件渲染出来。
    /// </summary>
    [Fact]
    public void NativeBlock_UsesTheRegisteredBlockFactory() => AvaloniaSession.Run(() =>
    {
        StackPanel body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "示例 .NET 区块正文" });
        using IDisposable reg = PluginUiRegistry.Register(new NativeUiEntry
        {
            Plugin = "degfix-demo",
            Kind = "block",
            Id = "withfactory",
            Label = "示例 .NET 区块",
            Factory = () => body,
        });

        Control card = NativePluginUi.Content(NativeBlock("degfix-demo", "withfactory", "示例 .NET 区块"))!;
        List<string> texts = Texts(card);
        _out.WriteLine("R32-DEGFIX mac native-block(with factory) texts=[" + string.Join(" | ", texts) + "]");

        Assert.True(C5bFiles.HasText(card, "示例 .NET 区块正文"), "texts=[" + string.Join(" | ", texts) + "]");
        Assert.False(C5bFiles.HasText(card, I18n.L("插件没有提供这个界面")), "texts=[" + string.Join(" | ", texts) + "]");
    });
}
