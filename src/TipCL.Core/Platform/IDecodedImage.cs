// 来源: Windows 版参考仓库 src/TipCL/Core/Platform/IDecodedImage.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
using System;

namespace TipCL.Core.Platform;

public interface IDecodedImage : IDisposable
{
	int Width { get; }

	int Height { get; }

	bool HasAlpha { get; }

	IDecodedImage Resize(int width, int height);

	byte[] EncodePng();

	byte[] EncodeJpeg(int quality);
}
