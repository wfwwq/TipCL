using System.Collections.Generic;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core;

/// <summary>
/// 对外开放的布局 v2 RPC —— 设计 <c>docs/layout-v2-design.md</c> §5.1 里
/// <c>Core/BackendApi.Layout.cs</c> 那一格(此前一直缺件,<c>PROGRESS.md</c> 的遗留清单记的是
/// 「若后续要对外开放布局 RPC 需要补」)。
///
/// **语义不另造一套**:两个方法直接转 <see cref="LayoutTools"/>,与 AI 工具的
/// <c>layout_get</c> / <c>layout_apply</c> 共用同一份实现 —— 校验、自动备份(改前把当前布局存成
/// <c>profiles["AI 备份 &lt;时间&gt;"]</c>、只留最近 5 份)、写通 v1 与清缓存,全部与 AI 那条路完全一致。
/// <list type="bullet">
/// <item><c>layout_v2_get()</c>(只读):页面区块顺序与显隐、页面模式、自建区块、分栏比例、顶栏页签、命名方案;</item>
/// <item><c>layout_v2_apply(...)</c>(写):参数与 AI 的 <c>layout_apply</c> 同一套 ——
///   <c>page</c>+<c>blocks</c>、<c>mode</c>/<c>cols</c>/<c>gap</c>、<c>profile</c>、<c>json</c>、
///   <c>widgets</c>、<c>shell_tabs</c>;失败返回中文原因(不把异常抛给调用方)。</item>
/// </list>
///
/// 为什么写在这个分部里:<see cref="BackendApi"/> 是 partial,RPC 按方法名反射派发
/// (<see cref="RpcDispatcher"/>;名字必须全小写、不能重载)⇒ **不必改 <c>BackendApi.cs</c> 本体**
/// 就能对外暴露。<c>UiRows</c>/<c>plugins_*</c> 那些既有方法一个字没动。
///
/// 谁能调:
/// <list type="bullet">
/// <item>插件 —— <c>ctx.Launcher("layout_v2_get", ...)</c>,需要 manifest 里的 <c>launcher</c> 权限;
///   <c>TipCL.Core.Plugins.PluginHostApi</c> 的 <c>LauncherDeny</c> 里**没有**这两个方法(只有
///   <c>ai_confirm</c>/<c>ai_answer</c>/<c>shutdown</c>/<c>start_task</c> 与 <c>plugins_*</c> 前缀被拒),
///   这是刻意留的口子:设计里这两个 RPC 的用途就是「供 AI / 插件 / 未来端口」;</item>
/// <item>外部端口 —— 桥的 <c>POST /rpc</c>,方法名同上。</item>
/// </list>
/// </summary>
public partial class BackendApi
{
	/// <summary>布局 v2 摘要(只读)。与 AI 工具 <c>layout_get</c> 返回同一份文本。</summary>
	public object layout_v2_get()
	{
		return LayoutTools.Get();
	}

	/// <summary>
	/// 改布局 v2。<paramref name="args"/> 的字段与 AI 工具 <c>layout_apply</c> 的 schema 完全一致
	/// (见 <see cref="LayoutTools"/>);用 <see cref="VarKwAttribute"/> 收全部关键字参数,与 AI 侧
	/// 拿到的那只 <c>PyDict</c> 同形。返回中文结果文本:成功里带自动备份的方案名,失败就是原因。
	/// </summary>
	public object layout_v2_apply([VarKw] PyDict args)
	{
		return LayoutTools.Apply(args ?? new PyDict());
	}
}
