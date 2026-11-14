namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:Markdown 块类型(= Windows <c>Pages\Ai\MdBlockKind.cs</c>,13 行,**字节级照抄**)。
/// <b>注意是**八类**,不是四类</b>:<c>Paragraph / Heading / Bullet / Ordered / Quote / Code / Table / Rule</c> ——
/// 顺序与取值一字不改(渲染层按这个顺序判分支)。
/// </summary>
internal enum MdBlockKind
{
	Paragraph,
	Heading,
	Bullet,
	Ordered,
	Quote,
	Code,
	Table,
	Rule,
}
