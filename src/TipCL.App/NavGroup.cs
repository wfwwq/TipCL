using System.Collections.Generic;

namespace TipCL;

/// <summary>
/// 导航分组(标题 + 组内导航键)。照搬 Windows 版 <c>NavGroup.cs</c>:
/// 成员名(<c>Title</c> / <c>Keys</c> / <c>Clone</c>)与默认值一字不改。
/// </summary>
public sealed class NavGroup
{
	public string Title { get; set; } = "";

	public List<string> Keys { get; set; } = new List<string>();

	public NavGroup Clone()
	{
		return new NavGroup
		{
			Title = Title,
			Keys = new List<string>(Keys)
		};
	}
}
