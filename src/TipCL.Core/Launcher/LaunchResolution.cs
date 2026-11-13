// 启动分辨率设置(default_resolution):R16 起「启动页」也有一处入口,与设置页共用同一个设置键。
//
// 与 Windows 参考实现的对应关系:
//   * Windows 把这对值最终落到 config 的 width / height,再由 get_settings 派生出
//     default_resolution(Core/BackendApi.cs:4125 读、:4339 写),启动页读写的就是这个派生键
//     (Pages/LaunchPage.cs:881 / :1002);
//   * Linux 侧设置页读写的本来就是 default_resolution 这个键本身
//     (Pages/Settings/SettingsPage.SaveLoad.cs:99 / :322),
//     故这里以 default_resolution 为唯一真源:启动页与设置页读写同一个键、同一份 config.json,
//     不再派生/回写 width·height(两套写法并存必然有一处先过期,「双改」就会互相覆盖)。
//
// 读到的值不可用(缺键 / 不是两元序列 / 非数字 / ≤0)一律回落 854×480 —— 即 Windows 的默认值。
using System;
using System.Collections;
using System.Collections.Generic;

namespace TipCL.Core.Launcher;

/// <summary>游戏窗口默认分辨率(设置键 <c>default_resolution</c>,形如 <c>[宽, 高]</c>)。</summary>
public static class LaunchResolution
{
	/// <summary>默认宽(对齐 Windows 的 GD("width", 854))。</summary>
	public const int DEFAULT_WIDTH = 854;

	/// <summary>默认高(对齐 Windows 的 GD("height", 480))。</summary>
	public const int DEFAULT_HEIGHT = 480;

	/// <summary>
	/// 解析设置里的值:<c>[宽, 高]</c> —— 任意序列(int[] / List&lt;object&gt; / object[] 都认)。
	/// 缺键 / 不是序列 / 只有一个元素 → 854×480;元素非数字或非正数按槽位回落默认。
	/// </summary>
	public static (int Width, int Height) Parse(object raw)
	{
		if (raw is string || !(raw is IEnumerable))
			return (DEFAULT_WIDTH, DEFAULT_HEIGHT);
		var items = new List<object>();
		foreach (object item in (IEnumerable)raw)
		{
			items.Add(item);
			if (items.Count == 2)
			{
				break;
			}
		}
		if (items.Count < 2)
		{
			return (DEFAULT_WIDTH, DEFAULT_HEIGHT);
		}
		return (ToPositive(items[0], DEFAULT_WIDTH), ToPositive(items[1], DEFAULT_HEIGHT));
	}

	/// <summary>解析两个输入框的文本(手输;TextBox.Text 可为 null):空、非数字、≤0 一律回落默认。</summary>
	public static (int Width, int Height) ParseText(string? width, string? height)
	{
		return (ToPositive(width, DEFAULT_WIDTH), ToPositive(height, DEFAULT_HEIGHT));
	}

	/// <summary>
	/// 读设置。Api 的 <c>get_settings</c> 返回的就是同一个 <see cref="Config.CONFIG"/> 单例,
	/// 所以设置页看到的值与这里读到的必然一致。
	/// </summary>
	public static (int Width, int Height) Read()
	{
		return Parse(Config.CONFIG.Get("default_resolution"));
	}

	/// <summary>写设置并立即落盘(与设置页同一个键;谁后改谁生效)。返回归一化后的值。</summary>
	public static (int Width, int Height) Write(int width, int height)
	{
		(int, int) pair = (ToPositive(width, DEFAULT_WIDTH), ToPositive(height, DEFAULT_HEIGHT));
		Config.CONFIG.Set("default_resolution", Py.L(pair.Item1, pair.Item2));
		Config.CONFIG.Save();
		return pair;
	}

	/// <summary>宽松取正数:沿用 Py.Int 的语义(字符串先 Trim、double 截断),越界或 ≤0 回落默认。</summary>
	private static int ToPositive(object value, int fallback)
	{
		long n;
		try
		{
			n = Py.Int(value);
		}
		catch (Exception)
		{
			return fallback;
		}
		return (n > 0 && n <= int.MaxValue) ? (int)n : fallback;
	}
}