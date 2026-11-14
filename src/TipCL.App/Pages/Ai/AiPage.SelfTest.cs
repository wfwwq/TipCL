using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K11:收尾分区 —— 四个自测缝与 <c>OnShown</c>。【Windows 源】<c>Pages\AiPage.cs:2690-2712</c>
/// (<c>SelfTestLoad</c> / <c>SelfTestUser</c> / <c>SelfTestBusy</c> / <c>SelfTestScrollEnd</c>)与
/// <c>:2744-2752</c>(<c>OnShown</c>)。
///
/// <b>K11 的另一半(那段静态分区)不在这里</b>:Windows <c>:2131-2202</c> 的
/// <c>PendingTasksOf</c> / <c>PendingTaskReport</c> / <c>UnsentOf</c> / <c>StopReasonOf</c> 四个静态成员
/// 早在 <c>Pages\AiPage.Tasks.cs</c> 里就落地了(C8b,<c>AiFixWindow</c> 的硬前置),所以本包**不再重复声明**
/// —— 两个文件合起来才等于 Windows 那一整块。其余静态小件在 K4/K10 两个分区里
/// (<c>Divider</c> / <c>Note</c> / <c>PayloadStr</c> / <c>TaskIdOf</c> / <c>BackendBusy</c> / <c>ChatIdOf</c> /
/// <c>IsMemoryTool</c> / <c>DetailOf</c> / <c>StopNote</c>)。K11 与 <c>Ai\AiSelfTest.cs</c> 合并的裁决
/// (<c>digest.md:13652</c> 那一轮的口径)在这里兑现:自测缝没有另起文件、也没有另起类。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><b>自测缝全部照抄</b>(名称、参数、可访问性 <c>internal</c> 一字不改):这四个缝是离屏冒烟
///   (<c>--smoke</c>)与用例驱动这一页的唯一入口,不把它们搬过来,这一页在 mac 上就没法被自动验;</item>
/// <item><c>OnShown</c>:Windows 先判 <c>_pluginViews.PluginViewActive</c>(插件视图盖在页面上时不去抢焦点)
///   —— 那一位依赖 mac 尚未移植的 <c>AiViewStrip</c> ⇒ 本次**只保留 <c>_input.Focus()</c>**
///   (没有插件视图标签条,也就没有"被盖住"这个状态;逐条列在交付报告)。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>【测试缝】灌一份对话数据并重画(【Windows 源】:2690-2695)。</summary>
	internal void SelfTestLoad(AiStoreDto store)
	{
		_store = store;
		RenderChats();
		RenderMessages();
	}

	/// <summary>【测试缝】以"用户发了一句"的姿态上屏并进入忙态(【Windows 源】:2697-2702)。</summary>
	internal void SelfTestUser(string text)
	{
		_runChatId = _store.ActiveId;
		SetBusy(on: true);
		Add(new Bubble("user", text, RetryFrom));
	}

	/// <summary>【测试缝】直接切忙态(【Windows 源】:2704-2707)。</summary>
	internal void SelfTestBusy(bool on)
	{
		SetBusy(on);
	}

	/// <summary>【测试缝】滚到底(【Windows 源】:2709-2712)。</summary>
	internal void SelfTestScrollEnd()
	{
		_scroll.ScrollToEnd();
	}

	/// <summary>页面显示时把焦点给输入框(【Windows 源】:2744-2752;插件视图那一判见类注释第 2 条)。</summary>
	public override void OnShown()
	{
		_input.Focus();
	}
}
