using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// M11:**字面量保真守卫** —— 移植文件里的用户可见串必须与 Windows 源逐字相同(标点宽度也要一样)。
///
/// <b>为什么这不是洁癖</b>:mac 的 i18n **键就是那句中文本身**,而 <c>I18n.L</c> 是**精确序数查表**
/// (`Services\I18n.cs:104/110` 的 `_current.TryGetValue(zh, …)`,中间没有任何折叠/归一化)。
/// 于是:词表里的键用 Windows 的**全角**标点(`（可多选）`),而源码里被"重打一遍"成半角(`(可多选)`)
/// ⇒ 查不到 ⇒ **英文界面回退显示中文**。这轮真发生过(mac-widgets 自查 5 处、mac-aipage 自查 70 处、
/// `AiChatOps.cs` 2 处),所以把它钉成永久守卫。
///
/// <b>判定(三条同时成立才算漂移)</b>:
/// <list type="number">
/// <item>该字面量**不是** mac 词表(`src\TipCL.Core\Resources\locales\{en,zh_CN}.json`)里的键;</item>
/// <item>存在一个**全角折叠成 ASCII 后相等**的键(即"只差标点宽度"的近键 —— 证明键其实在,只是写法不同);</item>
/// <item>**Windows 源里也有这条串**(折叠后相等)⇒ 它是移植过来的串,不是 mac 专有串。</item>
/// </list>
///
/// <b>Windows 源不可用时如实跳过</b>(<c>SKIP-NOTE:</c> 打进输出,形状照 <c>C1aPaletteTests.Windows_light_palette_source_matches…</c>);
/// 但跳过之前先断言 mac 词表与字面量都真的读到了 —— 否则"找不到源"会退化成"空集比对全绿",那正是假绿。
/// 另一条自测用例 <c>Detector_flags_a_width_only_drift_and_accepts_the_key</c> **不依赖 Windows 源**,
/// 用真实词表 + 合成基线把判定函数两个方向都钉住(标"验证验证者")。
///
/// <b>基线台账(棘轮)</b>:当前树上已有 **269 条**属于历史欠账(44 个文件,主要是 C3–C6 轮次的页面),
/// 逐条列在 <c>fixtures\literal-fidelity-baseline.txt</c>(每行 `路径 \t 字面量 \t 理由码`,理由码 B = 既有漂移欠账)。
/// 语义:**新出现**的漂移(不在台账里)⇒ 红;修好的条目从台账里消失 ⇒ 只提示、不变红(提示里会请人重生成台账)。
/// 这样既不让历史欠账把共享测试树搞红,又不允许任何人再新增一条。
/// 重生成办法:把台账清空跑一次,失败消息会列出全部漂移(文件 / 字面量 / 近键 / 位置)。
/// </summary>
public class LiteralFidelityTests
{
	/// <summary>被扫的移植目标(mac 侧):只扫这个子树,免得把测试自己的串算进去。</summary>
	private const string PortDir = "src/TipCL.App";

	/// <summary>Windows 源相对基准仓的路径(基准仓根 = <see cref="C1aFixtures.WindowsRoot"/>)。</summary>
	private const string WindowsPortDir = "src/TipCL";

	private const string LedgerRelative = "tests/TipCL.Tests/fixtures/literal-fidelity-baseline.txt";

	/// <summary>`I18n.L("…")` 的第一个字符串字面量(不支持逐字串/插值串 —— 扫到也不会误判,因为它们不是 `I18n.L(` 的实参)。</summary>
	private static readonly Regex LiteralPattern = new Regex("I18n\\.L\\(\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

	/// <summary>任意普通字符串字面量(用于"未包裹"通道:直接传给 `Ui.*` 的中文串)。</summary>
	private static readonly Regex AnyLiteralPattern = new Regex("(?<![A-Za-z0-9_])@?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

	/// <summary>字面量前面是不是 `I18n.L(`(可带空白)⇒ 属"已包裹"通道,由 <see cref="LiteralPattern"/> 那条扫。</summary>
	private static readonly Regex WrappedPrefix = new Regex("I18n\\.L\\s*\\(\\s*$", RegexOptions.CultureInvariant);

	/// <summary>
	/// **白名单:每条都带理由**(键 = 字面量本身,理由写在这张表里;台账只放"欠账",白名单只放"刻意保留")。
	///
	/// `AiUiText.StatePrefixes` 的五个状态前缀**刻意半角与全角并存**:Windows 源里前五条全角、后五条半角,
	/// mac 侧顺序相反,两组串互为"同词不同冒号宽度" ⇒ 集合等价、`foreach` 做前缀剥离的**行为等价**。
	/// 这不是漂移(mac-aipage 与 mac-tests 各自误 fold 过一次),Lead 2026-11-14 裁定:**保留 + 留档**。
	///
	/// 用 `\uXXXX` 写死字符,免得"半角/全角冒号"在编辑器里看不出差别(本 harness 也会把全角渲染成 ASCII)。
	/// </summary>
	private static readonly Dictionary<string, string> Whitelist = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["\u51C6\u5907:"] = "AiUiText.StatePrefixes half-width member; its fullwidth twin is in the same list (set-equal, order differs from Windows) and StripState strips prefixes identically - deliberately kept",
		["\u5931\u8D25:"] = "same as above (AiUiText.StatePrefixes)",
		["\u5B8C\u6210:"] = "same as above (AiUiText.StatePrefixes)",
		["\u5DF2\u8DF3\u8FC7:"] = "same as above (AiUiText.StatePrefixes)",
		["\u6267\u884C\u4E2D:"] = "same as above (AiUiText.StatePrefixes)",
	};

	/// <summary>一条漂移(file + literal 是身份,place 只用于报告)。</summary>
	internal sealed class Drift
	{
		internal string File { get; init; } = "";

		internal string Literal { get; init; } = "";

		internal string NearKey { get; init; } = "";

		internal string Place { get; init; } = "";

		internal string Identity => File + "\u0001" + Literal;
	}

	/// <summary>全角 → ASCII 折叠:全角形式区(U+FF01–U+FF5E)整体 -0xFEE0,外加三个中文标点特例。</summary>
	internal static string Fold(string text)
	{
		StringBuilder builder = new StringBuilder(text.Length);
		foreach (char ch in text)
		{
			if (ch == '\u3000')
			{
				builder.Append(' ');
			}
			else if (ch == '\u3001')
			{
				builder.Append(',');
			}
			else if (ch == '\u3002')
			{
				builder.Append('.');
			}
			else if (ch >= '\uFF01' && ch <= '\uFF5E')
			{
				builder.Append((char)(ch - 0xFEE0));
			}
			else
			{
				builder.Append(ch);
			}
		}

		return builder.ToString();
	}

	/// <summary>
	/// 判定函数(纯函数,便于用合成数据自测):字面量不是键 ∧ 存在折叠后相等的近键 ∧ Windows 源里也有这条串。
	/// </summary>
	internal static bool LooksLikeDrift(
		string literal,
		HashSet<string> keys,
		Dictionary<string, string> nearKeyByFold,
		HashSet<string> windowsFolded)
	{
		if (keys.Contains(literal))
		{
			return false;
		}

		string folded = Fold(literal);
		if (!nearKeyByFold.ContainsKey(folded))
		{
			return false;
		}

		return windowsFolded.Contains(folded);
	}

	/// <summary>
	/// 扫一棵源码树里的 `I18n.L("…")` 字面量 → 文件(仓库相对路径,正斜杠)+ 行号。
	/// **先剥注释**(复用 <see cref="C1aPaletteTests.StripComments"/>):守卫的意图是"代码里的查询",
	/// 移植笔记里引一句 Windows 原话(注释里出现 `I18n.L("…")`)不该被判成漂移 —— 这正是 C1a 调色板守卫
	/// 踩过的那条(注释里的假键名被当成真查询)。实测当前漂移 278 处命中**全部在代码行**、注释行 0 处,
	/// 所以台账在两种语义下一致。
	/// </summary>
	private static Dictionary<string, List<string>> CollectLiterals(string root, string repoPrefix)
	{
		var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
		{
			if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			{
				continue;
			}

			// StripComments 逐行处理并保证行数不变(每行输出以 \n 结尾)⇒ 行号仍然指向原文件的行。
			string[] lines = C1aPaletteTests.StripComments(File.ReadAllText(file)).Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				foreach (Match match in LiteralPattern.Matches(lines[i]))
				{
					string literal = match.Groups[1].Value;
					// 去掉根路径后面的分隔符再拼前缀,否则会得到 "src/TipCL.App//Pages/..." 这种双斜杠,
					// 与台账里的身份对不上(实测:那会让全部条目都被当成"新增"→守卫永远红)。
					string tail = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/');
					string relative = repoPrefix + tail.Replace(Path.DirectorySeparatorChar, '/');
					if (!found.TryGetValue(literal, out List<string>? places))
					{
						places = new List<string>();
						found[literal] = places;
					}

					places.Add(relative + ":" + (i + 1));
				}
			}
		}

		return found;
	}

	/// <summary>读 mac 词表(en + zh_CN)的键集合。</summary>
	private static HashSet<string> LoadKeys(string localesDir)
	{
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (string name in new[] { "en.json", "zh_CN.json" })
		{
			string path = Path.Combine(localesDir, name);
			Assert.True(File.Exists(path), "locale table missing: " + path);
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
			foreach (JsonProperty property in doc.RootElement.EnumerateObject())
			{
				keys.Add(property.Name);
			}
		}

		Assert.True(keys.Count > 0, "locale tables loaded but empty: " + localesDir);
		return keys;
	}

	/// <summary>台账:一行一条 `路径 \t 字面量 \t 理由码`;`#` 开头是注释。</summary>
	private static Dictionary<string, string> LoadLedger(string path)
	{
		var ledger = new Dictionary<string, string>(StringComparer.Ordinal);
		if (!File.Exists(path))
		{
			return ledger;
		}

		foreach (string raw in File.ReadAllLines(path))
		{
			string line = raw.TrimEnd('\r');
			if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
			{
				continue;
			}

			string[] parts = line.Split('\t');
			Assert.True(parts.Length >= 2, "bad ledger line: " + line);
			ledger[parts[0] + "\u0001" + Unescape(parts[1])] = (parts.Length >= 3) ? parts[2] : "?";
		}

		return ledger;
	}

	/// <summary>台账里的转义(只转 `\\` `\t` `\r` `\n`,中文原样保留以便人工审阅)。</summary>
	private static string Unescape(string text)
	{
		StringBuilder builder = new StringBuilder(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] != '\\' || i + 1 >= text.Length)
			{
				builder.Append(text[i]);
				continue;
			}

			i++;
			builder.Append(text[i] switch
			{
				'n' => '\n',
				'r' => '\r',
				't' => '\t',
				_ => text[i],
			});
		}

		return builder.ToString();
	}

	/// <summary>
	/// 扫**未包裹通道**:含中文的普通字符串字面量(直接交给 `Ui.Input(...)` / combo 标签 / `Tip(...)` 等),
	/// 且前面不是 `I18n.L(`。存在的理由:mac 侧有 12 处这样的串,产品与 Windows 只差标点宽度还不走词表
	/// (M11 只扫 `I18n.L("…")`,把它们全漏了)⇒ 这一通道必须单独扫。
	/// </summary>
	private static Dictionary<string, List<string>> CollectUnwrappedCjkLiterals(string root, string repoPrefix)
	{
		var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
		{
			if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			{
				continue;
			}

			string[] lines = C1aPaletteTests.StripComments(File.ReadAllText(file)).Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				foreach (Match match in AnyLiteralPattern.Matches(lines[i]))
				{
					string body = match.Groups[1].Value;
					if (!ContainsCjk(body))
					{
						continue;
					}

					string before = lines[i].Substring(0, match.Index);
					if (WrappedPrefix.IsMatch(before))
					{
						continue;   // already covered by the I18n.L scan
					}

					string tail = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/');
					string relative = repoPrefix + tail.Replace(Path.DirectorySeparatorChar, '/');
					if (!found.TryGetValue(body, out List<string>? places))
					{
						places = new List<string>();
						found[body] = places;
					}

					places.Add(relative + ":" + (i + 1));
				}
			}
		}

		return found;
	}

	/// <summary>
	/// 守卫本体:把 mac 侧的 `I18n.L` 字面量与 Windows 源 + mac 词表比对,
	/// **新出现**的漂移(不在台账里)一律报红;修好的条目只提示。
	/// </summary>
	[Fact]
	public void Ported_literals_keep_the_windows_punctuation_and_do_not_grow_beyond_the_baseline()
	{
		string macRoot = C1aFixtures.RepoRoot();
		string portRoot = Path.Combine(macRoot, "src", "TipCL.App");
		Dictionary<string, List<string>> literals = CollectLiterals(portRoot, PortDir + "/");
		HashSet<string> keys = LoadKeys(Path.Combine(macRoot, "src", "TipCL.Core", "Resources", "locales"));
		// 先断言"两边都真的读到了":否则"找不到源"会退化成空集比对、全绿 —— 那正是要防的假绿。
		Assert.True(literals.Count > 100, "expected the port tree to contain I18n.L literals, got " + literals.Count);

		string windowsPort = Path.Combine(C1aFixtures.WindowsRoot, "src", "TipCL");
		if (!Directory.Exists(windowsPort))
		{
			Console.WriteLine(
				"SKIP-NOTE: windows baseline not available, the literal cross-check was NOT run: " + windowsPort +
				" (the guard is skipped, not passed: the ledger ratchet needs a baseline to compare against)");
			return;
		}

		Dictionary<string, List<string>> windowsLiterals = CollectLiterals(windowsPort, "src/TipCL/");
		HashSet<string> windowsFolded = new HashSet<string>(windowsLiterals.Keys.Select(Fold), StringComparer.Ordinal);
		Dictionary<string, string> nearKeyByFold = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string key in keys)
		{
			nearKeyByFold[Fold(key)] = key;
		}

		List<Drift> drift = new List<Drift>();
		List<string> whitelisted = new List<string>();
		Dictionary<string, List<string>> unwrapped = CollectUnwrappedCjkLiterals(portRoot, PortDir + "/");

		void Collect(Dictionary<string, List<string>> source)
		{
			foreach (KeyValuePair<string, List<string>> entry in source)
			{
				if (Whitelist.ContainsKey(entry.Key))
				{
					whitelisted.Add(entry.Key + "  (" + entry.Value[0] + ")");
					continue;
				}

				if (!LooksLikeDrift(entry.Key, keys, nearKeyByFold, windowsFolded))
				{
					continue;
				}

				string place = entry.Value[0];
				drift.Add(new Drift
				{
					File = place.Substring(0, place.LastIndexOf(':')),
					Literal = entry.Key,
					NearKey = nearKeyByFold[Fold(entry.Key)],
					Place = place,
				});
			}
		}

		Collect(literals);    // wrapped channel: I18n.L("...")
		Collect(unwrapped);   // unwrapped channel: a CJK literal handed straight to Ui.*

		drift.Sort(static (a, b) => string.CompareOrdinal(a.Identity, b.Identity));
		string ledgerPath = Path.Combine(macRoot, LedgerRelative.Replace('/', Path.DirectorySeparatorChar));
		// 台账**可以**是空的:欠账修完之后 drift=0 且台账=0 就是成功终态(空台账 + 有漂移 ⇒ 全部被当成"新增" ⇒ 红,
		// 所以"台账被误删"不会静默降级,只会噪声变大)。要防的另一种假绿是"根本没比对":靠上面的
		// literals/keys 与这里的 windowsLiterals 计数去防。
		Assert.True(File.Exists(ledgerPath), "the ledger file is missing: " + LedgerRelative);
		Dictionary<string, string> ledger = LoadLedger(ledgerPath);
		Assert.True(windowsLiterals.Count > 100, "expected the windows baseline to contain I18n.L literals, got " + windowsLiterals.Count);

		List<Drift> fresh = drift.Where(d => !ledger.ContainsKey(d.Identity)).ToList();
		List<string> fixedUp = ledger.Keys.Where(identity => !drift.Any(d => d.Identity == identity)).ToList();

		Console.WriteLine(
			"literal-fidelity: literals=" + literals.Count + " unwrappedCjk=" + unwrapped.Count + " keys=" + keys.Count +
			" windowsLiterals=" + windowsLiterals.Count + " drift=" + drift.Count + " whitelisted=" + whitelisted.Count +
			" ledger=" + ledger.Count + " new=" + fresh.Count + " alreadyFixed=" + fixedUp.Count);
		foreach (string w in whitelisted)
		{
			Console.WriteLine("literal-fidelity whitelist: " + w + "  reason=" + Whitelist[w.Split(' ')[0]]);
		}
		// 影响面拆开报(修复优先级):含中文的条目会让**英文界面回退显示中文**;纯标点条目只是写法不一致。
		Console.WriteLine(
			"literal-fidelity impact: visuallyHarmful(contains CJK)=" + drift.Count(d => ContainsCjk(d.Literal)) +
			" cosmetic(punctuation only)=" + drift.Count(d => !ContainsCjk(d.Literal)));
		if (fixedUp.Count > 0)
		{
			Console.WriteLine(
				"NOTE: " + fixedUp.Count + " ledger entries no longer drift (good) - regenerate the ledger to shrink it: " + LedgerRelative);
		}

		string report = string.Join(Environment.NewLine, fresh.Take(400).Select(
			d => "  " + d.Place + "  mac " + Quote(d.Literal) + "  vs locale key " + Quote(d.NearKey)));
		Assert.True(
			fresh.Count == 0,
			"new literal drift (the mac literal is not a locale key, but a fold-equal key exists and Windows has the same string):" +
			Environment.NewLine + report + Environment.NewLine +
			"fix: use the Windows spelling (fullwidth punctuation) in the source literal - do NOT add a new ledger entry." +
			" (the list above is capped at 400 entries; regenerate the ledger with " +
			"_tmp/tasks/mac-tests/gen-literal-ledger.py against the same tree)");
	}

	/// <summary>
	/// 验证验证者(不依赖 Windows 源、也不依赖词表当前内容):用**合成词表 + 合成基线**把判定函数三个方向都钉住 ——
	/// 只差标点宽度的字面量必须被抓住,本身就是键的字面量必须放行,串本身不同的必须放行。
	/// 为什么用合成数据:判定函数是纯函数,合成数据能让这条用例**永远确定**(否则"修法换了方向"(例如有人改成
	/// 往词表里补半角别名)会让这条自测变成假红)。真实语料上的可证伪性由隔离树里的注入实验给出(见报告)。
	/// 最后再读一次**真实**词表:证明"找不到源"不是因为词表空 —— 那是另一种假绿。
	/// </summary>
	[Fact]
	public void Detector_flags_a_width_only_drift_and_accepts_the_key()
	{
		string fullwidth = "(\u53EF\u591A\u9009)".Replace('(', '\uFF08').Replace(')', '\uFF09');
		string ascii = "(\u53EF\u591A\u9009)";
		var keys = new HashSet<string>(new[] { fullwidth }, StringComparer.Ordinal);
		var nearKeyByFold = new Dictionary<string, string>(StringComparer.Ordinal) { [Fold(fullwidth)] = fullwidth };
		var windowsFolded = new HashSet<string>(new[] { Fold(fullwidth) }, StringComparer.Ordinal);

		// 方向 1:半角字面量(源码被重打成 ASCII)必须被判为漂移。
		Assert.False(keys.Contains(ascii), "precondition: the ASCII spelling must NOT be a key in this synthetic table");
		Assert.True(
			LooksLikeDrift(ascii, keys, nearKeyByFold, windowsFolded),
			"the detector failed to flag a width-only drift ((...) vs (...)) - the guard would be blind");

		// 方向 2:与键逐字相同的字面量必须放行(否则守卫会把对的也报红)。
		Assert.False(
			LooksLikeDrift(fullwidth, keys, nearKeyByFold, windowsFolded),
			"the detector flagged a literal that IS the locale key");

		// 方向 3:折叠后仍不相等的字面量不算宽度漂移(那是"串本身不同",不是本守卫的判定范围)。
		Assert.False(
			LooksLikeDrift("(\u5B8C\u5168\u4E0D\u540C\u7684\u53E5\u5B50)", keys, nearKeyByFold, windowsFolded),
			"the detector flagged an unrelated literal");

		// 方向 4:真实词表必须真的读到了(空词表会让守卫静默失去意义)。
		HashSet<string> realKeys = LoadKeys(Path.Combine(C1aFixtures.RepoRoot(), "src", "TipCL.Core", "Resources", "locales"));
		Console.WriteLine("literal-fidelity self-test: real locale keys=" + realKeys.Count +
			" keysWithFullwidthPunctuation=" + realKeys.Count(static k => k.IndexOf('\uFF08') >= 0 || k.IndexOf('\uFF1A') >= 0 || k.IndexOf('\uFF0C') >= 0));
		Assert.True(realKeys.Count > 1000, "the real locale tables did not load (keys=" + realKeys.Count + ")");
	}

	private static string Quote(string text)
	{
		StringBuilder builder = new StringBuilder("\"");
		foreach (char ch in text)
		{
			builder.Append(ch < ' ' || ch == '\\' || ch == '"' ? "\\u" + ((int)ch).ToString("X4") : ch.ToString());
		}

		return builder.Append('"').ToString();
	}

	/// <summary>字面量里有没有中日韩表意文字(有 ⇒ 英文界面回退时会**看到中文**,不只是标点写法不同)。</summary>
	private static bool ContainsCjk(string text)
	{
		return text.Any(static ch => ch >= '\u4E00' && ch <= '\u9FFF');
	}
}
