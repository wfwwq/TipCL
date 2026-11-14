using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Plugins;

/// <summary>
/// 钩子（DESIGN §4 钩子表）：launch.before / launch.after / game.exit / ai.tool.before / ai.tool.after / ai.turn.before。
/// 按 priority 从大到小依次调用；每个插件 10 秒超时（超时或出错视为不改）。
/// 处理函数返回修改后的 data（PyDict），返回 null 表示不改。
/// </summary>
public static class PluginHooks
{
	public static readonly string[] Known = new string[6] { "launch.before", "launch.after", "game.exit", "ai.tool.before", "ai.tool.after", "ai.turn.before" };

	public static TimeSpan Timeout = TimeSpan.FromSeconds(10.0);

	private sealed class Entry
	{
		public string Plugin;

		public string Hook;

		public long Priority;

		public long Order;

		/// <summary>(data, token) → 新 data 或 null。</summary>
		public Func<PyDict, CancellationToken, object> Handler;
	}

	private static readonly object Lock = new object();

	private static readonly List<Entry> Entries = new List<Entry>();

	private static long _order;

	/// <summary>钩子出错/超时时的日志回调 (plugin, message)。</summary>
	public static Action<string, string> OnError;

	public static bool IsKnown(string hook)
	{
		return Array.IndexOf(Known, hook) >= 0;
	}

	public static void Register(string plugin, string hook, long priority, Func<PyDict, CancellationToken, object> handler)
	{
		if (!IsKnown(hook))
		{
			throw new PluginRpcException("invalid_params", "未知钩子：" + hook);
		}
		lock (Lock)
		{
			Entries.RemoveAll((Entry e) => e.Plugin == plugin && e.Hook == hook);
			Entries.Add(new Entry
			{
				Plugin = plugin,
				Hook = hook,
				Priority = priority,
				Order = ++_order,
				Handler = handler
			});
		}
	}

	public static void Unregister(string plugin, string hook)
	{
		lock (Lock)
		{
			Entries.RemoveAll((Entry e) => e.Plugin == plugin && e.Hook == hook);
		}
	}

	public static void UnregisterPlugin(string plugin)
	{
		lock (Lock)
		{
			Entries.RemoveAll((Entry e) => e.Plugin == plugin);
		}
	}

	public static bool Any(string hook)
	{
		lock (Lock)
		{
			return Entries.Any((Entry e) => e.Hook == hook);
		}
	}

	public static void Clear()
	{
		lock (Lock)
		{
			Entries.Clear();
		}
	}

	/// <summary>
	/// 依次调用，返回最终 data（总是一个新副本）。<paramref name="stop"/> 返回 true 时提前结束（例如 ai.tool.before 返回了 block）。
	/// </summary>
	public static PyDict Invoke(string hook, PyDict data, Func<PyDict, bool> stop = null)
	{
		List<Entry> list;
		lock (Lock)
		{
			list = Entries.Where((Entry e) => e.Hook == hook).OrderByDescending((Entry e) => e.Priority).ThenBy((Entry e) => e.Order).ToList();
		}
		PyDict cur = (PyDict)Py.DeepCopy(data ?? new PyDict());
		foreach (Entry e in list)
		{
			object r = null;
			using (CancellationTokenSource cts = new CancellationTokenSource())
			{
				try
				{
					Task<object> t = Task.Run(() => e.Handler((PyDict)Py.DeepCopy(cur), cts.Token));
					if (!t.Wait(Timeout))
					{
						cts.Cancel();
						OnError?.Invoke(e.Plugin, "钩子 " + hook + " 超时（" + Timeout.TotalSeconds + " 秒），视为不改");
						continue;
					}
					r = t.Result;
				}
				catch (Exception ex)
				{
					Exception inner = (ex is AggregateException ae && ae.InnerException != null) ? ae.InnerException : ex;
					OnError?.Invoke(e.Plugin, "钩子 " + hook + " 出错：" + inner.Message);
					continue;
				}
			}
			if (r is PyDict nd)
			{
				cur = nd;
				if (stop != null && stop(cur))
				{
					break;
				}
			}
		}
		return cur;
	}

	/// <summary>不关心返回值的钩子（launch.after / game.exit）：后台跑，不阻塞调用方。</summary>
	public static void Fire(string hook, PyDict data)
	{
		if (!Any(hook))
		{
			return;
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				Invoke(hook, data);
			}
			catch (Exception)
			{
			}
		});
	}
}
