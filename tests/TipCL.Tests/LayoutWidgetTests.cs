using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// L6 纯模型测试:自建区块的内容工厂注册、五类块的取值与门禁、危险类型默认关闭、
/// 插件块 id 解析、AI 布局工具(layout_get / layout_apply)的参数校验与自动备份。
/// 界面渲染(WPF)由单测覆盖不到:测试宿主是 net8,只碰不依赖 WPF 的类型;
/// 渲染证据走 --shot(见交付报告)。全部落盘测试走 DirOverride 临时目录,绝不写用户真实数据目录。
/// </summary>
public class LayoutWidgetTests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_widget_tests_" + Guid.NewGuid().ToString("N"));

	private void UseDir()
	{
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();   // 与 App.OnStartup 同装配:v1 走 PageLayoutStore 语义
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
	}

	private void Release()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
		try
		{
			Directory.Delete(_tmp, true);
		}
		catch (Exception)
		{
		}
	}

	private static bool HasCjk(string? text)
	{
		return !string.IsNullOrWhiteSpace(text) && text.Any((char c) => c >= '\u4e00' && c <= '\u9fff');
	}

	private static WidgetDef Widget(string id, string type, params (string key, object? value)[] settings)
	{
		WidgetDef def = new WidgetDef
		{
			Id = id,
			Type = type,
			Title = type
		};
		foreach ((string key, object? value) item in settings)
		{
			def.Settings[item.key] = item.value;
		}
		return def;
	}

	// ————— 内容工厂:注册 / 认领范围 —————

	[Fact]
	public void RegistryClaimsCustomAndPluginBlocksOnly()
	{
		WidgetRegistry registry = new WidgetRegistry();
		Assert.True(registry.CanBuild("custom:w-1"));
		Assert.True(registry.CanBuild("plugin:hello-plugin:card"));
		Assert.False(registry.CanBuild("hero")); // 内置块由页面自己构建内容
		Assert.False(registry.CanBuild(""));
	}

	[Fact]
	public void EnsureRegisteredIsIdempotentAndVisibleToEngine()
	{
		LayoutContentRegistry.Clear();
		try
		{
			Assert.False(WidgetRegistry.Registered);
			Assert.True(WidgetRegistry.EnsureRegistered());
			Assert.True(WidgetRegistry.Registered);
			Assert.False(WidgetRegistry.EnsureRegistered()); // 第二次无副作用
			Assert.Equal(1, LayoutContentRegistry.Count);
			// 引擎的"不可用"判定随之变化:自建块/插件块有工厂了
			Assert.False(LayoutEngine.IsUnavailable("custom:w-1"));
			Assert.False(LayoutEngine.IsUnavailable("plugin:hello-plugin:card"));
			Assert.False(LayoutEngine.IsUnavailable("hero")); // 内置块永远不算不可用
		}
		finally
		{
			WidgetRegistry.Unregister();
			LayoutContentRegistry.Clear();
		}
		Assert.False(WidgetRegistry.Registered);
	}

	[Fact]
	public void UnavailableWhenNoFactoryRegistered()
	{
		LayoutContentRegistry.Clear();
		try
		{
			Assert.True(LayoutEngine.IsUnavailable("custom:w-1"));
			Assert.True(LayoutEngine.IsUnavailable("plugin:hello-plugin:card"));
		}
		finally
		{
			LayoutContentRegistry.Clear();
		}
	}

	// ————— 危险类型默认关闭(Q5) —————

	[Fact]
	public void DangerousWidgetsAreDisabledByDefault()
	{
		Assert.True(LayoutSchema.IsDangerousWidgetType("web"));
		Assert.True(LayoutSchema.IsDangerousWidgetType("button"));
		Assert.False(LayoutSchema.IsDangerousWidgetType("note"));

		WidgetDef web = Widget("w-1", "web");
		web.Settings["url"] = "https://example.com/";
		Assert.False(WidgetCommon.IsEnabled(web)); // 没写 enabled → 关闭

		web.Settings["enabled"] = false;
		Assert.False(WidgetCommon.IsEnabled(web));

		web.Settings["enabled"] = "true"; // 只认 JSON 布尔 true,字符串不算
		Assert.False(WidgetCommon.IsEnabled(web));

		web.Settings["enabled"] = true;
		Assert.True(WidgetCommon.IsEnabled(web));

		WidgetDef note = Widget("w-2", "note");
		Assert.False(WidgetCommon.IsEnabled(note)); // 非危险类型不看开关
	}

	[Fact]
	public void SafeTypesExcludeDangerousOnes()
	{
		Assert.Contains("note", WidgetRegistry.SafeTypes);
		Assert.Contains("shortcut", WidgetRegistry.SafeTypes);
		Assert.Contains("clock", WidgetRegistry.SafeTypes);
		Assert.Contains("divider", WidgetRegistry.SafeTypes);
		Assert.Contains("playtime", WidgetRegistry.SafeTypes);
		Assert.DoesNotContain("web", WidgetRegistry.SafeTypes);
		Assert.DoesNotContain("button", WidgetRegistry.SafeTypes);
	}

	// ————— web 地址准入(限 https,禁 file://) —————

	[Theory]
	[InlineData("https://example.com/a?b=1", true)]
	[InlineData("HTTPS://EXAMPLE.COM/", true)]
	[InlineData("http://example.com/", false)]
	[InlineData("file:///C:/Windows/system.ini", false)]
	[InlineData("javascript:alert(1)", false)]
	[InlineData("data:text/html,<b>x</b>", false)]
	[InlineData("ftp://example.com/", false)]
	[InlineData("example.com", false)]
	[InlineData("", false)]
	public void WebUrlGateAllowsHttpsOnly(string url, bool allowed)
	{
		bool ok = WidgetCommon.TryAcceptWebUrl(url, out string normalized, out string reason);
		Assert.Equal(allowed, ok);
		if (ok)
		{
			Assert.StartsWith("https://", normalized, StringComparison.OrdinalIgnoreCase);
		}
		else
		{
			// 拒绝必须给中文原因(界面直接显示给用户)
			Assert.True(HasCjk(reason), "拒绝原因应是中文:" + reason);
		}
	}

	[Fact]
	public void WebUrlGateRejectsOverlongUrl()
	{
		string url = "https://example.com/" + new string('a', WidgetCommon.UrlMaxLength);
		Assert.False(WidgetCommon.TryAcceptWebUrl(url, out string _, out string reason));
		Assert.False(string.IsNullOrWhiteSpace(reason));
	}

	// ————— button 内部动作白名单 —————

	[Theory]
	[InlineData("page:launch", true)]
	[InlineData("page:settings:general", true)]
	[InlineData("launch:1.20.1-Fabric", true)]
	[InlineData("launch:1.20.1-Fabric|1.20.1", true)]
	[InlineData("folder:data", true)]
	[InlineData("rpc:get_launcher_state", true)]
	[InlineData("rpc:list_instances", true)]
	[InlineData("folder:C:/Windows", false)]
	[InlineData("folder:data/../../../", false)]
	[InlineData("rpc:delete_instance", false)]
	[InlineData("rpc:fs_write", false)]
	[InlineData("cmd:calc.exe", false)]
	[InlineData("shell:start notepad", false)]
	[InlineData("launch:", false)]
	[InlineData("page", false)]
	[InlineData("", false)]
	public void ButtonActionWhitelist(string action, bool allowed)
	{
		bool ok = WidgetCommon.TryParseAction(action, out string kind, out string arg1, out string arg2, out string reason);
		Assert.Equal(allowed, ok);
		if (ok)
		{
			Assert.Contains(kind, WidgetCommon.ActionKinds);
			Assert.False(string.IsNullOrEmpty(arg1));
		}
		else
		{
			Assert.True(HasCjk(reason), "拒绝原因应是中文:" + reason);
		}
	}

	[Fact]
	public void ButtonActionParsesInstanceAndVersion()
	{
		Assert.True(WidgetCommon.TryParseAction("launch:My-Instance|1.20.1", out string kind, out string arg1, out string arg2, out string _));
		Assert.Equal("launch", kind);
		Assert.Equal("My-Instance", arg1);
		Assert.Equal("1.20.1", arg2);
	}

	// ————— 自建块定义查找(内容工厂的取数路径) —————

	[Fact]
	public void FindWidgetReadsDefinitionsFromLayoutStore()
	{
		UseDir();
		try
		{
			LayoutV2 doc = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "widgets": [
			      { "id": "w-1", "type": "note", "title": "便签", "settings": { "text": "hello" } },
			      { "id": "w-2", "type": "clock", "title": "时钟", "settings": { "format": "HH:mm" } }
			    ]
			  }
			}
			""");
			LayoutStoreV2.Save(doc);

			WidgetDef? note = WidgetRegistry.FindWidget("custom:w-1");
			Assert.NotNull(note);
			Assert.Equal("note", note!.Type);
			Assert.Equal("hello", WidgetCommon.Str(note.Settings, "text", ""));

			Assert.Null(WidgetRegistry.FindWidget("custom:nope"));
			Assert.Null(WidgetRegistry.FindWidget("hero")); // 内置块没有自建定义
			Assert.Equal(new[] { "w-1", "w-2" }, WidgetRegistry.AllWidgets().Select((WidgetDef w) => w.Id).ToArray());
		}
		finally
		{
			Release();
		}
	}

	// ————— 插件块 id —————

	[Fact]
	public void PluginBlockIdParsing()
	{
		Assert.Equal("block", WidgetRegistry.PluginKindBlock);
		Assert.True(LayoutEngine.TryParsePluginId("plugin:hello-plugin:card", out string plugin, out string inner));
		Assert.Equal("hello-plugin", plugin);
		Assert.Equal("card", inner);
		Assert.False(LayoutEngine.TryParsePluginId("plugin:hello-plugin", out string _, out string _));
		Assert.False(LayoutEngine.TryParsePluginId("custom:w-1", out string _, out string _));
	}

	// ————— AI 工具:工具表 / 参数校验 / 自动备份 —————

	[Fact]
	public void ToolSchemasAreRegisteredAndParse()
	{
		List<object> schemas = ExtraTools.Schemas(); // 合并了 LayoutTools.Schemas(),也验证 schema JSON 合法
		List<string> names = schemas.Select((object s) => Tools.SchemaName(s)).ToList();
		Assert.Contains("layout_get", names);
		Assert.Contains("layout_apply", names);
		Assert.True(ExtraTools.Handles("layout_get"));
		Assert.True(ExtraTools.Handles("layout_apply"));
		Assert.Contains("查看界面布局", ExtraTools.Labels["layout_get"]);
		Assert.Contains("调整界面布局", ExtraTools.Labels["layout_apply"]);
		// 元数据:读 = ro:true;写 = ro:false + write_local(走确认通道)
		ToolMetaInfo meta = ToolMeta.Get("layout_apply");
		Assert.NotNull(meta);
		Assert.False(meta!.@readonly);
		Assert.Equal("write_local", meta.side_effect);
		ToolMetaInfo readMeta = ToolMeta.Get("layout_get");
		Assert.NotNull(readMeta);
		Assert.True(readMeta!.@readonly);
		Assert.True(Tools.IsWriteTool("layout_apply"));
		Assert.False(Tools.IsWriteTool("layout_get"));
		// 关键词触发:用户提到"布局"时这两个工具会被选进工具表
		Assert.Contains(ExtraTools.Keywords, ((string[] keys, string[] extra) row) => row.extra.Contains("layout_apply"));
	}

	[Fact]
	public void LayoutGetSummarizesPagesAndWidgets()
	{
		UseDir();
		try
		{
			LayoutV2 doc = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "pages": { "tasks": { "version": 2, "blocks": [{ "id": "header" }, { "id": "list", "hidden": true }] } },
			    "widgets": [{ "id": "w-1", "type": "note", "title": "便签" }],
			    "splits": { "tasks": { "side": 0.4 } }
			  }
			}
			""");
			LayoutStoreV2.Save(doc);
			string json = LayoutTools.Get();
			Assert.Contains("\"tasks\"", json);
			Assert.Contains("list(hidden)", json);
			Assert.Contains("custom:w-1", json);
			Assert.Contains("\"type\": \"note\"", json);
			Assert.Contains("\"side\": 0.4", json);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyPageBlocksWritesLayoutAndBacksUp()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "pages": { "tasks": { "version": 2, "blocks": [{ "id": "header" }, { "id": "list" }] } },
			    "widgets": [{ "id": "w-1", "type": "note", "title": "便签" }]
			  }
			}
			"""));
			PyDict args = PyDict.Of("page", "tasks", "blocks", Py.L(PyDict.Of("id", "list"), PyDict.Of("id", "header", "hidden", true)), "gap", 12L);
			string result = LayoutTools.Run(LayoutTools.ApplyName, args);
			Assert.Contains("已更新布局", result);
			Assert.Contains("AI 备份 ", result);

			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Equal(new[] { "list", "header" }, after.Layout.Pages["tasks"].Blocks.Select((BlockLayout b) => b.Id).ToArray());
			Assert.True(after.Layout.Pages["tasks"].Blocks[1].Hidden);
			Assert.Equal(12.0, after.Layout.Pages["tasks"].Gap);
			// 备份里存的是**改之前**的布局
			string backup = after.Profiles.Keys.Single((string key) => key.StartsWith(LayoutTools.BackupPrefix, StringComparison.Ordinal));
			Assert.Equal(new[] { "header", "list" }, after.Profiles[backup].Pages["tasks"].Blocks.Select((BlockLayout b) => b.Id).ToArray());
			// 自建块与 v1 写通都没被破坏
			Assert.Single(after.Layout.Widgets);
			PageLayoutDoc? v1 = PageLayoutStore.Load("tasks");
			Assert.NotNull(v1);
			Assert.Equal(new[] { "list", "header" }, v1!.Blocks.Select((BlockState b) => b.Id).ToArray());
			Assert.True(v1.Blocks[1].Hidden);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyAddsWidgetAndKeepsSafeTypes()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutV2.CreateDefault());
			PyDict widget = PyDict.Of("id", "w-note", "type", "note", "title", "今日待办", "settings", PyDict.Of("text", "写作业"));
			string result = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("widgets", Py.L(widget)));
			Assert.Contains("已更新布局", result);
			LayoutV2 after = LayoutStoreV2.Load();
			WidgetDef def = Assert.Single(after.Layout.Widgets);
			Assert.Equal("w-note", def.Id);
			Assert.Equal("note", def.Type);
			Assert.Equal("今日待办", def.Title);
			Assert.Equal("写作业", WidgetCommon.Str(def.Settings, "text", ""));
			// 内容工厂能按 custom:<id> 找到它
			Assert.NotNull(WidgetRegistry.FindWidget("custom:w-note"));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyAcceptsDangerousWidgetButLeavesItDisabled()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutV2.CreateDefault());
			PyDict widget = PyDict.Of("id", "w-web", "type", "web", "title", "网页", "settings", PyDict.Of("url", "https://example.com/"));
			string result = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("widgets", Py.L(widget)));
			Assert.Contains("已更新布局", result);
			WidgetDef def = Assert.Single(LayoutStoreV2.Load().Layout.Widgets);
			Assert.True(LayoutSchema.IsDangerousWidgetType(def.Type));
			Assert.False(WidgetCommon.IsEnabled(def)); // 默认关闭:定义可以存在,但不渲染
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyRejectsUnknownWidgetTypeAndBadTabsAndMissingProfile()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutV2.CreateDefault());
			string unknown = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("widgets", Py.L(PyDict.Of("id", "w-1", "type", "rocket"))));
			Assert.Contains("不认识的区块类型", unknown);
			string badTab = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("shell_tabs", Py.L("launch", "nope")));
			Assert.Contains("顶栏页签", badTab);
			string noProfile = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("profile", "不存在的方案"));
			Assert.Contains("找不到名为", noProfile);
			string nothing = LayoutTools.Run(LayoutTools.ApplyName, new PyDict());
			Assert.Contains("没有识别到要修改的内容", nothing);
			// 全部失败路径都不落盘
			using (FileStream stream = new FileStream(Path.Combine(_tmp, "ui_layout_v2.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				Assert.True(stream.Length > 0);
			}
			Assert.Empty(LayoutStoreV2.Load().Layout.Widgets);
			Assert.Empty(LayoutStoreV2.Load().Profiles);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyRejectsInvalidJsonDocumentWithChineseReason()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutV2.CreateDefault());
			string result = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("json", "{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{}}"));
			Assert.Contains("布局 JSON 用不了", result);
			Assert.Contains("布局文件无效", result);
			Assert.Empty(LayoutStoreV2.Load().Profiles); // 失败不备份、不落盘
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyAcceptsFullJsonDocument()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutV2.CreateDefault());
			string json = """
			{ "version": 2, "kind": "tipcl-layout", "layout": { "pages": { "tasks": { "version": 2, "blocks": [{ "id": "header" }] } } } }
			""";
			string result = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("json", json));
			Assert.Contains("已更新布局", result);
			Assert.True(LayoutStoreV2.Load().Layout.Pages.ContainsKey("tasks"));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyAppliesNamedProfile()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": { "pages": { "tasks": { "version": 2, "blocks": [{ "id": "header" }] } } },
			  "profiles": { "极简": { "pages": { "tasks": { "version": 2, "blocks": [{ "id": "list", "hidden": true }] } } } }
			}
			"""));
			string result = LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("profile", "极简"));
			Assert.Contains("应用方案「极简」", result);
			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Equal("极简", after.ActiveProfile);
			Assert.Equal(new[] { "list" }, after.Layout.Pages["tasks"].Blocks.Select((BlockLayout b) => b.Id).ToArray());
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyKeepsAtMostFiveAiBackups()
	{
		UseDir();
		try
		{
			LayoutStoreV2.Save(LayoutV2.CreateDefault());
			for (int i = 0; i < LayoutTools.MaxBackups + 3; i++)
			{
				LayoutTools.Run(LayoutTools.ApplyName, PyDict.Of("page", "tasks", "blocks", Py.L(PyDict.Of("id", "header"))));
			}
			LayoutV2 after = LayoutStoreV2.Load();
			int backups = after.Profiles.Keys.Count((string key) => key.StartsWith(LayoutTools.BackupPrefix, StringComparison.Ordinal));
			Assert.True(backups <= LayoutTools.MaxBackups, "AI 备份应被裁剪到 " + LayoutTools.MaxBackups + " 份,实际 " + backups);
			Assert.True(backups > 0);
		}
		finally
		{
			Release();
		}
	}
}
