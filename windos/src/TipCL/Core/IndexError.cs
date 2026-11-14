namespace TipCL.Core;

public class IndexError : PyException
{
	public IndexError(string m)
		: base("IndexError", m)
	{
	}
}
