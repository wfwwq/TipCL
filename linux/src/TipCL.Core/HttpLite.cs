// 来源: Windows 版参考仓库 src/TipCL/Core/BridgeServer.cs 的 4 个 HTTP 工具函数（M3-C0 提取；
// BridgeServer 本体是 RPC 桥，按 M2 决定不搬，SkinServer 只用到这几个静态工具）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TipCL.Core;

/// <summary>BridgeServer 的 HTTP 解析工具子集，供 SkinServer 使用。</summary>
internal static class HttpLite
{
	internal static string ReadLine(Stream s, out bool eof, out bool tooLong)
	{
		eof = false;
		tooLong = false;
		MemoryStream memoryStream = new MemoryStream();
		while (true)
		{
			int num = s.ReadByte();
			if (num < 0)
			{
				eof = memoryStream.Length == 0;
				break;
			}
			memoryStream.WriteByte((byte)num);
			if (num == 10)
			{
				break;
			}
			if (memoryStream.Length > 65536)
			{
				tooLong = true;
				return null;
			}
		}
		byte[] array = memoryStream.ToArray();
		int num2 = array.Length;
		while (num2 > 0 && (array[num2 - 1] == 10 || array[num2 - 1] == 13))
		{
			num2--;
		}
		return Latin1(array, 0, num2);
	}

	private static string Latin1(byte[] b, int off, int n)
	{
		char[] array = new char[n];
		for (int i = 0; i < n; i++)
		{
			array[i] = (char)b[off + i];
		}
		return new string(array);
	}

	internal static void SplitTarget(string target, out string path, out string query)
	{
		string text = target;
		int num = text.IndexOf('#');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		int num2 = text.IndexOf('?');
		query = ((num2 >= 0) ? text.Substring(num2 + 1) : "");
		text = ((num2 >= 0) ? text.Substring(0, num2) : text);
		if (text.StartsWith("//"))
		{
			int num3 = text.IndexOf('/', 2);
			text = ((num3 >= 0) ? text.Substring(num3) : "");
		}
		else
		{
			int num4 = text.IndexOf(':');
			int num5 = text.IndexOf('/');
			if (num4 > 0 && (num5 < 0 || num4 < num5) && IsScheme(text.Substring(0, num4)))
			{
				text = text.Substring(num4 + 1);
				if (text.StartsWith("//"))
				{
					int num6 = text.IndexOf('/', 2);
					text = ((num6 >= 0) ? text.Substring(num6) : "");
				}
			}
		}
		path = text;
	}

	private static bool IsScheme(string s)
	{
		if (s.Length == 0 || !char.IsLetter(s[0]))
		{
			return false;
		}
		foreach (char c in s)
		{
			if (!char.IsLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
			{
				return false;
			}
		}
		return true;
	}

	internal static string Phrase(int code)
	{
		return code switch
		{
			100 => "Continue",
			200 => "OK",
			204 => "No Content",
			400 => "Bad Request",
			401 => "Unauthorized",
			403 => "Forbidden",
			404 => "Not Found",
			413 => "Request Entity Too Large",
			414 => "Request-URI Too Long",
			431 => "Request Header Fields Too Large",
			501 => "Not Implemented",
			505 => "HTTP Version Not Supported",
			_ => "",
		};
	}

	internal static string Unquote(string s)
	{
		s = s.Replace('+', ' ');
		if (s.IndexOf('%') < 0)
		{
			return s;
		}
		List<byte> list = new List<byte>();
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] == '%' && i + 2 < s.Length && IsHex(s[i + 1]) && IsHex(s[i + 2]))
			{
				list.Add((byte)Convert.ToInt32(s.Substring(i + 1, 2), 16));
				i += 2;
			}
			else
			{
				list.AddRange(Encoding.UTF8.GetBytes(s[i].ToString()));
			}
		}
		return Encoding.UTF8.GetString(list.ToArray());
	}

	private static bool IsHex(char c)
	{
		if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
		{
			if (c >= 'A')
			{
				return c <= 'F';
			}
			return false;
		}
		return true;
	}
}
