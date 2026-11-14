namespace TipCL.Core;

public class UnicodeDecodeError : ValueError
{
	public UnicodeDecodeError(string m)
		: base("UnicodeDecodeError", m, null)
	{
	}
}
