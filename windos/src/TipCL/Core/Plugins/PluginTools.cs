using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core.Plugins;

/// <summary>插件注册给 AI 的一个工具。</summary>
public sealed class PluginToolEntry
{
	public string Plugin;

	public string Name;

	public string Description = "";

	/// <summary>JSON Schema（type:object）。</summary>
	public PyDict Parameters = PyDict.Of("type", "object", "properties", new PyDict());

	/// <summary>执行超时；默认 30 分钟（DESIGN §0：插件工具无上限，可在工具上声明）。0 = 不限。</summary>
	public long TimeoutMs = PluginTools.DefaultTimeoutMs;

	public string Label = "";

	/// <summary>true：只在白名单匹配的预设对话里出现，普通对话不带。</summary>
	public bool PresetOnly;

	/// <summary>
	/// 执行：传入 tool.execute 的 params（{plugin, name, callId, chatId, args, messageText}），返回
	/// {content:[{type:"text",text} | {type:"image", attachment:{id} | data, mediaType}], isError?}。
	/// 取消（用户点停止）时 token 触发。
	/// </summary>
	public Func<PyDict, CancellationToken, object> Executor;

	public PyDict Schema()
	{
		return PyDict.Of("type", "function", "function", PyDict.Of("name", Name, "description", Description, "parameters", Parameters));
	}
}

/// <summary>工具执行结果转成给模型看的东西：文字 + 图片文件路径。</summary>
public sealed class PluginToolOutput
{
	public string Text = "";

	public List<string> Images = new List<string>();

	public bool IsError;

	/// <summary>原始 content（给 tool/result 事件用，图片只留 attachment 引用）。</summary>
	public List<object> Content = new List<object>();
}

/// <summary>插件工具注册表 + 执行。</summary>
public static class PluginTools
{
	public const long DefaultTimeoutMs = 30L * 60L * 1000L;

	private static readonly object Lock = new object();

	private static readonly Dictionary<string, PluginToolEntry> Tools = new Dictionary<string, PluginToolEntry>(StringComparer.Ordinal);

	/// <summary>内置工具名（不许插件覆盖）。可在测试里替换。</summary>
	public static Func<HashSet<string>> BuiltinNames = delegate
	{
		try
		{
			return new HashSet<string>(TipCL.Core.Launcher.Ai.Tools.TOOL_SCHEMAS.Select(TipCL.Core.Launcher.Ai.Tools.SchemaName));
		}
		catch (Exception)
		{
			return new HashSet<string>();
		}
	};

	public static bool IsValidName(string name)
	{
		return !string.IsNullOrEmpty(name) && name.Length <= 64 && System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_\\-]+$");
	}

	public static void Register(PluginToolEntry e)
	{
		if (!IsValidName(e.Name))
		{
			throw new PluginRpcException("invalid_params", "工具名只能用字母、数字、_ 和 -（最多 64 位）：" + e.Name);
		}
		if (e.Name.StartsWith("mcp_", StringComparison.Ordinal) || BuiltinNames().Contains(e.Name))
		{
			throw new PluginRpcException("conflict", "工具名和 TipCL 内置工具冲突：" + e.Name);
		}
		lock (Lock)
		{
			if (Tools.TryGetValue(e.Name, out PluginToolEntry old) && old.Plugin != e.Plugin)
			{
				throw new PluginRpcException("conflict", "工具名已被插件 " + old.Plugin + " 占用：" + e.Name);
			}
			Tools[e.Name] = e;
		}
	}

	public static bool Unregister(string plugin, string name)
	{
		lock (Lock)
		{
			if (Tools.TryGetValue(name, out PluginToolEntry e) && e.Plugin == plugin)
			{
				Tools.Remove(name);
				return true;
			}
			return false;
		}
	}

	public static void UnregisterPlugin(string plugin)
	{
		lock (Lock)
		{
			foreach (string k in Tools.Where((KeyValuePair<string, PluginToolEntry> kv) => kv.Value.Plugin == plugin).Select((KeyValuePair<string, PluginToolEntry> kv) => kv.Key).ToList())
			{
				Tools.Remove(k);
			}
		}
	}

	public static PluginToolEntry Get(string name)
	{
		lock (Lock)
		{
			return (name != null && Tools.TryGetValue(name, out PluginToolEntry e)) ? e : null;
		}
	}

	public static bool IsPluginTool(string name)
	{
		return Get(name) != null;
	}

	public static List<PluginToolEntry> All()
	{
		lock (Lock)
		{
			return Tools.Values.OrderBy((PluginToolEntry e) => e.Name, StringComparer.Ordinal).ToList();
		}
	}

	public static void Clear()
	{
		lock (Lock)
		{
			Tools.Clear();
		}
	}

	/// <summary>
	/// 参数写法兼容：JSON Schema 原样用；DSH 风格 <c>{k:{type, required, description}}</c> 转成 JSON Schema。
	/// </summary>
	public static PyDict NormalizeParameters(object raw)
	{
		if (!(raw is PyDict p) || p.Count == 0)
		{
			return PyDict.Of("type", "object", "properties", new PyDict());
		}
		if (Py.Eq(p.Get("type"), "object") || p.ContainsKey("properties"))
		{
			PyDict r = p.Copy();
			if (!r.ContainsKey("type"))
			{
				r["type"] = "object";
			}
			if (!(r.Get("properties") is PyDict))
			{
				r["properties"] = new PyDict();
			}
			return r;
		}
		PyDict props = new PyDict();
		List<object> required = new List<object>();
		foreach (KeyValuePair<string, object> kv in p)
		{
			PyDict f = (kv.Value as PyDict)?.Copy() ?? PyDict.Of("type", "string");
			if (Py.Truthy(f.Pop("required")))
			{
				required.Add(kv.Key);
			}
			f.Pop("optional");
			props[kv.Key] = f;
		}
		PyDict schema = PyDict.Of("type", "object", "properties", props);
		if (required.Count > 0)
		{
			schema["required"] = required;
		}
		return schema;
	}

	/// <summary>
	/// 执行插件工具。阻塞直到完成、超时或 <paramref name="cancelled"/> 返回 true（此时抛 <see cref="ToolCancelled"/>，并取消插件侧）。
	/// </summary>
	public static PluginToolOutput Execute(PluginToolEntry e, string chatId, string callId, PyDict args, string messageText, Func<bool> cancelled)
	{
		PyDict prm = PyDict.Of("plugin", e.Plugin, "name", e.Name, "callId", callId, "chatId", chatId ?? "", "args", args ?? new PyDict(), "messageText", messageText ?? "");
		using CancellationTokenSource cts = new CancellationTokenSource();
		Task<object> task = Task.Run(() => e.Executor(prm, cts.Token));
		double deadline = (e.TimeoutMs > 0) ? (Py.Monotonic() + e.TimeoutMs / 1000.0) : double.MaxValue;
		while (!task.Wait(200))
		{
			if (cancelled != null && cancelled())
			{
				cts.Cancel();
				throw new ToolCancelled();
			}
			if (Py.Monotonic() > deadline)
			{
				cts.Cancel();
				return new PluginToolOutput
				{
					IsError = true,
					Text = PyJson.Dumps(PyDict.Of("ok", false, "error_code", "timeout", "message", "插件工具执行超时（" + e.TimeoutMs / 1000 + " 秒）", "tool", e.Name), null, ensureAscii: false)
				};
			}
		}
		object result;
		try
		{
			result = task.GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
			if (cancelled != null && cancelled())
			{
				throw new ToolCancelled();
			}
			return ErrorOutput(e.Name, "插件工具被取消");
		}
		catch (Exception ex)
		{
			string msg = (ex is PluginRpcException pre) ? pre.Message : Py.ExcStr(ex);
			return ErrorOutput(e.Name, msg);
		}
		return ToOutput(result, e.Plugin);
	}

	private static PluginToolOutput ErrorOutput(string name, string msg)
	{
		return new PluginToolOutput
		{
			IsError = true,
			Text = PyJson.Dumps(PyDict.Of("ok", false, "error_code", "plugin_error", "message", msg, "tool", name), null, ensureAscii: false),
			Content = Py.L(PyDict.Of("type", "text", "text", msg))
		};
	}

	/// <summary>
	/// 把插件返回的 {content:[...], isError} 转成文字 + 图片。图片：attachment.id 解析成文件路径；data(base64) 先存成附件。
	/// 也接受直接返回字符串 / 任意 JSON（转成文字）。
	/// </summary>
	public static PluginToolOutput ToOutput(object result, string plugin)
	{
		PluginToolOutput o = new PluginToolOutput();
		List<object> content;
		if (result is PyDict d && d.Get("content") is List<object> c)
		{
			content = c;
			o.IsError = Py.Truthy(d.Get("isError"));
		}
		else if (result is List<object> l)
		{
			content = l;
		}
		else if (result is string s)
		{
			content = Py.L(PyDict.Of("type", "text", "text", s));
		}
		else
		{
			content = Py.L(PyDict.Of("type", "text", "text", (result == null) ? "" : PyJson.Dumps(result, null, ensureAscii: false)));
		}
		StringBuilder sb = new StringBuilder();
		foreach (object part in content)
		{
			if (part is string ps)
			{
				part2Text(ps);
				continue;
			}
			if (!(part is PyDict pd))
			{
				continue;
			}
			string type = Py.S(pd.Get("type"));
			if (type == "text")
			{
				part2Text(Py.S(pd.Get("text")));
			}
			else if (type == "image")
			{
				string path = null;
				string attId = null;
				try
				{
					if (pd.Get("attachment") is PyDict att)
					{
						attId = Py.S(att.Get("id"));
						path = PluginAttachments.Resolve(attId);
					}
					else if (pd.Get("attachment") is string attS)
					{
						attId = attS;
						path = PluginAttachments.Resolve(attS);
					}
					else if (pd.Get("data") is string b64)
					{
						PyDict saved = PluginAttachments.Save(plugin, Convert.FromBase64String(StripDataUrl(b64)), Py.S(pd.Get("mediaType")), Py.S(pd.Get("name")));
						attId = Py.S(saved["id"]);
						path = PluginAttachments.Resolve(attId);
					}
				}
				catch (Exception)
				{
					path = null;
				}
				if (path != null)
				{
					o.Images.Add(path);
					o.Content.Add(PyDict.Of("type", "image", "attachment", PyDict.Of("id", attId), "mediaType", Py.S(pd.Get("mediaType"))));
				}
				else
				{
					part2Text("（图片读取失败）");
				}
			}
			else
			{
				part2Text(PyJson.Dumps(pd, null, ensureAscii: false));
			}
		}
		o.Text = sb.ToString().Trim();
		if (o.Text.Length == 0 && o.Images.Count > 0)
		{
			o.Text = "（返回了 " + o.Images.Count + " 张图片，见下一条消息）";
		}
		return o;

		void part2Text(string t)
		{
			if (string.IsNullOrEmpty(t))
			{
				return;
			}
			if (sb.Length > 0)
			{
				sb.Append('\n');
			}
			sb.Append(t);
			o.Content.Add(PyDict.Of("type", "text", "text", t));
		}
	}

	private static string StripDataUrl(string b64)
	{
		int comma = b64.IndexOf(',');
		if (b64.StartsWith("data:", StringComparison.Ordinal) && comma > 0)
		{
			return b64.Substring(comma + 1);
		}
		return b64;
	}
}

/// <summary>
/// 插件附件（DESIGN §4 attachments.save）：图片存到 plugin-data/&lt;id&gt;/cache/attachments/&lt;attId&gt;.&lt;ext&gt;，
/// 返回 id；Agent 用 <see cref="Resolve"/> 拿到文件路径，作为视觉输入交给模型。
/// </summary>
public static class PluginAttachments
{
	public const long MaxBytes = 20L * 1024 * 1024;

	private static readonly object Lock = new object();

	private static readonly Dictionary<string, string> Index = new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>附件根（plugin-data）；测试可替换。</summary>
	public static Func<PluginDirs> Dirs = () => new PluginDirs();

	public static PyDict Save(string plugin, byte[] data, string mediaType, string name)
	{
		if (data == null || data.Length == 0)
		{
			throw new PluginRpcException("invalid_params", "图片是空的");
		}
		if (data.Length > MaxBytes)
		{
			throw new PluginRpcException("invalid_params", "图片太大（超过 20MB）");
		}
		string mime = string.IsNullOrEmpty(mediaType) ? (Images.SniffMime(data) ?? "image/png") : mediaType;
		string ext = ExtOf(mime);
		string id = "att_" + Guid.NewGuid().ToString("N").Substring(0, 20);
		string dir = Dirs().AttachmentsDir(plugin);
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, id + ext);
		File.WriteAllBytes(path, data);
		lock (Lock)
		{
			Index[id] = path;
		}
		Prune(dir);
		return PyDict.Of("id", id, "mediaType", mime, "name", string.IsNullOrEmpty(name) ? id : name);
	}

	/// <summary>附件 id → 文件路径；找不到返回 null。</summary>
	public static string Resolve(string id)
	{
		if (string.IsNullOrEmpty(id) || id.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
		{
			return null;
		}
		lock (Lock)
		{
			if (Index.TryGetValue(id, out string p) && File.Exists(p))
			{
				return p;
			}
		}
		try
		{
			string root = Dirs().DataRoot;
			if (!Directory.Exists(root))
			{
				return null;
			}
			foreach (string pd in Directory.GetDirectories(root))
			{
				string ad = Path.Combine(pd, "cache", "attachments");
				if (!Directory.Exists(ad))
				{
					continue;
				}
				string hit = Directory.GetFiles(ad, id + ".*").FirstOrDefault();
				if (hit != null)
				{
					lock (Lock)
					{
						Index[id] = hit;
					}
					return hit;
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private static string ExtOf(string mime)
	{
		switch ((mime ?? "").ToLowerInvariant())
		{
		case "image/jpeg":
		case "image/jpg":
			return ".jpg";
		case "image/gif":
			return ".gif";
		case "image/webp":
			return ".webp";
		case "image/bmp":
			return ".bmp";
		default:
			return ".png";
		}
	}

	/// <summary>每个插件最多留 500 个附件，多了删最旧的。</summary>
	private static void Prune(string dir)
	{
		try
		{
			FileInfo[] files = new DirectoryInfo(dir).GetFiles("att_*");
			if (files.Length <= 500)
			{
				return;
			}
			foreach (FileInfo f in files.OrderBy((FileInfo x) => x.LastWriteTimeUtc).Take(files.Length - 400))
			{
				try
				{
					f.Delete();
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}
}
