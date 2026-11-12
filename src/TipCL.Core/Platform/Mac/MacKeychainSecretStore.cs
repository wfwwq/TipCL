using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace TipCL.Core.Platform;

/// <summary>
/// 一次外部命令的退出码与输出(供 <see cref="MacKeychainSecretStore"/> 的运行器返回)。
/// </summary>
public readonly struct SecretStoreCommandResult
{
	public SecretStoreCommandResult(int exitCode, string stdOut, string stdErr)
	{
		ExitCode = exitCode;
		StdOut = stdOut;
		StdErr = stdErr;
	}

	public int ExitCode { get; }

	public string StdOut { get; }

	public string StdErr { get; }
}

/// <summary>
/// <see cref="ISecretStore"/> 的 macOS 实现:走 <c>/usr/bin/security</c> 的 generic password,
/// 语义对齐 Windows 的 <c>WinCredSecretStore</c>(见 PROGRESS.md 的 B1a 对照表)。
///
/// 存储编码:<b>写入的值一律是</b> <c>b64:</c> + 密码 UTF-8 字节的 Base64。
/// 原因:<c>security ... -w</c> 遇到非 ASCII / 不可打印字符会改用十六进制输出,且返回值带换行,
/// 直接用 argv 传原始密码再读回来不保真。读取时去掉末尾换行;带 <c>b64:</c> 前缀就解码,
/// 否则按原样返回(兼容别的工具直接写进去的条目)。
///
/// 已知风险(见 DECISIONS.md D-31):写入时密码出现在 <c>security</c> 的命令行参数里,
/// 同机其他用户在那一瞬间可以用 <c>ps</c> 看到。这是 GOAL「走 /usr/bin/security」的直接后果。
///
/// 本机是 Windows,<b>没有经过真机 Keychain 验证</b>;命令行拼装与解析逻辑有单测。
/// </summary>
public sealed class MacKeychainSecretStore : ISecretStore
{
	/// <summary>运行一次外部命令,返回退出码与输出。<c>arguments</c> 是 argv 级参数(不需要自己加引号)。</summary>
	public delegate SecretStoreCommandResult Runner(string executablePath, IReadOnlyList<string> arguments);

	/// <summary>Keychain 命令行工具的绝对路径(macOS 自带,不去 PATH 里找)。</summary>
	public const string SecurityToolPath = "/usr/bin/security";

	/// <summary><c>security</c> 在条目不存在时的退出码(errSecItemNotFound = 44)。</summary>
	public const int ItemNotFoundExitCode = 44;

	private const string Prefix = "b64:";

	private const string Redacted = "<redacted>";

	private readonly Runner _run;

	public MacKeychainSecretStore()
		: this(DefaultRunner)
	{
	}

	public MacKeychainSecretStore(Runner runner)
	{
		_run = runner ?? throw new ArgumentNullException("runner");
	}

	// ── 命令行拼装(纯函数,单测覆盖) ───────────────────────────────

	/// <summary><c>find-generic-password -s &lt;service&gt; -a &lt;account&gt; -w</c></summary>
	public static IReadOnlyList<string> ReadArguments(string service, string account)
	{
		return new string[6] { "find-generic-password", "-s", service, "-a", account, "-w" };
	}

	/// <summary><c>add-generic-password -U -s &lt;service&gt; -a &lt;account&gt; -w &lt;value&gt;</c></summary>
	public static IReadOnlyList<string> WriteArguments(string service, string account, string value)
	{
		return new string[8] { "add-generic-password", "-U", "-s", service, "-a", account, "-w", EncodeValue(value) };
	}

	/// <summary><c>delete-generic-password -s &lt;service&gt; -a &lt;account&gt;</c></summary>
	public static IReadOnlyList<string> DeleteArguments(string service, string account)
	{
		return new string[5] { "delete-generic-password", "-s", service, "-a", account };
	}

	/// <summary>明文 → <c>b64:</c> + UTF-8 字节的 Base64。</summary>
	public static string EncodeValue(string value)
	{
		return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
	}

	/// <summary><c>security</c> 的 stdout → 明文:去掉末尾换行,带前缀就 Base64 解码,否则原样返回。</summary>
	public static string DecodeValue(string output)
	{
		string text = (output ?? "").TrimEnd('\r', '\n');
		if (!text.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return text;
		}
		try
		{
			return Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(Prefix.Length)));
		}
		catch (FormatException)
		{
			// 不把值本身带进消息(可能是密钥)
			throw new OSError("(0, 'security', 'credential payload is not valid base64')");
		}
	}

	// ── ISecretStore ──────────────────────────────────────────────

	/// <summary>条目不存在时返回 null(对齐 <c>WinCredSecretStore.GetPassword</c>)。</summary>
	public string GetPassword(string service, string username)
	{
		SecretStoreCommandResult result = Run(ReadArguments(service, username), "find-generic-password");
		if (result.ExitCode == ItemNotFoundExitCode)
		{
			return null;
		}
		if (result.ExitCode != 0)
		{
			throw Error("find-generic-password", result.ExitCode, result.StdErr);
		}
		return DecodeValue(result.StdOut);
	}

	/// <summary>写入(null 与 Windows 实现一样落成字面量 "None")。</summary>
	public void SetPassword(string service, string username, string password)
	{
		string value = password ?? "None";
		SecretStoreCommandResult result = Run(WriteArguments(service, username, value), "add-generic-password");
		if (result.ExitCode != 0)
		{
			// 失败时 stderr 里若出现密码,先打码再进异常消息
			throw Error("add-generic-password", result.ExitCode, Redact(result.StdErr, password, EncodeValue(password ?? "")));
		}
	}

	/// <summary>条目不存在时抛 <see cref="SecretNotFoundException"/>(对齐 <c>WinCredSecretStore.DeletePassword</c>)。</summary>
	public void DeletePassword(string service, string username)
	{
		SecretStoreCommandResult result = Run(DeleteArguments(service, username), "delete-generic-password");
		if (result.ExitCode == 0)
		{
			return;
		}
		if (result.ExitCode == ItemNotFoundExitCode)
		{
			throw new SecretNotFoundException(service);
		}
		throw Error("delete-generic-password", result.ExitCode, result.StdErr);
	}

	// ── 内部 ─────────────────────────────────────────────────────

	private SecretStoreCommandResult Run(IReadOnlyList<string> arguments, string operation)
	{
		try
		{
			return _run(SecurityToolPath, arguments);
		}
		catch (Exception e) when (!(e is OSError))
		{
			// 起不了进程(路径不存在/权限)也要变成一个明确的错误,不静默当成「没有这个条目」
			throw new OSError("(0, 'security " + operation + "', '" + e.GetType().Name + "')");
		}
	}

	private static OSError Error(string operation, int exitCode, string stderr)
	{
		string detail = string.IsNullOrWhiteSpace(stderr) ? "no stderr" : stderr.Trim();
		return new OSError("(" + exitCode + ", 'security " + operation + "', '" + detail + "')");
	}

	/// <summary>把可能出现在 stderr 里的密码值替换掉(不记录、不回报明文)。</summary>
	private static string Redact(string text, params string[] secrets)
	{
		string result = text ?? "";
		foreach (string secret in secrets)
		{
			if (!string.IsNullOrEmpty(secret))
			{
				result = result.Replace(secret, Redacted);
			}
		}
		return result;
	}

	/// <summary>
	/// 默认运行器:argv 级参数走 <see cref="ProcessStartInfo.ArgumentList"/>(不经过 shell,自己不加引号),
	/// 三个流重定向,stdout/stderr 并发读以避免管道写满死锁。
	/// </summary>
	private static SecretStoreCommandResult DefaultRunner(string executablePath, IReadOnlyList<string> arguments)
	{
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = executablePath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = false
		};
		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(startInfo);
		Task<string> outTask = process.StandardOutput.ReadToEndAsync();
		Task<string> errTask = process.StandardError.ReadToEndAsync();
		process.WaitForExit();
		return new SecretStoreCommandResult(process.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
	}
}
