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
	/// 分栏比例(键 <c>"&lt;pageId&gt;/&lt;splitId&gt;"</c>,值 0.15~0.85)。
	/// <b>【R32-MWG:已降级为"只读旧值"】</b>真源是**布局 v2 文档**的 <c>Splits[pageId][splitId]</c>
	/// (<c>LayoutStoreV2</c>);<c>PageLayoutHost</c> 读的时候先看文档,文档没有才读这里(读到就写回文档并删掉本键),
	/// 写的时候一律写文档、成功后删掉本键。这个字典只为"升级前存过比例的机器"保底,新机器不会再长出新键。
	/// </summary>
	public Dictionary<string, double> Splits { get; set; } = new Dictionary<string, double>();

	/// <summary>
	/// 格子类布局的**列数**(键 = pageId,值 = 列数)。
	/// <b>【R32-MWG:已降级为"只读旧值"】</b>真源是**布局 v2 文档**的 <c>Pages[pageId].Cols</c>(且要求
	/// <c>Mode == grid</c>);<c>PageLayoutHost.PageColumns</c> 只在文档里没有这一页时才读这里,
	/// <c>WritePageColumns</c> 一律写文档并把该页声明成 <c>grid</c>、成功后删掉本键。
	/// 与 <see cref="Splits"/> 分开存(键形态不同,免得两套语义互相打架)。
	/// </summary>
	public Dictionary<string, int> Columns { get; set; } = new Dictionary<string, int>();
}
