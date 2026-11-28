using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-S 的界面接线守卫(任务书 §7 S 任务 3:UI 先只显示一行「子任务进行中:&lt;工具名&gt;」)。
/// <c>AiPage</c> 是 WPF 页面,net8 测试宿主实例化不了,所以按仓库惯例(LayoutV3SourceTests / LayoutPageSplitTests)
/// 用**源码文本 + 词表**断言:
///   1) <c>OnStatus</c> 认 payload 里的 <c>subagent</c> 字段并把这种事件提前拦下,不让子代理内部的步骤铺进父回合;
///   2) 文案走 <c>I18n.L("子任务进行中:{0}", …)</c>,而且这个 key 在 en / zh_CN 两份词表里都有
///      (缺 key = 英文界面显示中文,是仓库历史上踩过的坑)。
/// </summary>
public class AiSubagentUiWiringTests
{
	/// <summary>与 AiPage 里那条文案同一个字面量。</summary>
	private const string Key = "子任务进行中:{0}";

	private static string Root()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return dir!.FullName;
	}

	private static string Source(string rel)
	{
		return File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static HashSet<string> LocaleKeys(string file)
	{
		string path = Path.Combine(Root(), "src", "TipCL", "Resources", "locales", file);
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
		HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty p in doc.RootElement.EnumerateObject())
		{
			keys.Add(p.Name);
		}
		return keys;
	}

	[Fact]
	public void OnStatusShowsASingleSubtaskLine()
	{
		string src = Source("src/TipCL/Pages/AiPage.cs");
		int onStatus = src.IndexOf("private void OnStatus(BridgeEvent ev)", StringComparison.Ordinal);
		Assert.True(onStatus >= 0, "找不到 AiPage.OnStatus");
		// 子代理事件靠 subagent 字段识别,并且要在 switch 之前就返回(不铺子代理的步骤)
		int guard = src.IndexOf("PayloadStr(ev, \"subagent\")", onStatus, StringComparison.Ordinal);
		Assert.True(guard > onStatus, "OnStatus 里没有认 subagent 字段");
		int show = src.IndexOf("_status.Text = I18n.L(\"" + Key + "\", ev.Name);", guard, StringComparison.Ordinal);
		Assert.True(show > guard, "没有把「子任务进行中:<工具名>」写进状态行");
		int sw = src.IndexOf("switch (ev.Kind)", onStatus, StringComparison.Ordinal);
		Assert.True(sw > show, "子代理分支必须在 switch 之前返回");
		int ret = src.IndexOf("return;", show, StringComparison.Ordinal);
		Assert.True(ret > show && ret < sw, "子代理分支没有提前返回");
	}

	[Fact]
	public void SubtaskStatusTextIsTranslated()
	{
		Assert.Contains(Key, LocaleKeys("zh_CN.json"));
		HashSet<string> en = LocaleKeys("en.json");
		Assert.Contains(Key, en);
		// 英文词表里必须真的换成英文(只留 key 不改值 = 漏翻)
		string text = Source("src/TipCL/Resources/locales/en.json");
		// 注意 key 本身自带一个冒号(「子任务进行中:」),找分隔冒号要从 key 之后开始
		int keyAt = text.IndexOf("\"" + Key + "\"", StringComparison.Ordinal);
		Assert.True(keyAt >= 0, "en.json 里找不到 key");
		int colon = text.IndexOf(':', keyAt + Key.Length + 2);
		int open = text.IndexOf('"', colon + 1);
		int close = text.IndexOf('"', open + 1);
		Assert.True(colon > keyAt && open > colon && close > open, "en.json 里这条 key 的值不是字符串");
		string value = text.Substring(open + 1, close - open - 1);
		Assert.Contains("Subtask", value);
		Assert.DoesNotContain("子任务", value);
	}

	[Fact]
	public void SubtaskStatusTextResolvesAtRuntime()
	{
		// 真走一遍运行时链路:内嵌资源(locales/*.json.gz)→ CoreResources → I18nCore.Tr。
		// 证明词条不只是写进了源码:重新生成的 .gz 里也带上它了。
		Assert.Equal("Subtask running: {0}", I18nCore.Tr(Key, "en"));
		Assert.Equal("子任务进行中:{0}", I18nCore.Tr(Key, "zh_CN"));
	}

	[Fact]
	public void SubagentEventsCarryTheMarkerField()
	{
		// 界面认的字段由 Agent.RunSubagent 的 childStatus 加上:两个字段名必须仍然一致
		string agent = Source("src/TipCL/Core/Launcher/Ai/Agent.cs");
		Assert.Contains("copy[\"subagent\"] = subSessionId;", agent);
		Assert.Contains("copy[\"subagent_task\"] = Py.Slice(text, null, 80);", agent);
	}
}
