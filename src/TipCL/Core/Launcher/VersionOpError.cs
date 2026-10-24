namespace TipCL.Core.Launcher;

public class VersionOpError : PyException
{
	public VersionOpError(string m)
		: base("VersionOpError", m)
	{
	}
}
