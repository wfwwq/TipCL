using System;
using System.Collections.Generic;

namespace TipCL.Services;

/// <summary>
/// 「桌面快捷方式 / 命令行直接启动」的参数解析:形状由 <c>Core/Launcher/Shortcut.LaunchArgs</c> 产出 ——
/// <c>-i &lt;实例&gt; launch &lt;版本&gt; [--account A|--username U]</c>(Linux 侧 <c>Session/CommandLine.cs</c> 消费同一形状)。
///
/// 单独抽成纯函数的原因(第七轮复审):
///   1) Windows 侧以前**没人消费**这组参数(双击 .lnk 只打开启动器),落地时最容易在畸形输入上翻车:
///      缺版本、`-i` 落在末尾、空实例名、`-I`/`--i` 这种大小写/拼写变体、`--smoke` 之类自检参数混进来;
///   2) 抽出来之后可以逐条断言(见 <c>TipCL.Tests/StartArgsTests.cs</c>),不用起 WPF。
/// </summary>
public static class StartArgs
{
	/// <summary>
	/// 解析"直接启动"请求。严格按 <c>-i &lt;inst&gt; launch &lt;ver&gt;</c> 四连匹配(区分大小写、按序),
	/// 任何不完整的组合都判否 —— 宁可只是打开启动器,也不能把 `<c>--i18n-check</c>` 之类误当成 "-i"。
	/// </summary>
	/// <param name="instance">实例名(已 Trim,保证非空)</param>
	/// <param name="version">版本名(已 Trim,可能为空 —— 由后端按当前实例默认版本决定)</param>
	public static bool TryParse(IReadOnlyList<string>? args, out string instance, out string version)
	{
		instance = "";
		version = "";
		if (args == null)
		{
			return false;
		}
		for (int i = 0; i + 3 < args.Count; i++)
		{
			if (!string.Equals(args[i], "-i", StringComparison.Ordinal))
			{
				continue;
			}
			// 下一项必须是实例名(非空),第三项必须是 launch,第四项才是版本
			string inst = (args[i + 1] ?? "").Trim();
			if (inst.Length == 0 || !string.Equals(args[i + 2], "launch", StringComparison.Ordinal))
			{
				continue;
			}
			instance = inst;
			version = (args[i + 3] ?? "").Trim();
			return true;
		}
		return false;
	}
}