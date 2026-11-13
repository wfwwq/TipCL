namespace TipCL;

/// <summary>
/// 侧栏行:分组头 / 撑开的空白 / 可点条目。照搬 Windows 版 <c>NavEntry.cs</c>
/// (位置记录 <c>NavEntryKind Kind, string Key, string Label, bool Top, bool Draggable</c>,
/// 默认值与 <c>Stretch</c> / <c>Header(label)</c> 两个工厂一字不改)。
/// </summary>
public sealed record NavEntry(NavEntryKind Kind, string Key = "", string Label = "", bool Top = false, bool Draggable = false)
{
	/// <summary>把剩余空间顶到底部的撑条(Windows 同名字段)。</summary>
	public static readonly NavEntry Stretch = new NavEntry(NavEntryKind.Stretch);

	public static NavEntry Header(string label)
	{
		return new NavEntry(NavEntryKind.Header, "", label);
	}
}
