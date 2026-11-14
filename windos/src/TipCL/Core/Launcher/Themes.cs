using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TipCL.Core.Launcher;

public static class Themes
{
	public const string THEMES_DIR = "themes";

	public const string THEME_EXT = ".json";

	private static readonly string[] _KEYS = new string[13]
	{
		"theme_color", "ui_dark", "ui_background", "ui_sidebar_opacity", "ui_background_blur", "ui_background_dim", "ui_background_folder", "ui_background_shuffle", "ui_background_interval", "ui_bg_preset",
		"window_mode", "custom_homepage", "homepage_mode"
	};

	private static string _themes_dir()
	{
		string text = PPath.Join(Utils.ROOT, "themes");
		Directory.CreateDirectory(PPath.Abs(text));
		return text;
	}

	private static string _theme_path(object name)
	{
		return PPath.Join(_themes_dir(), _sanitize(name) + ".json");
	}

	public static bool IsAlnum(string s, int i)
	{
		UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(s, i);
		if ((uint)unicodeCategory <= 4u || (uint)(unicodeCategory - 8) <= 2u)
		{
			return true;
		}
		return false;
	}

	public static string _sanitize(object name)
	{
		string text = Py.AsStr(name, "__iter__");
		StringBuilder stringBuilder = new StringBuilder();
		int num;
		for (num = 0; num < text.Length; num++)
		{
			int num2 = ((!char.IsHighSurrogate(text[num]) || num + 1 >= text.Length || !char.IsLowSurrogate(text[num + 1])) ? 1 : 2);
			char value = text[num];
			if (IsAlnum(text, num) || (num2 == 1 && " _-".IndexOf(value) >= 0))
			{
				stringBuilder.Append(text, num, num2);
			}
			else
			{
				stringBuilder.Append('_');
			}
			num += num2 - 1;
		}
		string text2 = Py.Strip(stringBuilder.ToString());
		if (text2.Length <= 0)
		{
			return "untitled";
		}
		return text2;
	}

	private static PyDict _current_theme()
	{
		LauncherConfig cONFIG = Config.CONFIG;
		return PyDict.Of("name", "当前主题", "theme_color", cONFIG.Get("theme_color", "#2E9B6B"), "ui_dark", Py.Truthy(cONFIG.Get("ui_dark", false)), "ui_background", cONFIG.Get("ui_background", ""), "ui_sidebar_opacity", cONFIG.Get("ui_sidebar_opacity", 100L), "ui_background_blur", cONFIG.Get("ui_background_blur", 0L), "ui_background_dim", cONFIG.Get("ui_background_dim", 0L), "ui_background_folder", cONFIG.Get("ui_background_folder", ""), "ui_background_shuffle", cONFIG.Get("ui_background_shuffle", false), "ui_background_interval", cONFIG.Get("ui_background_interval", 10L), "ui_bg_preset", cONFIG.Get("ui_bg_preset", "default"), "window_mode", cONFIG.Get("window_mode", "window"), "custom_homepage", cONFIG.Get("custom_homepage", ""), "homepage_mode", cONFIG.Get("homepage_mode", "news"));
	}

	public static List<object> ListThemes()
	{
		string text = _themes_dir();
		List<object> list = new List<object>();
		if (!LocalFs.IsDir(text))
		{
			return list;
		}
		foreach (string item in LocalFs.SortedChildren(text))
		{
			PPath pPath = new PPath(item);
			if (!(pPath.Suffix.ToLowerInvariant() != ".json") && Utils.ReadJson(item) is PyDict pyDict)
			{
				list.Add(PyDict.Of("name", pyDict.ContainsKey("name") ? pyDict["name"] : pPath.Stem, "file", pPath.Name, "theme_color", pyDict.ContainsKey("theme_color") ? pyDict["theme_color"] : "#2E9B6B", "ui_dark", Py.Truthy(pyDict.ContainsKey("ui_dark") ? pyDict["ui_dark"] : ((object)false))));
			}
		}
		return list;
	}

	public static PyDict SaveTheme(object name)
	{
		PyDict pyDict = _current_theme();
		pyDict["name"] = name;
		Utils.WriteJson(_theme_path(name), pyDict);
		return pyDict;
	}

	public static object LoadTheme(object name)
	{
		string text = _theme_path(name);
		if (!LocalFs.IsFile(text))
		{
			throw new FileNotFoundError("主题包不存在: " + Py.Str(name));
		}
		if (!(Utils.ReadJson(text, new PyDict()) is PyDict pyDict))
		{
			throw new ValueError("主题包数据损坏: " + Py.Str(name));
		}
		PyDict pyDict2 = new PyDict();
		string[] kEYS = _KEYS;
		foreach (string key in kEYS)
		{
			if (pyDict.ContainsKey(key))
			{
				pyDict2[key] = pyDict[key];
			}
		}
		LauncherConfig cONFIG = Config.CONFIG;
		string text2 = Py.S(cONFIG.Get("ui_background"));
		string text3 = Py.S(cONFIG.Get("ui_background_folder"));
		string text4 = Py.S(pyDict2.ContainsKey("ui_background") ? pyDict2["ui_background"] : text2);
		string text5 = Py.S(pyDict2.ContainsKey("ui_background_folder") ? pyDict2["ui_background_folder"] : text3);
		if (text4 != text2 || text5 != text3)
		{
			(List<object> images, List<object> folders) tuple = Config.PushBackgroundHistory(text2, text3);
			List<object> item = tuple.images;
			List<object> item2 = tuple.folders;
			pyDict2["ui_background_history"] = item;
			pyDict2["ui_background_folder_history"] = item2;
		}
		if (pyDict2.Count > 0)
		{
			cONFIG.Update(pyDict2);
			cONFIG.Save();
		}
		return pyDict;
	}

	public static void DeleteTheme(object name)
	{
		string text = _theme_path(name);
		if (LocalFs.IsFile(text))
		{
			File.Delete(PPath.Abs(text));
		}
	}

	public static void Copy2(string src, string dst)
	{
		string text = PPath.Abs(dst);
		if (Directory.Exists(text))
		{
			text = Path.Combine(text, new PPath(src).Name);
		}
		File.Copy(PPath.Abs(src), text, overwrite: true);
		try
		{
			File.SetLastWriteTimeUtc(text, File.GetLastWriteTimeUtc(PPath.Abs(src)));
		}
		catch (IOException)
		{
		}
	}

	public static string ExportTheme(object name, object dest)
	{
		string text = _theme_path(name);
		if (!LocalFs.IsFile(text))
		{
			throw new FileNotFoundError("主题包不存在: " + Py.Str(name));
		}
		string text2 = PPath.Norm(Py.AsStr(dest, "__fspath__"));
		Copy2(text, text2);
		return text2;
	}

	public static string ImportTheme(object path)
	{
		string text = PPath.Norm(Py.AsStr(path, "__fspath__"));
		if (!LocalFs.IsFile(text))
		{
			throw new FileNotFoundError("文件不存在: " + Py.Str(path));
		}
		if (!(Utils.ReadJson(text) is PyDict pyDict))
		{
			throw new ValueError("文件不是有效的主题包");
		}
		string text2 = Py.Str(pyDict.ContainsKey("name") ? pyDict["name"] : new PPath(text).Stem);
		string dst = _theme_path(text2);
		Copy2(text, dst);
		return text2;
	}
}
