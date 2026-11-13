using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace TipCL.Themes;

/// <summary>
/// C5a:色板的**运行时可写覆盖层**(= Windows 版 <c>App.PaletteDictionary</c> + <c>App.ApplyTheme</c> 里
/// 那段"往色板字典里写新键"的能力)。
///
/// 为什么要有这个文件(而不是把覆盖层塞进设置页,也不是改 C1a 的 <see cref="Palette"/>):
/// <list type="bullet">
/// <item>内置色板(<c>Palette.Light.axaml</c> / <c>Palette.Dark.axaml</c>)是**编译进程序集的只读资源**,
///   Avalonia 的 <c>{DynamicResource}</c> 解析到的就是它们 —— 想"换个主题色立刻全局生效",只能在
///   **比它们更晚被查到**的地方放一份同名键;</item>
/// <item><see cref="Palette.Brush"/> / <see cref="Palette.Color"/> 是**全局**读的(C1a 起所有页面与外壳都走它),
///   所以覆盖层必须落在主题层:藏在设置页里就会变成一个"谁都不知道存在的全局",而且别的页面想用主题能力时
///   得去 import 一个页面类 —— 与 C3d「<c>FlyToTasks</c> 一次修在外壳、不在 13 个页面各加缝」同一条道理;</item>
/// <item><see cref="Palette"/> 是 C1a 的文件(红线:不改),所以本类**只**新增能力,不动它一个字。</item>
/// </list>
///
/// 机制:一个自己的 <see cref="ResourceDictionary"/>(<see cref="Layer"/>),第一次写入时**追加**到
/// <c>Application.Resources.MergedDictionaries</c> 的末尾。Avalonia 的 <c>ResourceDictionary.TryGetResource</c>
/// 先查自己的键、再**逆序**查合并字典(后合并的优先),所以追加在末尾的这一层压得过后挂在
/// <c>ThemeDictionaries</c> 里的内置色板 —— 优先级不是猜的,<c>C5aPaletteOverrideTests</c> 里有一条
/// 探针用例把"后合并者胜出 + <c>{DynamicResource}</c> 会当场重解析"这两件事分别钉住。
///
/// 覆盖层里存的是**普通键值**(不按主题分):主题一变,调用方(<c>SettingsPage</c>)会重算并重写一遍,
/// 与 Windows 每次 <c>ApplyVisuals</c> 重写 <c>PaletteDictionary</c> 的行为一致。
///
/// 读"内置值"这件事**必须绕过覆盖层**:<see cref="Palette.TryGet"/> 会先问应用资源(里面有本层),
/// 于是"把主题色恢复成默认绿"会读到被覆盖过的值。所以本类自己缓存两份 axaml(见 <see cref="Builtin"/>),
/// 与 Windows 每次 <c>new ResourceDictionary { Source = App.PaletteUri(isDark) }</c> 是同一件事。
/// </summary>
internal static class PaletteOverride
{
	/// <summary>覆盖层自己的字典键集合;为空时这一层**不挂在**应用资源上(免得白占一次查找)。</summary>
	private static readonly Dictionary<string, object> Entries = new Dictionary<string, object>(StringComparer.Ordinal);

	/// <summary>内置色板的直读缓存(按变体;绕开覆盖层,见类注释)。</summary>
	private static readonly Dictionary<ThemeVariant, IResourceProvider> BuiltinCache = new Dictionary<ThemeVariant, IResourceProvider>();

	private static ResourceDictionary? _layer;

	/// <summary>当前主题变体(只有明确 Dark 才算深色,与 <see cref="Palette.Normalize"/> 同一口径)。</summary>
	internal static ThemeVariant VariantNow => Palette.Current;

	/// <summary>现在是不是深色主题(Windows 的 <c>App.IsDark</c>)。</summary>
	internal static bool IsDarkNow => VariantNow == ThemeVariant.Dark;

	/// <summary>覆盖层里现在有几条(探针;命名避开 Avalonia 基类成员,见 C5a 的命名纪律)。</summary>
	internal static int CountNow => Entries.Count;

	/// <summary>覆盖层里现在的键(按写入顺序,快照)。</summary>
	internal static IReadOnlyList<string> KeysNow => new List<string>(Entries.Keys);

	/// <summary>覆盖层此刻是不是挂在应用资源上。</summary>
	internal static bool AttachedNow => _layer is not null && Application.Current is { } app && app.Resources.MergedDictionaries.Contains(_layer);

	/// <summary>
	/// 写一条覆盖(值可以是 <see cref="Color"/> 或 <see cref="IBrush"/>;与色板里的两种取法一致)。
	/// 已经写过就替换 —— 与 Windows 往 <c>ResourceDictionary</c> 上写同名键同一语义。
	/// </summary>
	internal static void Set(string key, object? value)
	{
		if (string.IsNullOrEmpty(key))
		{
			throw new ArgumentException("色板键不能为空", nameof(key));
		}

		if (value is null)
		{
			Remove(key);
			return;
		}

		Entries[key] = value;
		Attach();
		Poke(key, value);
	}

	/// <summary>写一条颜色覆盖(Windows 的 <c>paletteDictionary["C.xxx"] = color</c>)。</summary>
	internal static void SetColor(string key, Color color)
	{
		Set(key, color);
	}

	/// <summary>写一条刷子覆盖(Windows 的 <c>paletteDictionary["B.xxx"] = brush</c>)。</summary>
	internal static void SetBrush(string key, IBrush brush)
	{
		Set(key, brush);
	}

	/// <summary>撤掉一条覆盖(撤到 0 条时整层从应用资源上摘掉)。</summary>
	internal static void Remove(string key)
	{
		if (Entries.Remove(key))
		{
			if (_layer is not null)
			{
				_layer.Remove(key);
			}

			DetachIfEmpty();
		}
	}

	/// <summary>清空覆盖层 = 回到内置色板(Windows 里"主题色选默认"那一支的效果)。</summary>
	internal static void Reset()
	{
		Entries.Clear();
		if (_layer is not null)
		{
			_layer.Clear();
		}

		DetachIfEmpty();
	}

	/// <summary>覆盖层里这一条现在是什么(没有就 false);**不**查内置色板。</summary>
	internal static bool TryGetNow(string key, out object? value)
	{
		return Entries.TryGetValue(key, out value);
	}

	/// <summary>
	/// 内置色板(当前变体)里的**原始**值 —— 绕开覆盖层。
	/// 对应 Windows 那段 <c>new ResourceDictionary { Source = App.PaletteUri(isDark) }</c> 的直读。
	/// </summary>
	internal static object? Builtin(string key)
	{
		IResourceProvider provider = Builtin(VariantNow);
		return provider.TryGetResource(key, VariantNow, out object? value) ? value : null;
	}

	/// <summary>内置色板里的颜色(取不到 / 取到的不是颜色 → <see cref="Palette.FallbackColor"/>,与 C1a 同口径)。</summary>
	internal static Color BuiltinColor(string key)
	{
		switch (Builtin(key))
		{
			case Color color:
				return color;
			case ISolidColorBrush solid:
				return solid.Color;
			default:
				return Palette.FallbackColor;
		}
	}

	/// <summary>按当前变体归一化(只有明确 Dark 才算深色)。</summary>
	internal static ThemeVariant Normalize(ThemeVariant? variant)
	{
		return Palette.Normalize(variant);
	}

	/// <summary>把一条覆盖落到已挂上的那一层里,并让 <c>{DynamicResource}</c> 当场重解析。</summary>
	private static void Poke(string key, object value)
	{
		if (_layer is not null)
		{
			_layer[key] = value;
		}
	}

	/// <summary>第一次写入时把覆盖层挂到应用资源末尾(幂等)。</summary>
	private static void Attach()
	{
		Application? app = Application.Current;
		if (app is null)
		{
			// 没有 Application(纯单元测试早期):只留在 Entries 里,等有 app 时第一次 Set 再挂。
			return;
		}

		if (_layer is null)
		{
			_layer = new ResourceDictionary();
			foreach (KeyValuePair<string, object> pair in Entries)
			{
				_layer[pair.Key] = pair.Value;
			}
		}

		if (!app.Resources.MergedDictionaries.Contains(_layer))
		{
			// **追加在末尾**:Avalonia 逆序查合并字典 ⇒ 后合并的优先(探针用例钉住)。
			app.Resources.MergedDictionaries.Add(_layer);
		}
	}

	/// <summary>一条覆盖都不剩时把整层摘掉(应用资源恢复成"只有内置色板"的原样)。</summary>
	private static void DetachIfEmpty()
	{
		if (Entries.Count != 0 || _layer is null)
		{
			return;
		}

		Application? app = Application.Current;
		if (app is not null)
		{
			app.Resources.MergedDictionaries.Remove(_layer);
		}

		_layer = null;
	}

	/// <summary>载入内置色板(带缓存);加载失败给空字典(由调用方走兜底色,不抛)。</summary>
	private static IResourceProvider Builtin(ThemeVariant variant)
	{
		lock (BuiltinCache)
		{
			if (BuiltinCache.TryGetValue(variant, out IResourceProvider? cached))
			{
				return cached;
			}

			IResourceProvider loaded = Load(variant == ThemeVariant.Dark ? Palette.DarkUri : Palette.LightUri);
			BuiltinCache[variant] = loaded;
			return loaded;
		}
	}

	private static IResourceProvider Load(string uri)
	{
		try
		{
			object? loaded = AvaloniaXamlLoader.Load(new Uri(uri));
			if (loaded is IResourceProvider provider)
			{
				return provider;
			}

			if (loaded is ResourceDictionary dictionary)
			{
				return dictionary;
			}
		}
		catch (Exception)
		{
			// 资源没编进去 / XAML 坏了:交给兜底色,不让界面起不来(与 Palette.Load 同口径)。
		}

		return new ResourceDictionary();
	}
}
