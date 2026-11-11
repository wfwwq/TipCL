using System;

namespace TipCL.Core;

public sealed class RpcError : Exception
{
	public long Code { get; }

	public RpcError(long code, string message)
		: base(message)
	{
		Code = code;
	}
}
