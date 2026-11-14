using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// U6:Windows 版 <c>Pages\FeedbackConsent.cs</c>(7141 字节 / 219 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「反馈 / 隐私同意」:首次启动时问一次「是否上传诊断数据」,答案写回后端 <c>feedback_consent</c>;
/// 再配上三态读取(<see cref="ReadRaw"/>)与判定(<see cref="ShouldAsk"/>)这两条纯函数,以及
/// Windows 的 <c>--self-test</c> 用的 <see cref="SelfTest(System.IO.TextWriter)"/>。
///
/// <b>它为什么属于本包</b>:mac 侧全仓零引用 —— Windows 的两条接线点是
/// <c>App.xaml.cs:87</c>(<c>Environment.ExitCode = FeedbackConsent.SelfTest();</c>,自检通道)与
/// <c>MainWindow.xaml.cs:384</c>(<c>await FeedbackConsent.MaybeAskAsync();</c>,启动时那条"先问一次"的路径),
/// 前者 mac 没有对应的自检 CLI,后者的落点是外壳启动序列(不在本包可写范围内,见下面的接线说明)。
/// 所以本包落**整件**:纯逻辑 + 询问界面,一行不改语义。
///
/// 接线说明(照实说,不留假装):mac 侧的启动序列(<c>App</c> / <c>MainWindow</c>)属外壳文件(红线:本包不动),
/// 所以 <see cref="MaybeAskAsync"/> 目前没有被任何启动路径调用 —— 这与 Windows 的差异记在这里,
/// 等外壳侧接线时一行即可(Windows 就是这么一行 <c>await FeedbackConsent.MaybeAskAsync();</c>)。
///
/// Windows → Avalonia / mac 的差异(逐条):
/// <list type="bullet">
/// <item>本件不含任何 WPF 视觉类型(只有 <c>Dlg.Confirm</c> 一个界面调用)⇒ 框架替换数为 0;</item>
/// <item><c>AppServices.Client</c> / <c>Dlg</c> / <c>I18n</c> 与 Windows 同名;<c>TryCallAsync</c> 的泛型推断
///   在 mac 侧要显式写出类型实参(<c>TryCallAsync&lt;Dictionary&lt;string, JsonElement&gt;&gt;</c>),
///   因为 mac 的 <c>BridgeClient.TryCallAsync</c> 第三个参数是带默认值的 <c>fallback</c>(语义与 Windows 一致);</item>
/// <item><c>SelfTest</c> 的两个重载逐字保留(它写的是一份 UTF-8 无 BOM 的 JSON 自检报告);
///   mac 没有 <c>--self-test</c> 那样的 CLI 开关,所以它现在只能被代码/用例调用,不接外壳。</item>
/// </list>
/// </summary>
public static class FeedbackConsent
{
	/// <summary>【Windows 源】<c>:14</c>:配置文件里那一项的三态读取结果 —— 读得到吗、值是啥(没写过 = null)。</summary>
	public readonly record struct Raw(bool Readable, bool? Value);

	/// <summary>【Windows 源】<c>:16</c>:问之前先等 400ms(让首屏先画出来,免得提问卡在窗口还没起来的时候)。</summary>
	public const int DelayMs = 400;

	/// <summary>【Windows 源】<c>:18</c>:存进 config.json 的键名。</summary>
	public const string Key = "feedback_consent";

	/// <summary>
	/// 【Windows 源】<c>:20-38</c>:读配置文件里的那一项。<c>configPath</c> 为空 ⇒ 读不到(Readable=false);
	/// 文件不存在 ⇒ 读得到但没写过(Readable=true, Value=null);其余一律交给 <see cref="ParseRaw"/>。
	/// 任何异常都吞掉成"读不到" —— 读不出来时不假装用户已经选过。
	/// </summary>
	public static Raw ReadRaw(string? configPath)
	{
		if (string.IsNullOrWhiteSpace(configPath))
		{
			return new Raw(Readable: false, null);
		}
		try
		{
			if (!File.Exists(configPath))
			{
				return new Raw(Readable: true, null);
			}
			return ParseRaw(File.ReadAllText(configPath));
		}
		catch
		{
			return new Raw(Readable: false, null);
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:40-73</c>:解析设置 JSON。根不是对象 / JSON 坏了 ⇒ 读不到;
	/// 没有这个键或是 null ⇒ 没写过;true ⇒ 同意;<b>其它任何值都算"问过了"</b>(与 Python 侧 <c>is not None</c> 同口径)。
	/// </summary>
	public static Raw ParseRaw(string json)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				return new Raw(Readable: false, null);
			}
			if (!jsonDocument.RootElement.TryGetProperty("feedback_consent", out var value))
			{
				return new Raw(Readable: true, null);
			}
			Raw result;
			switch (value.ValueKind)
			{
			case JsonValueKind.Undefined:
			case JsonValueKind.Null:
				result = new Raw(Readable: true, null);
				break;
			case JsonValueKind.True:
				result = new Raw(Readable: true, true);
				break;
			default:
				result = new Raw(Readable: true, false);
				break;
			}
			return result;
		}
		catch (JsonException)
		{
			return new Raw(Readable: false, null);
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:75-82</c>:还要不要问 —— 配置文件读得到就以**它**为准(问过就不再问,即使用户选的是"暂不同意");
	/// 读不到才看后端的 <paramref name="bridgeConsent"/>(它说同意过就当问过了,否则问)。
	/// </summary>
	public static bool ShouldAsk(Raw raw, bool bridgeConsent)
	{
		if (raw.Readable)
		{
			return !raw.Value.HasValue;
		}
		return !bridgeConsent;
	}

	/// <summary>
	/// 【Windows 源】<c>:84-104</c>:启动时那条路径 —— 问后端拿设置,决定要不要问一次,问了就把答案写回 <c>feedback_consent</c>。
	/// 文案(标题 / 正文 / 两个按钮)逐字照抄,走 <c>I18n.L</c>(这两个键在两份词表里都有)。
	/// </summary>
	public static async Task MaybeAskAsync()
	{
		BridgeClient api = AppServices.Client;
		Dictionary<string, JsonElement> obj = (await api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		JsonElement value;
		string text = ((obj.TryGetValue("root", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		bool bridgeConsent = obj.TryGetValue("feedback_consent", out value2) && value2.ValueKind == JsonValueKind.True;
		if (ShouldAsk(ReadRaw((text.Length > 0) ? Path.Combine(text, "config.json") : null), bridgeConsent))
		{
			// 【Windows 源】<c>:94</c>:这里的字面 400 与 <see cref="DelayMs"/> 同值(Windows 源就是这么写的)。
			await Task.Delay(400);
			BridgeClient bridgeClient = api;
			await bridgeClient.TryCallAsync<object>("save_settings", new
			{
				data = new
				{
					feedback_consent = await Dlg.Confirm(I18n.L("是否上传诊断数据"), I18n.L("第一次打开需要你亲自选择。\n\n同意后才会向开发者上传：\n· 你提交的反馈内容\n· 本机配置（CPU / 内存 / 显卡 / Java / 已装版本）\n\n暂不同意则不会上传，以后可在设置里更改。"), I18n.L("同意"), I18n.L("暂不同意"))
				}
			});
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:106-211</c>:自检 —— 15 条用例(7 条解析 / 6 条判定 / 2 条"下一次启动不再问"),
	/// 逐条打进一个 JSON 报告。<b>用例名是 Windows 源里的英文字面串,一个字都不改</b>(它们不是用户可见文案);
	/// 返回值 0 = 全过,1 = 有失败(<c>failed</c> 计数)。
	/// </summary>
	public static int SelfTest(TextWriter output)
	{
		(string, string, Raw)[] parseCases = new(string, string, Raw)[7]
		{
			("empty object -> never asked", "{}", new Raw(Readable: true, null)),
			("explicit null -> never asked", "{\"feedback_consent\": null}", new Raw(Readable: true, null)),
			("true -> asked, consented", "{\"feedback_consent\": true, \"first_run\": false}", new Raw(Readable: true, true)),
			("false -> asked, declined", "{\"feedback_consent\": false}", new Raw(Readable: true, false)),
			("non-bool value counts as asked (python: is not None)", "{\"feedback_consent\": \"yes\"}", new Raw(Readable: true, false)),
			("broken json -> unreadable", "{not json", new Raw(Readable: false, null)),
			("json array -> unreadable", "[1,2]", new Raw(Readable: false, null))
		};
		(string, Raw, bool, bool)[] decideCases = new(string, Raw, bool, bool)[6]
		{
			("config readable, key null -> ask", new Raw(Readable: true, null), false, true),
			("config readable, key null even if bridge says true (file wins) -> ask", new Raw(Readable: true, null), true, true),
			("config readable, true -> no", new Raw(Readable: true, true), true, false),
			("config readable, false (declined earlier, maybe in Qt) -> no", new Raw(Readable: true, false), false, false),
			("config unreadable, bridge consented -> no", new Raw(Readable: false, null), true, false),
			("config unreadable, bridge false -> ask", new Raw(Readable: false, null), false, true)
		};
		(string, bool, bool)[] transitionCases = new(string, bool, bool)[2]
		{
			("after agreeing, next launch does not ask", true, false),
			("after declining, next launch does not ask", false, false)
		};
		List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
		int num = 0;
		foreach ((string, string, Raw) tuple in parseCases)
		{
			Raw raw = ParseRaw(tuple.Item2);
			bool flag = raw == tuple.Item3;
			if (!flag)
			{
				num++;
			}
			list.Add(new Dictionary<string, object>
			{
				["case"] = tuple.Item1,
				["kind"] = "parse",
				["json"] = tuple.Item2,
				["want"] = string.Format("{0}/{1}", tuple.Item3.Readable, tuple.Item3.Value?.ToString() ?? "null"),
				["got"] = string.Format("{0}/{1}", raw.Readable, raw.Value?.ToString() ?? "null"),
				["ok"] = flag
			});
		}
		foreach ((string, Raw, bool, bool) tuple2 in decideCases)
		{
			bool flag2 = ShouldAsk(tuple2.Item2, tuple2.Item3);
			bool flag3 = flag2 == tuple2.Item4;
			if (!flag3)
			{
				num++;
			}
			list.Add(new Dictionary<string, object>
			{
				["case"] = tuple2.Item1,
				["kind"] = "decide",
				["readable"] = tuple2.Item2.Readable,
				["raw"] = tuple2.Item2.Value,
				["bridge_consent"] = tuple2.Item3,
				["want"] = tuple2.Item4,
				["got"] = flag2,
				["ok"] = flag3
			});
		}
		foreach ((string, bool, bool) tuple3 in transitionCases)
		{
			bool flag4 = ShouldAsk(ParseRaw("{\"feedback_consent\": null}"), bridgeConsent: false);
			bool flag5 = ShouldAsk(ParseRaw(JsonSerializer.Serialize(new Dictionary<string, object> { ["feedback_consent"] = tuple3.Item2 })), tuple3.Item2);
			bool flag6 = flag4 && flag5 == tuple3.Item3;
			if (!flag6)
			{
				num++;
			}
			list.Add(new Dictionary<string, object>
			{
				["case"] = tuple3.Item1,
				["kind"] = "transition",
				["choice"] = tuple3.Item2,
				["first_launch_asks"] = flag4,
				["second_launch_asks"] = flag5,
				["want_second"] = tuple3.Item3,
				["ok"] = flag6
			});
		}
		output.Write(JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["total"] = parseCases.Length + decideCases.Length + transitionCases.Length,
			["failed"] = num,
			["delay_ms"] = 400,
			["cases"] = list
		}, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		}));
		output.Flush();
		return (num != 0) ? 1 : 0;
	}

	/// <summary>【Windows 源】<c>:213-218</c>:自检的入口(UTF-8 无 BOM 写到标准输出)。</summary>
	public static int SelfTest()
	{
		UTF8Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		using StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), encoding);
		return SelfTest(output);
	}
}
