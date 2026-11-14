// 来源: Windows 版参考仓库 src/TipCL/Core/RuntimeError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
using System;

namespace TipCL.Core;

public class RuntimeError : PyException
{
	public RuntimeError(string m, Exception inner = null)
		: base("RuntimeError", m, inner)
	{
	}

	protected RuntimeError(string t, string m, Exception inner)
		: base(t, m, inner)
	{
	}
}
