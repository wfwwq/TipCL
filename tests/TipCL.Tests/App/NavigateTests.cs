using TipCL.App.Shell;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 外壳导航回归:每个顶部页签都必须能切过去(含回到首个页签「启动」),
/// 覆盖 M3 真机暴露的「首次挂载才加载数据 / 页签点了没反应」两类问题。
/// 测试体一律经 TestApp.Run 落在无头 UI 线程上(控件构造有线程亲和)。
/// </summary>
public class NavigateTests
{
    [Fact]
    public void Navigate_SwitchesEveryTopTab_AndBackToLaunch() => TestApp.Run(() =>
    {
        var win = new MainWindow();
        Assert.Equal("launch", win.CurrentPageId); // 构造即落在启动页

        foreach (string id in new[] { "settings", "library", "download", "plugins", "tasks" })
        {
            win.Navigate(id);
            Assert.Equal(id, win.CurrentPageId);
        }

        // 回到第一个页签(真机上曾出现点了不切换)
        win.Navigate("launch");
        Assert.Equal("launch", win.CurrentPageId);
    });

    /// <summary>
    /// 任务页(2026-11-10 由占位转真实):构造必须成功并挂上 BlockHost 内容,
    /// 防「页签能切、页构建即炸」这类只在点击后暴露的回归。
    /// </summary>
    [Fact]
    public void TasksPage_Constructs_WithBlockHostContent() => TestApp.Run(() =>
    {
        var page = new TipCL.App.Pages.TasksPage();
        Assert.Equal("tasks", page.PageId);
        Assert.Equal("下载任务", page.PageTitle);
        Assert.NotNull(page.Content);
    });
}
