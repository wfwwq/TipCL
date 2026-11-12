using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using TipCL.Core.Platform;

namespace TipCL.Core.Plugins;

/// <summary>
/// 一个加密的 name → value 表，存成单个文件（plugin-secrets/&lt;id&gt;.bin）。
/// Windows 上用 DPAPI（CurrentUser）加密；非 Windows（只在单元测试里出现）退化为带标记的明文 + 简单混淆，
/// 不能当安全存储用。
/// B1a:非 Windows 且 PlatformServices.SecretStore 可用时改用 AES-256-GCM(TCS2 文件头),
/// 主密钥存系统凭据库;没有可用凭据库时保持原来的 TCS0 明文 + 混淆。TCS0 / TCS1 仍然可读。
/// </summary>
public sealed class SecretStore
{
	// 文件头：区分加密方式，方便以后迁移
	private static readonly byte[] MagicDpapi = Encoding.ASCII.GetBytes("TCS1");

	private static readonly byte[] MagicPlain = Encoding.ASCII.GetBytes("TCS0");

	// B1a:AES-256-GCM(mac / 非 Windows 且有可用凭据库时)。
	// 文件布局 = "TCS2"(4) + nonce(12,每次写入随机) + tag(16) + 密文;魔数 4 字节同时作为 AAD。
	private static readonly byte[] MagicAes = Encoding.ASCII.GetBytes("TCS2");

	private const int AesNonceSize = 12;

	private const int AesTagSize = 16;

	private const int MasterKeySize = 32;

	private const string MasterKeyService = "TipCL plugin secrets";

	private const string MasterKeyAccount = "master-key";

	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TipCL.PluginSecrets.v1");

	private readonly string _path;

	private readonly ISecretStore _keyStore;

	private readonly bool _windows;

	private readonly object _lock = new object();

	public SecretStore(string path)
		: this(path, PlatformServices.SecretStore, IsWindows)
	{
	}

	// B1a 的注入入口:让「非 Windows 的 AES-GCM 分支」在 Windows 上也能被测到。
	// keyStore 为 null 表示没有可用凭据库;公开构造函数的行为不受影响(mac 上由 PlatformBootstrap 接线)。
	internal SecretStore(string path, ISecretStore keyStore, bool treatAsWindows)
	{
		_path = path;
		_keyStore = keyStore;
		_windows = treatAsWindows;
	}

	public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

	public string Path => _path;

	private PyDict ReadAll()
	{
		if (!File.Exists(_path))
		{
			return new PyDict();
		}
		byte[] raw;
		try
		{
			raw = File.ReadAllBytes(_path);
		}
		catch (Exception)
		{
			// 文件本身读不出来(被占用 / 权限不足):沿用「读不出来 → 空表」的原有语义
			return new PyDict();
		}
		if (raw.Length < 4)
		{
			return new PyDict();
		}
		byte[] head = raw.Take(4).ToArray();
		byte[] body = raw.Skip(4).ToArray();
		byte[] plain;
		if (head.SequenceEqual(MagicDpapi))
		{
			if (!_windows || !OperatingSystem.IsWindows())
			{
				return new PyDict();
			}
			try
			{
				plain = DpapiUnprotect(body);
			}
			catch (Exception)
			{
				return new PyDict();
			}
		}
		else if (head.SequenceEqual(MagicAes))
		{
			// B1b:「取主密钥」这一段的异常**必须原样抛给调用方**,不能吞成空表。
			// 吞掉的后果是真实的密钥丢失:Set(name, value) 先 ReadAll 再 WriteAll,
			// 凭据库被锁 / 用户拒绝授权导致读失败、紧接着写成功时,文件会被只有新条目的内容覆盖,
			// 该插件原有的其它密钥全部消失;Get 也会把「暂时读不到」说成「没存过」。
			if (!AesGcm.IsSupported)
			{
				// 支持不了就必须报错(与 AesGcmEncrypt 一致),不能当成空表
				throw new PlatformNotSupportedException("AES-GCM is not supported on this platform");
			}
			if (body.Length < AesNonceSize + AesTagSize)
			{
				// 截断密文:先判定再取密钥,免得为一个根本读不出来的文件去碰凭据库(失败时会抛出)
				return new PyDict();
			}
			byte[] key = ReadMasterKey();
			if (key == null)
			{
				// 凭据库里没有主密钥:这份文件确实解不开,按「读不出来」处理(读取路径不创建密钥)
				return new PyDict();
			}
			try
			{
				plain = AesGcmDecrypt(body, key);
			}
			catch (Exception)
			{
				// 认证失败(密文被改 / 换了主密钥 / 密钥不对):沿用「读不出来 → 空表」的原有语义
				return new PyDict();
			}
		}
		else
		{
			if (!head.SequenceEqual(MagicPlain))
			{
				return new PyDict();
			}
			plain = Xor(body);
		}
		try
		{
			return (PyJson.Loads(Encoding.UTF8.GetString(plain)) as PyDict) ?? new PyDict();
		}
		catch (Exception)
		{
			// 内容不是合法 UTF-8 / JSON:沿用「读不出来 → 空表」的原有语义
			return new PyDict();
		}
	}

	private void WriteAll(PyDict d)
	{
		byte[] plain = Encoding.UTF8.GetBytes(PyJson.Dumps(d, null, ensureAscii: false));
		byte[] head;
		byte[] body;
		if (_windows)
		{
			// 分析器要看到 OperatingSystem.IsWindows() 才认账(CA1416);_windows 是给单测用的注入标志。
			// 这个组合(说自己是 Windows 但实际不是)只有单测能造出来,行为与 DPAPI 在非 Windows 上一致:报不支持。
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("DPAPI is not supported on this platform");
			}
			head = MagicDpapi;
			body = DpapiProtect(plain);
		}
		else if (_keyStore != null)
		{
			// 先取主密钥(拿不到就抛出去),再落盘:绝不静默降级成 TCS0 明文
			head = MagicAes;
			body = AesGcmEncrypt(plain);
		}
		else
		{
			head = MagicPlain;
			body = Xor(plain);
		}
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
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
	[SupportedOSPlatform("windows")]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static byte[] DpapiProtect(byte[] plain)
	{
		return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
	}

	// ── B1a:AES-256-GCM 分支(非 Windows + 有可用凭据库) ────────────────

	/// <summary>
	/// 主密钥:32 字节随机数,Base64 后存进凭据库(<see cref="MasterKeyService"/> / <see cref="MasterKeyAccount"/>);
	/// 不存在时生成并写入,已存在就复用(不会每次写入都换密钥)。
	/// 凭据库不可用时异常直接抛出去 —— 不降级成 TCS0。
	/// </summary>
	private byte[] MasterKey()
	{
		byte[] key = ReadMasterKey();
		if (key != null)
		{
			return key;
		}
		byte[] created = RandomNumberGenerator.GetBytes(MasterKeySize);
		_keyStore.SetPassword(MasterKeyService, MasterKeyAccount, Convert.ToBase64String(created));
		return created;
	}

	/// <summary>
	/// 读取用的主密钥:凭据库里没有主密钥就返回 null 且**不创建**(读操作不产生写副作用);
	/// 凭据库调用失败、主密钥不是合法 Base64 / 长度不是 32 字节 → 异常原样抛给调用方(ReadAll 里不吞)。
	/// </summary>
	private byte[] ReadMasterKey()
	{
		if (_keyStore == null)
		{
			// 没有可用凭据库:连「有没有主密钥」都问不出来。这里报错而不是返回空表,是为了避免上层 Set
			// 在「读不出来」之后把同一个文件覆盖成 TCS0(写路径在没有凭据库时正是走 TCS0)。
			throw new PlatformNotSupportedException("no secret store is available for the plugin master key");
		}
		string stored = _keyStore.GetPassword(MasterKeyService, MasterKeyAccount);
		if (string.IsNullOrEmpty(stored))
		{
			return null;
		}
		return DecodeMasterKey(stored);
	}

	/// <summary>主密钥的合法性检查:坏掉就抛 <see cref="InvalidDataException"/>,绝不悄悄换一把新的(换钥会让旧文件永久读不出来)。</summary>
	private static byte[] DecodeMasterKey(string stored)
	{
		byte[] existing;
		try
		{
			existing = Convert.FromBase64String(stored);
		}
		catch (FormatException)
		{
			// 不覆盖已有条目:坏掉就坏掉,由调用方看到异常(悄悄换密钥会让旧文件永久读不出来)
			throw new InvalidDataException("plugin master key in the secret store is not valid base64");
		}
		if (existing.Length != MasterKeySize)
		{
			throw new InvalidDataException("plugin master key in the secret store is not " + MasterKeySize + " bytes");
		}
		return existing;
	}

	private byte[] AesGcmEncrypt(byte[] plain)
	{
		if (!AesGcm.IsSupported)
		{
			throw new PlatformNotSupportedException("AES-GCM is not supported on this platform");
		}
		byte[] key = MasterKey();
		byte[] nonce = RandomNumberGenerator.GetBytes(AesNonceSize);
		byte[] tag = new byte[AesTagSize];
		byte[] cipher = new byte[plain.Length];
		using AesGcm aes = new AesGcm(key, AesTagSize);
		aes.Encrypt(nonce, plain, cipher, tag, MagicAes);
		return nonce.Concat(tag).Concat(cipher).ToArray();
	}

	private byte[] AesGcmDecrypt(byte[] body, byte[] key)
	{
		if (!AesGcm.IsSupported)
		{
			throw new PlatformNotSupportedException("AES-GCM is not supported on this platform");
		}
		if (body.Length < AesNonceSize + AesTagSize)
		{
			throw new InvalidDataException("TCS2 payload is truncated");
		}
		byte[] nonce = body.Take(AesNonceSize).ToArray();
		byte[] tag = body.Skip(AesNonceSize).Take(AesTagSize).ToArray();
		byte[] cipher = body.Skip(AesNonceSize + AesTagSize).ToArray();
		byte[] plain = new byte[cipher.Length];
		using AesGcm aes = new AesGcm(key, AesTagSize);
		aes.Decrypt(nonce, cipher, tag, plain, MagicAes);
		return plain;
	}

	[SupportedOSPlatform("windows")]
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
