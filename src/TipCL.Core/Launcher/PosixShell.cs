using System;
using System.Collections.Generic;

namespace TipCL.Core.Launcher;

/// <summary>
/// B2c3 新文件:POSIX sh 的命令行拼装(纯函数,不碰盘、不碰环境)。
///
/// 背景:移植前 Shortcut._create_macos / VersionOps.ExportLaunchBat 之外的 POSIX 分支
/// 直接用半角双引号包裹参数(Shortcut._quote 只在「有空格」时才加引号,而且不转义),
/// 而实例名 / 版本号 / 用户名都是用户可控文本 ——
/// 例如实例名 a'; rm -rf ~; echo ' 会被原样写进 .command 文件,双击执行即以用户权限跑任意命令。
///
/// 规则(任务书给定的 POSIX 事实,不再重新讨论):把一个字符串安全地放进 sh 的单个词,
/// 就是**整个用单引号包住**,再把里面每个单引号换成 '\''(退出单引号 + 转义单引号 + 重新进入);
/// 空串写成一对空单引号。这样包里除了单引号没有任何字符有特殊含义。
/// </summary>
internal static class PosixShell
{
	/// <summary>把 s 变成 sh 里可以安全当作单个词的字面量(单引号包裹)。null 按空串处理。</summary>
	internal static string Quote(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "''";
		}
		return "'" + s.Replace("'", "'\\''") + "'";
	}

	/// <summary>每个参数各自 Quote 之后用单个空格连接(args 为 null 时返回空串)。</summary>
	internal static string JoinCommand(IEnumerable<string> args)
	{
		if (args == null)
		{
			return "";
		}
		List<string> list = new List<string>();
		foreach (string arg in args)
		{
			list.Add(Quote(arg));
		}
		return string.Join(" ", list);
	}
}
