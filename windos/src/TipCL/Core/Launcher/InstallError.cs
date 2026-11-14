namespace TipCL.Core.Launcher;

public class InstallError : PyException
{
	public InstallError(string m)
		: base("InstallError", m)
	{
	}
}
