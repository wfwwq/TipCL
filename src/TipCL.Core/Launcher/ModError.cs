namespace TipCL.Core.Launcher;

public class ModError : PyException
{
	public ModError(string m)
		: base("ModError", m)
	{
	}
}
