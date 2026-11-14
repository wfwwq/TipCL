// 来源: Windows 版参考仓库 src/TipCL/Core/KeyError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core;

public class KeyError : PyException
{
	public object Key { get; }

	public KeyError(object key)
		: base("KeyError", Py.Repr(key))
	{
		Key = key;
	}
}
