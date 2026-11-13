using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TipCL.Services;
using TipCL.Tests;
using Xunit;

public class FileKindsTests
{
	private static string MakePluginZip(string dir, string id, string name, string version, bool nested)
	{
		string zipPath = Path.Combine(dir, id + ".zip");
		using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
		{
			string manifestPath = nested ? id + "/tipcl-plugin.json" : "tipcl-plugin.json";
			ZipArchiveEntry e = zip.CreateEntry(manifestPath);
			using (var w = new StreamWriter(e.Open()))
			{
				w.Write("{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"version\":\"" + version + "\"}");
			}
			using var w2 = new StreamWriter(zip.CreateEntry(nested ? id + "/lib/index.js" : "lib/index.js").Open());
			w2.Write("export default {};");
		}
		return zipPath;
	}

	public static IEnumerable<object[]> PluginZips()
	{
		yield return new object[] { false };
		yield return new object[] { true };
	}

	[Theory]
	[MemberData(nameof(PluginZips))]
	public void PluginZipIsRecognized(bool nested)
	{
		string dir = Path.Combine(TestHome.Root, "filekinds-" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);
		try
		{
			string zip = MakePluginZip(dir, "demo-" + (nested ? "n" : "r"), "演示插件", "0.2.0", nested);
			FileKinds.Verdict v = FileKinds.Identify(zip);
			Assert.Equal(new[] { "plugin" }, v.Kinds);
			Assert.True(v.Sure);
			Assert.Contains("演示插件", v.Detail);
			Assert.Contains("0.2.0", v.Detail);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void NonPluginZipIsNotPlugin()
	{
		string dir = Path.Combine(TestHome.Root, "filekinds-" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);
		try
		{
			string zip = Path.Combine(dir, "plain.zip");
			using (ZipArchive zip2 = ZipFile.Open(zip, ZipArchiveMode.Create))
			{
				using var w = new StreamWriter(zip2.CreateEntry("readme.txt").Open());
				w.Write("nothing here");
			}
			FileKinds.Verdict v = FileKinds.Identify(zip);
			Assert.DoesNotContain("plugin", v.Kinds);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}
}
