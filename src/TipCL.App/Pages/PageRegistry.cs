using System;
using System.Collections.Generic;

namespace TipCL.Pages;

/// <summary>
/// 页面工厂注册表(C2b 为 mac 侧新增的**唯一**结构性成员,理由见 D-92)。
///
/// Windows 版的 <c>MainWindow.NavDefs</c> 直接写着 <c>() =&gt; new LaunchPage()</c> —— 页面类型
/// 在导航表里就固化了。mac 版按 GOAL 是**一页一包**依次移植(C3+),C2b 时页面类还不存在,
/// 所以把"页面 id → 建页工厂"这一步抽成注册表:
/// <list type="bullet">
/// <item>C3 每个页面包在自己的文件里 <c>PageRegistry.Register("launch", () =&gt; new LaunchPage())</c>,
/// 不用改外壳,导航表(<c>MainWindow.NavDefs</c>)也一个字不用动;</item>
/// <item><see cref="NavDef.Create"/> 因此在 mac 侧可以是 null(未注册即"这一页还没有"),
/// <c>GetPage</c> 与 Windows 一样返回 null,<c>Navigate</c> 一样静默不切页;</item>
/// <item>id 一律**大小写敏感**的序号比较(<see cref="StringComparer.Ordinal"/>):与 Windows 的
/// <c>Dictionary&lt;string, PageBase&gt;</c> / <c>NavDef.Id == id</c> 同一套语义,未知 id 返回 null,不抛异常。</item>
/// </list>
/// 全局静态表,测试用完必须还原(注册表提供了 <see cref="Unregister"/> 与 <see cref="Snapshot"/>/<see cref="Restore"/>)。
/// </summary>
public static class PageRegistry
{
	private static readonly object Gate = new object();

	private static readonly Dictionary<string, Func<PageBase>> Factories = new Dictionary<string, Func<PageBase>>(StringComparer.Ordinal);

	/// <summary>注册一个页面的工厂(重复注册同一个 id = 覆盖)。</summary>
	public static void Register(string id, Func<PageBase> factory)
	{
		if (string.IsNullOrEmpty(id) || factory is null)
		{
			throw new ArgumentException("页面 id 与工厂都不能为空", nameof(id));
		}
		lock (Gate)
		{
			Factories[id] = factory;
		}
	}

	/// <summary>注销一个页面(id 不存在返回 false)。</summary>
	public static bool Unregister(string id)
	{
		lock (Gate)
		{
			return Factories.Remove(id);
		}
	}

	/// <summary>这个 id 有没有页面工厂。</summary>
	public static bool Contains(string id)
	{
		lock (Gate)
		{
			return Factories.ContainsKey(id);
		}
	}

	/// <summary>
	/// C7b(批3):**动态页 id 的受控回退口** —— 给外壳解析 <c>plugin:&lt;plugin&gt;:&lt;id&gt;</c> 这类
	/// 导航表里没有的 id 用(Windows 是 <c>MainWindow.CreatePluginPage(id)</c> 直接认前缀)。
	///
	/// 口径(与 Windows 一致,不许放宽):
	/// <list type="bullet">
	/// <item><b>只有注册表查不到时才问它</b> —— 已注册的 id 一律走注册表,回退口不参与(可测);</item>
	/// <item>它返回 <c>null</c> 就**等于没注册**:<see cref="Contains"/> 仍为 false、<see cref="Ids"/> 不变、
	///   外壳 <c>Navigate</c> 照旧**静默不切页**(这就是"未注册且非 plugin: 的 id 仍然静默不动"的落点);</item>
	/// <item>它**不写回**注册表(不改 <see cref="Ids"/>/<see cref="Snapshot"/> 语义)。</item>
	/// </list>
	/// 由外壳(`MainWindow.Plugins.cs`)接上:前缀不是 <c>plugin:</c> 就返回 null。
	/// </summary>
	internal static Func<string, PageBase?>? FallbackFactory { get; set; }

	/// <summary>按 id 建一个页面;**没注册返回 null**(不抛异常,与 Windows 的 GetPage 口径一致)。
	/// 注册表查不到时先问一次 <see cref="FallbackFactory"/>(动态插件页);它也没给就还是 null。</summary>
	public static PageBase? Create(string id)
	{
		Func<PageBase>? factory;
		lock (Gate)
		{
			if (!Factories.TryGetValue(id, out factory))
			{
				factory = null;
			}
		}

		if (factory != null)
		{
			return factory();
		}

		return FallbackFactory?.Invoke(id);
	}

	/// <summary>当前注册表的快照(测试用来"用完还原";顺序 = 注册顺序)。</summary>
	public static IReadOnlyDictionary<string, Func<PageBase>> Snapshot()
	{
		lock (Gate)
		{
			return new Dictionary<string, Func<PageBase>>(Factories, StringComparer.Ordinal);
		}
	}

	/// <summary>用一份快照整体替换注册表(测试收尾用)。</summary>
	public static void Restore(IReadOnlyDictionary<string, Func<PageBase>> snapshot)
	{
		lock (Gate)
		{
			Factories.Clear();
			if (snapshot is null)
			{
				return;
			}
			foreach (KeyValuePair<string, Func<PageBase>> pair in snapshot)
			{
				Factories[pair.Key] = pair.Value;
			}
		}
	}

	/// <summary>已注册的页面 id(快照,顺序 = 注册顺序)。</summary>
	public static IReadOnlyList<string> Ids
	{
		get
		{
			lock (Gate)
			{
				return new List<string>(Factories.Keys);
			}
		}
	}
}
