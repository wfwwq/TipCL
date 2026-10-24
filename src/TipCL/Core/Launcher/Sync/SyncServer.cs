using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace TipCL.Core.Launcher.Sync;

/// <summary>
/// 局域网存档同步服务：手机端通过 UDP 广播找到电脑，输入配对码拿到令牌，再用 HTTP 列出、下载、上传世界。
/// 只接受局域网 / 本机来源的连接。
/// </summary>
public static class SyncServer
{
	public const int ProtocolVersion = 1;

	public const int DefaultPort = 25586;

	public const int DiscoveryPort = 25587;

	public const string DiscoverMessage = "TIPCL_SYNC_DISCOVER";

	private const string Prefix = "/api/sync/v1";

	private const long MaxUpload = 4L * 1024 * 1024 * 1024;

	private static readonly object _lock = new object();

	private static readonly object _ioLock = new object();

	private static TcpListener _tcp;

	private static UdpClient _udp;

	private static volatile bool _running;

	private static string _code = "";

	private static int _failures;

	public static int Port { get; private set; } = DefaultPort;

	public static bool Running => _running;

	public static string LastError { get; private set; } = "";

	/// <summary>当前配对码（6 位数字）；服务没开时为空。</summary>
	public static string PairCode => _running ? _code : "";

	private static string StatePath => Path.Combine(Utils.ResolveRoot(), "phone_sync.json");

	#region 配置与已配对设备

	private sealed class State
	{
		public bool enabled { get; set; }

		public int port { get; set; } = DefaultPort;

		public string server_id { get; set; } = "";

		public List<Device> devices { get; set; } = new List<Device>();
	}

	public sealed class Device
	{
		public string id { get; set; } = "";

		public string name { get; set; } = "";

		public string token_sha256 { get; set; } = "";

		public long paired_at { get; set; }

		public long last_seen { get; set; }
	}

	private static State Load()
	{
		try
		{
			if (File.Exists(StatePath))
			{
				State s = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath, Encoding.UTF8));
				if (s != null)
				{
					s.devices = s.devices ?? new List<Device>();
					if (string.IsNullOrEmpty(s.server_id))
					{
						s.server_id = Guid.NewGuid().ToString("N");
						Save(s);
					}
					return s;
				}
			}
		}
		catch (Exception)
		{
		}
		State n = new State { server_id = Guid.NewGuid().ToString("N") };
		Save(n);
		return n;
	}

	private static void Save(State s)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
			File.WriteAllText(StatePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
		}
		catch (Exception)
		{
		}
	}

	public static bool Enabled
	{
		get
		{
			lock (_lock)
			{
				return Load().enabled;
			}
		}
	}

	public static List<Device> Devices()
	{
		lock (_lock)
		{
			return Load().devices.ToList();
		}
	}

	public static void ForgetDevices()
	{
		lock (_lock)
		{
			State s = Load();
			s.devices.Clear();
			Save(s);
		}
	}

	public static string ServerName => Environment.MachineName;

	public static string ServerId
	{
		get
		{
			lock (_lock)
			{
				return Load().server_id;
			}
		}
	}

	#endregion

	#region 启停

	public static void StartIfEnabled()
	{
		if (Enabled)
		{
			Start();
		}
	}

	/// <summary>打开或关闭，并记住选择。返回是否在运行。</summary>
	public static bool SetEnabled(bool on)
	{
		lock (_lock)
		{
			State s = Load();
			s.enabled = on;
			Save(s);
		}
		if (on)
		{
			Start();
		}
		else
		{
			Stop();
		}
		return _running;
	}

	public static void Start(int? port = null)
	{
		lock (_lock)
		{
			if (_running)
			{
				return;
			}
			int p = port ?? Load().port;
			try
			{
				_tcp = new TcpListener(IPAddress.Any, p);
				_tcp.Start();
				Port = ((IPEndPoint)_tcp.LocalEndpoint).Port;
				_running = true;
				LastError = "";
				NewCode();
				new Thread(AcceptLoop) { IsBackground = true, Name = "TipCL-Sync" }.Start();
			}
			catch (Exception ex)
			{
				_running = false;
				LastError = ex.Message;
				try
				{
					_tcp?.Stop();
				}
				catch (Exception)
				{
				}
				_tcp = null;
				return;
			}
			try
			{
				_udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort)) { EnableBroadcast = true };
				new Thread(DiscoveryLoop) { IsBackground = true, Name = "TipCL-SyncDiscovery" }.Start();
			}
			catch (Exception)
			{
				// 发现端口被占用时仍可手动输入 IP 连接
				_udp = null;
			}
		}
	}

	public static void Stop()
	{
		lock (_lock)
		{
			_running = false;
			try
			{
				_tcp?.Stop();
			}
			catch (Exception)
			{
			}
			try
			{
				_udp?.Close();
			}
			catch (Exception)
			{
			}
			_tcp = null;
			_udp = null;
			_code = "";
		}
	}

	/// <summary>手机上要填的地址，例如 192.168.1.5:25586。</summary>
	public static List<string> Addresses()
	{
		return Lan.LocalIps().Select((object ip) => Py.Str(ip) + ":" + Port).ToList();
	}

	public static string NewCode()
	{
		byte[] b = new byte[4];
		using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
		{
			rng.GetBytes(b);
		}
		_code = ((BitConverter.ToUInt32(b, 0) % 900000) + 100000).ToString();
		_failures = 0;
		return _code;
	}

	#endregion

	#region 发现

	public static byte[] DiscoveryReply()
	{
		return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
		{
			{ "service", "tipcl-sync" },
			{ "protocol", ProtocolVersion },
			{ "server_id", ServerId },
			{ "server_name", ServerName },
			{ "port", Port }
		}));
	}

	private static void DiscoveryLoop()
	{
		UdpClient udp = _udp;
		while (_running && udp != null)
		{
			try
			{
				IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
				byte[] data = udp.Receive(ref from);
				if (!IsLan(from.Address))
				{
					continue;
				}
				if (Encoding.UTF8.GetString(data).Trim() == DiscoverMessage)
				{
					byte[] reply = DiscoveryReply();
					udp.Send(reply, reply.Length, from);
				}
			}
			catch (Exception)
			{
				if (!_running)
				{
					break;
				}
			}
		}
	}

	#endregion

	#region HTTP

	public static bool IsLan(IPAddress a)
	{
		if (a.IsIPv4MappedToIPv6)
		{
			a = a.MapToIPv4();
		}
		if (IPAddress.IsLoopback(a))
		{
			return true;
		}
		if (a.AddressFamily == AddressFamily.InterNetworkV6)
		{
			return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || (a.GetAddressBytes()[0] & 0xFE) == 0xFC;
		}
		byte[] b = a.GetAddressBytes();
		return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
	}

	private static void AcceptLoop()
	{
		TcpListener tcp = _tcp;
		while (_running && tcp != null)
		{
			TcpClient c;
			try
			{
				c = tcp.AcceptTcpClient();
			}
			catch (Exception)
			{
				break;
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				Handle(c);
			});
		}
	}

	private sealed class Request
	{
		public string Method = "";

		public string Path = "";

		public Dictionary<string, string> Query = new Dictionary<string, string>();

		public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public long ContentLength = -1;

		public Stream Body;

		/// <summary>已读取的请求体字节数。</summary>
		public long Consumed;

		public int Read(byte[] buf, int off, int count)
		{
			if (ContentLength >= 0)
			{
				count = (int)Math.Min(count, ContentLength - Consumed);
			}
			if (count <= 0)
			{
				return 0;
			}
			int n = Body.Read(buf, off, count);
			if (n > 0)
			{
				Consumed += n;
			}
			return n;
		}

		/// <summary>
		/// 回复前把没读完的请求体读掉（最多 64 MB）。否则客户端还在发送时关闭连接，
		/// Windows 会回 RST，对方就收不到错误响应。
		/// </summary>
		public void Drain()
		{
			if (ContentLength <= Consumed || ContentLength - Consumed > 64L * 1024 * 1024)
			{
				return;
			}
			byte[] buf = new byte[81920];
			while (Read(buf, 0, buf.Length) > 0)
			{
			}
		}

		public string Q(string k)
		{
			return Query.TryGetValue(k, out var v) ? v : "";
		}
	}

	private static void Handle(TcpClient c)
	{
		using (c)
		{
			try
			{
				IPEndPoint remote = (IPEndPoint)c.Client.RemoteEndPoint;
				NetworkStream ns = c.GetStream();
				c.ReceiveTimeout = 60000;
				c.SendTimeout = 60000;
				if (!IsLan(remote.Address))
				{
					WriteJson(ns, 403, Err("只接受局域网连接"));
					return;
				}
				Request req = ReadHead(ns);
				if (req == null)
				{
					return;
				}
				try
				{
					Route(req, ns);
				}
				finally
				{
					try
					{
						req.Drain();
					}
					catch (Exception)
					{
					}
				}
			}
			catch (Exception)
			{
			}
		}
	}

	private static Request ReadHead(Stream s)
	{
		StringBuilder sb = new StringBuilder();
		List<byte> line = new List<byte>();
		List<string> lines = new List<string>();
		int total = 0;
		while (true)
		{
			int b = s.ReadByte();
			if (b < 0)
			{
				return null;
			}
			if (++total > 32768)
			{
				return null;
			}
			if (b == '\n')
			{
				string l = Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
				line.Clear();
				if (l.Length == 0)
				{
					break;
				}
				lines.Add(l);
			}
			else
			{
				line.Add((byte)b);
			}
		}
		if (lines.Count == 0)
		{
			return null;
		}
		string[] first = lines[0].Split(' ');
		if (first.Length < 2)
		{
			return null;
		}
		Request r = new Request { Method = first[0].ToUpperInvariant() };
		string target = first[1];
		int q = target.IndexOf('?');
		r.Path = Uri.UnescapeDataString(q < 0 ? target : target.Substring(0, q));
		if (q >= 0)
		{
			foreach (string kv in target.Substring(q + 1).Split('&'))
			{
				if (kv.Length == 0)
				{
					continue;
				}
				int e = kv.IndexOf('=');
				string k = Uri.UnescapeDataString((e < 0 ? kv : kv.Substring(0, e)).Replace('+', ' '));
				string v = e < 0 ? "" : Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' '));
				r.Query[k] = v;
			}
		}
		for (int i = 1; i < lines.Count; i++)
		{
			int c = lines[i].IndexOf(':');
			if (c > 0)
			{
				r.Headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
			}
		}
		if (r.Headers.TryGetValue("Content-Length", out var cl) && long.TryParse(cl, out var n))
		{
			r.ContentLength = n;
		}
		r.Body = s;
		return r;
	}

	private static Dictionary<string, object> Err(string msg, string code = "error")
	{
		return new Dictionary<string, object>
		{
			{ "error", code },
			{ "message", msg }
		};
	}

	private static string Reason(int code)
	{
		switch (code)
		{
		case 200: return "OK";
		case 400: return "Bad Request";
		case 401: return "Unauthorized";
		case 403: return "Forbidden";
		case 404: return "Not Found";
		case 405: return "Method Not Allowed";
		case 409: return "Conflict";
		case 411: return "Length Required";
		case 413: return "Payload Too Large";
		case 423: return "Locked";
		case 429: return "Too Many Requests";
		default: return "Internal Server Error";
		}
	}

	private static void WriteHead(Stream s, int code, string ctype, long length, Dictionary<string, string> extra = null)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(Reason(code)).Append("\r\n");
		sb.Append("Content-Type: ").Append(ctype).Append("\r\n");
		sb.Append("Content-Length: ").Append(length).Append("\r\n");
		sb.Append("Connection: close\r\n");
		if (extra != null)
		{
			foreach (KeyValuePair<string, string> kv in extra)
			{
				sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
			}
		}
		sb.Append("\r\n");
		byte[] h = Encoding.ASCII.GetBytes(sb.ToString());
		s.Write(h, 0, h.Length);
	}

	private static void WriteJson(Stream s, int code, object doc)
	{
		byte[] body = Encoding.UTF8.GetBytes(doc is string str ? str : PyJsonOrSystem(doc));
		WriteHead(s, code, "application/json; charset=utf-8", body.Length);
		s.Write(body, 0, body.Length);
		s.Flush();
	}

	private static string PyJsonOrSystem(object doc)
	{
		if (doc is PyDict || doc is List<object>)
		{
			return PyJson.Dumps(doc);
		}
		return JsonSerializer.Serialize(doc);
	}

	private static byte[] ReadSmallBody(Request r)
	{
		if (r.ContentLength <= 0)
		{
			return new byte[0];
		}
		if (r.ContentLength > 65536)
		{
			throw new InvalidDataException("请求体过大");
		}
		byte[] buf = new byte[r.ContentLength];
		int off = 0;
		while (off < buf.Length)
		{
			int n = r.Read(buf, off, buf.Length - off);
			if (n <= 0)
			{
				break;
			}
			off += n;
		}
		return buf;
	}

	private static Device Auth(Request r)
	{
		if (!r.Headers.TryGetValue("Authorization", out var h) || !h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string sha = SaveSync.Sha256Hex(Encoding.UTF8.GetBytes(h.Substring(7).Trim()));
		lock (_lock)
		{
			State s = Load();
			Device d = s.devices.FirstOrDefault((Device x) => x.token_sha256 == sha);
			if (d != null)
			{
				d.last_seen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				Save(s);
			}
			return d;
		}
	}

	/// <summary>用配对码换令牌。成功返回令牌，失败返回 null；连续错 5 次换新码。</summary>
	public static string Pair(string code, string deviceId, string deviceName)
	{
		lock (_lock)
		{
			if (!_running || string.IsNullOrEmpty(_code) || code != _code)
			{
				if (++_failures >= 5)
				{
					NewCode();
				}
				return null;
			}
			byte[] b = new byte[32];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(b);
			}
			string token = Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			State s = Load();
			deviceId = string.IsNullOrWhiteSpace(deviceId) ? Guid.NewGuid().ToString("N") : deviceId.Trim();
			s.devices.RemoveAll((Device d) => d.id == deviceId);
			long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			s.devices.Add(new Device { id = deviceId, name = string.IsNullOrWhiteSpace(deviceName) ? "手机" : deviceName.Trim(), token_sha256 = SaveSync.Sha256Hex(Encoding.UTF8.GetBytes(token)), paired_at = now, last_seen = now });
			Save(s);
			NewCode();
			return token;
		}
	}

	private static void Route(Request r, Stream s)
	{
		string p = r.Path.TrimEnd('/');
		if (!p.StartsWith(Prefix, StringComparison.Ordinal))
		{
			WriteJson(s, 404, Err("未知接口", "not_found"));
			return;
		}
		p = p.Substring(Prefix.Length);
		if (p == "/info" && r.Method == "GET")
		{
			WriteJson(s, 200, new Dictionary<string, object>
			{
				{ "service", "tipcl-sync" },
				{ "protocol", ProtocolVersion },
				{ "server_id", ServerId },
				{ "server_name", ServerName },
				{ "app", TipCL.Core.Launcher.Ai.Defaults.CLIENT_HEADER }
			});
			return;
		}
		if (p == "/pair" && r.Method == "POST")
		{
			string code = "", id = "", name = "";
			try
			{
				using JsonDocument doc = JsonDocument.Parse(ReadSmallBody(r));
				JsonElement root = doc.RootElement;
				code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
				id = root.TryGetProperty("device_id", out var i) ? i.GetString() ?? "" : "";
				name = root.TryGetProperty("device_name", out var n) ? n.GetString() ?? "" : "";
			}
			catch (Exception)
			{
				WriteJson(s, 400, Err("请求体应为 JSON", "bad_request"));
				return;
			}
			string token = Pair(code.Trim(), id, name);
			if (token == null)
			{
				WriteJson(s, 403, Err("配对码不对或已过期，请看电脑上显示的最新配对码", "bad_code"));
				return;
			}
			WriteJson(s, 200, new Dictionary<string, object>
			{
				{ "token", token },
				{ "server_id", ServerId },
				{ "server_name", ServerName }
			});
			return;
		}
		Device dev = Auth(r);
		if (dev == null)
		{
			r.Drain();
			WriteJson(s, 401, Err("未配对或令牌失效，请重新配对", "unauthorized"));
			return;
		}
		if (p == "/pair" && r.Method == "DELETE")
		{
			lock (_lock)
			{
				State st = Load();
				st.devices.RemoveAll((Device d) => d.id == dev.id);
				Save(st);
			}
			WriteJson(s, 200, new Dictionary<string, object> { { "ok", true } });
			return;
		}
		if (p == "/targets" && r.Method == "GET")
		{
			string def = Py.Str(Py.Or(Config.CONFIG.Get("default_instance", ""), ""));
			List<object> list = SaveSync.Targets().Select((SaveSync.Target t) => (object)PyDict.Of("instance", t.Instance, "version", t.Version, "label", t.Label, "default", t.Version.Length == 0 && t.Instance == def, "worlds", (long)SaveSync.ListWorlds(t, withHash: false).Count)).ToList();
			WriteJson(s, 200, PyDict.Of("targets", list));
			return;
		}
		SaveSync.Target target = SaveSync.FindTarget(r.Q("instance"), r.Q("version"));
		if (target == null)
		{
			WriteJson(s, 404, Err("找不到这个实例或版本", "no_target"));
			return;
		}
		if (p == "/worlds" && r.Method == "GET")
		{
			WriteJson(s, 200, PyDict.Of("instance", target.Instance, "version", target.Version, "worlds", SaveSync.ListWorlds(target)));
			return;
		}
		if (!p.StartsWith("/worlds/", StringComparison.Ordinal))
		{
			WriteJson(s, 404, Err("未知接口", "not_found"));
			return;
		}
		string rest = p.Substring("/worlds/".Length);
		bool isZip = rest.EndsWith("/zip", StringComparison.Ordinal);
		string world = isZip ? rest.Substring(0, rest.Length - 4) : rest;
		if (!SaveSync.SafeWorldName(world))
		{
			WriteJson(s, 400, Err("非法存档名", "bad_name"));
			return;
		}
		string worldDir = Path.Combine(target.SavesDir, world);
		if (isZip && r.Method == "GET")
		{
			Download(s, worldDir);
			return;
		}
		if (!isZip && r.Method == "GET")
		{
			if (!File.Exists(Path.Combine(worldDir, "level.dat")))
			{
				WriteJson(s, 404, Err("存档不存在", "no_world"));
				return;
			}
			WriteJson(s, 200, SaveSync.WorldInfo(worldDir));
			return;
		}
		if (isZip && r.Method == "PUT")
		{
			Upload(r, s, target, world, worldDir);
			return;
		}
		WriteJson(s, 405, Err("不支持的方法", "method"));
	}

	private static void Download(Stream s, string worldDir)
	{
		if (!File.Exists(Path.Combine(worldDir, "level.dat")))
		{
			WriteJson(s, 404, Err("存档不存在", "no_world"));
			return;
		}
		if (SaveSync.IsLocked(worldDir))
		{
			WriteJson(s, 423, Err("电脑上正在玩这个存档，退出世界后再同步", "locked"));
			return;
		}
		string tmp = Path.Combine(Path.GetTempPath(), "tipcl-sync-" + Guid.NewGuid().ToString("N") + ".zip");
		try
		{
			string hash;
			lock (_ioLock)
			{
				hash = SaveSync.WorldHash(worldDir);
				using FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite);
				SaveSync.ExportZip(worldDir, fs);
			}
			FileInfo fi = new FileInfo(tmp);
			WriteHead(s, 200, "application/zip", fi.Length, new Dictionary<string, string>
			{
				{ "X-TipCL-World-Hash", hash },
				{ "Content-Disposition", "attachment; filename*=UTF-8''" + Uri.EscapeDataString(Path.GetFileName(worldDir)) + ".zip" }
			});
			using (FileStream fs = File.OpenRead(tmp))
			{
				fs.CopyTo(s);
			}
			s.Flush();
		}
		finally
		{
			try
			{
				File.Delete(tmp);
			}
			catch (Exception)
			{
			}
		}
	}

	private static void Upload(Request r, Stream s, SaveSync.Target target, string world, string worldDir)
	{
		if (r.ContentLength < 0)
		{
			WriteJson(s, 411, Err("需要 Content-Length", "length_required"));
			return;
		}
		if (r.ContentLength > MaxUpload)
		{
			WriteJson(s, 413, Err("存档太大", "too_large"));
			return;
		}
		bool force = r.Q("force") == "1" || r.Q("force") == "true";
		string baseHash = r.Q("base");
		string tmp = Path.Combine(Path.GetTempPath(), "tipcl-up-" + Guid.NewGuid().ToString("N") + ".zip");
		try
		{
			// 先完整收下请求体再回复，避免客户端还在发送时就被断开
			using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
			{
				byte[] buf = new byte[81920];
				while (r.Consumed < r.ContentLength)
				{
					int n = r.Read(buf, 0, buf.Length);
					if (n <= 0)
					{
						return;
					}
					fs.Write(buf, 0, n);
				}
			}
			string bad = SaveSync.CheckZip(tmp);
			if (bad != null)
			{
				WriteJson(s, 400, Err(bad, "bad_zip"));
				return;
			}
			lock (_ioLock)
			{
				bool exists = File.Exists(Path.Combine(worldDir, "level.dat"));
				if (exists && SaveSync.IsLocked(worldDir))
				{
					WriteJson(s, 423, Err("电脑上正在玩这个存档，退出世界后再同步", "locked"));
					return;
				}
				if (!force)
				{
					string current = exists ? SaveSync.WorldHash(worldDir) : "";
					if (current != baseHash)
					{
						PyDict conflict = PyDict.Of("error", "conflict", "message", exists ? "电脑上的存档在上次同步后也改过" : "电脑上没有这个存档了", "exists", exists);
						if (exists)
						{
							conflict["world"] = SaveSync.WorldInfo(worldDir);
						}
						WriteJson(s, 409, conflict);
						return;
					}
				}
				PyDict info = SaveSync.ImportZip(target, world, tmp);
				WriteJson(s, 200, info);
			}
		}
		catch (Exception ex)
		{
			try
			{
				WriteJson(s, 500, Err(ex.Message));
			}
			catch (Exception)
			{
			}
		}
		finally
		{
			try
			{
				if (File.Exists(tmp))
				{
					File.Delete(tmp);
				}
			}
			catch (Exception)
			{
			}
		}
	}

	#endregion
}
