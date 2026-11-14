using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TipCL.Core.Plugins;

/// <summary>权限名（DESIGN §2 权限表）。</summary>
public static class PluginPermissions
{
	public const string Launcher = "launcher";

	public const string AiTools = "ai.tools";

	public const string AiPresets = "ai.presets";

	public const string AiSessions = "ai.sessions";

	public const string AiLlm = "ai.llm";

	public const string Credentials = "credentials";

	public const string Ui = "ui";

	public const string UiOverlay = "ui.overlay";

	public const string System = "system";

	public static readonly string[] All = new string[9] { Launcher, AiTools, AiPresets, AiSessions, AiLlm, Credentials, Ui, UiOverlay, System };

	public static bool IsKnown(string p)
	{
		return Array.IndexOf(All, p) >= 0;
	}

	/// <summary>给界面显示的中文说明。</summary>
	public static string Describe(string p)
	{
		switch (p)
		{
		case Launcher:
			return "调用启动器全部功能（启动游戏、改实例、读写设置等）";
		case AiTools:
			return "给 AI 助手添加工具";
		case AiPresets:
			return "添加 AI 预设（人设）";
		case AiSessions:
			return "新建、读取并驱动 AI 对话";
		case AiLlm:
			return "直接调用 AI 模型（消耗你的额度）";
		case Credentials:
			return "保存和读取自己的密钥";
		case Ui:
			return "添加页面、AI 标签、首页卡片和提示框";
		case UiOverlay:
			return "在主窗口上显示浮层";
		case System:
			return "完整电脑权限：可以读写文件、联网、运行程序";
		default:
			return p;
		}
	}
}

/// <summary>
/// 插件清单 tipcl-plugin.json（DESIGN §2）。<see cref="Raw"/> 保留原始 JSON，给 Node 宿主和界面原样转发。
/// </summary>
public sealed class PluginManifest
{
	public const string FileName = "tipcl-plugin.json";

	private static readonly Regex IdRe = new Regex("^[a-z0-9][a-z0-9\\-_.]{1,63}$", RegexOptions.CultureInvariant);

	private static readonly HashSet<string> ConfigTypes = new HashSet<string> { "string", "boolean", "integer", "number" };

	private static readonly HashSet<string> UiKinds = new HashSet<string> { "page", "aiView", "dashCard", "block", "overlay", "settings", "action" };

	public PyDict Raw { get; private set; }

	public string Id { get; private set; }

	public string Name { get; private set; }

	public string Version { get; private set; }

	public string Author { get; private set; }

	public string Description { get; private set; }

	public string Icon { get; private set; }

	public string Homepage { get; private set; }

	/// <summary>node | dotnet</summary>
	public string Runtime { get; private set; }

	/// <summary>Node 宿主端入口，可以是多个（DESIGN §8）。</summary>
	public List<string> Main { get; private set; } = new List<string>();

	public string Client { get; private set; }

	public string Assembly { get; private set; }

	public string EntryType { get; private set; }

	public bool NpmInstall { get; private set; }

	public string NpmRegistry { get; private set; }

	public string EngineTipcl { get; private set; }

	public string EngineNode { get; private set; }

	/// <summary>声明的权限（node 运行时自动带上 system）。</summary>
	public List<string> Permissions { get; private set; } = new List<string>();

	/// <summary>config 声明：key → {type, default, label, description, enum, enumLabels, min, max, secret}</summary>
	public PyDict Config { get; private set; } = new PyDict();

	public List<PyDict> Credentials { get; private set; } = new List<PyDict>();

	public List<PyDict> AgentPresets { get; private set; } = new List<PyDict>();

	/// <summary>contributes 里静态声明的 UI（kind → spec 列表），和运行时 ui.register 等价。</summary>
	public List<(string kind, PyDict spec)> UiContributions { get; private set; } = new List<(string, PyDict)>();

	public Dictionary<string, string> Dependencies { get; private set; } = new Dictionary<string, string>();

	public Dictionary<string, string> OptionalDependencies { get; private set; } = new Dictionary<string, string>();

	public bool IsNode => Runtime == "node";

	public bool IsDotnet => Runtime == "dotnet";

	public static bool IsValidId(string id)
	{
		return id != null && IdRe.IsMatch(id) && !id.Contains("..");
	}

	/// <summary>从插件目录读清单；目录里没有清单或不合法时抛 <see cref="ValueError"/>。</summary>
	public static PluginManifest LoadDir(string dir)
	{
		string path = Path.Combine(dir, FileName);
		if (!File.Exists(path))
		{
			throw new ValueError("找不到 " + FileName + "：" + dir);
		}
		return Parse(File.ReadAllText(path));
	}

	public static PluginManifest Parse(string json)
	{
		object obj;
		try
		{
			string text = json ?? "";
			if (text.Length > 0 && text[0] == '\ufeff')
			{
				text = text.Substring(1);
			}
			obj = PyJson.Loads(text);
		}
		catch (Exception ex)
		{
			throw new ValueError(FileName + " 不是合法 JSON：" + ex.Message);
		}
		if (!(obj is PyDict d))
		{
			throw new ValueError(FileName + " 必须是 JSON 对象");
		}
		return FromDict(d);
	}

	public static PluginManifest FromDict(PyDict d)
	{
		List<string> errors = Validate(d);
		if (errors.Count > 0)
		{
			throw new ValueError("插件清单有误：" + string.Join("；", errors));
		}
		PluginManifest m = new PluginManifest
		{
			Raw = d,
			Id = Py.S(d.Get("id")),
			Name = Py.S(d.Get("name")),
			Version = Py.S(d.Get("version")),
			Author = Py.S(d.Get("author")),
			Description = Py.S(d.Get("description")),
			Icon = Py.S(d.Get("icon")),
			Homepage = Py.S(d.Get("homepage")),
			Runtime = Py.S(d.Get("runtime")),
			Client = Py.S(d.Get("client")),
			Assembly = Py.S(d.Get("assembly")),
			EntryType = Py.S(d.Get("entryType"))
		};
		if (m.Runtime.Length == 0)
		{
			m.Runtime = "node";
		}
		if (m.Name.Length == 0)
		{
			m.Name = m.Id;
		}
		object main = d.Get("main");
		if (main is string s && s.Length > 0)
		{
			m.Main.Add(s);
		}
		else if (main is List<object> ml)
		{
			m.Main.AddRange(ml.OfType<string>().Where((string x) => x.Length > 0));
		}
		PyDict install = d.Get("install") as PyDict;
		if (install != null)
		{
			m.NpmInstall = Py.Truthy(install.Get("npm"));
			m.NpmRegistry = Py.S(install.Get("registry"));
		}
		if (d.Get("engines") is PyDict eng)
		{
			m.EngineTipcl = Py.S(eng.Get("tipcl"));
			m.EngineNode = Py.S(eng.Get("node"));
		}
		foreach (object p in Py.ListOr(d.Get("permissions")))
		{
			string ps = Py.S(p);
			if (ps.Length > 0 && !m.Permissions.Contains(ps))
			{
				m.Permissions.Add(ps);
			}
		}
		// Node 插件做不到沙箱：system 自动视为已声明（DESIGN §2）
		if (m.IsNode && !m.Permissions.Contains(PluginPermissions.System))
		{
			m.Permissions.Add(PluginPermissions.System);
		}
		if (d.Get("config") is PyDict cfg)
		{
			m.Config = cfg;
		}
		if (d.Get("contributes") is PyDict contrib)
		{
			m.Credentials = Py.ListOr(contrib.Get("credentials")).OfType<PyDict>().ToList();
			m.AgentPresets = Py.ListOr(contrib.Get("agentPresets")).OfType<PyDict>().ToList();
			foreach (string kind in UiKinds)
			{
				// contributes.pages / aiViews / dashCards / overlays / actions 列表，settings 为单个对象
				object v = contrib.Get(kind + "s") ?? contrib.Get(kind);
				if (v is PyDict one)
				{
					m.UiContributions.Add((kind, one));
				}
				else if (v is List<object> many)
				{
					foreach (PyDict spec in many.OfType<PyDict>())
					{
						m.UiContributions.Add((kind, spec));
					}
				}
			}
		}
		if (d.Get("dependencies") is PyDict deps)
		{
			foreach (KeyValuePair<string, object> kv in deps)
			{
				m.Dependencies[kv.Key] = Py.S(kv.Value);
			}
		}
		if (d.Get("optionalDependencies") is PyDict odeps)
		{
			foreach (KeyValuePair<string, object> kv2 in odeps)
			{
				m.OptionalDependencies[kv2.Key] = Py.S(kv2.Value);
			}
		}
		return m;
	}

	/// <summary>返回全部问题（空列表 = 合法）。</summary>
	public static List<string> Validate(PyDict d)
	{
		List<string> e = new List<string>();
		string id = Py.S(d.Get("id"));
		if (!IsValidId(id))
		{
			e.Add("id 必须是 2~64 位小写字母/数字/-_.，并以字母或数字开头");
		}
		else if (id == "host")
		{
			e.Add("id 不能是 host（保留给 Node 宿主）");
		}
		string ver = Py.S(d.Get("version"));
		if (!SemVer.IsValid(ver) || ver.Split('-')[0].Split('.').Length != 3)
		{
			e.Add("version 必须是 semver（如 1.0.0）");
		}
		string rt = Py.S(d.Get("runtime"));
		if (rt.Length == 0)
		{
			rt = "node";
		}
		if (rt != "node" && rt != "dotnet")
		{
			e.Add("runtime 只能是 node 或 dotnet");
		}
		if (rt == "node")
		{
			object main = d.Get("main");
			bool ok = (main is string s && s.Length > 0) || (main is List<object> l && l.Count > 0 && l.All((object x) => x is string xs && xs.Length > 0));
			if (main != null && !ok)
			{
				e.Add("main 必须是字符串或字符串数组");
			}
			if (main == null && !(d.Get("client") is string))
			{
				e.Add("node 插件至少要有 main 或 client");
			}
			foreach (string rel in new[] { Py.S(d.Get("client")) }.Concat(Py.ListOr(main is string ms ? Py.L(ms) : main).Select(Py.S)))
			{
				if (rel.Length > 0 && !IsSafeRelPath(rel))
				{
					e.Add("入口路径不能跳出插件目录：" + rel);
				}
			}
		}
		if (rt == "dotnet")
		{
			string asm = Py.S(d.Get("assembly"));
			if (asm.Length == 0 || !IsSafeRelPath(asm))
			{
				e.Add("dotnet 插件必须声明 assembly（插件目录内的相对路径）");
			}
			if (Py.S(d.Get("entryType")).Length == 0)
			{
				e.Add("dotnet 插件必须声明 entryType");
			}
		}
		foreach (object p in Py.ListOr(d.Get("permissions")))
		{
			if (!(p is string ps) || !PluginPermissions.IsKnown(ps))
			{
				e.Add("未知权限：" + Py.Str(p));
			}
		}
		if (d.Get("engines") is PyDict eng)
		{
			foreach (KeyValuePair<string, object> kv in eng)
			{
				if (!(kv.Value is string r) || !SemVer.IsValidRange(r))
				{
					e.Add("engines." + kv.Key + " 不是合法的版本范围");
				}
			}
		}
		foreach (string depKey in new[] { "dependencies", "optionalDependencies" })
		{
			if (d.Get(depKey) is PyDict deps)
			{
				foreach (KeyValuePair<string, object> kv2 in deps)
				{
					if (!IsValidId(kv2.Key))
					{
						e.Add(depKey + " 里的 id 不合法：" + kv2.Key);
					}
					if (!(kv2.Value is string r2) || !SemVer.IsValidRange(r2))
					{
						e.Add(depKey + "." + kv2.Key + " 不是合法的版本范围");
					}
				}
			}
			else if (d.Get(depKey) != null)
			{
				e.Add(depKey + " 必须是对象");
			}
		}
		if (d.Get("config") != null)
		{
			if (!(d.Get("config") is PyDict cfg))
			{
				e.Add("config 必须是对象");
			}
			else
			{
				foreach (KeyValuePair<string, object> kv3 in cfg)
				{
					if (!(kv3.Value is PyDict field))
					{
						e.Add("config." + kv3.Key + " 必须是对象");
						continue;
					}
					string t = Py.S(field.Get("type"));
					if (!ConfigTypes.Contains(t))
					{
						e.Add("config." + kv3.Key + ".type 只能是 string/boolean/integer/number");
					}
				}
			}
		}
		if (d.Get("contributes") is PyDict contrib)
		{
			foreach (PyDict preset in Py.ListOr(contrib.Get("agentPresets")).OfType<PyDict>())
			{
				string pid = Py.S(preset.Get("id"));
				if (!IsValidId(pid) && !Regex.IsMatch(pid, "^[A-Za-z0-9][A-Za-z0-9\\-_.]{0,63}$"))
				{
					e.Add("agentPresets 的 id 不合法：" + pid);
				}
				string spf = Py.S(preset.Get("systemPromptFile"));
				if (spf.Length > 0 && !IsSafeRelPath(spf))
				{
					e.Add("systemPromptFile 不能跳出插件目录：" + spf);
				}
			}
		}
		else if (d.Get("contributes") != null)
		{
			e.Add("contributes 必须是对象");
		}
		return e;
	}

	/// <summary>相对路径，不能是绝对路径，也不能有 ..。</summary>
	public static bool IsSafeRelPath(string rel)
	{
		if (string.IsNullOrWhiteSpace(rel))
		{
			return false;
		}
		string r = rel.Replace('\\', '/');
		if (r.StartsWith("/") || r.Contains(":"))
		{
			return false;
		}
		return !r.Split('/').Any((string seg) => seg == "..");
	}

	/// <summary>当前 TipCL 是否满足 engines.tipcl。</summary>
	public bool EngineOk(string tipclVersion, out string error)
	{
		error = null;
		if (!string.IsNullOrEmpty(EngineTipcl) && !SemVer.Satisfies(tipclVersion, EngineTipcl))
		{
			error = "需要 TipCL " + EngineTipcl + "（当前 " + tipclVersion + "）";
			return false;
		}
		return true;
	}

	/// <summary>config 默认值。</summary>
	public PyDict ConfigDefaults()
	{
		PyDict r = new PyDict();
		foreach (KeyValuePair<string, object> kv in Config)
		{
			if (kv.Value is PyDict f)
			{
				r[kv.Key] = DefaultFor(f);
			}
		}
		return r;
	}

	private static object DefaultFor(PyDict f)
	{
		if (f.ContainsKey("default"))
		{
			return f["default"];
		}
		switch (Py.S(f.Get("type")))
		{
		case "boolean":
			return false;
		case "integer":
			return 0L;
		case "number":
			return 0.0;
		default:
			return "";
		}
	}

	public bool IsSecretConfig(string key)
	{
		return Config.Get(key) is PyDict f && Py.Truthy(f.Get("secret"));
	}

	/// <summary>按 config 声明校验并规整一个值（类型转换、enum、min/max）。未声明的 key 抛错。</summary>
	public object CoerceConfig(string key, object value)
	{
		if (!(Config.Get(key) is PyDict f))
		{
			throw new ValueError("未声明的配置项：" + key);
		}
		string t = Py.S(f.Get("type"));
		object v = value;
		switch (t)
		{
		case "boolean":
			if (v is string bs)
			{
				v = bs == "true" || bs == "1";
			}
			else
			{
				v = Py.Truthy(v);
			}
			break;
		case "integer":
			try
			{
				v = (v is string isv) ? long.Parse(isv.Trim()) : Py.Int(v);
			}
			catch (Exception)
			{
				throw new ValueError(key + " 必须是整数");
			}
			break;
		case "number":
			try
			{
				v = (v is string nsv) ? double.Parse(nsv.Trim(), System.Globalization.CultureInfo.InvariantCulture) : Py.Float(v);
			}
			catch (Exception)
			{
				throw new ValueError(key + " 必须是数字");
			}
			break;
		default:
			v = (v == null) ? "" : ((v is string s) ? s : Py.Str(v));
			break;
		}
		if ((t == "integer" || t == "number") && f.Get("min") != null && Py.Float(v) < Py.Float(f["min"]))
		{
			throw new ValueError(key + " 不能小于 " + Py.Str(f["min"]));
		}
		if ((t == "integer" || t == "number") && f.Get("max") != null && Py.Float(v) > Py.Float(f["max"]))
		{
			throw new ValueError(key + " 不能大于 " + Py.Str(f["max"]));
		}
		if (f.Get("enum") is List<object> en && en.Count > 0 && !en.Any((object x) => Py.Eq(x, v)))
		{
			throw new ValueError(key + " 只能取：" + string.Join(" / ", en.Select(Py.Str)));
		}
		return v;
	}
}
