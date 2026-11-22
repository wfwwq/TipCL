using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TipCL.Pages;

/// <summary>
/// 启动页「服务器状态」组件用的最小 Server List Ping(Java 版 1.7+ 协议:握手 → 状态请求 → JSON)。
/// 只读、不登录;超时 / 任何异常都返回 null(组件显示「离线」)。不做 SRV 解析。
/// </summary>
public sealed class ServerPingResult
{
	public int Online;

	public int Max;

	public long Ms;

	public List<string> Sample = new List<string>();
}

public static class ServerPing
{
	public static async Task<ServerPingResult?> QueryAsync(string host, int port, int timeoutMs = 2500)
	{
		if (string.IsNullOrWhiteSpace(host))
		{
			return null;
		}
		if (port <= 0 || port > 65535)
		{
			port = 25565;
		}
		try
		{
			using TcpClient tcp = new TcpClient();
			Stopwatch sw = Stopwatch.StartNew();
			Task connect = tcp.ConnectAsync(host, port);
			if (await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false) != connect || !tcp.Connected)
			{
				return null;
			}
			await connect.ConfigureAwait(false);
			long connectMs = sw.ElapsedMilliseconds;
			NetworkStream ns = tcp.GetStream();
			ns.ReadTimeout = timeoutMs;
			ns.WriteTimeout = timeoutMs;
			byte[] hostBytes = Encoding.UTF8.GetBytes(host);
			MemoryStream body = new MemoryStream();
			WriteVarInt(body, 0);
			WriteVarInt(body, 47);
			WriteVarInt(body, hostBytes.Length);
			body.Write(hostBytes, 0, hostBytes.Length);
			body.WriteByte((byte)(port >> 8));
			body.WriteByte((byte)(port & 0xFF));
			WriteVarInt(body, 1);
			MemoryStream packet = new MemoryStream();
			WriteVarInt(packet, (int)body.Length);
			body.Position = 0;
			body.CopyTo(packet);
			packet.WriteByte(1);
			packet.WriteByte(0);
			byte[] buf = packet.ToArray();
			Task<string?> io = Task.Run(delegate
			{
				ns.Write(buf, 0, buf.Length);
				ns.Flush();
				ReadVarInt(ns);
				int id = ReadVarInt(ns);
				if (id != 0)
				{
					return (string?)null;
				}
				int len = ReadVarInt(ns);
				if (len <= 0 || len > 1 << 21)
				{
					return null;
				}
				byte[] data = new byte[len];
				int got = 0;
				while (got < len)
				{
					int n = ns.Read(data, got, len - got);
					if (n <= 0)
					{
						return null;
					}
					got += n;
				}
				return Encoding.UTF8.GetString(data);
			});
			if (await Task.WhenAny(io, Task.Delay(timeoutMs)).ConfigureAwait(false) != io)
			{
				return null;
			}
			string? json = await io.ConfigureAwait(false);
			long total = sw.ElapsedMilliseconds;
			if (json == null)
			{
				return null;
			}
			ServerPingResult r = Parse(json);
			// 往返时间:状态请求那一段(去掉建连);建连本身也算一次往返,取两者较小的那个更接近游戏里的延迟
			r.Ms = Math.Max(1L, Math.Min(connectMs, total - connectMs));
			return r;
		}
		catch
		{
			return null;
		}
	}

	public static ServerPingResult Parse(string json)
	{
		ServerPingResult r = new ServerPingResult();
		using JsonDocument doc = JsonDocument.Parse(json);
		if (doc.RootElement.TryGetProperty("players", out var p) && p.ValueKind == JsonValueKind.Object)
		{
			if (p.TryGetProperty("online", out var on) && on.TryGetInt32(out var a))
			{
				r.Online = a;
			}
			if (p.TryGetProperty("max", out var mx) && mx.TryGetInt32(out var b))
			{
				r.Max = b;
			}
			if (p.TryGetProperty("sample", out var s) && s.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement e in s.EnumerateArray())
				{
					if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
					{
						string name = n.GetString() ?? "";
						if (name.Length > 0)
						{
							r.Sample.Add(name);
						}
					}
				}
			}
		}
		return r;
	}

	private static void WriteVarInt(Stream s, int value)
	{
		uint v = (uint)value;
		do
		{
			byte b = (byte)(v & 0x7F);
			v >>= 7;
			if (v != 0)
			{
				b |= 0x80;
			}
			s.WriteByte(b);
		}
		while (v != 0);
	}

	private static int ReadVarInt(Stream s)
	{
		int result = 0;
		for (int i = 0; i < 5; i++)
		{
			int b = s.ReadByte();
			if (b < 0)
			{
				throw new EndOfStreamException();
			}
			result |= (b & 0x7F) << (7 * i);
			if ((b & 0x80) == 0)
			{
				return result;
			}
		}
		throw new IOException("varint too long");
	}
}
