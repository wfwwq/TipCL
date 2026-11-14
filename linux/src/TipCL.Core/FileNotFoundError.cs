// 来源: Windows 版参考仓库 src/TipCL/Core/FileNotFoundError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core;

public class FileNotFoundError : OSError
{
	public FileNotFoundError(string m)
		: base("FileNotFoundError", m, null)
	{
	}

	public static FileNotFoundError ForPath(string p)
	{
		return new FileNotFoundError("[Errno 2] No such file or directory: " + Py.Repr(p));
	}
}
