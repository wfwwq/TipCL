using System;
using System.Text;

namespace TipCL.Core.Launcher.Ai;

public static class Defaults
{
	private const int _GW_MASK = 90;

	private static readonly byte[] _GW_BYTES = new byte[19]
	{
		50, 46, 46, 42, 41, 96, 117, 117, 52, 63,
		45, 116, 105, 43, 116, 50, 59, 51, 40
	};

	private const int _TOK_MASK = 55;

	private static readonly byte[] _TOK_BYTES = new byte[51]
	{
		68, 92, 26, 64, 116, 0, 7, 110, 86, 3,
		125, 98, 98, 94, 100, 93, 71, 97, 123, 92,
		121, 113, 113, 83, 120, 6, 97, 85, 99, 65,
		91, 89, 123, 124, 85, 6, 88, 96, 94, 127,
		125, 112, 69, 97, 96, 6, 0, 2, 127, 85,
		66
	};

	public static readonly string DEFAULT_GATEWAY_URL = DefaultGatewayUrl();

	public static readonly string DEFAULT_API_BASE = DefaultApiBase();

	public static readonly string DEFAULT_API_KEY = DefaultApiKey();

	public const string DEFAULT_MODEL = "deepseek-v4-flash";

	public const string CLIENT_HEADER = "TipCL/1.0.6";

	public const int MAX_TOOL_ROUNDS_MAIN = 20;

	public const int MAX_TOOL_ROUNDS_SUBAGENT = 4;

	public const int MAX_TOOL_ROUNDS = 20;

	public const int MAX_HISTORY = 24;

	public const int MAX_TOOL_RESULT = 8000;

	public const int STREAM_CONNECT_TIMEOUT = 15;

	public const int STREAM_READ_TIMEOUT = 90;

	public const int ONCE_TIMEOUT = 90;

	private static string Unmask(byte[] data, int mask)
	{
		byte[] array = new byte[data.Length];
		for (int i = 0; i < data.Length; i++)
		{
			array[i] = (byte)((data[i] ^ mask) & 0xFF);
		}
		return Encoding.UTF8.GetString(array);
	}

	public static string DefaultGatewayUrl()
	{
		try
		{
			return Py.RStrip(Py.Strip(Unmask(_GW_BYTES, 90)), "/");
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static string DefaultApiBase()
	{
		string text = DefaultGatewayUrl();
		if (text.Length <= 0)
		{
			return "";
		}
		return text + "/v1";
	}

	public static string DefaultApiKey()
	{
		try
		{
			return Py.Strip(Unmask(_TOK_BYTES, 55));
		}
		catch (Exception)
		{
			return "";
		}
	}
}
