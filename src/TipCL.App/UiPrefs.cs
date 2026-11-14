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

	/// <summary>
	/// 分栏比例(键 <c>"&lt;pageId&gt;/&lt;splitId&gt;"</c>,值 0.15~0.85)—— 由 <c>PageLayoutHost</c> 读写。
	/// <b>【与 Windows 的存储位置不同】</b>:Windows 存在**布局 v2 文档**的 <c>Splits[pageId][splitId]</c> 里
	/// (那张文档在 mac 侧不存在,布局家族本版不移植);mac 复用既有偏好存储,语义(拖动后记住、双击复位)一致。
	/// </summary>
	public Dictionary<string, double> Splits { get; set; } = new Dictionary<string, double>();

	/// <summary>
	/// 格子类布局的**列数**(键 = pageId,值 = 列数)—— 由 <c>PageLayoutHost.PageColumns</c> 读写。
	/// <b>【与 Windows 的存储位置不同】</b>:Windows 存在**布局 v2 文档**的 <c>pages[pageId].Cols</c>(且要求
	/// <c>Mode == grid</c>);mac 复用该文档缺失时的既有偏好存储,语义一致:默认回落各页现状列数、夹到 [min,max]。
	/// 与 <see cref="Splits"/> 分开存(键形态不同,免得两套语义互相打架)。
	/// </summary>
	public Dictionary<string, int> Columns { get; set; } = new Dictionary<string, int>();
}
