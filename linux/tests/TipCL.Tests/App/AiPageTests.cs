using System;
using TipCL.App.Common;
using TipCL.App.Common.Ai;
using TipCL.App.Pages.Ai;
using TipCL.App.Shell;
using TipCL.Core;
using Client = TipCL.Core.Launcher.Ai.Client;

using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H P5 AI 页回归:AI 助手不再是占位页 —— 页面能构建、外壳能导航进去,标题栏 AI 药丸钮有落点。
/// (对话本身的流式解析由 Core/AiClientTests 覆盖;这里不做联网断言。)
/// </summary>
public class AiPageTests
{
    [Fact]
    public void AiPage_Constructs() => TestApp.Run(() =>
    {
        var page = new AiPage();
        Assert.Equal("ai", page.PageId);
        Assert.Equal("AI 助手", page.PageTitle);
        Assert.NotNull(page.Content);
    });

    [Fact]
    public void Shell_Navigates_ToAiPage() => TestApp.Run(() =>
    {
        var win = new MainWindow();
        win.Navigate("ai");
        Assert.Equal("ai", win.CurrentPageId);
        Assert.IsType<AiPage>(win.CurrentPage);
    });

    /// <summary>
    /// 端点解析的键要与 Core 真实输出一致(mode/model/url)——页面头部按 mode 显示「内置网关 / 自定义端点」,
    /// 并据此判断「是否已配置」。自定义端点缺地址时必须抛错(页面折成「未配置」引导)。
    /// </summary>
    [Fact]
    public void Endpoint_ExposesModeAndModel()
    {
        var custom = Client.ResolveEndpoint(PyDict.Of(
            "ai_mode", "custom",
            "ai_base_url", "http://127.0.0.1:1/v1",
            "ai_model", "test-model"));
        Assert.Equal("custom", Py.S(custom.Get("mode")));
        Assert.Equal("test-model", Py.S(custom.Get("model")));
        Assert.EndsWith("/chat/completions", Py.S(custom.Get("url")));

        // 地址为空时 Core 判定为内置模式(不抛),页面据此显示内置网关标签
        var builtin = Client.ResolveEndpoint(PyDict.Of("ai_mode", "custom", "ai_base_url", ""));
        Assert.Equal("builtin", Py.S(builtin.Get("mode")));
    }
}
