// 来源: Windows 版参考仓库 src/TipCL/Core/Platform/ISecretStore.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
namespace TipCL.Core.Platform;

public interface ISecretStore
{
	string GetPassword(string service, string username);

	void SetPassword(string service, string username, string password);

	void DeletePassword(string service, string username);
}
