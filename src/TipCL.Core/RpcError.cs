// 来源: Windows 版参考仓库 src/TipCL/Core/RpcError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
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
