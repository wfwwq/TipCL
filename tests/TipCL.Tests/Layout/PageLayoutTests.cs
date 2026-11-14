// 来源: Windows 版参考仓库 tests/TipCL.Tests/PageLayoutTests.cs（M2-B4 随 Core/Layout 模型层移植，语义逐条对齐）。
// 页面布局自定义（WP-U1）纯模型层测试：BlockState / PageLayoutDoc / PageLayoutStore。
// Store 全程走 DirOverride 临时目录，不动真实数据目录。
using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core.Layout;
using Xunit;

public class PageLayoutTests
{
	// ————— PageLayoutDoc.Default / Normalize —————

	[Fact]
	public void DefaultKeepsIdOrderAndMarksNonDefaultAsHidden()
	{
		var doc = PageLayoutDoc.Default(new[] { "a", "b", "c" }, new[] { "c", "a" });
		Assert.Equal(new[] { "a", "b", "c" }, Ids(doc));
		Assert.False(doc.Blocks[0].Hidden);
		Assert.True(doc.Blocks[1].Hidden);
		Assert.False(doc.Blocks[2].Hidden);
		Assert.Equal(PageLayoutDoc.CurrentVersion, doc.Version);
	}

	[Fact]
	public void NormalizeDropsUnknownIdsAndAppendsMissingAtTail()
	{
		var doc = PageLayoutDoc.Default(new[] { "a", "b", "c" }, new[] { "a", "b", "c" });
		// b 被用户隐藏、c 排到最前；随后页面把 b 删掉、新增 d
		doc.Blocks[1].Hidden = true;
		var c = doc.Blocks[2];
		doc.Blocks.RemoveAt(2);
		doc.Blocks.Insert(0, c);
		doc.Normalize(new[] { "a", "c", "d" }, new[] { "a" });
		Assert.Equal(new[] { "c", "a", "d" }, Ids(doc));
		// 已有项的用户选择不动；缺失项 d 不在 defaultVisible 里 → 补隐藏
		Assert.False(doc.Blocks[0].Hidden);
		Assert.True(doc.Blocks[2].Hidden);
	}

	[Fact]
	public void NormalizePreservesExistingHiddenFlag()
	{
		var doc = PageLayoutDoc.Default(new[] { "a", "b" }, new[] { "a", "b" });
		doc.Blocks[0].Hidden = true;
		doc.Normalize(new[] { "a", "b" }, new[] { "a", "b" });
		Assert.True(doc.Blocks[0].Hidden);
		Assert.False(doc.Blocks[1].Hidden);
	}

	[Fact]
	public void NormalizeIgnoresEmptyAndNullIds()
	{
		var doc = PageLayoutDoc.Default(new[] { "a", "", null! }, new[] { "a" });
		Assert.Single(doc.Blocks);
		Assert.Equal("a", doc.Blocks[0].Id);
		// 空集合也不炸
		doc.Normalize(new string[0], new string[0]);
		Assert.Empty(doc.Blocks);
	}

	// ————— FromJson 语义（经 ParseImport 的字符串入口） —————

	// 把单页文档包进导出信封，方便走 ParseImport 覆盖 FromJson 行为
	private static string Envelope(string pageJson)
	{
		return "{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{\"p\":" + pageJson + "}}";
	}

	[Fact]
	public void FromJsonParsesHiddenAndDedupsRepeatedIds()
	{
		// 存储层白名单 = 文档自带块集（ParseLoose），未知块的真正过滤交给页面 Normalize；
		// 重复 id 只认第一次
		var pages = PageLayoutStore.ParseImport(Envelope(
			"{\"version\":1,\"blocks\":[{\"id\":\"b\",\"hidden\":true},{\"id\":\"a\",\"hidden\":false},{\"id\":\"b\",\"hidden\":false}]}"));
		var doc = pages["p"];
		Assert.Equal(new[] { "b", "a" }, Ids(doc));
		Assert.True(doc.Blocks[0].Hidden);
		Assert.False(doc.Blocks[1].Hidden);
	}

	[Fact]
	public void FromJsonTreatsNonBoolHiddenAsVisible()
	{
		var pages = PageLayoutStore.ParseImport(Envelope(
			"{\"version\":1,\"blocks\":[{\"id\":\"a\",\"hidden\":\"yes\"},{\"id\":\"b\",\"hidden\":true}]}"));
		var doc = pages["p"];
		Assert.False(doc.Blocks[0].Hidden);
		Assert.True(doc.Blocks[1].Hidden);
	}

	[Fact]
	public void FromJsonRejectsWrongVersionAndBadShape()
	{
		// version != 1 的单页被跳过（FromJson 返回 null）；根不是对象同理
		Assert.Empty(PageLayoutStore.ParseImport(Envelope("{\"version\":2,\"blocks\":[{\"id\":\"a\"}]}")));
		Assert.Empty(PageLayoutStore.ParseImport(Envelope("{\"blocks\":[{\"id\":\"a\"}]}")));
		Assert.Empty(PageLayoutStore.ParseImport(Envelope("5")));
		Assert.Empty(PageLayoutStore.ParseImport(Envelope("\"x\"")));
	}

	[Fact]
	public void FromJsonKeepsVersionOnePageWithoutBlocks()
	{
		// 合法信封里 blocks 缺失/空数组 = 全默认（ParseLoose 白名单为空）
		var pages = PageLayoutStore.ParseImport(Envelope("{\"version\":1}"));
		var doc = pages["p"];
		Assert.Empty(doc.Blocks);
	}

	// ————— ToDict / ToJson 往返（经 ParseImport 读回） —————

	[Fact]
	public void ToJsonRoundTripKeepsOrderAndHidden()
	{
		var doc = PageLayoutDoc.Default(new[] { "a", "b", "c" }, new[] { "a" });
		doc.Blocks[1].Hidden = true;
		// ToJson 中文不转义（UnsafeRelaxedJsonEscaping），可直接喂回 ParseImport
		string json = PageLayoutStore.ExportJson(new[]
		{
			new KeyValuePair<string, PageLayoutDoc>("home", doc)
		});
		Assert.Contains("\"kind\": \"tipcl-page-layout\"", json);
		Assert.Contains("\"version\": 1", json);
		var back = PageLayoutStore.ParseImport(json)["home"];
		Assert.Equal(Ids(doc), Ids(back));
		Assert.Equal(Hiddens(doc), Hiddens(back));
	}

	// ————— PageLayoutStore —————

	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_pagelayout_tests_" + Guid.NewGuid().ToString("N"));

	private void WithTempDir()
	{
		PageLayoutStore.DirOverride = _tmp;
	}

	private void ResetDir()
	{
		PageLayoutStore.DirOverride = null;
		try { Directory.Delete(_tmp, recursive: true); } catch { }
	}

	[Fact]
	public void SaveThenLoadRoundTripsViaTempDir()
	{
		WithTempDir();
		try
		{
			Assert.Null(PageLayoutStore.Load("home"));
			Assert.Empty(PageLayoutStore.LoadAll());
			var doc = PageLayoutDoc.Default(new[] { "a", "b" }, new[] { "a" });
			PageLayoutStore.Save("home", doc);
			var loaded = PageLayoutStore.Load("home");
			Assert.NotNull(loaded);
			Assert.Equal(new[] { "a", "b" }, Ids(loaded));
			Assert.True(loaded.Blocks[1].Hidden);
			Assert.Null(PageLayoutStore.Load("other"));
			Assert.Single(PageLayoutStore.LoadAll());
		}
		finally { ResetDir(); }
	}

	[Fact]
	public void CorruptFileYieldsNullLoadAndEmptyLoadAll()
	{
		WithTempDir();
		try
		{
			Directory.CreateDirectory(PageLayoutStore.DirPath);
			File.WriteAllText(PageLayoutStore.FilePath, "{ not valid json !!!");
			Assert.Null(PageLayoutStore.Load("home"));
			Assert.Empty(PageLayoutStore.LoadAll());
			// 合法 JSON 但结构不对（根不是含 pages 的对象）同样按无配置处理
			File.WriteAllText(PageLayoutStore.FilePath, "[1,2,3]");
			Assert.Empty(PageLayoutStore.LoadAll());
		}
		finally { ResetDir(); }
	}

	[Fact]
	public void ExportImportRoundTripKeepsHiddenState()
	{
		WithTempDir();
		try
		{
			var doc = PageLayoutDoc.Default(new[] { "a", "b" }, new[] { "a", "b" });
			doc.Blocks[0].Hidden = true;
			PageLayoutStore.Save("home", doc);
			string json = PageLayoutStore.ExportJson(PageLayoutStore.LoadAll());
			// 换个子目录模拟另一台机器导入
			PageLayoutStore.DirOverride = Path.Combine(_tmp, "imported");
			var pages = PageLayoutStore.ParseImport(json);
			Assert.Single(pages);
			Assert.Equal(new[] { "a", "b" }, Ids(pages["home"]));
			Assert.True(pages["home"].Blocks[0].Hidden);
		}
		finally { ResetDir(); }
	}

	[Fact]
	public void ParseImportSkipsBrokenPagesButKeepsGoodOnes()
	{
		WithTempDir();
		try
		{
			string json = "{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{"
				+ "\"good\":{\"version\":1,\"blocks\":[{\"id\":\"a\",\"hidden\":true}]},"
				+ "\"bad\":{\"version\":9,\"blocks\":[]},"
				+ "\"ugly\":{\"version\":\"x\"}}}";
			var pages = PageLayoutStore.ParseImport(json);
			Assert.Single(pages);
			Assert.True(pages["good"].Blocks[0].Hidden);
		}
		finally { ResetDir(); }
	}

	[Fact]
	public void ParseImportThrowsOnWrongKindOrVersionOrGarbage()
	{
		WithTempDir();
		try
		{
			Assert.Throws<InvalidOperationException>(() => PageLayoutStore.ParseImport(
				"{\"version\":1,\"kind\":\"something-else\",\"pages\":{}}"));
			Assert.Throws<InvalidOperationException>(() => PageLayoutStore.ParseImport(
				"{\"version\":2,\"kind\":\"tipcl-page-layout\",\"pages\":{}}"));
			Assert.Throws<InvalidOperationException>(() => PageLayoutStore.ParseImport("not json at all"));
			Assert.Throws<InvalidOperationException>(() => PageLayoutStore.ParseImport("{\"version\":2}"));
		}
		finally { ResetDir(); }
	}

	[Fact]
	public void EmptyPageIdSaveIsIgnored()
	{
		WithTempDir();
		try
		{
			PageLayoutStore.Save("", PageLayoutDoc.Default(new[] { "a" }, new[] { "a" }));
			Assert.Empty(PageLayoutStore.LoadAll());
		}
		finally { ResetDir(); }
	}

	private static string[] Ids(PageLayoutDoc doc)
	{
		var list = new List<string>();
		foreach (var b in doc.Blocks) list.Add(b.Id);
		return list.ToArray();
	}

	private static bool[] Hiddens(PageLayoutDoc doc)
	{
		var list = new List<bool>();
		foreach (var b in doc.Blocks) list.Add(b.Hidden);
		return list.ToArray();
	}
}
