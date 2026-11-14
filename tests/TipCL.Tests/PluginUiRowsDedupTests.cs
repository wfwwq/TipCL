using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// <c>plugins_ui</c> 行的去重规则(<see cref="PluginManager.DedupNativeFirst"/>)。
///
/// 为什么单独钉:manifest 的 <c>contributes.&lt;kind&gt;</c> 与 .NET 插件的运行时 <c>ctx.Ui.Register*</c>
/// 会为**同一个入口**各产生一行(前者 <c>native:false</c>、后者 <c>native:true</c>),而前端
/// <c>PluginUi.Find</c> 是 <c>FirstOrDefault</c> —— 命中非 native 那一行就会去等 WebView 地址
/// (<c>url:""</c>),原生控件永远渲染不出来。运行时 E2E
/// (<c>tests\TipCL.NativeBlockE2E</c>,不进 TipCL.sln)拍到过这个现场,这条用例是它在 437 套件里的守卫。
/// </summary>
public class PluginUiRowsDedupTests
{
	private static PyDict Row(string plugin, string kind, string id, bool native, string label, long order = 100L)
	{
		return PyDict.Of(
			"plugin", plugin,
			"kind", kind,
			"id", id,
			"label", label,
			"icon", "",
			"order", order,
			"url", native ? "" : "https://example.invalid/" + id,
			"spec", PyDict.Of("id", id, "view", plugin),
			"native", native,
			"key", "plugin:" + plugin + "/" + id);
	}

	/// <summary>manifest 声明行(native:false、有 url)按 UiRows 的形状造一份。</summary>
	private static PyDict Manifest(string kind, string id, string plugin = "demo-plugin", long order = 100L)
	{
		return Row(plugin, kind, id, native: false, label: "manifest 行", order: order);
	}

	/// <summary>原生注册表行(native:true、url 空)按 NativeUiRows 的形状造一份。</summary>
	private static PyDict Native(string kind, string id, string plugin = "demo-plugin", long order = 100L)
	{
		return Row(plugin, kind, id, native: true, label: "原生行", order: order);
	}

	private static string[] Keys(IEnumerable<PyDict> rows)
	{
		return rows.Select((PyDict r) => Py.S(r["kind"]) + ":" + Py.S(r["id"]) + ":" + (Py.Truthy(r["native"]) ? "native" : "manifest")).ToArray();
	}

	[Fact]
	public void SameKindAndIdCollapsesToOneNativeRow()
	{
		List<PyDict> rows = PluginManager.DedupNativeFirst(new List<PyDict>
		{
			Manifest("block", "hello"),
			Native("block", "hello")
		});

		PyDict only = Assert.Single(rows);
		Assert.True(Py.Truthy(only["native"]));
		Assert.Equal("", Py.S(only["url"]));
		Assert.Equal("原生行", Py.S(only["label"]));
		// 位置留在首次出现处(前端按这个顺序建列表,不该因为去重把块挪到末尾)
		Assert.Equal(new string[1] { "block:hello:native" }, Keys(rows));
	}

	[Fact]
	public void NativeRowKeepsThePositionOfTheFirstOccurrence()
	{
		List<PyDict> rows = PluginManager.DedupNativeFirst(new List<PyDict>
		{
			Manifest("page", "p1"),
			Manifest("block", "hello"),
			Native("block", "hello"),
			Manifest("block", "zzz")
		});

		Assert.Equal(new string[3] { "page:p1:manifest", "block:hello:native", "block:zzz:manifest" }, Keys(rows));
	}

	[Fact]
	public void NativeAlreadyFirstStaysAsIs()
	{
		PyDict native = Native("block", "hello");
		List<PyDict> rows = PluginManager.DedupNativeFirst(new List<PyDict>
		{
			native,
			Manifest("block", "hello")
		});

		Assert.Same(native, Assert.Single(rows));
	}

	[Fact]
	public void DifferentPluginsKindsOrIdsAreNeverMerged()
	{
		List<PyDict> rows = PluginManager.DedupNativeFirst(new List<PyDict>
		{
			Manifest("page", "hello"),
			Native("block", "hello"),
			Native("block", "other"),
			Manifest("block", "other"),
			Manifest("block", "hello", "another-plugin")
		});

		Assert.Equal(new string[4]
		{
			"page:hello:manifest",
			"block:hello:native",
			"block:other:native",
			"block:hello:manifest"
		}, Keys(rows));
		// 最后一条是**另一个插件**的同名块:它必须活着 —— 只按 (kind,id) 去重会把它吞掉,
		// 而前端查的是 (plugin, kind, id)、行的身份是 key="plugin:<plugin>/<id>"。
		Assert.Equal("another-plugin", Py.S(rows[3]["plugin"]));
		Assert.Equal("plugin:another-plugin/hello", Py.S(rows[3]["key"]));
	}

	[Fact]
	public void AManifestOnlyRowIsLeftAlone()
	{
		// 纯 Node 插件(没有原生注册)不受影响:仍是 native:false、仍带 url
		List<PyDict> rows = PluginManager.DedupNativeFirst(new List<PyDict> { Manifest("page", "p1") });

		PyDict only = Assert.Single(rows);
		Assert.False(Py.Truthy(only["native"]));
		Assert.Equal("https://example.invalid/p1", Py.S(only["url"]));
	}

	[Fact]
	public void TwoNativeRowsForTheSameKeyKeepTheFirst()
	{
		PyDict first = Native("block", "hello");
		PyDict second = Native("block", "hello");
		List<PyDict> rows = PluginManager.DedupNativeFirst(new List<PyDict> { first, second });

		Assert.Same(first, Assert.Single(rows));
	}

	[Fact]
	public void EmptyInputStaysEmpty()
	{
		Assert.Empty(PluginManager.DedupNativeFirst(new List<PyDict>()));
	}
}
