using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TipCL.Tests;

// 源码里用到的每个图标码位（\uE000-\uF8FF）都必须在内置字体 TipCLIcons.ttf 里，
// 否则缺 Segoe 图标字体的系统（Win7/8.1、精简版、Wine）会显示成方框。
// C1a 恢复本文件时只做了 A6 约定的 B 类路径适配(逐条列在 PROGRESS.md 的 C1a 小节):
//   1. 仓库根标记 TipCL.sln -> TipCL.Mac.sln;
//   2. 字体资源路径 src/TipCL/... -> src/TipCL.App/...;
//   3. 扫描根 src/TipCL(Windows 版里同时含界面与 Core)-> src/TipCL.App + src/TipCL.Core。
// 断言本身一个字未改。
public class IconFontTests
{
	static string RepoRoot()
	{
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !File.Exists(Path.Combine(d.FullName, "TipCL.Mac.sln"))) d = d.Parent;
		Assert.NotNull(d);
		return d.FullName;
	}

	static HashSet<int> FontCodepoints(string path)
	{
		byte[] b = File.ReadAllBytes(path);
		int U16(int o) => (b[o] << 8) | b[o + 1];
		uint U32(int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
		int numTables = U16(4), cmap = -1;
		for (int i = 0; i < numTables; i++)
		{
			int r = 12 + 16 * i;
			if (b[r] == 'c' && b[r + 1] == 'm' && b[r + 2] == 'a' && b[r + 3] == 'p') cmap = (int)U32(r + 8);
		}
		Assert.True(cmap >= 0, "cmap table missing");
		var set = new HashSet<int>();
		int n = U16(cmap + 2);
		for (int i = 0; i < n; i++)
		{
			int sub = cmap + (int)U32(cmap + 4 + 8 * i + 4);
			int fmt = U16(sub);
			if (fmt == 4)
			{
				int segX2 = U16(sub + 6);
				int ends = sub + 14, starts = ends + segX2 + 2;
				for (int s = 0; s < segX2 / 2; s++)
					for (int c = U16(starts + 2 * s); c <= U16(ends + 2 * s) && c != 0xFFFF; c++) set.Add(c);
			}
			else if (fmt == 12)
			{
				uint groups = U32(sub + 12);
				for (int g = 0; g < groups; g++)
				{
					int o = sub + 16 + 12 * g;
					for (uint c = U32(o); c <= U32(o + 4); c++) set.Add((int)c);
				}
			}
		}
		return set;
	}

	[Fact]
	public void EveryGlyphUsedInSourceIsInEmbeddedFont()
	{
		string root = RepoRoot();
		var font = FontCodepoints(Path.Combine(root, "src", "TipCL.App", "Resources", "fonts", "TipCLIcons.ttf"));
		var used = new SortedDictionary<int, string>();
		var esc = new Regex(@"\\u([Ee][0-9A-Fa-f]{3}|[Ff][0-8][0-9A-Fa-f]{2})");
		var roots = new[]
		{
			Path.Combine(root, "src", "TipCL.App"),
			Path.Combine(root, "src", "TipCL.Core"),
		};
		foreach (var f in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.*", SearchOption.AllDirectories))
			.Where(p => (p.EndsWith(".cs") || p.EndsWith(".xaml")) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
		{
			string text = File.ReadAllText(f);
			foreach (Match m in esc.Matches(text)) used.TryAdd(Convert.ToInt32(m.Groups[1].Value, 16), f);
			foreach (char ch in text) if (ch >= '\uE000' && ch <= '\uF8FF') used.TryAdd(ch, f);
		}
		var missing = used.Where(kv => !font.Contains(kv.Key)).Select(kv => $"U+{kv.Key:X4} ({Path.GetFileName(kv.Value)})").ToList();
		Assert.True(missing.Count == 0, "内置图标字体缺少这些码位，见 tools/iconfont/README.md：" + string.Join(", ", missing));
	}
}
