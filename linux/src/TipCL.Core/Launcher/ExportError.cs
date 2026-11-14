namespace TipCL.Core.Launcher;

public class ExportError : PyException
{
	public ExportError(string m)
		: base("ExportError", m)
	{
	}
}
