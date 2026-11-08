// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/LauncherConfig.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core.Launcher;

public sealed class LauncherConfig
{
	public PyDict data;

	public long revision;

	public readonly object _lock = new object();

	public string instances_dir => PPath.Join(Utils.ROOT, Py.Str(data.Req("instances_dir")));

	public string java_dir => PPath.Join(Utils.ROOT, Py.Str(data.Req("java_dir")));

	public string cache_dir => PPath.Join(Utils.ROOT, "cache");

	public LauncherConfig()
	{
		data = (PyDict)Py.DeepCopy(Config.DEFAULT_CONFIG);
		revision = 0L;
		Load();
	}

	public void Load()
	{
		revision++;
		if (Utils.ReadJson(Config.CONFIG_FILE) is PyDict pyDict)
		{
			bool flag = false;
			foreach (string key in Config.DEFAULT_CONFIG.Keys)
			{
				if (pyDict.ContainsKey(key))
				{
					data[key] = pyDict[key];
				}
				else
				{
					flag = true;
				}
			}
			foreach (KeyValuePair<string, object> item in pyDict)
			{
				if (!Config.DEFAULT_CONFIG.ContainsKey(item.Key))
				{
					data[item.Key] = item.Value;
				}
			}
			if (MigrateLegacyInstancesDir())
			{
				flag = true;
			}
			if (MigrateDefaultIsolation())
			{
				flag = true;
			}
			if (MigrateAiGatewayMode())
			{
				flag = true;
			}
			if (ScrubBuiltinSecrets())
			{
				flag = true;
			}
			if (flag)
			{
				Save();
			}
		}
		else
		{
			string value = GameDirDetector.FindExisting();
			if (!string.IsNullOrEmpty(value))
			{
				data["instances_dir"] = value;
			}
			MigrateLegacyInstancesDir();
			MigrateDefaultIsolation();
			MigrateAiGatewayMode();
			ScrubBuiltinSecrets();
			Save();
		}
	}

	private static string Norm(object v)
	{
		return Py.RStrip(Py.Strip(Py.S(v)), "/");
	}

	internal bool ScrubBuiltinSecrets()
	{
		bool result = false;
		if (Py.Strip(Py.S(data.Get("curseforge_api_key"))) == Config.BuiltinCurseforgeKey())
		{
			data["curseforge_api_key"] = "";
			result = true;
		}
		(string, string[])[] array = new(string, string[])[2]
		{
			("ai_api_key", new string[1] { Defaults.DEFAULT_API_KEY }),
			("ai_base_url", new string[2]
			{
				Defaults.DEFAULT_API_BASE,
				Defaults.DEFAULT_GATEWAY_URL
			})
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string[]) tuple = array[i];
			string item = tuple.Item1;
			string[] item2 = tuple.Item2;
			string text = Norm(data.Get(item));
			HashSet<string> hashSet = new HashSet<string>(from v in item2
				where !string.IsNullOrEmpty(v)
				select v.TrimEnd('/'));
			if (text.Length > 0 && hashSet.Contains(text))
			{
				data[item] = "";
				result = true;
			}
		}
		return result;
	}

	internal bool MigrateLegacyInstancesDir()
	{
		if (Py.Strip(Py.S(data.Get("instances_dir"))) != "instances")
		{
			return false;
		}
		string text = PPath.Join(Utils.ROOT, "instances");
		string text2 = PPath.Join(Utils.ROOT, ".minecraft");
		if (Directory.Exists(text) && !Directory.Exists(text2) && !File.Exists(text2))
		{
			Directory.Move(text, text2);
		}
		data["instances_dir"] = ".minecraft";
		return true;
	}

	internal bool MigrateDefaultIsolation()
	{
		if (Py.Eq(data.Get("isolation_defaults"), "2026.09-isolate-all"))
		{
			return false;
		}
		if (Py.S(data.Get("default_isolation")) == "none")
		{
			data["default_isolation"] = "all";
		}
		data["isolation_defaults"] = "2026.09-isolate-all";
		return true;
	}

	/// <summary>AI 接入收成两种模式：builtin / custom。
	/// 旧的 public（自建网关）和没填地址的 custom 都改成 builtin；填过自己地址的保持 custom。
	/// 等于内置网关的地址当作没填（ScrubBuiltinSecrets 随后会清掉）。</summary>
	internal bool MigrateAiGatewayMode()
	{
		if (Py.Eq(data.Get("ai_gateway_defaults"), Config.AI_GATEWAY_DEFAULTS_VERSION))
		{
			return false;
		}
		string text = Norm(data.Get("ai_base_url"));
		bool flag = text.Length > 0 && text != Defaults.DEFAULT_API_BASE.TrimEnd('/') && text != Defaults.DEFAULT_GATEWAY_URL.TrimEnd('/');
		string text2 = Py.Lower(Py.Strip(Py.S(data.Get("ai_mode"))));
		data["ai_mode"] = ((text2 == "custom" || text2 == "newapi" || text2 == "自定义") && flag) ? "custom" : "builtin";
		data.Pop("ai_gateway_url");
		data["ai_gateway_defaults"] = Config.AI_GATEWAY_DEFAULTS_VERSION;
		return true;
	}

	public void Save()
	{
		lock (_lock)
		{
			try
			{
				Utils.WriteJson(Config.CONFIG_FILE, data);
			}
			catch (Exception ex)
			{
				// 配置存盘失败（多实例并发 / 杀软锁文件偶发）不能打断启动或连接流程：
				// 内存配置照用，2 秒后后台再试一次，仍失败只记日志。
				Utils.log.Warning("配置写入失败 %s: %s（稍后重试）", Config.CONFIG_FILE, Py.ExcStr(ex));
				Task.Run(delegate
				{
					Thread.Sleep(2000);
					try
					{
						Utils.WriteJson(Config.CONFIG_FILE, data);
					}
					catch (Exception ex2)
					{
						Utils.log.Warning("配置重试写入仍失败 %s: %s", Config.CONFIG_FILE, Py.ExcStr(ex2));
					}
				});
			}
		}
	}

	public object Get(string key, object dflt = null)
	{
		return data.Get(key, dflt);
	}

	public void Set(string key, object value)
	{
		lock (_lock)
		{
			data[key] = value;
			revision++;
		}
	}

	public void Update(PyDict mapping)
	{
		lock (_lock)
		{
			data.Update(mapping);
			revision++;
		}
	}

	public string LibrariesDir(string instance_dir)
	{
		if (Py.Truthy(data.Req("shared_libraries")))
		{
			return PPath.Join(Utils.ROOT, "shared", "libraries");
		}
		return PPath.Join(instance_dir, "libraries");
	}

	public string AssetsDir(string instance_dir)
	{
		if (Py.Truthy(data.Req("shared_assets")))
		{
			return PPath.Join(Utils.ROOT, "shared", "assets");
		}
		return PPath.Join(instance_dir, "assets");
	}
}
