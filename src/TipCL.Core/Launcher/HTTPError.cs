// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/HTTPError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core.Launcher;

public class HTTPError : PyException
{
	public long StatusCode { get; }

	public HTTPError(string m, long code)
		: base("HTTPError", m)
	{
		StatusCode = code;
	}
}
