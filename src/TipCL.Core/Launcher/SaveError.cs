// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/SaveError.cs（M2-B0 续搬，Saves 依赖）
namespace TipCL.Core.Launcher;

public class SaveError : PyException
{
	public SaveError(string m)
		: base("SaveError", m)
	{
	}
}
