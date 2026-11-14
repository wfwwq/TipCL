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
