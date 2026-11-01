using System.Linq;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

public class PluginMarketTests
{
	[Fact]
	public void DefaultIndexUrls_AllHttpsDistinctAndOfficialFirst()
	{
		Assert.Equal(PluginMarket.OfficialIndexUrl, PluginMarket.DefaultIndexUrls[0]);
		Assert.Equal(PluginMarket.DefaultIndexUrls.Length, PluginMarket.DefaultIndexUrls.Distinct().Count());
		Assert.All(PluginMarket.DefaultIndexUrls, u => Assert.StartsWith("https://", u));
		Assert.All(PluginMarket.DefaultIndexUrls, u => Assert.EndsWith("/index.json", u));
	}

	[Fact]
	public void DownloadCandidates_NonGithub_StaysSingle()
	{
		var list = PluginMarket.DownloadCandidates("https://example.com/a.zip");
		Assert.Single(list);
		Assert.Equal("https://example.com/a.zip", list[0]);
	}

	[Fact]
	public void DownloadCandidates_Github_OriginalFirstThenMirrors()
	{
		const string url = "https://github.com/wfwwq/TipCL-market/releases/download/x/y.zip";
		var list = PluginMarket.DownloadCandidates(url);
		Assert.Equal(1 + PluginMarket.GhDownloadMirrors.Length, list.Count);
		Assert.Equal(url, list[0]);
		foreach (var mirror in PluginMarket.GhDownloadMirrors)
		{
			Assert.Contains(mirror + url, list);
		}
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
