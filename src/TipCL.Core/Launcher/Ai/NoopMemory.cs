// 来源: Windows 版参考仓库 src/TipCL/Core\Launcher\Ai\NoopMemory.cs（M2-B0 续搬）
using System.Collections.Generic;

namespace TipCL.Core.Launcher.Ai;

public class NoopMemory
{
	public virtual string Load(string chatId)
	{
		return "";
	}

	public virtual void Save(string chatId, string text)
	{
	}

	public virtual List<object> Search(string chatId, string query, int k = 3)
	{
		return new List<object>();
	}
}
