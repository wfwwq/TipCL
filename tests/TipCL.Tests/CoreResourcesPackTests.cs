using System;
using System.IO;
using System.Linq;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

// 漂移守卫(D-38):Resources/{locales,ai,plugin-host} 下的源文件被 gzip 后内嵌(见
// tools/pack-core-resources.ps1)。任何人改了源却没重新生成 .gz,内嵌的就是旧内容 —— 本用例当场红。
// 它同时验证 TipCL.csproj 里 EmbeddedResource / LogicalName 的映射:某个源文件读不到(null),
// 说明那条 EmbeddedResource 没覆盖到它,或 LogicalName 写错了。
// 未压缩的树(例如 mac/linux 端口尚未跟进改造)同样通过:ReadBytes 对没有 gzip 魔数的资源原样返回。
public class CoreResourcesPackTests
{
	private static readonly string[] Groups = { "locales", "ai", "plugin-host" };

	private static DirectoryInfo RepoRoot()
	{
		DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !File.Exists(Path.Combine(d.FullName, "TipCL.sln")))
		{
			d = d.Parent;
		}
		Assert.NotNull(d);
		return d!;
	}

	public static TheoryData<string> Sources()
	{
		TheoryData<string> data = new TheoryData<string>();
		string resources = Path.Combine(RepoRoot().FullName, "src", "TipCL", "Resources");
		foreach (string group in Groups)
		{
			string dir = Path.Combine(resources, group);
			if (!Directory.Exists(dir))
			{
				continue;
			}
			foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
			{
				if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string rel = group + "/" + Path.GetRelativePath(dir, file).Replace('\\', '/');
				data.Add(rel);
			}
		}
		return data;
	}

	[Theory]
	[MemberData(nameof(Sources))]
	public void EmbeddedResourceMatchesSourceFile(string rel)
	{
		string path = Path.Combine(RepoRoot().FullName, "src", "TipCL", "Resources",
			rel.Replace('/', Path.DirectorySeparatorChar));
		byte[] expected = File.ReadAllBytes(path);
		byte[]? actual = CoreResources.ReadBytes(rel);
		Assert.True(actual != null,
			"内嵌资源读不到:" + rel + " —— TipCL.csproj 的 EmbeddedResource/LogicalName 没有覆盖它");
		Assert.Equal(expected.Length, actual!.Length);
		Assert.True(expected.SequenceEqual(actual),
			"内嵌资源与源文件不一致:" + rel + " —— 改了源却没重跑 tools/pack-core-resources.ps1");
	}
}
