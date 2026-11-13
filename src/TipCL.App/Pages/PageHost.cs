using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// 页面宿主:一块 <see cref="ContentControl"/>,负责"当前页是哪一页"。
///
/// Windows 版这部分逻辑长在 <c>MainWindow</c> 里(<c>_pages</c> / <c>_current</c> / <c>_currentId</c>
/// + <c>GetPage</c>/<c>Navigate</c> 里那几行),C2b 把它抽成独立控件,理由是 mac 侧到 C2b 还
/// **一个页面都没有**,生命周期与缓存语义要能单独测(不需要真窗口)。语义与 Windows 逐条对齐:
/// <list type="bullet">
/// <item>同一个 id 只建一次(**每页状态就靠这个缓存留着**,再切回来还是原来那个实例);</item>
/// <item>未知 id(没注册工厂)返回 null,<c>Navigate</c> **静默不切页**、当前页与当前 id 都不变;</item>
/// <item>切页时:老页 <c>OnHidden()</c> → 换 <c>Content</c>(<c>instant</c> 或动画关掉时直接换,
/// 否则走 <c>Motion.PageSwap</c>)→ 新页 <c>EnsureLoadedAsync()</c> + <c>OnShown()</c>;</item>
/// <item>已加载过的页再切回来**不再** <c>LoadAsync</c>(<c>PageBase</c> 的 <c>_loaded</c> 管着),
/// 但 <c>OnShown</c> 每次切到都调。</item>
/// </list>
/// </summary>
internal sealed class PageHost : ContentControl
{
	private readonly Dictionary<string, PageBase> _pages = new Dictionary<string, PageBase>(StringComparer.Ordinal);

	private readonly Func<string, PageBase?> _resolve;

	private PageBase? _current;

	private string _currentId = "";

	/// <param name="resolve">id → 页面工厂的解析器;默认走 <see cref="PageRegistry"/>。测试可以注入自己的表。</param>
	internal PageHost(Func<string, PageBase?>? resolve = null)
	{
		_resolve = resolve ?? PageRegistry.Create;
		Focusable = false;
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;
	}

	/// <summary>当前页(还没有页时是 null)。</summary>
	internal PageBase? Current => _current;

	/// <summary>当前页 id;与 Windows 一样,**没有页面时也是空串**(不是 null)。</summary>
	internal string CurrentId => _currentId;

	/// <summary>
	/// 取(或首次建)一个页面:先查缓存,再问解析器;解析不出来返回 null 且不缓存。
	/// 与 Windows 的 <c>MainWindow.GetPage</c> 同一条口径(含"设计样张页"那类不走导航表的特例:
	/// mac 侧只要注册表里有就能取到)。
	/// </summary>
	internal PageBase? GetPage(string id)
	{
		if (id is null)
		{
			return null;
		}
		if (_pages.TryGetValue(id, out PageBase? cached))
		{
			return cached;
		}
		PageBase? page = _resolve(id);
		if (page is null)
		{
			return null;
		}
		// Windows 在 GetPage 里回填 Id(页面自己可能没填),这里照做。
		page.Id = id;
		_pages[id] = page;
		return page;
	}

	/// <summary>已经建过的页面数(测试用;Windows 的 <c>_pages</c> 没有对外暴露)。</summary>
	internal int PageCount => _pages.Count;

	/// <summary>
	/// 切到某一页。返回是否真的切了(未知 id / 已经是当前页 → false 且什么都不做)。
	/// </summary>
	/// <param name="id">页面 id。</param>
	/// <param name="instant">true = 不带动画直接换(Windows 的 <c>Navigate(id, instant)</c>)。</param>
	/// <param name="forward">换页动画的方向(Windows 按导航顺序算 前进/后退)。</param>
	/// <param name="runHooks">要不要跑 <c>EnsureLoadedAsync</c> + <c>OnShown</c>
	/// (Windows 是在后端连上之后才算"可以加载";C2b 还没接后端,所以由调用方传 —— 见 D-92)。</param>
	internal bool Navigate(string id, bool instant, bool forward, bool runHooks)
	{
		if (_currentId == id)
		{
			return false;
		}
		PageBase? page = GetPage(id);
		if (page is null)
		{
			return false;
		}
		_current?.OnHidden();
		_currentId = id;
		_current = page;
		if (instant || !Motion.Enabled)
		{
			Content = page;
		}
		else
		{
			Motion.PageSwap(this, page, forward);
		}
		if (runHooks)
		{
			PageBase.Run(async delegate
			{
				await page.EnsureLoadedAsync();
				page.OnShown();
			});
		}
		return true;
	}

	/// <summary>把所有页面的缓存清掉(测试夹具收尾用;Windows 没有这个方法,页面缓存活到进程结束)。</summary>
	internal void Clear()
	{
		_pages.Clear();
		_current = null;
		_currentId = "";
		Content = null;
	}
}
