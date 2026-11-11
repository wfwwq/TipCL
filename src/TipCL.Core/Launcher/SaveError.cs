namespace TipCL.Core.Launcher;

public class SaveError : PyException
{
	public SaveError(string m)
		: base("SaveError", m)
	{
	}
}
