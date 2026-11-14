// 来源: Windows 版参考仓库 src/TipCL/Pages/Ai/MdBlock.cs(M4-H P5 搬运,只改命名空间;纯 C#,无 WPF 依赖)
using System.Collections.Generic;

namespace TipCL.App.Pages.Ai;

internal sealed class MdBlock
{
	public MdBlockKind Kind;

	public int Level;

	public int Start = 1;

	public string Lang = "";

	public string Raw = "";

	public List<MdInline> Inlines = new List<MdInline>();

	public List<List<MdInline>> Items = new List<List<MdInline>>();

	public List<int> ItemDepth = new List<int>();

	public List<List<MdInline>> Header = new List<List<MdInline>>();

	public List<List<List<MdInline>>> Rows = new List<List<List<MdInline>>>();

	public List<string> Align = new List<string>();

	public List<MdBlock> Children = new List<MdBlock>();
}
