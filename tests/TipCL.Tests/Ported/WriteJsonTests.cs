using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

public class WriteJsonTests
{
	[Fact]
	public async Task ConcurrentWritersKeepFileValidAndNeverThrow()
	{
		// A6 B 类改动:原来写 Path.GetTempPath(),红线要求测试临时文件必须落在仓库内 → 改到 TestHome.Root 下。
		string dir = Path.Combine(TestHome.Root, "tipcl-wjtest-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			string path = Path.Combine(dir, "config.json");
			const int threads = 4;
			const int writesPerThread = 125;
			Exception failure = null;
			using (ManualResetEvent gate = new ManualResetEvent(false))
			{
				List<Task> tasks = new List<Task>();
				for (int t = 0; t < threads; t++)
				{
					int id = t;
					tasks.Add(Task.Run(delegate
					{
						gate.WaitOne();
						for (int i = 0; i < writesPerThread; i++)
						{
							try
							{
								Utils.WriteJson(path, new Dictionary<string, object>
								{
									{ "writer", id },
									{ "seq", i },
									{ "note", "并发写 config.json 抗杀软/多实例锁" },
								});
							}
							catch (Exception ex)
							{
								Interlocked.CompareExchange(ref failure, ex, null);
								return;
							}
						}
					}));
				}
				gate.Set();
				await Task.WhenAll(tasks);
			}
			Assert.Null(failure);
			byte[] raw = File.ReadAllBytes(path);
			using (JsonDocument doc = JsonDocument.Parse(Encoding.UTF8.GetString(raw)))
			{
				Assert.True(doc.RootElement.TryGetProperty("writer", out _));
			}
			Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
		}
		finally
		{
			try { Directory.Delete(dir, true); } catch { }
		}
	}
}
