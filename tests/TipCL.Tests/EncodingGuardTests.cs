using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 【D1 守卫】编码守卫:仓库里所有受版本控制的文本文件都不许带 UTF-8 BOM(除 <c>tools\*.ps1</c>,
/// 那是 D-12 要求的)、不许含 CR 字节(除 <c>TipCL.Mac.sln</c>,解决方案文件惯例是 CRLF)、
/// 不许含 U+FFFD(替换字符 = 已经坏掉的解码痕迹)、必须是合法 UTF-8。
///
/// 为什么需要它:这个坑在本项目里已经踩过四次 —— C1b / C2b(误用 <c>Get-Content | Set-Content</c>
/// 写出 BOM+CRLF)/ C2c2(把中文改坏)/ C3b。每次都是 Lead 事后手工按字节扫描才发现的。
/// 把它变成用例以后,这类问题会在**两条通道里自动红**,而不是等下一次人工体检。
///
/// <para>扫描器本身是可测的:<see cref="The_scanner_detects_bom_cr_and_replacement_chars"/> 在一个
/// 临时目录里造几个坏文件,证明它真的抓得到(而不是"永远返回零个问题"的假绿)。</para>
///
/// <para>注意:这里**故意不查**「连续问号」。第一版加过这条启发式,结果被本文件自己误报
/// (本文件为了描述这条规则而字面包含那四个字符)。而真正的写坏痕迹一定会表现为
/// BOM、CR、非法 UTF-8 或 U+FFFD —— 这几条已经全部覆盖,不需要一条会自伤的规则。</para>
/// </summary>
public sealed class EncodingGuardTests
{
	/// <summary>允许带 BOM 的相对路径前缀(D-12:PowerShell 5.1 读无 BOM 的 UTF-8 会坏中文)。</summary>
	private static readonly string[] BomAllowed = { "tools/" };

	/// <summary>允许含 CR 的相对路径(解决方案文件惯例)。</summary>
	private static readonly string[] CrAllowed = { "TipCL.Mac.sln" };

	/// <summary>扫描时跳过的目录名(构建产物 / 版本库内部 / 本会话的临时物)。</summary>
	private static readonly string[] SkipDirs = { ".git", "bin", "obj", "artifacts", "dist", "test-home", "TestResults", ".vs", "node_modules" };

	private static readonly string[] TextExtensions =
	{
		".cs", ".axaml", ".xaml", ".csproj", ".props", ".targets", ".sln", ".md",
		".json", ".cmd", ".ps1", ".txt", ".yml", ".yaml",
	};

	/// <summary>仓库根:从测试程序集所在目录往上找 <c>TipCL.Mac.sln</c>。</summary>
	internal static string RepoRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "TipCL.Mac.sln")))
			{
				return dir.FullName;
			}
			dir = dir.Parent;
		}
		throw new InvalidOperationException("找不到仓库根(没有 TipCL.Mac.sln):" + AppContext.BaseDirectory);
	}

	/// <summary>返回问题清单(空 = 干净)。相对路径用 <c>/</c> 分隔,便于断言。</summary>
	internal static List<string> Scan(string root)
	{
		List<string> bad = new List<string>();
		foreach (string file in Enumerate(root))
		{
			string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
			bool isText = TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
				|| rel.EndsWith(".gitignore", StringComparison.Ordinal)
				|| rel.EndsWith(".gitattributes", StringComparison.Ordinal)
				|| rel.EndsWith(".editorconfig", StringComparison.Ordinal);
			if (!isText)
			{
				continue;
			}

			byte[] bytes = File.ReadAllBytes(file);

			bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
			if (bom && !BomAllowed.Any(p => rel.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				bad.Add(rel + " : 带 UTF-8 BOM");
			}

			if (Array.IndexOf(bytes, (byte)13) >= 0 && !CrAllowed.Contains(rel, StringComparer.Ordinal))
			{
				bad.Add(rel + " : 含 CR 字节(本仓库源文件一律 LF)");
			}

			// 只有能严格解出来的才继续查替换字符;解不出来本身就是问题。
			string text;
			try
			{
				text = new UTF8Encoding(false, true).GetString(bytes);
			}
			catch (DecoderFallbackException)
			{
				bad.Add(rel + " : 不是合法的 UTF-8");
				continue;
			}
			if (text.IndexOf('\uFFFD') >= 0)
			{
				bad.Add(rel + " : 含 U+FFFD(已经坏掉的解码痕迹)");
			}
		}
		bad.Sort(StringComparer.Ordinal);
		return bad;
	}

	private static IEnumerable<string> Enumerate(string dir)
	{
		foreach (string sub in Directory.EnumerateDirectories(dir))
		{
			if (SkipDirs.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
			{
				continue;
			}
			foreach (string f in Enumerate(sub))
			{
				yield return f;
			}
		}
		foreach (string f in Directory.EnumerateFiles(dir))
		{
			yield return f;
		}
	}

	/// <summary>【任务书】仓库里的文本文件必须是干净的无 BOM UTF-8 / 纯 LF / 无替换字符。</summary>
	[Fact]
	public void Repo_text_files_are_clean_utf8_without_bom_cr_or_replacement_chars()
	{
		List<string> bad = Scan(RepoRoot());
		Assert.True(bad.Count == 0,
			"仓库里有文件编码不干净(共 " + bad.Count + " 处):" + Environment.NewLine + string.Join(Environment.NewLine, bad));
	}

	/// <summary>【自查】扫描器必须真的抓得到这几种坏文件,否则上面那条就是假绿。</summary>
	[Fact]
	public void The_scanner_detects_bom_cr_and_replacement_chars()
	{
		string root = Path.Combine(TestHome.Root, "encoding-guard-" + Path.GetRandomFileName());
		Directory.CreateDirectory(root);
		try
		{
			// 正常文件:不许被误报
			File.WriteAllBytes(Path.Combine(root, "good.cs"), new UTF8Encoding(false).GetBytes("// 正常\nvar a = 1;\n"));

			// 1) 带 BOM(不在 tools 下)
			File.WriteAllText(Path.Combine(root, "bom.cs"), "// 带 BOM\n", new UTF8Encoding(true));

			// 2) 含 CRLF
			File.WriteAllBytes(Path.Combine(root, "crlf.cs"), new UTF8Encoding(false).GetBytes("// CRLF\r\n"));

			// 3) 含 U+FFFD(模拟把中文写坏)
			File.WriteAllBytes(Path.Combine(root, "mojibake.cs"), new UTF8Encoding(false).GetBytes("// 坏 " + '\uFFFD' + " 了\n"));

			// 4) 不是合法 UTF-8(写一串 GBK 字节)
			File.WriteAllBytes(Path.Combine(root, "gbk.cs"), new byte[] { 0x2F, 0x2F, 0xD6, 0xD0, 0xCE, 0xC4, 0x0A });

			List<string> bad = Scan(root);
			Assert.Contains(bad, s => s.StartsWith("bom.cs", StringComparison.Ordinal));
			Assert.Contains(bad, s => s.StartsWith("crlf.cs", StringComparison.Ordinal));
			Assert.Contains(bad, s => s.StartsWith("mojibake.cs", StringComparison.Ordinal));
			Assert.Contains(bad, s => s.StartsWith("gbk.cs", StringComparison.Ordinal));
			Assert.DoesNotContain(bad, s => s.StartsWith("good.cs", StringComparison.Ordinal));
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	/// <summary>【自查】<c>tools\*.ps1</c> 允许 BOM(D-12);别的目录不许。</summary>
	[Fact]
	public void Bom_is_allowed_only_for_tools_ps1()
	{
		string root = Path.Combine(TestHome.Root, "encoding-guard-ps1-" + Path.GetRandomFileName());
		Directory.CreateDirectory(Path.Combine(root, "tools"));
		try
		{
			File.WriteAllText(Path.Combine(root, "tools", "ok.ps1"), "# 允许\n", new UTF8Encoding(true));
			File.WriteAllText(Path.Combine(root, "notools.ps1"), "# 不允许\n", new UTF8Encoding(true));

			List<string> bad = Scan(root);
			Assert.DoesNotContain(bad, s => s.StartsWith("tools/ok.ps1", StringComparison.Ordinal));
			Assert.Contains(bad, s => s.StartsWith("notools.ps1", StringComparison.Ordinal));
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}
}
