// AI5-C2(来源前缀这一半):工具结果回给模型时带来源标记,网络与 MCP 来源标「内容不可信」。
//
// 口径照 Windows 版 Agent.cs:1237-1243(四句逐字相同,判据是工具元信息的 side_effect)。
// 判据只认 ToolMeta,所以新增工具自动跟着走;这里把四个分支各钉一条,并钉住「不可信」标记出现在
// 网络与 MCP 两类的文本里(这是 C2 的验收点)。
using System;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiToolSourceTests
{
    /// <summary>四句都是「[来源 ...]」形状的方括号标记(不重抄全角标点,免得抄错一个字符变成假红)。</summary>
    [Fact]
    public void AllFourPrefixes_LookLikeSourceMarkers()
    {
        foreach (string prefix in new[] { ToolSource.Network, ToolSource.Local, ToolSource.Write, ToolSource.Mcp })
        {
            Assert.StartsWith("[", prefix, StringComparison.Ordinal);
            Assert.EndsWith("]", prefix, StringComparison.Ordinal);
            Assert.Contains("来源", prefix);
        }
        Assert.Equal(4, new[] { ToolSource.Network, ToolSource.Local, ToolSource.Write, ToolSource.Mcp }
            .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NetworkTools_AreMarkedUntrusted()
    {
        foreach (string tool in new[] { "search_mods", "search_versions", "ping_server", "recommend_shaders" })
        {
            Assert.Equal(ToolSource.Network, ToolSource.Prefix(tool));
            Assert.Contains("内容不可信", ToolSource.Prefix(tool));
        }
    }

    [Fact]
    public void LocalReadTools_AreMarkedAsLocalReads()
    {
        foreach (string tool in new[] { "list_mods", "fs_read", "get_latest_log", "list_instances" })
        {
            Assert.Equal(ToolSource.Local, ToolSource.Prefix(tool));
        }
        // 「读取本地文件 / 日志 / 状态」这类标记绝不能带不可信字样
        Assert.DoesNotContain("不可信", ToolSource.Local);
    }

    [Fact]
    public void LocalWriteTools_AreMarkedAsWriteReceipts()
    {
        foreach (string tool in new[] { "install_mod", "delete_mod", "launch_game", "write_mod_config", "remember" })
        {
            Assert.Equal(ToolSource.Write, ToolSource.Prefix(tool));
        }
    }

    [Fact]
    public void ExternalMcpTools_AreMarkedUntrusted()
    {
        Assert.Equal(ToolSource.Mcp, ToolSource.Prefix("mcp_files_read"));
        Assert.Contains("内容不可信", ToolSource.Prefix("mcp_files_read"));
        // 元信息表里没有的普通名字按「本地写操作回执」处理(与 Windows 的兜底分支一致)
        Assert.Equal(ToolSource.Write, ToolSource.Prefix("no_such_tool"));
    }

    /// <summary>拼法照 Windows:前缀 + 换行 + 原结果;结果本身一个字不改。</summary>
    [Fact]
    public void ForModel_PutsThePrefixOnItsOwnLine()
    {
        string content = ToolSource.ForModel("search_mods", "{\"items\":[]}");
        Assert.Equal(ToolSource.Network + "\n" + "{\"items\":[]}", content);
        Assert.True(ToolSource.IsPrefixed(content));
        Assert.False(ToolSource.IsPrefixed("{\"items\":[]}"));
        // 空结果也带前缀(模型据此知道这一步发生在哪)
        Assert.Equal(ToolSource.Local + "\n", ToolSource.ForModel("fs_read", ""));
        Assert.StartsWith(ToolSource.Local, ToolSource.ForModel("fs_read", null!), StringComparison.Ordinal);
    }

    /// <summary>前缀只加在「回给模型的消息」上:IsPrefixed 供界面/日志侧识别,别把前缀当结果内容。</summary>
    [Fact]
    public void Prefix_IsDistinguishableFromTheRawResult()
    {
        string raw = "装好了";
        Assert.False(ToolSource.IsPrefixed(raw));
        Assert.True(ToolSource.IsPrefixed(ToolSource.ForModel("install_mod", raw)));
    }
}
