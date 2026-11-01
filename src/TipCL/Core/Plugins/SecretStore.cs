using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace TipCL.Core.Plugins;

/// <summary>
/// 一个加密的 name → value 表，存成单个文件（plugin-secrets/&lt;id&gt;.bin）。
/// Windows 上用 DPAPI（CurrentUser）加密；非 Windows（只在单元测试里出现）退化为带标记的明文 + 简单混淆，
/// 不能当安全存储用。
/// </summary>
public sealed class SecretStore
{
	// 文件头：区分加密方式，方便以后迁移
	private static readonly byte[] MagicDpapi = Encoding.ASCII.GetBytes("TCS1");

	private static readonly byte[] MagicPlain = Encoding.ASCII.GetBytes("TCS0");

	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TipCL.PluginSecrets.v1");

	private readonly string _path;

	private readonly object _lock = new object();

	public SecretStore(string path)
	{
		_path = path;
	}

	public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

	public string Path => _path;

	private PyDict ReadAll()
	{
		if (!File.Exists(_path))
		{
			return new PyDict();
		}
		try
		{
			byte[] raw = File.ReadAllBytes(_path);
			if (raw.Length < 4)
			{
				return new PyDict();
			}
			byte[] head = raw.Take(4).ToArray();
			byte[] body = raw.Skip(4).ToArray();
			byte[] plain;
			if (head.SequenceEqual(MagicDpapi))
			{
				if (!IsWindows)
				{
					return new PyDict();
				}
				plain = DpapiUnprotect(body);
			}
			else
			{
				if (!head.SequenceEqual(MagicPlain))
				{
					return new PyDict();
				}
				plain = Xor(body);
			}
			return (PyJson.Loads(Encoding.UTF8.GetString(plain)) as PyDict) ?? new PyDict();
		}
		catch (Exception)
		{
			return new PyDict();
		}
	}

	private void WriteAll(PyDict d)
	{
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
		byte[] plain = Encoding.UTF8.GetBytes(PyJson.Dumps(d, null, ensureAscii: false));
		byte[] head;
		byte[] body;
		if (IsWindows)
		{
			head = MagicDpapi;
			body = DpapiProtect(plain);
		}
		else
		{
			head = MagicPlain;
			body = Xor(plain);
		}
		string tmp = _path + ".tmp";
		File.WriteAllBytes(tmp, head.Concat(body).ToArray());
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
		File.Move(tmp, _path);
	}

	public string Get(string name)
	{
		lock (_lock)
		{
			return ReadAll().Get(name) as string;
		}
	}

	/// <summary>value 为 null 或空串表示删除。</summary>
	public void Set(string name, string value)
	{
		lock (_lock)
		{
			PyDict d = ReadAll();
			if (string.IsNullOrEmpty(value))
			{
				d.Remove(name);
			}
			else
			{
				d[name] = value;
			}
			if (d.Count == 0)
			{
				try
				{
					File.Delete(_path);
				}
				catch (Exception)
				{
				}
				return;
			}
			WriteAll(d);
		}
	}

	public List<string> Names()
	{
		lock (_lock)
		{
			return ReadAll().Keys.ToList();
		}
	}

	public void DeleteAll()
	{
		lock (_lock)
		{
			try
			{
				File.Delete(_path);
			}
			catch (Exception)
			{
			}
		}
	}

	// DPAPI 单独放在不内联的方法里：非 Windows 上根本不会 JIT 到这里，也就不会去加载 System.Security
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static byte[] DpapiProtect(byte[] plain)
	{
		return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static byte[] DpapiUnprotect(byte[] data)
	{
		return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
	}

	private static byte[] Xor(byte[] data)
	{
		byte[] r = new byte[data.Length];
		for (int i = 0; i < data.Length; i++)
		{
			r[i] = (byte)(data[i] ^ Entropy[i % Entropy.Length] ^ 0x5A);
		}
		return r;
	}
}
