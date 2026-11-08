using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

public class AiFileToolsTests : IDisposable
{
	private readonly string _root;

	private readonly List<string> _roots;

	public string Dir => _root;

	private static PyDict Args(params object[] kv)
	{
		PyDict d = new PyDict();
		for (int i = 0; i < kv.Length; i += 2)
		{
			d[Py.Str(kv[i])] = kv[i + 1];
		}
		return d;
	}

	public AiFileToolsTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "tipcl-fs-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
		_roots = new List<string> { _root };
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (Exception)
		{
		}
	}

	// ---------------- 路径守卫 ----------------

	[Fact]
	public void RelativePathResolvesAgainstBaseDir()
	{
		PPath p = ExtraTools.FsResolve("logs\\latest.log", _roots, _root);
		Assert.Equal(Path.Combine(_root, "logs", "latest.log").ToLowerInvariant(), p.Abs().ToLowerInvariant());
	}

	[Fact]
	public void AbsolutePathInsideRootAllowed()
	{
		string abs = Path.Combine(_root, "logs", "latest.log");
		PPath p = ExtraTools.FsResolve(abs, _roots, _root);
		Assert.Equal(abs.ToLowerInvariant(), p.Abs().ToLowerInvariant());
	}

	[Fact]
	public void TraversalEscapeRejected()
	{
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve("..\\outside.txt", _roots, _root));
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve(Path.Combine(_root, "..", "outside.txt"), _roots, _root));
	}

	[Fact]
	public void OutsideAbsolutePathRejected()
	{
		string other = Path.Combine(Path.GetTempPath(), "tipcl-fs-not-allowed.txt");
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve(other, _roots, _root));
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve("C:\\some\\where\\else.txt", _roots, _root));
	}

	[Fact]
	public void EmptyAndDriveOnlyPathRejected()
	{
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve("", _roots, _root));
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve("C:", _roots, _root));
	}

	[Fact]
	public void DenyListBlocksSystemDirs()
	{
		List<string> roots = new List<string> { "C:\\Windows" };
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve("C:\\Windows\\Temp\\x.txt", roots, "C:\\Windows"));
		Assert.True(ExtraTools.FsDenied(new PPath("C:\\Program Files\\x.txt")));
		Assert.False(ExtraTools.FsDenied(new PPath(Path.Combine(_root, "x.txt"))));
	}

	// ---------------- fs_read ----------------

	[Fact]
	public void ReadDefaultsToLast200Lines()
	{
		string path = Path.Combine(_root, "latest.log");
		List<string> lines = new List<string>();
		for (int i = 0; i < 300; i++)
		{
			lines.Add("line " + i);
		}
		File.WriteAllLines(path, lines);
		object r = ExtraTools.FsRead(Args("path", path), _roots, _root);
		string s = Assert.IsType<string>(r);
		Assert.Contains("line 299", s);
		Assert.DoesNotContain("line 50\n", s);
		Assert.Contains("共 300 行", s);
	}

	[Fact]
	public void ReadWithOffsetAndLimit()
	{
		string path = Path.Combine(_root, "latest.log");
		File.WriteAllLines(path, new string[5] { "a", "b", "c", "d", "e" });
		string s = Assert.IsType<string>(ExtraTools.FsRead(Args("path", path, "offset_line", 1L, "max_lines", 2L), _roots, _root));
		Assert.Contains("b\nc", s);
		Assert.DoesNotContain("a", s);
		Assert.DoesNotContain("d", s);
	}

	[Fact]
	public void ReadRejectsBinaryByExtension()
	{
		string path = Path.Combine(_root, "mod.zip");
		File.WriteAllBytes(path, new byte[10] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
		string s = Assert.IsType<string>(ExtraTools.FsRead(Args("path", path), _roots, _root));
		Assert.Contains("二进制", s);
	}

	[Fact]
	public void ReadRejectsNulBytes()
	{
		string path = Path.Combine(_root, "weird.log");
		byte[] head = Encoding.UTF8.GetBytes("hello");
		File.WriteAllBytes(path, head.Concat(new byte[3]).ToArray());
		string s = Assert.IsType<string>(ExtraTools.FsRead(Args("path", path), _roots, _root));
		Assert.Contains("空字节", s);
	}

	[Fact]
	public void ReadRejectsOversizeFile()
	{
		string path = Path.Combine(_root, "big.log");
		File.WriteAllText(path, new string('x', 600 * 1024));
		string s = Assert.IsType<string>(ExtraTools.FsRead(Args("path", path), _roots, _root));
		Assert.Contains("512 KB", s);
	}

	// ---------------- fs_write ----------------

	[Fact]
	public void WriteCreatesFileAndParentDirs()
	{
		string path = Path.Combine(_root, "config\\sub\\notes.txt");
		string s = Assert.IsType<string>(ExtraTools.FsWrite(Args("path", path, "content", "hello"), _roots, _root));
		Assert.Contains("已写入", s);
		Assert.Equal("hello", File.ReadAllText(path));
	}

	[Fact]
	public void WriteOverwriteAndAppend()
	{
		string path = Path.Combine(_root, "notes.txt");
		ExtraTools.FsWrite(Args("path", path, "content", "one"), _roots, _root);
		ExtraTools.FsWrite(Args("path", path, "content", "two", "append", true), _roots, _root);
		string text = File.ReadAllText(path);
		Assert.StartsWith("one", text);
		Assert.Contains("two", text);
		ExtraTools.FsWrite(Args("path", path, "content", "three"), _roots, _root);
		Assert.Equal("three", File.ReadAllText(path));
	}

	[Fact]
	public void WriteRejectsOversizeContent()
	{
		string path = Path.Combine(_root, "big.txt");
		string s = Assert.IsType<string>(ExtraTools.FsWrite(Args("path", path, "content", new string('x', 300 * 1024)), _roots, _root));
		Assert.Contains("256 KB", s);
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void WriteOutsideRootRejected()
	{
		string path = Path.Combine(Path.GetTempPath(), "tipcl-fs-escape.txt");
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsWrite(Args("path", path, "content", "x"), _roots, _root));
		Assert.False(File.Exists(path));
	}

	// ---------------- fs_list ----------------

	[Fact]
	public void ListShowsEntriesAndHonorsGlob()
	{
		Directory.CreateDirectory(Path.Combine(_root, "sub"));
		File.WriteAllText(Path.Combine(_root, "a.log"), "x");
		File.WriteAllText(Path.Combine(_root, "b.txt"), "y");
		object r = ExtraTools.FsList(Args("path", _root), _roots, _root);
		List<object> rows = Assert.IsType<List<object>>(r);
		Assert.Contains(rows, (object row) => Py.S(((PyDict)row).Get("name")) == "a.log");
		Assert.Contains(rows, (object row) => Py.S(((PyDict)row).Get("type")) == "dir");
		object r2 = ExtraTools.FsList(Args("path", _root, "glob", "*.log"), _roots, _root);
		List<object> rows2 = Assert.IsType<List<object>>(r2);
		Assert.Single(rows2);
	}

	[Fact]
	public void ListRejectsFileOrMissing()
	{
		string path = Path.Combine(_root, "a.txt");
		File.WriteAllText(path, "x");
		List<object> rows = Assert.IsType<List<object>>(ExtraTools.FsList(Args("path", path), _roots, _root));
		Assert.Single(rows);
		Assert.Throws<FileNotFoundError>(() => ExtraTools.FsList(Args("path", Path.Combine(_root, "nope")), _roots, _root));
	}

	[Fact]
	public void ListRejectsTraversalPath()
	{
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsList(Args("path", ".."), _roots, _root));
	}
}
