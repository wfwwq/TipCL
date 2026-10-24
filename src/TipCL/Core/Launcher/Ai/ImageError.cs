using System;

namespace TipCL.Core.Launcher.Ai;

public class ImageError : PyException
{
	public ImageError(string m, Exception inner = null)
		: base("ImageError", m, inner)
	{
	}
}
