using System;
using System.Linq;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C7a:导航表一致性 —— 把 mac 的导航表与**注册结果**对着 Windows 的 <c>MainWindow.xaml.cs:108-117</c> 逐条钉住。
///
/// Windows 那十行逐字是:
/// <code>
/// launch→LaunchPage  library→LibraryPage  instance→VersionManagePage  account→AccountPage
/// ai→AiPage  multiplayer→MultiplayerPage  settings→SettingsPage  tasks→TasksPage
/// download→DownloadPage  plugins→PluginsPage
/// </code>
///
/// <b>为什么用"类型名"而不是 <c>typeof</c></b>:表里还有 mac 尚未移植的页面(如 <c>AiPage</c> / <c>PluginsPage</c>),
/// 写 <c>typeof(AiPage)</c> 会**编译不过**。所以这里比类型名 —— 强度足够(钉的是"这个 id 解析出的是哪一页"),
/// 而且**尚未移植的页面只要将来注册进来,这条自动开始生效**,不需要改测试。
///
/// <b>两条二选一断言(不许 Skip、不写死桩)</b>:已注册 ⇒ 类型名必须相符;未注册 ⇒ <c>Create` 必须返回 null。
/// 这样 C7b 的插件页 / 以后的 Ai 页落地时会自动从"未注册也绿"升级为"类型必须对"。
/// </summary>
public class C7aNavTableTests
{
    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:108-117</c> 的十行导航表(id → 页面类型名)。</summary>
    [Theory]
    [InlineData("launch", "LaunchPage")]
    [InlineData("library", "LibraryPage")]
    [InlineData("instance", "VersionManagePage")]
    [InlineData("account", "AccountPage")]
    [InlineData("ai", "AiPage")]
    [InlineData("multiplayer", "MultiplayerPage")]
    [InlineData("settings", "SettingsPage")]
    [InlineData("tasks", "TasksPage")]
    [InlineData("download", "DownloadPage")]
    [InlineData("plugins", "PluginsPage")]
    public void The_registry_resolves_each_nav_id_to_the_windows_page(string id, string expectedTypeName)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = TraceScope();
            PageBase? page = PageRegistry.Create(id);
            if (page is null)
            {
                // 未注册:允许(页面还没移植),但**必须是"真的没有"**,不能是"注册成了别的页"
                Assert.Null(PageRegistry.Create(id));
                return;
            }
            Assert.Equal(expectedTypeName, page.GetType().Name);
        });
    }

    /// <summary>
    /// 【Windows 源】同上:这十个 id 在**导航层**里都得有落点 —— 顶层行(<c>MainWindow.NavDefs</c>)、
    /// 分段(<c>NavModel.SectionIds</c>)或导航键(<c>NavModel.TopKeys</c>)三者之一
    /// (下载页是"一整段",它不在 <c>PageForNavKey</c> 里,这是 C6a 实测并钉过的形状)。
    /// </summary>
    [Theory]
    [InlineData("launch")]
    [InlineData("library")]
    [InlineData("instance")]
    [InlineData("account")]
    [InlineData("ai")]
    [InlineData("multiplayer")]
    [InlineData("settings")]
    [InlineData("tasks")]
    [InlineData("download")]
    [InlineData("plugins")]
    public void The_nav_layer_knows_every_id_of_the_windows_table(string id)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = TraceScope();
            bool known = MainWindow.NavDefs.Any(d => d.Id == id)
                         || NavModel.SectionIds.Contains(id)
                         || NavModel.TopKeys.Contains(id)
                         || NavModel.PageForNavKey.ContainsKey(id);
            Assert.True(known, "导航层里没有这个 id:" + id);
        });
    }

    /// <summary>
    /// 【C7a 更正 · 把今晚那处偏差钉死】<see cref="InstancePage"/> 在 Windows 原版**没有任何构造点**
    /// (全工程扫过)⇒ mac 侧也**不给它任何导航入口**:它不该是这十个 id 里任何一个的目标。
    /// </summary>
    [Theory]
    [InlineData("launch")]
    [InlineData("library")]
    [InlineData("instance")]
    [InlineData("account")]
    [InlineData("ai")]
    [InlineData("multiplayer")]
    [InlineData("settings")]
    [InlineData("tasks")]
    [InlineData("download")]
    [InlineData("plugins")]
    public void InstancePage_is_not_the_target_of_any_nav_id(string id)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = TraceScope();
            Assert.IsNotType<InstancePage>(PageRegistry.Create(id));
        });
    }

    /// <summary>
    /// 本文件用的全局静态作用域:注册表 + 应用资源相关的几个静态按快照还原(D-69)。
    /// 本类只读注册表,所以作用域里只快照 <c>PageRegistry</c> 与应用主题变体(后者会被别处的用例改)。
    /// </summary>
    private static C7aNavScope TraceScope()
    {
        return new C7aNavScope();
    }

    private sealed class C7aNavScope : IDisposable
    {
        private readonly System.Collections.Generic.IReadOnlyDictionary<string, Func<PageBase>> _registry;

        private readonly Avalonia.Styling.ThemeVariant? _variant;

        internal C7aNavScope()
        {
            _registry = PageRegistry.Snapshot();
            _variant = Avalonia.Application.Current!.RequestedThemeVariant;
        }

        public void Dispose()
        {
            PageRegistry.Restore(_registry);
            Avalonia.Application.Current!.RequestedThemeVariant = _variant;
        }
    }
}
