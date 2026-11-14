// 来源: Windows 版参考仓库 src/TipCL/Core/PyException.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
using System;

namespace TipCL.Core;

public class PyException : Exception
{
	public string PyType { get; }

	public PyException(string pyType, string message, Exception inner = null)
		: base(message ?? "", inner)
	{
		PyType = pyType;
	}

	public override string ToString()
	{
		return PyType + ": " + Message;
	}
}
