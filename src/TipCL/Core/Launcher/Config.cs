using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher;

public static class Config
{
	public const string ISOLATION_DEFAULTS_VERSION = "2026.09-isolate-all";

	internal const string _LEGACY_DEFAULT_ISOLATION = "none";

	public const string AI_GATEWAY_DEFAULTS_VERSION = "2026.10-builtin-newapi";

	private const int _CF_MASK = 77;

	private static readonly byte[] _CF_BYTES = new byte[60]
	{
		105, 127, 44, 105, 124, 125, 105, 34, 117, 61,
		52, 42, 29, 63, 37, 59, 6, 15, 5, 56,
		56, 37, 120, 36, 32, 1, 127, 26, 99, 1,
		14, 3, 11, 37, 15, 124, 120, 55, 15, 20,
		12, 8, 53, 21, 53, 98, 25, 60, 25, 53,
		98, 23, 61, 120, 61, 53, 127, 33, 53, 56
	};

	public static readonly PyDict DEFAULT_CONFIG = BuildDefaults();

	private static LauncherConfig _config;

	private static readonly object _configGuard = new object();

	public const int BG_HISTORY_MAX = 20;

	public static string CONFIG_FILE => PPath.Join(Utils.ROOT, "config.json");

	public static LauncherConfig CONFIG
	{
		get
		{
			LauncherConfig config = _config;
			if (config != null)
			{
				return config;
			}
			lock (_configGuard)
			{
				return _config ?? (_config = new LauncherConfig());
			}
		}
	}

	public static string BuiltinCurseforgeKey()
	{
		char[] array = new char[_CF_BYTES.Length];
		for (int i = 0; i < array.Length; i++)
		{
			int num = (_CF_BYTES[i] ^ 0x4D) & 0xFF;
			array[i] = ((num < 128) ? ((char)num) : '\ufffd');
		}
		return new string(array);
	}

	public static string CurseforgeKey()
	{
		string text = Py.Strip(Py.S(CONFIG.Get("curseforge_api_key")));
		if (text.Length <= 0)
		{
			return BuiltinCurseforgeKey();
		}
		return text;
	}

	private static PyDict BuildDefaults()
	{
		PyDict pyDict = new PyDict();
		pyDict["instances_dir"] = ".minecraft";
		pyDict["default_instance"] = "";
		pyDict["java_dir"] = "java";
		pyDict["shared_libraries"] = false;
		pyDict["shared_assets"] = false;
		pyDict["memory_mb"] = 4096L;
		pyDict["download_threads"] = 8L;
		pyDict["download_segments"] = 4L;
		pyDict["width"] = 854L;
		pyDict["height"] = 480L;
		pyDict["microsoft_client_id"] = "00000000402b5328";
		pyDict["curseforge_api_key"] = "";
		pyDict["github_proxy_prefixes"] = Py.L("https://gitproxy.mrhjx.cn/", "https://ghproxy.vip/", "https://gh-proxy.com/", "https://v6.gh-proxy.org/", "https://cdn.gh-proxy.com/");
		pyDict["force_manifest_refresh"] = false;
		pyDict["download_source"] = "auto";
		pyDict["community_source"] = "auto";
		pyDict["use_system_proxy"] = true;
		pyDict["ai_mode"] = "custom";
		pyDict["ai_gateway_url"] = "";
		pyDict["ai_base_url"] = "";
		pyDict["ai_api_key"] = "";
		pyDict["ai_model"] = "deepseek-v4-flash";
		pyDict["ai_confirm_writes"] = true;
		pyDict["ai_permission_mode"] = "default";
		pyDict["ai_permission_rules"] = new List<object>();
		pyDict["ai_permission_dont_ask"] = false;
		pyDict["ai_context_window"] = 200000L;
		pyDict["ai_max_tokens"] = 8192L;
		pyDict["ai_model_caps_overrides"] = new PyDict();
		pyDict["terracotta_extra_nodes"] = Py.L("https://terracotta.glavo.site/acebc7d8-1208-47fd-b212-d03ac49e36e0");
		pyDict["feedback_url"] = "";
		pyDict["feedback_heartbeat"] = true;
		pyDict["feedback_consent"] = null;
		pyDict["device_id"] = "";
		pyDict["default_isolation"] = "all";
		pyDict["isolation_defaults"] = "";
		pyDict["ai_gateway_defaults"] = "";
		pyDict["default_jvm_args"] = "";
		pyDict["default_priority"] = "normal";
		pyDict["update_url"] = "https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json";
		pyDict["theme_color"] = "#2E9B6B";
		pyDict["ui_dark"] = false;
		pyDict["ui_background"] = "";
		pyDict["ui_background_folder"] = "";
		pyDict["ui_background_shuffle"] = false;
		pyDict["ui_background_interval"] = 10L;
		pyDict["ui_background_history"] = new List<object>();
		pyDict["ui_background_folder_history"] = new List<object>();
		pyDict["ui_sidebar_opacity"] = 85L;
		pyDict["ui_background_blur"] = 0L;
		pyDict["ui_background_dim"] = 25L;
		pyDict["ui_fly_animation"] = true;
		pyDict["ui_fly_duration_ms"] = 620L;
		pyDict["ui_motion"] = true;
		pyDict["ui_layout"] = null;
		pyDict["ui_layouts"] = new PyDict();
		pyDict["ui_layout_profile"] = "";
		pyDict["ui_launch_dock_corner"] = "auto";
		pyDict["ui_window_aspect"] = "4:3";
		pyDict["ui_nav_order"] = new List<object>();
		pyDict["ui_nav_hidden"] = new List<object>();
		pyDict["ui_nav_pinned"] = new List<object>();
		pyDict["ui_nav_groups"] = null;
		pyDict["ui_nav_defaults"] = "";
		pyDict["ui_nav_style"] = "grouped";
		pyDict["ui_section_members"] = new PyDict();
		pyDict["ui_sidebar_width"] = 0L;
		pyDict["default_java"] = "";
		pyDict["global_mods_dir"] = "";
		pyDict["launcher_visibility"] = "keep";
		pyDict["gc_preset"] = "auto";
		pyDict["download_limit_kbps"] = 0L;
		pyDict["auto_check_update"] = true;
		pyDict["custom_homepage"] = "";
		pyDict["homepage_mode"] = "news";
		pyDict["window_mode"] = "window";
		pyDict["skip_assets"] = false;
		pyDict["first_run"] = true;
		pyDict["show_hidden_versions"] = false;
		pyDict["catalog_favorites"] = new List<object>();
		pyDict["offline_skin"] = "default";
		pyDict["allow_multi_instance"] = false;
		pyDict["export_dir"] = "";
		pyDict["language"] = "zh_CN";
		return pyDict;
	}

	public static void Reset()
	{
		lock (_configGuard)
		{
			_config = null;
		}
	}

	public static List<object> HistoryPaths(object raw)
	{
		List<object> list = new List<object>();
		if (!(raw is List<object> list2))
		{
			return list;
		}
		foreach (object item2 in list2)
		{
			if (item2 is string item)
			{
				list.Add(item);
			}
		}
		return list;
	}

	public static (List<object> images, List<object> folders) BackgroundHistory()
	{
		List<object> list = HistoryPaths(CONFIG.Get("ui_background_history"));
		List<object> list2 = HistoryPaths(CONFIG.Get("ui_background_folder_history"));
		if (list2.Count != list.Count)
		{
			List<object> list3 = new List<object>(list2);
			for (int i = 0; i < list.Count; i++)
			{
				list3.Add("");
			}
			list2 = list3.Take(list.Count).ToList();
		}
		return (images: list, folders: list2);
	}

	public static (List<object> images, List<object> folders) PushBackgroundHistory(object old_image, object old_folder)
	{
		(List<object> images, List<object> folders) tuple = BackgroundHistory();
		List<object> item = tuple.images;
		List<object> item2 = tuple.folders;
		string text = Py.S(old_image);
		string text2 = Py.S(old_folder);
		if (item.Count > 0 && Py.Eq(item[item.Count - 1], text) && Py.Eq(item2[item2.Count - 1], text2))
		{
			return (images: item, folders: item2);
		}
		item.Add(text);
		item2.Add(text2);
		return (images: Py.Slice(item, -20), folders: Py.Slice(item2, -20));
	}
}
