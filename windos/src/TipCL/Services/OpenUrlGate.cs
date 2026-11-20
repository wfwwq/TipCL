using System;
using System.Collections.Generic;

namespace TipCL.Services;

/// <summary>
/// 打开外部链接的节流闸门(纯逻辑,net8 测试宿主直接断言)。
///
/// 为什么需要(现场 bug):联机页处于 <c>unsupported</c> 时那张说明卡上有个「了解」按钮,
/// 点下去直接 <c>Ui.OpenUrl(github.com/burningtnt/Terracotta)</c> —— 浏览器起来了但启动器界面
/// 一点变化都没有,用户以为"没反应"就再点,于是**同一个 GitHub 页面被反复打开**。
/// 现在:同一 URL 在 <see cref="DefaultWindowMs"/> 内只真正打开一次,其余静默丢弃(并记日志)。
/// </summary>
public static class OpenUrlGate
{
	/// <summary>同一 URL 的最小重复打开间隔(ms)。</summary>
	public const int DefaultWindowMs = 3000;

	private const int MaxEntries = 64;

	private static readonly object _lock = new object();

	private static readonly Dictionary<string, long> _last = new Dictionary<string, long>(StringComparer.Ordinal);

	/// <summary>
	/// 申请"打开这个 URL"的许可并记账。返回 false 表示刚打开过(或 URL 为空),调用方应静默跳过。
	/// <paramref name="nowMs"/> 由调用方给(生产用单调时钟,测试可注入)。
	/// </summary>
	public static bool TryAcquire(string? url, long nowMs, int windowMs = DefaultWindowMs)
	{
		string key = (url ?? "").Trim();
		if (key.Length == 0)
		{
			return false;
		}
		if (windowMs < 0)
		{
			windowMs = 0;
		}
		lock (_lock)
		{
			if (_last.TryGetValue(key, out long last) && nowMs >= last && nowMs - last < windowMs)
			{
				return false;
			}
			_last[key] = nowMs;
			if (_last.Count > MaxEntries)
			{
				Prune(nowMs, Math.Max(windowMs, DefaultWindowMs));
			}
			return true;
		}
	}

	/// <summary>测试与"换账号/重启流程"用:清掉记账。</summary>
	public static void Reset()
	{
		lock (_lock)
		{
			_last.Clear();
		}
	}

	private static void Prune(long nowMs, int keepMs)
	{
		List<string> stale = new List<string>();
		foreach (KeyValuePair<string, long> item in _last)
		{
			if (nowMs < item.Value || nowMs - item.Value >= keepMs)
			{
				stale.Add(item.Key);
			}
		}
		foreach (string key in stale)
		{
			_last.Remove(key);
		}
	}
}