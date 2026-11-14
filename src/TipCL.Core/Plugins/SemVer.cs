// 来源: Windows 版参考仓库 src/TipCL/Core\Plugins\SemVer.cs（M2-B0 续搬）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TipCL.Core.Plugins;

/// <summary>
/// 极简 semver：解析 <c>1.2.3-beta.1+build</c>，比较大小，判断是否满足范围。
/// 范围支持：<c>*</c> / 空、<c>1.2.3</c>（精确）、<c>=</c> <c>&gt;</c> <c>&gt;=</c> <c>&lt;</c> <c>&lt;=</c>、
/// <c>^1.2.3</c>、<c>~1.2.3</c>、<c>1.x</c> / <c>1.2.*</c>、空格分隔表示“且”、<c>||</c> 表示“或”、<c>1.0.0 - 2.0.0</c>。
/// 缺的部分按 0 补（<c>&gt;=22</c> 等价 <c>&gt;=22.0.0</c>），前导 <c>v</c> 忽略。
/// </summary>
public sealed class SemVer : IComparable<SemVer>
{
	public readonly long Major;

	public readonly long Minor;

	public readonly long Patch;

	/// <summary>预发布标识（点分隔），空表示正式版。</summary>
	public readonly string[] Pre;

	private SemVer(long major, long minor, long patch, string[] pre)
	{
		Major = major;
		Minor = minor;
		Patch = patch;
		Pre = pre ?? new string[0];
	}

	public static SemVer Of(long major, long minor = 0, long patch = 0)
	{
		return new SemVer(major, minor, patch, null);
	}

	/// <summary>解析失败返回 null。允许只写 <c>1</c> 或 <c>1.2</c>（缺的补 0）。</summary>
	public static SemVer TryParse(string s)
	{
		if (s == null)
		{
			return null;
		}
		s = s.Trim();
		if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
		{
			s = s.Substring(1);
		}
		if (s.Length == 0)
		{
			return null;
		}
		int plus = s.IndexOf('+');
		if (plus >= 0)
		{
			s = s.Substring(0, plus);
		}
		string[] pre = null;
		int dash = s.IndexOf('-');
		if (dash >= 0)
		{
			string p = s.Substring(dash + 1);
			s = s.Substring(0, dash);
			if (p.Length == 0)
			{
				return null;
			}
			pre = p.Split('.');
		}
		string[] parts = s.Split('.');
		if (parts.Length < 1 || parts.Length > 3)
		{
			return null;
		}
		long[] n = new long[3];
		for (int i = 0; i < parts.Length; i++)
		{
			if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i]))
			{
				return null;
			}
		}
		return new SemVer(n[0], n[1], n[2], pre);
	}

	public static SemVer Parse(string s)
	{
		return TryParse(s) ?? throw new ValueError("不是合法的版本号：" + s);
	}

	public static bool IsValid(string s)
	{
		return TryParse(s) != null;
	}

	public int CompareTo(SemVer other)
	{
		if (other == null)
		{
			return 1;
		}
		int c = Major.CompareTo(other.Major);
		if (c != 0)
		{
			return c;
		}
		c = Minor.CompareTo(other.Minor);
		if (c != 0)
		{
			return c;
		}
		c = Patch.CompareTo(other.Patch);
		if (c != 0)
		{
			return c;
		}
		// 有预发布的比正式版小
		if (Pre.Length == 0 && other.Pre.Length == 0)
		{
			return 0;
		}
		if (Pre.Length == 0)
		{
			return 1;
		}
		if (other.Pre.Length == 0)
		{
			return -1;
		}
		for (int i = 0; i < Math.Min(Pre.Length, other.Pre.Length); i++)
		{
			string a = Pre[i];
			string b = other.Pre[i];
			bool an = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out long ai);
			bool bn = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out long bi);
			if (an && bn)
			{
				c = ai.CompareTo(bi);
			}
			else if (an)
			{
				c = -1;
			}
			else if (bn)
			{
				c = 1;
			}
			else
			{
				c = string.CompareOrdinal(a, b);
			}
			if (c != 0)
			{
				return Math.Sign(c);
			}
		}
		return Pre.Length.CompareTo(other.Pre.Length);
	}

	/// <summary>比较两个版本字符串；解析不了的按字符串比较。</summary>
	public static int Compare(string a, string b)
	{
		SemVer x = TryParse(a);
		SemVer y = TryParse(b);
		if (x != null && y != null)
		{
			return x.CompareTo(y);
		}
		return string.CompareOrdinal(a ?? "", b ?? "");
	}

	public override string ToString()
	{
		string s = Major + "." + Minor + "." + Patch;
		if (Pre.Length > 0)
		{
			s = s + "-" + string.Join(".", Pre);
		}
		return s;
	}

	/// <summary>版本是否满足范围。范围写错时返回 false。</summary>
	public static bool Satisfies(string version, string range)
	{
		SemVer v = TryParse(version);
		if (v == null)
		{
			return false;
		}
		try
		{
			return SatisfiesRange(v, range);
		}
		catch (FormatException)
		{
			return false;
		}
	}

	/// <summary>范围本身是否写对了。</summary>
	public static bool IsValidRange(string range)
	{
		try
		{
			SatisfiesRange(Of(0), range);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private static bool SatisfiesRange(SemVer v, string range)
	{
		range = (range ?? "").Trim();
		if (range.Length == 0 || range == "*" || range.Equals("x", StringComparison.OrdinalIgnoreCase) || range == "latest")
		{
			return true;
		}
		bool any = false;
		foreach (string alt0 in range.Split(new[] { "||" }, StringSplitOptions.None))
		{
			string alt = alt0.Trim();
			if (alt.Length == 0)
			{
				throw new FormatException("empty range part");
			}
			if (SatisfiesSet(v, alt))
			{
				any = true;
			}
		}
		return any;
	}

	private static bool SatisfiesSet(SemVer v, string set)
	{
		// 连字符范围 a - b
		int hy = set.IndexOf(" - ", StringComparison.Ordinal);
		if (hy >= 0)
		{
			SemVer lo = ParsePartial(set.Substring(0, hy).Trim(), out _);
			string hiText = set.Substring(hy + 3).Trim();
			SemVer hi = ParsePartial(hiText, out int hiParts);
			bool okLo = v.CompareTo(lo) >= 0;
			bool okHi = hiParts >= 3 ? v.CompareTo(hi) <= 0 : v.CompareTo(BumpAt(hi, hiParts)) < 0;
			return okLo && okHi;
		}
		// 把 ">= 1.2" 这种运算符和版本中间有空格的写法并起来
		List<string> toks = new List<string>();
		foreach (string t in set.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (toks.Count > 0 && IsBareOp(toks[toks.Count - 1]))
			{
				toks[toks.Count - 1] += t;
			}
			else
			{
				toks.Add(t);
			}
		}
		foreach (string tok in toks)
		{
			if (!SatisfiesComparator(v, tok))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsBareOp(string t)
	{
		return t == ">" || t == ">=" || t == "<" || t == "<=" || t == "=" || t == "^" || t == "~";
	}

	private static bool SatisfiesComparator(SemVer v, string c)
	{
		string op = "";
		foreach (string o in new[] { ">=", "<=", ">", "<", "=", "^", "~" })
		{
			if (c.StartsWith(o, StringComparison.Ordinal))
			{
				op = o;
				c = c.Substring(o.Length).Trim();
				break;
			}
		}
		if (c.Length == 0 || c == "*" || c.Equals("x", StringComparison.OrdinalIgnoreCase))
		{
			if (op == "<" || op == ">")
			{
				return false;
			}
			return true;
		}
		SemVer b = ParsePartial(c, out int parts);
		switch (op)
		{
		case ">=":
			return v.CompareTo(b) >= 0;
		case ">":
			return parts >= 3 ? v.CompareTo(b) > 0 : v.CompareTo(BumpAt(b, parts)) >= 0;
		case "<":
			return v.CompareTo(b) < 0;
		case "<=":
			return parts >= 3 ? v.CompareTo(b) <= 0 : v.CompareTo(BumpAt(b, parts)) < 0;
		case "^":
		{
			SemVer upper;
			if (b.Major > 0 || parts == 1)
			{
				upper = Of(b.Major + 1);
			}
			else if (b.Minor > 0 || parts == 2)
			{
				upper = Of(0, b.Minor + 1);
			}
			else
			{
				upper = Of(0, 0, b.Patch + 1);
			}
			return v.CompareTo(b) >= 0 && v.CompareTo(upper) < 0 && PreOk(v, b);
		}
		case "~":
		{
			SemVer upper = parts == 1 ? Of(b.Major + 1) : Of(b.Major, b.Minor + 1);
			return v.CompareTo(b) >= 0 && v.CompareTo(upper) < 0 && PreOk(v, b);
		}
		default:
			// 精确或 1.x 这类通配
			if (parts >= 3)
			{
				return v.CompareTo(b) == 0;
			}
			return v.CompareTo(b) >= 0 && v.CompareTo(BumpAt(b, parts)) < 0;
		}
	}

	/// <summary>预发布版只在同一 major.minor.patch 下才能匹配带预发布的范围（npm 规则的简化版）。</summary>
	private static bool PreOk(SemVer v, SemVer b)
	{
		if (v.Pre.Length == 0)
		{
			return true;
		}
		return v.Major == b.Major && v.Minor == b.Minor && v.Patch == b.Patch && b.Pre.Length > 0;
	}

	/// <summary>parts=1 → major+1；parts=2 → minor+1。</summary>
	private static SemVer BumpAt(SemVer b, int parts)
	{
		if (parts <= 1)
		{
			return Of(b.Major + 1);
		}
		return Of(b.Major, b.Minor + 1);
	}

	/// <summary>解析可能不完整/带通配的版本：<c>1</c>、<c>1.2</c>、<c>1.x</c>、<c>1.2.*</c>。parts = 实际给出的段数。</summary>
	private static SemVer ParsePartial(string s, out int parts)
	{
		s = s.Trim();
		if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
		{
			s = s.Substring(1);
		}
		string core = s;
		string rest = "";
		int dash = s.IndexOf('-');
		int plus = s.IndexOf('+');
		int cut = dash >= 0 ? dash : plus;
		if (cut >= 0)
		{
			core = s.Substring(0, cut);
			rest = s.Substring(cut);
		}
		string[] segs = core.Split('.');
		List<string> real = new List<string>();
		foreach (string seg in segs)
		{
			if (seg == "x" || seg == "X" || seg == "*")
			{
				break;
			}
			real.Add(seg);
		}
		parts = real.Count;
		if (parts == 0 || parts > 3)
		{
			throw new FormatException("bad version: " + s);
		}
		string full = string.Join(".", real.Concat(Enumerable.Repeat("0", 3 - parts)));
		if (parts == 3)
		{
			full += rest;
		}
		SemVer v = TryParse(full);
		if (v == null)
		{
			throw new FormatException("bad version: " + s);
		}
		return v;
	}
}
