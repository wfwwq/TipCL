using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace TipCL.Core.Launcher;

public static class News
{
	public static readonly string[] URLS = new string[2] { "https://launchercontent.mojang.com/v2/javaPatchNotes.json", "https://launchercontent.mojang.com/javaPatchNotes.json" };

	public static string CACHE => PPath.Join(Utils.ROOT, "cache", "news.json");

	public static List<object> _rows_from(object payload)
	{
		object obj = new List<object>();
		if (payload is PyDict pyDict)
		{
			obj = Py.Or(pyDict["entries"], pyDict["patchNotes"], new List<object>());
		}
		else if (payload is List<object>)
		{
			obj = payload;
		}
		List<object> list = new List<object>();
		if (obj is PyDict)
		{
			throw new PyException("KeyError", "slice(None, 12, None)");
		}
		if (obj is string)
		{
			return list;
		}
		if (!(obj is List<object> list2))
		{
			throw new TypeError("'" + Py.TypeName(obj) + "' object is not subscriptable");
		}
		for (int i = 0; i < list2.Count && i < 12; i++)
		{
			if (list2[i] is PyDict pyDict2)
			{
				object o = Py.Or(Py.Or(pyDict2["title"], pyDict2["version"]), pyDict2["id"], "");
				object obj2 = Py.Or(Py.Or(pyDict2["shortText"], pyDict2["body"]), pyDict2["subtitle"], "");
				if (obj2 is string { Length: >160 } text)
				{
					obj2 = Py.Slice(text, 0, 160) + "…";
				}
				object obj3 = "";
				object obj4 = Py.Or(pyDict2["image"], pyDict2["cardBackground"], new PyDict());
				if (obj4 is PyDict pyDict3)
				{
					obj3 = Py.Or(pyDict3["url"], "");
				}
				else if (obj4 is string text2)
				{
					obj3 = text2;
				}
				list.Add(PyDict.Of("title", Py.Str(o), "body", Py.Strip(Py.Str(obj2)), "version", Py.Str(Py.Or(pyDict2["version"], pyDict2["id"], "")), "image", obj3, "date", Py.Slice(Py.Str(Py.Or(pyDict2["date"], pyDict2["updated_at"], "")), 0, 10)));
			}
		}
		return list;
	}

	public static List<object> Fetch(DownloadManager dm = null)
	{
		dm = dm ?? new DownloadManager(2L);
		Exception ex = null;
		string[] uRLS = URLS;
		foreach (string url in uRLS)
		{
			try
			{
				List<object> list = _rows_from(dm.FetchJson(url, 15L));
				if (list.Count > 0)
				{
					Utils.WriteJson(CACHE, list);
					return list;
				}
			}
			catch (Exception ex2)
			{
				ex = ex2;
			}
		}
		List<object> list2 = LoadCached();
		if (list2.Count > 0)
		{
			return list2;
		}
		if (ex != null)
		{
			ExceptionDispatchInfo.Capture(ex).Throw();
		}
		return new List<object>();
	}

	public static List<object> LoadCached()
	{
		object obj = Utils.ReadJson(CACHE);
		if (obj is List<object> result)
		{
			return result;
		}
		if (obj is PyDict)
		{
			return _rows_from(obj);
		}
		return new List<object>();
	}
}
