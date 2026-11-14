using System;
using System.IO;
using Xunit;

// 测试会改 TIPCL_HOME 这个进程级环境变量，所以关掉并行
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TipCL.Tests;

/// <summary>每个测试一个临时数据目录，结束后删除。</summary>
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
	}

	public void Dispose()
	{
		Environment.SetEnvironmentVariable("TIPCL_HOME", _old);
		try
		{
			Directory.Delete(Dir, recursive: true);
		}
		catch (Exception)
		{
		}
	}
}
