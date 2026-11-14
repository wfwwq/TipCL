// 来源: Windows 版参考仓库 src/TipCL/Core/Platform/SecretNotFoundException.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core.Platform;

public class SecretNotFoundException : PyException
{
	public SecretNotFoundException(string m = "Password not found")
		: base("PasswordDeleteError", m)
	{
	}
}
