using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace TipCL.Core.Launcher;

public static class Lan
{
	public static List<object> LocalIps()
	{
		List<object> list = new List<object>();
		try
		{
			IPAddress[] hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
			foreach (IPAddress iPAddress in hostAddresses)
			{
				if (iPAddress.AddressFamily == AddressFamily.InterNetwork)
				{
					string text = iPAddress.ToString();
					if (text.Length > 0 && !text.StartsWith("127.", StringComparison.Ordinal) && !list.Contains(text))
					{
						list.Add(text);
					}
				}
			}
		}
		catch (SocketException)
		{
		}
		try
		{
			using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Connect(new IPEndPoint(IPAddress.Parse("223.5.5.5"), 80));
			string text2 = ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
			if (text2.Length > 0 && !list.Contains(text2) && !text2.StartsWith("127.", StringComparison.Ordinal))
			{
				list.Insert(0, text2);
			}
		}
		catch (SocketException)
		{
		}
		if (list.Count <= 0)
		{
			return new List<object> { "127.0.0.1" };
		}
		return list;
	}

	public static string LanHint(object port)
	{
		IEnumerable<string> values = from ip in LocalIps()
			select ip?.ToString() + ":" + Py.Str(port);
		return "房主在游戏里「对局域网开放」后，把下面地址发给好友：\n" + string.Join("\n", values);
	}
}
