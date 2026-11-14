// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/AuthResponse.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
using System.Text;

namespace TipCL.Core.Launcher;

public sealed class AuthResponse
{
	public int StatusCode;

	public byte[] Content = new byte[0];

	public string Text => Encoding.UTF8.GetString(Content ?? new byte[0]);

	public string Text200 => Py.Slice(Text, 0, 200);

	public object Json()
	{
		string text = Text;
		if (text.Length > 0 && text[0] == '\ufeff')
		{
			text = text.Substring(1);
		}
		return PyJson.Loads(text);
	}
}
