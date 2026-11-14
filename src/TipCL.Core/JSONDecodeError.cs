// 来源: Windows 版参考仓库 src/TipCL/Core/JSONDecodeError.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core;

public class JSONDecodeError : ValueError
{
	public JSONDecodeError(string msg, string doc, int pos)
		: base("JSONDecodeError", Format(msg, doc, pos), null)
	{
	}

	private static string Format(string msg, string doc, int pos)
	{
		int num = 1;
		int num2 = -1;
		for (int i = 0; i < pos && i < doc.Length; i++)
		{
			if (doc[i] == '\n')
			{
				num++;
				num2 = i;
			}
		}
		int num3 = pos - num2;
		return $"{msg}: line {num} column {num3} (char {pos})";
	}
}
