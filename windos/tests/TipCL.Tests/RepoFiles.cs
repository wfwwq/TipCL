using System;
using System.IO;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 源码文本断言用的取文件工具:界面是 WPF,net8 测试宿主实例化不了控件,于是「接线」这类事实
/// 按仓库既有做法用源码文本钉住(同 LayoutV3SourceTests / LayoutShellTests)。
/// </summary>
internal static class RepoFiles
{
	/// <summary>Windows 那棵树里 TipCL.sln 所在目录的绝对路径。</summary>
	internal static string Root()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return dir!.FullName;
	}

	/// <summary>读仓库里的一个文件(路径相对 TipCL.sln 所在目录,斜杠用 / 或 \ 都行)。</summary>
	internal static string Src(string rel)
	{
		return File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));
	}
}