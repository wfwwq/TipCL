namespace TipCL.Core.Launcher;

public class ReError : PyException
{
	public ReError(string m)
		: base("error", m)
	{
	}
}
