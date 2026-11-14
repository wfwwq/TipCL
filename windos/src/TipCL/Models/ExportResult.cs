using System.Collections.Generic;

namespace TipCL.Models;

public sealed class ExportResult
{
	public List<string> Ok { get; set; } = new List<string>();

	public List<string> Failed { get; set; } = new List<string>();

	public string Dest { get; set; } = "";
}
