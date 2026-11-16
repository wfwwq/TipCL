using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3a 的测试基座。
///
/// <see cref="C3aHome"/> 把 <c>PageLayoutStore.DirOverride</c> 与 <c>LayoutStoreV2.DirOverride</c>
/// 一起指到 <c>TestHome.Root</c> 之下的同一一次性目录(红线:测试不许碰用户真实数据目录),
/// 并在 Dispose 里还原成 null + 丢内存缓存,这样用例之间(以及用例与 <c>Ported\PageLayoutTests</c>)不会串扰。
///
/// R32-MWG:BlockHost 现在是 LayoutEditor 的薄壳,布局真源变成 <c>ui_layout_v2.json</c>(LayoutStoreV2),
/// v1 的 <c>ui_page_layouts.json</c> 只是单向投影 —— 所以隔离必须两家一起做。进程级还有
/// <see cref="TestHome"/> 的 TIPCL_HOME 兜底(两个 DirPath 都经 Utils.ResolveRoot),
/// 这里再按用例分目录,保证"上一个用例存过的布局"不会漏进下一个用例。
/// </summary>
internal sealed class C3aHome : IDisposable
{
	private readonly string _dir;

	private readonly ILayoutV1Bridge? _previousBridge;

	internal C3aHome()
	{
		_dir = Path.Combine(TestHome.Root, "c3a-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
		PageLayoutStore.DirOverride = _dir;
		PageLayoutStore.ResetCache();
		// 测试进程里 App.OnFrameworkInitializationCompleted 不会跑,正常也是 null;
		// 仍显式存一份/清空,避免别的用例装配过桥之后串到这里(桥会把 v1 读写导到别处)。
		_previousBridge = LayoutStoreV2.V1Bridge;
		LayoutStoreV2.V1Bridge = null;
		LayoutStoreV2.DirOverride = _dir;
		LayoutStoreV2.ResetCache();
	}

	/// <summary>本次注入的落盘目录(一定在 TestHome.Root 之下)。</summary>
	internal string Dir => _dir;

	/// <summary>模拟"重启了应用 / 换了一台机器":丢掉内存缓存,下一次访问重新读盘。</summary>
	internal void ForgetCache()
	{
		PageLayoutStore.ResetCache();
		LayoutStoreV2.ResetCache();
	}

	public void Dispose()
	{
		PageLayoutStore.DirOverride = null;
		PageLayoutStore.ResetCache();
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.ResetCache();
		LayoutStoreV2.V1Bridge = _previousBridge;
		try
		{
			Directory.Delete(_dir, recursive: true);
		}
		catch (Exception)
		{
		}
	}
}

/// <summary>把 Toast 截下来(Windows 是 <c>AppServices.Window?.Toast</c>,mac 是 ToastSink;C2a 起的统一口径）。</summary>
internal sealed class C3aToasts : IDisposable
{
	private readonly Action<string, string, ToastKind>? _previous;

	internal C3aToasts()
	{
		_previous = AppServices.ToastSink;
		AppServices.ToastSink = delegate(string title, string body, ToastKind kind)
		{
			Items.Add((Title: title, Body: body, Kind: kind));
		};
	}

	internal List<(string Title, string Body, ToastKind Kind)> Items { get; } = new List<(string, string, ToastKind)>();

	public void Dispose()
	{
		AppServices.ToastSink = _previous;
	}
}

/// <summary>BlockHost 可视树路径(测试用:顺着真实结构走。R32-MWG 后 BlockHost 是 LayoutEditor 薄壳,形状与 Windows/Linux 逐层对应:
/// 根 Grid(*,Auto) → [0] ScrollViewer → Content 是 FillPanel(单列纵向流,每块一张 LayoutCard);[1] SPanel 里依次是托盘 Border 与工具条 Border)。</summary>
internal static class C3aTree
{
	internal static Grid Root(BlockHost host)
	{
		return Assert.IsAssignableFrom<Grid>(host.Content);
	}

	/// <summary>根 Grid 的第 0 行:滚动内容区(Windows 是 <c>Ui.Scroll(_list)</c>)。
	/// mac 没有 SmoothScroll,LayoutEditor 用原生 ScrollViewer(与 Linux 端口同口径)。</summary>
	internal static ScrollViewer ScrollOf(BlockHost host)
	{
		return Assert.IsAssignableFrom<ScrollViewer>(Root(host).Children[0]);
	}

	/// <summary>滚动区里的区块竖排容器(Windows 的 SPanel;_list 是 FillPanel,多了 V-1 的 FillChild)。</summary>
	internal static FillPanel ListOf(BlockHost host)
	{
		return Assert.IsAssignableFrom<FillPanel>(ScrollOf(host).Content);
	}

	/// <summary>根 Grid 的第 1 行:托盘 / 工具条那一列。</summary>
	internal static SPanel BottomOf(BlockHost host)
	{
		return Assert.IsAssignableFrom<SPanel>(Root(host).Children[1]);
	}

	internal static Border TrayHostOf(BlockHost host)
	{
		return Assert.IsAssignableFrom<Border>(BottomOf(host).Children[0]);
	}

	internal static Border BarHostOf(BlockHost host)
	{
		return Assert.IsAssignableFrom<Border>(BottomOf(host).Children[1]);
	}

	/// <summary>「已隐藏的区块」托盘(WrapPanel;trayHost 的子 SPanel 的第 2 个孩子)。</summary>
	internal static WrapPanel TrayOf(BlockHost host)
	{
		SPanel panel = Assert.IsAssignableFrom<SPanel>(TrayHostOf(host).Child);
		return Assert.IsAssignableFrom<WrapPanel>(panel.Children[1]);
	}

	/// <summary>此刻渲染在当前可视区里的块卡(按可视顺序)。
	/// 编辑态会把 stack 页临时折算成 12 列网格(LayoutEditor.PrepareFreeDrag),那时根 Grid 的第 0 行是
	/// 画布 ScrollViewer,卡挂在它的内容里,纵向 FillPanel 已退场 —— 所以统一从 ScrollViewer 的**内容**
	/// 往下取(两种模式下内容都在,且都不依赖模板展开)。</summary>
	internal static List<LayoutCard> RenderedCards(BlockHost host)
	{
		if (ScrollOf(host).Content is Visual surface)
		{
			return surface.GetVisualDescendants().OfType<LayoutCard>().ToList();
		}
		return new List<LayoutCard>();
	}

	/// <summary>此刻渲染在列表里的块 id(按可视顺序)。</summary>
	internal static List<string> RenderedIds(BlockHost host)
	{
		return RenderedCards(host).Select(card => card.Spec.Id).ToList();
	}

	/// <summary>此刻渲染在列表里的块 id 的另一种写法(直接读模型顺序)。</summary>
	internal static string[] OrderIds(BlockHost host)
	{
		string[] ids = new string[host.OrderNow.Count];
		for (int i = 0; i < host.OrderNow.Count; i++)
		{
			ids[i] = host.OrderNow[i].Id;
		}
		return ids;
	}
}

/// <summary>C3a 建块 / 建宿主的公共写法。</summary>
internal static class C3aBuild
{
	/// <summary>一个块描述:<paramref name="contentHeight"/> 给内容体一个确定高度,便于布局用例算数。</summary>
	internal static BlockSpec Spec(string id, bool visible = true, double contentHeight = 40.0, string? title = null)
	{
		return new BlockSpec
		{
			Id = id,
			Title = title ?? id,
			DefaultVisible = visible,
			Content = new Border { Height = contentHeight }
		};
	}

	internal static BlockHost Host(string pageId, params BlockSpec[] specs)
	{
		return new BlockHost(pageId, new List<BlockSpec>(specs));
	}

	/// <summary>一个没有内容的卡(内容工厂返回它,用来验证"同一 type 只造一次"）。</summary>
	internal static Border Content(double height = 30.0)
	{
		return new Border { Height = height, Background = Brushes.Red };
	}
}
