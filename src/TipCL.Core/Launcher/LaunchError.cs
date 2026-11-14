// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/（M3-C0 搬运，仅改跨平台适配；Windows 专属 API 用 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core.Launcher;

public class LaunchError : PyException
{
	public LaunchError(string m)
		: base("LaunchError", m)
	{
	}
}
