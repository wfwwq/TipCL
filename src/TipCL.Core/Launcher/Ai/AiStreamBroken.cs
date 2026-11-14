// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/Ai/AiStreamBroken.cs（M3-C1 续搬，逐行一致）
using System;

namespace TipCL.Core.Launcher.Ai;

public class AiStreamBroken : Exception
{
	public AiStreamBroken(string m, Exception inner = null)
		: base(m, inner)
	{
	}
}
