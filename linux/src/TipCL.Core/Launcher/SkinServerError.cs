// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/（M3-C0 搬运，连带依赖）
namespace TipCL.Core.Launcher;

public class SkinServerError : RuntimeError
{
	public SkinServerError(string m)
		: base("SkinServerError", m, null)
	{
	}
}
