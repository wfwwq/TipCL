// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/Ai/AiRequestException.cs（M3-C1 续搬，逐行一致）
using System;

namespace TipCL.Core.Launcher.Ai;

public class AiRequestException : Exception
{
	public AiRequestException(string m, Exception inner = null)
		: base(m, inner)
	{
	}
}
