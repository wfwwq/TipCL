using System;

namespace TipCL.Services;

public sealed class BridgeCallException : Exception
{
	public string Method { get; }

	public BridgeCallException(string method, string message)
		: base(message)
	{
		Method = method;
	}
}
