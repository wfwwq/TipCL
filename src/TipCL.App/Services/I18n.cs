using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TipCL.Core;

namespace TipCL.Services;

public static class I18n
{
	public const string DefaultLang = "zh_CN";

	private static readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

	private static Dictionary<string, string> _current = new Dictionary<string, string>(StringComparer.Ordinal);

	private static Dictionary<string, string> _en = new Dictionary<string, string>(StringComparer.Ordinal);

	private static readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);

	private static readonly object _gate = new object();

	private static bool _loaded;

	public static string Current { get; private set; } = "zh_CN";

	/// <summary>
	/// 词表来源。mac 侧改读 <b>Core 内嵌资源</b>(<c>TipCL.Core.Resources.locales.*.json</c>,
	/// A2 已按与 Windows csproj 相同的 LogicalName 嵌入),所以这里报的是资源位置而不是磁盘目录。
	/// 名字与类型与 Windows 版一致(公开面不变),只有取值语义变了(见 PROGRESS.md 的 C2a 差异表)。
	/// </summary>
	public static string LocalesDir { get; private set; } = "";

	public static IReadOnlyCollection<string> Missing
	{
		get
		{
			lock (_gate)
			{
				return (IReadOnlyCollection<string>)(object)_missing.ToArray();
			}
		}
	}

	public static IReadOnlyCollection<string> KnownKeys
	{
		get
		{
			lock (_gate)
			{
				Dictionary<string, string> value;
				return (IReadOnlyCollection<string>)(object)(_tables.TryGetValue("zh_CN", out value) ? value.Keys.ToArray() : Array.Empty<string>());
			}
		}
	}

	public static IReadOnlyList<string> Languages
	{
		get
		{
			EnsureTables();
			lock (_gate)
			{
				return _tables.Keys.OrderBy<string, string>((string k) => k, StringComparer.Ordinal).ToArray();
			}
		}
	}

	public static void Init(string[]? args = null)
	{
		Load(ArgValue(args, "--lang") ?? Environment.GetEnvironmentVariable("TIPCL_LANG") ?? ReadConfigLanguage());
	}

	public static void Load(string? lang)
	{
		EnsureTables();
		lang = (lang ?? "").Trim().Replace('-', '_');
		if (lang.Length == 0)
		{
			lang = "zh_CN";
		}
		lock (_gate)
		{
			if (!_tables.ContainsKey(lang))
			{
				lang = "zh_CN";
			}
			Current = lang;
			_current = _tables[lang];
			_en = (_tables.TryGetValue("en", out Dictionary<string, string> value) ? value : new Dictionary<string, string>(StringComparer.Ordinal));
			_missing.Clear();
		}
		CultureInfo.CurrentUICulture = ((!(lang == "en")) ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.GetCultureInfo("en-US"));
	}

	public static string L(string zh)
	{
		if (string.IsNullOrEmpty(zh))
		{
			return zh;
		}
		if (_current.TryGetValue(zh, out string value) && value.Length > 0)
		{
			return value;
		}
		if (Current != "zh_CN")
		{
			if (Current != "en" && _en.TryGetValue(zh, out string value2) && value2.Length > 0)
			{
				return value2;
			}
			if (HasCjk(zh))
			{
				lock (_gate)
				{
					_missing.Add(zh);
				}
			}
		}
		return zh;
	}

	public static string? Peek(string zh)
	{
		if (string.IsNullOrEmpty(zh))
		{
			return null;
		}
		if (_current.TryGetValue(zh, out string value) && value.Length > 0)
		{
			return value;
		}
		if (Current != "zh_CN" && Current != "en" && _en.TryGetValue(zh, out string value2) && value2.Length > 0)
		{
			return value2;
		}
		return null;
	}

	public static string L(string zh, params object?[] args)
	{
		string text = L(zh);
		if (args.Length == 0)
		{
			return text;
		}
		try
		{
			return string.Format(CultureInfo.CurrentCulture, text, args);
		}
		catch (FormatException)
		{
			try
			{
				return string.Format(CultureInfo.CurrentCulture, zh, args);
			}
			catch (FormatException)
			{
				return text;
			}
		}
	}

	public static bool Has(string lang, string key)
	{
		EnsureTables();
		lock (_gate)
		{
			Dictionary<string, string> value;
			string value2;
			return _tables.TryGetValue(lang, out value) && value.TryGetValue(key, out value2) && value2.Length > 0;
		}
	}

	private static void EnsureTables()
	{
		lock (_gate)
		{
			if (_loaded)
			{
				return;
			}
			_loaded = true;
			_tables["zh_CN"] = new Dictionary<string, string>(StringComparer.Ordinal);
			_tables["en"] = new Dictionary<string, string>(StringComparer.Ordinal);
			// mac 侧的一处最小改动(见 PROGRESS.md 的 C2a 差异表):
			// Windows 版从磁盘找词表(先 root\mclauncher\locales,开发态回退 root\src\TipCL\Resources\locales);
			// mac 版没有外部 payload,词表按 GOAL 的决定以与 Windows csproj 相同的 LogicalName 内嵌进
			// TipCL.Core,所以这里直接走 CoreResources 读内嵌资源 —— 与搬进 Core 的
			// I18nCore.LoadStrings() 是同一份数据、同一套资源名(A2 已落地)。
			LocalesDir = CoreResources.Prefix + "Resources.locales";
			foreach (string item in CoreResources.ListDir("locales"))
			{
				if (!item.EndsWith(".json", StringComparison.Ordinal))
				{
					continue;
				}
				try
				{
					string json = CoreResources.ReadText("locales/" + item);
					if (string.IsNullOrEmpty(json))
					{
						continue;
					}
					using JsonDocument jsonDocument = JsonDocument.Parse(json);
					if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
					{
						continue;
					}
					Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
					foreach (JsonProperty item2 in jsonDocument.RootElement.EnumerateObject())
					{
						if (item2.Value.ValueKind == JsonValueKind.String)
						{
							dictionary[item2.Name] = item2.Value.GetString() ?? "";
						}
					}
					_tables[Path.GetFileNameWithoutExtension(item)] = dictionary;
				}
				catch (Exception ex) when (((ex is JsonException || ex is IOException) ? 1 : 0) != 0)
				{
				}
			}
		}
	}

	/// <summary>
	/// 是否含 CJK 字符。Windows 版这个助手在 <c>I18nCheck</c> 里;mac 版 I18nCheck 属开发期工具、
	/// 本包没有整体搬,所以只把这一条按原样搬进 <see cref="I18n"/>(被 <see cref="L(string)"/> 用到)。
	/// </summary>
	internal static bool HasCjk(string s)
	{
		foreach (char c in s)
		{
			if (c >= '\u4e00' && c <= '\u9fff')
			{
				return true;
			}
		}
		return false;
	}

	private static string ReadConfigLanguage()
	{
		try
		{
			string path = Path.Combine(BridgeHost.FindRoot(), "config.json");
			if (!File.Exists(path))
			{
				return "zh_CN";
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement value;
			return (jsonDocument.RootElement.TryGetProperty("language", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "zh_CN") : "zh_CN";
		}
		catch
		{
			return "zh_CN";
		}
	}

	private static string? ArgValue(string[]? args, string name)
	{
		if (args == null)
		{
			return null;
		}
		int num = Array.IndexOf<string>(args, name);
		if (num < 0 || num + 1 >= args.Length)
		{
			return null;
		}
		return args[num + 1];
	}
}
