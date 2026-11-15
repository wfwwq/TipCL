using System;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

// 测试会改 TIPCL_HOME 这个进程级环境变量，所以关掉并行
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TipCL.Tests;

/// <summary>每个测试一个临时数据目录，结束后删除。</summary>
// R1 起同时调 Utils.SetRoot():Utils.ROOT 是惰性缓存的静态量,根一旦被别的代码钉住,
// 这里只设环境变量就再也不生效了(见 TestIsolation.cs / TestIsolationTests.cs)。
public sealed class TestHome : IDisposable
{
	private readonly string _old;

	public string Dir { get; }

	public TestHome()
	{
		_old = Environment.GetEnvironmentVariable("TIPCL_HOME");
		Dir = Path.Combine(Path.GetTempPath(), "tipcl-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Dir);
		Environment.SetEnvironmentVariable("TIPCL_HOME", Dir);
		// R1:Utils.ROOT 是惰性缓存的静态量,只改环境变量改不动已经被钉住的根 ——
		// 必须同时把它指到这个测试自己的目录,否则本用例会读写别的测试(甚至真实用户)的目录。
		Utils.SetRoot(Dir);
	}

	public void Dispose()
	{
		Environment.SetEnvironmentVariable("TIPCL_HOME", _old);
		// 交还给环境变量解析(模块初始化器已把进程根钉在隔离目录,不会落到真实用户目录)
		Utils.SetRoot(_old);
		try
		{
			Directory.Delete(Dir, recursive: true);
		}
		catch (Exception)
		{
		}
	}
}
