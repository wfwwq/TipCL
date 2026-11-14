using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TipCL.Plugins;

/// <summary>
/// .NET 插件注册的原生 WPF 界面（页面 / 首页卡片 / AI 标签）。只在进程内使用，WPF 界面直接读这个静态类：
/// <list type="bullet">
/// <item><see cref="Entries"/>：当前全部原生入口（快照）。</item>
/// <item><see cref="Find"/>：按 (plugin, kind, id) 找入口，然后调 <see cref="NativeUiEntry.Create"/> / <see cref="NativeUiEntry.CreateAiView"/> 生成控件。</item>
/// <item><see cref="Changed"/>：增删时触发（在注册线程上触发，界面要自己切回 UI 线程）。</item>
/// </list>
/// 同样的入口也会出现在 RPC <c>plugins_ui()</c> 的结果里，带 <c>native:true</c>、<c>url:""</c>，界面可以统一列出后再按 native 分流。
/// 工厂在 UI 线程上调用；插件抛异常时界面应显示错误占位而不是崩溃。
/// </summary>
public static class PluginUiRegistry
{
	private static readonly object Lock = new object();

	private static readonly List<NativeUiEntry> List = new List<NativeUiEntry>();

	/// <summary>入口增删时触发（任意线程）。</summary>
	public static event Action Changed;

	/// <summary>当前全部原生入口的快照。</summary>
	public static IReadOnlyList<NativeUiEntry> Entries
	{
		get
		{
			lock (Lock)
			{
				return List.ToList();
			}
		}
	}

	/// <summary>按插件、类型（page / dashCard / aiView）、入口 id 查找；找不到返回 null。</summary>
	public static NativeUiEntry Find(string plugin, string kind, string id)
	{
		lock (Lock)
		{
			return List.FirstOrDefault((NativeUiEntry e) => e.Plugin == plugin && e.Kind == kind && e.Id == id);
		}
	}

	/// <summary>注册（同 plugin+kind+id 会替换旧的）。返回的 IDisposable 用来注销。由 TipCL 内核调用，界面不用管。</summary>
	public static IDisposable Register(NativeUiEntry entry)
	{
		lock (Lock)
		{
			List.RemoveAll((NativeUiEntry e) => e.Plugin == entry.Plugin && e.Kind == entry.Kind && e.Id == entry.Id);
			List.Add(entry);
		}
		RaiseChanged();
		return new Unregisterer(entry);
	}

	public static void Unregister(NativeUiEntry entry)
	{
		bool removed;
		lock (Lock)
		{
			removed = List.Remove(entry);
		}
		if (removed)
		{
			RaiseChanged();
		}
	}

	/// <summary>移除某个插件的全部原生入口。</summary>
	public static void UnregisterPlugin(string plugin)
	{
		int n;
		lock (Lock)
		{
			n = List.RemoveAll((NativeUiEntry e) => e.Plugin == plugin);
		}
		if (n > 0)
		{
			RaiseChanged();
		}
	}

	private static void RaiseChanged()
	{
		try
		{
			Changed?.Invoke();
		}
		catch (Exception)
		{
		}
		try
		{
			NativeChangedHook?.Invoke();
		}
		catch (Exception)
		{
		}
	}

	/// <summary>内核用：变化时同步发 plugins.ui_changed 事件。</summary>
	internal static Action NativeChangedHook;

	private sealed class Unregisterer : IDisposable
	{
		private NativeUiEntry _e;

		public Unregisterer(NativeUiEntry e)
		{
			_e = e;
		}

		public void Dispose()
		{
			NativeUiEntry e = _e;
			_e = null;
			if (e != null)
			{
				Unregister(e);
			}
		}
	}
}

/// <summary>一个原生 WPF 入口。</summary>
public sealed class NativeUiEntry
{
	/// <summary>插件 id。</summary>
	public string Plugin { get; set; }

	/// <summary>page | dashCard | aiView</summary>
	public string Kind { get; set; }

	/// <summary>入口 id（插件内唯一）。</summary>
	public string Id { get; set; }

	public string Label { get; set; }

	/// <summary>page：Fluent 字形或图片路径（已解析为绝对路径）。</summary>
	public string Icon { get; set; }

	public long Order { get; set; } = 100L;

	/// <summary>page：top | more。</summary>
	public string Group { get; set; } = "more";

	/// <summary>dashCard：默认宽高（格）。</summary>
	public long DefaultW { get; set; } = 2L;

	public long DefaultH { get; set; } = 2L;

	/// <summary>aiView：只在这些预设的对话里显示（完整 id plugin:&lt;id&gt;/&lt;preset&gt;）；空 = 所有对话。</summary>
	public List<string> Presets { get; set; } = new List<string>();

	/// <summary>page / dashCard 的控件工厂。</summary>
	public Func<FrameworkElement> Factory { get; set; }

	/// <summary>aiView 的控件工厂，参数是 chatId。</summary>
	public Func<string, FrameworkElement> AiViewFactory { get; set; }

	/// <summary>生成 page / dashCard 控件（在 UI 线程调用）。</summary>
	public FrameworkElement Create()
	{
		return Factory?.Invoke();
	}

	/// <summary>生成 aiView 控件（在 UI 线程调用）。</summary>
	public FrameworkElement CreateAiView(string chatId)
	{
		return AiViewFactory?.Invoke(chatId);
	}
}
