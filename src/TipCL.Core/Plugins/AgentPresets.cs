using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TipCL.Core.Plugins;

/// <summary>一个 agent 预设（DESIGN §2 contributes.agentPresets / presets.register）。</summary>
public sealed class AgentPreset
{
	/// <summary>完整 id：<c>plugin:&lt;插件id&gt;/&lt;预设id&gt;</c></summary>
	public string FullId;

	public string Plugin;

	public string Id;

	public string Name;

	public string Description;

	public long Order;

	public string SystemPrompt = "";

	public bool ReplaceSystemPrompt;

	public bool IncludeRuntimeContext = true;

	/// <summary>工具白名单（glob，* 和 ?）。</summary>
	public List<string> ToolInclude = new List<string>();

	/// <summary>工具黑名单（glob），优先于白名单。</summary>
	public List<string> ToolExclude = new List<string>();

	/// <summary>true：还带 TipCL 内置工具（走原有的按需选择）。</summary>
	public bool Builtin = true;

	/// <summary>0 = 不限（硬上限 <see cref="AgentPresets.HardMaxRounds"/>）；&lt;0 = 用全局设置。</summary>
	public long MaxRounds = -1;

	public string View = "";

	public PyDict Raw = new PyDict();

	public static string MakeFullId(string plugin, string id)
	{
		return "plugin:" + plugin + "/" + id;
	}

	/// <summary>从 manifest / 运行时注册的字典解析。systemPromptFile 相对 <paramref name="pluginDir"/> 读。</summary>
	public static AgentPreset FromDict(string plugin, PyDict d, string pluginDir = null)
	{
		string id = Py.S(d.Get("id"));
		if (id.Length == 0)
		{
			throw new ValueError("预设缺少 id");
		}
		AgentPreset p = new AgentPreset
		{
			Plugin = plugin,
			Id = id,
			FullId = MakeFullId(plugin, id),
			Name = Py.S(d.Get("name")),
			Description = Py.S(d.Get("description")),
			Order = Py.IntOr(d.Get("order"), 100L),
			SystemPrompt = Py.S(d.Get("systemPrompt")),
			ReplaceSystemPrompt = Py.Truthy(d.Get("replaceSystemPrompt")),
			IncludeRuntimeContext = !d.ContainsKey("includeRuntimeContext") || Py.Truthy(d.Get("includeRuntimeContext")),
			View = Py.S(d.Get("view")),
			Raw = d.Copy()
		};
		if (p.Name.Length == 0)
		{
			p.Name = id;
		}
		string spf = Py.S(d.Get("systemPromptFile"));
		if (p.SystemPrompt.Length == 0 && spf.Length > 0 && pluginDir != null && PluginManifest.IsSafeRelPath(spf))
		{
			try
			{
				p.SystemPrompt = File.ReadAllText(Path.Combine(pluginDir, spf));
			}
			catch (Exception ex)
			{
				throw new ValueError("读不了 systemPromptFile " + spf + "：" + ex.Message);
			}
		}
		if (d.ContainsKey("maxRounds") && d.Get("maxRounds") != null)
		{
			p.MaxRounds = Math.Max(0L, Py.IntOr(d.Get("maxRounds"), 0L));
		}
		object tools = d.Get("tools");
		if (tools is PyDict td)
		{
			p.ToolInclude = Strs(td.Get("include"));
			p.ToolExclude = Strs(td.Get("exclude"));
			p.Builtin = !td.ContainsKey("builtin") || Py.Truthy(td.Get("builtin"));
		}
		else if (tools is List<object>)
		{
			p.ToolInclude = Strs(tools);
			p.Builtin = false;
		}
		return p;

		static List<string> Strs(object o)
		{
			if (o is string s)
			{
				return new List<string> { s };
			}
			return Py.ListOr(o).Select(Py.S).Where((string x) => x.Length > 0).ToList();
		}
	}

	/// <summary>给界面（新建对话选预设）用的摘要。</summary>
	public PyDict ToInfo()
	{
		return PyDict.Of("id", FullId, "plugin", Plugin, "name", Name, "description", Description, "order", Order, "view", View);
	}
}

/// <summary>
/// 预设注册表 + 工具筛选逻辑。纯逻辑，不依赖 Node。
/// </summary>
public static class AgentPresets
{
	/// <summary>maxRounds:0 时的硬上限，防失控。</summary>
	public const long HardMaxRounds = 10000L;

	private static readonly object Lock = new object();

	private static readonly Dictionary<string, AgentPreset> Presets = new Dictionary<string, AgentPreset>(StringComparer.Ordinal);

	public static void Register(AgentPreset p)
	{
		lock (Lock)
		{
			Presets[p.FullId] = p;
		}
	}

	public static bool Unregister(string plugin, string id)
	{
		lock (Lock)
		{
			return Presets.Remove(AgentPreset.MakeFullId(plugin, id));
		}
	}

	public static void UnregisterPlugin(string plugin)
	{
		lock (Lock)
		{
			foreach (string k in Presets.Where((KeyValuePair<string, AgentPreset> kv) => kv.Value.Plugin == plugin).Select((KeyValuePair<string, AgentPreset> kv) => kv.Key).ToList())
			{
				Presets.Remove(k);
			}
		}
	}

	/// <summary>按完整 id 找；也接受只写预设 id（唯一时）。</summary>
	public static AgentPreset Get(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return null;
		}
		lock (Lock)
		{
			if (Presets.TryGetValue(id, out AgentPreset p))
			{
				return p;
			}
			List<AgentPreset> hits = Presets.Values.Where((AgentPreset x) => x.Id == id).ToList();
			return (hits.Count == 1) ? hits[0] : null;
		}
	}

	public static List<AgentPreset> All()
	{
		lock (Lock)
		{
			return Presets.Values.OrderBy((AgentPreset p) => p.Order).ThenBy((AgentPreset p) => p.Name, StringComparer.Ordinal).ToList();
		}
	}

	public static void Clear()
	{
		lock (Lock)
		{
			Presets.Clear();
		}
	}

	/// <summary>glob（* 任意串，? 单字符），大小写敏感。</summary>
	public static bool GlobMatch(string pattern, string name)
	{
		if (pattern == null || name == null)
		{
			return false;
		}
		string re = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
		return Regex.IsMatch(name, re, RegexOptions.CultureInvariant);
	}

	public static bool ToolAllowed(AgentPreset p, string toolName)
	{
		if (p == null)
		{
			return true;
		}
		if (p.ToolExclude.Any((string g) => GlobMatch(g, toolName)))
		{
			return false;
		}
		return p.ToolInclude.Any((string g) => GlobMatch(g, toolName));
	}

	/// <summary>
	/// 本轮给模型的工具：
	/// <list type="bullet">
	/// <item>没有预设：内置按需选择的结果 + 全部插件工具（声明了 presetOnly 的除外）。</item>
	/// <item>有预设：白名单匹配到的插件工具；builtin:true 时再加内置按需选择的结果（只受黑名单 exclude 约束）。</item>
	/// </list>
	/// </summary>
	/// <param name="builtinSelected">内置按需选择出的 schema 列表（OpenAI tools 格式）。</param>
	/// <param name="pluginTools">插件工具：(schema, presetOnly)。</param>
	public static List<object> FilterTools(AgentPreset preset, List<object> builtinSelected, IEnumerable<(PyDict schema, bool presetOnly)> pluginTools)
	{
		List<object> r = new List<object>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (preset == null || preset.Builtin)
		{
			foreach (object s in builtinSelected ?? new List<object>())
			{
				string n = SchemaName(s);
				// builtin:true 时内置工具全保留（走原有按需选择），只受黑名单约束
				if (preset != null && preset.ToolExclude.Any((string g) => GlobMatch(g, n)))
				{
					continue;
				}
				if (seen.Add(n))
				{
					r.Add(s);
				}
			}
		}
		foreach ((PyDict schema, bool presetOnly) in pluginTools ?? Enumerable.Empty<(PyDict, bool)>())
		{
			string n2 = SchemaName(schema);
			bool ok = (preset == null) ? !presetOnly : ToolAllowed(preset, n2);
			if (ok && seen.Add(n2))
			{
				r.Add(schema);
			}
		}
		return r;
	}

	public static string SchemaName(object s)
	{
		if (s is PyDict d && d.Get("function") is PyDict f)
		{
			return Py.S(f.Get("name"));
		}
		return "";
	}

	/// <summary>实际最大步数：预设 0 → 硬上限；预设 n → n（不超过硬上限）；无预设 → 设置值（0 时默认 20）。</summary>
	public static long EffectiveMaxRounds(AgentPreset preset, long settingsValue)
	{
		if (preset != null && preset.MaxRounds >= 0)
		{
			return (preset.MaxRounds == 0) ? HardMaxRounds : Math.Min(preset.MaxRounds, HardMaxRounds);
		}
		return (settingsValue <= 0) ? 20L : settingsValue;
	}
}
