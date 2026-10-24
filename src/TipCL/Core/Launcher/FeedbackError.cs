using System;

namespace TipCL.Core.Launcher;

public class FeedbackError : PyException
{
	public FeedbackError(string m, Exception inner = null)
		: base("FeedbackError", m, inner)
	{
	}
}
