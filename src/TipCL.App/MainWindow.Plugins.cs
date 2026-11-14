using System;
using System.Collections.Generic;
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

	private Func<string, PageBase?>? _previousFallback;

	/// <summary>退订 + 还原回退口(**测试收尾用**;Windows 没有这一步 —— 主窗口活到进程结束)。
	/// 不还原就会把订阅与进程级回退口留给下一个用例(D-149 那族纪律)。</summary>
	internal void UnhookPlugins()
	{
		if (!_pluginsHooked)
		{
			return;
		}

		_pluginsHooked = false;
		PluginUi.Changed -= OnPluginUiChanged;
		PageRegistry.FallbackFactory = _previousFallback;
		_previousFallback = null;
	}

	/// <summary>
	/// 构造时调用一次:订阅插件界面变化 + 接上动态页 id 的回退口(幂等,靠 <see cref="_pluginsHooked"/>)。
	/// **内部可见性**是给测试用的(与 <c>Navigate</c>/<c>GetPage</c> 同一口径):外壳构造函数里的调用点下一轮接,
	/// 那时本条用例改为"从构造路径驱动"。
	/// </summary>
	internal void HookPlugins()
	{
		if (_pluginsHooked)
		{
			return;
		}

		_pluginsHooked = true;
		PluginUi.Changed += OnPluginUiChanged;
		// 动态页 id:注册表查不到时才轮到它(未注册且非 plugin: 的 id 仍静默不动 —— 见 PageRegistry 的注释)
		_previousFallback = PageRegistry.FallbackFactory;
		PageRegistry.FallbackFactory = CreatePluginPage;
	}

	/// <summary>
	/// 插件界面贡献变了(Windows 同名):遍历**已经打开过**的页面,只处理插件页 ——
	/// 贡献还在就 <see cref="PluginHostPage.Update"/>;不在了就 <see cref="PluginHostPage.MarkGone"/>,
	/// 并且**当前页留在缓存里(标成不可用),非当前页直接从缓存摘掉**(Windows 的 <c>_currentId != kv.Key</c> 判据)。
	/// </summary>
	private void OnPluginUiChanged()
	{
		foreach (KeyValuePair<string, PageBase> kv in _host.Pages)
		{
			if (kv.Value is not PluginHostPage page)
			{
				continue;
			}

			UiContribution? c = PluginUi.FindPage(kv.Key);
			if (c != null)
			{
				page.Update(c);
				continue;
			}

			page.MarkGone();
			if (!string.Equals(_host.CurrentId, kv.Key, StringComparison.Ordinal))
			{
				_host.RemovePage(kv.Key);
			}
		}

		// 【临时缺口 · 页签条】**批3 轮B 落地**;此处暂不重建 RebuildPluginTabs()。
	}

	/// <summary>连上后端后拉一次插件界面贡献(Windows 同名;内部可见性同上,给测试驱动用)。</summary>
	internal void LoadPluginsAfterConnect()
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
