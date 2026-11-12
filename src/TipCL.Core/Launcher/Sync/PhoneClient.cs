using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Sync;

/// <summary>
/// 手机启动器存档互传协议（protocol 1）的电脑端客户端。手机开 HTTP 服务（默认端口 27531），
/// 电脑通过局域网或 adb forward 连过去；每个请求都要带手机上显示的 6 位配对码。
/// </summary>
public sealed class PhoneClient
{
	public const int DefaultPort = 27531;

	public const string TokenHeader = "X-PyMCL-Token";

	public sealed class Info
	{
		public string App = "";

		public int Protocol;

		public string Device = "";

		public string Android = "";

		public int Port;

		public long DeviceTimeMs;
	}

	public sealed class Root
	{
		public string Id = "";

		public string Label = "";

		public bool Exists = true;
	}

	public sealed class World
	{
		public string DirName = "";

		public long SizeBytes;

		public long FileCount;

		/// <summary>世界目录内所有文件修改时间的最大值，Unix 毫秒。</summary>
		public long LastModified;
	}

	public sealed class Worlds
	{
		public Root Root = new Root();

		public List<Root> Roots = new List<Root>();

		public List<World> List = new List<World>();
	}

	public sealed class ApiException : Exception
	{
		public int Status { get; }

		public ApiException(int status, string message)
			: base(message)
		{
			Status = status;
		}
	}

	public string BaseUrl { get; }

	public string Token { get; }

	public int Timeout { get; set; } = 8000;

	public PhoneClient(string address, string token)
	{
		BaseUrl = NormalizeAddress(address);
		Token = (token ?? "").Trim();
	}

	/// <summary>"192.168.1.5"、"192.168.1.5:27532"、"http://..." 都接受，没写端口就用 27531。</summary>
	public static string NormalizeAddress(string address)
	{
		string a = (address ?? "").Trim().TrimEnd('/');
		if (a.Length == 0)
		{
			throw new ArgumentException("没有填手机地址");
		}
		if (!a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			a = "http://" + a;
		}
		Uri u = new Uri(a);
		int port = u.IsDefaultPort && !a.Substring(a.IndexOf("//", StringComparison.Ordinal) + 2).Contains(":") ? DefaultPort : u.Port;
		return u.Scheme + "://" + u.Host + ":" + port;
	}

	public static string Enc(string s)
	{
		return Uri.EscapeDataString(s ?? "");
	}

	private HttpWebRequest Request(string method, string pathAndQuery, int timeout)
	{
		HttpWebRequest r = (HttpWebRequest)WebRequest.Create(BaseUrl + pathAndQuery);
		r.Method = method;
		r.Proxy = null;
		r.KeepAlive = false;
		r.Timeout = timeout;
		r.ReadWriteTimeout = Math.Max(timeout, 600000);
		r.Headers[TokenHeader] = Token;
		return r;
	}

	private static HttpWebResponse Send(HttpWebRequest r)
	{
		try
		{
			return (HttpWebResponse)r.GetResponse();
		}
		catch (WebException e) when (e.Response is HttpWebResponse resp)
		{
			int code = (int)resp.StatusCode;
			string body = "";
			try
			{
				using StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
				body = sr.ReadToEnd();
			}
			catch (Exception)
			{
			}
			resp.Dispose();
			throw new ApiException(code, Describe(code, body));
		}
		catch (WebException e)
		{
			throw new ApiException(0, "连不上手机：" + e.Message);
		}
	}

	private static string Describe(int code, string body)
	{
		string err = "";
		try
		{
			using JsonDocument d = JsonDocument.Parse(body);
			if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out JsonElement e))
			{
				err = e.GetString() ?? "";
			}
		}
		catch (Exception)
		{
		}
		return code switch
		{
			401 => "配对码不对，请核对手机上显示的 6 位数字",
			404 => "手机上没有这个世界",
			409 => "手机上已有同名世界",
			_ => "手机返回 " + code + (err.Length > 0 ? "：" + err : ""),
		};
	}

	private JsonElement GetJson(string pathAndQuery)
	{
		using HttpWebResponse resp = Send(Request("GET", pathAndQuery, Timeout));
		using StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
		using JsonDocument d = JsonDocument.Parse(sr.ReadToEnd());
		return d.RootElement.Clone();
	}

	private static string Str(JsonElement e, string name)
	{
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
	}

	private static long Num(JsonElement e, string name)
	{
		if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v))
		{
			if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n))
			{
				return n;
			}
			if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out n))
			{
				return n;
			}
		}
		return 0L;
	}

	private static Root ParseRoot(JsonElement e)
	{
		bool exists = !(e.ValueKind == JsonValueKind.Object && e.TryGetProperty("exists", out JsonElement x) && x.ValueKind == JsonValueKind.False);
		return new Root { Id = Str(e, "id"), Label = Str(e, "label"), Exists = exists };
	}

	public Info Ping()
	{
		JsonElement j = GetJson("/api/ping");
		return new Info
		{
			App = Str(j, "app"),
			Protocol = (int)Num(j, "protocol"),
			Device = Str(j, "device"),
			Android = Str(j, "android"),
			Port = (int)Num(j, "port"),
			DeviceTimeMs = Num(j, "deviceTimeMs")
		};
	}

	public Worlds ListWorlds(string rootId)
	{
		JsonElement j = GetJson("/api/worlds" + (string.IsNullOrEmpty(rootId) ? "" : "?root=" + Enc(rootId)));
		Worlds w = new Worlds();
		if (j.TryGetProperty("root", out JsonElement r))
		{
			w.Root = ParseRoot(r);
		}
		if (j.TryGetProperty("roots", out JsonElement rs) && rs.ValueKind == JsonValueKind.Array)
		{
			w.Roots = rs.EnumerateArray().Select(ParseRoot).Where((Root x) => x.Id.Length > 0).ToList();
		}
		if (j.TryGetProperty("worlds", out JsonElement ws) && ws.ValueKind == JsonValueKind.Array)
		{
			w.List = ws.EnumerateArray().Select((JsonElement x) => new World
			{
				DirName = Str(x, "dirName"),
				SizeBytes = Num(x, "sizeBytes"),
				FileCount = Num(x, "fileCount"),
				LastModified = Num(x, "lastModified")
			}).Where((World x) => x.DirName.Length > 0).ToList();
		}
		return w;
	}

	/// <summary>下载世界 zip 到 destZip。手机端不给长度，读到连接关闭为止。</summary>
	public void Download(string rootId, string name, string destZip, Action<long> progress = null)
	{
		HttpWebRequest r = Request("GET", "/api/worlds/download?root=" + Enc(rootId) + "&name=" + Enc(name), Timeout);
		using HttpWebResponse resp = Send(r);
		using Stream src = resp.GetResponseStream();
		using FileStream dst = new FileStream(destZip, FileMode.Create, FileAccess.Write);
		byte[] buf = new byte[81920];
		long total = 0L;
		int n;
		while ((n = src.Read(buf, 0, buf.Length)) > 0)
		{
			dst.Write(buf, 0, n);
			total += n;
			progress?.Invoke(total);
		}
	}

	/// <summary>上传世界 zip。replace=false 时手机上已有同名世界会返回 409。返回手机实际落盘的目录名。</summary>
	public string Upload(string rootId, string name, string zipPath, bool replace, Action<long, long> progress = null)
	{
		long len = new FileInfo(zipPath).Length;
		HttpWebRequest r = Request("POST", "/api/worlds/upload?root=" + Enc(rootId) + "&name=" + Enc(name) + "&replace=" + (replace ? "1" : "0"), Timeout);
		r.ContentType = "application/zip";
		r.ContentLength = len;
		r.SendChunked = false;
		r.AllowWriteStreamBuffering = false;
		try
		{
			using Stream dst = r.GetRequestStream();
			using FileStream src = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
			byte[] buf = new byte[81920];
			long done = 0L;
			int n;
			while ((n = src.Read(buf, 0, buf.Length)) > 0)
			{
				dst.Write(buf, 0, n);
				done += n;
				progress?.Invoke(done, len);
			}
		}
		catch (WebException e) when (e.Response == null)
		{
			throw new ApiException(0, "上传中断：" + e.Message);
		}
		using HttpWebResponse resp = Send(r);
		using StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
		string body = sr.ReadToEnd();
		try
		{
			using JsonDocument d = JsonDocument.Parse(body);
			string got = Str(d.RootElement, "name");
			return got.Length > 0 ? got : name;
		}
		catch (Exception)
		{
			return name;
		}
	}

	public void Delete(string rootId, string name)
	{
		HttpWebRequest r = Request("POST", "/api/worlds/delete?root=" + Enc(rootId) + "&name=" + Enc(name), Timeout);
		r.ContentLength = 0L;
		Send(r).Dispose();
	}

	#region 局域网扫描与 USB（adb）

	/// <summary>扫描本机所在 /24 网段里开着同步端口的地址（只做 TCP 连接，不需要配对码）。</summary>
	public static List<string> ScanLan(int port = DefaultPort, int timeoutMs = 400)
	{
		List<string> prefixes = new List<string>();
		foreach (object o in Lan.LocalIps())
		{
			string ip = Py.Str(o);
			if (IPAddress.TryParse(ip, out IPAddress a) && a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
			{
				string p = ip.Substring(0, ip.LastIndexOf('.') + 1);
				if (!prefixes.Contains(p))
				{
					prefixes.Add(p);
				}
			}
		}
		List<string> hosts = prefixes.SelectMany((string p) => Enumerable.Range(1, 254).Select((int i) => p + i)).ToList();
		List<string> found = new List<string>();
		Parallel.ForEach(hosts, new ParallelOptions { MaxDegreeOfParallelism = 64 }, delegate(string h)
		{
			if (PortOpen(h, port, timeoutMs))
			{
				lock (found)
				{
					found.Add(h + ":" + port);
				}
			}
		});
		return found.OrderBy((string x) => x, StringComparer.Ordinal).ToList();
	}

	public static bool PortOpen(string host, int port, int timeoutMs)
	{
		try
		{
			using TcpClient c = new TcpClient();
			Task t = c.ConnectAsync(host, port);
			return t.Wait(timeoutMs) && c.Connected;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>找 adb：设置里指定的 → 启动器同目录 → 数据目录 → Android SDK → PATH。</summary>
	/// <remarks>
	/// B2c2:adb 候选路径(纯函数,只拼字符串,不碰文件系统)。
	/// <list type="bullet">
	/// <item>Windows = <c>os == "windows"</c>:候选与顺序**与改动前逐项相同** —— 配置项 → 程序目录\adb.exe →
	/// 数据根\adb.exe → ANDROID_HOME / ANDROID_SDK_ROOT 下 platform-tools\adb.exe →
	/// %LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe → PATH 各目录下 adb.exe 再 adb
	/// (每个 PATH 目录先 adb.exe、后 adb,与改动前一样);</item>
	/// <item>非 Windows:配置项 → 程序目录/adb → 数据根/adb → ANDROID_HOME / ANDROID_SDK_ROOT 下
	/// platform-tools/adb → (仅 osx)用户主目录/Library/Android/sdk/platform-tools/adb →
	/// (仅 osx) /opt/homebrew/bin/adb、/usr/local/bin/adb(从访达启动的应用拿不到 shell 的 PATH,所以显式列出)
	/// → PATH 各目录下 adb。非 Windows **不探测任何 adb.exe,也不看 LocalApplicationData**。</item>
	/// </list>
	/// mac 的 ~/Library/Android/sdk 与两个 Homebrew 位置**未经真机验证**(任务书给出的默认位置)。
	/// </remarks>
	internal static List<string> AdbCandidates(string os, string configured, string baseDir, string dataRoot, Func<string, string> getEnv, string homeDir, string localAppData, string pathValue, char pathSeparator)
	{
		bool windows = os == "windows";
		string exe = windows ? "adb.exe" : "adb";
		List<string> c = new List<string>();
		if (!string.IsNullOrEmpty(configured))
		{
			c.Add(configured);
		}
		c.Add(Path.Combine(baseDir, exe));
		if (!string.IsNullOrEmpty(dataRoot))
		{
			c.Add(Path.Combine(dataRoot, exe));
		}
		string[] envNames = new string[2] { "ANDROID_HOME", "ANDROID_SDK_ROOT" };
		foreach (string envName in envNames)
		{
			string envValue = ((getEnv == null) ? null : getEnv(envName));
			if (!string.IsNullOrEmpty(envValue))
			{
				c.Add(Path.Combine(envValue, "platform-tools", exe));
			}
		}
		if (windows)
		{
			c.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", exe));
		}
		else if (os == "osx")
		{
			if (!string.IsNullOrEmpty(homeDir))
			{
				c.Add(Path.Combine(homeDir, "Library", "Android", "sdk", "platform-tools", exe));
			}
			c.Add("/opt/homebrew/bin/adb");
			c.Add("/usr/local/bin/adb");
		}
		foreach (string dir in (pathValue ?? "").Split(pathSeparator))
		{
			if (dir.Trim().Length > 0)
			{
				c.Add(Path.Combine(dir.Trim(), exe));
				if (windows)
				{
					c.Add(Path.Combine(dir.Trim(), "adb"));
				}
			}
		}
		return c;
	}

	/// <summary>B2c2:按候选顺序返回第一个「存在」的 adb(存在性判断由调用方注入,便于单测)。</summary>
	internal static string FindAdb(string os, string configured, string baseDir, string dataRoot, Func<string, string> getEnv, string homeDir, string localAppData, string pathValue, char pathSeparator, Func<string, bool> exists)
	{
		return AdbCandidates(os, configured, baseDir, dataRoot, getEnv, homeDir, localAppData, pathValue, pathSeparator).FirstOrDefault(exists ?? File.Exists);
	}

	/// <summary>
	/// B2c2:找不到 adb 时的文案(纯函数,可注入 os)。
	/// Windows 分支**原样返回调用点传进来的那句话** —— 改动前的字面量一字未动,只是从常量变成了实参;
	/// 非 Windows 分支不提 adb.exe 与 AdbWin*.dll(这两样在 mac / linux 上根本不存在)。
	/// </summary>
	internal static string AdbMissingMessage(string os, string windowsText)
	{
		if (os == "windows")
		{
			return windowsText;
		}
		return "没找到 adb\u3002可在设置里填写 adb 的完整路径\uFF0C或安装 Android platform-tools 后重试";
	}

	public static string FindAdb()
	{
		string conf = Py.Str(Py.Or(Config.CONFIG.Get("adb_path", ""), ""));
		string dataRoot = null;
		try
		{
			dataRoot = Utils.ResolveRoot();
		}
		catch (Exception)
		{
		}
		return FindAdb(Utils.OS_NAME, conf, AppDomain.CurrentDomain.BaseDirectory, dataRoot, (string name) => Environment.GetEnvironmentVariable(name), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetEnvironmentVariable("PATH") ?? "", Path.PathSeparator, (string path) => File.Exists(path));
	}

	public static string RunAdb(string adb, string args, int timeoutMs = 15000)
	{
		ProcessStartInfo psi = new ProcessStartInfo(adb, args)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8
		};
		using Process p = Process.Start(psi);
		Task<string> err = p.StandardError.ReadToEndAsync();
		string o = p.StandardOutput.ReadToEnd();
		p.WaitForExit(timeoutMs);
		return o + (err.Wait(1000) ? err.Result : "");
	}

	/// <summary>解析 `adb devices`，返回已授权的设备序列号。</summary>
	public static List<string> ParseAdbDevices(string output)
	{
		List<string> list = new List<string>();
		foreach (string line in (output ?? "").Split('\n'))
		{
			string[] parts = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 && parts[1] == "device")
			{
				list.Add(parts[0]);
			}
		}
		return list;
	}

	/// <summary>
	/// USB 连接：找到第一台已授权的设备，把手机的同步端口（27531 起，被占用时手机会 +1）转发到本机，
	/// 用配对码逐个试，成功就返回可用的本机地址（如 127.0.0.1:51234）。
	/// </summary>
	public static string ConnectUsb(string token, out Info info, Action<string> log = null)
	{
		string adb = FindAdb() ?? throw new InvalidOperationException(AdbMissingMessage(Utils.OS_NAME, "没找到 adb.exe。把 Android platform-tools 里的 adb.exe（连同两个 AdbWin*.dll）放到 TipCL 同目录即可"));
		List<string> devs = ParseAdbDevices(RunAdb(adb, "devices"));
		if (devs.Count == 0)
		{
			throw new InvalidOperationException("没有检测到手机。请打开手机的 USB 调试并插线，在手机上允许这台电脑调试");
		}
		string serial = devs[0];
		log?.Invoke("设备 " + serial);
		ApiException last = null;
		for (int p = DefaultPort; p < DefaultPort + 5; p++)
		{
			string outp = RunAdb(adb, "-s " + serial + " forward tcp:0 tcp:" + p).Trim();
			if (!int.TryParse(outp.Split('\n')[0].Trim(), out int local))
			{
				continue;
			}
			string addr = "127.0.0.1:" + local;
			try
			{
				info = new PhoneClient(addr, token) { Timeout = 4000 }.Ping();
				return addr;
			}
			catch (ApiException e)
			{
				last = e;
				RunAdb(adb, "-s " + serial + " forward --remove tcp:" + local);
				if (e.Status == 401)
				{
					throw;
				}
			}
		}
		throw last ?? new ApiException(0, "手机上的同步服务没有响应，请先在手机启动器里点「启动同步服务」");
	}

	#endregion
}
