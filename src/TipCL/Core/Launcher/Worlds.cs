using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace TipCL.Core.Launcher;

public static class Worlds
{
	public static List<object> SearchWorlds(DownloadManager dm, object query, PyDict extra = null)
	{
		extra = new PyDict(extra ?? new PyDict());
		extra["kind"] = "world";
		return CatalogFiles.SearchProjects(dm, "world", query, Py.Or(extra["source"], "CurseForge"), extra);
	}

	public static string SavesRoot(Instance instance, object version_id = null)
	{
		if (Py.Truthy(version_id))
		{
			return PPath.Join(VersionSettings.GameDir(instance, version_id), "saves");
		}
		return PPath.Join(instance.path, "saves");
	}

	public static PyDict InstallWorld(DownloadManager dm, PyDict extra, Instance instance, Action<string, long, long> on_progress = null, object version_id = null)
	{
		instance.EnsureStandardDirs();
		string text = SavesRoot(instance, Py.Truthy(version_id) ? version_id : Py.S((extra ?? new PyDict())["version"]));
		Directory.CreateDirectory(PPath.Abs(text));
		extra = new PyDict(extra ?? new PyDict());
		object o = extra["path"];
		if (Py.Truthy(o) && LocalFs.IsFile(Py.Str(o)))
		{
			return _extract_world(PPath.Norm(Py.Str(o)), text);
		}
		object o2 = extra["url"];
		if (Py.Truthy(o2) && Py.Str(o2).StartsWith("http", StringComparison.Ordinal))
		{
			string text2 = Py.Str(o2).Split('/').Last()
				.Split('?')[0];
			string text3 = PPath.Join(Utils.ROOT, "cache", (text2.Length > 0) ? text2 : "world.zip");
			on_progress?.Invoke("下载世界", 0L, 1L);
			dm.Download(Py.Str(o2), text3, null, null, force: false, 900L);
			return _extract_world(text3, text);
		}
		object obj = extra["id"];
		if (!Py.Truthy(obj))
		{
			throw new WorldError("缺少 CurseForge 世界项目 id");
		}
		object file_id = Py.Or(extra["file_id"], extra["version_id"]);
		object obj2;
		if (Py.Truthy(file_id))
		{
			obj2 = Mods.CfFiles(dm, obj, null, null, null, 50L).FirstOrDefault((object f) => Py.Str(Mods.Gt(f, "id")) == Py.Str(file_id));
			if (obj2 == null)
			{
				throw new WorldError("找不到指定世界文件");
			}
		}
		else
		{
			object obj3 = Py.Or(extra["game_version"], extra["mc_version"]);
			List<object> list = Mods.CfFiles(dm, obj, null, Py.Truthy(obj3) ? obj3 : null, null, 50L);
			if (list.Count == 0)
			{
				throw new WorldError("该世界没有可下载文件");
			}
			obj2 = list[0];
		}
		string text4 = Py.Str(Py.Or(Mods.Gt(obj2, "fileName"), "world-" + Py.Str(obj) + ".zip"));
		string text5 = PPath.Join(Utils.ROOT, "cache", text4);
		List<object> list2 = new List<object>();
		if (Py.Truthy(Mods.Gt(obj2, "downloadUrl")))
		{
			list2.Add(Mods.Gt(obj2, "downloadUrl"));
		}
		list2.AddRange(Mods.CfModDownloadUrls(obj, Mods.Gt(obj2, "id"), text4));
		Exception ex = null;
		foreach (object item in list2)
		{
			try
			{
				on_progress?.Invoke("下载世界 " + text4, 0L, 1L);
				dm.Download(item, text5, null, null, force: false, 900L);
				ex = null;
			}
			catch (Exception ex2)
			{
				ex = ex2;
				continue;
			}
			break;
		}
		if (ex != null)
		{
			throw new WorldError("下载世界失败: " + Py.ExcStr(ex));
		}
		return _extract_world(text5, text);
	}

	private static PyDict _extract_world(string archive, string dest_root)
	{
		PPath pPath = new PPath(archive);
		if (Py.Lower(pPath.Suffix) != ".zip")
		{
			string text = PPath.Join(dest_root, pPath.Stem);
			Utils.EnsureDir(text);
			LocalFs.Copy2(archive, PPath.Join(text, pPath.Name));
			return PyDict.Of("files", Py.L(pPath.Stem), "source", "file");
		}
		ZipArchive zipArchive;
		try
		{
			zipArchive = Utils.OpenZip(archive);
		}
		catch (InvalidDataException ex)
		{
			throw new WorldError("世界压缩包损坏: " + ((ex != null) ? "File is not a zip file" : Py.ExcStr(ex)));
		}
		using (zipArchive)
		{
			List<string> list = (from n in zipArchive.Entries.Select(Utils.ZipEntryName)
				where n.Length > 0 && !n.EndsWith("/", StringComparison.Ordinal)
				select n.Replace("\\", "/")).ToList();
			if (list.Count == 0)
			{
				throw new WorldError("空的世界压缩包");
			}
			List<List<string>> list2 = list.Select((string n) => (from p in n.Split('/')
				where p != "" && p != "."
				select p).ToList()).ToList();
			string text2 = (list2.Any((List<string> parts) => parts.Count == 1 && parts[0] == "level.dat") ? pPath.Stem : "");
			HashSet<string> hashSet = new HashSet<string>();
			if (text2.Length > 0)
			{
				hashSet.Add(text2);
			}
			else
			{
				foreach (List<string> item in list2)
				{
					if (item.Count > 0)
					{
						hashSet.Add(item[0]);
					}
				}
			}
			_extract_members(zipArchive, dest_root, text2);
			List<string> list3 = hashSet.ToList();
			list3.Sort(string.CompareOrdinal);
			return PyDict.Of("files", list3.Cast<object>().ToList(), "source", "zip");
		}
	}

	private static void _extract_members(ZipArchive zf, string dest_root, string prefix)
	{
		PPath pPath = new PPath(dest_root).Resolve();
		List<KeyValuePair<ZipArchiveEntry, PPath>> list = new List<KeyValuePair<ZipArchiveEntry, PPath>>();
		foreach (ZipArchiveEntry entry in zf.Entries)
		{
			string text = Utils.ZipEntryName(entry);
			string text2 = text.Replace("\\", "/");
			if (text2.Length != 0 && !text2.EndsWith("/", StringComparison.Ordinal))
			{
				List<string> list2 = (from p in text2.Split('/')
					where p != "" && p != "."
					select p).ToList();
				if (list2.Count == 0 || list2.Any((string p) => p == "..") || new PPath(text2).IsAbsolute || list2[0].Contains(":"))
				{
					throw new WorldError("世界压缩包含非法路径: " + text);
				}
				List<object> list3 = new List<object> { pPath.ToString() };
				if (prefix.Length > 0)
				{
					list3.Add(prefix);
				}
				list3.AddRange(list2);
				PPath pPath2 = new PPath(list3.ToArray()).Resolve();
				string text3 = pPath2.ToString();
				string text4 = pPath.ToString();
				bool flag = text3.Length > text4.Length && text3.StartsWith(text4, StringComparison.Ordinal) && (text4.EndsWith("/") || text4.EndsWith("\\") || text3[text4.Length] == '/' || text3[text4.Length] == '\\');
				if (text3 == text4 || !flag)
				{
					throw new WorldError("世界压缩包含非法路径: " + text);
				}
				list.Add(new KeyValuePair<ZipArchiveEntry, PPath>(entry, pPath2));
			}
		}
		foreach (KeyValuePair<ZipArchiveEntry, PPath> item in list)
		{
			Directory.CreateDirectory(PPath.Abs(item.Value.Parent.ToString()));
			using Stream stream = item.Key.Open();
			using FileStream destination = new FileStream(PPath.Abs(item.Value.ToString()), FileMode.Create, FileAccess.Write);
			stream.CopyTo(destination);
		}
	}
}
