using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 自定义布局 v3(2026-11-20)的守卫。界面是 WPF,net8 测试宿主实例化不了,所以与
/// <see cref="LayoutPageSplitTests"/> / LayoutShellTests 同一套做法:按**源码文本**断言接线事实,
/// 再加一道词表守卫(v3 新增的 I18n.L 文案必须在 en.json / zh_CN.json 里都有键)。
///   1) 设置 → 布局 的界面文案里没有内部说法(「桥接」「标题栏:」文件名「(L6)」);
///   2) 编辑态「⋯」菜单是「导出本页… / 导入到本页… / 整套布局与方案…」,设置里叫「导出整套布局…」;
///   3) 约定入口 LayoutShellHooks.CanEditPage / BeginEditPage 存在,单块页门槛是 specs ≥ 2;
///   4) v3 涉及文件里的每个中文 I18n.L 字面量都在两份词表里(缺键 = 英文界面显示中文)。
/// </summary>
public class LayoutV3SourceTests
{
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

	private static string Src(string rel)
	{
		return File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static readonly Regex LiteralOfL = new Regex("I18n\\.L\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

	private static readonly Regex Cjk = new Regex("[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]", RegexOptions.Compiled);

	private static string Unescape(string s)
	{
		StringBuilder sb = new StringBuilder(s.Length);
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (c != '\\' || i + 1 >= s.Length)
			{
				sb.Append(c);
				continue;
			}
			char n = s[++i];
			switch (n)
			{
			case 'n': sb.Append('\n'); break;
			case 't': sb.Append('\t'); break;
			case 'r': sb.Append('\r'); break;
			case '0': sb.Append('\0'); break;
			case 'u':
				if (i + 4 < s.Length)
				{
					sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
					i += 4;
				}
				break;
			default: sb.Append(n); break;
			}
		}
		return sb.ToString();
	}

	private static List<string> Literals(string source)
	{
		return LiteralOfL.Matches(source).Cast<Match>().Select((Match m) => Unescape(m.Groups[1].Value)).ToList();
	}

	private static HashSet<string> LocaleKeys(string file)
	{
		using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "src", "TipCL", "Resources", "locales", file)));
		HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty p in json.RootElement.EnumerateObject())
		{
			keys.Add(p.Name);
		}
		return keys;
	}

	[Fact]
	public void SettingsLayoutUiStringsCarryNoInternalJargon()
	{
		// v3 §1.B-6:文件名、「桥接」、#编号、「(L6)」这类说法不许出现在界面文案里(源码注释不算)
		List<string> ui = Literals(Src("src/TipCL/Pages/SettingsLayoutPage.cs"));
		Assert.NotEmpty(ui);
		foreach (string word in new string[5] { "桥接", "标题栏:", "标题栏：", "ui_layout_v2.json", "(L6)" })
		{
			Assert.DoesNotContain(ui, (string s) => s.Contains(word));
		}
		// 设置这边的导出叫「导出整套布局…」
		Assert.Contains("导出整套布局…", ui);
	}

	[Fact]
	public void EditBarMoreMenuIsPageScopedAndJumpsToSettings()
	{
		List<string> ui = Literals(Src("src/TipCL/Pages/Layout/LayoutEditorV3.cs"));
		foreach (string item in new string[3] { "导出本页…", "导入到本页…", "整套布局与方案…" })
		{
			Assert.Contains(item, ui);
		}
		// 「吸附 N」去掉:v3 编辑外壳里不再有吸附按钮文案
		Assert.DoesNotContain(ui, (string s) => s.StartsWith("吸附", StringComparison.Ordinal));
	}

	[Fact]
	public void ShellHooksExposeV3EntryPointsAndSingleBlockPagesAreNotEditable()
	{
		string hooks = Src("src/TipCL/Pages/Layout/LayoutShellHooks.cs");
		Assert.Contains("public static bool CanEditPage(string pageKey)", hooks);
		Assert.Contains("public static bool BeginEditPage(string pageKey)", hooks);
		// 单块页不提供编辑入口:编辑器门槛回到「至少两块」(v2 时 D1 曾放宽到 ≥ 1)
		// R35:门槛与右键菜单共用 BlockHostRules(R34 WP-L5,MinBlocksForLayoutMenu = 2)
		Assert.Matches(new Regex("public bool CanEdit\\s*\\{\\s*get\\s*\\{[^}]*return BlockHostRules\\.OffersLayoutMenu\\(_specs\\.Count\\);", RegexOptions.Singleline), Src("src/TipCL/Pages/Layout/LayoutEditor.cs"));
		Assert.Equal(2, TipCL.Pages.BlockHostRules.MinBlocksForLayoutMenu);
		// 设置 → 布局「页面」卡片与编辑入口都走约定签名
		string settings = Src("src/TipCL/Pages/SettingsLayoutPage.cs");
		Assert.Contains("LayoutShellHooks.CanEditPage(", settings);
		Assert.Contains("LayoutShellHooks.BeginEditPage(", settings);
	}

	[Theory]
	[InlineData("src/TipCL/Pages/Layout/LayoutEditorV3.cs")]
	[InlineData("src/TipCL/Pages/Layout/LayoutEditor.cs")]
	[InlineData("src/TipCL/Pages/Layout/LayoutShellHooks.cs")]
	[InlineData("src/TipCL/Pages/Layout/LayoutJsonWindow.cs")]
	[InlineData("src/TipCL/Pages/Layout/PageLayoutHost.cs")]
	[InlineData("src/TipCL/Pages/Layout/ShellTabs.cs")]
	[InlineData("src/TipCL/Pages/BlockHost.cs")]
	[InlineData("src/TipCL/Pages/SettingsLayoutPage.cs")]
	public void V3StringsExistInBothWordTables(string file)
	{
		HashSet<string> en = LocaleKeys("en.json");
		HashSet<string> zh = LocaleKeys("zh_CN.json");
		List<string> cjk = Literals(Src(file)).Where((string s) => Cjk.IsMatch(s)).Distinct().ToList();
		List<string> missingEn = cjk.Where((string s) => !en.Contains(s)).ToList();
		List<string> missingZh = cjk.Where((string s) => !zh.Contains(s)).ToList();
		Assert.True(missingEn.Count == 0, file + " 有 " + missingEn.Count + " 条文案不在 en.json:" + string.Join(" / ", missingEn));
		Assert.True(missingZh.Count == 0, file + " 有 " + missingZh.Count + " 条文案不在 zh_CN.json:" + string.Join(" / ", missingZh));
	}

	// ————— 纯函数:老 canvas 存档按网格呈现(§1.A.7),存档对象本身不动 —————

	[Fact]
	public void LegacyCanvasPageIsPresentedAsIntegerColumnGridWithoutTouchingTheSource()
	{
		PageLayoutV2 canvas = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeCanvas,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "a", X = 0.0, Y = 0.0, W = 0.5, H = 0.4 },
				new BlockLayout { Id = "b", X = 0.5, Y = 0.01, W = 0.5, H = 0.4 },
				new BlockLayout { Id = "c", X = 0.0, Y = 0.5, W = 1.0, H = 0.5 }
			}
		};
		PageLayoutV2? grid = LayoutCanvasCompat.AsGrid(canvas);
		Assert.NotNull(grid);
		Assert.NotSame(canvas, grid);
		Assert.Equal(LayoutSchema.ModeGrid, grid!.Mode);
		foreach (BlockLayout block in grid.Blocks)
		{
			Assert.True(block.W >= 1.0 && block.W <= grid.Cols && Math.Abs(block.W - Math.Round(block.W)) < 1e-9, "块 " + block.Id + " 宽度不是整数列:" + block.W);
		}
		// 读取只换内存里的这一份:传进来的 canvas 对象(= 存档)原样不动
		Assert.Equal(LayoutSchema.ModeCanvas, canvas.Mode);
		Assert.Equal(0.5, canvas.Blocks[0].W, 6);
		// 非 canvas 页原样返回同一个对象
		PageLayoutV2 stack = new PageLayoutV2 { Mode = LayoutSchema.ModeStack };
		Assert.Same(stack, LayoutCanvasCompat.AsGrid(stack));
	}
}
