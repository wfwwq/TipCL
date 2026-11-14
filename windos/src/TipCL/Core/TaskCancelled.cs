namespace TipCL.Core;

public class TaskCancelled : PyException
{
	public TaskCancelled()
		: base("TaskCancelled", "")
	{
	}
}
