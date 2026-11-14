// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/VersionOpError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core.Launcher;

public class VersionOpError : PyException
{
	public VersionOpError(string m)
		: base("VersionOpError", m)
	{
	}
}
