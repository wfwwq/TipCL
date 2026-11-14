using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C7b(批3):**动态页 id 的受控回退口** `PageRegistry.FallbackFactory` 的口径。
///
/// Windows 那边是外壳直接认前缀(`MainWindow.CreatePluginPage(id)`);mac 把这一步抽成受控回退口,
/// 所以这条用例要断出**两种 id 的区别**:
/// <list type="bullet">
/// <item><c>plugin:&lt;plugin&gt;:&lt;id&gt;</c> —— 回退口命中 ⇒ 建成插件页;</item>
/// <item>未注册、也不是 <c>plugin:</c> 的 id —— 回退口返回 null ⇒ <b>仍然静默不动</b>:
///   <c>Create</c> 给 null、<c>Contains</c> 仍 false、<c>Ids</c> 不变、不抛。</item>
/// </list>
/// 纪律:回退口是**进程级静态**,本条用例自己在 <c>finally</c> 里写回进入前的值(D-149 那族)。
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

    /// <summary>命中:未注册的 `plugin:` id 走回退口建成页面。</summary>
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
                // 回退口**不写回**注册表:Ids 与 Contains 的语义不变
                Assert.False(PageRegistry.Contains("plugin:demo:board"));
                Assert.DoesNotContain("plugin:demo:board", PageRegistry.Ids);
            }
            finally
            {
                PageRegistry.FallbackFactory = original;
            }
        });
    }

    /// <summary>
    /// 未注册、也不是 `plugin:` 的 id ⇒ **仍然静默不动**:回退口被问到但返回 null,
    /// `Create` 给 null、`Contains` 仍 false、`Ids` 不变、**不抛**。
    /// </summary>
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
                PageBase? page = PageRegistry.Create("some-unknown-id");
                Assert.Null(page);
                Assert.False(PageRegistry.Contains("some-unknown-id"));
                Assert.Equal(idsBefore, PageRegistry.Ids.Count);
                // 回退口确实被问到过(不是"外壳根本没问" —— 区别要断出来)
                Assert.Contains("some-unknown-id", asked);
            }
            finally
            {
                PageRegistry.FallbackFactory = original;
            }
        });
    }

    /// <summary>已注册的 id 一律走注册表,回退口**不参与**(受控:回退口不能覆盖注册的页面)。</summary>
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
                PageBase? page = PageRegistry.Create("c7b-probe");
                Assert.NotNull(page);
                Assert.Equal(0, called);
            }
            finally
            {
                PageRegistry.FallbackFactory = original;
                PageRegistry.Unregister("c7b-probe");
            }
        });
    }
}
