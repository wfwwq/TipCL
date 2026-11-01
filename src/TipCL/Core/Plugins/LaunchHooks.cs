using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Plugins;

/// <summary>
/// 启动流程里的插件钩子（BackendApi._launch_game_impl 调用）。
/// 启动命令是一维列表 [java, jvm 参数..., 主类, 游戏参数...]，这里按主类拆成 jvmArgs / gameArgs 交给 launch.before，
/// 插件改完再拼回去。
/// </summary>
public static class LaunchHooks
{
	/// <summary>把命令按主类拆开。找不到主类时 jvmArgs = 除 java 外全部，gameArgs 为空，mainIndex = -1。</summary>
	public static (string java, List<string> jvm, string main, List<string> game, int mainIndex) Split(IList<string> cmd, string mainClass)
	{
		if (cmd == null || cmd.Count == 0)
		{
			return ("", new List<string>(), mainClass ?? "", new List<string>(), -1);
		}
		int idx = -1;
		if (!string.IsNullOrEmpty(mainClass))
		{
			for (int i = 1; i < cmd.Count; i++)
			{
				// 主类前面一个参数如果是 -cp / -p 这类带值的开关，那它是值不是主类（极少见，保险）
				if (cmd[i] == mainClass && !(i > 1 && (cmd[i - 1] == "-cp" || cmd[i - 1] == "-classpath" || cmd[i - 1] == "-p")))
				{
					idx = i;
					break;
				}
			}
		}
		if (idx < 0)
		{
			return (cmd[0], cmd.Skip(1).ToList(), mainClass ?? "", new List<string>(), -1);
		}
		return (cmd[0], cmd.Skip(1).Take(idx - 1).ToList(), cmd[idx], cmd.Skip(idx + 1).ToList(), idx);
	}

	public static List<string> Join(string java, List<string> jvm, string main, List<string> game, bool hadMain)
	{
		List<string> r = new List<string> { java };
		r.AddRange(jvm);
		if (hadMain)
		{
			r.Add(main);
		}
		r.AddRange(game);
		return r;
	}

	/// <summary>
	/// 跑 launch.before。没有插件注册时原样返回 (cmd, null)。
	/// 返回的 env 为 null 表示不改环境；否则是完整环境（当前进程环境 + 插件的覆盖，值为 null 的删除），可直接传给 GameProcess。
	/// </summary>
	public static (List<string> cmd, PyDict env) Before(List<string> cmd, string mainClass, string instance, string version, PyDict account, Action<string> log = null)
	{
		if (!PluginHooks.Any("launch.before"))
		{
			return (cmd, null);
		}
		var (java, jvm, main, game, mainIndex) = Split(cmd, mainClass);
		PyDict data = PyDict.Of("instance", instance, "version", version, "javaPath", java, "jvmArgs", jvm.Cast<object>().ToList(), "gameArgs", game.Cast<object>().ToList(), "env", new PyDict(), "account", account ?? new PyDict());
		PyDict r = PluginHooks.Invoke("launch.before", data);
		string java2 = Py.S(r.Get("javaPath"));
		if (java2.Length == 0)
		{
			java2 = java;
		}
		List<string> jvm2 = (r.Get("jvmArgs") is List<object> jl) ? jl.Select(Py.Str).ToList() : jvm;
		List<string> game2 = (r.Get("gameArgs") is List<object> gl) ? gl.Select(Py.Str).ToList() : game;
		List<string> cmd2 = Join(java2, jvm2, main, game2, mainIndex >= 0);
		PyDict env = null;
		if (r.Get("env") is PyDict envOver && envOver.Count > 0)
		{
			env = CurrentEnv();
			foreach (KeyValuePair<string, object> kv in envOver)
			{
				if (kv.Value == null)
				{
					RemoveKey(env, kv.Key);
				}
				else
				{
					RemoveKey(env, kv.Key);
					env[kv.Key] = Py.Str(kv.Value);
				}
			}
			log?.Invoke("[插件] 启动前钩子修改了环境变量：" + string.Join(", ", envOver.Keys));
		}
		if (!cmd2.SequenceEqual(cmd))
		{
			log?.Invoke("[插件] 启动前钩子修改了启动参数");
		}
		return (cmd2, env);
	}

	/// <summary>当前进程环境（Windows 上 key 不区分大小写，这里保留原样）。</summary>
	public static PyDict CurrentEnv()
	{
		PyDict d = new PyDict();
		foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
		{
			d[(string)e.Key] = (string)e.Value;
		}
		return d;
	}

	private static void RemoveKey(PyDict env, string key)
	{
		foreach (string k in env.Keys.ToList())
		{
			if (string.Equals(k, key, Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
			{
				env.Remove(k);
			}
		}
	}
}
