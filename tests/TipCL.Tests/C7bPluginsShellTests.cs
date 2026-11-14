using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TipCL;
using TipCL.Pages;
using TipCL.Pages.Plugins;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C7b(批3):动态页 id 的**受控回退口**口径 + 外壳侧的两条实质行为(都用**假后端驱动 `plugins_ui` 真数据路径**)。
///
/// 纪律:回退口与 `PluginUi.Changed` 都是**进程级静态** ⇒ 每条用例在 <c>finally</c> 里
/// <c>UnhookPlugins()</c>(退订 + 还原回退口),不把订阅留给下一个用例(D-149 那族)。
/// </summary>
public class C7bPluginsShellTests
{
    /// <summary>测试用的最小页(只做 PageBase 的形状,不碰后端)。</summary>
    private sealed class ProbePage : PageBase
    {
        public override string Title => "探针插件页";
    }

    /// <summary>回退口只对 <c>plugin:</c> 前缀作答;别的 id 一律 null(与 Windows 的外壳实现同形)。</summary>
    private static PageBase? PluginPrefixFallback(string id)
    {
        return id.StartsWith("plugin:", StringComparison.Ordinal) ? new ProbePage { Id = id } : null;
    }

    /// <summary>造一份"两个插件页贡献"的假后端答复。</summary>
    private static string TwoPages()
    {
        return "[{\"plugin\":\"demo\",\"kind\":\"page\",\"id\":\"board\",\"label\":\"看板\"}," +
               "{\"plugin\":\"demo\",\"kind\":\"page\",\"id\":\"other\",\"label\":\"另一个\"}]";
    }

    /// <summary>外壳 + 假后端:窗口不 Show(导航/缓存不需要窗口生命周期),收尾一律 <c>UnhookPlugins</c> + <c>Close</c>。</summary>
    private static MainWindow NewShell()
    {
        MainWindow window = new MainWindow(new UiPrefs());
        window.HookPlugins();
        return window;
    }

    // ================================================================ 回退口口径

    /// <summary>命中:未注册的 `plugin:` id 走回退口建成页面;回退口**不写回**注册表。</summary>
    [Fact]
    public void Fallback_factory_builds_a_page_for_an_unregistered_plugin_id()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            Func<string, PageBase?>? original = PageRegistry.FallbackFactory;
            try
            {
                PageRegistry.FallbackFactory = PluginPrefixFallback;
                PageBase? page = PageRegistry.Create("plugin:demo:board");
                Assert.NotNull(page);
                Assert.Equal("plugin:demo:board", page!.Id);
                Assert.False(PageRegistry.Contains("plugin:demo:board"));
                Assert.DoesNotContain("plugin:demo:board", PageRegistry.Ids);
            }
            finally
            {
                PageRegistry.FallbackFactory = original;
            }
        });
    }

    /// <summary>未注册、也不是 `plugin:` 的 id ⇒ **仍然静默不动**,而且回退口**确实被问到过**(排除假绿)。</summary>
    [Fact]
    public void Unregistered_non_plugin_ids_stay_silent()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            Func<string, PageBase?>? original = PageRegistry.FallbackFactory;
            List<string> asked = new List<string>();
            try
            {
                PageRegistry.FallbackFactory = id =>
                {
                    asked.Add(id);
                    return PluginPrefixFallback(id);
                };
                int idsBefore = PageRegistry.Ids.Count;
                Assert.Null(PageRegistry.Create("some-unknown-id"));
                Assert.False(PageRegistry.Contains("some-unknown-id"));
                Assert.Equal(idsBefore, PageRegistry.Ids.Count);
                Assert.Contains("some-unknown-id", asked);
            }
            finally
            {
                PageRegistry.FallbackFactory = original;
            }
        });
    }

    /// <summary>已注册的 id 一律走注册表,回退口**不参与**(受控)。</summary>
    [Fact]
    public void Registered_ids_never_go_through_the_fallback()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            Func<string, PageBase?>? original = PageRegistry.FallbackFactory;
            int called = 0;
            try
            {
                PageRegistry.Register("c7b-probe", () => new ProbePage { Id = "c7b-probe" });
                PageRegistry.FallbackFactory = id =>
                {
                    called++;
                    return new ProbePage { Id = id };
                };
                Assert.NotNull(PageRegistry.Create("c7b-probe"));
                Assert.Equal(0, called);
            }
            finally
            {
                PageRegistry.FallbackFactory = original;
                PageRegistry.Unregister("c7b-probe");
            }
        });
    }

    // ================================================================ 外壳行为(假后端驱动 plugins_ui)

    /// <summary>
    /// `plugin:` 命中回退口 ⇒ **页面真的换上**;未注册且非 `plugin:` 的 id ⇒ <c>Navigate</c> 给 false、
    /// 当前页与缓存都不变、不抛。
    /// </summary>
    [Fact]
    public void Plugin_page_id_swaps_the_page_and_unknown_ids_do_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("plugins_ui", "[{\"plugin\":\"demo\",\"kind\":\"page\",\"id\":\"board\",\"label\":\"看板\"}]")
                .Route("plugins_list", "[]");
            MainWindow window = NewShell();
            try
            {
                window.LoadPluginsAfterConnect();
                C5bFiles.Pump(() => PluginUi.FindPage("plugin:demo:board") != null);
                // 动态 id:真的切过去(mac 的 MainWindow.Navigate 返回 void ⇒ 判据是"页面被建出来且是同一实例")
                window.Navigate("plugin:demo:board");
                PluginHostPage board = Assert.IsType<PluginHostPage>(window.GetPage("plugin:demo:board"));
                Assert.Equal("plugin:demo:board", board.Id);
                // 未注册且非 plugin:静默不动 —— 不建页、不抛,当前页那页还在缓存里且是同一个实例
                window.Navigate("some-unknown-id");
                Assert.Null(window.GetPage("some-unknown-id"));
                Assert.Same(board, window.GetPage("plugin:demo:board"));
            }
            finally
            {
                window.UnhookPlugins();
                window.Close();
            }
        });
    }

    /// <summary>
    /// 界面贡献变了(全部消失)⇒ 每个已打开的插件页 <c>MarkGone()</c>;**非当前页从缓存摘掉**,
    /// **当前页留在缓存里**并换成"已经不可用"那一屏(Windows 的 `_currentId != kv.Key` 判据)。
    /// </summary>
    [Fact]
    public void Plugin_ui_changed_marks_gone_removes_non_current_pages_and_keeps_the_current_one()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("plugins_ui", TwoPages()).Route("plugins_list", "[]");
            MainWindow window = NewShell();
            try
            {
                window.LoadPluginsAfterConnect();
                C5bFiles.Pump(() => PluginUi.FindPage("plugin:demo:other") != null);
                // 两页都建出来,最后让 board 当当前页 ⇒ other 变"非当前页但已在缓存里"
                window.Navigate("plugin:demo:board");
                window.Navigate("plugin:demo:other");
                window.Navigate("plugin:demo:board");
                PluginHostPage board = (PluginHostPage)window.GetPage("plugin:demo:board")!;
                Assert.IsType<PluginHostPage>(window.GetPage("plugin:demo:other"));

                // 贡献全没了 ⇒ 再拉一次(Changed → 外壳的 OnPluginUiChanged)
                api.Route("plugins_ui", "[]");
                window.LoadPluginsAfterConnect();
                C5bFiles.Pump(() => C5bFiles.HasText(board, I18n.L("这个插件页面已经不可用")));

                // 当前页(board):留在缓存里、换成不可用那一屏
                Assert.Same(board, window.GetPage("plugin:demo:board"));
                Assert.True(C5bFiles.HasText(board, I18n.L("插件可能被禁用、卸载或正在重新加载。")));
                // 非当前页(other):从缓存摘掉 ⇒ 再取就没人给它了(回退口对"贡献已不在"的 id 返回 null)
                Assert.Null(window.GetPage("plugin:demo:other"));
            }
            finally
            {
                window.UnhookPlugins();
                window.Close();
            }
        });
    }
}
