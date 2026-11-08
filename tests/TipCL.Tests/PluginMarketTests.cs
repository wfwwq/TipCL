using System.Linq;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

public class PluginMarketTests
{
	[Fact]
	public void DefaultIndexUrls_AllHttpsDistinctAndOfficialPresent()
	{
		Assert.Equal(PluginMarket.BuiltinMirrors.Length, PluginMarket.DefaultIndexUrls.Length);
		Assert.Equal(PluginMarket.DefaultIndexUrls.Length, PluginMarket.DefaultIndexUrls.Distinct().Count());
		Assert.All(PluginMarket.DefaultIndexUrls, u => Assert.StartsWith("https://", u));
		Assert.All(PluginMarket.DefaultIndexUrls, u => Assert.EndsWith("/index.json", u));
		Assert.Contains(PluginMarket.OfficialIndexUrl, PluginMarket.DefaultIndexUrls);
	}

	[Fact]
	public void DownloadCandidates_NonGithub_OnlyOfficialDirect()
	{
		var list = PluginMarket.DownloadCandidates("https://example.com/a.zip", PluginMarket.EnabledMirrors(null));
		Assert.Single(list);
		Assert.Equal("https://example.com/a.zip", list[0]);
	}

	[Fact]
	public void DownloadCandidates_FollowsMirrorPriority()
	{
		const string url = "https://github.com/wfwwq/TipCL-market/releases/download/x/y.zip";
		// 用户把镜像设为（只开两个、镜像优先于官方）→ 候选按该顺序，官方直链排在镜像后
		var mirrors = PluginMarket.EnabledMirrors(new object[] { "gh-proxy", "github" });
		var list = PluginMarket.DownloadCandidates(url, mirrors);
		Assert.Equal(new[]
		{
			"https://gh-proxy.com/" + url,
			url,
		}, list);
	}

	[Fact]
	public void EnabledMirrors_NullConfig_EmptyButFullListIsBuiltinOrder()
	{
		// null = 没有任何镜像（Config 层的「缺失→默认全开」三态在 EnabledMirrors() 里处理）
		Assert.Empty(PluginMarket.EnabledMirrors(null));
		var full = PluginMarket.EnabledMirrors(PluginMarket.BuiltinMirrors.Select(m => (object)m.Id));
		Assert.Equal(PluginMarket.BuiltinMirrors.Select(m => m.Id), full.Select(m => m.Id));
	}

	[Fact]
	public void EnabledMirrors_UnknownIdsDropped_StrictList()
	{
		// 不在列表里 = 关闭（否则用户没法禁用镜像）；未知 id 丢弃
		var list = PluginMarket.EnabledMirrors(new object[] { "nope", "cdn-gh-proxy", "github" });
		Assert.Equal(new[] { "cdn-gh-proxy", "github" }, list.Select(m => m.Id));
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
