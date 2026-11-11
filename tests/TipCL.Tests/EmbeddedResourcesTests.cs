using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A2 步骤 4-1:嵌入资源清单。
/// 资源按 Windows 版 TipCL.csproj 的 LogicalName 嵌进 <c>TipCL.Core</c> 程序集
/// (CoreResources 读的是自己所在程序集),这里逐个点名断言,防止 glob 少收/多收。
/// </summary>
public class EmbeddedResourcesTests
{
    /// <summary>2 个 locales + 1 个 ai + 12 个 plugin-host = 15。</summary>
    private static readonly string[] Expected =
    {
        "TipCL.Core.locales.en.json",
        "TipCL.Core.locales.zh_CN.json",
        "TipCL.Core.ai.model_rules.json",
        "TipCL.Core.plugin-host.host.mjs",
        "TipCL.Core.plugin-host.host-console.mjs",
        "TipCL.Core.plugin-host.host-loader.mjs",
        "TipCL.Core.plugin-host.host-plugin.mjs",
        "TipCL.Core.plugin-host.host-rpc.mjs",
        "TipCL.Core.plugin-host.host-sessions.mjs",
        "TipCL.Core.plugin-host.host-tools.mjs",
        "TipCL.Core.plugin-host.host-util.mjs",
        "TipCL.Core.plugin-host.host-web.mjs",
        "TipCL.Core.plugin-host.sdk.js",
        "TipCL.Core.plugin-host.sdk-events-worker.js",
        "TipCL.Core.plugin-host.tipcl-plugin.d.ts",
    };

    [Fact]
    public void Manifest_HasExactlyThe15ExpectedCoreResources()
    {
        // 用编译进来的 Core 类型取程序集(CoreResources 自己要等 A3 才能编译)。
        Assembly asm = typeof(PyJson).Assembly;
        Assert.Equal("TipCL.Core", asm.GetName().Name);

        string[] actual = asm.GetManifestResourceNames()
                            .OrderBy(n => n, StringComparer.Ordinal)
                            .ToArray();
        string[] expected = Expected.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(15, actual.Length);
    }

    [Fact]
    public void Manifest_SplitsInto2Locales1AiAnd12PluginHost()
    {
        Assembly asm = typeof(PyJson).Assembly;
        List<string> names = asm.GetManifestResourceNames().ToList();

        Assert.Equal(2, names.Count(n => n.StartsWith("TipCL.Core.locales.", StringComparison.Ordinal)));
        Assert.Equal(1, names.Count(n => n.StartsWith("TipCL.Core.ai.", StringComparison.Ordinal)));
        Assert.Equal(12, names.Count(n => n.StartsWith("TipCL.Core.plugin-host.", StringComparison.Ordinal)));

        // plugin-host 里 9 个 .mjs + 2 个 .js + 1 个 .d.ts
        Assert.Equal(9, names.Count(n => n.StartsWith("TipCL.Core.plugin-host.", StringComparison.Ordinal)
                                        && n.EndsWith(".mjs", StringComparison.Ordinal)));
        Assert.Equal(2, names.Count(n => n.StartsWith("TipCL.Core.plugin-host.", StringComparison.Ordinal)
                                        && n.EndsWith(".js", StringComparison.Ordinal)));
        Assert.Equal(1, names.Count(n => n.StartsWith("TipCL.Core.plugin-host.", StringComparison.Ordinal)
                                        && n.EndsWith(".d.ts", StringComparison.Ordinal)));
    }

    [Fact]
    public void EveryExpectedResourceOpensAsANonEmptyStream()
    {
        Assembly asm = typeof(PyJson).Assembly;

        foreach (string name in Expected)
        {
            using System.IO.Stream? s = asm.GetManifestResourceStream(name);
            Assert.NotNull(s);
            Assert.True(s!.Length > 0, name + " 是空流");
        }
    }
}
