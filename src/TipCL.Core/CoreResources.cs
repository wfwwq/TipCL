using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TipCL.Core.Launcher;

namespace TipCL.Core;

public static class CoreResources
{
	public const string Prefix = "TipCL.Core.";

	public const string Suffix = "";

	private static string ResName(string rel)
	{
		return "TipCL.Core." + rel.Replace('/', '.').Replace('\\', '.') + Suffix;
	}

	public static byte[] ReadBytes(string rel)
	{
		return ReadByResourceName(ResName(rel));
	}

	public static byte[] ReadByResourceName(string resName)
	{
		using Stream stream = typeof(CoreResources).Assembly.GetManifestResourceStream(resName);
		if (stream == null)
		{
			return null;
		}
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	public static string ReadText(string rel)
	{
		byte[] array = ReadBytes(rel);
		if (array != null)
		{
			return Utils.DecodeUtf8Strict(array);
		}
		return null;
	}

	public static List<string> ListDir(string relDir)
	{
		string p = "TipCL.Core." + relDir.Replace('/', '.') + ".";
		return (from n in typeof(CoreResources).Assembly.GetManifestResourceNames()
			where n.StartsWith(p, StringComparison.Ordinal) 
			select n.Substring(p.Length) into n
			where n.IndexOf('.') == n.LastIndexOf('.')
			select n).OrderBy<string, string>((string n) => n, StringComparer.Ordinal).ToList();
	}
}
