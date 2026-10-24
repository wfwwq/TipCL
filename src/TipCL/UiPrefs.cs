using System.Collections.Generic;

namespace TipCL;

public sealed class UiPrefs
{
	public double SideWidth { get; set; } = 196.0;

	public bool Dark { get; set; }

	public bool Anim { get; set; } = true;

	public double AnimScale { get; set; } = 1.0;

	public Dictionary<string, bool> Expanded { get; set; } = new Dictionary<string, bool>();

	public string Notes { get; set; } = "";

	public string LastUser { get; set; } = "";

	public bool SideFrost { get; set; } = true;

	public int SideFrostRadius { get; set; } = 30;

	public string CatalogInstance { get; set; } = "";
}
