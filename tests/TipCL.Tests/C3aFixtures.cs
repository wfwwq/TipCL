using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3a 的测试基座。
///
/// <see cref="C3aHome"/> 把 <c>PageLayoutStore.DirOverride</c> 指到 <c>TestHome.Root</c> 之下的
/// 一次性目录(红线:测试不许碰用户真实数据目录),并在 Dispose 里还原成 null + 丢内存缓存,
/// 这样用例之间(以及用例与 <c>Ported\PageLayoutTests</c>)不会串扰。
/// </summary>
internal sealed class C3aHome : IDisposable
{
	private readonly string _dir;

	internal C3aHome()
	{
		_dir = Path.Combine(TestHome.Root, "c3a-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
		PageLayoutStore.DirOverride = _dir;
		PageLayoutStore.ResetCache();
	}

	/// <summary>本次注入的落盘目录(一定在 TestHome.Root 之下)。</summary>
	internal string Dir => _dir;

	/// <summary>模拟"重启了应用 / 换了一台机器":丢掉内存缓存,下一次访问重新读盘。</summary>
	internal void ForgetCache()
	{
		PageLayoutStore.ResetCache();
	}

	public void Dispose()
	{
		PageLayoutStore.DirOverride = null;
		PageLayoutStore.ResetCache();
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

/// <summary>BlockHost 的可视树路径(测试用:顺着真实结构走,而不是加一堆专用访问器）。</summary>
internal static class C3aTree
{
	internal static Grid Root(BlockHost host)
	{
		return Assert.IsAssignableFrom<Grid>(host.Content);
	}

	/// <summary>根 Grid 的第 0 行:滚动内容区(Windows 是同一条 <c>Ui.Scroll(_list)</c>)。</summary>
	internal static SmoothScroll ScrollOf(BlockHost host)
	{
		return Assert.IsAssignableFrom<SmoothScroll>(Root(host).Children[0]);
	}

	/// <summary>滚动区里的区块竖排容器(间距 = Windows 的 Gap 10)。</summary>
	internal static SPanel ListOf(BlockHost host)
	{
		return Assert.IsAssignableFrom<SPanel>(ScrollOf(host).Content);
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

	/// <summary>此刻渲染在列表里的块 id(按可视顺序）。</summary>
	internal static List<string> RenderedIds(BlockHost host)
	{
		List<string> list = new List<string>();
		foreach (Control child in ListOf(host).Children)
		{
			if (child is BlockHost.BlockCard card)
			{
				list.Add(card.Spec.Id);
			}
		}
		return list;
	}

	/// <summary>此刻渲染在列表里的块卡(按可视顺序）。</summary>
	internal static List<BlockHost.BlockCard> RenderedCards(BlockHost host)
	{
		List<BlockHost.BlockCard> list = new List<BlockHost.BlockCard>();
		foreach (Control child in ListOf(host).Children)
		{
			if (child is BlockHost.BlockCard card)
			{
				list.Add(card);
			}
		}
		return list;
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
