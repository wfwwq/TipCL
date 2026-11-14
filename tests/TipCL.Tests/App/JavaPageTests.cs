using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Pages.Settings;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H P3 Java 子页回归:发行版列表 / 标签 / 已检测 Java 列表 / 设置页内层导航第 3 项(Java)
/// 必须能构建出真实子页(此前只是空壳,缺下载卡与实例默认区)。
/// </summary>
public class JavaPageTests
{
    private readonly IApi _api = InProcessApi.Instance;

    [Fact]
    public void JavaVendor_ListAndLabel_AreWired() => TestApp.RunAsync(async () =>
    {
        var vendors = await _api.CallAsync<List<string>>("java_vendor_list");
        Assert.NotNull(vendors);
        Assert.NotEmpty(vendors!);
        Assert.Contains("adoptium", vendors);

        string label = await _api.CallAsync<string>("java_vendor_label", new { vendor = "adoptium" }) ?? "";
        Assert.False(string.IsNullOrEmpty(label), "Adoptium 必须有可读标签");
    });

    [Fact]
    public void JavaList_Rows_HaveNameMajorPath() => TestApp.RunAsync(async () =>
    {
        var rows = await _api.CallAsync<List<object?>>("get_java_list", new { scan_system = false });
        Assert.NotNull(rows);
        foreach (var item in rows!)
        {
            var d = Py.D(item!);
            Assert.False(string.IsNullOrEmpty(Py.S(d.Get("name"))), "Java 行缺 name");
            Assert.False(string.IsNullOrEmpty(Py.S(d.Get("major"))), "Java 行缺 major");
            // path 可能为空(某些探测只给目录),但键必须在
            Assert.NotNull(d.Get("path"));
        }
    });

    [Fact]
    public void JavaComboOptions_StartWithAutoChoice() => TestApp.RunAsync(async () =>
    {
        var opts = await _api.CallAsync<List<object?>>("java_combo_options", new { instance = "", scan_system = false });
        Assert.NotNull(opts);
        Assert.NotEmpty(opts!);
        var first = Py.D(opts[0]!);
        Assert.Equal("自动选择", Py.S(first.Get("label")));
        Assert.Equal("自动选择", Py.S(first.Get("value")));
    });

    /// <summary>设置页内层导航第 3 项(Java)必须切出一页真实内容(不是空占位)。</summary>
    [Fact]
    public void SettingsJavaTab_ShowsRealSubView() => TestApp.Run(() =>
    {
        var page = new SettingsPage();
        page.SelectTab(2);
        Assert.NotNull(page.Content);
        // 切回通用项也不应炸(子页生命周期转发)
        page.SelectTab(0);
        page.SelectTab(2);
        Assert.NotNull(page.Content);
    });
}
