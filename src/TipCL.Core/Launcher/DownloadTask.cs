// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/DownloadTask.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core.Launcher;

public sealed class DownloadTask
{
	public object Url;

	public string Dest;

	public string Sha1;

	public long? Size;

	public string Sha512;

	public DownloadTask(object url, string dest, string sha1 = null, long? size = null, string sha512 = null)
	{
		Url = url;
		Dest = dest;
		Sha1 = sha1;
		Size = size;
		Sha512 = sha512;
	}
}
