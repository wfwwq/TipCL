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

	public Dictionary<string, List<string>> VmFavorites { get; set; } = new Dictionary<string, List<string>>();

	/// <summary>布局编辑:「自动靠拢」(网格压紧上浮 / 左右互换 / 画布磁性吸附)。缺省开 = 与旧版行为一致。</summary>
	public bool LayoutAutoPack { get; set; } = true;

	/// <summary>启动页 v3 组件区(LaunchZone.ToJson);空 = 还没在编辑器里提交过,从旧版启动页区块读时推导。</summary>
	public string LaunchZone { get; set; } = "";
}
