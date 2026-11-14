using System.Collections.Generic;

namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:一个 Markdown 块(= Windows <c>Pages\Ai\MdBlock.cs</c>,30 行,**字节级照抄** —— 字段名/默认值/顺序都不改)。
/// 八类块共用这一个结构,用哪几个字段由 <see cref="Kind"/> 决定(与 Windows 同一套"宽记录"写法)。
/// </summary>
internal sealed class MdBlock
{
	public MdBlockKind Kind;

	public int Level;

	/// <summary>有序列表的起始号(默认 1)。</summary>
	public int Start = 1;

	/// <summary>代码块的语言标注。</summary>
	public string Lang = "";

	/// <summary>代码块的原文。</summary>
	public string Raw = "";

	/// <summary>段落 / 标题 / 引用首行等的行内序列。</summary>
	public List<MdInline> Inlines = new List<MdInline>();

	/// <summary>列表项(每项一条行内序列)。</summary>
	public List<List<MdInline>> Items = new List<List<MdInline>>();

	/// <summary>列表项缩进深度(0~4)。</summary>
	public List<int> ItemDepth = new List<int>();

	/// <summary>表格表头。</summary>
	public List<List<MdInline>> Header = new List<List<MdInline>>();

	/// <summary>表格数据行。</summary>
	public List<List<List<MdInline>>> Rows = new List<List<List<MdInline>>>();

	/// <summary>表格各列对齐(left / center / right)。</summary>
	public List<string> Align = new List<string>();

	/// <summary>引用块里的子块。</summary>
	public List<MdBlock> Children = new List<MdBlock>();
}
