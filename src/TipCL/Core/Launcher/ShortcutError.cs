namespace TipCL.Core.Launcher;

public class ShortcutError : PyException
{
	public ShortcutError(string m)
		: base("ShortcutError", m)
	{
	}
}
