using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TipCL.Core.Plugins;

/// <summary>plugins.json 里一个插件的记录（DESIGN §1）。</summary>
public sealed class PluginRecord
{
	public string Id;

	public bool Enabled = true;

	public string Version = "";

	/// <summary>market | local | dev</summary>
	public string Source = "local";

	public string DevPath = "";

	public List<string> GrantedPermissions = new List<string>();

	public string InstalledAt = "";

	public string Market = "";

	/// <summary>插件配置值（非密钥部分；secret 配置项存凭据里）。</summary>
	public PyDict Config = new PyDict();

	public PyDict ToDict()
	{
		PyDict d = PyDict.Of("enabled", Enabled, "version", Version, "source", Source, "grantedPermissions", GrantedPermissions.Cast<object>().ToList(), "installedAt", InstalledAt);
		if (!string.IsNullOrEmpty(DevPath))
		{
			d["devPath"] = DevPath;
		}
		if (!string.IsNullOrEmpty(Market))
		{
			d["market"] = Market;
		}
		if (Config != null && Config.Count > 0)
		{
			d["config"] = Config;
		}
		return d;
	}

	public static PluginRecord FromDict(string id, PyDict d)
	{
		d = d ?? new PyDict();
		return new PluginRecord
		{
			Id = id,
			Enabled = !d.ContainsKey("enabled") || Py.Truthy(d.Get("enabled")),
			Version = Py.S(d.Get("version")),
			Source = Py.Or(d.Get("source"), "local") as string ?? "local",
			DevPath = Py.S(d.Get("devPath")),
			GrantedPermissions = Py.ListOr(d.Get("grantedPermissions")).Select(Py.S).Where((string s) => s.Length > 0).Distinct().ToList(),
			InstalledAt = Py.S(d.Get("installedAt")),
			Market = Py.S(d.Get("market")),
			Config = (d.Get("config") as PyDict)?.Copy() ?? new PyDict()
		};
	}
}

/// <summary>额外 OpenAI 兼容模型供应商（DESIGN §6）。apiKey 不在这里，在宿主密钥文件里，引用名 provider:&lt;id&gt;。</summary>
public sealed class ProviderRecord
{
	public string Id;

	public string Name;

	public string BaseUrl;

	public string ApiKeyRef;

	public PyDict ToDict()
	{
		return PyDict.Of("id", Id, "name", Name, "baseUrl", BaseUrl, "apiKeyRef", ApiKeyRef);
	}

	public static ProviderRecord FromDict(PyDict d)
	{
		return new ProviderRecord
		{
			Id = Py.S(d.Get("id")),
			Name = Py.S(d.Get("name")),
			BaseUrl = Py.S(d.Get("baseUrl")),
			ApiKeyRef = Py.S(d.Get("apiKeyRef"))
		};
	}
}

/// <summary>
/// plugins.json 的读写。线程安全；每次修改都整体原子写回（先写临时文件再替换）。
/// </summary>
public sealed class PluginStateStore
{
	private readonly string _path;

	private readonly object _lock = new object();

	private readonly Dictionary<string, PluginRecord> _plugins = new Dictionary<string, PluginRecord>(StringComparer.Ordinal);

	private readonly List<ProviderRecord> _providers = new List<ProviderRecord>();

	public PluginStateStore(string path)
	{
		_path = path;
		Load();
	}

	public void Load()
	{
		lock (_lock)
		{
			_plugins.Clear();
			_providers.Clear();
			if (!File.Exists(_path))
			{
				return;
			}
			object obj;
			try
			{
				obj = PyJson.Loads(File.ReadAllText(_path, Encoding.UTF8).TrimStart('\ufeff'));
			}
			catch (Exception)
			{
				// 坏文件：备份一下再当空的
				try
				{
					File.Copy(_path, _path + ".corrupt", overwrite: true);
				}
				catch (Exception)
				{
				}
				return;
			}
			if (!(obj is PyDict root))
			{
				return;
			}
			if (root.Get("plugins") is PyDict ps)
			{
				foreach (KeyValuePair<string, object> kv in ps)
				{
					if (PluginManifest.IsValidId(kv.Key) && kv.Value is PyDict pd)
					{
						_plugins[kv.Key] = PluginRecord.FromDict(kv.Key, pd);
					}
				}
			}
			foreach (PyDict p in Py.ListOr(root.Get("providers")).OfType<PyDict>())
			{
				ProviderRecord pr = ProviderRecord.FromDict(p);
				if (pr.Id.Length > 0)
				{
					_providers.Add(pr);
				}
			}
		}
	}

	private void SaveLocked()
	{
		PyDict plugins = new PyDict();
		foreach (PluginRecord r in _plugins.Values.OrderBy((PluginRecord x) => x.Id, StringComparer.Ordinal))
		{
			plugins[r.Id] = r.ToDict();
		}
		PyDict root = PyDict.Of("version", 1L, "plugins", plugins, "providers", _providers.Select((ProviderRecord p) => (object)p.ToDict()).ToList());
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		string tmp = _path + ".tmp";
		File.WriteAllText(tmp, PyJson.Dumps(root, 2, ensureAscii: false), new UTF8Encoding(false));
		if (File.Exists(_path))
		{
			File.Replace(tmp, _path, null);
		}
		else
		{
			File.Move(tmp, _path);
		}
	}

	public List<PluginRecord> All()
	{
		lock (_lock)
		{
			return _plugins.Values.ToList();
		}
	}

	public PluginRecord Get(string id)
	{
		lock (_lock)
		{
			return (id != null && _plugins.TryGetValue(id, out PluginRecord r)) ? r : null;
		}
	}

	/// <summary>在锁里改一条记录（不存在则新建），然后写盘。</summary>
	public PluginRecord Update(string id, Action<PluginRecord> change)
	{
		lock (_lock)
		{
			if (!_plugins.TryGetValue(id, out PluginRecord r))
			{
				r = new PluginRecord { Id = id };
				_plugins[id] = r;
			}
			change?.Invoke(r);
			SaveLocked();
			return r;
		}
	}

	public bool Remove(string id)
	{
		lock (_lock)
		{
			bool removed = _plugins.Remove(id);
			if (removed)
			{
				SaveLocked();
			}
			return removed;
		}
	}

	public List<ProviderRecord> Providers()
	{
		lock (_lock)
		{
			return _providers.ToList();
		}
	}

	public void UpsertProvider(ProviderRecord p)
	{
		lock (_lock)
		{
			_providers.RemoveAll((ProviderRecord x) => x.Id == p.Id);
			_providers.Add(p);
			SaveLocked();
		}
	}

	public bool RemoveProvider(string id)
	{
		lock (_lock)
		{
			bool removed = _providers.RemoveAll((ProviderRecord x) => x.Id == id) > 0;
			if (removed)
			{
				SaveLocked();
			}
			return removed;
		}
	}
}
