using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

public class PluginMarketTests
{
	[Fact]
	public void DefaultIndexUrls_AllHttpsDistinctAndOfficialPresent()
	{
		Assert.Equal(6, PluginMarket.DefaultIndexUrls.Length);
		Assert.Equal(PluginMarket.DefaultIndexUrls.Length, PluginMarket.DefaultIndexUrls.Distinct().Count());
		Assert.All(PluginMarket.DefaultIndexUrls, u => Assert.StartsWith("https://", u));
		Assert.All(PluginMarket.DefaultIndexUrls, u => Assert.EndsWith("/index.json", u));
		Assert.Contains(PluginMarket.OfficialIndexUrl, PluginMarket.DefaultIndexUrls);
	}

	[Fact]
	public void DownloadCandidates_NonGithub_OnlySingle()
	{
		var list = PluginMarket.DownloadCandidates("https://example.com/a.zip", PluginMarket.DefaultMirrors());
		Assert.Single(list);
		Assert.Equal("https://example.com/a.zip", list[0]);
	}

	[Fact]
	public void DownloadCandidates_FollowsMirrorPriority()
	{
		const string url = "https://github.com/wfwwq/TipCL-market/releases/download/x/y.zip";
		const string path = "/wfwwq/TipCL-market/releases/download/x/y.zip";
		// 默认镜像列表：候选按 priority 1..6（gitproxy → ghproxy.vip → 官方 → gh-proxy → v6 → cdn），
		// 官方源也是普通条目（前缀 + 相对路径）
		var list = PluginMarket.DownloadCandidates(url, PluginMarket.DefaultMirrors());
		Assert.Equal(new[]
		{
			"https://gitproxy.mrhjx.cn/https://github.com" + path,
			"https://ghproxy.vip/https://github.com" + path,
			"https://github.com" + path,
			"https://gh-proxy.com/https://github.com" + path,
			"https://v6.gh-proxy.org/https://github.com" + path,
			"https://cdn.gh-proxy.com/https://github.com" + path,
		}, list);
	}

	[Fact]
	public void EnabledMirrors_DisabledFilteredAndPrioritySorted()
	{
		// 关掉 GitHub 官方 + 调乱 priority → 输出只含启用的、按 priority 升序
		var stored = new List<object>
		{
			PyDict.Of("id", "github", "name", "GitHub 官方源", "raw_prefix", "https://raw.githubusercontent.com", "download_prefix", "https://github.com", "enabled", false, "priority", 1L),
			PyDict.Of("id", "cdn-gh-proxy", "name", "cdn 镜像", "raw_prefix", "https://cdn.gh-proxy.com/https://raw.githubusercontent.com", "download_prefix", "https://cdn.gh-proxy.com/https://github.com", "enabled", true, "priority", 5L),
			PyDict.Of("id", "ghproxy-vip", "name", "ghproxy.vip 镜像", "raw_prefix", "https://ghproxy.vip/https://raw.githubusercontent.com", "download_prefix", "https://ghproxy.vip/https://github.com", "enabled", true, "priority", 2L),
		};
		var list = PluginMarket.EnabledMirrors(stored);
		Assert.Equal(new[] { "ghproxy-vip", "cdn-gh-proxy" }, list.Select(m => m.Id));
	}

	[Fact]
	public void EnabledMirrors_NullConfig_FallsBackToDefaults()
	{
		var list = PluginMarket.EnabledMirrors(null);
		Assert.Equal(6, list.Count);
		Assert.Equal(new[] { "gitproxy-mrhjx", "ghproxy-vip", "github", "gh-proxy", "v6-gh-proxy", "cdn-gh-proxy" }, list.Select(m => m.Id));
		Assert.All(list, m => Assert.True(m.Enabled));
	}

	[Fact]
	public void EnabledMirrors_EmptyConfig_ParsesToEmpty_FallbackInConfigOverload()
	{
		// 纯函数语义：空列表解析出空（「空→回落默认」在读取配置的 EnabledMirrors() 路径上处理）
		Assert.Empty(PluginMarket.EnabledMirrors(new List<object>()));
	}

	[Fact]
	public void MergeIndexes_DedupesByIdAcrossMirrors()
	{
		var entry = PyDict.Of(
			"id", "hello-plugin",
			"version", "0.1.0",
			"download", PyDict.Of("url", "https://example.com/p.zip", "sha256", new string('a', 64)));
		var index = PyDict.Of("schema", 1L, "name", "m", "plugins", new object[] { entry });
		// 同一索引经多个镜像地址重复出现，按 id 合并后只能剩一条
		var merged = PluginMarket.MergeIndexes(new (string, PyDict)[] { ("u1", index), ("u2", index) });
		var ids = merged.Select(m => Py.S(m.Get("id"))).ToArray();
		Assert.Equal(new[] { "hello-plugin" }, ids);
		Assert.Equal("m", Py.S(merged[0].Get("marketName")));
	}
}

public class PluginMarketMirrorExtraTests
{
	[Fact]
	public void IndexUrls_BuiltFromRawPrefixAndRepoPath()
	{
		var urls = PluginMarket.DefaultMirrors().Select(m => m.RawPrefix + PluginMarket.IndexRepoPath).ToList();
		Assert.Equal(6, urls.Count);
		Assert.All(urls, u => Assert.EndsWith("/wfwwq/TipCL-market/main/index.json", u));
		Assert.Contains("https://gitproxy.mrhjx.cn/https://raw.githubusercontent.com/wfwwq/TipCL-market/main/index.json", urls);
		Assert.Contains("https://raw.githubusercontent.com/wfwwq/TipCL-market/main/index.json", urls);
	}

	[Fact]
	public void DownloadCandidates_CustomMirrorParticipates()
	{
		var mirrors = new List<PluginMarket.GhMirror>
		{
			new() { Id = "custom-x", Label = "我的镜像", RawPrefix = "https://gh.example.com/https://raw.githubusercontent.com", DownloadPrefix = "https://gh.example.com/https://github.com", Enabled = true, Priority = 1 },
			new() { Id = "github", Label = "GitHub 官方源", RawPrefix = "https://raw.githubusercontent.com", DownloadPrefix = "https://github.com", Enabled = true, Priority = 2 },
		};
		var list = PluginMarket.DownloadCandidates("https://github.com/a/b/releases/download/v/p.zip", mirrors);
		Assert.Equal(new[]
		{
			"https://gh.example.com/https://github.com/a/b/releases/download/v/p.zip",
			"https://github.com/a/b/releases/download/v/p.zip",
		}, list);
	}

	[Fact]
	public void DownloadCandidates_DisabledMirrorExcluded()
	{
		var mirrors = PluginMarket.EnabledMirrors(new List<object>
		{
			PyDict.Of("id", "github", "name", "GitHub 官方源", "raw_prefix", "https://raw.githubusercontent.com", "download_prefix", "https://github.com", "enabled", true, "priority", 1L),
			PyDict.Of("id", "gh-proxy", "name", "gh-proxy.com 镜像", "raw_prefix", "https://gh-proxy.com/https://raw.githubusercontent.com", "download_prefix", "https://gh-proxy.com/https://github.com", "enabled", false, "priority", 2L),
		});
		var list = PluginMarket.DownloadCandidates("https://github.com/a/b/releases/download/v/p.zip", mirrors);
		Assert.Single(list);
		Assert.Equal("https://github.com/a/b/releases/download/v/p.zip", list[0]);
	}

	[Fact]
	public void EnabledMirrors_InvalidPrefixDropped()
	{
		// 前缀不是 https:// 的条目丢弃，不能让拼接出 http:// 明文或坏地址
		var mirrors = PluginMarket.EnabledMirrors(new List<object>
		{
			PyDict.Of("id", "bad", "name", "坏镜像", "raw_prefix", "http://evil.example.com", "download_prefix", "http://evil.example.com", "enabled", true, "priority", 1L),
			PyDict.Of("id", "github", "name", "GitHub 官方源", "raw_prefix", "https://raw.githubusercontent.com", "download_prefix", "https://github.com", "enabled", true, "priority", 2L),
		});
		Assert.Equal(new[] { "github" }, mirrors.Select(m => m.Id));
	}
}
