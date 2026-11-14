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
		return Unpack(memoryStream.ToArray());
	}

	// 内嵌资源在打包期被 gzip 过(见 tools/pack-core-resources.ps1),这里按 gzip 魔数透明解压 ——
	// 所以上层(Payload.WriteLocales 释放词表、NodeRuntime 释放 plugin-host 脚本、I18nCore、
	// ModelCaps)拿到的依旧是**未压缩的原始字节**,与改造前逐字节相同,Python / Node 侧零改动。
	// 未压缩的资源(例如 mac/linux 端口尚未跟进的树)魔数不匹配 → 原样返回,行为不变。
	private static byte[] Unpack(byte[] data)
	{
		if (data.Length < 3 || data[0] != 0x1F || data[1] != 0x8B || data[2] != 0x08)
		{
			return data;
		}
		using GZipStream gzip = new GZipStream(new MemoryStream(data, writable: false), CompressionMode.Decompress);
		using MemoryStream output = new MemoryStream(data.Length * 3);
		gzip.CopyTo(output);
		return output.ToArray();
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
