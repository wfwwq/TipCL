// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/NetError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
using System;

namespace TipCL.Core.Launcher;

public class NetError : PyException
{
	public NetError(string pyType, string m, Exception inner = null)
		: base(pyType, m, inner)
	{
	}
}
