using System;
using TipCL.Pages;
using TipCL.Pages.Plugins;
using TipCL.Services;

namespace TipCL;

/// <summary>
/// C7b(批3 轮A 后半-1):主窗口里和插件有关的那部分(Windows <c>Pages\Plugins\MainWindow.Plugins.cs</c>,125 行)。
///
/// 本文件**只落三个成员**(按 Lead 的切法,其余留给下一轮):
/// <list type="number">
/// <item><see cref="HookPlugins"/>(构造时调一次):订阅 <see cref="PluginUi.Changed"/>,并把**动态页 id 的回退口**
///   接上(<see cref="PageRegistry.FallbackFactory"/> ← <see cref="CreatePluginPage"/>);</item>
/// <item><see cref="LoadPluginsAfterConnect"/>:连上后端后拉一次插件界面贡献;</item>
/// <item><see cref="CreatePluginPage"/>:解析 <c>plugin:&lt;plugin&gt;:&lt;id&gt;</c> 这类导航表里没有的 id。</item>
/// </list>
///
/// Windows → mac 的差异:
/// <list type="bullet">
/// <item><c>PluginOverlays.Attach(this)</c> **不挂** —— 浮层已按 A 裁决并入 <c>PluginEvents</c>(见 D-158),
///   mac 侧没有浮层窗口可挂;</item>
/// <item>Windows 在 <c>MainWindow.GetPage</c> 里直接问 <c>CreatePluginPage</c>;mac 的页面解析走
///   <see cref="PageRegistry"/>,所以这里把同一个方法接到**受控回退口**上(只有注册表查不到时才轮到它);</item>
/// <item><c>Run(...)</c> → <c>PageBase.Run(...)</c>(mac 的外壳没有 Windows 那个 <c>Run</c> 薄壳)。</item>
/// </list>
/// </summary>
internal sealed partial class MainWindow
{
	private bool _pluginsHooked;

	/// <summary>
	/// 构造时调用一次:订阅插件界面变化 + 接上动态页 id 的回退口(幂等,靠 <see cref="_pluginsHooked"/>)。
	///
	/// 【临时缺口 · 界面变化后的刷新/摘页】**下一轮**落地 —— 那时这里订阅的会换成真正的
	/// <c>OnPluginUiChanged</c>(遍历页面缓存:`Update` / `MarkGone`,**当前页不摘、非当前页从缓存移除**)。
	/// 本轮先订一个空处理器,免得"事件订上了却没人接"这种半状态被误读成已实现。
	/// </summary>
	private void HookPlugins()
	{
		if (_pluginsHooked)
		{
			return;
		}

		_pluginsHooked = true;
		PluginUi.Changed += OnPluginUiChangedGapForNextRound;
		// 动态页 id:注册表查不到时才轮到它(未注册且非 plugin: 的 id 仍静默不动 —— 见 PageRegistry 的注释)
		PageRegistry.FallbackFactory = CreatePluginPage;
	}

	/// <summary>
	/// 【临时缺口】**下一轮替换**成真正的 <c>OnPluginUiChanged</c>:遍历 <c>PageHost</c> 的页面缓存,
	/// 贡献还在就 <c>Update(c)</c>,不在了就 <c>MarkGone()</c>(当前页不摘、非当前页从缓存移除),最后重建页签。
	/// 本轮它**什么都不做** —— 这是明写的缺口,不是遗漏(轮B 收口时与本注释一并删除)。
	/// </summary>
	private void OnPluginUiChangedGapForNextRound()
	{
		// 【临时缺口 · 页签条】**批3 轮B 落地**;此处暂不重建 RebuildPluginTabs()。
	}

	/// <summary>连上后端后拉一次插件界面贡献(Windows 同名)。</summary>
	private void LoadPluginsAfterConnect()
	{
		HookPlugins();
		PageBase.Run(PluginUi.RefreshAsync);
	}

	/// <summary>
	/// 导航表里没有的页面 id:插件页面 <c>plugin:&lt;plugin&gt;:&lt;id&gt;</c>(Windows 同名方法)。
	/// 前缀不对、或贡献已经不在(插件被禁用/卸载)⇒ 返回 null = **等于没注册**,外壳照旧静默不切页。
	/// </summary>
	private PageBase? CreatePluginPage(string id)
	{
		if (!id.StartsWith("plugin:", StringComparison.Ordinal))
		{
			return null;
		}

		UiContribution? c = PluginUi.FindPage(id);
		if (c == null)
		{
			return null;
		}

		return new PluginHostPage(c)
		{
			Id = id,
		};
	}
}
