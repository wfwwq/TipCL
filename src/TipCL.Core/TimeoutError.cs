namespace TipCL.Core;

public class TimeoutError : OSError
{
	public TimeoutError(string m)
		: base("TimeoutError", m, null)
	{
	}
}
