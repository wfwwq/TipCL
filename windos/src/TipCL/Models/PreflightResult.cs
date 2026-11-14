using System.Collections.Generic;

namespace TipCL.Models;

public sealed class PreflightResult
{
	public bool Ok { get; set; } = true;

	public List<PreflightItem> Items { get; set; } = new List<PreflightItem>();
}
