using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8b(第 1 步):**这是将来 <c>AiPage</c> 的一个分区** —— 先落地,是因为 <c>AiFixWindow</c> 调它。
///
/// 【Windows 源】<c>Pages\AiPage.cs:2153-2156</c>(注意:Windows 侧它在 <c>Pages\</c> 下,**不在** <c>Ai\</c> 子目录),
/// 全文只有 3 行,依赖只有 <see cref="BridgeEvent"/>(Core 已移植)与 <c>I18n.L</c>,
/// **不碰任何 <c>AiPage</c> 实例状态** —— 所以这一片是安全的,不会把整页拉进来。
///
/// 将来 `AiPage` 主文件落地时,写成同一个类的**另一个分区**即可(两边自然合起来,调用点不用改)。
/// **只搬这一个方法**:它旁边那几个静态(如 <c>UnsentOf</c>)等需要时再说,别顺手搬。
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>后台任务(下载/安装)结束时,给 AI 回一段"任务回报"文本(Windows 同名同文)。</summary>
	internal static string PendingTaskReport(string name, BridgeEvent finished)
	{
		return "[后台任务回报] " + name + " " + (finished.Success ? I18n.L("成功") : I18n.L("失败")) + ":" + finished.Message;
	}
}
