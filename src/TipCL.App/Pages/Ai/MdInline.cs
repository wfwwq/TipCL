using System.Collections.Generic;
using System.Text;

namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:一段行内内容(= Windows <c>Pages\Ai\MdInline.cs</c>,44 行,**行为逐字照抄**)。
/// <see cref="ToString"/> 会把行内重新拼回 Markdown 原文(粗体 <c>**x**</c>、斜体 <c>*x*</c>、
/// 删除线 <c>~~x~~</c>、链接 <c>[x](url)</c>、行内码 <c>`x`</c>、换行 <c>\n</c>)—— 这条被下游用来做
/// "取纯 Markdown"与调试输出,语义一字不改。
/// </summary>
internal sealed class MdInline
{
	public MdInlineKind Kind;

	public string Text = "";

	public string Url = "";

	public List<MdInline> Children = new List<MdInline>();

	public override string ToString()
	{
		switch (Kind)
		{
			case MdInlineKind.Text:
				return Text;
			case MdInlineKind.Code:
				return "`" + Text + "`";
			case MdInlineKind.Break:
				return "\n";
			default:
			{
				StringBuilder sb = new StringBuilder();
				foreach (MdInline child in Children)
				{
					sb.Append(child);
				}
				return Kind switch
				{
					MdInlineKind.Bold => "**" + sb?.ToString() + "**",
					MdInlineKind.Italic => "*" + sb?.ToString() + "*",
					MdInlineKind.Strike => "~~" + sb?.ToString() + "~~",
					MdInlineKind.Link => "[" + sb?.ToString() + "](" + Url + ")",
					_ => sb.ToString(),
				};
			}
		}
	}
}
