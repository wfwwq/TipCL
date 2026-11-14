using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL;
using TipCL.Core.Launcher;
using TipCL.Pages;
using TipCL.Pages.Plugins;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C7b(批4):**"无调用方清单"的驱动用例** —— 每一条都真的驱动那件东西并断言**具体结果**
/// (不是"已接线")。清单四项:①内容卡那条路径;②市场地址保存的 payload 形状;
/// ③未审核开关的确认分支;④`IPluginHostPage` 的两个调用点(假实现 + 真实现)。
///
/// 纪律:回退口/注册表/`ShellOpenHook` 都是**进程级静态** ⇒ 每条用例在 <c>finally</c> 里写回进入前的值。
/// </summary>
public class C7bChecklistTests
{
    /// <summary>一个"假插件主页":只记下自己被调了什么(用来证明卡片真的走到了接口上)。</summary>
    private sealed class FakeHostPage : PageBase, IPluginHostPage
    {
        public override string Title => "假插件主页";

        public int Refreshes;

        public readonly List<string> Tabs = new List<string>();

        public void RefreshMarketSoon()
        {
            Refreshes++;
        }

        public void ShowTab(string tab)
        {
            Tabs.Add(tab);
        }
    }

    // ============================================================ ① 内容卡那条路径

    /// <summary>
    /// 网页贡献 ⇒ **如实内容卡**(地址 + 「在浏览器中打开」);点它走的是 <c>Crash.ShellOpenHook</c>
    /// 拦下来的那条(**不是真开窗口**),并且拿到的正是那个带 pageToken 的地址。
    /// </summary>
    [Fact]
    public void Web_contribution_shows_the_honest_card_and_opens_through_the_shell_hook()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("plugins_ui", "[{\"plugin\":\"demo\",\"kind\":\"page\",\"id\":\"board\",\"label\":\"看板\"," +
                                     "\"url\":\"http://127.0.0.1:9/view?pageToken=abc\"}]")
                .Route("plugins_list", "[]");
            Action<string> originalHook = Crash.ShellOpenHook;
            List<string> opened = new List<string>();
            try
            {
                Crash.ShellOpenHook = path => opened.Add(path);
                PageBase.Run(PluginUi.RefreshAsync);
                C5bFiles.Pump(() => PluginUi.FindPage("plugin:demo:board") != null);
                UiContribution c = PluginUi.FindPage("plugin:demo:board")!;
                Control card = NativePluginUi.Content(c)!;
                // 如实内容卡:地址在卡上可见
                Assert.True(C5bFiles.HasText(card, "http://127.0.0.1:9/view?pageToken=abc"));
                Button open = C5bFiles.Descendants(card).OfType<Button>()
                    .First(b => string.Equals(AutomationProperties.GetName(b), I18n.L("在浏览器中打开"), StringComparison.Ordinal));
                C5bFiles.Click(open);
                C5bFiles.Pump(() => opened.Count > 0);
                Assert.Equal("http://127.0.0.1:9/view?pageToken=abc", opened[0]);
            }
            finally
            {
                Crash.ShellOpenHook = originalHook;
            }
        });
    }

    /// <summary>原生贡献不走内容卡,而是走 <see cref="NativePluginUi"/> 的反射工厂(没有工厂时给如实提示)。</summary>
    [Fact]
    public void Native_contribution_without_a_registry_shows_the_honest_notice()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            UiContribution native = UiContribution.From(System.Text.Json.JsonDocument
                .Parse("{\"plugin\":\"demo\",\"kind\":\"page\",\"id\":\"x\",\"label\":\"原生\",\"native\":true}").RootElement);
            Control card = NativePluginUi.Content(native)!;
            // 没有注册表 ⇒ 如实提示,而不是空白
            Assert.True(C5bFiles.HasText(card, I18n.L("插件没有提供这个界面")));
        });
    }

    // ============================================================ ② 市场地址保存的 payload 形状

    /// <summary>
    /// 设置卡的「插件市场地址」对话框:填一个地址 ⇒ 保存后 <c>save_settings</c> 的 payload 里
    /// <c>plugin_market_indexes</c> 是**字符串数组**(键名与值类型都要对,不只断言"发了")。
    /// </summary>
    [Fact]
    public void Market_indexes_save_sends_a_string_array()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("save_settings", "null");
            using var harness = C5bLayerHost.Show();
            Task<List<string>?> task = PluginSettingsCard.EditIndexesDialogAsync(new List<string>());
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            TextBox box = C5bFiles.Descendants(card).OfType<TextBox>().First();
            box.Text = "https://mirror.example/tipcl/index.json\nhttps://mirror2.example/tipcl/index.json";
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("保存")));
            C5bFiles.PumpTask(task);
            Assert.Equal(2, task.Result!.Count);
            string args = api.ParamsOf("save_settings");
            Assert.Contains("\"plugin_market_indexes\"", args, StringComparison.Ordinal);
            // 数组元素是字符串(不是对象、不是数字)
            Assert.Contains("[\"https://mirror.example/tipcl/index.json\",\"https://mirror2.example/tipcl/index.json\"]", args, StringComparison.Ordinal);
        });
    }

    // ============================================================ ④ IPluginHostPage:真实现

    /// <summary>真实现:注册表里的 <c>plugins</c> 页实现该接口 ⇒ <c>ShowTab("dev")</c> 真的切到开发者页签。</summary>
    [Fact]
    public void Real_plugins_page_implements_the_host_interface()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            IReadOnlyDictionary<string, Func<PageBase>> snapshot = PageRegistry.Snapshot();
            try
            {
                PluginsPageRegistration.Register();
                PageBase page = PageRegistry.Create("plugins")!;
                PluginsPage plugins = Assert.IsType<PluginsPage>(page);
                Assert.IsAssignableFrom<IPluginHostPage>(plugins);
                IPluginHostPage host = (IPluginHostPage)plugins;
                Assert.Equal("Installed", plugins.TabName);
                host.ShowTab("dev");
                Assert.Equal("Dev", plugins.TabName);
                host.ShowTab("market");
                Assert.Equal("Market", plugins.TabName);
            }
            finally
            {
                PageRegistry.Restore(snapshot);
            }
        });
    }

    // ============================================================ ④ IPluginHostPage:假实现

    /// <summary>
    /// 假实现:把一个假主页注册成 <c>plugins</c> ⇒ 设置卡上「模型供应商」那个入口
    /// **真的走到接口实现上**(断言假实现收到的参数是 <c>"dev"</c>)。
    /// </summary>
    [Fact]
    public void Settings_card_providers_entry_reaches_the_host_interface()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_settings", "{}");
            IReadOnlyDictionary<string, Func<PageBase>> snapshot = PageRegistry.Snapshot();
            FakeHostPage fake = new FakeHostPage();
            // 【Lead 的口径】这个调用点是**两层 as 串联**(`Window as MainWindow` → `GetPage(...) as IPluginHostPage`)
            // ⇒ 测试必须**显式断言"解析成功"**,否则任一环失败都会让整条调用静默 no-op、而测试看起来只是"没反应"。
            MainWindow shell = new MainWindow(new UiPrefs());
            try
            {
                PageRegistry.Register(PluginsPageRegistration.PageId, () => fake);
                Assert.True(ReferenceEquals(shell, AppServices.Window as MainWindow), "第一环解析失败:AppServices.Window 不是这个 MainWindow(调用点会被 `as` 静默吞掉)");
                Assert.NotNull(shell.GetPage(PluginsPageRegistration.PageId) as IPluginHostPage);
                Border card = PluginSettingsCard.Build();
                Button providers = C5bFiles.Descendants(card).OfType<Button>()
                    .First(b => string.Equals(AutomationProperties.GetName(b), I18n.L("模型供应商"), StringComparison.Ordinal));
                C5bFiles.Click(providers);
                C5bFiles.Pump(() => fake.Tabs.Count > 0);
                Assert.Equal(new[] { "dev" }, fake.Tabs.ToArray());
            }
            finally
            {
                PageRegistry.Restore(snapshot);
                shell.UnhookPlugins();
                shell.Close();
            }
        });
    }
}
