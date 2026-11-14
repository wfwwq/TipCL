using System;

namespace TipCL.Core.Launcher;

public class AuthHttpException : PyException
{
	public AuthHttpException(string m, Exception inner = null)
		: base("RequestException", m, inner)
	{
	}
}
