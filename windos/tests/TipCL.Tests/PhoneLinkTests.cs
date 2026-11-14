using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests;

/// <summary>用一个假的手机端（按手机启动器存档互传协议实现）跑一遍：握手 → 对比 → 推送 → 拉取 → 冲突判定。</summary>
public class PhoneLinkTests
{
	/// <summary>模拟手机端 HTTP 服务:世界放在 Dir/global 下。</summary>
	/// <remarks>
	/// 用裸 <see cref="TcpListener"/> 手写最小 HTTP/1.1 服务端,不用 <c>HttpListener</c>:
	/// 在受限宿主里(本仓库的机器验收环境),**派生进程**的 HTTP Server API 初始化会被拒
	/// ——实测 <c>HttpInitialize(HTTP_INITIALIZE_SERVER, 1.0/2.0)</c> 返回 5(ERROR_ACCESS_DENIED),
	/// 于是 <c>HttpListener.IsSupported == false</c>,<c>Start()</c> 在 <c>SetupV2Config()</c>
	/// 建 http.sys 会话时抛 <c>HttpListenerException ErrorCode=6</c>「句柄无效」。
	/// 这跟被测逻辑(握手/对比/推送/拉取/冲突)毫无关系,却会让整条用例在任何此类环境直接失败
	/// (dotnet test 的 testhost 也是派生进程)。裸 socket 不受这条限制,且换到 Linux CI 上行为一致。
	/// </remarks>
	private sealed class FakePhone : IDisposable
	{
		/// <summary>解析后的请求。</summary>
		private sealed class Req
		{
			public string Method = "";

			public string Path = "";

			public string Query = "";

			public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			public byte[] Body = Array.Empty<byte>();

			public string Header(string name)
			{
				return Headers.TryGetValue(name, out string v) ? v : "";
			}

			/// <summary>取查询串参数(缺省返回 null,与 HttpListener 的 QueryString 一致)。</summary>
			public string Q(string name)
			{
				foreach (string pair in Query.Split('&'))
				{
					int i = pair.IndexOf('=');
					if (i > 0 && Uri.UnescapeDataString(pair.Substring(0, i)) == name)
					{
						return Uri.UnescapeDataString(pair.Substring(i + 1).Replace("+", " "));
					}
				}
				return null;
			}
		}

		/// <summary>要回给客户端的东西。</summary>
		private sealed class Res
		{
			public int Code = 200;

			public string ContentType = "application/json";

			public byte[] Body = Array.Empty<byte>();
		}

		public readonly string Dir;

		public readonly string Token = "123456";

		public readonly int Port;

		private readonly TcpListener _tcp;

		private readonly Thread _thread;

		public string Saves => Path.Combine(Dir, "global");

		public FakePhone(string dir)
		{
			Dir = dir;
			Directory.CreateDirectory(Saves);
			_tcp = new TcpListener(IPAddress.Loopback, 0);
			_tcp.Start();
			Port = ((IPEndPoint)_tcp.LocalEndpoint).Port;
			_thread = new Thread(Loop) { IsBackground = true };
			_thread.Start();
		}

		private void Loop()
		{
			while (true)
			{
				TcpClient client;
				try
				{
					client = _tcp.AcceptTcpClient();
				}
				catch (Exception)
				{
					return; // Dispose 里 Stop() 让 Accept 抛异常 → 正常收工
				}
				try
				{
					using (client)
					using (NetworkStream s = client.GetStream())
					{
						Serve(s);
					}
				}
				catch (Exception)
				{
					// 单个连接出错不影响后续请求(与原来 HttpListener 版的宽容度一致)
				}
			}
		}

		private static string ReadHead(Stream s)
		{
			MemoryStream ms = new MemoryStream();
			byte[] last4 = new byte[4];
			while (true)
			{
				int b = s.ReadByte();
				if (b < 0)
				{
					break;
				}
				ms.WriteByte((byte)b);
				last4[0] = last4[1];
				last4[1] = last4[2];
				last4[2] = last4[3];
				last4[3] = (byte)b;
				if (ms.Length >= 4 && last4[0] == 13 && last4[1] == 10 && last4[2] == 13 && last4[3] == 10)
				{
					break; // CRLFCRLF = 头结束
				}
				if (ms.Length > 65536)
				{
					throw new InvalidDataException("请求头过大");
				}
			}
			return Encoding.UTF8.GetString(ms.ToArray());
		}

		private static Req ReadRequest(Stream s)
		{
			string head = ReadHead(s);
			if (head.Length == 0)
			{
				return null;
			}
			string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
			string[] first = lines[0].Split(' ');
			Req req = new Req
			{
				Method = (first.Length > 0) ? first[0] : "GET"
			};
			string target = (first.Length > 1) ? first[1] : "/";
			int q = target.IndexOf('?');
			req.Path = ((q < 0) ? target : target.Substring(0, q));
			req.Query = ((q < 0) ? "" : target.Substring(q + 1));
			for (int i = 1; i < lines.Length; i++)
			{
				int c = lines[i].IndexOf(':');
				if (c > 0)
				{
					req.Headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
				}
			}
			return req;
		}

		private static void ReadBody(Stream s, Req req)
		{
			if (!int.TryParse(req.Header("Content-Length"), out int len) || len <= 0)
			{
				return;
			}
			byte[] buf = new byte[len];
			int off = 0;
			while (off < len)
			{
				int n = s.Read(buf, off, len - off);
				if (n <= 0)
				{
					throw new EndOfStreamException("请求体不完整");
				}
				off += n;
			}
			req.Body = buf;
		}

		private void Serve(Stream s)
		{
			Req req = ReadRequest(s);
			if (req == null)
			{
				return;
			}
			// HttpWebRequest 上传时会先发 Expect: 100-continue 再等 100 才送 body(http.sys 以前代劳,这里自己回)
			if (req.Header("Expect").IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				WriteAscii(s, "HTTP/1.1 100 Continue\r\n\r\n");
			}
			ReadBody(s, req);
			Write(s, Handle(req));
		}

		private static void WriteAscii(Stream s, string text)
		{
			byte[] b = Encoding.ASCII.GetBytes(text);
			s.Write(b, 0, b.Length);
			s.Flush();
		}

		private static string Reason(int code)
		{
			switch (code)
			{
			case 200:
				return "OK";
			case 401:
				return "Unauthorized";
			case 404:
				return "Not Found";
			case 409:
				return "Conflict";
			case 500:
				return "Internal Server Error";
			default:
				return "Error";
			}
		}

		/// <summary>回 Content-Length + Connection: close:客户端(GetJson / Upload / Download)都按长度或读到 EOF 收尾,都不受影响。</summary>
		private static void Write(Stream s, Res res)
		{
			WriteAscii(s, "HTTP/1.1 " + res.Code + " " + Reason(res.Code) + "\r\n"
				+ "Content-Type: " + res.ContentType + "\r\n"
				+ "Content-Length: " + res.Body.Length + "\r\n"
				+ "Connection: close\r\n\r\n");
			s.Write(res.Body, 0, res.Body.Length);
			s.Flush();
		}

		private static Res Json(int code, object o)
		{
			return new Res { Code = code, Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)) };
		}

		private static long MaxMtime(string d)
		{
			return Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Select(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f)).ToUnixTimeMilliseconds()).DefaultIfEmpty(0).Max();
		}

		private Res Handle(Req r)
		{
			if (r.Header("X-PyMCL-Token") != Token)
			{
				return Json(401, new { error = "unauthorized" });
			}
			string name = r.Q("name") ?? "";
			string wd = Path.Combine(Saves, name);
			switch (r.Path)
			{
			case "/api/ping":
				return Json(200, new { app = "PyMCL", protocol = 1, device = "Test Phone", android = "14", port = Port, token = Token, deviceTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
			case "/api/worlds":
				return Json(200, new
				{
					root = new { id = "global", label = "全局存档", exists = true },
					roots = new[] { new { id = "global", label = "全局存档" }, new { id = "v:1.20.1", label = "版本隔离 1.20.1" } },
					worlds = Directory.GetDirectories(Saves).Where(d => File.Exists(Path.Combine(d, "level.dat"))).Select(d => new { dirName = Path.GetFileName(d), sizeBytes = 1L, fileCount = 1L, lastModified = MaxMtime(d) }).ToArray()
				});
			case "/api/worlds/download":
			{
				if (!Directory.Exists(wd))
				{
					return Json(404, new { error = "world not found" });
				}
				// 原来的实现是 SendChunked 边写边发;这里先在内存里成型,好给出 Content-Length(测试存档只有几 KB)
				using (MemoryStream zip = new MemoryStream())
				{
					SaveSync.ExportZip(wd, zip);
					return new Res { Code = 200, ContentType = "application/zip", Body = zip.ToArray() };
				}
			}
			case "/api/worlds/upload":
			{
				if (Directory.Exists(wd) && r.Q("replace") != "1")
				{
					return Json(409, new { ok = false, error = "conflict" });
				}
				if (Directory.Exists(wd))
				{
					Directory.Delete(wd, true);
				}
				using (MemoryStream ms = new MemoryStream(r.Body))
				using (ZipArchive z = new ZipArchive(ms))
				{
					foreach (ZipArchiveEntry e in z.Entries)
					{
						string rel = e.FullName.Substring(e.FullName.IndexOf('/') + 1);
						string dst = Path.Combine(wd, rel);
						Directory.CreateDirectory(Path.GetDirectoryName(dst));
						e.ExtractToFile(dst, true);
						File.SetLastWriteTime(dst, e.LastWriteTime.DateTime);
					}
				}
				return Json(200, new { ok = true, name, replaced = r.Q("replace") == "1" });
			}
			default:
				return Json(404, new { error = "not found" });
			}
		}

		public void Dispose()
		{
			try
			{
				_tcp.Stop();
			}
			catch (Exception)
			{
			}
		}
	}

	[Fact]
	public void NormalizeAddress()
	{
		Assert.Equal("http://192.168.1.5:27531", PhoneClient.NormalizeAddress("192.168.1.5"));
		Assert.Equal("http://192.168.1.5:27532", PhoneClient.NormalizeAddress("192.168.1.5:27532"));
		Assert.Equal("http://192.168.1.5:27531", PhoneClient.NormalizeAddress(" http://192.168.1.5/ "));
	}

	[Fact]
	public void ParseAdbDevices()
	{
		string o = "List of devices attached\nABC123\tdevice\nXYZ\tunauthorized\n10.0.0.2:5555\tdevice\n\n";
		Assert.Equal(new[] { "ABC123", "10.0.0.2:5555" }, PhoneClient.ParseAdbDevices(o));
	}

	[Fact]
	public void DecideWithoutBase()
	{
		PhoneClient.World w = new PhoneClient.World { DirName = "a", LastModified = 1_000_000 };
		Assert.Equal(SyncDecision.Same, PhoneLink.Decide("h", 1_001_500, w, null));
		Assert.Equal(SyncDecision.Conflict, PhoneLink.Decide("h", 1_100_000, w, null));
		Assert.Equal(SyncDecision.OnlyLocal, PhoneLink.Decide("h", 1, null, null));
		Assert.Equal(SyncDecision.OnlyRemote, PhoneLink.Decide(null, 0, w, null));
		PhoneLink.Base b = new PhoneLink.Base { local_hash = "h", remote_mtime = 1_000_000 };
		Assert.Equal(SyncDecision.Same, PhoneLink.Decide("h", 0, w, b));
		Assert.Equal(SyncDecision.Push, PhoneLink.Decide("h2", 0, w, b));
		Assert.Equal(SyncDecision.Pull, PhoneLink.Decide("h", 0, new PhoneClient.World { LastModified = 2 }, b));
		Assert.Equal(SyncDecision.Conflict, PhoneLink.Decide("h2", 0, new PhoneClient.World { LastModified = 2 }, b));
	}

	[Fact]
	public void FullFlow()
	{
		using TestHome home = new TestHome();
		using FakePhone phone = new FakePhone(Path.Combine(home.Dir, "phone"));
		try
		{
			string addr = "127.0.0.1:" + phone.Port;
			PhoneClient.ApiException bad = Assert.Throws<PhoneClient.ApiException>(() => PhoneLink.Connect(addr, "000000"));
			Assert.Equal(401, bad.Status);

			PhoneClient.Info info = PhoneLink.Connect(addr, phone.Token);
			Assert.Equal("Test Phone", info.Device);
			Assert.Equal(1, info.Protocol);
			Assert.Equal(phone.Token, PhoneLink.LastToken);
			Assert.Equal(2, PhoneLink.Roots().Count);

			SaveSync.Target t = SaveSync.FindTarget("", "");
			Assert.NotNull(t);
			Assert.Equal("global", PhoneLink.DefaultRoot(t, PhoneLink.Roots()));

			// 电脑上有一个世界，手机上有另一个
			string pcWorld = WorldFixture.Create(t.SavesDir, "电脑世界");
			WorldFixture.Create(phone.Saves, "手机世界", "手机上玩的", 1729779999000L);
			List<PhoneLink.Row> rows = PhoneLink.Compare(t, "global");
			Assert.Equal(SyncDecision.OnlyLocal, rows.Single(r => r.Name == "电脑世界").Decision);
			Assert.Equal(SyncDecision.OnlyRemote, rows.Single(r => r.Name == "手机世界").Decision);

			// 一键同步：两边互相补齐
			List<string> done = PhoneLink.SyncAll(t, "global", _ => SyncDecision.Same);
			Assert.Equal(2, done.Count);
			Assert.True(File.Exists(Path.Combine(phone.Saves, "电脑世界", "level.dat")));
			Assert.True(File.Exists(Path.Combine(t.SavesDir, "手机世界", "level.dat")));
			Assert.All(PhoneLink.Compare(t, "global"), r => Assert.Equal(SyncDecision.Same, r.Decision));

			// 电脑上改了 → Push；推过去后手机上的旧版本留在电脑的备份目录
			File.WriteAllText(Path.Combine(pcWorld, "region", "new.mca"), "changed on pc");
			Assert.Equal(SyncDecision.Push, PhoneLink.Compare(t, "global").Single(r => r.Name == "电脑世界").Decision);
			PhoneLink.Push(t, "global", "电脑世界");
			Assert.Equal("changed on pc", File.ReadAllText(Path.Combine(phone.Saves, "电脑世界", "region", "new.mca")));
			string bdir = Saves.BackupsDir(new Instance(t.Instance));
			Assert.Contains(Directory.GetFiles(bdir), f => Path.GetFileName(f).StartsWith("电脑世界-phone-"));
			Assert.Equal(SyncDecision.Same, PhoneLink.Compare(t, "global").Single(r => r.Name == "电脑世界").Decision);

			// 手机上改了 → Pull
			string pw = Path.Combine(phone.Saves, "手机世界", "region", "p.mca");
			File.WriteAllText(pw, "changed on phone");
			File.SetLastWriteTimeUtc(pw, DateTime.UtcNow.AddMinutes(5));
			Assert.Equal(SyncDecision.Pull, PhoneLink.Compare(t, "global").Single(r => r.Name == "手机世界").Decision);
			PhoneLink.Pull(t, "global", "手机世界");
			Assert.Equal("changed on phone", File.ReadAllText(Path.Combine(t.SavesDir, "手机世界", "region", "p.mca")));
			Assert.Equal(SyncDecision.Same, PhoneLink.Compare(t, "global").Single(r => r.Name == "手机世界").Decision);

			// 两边都改 → Conflict，一键同步时交给回调，跳过就什么都不动
			File.WriteAllText(Path.Combine(t.SavesDir, "手机世界", "region", "p.mca"), "pc edit");
			File.WriteAllText(pw, "phone edit 2");
			File.SetLastWriteTimeUtc(pw, DateTime.UtcNow.AddMinutes(10));
			Assert.Equal(SyncDecision.Conflict, PhoneLink.Compare(t, "global").Single(r => r.Name == "手机世界").Decision);
			Assert.Empty(PhoneLink.SyncAll(t, "global", _ => SyncDecision.Same));
			Assert.Equal("pc edit", File.ReadAllText(Path.Combine(t.SavesDir, "手机世界", "region", "p.mca")));
		}
		finally
		{
			PhoneLink.Disconnect();
		}
	}
}
