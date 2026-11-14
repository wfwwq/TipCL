using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:Markdown 解析器(= Windows <c>Pages\Ai\MdParser.cs</c>,465 行)—— **纯 C#,零 UI 依赖** ⇒ 逐字照搬,
/// 只把局部变量名写清、把 Windows 的行号注在注释里。渲染那一半在 <c>MdView</c>(C8a 里按"块级渲染器"重写,
/// 因为 Avalonia 11 没有 WPF 的 <c>FlowDocument</c>/<c>Paragraph</c>/<c>RichTextBox</c>)。
///
/// 产出结构:<see cref="MdBlockKind"/> **八类块**(Paragraph / Heading / Bullet / Ordered / Quote / Code / Table / Rule)+
/// <see cref="MdInlineKind"/> **七类行内**(Text / Bold / Italic / Code / Link / Strike / Break)。
///
/// 保真度:正则、判据顺序、缩进算法(制表符按 4 空格、深度夹在 0~4)、代码围栏的收尾规则、
/// 表格对齐推断、反斜杠转义集合、行内嵌套深度上限 6 —— **一条都没改**(这些是 Windows 的行为面,改了就与真机不一致)。
/// </summary>
internal static class MdParser
{
	/// <summary>【Windows 源】<c>:10</c> 有序列表项。</summary>
	private static readonly Regex Ordered = new Regex("^(\\s*)(\\d{1,9})[.)]\\s+(.*)$", RegexOptions.Compiled);

	/// <summary>【Windows 源】<c>:12</c> 无序列表项。</summary>
	private static readonly Regex Bullet = new Regex("^(\\s*)[-*+]\\s+(.*)$", RegexOptions.Compiled);

	/// <summary>【Windows 源】<c>:14</c> 标题(<c>#</c> ~ <c>######</c>,尾部可有闭合井号)。</summary>
	private static readonly Regex Heading = new Regex("^(#{1,6})\\s+(.*?)\\s*#*\\s*$", RegexOptions.Compiled);

	/// <summary>【Windows 源】<c>:16</c> 分隔线(<c>---</c> / <c>***</c> / <c>___</c>,最多 3 个前导空格)。</summary>
	private static readonly Regex RuleRx = new Regex("^\\s{0,3}([-*_])(\\s*\\1){2,}\\s*$", RegexOptions.Compiled);

	/// <summary>【Windows 源】<c>:18</c> 表格的分隔行。</summary>
	private static readonly Regex TableSep = new Regex("^\\s*\\|?\\s*:?-+:?\\s*(\\|\\s*:?-+:?\\s*)*\\|?\\s*$", RegexOptions.Compiled);

	/// <summary>【Windows 源】<c>:20-217</c>:把一个 Markdown 文本切成块序列。</summary>
	public static List<MdBlock> Parse(string? text)
	{
		string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		List<MdBlock> blocks = new List<MdBlock>();
		int i = 0;
		while (i < lines.Length)
		{
			string raw = lines[i];
			string line = raw.Trim();
			if (line.Length == 0)
			{
				i++;
				continue;
			}
			// ---- 代码围栏(``` 或 ~~~);收尾判据照 Windows:整行 Trim 后 StartsWith(前三个字符) ----
			if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
			{
				string fence = line.Substring(0, 3);
				string lang = line.Substring(3).Trim();
				StringBuilder code = new StringBuilder();
				for (i++; i < lines.Length && !lines[i].Trim().StartsWith(fence, StringComparison.Ordinal); i++)
				{
					if (code.Length > 0)
					{
						code.Append('\n');
					}
					code.Append(lines[i]);
				}
				i++;
				blocks.Add(new MdBlock
				{
					Kind = MdBlockKind.Code,
					Lang = lang,
					Raw = code.ToString(),
				});
				continue;
			}
			// ---- 标题 ----
			Match m = Heading.Match(line);
			if (m.Success)
			{
				blocks.Add(new MdBlock
				{
					Kind = MdBlockKind.Heading,
					Level = m.Groups[1].Value.Length,
					Inlines = ParseInline(m.Groups[2].Value),
				});
				i++;
				continue;
			}
			// ---- 分隔线(注意:判据用的是**未 Trim** 的原行,与 Windows 一致) ----
			if (RuleRx.IsMatch(raw))
			{
				blocks.Add(new MdBlock
				{
					Kind = MdBlockKind.Rule,
				});
				i++;
				continue;
			}
			// ---- 表格:本行含 | 且下一行是分隔行 ----
			if (line.Contains("|") && i + 1 < lines.Length && TableSep.IsMatch(lines[i + 1]))
			{
				MdBlock table = new MdBlock
				{
					Kind = MdBlockKind.Table,
				};
				foreach (string cell in SplitRow(line))
				{
					table.Header.Add(ParseInline(cell));
				}
				foreach (string spec in SplitRow(lines[i + 1].Trim()))
				{
					string s = spec.Trim();
					table.Align.Add((s.StartsWith(":") && s.EndsWith(":")) ? "center" : (s.EndsWith(":") ? "right" : "left"));
				}
				for (i += 2; i < lines.Length && lines[i].Trim().Length > 0 && lines[i].Contains("|"); i++)
				{
					List<List<MdInline>> cells = new List<List<MdInline>>();
					foreach (string cell2 in SplitRow(lines[i].Trim()))
					{
						cells.Add(ParseInline(cell2));
					}
					// 列数与表头对齐(少了补空、多了砍掉)
					while (cells.Count < table.Header.Count)
					{
						cells.Add(new List<MdInline>());
					}
					if (cells.Count > table.Header.Count)
					{
						cells.RemoveRange(table.Header.Count, cells.Count - table.Header.Count);
					}
					table.Rows.Add(cells);
				}
				blocks.Add(table);
				continue;
			}
			// ---- 引用:连续的 > 行,去掉一个 > 与一个可选空格,再递归解析 ----
			if (line.StartsWith(">", StringComparison.Ordinal))
			{
				StringBuilder quote = new StringBuilder();
				for (; i < lines.Length && lines[i].Trim().StartsWith(">", StringComparison.Ordinal); i++)
				{
					string inner = lines[i].Trim().Substring(1);
					if (inner.StartsWith(" "))
					{
						inner = inner.Substring(1);
					}
					quote.Append(inner).Append('\n');
				}
				blocks.Add(new MdBlock
				{
					Kind = MdBlockKind.Quote,
					Children = Parse(quote.ToString()),
				});
				continue;
			}
			// ---- 列表(有序 / 无序) ----
			Match ordered = Ordered.Match(raw);
			Match bullet = Bullet.Match(raw);
			if (ordered.Success || bullet.Success)
			{
				bool isOrdered = ordered.Success;
				MdBlock list = new MdBlock
				{
					Kind = (isOrdered ? MdBlockKind.Ordered : MdBlockKind.Bullet),
				};
				if (isOrdered)
				{
					int.TryParse(ordered.Groups[2].Value, out list.Start);
				}
				while (i < lines.Length)
				{
					Match o = Ordered.Match(lines[i]);
					Match b = Bullet.Match(lines[i]);
					Match same = (isOrdered ? o : b);
					if (same.Success)
					{
						// 缩进:制表符按 4 空格算,深度 = min(长度/2, 4)
						int indent = same.Groups[1].Value.Replace("\t", "    ").Length;
						string body = (isOrdered ? same.Groups[3].Value : same.Groups[2].Value);
						list.Items.Add(ParseInline(body));
						list.ItemDepth.Add(Math.Min(indent / 2, 4));
						i++;
						continue;
					}
					// 另一种标记 + 有真实缩进 ⇒ 当作嵌套项(深度至少 1)
					if ((isOrdered ? b : o).Success && lines[i].Length - lines[i].TrimStart().Length >= 2)
					{
						Match other = (isOrdered ? b : o);
						int indent2 = other.Groups[1].Value.Replace("\t", "    ").Length;
						list.Items.Add(ParseInline(isOrdered ? other.Groups[2].Value : other.Groups[3].Value));
						list.ItemDepth.Add(Math.Max(1, Math.Min(indent2 / 2, 4)));
						i++;
						continue;
					}
					// 续行:非空 + 至少两个前导空格 + 已经有列表项 ⇒ 接在最后一项后面
					if (lines[i].Trim().Length <= 0 || !lines[i].StartsWith("  ", StringComparison.Ordinal) || list.Items.Count <= 0)
					{
						break;
					}
					List<MdInline> last = list.Items[list.Items.Count - 1];
					last.Add(new MdInline
					{
						Kind = MdInlineKind.Text,
						Text = " ",
					});
					last.AddRange(ParseInline(lines[i].Trim()));
					i++;
				}
				blocks.Add(list);
				continue;
			}
			// ---- 段落:吃到下一个块开始为止;跨行之间插一个 Break ----
			List<string> para = new List<string>();
			for (; i < lines.Length; i++)
			{
				string cur = lines[i];
				string trimmed = cur.Trim();
				if (trimmed.Length == 0 || trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal) ||
					Heading.IsMatch(trimmed) || trimmed.StartsWith(">", StringComparison.Ordinal) ||
					(para.Count > 0 && (Ordered.IsMatch(cur) || Bullet.IsMatch(cur))) ||
					(trimmed.Contains("|") && i + 1 < lines.Length && TableSep.IsMatch(lines[i + 1])) ||
					(para.Count > 0 && RuleRx.IsMatch(cur)))
				{
					break;
				}
				para.Add(trimmed);
			}
			if (para.Count == 0)
			{
				para.Add(line);
				i++;
			}
			MdBlock paragraph = new MdBlock
			{
				Kind = MdBlockKind.Paragraph,
			};
			for (int j = 0; j < para.Count; j++)
			{
				if (j > 0)
				{
					paragraph.Inlines.Add(new MdInline
					{
						Kind = MdInlineKind.Break,
					});
				}
				paragraph.Inlines.AddRange(ParseInline(para[j]));
			}
			blocks.Add(paragraph);
		}
		return blocks;
	}

	/// <summary>【Windows 源】<c>:219-261</c>:按未转义的 <c>|</c> 切表格行(反引号里的 <c>|</c> 不算分隔)。</summary>
	internal static List<string> SplitRow(string row)
	{
		string s = row.Trim();
		if (s.StartsWith("|"))
		{
			s = s.Substring(1);
		}
		if (s.EndsWith("|") && !s.EndsWith("\\|"))
		{
			s = s.Substring(0, s.Length - 1);
		}
		List<string> cells = new List<string>();
		StringBuilder buf = new StringBuilder();
		bool inCode = false;
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (c == '\\' && i + 1 < s.Length && s[i + 1] == '|')
			{
				buf.Append('|');
				i++;
				continue;
			}
			switch (c)
			{
				case '`':
					inCode = !inCode;
					buf.Append(c);
					continue;
				case '|':
					if (!inCode)
					{
						cells.Add(buf.ToString().Trim());
						buf.Clear();
						continue;
					}
					break;
			}
			buf.Append(c);
		}
		cells.Add(buf.ToString().Trim());
		return cells;
	}

	/// <summary>【Windows 源】<c>:263-268</c>:解析一段行内(结果会先合并相邻纯文本)。</summary>
	public static List<MdInline> ParseInline(string? text)
	{
		List<MdInline> list = new List<MdInline>();
		ParseInto(text ?? "", list, 0);
		return Merge(list);
	}

	/// <summary>【Windows 源】<c>:270-399</c>:行内解析主体(转义 / 行内码 / 粗体 / 删除线 / 斜体 / 链接)。</summary>
	private static void ParseInto(string s, List<MdInline> outList, int depth)
	{
		StringBuilder buf = new StringBuilder();
		int at = 0;
		while (at < s.Length)
		{
			char c = s[at];
			// 转义:反斜杠 + 下列任一字符 ⇒ 输出该字符本身
			if (c == '\\' && at + 1 < s.Length && "\\`*_[]()#+-.!|~>".IndexOf(s[at + 1]) >= 0)
			{
				buf.Append(s[at + 1]);
				at += 2;
				continue;
			}
			// 行内码:同长度反引号包起来;找不到闭合就当普通字符走下面的分支
			if (c == '`')
			{
				int run;
				for (run = 1; at + run < s.Length && s[at + run] == '`'; run++)
				{
				}
				string mark = new string('`', run);
				int close = s.IndexOf(mark, at + run, StringComparison.Ordinal);
				if (close > 0)
				{
					Flush();
					string code = s.Substring(at + run, close - at - run);
					// 两端各一个空格时各去掉一个(与 Windows 一致)
					if (code.Length >= 2 && code[0] == ' ' && code[code.Length - 1] == ' ')
					{
						code = code.Substring(1, code.Length - 2);
					}
					outList.Add(new MdInline
					{
						Kind = MdInlineKind.Code,
						Text = code,
					});
					at = close + run;
					continue;
				}
			}
			// 粗体:** 或 __
			if (depth < 6 && (c == '*' || c == '_') && at + 1 < s.Length && s[at + 1] == c)
			{
				string mark = new string(c, 2);
				int close2 = FindClose(s, mark, at + 2);
				if (close2 > at + 2)
				{
					Flush();
					MdInline bold = new MdInline
					{
						Kind = MdInlineKind.Bold,
					};
					ParseInto(s.Substring(at + 2, close2 - at - 2), bold.Children, depth + 1);
					outList.Add(bold);
					at = close2 + 2;
					continue;
				}
			}
			// 删除线:~~
			if (depth < 6 && c == '~' && at + 1 < s.Length && s[at + 1] == '~')
			{
				int close3 = FindClose(s, "~~", at + 2);
				if (close3 > at + 2)
				{
					Flush();
					MdInline strike = new MdInline
					{
						Kind = MdInlineKind.Strike,
					};
					ParseInto(s.Substring(at + 2, close3 - at - 2), strike.Children, depth + 1);
					outList.Add(strike);
					at = close3 + 2;
					continue;
				}
			}
			// 斜体:* 或 _(带词边界保护,与 Windows 的两处判据一致)
			if (depth < 6 && (c == '*' || c == '_') && at + 1 < s.Length && s[at + 1] != ' ' && s[at + 1] != c)
			{
				int close4 = ((c == '*' || at == 0 || !char.IsLetterOrDigit(s[at - 1])) ? FindClose(s, c.ToString(), at + 1) : (-1));
				if (close4 > at + 1 && s[close4 - 1] != ' ' && (c == '*' || close4 + 1 >= s.Length || !char.IsLetterOrDigit(s[close4 + 1])))
				{
					Flush();
					MdInline italic = new MdInline
					{
						Kind = MdInlineKind.Italic,
					};
					ParseInto(s.Substring(at + 1, close4 - at - 1), italic.Children, depth + 1);
					outList.Add(italic);
					at = close4 + 1;
					continue;
				}
			}
			// 链接:[文本](url) —— 文本里不许有换行、url 里不许有空格
			if (c == '[')
			{
				int mid = s.IndexOf("](", at + 1, StringComparison.Ordinal);
				if (mid > at)
				{
					int end = s.IndexOf(')', mid + 2);
					if (end > mid)
					{
						string label = s.Substring(at + 1, mid - at - 1);
						string url = s.Substring(mid + 2, end - mid - 2).Trim();
						if (label.IndexOf('\n') < 0 && url.IndexOf(' ') < 0)
						{
							Flush();
							MdInline link = new MdInline
							{
								Kind = MdInlineKind.Link,
								Url = url,
							};
							ParseInto(label, link.Children, depth + 1);
							outList.Add(link);
							at = end + 1;
							continue;
						}
					}
				}
			}
			buf.Append(c);
			at++;
		}
		Flush();

		void Flush()
		{
			if (buf.Length > 0)
			{
				outList.Add(new MdInline
				{
					Kind = MdInlineKind.Text,
					Text = buf.ToString(),
				});
				buf.Clear();
			}
		}
	}

	/// <summary>【Windows 源】<c>:401-425</c>:找闭合标记,遇到反引号先跳到它自己的闭合处(行内码优先)。</summary>
	private static int FindClose(string s, string mark, int from)
	{
		int at = from;
		while (at <= s.Length - mark.Length)
		{
			if (s[at] == '`')
			{
				int next = s.IndexOf('`', at + 1);
				if (next < 0)
				{
					return -1;
				}
				at = next + 1;
			}
			else
			{
				if (string.CompareOrdinal(s, at, mark, 0, mark.Length) == 0)
				{
					return at;
				}
				at++;
			}
		}
		return -1;
	}

	/// <summary>【Windows 源】<c>:427-442</c>:合并相邻的纯文本段。</summary>
	private static List<MdInline> Merge(List<MdInline> list)
	{
		List<MdInline> merged = new List<MdInline>();
		foreach (MdInline item in list)
		{
			if (item.Kind == MdInlineKind.Text && merged.Count > 0 && merged[merged.Count - 1].Kind == MdInlineKind.Text)
			{
				merged[merged.Count - 1].Text += item.Text;
			}
			else
			{
				merged.Add(item);
			}
		}
		return merged;
	}

	/// <summary>【Windows 源】<c>:444-464</c>:行内序列 → 纯文本(换行算 <c>\n</c>)。</summary>
	public static string PlainText(IEnumerable<MdInline> inlines)
	{
		StringBuilder sb = new StringBuilder();
		foreach (MdInline inline in inlines)
		{
			switch (inline.Kind)
			{
				case MdInlineKind.Text:
				case MdInlineKind.Code:
					sb.Append(inline.Text);
					break;
				case MdInlineKind.Break:
					sb.Append('\n');
					break;
				default:
					sb.Append(PlainText(inline.Children));
					break;
			}
		}
		return sb.ToString();
	}
}
