// M3-C1：插件宿主（Core.Plugins.PluginHost）最小契约测试。全部走临时目录，
// 不碰真实 plugins 目录 / plugins.json。
using System;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests.Core;

public class PluginHostTests : IDisposable
{
	private readonly string _home;
	private readonly PluginDirs _dirs;
	private readonly PluginHost _host;

	public PluginHostTests()
	{
		_home = Path.Combine(Path.GetTempPath(), "tipcl-pluginhost-" + Guid.NewGuid().ToString("N"));
		_dirs = new PluginDirs(_home);
		_host = new PluginHost(_dirs, "0.1.0-m3");
	}

	public void Dispose()
	{
		try { Directory.Delete(_home, recursive: true); } catch (Exception) { }
	}

	private string Install(string id, string manifestJson)
	{
		string dir = _dirs.PluginDir(id);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifestJson);
		return dir;
	}

	private const string NodeManifest = "{\"id\":\"hello-node\",\"name\":\"你好 Node\",\"version\":\"1.0.0\",\"runtime\":\"node\",\"main\":\"index.js\"}";
	private const string NodeManifestWithUi = "{\"id\":\"hello-node\",\"name\":\"你好 Node\",\"version\":\"1.0.0\",\"runtime\":\"node\",\"main\":\"index.js\",\"permissions\":[\"ui\"],\"contributes\":{\"pages\":[{\"id\":\"panel\",\"label\":\"面板\",\"url\":\"https://example.com/panel\"}]}}";

	[Fact]
	public void DiscoverReturnsEmptyWhenRootMissing()
	{
		Assert.Empty(_host.Discover());
	}

	[Fact]
	public void DiscoverListsInstalledPluginsAsStopped()
	{
		Install("hello-node", NodeManifest);
		var list = _host.Discover();
		Assert.Single(list);
		Assert.Equal("hello-node", list[0].Id);
		Assert.True(list[0].Enabled);
		Assert.Equal("stopped", list[0].State);
		Assert.Equal("1.0.0", list[0].Manifest.Version);
	}

	[Fact]
	public void DiscoverFlagsBrokenManifestAsError()
	{
		Install("broken", "{ not json");
		var info = _host.Discover().Single((TipCL.Core.Plugins.PluginInstanceInfo i) => i.Id == "broken");
		Assert.Equal("error", info.State);
		Assert.False(string.IsNullOrEmpty(info.Error));
	}

	[Fact]
	public void StartNodePluginFailsWithDegradedError()
	{
		Install("hello-node", NodeManifest);
		var r = _host.Start("hello-node");
		Assert.Equal(false, r.Get("ok"));
		Assert.Contains("Node", Py.Str(r.Get("error")));
		Assert.False(_host.IsRunning("hello-node"));
		// 失败不回滚 enabled 状态与 Windows 语义一致（enabled=true 但进程未起）
		Assert.True(_host.Info("hello-node").Enabled);
	}

	[Fact]
	public void StartUnknownPluginThrows()
	{
		Assert.Throws<PyException>(() => _host.Start("nope"));
		Assert.Throws<PyException>(() => _host.Stop("nope"));
	}

	[Fact]
	public void StopPersistsDisabledAndStateSurvivesReload()
	{
		Install("hello-node", NodeManifest);
		_host.Start("hello-node"); // node 降级失败，但 enabled 已置 true
		var r = _host.Stop("hello-node");
		Assert.Equal(true, r.Get("ok"));

		// 新建宿主（模拟重启进程），disabled 从 plugins.json 读回
		var host2 = new PluginHost(new PluginDirs(_home), "0.1.0-m3");
		var info = host2.Discover().Single((TipCL.Core.Plugins.PluginInstanceInfo i) => i.Id == "hello-node");
		Assert.False(info.Enabled);
		Assert.Equal("disabled", info.State);
	}

	[Fact]
	public void UiContributionsComeFromEnabledManifests()
	{
		Install("hello-node", NodeManifestWithUi);
		Assert.Single(_host.UiContributions());
		var c = _host.UiContributions()[0];
		Assert.Equal("hello-node", Py.Str(c.Get("plugin")));
		Assert.Equal("page", Py.Str(c.Get("kind")));
		Assert.Equal("panel", Py.Str(c.Get("id")));

		_host.Stop("hello-node");
		Assert.Empty(_host.UiContributions());
	}

	[Fact]
	public void HostLogIsAppendedAndReadBack()
	{
		Install("hello-node", NodeManifest);
		_host.AppendLog("hello-node", "第一行");
		_host.AppendLog("hello-node", "第二行");
		string log = _host.ReadLog("hello-node");
		Assert.Contains("第一行", log);
		Assert.Contains("第二行", log);
		Assert.True(File.Exists(_dirs.LogFile("hello-node")));
	}

	[Fact]
	public void DevSourcePluginShowsDevState()
	{
		Install("dev-p", NodeManifest);
		var store = new PluginStateStore(_dirs.StateFile);
		store.Update("dev-p", delegate (PluginRecord r) { r.Source = "dev"; });
		var info = _host.Discover().Single((TipCL.Core.Plugins.PluginInstanceInfo i) => i.Id == "dev-p");
		Assert.Equal("dev", info.State);
	}
}
